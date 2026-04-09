namespace MMAAgent.Application.Abstractions;

public interface IWeeklyWorldUpdateService
{
    Task<WeeklyWorldUpdateSummary> AdvanceWeekAsync(CancellationToken cancellationToken = default);
    Task<WeeklyWorldUpdateSummary> ProcessCurrentWeekAsync(CancellationToken cancellationToken = default);
}

public sealed record WeeklyWorldUpdateSummary(
    string CurrentDate,
    int CurrentWeek,
    int CurrentYear,
    int SimulatedEvents,
    int NewFightOffers,
    int NewInboxMessages,
    int RankingChanges,
    string? Headline);
