namespace MMAAgent.Domain.Agents
{
    public sealed class FightOffer
    {
        public int Id { get; set; }
        public int FighterId { get; set; }
        public int PromotionId { get; set; }
        public int OpponentFighterId { get; set; }
        public int Purse { get; set; }
        public int WinBonus { get; set; }
        public int WeeksUntilFight { get; set; }
        public bool IsTitleFight { get; set; }
        public string Status { get; set; } = "";
    }
}