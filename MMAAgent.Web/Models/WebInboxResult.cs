namespace MMAAgent.Web.Models;

public sealed class WebInboxResult
{
    public IReadOnlyList<InboxMessageVm> Messages { get; init; } = Array.Empty<InboxMessageVm>();
    public IReadOnlyList<FightOfferVm> Offers { get; init; } = Array.Empty<FightOfferVm>();
    public IReadOnlyList<ContractOfferVm> ContractOffers { get; init; } = Array.Empty<ContractOfferVm>();
    public IReadOnlyList<DecisionEventVm> Decisions { get; init; } = Array.Empty<DecisionEventVm>();
}
