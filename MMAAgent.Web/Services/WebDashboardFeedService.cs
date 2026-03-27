using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Web.Services;

public sealed class WebDashboardFeedService
{
    private readonly SqliteConnectionFactory _factory;

    public WebDashboardFeedService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(IReadOnlyList<string> Events, IReadOnlyList<string> Messages, IReadOnlyList<string> Managed, IReadOnlyList<string> Champions, int PendingOffers)> LoadAsync()
    {
        using var conn = _factory.CreateConnection();

        var events = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT EventDate || ' · ' || Name FROM Events ORDER BY Id DESC LIMIT 5;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) events.Add(r.GetString(0));
        }

        var messages = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT Subject
FROM InboxMessages
WHERE AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
ORDER BY Id DESC
LIMIT 5;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) messages.Add(r.GetString(0));
        }

        var managed = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT (f.FirstName || ' ' || f.LastName)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
ORDER BY f.Popularity DESC, f.Skill DESC
LIMIT 5;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) managed.Add(r.GetString(0));
        }

        var champions = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT DISTINCT (f.FirstName || ' ' || f.LastName)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
JOIN Titles t ON t.ChampionFighterId = f.Id
WHERE mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
LIMIT 5;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) champions.Add(r.GetString(0));
        }

        int pendingOffers;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId
WHERE mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND fo.Status = 'Pending';";
            pendingOffers = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return (events, messages, managed, champions, pendingOffers);
    }
}
