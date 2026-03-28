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
            var eventDate = state.CurrentDate;
            var seed = DeriveEventSeed(state.WorldSeed, state.CurrentYear, state.CurrentWeek, promotionId);
            var rng = new Random(seed);

            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            var promoName = await ScalarStringAsync(conn, tx,
                "SELECT Name FROM Promotions WHERE Id = $id;",
                ("$id", promotionId)) ?? $"Promotion {promotionId}";

            var eventName = $"{promoName} Week {state.CurrentWeek}";
            var location = "TBD";

            int eventId = await EnsureWeeklyEventAsync(conn, tx, promotionId, eventDate, eventName, location);

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

            var sb = new StringBuilder();
            sb.AppendLine($"===== SIM EVENT: {promoName} (EventId={eventId}, Date={eventDate}, seed={seed}) =====");

            var scheduledFights = await QueryAsync(conn, tx, @"
SELECT Id, FighterAId, FighterBId, WeightClass, IsTitleFight
FROM Fights
WHERE EventId = $eventId
  AND Method = 'Scheduled';",
                ("$eventId", eventId));

            var fightersUsedThisEvent = new HashSet<int>();

            foreach (var sf in scheduledFights)
            {
                int fightId = sf.GetInt("Id");
                int aId = sf.GetInt("FighterAId");
                int bId = sf.GetInt("FighterBId");
                string wc = sf.GetString("WeightClass");
                bool isTitle = sf.GetInt("IsTitleFight") == 1;

                if (fightersUsedThisEvent.Contains(aId) || fightersUsedThisEvent.Contains(bId))
                    continue;

                var res = await SimFightAsync(conn, tx, rng, eventId, promotionId, wc, aId, bId, isTitle, eventDate, state.CurrentWeek);

                await UpdateScheduledFightAsync(conn, tx, fightId, res.WinnerId, res.Method, null, eventDate);

                sb.AppendLine($"[SCHEDULED {wc}] {res.Summary}");

                fightersUsedThisEvent.Add(aId);
                fightersUsedThisEvent.Add(bId);
            }

            foreach (var d in divs)
            {
                var wc = d.GetString("WeightClass");
                var hasRanking = d.GetInt("HasRanking");
                var rankingSize = d.GetInt("RankingSize");

                if (hasRanking != 1 || rankingSize <= 0)
                    continue;

                var ranked = await QueryAsync(conn, tx, @"
SELECT RankPosition, FighterId
FROM PromotionRankings
WHERE PromotionId = $p
  AND WeightClass = $wc
  AND FighterId NOT IN (
      SELECT FighterId
      FROM ManagedFighters
      WHERE IsActive = 1
  )
ORDER BY RankPosition;",
                    ("$p", promotionId),
                    ("$wc", wc));

                if (ranked.Count < 2)
                {
                    sb.AppendLine($"[{wc}] (skip) ranking insuficiente: {ranked.Count}");
                    continue;
                }

                int champId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                    ("$p", promotionId),
                    ("$wc", wc));

                bool champIsManaged = false;
                if (champId > 0)
                {
                    champIsManaged = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM ManagedFighters
WHERE FighterId = $fid
  AND IsActive = 1;",
                        ("$fid", champId)) > 0;
                }

                bool doTitle = champId > 0 && !champIsManaged;

                if (doTitle)
                {
                    int rank1Id = ranked[0].GetInt("FighterId");

                    if (rank1Id > 0 &&
                        champId != rank1Id &&
                        !fightersUsedThisEvent.Contains(champId) &&
                        !fightersUsedThisEvent.Contains(rank1Id))
                    {
                        var res = await SimFightAsync(conn, tx, rng, eventId, promotionId, wc, champId, rank1Id, true, eventDate, state.CurrentWeek);

                        sb.AppendLine($"[{wc}] TITLE: {res.Summary}");
                        fightersUsedThisEvent.Add(champId);
                        fightersUsedThisEvent.Add(rank1Id);
                    }
                }

                int made = 0;
                int startIndex = 1;

                while (made < FightsPerWeightClass && startIndex + 1 < ranked.Count)
                {
                    int aId = ranked[startIndex].GetInt("FighterId");
                    int bId = ranked[startIndex + 1].GetInt("FighterId");
                    startIndex += 2;

                    if (aId <= 0 || bId <= 0 || aId == bId) continue;
                    if (fightersUsedThisEvent.Contains(aId) || fightersUsedThisEvent.Contains(bId)) continue;

                    bool isTitleFight = champId > 0 && (aId == champId || bId == champId);

                    var res = await SimFightAsync(conn, tx, rng, eventId, promotionId, wc, aId, bId, isTitleFight, eventDate, state.CurrentWeek);

                    sb.AppendLine($"[{wc}] {res.Summary}" + (isTitleFight ? " (TITLE AUTO)" : ""));

                    fightersUsedThisEvent.Add(aId);
                    fightersUsedThisEvent.Add(bId);
                    made++;
                }

                await RebuildDivisionRankingsAndEnsureTitleAsync(conn, tx, promotionId, wc, rankingSize);
            }

            sb.AppendLine("===== END EVENT =====");
            System.Diagnostics.Debug.WriteLine(sb.ToString());

            tx.Commit();
        }

        private async Task<SimFightResult> SimFightAsync(
            SqliteConnection conn, SqliteTransaction tx, Random rng,
            int eventId, int promotionId, string weightClass,
            int aId, int bId, bool isTitle, string eventDate, int currentWeek)
        {
            var a = await GetFighterAsync(conn, tx, aId);
            var b = await GetFighterAsync(conn, tx, bId);

            if (a is null || b is null)
                return new SimFightResult(0, 0, "ERROR", isTitle, "ERROR: fighter missing");

            if (a.Retired != 0 || b.Retired != 0)
                return new SimFightResult(0, 0, "SKIP", isTitle, "SKIP: retired fighter");

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

            await ApplyRecoveryAsync(conn, tx, aId, bId, method, currentWeek, rng);

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

            var summary = $"{wName} def. {lName} via {method}" + (isTitle ? " (TITLE)" : "");
            return new SimFightResult(winnerId, loserId, method, isTitle, summary);
        }

        private static async Task ApplyRecoveryAsync(SqliteConnection conn, SqliteTransaction tx, int aId, int bId, string method, int currentWeek, Random rng)
        {
            var baseWeeks = method switch
            {
                "DEC" => 4,
                "SUB" => 5,
                "KO/TKO" => 8,
                _ => 4
            };

            var injuryWeeks = 0;
            if (method == "KO/TKO" && rng.NextDouble() < 0.55)
                injuryWeeks = rng.Next(6, 11);
            else if (method == "SUB" && rng.NextDouble() < 0.20)
                injuryWeeks = rng.Next(3, 6);
            else if (method == "DEC" && rng.NextDouble() < 0.08)
                injuryWeeks = rng.Next(2, 4);

            await ExecAsync(conn, tx, @"
UPDATE Fighters
SET
    IsBooked = 0,
    WeeksUntilAvailable = $weeks,
    InjuryWeeksRemaining = CASE WHEN $injuryWeeks > InjuryWeeksRemaining THEN $injuryWeeks ELSE InjuryWeeksRemaining END,
    IsInjured = CASE WHEN $injuryWeeks > 0 THEN 1 ELSE 0 END
WHERE Id IN ($a, $b);",
                ("$weeks", baseWeeks),
                ("$injuryWeeks", injuryWeeks),
                ("$a", aId),
                ("$b", bId));
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

        private static async Task UpdateScheduledFightAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int fightId,
            int winnerId,
            string method,
            int? round,
            string eventDate)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Fights
SET WinnerId = $winnerId,
    Method = $method,
    Round = $round,
    EventDate = $eventDate
WHERE Id = $fightId;";
            cmd.Parameters.AddWithValue("$winnerId", winnerId);
            cmd.Parameters.AddWithValue("$method", method);
            cmd.Parameters.AddWithValue("$round", round.HasValue ? round.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$eventDate", eventDate);
            cmd.Parameters.AddWithValue("$fightId", fightId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<int> EnsureWeeklyEventAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int promotionId,
            string eventDate,
            string eventName,
            string location)
        {
            using var findCmd = conn.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = @"
SELECT Id
FROM Events
WHERE PromotionId = $p
  AND Name = $n
LIMIT 1;";
            findCmd.Parameters.AddWithValue("$p", promotionId);
            findCmd.Parameters.AddWithValue("$n", eventName);

            var existing = await findCmd.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt32(existing);

            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
INSERT INTO Events (PromotionId, EventDate, Name, Location)
VALUES ($p, $d, $n, $l);";
            insertCmd.Parameters.AddWithValue("$p", promotionId);
            insertCmd.Parameters.AddWithValue("$d", eventDate);
            insertCmd.Parameters.AddWithValue("$n", eventName);
            insertCmd.Parameters.AddWithValue("$l", location);
            await insertCmd.ExecuteNonQueryAsync();

            insertCmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        }

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

        private sealed record SimFightResult(int WinnerId, int LoserId, string Method, bool IsTitle, string Summary);
    }
}
