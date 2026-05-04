using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Helpers;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebRosterService
{
    private readonly SqliteConnectionFactory _factory;

    public WebRosterService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<RosterQueryResult> SearchAsync(
        string? searchText,
        string? weightClass,
        string? country,
        string? status,
        string? sortBy,
        int take = 500)
    {
        using var conn = _factory.CreateConnection();

        var where = new List<string>();
        using var countCmd = conn.CreateCommand();
        using var listCmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            where.Add("(f.FirstName || ' ' || f.LastName LIKE $search OR c.Name LIKE $search)");
            countCmd.Parameters.AddWithValue("$search", $"%{searchText.Trim()}%");
            listCmd.Parameters.AddWithValue("$search", $"%{searchText.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(weightClass))
        {
            where.Add("f.WeightClass = $weightClass");
            countCmd.Parameters.AddWithValue("$weightClass", weightClass.Trim());
            listCmd.Parameters.AddWithValue("$weightClass", weightClass.Trim());
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            where.Add("c.Name = $country");
            countCmd.Parameters.AddWithValue("$country", country.Trim());
            listCmd.Parameters.AddWithValue("$country", country.Trim());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            switch (status.Trim())
            {
                case "Active":
                    where.Add("COALESCE(f.Retired, 0) = 0 AND f.PromotionId IS NOT NULL");
                    break;
                case "Free Agent":
                    where.Add("COALESCE(f.Retired, 0) = 0 AND (f.PromotionId IS NULL OR COALESCE(f.ContractStatus, '') = 'FreeAgent')");
                    break;
                case "Retired":
                    where.Add("COALESCE(f.Retired, 0) = 1");
                    break;
            }
        }

        var whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        var orderBySql = BuildOrderByClause(sortBy);

        countCmd.CommandText = $@"
SELECT COUNT(*)
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
{whereSql};";

        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        listCmd.CommandText = $@"
SELECT
    f.Id,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    COALESCE(f.Age, 0) AS Age,
    f.WeightClass,
    COALESCE(c.Name, '') AS CountryName,
    COALESCE(p.Name, 'Free Agent') AS PromotionName,
    CASE
        WHEN COALESCE(f.Retired, 0) = 1 THEN 'Retired'
        WHEN f.PromotionId IS NULL OR COALESCE(f.ContractStatus, '') = 'FreeAgent' THEN 'Free Agent'
        ELSE 'Active'
    END AS FighterStatus,
    f.Wins,
    f.Losses,
    f.Draws,
    COALESCE(sk.EstimatedSkillMin, MAX(1, f.Skill - 15)) AS EstimatedSkillMin,
    COALESCE(sk.EstimatedSkillMax, MIN(99, f.Skill + 15)) AS EstimatedSkillMax,
    COALESCE(sk.Confidence, 40) AS Confidence,
    COALESCE(sa.Status, '') AS ScoutAssignmentStatus,
    COALESCE(fs.BaseStyle, 'All-Rounder') AS BaseStyle,
    COALESCE(f.ReliabilityScore, 60) AS ReliabilityScore,
    COALESCE(f.MediaHeat, 20) AS MediaHeat
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
LEFT JOIN ScoutKnowledge sk
    ON sk.FighterId = f.Id
   AND sk.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
LEFT JOIN ScoutAssignments sa
    ON sa.FighterId = f.Id
   AND sa.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
   AND sa.Status = 'InProgress'
LEFT JOIN FighterStyles fs ON fs.FighterId = f.Id
{whereSql}
ORDER BY {orderBySql}
LIMIT $take;";
        listCmd.Parameters.AddWithValue("$take", take);

        var items = new List<RosterListItemVm>();
        using (var r = await listCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                items.Add(new RosterListItemVm(
                    Convert.ToInt32(r["Id"]),
                    r["FighterName"]?.ToString() ?? "",
                    Convert.ToInt32(r["Age"]),
                    r["WeightClass"]?.ToString() ?? "",
                    r["CountryName"]?.ToString() ?? "",
                    CountryFlagHelper.GetFlagImageUrl(r["CountryName"]?.ToString()),
                    r["PromotionName"]?.ToString() ?? "Free Agent",
                    r["FighterStatus"]?.ToString() ?? "Active",
                    Convert.ToInt32(r["Wins"]),
                    Convert.ToInt32(r["Losses"]),
                    Convert.ToInt32(r["Draws"]),
                    $"{Convert.ToInt32(r["EstimatedSkillMin"])}-{Convert.ToInt32(r["EstimatedSkillMax"])}",
                    DescribeConfidence(Convert.ToInt32(r["Confidence"])),
                    r["BaseStyle"]?.ToString() ?? "All-Rounder",
                    Convert.ToInt32(r["ReliabilityScore"]),
                    Convert.ToInt32(r["MediaHeat"]),
                    DescribeScoutStatus(
                        Convert.ToInt32(r["Confidence"]),
                        r["ScoutAssignmentStatus"]?.ToString() ?? "")));
            }
        }

        var weights = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT WeightClass FROM Fighters ORDER BY WeightClass;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                weights.Add(r.GetString(0));
        }

        var countries = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT DISTINCT c.Name
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
WHERE c.Name IS NOT NULL AND c.Name <> ''
ORDER BY c.Name;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                countries.Add(r.GetString(0));
        }

        return new RosterQueryResult(
            total,
            items,
            new RosterFilterOptions(weights, countries));
    }

    public async Task StartScoutAsync(int fighterId, string focus = "General")
    {
        using var conn = _factory.CreateConnection();

        int agentId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1;";
            agentId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM ScoutAssignments
WHERE AgentId = $agentId
  AND FighterId = $fighterId
  AND Status = 'InProgress';";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                return;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO ScoutAssignments
(AgentId, FighterId, Focus, Status, ProgressDays, DaysRequired, StartedDate, CompletedDate)
VALUES
($agentId, $fighterId, $focus, 'InProgress', 0, $daysRequired, date('now'), NULL);";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            cmd.Parameters.AddWithValue("$focus", focus);
            cmd.Parameters.AddWithValue("$daysRequired", string.Equals(focus, "Traits", StringComparison.OrdinalIgnoreCase) ? 4 : 3);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO InboxMessages (AgentId, MessageType, Subject, Body, CreatedDate, IsRead, IsArchived, IsDeleted)
VALUES
($agentId, 'ScoutingStarted', 'Scouting assignment launched', 'Your scouting team has started a new read on the selected fighter.', date('now'), 0, 0, 0);";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string DescribeConfidence(int confidence)
        => confidence switch
        {
            >= 90 => "Very High",
            >= 75 => "High",
            >= 60 => "Medium",
            >= 45 => "Low",
            _ => "Very Low"
        };

    private static string DescribeScoutStatus(int confidence, string assignmentStatus)
    {
        if (string.Equals(assignmentStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
            return "Scouting";

        return confidence switch
        {
            >= 90 => "Known",
            >= 70 => "Tracked",
            _ => "Unscouted"
        };
    }

    private static string BuildOrderByClause(string? sortBy)
        => sortBy?.Trim() switch
        {
            "AgeDesc" => "COALESCE(f.Age, 0) DESC, FighterName",
            "AgeAsc" => "COALESCE(f.Age, 0) ASC, FighterName",
            "RecordDesc" => "(COALESCE(f.Wins, 0) - COALESCE(f.Losses, 0)) DESC, COALESCE(f.Wins, 0) DESC, FighterName",
            "CountryAsc" => "COALESCE(c.Name, '') ASC, FighterName",
            "PopularityDesc" => "COALESCE(f.Popularity, 0) DESC, COALESCE(f.MediaHeat, 0) DESC, FighterName",
            _ => "COALESCE(sk.EstimatedSkillMax, f.Skill) DESC, COALESCE(f.Popularity, 0) DESC, COALESCE(f.Wins, 0) DESC, FighterName"
        };
}
