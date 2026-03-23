using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMAAgent.Application.Abstractions;
using MMAAgent.Desktop.ViewModels.Models;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class ScoutingViewModel : ObservableObject
    {
        private readonly IAgentProfileRepository _agentRepo;
        private readonly IManagedFighterRepository _managedRepo;
        private readonly IFighterRepository _fighterRepo;

        public ObservableCollection<ScoutingFighterRow> Fighters { get; } = new();

        private ScoutingFighterRow? _selectedFighter;
        public ScoutingFighterRow? SelectedFighter
        {
            get => _selectedFighter;
            set => SetProperty(ref _selectedFighter, value);
        }

        private string _titleText = "Scouting";
        public string TitleText
        {
            get => _titleText;
            set => SetProperty(ref _titleText, value);
        }

        private string _statusText = "Busca talento para tu agencia.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand SignSelectedFighterCommand { get; }

        public ScoutingViewModel(
            IAgentProfileRepository agentRepo,
            IManagedFighterRepository managedRepo,
            IFighterRepository fighterRepo)
        {
            _agentRepo = agentRepo;
            _managedRepo = managedRepo;
            _fighterRepo = fighterRepo;

            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            SignSelectedFighterCommand = new RelayCommand(async () => await SignSelectedAsync());
        }

        public async Task LoadAsync()
        {
            Fighters.Clear();

            var agent = await _agentRepo.GetAsync();
            if (agent == null)
            {
                StatusText = "No se encontró el agente.";
                return;
            }

            var roster = await _fighterRepo.GetRosterAsync(500);
            var managed = await _managedRepo.GetByAgentAsync(agent.Id);
            var managedIds = managed.Select(x => x.FighterId).ToHashSet();

            var query = roster
                .Where(f => !managedIds.Contains(f.Id));

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(f =>
                    (f.Name?.ToLowerInvariant().Contains(term) ?? false) ||
                    (f.CountryName?.ToLowerInvariant().Contains(term) ?? false) ||
                    (f.WeightClass?.ToLowerInvariant().Contains(term) ?? false));
            }

            var rows = query
                .OrderByDescending(f => f.Wins)
                .ThenBy(f => f.Losses)
                .Take(200)
                .Select(f => new ScoutingFighterRow
                {
                    FighterId = f.Id,
                    Name = f.Name,
                    Wins = f.Wins,
                    Losses = f.Losses,
                    CountryName = f.CountryName,
                    WeightClass = f.WeightClass
                });

            foreach (var row in rows)
                Fighters.Add(row);

            SelectedFighter = Fighters.FirstOrDefault();
            StatusText = $"Disponibles para scout: {Fighters.Count}";
        }

        private async Task SignSelectedAsync()
        {
            if (SelectedFighter == null)
            {
                StatusText = "Selecciona un luchador.";
                return;
            }

            var agent = await _agentRepo.GetAsync();
            if (agent == null)
            {
                StatusText = "No se encontró el agente.";
                return;
            }

            var alreadyManaged = await _managedRepo.IsManagedByAgentAsync(agent.Id, SelectedFighter.FighterId);
            if (alreadyManaged)
            {
                StatusText = $"{SelectedFighter.Name} ya forma parte de tu agencia.";
                return;
            }

            await _managedRepo.AddAsync(new ManagedFighter
            {
                AgentId = agent.Id,
                FighterId = SelectedFighter.FighterId,
                SignedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ManagementPercent = 15,
                IsActive = true
            });

            var signedName = SelectedFighter.Name;
            await LoadAsync();
            StatusText = $"Has firmado a {signedName}.";
        }
    }
}