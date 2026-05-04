using Microsoft.Data.Sqlite;
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

    private static bool IsValidCampFocus(string campFocus)
        => campFocus is "Cardio" or "Wrestling" or "Striking" or "Recovery" or "WeightManagement";

    private static async Task<string> LoadCurrentDateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, '2026-01-01') FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "2026-01-01";
    }
}
