namespace MMAAgent.Web.Models;

public sealed record ManagedFighterVm(
    int FighterId,
    string Name,
    string WeightClass,
    int? PromotionId,
    string PromotionName,
    int? RankPosition,
    bool IsChampion,
    int Wins,
    int Losses,
    int Draws,
    string ContractStatus,
    int ContractFightsRemaining,
    int TotalFightsInContract,
    int Salary);

public sealed record PromotionOptionVm(
    int PromotionId,
    string PromotionName);

public sealed record SaveCardVm(
    string Path,
    string FileName,
    DateTime LastWriteTimeUtc,
    long FileSizeBytes);
