using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Infrastructure.Files;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using MMAAgent.Web.Api;
using MMAAgent.Web.Components;
using MMAAgent.Web.Infrastructure;
using MMAAgent.Web.Services;
using MMAAgent.Infrastructure.Persistance.Sqlite.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;


var builder = WebApplication.CreateBuilder(args);
var supabaseAuthOptions = builder.Configuration
    .GetSection(SupabaseJwtOptions.SectionName)
    .Get<SupabaseJwtOptions>() ?? new SupabaseJwtOptions();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<AuthBridgeOptions>(
    builder.Configuration.GetSection(AuthBridgeOptions.SectionName));
builder.Services.Configure<ApiSecurityOptions>(
    builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
builder.Services.Configure<SaveCatalogOptions>(
    builder.Configuration.GetSection(SaveCatalogOptions.SectionName));
builder.Services.Configure<SupabaseJwtOptions>(
    builder.Configuration.GetSection(SupabaseJwtOptions.SectionName));
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AuthBridgeDefaults.HybridScheme;
        options.DefaultChallengeScheme = AuthBridgeDefaults.HybridScheme;
    })
    .AddPolicyScheme(
        AuthBridgeDefaults.HybridScheme,
        "JWT bearer or development bridge",
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (supabaseAuthOptions.Enabled
                    && !string.IsNullOrWhiteSpace(authHeader)
                    && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return AuthBridgeDefaults.Scheme;
            };
        })
    .AddScheme<AuthenticationSchemeOptions, AuthBridgeAuthenticationHandler>(
        AuthBridgeDefaults.Scheme,
        _ => { })
    .AddJwtBearer(options =>
    {
        var authority = string.IsNullOrWhiteSpace(supabaseAuthOptions.Issuer)
            ? null
            : supabaseAuthOptions.Issuer.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
            options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
        }

        options.RequireHttpsMetadata = supabaseAuthOptions.RequireHttpsMetadata;
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = supabaseAuthOptions.ValidateIssuer && !string.IsNullOrWhiteSpace(supabaseAuthOptions.Issuer),
            ValidIssuer = supabaseAuthOptions.Issuer,
            ValidateAudience = supabaseAuthOptions.ValidateAudience
                               && (!string.IsNullOrWhiteSpace(supabaseAuthOptions.Audience)
                                   || supabaseAuthOptions.Audiences.Length > 0),
            ValidAudience = supabaseAuthOptions.Audience,
            ValidAudiences = supabaseAuthOptions.Audiences.Length > 0 ? supabaseAuthOptions.Audiences : null,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = supabaseAuthOptions.ValidateLifetime,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddTransient<IClaimsTransformation, SupabaseClaimsTransformation>();

builder.Services.AddScoped<MMAAgent.Application.Abstractions.IContractOfferRepository,
    MMAAgent.Infrastructure.Persistence.Sqlite.Repositories.ContractOfferRepository>();

// Service nuevo de contratos
builder.Services.AddScoped<MMAAgent.Application.Abstractions.IContractLifecycleService,
    MMAAgent.Infrastructure.Persistance.Sqlite.Services.ContractLifecycleServiceSqlite>();

builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();


builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContextAccessor, HttpAwareUserContextAccessor>();
builder.Services.AddSingleton<JsonSaveCatalogService>();
builder.Services.AddSingleton<PostgresSaveCatalogService>();
builder.Services.AddSingleton<ISaveCatalogService, ConfiguredSaveCatalogService>();
builder.Services.AddSingleton<PostgresSaveSnapshotStore>();
builder.Services.AddSingleton<ISaveSnapshotStore>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SaveCatalogOptions>>().Value;
    if (string.Equals(options.Provider, SaveCatalogProviders.SupabasePostgres, StringComparison.OrdinalIgnoreCase)
        && (!string.IsNullOrWhiteSpace(options.PostgresConnectionString)
            || !string.IsNullOrWhiteSpace(options.FallbackPostgresConnectionString)))
    {
        return sp.GetRequiredService<PostgresSaveSnapshotStore>();
    }

    return new NoopSaveSnapshotStore();
});
builder.Services.AddSingleton<ISavePersistenceService, SavePersistenceService>();
builder.Services.AddSingleton<WebSaveSessionContext>();
builder.Services.AddSingleton<ISaveSessionContext>(sp => sp.GetRequiredService<WebSaveSessionContext>());
builder.Services.AddSingleton<ISavePathProvider>(sp => sp.GetRequiredService<WebSaveSessionContext>());
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<IDatabasePathInitializer, DatabasePathInitializer>();

builder.Services.AddScoped<IFighterRepository, SqliteFighterRepository>();
builder.Services.AddScoped<IPromotionRepository, PromotionRepositorySqlite>();
builder.Services.AddScoped<IGameStateRepository, SqliteGameStateRepository>();
builder.Services.AddScoped<IEventRepository, SqliteEventRepository>();

builder.Services.AddScoped<IAgentProfileRepository, AgentProfileRepository>();
builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<IFightOfferRepository, FightOfferRepository>();
builder.Services.AddScoped<IManagedFighterRepository, ManagedFighterRepository>();

builder.Services.AddScoped<IContractServiceSqlite, ContractServiceSqlite>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, PromotionEventScheduleRepositorySqlite>();
builder.Services.AddScoped<IWeeklySimulationService, WeeklySimulationService>();
builder.Services.AddScoped<GameTimeService>();

builder.Services.AddScoped<DbBootstrap>();
builder.Services.AddScoped<CareerSchemaPreparationService>();
builder.Services.AddScoped<IFighterWorldService, FighterWorldServiceSqlite>();
builder.Services.AddScoped<WorldEcosystemServiceSqlite>();
builder.Services.AddScoped<IWorldAgendaService, WorldAgendaServiceSqlite>();
builder.Services.AddScoped<IDailyWorldEventService, DailyWorldEventServiceSqlite>();
builder.Services.AddScoped<WorldFighterGeneratorSqlite>();
builder.Services.AddScoped<InitialSigningPassSqlite>();
builder.Services.AddScoped<BuildInitialRankingsSqlite>();
builder.Services.AddScoped<PromotionScheduleSeeder>();

builder.Services.AddScoped<WebGameSessionService>();
builder.Services.AddScoped<WebInboxService>();
builder.Services.AddScoped<DecisionPopupService>();
builder.Services.AddScoped<WebAgentProfileService>();
builder.Services.AddScoped<WebTimeAdvanceService>();
builder.Services.AddScoped<WebDashboardStatsService>();
builder.Services.AddScoped<WebRosterService>();
builder.Services.AddScoped<WebDivisionsService>();
builder.Services.AddScoped<WebPromotionProfileService>();
builder.Services.AddScoped<WebMyFightersService>();
builder.Services.AddScoped<WebMainMenuService>();
builder.Services.AddScoped<WebDashboardFeedService>();
builder.Services.AddScoped<WebWorldFeedService>();
builder.Services.AddScoped<WebProspectPipelineService>();
builder.Services.AddScoped<WebWeeklySummaryService>();
builder.Services.AddScoped<InboxStatusService>();
builder.Services.AddScoped<HighlightNotificationService>();
builder.Services.AddScoped<FightProfileReadService>();
builder.Services.AddScoped<IFighterSigningService, FighterSigningServiceSqlite>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();
builder.Services.AddScoped<IFightOfferResponseService, FightOfferResponseServiceSqlite>();
builder.Services.AddScoped<IMatchmakingService, MatchmakingServiceSqlite>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();

builder.Services.AddScoped<SqliteActionBridge>();
builder.Services.AddScoped<WebFighterActionService>();
builder.Services.AddScoped<WebDivisionActionService>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();
builder.Services.AddScoped<WebInboxService>();
builder.Services.AddScoped<WebDashboardStatsService>();
builder.Services.AddScoped<WebDashboardFeedService>();
builder.Services.AddScoped<WebWeeklySummaryService>();

builder.Services.AddScoped<IContractOfferRepository, ContractOfferRepository>();
builder.Services.AddScoped<IContractOfferResponseService, ContractOfferResponseServiceSqlite>();

builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<IFightOfferResponseService, FightOfferResponseServiceSqlite>();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabasePathInitializer>();
    initializer.Initialize();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapGameApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
