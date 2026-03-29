using MMAAgent.Domain.Fighters;

namespace MMAAgent.Application.Abstractions
{
    public interface IFighterRepository
    {
        Task<IReadOnlyList<FighterSummary>> GetRosterAsync(int take = 200);
        Task<bool> IsManagedAsync(int fighterId);
    }
}