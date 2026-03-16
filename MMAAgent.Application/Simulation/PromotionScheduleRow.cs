namespace MMAAgent.Application.Simulation
{
    public sealed record PromotionScheduleRow(
        int PromotionId,
        bool IsActive,
        int IntervalWeeks,
        int NextEventWeek
    );
}