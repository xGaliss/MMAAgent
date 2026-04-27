using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using System.Linq;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class FightOfferGenerationServiceSqlite : IFightOfferGenerationService
{
    private const int MinimumOfferLeadWeeks = 2;
    private const double ShortNoticeChance = 0.18;
    private const double ShortNoticePurseMultiplier = 1.35;
    private const double ShortNoticeWinBonusMultiplier = 1.20;

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
        var promotions = await LoadPromotionSnapshotsAsync(conn, tx, cancellationToken);
        var managedFighters = await LoadAvailableManagedFightersAsync(conn, tx, agent.Id, gameState.CurrentDate, cancellationToken);

        var offersCreated = 0;

        foreach (var fighter in managedFighters)
        {
            if (fighter.PromotionId is null)
                continue;

            if (fighter.ContractFightsRemaining <= 0)
                continue;

            if (await HasPendingOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            if (await HasPendingContractOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            var promotion = promotions.FirstOrDefault(x => x.PromotionId == fighter.PromotionId.Value);
            if (promotion is null)
                continue;

            var offerPlan = await ResolveOfferPlanAsync(
                conn,
                tx,
                fighter,
                promotion,
                absoluteWeek,
                cancellationToken);
            if (offerPlan is null)
                continue;
            var created = await TryInsertOfferAsync(conn, tx, fighter, promotion, offerPlan, agent.Id, cancellationToken);
            if (created is null)
                continue;

            inboxMessages.Add(created.InboxMessage);
            offersCreated++;
        }

        tx.Commit();

        foreach (var msg in inboxMessages)
            await _inboxRepository.CreateAsync(msg);

        return offersCreated;
    }

    public async Task<ServiceResult> GenerateOfferForManagedFighterAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        var gameState = await _gameStateRepository.GetAsync();
        if (agent == null || gameState == null)
            return ServiceResult.Fail("No active game session found.");

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var absoluteWeek = await LoadAbsoluteWeekAsync(conn, tx, cancellationToken);
        var promotions = await LoadPromotionSnapshotsAsync(conn, tx, cancellationToken);
        var fighter = (await LoadAvailableManagedFightersAsync(conn, tx, agent.Id, gameState.CurrentDate, cancellationToken))
            .FirstOrDefault(x => x.FighterId == fighterId);

        if (fighter is null)
            return ServiceResult.Fail("That fighter is not ready for a matchmaking push right now.");

        if (fighter.PromotionId is null)
            return ServiceResult.Fail("That fighter is a free agent. Try pitching them to a promotion first.");

        if (fighter.ContractFightsRemaining <= 0)
            return ServiceResult.Fail("That fighter does not have fights left on the current contract.");

        if (await HasPendingOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
            return ServiceResult.Fail("That fighter already has a pending fight offer.");

        if (await HasPendingContractOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
            return ServiceResult.Fail("Resolve the pending contract situation before asking for a fight.");

        var promotion = promotions.FirstOrDefault(x => x.PromotionId == fighter.PromotionId.Value);
        if (promotion is null)
            return ServiceResult.Fail("The fighter's promotion could not be resolved.");

        var offerPlan = await ResolveOfferPlanAsync(conn, tx, fighter, promotion, absoluteWeek, cancellationToken);
        if (offerPlan is null)
            return ServiceResult.Fail("No sensible matchup is available right now for that fighter.");

        var created = await TryInsertOfferAsync(conn, tx, fighter, promotion, offerPlan, agent.Id, cancellationToken);
        if (created is null)
            return ServiceResult.Fail("No sensible matchup is available right now for that fighter.");

        tx.Commit();
        await _inboxRepository.CreateAsync(created.InboxMessage);

        return ServiceResult.Ok(created.UserMessage);
    }

    private static bool PassesCampAcceptance(ManagedAvailability fighter, MatchCandidate opponent, OfferPlan plan)
    {
        if (plan.WeeksUntilFight < Math.Max(1, plan.CampWeeksGranted)) return false;
        if (fighter.ContractFightsRemaining <= 0) return false;
        if (fighter.ContractFightsRemaining == 1 && opponent.Skill > fighter.Skill + 8) return false;
        return true;
    }

    private static bool CanStartCampByDeadline(ManagedAvailability fighter, int availabilityDeadlineWeeks)
        => availabilityDeadlineWeeks >= 0
           && fighter.WeeksUntilAvailable <= availabilityDeadlineWeeks
           && fighter.InjuryWeeksRemaining <= availabilityDeadlineWeeks
           && fighter.MedicalSuspensionWeeksRemaining <= availabilityDeadlineWeeks;

    private static int ComputeOfferMoney(int baseAmount, double multiplier)
        => Math.Max(0, (int)Math.Round(baseAmount * multiplier, MidpointRounding.AwayFromZero));

    private static async Task<CreatedOffer?> TryInsertOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        PromotionSnapshot promotion,
        OfferPlan offerPlan,
        int agentId,
        CancellationToken cancellationToken)
    {
        var opponent = offerPlan.Opponent;
        if (opponent is null)
            return null;

        var eventName = $"{promotion.Name} Week {offerPlan.EventWeek}";
        var purse = ComputeOfferMoney(5000 + fighter.Skill * 100, offerPlan.PurseMultiplier);
        var winBonus = ComputeOfferMoney(2000 + fighter.Popularity * 50, offerPlan.WinBonusMultiplier);

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
    IsTitleEliminator,
    IsShortNotice,
    CampWeeksOffered,
    Notes,
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
    $isTitleFight,
    $isTitleEliminator,
    $isShortNotice,
    $campWeeksOffered,
    $notes,
    'Pending',
    NULL,
    $promotionId,
    $weightClass
);";
            cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
            cmd.Parameters.AddWithValue("$opponentId", opponent.FighterId);
            cmd.Parameters.AddWithValue("$purse", purse);
            cmd.Parameters.AddWithValue("$winBonus", winBonus);
            cmd.Parameters.AddWithValue("$weeksUntilFight", offerPlan.WeeksUntilFight);
            cmd.Parameters.AddWithValue("$isTitleFight", opponent.IsTitleFight ? 1 : 0);
            cmd.Parameters.AddWithValue("$isTitleEliminator", opponent.IsTitleEliminator ? 1 : 0);
            cmd.Parameters.AddWithValue("$isShortNotice", offerPlan.IsShortNotice ? 1 : 0);
            cmd.Parameters.AddWithValue("$campWeeksOffered", offerPlan.CampWeeksGranted);
            cmd.Parameters.AddWithValue("$notes", (object?)BuildOfferNotes(opponent, offerPlan) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$promotionId", promotion.PromotionId);
            cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var subject = opponent.IsTitleFight
            ? $"Title fight offer for {fighter.Name}"
            : opponent.IsTitleEliminator
                ? $"Title eliminator offer for {fighter.Name}"
                : offerPlan.IsShortNotice
                    ? $"Short-notice fight offer for {fighter.Name}"
                    : opponent.RivalryIntensity >= 58
                        ? $"Rivalry fight offer for {fighter.Name}"
                        : $"Fight offer for {fighter.Name}";

        var body = opponent.IsTitleFight
            ? $"{fighter.Name} has been offered a title fight vs {opponent.Name} at {eventName}."
            : opponent.IsTitleEliminator
                ? $"{fighter.Name} has been offered a title eliminator vs {opponent.Name} at {eventName}. The winner should move into next-contender territory."
                : offerPlan.IsShortNotice
                    ? $"{fighter.Name} has been offered a short-notice fight vs {opponent.Name} at {eventName}. Purse and bonus have been increased to reflect the short camp."
                    : opponent.RivalryIntensity >= 58
                        ? $"{fighter.Name} has been offered a rivalry fight vs {opponent.Name} at {eventName}. There is real history between both sides."
                        : $"{fighter.Name} has been offered a fight vs {opponent.Name} at {eventName}.";

        var userMessage = offerPlan.IsShortNotice
            ? $"{fighter.Name} received a short-notice fight offer for {eventName}."
            : opponent.IsTitleFight
                ? $"{fighter.Name} received a title fight offer for {eventName}."
                : opponent.IsTitleEliminator
                    ? $"{fighter.Name} received a title eliminator offer for {eventName}."
                    : $"{fighter.Name} received a new fight offer for {eventName}.";

        return new CreatedOffer(
            new InboxMessage
            {
                AgentId = agentId,
                MessageType = offerPlan.IsShortNotice ? "FightOfferShortNotice" : "FightOffer",
                Subject = subject,
                Body = body,
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            },
            userMessage);
    }

    private static string? BuildOfferNotes(MatchCandidate opponent, OfferPlan plan)
    {
        var notes = new List<string>();

        if (opponent.IsTitleEliminator)
            notes.Add("Title eliminator with real next-contender stakes.");

        if (opponent.RivalryIntensity >= 58)
            notes.Add("There is active rivalry heat around this matchup.");

        if (plan.IsShortNotice)
            notes.Add("Short-notice terms applied to keep the card moving.");

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    private static bool ShouldMarkTitleEliminator(ManagedAvailability fighter, MatchCandidate opponent)
    {
        if (fighter.IsChampion || opponent.IsTitleFight)
            return false;

        if (fighter.ContenderQueueRank is <= 0 or null)
            return false;

        if (opponent.ContenderQueueRank is <= 0 or null)
            return false;

        return fighter.ContenderQueueRank <= 2 && opponent.ContenderQueueRank <= 4;
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

    private static int ResolveRequiredCampWeeks(PromotionSnapshot promotion, ManagedAvailability fighter)
    {
        if (fighter.IsChampion || fighter.RankPosition is > 0 and <= 3 || fighter.ContenderQueueRank is > 0 and <= 3)
            return Math.Max(promotion.MajorCampWeeks, promotion.TitleCampWeeks);

        return promotion.StandardCampWeeks;
    }

    private static async Task<List<PromotionSnapshot>> LoadPromotionSnapshotsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    Id AS PromotionId,
    Name,
    COALESCE(NextEventWeek, 0) AS NextEventWeek,
    COALESCE(EventIntervalWeeks, 1) AS EventIntervalWeeks,
    COALESCE(StandardCampWeeks, 4) AS StandardCampWeeks,
    COALESCE(MajorCampWeeks, 6) AS MajorCampWeeks,
    COALESCE(TitleCampWeeks, 8) AS TitleCampWeeks,
    COALESCE(ShortNoticeCampWeeks, 1) AS ShortNoticeCampWeeks,
    COALESCE(ShortNoticeMaxLeadWeeks, 2) AS ShortNoticeMaxLeadWeeks
FROM Promotions
WHERE IsActive = 1
ORDER BY COALESCE(NextEventWeek, 999999), Id;";

        var list = new List<PromotionSnapshot>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new PromotionSnapshot(
                Convert.ToInt32(r["PromotionId"]),
                r["Name"]?.ToString() ?? "",
                Convert.ToInt32(r["NextEventWeek"]),
                Math.Max(1, Convert.ToInt32(r["EventIntervalWeeks"])),
                Math.Max(MinimumOfferLeadWeeks, Convert.ToInt32(r["StandardCampWeeks"])),
                Math.Max(MinimumOfferLeadWeeks, Convert.ToInt32(r["MajorCampWeeks"])),
                Math.Max(MinimumOfferLeadWeeks, Convert.ToInt32(r["TitleCampWeeks"])),
                Math.Max(1, Convert.ToInt32(r["ShortNoticeCampWeeks"])),
                Math.Max(1, Convert.ToInt32(r["ShortNoticeMaxLeadWeeks"]))));
        }

        return list;
    }

    private static int ResolveOfferEventWeek(int absoluteWeek, int nextEventWeek, int intervalWeeks, int minimumLeadWeeks)
    {
        var desiredWeek = absoluteWeek + Math.Max(MinimumOfferLeadWeeks, minimumLeadWeeks);
        var resolvedNextWeek = nextEventWeek > absoluteWeek
            ? nextEventWeek
            : absoluteWeek + intervalWeeks;

        while (resolvedNextWeek < desiredWeek)
            resolvedNextWeek += intervalWeeks;

        return resolvedNextWeek;
    }

    private static async Task<List<ManagedAvailability>> LoadAvailableManagedFightersAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string currentDate,
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
    pr.RankPosition,
    cq.QueueRank,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM Titles t
            WHERE t.PromotionId = f.PromotionId
              AND t.WeightClass = f.WeightClass
              AND t.ChampionFighterId = f.Id
        ) THEN 1
        ELSE 0
    END AS IsChampion,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.MedicalSuspensionWeeksRemaining, 0) AS MedicalSuspensionWeeksRemaining,
    COALESCE(f.ContractFightsRemaining, 0) AS ContractFightsRemaining
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
LEFT JOIN PromotionRankings pr
    ON pr.FighterId = f.Id
   AND pr.PromotionId = f.PromotionId
   AND pr.WeightClass = f.WeightClass
LEFT JOIN ContenderQueue cq
    ON cq.FighterId = f.Id
   AND cq.PromotionId = f.PromotionId
   AND cq.WeightClass = f.WeightClass
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND COALESCE(f.IsBooked, 0) = 0
  AND NOT EXISTS (
      SELECT 1
      FROM Fights sf
      WHERE sf.Method = 'Scheduled'
        AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
        AND COALESCE(sf.EventDate, '9999-12-31') > $currentDate
  )
  AND NOT EXISTS (
      SELECT 1
      FROM FightHistory fh
      WHERE fh.FightDate = $currentDate
        AND (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
  )
ORDER BY f.Popularity DESC, f.Skill DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

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
                r["RankPosition"] == DBNull.Value ? null : Convert.ToInt32(r["RankPosition"]),
                r["QueueRank"] == DBNull.Value ? null : Convert.ToInt32(r["QueueRank"]),
                Convert.ToInt32(r["IsChampion"]) == 1,
                Convert.ToInt32(r["WeeksUntilAvailable"]),
                Convert.ToInt32(r["InjuryWeeksRemaining"]),
                Convert.ToInt32(r["MedicalSuspensionWeeksRemaining"]),
                Convert.ToInt32(r["ContractFightsRemaining"])));
        }

        return list;
    }

    private static async Task<List<MatchCandidate>> FindOpponentCandidatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        int promotionId,
        int weeksUntilFight,
        int campWeeksGranted,
        bool includeTitleFights,
        CancellationToken cancellationToken)
    {
        var availabilityDeadlineWeeks = weeksUntilFight - campWeeksGranted;
        if (availabilityDeadlineWeeks < 0)
            return new List<MatchCandidate>();

        if (includeTitleFights)
        {
            var titleCandidates = await LoadTitleFightCandidatesAsync(
                conn,
                tx,
                fighter,
                promotionId,
                availabilityDeadlineWeeks,
                cancellationToken);

            if (titleCandidates.Count > 0)
                return titleCandidates;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill,
    cq.QueueRank,
    COALESCE(rv.Intensity, 0) AS RivalryIntensity
FROM Fighters f
LEFT JOIN ContenderQueue cq
    ON cq.FighterId = f.Id
   AND cq.PromotionId = f.PromotionId
   AND cq.WeightClass = f.WeightClass
LEFT JOIN Rivalries rv
    ON (
        rv.FighterAId = CASE WHEN $fighterId < f.Id THEN $fighterId ELSE f.Id END
        AND rv.FighterBId = CASE WHEN $fighterId < f.Id THEN f.Id ELSE $fighterId END
    )
WHERE f.Id <> $fighterId
  AND f.WeightClass = $weightClass
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= $availabilityDeadlineWeeks
  AND f.PromotionId = $promotionId
ORDER BY
    CASE
        WHEN cq.QueueRank IS NOT NULL
         AND $fighterQueueRank IS NOT NULL
         AND cq.QueueRank <= 4
         AND $fighterQueueRank <= 2 THEN 0
        ELSE 1
    END,
    COALESCE(rv.Intensity, 0) DESC,
    ABS(f.Skill - $skill),
    COALESCE(f.ReliabilityScore, 60) DESC,
    ABS(f.Popularity - $popularity)
LIMIT 12;";
        cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
        cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$availabilityDeadlineWeeks", availabilityDeadlineWeeks);
        cmd.Parameters.AddWithValue("$skill", fighter.Skill);
        cmd.Parameters.AddWithValue("$popularity", fighter.Popularity);
        cmd.Parameters.AddWithValue("$fighterQueueRank", (object?)fighter.ContenderQueueRank ?? DBNull.Value);

        var list = new List<MatchCandidate>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new MatchCandidate(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                Convert.ToInt32(r["Skill"]),
                r["QueueRank"] == DBNull.Value ? null : Convert.ToInt32(r["QueueRank"]),
                Convert.ToInt32(r["RivalryIntensity"]),
                fighter.IsChampion,
                false));
        }

        return list;
    }

    private static async Task<List<MatchCandidate>> LoadTitleFightCandidatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        int promotionId,
        int availabilityDeadlineWeeks,
        CancellationToken cancellationToken)
    {
        if (fighter.IsChampion)
        {
            return await LoadChampionDefenseCandidatesAsync(
                conn,
                tx,
                fighter,
                promotionId,
                availabilityDeadlineWeeks,
                cancellationToken);
        }

        if (fighter.RankPosition is > 0 and <= 3 || fighter.ContenderQueueRank is > 0 and <= 3)
        {
            return await LoadTitleChallengerCandidatesAsync(
                conn,
                tx,
                fighter,
                promotionId,
                availabilityDeadlineWeeks,
                cancellationToken);
        }

        return new List<MatchCandidate>();
    }

    private static async Task<List<MatchCandidate>> LoadChampionDefenseCandidatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        int promotionId,
        int availabilityDeadlineWeeks,
        CancellationToken cancellationToken)
    {
        using (var queueCmd = conn.CreateCommand())
        {
            queueCmd.Transaction = tx;
            queueCmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill,
    cq.QueueRank
FROM ContenderQueue cq
JOIN Fighters f ON f.Id = cq.FighterId
WHERE cq.PromotionId = $promotionId
  AND cq.WeightClass = $weightClass
  AND f.Id <> $fighterId
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= $availabilityDeadlineWeeks
ORDER BY cq.QueueRank, COALESCE(f.ReliabilityScore, 60) DESC
LIMIT 8;";
            queueCmd.Parameters.AddWithValue("$promotionId", promotionId);
            queueCmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
            queueCmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
            queueCmd.Parameters.AddWithValue("$availabilityDeadlineWeeks", availabilityDeadlineWeeks);

            var queueList = new List<MatchCandidate>();
            using var queueReader = await queueCmd.ExecuteReaderAsync(cancellationToken);
            while (await queueReader.ReadAsync(cancellationToken))
            {
                queueList.Add(new MatchCandidate(
                    Convert.ToInt32(queueReader["FighterId"]),
                    queueReader["FighterName"]?.ToString() ?? "",
                    Convert.ToInt32(queueReader["Skill"]),
                    queueReader["QueueRank"] == DBNull.Value ? null : Convert.ToInt32(queueReader["QueueRank"]),
                    0,
                    true,
                    false));
            }

            if (queueList.Count > 0)
                return queueList;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill,
    pr.RankPosition AS QueueRank
FROM PromotionRankings pr
JOIN Fighters f ON f.Id = pr.FighterId
WHERE pr.PromotionId = $promotionId
  AND pr.WeightClass = $weightClass
  AND f.Id <> $fighterId
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= $availabilityDeadlineWeeks
ORDER BY pr.RankPosition, COALESCE(f.ReliabilityScore, 60) DESC
LIMIT 8;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
        cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
        cmd.Parameters.AddWithValue("$availabilityDeadlineWeeks", availabilityDeadlineWeeks);

        var list = new List<MatchCandidate>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new MatchCandidate(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                Convert.ToInt32(r["Skill"]),
                r["QueueRank"] == DBNull.Value ? null : Convert.ToInt32(r["QueueRank"]),
                0,
                true,
                false));
        }

        return list;
    }

    private static async Task<List<MatchCandidate>> LoadTitleChallengerCandidatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        int promotionId,
        int availabilityDeadlineWeeks,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id AS FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.Skill
FROM Titles t
JOIN Fighters f ON f.Id = t.ChampionFighterId
WHERE t.PromotionId = $promotionId
  AND t.WeightClass = $weightClass
  AND COALESCE(t.ChampionFighterId, 0) > 0
  AND f.Id <> $fighterId
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= $availabilityDeadlineWeeks
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= $availabilityDeadlineWeeks
LIMIT 1;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);
        cmd.Parameters.AddWithValue("$weightClass", fighter.WeightClass);
        cmd.Parameters.AddWithValue("$fighterId", fighter.FighterId);
        cmd.Parameters.AddWithValue("$availabilityDeadlineWeeks", availabilityDeadlineWeeks);

        var list = new List<MatchCandidate>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new MatchCandidate(
                Convert.ToInt32(r["FighterId"]),
                r["FighterName"]?.ToString() ?? "",
                Convert.ToInt32(r["Skill"]),
                1,
                0,
                true,
                false));
        }

        return list;
    }

    private async Task<OfferPlan?> ResolveOfferPlanAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        PromotionSnapshot promotion,
        int absoluteWeek,
        CancellationToken cancellationToken)
    {
        var standardCampWeeks = ResolveRequiredCampWeeks(promotion, fighter);
        var standardPlan = BuildOfferPlan(
            absoluteWeek,
            ResolveOfferEventWeek(absoluteWeek, promotion.NextEventWeek, promotion.EventIntervalWeeks, standardCampWeeks),
            standardCampWeeks,
            false);

        var tryShortNotice = ShouldAttemptShortNotice(fighter, promotion, absoluteWeek);
        if (tryShortNotice)
        {
            var shortNoticePlan = BuildOfferPlan(
                absoluteWeek,
                ResolveOfferEventWeek(absoluteWeek, promotion.NextEventWeek, promotion.EventIntervalWeeks, promotion.ShortNoticeCampWeeks),
                promotion.ShortNoticeCampWeeks,
                true);

            if (shortNoticePlan.WeeksUntilFight <= promotion.ShortNoticeMaxLeadWeeks
                && CanStartCampByDeadline(fighter, shortNoticePlan.WeeksUntilFight - shortNoticePlan.CampWeeksGranted))
            {
                var shortNoticeOpponent = await FindOpponentForPlanAsync(
                    conn,
                    tx,
                    fighter,
                    promotion,
                    shortNoticePlan,
                    includeTitleFights: false,
                    cancellationToken);
                if (shortNoticeOpponent is not null)
                    return shortNoticePlan with { Opponent = shortNoticeOpponent };
            }
        }

        if (!CanStartCampByDeadline(fighter, standardPlan.WeeksUntilFight - standardPlan.CampWeeksGranted))
            return null;

        var opponent = await FindOpponentForPlanAsync(
            conn,
            tx,
            fighter,
            promotion,
            standardPlan,
            includeTitleFights: true,
            cancellationToken);

        return opponent is null
            ? null
            : standardPlan with { Opponent = opponent };
    }

    private async Task<MatchCandidate?> FindOpponentForPlanAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedAvailability fighter,
        PromotionSnapshot promotion,
        OfferPlan plan,
        bool includeTitleFights,
        CancellationToken cancellationToken)
    {
        var candidates = await FindOpponentCandidatesAsync(
            conn,
            tx,
            fighter,
            promotion.PromotionId,
            plan.WeeksUntilFight,
            plan.CampWeeksGranted,
            includeTitleFights,
            cancellationToken);

        foreach (var candidate in candidates)
        {
            if (await HaveRecentRematchAsync(conn, tx, fighter.FighterId, candidate.FighterId, cancellationToken))
                continue;

            if (!PassesCampAcceptance(fighter, candidate, plan))
                continue;

            return candidate with
            {
                IsTitleEliminator = ShouldMarkTitleEliminator(fighter, candidate)
            };
        }

        return null;
    }

    private static OfferPlan BuildOfferPlan(
        int absoluteWeek,
        int eventWeek,
        int campWeeksGranted,
        bool isShortNotice)
    {
        var weeksUntilFight = Math.Max(1, eventWeek - absoluteWeek);
        return new OfferPlan(
            eventWeek,
            weeksUntilFight,
            campWeeksGranted,
            isShortNotice,
            isShortNotice ? ShortNoticePurseMultiplier : 1.0,
            isShortNotice ? ShortNoticeWinBonusMultiplier : 1.0,
            null);
    }

    private static bool ShouldAttemptShortNotice(ManagedAvailability fighter, PromotionSnapshot promotion, int absoluteWeek)
    {
        if (fighter.IsChampion || fighter.RankPosition is > 0 and <= 3 || fighter.ContenderQueueRank is > 0 and <= 3)
            return false;

        var nextWeeksUntilFight = Math.Max(1, promotion.NextEventWeek - absoluteWeek);
        if (nextWeeksUntilFight > promotion.ShortNoticeMaxLeadWeeks)
            return false;

        var rollSeed = HashCode.Combine(absoluteWeek, promotion.PromotionId, fighter.FighterId);
        var normalized = (Math.Abs(rollSeed) % 1000) / 1000.0;
        return normalized < ShortNoticeChance;
    }

    private sealed record PromotionSnapshot(
        int PromotionId,
        string Name,
        int NextEventWeek,
        int EventIntervalWeeks,
        int StandardCampWeeks,
        int MajorCampWeeks,
        int TitleCampWeeks,
        int ShortNoticeCampWeeks,
        int ShortNoticeMaxLeadWeeks);

    private sealed record ManagedAvailability(
        int FighterId,
        string Name,
        string WeightClass,
        int Skill,
        int Popularity,
        int? PromotionId,
        int? RankPosition,
        int? ContenderQueueRank,
        bool IsChampion,
        int WeeksUntilAvailable,
        int InjuryWeeksRemaining,
        int MedicalSuspensionWeeksRemaining,
        int ContractFightsRemaining);

    private sealed record MatchCandidate(
        int FighterId,
        string Name,
        int Skill,
        int? ContenderQueueRank,
        int RivalryIntensity,
        bool IsTitleFight,
        bool IsTitleEliminator);

    private sealed record CreatedOffer(
        InboxMessage InboxMessage,
        string UserMessage);

    private sealed record OfferPlan(
        int EventWeek,
        int WeeksUntilFight,
        int CampWeeksGranted,
        bool IsShortNotice,
        double PurseMultiplier,
        double WinBonusMultiplier,
        MatchCandidate? Opponent);
}
