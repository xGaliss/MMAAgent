using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class FightOfferGenerationServiceSqlite : IFightOfferGenerationService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IGameStateRepository _gameStateRepository;
    private readonly IInboxRepository _inboxRepository;

    public FightOfferGenerationServiceSqlite(
        SqliteConnectionFactory factory,
        IAgentProfileRepository agentRepository,
        IGameStateRepository gameStateRepository,
        IInboxRepository inboxRepository)
    {
        _factory = factory;
        _agentRepository = agentRepository;
        _gameStateRepository = gameStateRepository;
        _inboxRepository = inboxRepository;
    }

    public async Task<int> GenerateWeeklyOffersAsync(CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        var gameState = await _gameStateRepository.GetAsync();
        if (agent == null || gameState == null)
            return 0;

        var inboxMessages = new List<InboxMessage>();

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var upcomingEvents = await LoadUpcomingEventsAsync(
    conn,
    tx,
    gameState.CurrentWeek,
    cancellationToken);
        var managedFighters = await LoadAvailableManagedFightersAsync(conn, tx, agent.Id, gameState.CurrentWeek, cancellationToken);

        var offersCreated = 0;

        foreach (var fighter in managedFighters)
        {
            var ev = upcomingEvents.FirstOrDefault(x =>
    fighter.PromotionId == null || x.PromotionId == fighter.PromotionId.Value);
            if (ev is null)
                break;

            if (await HasPendingOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            var opponent = await FindOpponentAsync(conn, tx, fighter, ev.PromotionId, cancellationToken);
            if (opponent is null)
                continue;

            var weeksUntilFight = Math.Max(1, ev.EventWeek - gameState.CurrentWeek);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO FightOffers
(
    FighterId,
    OpponentFighterId,
    Purse,
    WinBonus,
    WeeksUntilFight,
    IsTitleFight,
    Status,
    EventId,
    PromotionId,
    WeightClass
)
VALUES
(
    $fighterId,
    $opponentId,
    $purse,
    $winBonus,
    $weeksUntilFight,
    0,
    'Pending',
    $eventId,
    $promotionId,
    $weightClass
);";

                cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
                cmd.Parameters.AddWithValue("$opponentId", opponent.FighterId);
                cmd.Parameters.AddWithValue("$purse", 5000 + fighter.Skill * 100);
                cmd.Parameters.AddWithValue("$winBonus", 2000 + fighter.Popularity * 50);
                cmd.Parameters.AddWithValue("$weeksUntilFight", weeksUntilFight);
                cmd.Parameters.AddWithValue("$eventId", DBNull.Value);
                cmd.Parameters.AddWithValue("$promotionId", ev.PromotionId);
                cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            inboxMessages.Add(new InboxMessage
            {
                AgentId = agent.Id,
                MessageType = "FightOffer",
                Subject = $"Fight offer for {fighter.Name}",
                Body = $"{fighter.Name} has been offered a fight vs {opponent.Name} at {ev.EventName}.",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            });

            offersCreated++;
        }

        tx.Commit();

        foreach (var msg in inboxMessages)
        {
            await _inboxRepository.CreateAsync(msg);
        }

        return offersCreated;
    }

    private static async Task<bool> HasPendingOfferAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM FightOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<List<UpcomingEvent>> LoadUpcomingEventsAsync(
    SqliteConnection conn,
    SqliteTransaction tx,
    int currentWeek,
    CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = @"
SELECT
    Id AS PromotionId,
    Name,
    NextEventWeek,
    EventIntervalWeeks
FROM Promotions
WHERE IsActive = 1
  AND NextEventWeek >= $minWeek
  AND NextEventWeek <= $maxWeek
ORDER BY NextEventWeek;";

        cmd.Parameters.AddWithValue("$minWeek", currentWeek + 1);
        cmd.Parameters.AddWithValue("$maxWeek", currentWeek + 6);

        var list = new List<UpcomingEvent>();

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var promotionId = Convert.ToInt32(r["PromotionId"]);
            var promoName = r["Name"]?.ToString() ?? $"Promotion {promotionId}";
            var nextEventWeek = Convert.ToInt32(r["NextEventWeek"]);

            list.Add(new UpcomingEvent(
                EventId: 0, // todavía no existe en Events
                EventName: $"{promoName} Week {nextEventWeek}",
                PromotionId: promotionId,
                EventWeek: nextEventWeek));
        }

        return list;
    }

    private static async Task<List<ManagedAvailability>> LoadAvailableManagedFightersAsync(SqliteConnection conn, SqliteTransaction tx, int agentId, int currentWeek, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.WeightClass,
    f.Skill,
    f.Popularity,
    f.PromotionId
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(f.IsInjured, 0) = 0
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.AvailableFromWeek, 0) <= $currentWeek
ORDER BY f.Popularity DESC, f.Skill DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$currentWeek", currentWeek);

        var list = new List<ManagedAvailability>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
            list.Add(new ManagedAvailability(
    Convert.ToInt32(r["FighterId"]),
    r["FighterName"]?.ToString() ?? "",
    r["WeightClass"]?.ToString() ?? "",
    Convert.ToInt32(r["Skill"]),
    Convert.ToInt32(r["Popularity"]),
    r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"])));
        return list;
    }

    private static async Task<OpponentSnapshot?> FindOpponentAsync(SqliteConnection conn, SqliteTransaction tx, ManagedAvailability fighter, int promotionId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT f.Id AS FighterId, (f.FirstName || ' ' || f.LastName) AS FighterName
FROM Fighters f
WHERE f.Id <> $fighterId
  AND f.WeightClass = $weightClass
  AND COALESCE(f.IsInjured, 0) = 0
  AND COALESCE(f.IsBooked, 0) = 0
  AND f.PromotionId = $promotionId
ORDER BY ABS(f.Skill - $skill), ABS(f.Popularity - $popularity)
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
        cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$skill", fighter.Skill);
        cmd.Parameters.AddWithValue("$popularity", fighter.Popularity);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new OpponentSnapshot(Convert.ToInt32(r["FighterId"]), r["FighterName"]?.ToString() ?? "");
    }

    private sealed record UpcomingEvent(int EventId, string EventName, int PromotionId, int EventWeek);
    private sealed record ManagedAvailability(
    int FighterId,
    string Name,
    string WeightClass,
    int Skill,
    int Popularity,
    int? PromotionId);
    private sealed record OpponentSnapshot(int FighterId, string Name);
}
