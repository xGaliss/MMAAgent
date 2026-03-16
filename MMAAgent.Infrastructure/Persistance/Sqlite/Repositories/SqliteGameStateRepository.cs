using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Common;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using System.Globalization;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class SqliteGameStateRepository : IGameStateRepository
    {
        private readonly SqliteConnectionFactory _factory;

        public SqliteGameStateRepository(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<GameState?> GetAsync()
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"SELECT Id, StartDate, CurrentDate, CurrentWeek, CurrentYear, WorldSeed
                                FROM GameState WHERE Id=1 LIMIT 1;";

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            return new GameState
            {
                Id = 1,
                StartDate = r["StartDate"]?.ToString() ?? "",
                CurrentDate = r["CurrentDate"]?.ToString() ?? "",
                CurrentWeek = System.Convert.ToInt32(r["CurrentWeek"]),
                CurrentYear = System.Convert.ToInt32(r["CurrentYear"]),
                WorldSeed = System.Convert.ToInt32(r["WorldSeed"])
            };
        }

        public async Task EnsureCreatedAsync(DateTime startDate, int worldSeed)
        {
            var s = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO GameState (Id, StartDate, CurrentDate, CurrentWeek, CurrentYear, WorldSeed)
VALUES (1, $StartDate, $CurrentDate, 1, 1, $WorldSeed)
ON CONFLICT(Id) DO UPDATE SET
    StartDate = excluded.StartDate,
    CurrentDate = excluded.CurrentDate,
    CurrentWeek = excluded.CurrentWeek,
    CurrentYear = excluded.CurrentYear,
    WorldSeed = excluded.WorldSeed;
";

            cmd.Parameters.AddWithValue("$StartDate", s);
            cmd.Parameters.AddWithValue("$CurrentDate", s);
            cmd.Parameters.AddWithValue("$WorldSeed", worldSeed);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateAsync(GameState state)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE GameState
SET StartDate=$StartDate,
    CurrentDate=$CurrentDate,
    CurrentWeek=$CurrentWeek,
    CurrentYear=$CurrentYear,
    WorldSeed=$WorldSeed
WHERE Id=1;";

            cmd.Parameters.AddWithValue("$StartDate", state.StartDate);
            cmd.Parameters.AddWithValue("$CurrentDate", state.CurrentDate);
            cmd.Parameters.AddWithValue("$CurrentWeek", state.CurrentWeek);
            cmd.Parameters.AddWithValue("$CurrentYear", state.CurrentYear);
            cmd.Parameters.AddWithValue("$WorldSeed", state.WorldSeed);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}