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

        // 1) Simular eventos debidos esta semana
        var duePromotions = await _scheduleRepository.GetDueAsync(state.CurrentWeek);
        var simulatedEvents = 0;

        foreach (var promo in duePromotions)
        {
            await _eventSimulator.SimulatePromotionEventAsync(promo.PromotionId, state);
            simulatedEvents++;

            var nextWeek = state.CurrentWeek + Math.Max(1, promo.IntervalWeeks);
            await _scheduleRepository.SetNextEventWeekAsync(promo.PromotionId, nextWeek);
        }

        // 2) Generar nuevas offers después de simular
        var newOffers = await _fightOfferGenerationService.GenerateWeeklyOffersAsync(cancellationToken);

        // 3) Resumen básico
        using var conn = _factory.CreateConnection();

        int newMessages;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE CreatedDate = $date
  AND AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1);";
            cmd.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            newMessages = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        string? headline = null;
        using (var cmd = conn.CreateCommand())
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
}