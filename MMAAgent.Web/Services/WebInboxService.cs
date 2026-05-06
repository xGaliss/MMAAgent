using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Web.Models;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Web.Services;

public sealed class WebInboxService
{
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly SqliteConnectionFactory _factory;

    public WebInboxService(
        IAgentProfileRepository agentProfileRepository,
        IInboxRepository inboxRepository,
        SqliteConnectionFactory factory)
    {
        _agentProfileRepository = agentProfileRepository;
        _inboxRepository = inboxRepository;
        _factory = factory;
    }

    public async Task<WebInboxResult> LoadAsync(string? messageType, bool includeArchived = false, bool archivedOnly = false)
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null)
            return new WebInboxResult();

        var messages = await _inboxRepository.ListAsync(agent.Id, messageType, includeArchived, archivedOnly);
        var fightOffers = await LoadFightOffersAsync(agent.Id);
        var contractOffers = await LoadContractOffersAsync(agent.Id);
        var decisions = await LoadDecisionEventsAsync(agent.Id);

        return new WebInboxResult
        {
            Messages = messages.Select(x => new InboxMessageVm
            {
                Id = x.Id,
                Subject = x.Subject,
                Body = x.Body,
                MessageType = x.MessageType,
                CreatedDate = x.CreatedDate,
                IsRead = x.IsRead,
                IsArchived = x.IsArchived
            }).ToList(),
            Offers = fightOffers,
            ContractOffers = contractOffers,
            Decisions = decisions
        };
    }

    public async Task<IReadOnlyList<DecisionEventVm>> LoadPendingDecisionsAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null)
            return Array.Empty<DecisionEventVm>();

        return await LoadDecisionEventsAsync(agent.Id);
    }

    public async Task MarkAllReadAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;
        await _inboxRepository.MarkAllReadAsync(agent.Id);
    }

    public Task MarkMessageAsReadAsync(int messageId)
        => _inboxRepository.MarkAsReadAsync(messageId);

    public Task ArchiveMessageAsync(int messageId)
        => _inboxRepository.ArchiveAsync(messageId);

    public Task RestoreMessageAsync(int messageId)
        => _inboxRepository.RestoreAsync(messageId);

    public Task DeleteMessageAsync(int messageId)
        => _inboxRepository.DeleteAsync(messageId);

    public async Task ArchiveReadAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;
        await _inboxRepository.ArchiveReadAsync(agent.Id);
    }

    public async Task DeleteReadAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;
        await _inboxRepository.DeleteReadAsync(agent.Id);
    }

    public async Task DeleteFightOfferAsync(int offerId)
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM FightOffers
WHERE Id = $offerId
  AND EXISTS (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = FightOffers.FighterId
        AND mf.AgentId = $agentId
        AND COALESCE(mf.IsActive, 1) = 1
  );";
        cmd.Parameters.AddWithValue("$offerId", offerId);
        cmd.Parameters.AddWithValue("$agentId", agent.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteProcessedFightOffersAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM FightOffers
WHERE Status <> 'Pending'
  AND EXISTS (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = FightOffers.FighterId
        AND mf.AgentId = $agentId
        AND COALESCE(mf.IsActive, 1) = 1
  );";
        cmd.Parameters.AddWithValue("$agentId", agent.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteContractOfferAsync(int contractOfferId)
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM ContractOffers
WHERE Id = $offerId
  AND EXISTS (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = ContractOffers.FighterId
        AND mf.AgentId = $agentId
        AND COALESCE(mf.IsActive, 1) = 1
  );";
        cmd.Parameters.AddWithValue("$offerId", contractOfferId);
        cmd.Parameters.AddWithValue("$agentId", agent.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteProcessedContractOffersAsync()
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null) return;

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DELETE FROM ContractOffers
WHERE Status <> 'Pending'
  AND EXISTS (
      SELECT 1
      FROM ManagedFighters mf
      WHERE mf.FighterId = ContractOffers.FighterId
        AND mf.AgentId = $agentId
        AND COALESCE(mf.IsActive, 1) = 1
  );";
        cmd.Parameters.AddWithValue("$agentId", agent.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResolveDecisionAsync(int decisionId, string optionKey)
    {
        var agent = await _agentProfileRepository.GetAsync();
        if (agent is null)
            return;

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        DecisionSnapshot? decision;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, FighterId, FightId, DecisionType, OptionAKey, OptionBKey
FROM DecisionEvents
WHERE Id = $decisionId
  AND AgentId = $agentId
  AND Status = 'Pending'
LIMIT 1;";
            cmd.Parameters.AddWithValue("$decisionId", decisionId);
            cmd.Parameters.AddWithValue("$agentId", agent.Id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return;

            decision = new DecisionSnapshot(
                Convert.ToInt32(reader["Id"]),
                reader["FighterId"] == DBNull.Value ? null : Convert.ToInt32(reader["FighterId"]),
                reader["FightId"] == DBNull.Value ? null : Convert.ToInt32(reader["FightId"]),
                reader["DecisionType"]?.ToString() ?? "",
                reader["OptionAKey"]?.ToString() ?? "",
                reader["OptionBKey"]?.ToString() ?? "");
        }

        var resolvedOption = string.Equals(optionKey, decision.OptionBKey, StringComparison.OrdinalIgnoreCase)
            ? decision.OptionBKey
            : decision.OptionAKey;
        var outcomeSummary = await ApplyDecisionOutcomeAsync(conn, tx, agent.Id, decision, resolvedOption);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE DecisionEvents
SET Status = 'Resolved',
    ResolvedDate = date('now'),
    OutcomeSummary = $summary
WHERE Id = $decisionId;";
            cmd.Parameters.AddWithValue("$summary", outcomeSummary);
            cmd.Parameters.AddWithValue("$decisionId", decision.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }

    private async Task<string> ApplyDecisionOutcomeAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        DecisionSnapshot decision,
        string optionKey)
    {
        switch (decision.DecisionType)
        {
            case "FightWeekApproach":
                if (decision.FightId is int fightId && decision.FighterId is int fighterId)
                {
                    var personality = await LoadDecisionPersonalityAsync(conn, tx, fighterId);
                    var quietCamp = string.Equals(optionKey, "QuietCamp", StringComparison.OrdinalIgnoreCase);
                    var performanceModifier = quietCamp
                        ? 1 + (personality.Discipline >= 70 ? 1 : 0) + (personality.Stability >= 70 ? 1 : 0)
                        : -1 + (personality.Showmanship >= 70 ? 1 : 0) + (personality.Ambition >= 70 ? 1 : 0);
                    var riskModifier = quietCamp
                        ? -1 - (personality.Stability >= 70 ? 1 : 0)
                        : 1 + (personality.RiskTolerance >= 70 ? 1 : 0);
                    var decisionNotes = quietCamp
                        ? "The team shut out the noise and kept the fighter focused through fight week."
                        : "The team leaned into the spotlight and accepted a little more volatility for extra attention.";

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
UPDATE FightPreparations
SET ManagerDecisionType = 'FightWeekApproach',
    ManagerDecisionChoice = $choice,
    PerformanceModifier = COALESCE(PerformanceModifier, 0) + $performanceModifier,
    RiskModifier = COALESCE(RiskModifier, 0) + $riskModifier,
    DecisionNotes = $decisionNotes,
    LastUpdatedDate = COALESCE(LastUpdatedDate, date('now'))
WHERE FightId = $fightId
  AND FighterId = $fighterId;";
                    cmd.Parameters.AddWithValue("$choice", optionKey);
                    cmd.Parameters.AddWithValue("$performanceModifier", performanceModifier);
                    cmd.Parameters.AddWithValue("$riskModifier", riskModifier);
                    cmd.Parameters.AddWithValue("$decisionNotes", decisionNotes);
                    cmd.Parameters.AddWithValue("$fightId", fightId);
                    cmd.Parameters.AddWithValue("$fighterId", fighterId);
                    await cmd.ExecuteNonQueryAsync();

                    if (!quietCamp)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + $heatGain),
    Marketability = MIN(99, COALESCE(Marketability, 50) + $marketabilityGain),
    Momentum = MIN(99, COALESCE(Momentum, 50) + $momentumGain)
WHERE Id = $fighterId;",
                            ("$fighterId", fighterId),
                            ("$heatGain", 4 + (personality.Showmanship >= 70 ? 2 : 0)),
                            ("$marketabilityGain", 2 + (personality.Showmanship >= 75 ? 2 : 0)),
                            ("$momentumGain", personality.Ambition >= 68 ? 2 : 1));
                    }

                    await InsertDecisionMessageAsync(conn, tx, agentId, "FightWeekDecision", "Fight week call made", decisionNotes);
                    return decisionNotes;
                }
                break;

            case "WeightCutCall":
                if (decision.FightId is int weightCutFightId && decision.FighterId is int fighterId2)
                {
                    var personality = await LoadDecisionPersonalityAsync(conn, tx, fighterId2);
                    var protect = string.Equals(optionKey, "ProtectFighter", StringComparison.OrdinalIgnoreCase);
                    var performanceModifier = protect
                        ? 1 + (personality.Discipline >= 68 ? 1 : 0)
                        : -2 + (personality.RiskTolerance >= 75 ? 1 : 0);
                    var riskModifier = protect
                        ? -2 - (personality.Stability >= 68 ? 1 : 0)
                        : 3 + (personality.RiskTolerance >= 72 ? 1 : 0);
                    var decisionNotes = protect
                        ? "The team protected the cut, trimmed the extra stress and accepted a slightly safer approach."
                        : "The team forced the cut line hard, gambling on professionalism and a little more chaos.";

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
UPDATE FightPreparations
SET ManagerDecisionType = 'WeightCutCall',
    ManagerDecisionChoice = $choice,
    PerformanceModifier = COALESCE(PerformanceModifier, 0) + $performanceModifier,
    RiskModifier = COALESCE(RiskModifier, 0) + $riskModifier,
    DecisionNotes = CASE
        WHEN COALESCE(DecisionNotes, '') = '' THEN $decisionNotes
        ELSE DecisionNotes || ' ' || $decisionNotes
    END,
    LastUpdatedDate = COALESCE(LastUpdatedDate, date('now'))
WHERE FightId = $fightId
  AND FighterId = $fighterId;";
                    cmd.Parameters.AddWithValue("$choice", optionKey);
                    cmd.Parameters.AddWithValue("$performanceModifier", performanceModifier);
                    cmd.Parameters.AddWithValue("$riskModifier", riskModifier);
                    cmd.Parameters.AddWithValue("$decisionNotes", decisionNotes);
                    cmd.Parameters.AddWithValue("$fightId", weightCutFightId);
                    cmd.Parameters.AddWithValue("$fighterId", fighterId2);
                    await cmd.ExecuteNonQueryAsync();

                    if (!protect)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + $heatGain),
    ReliabilityScore = MAX(15, COALESCE(ReliabilityScore, 60) - $reliabilityLoss)
WHERE Id = $fighterId;",
                            ("$fighterId", fighterId2),
                            ("$heatGain", 2 + (personality.Showmanship >= 70 ? 2 : 0)),
                            ("$reliabilityLoss", personality.Discipline >= 70 ? 1 : 2));
                    }

                    await InsertDecisionMessageAsync(conn, tx, agentId, "WeightCutDecision", "Weight cut decision made", decisionNotes);
                    return decisionNotes;
                }
                break;

            case "SponsorSpotlight":
                if (decision.FighterId is int fighterId3)
                {
                    var personality = await LoadDecisionPersonalityAsync(conn, tx, fighterId3);
                    var adCampaign = string.Equals(optionKey, "AdCampaign", StringComparison.OrdinalIgnoreCase);
                    if (adCampaign)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Money = COALESCE(Money, 0) + 2200,
    PublicReputation = MIN(99, COALESCE(PublicReputation, 50) + 2)
WHERE Id = $agentId;", ("$agentId", agentId));

                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + $heatGain),
    Marketability = MIN(99, COALESCE(Marketability, 50) + $marketabilityGain),
    Momentum = MIN(99, COALESCE(Momentum, 50) + $momentumGain)
WHERE Id = $fighterId;",
                            ("$fighterId", fighterId3),
                            ("$heatGain", 4 + (personality.Showmanship >= 70 ? 3 : 1)),
                            ("$marketabilityGain", 3 + (personality.Showmanship >= 75 ? 3 : 1)),
                            ("$momentumGain", 1 + (personality.Ambition >= 68 ? 2 : 1)));

                        await ExecAsync(conn, tx, @"
INSERT INTO AgentTransactions (AgentId, TxDate, Amount, TxType, Notes)
VALUES ($agentId, date('now'), 2200, 'SponsorDeal', 'Commercial activation accepted.');",
                            ("$agentId", agentId));
                    }
                    else
                    {
                        await ExecAsync(conn, tx, @"
UPDATE FighterStates
SET Morale = MIN(95, COALESCE(Morale, 50) + $moraleGain),
    Sharpness = MIN(95, COALESCE(Sharpness, 50) + $sharpnessGain)
WHERE FighterId = $fighterId;",
                            ("$fighterId", fighterId3),
                            ("$moraleGain", personality.Stability >= 68 ? 5 : 4),
                            ("$sharpnessGain", personality.Discipline >= 68 ? 4 : 3));

                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET FighterTrust = MIN(99, COALESCE(FighterTrust, 50) + 2)
WHERE Id = $agentId;", ("$agentId", agentId));
                    }

                    var summary = adCampaign
                        ? "The fighter took the ad spot, bringing money and visibility while making camp a little louder."
                        : "The team skipped the commercial noise and kept the fighter locked into preparation.";

                    await InsertDecisionMessageAsync(conn, tx, agentId, "SponsorDecision", "Commercial decision made", summary);
                    return summary;
                }
                break;

            case "PressInterview":
                if (decision.FighterId is int fighterId4)
                {
                    var personality = await LoadDecisionPersonalityAsync(conn, tx, fighterId4);
                    var stayMeasured = string.Equals(optionKey, "StayMeasured", StringComparison.OrdinalIgnoreCase);
                    if (stayMeasured)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET ReliabilityScore = MIN(99, COALESCE(ReliabilityScore, 60) + $reliabilityGain),
    Popularity = MIN(100, COALESCE(Popularity, 50) + 1),
    MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 1),
    Momentum = MIN(99, COALESCE(Momentum, 50) + 1)
WHERE Id = $fighterId;",
                            ("$fighterId", fighterId4),
                            ("$reliabilityGain", personality.Stability >= 68 ? 4 : 3));

                        await ExecAsync(conn, tx, @"
UPDATE FighterStates
SET Morale = MIN(95, COALESCE(Morale, 50) + 2)
WHERE FighterId = $fighterId;", ("$fighterId", fighterId4));

                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Reputation = MIN(99, COALESCE(Reputation, 50) + 1),
    FighterTrust = MIN(99, COALESCE(FighterTrust, 50) + 1),
    PromotionLeverage = MIN(99, COALESCE(PromotionLeverage, 50) + 1)
WHERE Id = $agentId;", ("$agentId", agentId));
                    }
                    else
                    {
                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + $heatGain),
    Marketability = MIN(99, COALESCE(Marketability, 50) + $marketabilityGain),
    Momentum = MIN(99, COALESCE(Momentum, 50) + $momentumGain),
    ReliabilityScore = MAX(15, COALESCE(ReliabilityScore, 60) - $reliabilityLoss)
WHERE Id = $fighterId;",
                            ("$fighterId", fighterId4),
                            ("$heatGain", 5 + (personality.Showmanship >= 72 ? 3 : 0)),
                            ("$marketabilityGain", 3 + (personality.Showmanship >= 72 ? 2 : 0)),
                            ("$momentumGain", 2 + (personality.Ambition >= 68 ? 1 : 0)),
                            ("$reliabilityLoss", personality.Stability >= 70 ? 2 : 3));

                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET PublicReputation = MIN(99, COALESCE(PublicReputation, 50) + 2)
WHERE Id = $agentId;", ("$agentId", agentId));
                    }

                    var summary = stayMeasured
                        ? "The interview stayed measured. The fighter gave off a controlled, professional tone and the room stayed calmer."
                        : "The fighter called a louder shot in the media. Attention spiked, but so did the noise around the camp.";

                    await InsertDecisionMessageAsync(conn, tx, agentId, "PressDecision", "Media decision made", summary);
                    return summary;
                }
                break;

            case "GymOpportunity":
                if (decision.FighterId is int fighterId5)
                {
                    var personality = await LoadDecisionPersonalityAsync(conn, tx, fighterId5);
                    var bringSpecialists = string.Equals(optionKey, "BringSpecialists", StringComparison.OrdinalIgnoreCase);
                    if (bringSpecialists)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Money = COALESCE(Money, 0) - 2400,
    FighterTrust = MIN(99, COALESCE(FighterTrust, 50) + 1)
WHERE Id = $agentId;", ("$agentId", agentId));

                        await ExecAsync(conn, tx, @"
INSERT INTO AgentTransactions (AgentId, TxDate, Amount, TxType, Notes)
VALUES ($agentId, date('now'), -2400, 'GymUpgrade', 'Outside specialists brought into camp.');",
                            ("$agentId", agentId));

                        await ExecAsync(conn, tx, @"
UPDATE FighterStates
SET CampQuality = MIN(95, COALESCE(CampQuality, 50) + $campGain),
    Sharpness = MIN(95, COALESCE(Sharpness, 50) + $sharpnessGain),
    Energy = MAX(15, COALESCE(Energy, 50) - $energyCost)
WHERE FighterId = $fighterId;",
                            ("$fighterId", fighterId5),
                            ("$campGain", 5 + (personality.Discipline >= 68 ? 2 : 1)),
                            ("$sharpnessGain", 3 + (personality.Ambition >= 68 ? 2 : 1)),
                            ("$energyCost", personality.Stability >= 70 ? 1 : 2));

                        if (decision.FightId is int gymFightId)
                        {
                            await ExecAsync(conn, tx, @"
UPDATE FightPreparations
SET ManagerDecisionType = 'GymOpportunity',
    ManagerDecisionChoice = $choice,
    PerformanceModifier = COALESCE(PerformanceModifier, 0) + $performanceModifier,
    RiskModifier = COALESCE(RiskModifier, 0) + $riskModifier,
    DecisionNotes = CASE
        WHEN COALESCE(DecisionNotes, '') = '' THEN $notes
        ELSE DecisionNotes || ' ' || $notes
    END,
    LastUpdatedDate = COALESCE(LastUpdatedDate, date('now'))
WHERE FightId = $fightId
  AND FighterId = $fighterId;", 
                                ("$choice", optionKey),
                                ("$performanceModifier", personality.Discipline >= 68 ? 3 : 2),
                                ("$riskModifier", personality.Stability >= 68 ? 0 : 1),
                                ("$notes", "Outside specialists were brought in late to sharpen the camp."),
                                ("$fightId", gymFightId),
                                ("$fighterId", fighterId5));
                        }
                    }
                    else
                    {
                        await ExecAsync(conn, tx, @"
UPDATE FighterStates
SET Morale = MIN(95, COALESCE(Morale, 50) + 2),
    Energy = MIN(95, COALESCE(Energy, 50) + 1)
WHERE FighterId = $fighterId;", ("$fighterId", fighterId5));

                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET FighterTrust = MIN(99, COALESCE(FighterTrust, 50) + 1)
WHERE Id = $agentId;", ("$agentId", agentId));
                    }

                    var summary = bringSpecialists
                        ? "You paid for outside specialists. Camp quality spiked, but it cost money and added a bit more intensity."
                        : "You kept the core routine together. The room stayed stable, and the fighter held rhythm without extra expense.";

                    await InsertDecisionMessageAsync(conn, tx, agentId, "GymDecision", "Camp decision made", summary);
                    return summary;
                }
                break;
        }

        return "Decision recorded.";
    }

    private static async Task<DecisionPersonalitySnapshot> LoadDecisionPersonalityAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int fighterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    COALESCE(Ambition, 50),
    COALESCE(Discipline, 50),
    COALESCE(RiskTolerance, 50),
    COALESCE(Stability, 50),
    COALESCE(Showmanship, 40)
FROM Fighters
WHERE Id = $fighterId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new DecisionPersonalitySnapshot(50, 50, 50, 50, 40);

        return new DecisionPersonalitySnapshot(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)),
            Convert.ToInt32(reader.GetValue(3)),
            Convert.ToInt32(reader.GetValue(4)));
    }

    private async Task<IReadOnlyList<FightOfferVm>> LoadFightOffersAsync(int agentId)
    {
        var list = new List<FightOfferVm>();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT fo.Id,
       fo.FighterId,
       (f.FirstName || ' ' || f.LastName) AS FighterName,
       fo.OpponentFighterId,
       (o.FirstName || ' ' || o.LastName) AS OpponentName,
       fo.PromotionId,
       COALESCE(p.Name, 'Unknown Promotion') AS PromotionName,
       fo.Purse,
       fo.WinBonus,
       fo.WeeksUntilFight,
       COALESCE(fo.CampWeeksOffered, 0) AS CampWeeksOffered,
       COALESCE(fo.IsTitleFight, 0) AS IsTitleFight,
       COALESCE(fo.IsTitleEliminator, 0) AS IsTitleEliminator,
       COALESCE(fo.IsShortNotice, 0) AS IsShortNotice,
       fo.Notes,
       fo.Status
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = fo.FighterId
JOIN Fighters o ON o.Id = fo.OpponentFighterId
LEFT JOIN Promotions p ON p.Id = fo.PromotionId
ORDER BY CASE WHEN fo.Status = 'Pending' THEN 0 ELSE 1 END,
         fo.Id DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new FightOfferVm
            {
                OfferId = Convert.ToInt32(reader["Id"]),
                FighterId = Convert.ToInt32(reader["FighterId"]),
                FighterName = reader["FighterName"]?.ToString() ?? "",
                OpponentId = Convert.ToInt32(reader["OpponentFighterId"]),
                OpponentName = reader["OpponentName"]?.ToString() ?? "",
                PromotionId = reader["PromotionId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["PromotionId"]),
                PromotionName = reader["PromotionName"]?.ToString() ?? "",
                Purse = Convert.ToInt32(reader["Purse"]),
                WinBonus = Convert.ToInt32(reader["WinBonus"]),
                WeeksUntilFight = Convert.ToInt32(reader["WeeksUntilFight"]),
                CampWeeksOffered = Convert.ToInt32(reader["CampWeeksOffered"]),
                IsTitleFight = Convert.ToInt32(reader["IsTitleFight"]) == 1,
                IsTitleEliminator = Convert.ToInt32(reader["IsTitleEliminator"]) == 1,
                IsShortNotice = Convert.ToInt32(reader["IsShortNotice"]) == 1,
                Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString(),
                Status = reader["Status"]?.ToString() ?? ""
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<ContractOfferVm>> LoadContractOffersAsync(int agentId)
    {
        var list = new List<ContractOfferVm>();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT co.Id,
       co.FighterId,
       (f.FirstName || ' ' || f.LastName) AS FighterName,
       co.PromotionId,
       COALESCE(p.Name, 'Unknown Promotion') AS PromotionName,
       co.OfferedFights,
       co.BasePurse,
       co.WinBonus,
       COALESCE(co.SigningBonus, 0) AS SigningBonus,
       COALESCE(co.ExclusivityType, 'Exclusive') AS ExclusivityType,
       co.WeeksToRespond,
       co.Status,
       co.SourceType,
       co.Notes,
       COALESCE(f.Age, 28) AS FighterAge,
       COALESCE(f.Popularity, 50) AS FighterPopularity,
       COALESCE(f.ReliabilityScore, 60) AS FighterReliability,
       COALESCE(f.Marketability, 50) AS FighterMarketability,
       COALESCE(p.Prestige, 50) AS PromotionPrestige,
       COALESCE(p.Budget, 0) AS PromotionBudget
FROM ContractOffers co
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1
JOIN Fighters f ON f.Id = co.FighterId
LEFT JOIN Promotions p ON p.Id = co.PromotionId
ORDER BY CASE WHEN co.Status = 'Pending' THEN 0 ELSE 1 END,
         co.Id DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ContractOfferVm
            {
                Id = Convert.ToInt32(reader["Id"]),
                FighterId = Convert.ToInt32(reader["FighterId"]),
                FighterName = reader["FighterName"]?.ToString() ?? "",
                PromotionId = Convert.ToInt32(reader["PromotionId"]),
                PromotionName = reader["PromotionName"]?.ToString() ?? "",
                OfferedFights = Convert.ToInt32(reader["OfferedFights"]),
                BasePurse = Convert.ToInt32(reader["BasePurse"]),
                WinBonus = Convert.ToInt32(reader["WinBonus"]),
                SigningBonus = Convert.ToInt32(reader["SigningBonus"]),
                ExclusivityType = reader["ExclusivityType"]?.ToString() ?? "Exclusive",
                WeeksToRespond = Convert.ToInt32(reader["WeeksToRespond"]),
                Status = reader["Status"]?.ToString() ?? "",
                SourceType = reader["SourceType"]?.ToString() ?? "",
                Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString(),
                Recommendation = BuildContractRecommendation(
                    Convert.ToInt32(reader["OfferedFights"]),
                    Convert.ToInt32(reader["BasePurse"]),
                    Convert.ToInt32(reader["WinBonus"]),
                    Convert.ToInt32(reader["SigningBonus"]),
                    reader["ExclusivityType"]?.ToString() ?? "Exclusive",
                    reader["SourceType"]?.ToString() ?? "",
                    Convert.ToInt32(reader["FighterAge"]),
                    Convert.ToInt32(reader["FighterPopularity"]),
                    Convert.ToInt32(reader["FighterReliability"]),
                    Convert.ToInt32(reader["FighterMarketability"]),
                    Convert.ToInt32(reader["PromotionPrestige"]),
                    Convert.ToInt32(reader["PromotionBudget"]))
            });
        }

        return list;
    }

    private async Task<IReadOnlyList<DecisionEventVm>> LoadDecisionEventsAsync(int agentId)
    {
        var items = new List<DecisionEventVm>();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FighterId, DecisionType, Headline, Body,
       OptionAKey, OptionALabel, OptionADescription,
       OptionBKey, OptionBLabel, OptionBDescription,
       CreatedDate, Status
FROM DecisionEvents
WHERE AgentId = $agentId
  AND Status = 'Pending'
ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new DecisionEventVm
            {
                Id = Convert.ToInt32(reader["Id"]),
                FighterId = reader["FighterId"] == DBNull.Value ? null : Convert.ToInt32(reader["FighterId"]),
                DecisionType = reader["DecisionType"]?.ToString() ?? "",
                Headline = reader["Headline"]?.ToString() ?? "",
                Body = reader["Body"]?.ToString() ?? "",
                OptionAKey = reader["OptionAKey"]?.ToString() ?? "",
                OptionALabel = reader["OptionALabel"]?.ToString() ?? "",
                OptionADescription = reader["OptionADescription"] == DBNull.Value ? null : reader["OptionADescription"]?.ToString(),
                OptionBKey = reader["OptionBKey"]?.ToString() ?? "",
                OptionBLabel = reader["OptionBLabel"]?.ToString() ?? "",
                OptionBDescription = reader["OptionBDescription"] == DBNull.Value ? null : reader["OptionBDescription"]?.ToString(),
                CreatedDate = reader["CreatedDate"]?.ToString() ?? "",
                Status = reader["Status"]?.ToString() ?? ""
            });
        }

        return items;
    }

    private static string BuildContractRecommendation(
        int offeredFights,
        int basePurse,
        int winBonus,
        int signingBonus,
        string exclusivityType,
        string sourceType,
        int fighterAge,
        int fighterPopularity,
        int fighterReliability,
        int fighterMarketability,
        int promotionPrestige,
        int promotionBudget)
    {
        if (fighterAge >= 33 && offeredFights >= 4)
            return "Security";

        if (promotionPrestige >= 78 && fighterPopularity < 62 && fighterMarketability < 62)
            return "Exposure";

        if (signingBonus >= 3500 || winBonus >= Math.Max(2800, (int)(basePurse * 0.45)))
            return "Bonus";

        if (string.Equals(exclusivityType, "Open", StringComparison.OrdinalIgnoreCase) && fighterAge >= 31)
            return "Take as-is";

        if (basePurse >= 12000 || winBonus >= 4500)
            return "Money";

        if (fighterReliability < 52 && promotionPrestige >= 70)
            return "Take as-is";

        if (promotionBudget < 240000 && (basePurse >= 9000 || winBonus >= 3200))
            return "Take as-is";

        if (offeredFights >= 4)
            return "Security";

        if (string.Equals(sourceType, "Renewal", StringComparison.OrdinalIgnoreCase))
            return "Stability";

        return "Exposure";
    }

    private static async Task InsertDecisionMessageAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string messageType,
        string subject,
        string body)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO InboxMessages (AgentId, MessageType, Subject, Body, CreatedDate, IsRead, IsArchived, IsDeleted)
VALUES ($agentId, $messageType, $subject, $body, date('now'), 0, 0, 0);";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$messageType", messageType);
        cmd.Parameters.AddWithValue("$subject", subject);
        cmd.Parameters.AddWithValue("$body", body);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record DecisionSnapshot(
        int Id,
        int? FighterId,
        int? FightId,
        string DecisionType,
        string OptionAKey,
        string OptionBKey);

    private sealed record DecisionPersonalitySnapshot(
        int Ambition,
        int Discipline,
        int RiskTolerance,
        int Stability,
        int Showmanship);
}
