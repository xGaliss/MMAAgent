namespace MMAAgent.Desktop.ViewModels;

public sealed class PromotionListItemVm : ObservableObject
{
    public int Id { get; }
    public string Name { get; }
    public int Prestige { get; }
    public int Budget { get; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    private int _intervalWeeks = 1;
    public int IntervalWeeks
    {
        get => _intervalWeeks;
        set => SetProperty(ref _intervalWeeks, value);
    }

    private int _nextEventWeek = 0;
    public int NextEventWeek
    {
        get => _nextEventWeek;
        set => SetProperty(ref _nextEventWeek, value);
    }

    public PromotionListItemVm(int id, string name, int prestige, int budget, bool isActive, int intervalWeeks, int nextEventWeek)
    {
        Id = id;
        Name = name;
        Prestige = prestige;
        Budget = budget;
        _isActive = isActive;
        _intervalWeeks = intervalWeeks;
        _nextEventWeek = nextEventWeek;
    }
}

public sealed record PromotionWeightClassItem(string WeightClass, bool HasRanking, int RankingSize);