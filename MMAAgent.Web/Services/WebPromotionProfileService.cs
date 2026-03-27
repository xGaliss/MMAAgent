using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebPromotionProfileService
{
    private readonly SqliteConnectionFactory _factory;

    public WebPromotionProfileService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<PromotionProfileVm?> LoadAsync(int promotionId)
    {
        using var conn = _factory.CreateConnection();

        PromotionProfileVm? profile;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT Id, Name, Prestige, Budget, IsActive, EventIntervalWeeks, NextEventWeek
FROM Promotions
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return null;

            profile = new PromotionProfileVm(
                Convert.ToInt32(r["Id"]),
                r["Name"]?.ToString() ?? "",
                Convert.ToInt32(r["Prestige"]),
                Convert.ToInt32(r["Budget"]),
                Convert.ToInt32(r["IsActive"]) == 1,
                Convert.ToInt32(r["EventIntervalWeeks"]),
                Convert.ToInt32(r["NextEventWeek"]),
                Array.Empty<PromotionChampionVm>(),
                Array.Empty<PromotionRankingVm>());
        }

        var champions = new List<PromotionChampionVm>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    t.WeightClass,
    t.ChampionFighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName
FROM Titles t
LEFT JOIN Fighters f ON f.Id = t.ChampionFighterId
WHERE t.PromotionId = $id
ORDER BY t.WeightClass;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                champions.Add(new PromotionChampionVm(
                    r["WeightClass"]?.ToString() ?? "",
                    Convert.ToInt32(r["ChampionFighterId"]),
                    r["FighterName"]?.ToString() ?? ""));
            }
        }

        var rankings = new List<PromotionRankingVm>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    WeightClass,
    RankPosition,
    FighterId,
    FighterName
FROM (
    SELECT
        pr.WeightClass,
        pr.RankPosition,
        pr.FighterId,
        (f.FirstName || ' ' || f.LastName) AS FighterName,
        ROW_NUMBER() OVER (PARTITION BY pr.WeightClass ORDER BY pr.RankPosition) AS rn
    FROM PromotionRankings pr
    LEFT JOIN Fighters f ON f.Id = pr.FighterId
    WHERE pr.PromotionId = $id
)
WHERE rn <= 10
ORDER BY WeightClass, RankPosition;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                rankings.Add(new PromotionRankingVm(
                    r["WeightClass"]?.ToString() ?? "",
                    Convert.ToInt32(r["RankPosition"]),
                    Convert.ToInt32(r["FighterId"]),
                    r["FighterName"]?.ToString() ?? ""));
            }
        }

        return profile with
        {
            Champions = champions,
            Rankings = rankings
        };
    }
}
