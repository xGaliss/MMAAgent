namespace MMAAgent.Web.Models;

public sealed record AgentProfileVm(
    int Id,
    string Name,
    string AgencyName,
    int Money,
    int Reputation,
    string CreatedDate,
    int ManagedFightersCount);
