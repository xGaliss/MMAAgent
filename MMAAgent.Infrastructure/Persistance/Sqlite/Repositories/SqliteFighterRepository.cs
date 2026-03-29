using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Fighters;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class SqliteFighterRepository : IFighterRepository
    {
        private readonly SqliteConnectionFactory _factory;

        public SqliteFighterRepository(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }
        public async Task<bool> IsManagedAsync(int fighterId)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT COUNT(1)
FROM ManagedFighters
WHERE FighterId = $id;";

            cmd.Parameters.AddWithValue("$id", fighterId);

            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return result > 0;
        }

        public async Task<IReadOnlyList<FighterSummary>> GetRosterAsync(int take = 200)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            // Ajusta nombres de columnas si en tu tabla difieren
            cmd.CommandText = @"
SELECT 
    f.Id,
    f.FirstName,
    f.LastName,
    f.Wins,
    f.Losses,
    c.Name AS CountryName,
    f.WeightClass
FROM Fighters f
JOIN Countries c ON c.Id = f.CountryId
ORDER BY f.Wins DESC, f.Losses ASC
LIMIT $take;
";
            cmd.Parameters.AddWithValue("$take", take);

            var list = new List<FighterSummary>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var first = reader["FirstName"]?.ToString() ?? "";
                var last = reader["LastName"]?.ToString() ?? "";

                list.Add(new FighterSummary
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = (first + " " + last).Trim(),
                    Wins = Convert.ToInt32(reader["Wins"]),
                    Losses = Convert.ToInt32(reader["Losses"]),
                    CountryName = reader["CountryName"]?.ToString() ?? "",
                    WeightClass = reader["WeightClass"]?.ToString() ?? ""
                });
            }

            return list;
        }
    }
}