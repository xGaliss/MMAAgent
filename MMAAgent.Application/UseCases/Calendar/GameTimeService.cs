using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Common;
using System.Globalization;

namespace MMAAgent.Application
{
    public sealed class GameTimeService
    {
        private readonly IGameStateRepository _repo;

        public GameTimeService(IGameStateRepository repo)
        {
            _repo = repo;
        }

        public Task<GameState?> GetAsync() => _repo.GetAsync();

        public async Task<GameState> AdvanceWeeksAsync(int weeks)
        {
            if (weeks <= 0)
                throw new ArgumentOutOfRangeException(nameof(weeks));

            return await AdvanceDaysAsync(weeks * 7);
        }

        public async Task<GameState> AdvanceDaysAsync(int days)
        {
            if (days <= 0)
                throw new ArgumentOutOfRangeException(nameof(days));

            var state = await _repo.GetAsync()
                        ?? throw new InvalidOperationException("GameState no existe (Id=1).");

            var currentDate = ParseDate(state.CurrentDate);
            var startDate = ParseDate(string.IsNullOrWhiteSpace(state.StartDate) ? state.CurrentDate : state.StartDate);
            var newDate = currentDate.AddDays(days);

            state.CurrentDate = newDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var totalDaysSinceStart = Math.Max(0, (int)(newDate.Date - startDate.Date).TotalDays);
            var zeroBasedWeekIndex = totalDaysSinceStart / 7;

            state.CurrentYear = (zeroBasedWeekIndex / 52) + 1;
            state.CurrentWeek = (zeroBasedWeekIndex % 52) + 1;

            await _repo.UpdateAsync(state);
            return state;
        }

        private static DateTime ParseDate(string value)
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.Date;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.Date;

            return DateTime.UtcNow.Date;
        }
    }
}
