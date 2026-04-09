namespace MMAAgent.Domain.Fighters;

public sealed record FighterStyleProfile(
    int FighterId,
    string BaseStyle,
    string TacticalStyle,
    string StyleSummary,
    IReadOnlyList<string> Traits);
