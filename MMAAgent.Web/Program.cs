using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Application.Simulation;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using MMAAgent.Web.Components;
using MMAAgent.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddSingleton<ISavePathProvider, WebSavePathProvider>();
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<IDatabasePathInitializer, DatabasePathInitializer>();

builder.Services.AddScoped<IFighterRepository, SqliteFighterRepository>();
builder.Services.AddScoped<IPromotionRepository, PromotionRepositorySqlite>();
builder.Services.AddScoped<IGameStateRepository, SqliteGameStateRepository>();

builder.Services.AddScoped<IContractServiceSqlite, ContractServiceSqlite>();
builder.Services.AddScoped<IEventSimulator, SimulateEventSqlite>();
builder.Services.AddScoped<IPromotionEventScheduleRepository, PromotionEventScheduleRepositorySqlite>();
builder.Services.AddScoped<IWeeklySimulationService, WeeklySimulationService>();
builder.Services.AddScoped<GameTimeService>();

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
