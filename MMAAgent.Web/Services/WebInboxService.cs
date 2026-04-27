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
                    var performanceModifier = string.Equals(optionKey, "QuietCamp", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                    var riskModifier = string.Equals(optionKey, "QuietCamp", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                    var decisionNotes = string.Equals(optionKey, "QuietCamp", StringComparison.OrdinalIgnoreCase)
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

                    if (!string.Equals(optionKey, "QuietCamp", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 5),
    Marketability = MIN(99, COALESCE(Marketability, 50) + 3),
    Momentum = MIN(99, COALESCE(Momentum, 50) + 1)
WHERE Id = $fighterId;", ("$fighterId", fighterId));
                    }

                    await InsertDecisionMessageAsync(conn, tx, agentId, "FightWeekDecision", "Fight week call made", decisionNotes);
                    return decisionNotes;
                }
                break;

            case "WeightCutCall":
                if (decision.FightId is int weightCutFightId && decision.FighterId is int fighterId2)
                {
                    var protect = string.Equals(optionKey, "ProtectFighter", StringComparison.OrdinalIgnoreCase);
                    var performanceModifier = protect ? 1 : -2;
                    var riskModifier = protect ? -2 : 3;
                    var decisionNotes = protect
                        ? "The team eased the final cut and prioritized recovery for fight night."
                        : "The team forced the cut all the way through to chase the event upside.";

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
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 3),
    ReliabilityScore = MAX(15, COALESCE(ReliabilityScore, 60) - 2)
WHERE Id = $fighterId;", ("$fighterId", fighterId2));
                    }

                    await InsertDecisionMessageAsync(conn, tx, agentId, "WeightCutDecision", "Weight cut decision made", decisionNotes);
                    return decisionNotes;
                }
                break;

            case "SponsorSpotlight":
                if (decision.FighterId is int fighterId3)
                {
                    var adCampaign = string.Equals(optionKey, "AdCampaign", StringComparison.OrdinalIgnoreCase);
                    if (adCampaign)
                    {
                        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Money = COALESCE(Money, 0) + 2200
WHERE Id = $agentId;", ("$agentId", agentId));

                        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 6),
    Marketability = MIN(99, COALESCE(Marketability, 50) + 5),
    Momentum = MIN(99, COALESCE(Momentum, 50) + 2)
WHERE Id = $fighterId;", ("$fighterId", fighterId3));

                        await ExecAsync(conn, tx, @"
INSERT INTO AgentTransactions (AgentId, TxDate, Amount, TxType, Notes)
VALUES ($agentId, date('now'), 2200, 'SponsorDeal', 'Commercial activation accepted.');",
                            ("$agentId", agentId));
                    }
                    else
                    {
                        await ExecAsync(conn, tx, @"
UPDATE FighterStates
SET Morale = MIN(95, COALESCE(Morale, 50) + 4),
    Sharpness = MIN(95, COALESCE(Sharpness, 50) + 3)
WHERE FighterId = $fighterId;", ("$fighterId", fighterId3));
                    }

                    var summary = adCampaign
                        ? "The fighter took the ad spot, bringing money and visibility at the cost of a noisier week."
                        : "The team skipped the commercial noise and kept the fighter locked into preparation.";

                    await InsertDecisionMessageAsync(conn, tx, agentId, "SponsorDecision", "Commercial decision made", summary);
                    return summary;
                }
                break;
        }

        return "Decision recorded.";
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
       co.WeeksToRespond,
       co.Status,
       co.SourceType,
       co.Notes
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
                WeeksToRespond = Convert.ToInt32(reader["WeeksToRespond"]),
                Status = reader["Status"]?.ToString() ?? "",
                SourceType = reader["SourceType"]?.ToString() ?? "",
                Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString(),
                Recommendation = BuildContractRecommendation(
                    Convert.ToInt32(reader["OfferedFights"]),
                    Convert.ToInt32(reader["BasePurse"]),
                    Convert.ToInt32(reader["WinBonus"]),
                    reader["SourceType"]?.ToString() ?? "")
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

    private static string BuildContractRecommendation(int offeredFights, int basePurse, int winBonus, string sourceType)
    {
        if (offeredFights >= 4)
            return "Security";

        if (basePurse >= 12000 || winBonus >= 4500)
            return "Money";

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
}
