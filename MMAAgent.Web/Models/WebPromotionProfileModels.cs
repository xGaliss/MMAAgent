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

public sealed record PromotionContenderVm(
    string WeightClass,
    int QueueRank,
    int FighterId,
    string FighterName,
    int QueueScore,
    string Notes);

public sealed record PromotionDivisionPictureVm(
    string WeightClass,
    int? ChampionId,
    string ChampionName,
    IReadOnlyList<PromotionContenderVm> NextContenders,
    string? UpcomingStakes,
    string? RivalryHeadline);

public sealed record PromotionProfileVm(
    int Id,
    string Name,
    int Prestige,
    int Budget,
    bool IsActive,
    int EventIntervalWeeks,
    int NextEventWeek,
    IReadOnlyList<PromotionChampionVm> Champions,
    IReadOnlyList<PromotionRankingVm> Rankings,
    IReadOnlyList<PromotionContenderVm> Contenders,
    IReadOnlyList<PromotionDivisionPictureVm> Divisions);
