namespace MMAAgent.Application.Abstractions;

public interface IFightOfferResponseService
{
    Task<FightOfferResponseResult> AcceptOfferAsync(int offerId, CancellationToken cancellationToken = default);
    Task<FightOfferResponseResult> RejectOfferAsync(int offerId, CancellationToken cancellationToken = default);
}

public sealed record FightOfferResponseResult(
    bool Success,
    string Message,
    int OfferId,
    int? EventId,
    int? FightId);
