namespace MMAAgent.Application.Abstractions;

public interface IFightOfferResponseService
{
    Task<FightOfferResponseResult> AcceptOfferAsync(int offerId, CancellationToken cancellationToken = default);
    Task<FightOfferResponseResult> RejectOfferAsync(int offerId, CancellationToken cancellationToken = default);
}