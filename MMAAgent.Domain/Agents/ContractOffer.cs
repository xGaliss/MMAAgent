namespace MMAAgent.Domain.Agents;

public sealed class ContractOffer
{
    public int Id { get; set; }
    public int FighterId { get; set; }
    public int PromotionId { get; set; }
    public int OfferedFights { get; set; }
    public int BasePurse { get; set; }
    public int WinBonus { get; set; }
    public int WeeksToRespond { get; set; } = 2;
    public string Status { get; set; } = "Pending";
    public string SourceType { get; set; } = "Market";
    public string? Notes { get; set; }
    public int CreatedWeek { get; set; }
    public string CreatedDate { get; set; } = "";
    public string? RespondedDate { get; set; }
}
