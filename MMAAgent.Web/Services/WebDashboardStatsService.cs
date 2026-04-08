using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebDashboardStatsService
{
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IContractOfferRepository _contractOfferRepository;
    private readonly SqliteConnectionFactory _factory;

    public WebDashboardStatsService(
        IAgentProfileRepository agentProfileRepository,
        IInboxRepository inboxRepository,
        IContractOfferRepository contractOfferRepository,
        SqliteConnectionFactory factory)
    {
        _agentProfileRepository = agentProfileRepository;
        _inboxRepository = inboxRepository;
        _contractOfferRepository = contractOfferRepository;
        _factory = factory;
    }

    public async Task<DashboardStatsVm> LoadAsync()
    {
        var vm = new DashboardStatsVm();
        var agent = await _agentProfileRepository.GetAsync();

        using var conn = _factory.CreateConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Fighters;";
            vm.FighterCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Promotions;";
            vm.PromotionCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        if (agent is not null)
        {
            vm.UnreadMessages = await _inboxRepository.CountUnreadAsync(agent.Id);
            vm.PendingContractOffers = await _contractOfferRepository.CountPendingByAgentAsync(agent.Id);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(*)
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1
WHERE fo.Status = 'Pending';";
            cmd.Parameters.AddWithValue("$agentId", agent.Id);
            vm.PendingFightOffers = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return vm;
    }
}
