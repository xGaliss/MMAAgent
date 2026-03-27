namespace MMAAgent.Web.Models;

public sealed record FightHistoryItem(
    string Date,
    string Opponent,
    string Result,
    string Method,
    bool IsTitle,
    string Promotion,
    string? EventName);

public sealed record FighterProfile(
    int Id,
    string Name,
    string CountryName,
    string WeightClass,
    int Age,
    int Wins,
    int Losses,
    int Draws,
    int Skill,
    int Potential,
    int Popularity,
    int Striking,
    int Grappling,
    int Wrestling,
    int Cardio,
    int Chin,
    int FightIQ,
    string ContractStatus,
    int? PromotionId,
    string? PromotionName,
    int Salary,
    int ContractFightsRemaining,
    int TotalFightsInContract,
    int? RankPosition,
    bool IsChampion);
