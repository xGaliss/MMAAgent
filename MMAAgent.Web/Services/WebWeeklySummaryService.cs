using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebWeeklySummaryService
{
    private readonly IGameStateRepository _gameStateRepository;
    private readonly SqliteConnectionFactory _factory;

    public WebWeeklySummaryService(
        IGameStateRepository gameStateRepository,
        SqliteConnectionFactory factory)
    {
        _gameStateRepository = gameStateRepository;
        _factory = factory;
    }

    public async Task<WeeklyAdvanceSummaryVm> BuildAsync()
    {
        var state = await _gameStateRepository.GetAsync()
            ?? throw new InvalidOperationException("GameState not found.");

        using var conn = _factory.CreateConnection();

        int newMessages;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
  AND CreatedDate = $today;";
            cmd.Parameters.AddWithValue("$today", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            newMessages = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        int recentEvents;
        string? headline = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Name FROM Events ORDER BY Id DESC LIMIT 1;";
            var obj = await cmd.ExecuteScalarAsync();
            headline = obj?.ToString();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Events;";
            recentEvents = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return new WeeklyAdvanceSummaryVm(
            state.CurrentDate,
            state.CurrentWeek,
            state.CurrentYear,
            newMessages,
            recentEvents,
            headline);
    }
}
