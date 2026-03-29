using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Repositories;

public sealed class SqliteContractOfferRepository : IContractOfferRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteContractOfferRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task CreateAsync(ContractOffer offer, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ContractOffers
(
    FighterId,
    PromotionId,
    OfferedFights,
    BasePurse,
    WinBonus,
    WeeksToRespond,
    Status,
    SourceType,
    Notes,
    CreatedWeek,
    CreatedDate,
    RespondedDate
)
VALUES
(
    $fighterId,
    $promotionId,
    $offeredFights,
    $basePurse,
    $winBonus,
    $weeksToRespond,
    $status,
    $sourceType,
    $notes,
    $createdWeek,
    $createdDate,
    $respondedDate
);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
        cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
        cmd.Parameters.AddWithValue("$offeredFights", offer.OfferedFights);
        cmd.Parameters.AddWithValue("$basePurse", offer.BasePurse);
        cmd.Parameters.AddWithValue("$winBonus", offer.WinBonus);
        cmd.Parameters.AddWithValue("$weeksToRespond", offer.WeeksToRespond);
        cmd.Parameters.AddWithValue("$status", offer.Status);
        cmd.Parameters.AddWithValue("$sourceType", offer.SourceType);
        cmd.Parameters.AddWithValue("$notes", (object?)offer.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdWeek", offer.CreatedWeek);
        cmd.Parameters.AddWithValue("$createdDate", offer.CreatedDate);
        cmd.Parameters.AddWithValue("$respondedDate", (object?)offer.RespondedDate ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ContractOffer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FighterId, PromotionId, OfferedFights, BasePurse, WinBonus,
       WeeksToRespond, Status, SourceType, Notes, CreatedWeek, CreatedDate, RespondedDate
FROM ContractOffers
WHERE Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return Map(reader);
    }

    public async Task<IReadOnlyList<ContractOffer>> ListByAgentAsync(int agentId, bool pendingOnly = false, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT co.Id, co.FighterId, co.PromotionId, co.OfferedFights, co.BasePurse, co.WinBonus,
       co.WeeksToRespond, co.Status, co.SourceType, co.Notes, co.CreatedWeek, co.CreatedDate, co.RespondedDate
FROM ContractOffers co
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND mf.AgentId = $agentId
{(pendingOnly ? "WHERE co.Status = 'Pending'" : string.Empty)}
ORDER BY CASE WHEN co.Status = 'Pending' THEN 0 ELSE 1 END,
         co.Id DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var list = new List<ContractOffer>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Map(reader));
        }
        return list;
    }

    public async Task<bool> HasPendingOfferAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ContractOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task UpdateStatusAsync(int id, string status, string? respondedDate = null, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE ContractOffers
SET Status = $status,
    RespondedDate = $respondedDate
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$respondedDate", (object?)respondedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountPendingByAgentAsync(int agentId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*)
FROM ContractOffers co
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND mf.AgentId = $agentId
WHERE co.Status = 'Pending';";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static ContractOffer Map(SqliteDataReader reader)
    {
        return new ContractOffer
        {
            Id = Convert.ToInt32(reader["Id"]),
            FighterId = Convert.ToInt32(reader["FighterId"]),
            PromotionId = Convert.ToInt32(reader["PromotionId"]),
            OfferedFights = Convert.ToInt32(reader["OfferedFights"]),
            BasePurse = Convert.ToInt32(reader["BasePurse"]),
            WinBonus = Convert.ToInt32(reader["WinBonus"]),
            WeeksToRespond = Convert.ToInt32(reader["WeeksToRespond"]),
            Status = reader["Status"]?.ToString() ?? "Pending",
            SourceType = reader["SourceType"]?.ToString() ?? "Market",
            Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString(),
            CreatedWeek = Convert.ToInt32(reader["CreatedWeek"]),
            CreatedDate = reader["CreatedDate"]?.ToString() ?? "",
            RespondedDate = reader["RespondedDate"] == DBNull.Value ? null : reader["RespondedDate"]?.ToString()
        };
    }
}
