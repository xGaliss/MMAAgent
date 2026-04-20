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

        await CleanupOrphanPreparationsAsync(conn, tx, cancellationToken);
        await EnsurePreparationRowsAsync(conn, tx, currentDate, cancellationToken);

        var campUpdates = await ProcessCampStartsAsync(conn, tx, currentDate, agentId, cancellationToken);
        var fightWeekUpdates = await ProcessFightWeekAsync(conn, tx, currentDate, agentId, cancellationToken);
        var weighInUpdates = await ProcessWeighInsAsync(conn, tx, currentDate, agentId, cancellationToken);
        var aftermathUpdates = await ProcessAftermathAsync(conn, tx, currentDate, agentId, cancellationToken);

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
                string.Equals(reader["EventTier"]?.ToString(), "Major", StringComparison.OrdinalIgnoreCase));

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
    COALESCE(fp.CampOutcome, '') AS CampOutcome,
    me.FightIQ,
    me.Cardio,
    me.Popularity,
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
            var campOutcome = reader["CampOutcome"]?.ToString() ?? "";
            var outcome = DetermineFightWeekOutcome(
                fightId,
                fighterId,
                Convert.ToInt32(reader["FightIQ"]),
                Convert.ToInt32(reader["Cardio"]),
                Convert.ToInt32(reader["Popularity"]),
                campOutcome);

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
                var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
                var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
                var eventName = reader["EventName"]?.ToString() ?? "Upcoming Event";
                var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";

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
                reader["CampOutcome"]?.ToString() ?? "");

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
    COALESCE(fp.CampOutcome, '') AS CampOutcome,
    COALESCE(fp.FightWeekOutcome, '') AS FightWeekOutcome,
    COALESCE(fp.WeighInOutcome, '') AS WeighInOutcome,
    COALESCE(fh.FightDate, f.EventDate, $currentDate) AS FightDate,
    COALESCE(e.Name, 'Completed Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(fh.IsTitle, COALESCE(f.IsTitleFight, 0)) AS IsTitleFight,
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
            var aftermathNote = await ApplyAftermathImpactAsync(
                conn,
                tx,
                fighterId,
                method,
                won,
                Convert.ToInt32(reader["IsTitleFight"]) == 1,
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

    private static async Task<string?> ApplyAftermathImpactAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        string method,
        bool won,
        bool isTitleFight,
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
            popularityDelta += isMainEvent ? 2 : isCoMainEvent ? 1 : 0;
            popularityDelta += string.Equals(eventTier, "Major", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            popularityDelta += method is "KO/TKO" or "SUB" ? 2 : 0;
            popularityDelta += string.Equals(fightWeekOutcome, "LockedIn", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            momentumDelta += 4 + (method is "KO/TKO" or "SUB" ? 2 : 0);
        }
        else
        {
            popularityDelta -= isTitleFight ? 2 : 1;
            popularityDelta -= method is "KO/TKO" or "SUB" ? 1 : 0;
            momentumDelta -= method is "KO/TKO" or "SUB" ? 5 : 3;
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
    Momentum = MIN(100, MAX(0, COALESCE(Momentum, 50) + $momentumDelta))
WHERE Id = $fighterId;", cancellationToken,
            ("$popularityDelta", popularityDelta),
            ("$momentumDelta", momentumDelta),
            ("$fighterId", fighterId));

        var fragments = new List<string>();
        if (popularityDelta != 0)
            fragments.Add($"Popularity {(popularityDelta > 0 ? "up" : "down")} {Math.Abs(popularityDelta)}.");
        if (momentumDelta != 0)
            fragments.Add($"Momentum {(momentumDelta > 0 ? "up" : "down")} {Math.Abs(momentumDelta)}.");

        return string.Join(" ", fragments);
    }

    private static string DetermineCampOutcome(
        int fightId,
        int fighterId,
        int cardio,
        int fightIq,
        int potential,
        int campWeeks,
        bool isTitleFight,
        bool isMajorEvent)
    {
        var prepScore = (cardio + fightIq + potential) / 3;
        var premiumCamp = isTitleFight || isMajorEvent;
        var shortCamp = campWeeks <= 2;
        var roll = CreateDeterministicRandom(fightId, fighterId, "Camp").Next(100);

        var excellentThreshold = prepScore >= 74 ? 28 : prepScore >= 62 ? 18 : 10;
        var disruptedThreshold = shortCamp ? 30 : premiumCamp ? 20 : 24;
        var campInjuryThreshold = prepScore >= 72 ? 2 : prepScore >= 60 ? 4 : 6;
        if (shortCamp)
            campInjuryThreshold += 1;

        var injuryThreshold = shortCamp ? 10 : 6;

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
        string campOutcome)
    {
        var score = (fightIq + cardio + popularity) / 3;
        var roll = CreateDeterministicRandom(fightId, fighterId, "FightWeek").Next(100);

        if (string.Equals(campOutcome, "Excellent", StringComparison.OrdinalIgnoreCase) && roll < Math.Max(18, score / 4))
            return "LockedIn";

        if ((string.Equals(campOutcome, "Disrupted", StringComparison.OrdinalIgnoreCase)
             || string.Equals(campOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
            && roll >= 55)
        {
            return "Flat";
        }

        if (popularity >= 70 && fightIq < 72 && roll >= 70)
            return "MediaSwirl";

        return "Steady";
    }

    private static string DetermineWeighInOutcome(
        int fightId,
        int fighterId,
        int cardio,
        int fightIq,
        int popularity,
        string campOutcome)
    {
        var cutScore = (cardio * 2 + fightIq + popularity) / 4;
        var roll = CreateDeterministicRandom(fightId, fighterId, "WeighIn").Next(100);
        var penalty = campOutcome switch
        {
            "MinorInjury" => 10,
            "Disrupted" => 8,
            _ => 0
        };

        var missWeightThreshold = cutScore >= 72 ? 4 : cutScore >= 60 ? 8 : 14;
        var toughCutThreshold = cutScore >= 72 ? 16 : cutScore >= 60 ? 24 : 32;
        var adjustedRoll = roll + penalty;

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
}
