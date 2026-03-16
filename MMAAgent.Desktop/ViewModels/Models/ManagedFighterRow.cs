namespace MMAAgent.Desktop.ViewModels.Models
{
    public sealed class ManagedFighterRow
    {
        public int FighterId { get; set; }
        public string Name { get; set; } = "";
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string CountryName { get; set; } = "";
        public string WeightClass { get; set; } = "";
        public int ManagementPercent { get; set; }
        public string SignedDate { get; set; } = "";
    }
}