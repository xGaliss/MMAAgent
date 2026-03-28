namespace MMAAgent.Application.Abstractions;

public interface IFighterSigningService
{
    Task<SignFighterResult> AttemptSignAsync(int fighterId, CancellationToken cancellationToken = default);
}

public sealed record SignFighterResult(
    bool Success,
    string Message,
    int? AgentId,
    int FighterId);
