using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class DailyWorldEventServiceSqlite : IDailyWorldEventService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IContractLifecycleService _contractLifecycleService;

    public DailyWorldEventServiceSqlite(
        SqliteConnectionFactory factory,
        IContractLifecycleService contractLifecycleService)
    {
        _factory = factory;
        _contractLifecycleService = contractLifecycleService;
    }

    public async Task<DailyWorldEventSummary> ProcessCurrentDayAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);
        var agentId = await LoadAgentIdAsync(conn, tx, cancellationToken);
        var investmentLevels = agentId.HasValue
            ? await LoadAgentInvestmentLevelsAsync(conn, tx, agentId.Value, cancellationToken)
            : (CampInvestmentLevel: 1, MedicalInvestmentLevel: 1);

        await CleanupOrphanPreparationsAsync(conn, tx, cancellationToken);
        await EnsurePreparationRowsAsync(conn, tx, currentDate, cancellationToken);

        var campUpdates = await ProcessCampStartsAsync(conn, tx, currentDate, agentId, investmentLevels.CampInvestmentLevel, investmentLevels.MedicalInvestmentLevel, cancellationToken);
        var fightWeekUpdates = await ProcessFightWeekAsync(conn, tx, currentDate, agentId, cancellationToken);
        var weighInUpdates = await ProcessWeighInsAsync(conn, tx, currentDate, agentId, investmentLevels.CampInvestmentLevel, investmentLevels.MedicalInvestmentLevel, cancellationToken);
        var aftermathUpdates = await ProcessAftermathAsync(conn, tx, currentDate, agentId, cancellationToken);
        if (agentId.HasValue)
        {
            await ProcessScoutAssignmentsAsync(conn, tx, currentDate, agentId.Value, cancellationToken);
            await ProcessCommercialOpportunitiesAsync(conn, tx, currentDate, agentId.Value, cancellationToken);
        }

        tx.Commit();

        var contractUpdates = await _contractLifecycleService.ProcessDailyAsync(cancellationToken);

        return new DailyWorldEventSummary(
            campUpdates,
            fightWeekUpdates,
            weighInUpdates,
            aftermathUpdates,
            campUpdates + fightWeekUpdates + weighInUpdates + aftermathUpdates + contractUpdates);
    }

    private static async Task CleanupOrphanPreparationsAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
DELETE FROM FightPreparations
WHERE FightId NOT IN (SELECT Id FROM Fights)
   OR FighterId NOT IN (SELECT Id FROM Fighters);", cancellationToken);

        await ExecAsync(conn, tx, @"
DELETE FROM FightPreparations
WHERE NOT EXISTS
(
    SELECT 1
    FROM ManagedFighters mf
    WHERE mf.FighterId = FightPreparations.FighterId
      AND COALESCE(mf.IsActive, 1) = 1
);", cancellationToken);
    }

    private static async Task EnsurePreparationRowsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO FightPreparations
(
    FightId,
    FighterId,
    CampWeeksPlanned,
    CampStartProcessed,
    FightWeekProcessed,
    WeighInProcessed,
    AftermathProcessed,
    LastUpdatedDate
)
SELECT
    f.Id,
    mf.FighterId,
    CASE
        WHEN COALESCE(f.IsTitleFight, 0) = 1 THEN COALESCE(p.TitleCampWeeks, 8)
        WHEN COALESCE(e.EventTier, 'Standard') = 'Major' THEN COALESCE(p.MajorCampWeeks, 6)
        ELSE COALESCE(p.StandardCampWeeks, 4)
    END,
    0,
    0,
    0,
    0,
    $currentDate
FROM Fights f
JOIN ManagedFighters mf
    ON (mf.FighterId = f.FighterAId OR mf.FighterId = f.FighterBId)
   AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters me ON me.Id = mf.FighterId
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE f.Method = 'Scheduled'
  AND COALESCE(f.EventDate, '') <> ''
  AND f.EventDate >= $currentDate
  AND NOT EXISTS
  (
      SELECT 1
      FROM FightPreparations fp
      WHERE fp.FightId = f.Id
        AND fp.FighterId = mf.FighterId
  );", cancellationToken, ("$currentDate", currentDate));
    }

    private static async Task<int> ProcessCampStartsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int? agentId,
        int campInvestmentLevel,
        int medicalInvestmentLevel,
        CancellationToken cancellationToken)
    {
        var updates = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    fp.FightId,
    fp.FighterId,
    fp.CampWeeksPlanned,
    opp.Id AS OpponentFighterId,
    me.Cardio,
    me.FightIQ,
    me.Potential,
    me.Popularity,
    COALESCE(f.IsTitleFight, 0) AS IsTitleFight,
    COALESCE(e.EventTier, 'Standard') AS EventTier,
    COALESCE(e.Name, 'Upcoming Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(f.EventDate, '') AS EventDate,
    (me.FirstName || ' ' || me.LastName) AS FighterName,
    (opp.FirstName || ' ' || opp.LastName) AS OpponentName
FROM FightPreparations fp
JOIN Fights f ON f.Id = fp.FightId
JOIN Fighters me ON me.Id = fp.FighterId
JOIN Fighters opp ON opp.Id = CASE WHEN f.FighterAId = fp.FighterId THEN f.FighterBId ELSE f.FighterAId END
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE f.Method = 'Scheduled'
  AND fp.CampStartProcessed = 0
  AND COALESCE(f.EventDate, '') <> ''
  AND date($currentDate) >= date(f.EventDate, '-' || (fp.CampWeeksPlanned * 7) || ' day')
ORDER BY f.EventDate, fp.FightId;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        var cancelledFightIds = new HashSet<int>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fightId = Convert.ToInt32(reader["FightId"]);
            if (cancelledFightIds.Contains(fightId))
                continue;

            var fighterId = Convert.ToInt32(reader["FighterId"]);
            var opponentFighterId = Convert.ToInt32(reader["OpponentFighterId"]);
            var campWeeks = Convert.ToInt32(reader["CampWeeksPlanned"]);
            var outcome = DetermineCampOutcome(
                fightId,
                fighterId,
                Convert.ToInt32(reader["Cardio"]),
                Convert.ToInt32(reader["FightIQ"]),
                Convert.ToInt32(reader["Potential"]),
                campWeeks,
                Convert.ToInt32(reader["IsTitleFight"]) == 1,
                string.Equals(reader["EventTier"]?.ToString(), "Major", StringComparison.OrdinalIgnoreCase),
                campInvestmentLevel,
                medicalInvestmentLevel);

            var notes = outcome switch
            {
                "Excellent" => "Camp is clicking. The preparation looks sharp and focused.",
                "Disrupted" => "Camp hit a few bumps and the preparation feels uneven.",
                "MinorInjury" => "A minor issue popped up in camp, but the fight is still expected to happen.",
                "CampInjury" => "A serious camp injury forced the booking off before fight week.",
                _ => "Camp opened smoothly and the team is on schedule."
            };

            var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
            var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
            var eventName = reader["EventName"]?.ToString() ?? "Upcoming Event";
            var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";

            if (string.Equals(outcome, "CampInjury", StringComparison.OrdinalIgnoreCase))
            {
                await CancelFightForCampInjuryAsync(
                    conn,
                    tx,
                    fightId,
                    fighterId,
                    opponentFighterId,
                    currentDate,
                    fighterName,
                    opponentName,
                    eventName,
                    promotionName,
                    agentId,
                    cancellationToken);

                cancelledFightIds.Add(fightId);
                updates++;
                continue;
            }

            await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET CampStartProcessed = 1,
    CampOutcome = $campOutcome,
    CampNotes = $campNotes,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId
  AND FighterId = $fighterId;", cancellationToken,
                ("$campOutcome", outcome),
                ("$campNotes", notes),
                ("$currentDate", currentDate),
                ("$fightId", fightId),
                ("$fighterId", fighterId));

            if (agentId.HasValue)
            {
                var subject = outcome switch
                {
                    "Excellent" => $"Camp flying for {fighterName}",
                    "Disrupted" => $"Camp turbulence for {fighterName}",
                    "MinorInjury" => $"Camp scare for {fighterName}",
                    _ => $"Camp underway for {fighterName}"
                };
                var body = $"{fighterName} has opened camp for {eventName} vs {opponentName} at {promotionName}. {notes}";

                await InsertInboxMessageAsync(
                    conn,
                    tx,
                    agentId.Value,
                    "CampUpdate",
                    subject,
                    body,
                    currentDate,
                    cancellationToken);
            }

            updates++;
        }

        return updates;
    }

    private static async Task<int> ProcessFightWeekAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int? agentId,
        CancellationToken cancellationToken)
    {
        var updates = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    fp.FightId,
    fp.FighterId,
    opp.Id AS OpponentFighterId,
    COALESCE(f.EventId, 0) AS EventId,
    COALESCE(CASE WHEN e.PromotionId IS NOT NULL THEN e.PromotionId ELSE me.PromotionId END, 0) AS PromotionId,
    COALESCE(f.WeightClass, me.WeightClass, '') AS WeightClass,
    COALESCE(f.IsTitleFight, 0) AS IsTitleFight,
    COALESCE(f.IsTitleEliminator, 0) AS IsTitleEliminator,
    COALESCE(f.Purse, 0) AS Purse,
    COALESCE(f.WinBonus, 0) AS WinBonus,
    COALESCE(fp.CampOutcome, '') AS CampOutcome,
    me.FightIQ,
    me.Cardio,
    me.Popularity,
    COALESCE(me.MediaHeat, 20) AS MediaHeat,
    me.Skill,
    COALESCE(e.Name, 'Upcoming Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(f.EventDate, '') AS EventDate,
    (me.FirstName || ' ' || me.LastName) AS FighterName,
    (opp.FirstName || ' ' || opp.LastName) AS OpponentName
FROM FightPreparations fp
JOIN Fights f ON f.Id = fp.FightId
JOIN Fighters me ON me.Id = fp.FighterId
JOIN Fighters opp ON opp.Id = CASE WHEN f.FighterAId = fp.FighterId THEN f.FighterBId ELSE f.FighterAId END
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE f.Method = 'Scheduled'
  AND fp.FightWeekProcessed = 0
  AND COALESCE(f.EventDate, '') <> ''
  AND date($currentDate) >= date(f.EventDate, '-7 day')
ORDER BY f.EventDate, fp.FightId;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fightId = Convert.ToInt32(reader["FightId"]);
            var fighterId = Convert.ToInt32(reader["FighterId"]);
            var opponentFighterId = Convert.ToInt32(reader["OpponentFighterId"]);
            var campOutcome = reader["CampOutcome"]?.ToString() ?? "";
            var outcome = DetermineFightWeekOutcome(
                fightId,
                fighterId,
                Convert.ToInt32(reader["FightIQ"]),
                Convert.ToInt32(reader["Cardio"]),
                Convert.ToInt32(reader["Popularity"]),
                Convert.ToInt32(reader["MediaHeat"]),
                campOutcome);
            var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
            var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
            var eventName = reader["EventName"]?.ToString() ?? "Upcoming Event";
            var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";

            if (string.Equals(outcome, "OpponentOut", StringComparison.OrdinalIgnoreCase))
            {
                var replacementCreated = await HandleOpponentWithdrawalAsync(
                    conn,
                    tx,
                    fightId,
                    fighterId,
                    opponentFighterId,
                    Convert.ToInt32(reader["EventId"]),
                    Convert.ToInt32(reader["PromotionId"]),
                    reader["WeightClass"]?.ToString() ?? "",
                    Convert.ToInt32(reader["Skill"]),
                    Convert.ToInt32(reader["Popularity"]),
                    Convert.ToInt32(reader["Purse"]),
                    Convert.ToInt32(reader["WinBonus"]),
                    Convert.ToInt32(reader["IsTitleFight"]) == 1,
                    Convert.ToInt32(reader["IsTitleEliminator"]) == 1,
                    currentDate,
                    fighterName,
                    opponentName,
                    eventName,
                    promotionName,
                    agentId,
                    cancellationToken);

                if (!replacementCreated && agentId.HasValue)
                {
                    await InsertInboxMessageAsync(
                        conn,
                        tx,
                        agentId.Value,
                        "FightWeekNotice",
                        $"Opponent out for {fighterName}",
                        $"{opponentName} withdrew from {eventName} at {promotionName}. No suitable replacement was found in time, so the booking has been cancelled.",
                        currentDate,
                        cancellationToken);
                }

                updates++;
                continue;
            }

            var notes = outcome switch
            {
                "LockedIn" => "The fighter looks locked in and the team loves the vibe in fight week.",
                "MediaSwirl" => "Fight week is noisy and distracting, with extra media pressure around the booking.",
                "Flat" => "The fighter made it to fight week, but the energy feels flatter than ideal.",
                _ => campOutcome switch
                {
                    "Excellent" => "The camp was strong and confidence is building into fight week.",
                    "Disrupted" => "The team is trying to steady the ship after a messy camp.",
                    "MinorInjury" => "The fighter is carrying a minor camp issue into fight week.",
                    _ => "Fight week has started and the focus shifts to game plan and weight."
                }
            };

            await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET FightWeekProcessed = 1,
    FightWeekOutcome = $outcome,
    FightWeekNotes = $notes,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId
  AND FighterId = $fighterId;", cancellationToken,
                ("$outcome", outcome),
                ("$notes", notes),
                ("$currentDate", currentDate),
                ("$fightId", fightId),
                ("$fighterId", fighterId));

            if (agentId.HasValue)
            {
                await InsertInboxMessageAsync(
                    conn,
                    tx,
                    agentId.Value,
                    "FightWeekNotice",
                    outcome switch
                    {
                        "LockedIn" => $"{fighterName} is locked in",
                        "MediaSwirl" => $"Fight week noise around {fighterName}",
                        "Flat" => $"{fighterName} feels flat in fight week",
                        _ => $"Fight week for {fighterName}"
                    },
                    $"{fighterName} enters fight week for {eventName} vs {opponentName} at {promotionName}. {notes}",
                    currentDate,
                    cancellationToken);

                if (string.Equals(outcome, "MediaSwirl", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(outcome, "Flat", StringComparison.OrdinalIgnoreCase))
                {
                    await TryInsertDecisionEventAsync(
                        conn,
                        tx,
                        agentId.Value,
                        fighterId,
                        fightId,
                        "FightWeekApproach",
                        $"Fight week choice for {fighterName}",
                        $"{fighterName} is heading into {eventName} with a tricky vibe. You can tighten the room and protect the prep, or lean into the spotlight for extra buzz.",
                        "QuietCamp",
                        "Quiet camp",
                        "Protect focus, smooth the week and trade a little hype for stability.",
                        "ChaseHeat",
                        "Chase heat",
                        "Lean into the coverage, gain attention and accept a little more volatility.",
                        currentDate,
                        cancellationToken);
                }
            }

            updates++;
        }

        return updates;
    }

    private static async Task<int> ProcessWeighInsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int? agentId,
        int campInvestmentLevel,
        int medicalInvestmentLevel,
        CancellationToken cancellationToken)
    {
        var updates = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    fp.FightId,
    fp.FighterId,
    me.Cardio,
    me.FightIQ,
    me.Popularity,
    COALESCE(fp.CampOutcome, '') AS CampOutcome,
    COALESCE(e.Name, 'Upcoming Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(f.EventDate, '') AS EventDate,
    (me.FirstName || ' ' || me.LastName) AS FighterName,
    (opp.FirstName || ' ' || opp.LastName) AS OpponentName
FROM FightPreparations fp
JOIN Fights f ON f.Id = fp.FightId
JOIN Fighters me ON me.Id = fp.FighterId
JOIN Fighters opp ON opp.Id = CASE WHEN f.FighterAId = fp.FighterId THEN f.FighterBId ELSE f.FighterAId END
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE f.Method = 'Scheduled'
  AND fp.WeighInProcessed = 0
  AND COALESCE(f.EventDate, '') <> ''
  AND date($currentDate) >= date(f.EventDate, '-2 day')
ORDER BY f.EventDate, fp.FightId;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fightId = Convert.ToInt32(reader["FightId"]);
            var fighterId = Convert.ToInt32(reader["FighterId"]);
            var outcome = DetermineWeighInOutcome(
                fightId,
                fighterId,
                Convert.ToInt32(reader["Cardio"]),
                Convert.ToInt32(reader["FightIQ"]),
                Convert.ToInt32(reader["Popularity"]),
                reader["CampOutcome"]?.ToString() ?? "",
                campInvestmentLevel,
                medicalInvestmentLevel);

            var notes = outcome switch
            {
                "MissedWeight" => "The cut went sideways and the fighter missed weight. Expect a hit to marketability and momentum.",
                "ToughCut" => "The fighter made the number, but the cut took a visible toll.",
                _ => "The fighter hit the target without drama."
            };

            await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET WeighInProcessed = 1,
    WeighInOutcome = $outcome,
    WeighInNotes = $notes,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId
  AND FighterId = $fighterId;", cancellationToken,
                ("$outcome", outcome),
                ("$notes", notes),
                ("$currentDate", currentDate),
                ("$fightId", fightId),
                ("$fighterId", fighterId));

            await ApplyWeighInPenaltyAsync(conn, tx, fighterId, outcome, cancellationToken);

            if (agentId.HasValue)
            {
                var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
                var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
                var eventName = reader["EventName"]?.ToString() ?? "Upcoming Event";
                var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";
                var subject = outcome switch
                {
                    "MissedWeight" => $"Weight miss for {fighterName}",
                    "ToughCut" => $"Hard cut for {fighterName}",
                    _ => $"Weigh-in cleared for {fighterName}"
                };

                await InsertInboxMessageAsync(
                    conn,
                    tx,
                    agentId.Value,
                    "WeighInAlert",
                    subject,
                    $"{fighterName} heads into {eventName} vs {opponentName} at {promotionName}. {notes}",
                    currentDate,
                    cancellationToken);

                if (string.Equals(outcome, "ToughCut", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(outcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
                {
                    await TryInsertDecisionEventAsync(
                        conn,
                        tx,
                        agentId.Value,
                        fighterId,
                        fightId,
                        "WeightCutCall",
                        $"Weight cut call for {fighterName}",
                        $"{fighterName} is going into {eventName} after a rough cut. You can protect the fighter and stabilize the night, or force the issue and chase the event upside.",
                        "ProtectFighter",
                        "Protect fighter",
                        "Lower the risk and try to keep the body together for fight night.",
                        "PushThrough",
                        "Push through",
                        "Maximize the event upside and accept a rougher, riskier night.",
                        currentDate,
                        cancellationToken);
                }
            }

            updates++;
        }

        return updates;
    }

    private static async Task<int> ProcessAftermathAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int? agentId,
        CancellationToken cancellationToken)
    {
        var updates = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    fp.FightId,
    fp.FighterId,
    f.WinnerId,
    COALESCE(f.Method, '') AS Method,
    COALESCE(f.Purse, 0) AS Purse,
    COALESCE(f.WinBonus, 0) AS WinBonus,
    COALESCE(f.IsShortNotice, 0) AS IsShortNotice,
    COALESCE(fp.CampOutcome, '') AS CampOutcome,
    COALESCE(fp.FightWeekOutcome, '') AS FightWeekOutcome,
    COALESCE(fp.WeighInOutcome, '') AS WeighInOutcome,
    COALESCE(fh.FightDate, f.EventDate, $currentDate) AS FightDate,
    COALESCE(e.Name, 'Completed Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(fh.IsTitle, COALESCE(f.IsTitleFight, 0)) AS IsTitleFight,
    COALESCE(fh.IsTitleEliminator, COALESCE(f.IsTitleEliminator, 0)) AS IsTitleEliminator,
    COALESCE(fh.IsMainEvent, COALESCE(f.IsMainEvent, 0)) AS IsMainEvent,
    COALESCE(fh.IsCoMainEvent, COALESCE(f.IsCoMainEvent, 0)) AS IsCoMainEvent,
    COALESCE(fh.EventTier, COALESCE(e.EventTier, 'Standard')) AS EventTier,
    (me.FirstName || ' ' || me.LastName) AS FighterName,
    (opp.FirstName || ' ' || opp.LastName) AS OpponentName
FROM FightPreparations fp
JOIN Fights f ON f.Id = fp.FightId
JOIN Fighters me ON me.Id = fp.FighterId
JOIN Fighters opp ON opp.Id = CASE WHEN f.FighterAId = fp.FighterId THEN f.FighterBId ELSE f.FighterAId END
LEFT JOIN FightHistory fh
    ON fh.EventId = f.EventId
   AND ((fh.FighterAId = f.FighterAId AND fh.FighterBId = f.FighterBId)
        OR (fh.FighterAId = f.FighterBId AND fh.FighterBId = f.FighterAId))
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE fp.AftermathProcessed = 0
  AND f.Method <> 'Scheduled';";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fightId = Convert.ToInt32(reader["FightId"]);
            var fighterId = Convert.ToInt32(reader["FighterId"]);
            var method = reader["Method"]?.ToString() ?? "";
            var winnerId = reader["WinnerId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["WinnerId"]);
            var won = winnerId == fighterId && winnerId > 0;
            var outcomeVerb = string.Equals(method, "Cancelled", StringComparison.OrdinalIgnoreCase)
                ? "was cancelled"
                : won ? "won" : "lost";
            var payoutNote = await ApplyAgentFightPayoutAsync(
                conn,
                tx,
                agentId,
                method,
                Convert.ToInt32(reader["Purse"]),
                Convert.ToInt32(reader["WinBonus"]),
                won,
                reader["WeighInOutcome"]?.ToString() ?? "",
                Convert.ToInt32(reader["IsShortNotice"]) == 1,
                cancellationToken);
            var aftermathNote = await ApplyAftermathImpactAsync(
                conn,
                tx,
                fighterId,
                method,
                won,
                Convert.ToInt32(reader["IsTitleFight"]) == 1,
                Convert.ToInt32(reader["IsTitleEliminator"]) == 1,
                Convert.ToInt32(reader["IsMainEvent"]) == 1,
                Convert.ToInt32(reader["IsCoMainEvent"]) == 1,
                reader["EventTier"]?.ToString() ?? "Standard",
                reader["FightWeekOutcome"]?.ToString() ?? "",
                reader["WeighInOutcome"]?.ToString() ?? "",
                cancellationToken);

            await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET AftermathProcessed = 1,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId
  AND FighterId = $fighterId;", cancellationToken,
                ("$currentDate", currentDate),
                ("$fightId", fightId),
                ("$fighterId", fighterId));

            if (agentId.HasValue)
            {
                var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
                var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
                var eventName = reader["EventName"]?.ToString() ?? "Completed Event";
                var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";
                var contextNote = BuildAftermathPrepNote(
                    reader["CampOutcome"]?.ToString() ?? "",
                    reader["FightWeekOutcome"]?.ToString() ?? "",
                    reader["WeighInOutcome"]?.ToString() ?? "",
                    won);
                var body = string.Equals(method, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? $"{fighterName}'s bout with {opponentName} at {eventName} ({promotionName}) was cancelled."
                    : $"{fighterName} {outcomeVerb} against {opponentName} at {eventName} ({promotionName}) via {method}.";
                if (!string.IsNullOrWhiteSpace(contextNote))
                    body = $"{body} {contextNote}";
                if (!string.IsNullOrWhiteSpace(payoutNote))
                    body = $"{body} {payoutNote}";
                if (!string.IsNullOrWhiteSpace(aftermathNote))
                    body = $"{body} {aftermathNote}";

                await InsertInboxMessageAsync(
                    conn,
                    tx,
                    agentId.Value,
                    "FightAftermath",
                    $"{fighterName} aftermath",
                    body,
                    currentDate,
                    cancellationToken);
            }

            updates++;
        }

        return updates;
    }

    private static async Task CancelFightForCampInjuryAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fightId,
        int fighterId,
        int opponentFighterId,
        string currentDate,
        string fighterName,
        string opponentName,
        string eventName,
        string promotionName,
        int? agentId,
        CancellationToken cancellationToken)
    {
        var injuryWeeks = DetermineCampInjuryWeeks(fightId, fighterId);
        var injuredNotes = $"A serious camp injury forced the fight off. Estimated recovery: {injuryWeeks} weeks.";
        var opponentNotes = $"The opponent withdrew during camp, so the fight has been called off.";

        await ExecAsync(conn, tx, @"
UPDATE Fights
SET WinnerId = NULL,
    Method = 'Cancelled',
    Round = NULL
WHERE Id = $fightId;", cancellationToken,
            ("$fightId", fightId));

        await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET CampStartProcessed = 1,
    CampOutcome = CASE
        WHEN FighterId = $injuredFighterId THEN 'CampInjury'
        ELSE 'OpponentWithdrawal'
    END,
    CampNotes = CASE
        WHEN FighterId = $injuredFighterId THEN $injuredNotes
        ELSE $opponentNotes
    END,
    FightWeekProcessed = 1,
    WeighInProcessed = 1,
    AftermathProcessed = 1,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId;", cancellationToken,
            ("$injuredFighterId", fighterId),
            ("$injuredNotes", injuredNotes),
            ("$opponentNotes", opponentNotes),
            ("$currentDate", currentDate),
            ("$fightId", fightId));

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET InjuryWeeksRemaining = MAX(COALESCE(InjuryWeeksRemaining, 0), $injuryWeeks),
    WeeksUntilAvailable = MAX(COALESCE(WeeksUntilAvailable, 0), $injuryWeeks),
    CampWithdrawalCount = COALESCE(CampWithdrawalCount, 0) + 1,
    Popularity = MAX(0, COALESCE(Popularity, 0) - 2)
WHERE Id = $fighterId;", cancellationToken,
            ("$injuryWeeks", injuryWeeks),
            ("$fighterId", fighterId));

        await RecalculateBookedFlagAsync(conn, tx, fighterId, currentDate, cancellationToken);
        await RecalculateBookedFlagAsync(conn, tx, opponentFighterId, currentDate, cancellationToken);

        if (!agentId.HasValue)
            return;

        await InsertInboxMessageAsync(
            conn,
            tx,
            agentId.Value,
            "CampUpdate",
            $"Fight off for {fighterName}",
            $"{fighterName} suffered a camp injury before {eventName} vs {opponentName} at {promotionName}. The bout has been cancelled. Estimated recovery: {injuryWeeks} weeks.",
            currentDate,
            cancellationToken);
    }

    private static async Task RecalculateBookedFlagAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET IsBooked = CASE
    WHEN EXISTS
    (
        SELECT 1
        FROM Fights sf
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = $fighterId OR sf.FighterBId = $fighterId)
          AND COALESCE(sf.EventDate, '9999-12-31') >= $currentDate
    )
    THEN 1 ELSE 0
END
WHERE Id = $fighterId;", cancellationToken,
            ("$fighterId", fighterId),
            ("$currentDate", currentDate));
    }

    private static async Task ApplyWeighInPenaltyAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        string outcome,
        CancellationToken cancellationToken)
    {
        var popularityPenalty = outcome switch
        {
            "MissedWeight" => 4,
            "ToughCut" => 1,
            _ => 0
        };

        var weightMissIncrement = string.Equals(outcome, "MissedWeight", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (popularityPenalty <= 0 && weightMissIncrement <= 0)
            return;

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET Popularity = MAX(0, COALESCE(Popularity, 0) - $popularityPenalty),
    WeightMissCount = COALESCE(WeightMissCount, 0) + $weightMissIncrement
WHERE Id = $fighterId;", cancellationToken,
            ("$popularityPenalty", popularityPenalty),
            ("$weightMissIncrement", weightMissIncrement),
            ("$fighterId", fighterId));
    }

    private static async Task<bool> HandleOpponentWithdrawalAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fightId,
        int fighterId,
        int opponentFighterId,
        int eventId,
        int promotionId,
        string weightClass,
        int fighterSkill,
        int fighterPopularity,
        int originalPurse,
        int originalWinBonus,
        bool isTitleFight,
        bool isTitleEliminator,
        string currentDate,
        string fighterName,
        string opponentName,
        string eventName,
        string promotionName,
        int? agentId,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
UPDATE Fights
SET WinnerId = NULL,
    Method = 'Cancelled',
    Round = NULL
WHERE Id = $fightId;", cancellationToken,
            ("$fightId", fightId));

        await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET FightWeekProcessed = 1,
    FightWeekOutcome = 'OpponentOut',
    FightWeekNotes = 'The original opponent withdrew during fight week.',
    WeighInProcessed = 1,
    AftermathProcessed = 1,
    LastUpdatedDate = $currentDate
WHERE FightId = $fightId;", cancellationToken,
            ("$currentDate", currentDate),
            ("$fightId", fightId));

        await RecalculateBookedFlagAsync(conn, tx, fighterId, currentDate, cancellationToken);
        await RecalculateBookedFlagAsync(conn, tx, opponentFighterId, currentDate, cancellationToken);

        if (eventId <= 0 || promotionId <= 0 || string.IsNullOrWhiteSpace(weightClass))
            return false;

        if (await HasPendingFightOfferAsync(conn, tx, fighterId, cancellationToken))
            return false;

        var replacement = await FindEmergencyReplacementAsync(
            conn,
            tx,
            fighterId,
            opponentFighterId,
            promotionId,
            weightClass,
            currentDate,
            cancellationToken);

        if (replacement is null)
            return false;

        var eventDate = await LoadEventDateAsync(conn, tx, eventId, cancellationToken) ?? currentDate;
        var daysUntilFight = Math.Max(1, DaysUntil(currentDate, eventDate));
        var weeksUntilFight = Math.Max(1, (int)Math.Ceiling(daysUntilFight / 7d));
        var purse = Math.Max(
            originalPurse > 0 ? (int)Math.Round(originalPurse * 1.35, MidpointRounding.AwayFromZero) : 0,
            ComputeEmergencyMoney(5000 + fighterSkill * 100));
        var winBonus = Math.Max(
            originalWinBonus > 0 ? (int)Math.Round(originalWinBonus * 1.20, MidpointRounding.AwayFromZero) : 0,
            ComputeEmergencyMoney(2000 + fighterPopularity * 50));

        await ExecAsync(conn, tx, @"
INSERT INTO FightOffers
(
    FighterId,
    OpponentFighterId,
    Purse,
    WinBonus,
    WeeksUntilFight,
    IsTitleFight,
    IsTitleEliminator,
    IsShortNotice,
    CampWeeksOffered,
    Status,
    EventId,
    PromotionId,
    WeightClass,
    Notes
)
VALUES
(
    $fighterId,
    $opponentFighterId,
    $purse,
    $winBonus,
    $weeksUntilFight,
    $isTitleFight,
    $isTitleEliminator,
    1,
    1,
    'Pending',
    $eventId,
    $promotionId,
    $weightClass,
    $notes
);", cancellationToken,
            ("$fighterId", fighterId),
            ("$opponentFighterId", replacement.FighterId),
            ("$purse", purse),
            ("$winBonus", winBonus),
            ("$weeksUntilFight", weeksUntilFight),
            ("$isTitleFight", isTitleFight ? 1 : 0),
            ("$isTitleEliminator", isTitleEliminator ? 1 : 0),
            ("$eventId", eventId),
            ("$promotionId", promotionId),
            ("$weightClass", weightClass),
            ("$notes", isTitleEliminator
                ? "Emergency replacement offer for a title eliminator slot."
                : "Emergency replacement offer to keep the fight on the card."));

        if (agentId.HasValue)
        {
            await InsertInboxMessageAsync(
                conn,
                tx,
                agentId.Value,
                "FightOfferShortNotice",
                $"Emergency replacement offer for {fighterName}",
                $"{opponentName} withdrew from {eventName} at {promotionName}. A short-notice replacement bout vs {replacement.Name} is now available for the same card, with improved purse terms{(isTitleEliminator ? " and next-contender stakes still on the line" : "")}.",
                currentDate,
                cancellationToken);
        }

        return true;
    }

    private static async Task ProcessScoutAssignmentsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int agentId,
        CancellationToken cancellationToken)
    {
        var completedAssignments = new List<(int FighterId, string Focus)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ScoutAssignments
SET ProgressDays = COALESCE(ProgressDays, 0) + 1
WHERE AgentId = $agentId
  AND Status = 'InProgress';";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT FighterId, Focus
FROM ScoutAssignments
WHERE AgentId = $agentId
  AND Status = 'InProgress'
  AND COALESCE(ProgressDays, 0) >= COALESCE(DaysRequired, 3);";
            cmd.Parameters.AddWithValue("$agentId", agentId);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                completedAssignments.Add((
                    Convert.ToInt32(reader["FighterId"]),
                    reader["Focus"]?.ToString() ?? "General"));
            }
        }

        foreach (var assignment in completedAssignments)
        {
            await TightenScoutKnowledgeAsync(conn, tx, agentId, assignment.FighterId, assignment.Focus, cancellationToken);

            await ExecAsync(conn, tx, @"
UPDATE ScoutAssignments
SET Status = 'Completed',
    CompletedDate = $currentDate
WHERE AgentId = $agentId
  AND FighterId = $fighterId
  AND Status = 'InProgress';", cancellationToken,
                ("$currentDate", currentDate),
                ("$agentId", agentId),
                ("$fighterId", assignment.FighterId));

            await InsertInboxMessageAsync(
                conn,
                tx,
                agentId,
                "ScoutingComplete",
                "Scouting report complete",
                $"Your scouting team wrapped a fresh {assignment.Focus.ToLowerInvariant()} read on fighter #{assignment.FighterId}. Confidence has improved and the estimate should be tighter now.",
                currentDate,
                cancellationToken);
        }
    }

    private static async Task ProcessCommercialOpportunitiesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        int agentId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT f.Id,
       (f.FirstName || ' ' || f.LastName) AS FighterName
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND COALESCE(f.Popularity, 0) >= 58
  AND NOT EXISTS
  (
      SELECT 1
      FROM DecisionEvents de
      WHERE de.AgentId = $agentId
        AND de.FighterId = f.Id
        AND de.DecisionType = 'SponsorSpotlight'
        AND de.Status = 'Pending'
  )
ORDER BY COALESCE(f.MediaHeat, 20) DESC, f.Id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return;

        var fighterId = Convert.ToInt32(reader["Id"]);
        var fighterName = reader["FighterName"]?.ToString() ?? "Your fighter";
        var triggerSeed = Math.Abs(HashCode.Combine(fighterId, currentDate, "SponsorSpotlight")) % 7;
        if (triggerSeed != 0)
            return;

        await TryInsertDecisionEventAsync(
            conn,
            tx,
            agentId,
            fighterId,
            null,
            "SponsorSpotlight",
            $"Commercial call for {fighterName}",
            $"{fighterName} has a quick sponsor activation on the table. You can cash it in for money and attention, or keep the week cleaner and more fighter-focused.",
            "AdCampaign",
            "Take the ad",
            "Immediate cash and extra buzz, but the week gets noisier.",
            "StayFocused",
            "Stay focused",
            "Skip the distraction and protect morale and sharpness.",
            currentDate,
            cancellationToken);
    }

    private static async Task<ReplacementCandidate?> FindEmergencyReplacementAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        int previousOpponentId,
        int promotionId,
        string weightClass,
        string currentDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill,
    f.Popularity
FROM Fighters f
WHERE f.Id NOT IN ($fighterId, $previousOpponentId)
  AND f.PromotionId = $promotionId
  AND f.WeightClass = $weightClass
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= 1
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= 0
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = f.Id
        AND COALESCE(mf.IsActive, 1) = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM Fights sf
      WHERE sf.Method = 'Scheduled'
        AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
        AND COALESCE(sf.EventDate, '9999-12-31') >= $currentDate
  )
ORDER BY COALESCE(f.ReliabilityScore, 60) DESC, f.Popularity DESC, f.Skill DESC, f.Id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        cmd.Parameters.AddWithValue("$previousOpponentId", previousOpponentId);
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$weightClass", weightClass);
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ReplacementCandidate(
            Convert.ToInt32(reader["FighterId"]),
            reader["FighterName"]?.ToString() ?? "Replacement",
            Convert.ToInt32(reader["Skill"]),
            Convert.ToInt32(reader["Popularity"]));
    }

    private static async Task<string?> LoadEventDateAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int eventId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EventDate FROM Events WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", eventId);
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<bool> HasPendingFightOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM FightOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static int ComputeEmergencyMoney(int baseAmount)
        => Math.Max(0, (int)Math.Round(baseAmount * 1.35, MidpointRounding.AwayFromZero));

    private static int DaysUntil(string fromDate, string toDate)
    {
        if (!DateTime.TryParse(fromDate, out var from))
            from = DateTime.UtcNow.Date;

        if (!DateTime.TryParse(toDate, out var to))
            to = from.AddDays(7);

        return Math.Max(1, (int)Math.Ceiling((to.Date - from.Date).TotalDays));
    }

    private static async Task<string?> ApplyAgentFightPayoutAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int? agentId,
        string method,
        int purse,
        int winBonus,
        bool won,
        string weighInOutcome,
        bool isShortNotice,
        CancellationToken cancellationToken)
    {
        if (!agentId.HasValue || purse <= 0 || string.Equals(method, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return null;

        var payout = purse + (won ? winBonus : 0);
        var fine = string.Equals(weighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(purse * 0.20, MidpointRounding.AwayFromZero)
            : 0;
        var net = Math.Max(0, payout - fine);
        if (net <= 0)
            return null;

        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Money = COALESCE(Money, 0) + $net
WHERE Id = $agentId;", cancellationToken,
            ("$net", net),
            ("$agentId", agentId.Value));

        var transactionNotes = won && winBonus > 0
            ? $"Fight purse {purse} plus win bonus {winBonus}."
            : $"Fight purse {purse}.";

        if (fine > 0)
            transactionNotes += $" Fine withheld {fine}.";

        if (isShortNotice)
            transactionNotes += " Short-notice booking.";

        await ExecAsync(conn, tx, @"
INSERT INTO AgentTransactions
(
    AgentId,
    TxDate,
    Amount,
    TxType,
    Notes
)
VALUES
(
    $agentId,
    COALESCE((SELECT CurrentDate FROM GameState LIMIT 1), date('now')),
    $net,
    'FightPayout',
    $notes
);", cancellationToken,
            ("$agentId", agentId.Value),
            ("$net", net),
            ("$notes", transactionNotes));

        var fragments = new List<string> { $"Agency earned {net}." };
        if (won && winBonus > 0)
            fragments.Add($"Win bonus paid: {winBonus}.");
        if (fine > 0)
            fragments.Add($"Weight fine withheld: {fine}.");
        if (isShortNotice && won)
            fragments.Add("Short-notice payday landed cleanly.");

        return string.Join(" ", fragments);
    }

    private static async Task<string?> ApplyAftermathImpactAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        string method,
        bool won,
        bool isTitleFight,
        bool isTitleEliminator,
        bool isMainEvent,
        bool isCoMainEvent,
        string eventTier,
        string fightWeekOutcome,
        string weighInOutcome,
        CancellationToken cancellationToken)
    {
        var popularityDelta = 0;
        var momentumDelta = 0;

        if (won)
        {
            popularityDelta += isTitleFight ? 4 : 1;
            popularityDelta += isTitleEliminator ? 2 : 0;
            popularityDelta += isMainEvent ? 2 : isCoMainEvent ? 1 : 0;
            popularityDelta += string.Equals(eventTier, "Major", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            popularityDelta += method is "KO/TKO" or "SUB" ? 2 : 0;
            popularityDelta += string.Equals(fightWeekOutcome, "LockedIn", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            momentumDelta += 4 + (method is "KO/TKO" or "SUB" ? 2 : 0) + (isTitleEliminator ? 2 : 0);
        }
        else
        {
            popularityDelta -= isTitleFight ? 2 : 1;
            popularityDelta -= method is "KO/TKO" or "SUB" ? 1 : 0;
            momentumDelta -= (method is "KO/TKO" or "SUB" ? 5 : 3) + (isTitleEliminator ? 1 : 0);
        }

        if (string.Equals(weighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
        {
            popularityDelta -= 2;
            momentumDelta -= 3;
        }
        else if (string.Equals(weighInOutcome, "ToughCut", StringComparison.OrdinalIgnoreCase))
        {
            momentumDelta -= 1;
        }

        if (popularityDelta == 0 && momentumDelta == 0)
            return null;

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET Popularity = MIN(100, MAX(0, COALESCE(Popularity, 0) + $popularityDelta)),
    Momentum = MIN(100, MAX(0, COALESCE(Momentum, 50) + $momentumDelta)),
    DamageMiles = MAX(0, COALESCE(DamageMiles, 0) + $damageMilesDelta)
WHERE Id = $fighterId;", cancellationToken,
            ("$popularityDelta", popularityDelta),
            ("$momentumDelta", momentumDelta),
            ("$damageMilesDelta", ComputeDamageMilesDelta(method, won, isTitleFight, isMainEvent, isCoMainEvent)),
            ("$fighterId", fighterId));

        var fragments = new List<string>();
        if (popularityDelta != 0)
            fragments.Add($"Popularity {(popularityDelta > 0 ? "up" : "down")} {Math.Abs(popularityDelta)}.");
        if (momentumDelta != 0)
            fragments.Add($"Momentum {(momentumDelta > 0 ? "up" : "down")} {Math.Abs(momentumDelta)}.");
        var damageMilesDelta = ComputeDamageMilesDelta(method, won, isTitleFight, isMainEvent, isCoMainEvent);
        if (damageMilesDelta > 0)
            fragments.Add($"Wear +{damageMilesDelta}.");

        return string.Join(" ", fragments);
    }

    private static int ComputeDamageMilesDelta(
        string method,
        bool won,
        bool isTitleFight,
        bool isMainEvent,
        bool isCoMainEvent)
    {
        var delta = method switch
        {
            "KO/TKO" => won ? 4 : 12,
            "SUB" => won ? 3 : 8,
            _ => won ? 2 : 6
        };

        if (isTitleFight)
            delta += 2;
        else if (isMainEvent)
            delta += 1;
        else if (isCoMainEvent)
            delta += 1;

        return delta;
    }

    private static string DetermineCampOutcome(
        int fightId,
        int fighterId,
        int cardio,
        int fightIq,
        int potential,
        int campWeeks,
        bool isTitleFight,
        bool isMajorEvent,
        int campInvestmentLevel,
        int medicalInvestmentLevel)
    {
        var prepScore = ((cardio + fightIq + potential) / 3)
            + (campInvestmentLevel * 4)
            + (medicalInvestmentLevel * 2);
        var premiumCamp = isTitleFight || isMajorEvent;
        var shortCamp = campWeeks <= 2;
        var roll = CreateDeterministicRandom(fightId, fighterId, "Camp").Next(100);

        var excellentThreshold = (prepScore >= 74 ? 28 : prepScore >= 62 ? 18 : 10) + (campInvestmentLevel * 5);
        var disruptedThreshold = (shortCamp ? 30 : premiumCamp ? 20 : 24) - (campInvestmentLevel * 4);
        var campInjuryThreshold = (prepScore >= 72 ? 2 : prepScore >= 60 ? 4 : 6) - medicalInvestmentLevel;
        if (shortCamp)
            campInjuryThreshold += 1;

        var injuryThreshold = (shortCamp ? 10 : 6) - medicalInvestmentLevel;

        excellentThreshold = Math.Clamp(excellentThreshold, 8, 42);
        disruptedThreshold = Math.Clamp(disruptedThreshold, 8, 34);
        campInjuryThreshold = Math.Clamp(campInjuryThreshold, 1, 8);
        injuryThreshold = Math.Clamp(injuryThreshold, 2, 12);

        if (roll < excellentThreshold)
            return "Excellent";

        if (roll >= 100 - campInjuryThreshold)
            return "CampInjury";

        if (roll >= 100 - campInjuryThreshold - injuryThreshold)
            return "MinorInjury";

        if (roll >= 100 - campInjuryThreshold - injuryThreshold - disruptedThreshold)
            return "Disrupted";

        return "Stable";
    }

    private static int DetermineCampInjuryWeeks(int fightId, int fighterId)
        => 4 + CreateDeterministicRandom(fightId, fighterId, "CampInjuryWeeks").Next(0, 7);

    private static string DetermineFightWeekOutcome(
        int fightId,
        int fighterId,
        int fightIq,
        int cardio,
        int popularity,
        int mediaHeat,
        string campOutcome)
    {
        var score = (fightIq + cardio + popularity + mediaHeat) / 4;
        var roll = CreateDeterministicRandom(fightId, fighterId, "FightWeek").Next(100);

        if (!string.Equals(campOutcome, "CampInjury", StringComparison.OrdinalIgnoreCase)
            && roll >= 92
            && !string.Equals(campOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
        {
            return "OpponentOut";
        }

        if (string.Equals(campOutcome, "Excellent", StringComparison.OrdinalIgnoreCase) && roll < Math.Max(18, score / 4))
            return "LockedIn";

        if ((string.Equals(campOutcome, "Disrupted", StringComparison.OrdinalIgnoreCase)
             || string.Equals(campOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
            && roll >= 55)
        {
            return "Flat";
        }

        if (mediaHeat >= 75 && fightIq < 75 && roll >= 62)
            return "MediaSwirl";

        if (popularity >= 70 && fightIq < 72 && roll >= 74)
            return "MediaSwirl";

        return "Steady";
    }

    private static string DetermineWeighInOutcome(
        int fightId,
        int fighterId,
        int cardio,
        int fightIq,
        int popularity,
        string campOutcome,
        int campInvestmentLevel,
        int medicalInvestmentLevel)
    {
        var cutScore = ((cardio * 2 + fightIq + popularity) / 4)
            + (campInvestmentLevel * 3)
            + (medicalInvestmentLevel * 4);
        var roll = CreateDeterministicRandom(fightId, fighterId, "WeighIn").Next(100);
        var penalty = campOutcome switch
        {
            "MinorInjury" => 10,
            "Disrupted" => 8,
            _ => 0
        };

        var missWeightThreshold = (cutScore >= 72 ? 4 : cutScore >= 60 ? 8 : 14) - medicalInvestmentLevel - (campInvestmentLevel > 1 ? 1 : 0);
        var toughCutThreshold = (cutScore >= 72 ? 16 : cutScore >= 60 ? 24 : 32) - (campInvestmentLevel * 3);
        var adjustedRoll = roll + penalty;

        missWeightThreshold = Math.Clamp(missWeightThreshold, 2, 16);
        toughCutThreshold = Math.Clamp(toughCutThreshold, 8, 34);

        if (adjustedRoll >= 100 - missWeightThreshold)
            return "MissedWeight";

        if (adjustedRoll >= 100 - missWeightThreshold - toughCutThreshold)
            return "ToughCut";

        return "OnWeight";
    }

    private static Random CreateDeterministicRandom(int fightId, int fighterId, string scope)
    {
        var seed = HashCode.Combine(fightId, fighterId, scope);
        if (seed == int.MinValue)
            seed = int.MaxValue;

        return new Random(Math.Abs(seed));
    }

    private static string BuildAftermathPrepNote(string campOutcome, string fightWeekOutcome, string weighInOutcome, bool won)
    {
        var fragments = new List<string>();

        if (string.Equals(campOutcome, "Excellent", StringComparison.OrdinalIgnoreCase) && won)
            fragments.Add("The strong camp carried into the result.");
        else if (string.Equals(campOutcome, "Disrupted", StringComparison.OrdinalIgnoreCase))
            fragments.Add("The fight came after a disrupted camp.");
        else if (string.Equals(campOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
            fragments.Add("The fighter carried a minor camp issue into the bout.");

        if (string.Equals(fightWeekOutcome, "LockedIn", StringComparison.OrdinalIgnoreCase) && won)
            fragments.Add("Fight week momentum was clearly on their side.");
        else if (string.Equals(fightWeekOutcome, "MediaSwirl", StringComparison.OrdinalIgnoreCase))
            fragments.Add("The week also came with extra media noise.");
        else if (string.Equals(fightWeekOutcome, "Flat", StringComparison.OrdinalIgnoreCase))
            fragments.Add("They never looked fully fresh in fight week.");

        if (string.Equals(weighInOutcome, "ToughCut", StringComparison.OrdinalIgnoreCase))
            fragments.Add("It followed a draining weight cut.");
        else if (string.Equals(weighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
            fragments.Add("The bout went ahead after a weight miss.");

        return string.Join(" ", fragments);
    }

    private static async Task<int?> LoadAgentIdAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1;";
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static async Task TightenScoutKnowledgeAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        int fighterId,
        string focus,
        CancellationToken cancellationToken)
    {
        var confidenceGain = string.Equals(focus, "Traits", StringComparison.OrdinalIgnoreCase) ? 14 : 18;
        var tightenAmount = string.Equals(focus, "Traits", StringComparison.OrdinalIgnoreCase) ? 3 : 5;

        await ExecAsync(conn, tx, @"
UPDATE ScoutKnowledge
SET Confidence = MIN(96, COALESCE(Confidence, 40) + $confidenceGain),
    EstimatedSkillMin = MIN(EstimatedSkillMax, EstimatedSkillMin + $tightenAmount),
    EstimatedSkillMax = MAX(EstimatedSkillMin, EstimatedSkillMax - $tightenAmount),
    EstimatedPotentialMin = MIN(EstimatedPotentialMax, EstimatedPotentialMin + $tightenAmount),
    EstimatedPotentialMax = MAX(EstimatedPotentialMin, EstimatedPotentialMax - $tightenAmount),
    EstimatedStrikingMin = MIN(EstimatedStrikingMax, EstimatedStrikingMin + $tightenAmount),
    EstimatedStrikingMax = MAX(EstimatedStrikingMin, EstimatedStrikingMax - $tightenAmount),
    EstimatedGrapplingMin = MIN(EstimatedGrapplingMax, EstimatedGrapplingMin + $tightenAmount),
    EstimatedGrapplingMax = MAX(EstimatedGrapplingMin, EstimatedGrapplingMax - $tightenAmount),
    EstimatedWrestlingMin = MIN(EstimatedWrestlingMax, EstimatedWrestlingMin + $tightenAmount),
    EstimatedWrestlingMax = MAX(EstimatedWrestlingMin, EstimatedWrestlingMax - $tightenAmount),
    EstimatedCardioMin = MIN(EstimatedCardioMax, EstimatedCardioMin + $tightenAmount),
    EstimatedCardioMax = MAX(EstimatedCardioMin, EstimatedCardioMax - $tightenAmount),
    EstimatedChinMin = MIN(EstimatedChinMax, EstimatedChinMin + $tightenAmount),
    EstimatedChinMax = MAX(EstimatedChinMin, EstimatedChinMax - $tightenAmount),
    EstimatedFightIQMin = MIN(EstimatedFightIQMax, EstimatedFightIQMin + $tightenAmount),
    EstimatedFightIQMax = MAX(EstimatedFightIQMin, EstimatedFightIQMax - $tightenAmount),
    LastUpdatedDate = $currentDate
WHERE AgentId = $agentId
  AND FighterId = $fighterId;", cancellationToken,
            ("$confidenceGain", confidenceGain),
            ("$tightenAmount", tightenAmount),
            ("$currentDate", DateTime.UtcNow.ToString("yyyy-MM-dd")),
            ("$agentId", agentId),
            ("$fighterId", fighterId));
    }

    private static async Task TryInsertDecisionEventAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        int? fighterId,
        int? fightId,
        string decisionType,
        string headline,
        string body,
        string optionAKey,
        string optionALabel,
        string? optionADescription,
        string optionBKey,
        string optionBLabel,
        string? optionBDescription,
        string currentDate,
        CancellationToken cancellationToken)
    {
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.Transaction = tx;
            checkCmd.CommandText = @"
SELECT COUNT(*)
FROM DecisionEvents
WHERE AgentId = $agentId
  AND COALESCE(FighterId, 0) = COALESCE($fighterId, 0)
  AND COALESCE(FightId, 0) = COALESCE($fightId, 0)
  AND DecisionType = $decisionType
  AND Status = 'Pending';";
            checkCmd.Parameters.AddWithValue("$agentId", agentId);
            checkCmd.Parameters.AddWithValue("$fighterId", (object?)fighterId ?? DBNull.Value);
            checkCmd.Parameters.AddWithValue("$fightId", (object?)fightId ?? DBNull.Value);
            checkCmd.Parameters.AddWithValue("$decisionType", decisionType);

            if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0)
                return;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO DecisionEvents
(AgentId, FighterId, FightId, DecisionType, Headline, Body, OptionAKey, OptionALabel, OptionADescription, OptionBKey, OptionBLabel, OptionBDescription, Status, CreatedDate)
VALUES
($agentId, $fighterId, $fightId, $decisionType, $headline, $body, $optionAKey, $optionALabel, $optionADescription, $optionBKey, $optionBLabel, $optionBDescription, 'Pending', $createdDate);";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$fighterId", (object?)fighterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fightId", (object?)fightId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$decisionType", decisionType);
        cmd.Parameters.AddWithValue("$headline", headline);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$optionAKey", optionAKey);
        cmd.Parameters.AddWithValue("$optionALabel", optionALabel);
        cmd.Parameters.AddWithValue("$optionADescription", (object?)optionADescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$optionBKey", optionBKey);
        cmd.Parameters.AddWithValue("$optionBLabel", optionBLabel);
        cmd.Parameters.AddWithValue("$optionBDescription", (object?)optionBDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdDate", currentDate);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int CampInvestmentLevel, int MedicalInvestmentLevel)> LoadAgentInvestmentLevelsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    COALESCE(CampInvestmentLevel, 1) AS CampInvestmentLevel,
    COALESCE(MedicalInvestmentLevel, 1) AS MedicalInvestmentLevel
FROM AgentProfile
WHERE Id = $agentId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (1, 1);

        return (
            Math.Clamp(Convert.ToInt32(reader["CampInvestmentLevel"]), 0, 2),
            Math.Clamp(Convert.ToInt32(reader["MedicalInvestmentLevel"]), 0, 2));
    }

    private static async Task<string> LoadCurrentDateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, '2026-01-01') FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "2026-01-01";
    }

    private static async Task InsertInboxMessageAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string messageType,
        string subject,
        string body,
        string createdDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO InboxMessages
(
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead,
    IsArchived,
    IsDeleted,
    DeletedAt
)
VALUES
(
    $agentId,
    $messageType,
    $subject,
    $body,
    $createdDate,
    0,
    0,
    0,
    NULL
);";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$messageType", messageType);
        cmd.Parameters.AddWithValue("$subject", subject);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$createdDate", createdDate);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record ReplacementCandidate(int FighterId, string Name, int Skill, int Popularity);
}
