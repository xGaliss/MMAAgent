using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Files;
using MMAAgent.Infrastructure.Generation;
using MMAAgent.Infrastructure.Persistence.Sqlite.Services;
using System;

namespace MMAAgent.Desktop.Services
{
    public sealed class NewGameService
    {
        private readonly DbBootstrap _bootstrap;
        private readonly ISavePathProvider _savePathProvider;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly WorldFighterGeneratorSqlite _worldGen;
        private readonly InitialSigningPassSqlite _initialSigning;
        private readonly BuildInitialRankingsSqlite _rankings;
        private readonly PromotionScheduleSeeder _scheduleSeeder;

        public NewGameService(
            DbBootstrap bootstrap,
            ISavePathProvider savePathProvider,
            IGameStateRepository gameStateRepo,
            WorldFighterGeneratorSqlite worldGen,
            InitialSigningPassSqlite initialSigning,
            BuildInitialRankingsSqlite rankings,
            PromotionScheduleSeeder scheduleSeeder)
        {
            _bootstrap = bootstrap;
            _savePathProvider = savePathProvider;
            _gameStateRepo = gameStateRepo;
            _worldGen = worldGen;
            _initialSigning = initialSigning;
            _rankings = rankings;
            _scheduleSeeder = scheduleSeeder;
        }

        public string CreateAndLoadNewGame(string? saveName = null, int fighterCount = 800)
        {
            // 1) crear save db desde plantilla
            var templateDbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets",
                "Database",
                "MMA_Agent.db"
            );

            var savePath = _bootstrap.CreateNewSaveFromTemplate(templateDbPath, saveName);

            // 2) setear DB activa
            _savePathProvider.Set(savePath);

            // 3) seed aleatorio
            var seed = Random.Shared.Next(1, int.MaxValue);
            System.Diagnostics.Debug.WriteLine($"[NEW GAME] Seed = {seed}");

            // 4) crear/actualizar GameState (UPSERT)
            var startDate = new DateTime(2026, 1, 1);
            _gameStateRepo.EnsureCreatedAsync(startDate, seed).GetAwaiter().GetResult();

            // 5) lee el seed REAL guardado (por seguridad)
            var state = _gameStateRepo.GetAsync().GetAwaiter().GetResult();
            var realSeed = state?.WorldSeed ?? seed;

            // ✅ 5.5) inicializa calendario de eventos (NextEventWeek absoluto = 0)
            _scheduleSeeder.InitializeForNewSaveAsync(startAbsoluteWeek: 0)
                           .GetAwaiter().GetResult();

            // 6) generar mundo
            _worldGen.SetSeed(realSeed);
            _worldGen.GenerateCount = fighterCount;
            _worldGen.ClearExistingFighters = true;
            _worldGen.GenerateWorld();

            // 7) signing pass (asigna PromotionId + contrato)
            _initialSigning.SetSeed(realSeed);
            _initialSigning.RunAsync().GetAwaiter().GetResult();

            // 8) rankings + titles
            _rankings.SetSeed(realSeed);
            _rankings.RunAsync().GetAwaiter().GetResult();

            // 9) guardar "última partida"
            LastSave.Save(savePath);

            return savePath;
        }
    }
}