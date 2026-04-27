using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebDivisionsService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly WebPromotionProfileService _promotionProfileService;

    public WebDivisionsService(
        SqliteConnectionFactory factory,
        WebPromotionProfileService promotionProfileService)
    {
        _factory = factory;
        _promotionProfileService = promotionProfileService;
    }

    public async Task<IReadOnlyList<DivisionPromotionGroupVm>> LoadAsync()
    {
        var promotions = await LoadPromotionSummariesAsync();
        var groups = new List<DivisionPromotionGroupVm>();

        foreach (var promotion in promotions)
        {
            var profile = await _promotionProfileService.LoadAsync(promotion.Id);
            if (profile is null || profile.Divisions.Count == 0)
                continue;

            groups.Add(new DivisionPromotionGroupVm(
                promotion.Id,
                promotion.Name,
                profile.Divisions));
        }

        return groups;
    }

    private async Task<IReadOnlyList<PromotionSummaryVm>> LoadPromotionSummariesAsync()
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, Name, Prestige, Budget, IsActive, EventIntervalWeeks, NextEventWeek
FROM Promotions
WHERE COALESCE(IsActive, 1) = 1
ORDER BY Prestige DESC, Name;";

        var items = new List<PromotionSummaryVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new PromotionSummaryVm(
                Convert.ToInt32(reader["Id"]),
                reader["Name"]?.ToString() ?? "",
                Convert.ToInt32(reader["Prestige"]),
                Convert.ToInt32(reader["Budget"]),
                Convert.ToInt32(reader["IsActive"]) == 1,
                Convert.ToInt32(reader["EventIntervalWeeks"]),
                Convert.ToInt32(reader["NextEventWeek"])));
        }

        return items;
    }
}
