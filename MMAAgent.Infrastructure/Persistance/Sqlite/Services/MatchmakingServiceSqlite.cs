using MMAAgent.Application.Abstractions;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class MatchmakingServiceSqlite : IMatchmakingService
{
    private readonly IFightOfferGenerationService _offerGenerationService;

    public MatchmakingServiceSqlite(IFightOfferGenerationService offerGenerationService)
    {
        _offerGenerationService = offerGenerationService;
    }

    public Task<int> FillUpcomingCardsAsync(CancellationToken cancellationToken = default)
        => _offerGenerationService.GenerateWeeklyOffersAsync(cancellationToken);
}
