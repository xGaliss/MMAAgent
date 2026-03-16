using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public interface IEventRepository
    {
        Task<IReadOnlyList<(int Id, string EventDate, string Name)>> GetLastEventsAsync(int take);
        Task<IReadOnlyList<(string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventAsync(int eventId);
    }

    public sealed class SqliteEventRepository : IEventRepository
    {
        private readonly SqliteConnectionFactory _factory;
        public SqliteEventRepository(SqliteConnectionFactory factory) => _factory = factory;

        public async Task<IReadOnlyList<(int Id, string EventDate, string Name)>> GetLastEventsAsync(int take)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, EventDate, COALESCE(Name,'') AS Name
FROM Events
ORDER BY Id DESC
LIMIT $take;";
            cmd.Parameters.AddWithValue("$take", take);

            var list = new List<(int, string, string)>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add((
                    Convert.ToInt32(r["Id"]),
                    r["EventDate"]?.ToString() ?? "",
                    r["Name"]?.ToString() ?? ""
                ));
            }
            return list;
        }

        public async Task<IReadOnlyList<(string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventAsync(int eventId)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT fh.WeightClass,
       fa.FirstName || ' ' || fa.LastName AS FighterA,
       fb.FirstName || ' ' || fb.LastName AS FighterB,
       fw.FirstName || ' ' || fw.LastName AS Winner,
       fh.Method,
       fh.IsTitle
FROM FightHistory fh
JOIN Fighters fa ON fa.Id = fh.FighterAId
JOIN Fighters fb ON fb.Id = fh.FighterBId
JOIN Fighters fw ON fw.Id = fh.WinnerId
WHERE fh.EventId = $eventId
ORDER BY fh.Id ASC;";
            cmd.Parameters.AddWithValue("$eventId", eventId);

            var list = new List<(string, string, string, string, bool)>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var wc = r["WeightClass"]?.ToString() ?? "";
                var a = r["FighterA"]?.ToString() ?? "";
                var b = r["FighterB"]?.ToString() ?? "";
                var w = r["Winner"]?.ToString() ?? "";
                var m = r["Method"]?.ToString() ?? "";
                var t = Convert.ToInt32(r["IsTitle"]) == 1;

                list.Add((wc, $"{a} vs {b}", w, m, t));
            }
            return list;
        }
    }
}