using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions;

public interface IContractOfferRepository
{
    Task CreateAsync(ContractOffer offer, CancellationToken cancellationToken = default);
    Task<ContractOffer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContractOffer>> ListByAgentAsync(int agentId, bool pendingOnly, CancellationToken cancellationToken = default);
    Task<bool> HasPendingOfferAsync(int fighterId, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int id, string status, string? respondedDate, CancellationToken cancellationToken = default);
    Task<int> CountPendingByAgentAsync(int agentId, CancellationToken cancellationToken = default);
}