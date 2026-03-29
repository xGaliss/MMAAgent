namespace MMAAgent.Application.Abstractions;

public interface IContractOfferResponseService
{
    Task<ServiceResult> AcceptAsync(int contractOfferId, CancellationToken cancellationToken = default);
    Task<ServiceResult> RejectAsync(int contractOfferId, CancellationToken cancellationToken = default);
}
