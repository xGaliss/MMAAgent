namespace MMAAgent.Application.Abstractions;

public interface IMatchmakingService
{
    Task<int> FillUpcomingCardsAsync(CancellationToken cancellationToken = default);
}
