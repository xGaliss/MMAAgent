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
        private readonly GameShellViewModel _gameShellVm; // 👈

        private readonly GameShellViewModel _shell;

public MainViewModel(ISavePathProvider savePath, NewGameService newGame,
    MainMenuViewModel menuVm, GameShellViewModel shell)
{
    _savePath = savePath;
    _newGame = newGame;
    _menuVm = menuVm;
    _shell = shell;

    _menuVm.OnNewGame = NewGame;
    _menuVm.OnLoadLast = LoadLast;

    CurrentView = _menuVm;
}

public async void NewGame()
{
    _newGame.CreateAndLoadNewGame("MiPartida", fighterCount: 800);
    CurrentView = _shell;
    await _shell.Game.LoadAsync();  // carga la página principal
}

public async void LoadLast()
{
    if (!string.IsNullOrWhiteSpace(_savePath.CurrentPath))
    {
        CurrentView = _shell;
        await _shell.Game.LoadAsync();
    }
}
    }
}