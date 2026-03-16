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

        private bool MeetsRequirements(FighterRow f, PromotionRow p)
            => f.Skill >= p.MinSkillToSign && f.Popularity >= p.MinPopularityToSign;

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
        }

        private static async Task<int> ScalarIntAsync(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var o = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(o);
        }
    }
}