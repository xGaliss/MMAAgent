using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Services;

public sealed class WebDivisionActionService
{
    private readonly SqliteActionBridge _bridge;

    public WebDivisionActionService(SqliteActionBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<ServiceResult> RequestTitleShotAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => _bridge.RequestTitleShotAsync(promotionId, weightClass, fighterId, cancellationToken);

    public Task<ServiceResult> RequestEliminatorAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => _bridge.RequestEliminatorAsync(promotionId, weightClass, fighterId, cancellationToken);

    public Task<ServiceResult> PushManagedFighterAsync(int promotionId, string weightClass, int fighterId, CancellationToken cancellationToken = default)
        => _bridge.PushManagedFighterAsync(promotionId, weightClass, fighterId, cancellationToken);
}
