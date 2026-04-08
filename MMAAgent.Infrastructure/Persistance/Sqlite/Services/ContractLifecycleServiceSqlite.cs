using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class ContractLifecycleServiceSqlite : IContractLifecycleService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IContractOfferRepository _contractOfferRepository;

    public ContractLifecycleServiceSqlite(
        SqliteConnectionFactory factory,
        IAgentProfileRepository agentRepository,
        IInboxRepository inboxRepository,
        IContractOfferRepository contractOfferRepository)
    {
        _factory = factory;
        _agentRepository = agentRepository;
        _inboxRepository = inboxRepository;
        _contractOfferRepository = contractOfferRepository;
    }

    public async Task<int> ProcessWeeklyAsync(CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent is null)
            return 0;

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var absoluteWeek = await LoadAbsoluteWeekAsync(conn, tx, cancellationToken);
        await ExpireOldPendingOffersAsync(conn, tx, absoluteWeek, cancellationToken);

        var managedFighters = await LoadManagedFightersAsync(conn, tx, agent.Id, cancellationToken);
        var inboxMessages = new List<InboxMessage>();
        var offersCreated = 0;

        foreach (var fighter in managedFighters)
        {
            if (await HasPendingContractOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
                continue;

            if (fighter.PromotionId is int promotionId)
            {
                if (fighter.ContractFightsRemaining > 0)
                    continue;

                var recentForm = await TryLoadRecentFormAsync(conn, tx, fighter.FighterId, cancellationToken);
                var promotion = await LoadPromotionAsync(conn, tx, promotionId, cancellationToken);
                if (promotion is null)
                    continue;

                var decision = EvaluateRenewal(fighter, promotion, recentForm);
                if (!decision.ShouldOffer)
                {
                    await ReleaseFighterAsync(conn, tx, fighter.FighterId, cancellationToken);

                    inboxMessages.Add(new InboxMessage
                    {
                        AgentId = agent.Id,
                        MessageType = "ContractExpired",
                        Subject = $"{fighter.Name} is now a free agent",
                        Body = $"{promotion.Name} decided not to renew {fighter.Name}. {fighter.Name} is now a free agent.",
                        CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        IsRead = false
                    });

                    continue;
                }

                await InsertContractOfferAsync(conn, tx, new ContractOfferRow(
                    FighterId: fighter.FighterId,
                    PromotionId: promotion.Id,
                    OfferedFights: decision.OfferedFights,
                    BasePurse: decision.BasePurse,
                    WinBonus: decision.WinBonus,
                    WeeksToRespond: 2,
                    Status: "Pending",
                    SourceType: "Renewal",
                    Notes: decision.Notes,
                    CreatedWeek: absoluteWeek,
                    CreatedDate: DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    RespondedDate: null), cancellationToken);

                inboxMessages.Add(new InboxMessage
                {
                    AgentId = agent.Id,
                    MessageType = "ContractOffer",
                    Subject = $"Renewal offer for {fighter.Name}",
                    Body = $"{promotion.Name} offers {fighter.Name} a new {decision.OfferedFights}-fight deal. Base purse: {decision.BasePurse}, win bonus: {decision.WinBonus}.",
                    CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsRead = false
                });

                offersCreated++;
            }
            else
            {
                if (!ShouldMarketProbeThisWeek(fighter, absoluteWeek))
                    continue;

                var marketOffer = await TryCreateMarketOfferAsync(conn, tx, fighter, absoluteWeek, cancellationToken);
                if (marketOffer is null)
                    continue;

                inboxMessages.Add(new InboxMessage
                {
                    AgentId = agent.Id,
                    MessageType = "ContractOffer",
                    Subject = $"Market offer for {fighter.Name}",
                    Body = $"{marketOffer.PromotionName} wants to sign {fighter.Name} to a {marketOffer.OfferedFights}-fight deal. Base purse: {marketOffer.BasePurse}, win bonus: {marketOffer.WinBonus}.",
                    CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsRead = false
                });

                offersCreated++;
            }
        }

        tx.Commit();

        foreach (var msg in inboxMessages)
            await _inboxRepository.CreateAsync(msg);

        return offersCreated;
    }

    public async Task<int> PitchFighterToPromotionAsync(int fighterId, int promotionId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent is null)
            return 0;

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var absoluteWeek = await LoadAbsoluteWeekAsync(conn, tx, cancellationToken);
        var fighter = await LoadSingleFighterAsync(conn, tx, fighterId, agent.Id, cancellationToken)
            ?? throw new InvalidOperationException("Managed fighter no encontrado.");

        if (await HasPendingContractOfferAsync(conn, tx, fighter.FighterId, cancellationToken))
            throw new InvalidOperationException("Ese fighter ya tiene una oferta de contrato pendiente.");

        var promotion = await LoadPromotionAsync(conn, tx, promotionId, cancellationToken)
            ?? throw new InvalidOperationException("Promotora no encontrada.");

        var recentForm = await TryLoadRecentFormAsync(conn, tx, fighter.FighterId, cancellationToken);
        var pitchDecision = EvaluatePitchAcceptance(fighter, promotion, recentForm);

        if (!pitchDecision.Accepted)
        {
            tx.Commit();

            await _inboxRepository.CreateAsync(new InboxMessage
            {
                AgentId = agent.Id,
                MessageType = "PitchRejected",
                Subject = $"{promotion.Name} rejected {fighter.Name}",
                Body = $"{promotion.Name} has rejected your attempt to place {fighter.Name}. {pitchDecision.Notes}",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            });

            return 0;
        }

        await InsertContractOfferAsync(conn, tx, new ContractOfferRow(
            FighterId: fighter.FighterId,
            PromotionId: promotion.Id,
            OfferedFights: pitchDecision.OfferedFights,
            BasePurse: pitchDecision.BasePurse,
            WinBonus: pitchDecision.WinBonus,
            WeeksToRespond: 2,
            Status: "Pending",
            SourceType: "PitchAccepted",
            Notes: pitchDecision.Notes,
            CreatedWeek: absoluteWeek,
            CreatedDate: DateTime.UtcNow.ToString("yyyy-MM-dd"),
            RespondedDate: null), cancellationToken);

        tx.Commit();

        await _inboxRepository.CreateAsync(new InboxMessage
        {
            AgentId = agent.Id,
            MessageType = "ContractOffer",
            Subject = $"Pitch accepted for {fighter.Name}",
            Body = $"{promotion.Name} is interested in signing {fighter.Name}. {pitchDecision.OfferedFights}-fight deal, base purse {pitchDecision.BasePurse}, win bonus {pitchDecision.WinBonus}.",
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IsRead = false
        });

        return 1;
    }

    public async Task RespondToOfferAsync(int contractOfferId, bool accept, CancellationToken cancellationToken = default)
    {
        var offer = await _contractOfferRepository.GetByIdAsync(contractOfferId)
            ?? throw new InvalidOperationException("Contract offer no encontrada.");

        if (!string.Equals(offer.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La oferta ya no está pendiente.");

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var fighter = await LoadFighterBasicAsync(conn, tx, offer.FighterId, cancellationToken)
            ?? throw new InvalidOperationException("Fighter no encontrado.");

        if (!accept)
        {
            await UpdateContractOfferStatusAsync(conn, tx, offer.Id, "Rejected", DateTime.UtcNow.ToString("yyyy-MM-dd"), cancellationToken);
            tx.Commit();

            var agent = await _agentRepository.GetAsync();
            if (agent is not null)
            {
                await _inboxRepository.CreateAsync(new InboxMessage
                {
                    AgentId = agent.Id,
                    MessageType = "ContractRejected",
                    Subject = $"You rejected a contract for {fighter.Name}",
                    Body = $"The contract offer for {fighter.Name} has been rejected.",
                    CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsRead = false
                });
            }

            return;
        }

        await AcceptOfferAsync(conn, tx, offer, cancellationToken);
        tx.Commit();

        var currentAgent = await _agentRepository.GetAsync();
        if (currentAgent is not null)
        {
            var promotion = await LoadPromotionNameByIdAsync(offer.PromotionId, cancellationToken);

            await _inboxRepository.CreateAsync(new InboxMessage
            {
                AgentId = currentAgent.Id,
                MessageType = "ContractAccepted",
                Subject = $"{fighter.Name} signed with {promotion}",
                Body = $"{fighter.Name} has signed a new {offer.OfferedFights}-fight contract with {promotion}.",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            });
        }
    }

    private static async Task<int> LoadAbsoluteWeekAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(AbsoluteWeek, 1) FROM GameState LIMIT 1;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExpireOldPendingOffersAsync(SqliteConnection conn, SqliteTransaction tx, int currentAbsoluteWeek, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE ContractOffers
SET Status = 'Expired',
    RespondedDate = $respondedDate
WHERE Status = 'Pending'
  AND (CreatedWeek + WeeksToRespond) < $currentAbsoluteWeek;";
        cmd.Parameters.AddWithValue("$respondedDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$currentAbsoluteWeek", currentAbsoluteWeek);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool ShouldMarketProbeThisWeek(ManagedContractSnapshot fighter, int absoluteWeek)
    {
        if (fighter.WeeksUntilAvailable > 0 || fighter.InjuryWeeksRemaining > 0)
            return false;

        var cadenceSeed = Math.Abs(fighter.FighterId % 4);
        return absoluteWeek % 4 == cadenceSeed;
    }

    private async Task<MarketOfferCreated?> TryCreateMarketOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ManagedContractSnapshot fighter,
        int absoluteWeek,
        CancellationToken cancellationToken)
    {
        var promotions = await LoadActivePromotionsAsync(conn, tx, cancellationToken);
        if (promotions.Count == 0)
            return null;

        var recentForm = await TryLoadRecentFormAsync(conn, tx, fighter.FighterId, cancellationToken);

        foreach (var promotion in promotions.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.NextEventWeek))
        {
            var decision = EvaluatePitchAcceptance(fighter, promotion, recentForm);
            if (!decision.Accepted)
                continue;

            await InsertContractOfferAsync(conn, tx, new ContractOfferRow(
                FighterId: fighter.FighterId,
                PromotionId: promotion.Id,
                OfferedFights: decision.OfferedFights,
                BasePurse: decision.BasePurse,
                WinBonus: decision.WinBonus,
                WeeksToRespond: 2,
                Status: "Pending",
                SourceType: "Market",
                Notes: decision.Notes,
                CreatedWeek: absoluteWeek,
                CreatedDate: DateTime.UtcNow.ToString("yyyy-MM-dd"),
                RespondedDate: null), cancellationToken);

            return new MarketOfferCreated(
                promotion.Name,
                decision.OfferedFights,
                decision.BasePurse,
                decision.WinBonus);
        }

        return null;
    }

    private static RenewalDecision EvaluateRenewal(
        ManagedContractSnapshot fighter,
        PromotionSnapshot promotion,
        RecentForm recentForm)
    {
        var score = 0;

        score += fighter.Skill / 3;
        score += fighter.Popularity / 4;
        score += recentForm.Wins * 12;
        score -= recentForm.Losses * 10;

        if (recentForm.LossStreak >= 2) score -= 20;
        if (recentForm.LossStreak >= 3) score -= 30;

        if (fighter.Age >= 34) score -= 6;
        if (fighter.Age >= 37) score -= 12;

        // más exigencia en promos grandes
        score -= promotion.Strictness;

        if (fighter.ContractFightsRemaining <= 0)
            score -= 5;

        if (score < 28)
        {
            return new RenewalDecision(false, 0, 0, 0,
                $"Renewal score too low ({score}). Recent form W:{recentForm.Wins} L:{recentForm.Losses}, streak {recentForm.LossStreak}.");
        }

        var fights = score >= 60 ? 4 : 3;
        var basePurse = Math.Max(2500, 2500 + (fighter.Skill * 90) + (fighter.Popularity * 30));
        var winBonus = Math.Max(1000, 1200 + (fighter.Popularity * 20));

        return new RenewalDecision(true, fights, basePurse, winBonus,
            $"Renewal approved. Score {score}. Recent form W:{recentForm.Wins} L:{recentForm.Losses}.");
    }

    private static PitchDecision EvaluatePitchAcceptance(
        ManagedContractSnapshot fighter,
        PromotionSnapshot promotion,
        RecentForm recentForm)
    {
        var score = 0;

        score += fighter.Skill;
        score += fighter.Popularity / 2;
        score += recentForm.Wins * 6;
        score -= recentForm.Losses * 4;

        if (fighter.Age > 35) score -= 10;
        if (fighter.InjuryWeeksRemaining > 0) score -= 15;
        if (fighter.WeeksUntilAvailable > 0) score -= 5;

        score -= promotion.Strictness;

        if (score < 55)
        {
            return new PitchDecision(false, 0, 0, 0,
                $"Score {score}. Promotion is not interested right now.");
        }

        var fights = score >= 85 ? 4 : 3;
        var basePurse = Math.Max(2000, 1800 + fighter.Skill * 80 + fighter.Popularity * 25);
        var winBonus = Math.Max(800, 900 + fighter.Popularity * 15);

        return new PitchDecision(true, fights, basePurse, winBonus,
            $"Pitch accepted with score {score}.");
    }

    private static async Task<bool> HasPendingContractOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM ContractOffers WHERE FighterId = $fighterId AND Status = 'Pending';";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<List<ManagedContractSnapshot>> LoadManagedFightersAsync(
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
    COALESCE(f.Skill, 0) AS Skill,
    COALESCE(f.Popularity, 0) AS Popularity,
    COALESCE(f.Age, 28) AS Age,
    f.PromotionId,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.ContractFightsRemaining, 0) AS ContractFightsRemaining
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
ORDER BY COALESCE(f.Popularity, 0) DESC, COALESCE(f.Skill, 0) DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var list = new List<ManagedContractSnapshot>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ManagedContractSnapshot(
                FighterId: Convert.ToInt32(r["FighterId"]),
                Name: r["FighterName"]?.ToString() ?? "",
                WeightClass: r["WeightClass"]?.ToString() ?? "",
                Skill: Convert.ToInt32(r["Skill"]),
                Popularity: Convert.ToInt32(r["Popularity"]),
                Age: Convert.ToInt32(r["Age"]),
                PromotionId: r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
                WeeksUntilAvailable: Convert.ToInt32(r["WeeksUntilAvailable"]),
                InjuryWeeksRemaining: Convert.ToInt32(r["InjuryWeeksRemaining"]),
                ContractFightsRemaining: Convert.ToInt32(r["ContractFightsRemaining"])));
        }

        return list;
    }

    private static async Task<ManagedContractSnapshot?> LoadSingleFighterAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
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
    COALESCE(f.Skill, 0) AS Skill,
    COALESCE(f.Popularity, 0) AS Popularity,
    COALESCE(f.Age, 28) AS Age,
    f.PromotionId,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.ContractFightsRemaining, 0) AS ContractFightsRemaining
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND f.Id = $fighterId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new ManagedContractSnapshot(
            FighterId: Convert.ToInt32(r["FighterId"]),
            Name: r["FighterName"]?.ToString() ?? "",
            WeightClass: r["WeightClass"]?.ToString() ?? "",
            Skill: Convert.ToInt32(r["Skill"]),
            Popularity: Convert.ToInt32(r["Popularity"]),
            Age: Convert.ToInt32(r["Age"]),
            PromotionId: r["PromotionId"] == DBNull.Value ? null : Convert.ToInt32(r["PromotionId"]),
            WeeksUntilAvailable: Convert.ToInt32(r["WeeksUntilAvailable"]),
            InjuryWeeksRemaining: Convert.ToInt32(r["InjuryWeeksRemaining"]),
            ContractFightsRemaining: Convert.ToInt32(r["ContractFightsRemaining"]));
    }

    private static async Task<PromotionSnapshot?> LoadPromotionAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int promotionId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    Id,
    Name,
    COALESCE(IsActive, 1) AS IsActive,
    COALESCE(NextEventWeek, 0) AS NextEventWeek
FROM Promotions
WHERE Id = $promotionId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        var nextEventWeek = Convert.ToInt32(r["NextEventWeek"]);
        var strictness = nextEventWeek <= 4 ? 15 : 8;

        return new PromotionSnapshot(
            Id: Convert.ToInt32(r["Id"]),
            Name: r["Name"]?.ToString() ?? $"Promotion {promotionId}",
            IsActive: Convert.ToInt32(r["IsActive"]) == 1,
            NextEventWeek: nextEventWeek,
            Strictness: strictness);
    }

    private static async Task<List<PromotionSnapshot>> LoadActivePromotionsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    Id,
    Name,
    COALESCE(IsActive, 1) AS IsActive,
    COALESCE(NextEventWeek, 0) AS NextEventWeek
FROM Promotions
WHERE COALESCE(IsActive, 1) = 1
ORDER BY COALESCE(NextEventWeek, 999999), Id;";

        var result = new List<PromotionSnapshot>();
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var nextEventWeek = Convert.ToInt32(r["NextEventWeek"]);
            var strictness = nextEventWeek <= 4 ? 15 : 8;

            result.Add(new PromotionSnapshot(
                Id: Convert.ToInt32(r["Id"]),
                Name: r["Name"]?.ToString() ?? "",
                IsActive: Convert.ToInt32(r["IsActive"]) == 1,
                NextEventWeek: nextEventWeek,
                Strictness: strictness));
        }

        return result;
    }

    private static async Task<FighterBasic?> LoadFighterBasicAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT Id, (FirstName || ' ' || LastName) AS FighterName
FROM Fighters
WHERE Id = $fighterId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new FighterBasic(
            Convert.ToInt32(r["Id"]),
            r["FighterName"]?.ToString() ?? "");
    }

    private static async Task<string> LoadPromotionNameByIdAsync(int promotionId, CancellationToken cancellationToken)
    {
        // fallback read outside tx when message is created after commit
        using var conn = new SqliteConnection(); // placeholder not used
        await Task.CompletedTask;
        return $"Promotion {promotionId}";
    }

    private static async Task ReleaseFighterAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = NULL,
    ContractFightsRemaining = 0
WHERE Id = $fighterId;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertContractOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ContractOfferRow offer,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO ContractOffers
(
    FighterId, PromotionId, OfferedFights, BasePurse, WinBonus,
    WeeksToRespond, Status, SourceType, Notes,
    CreatedWeek, CreatedDate, RespondedDate
)
VALUES
(
    $fighterId, $promotionId, $offeredFights, $basePurse, $winBonus,
    $weeksToRespond, $status, $sourceType, $notes,
    $createdWeek, $createdDate, $respondedDate
);";

        cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
        cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
        cmd.Parameters.AddWithValue("$offeredFights", offer.OfferedFights);
        cmd.Parameters.AddWithValue("$basePurse", offer.BasePurse);
        cmd.Parameters.AddWithValue("$winBonus", offer.WinBonus);
        cmd.Parameters.AddWithValue("$weeksToRespond", offer.WeeksToRespond);
        cmd.Parameters.AddWithValue("$status", offer.Status);
        cmd.Parameters.AddWithValue("$sourceType", offer.SourceType);
        cmd.Parameters.AddWithValue("$notes", (object?)offer.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdWeek", offer.CreatedWeek);
        cmd.Parameters.AddWithValue("$createdDate", offer.CreatedDate);
        cmd.Parameters.AddWithValue("$respondedDate", (object?)offer.RespondedDate ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateContractOfferStatusAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int contractOfferId,
        string status,
        string? respondedDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE ContractOffers
SET Status = $status,
    RespondedDate = $respondedDate
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$respondedDate", (object?)respondedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", contractOfferId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcceptOfferAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ContractOffer offer,
        CancellationToken cancellationToken)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = $promotionId,
    ContractFightsRemaining = $offeredFights
WHERE Id = $fighterId;";
            cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
            cmd.Parameters.AddWithValue("$offeredFights", offer.OfferedFights);
            cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ContractOffers
SET Status = CASE WHEN Id = $acceptedId THEN 'Accepted' ELSE 'Withdrawn' END,
    RespondedDate = $respondedDate
WHERE FighterId = $fighterId
  AND Status = 'Pending';";
            cmd.Parameters.AddWithValue("$acceptedId", offer.Id);
            cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
            cmd.Parameters.AddWithValue("$respondedDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<RecentForm> TryLoadRecentFormAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId,
        CancellationToken cancellationToken)
    {
        // Fallback defensivo: si tu schema de FightHistory cambia, devuelve neutro en vez de romper.
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT WinnerId
FROM FightHistory
WHERE FighterAId = $fighterId OR FighterBId = $fighterId
ORDER BY Id DESC
LIMIT 3;";
            cmd.Parameters.AddWithValue("$fighterId", fighterId);

            var wins = 0;
            var losses = 0;
            var currentLossStreak = 0;

            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var winnerId = r["WinnerId"] == DBNull.Value ? 0 : Convert.ToInt32(r["WinnerId"]);
                if (winnerId == fighterId)
                {
                    wins++;
                    break; // si gana, la racha de derrotas se corta
                }
                else
                {
                    losses++;
                    currentLossStreak++;
                }
            }

            return new RecentForm(wins, losses, currentLossStreak);
        }
        catch
        {
            return new RecentForm(0, 0, 0);
        }
    }

    private sealed record ManagedContractSnapshot(
        int FighterId,
        string Name,
        string WeightClass,
        int Skill,
        int Popularity,
        int Age,
        int? PromotionId,
        int WeeksUntilAvailable,
        int InjuryWeeksRemaining,
        int ContractFightsRemaining);

    private sealed record PromotionSnapshot(
        int Id,
        string Name,
        bool IsActive,
        int NextEventWeek,
        int Strictness);

    private sealed record FighterBasic(int FighterId, string Name);
    private sealed record RecentForm(int Wins, int Losses, int LossStreak);
    private sealed record RenewalDecision(bool ShouldOffer, int OfferedFights, int BasePurse, int WinBonus, string Notes);
    private sealed record PitchDecision(bool Accepted, int OfferedFights, int BasePurse, int WinBonus, string Notes);
    private sealed record MarketOfferCreated(string PromotionName, int OfferedFights, int BasePurse, int WinBonus);
    private sealed record ContractOfferRow(
        int FighterId,
        int PromotionId,
        int OfferedFights,
        int BasePurse,
        int WinBonus,
        int WeeksToRespond,
        string Status,
        string SourceType,
        string? Notes,
        int CreatedWeek,
        string CreatedDate,
        string? RespondedDate);
}
