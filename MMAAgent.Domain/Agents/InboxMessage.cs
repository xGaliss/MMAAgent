namespace MMAAgent.Domain.Agents
{
    public sealed class InboxMessage
    {
        public int Id { get; set; }
        public int AgentId { get; set; }
        public string MessageType { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public string CreatedDate { get; set; } = "";
        public bool IsRead { get; set; }
    }
}