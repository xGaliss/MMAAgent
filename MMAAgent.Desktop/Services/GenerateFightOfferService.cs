using System;
using System.Linq;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;

namespace MMAAgent.Desktop.Services
{
    public sealed class GenerateFightOfferService
    {
        private readonly IAgentProfileRepository _agentRepo;
        private readonly IManagedFighterRepository _managedRepo;
        private readonly IFightOfferRepository _fightOfferRepo;
        private readonly IInboxRepository _inboxRepo;
        private readonly IFighterRepository _fighterRepo;

        public GenerateFightOfferService(
            IAgentProfileRepository agentRepo,
            IManagedFighterRepository managedRepo,
            IFightOfferRepository fightOfferRepo,
            IInboxRepository inboxRepo,
            IFighterRepository fighterRepo)
        {
            _agentRepo = agentRepo;
            _managedRepo = managedRepo;
            _fightOfferRepo = fightOfferRepo;
            _inboxRepo = inboxRepo;
            _fighterRepo = fighterRepo;
        }

        public async Task<string> GenerateAsync()
        {
            var agent = await _agentRepo.GetAsync();
            if (agent == null)
                return "No se encontró el agente.";

            var managed = await _managedRepo.GetByAgentAsync(agent.Id);
            if (managed.Count == 0)
                return "No tienes luchadores representados.";

            var roster = await _fighterRepo.GetRosterAsync(500);
            var rnd = new Random();

            var managedFighter = managed[rnd.Next(managed.Count)];
            var myFighter = roster.FirstOrDefault(x => x.Id == managedFighter.FighterId);

            if (myFighter == null)
                return "No se pudo localizar a tu luchador en el roster.";

            var opponentCandidates = roster
                .Where(x => x.Id != myFighter.Id)
                .ToList();

            if (opponentCandidates.Count == 0)
                return "No hay rivales disponibles.";

            var opponent = opponentCandidates[rnd.Next(opponentCandidates.Count)];

            var purse = 8000 + rnd.Next(0, 7000);
            var winBonus = purse / 2;
            var weeks = 2 + rnd.Next(0, 5);

            var offerId = await _fightOfferRepo.CreateAsync(new FightOffer
            {
                FighterId = myFighter.Id,
                PromotionId = 1,
                OpponentFighterId = opponent.Id,
                Purse = purse,
                WinBonus = winBonus,
                WeeksUntilFight = weeks,
                IsTitleFight = false,
                Status = "Pending"
            });

            await _inboxRepo.CreateAsync(new InboxMessage
            {
                AgentId = agent.Id,
                MessageType = "FightOffer",
                Subject = $"Oferta de combate para {myFighter.Name}",
                Body = $"Oferta #{offerId}: {myFighter.Name} vs {opponent.Name} · Bolsa ${purse} · Bonus ${winBonus} · En {weeks} semanas.",
                CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsRead = false
            });

            return $"Oferta generada: {myFighter.Name} vs {opponent.Name}.";
        }
    }
}