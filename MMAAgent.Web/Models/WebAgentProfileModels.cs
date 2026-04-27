namespace MMAAgent.Web.Models;

public sealed record AgentTransactionVm(
    string Date,
    int Amount,
    string TxType,
    string? Notes);

public sealed record AgentProfileVm(
    int Id,
    string Name,
    string AgencyName,
    int Money,
    int Reputation,
    string CreatedDate,
    int ManagedFightersCount,
    int CampInvestmentLevel,
    int MedicalInvestmentLevel,
    IReadOnlyList<AgentTransactionVm> Transactions);
