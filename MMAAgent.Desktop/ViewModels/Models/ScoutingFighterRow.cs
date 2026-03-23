namespace MMAAgent.Desktop.ViewModels.Models
{
    public sealed class ScoutingFighterRow
    {
        public int FighterId { get; set; }
        public string Name { get; set; } = "";
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string CountryName { get; set; } = "";
        public string WeightClass { get; set; } = "";
    }
}