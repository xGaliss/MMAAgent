namespace MMAAgent.Web.Models;

public sealed record AgendaItemVm(
    string ScheduledDate,
    string EventType,
    string Headline,
    string? Subtitle,
    int Priority);
