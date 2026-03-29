namespace MMAAgent.Web.Models;

public sealed class InboxMessageVm
{
    public int Id { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string MessageType { get; set; } = "";
    public string CreatedDate { get; set; } = "";
    public bool IsRead { get; set; }
    public bool IsArchived { get; set; }
}