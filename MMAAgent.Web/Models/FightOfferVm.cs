namespace MMAAgent.Web.Models;

public sealed class FightOfferVm
{
    public int OfferId { get; set; }
    public int FighterId { get; set; }
    public int OpponentId { get; set; }
    public int PromotionId { get; set; }

    public string FighterName { get; set; } = "";
    public string OpponentName { get; set; } = "";
    public string PromotionName { get; set; } = "";

    public int Purse { get; set; }
    public int WinBonus { get; set; }
    public int WeeksUntilFight { get; set; }
    public int CampWeeksOffered { get; set; }
    public bool IsTitleFight { get; set; }
    public bool IsTitleEliminator { get; set; }
    public bool IsShortNotice { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "";
}
