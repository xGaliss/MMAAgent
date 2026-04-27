using System.Threading;
using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class WeeklyWorldUpdateService : IWeeklyWorldUpdateService
{
    private static readonly SemaphoreSlim _advanceLock = new(1, 1);

    private readonly GameTimeService _gameTimeService;
    private readonly IFightOfferGenerationService _fightOfferGenerationService;
    private readonly InitialSigningPassSqlite _initialSigningPass;
    private readonly WorldFighterGeneratorSqlite _worldFighterGenerator;
    private readonly IFighterWorldService _fighterWorldService;
    private readonly WorldEcosystemServiceSqlite _worldEcosystemService;
    private readonly IWorldAgendaService _worldAgendaService;
    private readonly IPromotionEventScheduleRepository _scheduleRepository;
    private readonly IEventSimulator _eventSimulator;
    private readonly SqliteConnectionFactory _factory;

    public WeeklyWorldUpdateService(
        GameTimeService gameTimeService,
        IFightOfferGenerationService fightOfferGenerationService,
        InitialSigningPassSqlite initialSigningPass,
        WorldFighterGeneratorSqlite worldFighterGenerator,
        IFighterWorldService fighterWorldService,
        WorldEcosystemServiceSqlite worldEcosystemService,
        IWorldAgendaService worldAgendaService,
        IPromotionEventScheduleRepository scheduleRepository,
        IEventSimulator eventSimulator,
        SqliteConnectionFactory factory)
    {
        _gameTimeService = gameTimeService;
        _fightOfferGenerationService = fightOfferGenerationService;
        _initialSigningPass = initialSigningPass;
        _worldFighterGenerator = worldFighterGenerator;
        _fighterWorldService = fighterWorldService;
        _worldEcosystemService = worldEcosystemService;
        _worldAgendaService = worldAgendaService;
        _scheduleRepository = scheduleRepository;
        _eventSimulator = eventSimulator;
        _factory = factory;
    }

    public async Task<WeeklyWorldUpdateSummary> AdvanceWeekAsync(CancellationToken cancellationToken = default)
    {
        await _advanceLock.WaitAsync(cancellationToken);

        try
        {
            var previousState = await _gameTimeService.GetAsync();
            var previousYear = previousState?.CurrentYear ?? 0;
            var worldSeed = previousState?.WorldSeed ?? 0;

            await _gameTimeService.AdvanceDaysAsync(7);
            return await ProcessCurrentWeekCoreAsync(previousYear, worldSeed, cancellationToken);
        }
        finally
        {
            _advanceLock.Release();
        }
    }

    public async Task<WeeklyWorldUpdateSummary> ProcessCurrentWeekAsync(CancellationToken cancellationToken = default)
    {
        await _advanceLock.WaitAsync(cancellationToken);

        try
        {
            var state = await _gameTimeService.GetAsync();
            var previousYear = state?.CurrentWeek == 1
                ? Math.Max(0, (state?.CurrentYear ?? 1) - 1)
                : state?.CurrentYear ?? 0;
            var worldSeed = state?.WorldSeed ?? 0;

            return await ProcessCurrentWeekCoreAsync(previousYear, worldSeed, cancellationToken);
        }
        finally
        {
            _advanceLock.Release();
        }
    }

    private async Task<WeeklyWorldUpdateSummary> ProcessCurrentWeekCoreAsync(
        int previousYear,
        int worldSeed,
        CancellationToken cancellationToken)
    {
        var state = await _gameTimeService.GetAsync()
            ?? throw new InvalidOperationException("Game state not found.");

        using (var conn = _factory.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE GameState SET AbsoluteWeek = COALESCE(AbsoluteWeek, 0) + 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await TickRecoveryAsync(cancellationToken);

        using var readConn = _factory.CreateConnection();

        int absoluteWeek;
        using (var cmd = readConn.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(AbsoluteWeek, 1) FROM GameState LIMIT 1;";
            absoluteWeek = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        var duePromotions = await _scheduleRepository.GetDueAsync(absoluteWeek);
        var simulatedEvents = 0;

        foreach (var promo in duePromotions)
        {
            await _eventSimulator.SimulatePromotionEventAsync(promo.PromotionId, state);
            simulatedEvents++;

            var nextAbsoluteWeek = absoluteWeek + Math.Max(1, promo.IntervalWeeks);
            await _scheduleRepository.SetNextEventWeekAsync(promo.PromotionId, nextAbsoluteWeek);
        }

        if (state.CurrentYear > previousYear)
        {
            await _worldEcosystemService.ApplyAnnualEvolutionAsync(state.CurrentYear, cancellationToken);
            _worldFighterGenerator.SetSeed(ComputeAnnualIntakeSeed(worldSeed, state.CurrentYear));
            _worldFighterGenerator.GenerateAnnualNewcomers();
        }

        await CleanupStaleScheduledFightsAsync(state.CurrentDate, cancellationToken);
        await _initialSigningPass.RunWeeklyTopUpAsync(cancellationToken);
        await _fighterWorldService.AdvanceWeekAsync(absoluteWeek, state.CurrentDate, cancellationToken);
        await _worldEcosystemService.SynchronizeAsync(cancellationToken);
        await _worldEcosystemService.ApplyWeeklyExpensesAsync(cancellationToken);
        await _worldAgendaService.SynchronizeAsync(cancellationToken);
        var newFightOffers = await _fightOfferGenerationService.GenerateWeeklyOffersAsync(cancellationToken);

        int newMessages;
        using (var cmd = readConn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE CreatedDate = $date
  AND AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND COALESCE(IsDeleted, 0) = 0;";
            cmd.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            newMessages = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        string? headline;
        using (var cmd = readConn.CreateCommand())
        {
            cmd.CommandText = "SELECT Name FROM Events ORDER BY Id DESC LIMIT 1;";
            headline = (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString();
        }

        return new WeeklyWorldUpdateSummary(
            state.CurrentDate,
            state.CurrentWeek,
            state.CurrentYear,
            simulatedEvents,
            newFightOffers,
            newMessages,
            0,
            headline);
    }

    private async Task TickRecoveryAsync(CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE Fighters
SET WeeksUntilAvailable = CASE WHEN COALESCE(WeeksUntilAvailable, 0) > 0 THEN WeeksUntilAvailable - 1 ELSE 0 END,
    MedicalSuspensionWeeksRemaining = CASE WHEN COALESCE(MedicalSuspensionWeeksRemaining, 0) > 0 THEN MedicalSuspensionWeeksRemaining - 1 ELSE 0 END,
    InjuryWeeksRemaining = CASE WHEN COALESCE(InjuryWeeksRemaining, 0) > 0 THEN InjuryWeeksRemaining - 1 ELSE 0 END,
    IsInjured = CASE WHEN COALESCE(InjuryWeeksRemaining, 0) > 1 THEN 1 ELSE 0 END,
    AvailableFromWeek = CASE
        WHEN MAX(
            CASE WHEN COALESCE(WeeksUntilAvailable, 0) > 0 THEN WeeksUntilAvailable - 1 ELSE 0 END,
            CASE WHEN COALESCE(MedicalSuspensionWeeksRemaining, 0) > 0 THEN MedicalSuspensionWeeksRemaining - 1 ELSE 0 END,
            CASE WHEN COALESCE(InjuryWeeksRemaining, 0) > 0 THEN InjuryWeeksRemaining - 1 ELSE 0 END
        ) > 0
        THEN COALESCE((SELECT AbsoluteWeek FROM GameState LIMIT 1), 0)
             + MAX(
                 CASE WHEN COALESCE(WeeksUntilAvailable, 0) > 0 THEN WeeksUntilAvailable - 1 ELSE 0 END,
                 CASE WHEN COALESCE(MedicalSuspensionWeeksRemaining, 0) > 0 THEN MedicalSuspensionWeeksRemaining - 1 ELSE 0 END,
                 CASE WHEN COALESCE(InjuryWeeksRemaining, 0) > 0 THEN InjuryWeeksRemaining - 1 ELSE 0 END
             )
        ELSE COALESCE((SELECT AbsoluteWeek FROM GameState LIMIT 1), 0)
    END;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int ComputeAnnualIntakeSeed(int worldSeed, int currentYear)
        => worldSeed == 0
            ? currentYear
            : unchecked((worldSeed * 397) ^ currentYear);

    private async Task CleanupStaleScheduledFightsAsync(string currentDate, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
UPDATE Fights
SET Method = 'Cancelled'
WHERE Method = 'Scheduled'
  AND COALESCE(EventDate, '') <> ''
  AND EventDate <= $currentDate;";
            cmd.Parameters.AddWithValue("$currentDate", currentDate);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
UPDATE Fighters
SET IsBooked = CASE
    WHEN EXISTS (
        SELECT 1
        FROM Fights sf
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = Fighters.Id OR sf.FighterBId = Fighters.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > $currentDate
    ) THEN 1
    ELSE 0
END;";
            cmd.Parameters.AddWithValue("$currentDate", currentDate);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

}
