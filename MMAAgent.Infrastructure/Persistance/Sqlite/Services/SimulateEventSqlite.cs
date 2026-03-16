using Microsoft.Data.Sqlite;
using MMAAgent.Application.Simulation;
using MMAAgent.Domain.Common;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using System.Text;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class SimulateEventSqlite : IEventSimulator
    {
        private readonly SqliteConnectionFactory _factory;
        private readonly IContractServiceSqlite _contracts;


        public int FightsPerWeightClass { get; set; } = 2;
        public double TitleFightChance { get; set; } = 0.20;
        public double Randomness { get; set; } = 0.18;



        public SimulateEventSqlite(SqliteConnectionFactory factory, IContractServiceSqlite contracts)
        {
            _factory = factory;
            _contracts = contracts;
        }

        public async Task SimulatePromotionEventAsync(int promotionId, GameState state)
        {
            // Fecha del juego
            var eventDate = state.CurrentDate; // "yyyy-MM-dd"
            // Seed determinístico por promo+semana
            var seed = DeriveEventSeed(state.WorldSeed, state.CurrentYear, state.CurrentWeek, promotionId);
            var rng = new Random(seed);

            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            // 1) Nombre de promotora
            var promoName = await ScalarStringAsync(conn, tx,
                "SELECT Name FROM Promotions WHERE Id = $id;",
                ("$id", promotionId)) ?? $"Promotion {promotionId}";

            // 2) Crear evento
            var eventName = $"{promoName} Week {state.CurrentWeek}";
            var location = "TBD";

            await ExecAsync(conn, tx, @"
INSERT INTO Events (PromotionId, EventDate, Name, Location)
VALUES ($p, $d, $n, $l);",
                ("$p", promotionId),
                ("$d", eventDate),
                ("$n", eventName),
                ("$l", location));

            var eventId = await ScalarIntAsync(conn, tx, "SELECT last_insert_rowid();");

            // 3) Divisiones de esa promotora
            var divs = await QueryAsync(conn, tx, @"
SELECT WeightClass, HasRanking, RankingSize
FROM PromotionWeightClasses
WHERE PromotionId = $p;",
                ("$p", promotionId));

            if (divs.Count == 0)
            {
                tx.Commit();
                return;
            }

            var fightersUsedThisEvent = new HashSet<int>();
            var sb = new StringBuilder();
            sb.AppendLine($"===== SIM EVENT: {promoName} (EventId={eventId}, Date={eventDate}, seed={seed}) =====");

            foreach (var d in divs)
            {
                var wc = d.GetString("WeightClass");
                var hasRanking = d.GetInt("HasRanking");
                var rankingSize = d.GetInt("RankingSize");

                if (hasRanking != 1 || rankingSize <= 0)
                    continue;

                // Ranking actual
                var ranked = await QueryAsync(conn, tx, @"
SELECT RankPosition, FighterId
FROM PromotionRankings
WHERE PromotionId = $p AND WeightClass = $wc
ORDER BY RankPosition;",
                    ("$p", promotionId),
                    ("$wc", wc));

                if (ranked.Count < 2)
                {
                    sb.AppendLine($"[{wc}] (skip) ranking insuficiente: {ranked.Count}");
                    continue;
                }

                bool doTitle = rng.NextDouble() < TitleFightChance;

                // --- TITLE: Champ vs #1 ---
                if (doTitle)
                {
                    int champId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                        ("$p", promotionId),
                        ("$wc", wc));

                    int rank1Id = ranked[0].GetInt("FighterId");

                    if (champId > 0 && rank1Id > 0 && champId != rank1Id &&
                        !fightersUsedThisEvent.Contains(champId) &&
                        !fightersUsedThisEvent.Contains(rank1Id))
                    {
                        var res = await SimFightAsync(conn, tx, rng, eventId, promotionId, wc,
                            champId, rank1Id, isTitle: true, eventDate);

                        sb.AppendLine($"[{wc}] TITLE: {res}");
                        fightersUsedThisEvent.Add(champId);
                        fightersUsedThisEvent.Add(rank1Id);
                    }
                }

                // --- PAREJAS: #2 vs #3, #4 vs #5...
                int made = 0;
                int startIndex = 1; // 0 = rank #1

                while (made < FightsPerWeightClass && startIndex + 1 < ranked.Count)
                {
                    int aId = ranked[startIndex].GetInt("FighterId");
                    int bId = ranked[startIndex + 1].GetInt("FighterId");
                    startIndex += 2;

                    if (aId <= 0 || bId <= 0 || aId == bId) continue;
                    if (fightersUsedThisEvent.Contains(aId) || fightersUsedThisEvent.Contains(bId)) continue;

                    var res = await SimFightAsync(conn, tx, rng, eventId, promotionId, wc,
                        aId, bId, isTitle: false, eventDate);

                    sb.AppendLine($"[{wc}] {res}");

                    fightersUsedThisEvent.Add(aId);
                    fightersUsedThisEvent.Add(bId);
                    made++;
                }

                // rebuild ranking + aseguramos title
                await RebuildDivisionRankingsAndEnsureTitleAsync(conn, tx, promotionId, wc, rankingSize);
            }

            sb.AppendLine("===== END EVENT =====");
            System.Diagnostics.Debug.WriteLine(sb.ToString());

            tx.Commit();
        }

        // ---------------- Fight sim ----------------

        private async Task<string> SimFightAsync(
            SqliteConnection conn, SqliteTransaction tx, Random rng,
            int eventId, int promotionId, string weightClass,
            int aId, int bId, bool isTitle, string eventDate)
        {
            var a = await GetFighterAsync(conn, tx, aId);
            var b = await GetFighterAsync(conn, tx, bId);

            if (a is null || b is null) return "ERROR: fighter missing";
            if (a.Retired != 0 || b.Retired != 0) return "SKIP: retired fighter";

            double aPower = CombatPower(a, rng);
            double bPower = CombatPower(b, rng);

            double diff = aPower - bPower;
            double pA = 1.0 / (1.0 + Math.Exp(-diff / 12.0));
            pA = Clamp01(pA * (1.0 - Randomness) + rng.NextDouble() * Randomness);

            bool aWins = rng.NextDouble() < pA;
            int winnerId = aWins ? aId : bId;
            int loserId = aWins ? bId : aId;

            var winner = aWins ? a : b;
            var loser = aWins ? b : a;

            string method = DecideMethod(winner, loser, rng);

            await ApplyResultAsync(conn, tx, rng, winnerId, loserId, method, isTitle);

            await InsertFightHistoryAsync(conn, tx,
                eventDate, promotionId, weightClass,
                aId, bId, winnerId, loserId, method, isTitle,
                eventId, notes: null);

            await _contracts.PostFightContractTickAsync(conn, tx, winnerId);
            await _contracts.PostFightContractTickAsync(conn, tx, loserId);

            // TODO: contracts (cuando portes tu ContractManager a Sqlite/Service)
            // _contracts.PostFightTick(winnerId);
            // _contracts.PostFightTick(loserId);

            // Si title fight y el campeón perdió => cambia campeón
            if (isTitle)
            {
                int champId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                    ("$p", promotionId), ("$wc", weightClass));

                if (champId == loserId)
                {
                    await ExecAsync(conn, tx, @"
UPDATE Titles
SET ChampionFighterId = $w
WHERE PromotionId = $p AND WeightClass = $wc;",
                        ("$w", winnerId),
                        ("$p", promotionId),
                        ("$wc", weightClass));
                }
            }

            string aName = $"{a.FirstName} {a.LastName}";
            string bName = $"{b.FirstName} {b.LastName}";
            string wName = aWins ? aName : bName;
            string lName = aWins ? bName : aName;

            return $"{wName} def. {lName} via {method}" + (isTitle ? " (TITLE)" : "");
        }

        private static double CombatPower(FighterRow f, Random rng)
        {
            double core = 0.35 * f.Skill
                        + 0.14 * f.Striking
                        + 0.14 * f.Wrestling
                        + 0.14 * f.Grappling
                        + 0.10 * f.Cardio
                        + 0.07 * f.Chin
                        + 0.06 * f.FightIQ;

            core += NextGaussian(rng, 0, 4.5);
            return core;
        }

        private static string DecideMethod(FighterRow winner, FighterRow loser, Random rng)
        {
            double koP = 0.33;
            double subP = 0.22;

            koP += Clamp01((winner.Striking - loser.Chin) / 60.0) * 0.25;

            double groundEdge = (winner.Grappling + winner.Wrestling) - (loser.Grappling + loser.Wrestling);
            subP += Clamp01(groundEdge / 80.0) * 0.25;

            koP = Clamp(koP, 0.15, 0.70);
            subP = Clamp(subP, 0.10, 0.60);

            double r = rng.NextDouble();
            if (r < koP) return "KO/TKO";
            if (r < koP + subP) return "SUB";
            return "DEC";
        }

        private static async Task ApplyResultAsync(SqliteConnection conn, SqliteTransaction tx, Random rng,
            int winnerId, int loserId, string method, bool isTitle)
        {
            if (method == "KO/TKO")
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, KOWins = KOWins + 1 WHERE Id = $id;", ("$id", winnerId));
            else if (method == "SUB")
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, SubWins = SubWins + 1 WHERE Id = $id;", ("$id", winnerId));
            else
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, DecWins = DecWins + 1 WHERE Id = $id;", ("$id", winnerId));

            await ExecAsync(conn, tx, "UPDATE Fighters SET Losses = Losses + 1 WHERE Id = $id;", ("$id", loserId));

            int wDelta = isTitle ? rng.Next(3, 8) : rng.Next(1, 5);
            int lDelta = isTitle ? rng.Next(2, 6) : rng.Next(1, 4);

            await ExecAsync(conn, tx, "UPDATE Fighters SET Popularity = MIN(100, Popularity + $d) WHERE Id = $id;", ("$d", wDelta), ("$id", winnerId));
            await ExecAsync(conn, tx, "UPDATE Fighters SET Popularity = MAX(0, Popularity - $d) WHERE Id = $id;", ("$d", lDelta), ("$id", loserId));
        }

        // ---------------- Rankings rebuild ----------------

        private static async Task RebuildDivisionRankingsAndEnsureTitleAsync(
            SqliteConnection conn, SqliteTransaction tx,
            int promotionId, string weightClass, int rankingSize)
        {
            await ExecAsync(conn, tx,
                "DELETE FROM PromotionRankings WHERE PromotionId = $p AND WeightClass = $wc;",
                ("$p", promotionId), ("$wc", weightClass));

            var top = await QueryAsync(conn, tx, @"
SELECT Id
FROM Fighters
WHERE PromotionId = $p AND WeightClass = $wc AND Retired = 0
ORDER BY (Skill*0.75 + Popularity*0.25) DESC
LIMIT $take;",
                ("$p", promotionId), ("$wc", weightClass), ("$take", rankingSize));

            for (int i = 0; i < top.Count; i++)
            {
                int fid = top[i].GetInt("Id");
                await ExecAsync(conn, tx, @"
INSERT INTO PromotionRankings (PromotionId, WeightClass, RankPosition, FighterId)
VALUES ($p, $wc, $pos, $fid);",
                    ("$p", promotionId), ("$wc", weightClass), ("$pos", i + 1), ("$fid", fid));
            }

            // asegurar fila de título (si no existe, crear)
            int titleRowCount = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc;",
                ("$p", promotionId), ("$wc", weightClass));

            if (titleRowCount == 0)
            {
                await ExecAsync(conn, tx, @"
INSERT INTO Titles (PromotionId, WeightClass, ChampionFighterId, InterimChampionFighterId)
VALUES ($p, $wc, NULL, NULL);",
                    ("$p", promotionId), ("$wc", weightClass));
            }

            int champ = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                ("$p", promotionId), ("$wc", weightClass));

            if (champ <= 0 && top.Count > 0)
            {
                await ExecAsync(conn, tx, @"
UPDATE Titles
SET ChampionFighterId = $c
WHERE PromotionId = $p AND WeightClass = $wc;",
                    ("$c", top[0].GetInt("Id")), ("$p", promotionId), ("$wc", weightClass));
            }
        }

        // ---------------- FightHistory ----------------

        private static Task InsertFightHistoryAsync(
            SqliteConnection conn, SqliteTransaction tx,
            string date, int promotionId, string weightClass,
            int fighterAId, int fighterBId,
            int winnerId, int loserId,
            string method, bool isTitle,
            int eventId, string? notes = null)
        {
            return ExecAsync(conn, tx, @"
INSERT INTO FightHistory
(FightDate, PromotionId, WeightClass, FighterAId, FighterBId, WinnerId, LoserId, Method, IsTitle, Notes, EventId)
VALUES
($d, $pid, $wc, $a, $b, $w, $l, $m, $t, $n, $eid);",
                ("$d", date),
                ("$pid", promotionId),
                ("$wc", weightClass),
                ("$a", fighterAId),
                ("$b", fighterBId),
                ("$w", winnerId),
                ("$l", loserId),
                ("$m", method),
                ("$t", isTitle ? 1 : 0),
                ("$n", notes ?? (object)DBNull.Value),
                ("$eid", eventId)
            );
        }

        // ---------------- Fighter read ----------------

        private static async Task<FighterRow?> GetFighterAsync(SqliteConnection conn, SqliteTransaction tx, int id)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, FirstName, LastName, Retired,
       Skill, Popularity,
       Striking, Grappling, Wrestling, Cardio, Chin, FightIQ
FROM Fighters
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            return new FighterRow
            {
                Id = Convert.ToInt32(r["Id"]),
                FirstName = r["FirstName"]?.ToString() ?? "",
                LastName = r["LastName"]?.ToString() ?? "",
                Retired = Convert.ToInt32(r["Retired"]),
                Skill = Convert.ToInt32(r["Skill"]),
                Popularity = Convert.ToInt32(r["Popularity"]),
                Striking = Convert.ToInt32(r["Striking"]),
                Grappling = Convert.ToInt32(r["Grappling"]),
                Wrestling = Convert.ToInt32(r["Wrestling"]),
                Cardio = Convert.ToInt32(r["Cardio"]),
                Chin = Convert.ToInt32(r["Chin"]),
                FightIQ = Convert.ToInt32(r["FightIQ"]),
            };
        }

        // ---------------- SQL helpers ----------------

        private sealed class Row
        {
            private readonly Dictionary<string, object?> _d;
            public Row(Dictionary<string, object?> d) { _d = d; }
            public string GetString(string k) => _d[k]?.ToString() ?? "";
            public int GetInt(string k) => Convert.ToInt32(_d[k]);
        }

        private static async Task<List<Row>> QueryAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);

            var list = new List<Row>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < r.FieldCount; i++)
                    d[r.GetName(i)] = r.GetValue(i);
                list.Add(new Row(d));
            }
            return list;
        }

        private static async Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<int> ScalarIntAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
            var o = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(o);
        }

        private static async Task<string?> ScalarStringAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
            var o = await cmd.ExecuteScalarAsync();
            return o?.ToString();
        }

        private static int DeriveEventSeed(int worldSeed, int year, int week, int promoId)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + worldSeed;
                h = h * 31 + year;
                h = h * 31 + week;
                h = h * 31 + promoId;
                if (h == 0) h = 1;
                return Math.Abs(h);
            }
        }

        private static double NextGaussian(Random rng, double mean, double stdDev)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        private static double Clamp(double v, double min, double max) => Math.Min(max, Math.Max(min, v));

        private sealed class FighterRow
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public int Retired { get; set; }

            public int Skill { get; set; }
            public int Popularity { get; set; }

            public int Striking { get; set; }
            public int Grappling { get; set; }
            public int Wrestling { get; set; }
            public int Cardio { get; set; }
            public int Chin { get; set; }
            public int FightIQ { get; set; }
        }
    }
}