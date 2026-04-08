using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using System;
using System.Collections.Generic;

namespace MMAAgent.Infrastructure.Generation
{
    public sealed class WorldFighterGeneratorSqlite
    {
        private readonly SqliteConnectionFactory _factory;
        private Random _rng = new Random();

        public void SetSeed(int seed)
        {
            // seed 0 también es válido; si quieres evitar 0, lo hacemos en NewGameService
            _rng = new Random(seed);
        }

        public int GenerateCount { get; set; } = 300;
        public int AnnualNewcomerCount { get; set; } = 48;
        public bool ClearExistingFighters { get; set; } = true;

        // --- Caches ---
        private List<CountryRow> _countries = new();
        private int _totalCountryWeight = 0;

        private readonly Dictionary<int, List<NameWeight>> _firstNamesByCountry = new();
        private readonly Dictionary<int, List<NameWeight>> _lastNamesByCountry = new();
        private readonly Dictionary<string, List<NameWeight>> _firstNamesByGroup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NameWeight>> _lastNamesByGroup = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _usedFullNames = new();

        private readonly (string wc, int w)[] _weightClasses = new[]
        {
            ("Flyweight", 6),
            ("Bantamweight", 10),
            ("Featherweight", 12),
            ("Lightweight", 16),
            ("Welterweight", 16),
            ("Middleweight", 12),
            ("LightHeavyweight", 8),
            ("Heavyweight", 6),
        };

        public WorldFighterGeneratorSqlite(SqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public void GenerateWorld()
        {
            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            LoadCountries(conn, tx);
            LoadNameCaches(conn, tx);

            if (_countries.Count == 0 || _totalCountryWeight <= 0)
                throw new InvalidOperationException("No hay países válidos (FighterSpawnWeight > 0).");

            if (ClearExistingFighters)
            {
                ExecuteNonQuery(conn, tx, "DELETE FROM Fighters;");
                ExecuteNonQuery(conn, tx, "DELETE FROM sqlite_sequence WHERE name='Fighters';");
            }

            _usedFullNames.Clear();

            for (int i = 0; i < GenerateCount; i++)
            {
                var c = PickCountry();
                var (first, last) = GenerateUniqueName(c);

                int age = GenerateAge();
                string wc = PickWeightClass();

                int primeStart = _rng.Next(26, 29);
                int primeEnd = primeStart + _rng.Next(5, 8);

                int potential = ClampInt((int)(c.MmaLevel + NextGaussian(0, 12)), 20, 99);
                int skill = GenerateSkillFromAgePotential(age, potential);
                string style = PickStyle(c);

                var stats = GenerateStats(skill, potential, c, style);
                var record = GenerateRecord(age, skill);

                InsertFighter(
                    conn, tx,
                    first, last, c.Id,
                    age, wc,
                    primeStart, primeEnd,
                    potential, skill,
                    stats.Striking, stats.Grappling, stats.Wrestling, stats.Cardio, stats.Chin, stats.FightIQ,
                    style,
                    record.W, record.L, record.D,
                    record.KO, record.SUB, record.DEC,
                    popularity: GeneratePopularity(skill, record.W, record.L),
                    retired: 0,
                    promotionId: null,
                    salary: 0,
                    contractFightsRemaining: 0,
                    totalFightsInContract: 0,
                    contractStatus: "FreeAgent",
                    negotiationTurnsRemaining: 0
                );
            }

            tx.Commit();
        }

        public int GenerateAnnualNewcomers(int? count = null)
        {
            var totalToGenerate = Math.Max(0, count ?? AnnualNewcomerCount);
            if (totalToGenerate <= 0)
                return 0;

            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            LoadCountries(conn, tx);
            LoadNameCaches(conn, tx);
            LoadUsedFullNames(conn, tx);

            if (_countries.Count == 0 || _totalCountryWeight <= 0)
                throw new InvalidOperationException("No hay países válidos (FighterSpawnWeight > 0).");

            var generated = 0;

            for (int i = 0; i < totalToGenerate; i++)
            {
                var c = PickCountry();
                var (first, last) = GenerateUniqueName(c);
                var age = GenerateNewcomerAge();
                var wc = PickWeightClass();

                int primeStart = _rng.Next(26, 29);
                int primeEnd = primeStart + _rng.Next(5, 8);

                int potential = GenerateNewcomerPotential(c);
                int skill = GenerateNewcomerSkill(age, potential);
                string style = PickStyle(c);

                var stats = GenerateStats(skill, potential, c, style);
                var record = GenerateNewcomerRecord(age, skill);

                InsertFighter(
                    conn, tx,
                    first, last, c.Id,
                    age, wc,
                    primeStart, primeEnd,
                    potential, skill,
                    stats.Striking, stats.Grappling, stats.Wrestling, stats.Cardio, stats.Chin, stats.FightIQ,
                    style,
                    record.W, record.L, record.D,
                    record.KO, record.SUB, record.DEC,
                    popularity: GenerateNewcomerPopularity(skill, record.W, record.L),
                    retired: 0,
                    promotionId: null,
                    salary: 0,
                    contractFightsRemaining: 0,
                    totalFightsInContract: 0,
                    contractStatus: "FreeAgent",
                    negotiationTurnsRemaining: 0
                );

                generated++;
            }

            tx.Commit();
            return generated;
        }

        // ---------------- Loaders ----------------

        private void LoadCountries(SqliteConnection conn, SqliteTransaction tx)
        {
            _countries = new List<CountryRow>();
            _totalCountryWeight = 0;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, Name, FighterSpawnWeight, CulturalGroup, MmaLevel, WrestlingBias, StrikingBias, GrapplingBias
FROM Countries
WHERE FighterSpawnWeight > 0;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var c = new CountryRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"]?.ToString() ?? "",
                    FighterSpawnWeight = Convert.ToInt32(reader["FighterSpawnWeight"]),
                    CulturalGroup = reader["CulturalGroup"]?.ToString() ?? "",
                    MmaLevel = Convert.ToInt32(reader["MmaLevel"]),
                    WrestlingBias = Convert.ToInt32(reader["WrestlingBias"]),
                    StrikingBias = Convert.ToInt32(reader["StrikingBias"]),
                    GrapplingBias = Convert.ToInt32(reader["GrapplingBias"]),
                };
                _countries.Add(c);
                _totalCountryWeight += Math.Max(0, c.FighterSpawnWeight);
            }
        }

        private void LoadNameCaches(SqliteConnection conn, SqliteTransaction tx)
        {
            _firstNamesByCountry.Clear();
            _lastNamesByCountry.Clear();
            _firstNamesByGroup.Clear();
            _lastNamesByGroup.Clear();

            // FirstNameCountries
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT fnc.CountryId AS CountryId, fn.Name AS Name, fnc.Weight AS Weight
FROM FirstNameCountries fnc
JOIN FirstNames fn ON fn.Id = fnc.FirstNameId
WHERE fnc.Weight > 0;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int countryId = Convert.ToInt32(r["CountryId"]);
                    var row = new NameWeight
                    {
                        Name = r["Name"]?.ToString() ?? "Unknown",
                        Weight = Convert.ToInt32(r["Weight"])
                    };
                    if (!_firstNamesByCountry.TryGetValue(countryId, out var list))
                        _firstNamesByCountry[countryId] = list = new List<NameWeight>();
                    list.Add(row);
                }
            }

            // LastNameCountries
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT lnc.CountryId AS CountryId, ln.Name AS Name, lnc.Weight AS Weight
FROM LastNameCountries lnc
JOIN LastNames ln ON ln.Id = lnc.LastNameId
WHERE lnc.Weight > 0;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int countryId = Convert.ToInt32(r["CountryId"]);
                    var row = new NameWeight
                    {
                        Name = r["Name"]?.ToString() ?? "Unknown",
                        Weight = Convert.ToInt32(r["Weight"])
                    };
                    if (!_lastNamesByCountry.TryGetValue(countryId, out var list))
                        _lastNamesByCountry[countryId] = list = new List<NameWeight>();
                    list.Add(row);
                }
            }

            // FirstNameGroups
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT fng.CulturalGroup AS CulturalGroup, fn.Name AS Name, fng.Weight AS Weight
FROM FirstNameGroups fng
JOIN FirstNames fn ON fn.Id = fng.FirstNameId
WHERE fng.Weight > 0;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string group = r["CulturalGroup"]?.ToString() ?? "";
                    var row = new NameWeight
                    {
                        Name = r["Name"]?.ToString() ?? "Unknown",
                        Weight = Convert.ToInt32(r["Weight"])
                    };
                    if (!_firstNamesByGroup.TryGetValue(group, out var list))
                        _firstNamesByGroup[group] = list = new List<NameWeight>();
                    list.Add(row);
                }
            }

            // LastNameGroups
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT lng.CulturalGroup AS CulturalGroup, ln.Name AS Name, lng.Weight AS Weight
FROM LastNameGroups lng
JOIN LastNames ln ON ln.Id = lng.LastNameId
WHERE lng.Weight > 0;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string group = r["CulturalGroup"]?.ToString() ?? "";
                    var row = new NameWeight
                    {
                        Name = r["Name"]?.ToString() ?? "Unknown",
                        Weight = Convert.ToInt32(r["Weight"])
                    };
                    if (!_lastNamesByGroup.TryGetValue(group, out var list))
                        _lastNamesByGroup[group] = list = new List<NameWeight>();
                    list.Add(row);
                }
            }
        }

        private void LoadUsedFullNames(SqliteConnection conn, SqliteTransaction tx)
        {
            _usedFullNames.Clear();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT f.FirstName, f.LastName, COALESCE(c.Name, '') AS CountryName
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var first = reader["FirstName"]?.ToString() ?? "Unknown";
                var last = reader["LastName"]?.ToString() ?? "Unknown";
                var countryName = reader["CountryName"]?.ToString() ?? "";
                _usedFullNames.Add(BuildNameKey(first, last, countryName));
            }
        }

        // ---------------- Picks ----------------

        private CountryRow PickCountry()
        {
            int roll = _rng.Next(_totalCountryWeight);
            int cum = 0;
            foreach (var c in _countries)
            {
                cum += Math.Max(0, c.FighterSpawnWeight);
                if (roll < cum) return c;
            }
            return _countries[0];
        }

        private string PickFirstName(int countryId, string group)
        {
            if (_firstNamesByCountry.TryGetValue(countryId, out var list) && list.Count > 0)
                return WeightedPick(list);

            if (!string.IsNullOrWhiteSpace(group) &&
                _firstNamesByGroup.TryGetValue(group, out var glist) && glist.Count > 0)
                return WeightedPick(glist);

            foreach (var kv in _firstNamesByGroup)
                if (kv.Value.Count > 0) return WeightedPick(kv.Value);

            return "Unknown";
        }

        private string PickLastName(int countryId, string group)
        {
            if (_lastNamesByCountry.TryGetValue(countryId, out var list) && list.Count > 0)
                return WeightedPick(list);

            if (!string.IsNullOrWhiteSpace(group) &&
                _lastNamesByGroup.TryGetValue(group, out var glist) && glist.Count > 0)
                return WeightedPick(glist);

            foreach (var kv in _lastNamesByGroup)
                if (kv.Value.Count > 0) return WeightedPick(kv.Value);

            return "Unknown";
        }

        private string PickWeightClass()
        {
            int total = 0;
            for (int i = 0; i < _weightClasses.Length; i++) total += _weightClasses[i].w;
            int roll = _rng.Next(total);
            int cum = 0;
            for (int i = 0; i < _weightClasses.Length; i++)
            {
                cum += _weightClasses[i].w;
                if (roll < cum) return _weightClasses[i].wc;
            }
            return "Lightweight";
        }

        private string PickStyle(CountryRow c)
        {
            int striker = 30 + c.StrikingBias;
            int wrestler = 30 + c.WrestlingBias;
            int grappler = 30 + c.GrapplingBias;
            int all = 25;

            striker = Math.Max(5, striker);
            wrestler = Math.Max(5, wrestler);
            grappler = Math.Max(5, grappler);
            all = Math.Max(5, all);

            int total = striker + wrestler + grappler + all;
            int roll = _rng.Next(total);
            if (roll < striker) return "Striker";
            roll -= striker;
            if (roll < wrestler) return "Wrestler";
            roll -= wrestler;
            if (roll < grappler) return "Grappler";
            return "AllRounder";
        }

        private (string First, string Last) GenerateUniqueName(CountryRow c)
        {
            string first = PickFirstName(c.Id, c.CulturalGroup);
            string last = PickLastName(c.Id, c.CulturalGroup);
            string key = BuildNameKey(first, last, c.Name);
            int attempts = 0;

            while (_usedFullNames.Contains(key) && attempts < 15)
            {
                first = PickFirstName(c.Id, c.CulturalGroup);
                last = PickLastName(c.Id, c.CulturalGroup);
                key = BuildNameKey(first, last, c.Name);
                attempts++;
            }

            _usedFullNames.Add(key);
            return (first, last);
        }

        private static string BuildNameKey(string first, string last, string countryName)
            => $"{first} {last} ({countryName})";

        // ---------------- Generation logic ----------------

        private int GenerateAge()
        {
            int age = (int)Math.Round(NextGaussian(28, 5));
            return ClampInt(age, 18, 42);
        }

        private int GenerateNewcomerAge()
            => WeightedInt(new (int value, int weight)[]
            {
                (18, 24),
                (19, 24),
                (20, 22),
                (21, 16),
                (22, 10),
                (23, 6),
                (24, 3),
                (25, 1)
            });

        private int GenerateSkillFromAgePotential(int age, int potential)
        {
            double t;
            if (age <= 22) t = 0.35;
            else if (age <= 26) t = 0.55;
            else if (age <= 30) t = 0.75;
            else if (age <= 33) t = 0.80;
            else if (age <= 36) t = 0.75;
            else t = 0.68;

            int skill = (int)Math.Round(potential * t + NextGaussian(0, 6));
            return ClampInt(skill, 15, potential);
        }

        private int GenerateNewcomerPotential(CountryRow c)
        {
            int potential = ClampInt((int)Math.Round(c.MmaLevel + 6 + NextGaussian(0, 13)), 28, 92);
            if (_rng.NextDouble() < 0.08)
                potential = ClampInt(potential + _rng.Next(6, 14), 32, 97);

            return potential;
        }

        private int GenerateNewcomerSkill(int age, int potential)
        {
            double readiness;
            if (age <= 19) readiness = 0.32;
            else if (age <= 21) readiness = 0.38;
            else if (age <= 23) readiness = 0.45;
            else readiness = 0.52;

            int skill = (int)Math.Round(potential * readiness + NextGaussian(0, 5));
            return ClampInt(skill, 12, Math.Min(72, potential));
        }

        private (int Striking, int Grappling, int Wrestling, int Cardio, int Chin, int FightIQ)
        GenerateStats(int skill, int potential, CountryRow c, string style)
        {
            int baseStat = skill;

            int striking = baseStat;
            int grappling = baseStat;
            int wrestling = baseStat;

            if (style == "Striker") { striking += 10; grappling -= 5; wrestling -= 5; }
            if (style == "Wrestler") { wrestling += 10; striking -= 5; grappling -= 5; }
            if (style == "Grappler") { grappling += 10; striking -= 5; wrestling -= 5; }
            if (style == "AllRounder") { striking += 2; grappling += 2; wrestling += 2; }

            wrestling += (int)Math.Round(c.WrestlingBias * 0.2);
            striking += (int)Math.Round(c.StrikingBias * 0.2);
            grappling += (int)Math.Round(c.GrapplingBias * 0.2);

            int cardio = ClampInt((int)Math.Round(skill + NextGaussian(0, 8)), 10, 99);
            int chin = ClampInt((int)Math.Round(skill + NextGaussian(0, 10)), 10, 99);
            int iq = ClampInt((int)Math.Round(skill + NextGaussian(0, 7)), 10, 99);

            striking = ClampInt((int)Math.Round(striking + NextGaussian(0, 6)), 10, 99);
            grappling = ClampInt((int)Math.Round(grappling + NextGaussian(0, 6)), 10, 99);
            wrestling = ClampInt((int)Math.Round(wrestling + NextGaussian(0, 6)), 10, 99);

            int cap = Math.Min(99, potential + 5);
            striking = Math.Min(striking, cap);
            grappling = Math.Min(grappling, cap);
            wrestling = Math.Min(wrestling, cap);
            cardio = Math.Min(cardio, cap);
            chin = Math.Min(chin, cap);
            iq = Math.Min(iq, cap);

            return (striking, grappling, wrestling, cardio, chin, iq);
        }

        private (int W, int L, int D, int KO, int SUB, int DEC) GenerateRecord(int age, int skill)
        {
            int debutAge = WeightedInt(new (int value, int weight)[] {
                (18, 3), (19, 6), (20, 10), (21, 16), (22, 18),
                (23, 18), (24, 14), (25, 9), (26, 6)
            });

            debutAge = Math.Min(debutAge, age);
            int yearsActive = Math.Max(0, age - debutAge);

            int fpy = WeightedInt(new (int value, int weight)[] {
                (0, yearsActive == 0 ? 10 : 0),
                (1, 26),
                (2, 44),
                (3, 22),
                (4, 6),
                (5, 2)
            });

            if (yearsActive == 0) fpy = _rng.Next(0, 3);

            int fights = yearsActive * fpy + _rng.Next(0, 3);
            if (yearsActive == 0) fights = _rng.Next(0, 3);

            int maxByAge = MaxFightsByAge(age);
            fights = ClampInt(fights, 0, maxByAge);

            double wr = 0.28 + (skill / 130.0);
            wr += NextGaussian(0, 0.06);
            wr = ClampDouble(wr, 0.08, 0.90);

            if (age <= 20)
            {
                fights = Math.Min(fights, 6);
                wr = Math.Min(wr, 0.80);
            }
            else if (age <= 22)
            {
                fights = Math.Min(fights, 12);
                wr = Math.Min(wr, 0.84);
            }
            else if (age <= 25)
            {
                fights = Math.Min(fights, 22);
                wr = Math.Min(wr, 0.88);
            }

            int wins = (int)Math.Round(fights * wr);
            wins = ClampInt(wins, 0, fights);
            int losses = fights - wins;

            if (fights >= 30 && losses < 2)
            {
                int forcedLosses = _rng.Next(2, 6);
                forcedLosses = Math.Min(forcedLosses, fights);
                losses = forcedLosses;
                wins = fights - losses;
            }

            int draws = 0;
            if (fights >= 12 && _rng.NextDouble() < 0.03)
            {
                draws = 1;
                if (losses > 0) losses -= 1;
                else if (wins > 0) wins -= 1;
            }

            int ko = 0, sub = 0, dec = 0;
            double koP = ClampDouble(0.30 + (skill - 50) / 220.0, 0.18, 0.55);
            double subP = ClampDouble(0.20 + (skill - 50) / 260.0, 0.12, 0.40);

            for (int i = 0; i < wins; i++)
            {
                double r = _rng.NextDouble();
                if (r < koP) ko++;
                else if (r < koP + subP) sub++;
                else dec++;
            }

            return (wins, losses, draws, ko, sub, dec);
        }

        private (int W, int L, int D, int KO, int SUB, int DEC) GenerateNewcomerRecord(int age, int skill)
        {
            int stage = WeightedInt(new (int value, int weight)[]
            {
                (0, 44),
                (1, 36),
                (2, 16),
                (3, 4)
            });

            int fights = stage switch
            {
                0 => _rng.NextDouble() < 0.65 ? 0 : 1,
                1 => _rng.Next(1, 4),
                2 => _rng.Next(3, 7),
                _ => _rng.Next(6, 11)
            };

            fights = Math.Min(fights, MaxFightsByAge(age));
            if (fights <= 0)
                return (0, 0, 0, 0, 0, 0);

            double wr = 0.34 + (skill / 145.0) + NextGaussian(0, 0.05);
            wr = ClampDouble(wr, 0.18, 0.86);

            int wins = ClampInt((int)Math.Round(fights * wr), 0, fights);
            int losses = fights - wins;
            int draws = fights >= 4 && _rng.NextDouble() < 0.02 ? 1 : 0;

            if (draws == 1)
            {
                if (losses > 0) losses -= 1;
                else if (wins > 0) wins -= 1;
            }

            int ko = 0, sub = 0, dec = 0;
            double koP = ClampDouble(0.26 + (skill - 45) / 240.0, 0.15, 0.48);
            double subP = ClampDouble(0.18 + (skill - 45) / 280.0, 0.10, 0.34);

            for (int i = 0; i < wins; i++)
            {
                double r = _rng.NextDouble();
                if (r < koP) ko++;
                else if (r < koP + subP) sub++;
                else dec++;
            }

            return (wins, losses, draws, ko, sub, dec);
        }

        private int MaxFightsByAge(int age)
        {
            if (age <= 18) return 2;
            if (age <= 20) return 6;
            if (age <= 22) return 12;
            if (age <= 25) return 22;
            if (age <= 28) return 34;
            if (age <= 31) return 44;
            if (age <= 35) return 54;
            return 60;
        }

        private int WeightedInt((int value, int weight)[] items)
        {
            int total = 0;
            for (int i = 0; i < items.Length; i++) total += Math.Max(0, items[i].weight);
            int roll = _rng.Next(Math.Max(1, total));
            int cum = 0;
            for (int i = 0; i < items.Length; i++)
            {
                cum += Math.Max(0, items[i].weight);
                if (roll < cum) return items[i].value;
            }
            return items[items.Length - 1].value;
        }

        private int GeneratePopularity(int skill, int wins, int losses)
        {
            int pop = (int)Math.Round(NextGaussian(20, 12) + skill * 0.35 + (wins - losses) * 0.8);
            return ClampInt(pop, 0, 100);
        }

        private int GenerateNewcomerPopularity(int skill, int wins, int losses)
        {
            int pop = (int)Math.Round(NextGaussian(6, 5) + skill * 0.12 + wins * 0.7 - losses * 0.3);
            return ClampInt(pop, 0, 35);
        }

        // ---------------- Insert ----------------

        private void InsertFighter(
            SqliteConnection conn, SqliteTransaction tx,
            string first, string last, int countryId,
            int age, string weightClass,
            int primeStart, int primeEnd,
            int potential, int skill,
            int striking, int grappling, int wrestling, int cardio, int chin, int fightIQ,
            string style,
            int wins, int losses, int draws,
            int koWins, int subWins, int decWins,
            int popularity, int retired,
            int? promotionId,
            int salary,
            int contractFightsRemaining,
            int totalFightsInContract,
            string contractStatus,
            int negotiationTurnsRemaining
        )
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
INSERT INTO Fighters
(FirstName, LastName, CountryId,
 Age, WeightClass,
 PrimeAgeStart, PrimeAgeEnd,
 Potential, Skill,
 Striking, Grappling, Wrestling, Cardio, Chin, FightIQ,
 Style,
 Wins, Losses, Draws,
 KOWins, SubWins, DecWins,
 Popularity, Retired,
 PromotionId, Salary, ContractFightsRemaining, TotalFightsInContract, ContractStatus, NegotiationTurnsRemaining)
VALUES
($FirstName, $LastName, $CountryId,
 $Age, $WeightClass,
 $PrimeAgeStart, $PrimeAgeEnd,
 $Potential, $Skill,
 $Striking, $Grappling, $Wrestling, $Cardio, $Chin, $FightIQ,
 $Style,
 $Wins, $Losses, $Draws,
 $KOWins, $SubWins, $DecWins,
 $Popularity, $Retired,
 $PromotionId, $Salary, $ContractFightsRemaining, $TotalFightsInContract, $ContractStatus, $NegotiationTurnsRemaining);
";

            cmd.Parameters.AddWithValue("$FirstName", first);
            cmd.Parameters.AddWithValue("$LastName", last);
            cmd.Parameters.AddWithValue("$CountryId", countryId);
            cmd.Parameters.AddWithValue("$Age", age);
            cmd.Parameters.AddWithValue("$WeightClass", weightClass);
            cmd.Parameters.AddWithValue("$PrimeAgeStart", primeStart);
            cmd.Parameters.AddWithValue("$PrimeAgeEnd", primeEnd);
            cmd.Parameters.AddWithValue("$Potential", potential);
            cmd.Parameters.AddWithValue("$Skill", skill);
            cmd.Parameters.AddWithValue("$Striking", striking);
            cmd.Parameters.AddWithValue("$Grappling", grappling);
            cmd.Parameters.AddWithValue("$Wrestling", wrestling);
            cmd.Parameters.AddWithValue("$Cardio", cardio);
            cmd.Parameters.AddWithValue("$Chin", chin);
            cmd.Parameters.AddWithValue("$FightIQ", fightIQ);
            cmd.Parameters.AddWithValue("$Style", style);
            cmd.Parameters.AddWithValue("$Wins", wins);
            cmd.Parameters.AddWithValue("$Losses", losses);
            cmd.Parameters.AddWithValue("$Draws", draws);
            cmd.Parameters.AddWithValue("$KOWins", koWins);
            cmd.Parameters.AddWithValue("$SubWins", subWins);
            cmd.Parameters.AddWithValue("$DecWins", decWins);
            cmd.Parameters.AddWithValue("$Popularity", popularity);
            cmd.Parameters.AddWithValue("$Retired", retired);

            if (promotionId.HasValue) cmd.Parameters.AddWithValue("$PromotionId", promotionId.Value);
            else cmd.Parameters.AddWithValue("$PromotionId", DBNull.Value);

            cmd.Parameters.AddWithValue("$Salary", salary);
            cmd.Parameters.AddWithValue("$ContractFightsRemaining", contractFightsRemaining);
            cmd.Parameters.AddWithValue("$TotalFightsInContract", totalFightsInContract);
            cmd.Parameters.AddWithValue("$ContractStatus", contractStatus);
            cmd.Parameters.AddWithValue("$NegotiationTurnsRemaining", negotiationTurnsRemaining);

            cmd.ExecuteNonQuery();
        }

        // ---------------- Utils ----------------

        private string WeightedPick(List<NameWeight> list)
        {
            if (list == null || list.Count == 0) return "Unknown";

            int total = 0;
            for (int i = 0; i < list.Count; i++) total += Math.Max(0, list[i].Weight);
            if (total <= 0) return list[0].Name;

            int roll = _rng.Next(total);
            int cum = 0;
            for (int i = 0; i < list.Count; i++)
            {
                cum += Math.Max(0, list[i].Weight);
                if (roll < cum) return list[i].Name;
            }
            return list[list.Count - 1].Name;
        }

        private int ClampInt(int v, int min, int max) => Math.Min(max, Math.Max(min, v));
        private double ClampDouble(double v, double min, double max) => Math.Min(max, Math.Max(min, v));

        private double NextGaussian(double mean, double stdDev)
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        private static void ExecuteNonQuery(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // ---------------- DTOs ----------------

        private sealed class CountryRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int FighterSpawnWeight { get; set; }
            public string CulturalGroup { get; set; } = "";
            public int MmaLevel { get; set; }
            public int WrestlingBias { get; set; }
            public int StrikingBias { get; set; }
            public int GrapplingBias { get; set; }
        }

        private sealed class NameWeight
        {
            public string Name { get; set; } = "";
            public int Weight { get; set; }
        }
     
    }
}
