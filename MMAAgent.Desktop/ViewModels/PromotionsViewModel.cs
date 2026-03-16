using MMAAgent.Application.Abstractions;
using System.Collections.ObjectModel;
using MMAAgent.Application.DTOs;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class PromotionsViewModel : ObservableObject
    {
        private readonly IPromotionRepository _repo;

        public ObservableCollection<PromotionListItem> Promotions { get; } = new();

        private PromotionListItem? _selected;
        public PromotionListItem? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        public PromotionsViewModel(IPromotionRepository repo)
        {
            _repo = repo;
        }

        public async Task LoadAsync()
        {
            Promotions.Clear();

            var rows = await _repo.GetAllAsync();

            foreach (var p in rows)
                Promotions.Add(p);
        }
    }
}