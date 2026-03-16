namespace MMAAgent.Application.DTOs;

public sealed class PromotionListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Prestige { get; init; }
    public int Budget { get; init; }
    public bool IsActive { get; init; }

    public int EventIntervalWeeks { get; init; }
    public int NextEventWeek { get; init; }
}