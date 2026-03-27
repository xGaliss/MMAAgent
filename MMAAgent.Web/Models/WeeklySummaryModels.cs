namespace MMAAgent.Web.Models;

public sealed record WeeklyAdvanceSummaryVm(
    string NewDate,
    int NewWeek,
    int NewYear,
    int NewMessages,
    int RecentEventsLoaded,
    string? Headline);
