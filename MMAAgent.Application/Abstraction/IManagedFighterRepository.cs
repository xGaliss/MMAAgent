using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions
{
    public interface IManagedFighterRepository
    {
        Task<IReadOnlyList<ManagedFighter>> GetByAgentAsync(int agentId);
        Task<bool> IsManagedByAgentAsync(int agentId, int fighterId);
        Task<int> AddAsync(ManagedFighter item);
    }
}
