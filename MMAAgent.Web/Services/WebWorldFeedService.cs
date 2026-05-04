using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebWorldFeedService
{
    private readonly SqliteConnectionFactory _factory;

    public WebWorldFeedService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<WorldFeedVm> LoadAsync()
    {
        using var conn = _factory.CreateConnection();

        var headlines = new List<WorldFeedItemVm>();
        headlines.AddRange(await LoadTitleFightHeadlinesAsync(conn));
        headlines.AddRange(await LoadEliminatorHeadlinesAsync(conn));
        headlines.AddRange(await LoadAnnualShiftHeadlinesAsync(conn));

        var orderedHeadlines = headlines
            .OrderByDescending(x => x.Date, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Bucket, StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToArray();

        var storylines = await LoadStorylinesAsync(conn);

        return new WorldFeedVm
        {
            Headlines = orderedHeadlines,
            Storylines = storylines
        };
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadTitleFightHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(fh.FightDate, '') AS FeedDate,
    (w.FirstName || ' ' || w.LastName) AS WinnerName,
    (l.FirstName || ' ' || l.LastName) AS LoserName,
    COALESCE(fh.Method, 'DEC') AS Method,
    COALESCE(fh.WeightClass, 'Division') AS WeightClass,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(e.Name, '') AS EventName,
    fh.WinnerId
FROM FightHistory fh
JOIN Fighters w ON w.Id = fh.WinnerId
JOIN Fighters l ON l.Id = fh.LoserId
LEFT JOIN Promotions p ON p.Id = fh.PromotionId
LEFT JOIN Events e ON e.Id = fh.EventId
WHERE COALESCE(fh.IsTitle, 0) = 1
ORDER BY fh.Id DESC
LIMIT 8;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var winnerName = reader["WinnerName"]?.ToString() ?? "Winner";
            var loserName = reader["LoserName"]?.ToString() ?? "Opponent";
            var method = reader["Method"]?.ToString() ?? "DEC";
            var promotion = reader["PromotionName"]?.ToString() ?? "Promotion";
            var weightClass = reader["WeightClass"]?.ToString() ?? "Division";
            var eventName = reader["EventName"]?.ToString();
            var winnerId = Convert.ToInt32(reader["WinnerId"]);

            items.Add(new WorldFeedItemVm(
                Bucket: "Title Fight",
                Headline: $"{winnerName} takes the {weightClass} crown",
                Summary: string.IsNullOrWhiteSpace(eventName)
                    ? $"{winnerName} beat {loserName} via {method} in {promotion}."
                    : $"{winnerName} beat {loserName} via {method} at {eventName} ({promotion}).",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "gold",
                LinkHref: $"/fighters/{winnerId}"));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadEliminatorHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(fh.FightDate, '') AS FeedDate,
    (w.FirstName || ' ' || w.LastName) AS WinnerName,
    (l.FirstName || ' ' || l.LastName) AS LoserName,
    COALESCE(fh.Method, 'DEC') AS Method,
    COALESCE(fh.WeightClass, 'Division') AS WeightClass,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    fh.WinnerId
FROM FightHistory fh
JOIN Fighters w ON w.Id = fh.WinnerId
JOIN Fighters l ON l.Id = fh.LoserId
LEFT JOIN Promotions p ON p.Id = fh.PromotionId
WHERE COALESCE(fh.IsTitleEliminator, 0) = 1
ORDER BY fh.Id DESC
LIMIT 8;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var winnerName = reader["WinnerName"]?.ToString() ?? "Winner";
            var loserName = reader["LoserName"]?.ToString() ?? "Opponent";
            var method = reader["Method"]?.ToString() ?? "DEC";
            var promotion = reader["PromotionName"]?.ToString() ?? "Promotion";
            var weightClass = reader["WeightClass"]?.ToString() ?? "Division";
            var winnerId = Convert.ToInt32(reader["WinnerId"]);

            items.Add(new WorldFeedItemVm(
                Bucket: "Eliminator",
                Headline: $"{winnerName} moves into the {weightClass} title picture",
                Summary: $"{winnerName} beat {loserName} via {method} and pushed deeper into the contender line in {promotion}.",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "warn",
                LinkHref: $"/fighters/{winnerId}"));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadAnnualShiftHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(CreatedDate, '') AS FeedDate,
    COALESCE(Subject, 'Annual world shift') AS Subject,
    COALESCE(Body, '') AS Body
FROM InboxMessages
WHERE MessageType = 'AnnualWorldShift'
  AND COALESCE(IsDeleted, 0) = 0
ORDER BY Id DESC
LIMIT 4;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new WorldFeedItemVm(
                Bucket: "Year Shift",
                Headline: reader["Subject"]?.ToString() ?? "Annual world shift",
                Summary: reader["Body"]?.ToString() ?? "",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "danger",
                LinkHref: "/inbox"));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadStorylinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(s.StoryType, 'Storyline') AS StoryType,
    COALESCE(s.Headline, 'World storyline') AS Headline,
    COALESCE(s.Body, '') AS Body,
    COALESCE(s.Intensity, 0) AS Intensity,
    s.EntityType,
    s.EntityId
FROM Storylines s
WHERE COALESCE(s.Status, 'Active') = 'Active'
ORDER BY COALESCE(s.Intensity, 0) DESC, s.Id DESC
LIMIT 12;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityType = reader["EntityType"]?.ToString() ?? "";
            var entityId = reader["EntityId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["EntityId"]);

            items.Add(new WorldFeedItemVm(
                Bucket: reader["StoryType"]?.ToString() ?? "Storyline",
                Headline: reader["Headline"]?.ToString() ?? "World storyline",
                Summary: reader["Body"]?.ToString() ?? "",
                Date: string.Empty,
                Tone: Convert.ToInt32(reader["Intensity"]) >= 80 ? "gold" : "neutral",
                LinkHref: entityType == "Fighter" && entityId > 0 ? $"/fighters/{entityId}" : null));
        }

        return items;
    }
}
