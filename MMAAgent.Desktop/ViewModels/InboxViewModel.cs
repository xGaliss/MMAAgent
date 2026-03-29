using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMAAgent.Application.Abstractions;
using MMAAgent.Desktop.Services;
using MMAAgent.Desktop.ViewModels.Models;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class InboxViewModel : ObservableObject
    {
        private readonly IAgentProfileRepository _agentRepo;
        private readonly IInboxRepository _inboxRepo;
        private readonly IFightOfferRepository _fightOfferRepo;
        private readonly IFighterRepository _fighterRepo;
        private readonly GenerateFightOfferService _generateFightOfferService;

        public ObservableCollection<InboxMessageRow> Messages { get; } = new();
        public ObservableCollection<FightOfferRow> Offers { get; } = new();

        private FightOfferRow? _selectedOffer;
        public FightOfferRow? SelectedOffer
        {
            get => _selectedOffer;
            set => SetProperty(ref _selectedOffer, value);
        }

        private string _titleText = "Inbox";
        public string TitleText
        {
            get => _titleText;
            set => SetProperty(ref _titleText, value);
        }

        private string _statusText = "Sin mensajes.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand GenerateTestOfferCommand { get; }
        public ICommand AcceptSelectedOfferCommand { get; }
        public ICommand RejectSelectedOfferCommand { get; }

        public InboxViewModel(
            IAgentProfileRepository agentRepo,
            IInboxRepository inboxRepo,
            IFightOfferRepository fightOfferRepo,
            IFighterRepository fighterRepo,
            GenerateFightOfferService generateFightOfferService)
        {
            _agentRepo = agentRepo;
            _inboxRepo = inboxRepo;
            _fightOfferRepo = fightOfferRepo;
            _fighterRepo = fighterRepo;
            _generateFightOfferService = generateFightOfferService;

            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            GenerateTestOfferCommand = new RelayCommand(async () => await GenerateTestOfferAsync());
            AcceptSelectedOfferCommand = new RelayCommand(async () => await AcceptSelectedAsync());
            RejectSelectedOfferCommand = new RelayCommand(async () => await RejectSelectedAsync());
        }

        public async Task LoadAsync()
        {
            Messages.Clear();
            Offers.Clear();

            var agent = await _agentRepo.GetAsync();
            if (agent == null)
            {
                StatusText = "No se encontró el agente.";
                return;
            }

            var messages = await _inboxRepo.ListAsync(agent.Id, null, false, false);
            foreach (var item in messages)
            {
                Messages.Add(new InboxMessageRow
                {
                    MessageId = item.Id,
                    MessageType = item.MessageType,
                    Subject = item.Subject,
                    Body = item.Body,
                    CreatedDate = item.CreatedDate,
                    IsRead = item.IsRead
                });
            }

            var offers = await _fightOfferRepo.GetByAgentAsync(agent.Id);
            var roster = await _fighterRepo.GetRosterAsync(500);

            foreach (var item in offers)
            {
                var fighterName = roster.FirstOrDefault(x => x.Id == item.FighterId)?.Name ?? $"Fighter {item.FighterId}";
                var opponentName = roster.FirstOrDefault(x => x.Id == item.OpponentFighterId)?.Name ?? $"Fighter {item.OpponentFighterId}";

                Offers.Add(new FightOfferRow
                {
                    OfferId = item.Id,
                    FighterName = fighterName,
                    OpponentName = opponentName,
                    Purse = item.Purse,
                    WinBonus = item.WinBonus,
                    WeeksUntilFight = item.WeeksUntilFight,
                    IsTitleFight = item.IsTitleFight,
                    Status = item.Status
                });
            }

            SelectedOffer = Offers.FirstOrDefault();
            StatusText = $"Mensajes: {Messages.Count} · Ofertas: {Offers.Count}";
        }

        private async Task GenerateTestOfferAsync()
        {
            StatusText = await _generateFightOfferService.GenerateAsync();
            await LoadAsync();
        }

        private async Task AcceptSelectedAsync()
        {
            if (SelectedOffer == null)
            {
                StatusText = "Selecciona una oferta.";
                return;
            }

            await _fightOfferRepo.UpdateStatusAsync(SelectedOffer.OfferId, "Accepted");

            var agent = await _agentRepo.GetAsync();
            if (agent != null)
            {
                await _inboxRepo.CreateAsync(new InboxMessage
                {
                    AgentId = agent.Id,
                    MessageType = "FightOfferResponse",
                    Subject = "Oferta aceptada",
                    Body = $"Has aceptado la oferta #{SelectedOffer.OfferId}: {SelectedOffer.FighterName} vs {SelectedOffer.OpponentName}.",
                    CreatedDate = System.DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsRead = false
                });
            }

            await LoadAsync();
            StatusText = "Oferta aceptada.";
        }

        private async Task RejectSelectedAsync()
        {
            if (SelectedOffer == null)
            {
                StatusText = "Selecciona una oferta.";
                return;
            }

            await _fightOfferRepo.UpdateStatusAsync(SelectedOffer.OfferId, "Rejected");

            var agent = await _agentRepo.GetAsync();
            if (agent != null)
            {
                await _inboxRepo.CreateAsync(new InboxMessage
                {
                    AgentId = agent.Id,
                    MessageType = "FightOfferResponse",
                    Subject = "Oferta rechazada",
                    Body = $"Has rechazado la oferta #{SelectedOffer.OfferId}: {SelectedOffer.FighterName} vs {SelectedOffer.OpponentName}.",
                    CreatedDate = System.DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    IsRead = false
                });
            }

            await LoadAsync();
            StatusText = "Oferta rechazada.";
        }
    }
}