using System;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Desktop.Services
{
    public sealed class CreateAgentProfileService
    {
        private readonly IAgentProfileRepository _repo;

        public CreateAgentProfileService(IAgentProfileRepository repo)
        {
            _repo = repo;
        }

        public async Task<int> CreateAsync(string agentName, string agencyName)
        {
            var profile = new AgentProfile
            {
                Name = agentName.Trim(),
                AgencyName = agencyName.Trim(),
                Money = 50000,
                Reputation = 10,
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            return await _repo.CreateAsync(profile);
        }
    }
}