using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebMyFightersService
{
    private readonly SqliteConnectionFactory _factory;

    public WebMyFightersService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ManagedFighterVm>> LoadAsync(
        string? search = null,
        string? promotion = null,
        string? weightClass = null)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var filters = new List<string> { "mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)" };

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("(f.FirstName || ' ' || f.LastName LIKE $search)");
            cmd.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(promotion))
        {
            if (string.Equals(promotion.Trim(), "Free Agent", System.StringComparison.OrdinalIgnoreCase))
            {
                filters.Add("f.PromotionId IS NULL");
            }
            else
            {
                filters.Add("p.Name = $promotion");
                cmd.Parameters.AddWithValue("$promotion", promotion.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(weightClass))
        {
            filters.Add("f.WeightClass = $weightClass");
            cmd.Parameters.AddWithValue("$weightClass", weightClass.Trim());
        }

        cmd.CommandText = $@"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.WeightClass,
    f.PromotionId,
    COALESCE(p.Name,'Free Agent') AS PromotionName,
    pr.RankPosition,
    CASE WHEN t.ChampionFighterId = f.Id THEN 1 ELSE 0 END AS IsChampion,
    f.Wins,
    f.Losses,
    f.Draws,
    f.ContractStatus,
    f.ContractFightsRemaining,
    f.TotalFightsInContract,
    f.Salary
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
LEFT JOIN PromotionRankings pr
    ON pr.FighterId = f.Id
   AND pr.PromotionId = f.PromotionId
   AND pr.WeightClass = f.WeightClass
LEFT JOIN Titles t
   ON t.PromotionId = f.PromotionId
   AND t.WeightClass = f.WeightClass
WHERE {string.Join(" AND ", filters)}
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY f.Popularity DESC, f.Skill DESC, f.Wins DESC;";

        var list = new List<ManagedFighterVm>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ManagedFighterVm(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                r["WeightClass"]?.ToString() ?? "",
                r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
                r["PromotionName"]?.ToString() ?? "",
                r["RankPosition"] == DBNull.Value ? null : Convert.ToInt32(r["RankPosition"]),
                Convert.ToInt32(r["IsChampion"]) == 1,
                Convert.ToInt32(r["Wins"]),
                Convert.ToInt32(r["Losses"]),
                Convert.ToInt32(r["Draws"]),
                r["ContractStatus"]?.ToString() ?? "",
                Convert.ToInt32(r["ContractFightsRemaining"]),
                Convert.ToInt32(r["TotalFightsInContract"]),
                Convert.ToInt32(r["Salary"])));
        }

        return list;
    }

    public async Task<(IReadOnlyList<string> Promotions, IReadOnlyList<string> WeightClasses)> LoadFiltersAsync()
    {
        using var conn = _factory.CreateConnection();

        var promotions = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT DISTINCT COALESCE(p.Name,'Free Agent') AS PromotionName
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
WHERE mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY PromotionName;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                promotions.Add(r.GetString(0));
        }

        var weights = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT DISTINCT f.WeightClass
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY f.WeightClass;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                weights.Add(r.GetString(0));
        }

        return (promotions, weights);
    }

    public async Task<IReadOnlyList<PromotionOptionVm>> LoadPromotionTargetsAsync()
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Name
FROM Promotions
WHERE COALESCE(IsActive, 1) = 1
ORDER BY COALESCE(Prestige, 0) DESC, Name;";

        var list = new List<PromotionOptionVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PromotionOptionVm(
                Convert.ToInt32(reader["Id"]),
                reader["Name"]?.ToString() ?? string.Empty));
        }

        return list;
    }
}
