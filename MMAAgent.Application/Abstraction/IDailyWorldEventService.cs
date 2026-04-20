namespace MMAAgent.Application.Abstractions;

public interface IDailyWorldEventService
{
    Task<DailyWorldEventSummary> ProcessCurrentDayAsync(CancellationToken cancellationToken = default);
}

public sealed record DailyWorldEventSummary(
    int CampUpdates,
    int FightWeekUpdates,
    int WeighInUpdates,
    int AftermathUpdates,
    int InboxMessagesCreated);
