using System.Threading.Tasks;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions
{
    public interface IAgentProfileRepository
    {
        Task<AgentProfile?> GetAsync();
        Task<int> CreateAsync(AgentProfile profile);
    }
}