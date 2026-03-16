namespace MMAAgent.Application.DTOs
{
    public sealed class PromotionWeightClassItem
    {
        public int PromotionId { get; init; }
        public string WeightClass { get; init; } = "";
        public bool HasRanking { get; init; }
        public int RankingSize { get; init; }
    }
}