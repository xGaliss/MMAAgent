namespace MMAAgent.Web.Models;

public sealed class DashboardStatsVm
{
    public int FighterCount { get; set; }
    public int PromotionCount { get; set; }
    public int UnreadMessages { get; set; }
    public int PendingFightOffers { get; set; }
    public int PendingContractOffers { get; set; }
}
