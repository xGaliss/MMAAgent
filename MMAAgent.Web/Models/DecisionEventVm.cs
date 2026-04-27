namespace MMAAgent.Web.Models;

public sealed class DecisionEventVm
{
    public int Id { get; set; }
    public int? FighterId { get; set; }
    public string DecisionType { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Body { get; set; } = "";
    public string OptionAKey { get; set; } = "";
    public string OptionALabel { get; set; } = "";
    public string? OptionADescription { get; set; }
    public string OptionBKey { get; set; } = "";
    public string OptionBLabel { get; set; } = "";
    public string? OptionBDescription { get; set; }
    public string CreatedDate { get; set; } = "";
    public string Status { get; set; } = "";
}
