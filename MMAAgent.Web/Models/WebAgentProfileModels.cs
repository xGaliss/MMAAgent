namespace MMAAgent.Web.Models;

public sealed record AgentTransactionVm(
    string Date,
    int Amount,
    string TxType,
    string? Notes);

public sealed record AgentPromotionRelationVm(
    int PromotionId,
    string PromotionName,
    int RelationshipScore,
    string RelationshipBand,
    string LastUpdatedDate,
    string? Notes);

public sealed record StaffImpactVm(
    string Title,
    string LevelLabel,
    string PrimaryEffect,
    string SecondaryEffect,
    string TertiaryEffect);

public sealed record AgentProfileVm(
    int Id,
    string Name,
    string AgencyName,
    string Nationality,
    string? CountryFlagUrl,
    string AvatarKey,
    int Money,
    int Reputation,
    int PublicReputation,
    int FighterTrust,
    int PromotionLeverage,
    string CreatedDate,
    int ManagedFightersCount,
    int CampInvestmentLevel,
    int MedicalInvestmentLevel,
    int ScoutingStaffLevel,
    int MediaStaffLevel,
    int NegotiationStaffLevel,
    int PerformanceStaffLevel,
    IReadOnlyList<StaffImpactVm> StaffImpacts,
    IReadOnlyList<AgentTransactionVm> Transactions,
    IReadOnlyList<AgentPromotionRelationVm> PromotionRelations);
