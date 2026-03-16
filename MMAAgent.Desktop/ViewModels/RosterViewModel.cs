using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Fighters;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class RosterViewModel : INotifyPropertyChanged
    {
        private readonly IFighterRepository _repo;

        public ObservableCollection<FighterSummary> Fighters { get; } = new();

        public RosterViewModel(IFighterRepository repo)
        {
            _repo = repo;
        }

        // ✅ Selección para el panel derecho
        private FighterSummary? _selectedFighter;
        public FighterSummary? SelectedFighter
        {
            get => _selectedFighter;
            set
            {
                _selectedFighter = value;
                OnPropertyChanged();
            }
        }

        // ✅ Texto del header (Total: X)
        public string RosterCountText => $"Total: {Fighters.Count}";

        public async Task LoadAsync()
        {
            Fighters.Clear();

            var items = await _repo.GetRosterAsync(200);

            foreach (var f in items)
                Fighters.Add(f);

            // seleccionar el primero automáticamente
            SelectedFighter = Fighters.FirstOrDefault();

            OnPropertyChanged(nameof(RosterCountText));
        }

        // 🔧 INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}