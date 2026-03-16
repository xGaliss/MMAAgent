using System.Collections.Generic;
using System.Threading.Tasks;
using MMAAgent.Desktop.ViewModels.Models;

namespace MMAAgent.Application.Abstractions
{
    public interface IRosterRepository
    {
        Task<IReadOnlyList<FighterRow>> GetRosterAsync(int take = 200);
    }
}