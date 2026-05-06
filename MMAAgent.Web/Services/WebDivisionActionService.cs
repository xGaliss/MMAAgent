using MMAAgent.Application.Abstractions;
using MMAAgent.Web.Infrastructure;

namespace MMAAgent.Web.Services;

public sealed class WebDivisionActionService
{
    private readonly SqliteActionBridge _bridge;
    private readonly ISavePersistenceService _savePersistenceService;

    public WebDivisionActionService(SqliteActionBridge bridge, ISavePersistenceService savePersistenceService)
    {
        _bridge = bridge;
        _savePersistenceService = savePersistenceService;
    }

    public async Task<ServiceResult> RequestTitleShotAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
    {
        var result = await _bridge.RequestTitleShotAsync(promotionId, weightClass, fighterId, cancellationToken);
        if (result.Success)
            await _savePersistenceService.PersistCurrentSaveAsync("request-title-shot", cancellationToken);
        return result;
    }

    public async Task<ServiceResult> RequestEliminatorAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
    {
        var result = await _bridge.RequestEliminatorAsync(promotionId, weightClass, fighterId, cancellationToken);
        if (result.Success)
            await _savePersistenceService.PersistCurrentSaveAsync("request-eliminator", cancellationToken);
        return result;
    }

    public async Task<ServiceResult> PushManagedFighterAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
    {
        var result = await _bridge.PushManagedFighterAsync(promotionId, weightClass, fighterId, cancellationToken);
        if (result.Success)
            await _savePersistenceService.PersistCurrentSaveAsync("push-managed-fighter", cancellationToken);
        return result;
    }
}
