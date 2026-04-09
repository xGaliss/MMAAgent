namespace MMAAgent.Application.Abstractions;

public interface IWorldAgendaService
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
