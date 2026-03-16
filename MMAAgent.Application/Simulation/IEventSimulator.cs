using MMAAgent.Domain.Common;

namespace MMAAgent.Application.Simulation
{
    public interface IEventSimulator
    {
        Task SimulatePromotionEventAsync(int promotionId, GameState state);
    }
}