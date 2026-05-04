namespace MMAAgent.Web.Models;

public sealed record RosterFilterOptions(
    IReadOnlyList<string> WeightClasses,
    IReadOnlyList<string> Countries);

public sealed record RosterListItemVm(
    int Id,
    string Name,
    int Age,
    string WeightClass,
    string CountryName,
    string? CountryFlagUrl,
    string PromotionName,
    string Status,
    int Wins,
    int Losses,
    int Draws,
    string ScoutRead,
    string ConfidenceLabel,
    string BaseStyle,
    int ReliabilityScore,
    int MediaHeat,
    string ScoutStatus);

public sealed record RosterQueryResult(
    int TotalCount,
    IReadOnlyList<RosterListItemVm> Items,
    RosterFilterOptions Filters);
