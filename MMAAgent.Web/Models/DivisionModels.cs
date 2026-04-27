namespace MMAAgent.Web.Models;

public sealed record DivisionPromotionGroupVm(
    int PromotionId,
    string PromotionName,
    IReadOnlyList<PromotionDivisionPictureVm> Divisions);
