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
}
