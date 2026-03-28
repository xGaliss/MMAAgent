namespace MMAAgent.Application.Abstractions;

public interface IFightOfferGenerationService
{
    Task<int> GenerateWeeklyOffersAsync(CancellationToken cancellationToken = default);
}
