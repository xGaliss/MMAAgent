using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Files;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using MMAAgent.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Services;

public sealed class WebGameSessionService
{
    private readonly DatabaseOptions _dbOptions;
    private readonly ISavePathProvider _savePathProvider;
    private readonly IGameStateRepository _gameStateRepo;
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly DbBootstrap _bootstrap;
    private readonly CareerSchemaPreparationService _careerSchemaPreparation;
    private readonly IFighterWorldService _fighterWorldService;
    private readonly IWorldAgendaService _worldAgendaService;
    private readonly WorldFighterGeneratorSqlite _worldGen;
    private readonly InitialSigningPassSqlite _initialSigning;
    private readonly BuildInitialRankingsSqlite _rankings;
    private readonly PromotionScheduleSeeder _scheduleSeeder;

    public WebGameSessionService(
        IOptions<DatabaseOptions> dbOptions,
        ISavePathProvider savePathProvider,
        IGameStateRepository gameStateRepo,
        IAgentProfileRepository agentProfileRepository,
        DbBootstrap bootstrap,
        CareerSchemaPreparationService careerSchemaPreparation,
        IFighterWorldService fighterWorldService,
        IWorldAgendaService worldAgendaService,
        WorldFighterGeneratorSqlite worldGen,
        InitialSigningPassSqlite initialSigning,
        BuildInitialRankingsSqlite rankings,
        PromotionScheduleSeeder scheduleSeeder)
    {
        _dbOptions = dbOptions.Value;
        _savePathProvider = savePathProvider;
        _gameStateRepo = gameStateRepo;
        _agentProfileRepository = agentProfileRepository;
        _bootstrap = bootstrap;
        _careerSchemaPreparation = careerSchemaPreparation;
        _fighterWorldService = fighterWorldService;
        _worldAgendaService = worldAgendaService;
        _worldGen = worldGen;
        _initialSigning = initialSigning;
        _rankings = rankings;
        _scheduleSeeder = scheduleSeeder;
    }

    public string? CurrentSavePath => _savePathProvider.CurrentPath;

    public Task LoadConfiguredSaveAsync()
    {
        var path = _dbOptions.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Database:Path no está configurado.");

        return LoadByPathAsync(path);
    }

    public async Task<bool> TryLoadLastSaveAsync()
    {
        var path = ReadLastSavePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        await LoadByPathAsync(path);
        return true;
    }

    public async Task LoadByPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Debes indicar una ruta de save.");

        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró la save DB.", path);

        _savePathProvider.Set(path);
        await _careerSchemaPreparation.PrepareAsync();
        await _fighterWorldService.SynchronizeAsync();
        await _worldAgendaService.SynchronizeAsync();
        SaveLastPath(path);
    }

    public async Task<string> CreateNewGameAsync(string? saveName, string agentName, string agencyName, int fighterCount)
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
        _savePathProvider.Set(savePath);
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
        await _fighterWorldService.SynchronizeAsync();
        await _worldAgendaService.SynchronizeAsync();

        await _agentProfileRepository.CreateAsync(new AgentProfile
        {
            Name = agentName.Trim(),
            AgencyName = agencyName.Trim(),
            Money = 50000,
            Reputation = 10,
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        SaveLastPath(savePath);
        return savePath;
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
