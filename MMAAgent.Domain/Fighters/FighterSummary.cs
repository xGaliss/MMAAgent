namespace MMAAgent.Domain.Fighters
{
    public sealed class FighterSummary
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int Wins { get; init; }
        public int Losses { get; init; }
        public string CountryName { get; init; } = "";
        public string WeightClass { get; init; } = "";
    }
}