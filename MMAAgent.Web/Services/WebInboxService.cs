using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebInboxService
{
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IFightOfferRepository _fightOfferRepository;
    private readonly IFighterRepository _fighterRepository;

    public WebInboxService(
        IAgentProfileRepository agentRepository,
        IInboxRepository inboxRepository,
        IFightOfferRepository fightOfferRepository,
        IFighterRepository fighterRepository)
    {
        _agentRepository = agentRepository;
        _inboxRepository = inboxRepository;
        _fightOfferRepository = fightOfferRepository;
        _fighterRepository = fighterRepository;
    }

    public async Task<(IReadOnlyList<InboxMessageVm> Messages, IReadOnlyList<FightOfferVm> Offers)> LoadAsync()
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return (Array.Empty<InboxMessageVm>(), Array.Empty<FightOfferVm>());

        var messages = await _inboxRepository.GetByAgentAsync(agent.Id);
        var offers = await _fightOfferRepository.GetByAgentAsync(agent.Id);
        var roster = await _fighterRepository.GetRosterAsync(500);

        var messageRows = messages
            .Select(x => new InboxMessageVm(
                x.Id,
                x.MessageType,
                x.Subject,
                x.Body,
                x.CreatedDate,
                x.IsRead))
            .ToList();

        var offerRows = offers
            .Select(x => new FightOfferVm(
                x.Id,
                x.FighterId,
                roster.FirstOrDefault(r => r.Id == x.FighterId)?.Name ?? $"Fighter {x.FighterId}",
                x.OpponentFighterId,
                roster.FirstOrDefault(r => r.Id == x.OpponentFighterId)?.Name ?? $"Fighter {x.OpponentFighterId}",
                x.Purse,
                x.WinBonus,
                x.WeeksUntilFight,
                x.IsTitleFight,
                x.Status))
            .ToList();

        return (messageRows, offerRows);
    }

    public Task MarkMessageAsReadAsync(int messageId) =>
        _inboxRepository.MarkAsReadAsync(messageId);

    public async Task AcceptOfferAsync(int offerId)
    {
        await UpdateOfferStatusAsync(offerId, "Accepted", "Oferta aceptada");
    }

    public async Task RejectOfferAsync(int offerId)
    {
        await UpdateOfferStatusAsync(offerId, "Rejected", "Oferta rechazada");
    }

    private async Task UpdateOfferStatusAsync(int offerId, string status, string subject)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            throw new InvalidOperationException("No se encontró el agente.");

        var result = await LoadAsync();
        var offer = result.Offers.FirstOrDefault(x => x.OfferId == offerId);
        if (offer == null)
            throw new InvalidOperationException("No se encontró la oferta.");

        await _fightOfferRepository.UpdateStatusAsync(offerId, status);

        await _inboxRepository.CreateAsync(new InboxMessage
        {
            AgentId = agent.Id,
            MessageType = "FightOfferResponse",
            Subject = subject,
            Body = $"Oferta #{offerId}: {offer.FighterName} vs {offer.OpponentName}.",
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IsRead = false
        });
    }
}
