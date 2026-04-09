namespace MMAAgent.Domain.Fighters;

public sealed record FighterStateSnapshot(
    int FighterId,
    int Form,
    int Energy,
    int Sharpness,
    int Morale,
    int CampQuality,
    int WeightCutReadiness,
    int InjuryRisk,
    string CurrentPhase,
    string? NextMilestoneType,
    string? NextMilestoneDate,
    string? LastFightDate,
    string? LastFightResult,
    int LastUpdatedWeek);
