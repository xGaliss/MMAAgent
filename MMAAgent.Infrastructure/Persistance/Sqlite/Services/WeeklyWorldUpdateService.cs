using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class WeeklyWorldUpdateService : IWeeklyWorldUpdateService
{
    private readonly GameTimeService _gameTimeService;
    private readonly IFightOfferGenerationService _fightOfferGenerationService;
    private readonly IPromotionEventScheduleRepository _scheduleRepository;
    private readonly IEventSimulator _eventSimulator;
    private readonly SqliteConnectionFactory _factory;

    public WeeklyWorldUpdateService(
        GameTimeService gameTimeService,
        IFightOfferGenerationService fightOfferGenerationService,
        IPromotionEventScheduleRepository scheduleRepository,
        IEventSimulator eventSimulator,
        SqliteConnectionFactory factory)
    {
        _gameTimeService = gameTimeService;
        _fightOfferGenerationService = fightOfferGenerationService;
        _scheduleRepository = scheduleRepository;
        _eventSimulator = eventSimulator;
        _factory = factory;
    }

    public async Task<WeeklyWorldUpdateSummary> AdvanceWeekAsync(CancellationToken cancellationToken = default)
    {
        var state = await _gameTimeService.AdvanceWeeksAsync(1);

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

        var newOffers = await _fightOfferGenerationService.GenerateWeeklyOffersAsync(cancellationToken);

        int newMessages;
        using (var cmd = readConn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE CreatedDate = $date
  AND AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1);";
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
            newOffers,
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
SET
    WeeksUntilAvailable = CASE
        WHEN COALESCE(WeeksUntilAvailable, 0) > 0 THEN WeeksUntilAvailable - 1
        ELSE 0
    END,
    InjuryWeeksRemaining = CASE
        WHEN COALESCE(InjuryWeeksRemaining, 0) > 0 THEN InjuryWeeksRemaining - 1
        ELSE 0
    END,
    IsInjured = CASE
        WHEN COALESCE(InjuryWeeksRemaining, 0) > 1 THEN 1
        ELSE 0
    END;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
