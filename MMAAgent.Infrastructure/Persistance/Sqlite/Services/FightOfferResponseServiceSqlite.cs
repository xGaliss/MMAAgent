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
            gameState.CurrentWeek,
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
    IsTitleFight
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
    $isTitleFight
);";

            cmd.Parameters.AddWithValue("$fighterAId", offer.FighterId);
            cmd.Parameters.AddWithValue("$fighterBId", offer.OpponentFighterId);
            cmd.Parameters.AddWithValue("$method", "Scheduled");
            cmd.Parameters.AddWithValue("$eventDate", (object?)eventDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$eventId", resolvedEventId);
            cmd.Parameters.AddWithValue("$weightClass", offer.WeightClass ?? "");
            cmd.Parameters.AddWithValue("$isTitleFight", offer.IsTitleFight ? 1 : 0);

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
            Subject = "Fight offer accepted",
            Body = $"Accepted fight offer #{offerId}. The bout has been added to event #{resolvedEventId}.",
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
    WeeksUntilFight,
    WeightClass,
    IsTitleFight
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
            Convert.ToInt32(r["WeeksUntilFight"]),
            r["WeightClass"]?.ToString() ?? "",
            Convert.ToInt32(r["IsTitleFight"]) == 1);
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
        int currentWeek,
        int weeksUntilFight,
        CancellationToken cancellationToken)
    {
        var targetWeek = currentWeek + Math.Max(1, weeksUntilFight);

        using var promoCmd = conn.CreateCommand();
        promoCmd.Transaction = tx;
        promoCmd.CommandText = "SELECT Name FROM Promotions WHERE Id = $id LIMIT 1;";
        promoCmd.Parameters.AddWithValue("$id", promotionId);
        var promoName = (await promoCmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? $"Promotion {promotionId}";

        var targetEventName = $"{promoName} Week {targetWeek}";

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

        var eventDate = DateTime.UtcNow
            .AddDays(Math.Max(7, weeksUntilFight * 7))
            .ToString("yyyy-MM-dd");

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

    private sealed record OfferSnapshot(
        int Id,
        int FighterId,
        int OpponentFighterId,
        int? EventId,
        int? PromotionId,
        int WeeksUntilFight,
        string WeightClass,
        bool IsTitleFight);
}