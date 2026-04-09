using MMAAgent.Domain.Common;

namespace MMAAgent.Application.Simulation
{
    public sealed class WeeklySimulationService : IWeeklySimulationService
    {
        private readonly IPromotionEventScheduleRepository _schedule;
        private readonly IEventSimulator _events;

        public WeeklySimulationService(IPromotionEventScheduleRepository schedule, IEventSimulator events)
        {
            _schedule = schedule;
            _events = events;
        }

        public async Task RunWeekAsync(GameState state)
        {
            int absWeek = ToAbsoluteWeek(state.CurrentYear, state.CurrentWeek);

            var due = await _schedule.GetDueAsync(absWeek);

            foreach (var p in due)
            {
                await _events.SimulatePromotionEventAsync(p.PromotionId, state);

                int interval = Math.Max(1, p.IntervalWeeks);
                int next = absWeek + interval;
                await _schedule.SetNextEventWeekAsync(p.PromotionId, next);
            }
        }

        private static int ToAbsoluteWeek(int year, int week)
            => Math.Max(1, (year - 1) * 52 + week);
    }
}
