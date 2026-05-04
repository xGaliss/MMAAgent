using Microsoft.Data.Sqlite;

namespace MMAAgent.Tools.Commands;

internal static class CountryCultureValidationCommand
{
    private static readonly string DefaultDbPath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "MMAAgent.Web",
        "Assets",
        "Database",
        "MMA_Agent.db");

    public static async Task<int> RunAsync(string[] args)
    {
        var dbPath = ResolveDbPath(args);
        if (dbPath is null)
            return 1;

        var fullPath = Path.GetFullPath(dbPath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Database not found: {fullPath}");
            return 1;
        }

        var report = new ValidationReport(fullPath);

        await using var conn = new SqliteConnection($"Data Source={fullPath}");
        await conn.OpenAsync();

        await ValidateSchemaAsync(conn, report);
        if (report.Errors.Count > 0)
        {
            report.Print();
            return 1;
        }

        await ValidateCountryCultureCoverageAsync(conn, report);
        await ValidateCultureNamePoolsAsync(conn, report);
        await ValidateCountryOverrideTablesAsync(conn, report);

        report.Print();
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private static string? ResolveDbPath(string[] args)
    {
        if (args.Length == 0)
            return DefaultDbPath;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                Console.Error.WriteLine("Missing value for --db.");
                return null;
            }

            return args[i + 1];
        }

        Console.Error.WriteLine("Usage: validate-country-data [--db <path>]");
        return null;
    }

    private static async Task ValidateSchemaAsync(SqliteConnection conn, ValidationReport report)
    {
        var requiredTables = new[]
        {
            "Countries",
            "CountryCultureWeights",
            "FirstNames",
            "FirstNameGroups",
            "FirstNameCountries",
            "LastNames",
            "LastNameGroups",
            "LastNameCountries",
            "Fighters"
        };

        foreach (var table in requiredTables)
        {
            if (!await TableExistsAsync(conn, table))
                report.Error($"Missing required table: {table}");
        }

        if (!await ColumnExistsAsync(conn, "Fighters", "CulturalGroup"))
            report.Error("Missing required column: Fighters.CulturalGroup");

        if (!await UniqueIndexExistsAsync(conn, "UX_CountryCultureWeights_Country_Culture"))
            report.Warn("Missing unique index UX_CountryCultureWeights_Country_Culture on CountryCultureWeights.");
    }

    private static async Task ValidateCountryCultureCoverageAsync(SqliteConnection conn, ValidationReport report)
    {
        const string sql = @"
SELECT
    c.Id,
    c.Name,
    c.CulturalGroup,
    c.FighterSpawnWeight,
    COUNT(ccw.Id) AS CultureRows,
    COALESCE(SUM(ccw.Weight), 0) AS TotalCultureWeight
FROM Countries c
LEFT JOIN CountryCultureWeights ccw ON ccw.CountryId = c.Id
GROUP BY c.Id, c.Name, c.CulturalGroup, c.FighterSpawnWeight
ORDER BY c.Id;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var countryName = reader.GetString(1);
            var legacyCulture = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var spawnWeight = reader.GetInt32(3);
            var cultureRows = reader.GetInt32(4);
            var totalWeight = reader.GetInt32(5);

            if (spawnWeight > 0 && cultureRows == 0)
            {
                report.Error($"Country '{countryName}' has FighterSpawnWeight > 0 but no CountryCultureWeights rows.");
                continue;
            }

            if (spawnWeight > 0 && totalWeight <= 0)
                report.Error($"Country '{countryName}' has non-positive total culture weight.");

            if (cultureRows == 0 && string.IsNullOrWhiteSpace(legacyCulture))
                report.Error($"Country '{countryName}' has no CountryCultureWeights and no legacy CulturalGroup fallback.");
        }
    }

    private static async Task ValidateCultureNamePoolsAsync(SqliteConnection conn, ValidationReport report)
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT CulturalGroup FROM CountryCultureWeights WHERE TRIM(CulturalGroup) <> '';";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cultures.Add(reader.GetString(0));
        }

        foreach (var culture in cultures.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase))
        {
            var firstCount = await ScalarIntAsync(conn,
                "SELECT COUNT(*) FROM FirstNameGroups WHERE CulturalGroup = $culture AND Weight > 0;",
                ("$culture", culture));

            var lastCount = await ScalarIntAsync(conn,
                "SELECT COUNT(*) FROM LastNameGroups WHERE CulturalGroup = $culture AND Weight > 0;",
                ("$culture", culture));

            if (firstCount == 0)
                report.Error($"Culture '{culture}' has no positive-weight first names in FirstNameGroups.");

            if (lastCount == 0)
                report.Error($"Culture '{culture}' has no positive-weight last names in LastNameGroups.");
        }
    }

    private static async Task ValidateCountryOverrideTablesAsync(SqliteConnection conn, ValidationReport report)
    {
        var orphanFirstOverrides = await ScalarIntAsync(conn, @"
SELECT COUNT(*)
FROM FirstNameCountries fnc
LEFT JOIN Countries c ON c.Id = fnc.CountryId
LEFT JOIN FirstNames fn ON fn.Id = fnc.FirstNameId
WHERE c.Id IS NULL OR fn.Id IS NULL;");

        var orphanLastOverrides = await ScalarIntAsync(conn, @"
SELECT COUNT(*)
FROM LastNameCountries lnc
LEFT JOIN Countries c ON c.Id = lnc.CountryId
LEFT JOIN LastNames ln ON ln.Id = lnc.LastNameId
WHERE c.Id IS NULL OR ln.Id IS NULL;");

        if (orphanFirstOverrides > 0)
            report.Error($"FirstNameCountries contains {orphanFirstOverrides} orphan override rows.");

        if (orphanLastOverrides > 0)
            report.Error($"LastNameCountries contains {orphanLastOverrides} orphan override rows.");

        var nonPositiveFirstOverrides = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM FirstNameCountries WHERE Weight <= 0;");
        var nonPositiveLastOverrides = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM LastNameCountries WHERE Weight <= 0;");

        if (nonPositiveFirstOverrides > 0)
            report.Warn($"FirstNameCountries contains {nonPositiveFirstOverrides} non-positive override rows.");

        if (nonPositiveLastOverrides > 0)
            report.Warn($"LastNameCountries contains {nonPositiveLastOverrides} non-positive override rows.");
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<bool> UniqueIndexExistsAsync(SqliteConnection conn, string indexName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM sqlite_master
WHERE type = 'index'
  AND name = $name
LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", indexName);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private sealed class ValidationReport
    {
        public ValidationReport(string dbPath)
        {
            DbPath = dbPath;
        }

        public string DbPath { get; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public void Error(string message) => Errors.Add(message);
        public void Warn(string message) => Warnings.Add(message);

        public void Print()
        {
            Console.WriteLine($"Validating country/culture data: {DbPath}");
            Console.WriteLine();

            if (Errors.Count == 0 && Warnings.Count == 0)
            {
                Console.WriteLine("OK: no issues found.");
                return;
            }

            if (Errors.Count > 0)
            {
                Console.WriteLine("Errors:");
                foreach (var error in Errors)
                    Console.WriteLine($"  - {error}");
                Console.WriteLine();
            }

            if (Warnings.Count > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var warning in Warnings)
                    Console.WriteLine($"  - {warning}");
                Console.WriteLine();
            }

            Console.WriteLine($"Summary: {Errors.Count} error(s), {Warnings.Count} warning(s).");
        }
    }
}
