using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class ContractOfferRepository : IContractOfferRepository
    {
        private readonly ISavePathProvider _savePathProvider;

        public ContractOfferRepository(ISavePathProvider savePathProvider)
        {
            _savePathProvider = savePathProvider;
        }

        public async Task CreateAsync(ContractOffer offer, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
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
);";

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
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT
    Id,
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
FROM ContractOffers
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return MapOffer(reader);

            return null;
        }

        public async Task<IReadOnlyList<ContractOffer>> ListByAgentAsync(
            int agentId,
            bool pendingOnly,
            CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            var result = new List<ContractOffer>();

            if (string.IsNullOrWhiteSpace(dbPath))
                return result;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT
    co.Id,
    co.FighterId,
    co.PromotionId,
    co.OfferedFights,
    co.BasePurse,
    co.WinBonus,
    co.WeeksToRespond,
    co.Status,
    co.SourceType,
    co.Notes,
    co.CreatedWeek,
    co.CreatedDate,
    co.RespondedDate
FROM ContractOffers co
INNER JOIN ManagedFighters mf ON mf.FighterId = co.FighterId
WHERE mf.AgentId = $agentId
  AND ($pendingOnly = 0 OR co.Status = 'Pending')
ORDER BY co.Id DESC;";
            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$pendingOnly", pendingOnly ? 1 : 0);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(MapOffer(reader));
            }

            return result;
        }

        public async Task<bool> HasPendingOfferAsync(int fighterId, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return false;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(*)
FROM ContractOffers
WHERE FighterId = $fighterId
  AND Status = 'Pending';";
            cmd.Parameters.AddWithValue("$fighterId", fighterId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }

        public async Task UpdateStatusAsync(
            int id,
            string status,
            string? respondedDate,
            CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE ContractOffers
SET Status = $status,
    RespondedDate = $respondedDate
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$respondedDate", (object?)respondedDate ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<int> CountPendingByAgentAsync(int agentId, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return 0;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(*)
FROM ContractOffers co
INNER JOIN ManagedFighters mf ON mf.FighterId = co.FighterId
WHERE mf.AgentId = $agentId
  AND co.Status = 'Pending';";
            cmd.Parameters.AddWithValue("$agentId", agentId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }

        private static ContractOffer MapOffer(SqliteDataReader reader)
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
                Status = reader["Status"]?.ToString() ?? "",
                SourceType = reader["SourceType"]?.ToString() ?? "",
                Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString(),
                CreatedWeek = Convert.ToInt32(reader["CreatedWeek"]),
                CreatedDate = reader["CreatedDate"]?.ToString() ?? "",
                RespondedDate = reader["RespondedDate"] == DBNull.Value ? null : reader["RespondedDate"]?.ToString()
            };
        }
    }
}