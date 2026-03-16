using MMAAgent.Application.Abstractions;
using MMAAgent.Application.DTOs;
namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class PromotionRepositorySqlite : IPromotionRepository
    {
        private readonly SqliteConnectionFactory _factory;

        public PromotionRepositorySqlite(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<PromotionListItem>> GetAllAsync()
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id,
       Name,
       Prestige,
       Budget,
       IsActive,
       EventIntervalWeeks,
       NextEventWeek
FROM Promotions
ORDER BY Prestige DESC, Name ASC;";

            var list = new List<PromotionListItem>();

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new PromotionListItem
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Name = r["Name"]?.ToString() ?? "",
                    Prestige = Convert.ToInt32(r["Prestige"]),
                    Budget = Convert.ToInt32(r["Budget"]),
                    IsActive = Convert.ToInt32(r["IsActive"]) == 1,
                    EventIntervalWeeks = Convert.ToInt32(r["EventIntervalWeeks"]),
                    NextEventWeek = Convert.ToInt32(r["NextEventWeek"])
                });
            }

            return list;
        }
    }
}