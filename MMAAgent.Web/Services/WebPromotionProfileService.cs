using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

using Microsoft.Data.Sqlite;
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
SELECT
    Id,
    Name,
    COALESCE(CircuitType, 'Professional') AS CircuitType,
    COALESCE(Prestige, 0) AS Prestige,
    COALESCE(Budget, 0) AS Budget,
    COALESCE(IsActive, 1) AS IsActive,
    COALESCE(EventIntervalWeeks, 1) AS EventIntervalWeeks,
    COALESCE(NextEventWeek, 0) AS NextEventWeek
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
                r["CircuitType"]?.ToString() ?? "Professional",
                Convert.ToInt32(r["Prestige"]),
                Convert.ToInt32(r["Budget"]),
                Convert.ToInt32(r["IsActive"]) == 1,
                Convert.ToInt32(r["EventIntervalWeeks"]),
                Convert.ToInt32(r["NextEventWeek"]),
                Array.Empty<PromotionChampionVm>(),
                Array.Empty<PromotionRankingVm>(),
                Array.Empty<PromotionContenderVm>(),
                Array.Empty<PromotionDivisionPictureVm>());
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
                    r["ChampionFighterId"] == DBNull.Value ? null : Convert.ToInt32(r["ChampionFighterId"]),
                    r["FighterName"] == DBNull.Value ? "Vacant" : (r["FighterName"]?.ToString() ?? "Vacant")));
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

        var contenders = new List<PromotionContenderVm>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    cq.WeightClass,
    cq.QueueRank,
    cq.FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    cq.QueueScore,
    COALESCE(cq.Notes, '') AS Notes
FROM ContenderQueue cq
JOIN Fighters f ON f.Id = cq.FighterId
WHERE cq.PromotionId = $id
  AND cq.QueueRank <= 5
ORDER BY cq.WeightClass, cq.QueueRank;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                contenders.Add(new PromotionContenderVm(
                    r["WeightClass"]?.ToString() ?? "",
                    Convert.ToInt32(r["QueueRank"]),
                    Convert.ToInt32(r["FighterId"]),
                    r["FighterName"]?.ToString() ?? "",
                    Convert.ToInt32(r["QueueScore"]),
                    r["Notes"]?.ToString() ?? ""));
            }
        }

        var divisionPictures = BuildDivisionPictures(
            profile.Name,
            champions,
            contenders,
            await LoadConfiguredWeightClassesAsync(conn, promotionId),
            await LoadManagedContendersAsync(conn, promotionId),
            await LoadChampionHistoryAsync(conn, promotionId),
            await LoadDivisionStakesAsync(conn, promotionId),
            await LoadDivisionRivalriesAsync(conn, promotionId));

        return profile with
        {
            Champions = champions,
            Rankings = rankings,
            Contenders = contenders,
            Divisions = divisionPictures
        };
    }

    private static IReadOnlyList<PromotionDivisionPictureVm> BuildDivisionPictures(
        string promotionName,
        IReadOnlyList<PromotionChampionVm> champions,
        IReadOnlyList<PromotionContenderVm> contenders,
        IReadOnlyList<string> configuredWeightClasses,
        IReadOnlyDictionary<string, PromotionContenderVm> managedContendersByWeightClass,
        IReadOnlyDictionary<string, IReadOnlyList<DivisionChampionHistoryVm>> championHistoryByWeightClass,
        IReadOnlyDictionary<string, string> stakesByWeightClass,
        IReadOnlyDictionary<string, string> rivalriesByWeightClass)
    {
        var weightClasses = champions.Select(x => x.WeightClass)
            .Concat(contenders.Select(x => x.WeightClass))
            .Concat(configuredWeightClasses)
            .Concat(managedContendersByWeightClass.Keys)
            .Concat(championHistoryByWeightClass.Keys)
            .Concat(stakesByWeightClass.Keys)
            .Concat(rivalriesByWeightClass.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        return weightClasses
            .Select(weightClass =>
            {
                var champion = champions.FirstOrDefault(x => string.Equals(x.WeightClass, weightClass, StringComparison.OrdinalIgnoreCase));
                var nextContenders = contenders
                    .Where(x => string.Equals(x.WeightClass, weightClass, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.QueueRank)
                    .Take(3)
                    .ToList();

                stakesByWeightClass.TryGetValue(weightClass, out var stakes);
                rivalriesByWeightClass.TryGetValue(weightClass, out var rivalry);
                managedContendersByWeightClass.TryGetValue(weightClass, out var managedContender);
                championHistoryByWeightClass.TryGetValue(weightClass, out var recentChampions);

                return new PromotionDivisionPictureVm(
                    weightClass,
                    champion?.FighterId,
                    champion?.FighterId is int ? (champion.FighterName ?? "Vacant") : "Vacant",
                    nextContenders,
                    managedContender,
                    recentChampions ?? Array.Empty<DivisionChampionHistoryVm>(),
                    stakes,
                    rivalry);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> LoadConfiguredWeightClassesAsync(SqliteConnection conn, int promotionId)
    {
        var result = new List<string>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT WeightClass
FROM PromotionWeightClasses
WHERE PromotionId = $promotionId
ORDER BY WeightClass;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weightClass = reader["WeightClass"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(weightClass))
                continue;

            result.Add(weightClass);
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, PromotionContenderVm>> LoadManagedContendersAsync(SqliteConnection conn, int promotionId)
    {
        var result = new Dictionary<string, PromotionContenderVm>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    cq.WeightClass,
    cq.QueueRank,
    cq.FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    cq.QueueScore,
    COALESCE(cq.Notes, '') AS Notes
FROM ContenderQueue cq
JOIN Fighters f ON f.Id = cq.FighterId
JOIN ManagedFighters mf ON mf.FighterId = cq.FighterId
WHERE cq.PromotionId = $promotionId
  AND COALESCE(mf.IsActive, 1) = 1
  AND mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
ORDER BY cq.WeightClass, cq.QueueRank, cq.QueueScore DESC;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weightClass = reader["WeightClass"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(weightClass) || result.ContainsKey(weightClass))
                continue;

            result[weightClass] = new PromotionContenderVm(
                weightClass,
                Convert.ToInt32(reader["QueueRank"]),
                Convert.ToInt32(reader["FighterId"]),
                reader["FighterName"]?.ToString() ?? "",
                Convert.ToInt32(reader["QueueScore"]),
                reader["Notes"]?.ToString() ?? "");
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<DivisionChampionHistoryVm>>> LoadChampionHistoryAsync(SqliteConnection conn, int promotionId)
    {
        var result = new Dictionary<string, IReadOnlyList<DivisionChampionHistoryVm>>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(fh.WeightClass, '') AS WeightClass,
    fh.WinnerId,
    (w.FirstName || ' ' || w.LastName) AS WinnerName,
    COALESCE(fh.FightDate, '') AS FightDate
FROM FightHistory fh
JOIN Fighters w ON w.Id = fh.WinnerId
WHERE fh.PromotionId = $promotionId
  AND COALESCE(fh.IsTitle, 0) = 1
ORDER BY fh.WeightClass, fh.FightDate DESC, fh.Id DESC;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weightClass = reader["WeightClass"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(weightClass))
                continue;

            if (!result.TryGetValue(weightClass, out var existing))
            {
                existing = new List<DivisionChampionHistoryVm>();
                result[weightClass] = existing;
            }

            var list = (List<DivisionChampionHistoryVm>)existing;
            if (list.Count >= 3)
                continue;

            var fighterId = Convert.ToInt32(reader["WinnerId"]);
            if (list.Any(x => x.FighterId == fighterId))
                continue;

            list.Add(new DivisionChampionHistoryVm(
                fighterId,
                reader["WinnerName"]?.ToString() ?? "Champion",
                reader["FightDate"]?.ToString() ?? ""));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadDivisionStakesAsync(SqliteConnection conn, int promotionId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(f.WeightClass, '') AS WeightClass,
    CASE
        WHEN COALESCE(f.IsTitleFight, 0) = 1 THEN 'Upcoming title fight scheduled'
        WHEN COALESCE(f.IsTitleEliminator, 0) = 1 THEN 'Upcoming title eliminator scheduled'
        ELSE 'Upcoming divisional bout scheduled'
    END AS StakesRead
FROM Fights f
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Fighters fa ON fa.Id = f.FighterAId
WHERE COALESCE(e.PromotionId, fa.PromotionId) = $promotionId
  AND f.Method = 'Scheduled'
  AND COALESCE(f.EventDate, '') <> ''
ORDER BY
  CASE
      WHEN COALESCE(f.IsTitleFight, 0) = 1 THEN 0
      WHEN COALESCE(f.IsTitleEliminator, 0) = 1 THEN 1
      ELSE 2
  END,
  f.EventDate,
  f.Id;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weightClass = reader["WeightClass"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(weightClass) || result.ContainsKey(weightClass))
                continue;

            result[weightClass] = reader["StakesRead"]?.ToString() ?? "Upcoming divisional bout scheduled";
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadDivisionRivalriesAsync(SqliteConnection conn, int promotionId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    fa.WeightClass,
    (fa.FirstName || ' ' || fa.LastName || ' vs ' || fb.FirstName || ' ' || fb.LastName) AS RivalryHeadline,
    COALESCE(r.Intensity, 0) AS Intensity
FROM Rivalries r
JOIN Fighters fa ON fa.Id = r.FighterAId
JOIN Fighters fb ON fb.Id = r.FighterBId
WHERE fa.PromotionId = $promotionId
  AND fb.PromotionId = $promotionId
  AND fa.WeightClass = fb.WeightClass
ORDER BY fa.WeightClass, r.Intensity DESC, r.LastFightDate DESC;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weightClass = reader["WeightClass"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(weightClass) || result.ContainsKey(weightClass))
                continue;

            result[weightClass] = reader["RivalryHeadline"]?.ToString() ?? "";
        }

        return result;
    }
}
