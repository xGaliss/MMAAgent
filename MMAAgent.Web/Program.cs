using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Infrastructure.Files;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using MMAAgent.Web.Components;
using MMAAgent.Web.Infrastructure;
using MMAAgent.Web.Services;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistance.Sqlite.Services;
using MMAAgent.Web.Services;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));



builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();


builder.Services.AddSingleton<ISavePathProvider, WebSavePathProvider>();
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
builder.Services.AddScoped<WorldFighterGeneratorSqlite>();
builder.Services.AddScoped<InitialSigningPassSqlite>();
builder.Services.AddScoped<BuildInitialRankingsSqlite>();
builder.Services.AddScoped<PromotionScheduleSeeder>();

builder.Services.AddScoped<WebGameSessionService>();
builder.Services.AddScoped<WebInboxService>();
builder.Services.AddScoped<WebAgentProfileService>();
builder.Services.AddScoped<WebDashboardStatsService>();
builder.Services.AddScoped<WebRosterService>();
builder.Services.AddScoped<WebPromotionProfileService>();
builder.Services.AddScoped<WebMyFightersService>();
builder.Services.AddScoped<WebMainMenuService>();
builder.Services.AddScoped<WebDashboardFeedService>();
builder.Services.AddScoped<WebWeeklySummaryService>();
builder.Services.AddScoped<FightProfileReadService>();
builder.Services.AddScoped<IFighterSigningService, FighterSigningServiceSqlite>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();
builder.Services.AddScoped<IFightOfferResponseService, FightOfferResponseServiceSqlite>();
builder.Services.AddScoped<IMatchmakingService, MatchmakingServiceSqlite>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();

builder.Services.AddScoped<SqliteActionBridge>();
builder.Services.AddScoped<WebFighterActionService>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
builder.Services.AddScoped<IWeeklyWorldUpdateService, WeeklyWorldUpdateService>();
builder.Services.AddScoped<IFightOfferGenerationService, FightOfferGenerationServiceSqlite>();


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

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
