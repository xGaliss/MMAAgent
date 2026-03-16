using MMAAgent.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class BuildInitialRankingsSqlite
    {
        private readonly SqliteConnectionFactory _factory;
        private Random _rng = new Random();

        public BuildInitialRankingsSqlite(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public void SetSeed(int seed) => _rng = new Random(seed);

        public async Task RunAsync()
        {
            using var conn = _factory.CreateConnection();

            // Limpia rankings/títulos
            using (var tx = conn.BeginTransaction())
            {
                await ExecAsync(conn, "DELETE FROM PromotionRankings;", tx);
                await ExecAsync(conn, "DELETE FROM Titles;", tx);
                await ExecAsync(conn, "DELETE FROM sqlite_sequence WHERE name='PromotionRankings';", tx);
                await ExecAsync(conn, "DELETE FROM sqlite_sequence WHERE name='Titles';", tx);
                tx.Commit();
            }

            // Leer configs
            var configs = new List<(int PromotionId, string WeightClass, int HasRanking, int RankingSize)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT PromotionId, WeightClass, HasRanking, RankingSize
                                    FROM PromotionWeightClasses;";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    configs.Add((
                        Convert.ToInt32(r["PromotionId"]),
                        r["WeightClass"].ToString() ?? "",
                        Convert.ToInt32(r["HasRanking"]),
                        Convert.ToInt32(r["RankingSize"])
                    ));
                }
            }

            int totalRanked = 0, totalTitles = 0;

            using (var tx = conn.BeginTransaction())
            {
                foreach (var cfg in configs)
                {
                    int take = (cfg.HasRanking == 1 ? Math.Max(1, cfg.RankingSize) : 1);

                    var fighters = new List<int>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            @"SELECT Id
                              FROM Fighters
                              WHERE PromotionId = $p
                                AND WeightClass = $wc
                                AND Retired = 0
                              ORDER BY (Skill*0.75 + Popularity*0.25) DESC
                              LIMIT $take;";
                        cmd.Parameters.AddWithValue("$p", cfg.PromotionId);
                        cmd.Parameters.AddWithValue("$wc", cfg.WeightClass);
                        cmd.Parameters.AddWithValue("$take", take);

                        using var r = await cmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                            fighters.Add(Convert.ToInt32(r["Id"]));
                    }

                    if (fighters.Count == 0) continue;

                    int championId = fighters[0];

                    // Title
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            @"INSERT OR REPLACE INTO Titles
                              (PromotionId, WeightClass, ChampionFighterId, InterimChampionFighterId)
                              VALUES ($p, $wc, $champ, NULL);";
                        cmd.Parameters.AddWithValue("$p", cfg.PromotionId);
                        cmd.Parameters.AddWithValue("$wc", cfg.WeightClass);
                        cmd.Parameters.AddWithValue("$champ", championId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    totalTitles++;

                    // Rankings
                    if (cfg.HasRanking == 1)
                    {
                        int max = Math.Min(cfg.RankingSize, fighters.Count);
                        for (int i = 0; i < max; i++)
                        {
                            using var cmd = conn.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                @"INSERT OR REPLACE INTO PromotionRankings
                                  (PromotionId, WeightClass, RankPosition, FighterId)
                                  VALUES ($p, $wc, $pos, $fid);";
                            cmd.Parameters.AddWithValue("$p", cfg.PromotionId);
                            cmd.Parameters.AddWithValue("$wc", cfg.WeightClass);
                            cmd.Parameters.AddWithValue("$pos", i + 1);
                            cmd.Parameters.AddWithValue("$fid", fighters[i]);
                            await cmd.ExecuteNonQueryAsync();
                            totalRanked++;
                        }
                    }
                }

                tx.Commit();
            }

            System.Diagnostics.Debug.WriteLine($"[Rankings] Ranked: {totalRanked}, Titles: {totalTitles}");
        }

        private static async Task ExecAsync(SqliteConnection conn, string sql, SqliteTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}