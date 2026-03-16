namespace MMAAgent.Domain.Agents
{
    public sealed class AgentProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string AgencyName { get; set; } = "";
        public int Money { get; set; }
        public int Reputation { get; set; }
        public string CreatedDate { get; set; } = "";
    }
}