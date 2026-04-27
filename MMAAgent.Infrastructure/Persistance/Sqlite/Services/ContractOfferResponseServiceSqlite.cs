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
        => await AcceptInternalAsync(contractOfferId, "Standard", cancellationToken);

    public async Task<ServiceResult> AcceptForMoneyAsync(int contractOfferId, CancellationToken cancellationToken = default)
        => await AcceptInternalAsync(contractOfferId, "Money", cancellationToken);

    public async Task<ServiceResult> AcceptForSecurityAsync(int contractOfferId, CancellationToken cancellationToken = default)
        => await AcceptInternalAsync(contractOfferId, "Security", cancellationToken);

    public async Task<ServiceResult> AcceptForExposureAsync(int contractOfferId, CancellationToken cancellationToken = default)
        => await AcceptInternalAsync(contractOfferId, "Exposure", cancellationToken);

    private async Task<ServiceResult> AcceptInternalAsync(int contractOfferId, string strategy, CancellationToken cancellationToken)
    {
        var offer = await _contractOfferRepository.GetByIdAsync(contractOfferId, cancellationToken);
        if (offer is null)
            return ServiceResult.Fail("Contract offer not found.");

        if (!string.Equals(offer.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return ServiceResult.Fail("This contract offer is no longer pending.");

        using var preConn = _factory.CreateConnection();
        var fighterSignals = await LoadFighterSignalsAsync(preConn, offer.FighterId, cancellationToken);
        var promotionSignals = await LoadPromotionSignalsAsync(preConn, offer.PromotionId, cancellationToken);
        var negotiation = BuildNegotiationOutcome(offer, fighterSignals, promotionSignals, strategy);

        if (!negotiation.Accepted)
            return ServiceResult.Fail(negotiation.Message);

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
    WinBonus = $winBonus,
    Popularity = MIN(100, COALESCE(Popularity, 50) + $popularityDelta),
    Marketability = MIN(99, COALESCE(Marketability, 50) + $marketabilityDelta),
    MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + $mediaHeatDelta)
WHERE Id = $fighterId;";
            cmd.Parameters.AddWithValue("$promotionId", offer.PromotionId);
            cmd.Parameters.AddWithValue("$contractFightsRemaining", negotiation.OfferedFights);
            cmd.Parameters.AddWithValue("$basePurse", negotiation.BasePurse);
            cmd.Parameters.AddWithValue("$winBonus", negotiation.WinBonus);
            cmd.Parameters.AddWithValue("$popularityDelta", negotiation.PopularityDelta);
            cmd.Parameters.AddWithValue("$marketabilityDelta", negotiation.MarketabilityDelta);
            cmd.Parameters.AddWithValue("$mediaHeatDelta", negotiation.MediaHeatDelta);
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
                Body = $"Your fighter has signed with {promotionName} for {negotiation.OfferedFights} fights. {negotiation.Message}",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            }, cancellationToken);
        }

        return ServiceResult.Ok(negotiation.Message);
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

    private async Task<FighterSignals> LoadFighterSignalsAsync(SqliteConnection conn, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(Popularity, 50),
    COALESCE(MediaHeat, 20),
    COALESCE(ReliabilityScore, 60),
    COALESCE(Marketability, 50),
    COALESCE(Age, 28)
FROM Fighters
WHERE Id = $fighterId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new FighterSignals(50, 20, 60, 50, 28);

        return new FighterSignals(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)),
            Convert.ToInt32(reader.GetValue(3)),
            Convert.ToInt32(reader.GetValue(4)));
    }

    private async Task<PromotionSignals> LoadPromotionSignalsAsync(SqliteConnection conn, int promotionId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(Prestige, 50),
    COALESCE(Budget, 0)
FROM Promotions
WHERE Id = $promotionId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$promotionId", promotionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new PromotionSignals(50, 0);

        return new PromotionSignals(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)));
    }

    private static NegotiationOutcome BuildNegotiationOutcome(
        ContractOffer offer,
        FighterSignals fighter,
        PromotionSignals promotion,
        string strategy)
    {
        var leverage = (fighter.Popularity + fighter.MediaHeat + fighter.ReliabilityScore + fighter.Marketability) / 4;
        var promotionStrictness = Math.Max(0, (promotion.Prestige - 55) / 8) + Math.Max(0, (400000 - promotion.Budget) / 120000);
        var accepted = strategy == "Standard" || leverage + 6 >= 40 + (promotionStrictness * 6);

        if (!accepted)
        {
            return new NegotiationOutcome(false, offer.OfferedFights, offer.BasePurse, offer.WinBonus, 0, 0, 0,
                "The promotion held firm and the original deal remains the best available option right now.");
        }

        return strategy switch
        {
            "Money" => new NegotiationOutcome(
                true,
                Math.Max(1, offer.OfferedFights - (offer.OfferedFights >= 4 ? 1 : 0)),
                (int)Math.Round(offer.BasePurse * 1.12),
                (int)Math.Round(offer.WinBonus * 1.10),
                0,
                1,
                0,
                "You pushed for money and landed a richer deal, with a little less long-term security."),
            "Security" => new NegotiationOutcome(
                true,
                offer.OfferedFights + 1,
                (int)Math.Round(offer.BasePurse * 0.95),
                offer.WinBonus,
                0,
                0,
                0,
                "You pushed for stability and secured extra fights, though the money softened slightly."),
            "Exposure" => new NegotiationOutcome(
                true,
                offer.OfferedFights,
                (int)Math.Round(offer.BasePurse * 1.04),
                (int)Math.Round(offer.WinBonus * 1.04),
                2,
                3,
                5,
                "You pushed for exposure and came away with a slightly better deal plus more spotlight around the signing."),
            _ => new NegotiationOutcome(
                true,
                offer.OfferedFights,
                offer.BasePurse,
                offer.WinBonus,
                0,
                0,
                0,
                "You accepted the original offer and kept the process clean.")
        };
    }

    private sealed record FighterSignals(int Popularity, int MediaHeat, int ReliabilityScore, int Marketability, int Age);
    private sealed record PromotionSignals(int Prestige, int Budget);
    private sealed record NegotiationOutcome(
        bool Accepted,
        int OfferedFights,
        int BasePurse,
        int WinBonus,
        int PopularityDelta,
        int MarketabilityDelta,
        int MediaHeatDelta,
        string Message);
}
