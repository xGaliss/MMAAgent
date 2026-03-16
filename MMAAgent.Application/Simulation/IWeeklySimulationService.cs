using MMAAgent.Domain.Common;

namespace MMAAgent.Application.Simulation
{
    public interface IWeeklySimulationService
    {
        Task RunWeekAsync(GameState state);
    }
}