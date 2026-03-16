namespace MMAAgent.Domain.Agents
{
    public sealed class ManagedFighter
    {
        public int Id { get; set; }
        public int AgentId { get; set; }
        public int FighterId { get; set; }
        public string SignedDate { get; set; } = "";
        public int ManagementPercent { get; set; }
        public bool IsActive { get; set; }
    }
}