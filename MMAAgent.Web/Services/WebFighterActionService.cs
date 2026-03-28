using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Services;

public sealed class WebFighterActionService
{
    private readonly IFighterSigningService _signingService;
    private readonly SqliteActionBridge _bridge;

    public WebFighterActionService(
        IFighterSigningService signingService,
        SqliteActionBridge bridge)
    {
        _signingService = signingService;
        _bridge = bridge;
    }

    public Task<SignFighterResult> AttemptSignAsync(int fighterId, CancellationToken cancellationToken = default)
        => _signingService.AttemptSignAsync(fighterId, cancellationToken);

    public Task ReleaseFighterAsync(int fighterId, CancellationToken cancellationToken = default)
        => _bridge.ReleaseFighterAsync(fighterId, cancellationToken);

    public Task SetAvailabilityAsync(int fighterId, int availableFromWeek, CancellationToken cancellationToken = default)
        => _bridge.SetAvailabilityAsync(fighterId, availableFromWeek, cancellationToken);

    public Task SeekFightAsync(int fighterId, CancellationToken cancellationToken = default)
        => _bridge.ClearBookedStateAsync(fighterId, cancellationToken);

    public Task NegotiateContractAsync(int fighterId, CancellationToken cancellationToken = default)
        => _bridge.ExtendContractAsync(fighterId, cancellationToken);
}
