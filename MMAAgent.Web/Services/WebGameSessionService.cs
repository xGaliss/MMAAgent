using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Files;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using MMAAgent.Infrastructure.Persistance.Sqlite.Services;
using MMAAgent.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Services;

public sealed class WebGameSessionService
{
    public sealed record SessionContextSnapshot(
        string UserId,
        string UserDisplayName,
        string? SaveId,
        string? OwnerUserId,
        string? SavePath,
        string? StorageKind,
        string? StorageLocator,
        string? SaveState,
        string? TemplateSource,
        string? BackendInstance);

    private readonly DatabaseOptions _dbOptions;
    private readonly ISaveSessionContext _saveSessionContext;
    private readonly IUserContextAccessor _userContextAccessor;
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly IGameStateRepository _gameStateRepo;
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly DbBootstrap _bootstrap;
    private readonly CareerSchemaPreparationService _careerSchemaPreparation;
    private readonly IFighterWorldService _fighterWorldService;
    private readonly WorldEcosystemServiceSqlite _worldEcosystemService;
    private readonly IWorldAgendaService _worldAgendaService;
    private readonly WorldFighterGeneratorSqlite _worldGen;
    private readonly InitialSigningPassSqlite _initialSigning;
    private readonly BuildInitialRankingsSqlite _rankings;
    private readonly PromotionScheduleSeeder _scheduleSeeder;

    public WebGameSessionService(
        IOptions<DatabaseOptions> dbOptions,
        ISaveSessionContext saveSessionContext,
        IUserContextAccessor userContextAccessor,
        ISaveCatalogService saveCatalogService,
        IGameStateRepository gameStateRepo,
        IAgentProfileRepository agentProfileRepository,
        DbBootstrap bootstrap,
        CareerSchemaPreparationService careerSchemaPreparation,
        IFighterWorldService fighterWorldService,
        WorldEcosystemServiceSqlite worldEcosystemService,
        IWorldAgendaService worldAgendaService,
        WorldFighterGeneratorSqlite worldGen,
        InitialSigningPassSqlite initialSigning,
        BuildInitialRankingsSqlite rankings,
        PromotionScheduleSeeder scheduleSeeder)
    {
        _dbOptions = dbOptions.Value;
        _saveSessionContext = saveSessionContext;
        _userContextAccessor = userContextAccessor;
        _saveCatalogService = saveCatalogService;
        _gameStateRepo = gameStateRepo;
        _agentProfileRepository = agentProfileRepository;
        _bootstrap = bootstrap;
        _careerSchemaPreparation = careerSchemaPreparation;
        _fighterWorldService = fighterWorldService;
        _worldEcosystemService = worldEcosystemService;
        _worldAgendaService = worldAgendaService;
        _worldGen = worldGen;
        _initialSigning = initialSigning;
        _rankings = rankings;
        _scheduleSeeder = scheduleSeeder;
    }

    public string? CurrentSavePath => _saveSessionContext.CurrentPath;
    public string? CurrentSaveId => _saveSessionContext.CurrentSaveId;
    public string? CurrentOwnerUserId => _saveSessionContext.CurrentOwnerUserId;

    public SessionContextSnapshot GetSessionContext()
        => new(
            _userContextAccessor.CurrentUserId,
            _userContextAccessor.DisplayName,
            CurrentSaveId,
            CurrentOwnerUserId,
            CurrentSavePath,
            _saveSessionContext.CurrentStorageKind,
            _saveSessionContext.CurrentStorageLocator,
            _saveSessionContext.CurrentSaveState,
            _saveSessionContext.CurrentTemplateSource,
            _saveSessionContext.CurrentBackendInstance);

    public Task<SaveRecord?> GetCurrentSaveRecordAsync(CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(CurrentSaveId)
            ? Task.FromResult<SaveRecord?>(null)
            : _saveCatalogService.GetBySaveIdAsync(CurrentSaveId, cancellationToken);

    public Task LoadConfiguredSaveAsync()
    {
        var path = _dbOptions.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Database:Path no está configurado.");

        return LoadByPathAsync(path);
    }

    public async Task<bool> TryLoadLastSaveAsync()
    {
        var ownerUserId = _userContextAccessor.CurrentUserId;
        var entry = await _saveCatalogService.GetLastOpenedAsync(ownerUserId);

        if (entry is not null && !string.IsNullOrWhiteSpace(entry.LocalPath) && File.Exists(entry.LocalPath))
        {
            await LoadCatalogedSaveAsync(entry);
            return true;
        }

        var path = ReadLastSavePath();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            await LoadByPathAsync(path);
            return true;
        }

        return false;
    }

    public async Task LoadByPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Debes indicar una ruta de save.");

        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró la save DB.", path);

        var entry = await _saveCatalogService.RegisterOrUpdateLocalAsync(
            path,
            _userContextAccessor.CurrentUserId,
            markOpened: true);

        await LoadCatalogedSaveAsync(entry);
    }

    public async Task LoadBySaveIdAsync(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            throw new InvalidOperationException("Debes indicar un save id.");

        var entry = await _saveCatalogService.GetBySaveIdAsync(saveId);
        if (entry is null)
            throw new FileNotFoundException("No se encontró la save registrada.", saveId);

        if (string.IsNullOrWhiteSpace(entry.LocalPath) || !File.Exists(entry.LocalPath))
            throw new FileNotFoundException("La save registrada ya no existe en disco.", entry.LocalPath ?? entry.StorageLocator);

        var refreshed = await _saveCatalogService.RegisterOrUpdateLocalAsync(
            entry.LocalPath,
            entry.OwnerUserId,
            entry.DisplayName,
            entry.TemplateSource,
            markOpened: true);

        await LoadCatalogedSaveAsync(refreshed);
    }

    public async Task<string> CreateNewGameAsync(
        string? saveName,
        string agentName,
        string agencyName,
        int fighterCount,
        string? nationality,
        string? avatarKey)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            throw new InvalidOperationException("Introduce el nombre del agente.");

        if (string.IsNullOrWhiteSpace(agencyName))
            throw new InvalidOperationException("Introduce el nombre de la agencia.");

        var templateDbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets",
            "Database",
            "MMA_Agent.db");

        if (!File.Exists(templateDbPath))
            throw new FileNotFoundException("No se encontró la DB plantilla.", templateDbPath);

        var savePath = _bootstrap.CreateNewSaveFromTemplate(templateDbPath, saveName);
        var registryEntry = await _saveCatalogService.RegisterOrUpdateLocalAsync(
            savePath,
            _userContextAccessor.CurrentUserId,
            saveName,
            SaveTemplateSources.DefaultTemplateDb,
            markOpened: true);

        _saveSessionContext.SetCurrent(registryEntry);
        await _careerSchemaPreparation.PrepareAsync();

        var seed = Random.Shared.Next(1, int.MaxValue);
        var startDate = new DateTime(2026, 1, 1);

        await _gameStateRepo.EnsureCreatedAsync(startDate, seed);

        var state = await _gameStateRepo.GetAsync();
        var realSeed = state?.WorldSeed ?? seed;

        await _scheduleSeeder.InitializeForNewSaveAsync(startAbsoluteWeek: 0);

        _worldGen.SetSeed(realSeed);
        _worldGen.GenerateCount = fighterCount;
        _worldGen.ClearExistingFighters = true;
        _worldGen.GenerateWorld();

        _initialSigning.SetSeed(realSeed);
        await _initialSigning.RunAsync();

        _rankings.SetSeed(realSeed);
        await _rankings.RunAsync();

        await _agentProfileRepository.CreateAsync(new AgentProfile
        {
            Name = agentName.Trim(),
            AgencyName = agencyName.Trim(),
            Nationality = string.IsNullOrWhiteSpace(nationality) ? "Spain" : nationality.Trim(),
            AvatarKey = string.IsNullOrWhiteSpace(avatarKey) ? "Promoter" : avatarKey.Trim(),
            Money = 50000,
            Reputation = 10,
            PublicReputation = 48,
            FighterTrust = 52,
            PromotionLeverage = 46,
            ScoutingStaffLevel = 1,
            MediaStaffLevel = 1,
            NegotiationStaffLevel = 1,
            PerformanceStaffLevel = 1,
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        await _fighterWorldService.SynchronizeAsync();
        await _worldEcosystemService.SynchronizeAsync();
        await _worldAgendaService.SynchronizeAsync();

        SaveLastPath(registryEntry.LocalPath ?? registryEntry.StorageLocator);
        return registryEntry.LocalPath ?? registryEntry.StorageLocator;
    }

    private async Task LoadCatalogedSaveAsync(SaveRecord entry)
    {
        _saveSessionContext.SetCurrent(entry);
        await _careerSchemaPreparation.PrepareAsync();
        await _fighterWorldService.SynchronizeAsync();
        await _worldEcosystemService.SynchronizeAsync();
        await _worldAgendaService.SynchronizeAsync();
        SaveLastPath(entry.LocalPath ?? entry.StorageLocator);
    }

    private static string GetLastSaveFilePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MMAAgent");

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "lastsave.txt");
    }

    private static void SaveLastPath(string path) => File.WriteAllText(GetLastSaveFilePath(), path);

    private static string? ReadLastSavePath()
    {
        var file = GetLastSaveFilePath();
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }
}
