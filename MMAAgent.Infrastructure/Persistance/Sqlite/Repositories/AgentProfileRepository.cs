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
SELECT Id,
       Name,
       AgencyName,
       COALESCE(Nationality, ''),
       COALESCE(AvatarKey, ''),
       Money,
       Reputation,
       COALESCE(PublicReputation, 50),
       COALESCE(FighterTrust, 50),
       COALESCE(PromotionLeverage, 50),
       COALESCE(ScoutingStaffLevel, 1),
       COALESCE(MediaStaffLevel, 1),
       COALESCE(NegotiationStaffLevel, 1),
       COALESCE(PerformanceStaffLevel, 1),
       CreatedDate
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
                Nationality = reader.GetString(3),
                AvatarKey = reader.GetString(4),
                Money = reader.GetInt32(5),
                Reputation = reader.GetInt32(6),
                PublicReputation = reader.GetInt32(7),
                FighterTrust = reader.GetInt32(8),
                PromotionLeverage = reader.GetInt32(9),
                ScoutingStaffLevel = reader.GetInt32(10),
                MediaStaffLevel = reader.GetInt32(11),
                NegotiationStaffLevel = reader.GetInt32(12),
                PerformanceStaffLevel = reader.GetInt32(13),
                CreatedDate = reader.GetString(14)
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
INSERT INTO AgentProfile
(
    Name,
    AgencyName,
    Nationality,
    AvatarKey,
    Money,
    Reputation,
    PublicReputation,
    FighterTrust,
    PromotionLeverage,
    ScoutingStaffLevel,
    MediaStaffLevel,
    NegotiationStaffLevel,
    PerformanceStaffLevel,
    CreatedDate
)
VALUES
(
    $name,
    $agencyName,
    $nationality,
    $avatarKey,
    $money,
    $reputation,
    $publicReputation,
    $fighterTrust,
    $promotionLeverage,
    $scoutingStaffLevel,
    $mediaStaffLevel,
    $negotiationStaffLevel,
    $performanceStaffLevel,
    $createdDate
);

SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$name", profile.Name);
            cmd.Parameters.AddWithValue("$agencyName", profile.AgencyName);
            cmd.Parameters.AddWithValue("$nationality", profile.Nationality ?? "");
            cmd.Parameters.AddWithValue("$avatarKey", profile.AvatarKey ?? "");
            cmd.Parameters.AddWithValue("$money", profile.Money);
            cmd.Parameters.AddWithValue("$reputation", profile.Reputation);
            cmd.Parameters.AddWithValue("$publicReputation", profile.PublicReputation);
            cmd.Parameters.AddWithValue("$fighterTrust", profile.FighterTrust);
            cmd.Parameters.AddWithValue("$promotionLeverage", profile.PromotionLeverage);
            cmd.Parameters.AddWithValue("$scoutingStaffLevel", profile.ScoutingStaffLevel);
            cmd.Parameters.AddWithValue("$mediaStaffLevel", profile.MediaStaffLevel);
            cmd.Parameters.AddWithValue("$negotiationStaffLevel", profile.NegotiationStaffLevel);
            cmd.Parameters.AddWithValue("$performanceStaffLevel", profile.PerformanceStaffLevel);
            cmd.Parameters.AddWithValue("$createdDate", profile.CreatedDate);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}
