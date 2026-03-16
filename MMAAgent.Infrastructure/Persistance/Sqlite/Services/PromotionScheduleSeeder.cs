using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class PromotionScheduleSeeder
    {
        private readonly SqliteConnectionFactory _factory;

        public PromotionScheduleSeeder(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InitializeForNewSaveAsync(int startAbsoluteWeek = 0)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
UPDATE Promotions
SET NextEventWeek = $w
WHERE IsActive = 1;";
            cmd.Parameters.AddWithValue("$w", startAbsoluteWeek);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}