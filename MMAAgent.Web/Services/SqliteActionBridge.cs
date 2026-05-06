using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Web.Services;

public sealed class SqliteActionBridge
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteActionBridge(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task ReleaseFighterAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ManagedFighters
SET IsActive = 0
WHERE FighterId = $fighterId
  AND AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND COALESCE(IsActive, 1) = 1;";
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE FightOffers
SET Status = 'Rejected'
WHERE FighterId = $fighterId
  AND Status = 'Pending';";
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ContractOffers
SET Status = 'Withdrawn',
    RespondedDate = $respondedDate
WHERE FighterId = $fighterId
  AND Status = 'Pending';";
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            cmd.Parameters.AddWithValue("$respondedDate", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
    }

    public async Task<bool> SetCampFocusAsync(int fighterId, string campFocus, CancellationToken cancellationToken = default)
    {
        if (!IsValidCampFocus(campFocus))
            throw new InvalidOperationException("Camp focus no valido.");

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);

        using (var ensureCmd = conn.CreateCommand())
        {
            ensureCmd.Transaction = tx;
            ensureCmd.CommandText = @"
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
    $fighterId,
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
JOIN Fighters me ON me.Id = $fighterId
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, me.PromotionId)
WHERE f.Method = 'Scheduled'
  AND (f.FighterAId = $fighterId OR f.FighterBId = $fighterId)
  AND COALESCE(f.EventDate, '') <> ''
  AND f.EventDate > $currentDate
  AND EXISTS
  (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = $fighterId
        AND mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
        AND COALESCE(mf.IsActive, 1) = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM FightPreparations fp
      WHERE fp.FightId = f.Id
        AND fp.FighterId = $fighterId
  );";
            ensureCmd.Parameters.AddWithValue("$fighterId", fighterId);
            ensureCmd.Parameters.AddWithValue("$currentDate", currentDate);
            await ensureCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE FightPreparations
SET CampFocus = $campFocus,
    LastUpdatedDate = $currentDate
WHERE FighterId = $fighterId
  AND EXISTS
  (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = $fighterId
        AND mf.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
        AND COALESCE(mf.IsActive, 1) = 1
  )
  AND FightId IN
  (
      SELECT sf.Id
      FROM Fights sf
      WHERE sf.Method = 'Scheduled'
        AND (sf.FighterAId = $fighterId OR sf.FighterBId = $fighterId)
        AND COALESCE(sf.EventDate, '9999-12-31') > $currentDate
  );";
        cmd.Parameters.AddWithValue("$campFocus", campFocus);
        cmd.Parameters.AddWithValue("$currentDate", currentDate);
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        var updated = await cmd.ExecuteNonQueryAsync(cancellationToken);
        tx.Commit();
        return updated > 0;
    }

    public async Task<bool> SetProspectWatchAsync(int fighterId, bool watch, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var agentId = await LoadPrimaryAgentIdAsync(conn, tx, cancellationToken);
        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);

        if (watch)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO AmateurProspectWatchlist
(
    AgentId,
    FighterId,
    AddedDate,
    LastAlertDate,
    LastAlertType,
    Notes
)
VALUES
(
    $agentId,
    $fighterId,
    $currentDate,
    NULL,
    NULL,
    'Prospect added to the agency watchlist.'
)
ON CONFLICT(AgentId, FighterId)
DO NOTHING;";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            cmd.Parameters.AddWithValue("$currentDate", currentDate);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
DELETE FROM AmateurProspectWatchlist
WHERE AgentId = $agentId
  AND FighterId = $fighterId;";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
        return watch;
    }

    public Task<ServiceResult> RequestTitleShotAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => ExecuteDivisionPushAsync(
            promotionId,
            weightClass,
            fighterId,
            "TitleShotRequest",
            minQueueRank: 2,
            queueBoost: 18,
            subjectFactory: fighterName => $"Title shot push filed for {fighterName}",
            bodyFactory: (fighterName, rankText) => $"You made a formal title shot push for {fighterName} in {weightClass}. Matchmaking has logged the pressure and the contender queue will react if the run is strong enough. Current spot: {rankText}.",
            cancellationToken);

    public Task<ServiceResult> RequestEliminatorAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => ExecuteDivisionPushAsync(
            promotionId,
            weightClass,
            fighterId,
            "EliminatorRequest",
            minQueueRank: 5,
            queueBoost: 12,
            subjectFactory: fighterName => $"Eliminator case made for {fighterName}",
            bodyFactory: (fighterName, rankText) => $"You pushed for an eliminator path for {fighterName} in {weightClass}. The promotion has acknowledged the case and the queue score gets a bump. Current spot: {rankText}.",
            cancellationToken);

    public Task<ServiceResult> PushManagedFighterAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => ExecuteDivisionPushAsync(
            promotionId,
            weightClass,
            fighterId,
            "MatchmakingPush",
            minQueueRank: 12,
            queueBoost: 8,
            subjectFactory: fighterName => $"Matchmaking pressure applied for {fighterName}",
            bodyFactory: (fighterName, rankText) => $"You leaned on matchmaking for {fighterName} in {weightClass}. It is not a promise, but the division now feels a little more pressure to move around your fighter. Current spot: {rankText}.",
            cancellationToken);

    private static bool IsValidCampFocus(string campFocus)
        => campFocus is "Cardio" or "Wrestling" or "Striking" or "Recovery" or "WeightManagement";

    private async Task<ServiceResult> ExecuteDivisionPushAsync(
        int promotionId,
        string weightClass,
        int fighterId,
        string messageType,
        int minQueueRank,
        int queueBoost,
        Func<string, string> subjectFactory,
        Func<string, string, string> bodyFactory,
        CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);
        var agentId = await LoadPrimaryAgentIdAsync(conn, tx, cancellationToken);
        if (agentId <= 0)
            return ServiceResult.Fail("No active agent profile was found.");

        var contender = await LoadManagedContenderAsync(conn, tx, promotionId, weightClass, fighterId, agentId, cancellationToken);
        if (contender is null)
            return ServiceResult.Fail("That fighter is not in your active contender picture for this division.");

        if (contender.Value.QueueRank > minQueueRank)
        {
            var earlyPushRelationshipDelta = messageType switch
            {
                "TitleShotRequest" => -2,
                "EliminatorRequest" => -2,
                _ => -1
            };

            await UpsertPromotionRelationAsync(
                conn,
                tx,
                agentId,
                promotionId,
                earlyPushRelationshipDelta,
                currentDate,
                messageType switch
                {
                    "TitleShotRequest" => "Agency pushed for a title shot too early and annoyed the promotion.",
                    "EliminatorRequest" => "Agency pushed for an eliminator too early and met resistance.",
                    _ => "Agency leaned on matchmaking before the division was ready."
                },
                cancellationToken);

            await InsertInboxMessageAsync(
                conn,
                tx,
                agentId,
                messageType,
                $"{contender.Value.FighterName} push cooled off",
                $"You pushed {contender.Value.FighterName} too early in {weightClass}. The promotion logged the request, but the fighter is only #{contender.Value.QueueRank} and the relationship took a small hit.",
                currentDate,
                cancellationToken);

            tx.Commit();
            return ServiceResult.Fail($"The fighter is still too far back in the queue. Current spot: #{contender.Value.QueueRank}. The promotion did not love the pressure.");
        }

        var subject = subjectFactory(contender.Value.FighterName);
        if (await HasRecentManagerMoveAsync(conn, tx, agentId, messageType, subject, currentDate, cancellationToken))
            return ServiceResult.Fail("You already made that push recently. Give the division some time to react.");

        using (var updateCmd = conn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = @"
UPDATE ContenderQueue
SET QueueScore = QueueScore + $queueBoost,
    Notes = TRIM(COALESCE(Notes, '') || CASE WHEN COALESCE(Notes, '') = '' THEN '' ELSE ' · ' END || $note)
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
  AND FighterId = $fighterId;";
            updateCmd.Parameters.AddWithValue("$queueBoost", queueBoost);
            updateCmd.Parameters.AddWithValue("$note", $"{messageType} filed {currentDate}");
            updateCmd.Parameters.AddWithValue("$promotionId", promotionId);
            updateCmd.Parameters.AddWithValue("$weightClass", weightClass);
            updateCmd.Parameters.AddWithValue("$fighterId", fighterId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var relationshipDelta = messageType switch
        {
            "TitleShotRequest" => contender.Value.QueueRank <= 1 ? 1 : 0,
            "EliminatorRequest" => contender.Value.QueueRank <= 3 ? 1 : 0,
            "MatchmakingPush" => contender.Value.QueueRank <= 6 ? 1 : -1,
            _ => 0
        };

        if (relationshipDelta != 0)
        {
            await UpsertPromotionRelationAsync(
                conn,
                tx,
                agentId,
                promotionId,
                relationshipDelta,
                currentDate,
                messageType switch
                {
                    "TitleShotRequest" => "Agency pushed for a title shot.",
                    "EliminatorRequest" => "Agency pushed for an eliminator.",
                    _ => "Agency leaned on matchmaking."
                },
                cancellationToken);
        }

        var body = bodyFactory(contender.Value.FighterName, $"#{contender.Value.QueueRank}");
        await InsertInboxMessageAsync(conn, tx, agentId, messageType, subject, body, currentDate, cancellationToken);

        tx.Commit();
        return ServiceResult.Ok("The request has been made and the division pressure has shifted a little.");
    }

    private static async Task<string> LoadCurrentDateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, '2026-01-01') FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "2026-01-01";
    }

    private static async Task<int> LoadPrimaryAgentIdAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(Id, 0) FROM AgentProfile ORDER BY Id LIMIT 1;";
        return Convert.ToInt32((await cmd.ExecuteScalarAsync(cancellationToken)) ?? 0);
    }

    private static async Task<(int QueueRank, string FighterName)?> LoadManagedContenderAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int promotionId,
        string weightClass,
        int fighterId,
        int agentId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    cq.QueueRank,
    (f.FirstName || ' ' || f.LastName) AS FighterName
FROM ContenderQueue cq
JOIN Fighters f ON f.Id = cq.FighterId
JOIN ManagedFighters mf ON mf.FighterId = cq.FighterId
WHERE cq.PromotionId = $promotionId
  AND cq.WeightClass = $weightClass
  AND cq.FighterId = $fighterId
  AND mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
LIMIT 1;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$weightClass", weightClass);
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return (
            Convert.ToInt32(reader["QueueRank"]),
            reader["FighterName"]?.ToString() ?? "Managed fighter");
    }

    private static async Task<bool> HasRecentManagerMoveAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string messageType,
        string subject,
        string currentDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT 1
FROM InboxMessages
WHERE AgentId = $agentId
  AND MessageType = $messageType
  AND Subject = $subject
  AND COALESCE(IsDeleted, 0) = 0
  AND date(CreatedDate) >= date($currentDate, '-21 day')
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$messageType", messageType);
        cmd.Parameters.AddWithValue("$subject", subject);
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        return (await cmd.ExecuteScalarAsync(cancellationToken)) is not null;
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
INSERT INTO InboxMessages (AgentId, MessageType, Subject, Body, CreatedDate, IsRead, IsArchived, IsDeleted)
VALUES ($agentId, $messageType, $subject, $body, $createdDate, 0, 0, 0);";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$messageType", messageType);
        cmd.Parameters.AddWithValue("$subject", subject);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$createdDate", createdDate);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertPromotionRelationAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        int promotionId,
        int delta,
        string currentDate,
        string notes,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO AgentPromotionRelations
(
    AgentId,
    PromotionId,
    RelationshipScore,
    LastUpdatedDate,
    Notes
)
VALUES
(
    $agentId,
    $promotionId,
    MIN(99, MAX(15, 50 + $delta)),
    $currentDate,
    $notes
)
ON CONFLICT(AgentId, PromotionId)
DO UPDATE SET
    RelationshipScore = MIN(99, MAX(15, COALESCE(AgentPromotionRelations.RelationshipScore, 50) + $delta)),
    LastUpdatedDate = $currentDate,
    Notes = $notes;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$delta", delta);
        cmd.Parameters.AddWithValue("$currentDate", currentDate);
        cmd.Parameters.AddWithValue("$notes", notes);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
