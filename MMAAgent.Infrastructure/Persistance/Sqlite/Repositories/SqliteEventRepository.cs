using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;

using System.Linq;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public interface IEventRepository
    {
        Task<IReadOnlyList<(int Id, string EventDate, string Name)>> GetLastEventsAsync(int take);
        Task<IReadOnlyList<(string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventAsync(int eventId);
        Task<IReadOnlyList<(int Id, string EventDate, string Name, string EventTier, int PlannedFightCount, int CompletedFightCount)>> GetLastEventsDetailedAsync(int take);
        Task<IReadOnlyList<(string CardSegment, int CardOrder, bool IsMainEvent, bool IsCoMainEvent, string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventDetailedAsync(int eventId);
    }

    public sealed class SqliteEventRepository : IEventRepository
    {
        private readonly SqliteConnectionFactory _factory;
        public SqliteEventRepository(SqliteConnectionFactory factory) => _factory = factory;

        public async Task<IReadOnlyList<(int Id, string EventDate, string Name)>> GetLastEventsAsync(int take)
        {
            var detailed = await GetLastEventsDetailedAsync(take);
            return detailed
                .Select(x => (x.Id, x.EventDate, x.Name))
                .ToList();
        }

        public async Task<IReadOnlyList<(int Id, string EventDate, string Name, string EventTier, int PlannedFightCount, int CompletedFightCount)>> GetLastEventsDetailedAsync(int take)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT
    Id,
    EventDate,
    COALESCE(Name,'') AS Name,
    COALESCE(EventTier, 'Standard') AS EventTier,
    COALESCE(
        NULLIF(PlannedFightCount, 0),
        (
            SELECT COUNT(*)
            FROM Fights f
            WHERE f.EventId = Events.Id
              AND COALESCE(f.Method, '') <> 'Cancelled'
        ),
        0
    ) AS PlannedFightCount,
    COALESCE(
        NULLIF(CompletedFightCount, 0),
        (
            SELECT COUNT(*)
            FROM FightHistory fh
            WHERE fh.EventId = Events.Id
        ),
        0
    ) AS CompletedFightCount
FROM Events
WHERE EXISTS (
        SELECT 1
        FROM Fights f
        WHERE f.EventId = Events.Id
          AND COALESCE(f.Method, '') <> 'Cancelled'
    )
   OR EXISTS (
        SELECT 1
        FROM FightHistory fh
        WHERE fh.EventId = Events.Id
    )
ORDER BY Id DESC
LIMIT $take;";
            cmd.Parameters.AddWithValue("$take", take);

            var list = new List<(int, string, string, string, int, int)>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add((
                    Convert.ToInt32(r["Id"]),
                    r["EventDate"]?.ToString() ?? "",
                    r["Name"]?.ToString() ?? "",
                    r["EventTier"]?.ToString() ?? "Standard",
                    Convert.ToInt32(r["PlannedFightCount"]),
                    Convert.ToInt32(r["CompletedFightCount"])
                ));
            }
            return list;
        }

        public async Task<IReadOnlyList<(string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventAsync(int eventId)
        {
            var detailed = await GetFightsByEventDetailedAsync(eventId);
            return detailed
                .Select(x => (x.WeightClass, x.Matchup, x.Winner, x.Method, x.IsTitle))
                .ToList();
        }

        public async Task<IReadOnlyList<(string CardSegment, int CardOrder, bool IsMainEvent, bool IsCoMainEvent, string WeightClass, string Matchup, string Winner, string Method, bool IsTitle)>> GetFightsByEventDetailedAsync(int eventId)
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT COALESCE(fh.CardSegment, 'Unassigned') AS CardSegment,
       COALESCE(fh.CardOrder, 0) AS CardOrder,
       COALESCE(fh.IsMainEvent, 0) AS IsMainEvent,
       COALESCE(fh.IsCoMainEvent, 0) AS IsCoMainEvent,
       fh.WeightClass,
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
ORDER BY COALESCE(fh.CardOrder, 0) DESC, fh.Id DESC;";
            cmd.Parameters.AddWithValue("$eventId", eventId);

            var list = new List<(string, int, bool, bool, string, string, string, string, bool)>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var cardSegment = r["CardSegment"]?.ToString() ?? "Unassigned";
                var cardOrder = Convert.ToInt32(r["CardOrder"]);
                var isMainEvent = Convert.ToInt32(r["IsMainEvent"]) == 1;
                var isCoMainEvent = Convert.ToInt32(r["IsCoMainEvent"]) == 1;
                var wc = r["WeightClass"]?.ToString() ?? "";
                var a = r["FighterA"]?.ToString() ?? "";
                var b = r["FighterB"]?.ToString() ?? "";
                var w = r["Winner"]?.ToString() ?? "";
                var m = r["Method"]?.ToString() ?? "";
                var t = Convert.ToInt32(r["IsTitle"]) == 1;

                list.Add((cardSegment, cardOrder, isMainEvent, isCoMainEvent, wc, $"{a} vs {b}", w, m, t));
            }
            return list;
        }
    }
}
