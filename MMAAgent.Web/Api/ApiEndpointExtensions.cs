using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Domain.Common;
using MMAAgent.Web.Infrastructure;
using MMAAgent.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Api;

public static class ApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapGameApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1")
            .WithTags("MMA Agent API");

        group.MapGet("/health", (IHostEnvironment environment) =>
            Results.Ok(new ApiHealthResponse(
                Name: "MMA Agent API",
                Environment: environment.EnvironmentName,
                UtcNow: DateTime.UtcNow.ToString("O"),
                ApiVersion: "v1")))
            .WithName("GetApiHealth");
        group.MapGet("/auth/me", GetAuthMeAsync)
            .WithName("GetAuthMe");

        group.MapGet("/session", GetSessionAsync)
            .WithName("GetCurrentSession");
        group.MapGet("/session/saves", GetDetectedSavesAsync)
            .WithName("GetDetectedSaves");
        group.MapPost("/session/load/configured", LoadConfiguredSaveAsync)
            .WithName("LoadConfiguredSave");
        group.MapPost("/session/load/last", LoadLastSaveAsync)
            .WithName("LoadLastSave");
        group.MapPost("/session/load/id", LoadBySaveIdAsync)
            .WithName("LoadSaveById");
        group.MapPost("/session/load/path", LoadByPathAsync)
            .WithName("LoadSaveByPath");
        group.MapPost("/session/create", CreateNewGameAsync)
            .WithName("CreateNewGame");
        group.MapPost("/session/persist", PersistCurrentSaveAsync)
            .WithName("PersistCurrentSave");

        group.MapGet("/dashboard", GetDashboardAsync)
            .WithName("GetDashboard");
        group.MapGet("/roster", GetRosterAsync)
            .WithName("GetRoster");
        group.MapGet("/world-feed", GetWorldFeedAsync)
            .WithName("GetWorldFeed");
        group.MapGet("/prospects", GetProspectsAsync)
            .WithName("GetProspects");
        group.MapGet("/agent", GetAgentProfileAsync)
            .WithName("GetAgentProfile");

        return endpoints;
    }

    private static IResult GetAuthMeAsync(WebGameSessionService gameSessionService)
    {
        var context = gameSessionService.GetSessionContext();
        return Results.Ok(new ApiAuthIdentityResponse(
            new ApiUserContextSummary(
                context.UserId,
                context.UserDisplayName,
                context.IsAuthenticated,
                context.AuthMode,
                context.AuthProvider,
                context.ProviderUserId)));
    }

    private static async Task<IResult> GetSessionAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository)
    {
        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetDetectedSavesAsync(
        WebMainMenuService mainMenuService,
        WebGameSessionService gameSessionService,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        try
        {
            var saves = await mainMenuService.DetectSavesAsync();
            return Results.Ok(saves.Select(x => new ApiSaveSummary(
                x.SaveId ?? string.Empty,
                x.OwnerUserId ?? string.Empty,
                x.Path,
                x.FileName,
                x.DisplayName ?? x.FileName,
                x.LastWriteTimeUtc,
                x.FileSizeBytes,
                x.IsCurrent,
                x.StorageKind ?? SaveStorageKinds.LocalSqliteFile,
                x.LifecycleState ?? SaveLifecycleStates.Ready,
                x.TemplateSource ?? SaveTemplateSources.DefaultTemplateDb,
                null,
                x.Path)));
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to detect saves.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }
    }

    private static async Task<IResult> LoadConfiguredSaveAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        try
        {
            await gameSessionService.LoadConfiguredSaveAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to load configured save.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Ok(response);
    }

    private static async Task<IResult> LoadLastSaveAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        bool loaded;
        try
        {
            loaded = await gameSessionService.TryLoadLastSaveAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to load last save.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        if (!loaded)
            return Results.NotFound(new { message = "No previous save could be loaded." });

        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Ok(response);
    }

    private static async Task<IResult> LoadBySaveIdAsync(
        ApiLoadBySaveIdRequest request,
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        if (string.IsNullOrWhiteSpace(request.SaveId))
            return Results.BadRequest(new { message = "A save id is required." });

        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        try
        {
            await gameSessionService.LoadBySaveIdAsync(request.SaveId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to load save by id.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Ok(response);
    }

    private static async Task<IResult> LoadByPathAsync(
        ApiLoadByPathRequest request,
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return Results.BadRequest(new { message = "A save path is required." });

        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        try
        {
            await gameSessionService.LoadByPathAsync(request.Path);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to load save by path.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateNewGameAsync(
        ApiCreateGameRequest request,
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName))
            return Results.BadRequest(new { message = "AgentName is required." });

        if (string.IsNullOrWhiteSpace(request.AgencyName))
            return Results.BadRequest(new { message = "AgencyName is required." });

        if (request.FighterCount <= 0)
            return Results.BadRequest(new { message = "FighterCount must be greater than zero." });

        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        try
        {
            await gameSessionService.CreateNewGameAsync(
                request.SaveName,
                request.AgentName,
                request.AgencyName,
                request.FighterCount,
                request.Nationality,
                request.AvatarKey);
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to create new game.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        var response = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        return Results.Created("/api/v1/session", response);
    }

    private static async Task<IResult> PersistCurrentSaveAsync(
        WebGameSessionService gameSessionService,
        ISavePersistenceService savePersistenceService,
        IHostEnvironment environment,
        IOptions<SupabaseJwtOptions> supabaseOptions,
        IOptions<ApiSecurityOptions> apiSecurityOptions)
    {
        var authGuard = RequireSaveOperationIdentity(
            gameSessionService,
            environment,
            supabaseOptions.Value,
            apiSecurityOptions.Value);
        if (authGuard is not null)
            return authGuard;

        bool persisted;
        try
        {
            persisted = await savePersistenceService.PersistCurrentSaveAsync("manual-api-persist");
        }
        catch (Exception ex)
        {
            if (environment.IsDevelopment())
            {
                return Results.Json(
                    new
                    {
                        message = "Failed to persist current save.",
                        detail = ex.ToString()
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            throw;
        }

        return Results.Ok(new ApiPersistSaveResponse(
            persisted,
            gameSessionService.CurrentSaveId,
            persisted ? "manual-api-persist" : "no-op"));
    }

    private static async Task<IResult> GetDashboardAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        WebDashboardStatsService dashboardStatsService,
        WebDashboardFeedService dashboardFeedService)
    {
        var session = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        if (!session.HasActiveSave)
            return Results.Conflict(new { message = "Load or create a save before requesting dashboard data." });

        var stats = await dashboardStatsService.LoadAsync();
        var feed = await dashboardFeedService.LoadAsync();

        return Results.Ok(new ApiEnvelope<ApiDashboardResponse>(
            gameSessionService.GetSessionContext().ToApiResponseContext(),
            new ApiDashboardResponse(
                session.GameState,
                stats.ToApi(),
                feed.ToApi())));
    }

    private static async Task<IResult> GetRosterAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        WebRosterService rosterService,
        string? searchText,
        string? weightClass,
        string? country,
        string? status,
        string? sortBy,
        int? take)
    {
        var session = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        if (!session.HasActiveSave)
            return Results.Conflict(new { message = "Load or create a save before requesting roster data." });

        var result = await rosterService.SearchAsync(
            searchText,
            weightClass,
            country,
            status,
            sortBy,
            Math.Clamp(take ?? 500, 1, 2000));

        return Results.Ok(new ApiEnvelope<ApiRosterResponse>(
            gameSessionService.GetSessionContext().ToApiResponseContext(),
            result.ToApi()));
    }

    private static async Task<IResult> GetWorldFeedAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        WebWorldFeedService worldFeedService)
    {
        var session = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        if (!session.HasActiveSave)
            return Results.Conflict(new { message = "Load or create a save before requesting world feed data." });

        var result = await worldFeedService.LoadAsync();
        return Results.Ok(new ApiEnvelope<ApiWorldFeedResponse>(
            gameSessionService.GetSessionContext().ToApiResponseContext(),
            result.ToApi()));
    }

    private static async Task<IResult> GetProspectsAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        WebProspectPipelineService prospectPipelineService)
    {
        var session = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        if (!session.HasActiveSave)
            return Results.Conflict(new { message = "Load or create a save before requesting prospect pipeline data." });

        var result = await prospectPipelineService.LoadAsync();
        return Results.Ok(new ApiEnvelope<ApiProspectPipelineResponse>(
            gameSessionService.GetSessionContext().ToApiResponseContext(),
            result.ToApi()));
    }

    private static async Task<IResult> GetAgentProfileAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository,
        WebAgentProfileService agentProfileService)
    {
        var session = await BuildSessionResponseAsync(
            gameSessionService,
            agentProfileRepository,
            gameStateRepository);

        if (!session.HasActiveSave)
            return Results.Conflict(new { message = "Load or create a save before requesting agent data." });

        var result = await agentProfileService.LoadAsync();
        if (result is null)
            return Results.NotFound(new { message = "Agent profile was not found for the current save." });

        return Results.Ok(new ApiEnvelope<ApiAgentProfileResponse>(
            gameSessionService.GetSessionContext().ToApiResponseContext(),
            result.ToApi()));
    }

    private static async Task<ApiSessionResponse> BuildSessionResponseAsync(
        WebGameSessionService gameSessionService,
        IAgentProfileRepository agentProfileRepository,
        IGameStateRepository gameStateRepository)
    {
        var sessionContext = gameSessionService.GetSessionContext();
        var user = new ApiUserContextSummary(
            sessionContext.UserId,
            sessionContext.UserDisplayName,
            sessionContext.IsAuthenticated,
            sessionContext.AuthMode,
            sessionContext.AuthProvider,
            sessionContext.ProviderUserId);
        var currentSaveRecord = await gameSessionService.GetCurrentSaveRecordAsync();
        var currentSavePath = gameSessionService.CurrentSavePath;
        var currentSaveId = gameSessionService.CurrentSaveId;
        var currentOwnerUserId = gameSessionService.CurrentOwnerUserId;

        if (string.IsNullOrWhiteSpace(currentSavePath)
            || string.IsNullOrWhiteSpace(currentSaveId)
            || currentSaveRecord is null
            || !string.Equals(currentSaveRecord.OwnerUserId, sessionContext.UserId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentSaveRecord.LifecycleState, SaveLifecycleStates.Ready, StringComparison.OrdinalIgnoreCase))
            return new ApiSessionResponse(false, user, null, null, null, null, null, null);

        var currentSave = new ApiSaveContextSummary(
            currentSaveRecord.SaveId,
            currentSaveRecord.OwnerUserId,
            currentSaveRecord.LocalPath ?? currentSaveRecord.StorageLocator,
            currentSaveRecord.StorageKind,
            currentSaveRecord.StorageLocator,
            currentSaveRecord.LifecycleState,
            currentSaveRecord.TemplateSource,
            currentSaveRecord.BackendInstance);

        try
        {
            var agent = await agentProfileRepository.GetAsync();
            var gameState = await gameStateRepository.GetAsync();

            if (gameState is null)
            {
                return new ApiSessionResponse(
                    HasActiveSave: false,
                    User: user,
                    CurrentSaveId: null,
                    CurrentOwnerUserId: null,
                    CurrentSavePath: null,
                    CurrentSave: null,
                    Agent: ToApi(agent),
                    GameState: null);
            }

            return new ApiSessionResponse(
                HasActiveSave: true,
                User: user,
                CurrentSaveId: currentSaveId,
                CurrentOwnerUserId: currentOwnerUserId,
                CurrentSavePath: currentSavePath,
                CurrentSave: currentSave,
                Agent: ToApi(agent),
                GameState: ToApi(gameState));
        }
        catch (SqliteException ex) when (IsBootstrapStateException(ex))
        {
            return new ApiSessionResponse(
                HasActiveSave: false,
                User: user,
                CurrentSaveId: null,
                CurrentOwnerUserId: null,
                CurrentSavePath: null,
                CurrentSave: null,
                Agent: null,
                GameState: null);
        }
    }

    private static ApiAgentSummary? ToApi(AgentProfile? agent)
        => agent is null
            ? null
            : new ApiAgentSummary(
                agent.Id,
                agent.Name,
                agent.AgencyName,
                string.IsNullOrWhiteSpace(agent.Nationality) ? "Spain" : agent.Nationality,
                string.IsNullOrWhiteSpace(agent.AvatarKey) ? "Promoter" : agent.AvatarKey);

    private static ApiGameStateSummary? ToApi(GameState? state)
        => state is null
            ? null
            : new ApiGameStateSummary(
                state.StartDate,
                state.CurrentDate,
                state.CurrentWeek,
                state.CurrentYear,
                state.WorldSeed);

    private static bool IsBootstrapStateException(SqliteException ex)
        => ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);

    private static IResult? RequireSaveOperationIdentity(
        WebGameSessionService gameSessionService,
        IHostEnvironment environment,
        SupabaseJwtOptions supabaseOptions,
        ApiSecurityOptions apiSecurityOptions)
    {
        if (!supabaseOptions.Enabled || !apiSecurityOptions.RequireExternalAuthForSaveOperations)
            return null;

        var context = gameSessionService.GetSessionContext();
        if (context.IsAuthenticated && string.Equals(context.AuthMode, AuthModes.External, StringComparison.OrdinalIgnoreCase))
            return null;

        if (environment.IsDevelopment()
            && apiSecurityOptions.AllowDevelopmentBypassForSaveOperations
            && (string.Equals(context.AuthMode, AuthModes.Header, StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.AuthMode, AuthModes.Local, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return Results.Json(
            new
            {
                message = "This save operation requires an authenticated external user.",
                requiredAuthMode = AuthModes.External
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }
}
