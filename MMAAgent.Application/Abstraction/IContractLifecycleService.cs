using System.Threading;
using System.Threading.Tasks;

namespace MMAAgent.Application.Abstractions
{
    public interface IContractLifecycleService
    {
        Task<int> ProcessWeeklyAsync(CancellationToken cancellationToken = default);
        Task<int> PitchFighterToPromotionAsync(int fighterId, int promotionId, CancellationToken cancellationToken = default);
        Task RespondToOfferAsync(int contractOfferId, bool accept, CancellationToken cancellationToken = default);
    }
}
