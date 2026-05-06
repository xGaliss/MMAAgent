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
        headlines.AddRange(await LoadShortNoticeHeadlinesAsync(conn));
        headlines.AddRange(await LoadWeightMissHeadlinesAsync(conn));
        headlines.AddRange(await LoadRetirementHeadlinesAsync(conn));
        headlines.AddRange(await LoadProspectHeadlinesAsync(conn));
        headlines.AddRange(await LoadProspectAlertHeadlinesAsync(conn));
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

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadShortNoticeHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(f.EventDate, '') AS FeedDate,
    (fa.FirstName || ' ' || fa.LastName) AS FighterAName,
    (fb.FirstName || ' ' || fb.LastName) AS FighterBName,
    COALESCE(f.Method, 'DEC') AS Method,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(e.Name, '') AS EventName,
    f.WinnerId
FROM Fights f
JOIN Fighters fa ON fa.Id = f.FighterAId
JOIN Fighters fb ON fb.Id = f.FighterBId
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = e.PromotionId
WHERE COALESCE(f.IsShortNotice, 0) = 1
  AND COALESCE(f.Method, 'Scheduled') <> 'Scheduled'
ORDER BY f.Id DESC
LIMIT 6;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fighterA = reader["FighterAName"]?.ToString() ?? "Fighter A";
            var fighterB = reader["FighterBName"]?.ToString() ?? "Fighter B";
            var eventName = reader["EventName"]?.ToString();
            var promotion = reader["PromotionName"]?.ToString() ?? "Promotion";
            var method = reader["Method"]?.ToString() ?? "DEC";
            var winnerId = reader["WinnerId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["WinnerId"]);

            items.Add(new WorldFeedItemVm(
                Bucket: "Short Notice",
                Headline: $"{fighterA} vs {fighterB} held together on short notice",
                Summary: string.IsNullOrWhiteSpace(eventName)
                    ? $"The bout still happened in {promotion} and ended via {method}."
                    : $"The matchup survived late chaos at {eventName} ({promotion}) and still ended via {method}.",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "warn",
                LinkHref: winnerId > 0 ? $"/fighters/{winnerId}" : null));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadWeightMissHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(f.EventDate, '') AS FeedDate,
    (fa.FirstName || ' ' || fa.LastName) AS FighterAName,
    (fb.FirstName || ' ' || fb.LastName) AS FighterBName,
    COALESCE(f.WeightClass, 'Division') AS WeightClass,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(e.Name, '') AS EventName,
    COALESCE(fpa.WeighInOutcome, '') AS FighterAWeighInOutcome,
    COALESCE(fpb.WeighInOutcome, '') AS FighterBWeighInOutcome
FROM Fights f
JOIN Fighters fa ON fa.Id = f.FighterAId
JOIN Fighters fb ON fb.Id = f.FighterBId
LEFT JOIN Events e ON e.Id = f.EventId
LEFT JOIN Promotions p ON p.Id = e.PromotionId
LEFT JOIN FightPreparations fpa ON fpa.FightId = f.Id AND fpa.FighterId = f.FighterAId
LEFT JOIN FightPreparations fpb ON fpb.FightId = f.Id AND fpb.FighterId = f.FighterBId
WHERE COALESCE(f.Method, 'Scheduled') <> 'Scheduled'
  AND (
       COALESCE(fpa.WeighInOutcome, '') = 'MissedWeight'
    OR COALESCE(fpb.WeighInOutcome, '') = 'MissedWeight'
  )
ORDER BY f.Id DESC
LIMIT 6;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fighterA = reader["FighterAName"]?.ToString() ?? "Fighter A";
            var fighterB = reader["FighterBName"]?.ToString() ?? "Fighter B";
            var culprit = string.Equals(reader["FighterAWeighInOutcome"]?.ToString(), "MissedWeight", StringComparison.OrdinalIgnoreCase)
                ? fighterA
                : fighterB;
            var eventName = reader["EventName"]?.ToString();
            var promotion = reader["PromotionName"]?.ToString() ?? "Promotion";
            var weightClass = reader["WeightClass"]?.ToString() ?? "Division";

            items.Add(new WorldFeedItemVm(
                Bucket: "Weight Miss",
                Headline: $"{culprit} missed weight in {weightClass}",
                Summary: string.IsNullOrWhiteSpace(eventName)
                    ? $"The cut went wrong in {promotion}, adding pressure around future bookings."
                    : $"The cut went wrong at {eventName} ({promotion}), adding pressure around future bookings.",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "danger",
                LinkHref: null));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadRetirementHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    (FirstName || ' ' || LastName) AS FighterName,
    COALESCE(WeightClass, 'Division') AS WeightClass,
    COALESCE(Popularity, 0) AS Popularity,
    Id
FROM Fighters
WHERE COALESCE(Retired, 0) = 1
ORDER BY COALESCE(Popularity, 0) DESC, COALESCE(Wins, 0) DESC, Id DESC
LIMIT 4;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fighterName = reader["FighterName"]?.ToString() ?? "Retired fighter";
            var weightClass = reader["WeightClass"]?.ToString() ?? "Division";
            var fighterId = Convert.ToInt32(reader["Id"]);

            items.Add(new WorldFeedItemVm(
                Bucket: "Retirement",
                Headline: $"{fighterName} has stepped away",
                Summary: $"{fighterName} is now out of the active picture at {weightClass}, leaving space for the division to shift.",
                Date: string.Empty,
                Tone: "neutral",
                LinkHref: $"/fighters/{fighterId}"));
        }

        return items;
    }

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadProspectHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(s.Headline, 'Prospect surge') AS Headline,
    COALESCE(s.Body, '') AS Body,
    s.EntityId
FROM Storylines s
WHERE COALESCE(s.Status, 'Active') = 'Active'
  AND s.StoryType IN ('ProspectSurge', 'LateCareerRun')
ORDER BY COALESCE(s.Intensity, 0) DESC, s.Id DESC
LIMIT 6;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityId = reader["EntityId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["EntityId"]);
            items.Add(new WorldFeedItemVm(
                Bucket: "Prospect Watch",
                Headline: reader["Headline"]?.ToString() ?? "Prospect surge",
                Summary: reader["Body"]?.ToString() ?? "",
                Date: string.Empty,
                Tone: "gold",
                LinkHref: entityId > 0 ? $"/fighters/{entityId}" : null));
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

    private static async Task<IReadOnlyList<WorldFeedItemVm>> LoadProspectAlertHeadlinesAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(CreatedDate, '') AS FeedDate,
    COALESCE(Subject, 'Prospect movement') AS Subject,
    COALESCE(Body, '') AS Body
FROM InboxMessages
WHERE MessageType = 'ProspectWatchAlert'
  AND COALESCE(IsDeleted, 0) = 0
ORDER BY Id DESC
LIMIT 6;";

        var items = new List<WorldFeedItemVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new WorldFeedItemVm(
                Bucket: "Prospect Alert",
                Headline: reader["Subject"]?.ToString() ?? "Prospect movement",
                Summary: reader["Body"]?.ToString() ?? "",
                Date: reader["FeedDate"]?.ToString() ?? "",
                Tone: "gold",
                LinkHref: "/prospects"));
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
