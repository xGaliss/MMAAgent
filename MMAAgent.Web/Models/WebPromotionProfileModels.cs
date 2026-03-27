namespace MMAAgent.Web.Models;

public sealed record PromotionSummaryVm(
    int Id,
    string Name,
    int Prestige,
    int Budget,
    bool IsActive,
    int EventIntervalWeeks,
    int NextEventWeek);

public sealed record PromotionChampionVm(
    string WeightClass,
    int FighterId,
    string FighterName);

public sealed record PromotionRankingVm(
    string WeightClass,
    int RankPosition,
    int FighterId,
    string FighterName);

public sealed record PromotionProfileVm(
    int Id,
    string Name,
    int Prestige,
    int Budget,
    bool IsActive,
    int EventIntervalWeeks,
    int NextEventWeek,
    IReadOnlyList<PromotionChampionVm> Champions,
    IReadOnlyList<PromotionRankingVm> Rankings);
