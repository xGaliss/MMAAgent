using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class ManagedFighterRepository : IManagedFighterRepository
    {
        private readonly ISavePathProvider _savePathProvider;

        public ManagedFighterRepository(ISavePathProvider savePathProvider)
        {
            _savePathProvider = savePathProvider;
        }

        public async Task<IReadOnlyList<ManagedFighter>> GetByAgentAsync(int agentId)
        {
            var dbPath = _savePathProvider.CurrentPath;
            var result = new List<ManagedFighter>();

            if (string.IsNullOrWhiteSpace(dbPath))
                return result;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT Id, AgentId, FighterId, SignedDate, ManagementPercent, IsActive
FROM ManagedFighters
WHERE AgentId = $agentId
ORDER BY Id;";

            cmd.Parameters.AddWithValue("$agentId", agentId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ManagedFighter
                {
                    Id = reader.GetInt32(0),
                    AgentId = reader.GetInt32(1),
                    FighterId = reader.GetInt32(2),
                    SignedDate = reader.GetString(3),
                    ManagementPercent = reader.GetInt32(4),
                    IsActive = reader.GetInt32(5) == 1
                });
            }

            return result;
        }

        public async Task<bool> IsManagedByAgentAsync(int agentId, int fighterId)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return false;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(1)
FROM ManagedFighters
WHERE AgentId = $agentId
  AND FighterId = $fighterId
  AND IsActive = 1;";

            cmd.Parameters.AddWithValue("$agentId", agentId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return count > 0;
        }

        public async Task<int> AddAsync(ManagedFighter item)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO ManagedFighters (AgentId, FighterId, SignedDate, ManagementPercent, IsActive)
VALUES ($agentId, $fighterId, $signedDate, $managementPercent, $isActive);

SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$agentId", item.AgentId);
            cmd.Parameters.AddWithValue("$fighterId", item.FighterId);
            cmd.Parameters.AddWithValue("$signedDate", item.SignedDate);
            cmd.Parameters.AddWithValue("$managementPercent", item.ManagementPercent);
            cmd.Parameters.AddWithValue("$isActive", item.IsActive ? 1 : 0);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}