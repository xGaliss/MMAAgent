using MMAAgent.Domain.Common;

namespace MMAAgent.Application.Abstractions
{
    public interface IGameStateRepository
    {
        Task<GameState?> GetAsync();
        Task EnsureCreatedAsync(DateTime startDate, int worldSeed);
        Task UpdateAsync(GameState state);
    }
}