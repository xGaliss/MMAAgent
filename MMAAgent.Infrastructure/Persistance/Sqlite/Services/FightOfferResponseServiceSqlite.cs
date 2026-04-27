using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class FightOfferResponseServiceSqlite : IFightOfferResponseService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IInboxRepository _inboxRepository;

    public FightOfferResponseServiceSqlite(
        SqliteConnectionFactory factory,
        IAgentProfileRepository agentRepository,
        IInboxRepository inboxRepository)
    {
        _factory = factory;
        _agentRepository = agentRepository;
        _inboxRepository = inboxRepository;
    }

    public async Task<FightOfferResponseResult> AcceptOfferAsync(int offerId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return new FightOfferResponseResult(false, "No active agent found.", offerId, null, null);

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var offer = await LoadOfferAsync(conn, tx, offerId, cancellationToken);
        if (offer is null)
        {
            tx.Commit();
            return new FightOfferResponseResult(false, "Offer not found.", offerId, null, null);
        }

        if (offer.PromotionId is null)
        {
            tx.Commit();
            return new FightOfferResponseResult(false, "Offer has no promotion.", offerId, null, null);
        }

        var gameStateRepo = new SqliteGameStateRepository(_factory);
        var gameState = await gameStateRepo.GetAsync();
        if (gameState == null)
        {
            tx.Commit();
            return new FightOfferResponseResult(false, "Game state not found.", offerId, null, null);
        }

        var resolvedEventId = offer.EventId ?? await EnsureEventAsync(
            conn,
            tx,
            offer.PromotionId.Value,
            gameState.CurrentYear,
            gameState.CurrentWeek,
            gameState.CurrentDate,
            offer.WeeksUntilFight,
            cancellationToken);

        var eventDate = await GetEventDateAsync(conn, tx, resolvedEventId, cancellationToken);

        int fightId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO Fights
(
    FighterAId,
    FighterBId,
    WinnerId,
    Method,
    Round,
    EventDate,
    EventId,
    WeightClass,
    IsTitleFight,
    IsTitleEliminator,
    Purse,
    WinBonus,
    IsShortNotice
)
VALUES
(
    $fighterAId,
    $fighterBId,
    NULL,
    $method,
    NULL,
    $eventDate,
    $eventId,
    $weightClass,
    $isTitleFight,
    $isTitleEliminator,
    $purse,
    $winBonus,
    $isShortNotice
);";

            cmd.Parameters.AddWithValue("$fighterAId", offer.FighterId);
            cmd.Parameters.AddWithValue("$fighterBId", offer.OpponentFighterId);
            cmd.Parameters.AddWithValue("$method", "Scheduled");
            cmd.Parameters.AddWithValue("$eventDate", (object?)eventDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$eventId", resolvedEventId);
            cmd.Parameters.AddWithValue("$weightClass", offer.WeightClass ?? "");
            cmd.Parameters.AddWithValue("$isTitleFight", offer.IsTitleFight ? 1 : 0);
            cmd.Parameters.AddWithValue("$isTitleEliminator", offer.IsTitleEliminator ? 1 : 0);
            cmd.Parameters.AddWithValue("$purse", offer.Purse);
            cmd.Parameters.AddWithValue("$winBonus", offer.WinBonus);
            cmd.Parameters.AddWithValue("$isShortNotice", offer.IsShortNotice ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "SELECT last_insert_rowid();";
            fightId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Fighters
SET IsBooked = 1
WHERE Id IN ($fighterA, $fighterB);";
            cmd.Parameters.AddWithValue("$fighterA", offer.FighterId);
            cmd.Parameters.AddWithValue("$fighterB", offer.OpponentFighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE FightOffers
SET Status = 'Accepted',
    EventId = $eventId
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", offerId);
            cmd.Parameters.AddWithValue("$eventId", resolvedEventId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();

        await _inboxRepository.CreateAsync(new InboxMessage
        {
            AgentId = agent.Id,
            MessageType = "FightOfferResponse",
            Subject = offer.IsTitleFight
                ? "Title fight accepted"
                : offer.IsTitleEliminator
                    ? "Title eliminator accepted"
                    : "Fight offer accepted",
            Body = $"Accepted fight offer #{offerId}. The bout has been added to event #{resolvedEventId}."
                 + (offer.IsTitleEliminator ? " Next-contender stakes are attached to this booking." : string.Empty),
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IsRead = false
        });

        return new FightOfferResponseResult(true, "Offer accepted and fight created.", offerId, resolvedEventId, fightId);
    }

    public async Task<FightOfferResponseResult> RejectOfferAsync(int offerId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return new FightOfferResponseResult(false, "No active agent found.", offerId, null, null);

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE FightOffers SET Status = 'Rejected' WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", offerId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();

        await _inboxRepository.CreateAsync(new InboxMessage
        {
            AgentId = agent.Id,
            MessageType = "FightOfferResponse",
            Subject = "Fight offer rejected",
            Body = $"Rejected fight offer #{offerId}.",
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IsRead = false
        });

        return new FightOfferResponseResult(true, "Offer rejected.", offerId, null, null);
    }

    private static async Task<OfferSnapshot?> LoadOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int offerId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    Id,
    FighterId,
    OpponentFighterId,
    EventId,
    PromotionId,
    Purse,
    WinBonus,
    WeeksUntilFight,
    IsShortNotice,
    WeightClass,
    IsTitleFight,
    COALESCE(IsTitleEliminator, 0) AS IsTitleEliminator
FROM FightOffers
WHERE Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", offerId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new OfferSnapshot(
            Convert.ToInt32(r["Id"]),
            Convert.ToInt32(r["FighterId"]),
            Convert.ToInt32(r["OpponentFighterId"]),
            r["EventId"] == DBNull.Value ? null : Convert.ToInt32(r["EventId"]),
            r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
            Convert.ToInt32(r["Purse"]),
            Convert.ToInt32(r["WinBonus"]),
            Convert.ToInt32(r["WeeksUntilFight"]),
            Convert.ToInt32(r["IsShortNotice"]) == 1,
            r["WeightClass"]?.ToString() ?? "",
            Convert.ToInt32(r["IsTitleFight"]) == 1,
            Convert.ToInt32(r["IsTitleEliminator"]) == 1);
    }

    private static async Task<string?> GetEventDateAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int eventId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EventDate FROM Events WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", eventId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    private static async Task<int> EnsureEventAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int promotionId,
        int currentYear,
        int currentWeek,
        string currentDate,
        int weeksUntilFight,
        CancellationToken cancellationToken)
    {
        var promotion = await LoadPromotionSnapshotAsync(conn, tx, promotionId, cancellationToken);
        var currentAbsoluteWeek = ToAbsoluteWeek(currentYear, currentWeek);
        var targetAbsoluteWeek = ResolveScheduledEventWeek(
            currentAbsoluteWeek,
            weeksUntilFight,
            promotion?.NextEventWeek ?? 0,
            promotion?.EventIntervalWeeks ?? 1);

        var promoName = promotion?.Name ?? $"Promotion {promotionId}";
        var targetEventName = BuildEventName(promoName, targetAbsoluteWeek);

        var eventDate = ResolveEventDate(currentDate, currentAbsoluteWeek, targetAbsoluteWeek);

        using (var findCmd = conn.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = @"
SELECT Id
FROM Events
WHERE PromotionId = $p
  AND Name = $name
LIMIT 1;";
            findCmd.Parameters.AddWithValue("$p", promotionId);
            findCmd.Parameters.AddWithValue("$name", targetEventName);

            var existing = await findCmd.ExecuteScalarAsync(cancellationToken);
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt32(existing);
        }

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
INSERT INTO Events (PromotionId, EventDate, Name, Location)
VALUES ($p, $d, $n, $l);";
        insertCmd.Parameters.AddWithValue("$p", promotionId);
        insertCmd.Parameters.AddWithValue("$d", eventDate);
        insertCmd.Parameters.AddWithValue("$n", targetEventName);
        insertCmd.Parameters.AddWithValue("$l", "TBD");
        await insertCmd.ExecuteNonQueryAsync(cancellationToken);

        insertCmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(await insertCmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<PromotionSnapshot?> LoadPromotionSnapshotAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int promotionId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT Name, COALESCE(NextEventWeek, 0) AS NextEventWeek
     , COALESCE(EventIntervalWeeks, 1) AS EventIntervalWeeks
FROM Promotions
WHERE Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", promotionId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new PromotionSnapshot(
            r["Name"]?.ToString() ?? $"Promotion {promotionId}",
            Convert.ToInt32(r["NextEventWeek"]),
            Convert.ToInt32(r["EventIntervalWeeks"]));
    }

    private static string ResolveEventDate(string currentDate, int currentAbsoluteWeek, int targetAbsoluteWeek)
    {
        if (!DateTime.TryParse(currentDate, out var parsedCurrentDate))
            parsedCurrentDate = DateTime.UtcNow.Date;

        var weeksUntilEvent = Math.Max(1, targetAbsoluteWeek - currentAbsoluteWeek);
        return parsedCurrentDate
            .AddDays(weeksUntilEvent * 7)
            .ToString("yyyy-MM-dd");
    }

    private static string BuildEventName(string promotionName, int absoluteWeek)
        => $"{promotionName} Week {absoluteWeek}";

    private static int ResolveScheduledEventWeek(
        int currentAbsoluteWeek,
        int weeksUntilFight,
        int nextEventWeek,
        int eventIntervalWeeks)
    {
        var desiredWeek = currentAbsoluteWeek + Math.Max(1, weeksUntilFight);
        var intervalWeeks = Math.Max(1, eventIntervalWeeks);
        var resolvedWeek = nextEventWeek > currentAbsoluteWeek
            ? nextEventWeek
            : currentAbsoluteWeek + intervalWeeks;

        while (resolvedWeek < desiredWeek)
            resolvedWeek += intervalWeeks;

        return resolvedWeek;
    }

    private static int ToAbsoluteWeek(int year, int week)
        => Math.Max(1, (year - 1) * 52 + week);

    private sealed record OfferSnapshot(
        int Id,
        int FighterId,
        int OpponentFighterId,
        int? EventId,
        int? PromotionId,
        int Purse,
        int WinBonus,
        int WeeksUntilFight,
        bool IsShortNotice,
        string WeightClass,
        bool IsTitleFight,
        bool IsTitleEliminator);

    private sealed record PromotionSnapshot(
        string Name,
        int NextEventWeek,
        int EventIntervalWeeks);
}
