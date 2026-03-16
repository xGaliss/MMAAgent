using Microsoft.Extensions.DependencyInjection;
using MMAAgent.Application;
using MMAAgent.Application.Abstractions;

using MMAAgent.Application.Simulation;
using MMAAgent.Desktop.Services;
using MMAAgent.Desktop.ViewModels;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite.Repositories;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using System;
using System.IO;

namespace MMAAgent.Desktop
{
    public partial class App : System.Windows.Application
    {
        
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);

            Services = services.BuildServiceProvider();

            var last = LastSave.Load();
            if (last != null)
            {
                var sp = Services.GetRequiredService<ISavePathProvider>();
                sp.Set(last);
            }

            // ✅ MainWindow creada por DI
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Ventana principal
            services.AddSingleton<MainWindow>();

            // ✅ 1) SavePathProvider (ruta de la partida actual)
            services.AddSingleton<ISavePathProvider, MMAAgent.Desktop.Services.SavePathProvider>();

            // ✅ 2) ConnectionFactory (DI le inyecta el ISavePathProvider)
            services.AddSingleton<SqliteConnectionFactory>();

            // Repos / ViewModels
            services.AddSingleton<IFighterRepository, SqliteFighterRepository>();
            services.AddSingleton<ViewModels.RosterViewModel>();
            services.AddSingleton<MMAAgent.Infrastructure.Files.DbBootstrap>();
            services.AddSingleton<MMAAgent.Desktop.Services.NewGameService>();
            services.AddSingleton<MMAAgent.Infrastructure.Generation.WorldFighterGeneratorSqlite>();
            services.AddSingleton<MMAAgent.Desktop.Services.NewGameService>();
            services.AddSingleton<ViewModels.MainMenuViewModel>();
            services.AddSingleton<ViewModels.GameViewModel>();
            services.AddSingleton<ViewModels.MainViewModel>();
            services.AddSingleton<IGameStateRepository, SqliteGameStateRepository>();
            services.AddSingleton<GameTimeService>();
            services.AddSingleton<MMAAgent.Application.Simulation.IWeeklySimulationService, MMAAgent.Application.Simulation.WeeklySimulationService>();
            services.AddSingleton<MMAAgent.Infrastructure.Persistence.Sqlite.Services.BuildInitialRankingsSqlite>();
            services.AddSingleton<MMAAgent.Infrastructure.Persistence.Sqlite.Services.InitialSigningPassSqlite>();
            services.AddSingleton<NewGameService>();
            services.AddSingleton<InitialSigningPassSqlite>();
            services.AddSingleton<BuildInitialRankingsSqlite>();
            services.AddSingleton<WorldFighterGeneratorSqlite>();
            services.AddSingleton<IWeeklySimulationService, WeeklySimulationService>();
            services.AddSingleton<GameTimeService>();
            services.AddSingleton<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
            services.AddSingleton<IEventSimulator, SimulateEventSqlite>();
            services.AddSingleton<IWeeklySimulationService, WeeklySimulationService>();
            services.AddSingleton<IPromotionEventScheduleRepository, SqlitePromotionEventScheduleRepository>();
            services.AddSingleton<IEventSimulator, SimulateEventSqlite>();
            services.AddSingleton<IWeeklySimulationService, WeeklySimulationService>();
            services.AddSingleton<PromotionScheduleSeeder>();
            services.AddSingleton<IContractServiceSqlite, ContractServiceSqlite>();
            services.AddSingleton<IEventRepository, SqliteEventRepository>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainMenuViewModel>();

            services.AddSingleton<GameShellViewModel>();
            //services.AddSingleton<DashboardViewModel>();
            //services.AddSingleton<PromotionsViewModel>();
            services.AddSingleton<RosterViewModel>();
            services.AddSingleton<FightProfileViewModel>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<PromotionsViewModel>();
            services.AddSingleton<RosterViewModel>();
            services.AddSingleton<GameShellViewModel>();
            // si sigues usando el viejo
            services.AddSingleton<GameViewModel>();
            services.AddSingleton<PromotionsViewModel>();
            services.AddSingleton<MMAAgent.Infrastructure.Persistence.Sqlite.Repositories.PromotionRepositorySqlite>();    
            services.AddTransient<PromotionsViewModel>();
            services.AddSingleton<MMAAgent.Infrastructure.Persistence.Sqlite.Repositories.PromotionEventScheduleRepositorySqlite>();
            services.AddSingleton<IPromotionEventScheduleRepository, MMAAgent.Infrastructure.Persistence.Sqlite.Repositories.PromotionEventScheduleRepositorySqlite>();
            services.AddSingleton<IPromotionRepository, PromotionRepositorySqlite>();
            services.AddSingleton<PromotionsViewModel>();
            services.AddSingleton<MMAAgent.Application.Abstractions.IAgentProfileRepository, MMAAgent.Infrastructure.Persistence.Sqlite.Repositories.AgentProfileRepository>();
            services.AddSingleton<MMAAgent.Desktop.Services.CreateAgentProfileService>();
            services.AddSingleton<MMAAgent.Desktop.ViewModels.NewGameSetupViewModel>();
            services.AddSingleton<IManagedFighterRepository, ManagedFighterRepository>();
            services.AddSingleton<MyFightersViewModel>();
            // ⚠️ dbPath aquí todavía NO lo usamos, porque ahora trabajamos con DB de partida (save).
            // Cuando hagamos DbBootstrap, ahí sí necesitaremos la ruta a la plantilla.
        }
    }
}