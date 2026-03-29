using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions;

public interface IInboxRepository
{
    Task CreateAsync(InboxMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxMessage>> ListAsync(
        int agentId,
        string? messageType,
        bool includeArchived,
        bool archivedOnly,
        CancellationToken cancellationToken = default);

    Task<InboxMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(int agentId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default);
    Task ArchiveAsync(int id, CancellationToken cancellationToken = default);
    Task RestoreAsync(int id, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task ArchiveReadAsync(int agentId, CancellationToken cancellationToken = default);
    Task DeleteReadAsync(int agentId, CancellationToken cancellationToken = default);
    Task<int> CountUnreadAsync(int agentId, CancellationToken cancellationToken = default);
}