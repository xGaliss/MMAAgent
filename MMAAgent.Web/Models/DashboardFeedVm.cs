namespace MMAAgent.Web.Models;

public sealed class DashboardFeedVm
{
    public IReadOnlyList<AgendaItemVm> Agenda { get; init; } = Array.Empty<AgendaItemVm>();
    public IReadOnlyList<string> CompetitivePulse { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Events { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Managed { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Champions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PendingFightOfferItems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PendingContractOfferItems { get; init; } = Array.Empty<string>();
}
