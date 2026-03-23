using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Application.Abstractions
{
    public interface IFightOfferRepository
    {
        Task<int> CreateAsync(FightOffer offer);
        Task<IReadOnlyList<FightOffer>> GetByAgentAsync(int agentId);
        Task UpdateStatusAsync(int offerId, string status);
    }
}