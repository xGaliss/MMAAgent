namespace MMAAgent.Web.Models;

public sealed class ContractOfferVm
{
    public int Id { get; set; }
    public int FighterId { get; set; }
    public string FighterName { get; set; } = "";
    public int PromotionId { get; set; }
    public string PromotionName { get; set; } = "";
    public int OfferedFights { get; set; }
    public int BasePurse { get; set; }
    public int WinBonus { get; set; }
    public int WeeksToRespond { get; set; }
    public string Status { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string? Notes { get; set; }
}