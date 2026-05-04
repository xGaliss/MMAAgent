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

        private const string DefaultCulturalGroup = "Anglo";
        private const double CountrySpecificNameChance = 0.15;

        // --- Caches ---
        private List<CountryRow> _countries = new();
        private readonly List<WeightedValue<CountryRow>> _countryWeights = new();

        private readonly Dictionary<int, List<NameWeight>> _firstNamesByCountry = new();
        private readonly Dictionary<int, List<NameWeight>> _lastNamesByCountry = new();
        private readonly Dictionary<string, List<NameWeight>> _firstNamesByGroup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NameWeight>> _lastNamesByGroup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<WeightedValue<string>>> _culturesByCountry = new();
        private readonly List<NameWeight> _globalFirstNames = new();
        private readonly List<NameWeight> _globalLastNames = new();

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
            LoadCountryCultureWeights(conn, tx);

            if (_countries.Count == 0 || _countryWeights.Count == 0)
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
                var culturalGroup = PickCultureForCountry(c);
                var (first, last) = GenerateUniqueName(c, culturalGroup);

                int age = GenerateAge();
                string wc = PickWeightClass();

                int primeStart = _rng.Next(26, 29);
                int primeEnd = primeStart + _rng.Next(5, 8);

                int potential = GeneratePotential(c, isNewcomer: false);
                int skill = GenerateSkillFromAgePotential(age, potential);
                string style = PickStyle(c);
                var personality = GeneratePersonalityProfile(c, age, potential, style, isNewcomer: false);

                var stats = GenerateAttributes(c, skill, potential, style);
                var record = GenerateRecord(age, skill);
                var popularity = GeneratePopularity(skill, record.W, record.L, personality, age, isNewcomer: false);
                var marketability = GenerateMarketability(popularity, personality, style, age, potential, isNewcomer: false);
                var momentum = GenerateMomentum(skill, potential, age, record.W, record.L, isNewcomer: false);
                var reliability = GenerateReliability(personality, age, record.W, record.L);
                var mediaHeat = GenerateMediaHeat(popularity, marketability, momentum, personality, style, isNewcomer: false);

                InsertFighter(
                    conn, tx,
                    first, last, c.Id,
                    culturalGroup,
                    age, wc,
                    primeStart, primeEnd,
                    potential, skill,
                    stats.Striking, stats.Grappling, stats.Wrestling, stats.Cardio, stats.Chin, stats.FightIQ,
                    style,
                    record.W, record.L, record.D,
                    record.KO, record.SUB, record.DEC,
                    popularity: popularity,
                    marketability: marketability,
                    momentum: momentum,
                    reliabilityScore: reliability,
                    mediaHeat: mediaHeat,
                    ambition: personality.Ambition,
                    discipline: personality.Discipline,
                    riskTolerance: personality.RiskTolerance,
                    stability: personality.Stability,
                    showmanship: personality.Showmanship,
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
            LoadCountryCultureWeights(conn, tx);
            LoadUsedFullNames(conn, tx);

            if (_countries.Count == 0 || _countryWeights.Count == 0)
                throw new InvalidOperationException("No hay países válidos (FighterSpawnWeight > 0).");

            var generated = 0;

            for (int i = 0; i < totalToGenerate; i++)
            {
                var c = PickCountry();
                var culturalGroup = PickCultureForCountry(c);
                var (first, last) = GenerateUniqueName(c, culturalGroup);
                var age = GenerateNewcomerAge();
                var wc = PickWeightClass();

                int primeStart = _rng.Next(26, 29);
                int primeEnd = primeStart + _rng.Next(5, 8);

                int potential = GeneratePotential(c, isNewcomer: true);
                int skill = GenerateNewcomerSkill(age, potential);
                string style = PickStyle(c);
                var personality = GeneratePersonalityProfile(c, age, potential, style, isNewcomer: true);

                var stats = GenerateAttributes(c, skill, potential, style);
                var record = GenerateNewcomerRecord(age, skill);
                var popularity = GeneratePopularity(skill, record.W, record.L, personality, age, isNewcomer: true);
                var marketability = GenerateMarketability(popularity, personality, style, age, potential, isNewcomer: true);
                var momentum = GenerateMomentum(skill, potential, age, record.W, record.L, isNewcomer: true);
                var reliability = GenerateReliability(personality, age, record.W, record.L);
                var mediaHeat = GenerateMediaHeat(popularity, marketability, momentum, personality, style, isNewcomer: true);

                InsertFighter(
                    conn, tx,
                    first, last, c.Id,
                    culturalGroup,
                    age, wc,
                    primeStart, primeEnd,
                    potential, skill,
                    stats.Striking, stats.Grappling, stats.Wrestling, stats.Cardio, stats.Chin, stats.FightIQ,
                    style,
                    record.W, record.L, record.D,
                    record.KO, record.SUB, record.DEC,
                    popularity: popularity,
                    marketability: marketability,
                    momentum: momentum,
                    reliabilityScore: reliability,
                    mediaHeat: mediaHeat,
                    ambition: personality.Ambition,
                    discipline: personality.Discipline,
                    riskTolerance: personality.RiskTolerance,
                    stability: personality.Stability,
                    showmanship: personality.Showmanship,
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
            _countryWeights.Clear();

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
                if (c.FighterSpawnWeight > 0)
                    _countryWeights.Add(new WeightedValue<CountryRow>(c, c.FighterSpawnWeight));
            }
        }

        private void LoadNameCaches(SqliteConnection conn, SqliteTransaction tx)
        {
            _firstNamesByCountry.Clear();
            _lastNamesByCountry.Clear();
            _firstNamesByGroup.Clear();
            _lastNamesByGroup.Clear();
            _globalFirstNames.Clear();
            _globalLastNames.Clear();

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
                    _globalFirstNames.Add(row);
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
                    _globalLastNames.Add(row);
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
                    _globalFirstNames.Add(row);
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
                    _globalLastNames.Add(row);
                }
            }
        }

        private void LoadCountryCultureWeights(SqliteConnection conn, SqliteTransaction tx)
        {
            _culturesByCountry.Clear();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT CountryId, CulturalGroup, Weight
FROM CountryCultureWeights
WHERE Weight > 0
ORDER BY CountryId, Weight DESC, CulturalGroup;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var countryId = Convert.ToInt32(reader["CountryId"]);
                var culturalGroup = reader["CulturalGroup"]?.ToString() ?? DefaultCulturalGroup;
                var weight = Convert.ToInt32(reader["Weight"]);

                if (!_culturesByCountry.TryGetValue(countryId, out var list))
                    _culturesByCountry[countryId] = list = new List<WeightedValue<string>>();

                list.Add(new WeightedValue<string>(culturalGroup, weight));
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
            => WeightedRandomPicker.PickOrDefault(_countryWeights, _rng, _countries[0]);

        /// <summary>
        /// Country describes geography and MMA environment.
        /// Culture describes naming pool and background flavor inside that country.
        /// We prefer CountryCultureWeights and only fall back to Countries.CulturalGroup when needed.
        /// </summary>
        private string PickCultureForCountry(CountryRow country)
        {
            if (_culturesByCountry.TryGetValue(country.Id, out var weightedCultures) && weightedCultures.Count > 0)
                return WeightedRandomPicker.PickOrDefault(weightedCultures, _rng, DefaultCulturalGroup);

            if (!string.IsNullOrWhiteSpace(country.CulturalGroup))
                return country.CulturalGroup;

            return DefaultCulturalGroup;
        }

        /// <summary>
        /// Country-specific name tables are only overrides/flavor.
        /// The main source is the cultural pool so we do not need to duplicate every name by country.
        /// About 15% of the time we try the country-specific pool first if it exists.
        /// </summary>
        private string PickFirstName(string culturalGroup, int countryId)
        {
            var countryPool = _firstNamesByCountry.TryGetValue(countryId, out var namesByCountry) ? namesByCountry : null;
            var culturePool = !string.IsNullOrWhiteSpace(culturalGroup) && _firstNamesByGroup.TryGetValue(culturalGroup, out var namesByCulture)
                ? namesByCulture
                : null;

            return PickNameFromPools(countryPool, culturePool, _globalFirstNames);
        }

        private string PickLastName(string culturalGroup, int countryId)
        {
            var countryPool = _lastNamesByCountry.TryGetValue(countryId, out var namesByCountry) ? namesByCountry : null;
            var culturePool = !string.IsNullOrWhiteSpace(culturalGroup) && _lastNamesByGroup.TryGetValue(culturalGroup, out var namesByCulture)
                ? namesByCulture
                : null;

            return PickNameFromPools(countryPool, culturePool, _globalLastNames);
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
            var options = new List<WeightedValue<string>>
            {
                new("Boxer", Math.Max(6, 16 + c.StrikingBias + _rng.Next(-4, 5))),
                new("Kickboxer", Math.Max(6, 18 + c.StrikingBias + _rng.Next(-4, 5))),
                new("Counter Striker", Math.Max(6, 12 + c.StrikingBias + _rng.Next(-3, 4))),
                new("Pressure Wrestler", Math.Max(6, 18 + c.WrestlingBias + _rng.Next(-4, 5))),
                new("Control Wrestler", Math.Max(6, 18 + c.WrestlingBias + _rng.Next(-4, 5))),
                new("Submission Hunter", Math.Max(6, 18 + c.GrapplingBias + _rng.Next(-4, 5))),
                new("Scrambler", Math.Max(6, 12 + c.GrapplingBias + c.WrestlingBias / 2 + _rng.Next(-4, 5))),
                new("Brawler", Math.Max(6, 12 + c.StrikingBias + _rng.Next(-4, 5))),
                new("Well Rounded", Math.Max(10, 24 + _rng.Next(-6, 7)))
            };

            return WeightedRandomPicker.PickOrDefault(options, _rng, "Well Rounded");
        }

        private (string First, string Last) GenerateUniqueName(CountryRow country, string culturalGroup)
        {
            string first = PickFirstName(culturalGroup, country.Id);
            string last = PickLastName(culturalGroup, country.Id);
            string key = BuildNameKey(first, last, country.Name);
            int attempts = 0;

            while (_usedFullNames.Contains(key) && attempts < 15)
            {
                first = PickFirstName(culturalGroup, country.Id);
                last = PickLastName(culturalGroup, country.Id);
                key = BuildNameKey(first, last, country.Name);
                attempts++;
            }

            _usedFullNames.Add(key);
            return (first, last);
        }

        private static string BuildNameKey(string first, string last, string countryName)
            => $"{first} {last} ({countryName})";

        private string PickNameFromPools(
            List<NameWeight>? countryPool,
            List<NameWeight>? culturePool,
            List<NameWeight> globalPool)
        {
            var tryCountryFirst = countryPool is { Count: > 0 } && _rng.NextDouble() < CountrySpecificNameChance;

            if (tryCountryFirst)
            {
                var picked = WeightedPickOrNull(countryPool!);
                if (!string.IsNullOrWhiteSpace(picked))
                    return picked!;
            }

            if (culturePool is { Count: > 0 })
            {
                var picked = WeightedPickOrNull(culturePool);
                if (!string.IsNullOrWhiteSpace(picked))
                    return picked!;
            }

            if (!tryCountryFirst && countryPool is { Count: > 0 })
            {
                var picked = WeightedPickOrNull(countryPool);
                if (!string.IsNullOrWhiteSpace(picked))
                    return picked!;
            }

            var globalPicked = WeightedPickOrNull(globalPool);
            return !string.IsNullOrWhiteSpace(globalPicked) ? globalPicked! : "Unknown";
        }

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

        private int GeneratePotential(CountryRow country, bool isNewcomer)
        {
            var baseline = country.MmaLevel + (isNewcomer ? 6 : 0);
            var spread = isNewcomer ? 13 : 12;
            var potential = ClampInt((int)Math.Round(baseline + NextGaussian(0, spread)), isNewcomer ? 28 : 20, 99);

            // Weak countries can still produce rare prodigies.
            var prodigyChance = ClampDouble(0.02 + (country.MmaLevel / 2000.0), 0.02, 0.08);
            if (_rng.NextDouble() < prodigyChance)
                potential = ClampInt(potential + _rng.Next(8, 21), isNewcomer ? 32 : 24, 99);

            // Strong countries can still produce mediocre talent.
            var mediocreChance = ClampDouble(0.02 + (country.MmaLevel / 2500.0), 0.02, 0.06);
            if (_rng.NextDouble() < mediocreChance)
                potential = ClampInt(potential - _rng.Next(6, 17), isNewcomer ? 24 : 18, 99);

            return potential;
        }

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

        private PersonalityProfile GeneratePersonalityProfile(CountryRow country, int age, int potential, string style, bool isNewcomer)
        {
            var ambition = ClampInt((int)Math.Round(45 + NextGaussian(0, 14) + ((potential - 50) * 0.18) + (isNewcomer ? 5 : 0)), 15, 95);
            var discipline = ClampInt((int)Math.Round(50 + NextGaussian(0, 13) + (country.WrestlingBias * 0.18) + (country.MmaLevel - 50) * 0.08), 15, 95);
            var riskTolerance = ClampInt((int)Math.Round(46 + NextGaussian(0, 14)
                + (style is "Brawler" or "Boxer" or "Counter Striker" ? 10 : 0)
                + (style is "Pressure Wrestler" or "Submission Hunter" ? 6 : 0)
                - (style == "Control Wrestler" ? 8 : 0)), 15, 95);
            var stability = ClampInt((int)Math.Round(52 + NextGaussian(0, 12) + (age >= 30 ? 5 : 0) - (isNewcomer ? 4 : 0)), 15, 95);
            var showmanship = ClampInt((int)Math.Round(34 + NextGaussian(0, 16)
                + (style is "Boxer" or "Kickboxer" or "Brawler" ? 10 : 0)
                + (country.StrikingBias * 0.12)
                + ((potential - 50) * 0.10)), 5, 95);

            return new PersonalityProfile(ambition, discipline, riskTolerance, stability, showmanship);
        }

        private (int Striking, int Grappling, int Wrestling, int Cardio, int Chin, int FightIQ)
        GenerateAttributes(CountryRow c, int skill, int potential, string style)
        {
            int baseStat = skill;

            int striking = baseStat;
            int grappling = baseStat;
            int wrestling = baseStat;
            int cardioBoost = 0;
            int chinBoost = 0;
            int fightIQBoost = 0;

            switch (style)
            {
                case "Boxer":
                    striking += 12; grappling -= 5; wrestling -= 4;
                    break;
                case "Kickboxer":
                    striking += 10; grappling -= 4; wrestling -= 4; cardioBoost += 1;
                    break;
                case "Counter Striker":
                    striking += 9; fightIQBoost = 4; wrestling -= 3;
                    break;
                case "Pressure Wrestler":
                    wrestling += 11; cardioBoost = 4; striking -= 3;
                    break;
                case "Control Wrestler":
                    wrestling += 10; grappling += 4; striking -= 5;
                    break;
                case "Submission Hunter":
                    grappling += 12; wrestling += 3; striking -= 5;
                    break;
                case "Scrambler":
                    grappling += 7; wrestling += 6; cardioBoost = 3; chinBoost = -1;
                    break;
                case "Brawler":
                    striking += 9; chinBoost = 2; cardioBoost = -2; fightIQBoost = -2;
                    break;
                default:
                    striking += 2; grappling += 2; wrestling += 2;
                    break;
            }

            wrestling += (int)Math.Round(c.WrestlingBias * 0.28) + _rng.Next(-2, 3);
            striking += (int)Math.Round(c.StrikingBias * 0.28) + _rng.Next(-2, 3);
            grappling += (int)Math.Round(c.GrapplingBias * 0.28) + _rng.Next(-2, 3);

            int cardio = ClampInt((int)Math.Round(skill + NextGaussian(0, 8) + (c.WrestlingBias * 0.05) + cardioBoost), 10, 99);
            int chin = ClampInt((int)Math.Round(skill + NextGaussian(0, 10) + chinBoost), 10, 99);
            int iq = ClampInt((int)Math.Round(skill + NextGaussian(0, 7) + ((c.WrestlingBias + c.GrapplingBias + c.StrikingBias) / 30.0) + fightIQBoost), 10, 99);

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

        private int GeneratePopularity(int skill, int wins, int losses, PersonalityProfile personality, int age, bool isNewcomer)
        {
            var ageVisibility = age is >= 25 and <= 32 ? 4 : age <= 21 ? -3 : 0;
            var newcomerPenalty = isNewcomer ? 12 : 0;
            int pop = (int)Math.Round(
                NextGaussian(isNewcomer ? 14 : 24, isNewcomer ? 7 : 12)
                + skill * (isNewcomer ? 0.14 : 0.28)
                + (wins - losses) * (isNewcomer ? 0.55 : 0.75)
                + (personality.Showmanship * 0.24)
                + (personality.Ambition * 0.08)
                + ageVisibility
                - newcomerPenalty);

            return ClampInt(pop, 0, isNewcomer ? 42 : 100);
        }

        private int GenerateMarketability(int popularity, PersonalityProfile personality, string style, int age, int potential, bool isNewcomer)
        {
            var styleBonus = style is "Boxer" or "Kickboxer" or "Brawler" or "Counter Striker" ? 7
                : style is "Submission Hunter" or "Pressure Wrestler" ? 3
                : 0;
            var ageBonus = age is >= 24 and <= 33 ? 5 : age >= 36 ? -4 : 0;
            var newcomerPenalty = isNewcomer ? 10 : 0;

            return ClampInt((int)Math.Round(
                16
                + (popularity * 0.55)
                + (personality.Showmanship * 0.30)
                + (personality.RiskTolerance * 0.08)
                + ((potential - 50) * 0.10)
                + styleBonus
                + ageBonus
                - newcomerPenalty), 10, 99);
        }

        private int GenerateMomentum(int skill, int potential, int age, int wins, int losses, bool isNewcomer)
        {
            var upside = Math.Max(0, potential - skill);
            var youthPush = age <= 24 ? 6 : age >= 35 ? -4 : 0;
            var newcomerPush = isNewcomer ? 4 : 0;
            return ClampInt((int)Math.Round(
                42
                + NextGaussian(0, 8)
                + (upside * 0.35)
                + ((wins - losses) * 0.8)
                + youthPush
                + newcomerPush), 8, 92);
        }

        private int GenerateReliability(PersonalityProfile personality, int age, int wins, int losses)
        {
            return ClampInt((int)Math.Round(
                42
                + (personality.Discipline * 0.32)
                + (personality.Stability * 0.22)
                - (personality.RiskTolerance * 0.12)
                + (age >= 29 ? 3 : 0)
                + (wins >= losses ? 2 : -1)
                + NextGaussian(0, 5)), 18, 95);
        }

        private int GenerateMediaHeat(int popularity, int marketability, int momentum, PersonalityProfile personality, string style, bool isNewcomer)
        {
            var styleBonus = style is "Boxer" or "Kickboxer" or "Brawler" ? 6
                : style is "Counter Striker" ? 4
                : 0;
            var newcomerPenalty = isNewcomer ? 8 : 0;

            return ClampInt((int)Math.Round(
                10
                + (popularity * 0.28)
                + (marketability * 0.24)
                + (momentum * 0.20)
                + (personality.Showmanship * 0.18)
                + styleBonus
                - newcomerPenalty
                + NextGaussian(0, 4)), 5, 95);
        }

        // ---------------- Insert ----------------

        private void InsertFighter(
            SqliteConnection conn, SqliteTransaction tx,
            string first, string last, int countryId,
            string culturalGroup,
            int age, string weightClass,
            int primeStart, int primeEnd,
            int potential, int skill,
            int striking, int grappling, int wrestling, int cardio, int chin, int fightIQ,
            string style,
            int wins, int losses, int draws,
            int koWins, int subWins, int decWins,
            int popularity,
            int marketability,
            int momentum,
            int reliabilityScore,
            int mediaHeat,
            int ambition,
            int discipline,
            int riskTolerance,
            int stability,
            int showmanship,
            int retired,
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
(FirstName, LastName, CountryId, CulturalGroup,
 Age, WeightClass,
 PrimeAgeStart, PrimeAgeEnd,
 Potential, Skill,
 Striking, Grappling, Wrestling, Cardio, Chin, FightIQ,
 Style,
 Wins, Losses, Draws,
 KOWins, SubWins, DecWins,
 Popularity, Marketability, Momentum, ReliabilityScore, MediaHeat,
 Ambition, Discipline, RiskTolerance, Stability, Showmanship,
 Retired,
 PromotionId, Salary, ContractFightsRemaining, TotalFightsInContract, ContractStatus, NegotiationTurnsRemaining)
VALUES
($FirstName, $LastName, $CountryId, $CulturalGroup,
 $Age, $WeightClass,
 $PrimeAgeStart, $PrimeAgeEnd,
 $Potential, $Skill,
 $Striking, $Grappling, $Wrestling, $Cardio, $Chin, $FightIQ,
 $Style,
 $Wins, $Losses, $Draws,
 $KOWins, $SubWins, $DecWins,
 $Popularity, $Marketability, $Momentum, $ReliabilityScore, $MediaHeat,
 $Ambition, $Discipline, $RiskTolerance, $Stability, $Showmanship,
 $Retired,
 $PromotionId, $Salary, $ContractFightsRemaining, $TotalFightsInContract, $ContractStatus, $NegotiationTurnsRemaining);
";

            cmd.Parameters.AddWithValue("$FirstName", first);
            cmd.Parameters.AddWithValue("$LastName", last);
            cmd.Parameters.AddWithValue("$CountryId", countryId);
            cmd.Parameters.AddWithValue("$CulturalGroup", culturalGroup);
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
            cmd.Parameters.AddWithValue("$Marketability", marketability);
            cmd.Parameters.AddWithValue("$Momentum", momentum);
            cmd.Parameters.AddWithValue("$ReliabilityScore", reliabilityScore);
            cmd.Parameters.AddWithValue("$MediaHeat", mediaHeat);
            cmd.Parameters.AddWithValue("$Ambition", ambition);
            cmd.Parameters.AddWithValue("$Discipline", discipline);
            cmd.Parameters.AddWithValue("$RiskTolerance", riskTolerance);
            cmd.Parameters.AddWithValue("$Stability", stability);
            cmd.Parameters.AddWithValue("$Showmanship", showmanship);
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

        private string? WeightedPickOrNull(List<NameWeight> list)
        {
            if (list == null || list.Count == 0)
                return null;

            var weighted = new List<WeightedValue<string>>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Weight > 0 && !string.IsNullOrWhiteSpace(list[i].Name))
                    weighted.Add(new WeightedValue<string>(list[i].Name, list[i].Weight));
            }

            return WeightedRandomPicker.PickOrDefault(weighted, _rng, null!);
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

        private sealed record PersonalityProfile(
            int Ambition,
            int Discipline,
            int RiskTolerance,
            int Stability,
            int Showmanship);
     
    }
}
