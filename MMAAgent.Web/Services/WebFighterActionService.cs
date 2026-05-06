using MMAAgent.Application.Abstractions;
using MMAAgent.Web.Infrastructure;

namespace MMAAgent.Web.Services;

public sealed class WebFighterActionService
{
    private readonly IFighterSigningService _signingService;
    private readonly IContractLifecycleService _contractLifecycleService;
    private readonly IFightOfferGenerationService _fightOfferGenerationService;
    private readonly SqliteActionBridge _bridge;
    private readonly ISavePersistenceService _savePersistenceService;

    public WebFighterActionService(
        IFighterSigningService signingService,
        IContractLifecycleService contractLifecycleService,
        IFightOfferGenerationService fightOfferGenerationService,
        SqliteActionBridge bridge,
        ISavePersistenceService savePersistenceService)
    {
        _signingService = signingService;
        _contractLifecycleService = contractLifecycleService;
        _fightOfferGenerationService = fightOfferGenerationService;
        _bridge = bridge;
        _savePersistenceService = savePersistenceService;
    }

    public async Task<SignFighterResult> AttemptSignAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        var result = await _signingService.AttemptSignAsync(fighterId, cancellationToken);
        if (result.Success)
            await _savePersistenceService.PersistCurrentSaveAsync("sign-fighter", cancellationToken);
        return result;
    }

    public async Task ReleaseFighterAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        await _bridge.ReleaseFighterAsync(fighterId, cancellationToken);
        await _savePersistenceService.PersistCurrentSaveAsync("release-fighter", cancellationToken);
    }

    public async Task<ServiceResult> PitchToPromotionAsync(int fighterId, int promotionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _contractLifecycleService.PitchFighterToPromotionAsync(fighterId, promotionId, cancellationToken);
            if (created > 0)
                await _savePersistenceService.PersistCurrentSaveAsync("pitch-promotion", cancellationToken);
            return created > 0
                ? ServiceResult.Ok("The promotion listened. A contract offer has been sent to your inbox.")
                : ServiceResult.Fail("That promotion passed on the pitch right now.");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult> SeekFightAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        var result = await _fightOfferGenerationService.GenerateOfferForManagedFighterAsync(fighterId, cancellationToken);
        if (result.Success)
            await _savePersistenceService.PersistCurrentSaveAsync("seek-fight", cancellationToken);
        return result;
    }

    public async Task<ServiceResult> SetCampFocusAsync(int fighterId, string campFocus, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _bridge.SetCampFocusAsync(fighterId, campFocus, cancellationToken);
            if (updated)
                await _savePersistenceService.PersistCurrentSaveAsync("set-camp-focus", cancellationToken);
            return updated
                ? ServiceResult.Ok($"Camp focus switched to {FormatCampFocus(campFocus)}.")
                : ServiceResult.Fail("No active booked camp was found for that fighter.");
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ex.Message);
        }
    }

    private static string FormatCampFocus(string campFocus)
        => campFocus switch
        {
            "WeightManagement" => "Weight Management",
            _ => campFocus
        };
}
