using MMAAgent.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class InitialSigningPassSqlite
    {
        private readonly SqliteConnectionFactory _factory;
        private Random _rng = new Random();

        // Signing rules
        public double BaseSignChance { get; set; } = 0.55;     // 0..1
        public double PrestigeInfluence { get; set; } = 0.25;  // 0..1
        public double BudgetInfluence { get; set; } = 0.20;    // 0..1

        // Contract rules
        public int MinContractFights { get; set; } = 2;
        public int MaxContractFights { get; set; } = 6;

        // Salary rules
        public int MinSalary { get; set; } = 500;
        public int MaxSalary { get; set; } = 200_000;

        public InitialSigningPassSqlite(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public void SetSeed(int seed) => _rng = new Random(seed);

        public async Task RunAsync()
        {
            using var conn = _factory.CreateConnection();

            // Promotions activas
            var promotions = new List<PromotionRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Name, MinSkillToSign, MinPopularityToSign, Prestige, Budget, IsActive
FROM Promotions
WHERE IsActive = 1
ORDER BY Prestige DESC;";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    promotions.Add(new PromotionRow
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Name = r["Name"]?.ToString() ?? "",
                        MinSkillToSign = Convert.ToInt32(r["MinSkillToSign"]),
                        MinPopularityToSign = Convert.ToInt32(r["MinPopularityToSign"]),
                        Prestige = Convert.ToInt32(r["Prestige"]),
                        Budget = Convert.ToInt32(r["Budget"]),
                        IsActive = Convert.ToInt32(r["IsActive"])
                    });
                }
            }

            if (promotions.Count == 0)
                return;

            // Free agents
            var freeAgents = new List<FighterRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Skill, Popularity
FROM Fighters
WHERE PromotionId IS NULL OR ContractStatus = 'FreeAgent';";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    freeAgents.Add(new FighterRow
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Skill = Convert.ToInt32(r["Skill"]),
                        Popularity = Convert.ToInt32(r["Popularity"])
                    });
                }
            }

            int signed = 0;

            using (var tx = conn.BeginTransaction())
            {
                foreach (var f in freeAgents)
                {
                    bool didSign = false;

                    foreach (var p in promotions)
                    {
                        if (!MeetsRequirements(f, p)) continue;

                        double chance = ComputeSignChance(p);
                        if (_rng.NextDouble() <= chance)
                        {
                            int fights = _rng.Next(MinContractFights, MaxContractFights + 1);
                            int salary = ComputeSalary(f.Skill, f.Popularity, p.Prestige, p.Budget);

                            using var cmd = conn.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = $pid,
    Salary = $salary,
    TotalFightsInContract = $fights,
    ContractFightsRemaining = $fights,
    ContractStatus = 'Active',
    NegotiationTurnsRemaining = 0
WHERE Id = $fid;";
                            cmd.Parameters.AddWithValue("$pid", p.Id);
                            cmd.Parameters.AddWithValue("$salary", salary);
                            cmd.Parameters.AddWithValue("$fights", fights);
                            cmd.Parameters.AddWithValue("$fid", f.Id);
                            await cmd.ExecuteNonQueryAsync();

                            signed++;
                            didSign = true;
                            break;
                        }
                    }

                    if (!didSign)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = NULL,
    Salary = 0,
    TotalFightsInContract = 0,
    ContractFightsRemaining = 0,
    ContractStatus = 'FreeAgent',
    NegotiationTurnsRemaining = 0
WHERE Id = $fid;";
                        cmd.Parameters.AddWithValue("$fid", f.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }

            // opcional: logs
            // var totalWithPromotion = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM Fighters WHERE PromotionId IS NOT NULL;");
            // var totalFree = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM Fighters WHERE PromotionId IS NULL;");
            // Debug.WriteLine($"[InitialSigning] Signed: {signed} | WithPromotion: {totalWithPromotion} | Free: {totalFree}");
        }

        public async Task<int> RunWeeklyTopUpAsync(CancellationToken cancellationToken = default)
        {
            using var conn = _factory.CreateConnection();

            var promotions = await LoadPromotionsAsync(conn, cancellationToken);
            if (promotions.Count == 0)
                return 0;

            var divisions = await LoadDivisionConfigsAsync(conn, cancellationToken);
            if (divisions.Count == 0)
                return 0;

            var affectedDivisions = new HashSet<(int PromotionId, string WeightClass)>();
            var signed = 0;

            using var tx = conn.BeginTransaction();

            foreach (var division in divisions)
            {
                var promotion = promotions.FirstOrDefault(x => x.Id == division.PromotionId);
                if (promotion is null)
                    continue;

                var currentCount = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM Fighters
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
  AND Retired = 0;",
                    ("$promotionId", division.PromotionId),
                    ("$weightClass", division.WeightClass));

                var targetCount = division.HasRanking == 1
                    ? Math.Max(division.RankingSize + 2, 6)
                    : 4;

                if (currentCount >= targetCount)
                    continue;

                var candidates = await LoadFreeAgentsAsync(conn, tx, division.WeightClass, cancellationToken);
                foreach (var fighter in candidates)
                {
                    if (currentCount >= targetCount)
                        break;

                    if (!MeetsRequirements(fighter, promotion) &&
                        !MeetsRelaxedRequirements(fighter, promotion, currentCount, targetCount))
                    {
                        continue;
                    }

                    var fights = _rng.Next(MinContractFights, MaxContractFights + 1);
                    var salary = ComputeSalary(fighter.Skill, fighter.Popularity, promotion.Prestige, promotion.Budget);

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
UPDATE Fighters
SET PromotionId = $pid,
    Salary = $salary,
    TotalFightsInContract = $fights,
    ContractFightsRemaining = $fights,
    ContractStatus = 'Active',
    NegotiationTurnsRemaining = 0
WHERE Id = $fid;";
                    cmd.Parameters.AddWithValue("$pid", promotion.Id);
                    cmd.Parameters.AddWithValue("$salary", salary);
                    cmd.Parameters.AddWithValue("$fights", fights);
                    cmd.Parameters.AddWithValue("$fid", fighter.Id);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    currentCount++;
                    signed++;
                    affectedDivisions.Add((division.PromotionId, division.WeightClass));
                }
            }

            foreach (var division in affectedDivisions)
            {
                await RefreshDivisionAsync(conn, tx, division.PromotionId, division.WeightClass, cancellationToken);
            }

            tx.Commit();
            return signed;
        }

        private bool MeetsRequirements(FighterRow f, PromotionRow p)
            => f.Skill >= p.MinSkillToSign && f.Popularity >= p.MinPopularityToSign;

        private static bool MeetsRelaxedRequirements(FighterRow f, PromotionRow p, int currentCount, int targetCount)
        {
            if (currentCount >= Math.Max(2, targetCount / 2))
                return false;

            var relaxedSkill = Math.Max(0, p.MinSkillToSign - 12);
            var relaxedPopularity = Math.Max(0, p.MinPopularityToSign - 12);
            return f.Skill >= relaxedSkill && f.Popularity >= relaxedPopularity;
        }

        private double ComputeSignChance(PromotionRow p)
        {
            double pr = p.Prestige / 100.0;
            double bu = p.Budget / 100.0;

            double chance = BaseSignChance
                          + PrestigeInfluence * pr
                          + BudgetInfluence * bu;

            return Math.Max(0.05, Math.Min(0.95, chance));
        }

        private int ComputeSalary(int skill, int popularity, int prestige, int budget)
        {
            double s = skill / 100.0;
            double p = popularity / 100.0;
            double pr = prestige / 100.0;
            double bu = budget / 100.0;

            double score = (0.65 * s) + (0.35 * p);
            double multiplier = 0.6 + 0.4 * pr + 0.3 * bu;

            int salary = (int)Math.Round(MinSalary + (MaxSalary - MinSalary) * score * multiplier);

            salary = (int)Math.Round(salary * (0.85 + _rng.NextDouble() * 0.30));

            return Math.Max(MinSalary, Math.Min(MaxSalary, salary));
        }

        private sealed class PromotionRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int MinSkillToSign { get; set; }
            public int MinPopularityToSign { get; set; }
            public int Prestige { get; set; }
            public int Budget { get; set; }
            public int IsActive { get; set; }
        }

        private sealed class FighterRow
        {
            public int Id { get; set; }
            public int Skill { get; set; }
            public int Popularity { get; set; }
            public string WeightClass { get; set; } = "";
        }

        private static async Task<int> ScalarIntAsync(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var o = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(o);
        }

        private static async Task<int> ScalarIntAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
            var o = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(o);
        }

        private static async Task<List<PromotionRow>> LoadPromotionsAsync(SqliteConnection conn, CancellationToken cancellationToken)
        {
            var promotions = new List<PromotionRow>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, MinSkillToSign, MinPopularityToSign, Prestige, Budget, IsActive
FROM Promotions
WHERE IsActive = 1
ORDER BY Prestige DESC;";
            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                promotions.Add(new PromotionRow
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Name = r["Name"]?.ToString() ?? "",
                    MinSkillToSign = Convert.ToInt32(r["MinSkillToSign"]),
                    MinPopularityToSign = Convert.ToInt32(r["MinPopularityToSign"]),
                    Prestige = Convert.ToInt32(r["Prestige"]),
                    Budget = Convert.ToInt32(r["Budget"]),
                    IsActive = Convert.ToInt32(r["IsActive"])
                });
            }

            return promotions;
        }

        private static async Task<List<DivisionConfig>> LoadDivisionConfigsAsync(SqliteConnection conn, CancellationToken cancellationToken)
        {
            var divisions = new List<DivisionConfig>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT pwc.PromotionId, pwc.WeightClass, pwc.HasRanking, pwc.RankingSize
FROM PromotionWeightClasses pwc
JOIN Promotions p ON p.Id = pwc.PromotionId
WHERE COALESCE(p.IsActive, 1) = 1
ORDER BY pwc.PromotionId, pwc.WeightClass;";
            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                divisions.Add(new DivisionConfig(
                    Convert.ToInt32(r["PromotionId"]),
                    r["WeightClass"]?.ToString() ?? "",
                    Convert.ToInt32(r["HasRanking"]),
                    Convert.ToInt32(r["RankingSize"])));
            }

            return divisions;
        }

        private static async Task<List<FighterRow>> LoadFreeAgentsAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            string weightClass,
            CancellationToken cancellationToken)
        {
            var fighters = new List<FighterRow>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, Skill, Popularity, WeightClass
FROM Fighters
WHERE (PromotionId IS NULL OR ContractStatus = 'FreeAgent')
  AND Retired = 0
  AND WeightClass = $weightClass
ORDER BY (Skill * 0.75 + Popularity * 0.25) DESC, Id;";
            cmd.Parameters.AddWithValue("$weightClass", weightClass);

            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                fighters.Add(new FighterRow
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Skill = Convert.ToInt32(r["Skill"]),
                    Popularity = Convert.ToInt32(r["Popularity"]),
                    WeightClass = r["WeightClass"]?.ToString() ?? ""
                });
            }

            return fighters;
        }

        private static async Task RefreshDivisionAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int promotionId,
            string weightClass,
            CancellationToken cancellationToken)
        {
            var rankingSize = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(RankingSize, 0)
FROM PromotionWeightClasses
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
LIMIT 1;",
                ("$promotionId", promotionId),
                ("$weightClass", weightClass));

            var hasRanking = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(HasRanking, 0)
FROM PromotionWeightClasses
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
LIMIT 1;",
                ("$promotionId", promotionId),
                ("$weightClass", weightClass));

            var fighters = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT Id
FROM Fighters
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
  AND Retired = 0
ORDER BY (Skill * 0.75 + Popularity * 0.25) DESC, Id;";
                cmd.Parameters.AddWithValue("$promotionId", promotionId);
                cmd.Parameters.AddWithValue("$weightClass", weightClass);

                using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                    fighters.Add(Convert.ToInt32(r["Id"]));
            }

            using (var clearChampCmd = conn.CreateCommand())
            {
                clearChampCmd.Transaction = tx;
                clearChampCmd.CommandText = @"
UPDATE Fighters
SET IsChampion = 0
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass;";
                clearChampCmd.Parameters.AddWithValue("$promotionId", promotionId);
                clearChampCmd.Parameters.AddWithValue("$weightClass", weightClass);
                await clearChampCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteRankingsCmd = conn.CreateCommand())
            {
                deleteRankingsCmd.Transaction = tx;
                deleteRankingsCmd.CommandText = @"
DELETE FROM PromotionRankings
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass;";
                deleteRankingsCmd.Parameters.AddWithValue("$promotionId", promotionId);
                deleteRankingsCmd.Parameters.AddWithValue("$weightClass", weightClass);
                await deleteRankingsCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (hasRanking == 1)
            {
                var max = Math.Min(rankingSize, fighters.Count);
                for (var i = 0; i < max; i++)
                {
                    using var insertRankingCmd = conn.CreateCommand();
                    insertRankingCmd.Transaction = tx;
                    insertRankingCmd.CommandText = @"
INSERT INTO PromotionRankings (PromotionId, WeightClass, RankPosition, FighterId)
VALUES ($promotionId, $weightClass, $rankPosition, $fighterId);";
                    insertRankingCmd.Parameters.AddWithValue("$promotionId", promotionId);
                    insertRankingCmd.Parameters.AddWithValue("$weightClass", weightClass);
                    insertRankingCmd.Parameters.AddWithValue("$rankPosition", i + 1);
                    insertRankingCmd.Parameters.AddWithValue("$fighterId", fighters[i]);
                    await insertRankingCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            var currentChampionId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass
LIMIT 1;",
                ("$promotionId", promotionId),
                ("$weightClass", weightClass));

            var championIsStillValid = currentChampionId > 0 && fighters.Contains(currentChampionId);
            var resolvedChampionId = championIsStillValid
                ? currentChampionId
                : (fighters.Count > 0 ? fighters[0] : 0);

            var titleRowExists = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM Titles
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass;",
                ("$promotionId", promotionId),
                ("$weightClass", weightClass)) > 0;

            if (!titleRowExists)
            {
                using var insertTitleCmd = conn.CreateCommand();
                insertTitleCmd.Transaction = tx;
                insertTitleCmd.CommandText = @"
INSERT INTO Titles (PromotionId, WeightClass, ChampionFighterId, InterimChampionFighterId)
VALUES ($promotionId, $weightClass, $championId, NULL);";
                insertTitleCmd.Parameters.AddWithValue("$promotionId", promotionId);
                insertTitleCmd.Parameters.AddWithValue("$weightClass", weightClass);
                insertTitleCmd.Parameters.AddWithValue("$championId", resolvedChampionId > 0 ? resolvedChampionId : (object)DBNull.Value);
                await insertTitleCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                using var updateTitleCmd = conn.CreateCommand();
                updateTitleCmd.Transaction = tx;
                updateTitleCmd.CommandText = @"
UPDATE Titles
SET ChampionFighterId = $championId
WHERE PromotionId = $promotionId
  AND WeightClass = $weightClass;";
                updateTitleCmd.Parameters.AddWithValue("$promotionId", promotionId);
                updateTitleCmd.Parameters.AddWithValue("$weightClass", weightClass);
                updateTitleCmd.Parameters.AddWithValue("$championId", resolvedChampionId > 0 ? resolvedChampionId : (object)DBNull.Value);
                await updateTitleCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (resolvedChampionId > 0)
            {
                using var setChampCmd = conn.CreateCommand();
                setChampCmd.Transaction = tx;
                setChampCmd.CommandText = "UPDATE Fighters SET IsChampion = 1 WHERE Id = $fighterId;";
                setChampCmd.Parameters.AddWithValue("$fighterId", resolvedChampionId);
                await setChampCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private sealed record DivisionConfig(int PromotionId, string WeightClass, int HasRanking, int RankingSize);
    }
}
