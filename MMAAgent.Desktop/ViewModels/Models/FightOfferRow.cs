namespace MMAAgent.Desktop.ViewModels.Models
{
    public sealed class FightOfferRow
    {
        public int OfferId { get; set; }
        public string FighterName { get; set; } = "";
        public string OpponentName { get; set; } = "";
        public int Purse { get; set; }
        public int WinBonus { get; set; }
        public int WeeksUntilFight { get; set; }
        public bool IsTitleFight { get; set; }
        public string Status { get; set; } = "";
    }
}