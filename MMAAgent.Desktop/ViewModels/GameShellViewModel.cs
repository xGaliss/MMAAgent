using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class GameShellViewModel : ObservableObject
    {
        private object? _currentPage;

        public object? CurrentPage
        {
            get => _currentPage;
            private set => SetProperty(ref _currentPage, value);
        }

        public GameViewModel Game { get; }
        public RosterViewModel Roster { get; }
        public PromotionsViewModel Promotions { get; }
        public FightProfileViewModel FightProfile { get; }

        public ICommand GoDashboardCommand { get; }
        public ICommand GoPromotionsCommand { get; }
        public ICommand GoRosterCommand { get; }

        public GameShellViewModel(
            GameViewModel game,
            DashboardViewModel dashboard,
            RosterViewModel roster,
            PromotionsViewModel promotions,
            FightProfileViewModel fightProfile)
        {
            Game = game;
            Roster = roster;
            Promotions = promotions;
            FightProfile = fightProfile;

            GoDashboardCommand = new RelayCommand(async () =>
            {
                await Game.LoadAsync();
                CurrentPage = Game;
            });

            GoPromotionsCommand = new RelayCommand(() =>
            {
                CurrentPage = Promotions;
            });

            GoRosterCommand = new RelayCommand(async () =>
            {
                await Roster.LoadAsync();
                CurrentPage = Roster;
            });

            CurrentPage = Game;
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}