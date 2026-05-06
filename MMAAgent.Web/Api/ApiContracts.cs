using MMAAgent.Web.Models;
using MMAAgent.Web.Services;
using MMAAgent.Web.Infrastructure;

namespace MMAAgent.Web.Api;

public sealed record ApiHealthResponse(
    string Name,
    string Environment,
    string UtcNow,
    string ApiVersion);

public sealed record ApiAuthIdentityResponse(
    ApiUserContextSummary User);

public sealed record ApiSaveSummary(
    string SaveId,
    string OwnerUserId,
    string Path,
    string FileName,
    string DisplayName,
    DateTime LastWriteTimeUtc,
    long FileSizeBytes,
    bool IsCurrent,
    string StorageKind,
    string LifecycleState,
    string TemplateSource,
    string? BackendInstance,
    string StorageLocator);

public sealed record ApiAgentSummary(
    int Id,
    string Name,
    string AgencyName,
    string Nationality,
    string AvatarKey);

public sealed record ApiGameStateSummary(
    string StartDate,
    string CurrentDate,
    int CurrentWeek,
    int CurrentYear,
    int WorldSeed);

public sealed record ApiUserContextSummary(
    string UserId,
    string DisplayName,
    bool IsAuthenticated,
    string AuthMode,
    string? Provider,
    string? ProviderUserId);

public sealed record ApiSaveContextSummary(
    string SaveId,
    string OwnerUserId,
    string SavePath,
    string StorageKind,
    string StorageLocator,
    string LifecycleState,
    string TemplateSource,
    string? BackendInstance);

public sealed record ApiResponseContext(
    ApiUserContextSummary User,
    ApiSaveContextSummary? Save);

public sealed record ApiEnvelope<T>(
    ApiResponseContext Context,
    T Data);

public sealed record ApiSessionResponse(
    bool HasActiveSave,
    ApiUserContextSummary User,
    string? CurrentSaveId,
    string? CurrentOwnerUserId,
    string? CurrentSavePath,
    ApiSaveContextSummary? CurrentSave,
    ApiAgentSummary? Agent,
    ApiGameStateSummary? GameState);

public sealed record ApiLoadBySaveIdRequest(string SaveId);
public sealed record ApiLoadByPathRequest(string Path);

public sealed record ApiCreateGameRequest(
    string? SaveName,
    string AgentName,
    string AgencyName,
    int FighterCount,
    string? Nationality,
    string? AvatarKey);

public sealed record ApiPersistSaveResponse(
    bool Persisted,
    string? SaveId,
    string? Reason);

public sealed record ApiDashboardStatsResponse(
    int FighterCount,
    int PromotionCount,
    int UnreadMessages,
    int PendingFightOffers,
    int PendingContractOffers);

public sealed record ApiAgendaItemResponse(
    string ScheduledDate,
    string EventType,
    string Headline,
    string? Subtitle,
    int Priority);

public sealed record ApiDashboardFeedResponse(
    IReadOnlyList<ApiAgendaItemResponse> Agenda,
    IReadOnlyList<string> CompetitivePulse,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Messages,
    IReadOnlyList<string> Managed,
    IReadOnlyList<string> Champions,
    IReadOnlyList<string> PendingFightOfferItems,
    IReadOnlyList<string> PendingContractOfferItems);

public sealed record ApiDashboardResponse(
    ApiGameStateSummary? GameState,
    ApiDashboardStatsResponse Stats,
    ApiDashboardFeedResponse Feed);

public sealed record ApiRosterFilterOptionsResponse(
    IReadOnlyList<string> WeightClasses,
    IReadOnlyList<string> Countries);

public sealed record ApiRosterListItemResponse(
    int Id,
    string Name,
    int Age,
    string WeightClass,
    string CountryName,
    string? CountryFlagUrl,
    string PromotionName,
    string Status,
    int Wins,
    int Losses,
    int Draws,
    string ScoutRead,
    string ConfidenceLabel,
    string BaseStyle,
    int ReliabilityScore,
    int MediaHeat,
    string ScoutStatus);

public sealed record ApiRosterResponse(
    int TotalCount,
    IReadOnlyList<ApiRosterListItemResponse> Items,
    ApiRosterFilterOptionsResponse Filters);

public sealed record ApiWorldFeedItemResponse(
    string Bucket,
    string Headline,
    string Summary,
    string Date,
    string Tone,
    string? LinkHref);

public sealed record ApiWorldFeedResponse(
    IReadOnlyList<ApiWorldFeedItemResponse> Headlines,
    IReadOnlyList<ApiWorldFeedItemResponse> Storylines);

public sealed record ApiProspectItemResponse(
    int Id,
    string Name,
    int Age,
    string WeightClass,
    string CountryName,
    string? CountryFlagUrl,
    string PromotionName,
    string CareerStage,
    string CircuitType,
    int Wins,
    int Losses,
    int Draws,
    int AmateurWins,
    int AmateurLosses,
    int AmateurDraws,
    int Skill,
    int Potential,
    int Popularity,
    int Marketability,
    int Momentum,
    int MediaHeat,
    string ScoutStatus,
    string ConfidenceLabel,
    bool IsWatched,
    bool IsReadyForProJump,
    string Summary);

public sealed record ApiProspectPipelineResponse(
    int AmateurCount,
    int WatchedCount,
    int ReadyCount,
    int GraduatingProFreeAgentsCount,
    IReadOnlyList<ApiProspectItemResponse> WatchedProspects,
    IReadOnlyList<ApiProspectItemResponse> ReadyForProJump,
    IReadOnlyList<ApiProspectItemResponse> HotAmateurs,
    IReadOnlyList<ApiProspectItemResponse> NewProFreeAgents);

public sealed record ApiAgentTransactionResponse(
    string Date,
    int Amount,
    string TxType,
    string? Notes);

public sealed record ApiAgentPromotionRelationResponse(
    int PromotionId,
    string PromotionName,
    int RelationshipScore,
    string RelationshipBand,
    string LastUpdatedDate,
    string? Notes);

public sealed record ApiStaffImpactResponse(
    string Title,
    string LevelLabel,
    string PrimaryEffect,
    string SecondaryEffect,
    string TertiaryEffect);

public sealed record ApiAgentProfileResponse(
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
    IReadOnlyList<ApiStaffImpactResponse> StaffImpacts,
    IReadOnlyList<ApiAgentTransactionResponse> Transactions,
    IReadOnlyList<ApiAgentPromotionRelationResponse> PromotionRelations);

public static class ApiContractMappings
{
    public static ApiDashboardStatsResponse ToApi(this DashboardStatsVm vm)
        => new(
            vm.FighterCount,
            vm.PromotionCount,
            vm.UnreadMessages,
            vm.PendingFightOffers,
            vm.PendingContractOffers);

    public static ApiDashboardFeedResponse ToApi(this DashboardFeedVm vm)
        => new(
            vm.Agenda.Select(ToApi).ToArray(),
            vm.CompetitivePulse,
            vm.Events,
            vm.Messages,
            vm.Managed,
            vm.Champions,
            vm.PendingFightOfferItems,
            vm.PendingContractOfferItems);

    public static ApiAgendaItemResponse ToApi(this AgendaItemVm vm)
        => new(
            vm.ScheduledDate,
            vm.EventType,
            vm.Headline,
            vm.Subtitle,
            vm.Priority);

    public static ApiRosterResponse ToApi(this RosterQueryResult result)
        => new(
            result.TotalCount,
            result.Items.Select(ToApi).ToArray(),
            new ApiRosterFilterOptionsResponse(
                result.Filters.WeightClasses,
                result.Filters.Countries));

    public static ApiRosterListItemResponse ToApi(this RosterListItemVm vm)
        => new(
            vm.Id,
            vm.Name,
            vm.Age,
            vm.WeightClass,
            vm.CountryName,
            vm.CountryFlagUrl,
            vm.PromotionName,
            vm.Status,
            vm.Wins,
            vm.Losses,
            vm.Draws,
            vm.ScoutRead,
            vm.ConfidenceLabel,
            vm.BaseStyle,
            vm.ReliabilityScore,
            vm.MediaHeat,
            vm.ScoutStatus);

    public static ApiWorldFeedResponse ToApi(this WorldFeedVm vm)
        => new(
            vm.Headlines.Select(ToApi).ToArray(),
            vm.Storylines.Select(ToApi).ToArray());

    public static ApiWorldFeedItemResponse ToApi(this WorldFeedItemVm vm)
        => new(
            vm.Bucket,
            vm.Headline,
            vm.Summary,
            vm.Date,
            vm.Tone,
            vm.LinkHref);

    public static ApiProspectPipelineResponse ToApi(this ProspectPipelineVm vm)
        => new(
            vm.AmateurCount,
            vm.WatchedCount,
            vm.ReadyCount,
            vm.GraduatingProFreeAgentsCount,
            vm.WatchedProspects.Select(ToApi).ToArray(),
            vm.ReadyForProJump.Select(ToApi).ToArray(),
            vm.HotAmateurs.Select(ToApi).ToArray(),
            vm.NewProFreeAgents.Select(ToApi).ToArray());

    public static ApiProspectItemResponse ToApi(this ProspectItemVm vm)
        => new(
            vm.Id,
            vm.Name,
            vm.Age,
            vm.WeightClass,
            vm.CountryName,
            vm.CountryFlagUrl,
            vm.PromotionName,
            vm.CareerStage,
            vm.CircuitType,
            vm.Wins,
            vm.Losses,
            vm.Draws,
            vm.AmateurWins,
            vm.AmateurLosses,
            vm.AmateurDraws,
            vm.Skill,
            vm.Potential,
            vm.Popularity,
            vm.Marketability,
            vm.Momentum,
            vm.MediaHeat,
            vm.ScoutStatus,
            vm.ConfidenceLabel,
            vm.IsWatched,
            vm.IsReadyForProJump,
            vm.Summary);

    public static ApiAgentProfileResponse ToApi(this AgentProfileVm vm)
        => new(
            vm.Id,
            vm.Name,
            vm.AgencyName,
            vm.Nationality,
            vm.CountryFlagUrl,
            vm.AvatarKey,
            vm.Money,
            vm.Reputation,
            vm.PublicReputation,
            vm.FighterTrust,
            vm.PromotionLeverage,
            vm.CreatedDate,
            vm.ManagedFightersCount,
            vm.CampInvestmentLevel,
            vm.MedicalInvestmentLevel,
            vm.ScoutingStaffLevel,
            vm.MediaStaffLevel,
            vm.NegotiationStaffLevel,
            vm.PerformanceStaffLevel,
            vm.StaffImpacts.Select(ToApi).ToArray(),
            vm.Transactions.Select(ToApi).ToArray(),
            vm.PromotionRelations.Select(ToApi).ToArray());

    public static ApiStaffImpactResponse ToApi(this StaffImpactVm vm)
        => new(
            vm.Title,
            vm.LevelLabel,
            vm.PrimaryEffect,
            vm.SecondaryEffect,
            vm.TertiaryEffect);

    public static ApiAgentTransactionResponse ToApi(this AgentTransactionVm vm)
        => new(
            vm.Date,
            vm.Amount,
            vm.TxType,
            vm.Notes);

    public static ApiAgentPromotionRelationResponse ToApi(this AgentPromotionRelationVm vm)
        => new(
            vm.PromotionId,
            vm.PromotionName,
            vm.RelationshipScore,
            vm.RelationshipBand,
            vm.LastUpdatedDate,
            vm.Notes);

    public static ApiResponseContext ToApiResponseContext(this WebGameSessionService.SessionContextSnapshot snapshot)
        => new(
            new ApiUserContextSummary(
                snapshot.UserId,
                snapshot.UserDisplayName,
                snapshot.IsAuthenticated,
                snapshot.AuthMode,
                snapshot.AuthProvider,
                snapshot.ProviderUserId),
            string.IsNullOrWhiteSpace(snapshot.SaveId) || string.IsNullOrWhiteSpace(snapshot.SavePath)
                ? null
                : new ApiSaveContextSummary(
                    snapshot.SaveId,
                    snapshot.OwnerUserId ?? snapshot.UserId,
                    snapshot.SavePath,
                    snapshot.StorageKind ?? SaveStorageKinds.LocalSqliteFile,
                    snapshot.StorageLocator ?? snapshot.SavePath,
                    snapshot.SaveState ?? SaveLifecycleStates.Ready,
                    snapshot.TemplateSource ?? SaveTemplateSources.DefaultTemplateDb,
                    snapshot.BackendInstance));
}
