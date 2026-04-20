using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;
using System.Linq;

namespace MMAAgent.Web.Services;

public sealed class FightProfileReadService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ISavePathProvider _savePath;

    public FightProfileReadService(SqliteConnectionFactory factory, ISavePathProvider savePath)
    {
        _factory = factory;
        _savePath = savePath;
    }

    public async Task<(FighterProfile? Fighter, IReadOnlyList<FightHistoryItem> History)> LoadAsync(int fighterId, int take = 15)
    {
        if (string.IsNullOrWhiteSpace(_savePath.CurrentPath))
            throw new InvalidOperationException("No hay DB activa.");

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var fighter = await LoadProfileAsync(conn, tx, fighterId);
        var history = await LoadHistoryAsync(conn, tx, fighterId, take);

        tx.Commit();
        return (fighter, history);
    }

    private static async Task<FighterProfile?> LoadProfileAsync(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id,
    f.FirstName || ' ' || f.LastName AS Name,
    COALESCE(c.Name,'') AS CountryName,
    f.WeightClass,
    f.Age,
    f.Wins,
    f.Losses,
    f.Draws,
    f.KOWins,
    f.SubWins,
    f.DecWins,
    f.Skill,
    f.Potential,
    f.Popularity,
    COALESCE(f.Marketability, 50) AS Marketability,
    COALESCE(f.Momentum, 50) AS Momentum,
    COALESCE(f.WeightMissCount, 0) AS WeightMissCount,
    COALESCE(f.CampWithdrawalCount, 0) AS CampWithdrawalCount,
    f.Striking,
    f.Grappling,
    f.Wrestling,
    f.Cardio,
    f.Chin,
    f.FightIQ,
    f.ContractStatus,
    f.PromotionId,
    COALESCE(p.Name,'') AS PromotionName,
    f.Salary,
    f.ContractFightsRemaining,
    f.TotalFightsInContract,
    COALESCE(f.IsBooked, 0) AS IsBooked,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.MedicalSuspensionWeeksRemaining, 0) AS MedicalSuspensionWeeksRemaining,
    COALESCE(pr.RankPosition, 0) AS RankPosition,
    CASE WHEN t.ChampionFighterId = f.Id THEN 1 ELSE 0 END AS IsChampion,
    (
        SELECT op.FirstName || ' ' || op.LastName
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledOpponentName,
    (
        SELECT e.Name
        FROM Fights sf
        LEFT JOIN Events e ON e.Id = sf.EventId
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledEventName,
    (
        SELECT sf.EventDate
        FROM Fights sf
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledEventDate,
    (
        SELECT COUNT(*)
        FROM FightHistory fh
        WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
          AND COALESCE(fh.IsTitle, 0) = 1
    ) AS TitleFightAppearances,
    (
        SELECT COUNT(*)
        FROM FightHistory fh
        WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
          AND (
              COALESCE(fh.CardSegment, '') = 'MainCard'
              OR COALESCE(fh.IsMainEvent, 0) = 1
              OR COALESCE(fh.IsCoMainEvent, 0) = 1
          )
    ) AS MainCardAppearances,
    (
        SELECT COUNT(*)
        FROM FightHistory fh
        WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
          AND COALESCE(fh.IsMainEvent, 0) = 1
    ) AS MainEventAppearances,
    (
        SELECT COUNT(*)
        FROM FightHistory fh
        WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
          AND COALESCE(fh.IsCoMainEvent, 0) = 1
    ) AS CoMainEventAppearances,
    COALESCE(fs.BaseStyle, 'All-Rounder') AS BaseStyle,
    COALESCE(fs.TacticalStyle, 'Measured') AS TacticalStyle,
    COALESCE(fs.StyleSummary, '') AS StyleSummary,
    COALESCE(st.Form, 50) AS Form,
    COALESCE(st.Energy, 70) AS Energy,
    COALESCE(st.Sharpness, 50) AS Sharpness,
    COALESCE(st.Morale, 50) AS Morale,
    COALESCE(st.CampQuality, 50) AS CampQuality,
    COALESCE(st.WeightCutReadiness, 55) AS WeightCutReadiness,
    COALESCE(st.InjuryRisk, 20) AS InjuryRisk,
    COALESCE(st.CurrentPhase, 'Idle') AS CurrentPhase,
    st.NextMilestoneType,
    st.NextMilestoneDate,
    (
        SELECT CASE
            WHEN COALESCE(fp.WeighInNotes, '') <> '' THEN fp.WeighInNotes
            WHEN COALESCE(fp.FightWeekNotes, '') <> '' THEN fp.FightWeekNotes
            WHEN COALESCE(fp.CampNotes, '') <> '' THEN fp.CampNotes
            ELSE NULL
        END
        FROM FightPreparations fp
        WHERE fp.FighterId = f.Id
        ORDER BY COALESCE(fp.LastUpdatedDate, '0001-01-01') DESC, fp.FightId DESC
        LIMIT 1
    ) AS LatestPrepNote,
    (
        SELECT group_concat(TraitCode, '|')
        FROM (
            SELECT ft.TraitCode
            FROM FighterTraits ft
            WHERE ft.FighterId = f.Id
            ORDER BY ft.Intensity DESC, ft.TraitCode
            LIMIT 4
        )
    ) AS TraitCodes
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
LEFT JOIN FighterStyles fs ON fs.FighterId = f.Id
LEFT JOIN FighterStates st ON st.FighterId = f.Id
LEFT JOIN PromotionRankings pr
    ON pr.FighterId = f.Id
   AND pr.PromotionId = f.PromotionId
   AND pr.WeightClass = f.WeightClass
LEFT JOIN Titles t
    ON t.PromotionId = f.PromotionId
   AND t.WeightClass = f.WeightClass
WHERE f.Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;

        int rank = Convert.ToInt32(r["RankPosition"]);
        int wins = Convert.ToInt32(r["Wins"]);
        int koWins = Convert.ToInt32(r["KOWins"]);
        int subWins = Convert.ToInt32(r["SubWins"]);
        int decWins = Convert.ToInt32(r["DecWins"]);
        double finishRate = wins > 0
            ? Math.Round(((double)(koWins + subWins) / wins) * 100.0, 1)
            : 0;

        return new FighterProfile(
            Id: Convert.ToInt32(r["Id"]),
            Name: r["Name"]?.ToString() ?? "",
            CountryName: r["CountryName"]?.ToString() ?? "",
            WeightClass: r["WeightClass"]?.ToString() ?? "",
            Age: Convert.ToInt32(r["Age"]),
            Wins: wins,
            Losses: Convert.ToInt32(r["Losses"]),
            Draws: Convert.ToInt32(r["Draws"]),
            KOWins: koWins,
            SubWins: subWins,
            DecWins: decWins,
            Skill: Convert.ToInt32(r["Skill"]),
            Potential: Convert.ToInt32(r["Potential"]),
            Popularity: Convert.ToInt32(r["Popularity"]),
            Marketability: Convert.ToInt32(r["Marketability"]),
            Momentum: Convert.ToInt32(r["Momentum"]),
            WeightMissCount: Convert.ToInt32(r["WeightMissCount"]),
            CampWithdrawalCount: Convert.ToInt32(r["CampWithdrawalCount"]),
            Striking: Convert.ToInt32(r["Striking"]),
            Grappling: Convert.ToInt32(r["Grappling"]),
            Wrestling: Convert.ToInt32(r["Wrestling"]),
            Cardio: Convert.ToInt32(r["Cardio"]),
            Chin: Convert.ToInt32(r["Chin"]),
            FightIQ: Convert.ToInt32(r["FightIQ"]),
            ContractStatus: r["ContractStatus"]?.ToString() ?? "",
            PromotionId: r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
            PromotionName: r["PromotionName"]?.ToString(),
            Salary: Convert.ToInt32(r["Salary"]),
            ContractFightsRemaining: Convert.ToInt32(r["ContractFightsRemaining"]),
            TotalFightsInContract: Convert.ToInt32(r["TotalFightsInContract"]),
            RankPosition: rank > 0 ? rank : null,
            IsChampion: Convert.ToInt32(r["IsChampion"]) == 1,
            TitleFightAppearances: Convert.ToInt32(r["TitleFightAppearances"]),
            MainCardAppearances: Convert.ToInt32(r["MainCardAppearances"]),
            MainEventAppearances: Convert.ToInt32(r["MainEventAppearances"]),
            CoMainEventAppearances: Convert.ToInt32(r["CoMainEventAppearances"]),
            FinishRate: finishRate,
            BaseStyle: r["BaseStyle"]?.ToString() ?? "All-Rounder",
            TacticalStyle: r["TacticalStyle"]?.ToString() ?? "Measured",
            StyleSummary: r["StyleSummary"]?.ToString() ?? "",
            Traits: ParseTraits(r["TraitCodes"]?.ToString()),
            Form: Convert.ToInt32(r["Form"]),
            Energy: Convert.ToInt32(r["Energy"]),
            Sharpness: Convert.ToInt32(r["Sharpness"]),
            Morale: Convert.ToInt32(r["Morale"]),
            CampQuality: Convert.ToInt32(r["CampQuality"]),
            WeightCutReadiness: Convert.ToInt32(r["WeightCutReadiness"]),
            InjuryRisk: Convert.ToInt32(r["InjuryRisk"]),
            CurrentPhase: r["CurrentPhase"]?.ToString() ?? "Idle",
            NextMilestoneType: r["NextMilestoneType"]?.ToString(),
            NextMilestoneDate: r["NextMilestoneDate"]?.ToString(),
            IsBooked: Convert.ToInt32(r["IsBooked"]) == 1,
            WeeksUntilAvailable: Convert.ToInt32(r["WeeksUntilAvailable"]),
            InjuryWeeksRemaining: Convert.ToInt32(r["InjuryWeeksRemaining"]),
            MedicalSuspensionWeeksRemaining: Convert.ToInt32(r["MedicalSuspensionWeeksRemaining"]),
            LatestPrepNote: r["LatestPrepNote"]?.ToString(),
            ScheduledOpponentName: r["ScheduledOpponentName"]?.ToString(),
            ScheduledEventName: r["ScheduledEventName"]?.ToString(),
            ScheduledEventDate: r["ScheduledEventDate"]?.ToString()
        );
    }

    private static IReadOnlyList<string> ParseTraits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<FightHistoryItem>> LoadHistoryAsync(SqliteConnection conn, SqliteTransaction tx, int id, int take)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    fh.FightDate,
    CASE WHEN fh.WinnerId = $id THEN 1 ELSE 0 END AS Won,
    fh.Method,
    fh.IsTitle,
    p.Name AS PromotionName,
    e.Name AS EventName,
    (op.FirstName || ' ' || op.LastName) AS Opponent
FROM FightHistory fh
JOIN Promotions p ON p.Id = fh.PromotionId
LEFT JOIN Events e ON e.Id = fh.EventId
JOIN Fighters op ON op.Id = CASE
    WHEN fh.FighterAId = $id THEN fh.FighterBId
    ELSE fh.FighterAId
END
WHERE (fh.FighterAId = $id OR fh.FighterBId = $id)
ORDER BY fh.Id DESC
LIMIT $take;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$take", take);

        var list = new List<FightHistoryItem>();
        using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            bool won = Convert.ToInt32(r["Won"]) == 1;

            list.Add(new FightHistoryItem(
                Date: r["FightDate"]?.ToString() ?? "",
                Opponent: r["Opponent"]?.ToString() ?? "",
                Result: won ? "W" : "L",
                Method: r["Method"]?.ToString() ?? "",
                IsTitle: Convert.ToInt32(r["IsTitle"]) == 1,
                Promotion: r["PromotionName"]?.ToString() ?? "",
                EventName: r["EventName"]?.ToString()
            ));
        }

        return list;
    }
}
