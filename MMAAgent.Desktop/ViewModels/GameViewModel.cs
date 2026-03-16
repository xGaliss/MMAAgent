using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Desktop.ViewModels.Models;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class GameViewModel : ObservableObject
    {
        private readonly RosterViewModel _roster;
        private readonly GameTimeService _time;
        private readonly ISavePathProvider _savePath;
        private readonly IWeeklySimulationService _weekly;
        private readonly IEventRepository _eventRepo;

        public RosterViewModel Roster => _roster;

        private string _dateText = "—";
        public string DateText
        {
            get => _dateText;
            private set => SetProperty(ref _dateText, value);
        }

        private string _weekYearText = "—";
        public string WeekYearText
        {
            get => _weekYearText;
            private set => SetProperty(ref _weekYearText, value);
        }

        private string _seedText = "—";
        public string SeedText
        {
            get => _seedText;
            private set => SetProperty(ref _seedText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public ObservableCollection<EventListItem> Events { get; } = new();
        public ObservableCollection<FightListItem> SelectedEventFights { get; } = new();

        public ObservableCollection<FeaturedFighterRow> FeaturedFighters { get; } = new();
        public ObservableCollection<string> WorldNotes { get; } = new();

        private EventListItem? _selectedEvent;
        public EventListItem? SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (SetProperty(ref _selectedEvent, value))
                    _ = LoadSelectedEventFightsAsync(value);
            }
        }

        public GameViewModel(
            RosterViewModel roster,
            GameTimeService time,
            ISavePathProvider savePath,
            IWeeklySimulationService weekly,
            IEventRepository eventRepo)
        {
            _roster = roster;
            _time = time;
            _savePath = savePath;
            _weekly = weekly;
            _eventRepo = eventRepo;
        }

        public async Task LoadAsync()
        {
            if (string.IsNullOrWhiteSpace(_savePath.CurrentPath))
                return;

            IsBusy = true;
            try
            {
                await LoadGameStateAsync();
                await _roster.LoadAsync();

                await LoadLastEventsAsync();
                await LoadFeaturedFightersAsync();
                LoadWorldNotes();

                if (SelectedEvent == null && Events.Count > 0)
                    SelectedEvent = Events[0];
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task AdvanceWeekAsync()
        {
            if (string.IsNullOrWhiteSpace(_savePath.CurrentPath))
                return;

            IsBusy = true;
            try
            {
                var state = await _time.AdvanceWeeksAsync(1);

                await _weekly.RunWeekAsync(state);

                DateText = state.CurrentDate;
                WeekYearText = $"Week {state.CurrentWeek} • Year {state.CurrentYear}";
                SeedText = $"Seed: {state.WorldSeed}";

                await _roster.LoadAsync();
                await LoadLastEventsAsync();
                await LoadFeaturedFightersAsync();
                LoadWorldNotes();

                if (SelectedEvent == null && Events.Count > 0)
                    SelectedEvent = Events[0];
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadGameStateAsync()
        {
            var state = await _time.GetAsync();
            if (state == null)
            {
                DateText = "—";
                WeekYearText = "—";
                SeedText = "—";
                return;
            }

            DateText = state.CurrentDate;
            WeekYearText = $"Week {state.CurrentWeek} • Year {state.CurrentYear}";
            SeedText = $"Seed: {state.WorldSeed}";
        }

        private async Task LoadLastEventsAsync()
        {
            Events.Clear();

            var rows = await _eventRepo.GetLastEventsAsync(take: 12);
            foreach (var r in rows)
                Events.Add(new EventListItem(r.Id, r.EventDate, r.Name));
        }

        private async Task LoadSelectedEventFightsAsync(EventListItem? ev)
        {
            SelectedEventFights.Clear();
            if (ev is null) return;

            var fights = await _eventRepo.GetFightsByEventAsync(ev.Id);
            foreach (var f in fights)
                SelectedEventFights.Add(new FightListItem(
                    f.WeightClass,
                    f.Matchup,
                    f.Winner,
                    f.Method,
                    f.IsTitle));
        }

        private async Task LoadFeaturedFightersAsync()
        {
            FeaturedFighters.Clear();

            var fighters = _roster.Fighters.ToList();
            if (fighters.Count == 0)
                return;

            var mostWins = fighters
                .OrderByDescending(f => f.Wins)
                .First();

            var bestRecord = fighters
                .Where(f => (f.Wins + f.Losses) >= 10)
                .OrderByDescending(f => (double)f.Wins / (f.Wins + f.Losses))
                .FirstOrDefault();

            var prospect = fighters
                .Where(f => f.Wins >= 8 && f.Losses <= 2)
                .OrderByDescending(f => f.Wins)
                .FirstOrDefault();

            var veteran = fighters
                .OrderByDescending(f => f.Wins + f.Losses)
                .FirstOrDefault();

            FeaturedFighters.Add(new FeaturedFighterRow
            {
                Category = "Más victorias",
                FighterName = mostWins.Name,
                Value = mostWins.Wins.ToString()
            });

            if (bestRecord != null)
            {
                FeaturedFighters.Add(new FeaturedFighterRow
                {
                    Category = "Mejor récord",
                    FighterName = bestRecord.Name,
                    Value = $"{bestRecord.Wins}-{bestRecord.Losses}"
                });
            }

            if (prospect != null)
            {
                FeaturedFighters.Add(new FeaturedFighterRow
                {
                    Category = "Prospecto",
                    FighterName = prospect.Name,
                    Value = $"{prospect.Wins}-{prospect.Losses}"
                });
            }

            if (veteran != null)
            {
                FeaturedFighters.Add(new FeaturedFighterRow
                {
                    Category = "Más peleas",
                    FighterName = veteran.Name,
                    Value = (veteran.Wins + veteran.Losses).ToString()
                });
            }

            await Task.CompletedTask;
        }

        private void LoadWorldNotes()
        {
            WorldNotes.Clear();

            WorldNotes.Add($"{WeekYearText} en curso.");
            WorldNotes.Add($"Hay {Events.Count} eventos recientes cargados.");
            WorldNotes.Add($"Roster visible: {_roster.Fighters.Count} luchadores.");

            var latest = Events.FirstOrDefault();
            if (latest != null)
                WorldNotes.Add($"Último evento registrado: {latest.Name}.");
        }
    }
}