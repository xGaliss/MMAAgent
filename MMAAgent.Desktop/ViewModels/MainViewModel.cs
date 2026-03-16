using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private object? _currentView;

        public object? CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        private readonly ISavePathProvider _savePath;
        private readonly MainMenuViewModel _menuVm;
        private readonly NewGameSetupViewModel _newGameSetupVm;
        private readonly GameShellViewModel _gameShellVm;

        public MainViewModel(
            ISavePathProvider savePath,
            MainMenuViewModel menuVm,
            NewGameSetupViewModel newGameSetupVm,
            GameShellViewModel gameShellVm)
        {
            _savePath = savePath;
            _menuVm = menuVm;
            _newGameSetupVm = newGameSetupVm;
            _gameShellVm = gameShellVm;

            _menuVm.OnNewGame = ShowNewGameSetup;
            _menuVm.OnLoadLast = LoadLast;

            _newGameSetupVm.OnCancel = GoToMenu;
            _newGameSetupVm.OnCreateCompleted = EnterGameAfterCreationAsync;

            CurrentView = _menuVm;
        }

        private void ShowNewGameSetup()
        {
            _newGameSetupVm.AgentName = "";
            _newGameSetupVm.AgencyName = "";
            _newGameSetupVm.ErrorText = "";
            CurrentView = _newGameSetupVm;
        }

        private async Task EnterGameAfterCreationAsync()
        {
            await _gameShellVm.Dashboard.LoadAsync();
            CurrentView = _gameShellVm;
        }

        private async void LoadLast()
        {
            if (!string.IsNullOrWhiteSpace(_savePath.CurrentPath))
            {
                await _gameShellVm.Dashboard.LoadAsync();
                CurrentView = _gameShellVm;
            }
        }

        private void GoToMenu()
        {
            CurrentView = _menuVm;
        }
    }
}