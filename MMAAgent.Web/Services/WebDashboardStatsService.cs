using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Web.Services;

public sealed class WebDashboardStatsService
{
    private readonly SqliteConnectionFactory _factory;

    public WebDashboardStatsService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(int FighterCount, int PromotionCount)> LoadAsync()
    {
        using var conn = _factory.CreateConnection();

        using var fightersCmd = conn.CreateCommand();
        fightersCmd.CommandText = "SELECT COUNT(*) FROM Fighters;";
        var fighters = Convert.ToInt32(await fightersCmd.ExecuteScalarAsync());

        using var promotionsCmd = conn.CreateCommand();
        promotionsCmd.CommandText = "SELECT COUNT(*) FROM Promotions;";
        var promotions = Convert.ToInt32(await promotionsCmd.ExecuteScalarAsync());

        return (fighters, promotions);
    }
}
