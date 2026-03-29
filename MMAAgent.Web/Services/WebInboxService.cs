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
            ContractOffers = contractOffers
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
       COALESCE(fo.IsTitleFight, 0) AS IsTitleFight,
       fo.Status
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId AND mf.AgentId = $agentId
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
                IsTitleFight = Convert.ToInt32(reader["IsTitleFight"]) == 1,
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
JOIN ManagedFighters mf ON mf.FighterId = co.FighterId AND mf.AgentId = $agentId
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
                Notes = reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString()
            });
        }

        return list;
    }
}
