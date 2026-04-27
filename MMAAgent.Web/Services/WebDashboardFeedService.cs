using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebDashboardFeedService
{
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly IWorldAgendaService _worldAgendaService;
    private readonly SqliteConnectionFactory _factory;

    public WebDashboardFeedService(
        IAgentProfileRepository agentProfileRepository,
        IWorldAgendaService worldAgendaService,
        SqliteConnectionFactory factory)
    {
        _agentProfileRepository = agentProfileRepository;
        _worldAgendaService = worldAgendaService;
        _factory = factory;
    }

    public async Task<DashboardFeedVm> LoadAsync()
    {
        await _worldAgendaService.SynchronizeAsync();

        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null)
            return new DashboardFeedVm();

        using var conn = _factory.CreateConnection();

        var agenda = await LoadAgendaAsync(conn);
        var competitivePulse = await LoadSimpleListAsync(conn, @"
SELECT
    (s.Headline || ' · ' || COALESCE(f.FirstName || ' ' || f.LastName, 'World'))
FROM Storylines s
LEFT JOIN Fighters f
    ON s.EntityType = 'Fighter'
   AND f.Id = s.EntityId
WHERE COALESCE(s.Status, 'Active') = 'Active'
ORDER BY COALESCE(s.Intensity, 0) DESC, s.Id DESC
LIMIT 6;");

        var events = await LoadSimpleListAsync(conn, @"
SELECT COALESCE(Name, 'Event')
FROM Events
WHERE COALESCE(EventDate, '') <> ''
ORDER BY EventDate DESC, Id DESC
LIMIT 5;");

        var messages = await LoadSimpleListAsync(conn, @"
SELECT Subject
FROM InboxMessages
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0
ORDER BY Id DESC
LIMIT 5;", agent.Id);

        var managed = await LoadSimpleListAsync(conn, @"
SELECT (f.FirstName || ' ' || f.LastName || ' · ' || f.WeightClass)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY COALESCE(f.Popularity, 0) DESC, COALESCE(f.Skill, 0) DESC
LIMIT 5;", agent.Id);

        var champions = await LoadSimpleListAsync(conn, @"
SELECT (f.FirstName || ' ' || f.LastName || ' · ' || p.Name)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND COALESCE(f.IsChampion, 0) = 1
ORDER BY COALESCE(f.Popularity, 0) DESC
LIMIT 5;", agent.Id);

        var pendingFightOffers = await LoadSimpleListAsync(conn, @"
SELECT
    ((f.FirstName || ' ' || f.LastName) || ' vs ' || (o.FirstName || ' ' || o.LastName)
        || CASE
            WHEN COALESCE(fo.IsTitleFight, 0) = 1 THEN ' · title fight'
            WHEN COALESCE(fo.IsTitleEliminator, 0) = 1 THEN ' · title eliminator'
            WHEN COALESCE(fo.IsShortNotice, 0) = 1 THEN ' · short notice'
            ELSE ''
           END)
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = fo.FighterId
JOIN Fighters o ON o.Id = fo.OpponentFighterId
WHERE fo.Status = 'Pending'
ORDER BY fo.Id DESC
LIMIT 5;", agent.Id);

        var pendingContractOffers = await LoadSimpleListAsync(conn, @"
SELECT ((f.FirstName || ' ' || f.LastName) || ' · ' || COALESCE(p.Name, 'Promotion'))
FROM ContractOffers co
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = co.FighterId
LEFT JOIN Promotions p ON p.Id = co.PromotionId
WHERE co.Status = 'Pending'
ORDER BY co.Id DESC
LIMIT 5;", agent.Id);

        return new DashboardFeedVm
        {
            Agenda = agenda,
            CompetitivePulse = competitivePulse,
            Events = events,
            Messages = messages,
            Managed = managed,
            Champions = champions,
            PendingFightOfferItems = pendingFightOffers,
            PendingContractOfferItems = pendingContractOffers
        };
    }

    private static async Task<IReadOnlyList<AgendaItemVm>> LoadAgendaAsync(SqliteConnection conn)
    {
        var list = new List<AgendaItemVm>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ScheduledDate, EventType, Headline, Subtitle, Priority
FROM TimeQueue
WHERE COALESCE(Status, 'Pending') = 'Pending'
ORDER BY ScheduledDate, Priority DESC, Id
LIMIT 8;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new AgendaItemVm(
                ScheduledDate: reader["ScheduledDate"]?.ToString() ?? "",
                EventType: reader["EventType"]?.ToString() ?? "",
                Headline: reader["Headline"]?.ToString() ?? "",
                Subtitle: reader["Subtitle"] == DBNull.Value ? null : reader["Subtitle"]?.ToString(),
                Priority: Convert.ToInt32(reader["Priority"])));
        }

        return list;
    }

    private static async Task<IReadOnlyList<string>> LoadSimpleListAsync(SqliteConnection conn, string sql, int? agentId = null)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (agentId.HasValue)
            cmd.Parameters.AddWithValue("$agentId", agentId.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader[0]?.ToString() ?? string.Empty);
        }

        return list;
    }
}
