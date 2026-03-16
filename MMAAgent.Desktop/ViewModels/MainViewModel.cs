using MMAAgent.Application.Abstractions;
using MMAAgent.Desktop.Services;

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
        private readonly NewGameService _newGame;
        private readonly MainMenuViewModel _menuVm;
        private readonly GameShellViewModel _gameShellVm;

        public MainViewModel(
            ISavePathProvider savePath,
            NewGameService newGame,
            MainMenuViewModel menuVm,
            GameShellViewModel gameShellVm)
        {
            _savePath = savePath;
            _newGame = newGame;
            _menuVm = menuVm;
            _gameShellVm = gameShellVm;

            _menuVm.OnNewGame = NewGame;
            _menuVm.OnLoadLast = LoadLast;

            CurrentView = _menuVm;
        }

        public async void NewGame()
        {
            _newGame.CreateAndLoadNewGame("MiPartida", fighterCount: 800);
            await _gameShellVm.Dashboard.LoadAsync();
            CurrentView = _gameShellVm;
        }

        public async void LoadLast()
        {
            if (!string.IsNullOrWhiteSpace(_savePath.CurrentPath))
            {
                await _gameShellVm.Dashboard.LoadAsync();
                CurrentView = _gameShellVm;
            }
        }
    }
}