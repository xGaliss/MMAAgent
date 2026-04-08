using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class FightOfferRepository : IFightOfferRepository
    {
        private readonly ISavePathProvider _savePathProvider;

        public FightOfferRepository(ISavePathProvider savePathProvider)
        {
            _savePathProvider = savePathProvider;
        }

        public async Task<int> CreateAsync(FightOffer offer)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO FightOffers
(
    FighterId,
    PromotionId,
    OpponentFighterId,
    Purse,
    WinBonus,
    WeeksUntilFight,
    IsTitleFight,
    Status
)
VALUES
(
    $fighterId,
    $promotionId,
    $opponentFighterId,
    $purse,
    $winBonus,
    $weeksUntilFight,
    $isTitleFight,
    $status
);

SELECT last_insert_rowid();
";

            cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
            cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
            cmd.Parameters.AddWithValue("$opponentFighterId", offer.OpponentFighterId);
            cmd.Parameters.AddWithValue("$purse", offer.Purse);
            cmd.Parameters.AddWithValue("$winBonus", offer.WinBonus);
            cmd.Parameters.AddWithValue("$weeksUntilFight", offer.WeeksUntilFight);
            cmd.Parameters.AddWithValue("$isTitleFight", offer.IsTitleFight ? 1 : 0);
            cmd.Parameters.AddWithValue("$status", offer.Status);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<IReadOnlyList<FightOffer>> GetByAgentAsync(int agentId)
        {
            var dbPath = _savePathProvider.CurrentPath;
            var result = new List<FightOffer>();

            if (string.IsNullOrWhiteSpace(dbPath))
                return result;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT fo.Id,
       fo.FighterId,
       fo.PromotionId,
       fo.OpponentFighterId,
       fo.Purse,
       fo.WinBonus,
       fo.WeeksUntilFight,
       fo.IsTitleFight,
       fo.Status
FROM FightOffers fo
INNER JOIN ManagedFighters mf
    ON mf.FighterId = fo.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY fo.Id DESC;
";

            cmd.Parameters.AddWithValue("$agentId", agentId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new FightOffer
                {
                    Id = reader.GetInt32(0),
                    FighterId = reader.GetInt32(1),
                    PromotionId = reader.GetInt32(2),
                    OpponentFighterId = reader.GetInt32(3),
                    Purse = reader.GetInt32(4),
                    WinBonus = reader.GetInt32(5),
                    WeeksUntilFight = reader.GetInt32(6),
                    IsTitleFight = reader.GetInt32(7) == 1,
                    Status = reader.GetString(8)
                });
            }

            return result;
        }

        public async Task UpdateStatusAsync(int offerId, string status)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE FightOffers
SET Status = $status
WHERE Id = $id;
";

            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$id", offerId);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
