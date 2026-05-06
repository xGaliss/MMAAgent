namespace MMAAgent.Domain.Agents
{
    public sealed class AgentProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string AgencyName { get; set; } = "";
        public string Nationality { get; set; } = "";
        public string AvatarKey { get; set; } = "";
        public int Money { get; set; }
        public int Reputation { get; set; }
        public int PublicReputation { get; set; }
        public int FighterTrust { get; set; }
        public int PromotionLeverage { get; set; }
        public int ScoutingStaffLevel { get; set; }
        public int MediaStaffLevel { get; set; }
        public int NegotiationStaffLevel { get; set; }
        public int PerformanceStaffLevel { get; set; }
        public string CreatedDate { get; set; } = "";
    }
}
