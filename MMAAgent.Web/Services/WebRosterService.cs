using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebRosterService
{
    private readonly SqliteConnectionFactory _factory;

    public WebRosterService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<RosterQueryResult> SearchAsync(
        string? searchText,
        string? weightClass,
        string? country,
        int take = 500)
    {
        using var conn = _factory.CreateConnection();

        var where = new List<string>();
        using var countCmd = conn.CreateCommand();
        using var listCmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            where.Add("(f.FirstName || ' ' || f.LastName LIKE $search OR c.Name LIKE $search)");
            countCmd.Parameters.AddWithValue("$search", $"%{searchText.Trim()}%");
            listCmd.Parameters.AddWithValue("$search", $"%{searchText.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(weightClass))
        {
            where.Add("f.WeightClass = $weightClass");
            countCmd.Parameters.AddWithValue("$weightClass", weightClass.Trim());
            listCmd.Parameters.AddWithValue("$weightClass", weightClass.Trim());
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            where.Add("c.Name = $country");
            countCmd.Parameters.AddWithValue("$country", country.Trim());
            listCmd.Parameters.AddWithValue("$country", country.Trim());
        }

        var whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        countCmd.CommandText = $@"
SELECT COUNT(*)
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
{whereSql};";

        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        listCmd.CommandText = $@"
SELECT
    f.Id,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    f.WeightClass,
    COALESCE(c.Name, '') AS CountryName,
    f.Wins,
    f.Losses,
    f.Draws
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
{whereSql}
ORDER BY f.Skill DESC, f.Popularity DESC, f.Wins DESC
LIMIT $take;";
        listCmd.Parameters.AddWithValue("$take", take);

        var items = new List<RosterListItemVm>();
        using (var r = await listCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                items.Add(new RosterListItemVm(
                    Convert.ToInt32(r["Id"]),
                    r["FighterName"]?.ToString() ?? "",
                    r["WeightClass"]?.ToString() ?? "",
                    r["CountryName"]?.ToString() ?? "",
                    Convert.ToInt32(r["Wins"]),
                    Convert.ToInt32(r["Losses"]),
                    Convert.ToInt32(r["Draws"])));
            }
        }

        var weights = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT WeightClass FROM Fighters ORDER BY WeightClass;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                weights.Add(r.GetString(0));
        }

        var countries = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT DISTINCT c.Name
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
WHERE c.Name IS NOT NULL AND c.Name <> ''
ORDER BY c.Name;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                countries.Add(r.GetString(0));
        }

        return new RosterQueryResult(
            total,
            items,
            new RosterFilterOptions(weights, countries));
    }
}
