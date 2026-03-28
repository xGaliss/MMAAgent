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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ManagedFighters WHERE FighterId = $fighterId;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetAvailabilityAsync(int fighterId, int availableFromWeek, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE Fighters
SET AvailableFromWeek = $availableFromWeek,
    IsInjured = 0,
    IsBooked = 0
WHERE Id = $fighterId;";
        cmd.Parameters.AddWithValue("$availableFromWeek", availableFromWeek);
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearBookedStateAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Fighters SET IsBooked = 0 WHERE Id = $fighterId;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ExtendContractAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE Fighters
SET ContractFightsRemaining = ContractFightsRemaining + 2,
    TotalFightsInContract = TotalFightsInContract + 2
WHERE Id = $fighterId;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
