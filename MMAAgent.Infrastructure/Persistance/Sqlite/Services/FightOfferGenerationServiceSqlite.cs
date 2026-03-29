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
        if (agent == null || gameState == null) return 0;

        var inboxMessages = new List<InboxMessage>();

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var absoluteWeek = await LoadAbsoluteWeekAsync(conn, tx, cancellationToken);
        var upcomingEvents = await LoadUpcomingPromotionsAsync(conn, tx, absoluteWeek, cancellationToken);
        var managedFighters = await LoadAvailableManagedFightersAsync(conn, tx, agent.Id, cancellationToken);

        var offersCreated = 0;

        foreach (var fighter in managedFighters)
        {
            // ✅ CAMBIO CLAVE:
            // si no tiene promotora o no tiene peleas de contrato, no se le agenda pelea
            if (fighter.PromotionId is null)
                continue;

            if (fighter.ContractFightsRemaining <= 0)
                continue;

            if (await HasPendingOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            if (await HasPendingContractOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            if (fighter.WeeksUntilAvailable > 0 || fighter.InjuryWeeksRemaining > 0)
                continue;

            var ev = upcomingEvents.FirstOrDefault(x => x.PromotionId == fighter.PromotionId.Value);
            if (ev is null)
                continue;

            if (ev.EventWeek - absoluteWeek < 1)
                continue;

            var candidates = await FindOpponentCandidatesAsync(conn, tx, fighter, ev.PromotionId, cancellationToken);

            OpponentSnapshot? opponent = null;
            foreach (var candidate in candidates)
            {
                if (await HaveRecentRematchAsync(conn, tx, fighter.FighterId, candidate.FighterId, cancellationToken))
                    continue;

                if (!PassesCampAcceptance(fighter, candidate, ev.EventWeek - absoluteWeek))
                    continue;

                opponent = candidate;
                break;
            }

            if (opponent is null)
                continue;

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
    NULL,
    $promotionId,
    $weightClass
);";
                cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
                cmd.Parameters.AddWithValue("$opponentId", opponent.FighterId);
                cmd.Parameters.AddWithValue("$purse", 5000 + fighter.Skill * 100);
                cmd.Parameters.AddWithValue("$winBonus", 2000 + fighter.Popularity * 50);
                cmd.Parameters.AddWithValue("$weeksUntilFight", Math.Max(1, ev.EventWeek - absoluteWeek));
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
            await _inboxRepository.CreateAsync(msg);

        return offersCreated;
    }

    private static bool PassesCampAcceptance(ManagedAvailability fighter, OpponentSnapshot opponent, int weeksUntilFight)
    {
        if (weeksUntilFight < 2) return false;
        if (fighter.ContractFightsRemaining <= 0) return false;
        if (fighter.ContractFightsRemaining == 1 && opponent.Skill > fighter.Skill + 8) return false;
        if (fighter.InjuryWeeksRemaining > 0) return false;
        return true;
    }

    private static async Task<bool> HaveRecentRematchAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        int opponentId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT COUNT(*) FROM
(
    SELECT Id
    FROM FightHistory
    WHERE ((FighterAId = $a AND FighterBId = $b) OR (FighterAId = $b AND FighterBId = $a))
    ORDER BY Id DESC
    LIMIT 2
) t;";
        cmd.Parameters.AddWithValue("$a", fighterId);
        cmd.Parameters.AddWithValue("$b", opponentId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) >= 2;
    }

    private static async Task<int> LoadAbsoluteWeekAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(AbsoluteWeek, 1) FROM GameState LIMIT 1;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> HasPendingOfferAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM FightOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasPendingContractOfferAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM ContractOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<List<UpcomingEvent>> LoadUpcomingPromotionsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int absoluteWeek,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    Id AS PromotionId,
    Name,
    NextEventWeek
FROM Promotions
WHERE IsActive = 1
  AND NextEventWeek >= $minWeek
  AND NextEventWeek <= $maxWeek
ORDER BY NextEventWeek;";
        cmd.Parameters.AddWithValue("$minWeek", absoluteWeek + 1);
        cmd.Parameters.AddWithValue("$maxWeek", absoluteWeek + 10);

        var list = new List<UpcomingEvent>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var promotionId = Convert.ToInt32(r["PromotionId"]);
            var promoName = r["Name"]?.ToString() ?? $"Promotion {promotionId}";
            var nextEventWeek = Convert.ToInt32(r["NextEventWeek"]);
            list.Add(new UpcomingEvent(0, $"{promoName} Week {nextEventWeek}", promotionId, nextEventWeek));
        }

        return list;
    }

    private static async Task<List<ManagedAvailability>> LoadAvailableManagedFightersAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        CancellationToken cancellationToken)
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
    f.PromotionId,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.ContractFightsRemaining, 0) AS ContractFightsRemaining
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(f.IsBooked, 0) = 0
ORDER BY f.Popularity DESC, f.Skill DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var list = new List<ManagedAvailability>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ManagedAvailability(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                r["WeightClass"]?.ToString() ?? "",
                Convert.ToInt32(r["Skill"]),
                Convert.ToInt32(r["Popularity"]),
                r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
                Convert.ToInt32(r["WeeksUntilAvailable"]),
                Convert.ToInt32(r["InjuryWeeksRemaining"]),
                Convert.ToInt32(r["ContractFightsRemaining"])));
        }

        return list;
    }

    private static async Task<List<OpponentSnapshot>> FindOpponentCandidatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        int promotionId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill
FROM Fighters f
WHERE f.Id <> $fighterId
  AND f.WeightClass = $weightClass
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) = 0
  AND COALESCE(f.InjuryWeeksRemaining, 0) = 0
  AND f.PromotionId = $promotionId
ORDER BY ABS(f.Skill - $skill), ABS(f.Popularity - $popularity)
LIMIT 12;";
        cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
        cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$skill", fighter.Skill);
        cmd.Parameters.AddWithValue("$popularity", fighter.Popularity);

        var list = new List<OpponentSnapshot>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new OpponentSnapshot(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                Convert.ToInt32(r["Skill"])));
        }

        return list;
    }

    private sealed record UpcomingEvent(int EventId, string EventName, int PromotionId, int EventWeek);

    private sealed record ManagedAvailability(
        int FighterId,
        string Name,
        string WeightClass,
        int Skill,
        int Popularity,
        int? PromotionId,
        int WeeksUntilAvailable,
        int InjuryWeeksRemaining,
        int ContractFightsRemaining);

    private sealed record OpponentSnapshot(int FighterId, string Name, int Skill);
}
