namespace MMAAgent.Application.Abstractions;

public interface IFighterWorldService
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
    Task AdvanceWeekAsync(int absoluteWeek, string currentDate, CancellationToken cancellationToken = default);
}
