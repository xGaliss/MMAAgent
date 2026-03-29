using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class ContractOfferResponseServiceSqlite : IContractOfferResponseService
{
    private readonly IContractOfferRepository _contractOfferRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly SqliteConnectionFactory _factory;

    public ContractOfferResponseServiceSqlite(
        IContractOfferRepository contractOfferRepository,
        IInboxRepository inboxRepository,
        SqliteConnectionFactory factory)
    {
        _contractOfferRepository = contractOfferRepository;
        _inboxRepository = inboxRepository;
        _factory = factory;
    }

    public async Task<ServiceResult> AcceptAsync(int contractOfferId, CancellationToken cancellationToken = default)
    {
        var offer = await _contractOfferRepository.GetByIdAsync(contractOfferId, cancellationToken);
        if (offer is null)
            return ServiceResult.Fail("Contract offer not found.");

        if (!string.Equals(offer.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return ServiceResult.Fail("This contract offer is no longer pending.");

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = $promotionId,
    ContractFightsRemaining = $contractFightsRemaining,
    BasePurse = $basePurse,
    WinBonus = $winBonus
WHERE Id = $fighterId;";
            cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
            cmd.Parameters.AddWithValue("$contractFightsRemaining", offer.OfferedFights);
            cmd.Parameters.AddWithValue("$basePurse", offer.BasePurse);
            cmd.Parameters.AddWithValue("$winBonus", offer.WinBonus);
            cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ContractOffers
SET Status = 'Accepted',
    RespondedDate = $respondedDate
WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$respondedDate", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", offer.Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE ContractOffers
SET Status = 'Withdrawn',
    RespondedDate = $respondedDate
WHERE FighterId = $fighterId
  AND Id <> $id
  AND Status = 'Pending';";
            cmd.Parameters.AddWithValue("$respondedDate", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$fighterId", offer.FighterId);
            cmd.Parameters.AddWithValue("$id", offer.Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();

        var agentId = await LoadAgentIdAsync(offer.FighterId, cancellationToken);
        if (agentId.HasValue)
        {
            var promotionName = await LoadPromotionNameAsync(offer.PromotionId, cancellationToken);
            await _inboxRepository.CreateAsync(new InboxMessage
            {
                AgentId = agentId.Value,
                MessageType = "ContractAccepted",
                Subject = $"Contract accepted for fighter {offer.FighterId}",
                Body = $"Your fighter has signed with {promotionName} for {offer.OfferedFights} fights.",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            }, cancellationToken);
        }

        return ServiceResult.Ok("Contract offer accepted.");
    }

    public async Task<ServiceResult> RejectAsync(int contractOfferId, CancellationToken cancellationToken = default)
    {
        var offer = await _contractOfferRepository.GetByIdAsync(contractOfferId, cancellationToken);
        if (offer is null)
            return ServiceResult.Fail("Contract offer not found.");

        if (!string.Equals(offer.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return ServiceResult.Fail("This contract offer is no longer pending.");

        await _contractOfferRepository.UpdateStatusAsync(
            offer.Id,
            "Rejected",
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            cancellationToken);

        var agentId = await LoadAgentIdAsync(offer.FighterId, cancellationToken);
        if (agentId.HasValue)
        {
            var promotionName = await LoadPromotionNameAsync(offer.PromotionId, cancellationToken);
            await _inboxRepository.CreateAsync(new InboxMessage
            {
                AgentId = agentId.Value,
                MessageType = "ContractRejected",
                Subject = $"Contract rejected for fighter {offer.FighterId}",
                Body = $"You rejected the contract offer from {promotionName}.",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            }, cancellationToken);
        }

        return ServiceResult.Ok("Contract offer rejected.");
    }

    private async Task<int?> LoadAgentIdAsync(int fighterId, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AgentId FROM ManagedFighters WHERE FighterId = $fighterId LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private async Task<string> LoadPromotionNameAsync(int promotionId, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Promotions WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", promotionId);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value?.ToString() ?? $"Promotion {promotionId}";
    }
}
