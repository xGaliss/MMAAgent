using MMAAgent.Application.Abstractions;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebAgentProfileService
{
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IManagedFighterRepository _managedFighterRepository;

    public WebAgentProfileService(
        IAgentProfileRepository agentRepository,
        IManagedFighterRepository managedFighterRepository)
    {
        _agentRepository = agentRepository;
        _managedFighterRepository = managedFighterRepository;
    }

    public async Task<AgentProfileVm?> LoadAsync()
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return null;

        var managed = await _managedFighterRepository.GetByAgentAsync(agent.Id);

        return new AgentProfileVm(
            agent.Id,
            agent.Name,
            agent.AgencyName,
            agent.Money,
            agent.Reputation,
            agent.CreatedDate,
            managed.Count);
    }
}
