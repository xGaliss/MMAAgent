namespace MMAAgent.Web.Models;

public sealed record ProspectItemVm(
    int Id,
    string Name,
    int Age,
    string WeightClass,
    string CountryName,
    string? CountryFlagUrl,
    string PromotionName,
    string CareerStage,
    string CircuitType,
    int Wins,
    int Losses,
    int Draws,
    int AmateurWins,
    int AmateurLosses,
    int AmateurDraws,
    int Skill,
    int Potential,
    int Popularity,
    int Marketability,
    int Momentum,
    int MediaHeat,
    string ScoutStatus,
    string ConfidenceLabel,
    bool IsWatched,
    bool IsReadyForProJump,
    string Summary);

public sealed record ProspectPipelineVm(
    int AmateurCount,
    int WatchedCount,
    int ReadyCount,
    int GraduatingProFreeAgentsCount,
    IReadOnlyList<ProspectItemVm> WatchedProspects,
    IReadOnlyList<ProspectItemVm> ReadyForProJump,
    IReadOnlyList<ProspectItemVm> HotAmateurs,
    IReadOnlyList<ProspectItemVm> NewProFreeAgents);
