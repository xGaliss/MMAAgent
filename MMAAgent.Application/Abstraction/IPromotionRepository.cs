using MMAAgent.Application.DTOs;

namespace MMAAgent.Application.Abstractions
{
    public interface IPromotionRepository
    {
        Task<IReadOnlyList<PromotionListItem>> GetAllAsync();
    }
}