using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMAAgent.Application.Abstractions;
using MMAAgent.Desktop.ViewModels.Models;
using MMAAgent.Domain.Agents;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class MyFightersViewModel : ObservableObject
    {
        private readonly IAgentProfileRepository _agentRepo;
        private readonly IManagedFighterRepository _managedRepo;
        private readonly IRosterRepository _fighterRepo;

        public ObservableCollection<ManagedFighterRow> Fighters { get; } = new();

        private ManagedFighterRow? _selectedFighter;
        public ManagedFighterRow? SelectedFighter
        {
            get => _selectedFighter;
            set => SetProperty(ref _selectedFighter, value);
        }

        private string _titleText = "Mis luchadores";
        public string TitleText
        {
            get => _titleText;
            set => SetProperty(ref _titleText, value);
        }

        private string _statusText = "Sin datos.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand SignRandomFighterCommand { get; }

        public MyFightersViewModel(
            IAgentProfileRepository agentRepo,
            IManagedFighterRepository managedRepo,
            IRosterRepository fighterRepo)
        {
            _agentRepo = agentRepo;
            _managedRepo = managedRepo;
            _fighterRepo = fighterRepo;

            SignRandomFighterCommand = new RelayCommand(async () => await SignRandomFighterAsync());
        }

        public async Task LoadAsync()
        {
            IsBusy = true;

            try
            {
                Fighters.Clear();

                var agent = await _agentRepo.GetAsync();
                if (agent == null)
                {
                    StatusText = "No se encontró el agente.";
                    return;
                }

                var managed = await _managedRepo.GetByAgentAsync(agent.Id);
                var roster = await _fighterRepo.GetRosterAsync(500);

                var rows = from m in managed
                           join f in roster on m.FighterId equals f.Id
                           select new ManagedFighterRow
                           {
                               FighterId = f.Id,
                               Name = f.Name,
                               Wins = f.Wins,
                               Losses = f.Losses,
                               CountryName = f.CountryName,
                               WeightClass = f.WeightClass,
                               ManagementPercent = m.ManagementPercent,
                               SignedDate = m.SignedDate
                           };

                foreach (var row in rows.OrderByDescending(x => x.Wins))
                    Fighters.Add(row);

                SelectedFighter = Fighters.FirstOrDefault();
                StatusText = Fighters.Count == 0
                    ? "Todavía no representas a ningún luchador."
                    : $"Clientes activos: {Fighters.Count}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SignRandomFighterAsync()
        {
            IsBusy = true;

            try
            {
                var agent = await _agentRepo.GetAsync();
                if (agent == null)
                {
                    StatusText = "No se encontró el agente.";
                    return;
                }

                var roster = await _fighterRepo.GetRosterAsync(500);
                var rnd = new Random();

                var candidates = roster
                    .Where(f => f.Id > 0)
                    .OrderBy(_ => rnd.Next())
                    .ToList();

                foreach (var fighter in candidates)
                {
                    var alreadyManaged = await _managedRepo.IsManagedByAgentAsync(agent.Id, fighter.Id);
                    if (alreadyManaged)
                        continue;

                    await _managedRepo.AddAsync(new ManagedFighter
                    {
                        AgentId = agent.Id,
                        FighterId = fighter.Id,
                        SignedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ManagementPercent = 15,
                        IsActive = true
                    });

                    await LoadAsync();
                    StatusText = $"Has firmado a {fighter.Name}.";
                    return;
                }

                StatusText = "No quedan luchadores disponibles para firmar.";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}