namespace MMAAgent.Application.Simulation
{
    public interface IPromotionEventScheduleRepository
    {
        Task<PromotionScheduleRow?> GetAsync(int promotionId);
        Task<IReadOnlyList<PromotionScheduleRow>> GetDueAsync(int absoluteWeek);
        Task SetNextEventWeekAsync(int promotionId, int nextAbsoluteWeek);
        Task UpsertAsync(PromotionScheduleRow row);
    }
}