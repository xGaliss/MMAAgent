using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Helpers;
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
        var storylines = fighter is null
            ? Array.Empty<FighterStorylineItem>()
            : await LoadStorylinesAsync(conn, tx, fighterId, 6);
        var legacyTags = fighter is null
            ? Array.Empty<FighterLegacyTagItem>()
            : await LoadLegacyTagsAsync(conn, tx, fighterId, 6);

        tx.Commit();
        return (fighter is null ? null : fighter with { Storylines = storylines, LegacyTags = legacyTags }, history);
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
    COALESCE(f.CareerStage, 'Pro') AS CareerStage,
    f.WeightClass,
    f.Age,
    f.Wins,
    f.Losses,
    f.Draws,
    COALESCE(f.AmateurWins, 0) AS AmateurWins,
    COALESCE(f.AmateurLosses, 0) AS AmateurLosses,
    COALESCE(f.AmateurDraws, 0) AS AmateurDraws,
    f.KOWins,
    f.SubWins,
    f.DecWins,
    f.Skill,
    f.Potential,
    f.Popularity,
    COALESCE(f.Marketability, 50) AS Marketability,
    COALESCE(f.Momentum, 50) AS Momentum,
    COALESCE(f.ReliabilityScore, 60) AS ReliabilityScore,
    COALESCE(f.MediaHeat, 20) AS MediaHeat,
    COALESCE(f.DamageMiles, 0) AS DamageMiles,
    COALESCE(f.WeightMissCount, 0) AS WeightMissCount,
    COALESCE(f.CampWithdrawalCount, 0) AS CampWithdrawalCount,
    COALESCE(f.Ambition, 50) AS Ambition,
    COALESCE(f.Discipline, 50) AS Discipline,
    COALESCE(f.RiskTolerance, 50) AS RiskTolerance,
    COALESCE(f.Stability, 50) AS Stability,
    COALESCE(f.Showmanship, 40) AS Showmanship,
    f.Striking,
    f.Grappling,
    f.Wrestling,
    f.Cardio,
    f.Chin,
    f.FightIQ,
    COALESCE(sk.Confidence, 40) AS ScoutConfidence,
    COALESCE(sa.Status, '') AS ScoutAssignmentStatus,
    COALESCE(sk.EstimatedSkillMin, MAX(1, f.Skill - 15)) AS EstimatedSkillMin,
    COALESCE(sk.EstimatedSkillMax, MIN(99, f.Skill + 15)) AS EstimatedSkillMax,
    COALESCE(sk.EstimatedPotentialMin, MAX(1, f.Potential - 18)) AS EstimatedPotentialMin,
    COALESCE(sk.EstimatedPotentialMax, MIN(99, f.Potential + 18)) AS EstimatedPotentialMax,
    COALESCE(sk.EstimatedStrikingMin, MAX(1, f.Striking - 15)) AS EstimatedStrikingMin,
    COALESCE(sk.EstimatedStrikingMax, MIN(99, f.Striking + 15)) AS EstimatedStrikingMax,
    COALESCE(sk.EstimatedGrapplingMin, MAX(1, f.Grappling - 15)) AS EstimatedGrapplingMin,
    COALESCE(sk.EstimatedGrapplingMax, MIN(99, f.Grappling + 15)) AS EstimatedGrapplingMax,
    COALESCE(sk.EstimatedWrestlingMin, MAX(1, f.Wrestling - 15)) AS EstimatedWrestlingMin,
    COALESCE(sk.EstimatedWrestlingMax, MIN(99, f.Wrestling + 15)) AS EstimatedWrestlingMax,
    COALESCE(sk.EstimatedCardioMin, MAX(1, f.Cardio - 15)) AS EstimatedCardioMin,
    COALESCE(sk.EstimatedCardioMax, MIN(99, f.Cardio + 15)) AS EstimatedCardioMax,
    COALESCE(sk.EstimatedChinMin, MAX(1, f.Chin - 15)) AS EstimatedChinMin,
    COALESCE(sk.EstimatedChinMax, MIN(99, f.Chin + 15)) AS EstimatedChinMax,
    COALESCE(sk.EstimatedFightIQMin, MAX(1, f.FightIQ - 15)) AS EstimatedFightIQMin,
    COALESCE(sk.EstimatedFightIQMax, MIN(99, f.FightIQ + 15)) AS EstimatedFightIQMax,
    f.ContractStatus,
    f.PromotionId,
    COALESCE(p.Name,'') AS PromotionName,
    COALESCE(p.CircuitType, 'Professional') AS PromotionCircuitType,
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
        SELECT COALESCE(ofs.BaseStyle, 'All-Rounder') || ' · ' || COALESCE(ofs.TacticalStyle, 'Measured')
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        LEFT JOIN FighterStyles ofs ON ofs.FighterId = op.Id
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledOpponentStyleSummary,
    (
        SELECT op.Id
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledOpponentId,
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
        SELECT op.Wins || '-' || op.Losses || '-' || op.Draws
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS ScheduledOpponentRecord,
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
        SELECT op.Striking
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentStriking,
    (
        SELECT op.Grappling
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentGrappling,
    (
        SELECT op.Wrestling
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentWrestling,
    (
        SELECT op.Cardio
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentCardio,
    (
        SELECT op.Chin
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentChin,
    (
        SELECT op.FightIQ
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentFightIQ,
    (
        SELECT COALESCE(op.WeightMissCount, 0)
        FROM Fights sf
        JOIN Fighters op ON op.Id = CASE WHEN sf.FighterAId = f.Id THEN sf.FighterBId ELSE sf.FighterAId END
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS OpponentWeightMissCount,
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
    (
        SELECT COALESCE(fp.CampFocus, '')
        FROM FightPreparations fp
        JOIN Fights sf ON sf.Id = fp.FightId
        WHERE fp.FighterId = f.Id
          AND sf.Method = 'Scheduled'
          AND COALESCE(sf.EventDate, '9999-12-31') > COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), '0001-01-01')
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS CampFocus,
    st.NextMilestoneType,
    st.NextMilestoneDate,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM ManagedFighters mfSelf
            WHERE mfSelf.FighterId = f.Id
              AND mfSelf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
              AND COALESCE(mfSelf.IsActive, 1) = 1
        ) THEN 1
        ELSE 0
    END AS IsManagedByPlayer,
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
LEFT JOIN ScoutKnowledge sk
    ON sk.FighterId = f.Id
   AND sk.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
LEFT JOIN ScoutAssignments sa
    ON sa.FighterId = f.Id
   AND sa.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
   AND sa.Status = 'InProgress'
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
        var scheduledOpponentStyleSummary = r["ScheduledOpponentStyleSummary"]?.ToString();
        var campRecommendation = BuildCampRecommendation(
            baseStyle: r["BaseStyle"]?.ToString() ?? "All-Rounder",
            tacticalStyle: r["TacticalStyle"]?.ToString() ?? "Measured",
            striking: Convert.ToInt32(r["Striking"]),
            grappling: Convert.ToInt32(r["Grappling"]),
            wrestling: Convert.ToInt32(r["Wrestling"]),
            cardio: Convert.ToInt32(r["Cardio"]),
            chin: Convert.ToInt32(r["Chin"]),
            fightIq: Convert.ToInt32(r["FightIQ"]),
            weightCutReadiness: Convert.ToInt32(r["WeightCutReadiness"]),
            injuryRisk: Convert.ToInt32(r["InjuryRisk"]),
            energy: Convert.ToInt32(r["Energy"]),
            damageMiles: Convert.ToInt32(r["DamageMiles"]),
            ownWeightMissCount: Convert.ToInt32(r["WeightMissCount"]),
            scheduledOpponentName: r["ScheduledOpponentName"]?.ToString(),
            scheduledOpponentStyleSummary: scheduledOpponentStyleSummary,
            opponentStriking: ReadNullableInt(r, "OpponentStriking"),
            opponentGrappling: ReadNullableInt(r, "OpponentGrappling"),
            opponentWrestling: ReadNullableInt(r, "OpponentWrestling"),
            opponentCardio: ReadNullableInt(r, "OpponentCardio"),
            opponentChin: ReadNullableInt(r, "OpponentChin"),
            opponentFightIq: ReadNullableInt(r, "OpponentFightIQ"),
            opponentWeightMissCount: ReadNullableInt(r, "OpponentWeightMissCount"));
        double finishRate = wins > 0
            ? Math.Round(((double)(koWins + subWins) / wins) * 100.0, 1)
            : 0;

        return new FighterProfile(
            Id: Convert.ToInt32(r["Id"]),
            Name: r["Name"]?.ToString() ?? "",
            CountryName: r["CountryName"]?.ToString() ?? "",
            CountryFlagUrl: CountryFlagHelper.GetFlagImageUrl(r["CountryName"]?.ToString()),
            CareerStage: r["CareerStage"]?.ToString() ?? "Pro",
            WeightClass: r["WeightClass"]?.ToString() ?? "",
            Age: Convert.ToInt32(r["Age"]),
            Wins: wins,
            Losses: Convert.ToInt32(r["Losses"]),
            Draws: Convert.ToInt32(r["Draws"]),
            AmateurWins: Convert.ToInt32(r["AmateurWins"]),
            AmateurLosses: Convert.ToInt32(r["AmateurLosses"]),
            AmateurDraws: Convert.ToInt32(r["AmateurDraws"]),
            KOWins: koWins,
            SubWins: subWins,
            DecWins: decWins,
            Skill: Convert.ToInt32(r["Skill"]),
            Potential: Convert.ToInt32(r["Potential"]),
            Popularity: Convert.ToInt32(r["Popularity"]),
            Marketability: Convert.ToInt32(r["Marketability"]),
            Momentum: Convert.ToInt32(r["Momentum"]),
            ReliabilityScore: Convert.ToInt32(r["ReliabilityScore"]),
            MediaHeat: Convert.ToInt32(r["MediaHeat"]),
            DamageMiles: Convert.ToInt32(r["DamageMiles"]),
            WeightMissCount: Convert.ToInt32(r["WeightMissCount"]),
            CampWithdrawalCount: Convert.ToInt32(r["CampWithdrawalCount"]),
            Ambition: Convert.ToInt32(r["Ambition"]),
            Discipline: Convert.ToInt32(r["Discipline"]),
            RiskTolerance: Convert.ToInt32(r["RiskTolerance"]),
            Stability: Convert.ToInt32(r["Stability"]),
            Showmanship: Convert.ToInt32(r["Showmanship"]),
            Striking: Convert.ToInt32(r["Striking"]),
            Grappling: Convert.ToInt32(r["Grappling"]),
            Wrestling: Convert.ToInt32(r["Wrestling"]),
            Cardio: Convert.ToInt32(r["Cardio"]),
            Chin: Convert.ToInt32(r["Chin"]),
            FightIQ: Convert.ToInt32(r["FightIQ"]),
            ScoutConfidence: Convert.ToInt32(r["ScoutConfidence"]),
            ScoutStatus: DescribeScoutStatus(
                Convert.ToInt32(r["ScoutConfidence"]),
                r["ScoutAssignmentStatus"]?.ToString() ?? ""),
            EstimatedSkillMin: Convert.ToInt32(r["EstimatedSkillMin"]),
            EstimatedSkillMax: Convert.ToInt32(r["EstimatedSkillMax"]),
            EstimatedPotentialMin: Convert.ToInt32(r["EstimatedPotentialMin"]),
            EstimatedPotentialMax: Convert.ToInt32(r["EstimatedPotentialMax"]),
            EstimatedStrikingMin: Convert.ToInt32(r["EstimatedStrikingMin"]),
            EstimatedStrikingMax: Convert.ToInt32(r["EstimatedStrikingMax"]),
            EstimatedGrapplingMin: Convert.ToInt32(r["EstimatedGrapplingMin"]),
            EstimatedGrapplingMax: Convert.ToInt32(r["EstimatedGrapplingMax"]),
            EstimatedWrestlingMin: Convert.ToInt32(r["EstimatedWrestlingMin"]),
            EstimatedWrestlingMax: Convert.ToInt32(r["EstimatedWrestlingMax"]),
            EstimatedCardioMin: Convert.ToInt32(r["EstimatedCardioMin"]),
            EstimatedCardioMax: Convert.ToInt32(r["EstimatedCardioMax"]),
            EstimatedChinMin: Convert.ToInt32(r["EstimatedChinMin"]),
            EstimatedChinMax: Convert.ToInt32(r["EstimatedChinMax"]),
            EstimatedFightIQMin: Convert.ToInt32(r["EstimatedFightIQMin"]),
            EstimatedFightIQMax: Convert.ToInt32(r["EstimatedFightIQMax"]),
            ContractStatus: r["ContractStatus"]?.ToString() ?? "",
            PromotionId: r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
            PromotionName: r["PromotionName"]?.ToString(),
            PromotionCircuitType: r["PromotionCircuitType"]?.ToString() ?? "Professional",
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
            CampFocus: string.IsNullOrWhiteSpace(r["CampFocus"]?.ToString()) ? null : r["CampFocus"]?.ToString(),
            CampRecommendationFocus: campRecommendation?.Focus,
            CampRecommendationReason: campRecommendation?.Reason,
            ScheduledOpponentStyleSummary: scheduledOpponentStyleSummary,
            NextMilestoneType: r["NextMilestoneType"]?.ToString(),
            NextMilestoneDate: r["NextMilestoneDate"]?.ToString(),
            IsBooked: Convert.ToInt32(r["IsBooked"]) == 1,
            WeeksUntilAvailable: Convert.ToInt32(r["WeeksUntilAvailable"]),
            InjuryWeeksRemaining: Convert.ToInt32(r["InjuryWeeksRemaining"]),
            MedicalSuspensionWeeksRemaining: Convert.ToInt32(r["MedicalSuspensionWeeksRemaining"]),
            IsManagedByPlayer: Convert.ToInt32(r["IsManagedByPlayer"]) == 1,
            Storylines: Array.Empty<FighterStorylineItem>(),
            LegacyTags: Array.Empty<FighterLegacyTagItem>(),
            LatestPrepNote: r["LatestPrepNote"]?.ToString(),
            ScheduledOpponentId: r["ScheduledOpponentId"] == DBNull.Value ? null : Convert.ToInt32(r["ScheduledOpponentId"]),
            ScheduledOpponentName: r["ScheduledOpponentName"]?.ToString(),
            ScheduledOpponentRecord: r["ScheduledOpponentRecord"]?.ToString(),
            ScheduledEventName: r["ScheduledEventName"]?.ToString(),
            ScheduledEventDate: r["ScheduledEventDate"]?.ToString()
        );
    }

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

    private static int? ReadNullableInt(SqliteDataReader reader, string columnName)
        => reader[columnName] == DBNull.Value ? null : Convert.ToInt32(reader[columnName]);

    // This is a staff-facing heuristic, not a hidden simulation rule.
    // It translates matchup signals into a readable camp plan for the player.
    private static CampRecommendation? BuildCampRecommendation(
        string baseStyle,
        string tacticalStyle,
        int striking,
        int grappling,
        int wrestling,
        int cardio,
        int chin,
        int fightIq,
        int weightCutReadiness,
        int injuryRisk,
        int energy,
        int damageMiles,
        int ownWeightMissCount,
        string? scheduledOpponentName,
        string? scheduledOpponentStyleSummary,
        int? opponentStriking,
        int? opponentGrappling,
        int? opponentWrestling,
        int? opponentCardio,
        int? opponentChin,
        int? opponentFightIq,
        int? opponentWeightMissCount)
    {
        if (string.IsNullOrWhiteSpace(scheduledOpponentName))
            return null;

        var opponentLabel = string.IsNullOrWhiteSpace(scheduledOpponentStyleSummary)
            ? "the matchup"
            : scheduledOpponentStyleSummary;

        if (weightCutReadiness <= 42 || ownWeightMissCount >= 2)
        {
            return new CampRecommendation(
                "WeightManagement",
                $"Staff read: keep the cut clean first. Your side is the bigger risk than {scheduledOpponentName}'s look, so weight management protects the booking.");
        }

        if (injuryRisk >= 68 || energy <= 44 || damageMiles >= 48)
        {
            return new CampRecommendation(
                "Recovery",
                $"Staff read: protect the body this camp. The concern is arriving fresh enough for {scheduledOpponentName}, not squeezing out one more hard push.");
        }

        if (opponentWrestling.HasValue && opponentWrestling.Value >= 74 && opponentWrestling.Value - wrestling >= 8)
        {
            var focus = cardio >= 70 && cardio >= wrestling + 3 ? "Cardio" : "Wrestling";
            var reason = focus == "Cardio"
                ? $"{scheduledOpponentName} reads as a strong wrestling grinder ({opponentLabel}). Cardio gives you the best chance to survive pace, scrambles and late rounds."
                : $"{scheduledOpponentName} reads as a strong wrestling grinder ({opponentLabel}). Wrestling camp is the cleanest answer to entries, clinch control and defensive wrestling.";

            return new CampRecommendation(focus, $"Staff read: {reason}");
        }

        if (opponentGrappling.HasValue && opponentGrappling.Value >= 74 && opponentGrappling.Value - grappling >= 8)
        {
            return new CampRecommendation(
                "Wrestling",
                $"Staff read: {scheduledOpponentName} looks dangerous in grappling exchanges ({opponentLabel}). Wrestling focus should help with top control, scrambles and keeping the fight in safer phases.");
        }

        if (opponentCardio.HasValue && opponentCardio.Value >= 76)
        {
            return new CampRecommendation(
                "Cardio",
                $"Staff read: {scheduledOpponentName} looks built for pace ({opponentLabel}). Cardio focus helps you survive volume and keep your own work rate deep into the fight.");
        }

        if (opponentWeightMissCount.GetValueOrDefault() >= 2 && cardio >= 58)
        {
            return new CampRecommendation(
                "Cardio",
                $"Staff read: {scheduledOpponentName} has shown weight trouble before. A cardio camp gives you the best chance to weaponize pace if the opponent fades after the cut.");
        }

        if (opponentStriking.HasValue && opponentStriking.Value >= 74 && opponentStriking.Value - striking >= 9)
        {
            if (wrestling >= opponentWrestling.GetValueOrDefault() + 4 || grappling >= opponentGrappling.GetValueOrDefault() + 4)
            {
                return new CampRecommendation(
                    "Wrestling",
                    $"Staff read: {scheduledOpponentName} is the more dangerous striker ({opponentLabel}). Wrestling focus is the safer route to break rhythm and avoid a clean striking fight.");
            }

            var defensiveFocus = chin <= 55 || fightIq <= 56 ? "Recovery" : "Cardio";
            var defensiveReason = defensiveFocus == "Recovery"
                ? "Recovery helps you arrive steadier and less exposed to a bad exchange."
                : "Cardio helps you stay disciplined and move through rounds without panic.";

            return new CampRecommendation(
                defensiveFocus,
                $"Staff read: {scheduledOpponentName} is the sharper striker ({opponentLabel}). {defensiveReason}");
        }

        if (striking - opponentStriking.GetValueOrDefault(striking) >= 8 && striking >= 68)
        {
            return new CampRecommendation(
                "Striking",
                $"Staff read: your cleanest edge on {scheduledOpponentName} is on the feet. A striking camp should sharpen timing and let you press that advantage.");
        }

        if (((wrestling + grappling) - (opponentWrestling.GetValueOrDefault(wrestling) + opponentGrappling.GetValueOrDefault(grappling))) >= 10)
        {
            return new CampRecommendation(
                "Wrestling",
                $"Staff read: your clearest edge on {scheduledOpponentName} is control and mat work. Wrestling focus should make that path easier to force.");
        }

        if (baseStyle.Contains("Wrestler", StringComparison.OrdinalIgnoreCase) || tacticalStyle.Contains("Pressure", StringComparison.OrdinalIgnoreCase))
        {
            return new CampRecommendation(
                "Wrestling",
                $"Staff read: this matchup should still reward your natural control game. Wrestling focus keeps the camp tied to what you already do best.");
        }

        if (baseStyle.Contains("Striker", StringComparison.OrdinalIgnoreCase)
            || baseStyle.Contains("Boxer", StringComparison.OrdinalIgnoreCase)
            || baseStyle.Contains("Kickboxer", StringComparison.OrdinalIgnoreCase)
            || tacticalStyle.Contains("Counter", StringComparison.OrdinalIgnoreCase))
        {
            return new CampRecommendation(
                "Striking",
                $"Staff read: no glaring danger jumps off the matchup, so the best plan is sharpening your natural striking identity before {scheduledOpponentName}.");
        }

        return new CampRecommendation(
            cardio >= 64 ? "Cardio" : "Recovery",
            cardio >= 64
                ? $"Staff read: the matchup against {scheduledOpponentName} looks balanced enough that pace may decide it. Cardio is the safest broad upgrade."
                : $"Staff read: there is no single tactical emergency here, so recovery is the steadier camp to arrive fresh and consistent.");
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
    COALESCE(fh.IsTitleEliminator, 0) AS IsTitleEliminator,
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
                IsTitleEliminator: Convert.ToInt32(r["IsTitleEliminator"]) == 1,
                Promotion: r["PromotionName"]?.ToString() ?? "",
                EventName: r["EventName"]?.ToString()
            ));
        }

        return list;
    }

    private static async Task<IReadOnlyList<FighterStorylineItem>> LoadStorylinesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        int take)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT StoryType, Headline, Body, Intensity
FROM Storylines
WHERE EntityType = 'Fighter'
  AND EntityId = $fighterId
  AND COALESCE(Status, 'Active') = 'Active'
ORDER BY Intensity DESC, Id DESC
LIMIT $take;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        cmd.Parameters.AddWithValue("$take", take);

        var items = new List<FighterStorylineItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new FighterStorylineItem(
                reader["StoryType"]?.ToString() ?? "",
                reader["Headline"]?.ToString() ?? "",
                reader["Body"]?.ToString() ?? "",
                Convert.ToInt32(reader["Intensity"])));
        }

        return items;
    }

    private static async Task<IReadOnlyList<FighterLegacyTagItem>> LoadLegacyTagsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        int take)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT TagCode, Summary, Intensity
FROM LegacyTags
WHERE FighterId = $fighterId
ORDER BY Intensity DESC, TagCode
LIMIT $take;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        cmd.Parameters.AddWithValue("$take", take);

        var items = new List<FighterLegacyTagItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new FighterLegacyTagItem(
                reader["TagCode"]?.ToString() ?? "",
                reader["Summary"]?.ToString() ?? "",
                Convert.ToInt32(reader["Intensity"])));
        }

        return items;
    }

    private sealed record CampRecommendation(string Focus, string Reason);
}
