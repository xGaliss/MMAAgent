using Microsoft.Data.Sqlite;
using MMAAgent.Application.Simulation;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class SqlitePromotionEventScheduleRepository : IPromotionEventScheduleRepository
    {
        private readonly SqliteConnectionFactory _factory;
        public SqlitePromotionEventScheduleRepository(SqliteConnectionFactory factory) => _factory = factory;

        public async Task<PromotionScheduleRow?> GetAsync(int promotionId)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id AS PromotionId, EventIntervalWeeks, NextEventWeek, IsActive
FROM Promotions
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            return new PromotionScheduleRow(
                PromotionId: Convert.ToInt32(r["PromotionId"]),
                IntervalWeeks: Convert.ToInt32(r["EventIntervalWeeks"]),
                NextEventWeek: Convert.ToInt32(r["NextEventWeek"]),
                IsActive: Convert.ToInt32(r["IsActive"]) == 1
            );
        }

        public async Task<IReadOnlyList<PromotionScheduleRow>> GetDueAsync(int absoluteWeek)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id AS PromotionId, EventIntervalWeeks, NextEventWeek, IsActive
FROM Promotions
WHERE IsActive = 1
  AND NextEventWeek <= $w;";
            cmd.Parameters.AddWithValue("$w", absoluteWeek);

            var list = new List<PromotionScheduleRow>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new PromotionScheduleRow(
                    PromotionId: Convert.ToInt32(r["PromotionId"]),
                    IntervalWeeks: Convert.ToInt32(r["EventIntervalWeeks"]),
                    NextEventWeek: Convert.ToInt32(r["NextEventWeek"]),
                    IsActive: Convert.ToInt32(r["IsActive"]) == 1
                ));
            }
            return list;
        }

        public async Task SetNextEventWeekAsync(int promotionId, int nextAbsoluteWeek)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Promotions
SET NextEventWeek = $n
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$n", Math.Max(0, nextAbsoluteWeek));
            cmd.Parameters.AddWithValue("$id", promotionId);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpsertAsync(PromotionScheduleRow row)
        {
            // Como tu schedule vive en la tabla Promotions, “Upsert” aquí realmente es “Update”.
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Promotions
SET IsActive = $a,
    EventIntervalWeeks = $i,
    NextEventWeek = $n
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", row.PromotionId);
            cmd.Parameters.AddWithValue("$a", row.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$i", Math.Max(1, row.IntervalWeeks));
            cmd.Parameters.AddWithValue("$n", Math.Max(0, row.NextEventWeek));

            await cmd.ExecuteNonQueryAsync();
        }
    }
}