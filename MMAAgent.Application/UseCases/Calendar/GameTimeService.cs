using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Domain.Common;
using System.Globalization;

namespace MMAAgent.Application
{
    public sealed class GameTimeService
    {
        private readonly IGameStateRepository _repo;
        private readonly IWeeklySimulationService _weekly;

        public GameTimeService(IGameStateRepository repo, IWeeklySimulationService weekly)
        {
            _repo = repo;
            _weekly = weekly;
        }

        public Task<GameState?> GetAsync() => _repo.GetAsync();

        public async Task<GameState> AdvanceWeeksAsync(int weeks)
        {
            if (weeks <= 0)
                throw new ArgumentOutOfRangeException(nameof(weeks));

            var state = await _repo.GetAsync()
                        ?? throw new InvalidOperationException("GameState no existe (Id=1).");

            // Parse CurrentDate (TEXT "yyyy-MM-dd")
            var date = DateTime.ParseExact(state.CurrentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            for (int i = 0; i < weeks; i++)
            {
                // Avanzar 7 días
                date = date.AddDays(7);
                state.CurrentDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                // Semana / año
                state.CurrentWeek += 1;
                if (state.CurrentWeek > 52)
                {
                    state.CurrentWeek = 1;
                    state.CurrentYear += 1;
                    // aquí luego: AgeUpFighters(), seasonal reset...
                }

                // ✅ Hook semanal (aquí pasa “el juego”)
                await _weekly.RunWeekAsync(state);
            }

            // Persistir estado final
            await _repo.UpdateAsync(state);
            return state;
        }
    }
}