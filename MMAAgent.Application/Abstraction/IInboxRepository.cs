using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions
{
    public interface IInboxRepository
    {
        Task<int> CreateAsync(InboxMessage message);
        Task<IReadOnlyList<InboxMessage>> GetByAgentAsync(int agentId);
        Task MarkAsReadAsync(int messageId);
    }
}