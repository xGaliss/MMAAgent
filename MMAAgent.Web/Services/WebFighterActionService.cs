using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Services;

public sealed class WebFighterActionService
{
    private readonly IFighterSigningService _signingService;
    private readonly IContractLifecycleService _contractLifecycleService;
    private readonly IFightOfferGenerationService _fightOfferGenerationService;
    private readonly SqliteActionBridge _bridge;

    public WebFighterActionService(
        IFighterSigningService signingService,
        IContractLifecycleService contractLifecycleService,
        IFightOfferGenerationService fightOfferGenerationService,
        SqliteActionBridge bridge)
    {
        _signingService = signingService;
        _contractLifecycleService = contractLifecycleService;
        _fightOfferGenerationService = fightOfferGenerationService;
        _bridge = bridge;
    }

    public Task<SignFighterResult> AttemptSignAsync(int fighterId, CancellationToken cancellationToken = default)
        => _signingService.AttemptSignAsync(fighterId, cancellationToken);

    public Task ReleaseFighterAsync(int fighterId, CancellationToken cancellationToken = default)
        => _bridge.ReleaseFighterAsync(fighterId, cancellationToken);

    public async Task<ServiceResult> PitchToPromotionAsync(int fighterId, int promotionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _contractLifecycleService.PitchFighterToPromotionAsync(fighterId, promotionId, cancellationToken);
            return created > 0
                ? ServiceResult.Ok("The promotion listened. A contract offer has been sent to your inbox.")
                : ServiceResult.Fail("That promotion passed on the pitch right now.");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ex.Message);
        }
    }

    public Task<ServiceResult> SeekFightAsync(int fighterId, CancellationToken cancellationToken = default)
        => _fightOfferGenerationService.GenerateOfferForManagedFighterAsync(fighterId, cancellationToken);
}
