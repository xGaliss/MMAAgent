using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class WorldAgendaServiceSqlite : IWorldAgendaService
{
    private readonly SqliteConnectionFactory _factory;

    public WorldAgendaServiceSqlite(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);
        await ExecAsync(conn, tx, "DELETE FROM TimeQueue;", cancellationToken);
        await InsertPendingFightOffersAsync(conn, tx, currentDate, cancellationToken);
        await InsertPendingContractOffersAsync(conn, tx, currentDate, cancellationToken);
        await InsertManagedFightMilestonesAsync(conn, tx, currentDate, cancellationToken);
        await InsertRecoveryMilestonesAsync(conn, tx, currentDate, cancellationToken);

        tx.Commit();
    }

    private static async Task InsertPendingFightOffersAsync(SqliteConnection conn, SqliteTransaction tx, string currentDate, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO TimeQueue
(
    ScheduledDate,
    EventType,
    EntityType,
    EntityId,
    Priority,
    Headline,
    Subtitle,
    MetadataJson,
    Status
)
SELECT
    $currentDate,
    'Decision',
    'FightOffer',
    fo.Id,
    95,
    'Fight offer awaiting response',
    (f.FirstName || ' ' || f.LastName || ' vs ' || o.FirstName || ' ' || o.LastName || ' · ' || COALESCE(p.Name, 'Promotion')),
    NULL,
    'Pending'
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = fo.FighterId
JOIN Fighters o ON o.Id = fo.OpponentFighterId
LEFT JOIN Promotions p ON p.Id = fo.PromotionId
WHERE fo.Status = 'Pending';", cancellationToken, ("$currentDate", currentDate));
    }

    private static async Task InsertPendingContractOffersAsync(SqliteConnection conn, SqliteTransaction tx, string currentDate, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO TimeQueue
(
    ScheduledDate,
    EventType,
    EntityType,
    EntityId,
    Priority,
    Headline,
    Subtitle,
    MetadataJson,
    Status
)
SELECT
    $currentDate,
    'Decision',
    'ContractOffer',
    co.Id,
    88,
    'Contract offer awaiting response',
    (f.FirstName || ' ' || f.LastName || ' · ' || COALESCE(p.Name, 'Promotion')),
    NULL,
    'Pending'
FROM ContractOffers co
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = co.FighterId
LEFT JOIN Promotions p ON p.Id = co.PromotionId
WHERE co.Status = 'Pending';", cancellationToken, ("$currentDate", currentDate));
    }

    private static async Task InsertManagedFightMilestonesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    sf.Id AS FightId,
    sf.EventDate,
    COALESCE(sf.IsTitleFight, 0) AS IsTitleFight,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    (o.FirstName || ' ' || o.LastName) AS OpponentName,
    COALESCE(e.Name, 'Upcoming Event') AS EventName,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(p.StandardCampWeeks, 4) AS StandardCampWeeks,
    COALESCE(p.MajorCampWeeks, 6) AS MajorCampWeeks,
    COALESCE(p.TitleCampWeeks, 8) AS TitleCampWeeks
FROM Fights sf
JOIN ManagedFighters mf ON (mf.FighterId = sf.FighterAId OR mf.FighterId = sf.FighterBId) AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = mf.FighterId
JOIN Fighters o ON o.Id = CASE WHEN sf.FighterAId = mf.FighterId THEN sf.FighterBId ELSE sf.FighterAId END
LEFT JOIN Events e ON e.Id = sf.EventId
LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, f.PromotionId)
WHERE sf.Method = 'Scheduled'
  AND COALESCE(sf.EventDate, '') <> ''
  AND sf.EventDate >= $currentDate
GROUP BY sf.Id
ORDER BY sf.EventDate, sf.Id;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fightId = Convert.ToInt32(reader["FightId"]);
            var eventDate = reader["EventDate"]?.ToString();
            if (!DateTime.TryParse(eventDate, out var parsedEventDate))
                continue;

            var isTitleFight = Convert.ToInt32(reader["IsTitleFight"]) == 1;
            var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
            var opponentName = reader["OpponentName"]?.ToString() ?? "Opponent";
            var eventName = reader["EventName"]?.ToString() ?? "Upcoming Event";
            var promotionName = reader["PromotionName"]?.ToString() ?? "Promotion";

            var campWeeks = isTitleFight
                ? Convert.ToInt32(reader["TitleCampWeeks"])
                : Convert.ToInt32(reader["StandardCampWeeks"]);

            if (!isTitleFight)
                campWeeks = Math.Max(campWeeks, Convert.ToInt32(reader["MajorCampWeeks"]) - 1);

            await InsertAgendaRowAsync(
                conn, tx, parsedEventDate.AddDays(-(campWeeks * 7)), currentDate,
                "CampStart", "Fight", fightId, 70,
                $"Camp starts for {fighterName}",
                $"{eventName} vs {opponentName} · {promotionName}",
                cancellationToken);

            await InsertAgendaRowAsync(
                conn, tx, parsedEventDate.AddDays(-7), currentDate,
                "FightWeek", "Fight", fightId, 82,
                $"Fight week for {fighterName}",
                $"{eventName} vs {opponentName}",
                cancellationToken);

            await InsertAgendaRowAsync(
                conn, tx, parsedEventDate.AddDays(-2), currentDate,
                "WeighIn", "Fight", fightId, 86,
                $"Weigh-in approaching for {fighterName}",
                $"{eventName} · weight cut checkpoint",
                cancellationToken);

            await InsertAgendaRowAsync(
                conn, tx, parsedEventDate, currentDate,
                "FightNight", "Fight", fightId, 92,
                $"Fight night: {fighterName} vs {opponentName}",
                eventName,
                cancellationToken);
        }
    }

    private static async Task InsertRecoveryMilestonesAsync(SqliteConnection conn, SqliteTransaction tx, string currentDate, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    COALESCE(f.MedicalSuspensionWeeksRemaining, 0) AS MedicalSuspensionWeeksRemaining,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE COALESCE(mf.IsActive, 1) = 1
  AND (
      COALESCE(f.MedicalSuspensionWeeksRemaining, 0) > 0
      OR COALESCE(f.InjuryWeeksRemaining, 0) > 0
      OR COALESCE(f.WeeksUntilAvailable, 0) > 0
  );";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fighterId = Convert.ToInt32(reader["Id"]);
            var fighterName = reader["FighterName"]?.ToString() ?? "Managed fighter";
            var medicalWeeks = Convert.ToInt32(reader["MedicalSuspensionWeeksRemaining"]);
            var injuryWeeks = Convert.ToInt32(reader["InjuryWeeksRemaining"]);
            var availabilityWeeks = Convert.ToInt32(reader["WeeksUntilAvailable"]);
            var furthestWeeks = Math.Max(medicalWeeks, Math.Max(injuryWeeks, availabilityWeeks));
            if (furthestWeeks <= 0)
                continue;

            var recoveryDate = DateTime.TryParse(currentDate, out var parsedCurrentDate)
                ? parsedCurrentDate.AddDays(furthestWeeks * 7)
                : DateTime.UtcNow.Date.AddDays(furthestWeeks * 7);

            var reason = medicalWeeks > 0
                ? "medical clearance"
                : injuryWeeks > 0
                    ? "injury recovery"
                    : "full availability";

            await InsertAgendaRowAsync(
                conn, tx, recoveryDate, currentDate,
                "Recovery", "Fighter", fighterId, 58,
                $"{fighterName} should return to action",
                $"Projected {reason}",
                cancellationToken);
        }
    }

    private static async Task InsertAgendaRowAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        DateTime scheduledDate,
        string currentDate,
        string eventType,
        string entityType,
        int entityId,
        int priority,
        string headline,
        string subtitle,
        CancellationToken cancellationToken)
    {
        if (!DateTime.TryParse(currentDate, out var parsedCurrentDate))
            parsedCurrentDate = DateTime.UtcNow.Date;

        if (scheduledDate.Date < parsedCurrentDate.AddDays(-1))
            return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO TimeQueue
(
    ScheduledDate,
    EventType,
    EntityType,
    EntityId,
    Priority,
    Headline,
    Subtitle,
    MetadataJson,
    Status
)
VALUES
(
    $scheduledDate,
    $eventType,
    $entityType,
    $entityId,
    $priority,
    $headline,
    $subtitle,
    NULL,
    'Pending'
);";
        cmd.Parameters.AddWithValue("$scheduledDate", scheduledDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$eventType", eventType);
        cmd.Parameters.AddWithValue("$entityType", entityType);
        cmd.Parameters.AddWithValue("$entityId", entityId);
        cmd.Parameters.AddWithValue("$priority", priority);
        cmd.Parameters.AddWithValue("$headline", headline);
        cmd.Parameters.AddWithValue("$subtitle", subtitle);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> LoadCurrentDateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, '2026-01-01') FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "2026-01-01";
    }

    private static async Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
