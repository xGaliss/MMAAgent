namespace MMAAgent.Application.Abstractions;

public interface IFightOfferGenerationService
{
    Task<int> GenerateWeeklyOffersAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> GenerateOfferForManagedFighterAsync(int fighterId, CancellationToken cancellationToken = default);
}
