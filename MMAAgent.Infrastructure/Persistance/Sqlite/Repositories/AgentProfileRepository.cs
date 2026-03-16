using System;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class AgentProfileRepository : IAgentProfileRepository
    {
        private readonly ISavePathProvider _savePathProvider;

        public AgentProfileRepository(ISavePathProvider savePathProvider)
        {
            _savePathProvider = savePathProvider;
        }

        public async Task<AgentProfile?> GetAsync()
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, AgencyName, Money, Reputation, CreatedDate
FROM AgentProfile
ORDER BY Id
LIMIT 1;";

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new AgentProfile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                AgencyName = reader.GetString(2),
                Money = reader.GetInt32(3),
                Reputation = reader.GetInt32(4),
                CreatedDate = reader.GetString(5)
            };
        }

        public async Task<int> CreateAsync(AgentProfile profile)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO AgentProfile (Name, AgencyName, Money, Reputation, CreatedDate)
VALUES ($name, $agencyName, $money, $reputation, $createdDate);

SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$name", profile.Name);
            cmd.Parameters.AddWithValue("$agencyName", profile.AgencyName);
            cmd.Parameters.AddWithValue("$money", profile.Money);
            cmd.Parameters.AddWithValue("$reputation", profile.Reputation);
            cmd.Parameters.AddWithValue("$createdDate", profile.CreatedDate);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}