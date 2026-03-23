namespace MMAAgent.Desktop.ViewModels.Models
{
    public sealed class InboxMessageRow
    {
        public int MessageId { get; set; }
        public string MessageType { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public string CreatedDate { get; set; } = "";
        public bool IsRead { get; set; }
    }
}