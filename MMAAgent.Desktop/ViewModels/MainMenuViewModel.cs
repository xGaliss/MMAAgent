using MMAAgent.Application.Abstractions;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class MainMenuViewModel : ObservableObject
    {
        private readonly ISavePathProvider _savePath;

        public System.Action? OnNewGame { get; set; }
        public System.Action? OnLoadLast { get; set; }

        public bool HasLastSave => !string.IsNullOrWhiteSpace(_savePath.CurrentPath);

        public MainMenuViewModel(ISavePathProvider savePath)
        {
            _savePath = savePath;
        }

        public void NewGame() => OnNewGame?.Invoke();
        public void LoadLast() => OnLoadLast?.Invoke();
    }
}