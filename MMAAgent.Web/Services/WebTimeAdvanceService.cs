using Microsoft.Data.Sqlite;
using MMAAgent.Application;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;
using System.Globalization;

namespace MMAAgent.Web.Services;

public sealed class WebTimeAdvanceService
{
    private readonly IGameStateRepository _gameStateRepository;
    private readonly IDailyWorldEventService _dailyWorldEventService;
    private readonly IWeeklyWorldUpdateService _weeklyWorldUpdateService;
    private readonly IFighterWorldService _fighterWorldService;
    private readonly IWorldAgendaService _worldAgendaService;
    private readonly GameTimeService _gameTimeService;
    private readonly SqliteConnectionFactory _factory;

    public WebTimeAdvanceService(
        IGameStateRepository gameStateRepository,
        IDailyWorldEventService dailyWorldEventService,
        IWeeklyWorldUpdateService weeklyWorldUpdateService,
        IFighterWorldService fighterWorldService,
        IWorldAgendaService worldAgendaService,
        GameTimeService gameTimeService,
        SqliteConnectionFactory factory)
    {
        _gameStateRepository = gameStateRepository;
        _dailyWorldEventService = dailyWorldEventService;
        _weeklyWorldUpdateService = weeklyWorldUpdateService;
        _fighterWorldService = fighterWorldService;
        _worldAgendaService = worldAgendaService;
        _gameTimeService = gameTimeService;
        _factory = factory;
    }

    public async Task<TimeAdvanceResultVm> AdvanceDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days <= 0)
            return new TimeAdvanceResultVm(false, 0, 0, null, null, "Days to advance must be greater than zero.");

        var targetDate = await ResolveTargetDateAsync(days, cancellationToken);
        if (targetDate is null)
            return new TimeAdvanceResultVm(false, 0, 0, null, null, "Game state not found.");

        return await AdvanceToDateAsync(
            targetDate.Value.ToString("yyyy-MM-dd"),
            $"Time jump (+{days} day{(days == 1 ? string.Empty : "s")})",
            cancellationToken);
    }

    public async Task<TimeAdvanceResultVm> AdvanceToNextMilestoneAsync(CancellationToken cancellationToken = default)
    {
        await _worldAgendaService.SynchronizeAsync(cancellationToken);

        var state = await _gameStateRepository.GetAsync();
        if (state is null)
            return new TimeAdvanceResultVm(false, 0, 0, null, null, "Game state not found.");

        var nextMilestone = await LoadNextMilestoneAsync(state.CurrentDate, cancellationToken);
        if (nextMilestone is null)
            return new TimeAdvanceResultVm(false, 0, 0, null, null, "No future milestones available.");

        return await AdvanceToDateAsync(nextMilestone.ScheduledDate, nextMilestone.Headline, cancellationToken);
    }

    private async Task<TimeAdvanceResultVm> AdvanceToDateAsync(
        string targetDateText,
        string? headline,
        CancellationToken cancellationToken)
    {
        var state = await _gameStateRepository.GetAsync();
        if (state is null)
            return new TimeAdvanceResultVm(false, 0, 0, null, null, "Game state not found.");

        var startDate = ParseDate(state.StartDate, fallback: state.CurrentDate);
        var currentDate = ParseDate(state.CurrentDate, fallback: state.CurrentDate);
        var targetDate = ParseDate(targetDateText, fallback: state.CurrentDate);

        if (targetDate <= currentDate)
        {
            return new TimeAdvanceResultVm(
                false,
                0,
                0,
                targetDateText,
                headline,
                $"Next milestone is already due: {headline ?? targetDateText}.");
        }

        var totalDaysAdvanced = 0;
        var totalWeeksProcessed = 0;
        var totalDailyMessages = 0;

        while (currentDate < targetDate)
        {
            var daysIntoWeek = GetDaysIntoWeek(startDate, currentDate);
            var daysToWeekBoundary = 7 - daysIntoWeek;

            await _gameTimeService.AdvanceDaysAsync(1);
            totalDaysAdvanced++;

            state = await _gameStateRepository.GetAsync();
            if (state is null)
                break;

            currentDate = ParseDate(state.CurrentDate, fallback: currentDate.ToString("yyyy-MM-dd"));

            if (daysToWeekBoundary == 1)
            {
                await _weeklyWorldUpdateService.ProcessCurrentWeekAsync(cancellationToken);
                totalWeeksProcessed++;

                state = await _gameStateRepository.GetAsync();
                if (state is null)
                    break;

                currentDate = ParseDate(state.CurrentDate, fallback: currentDate.ToString("yyyy-MM-dd"));
            }

            var dailySummary = await _dailyWorldEventService.ProcessCurrentDayAsync(cancellationToken);
            totalDailyMessages += dailySummary.InboxMessagesCreated;
        }

        await _fighterWorldService.SynchronizeAsync(cancellationToken);
        await _worldAgendaService.SynchronizeAsync(cancellationToken);
        state = await _gameStateRepository.GetAsync();

        var finalDate = state?.CurrentDate ?? targetDateText;
        var dayLabel = totalDaysAdvanced == 1 ? "day" : "days";
        var weekSuffix = totalWeeksProcessed > 0 ? $" ({totalWeeksProcessed} weekly tick{(totalWeeksProcessed == 1 ? string.Empty : "s")})" : string.Empty;
        var eventSuffix = totalDailyMessages > 0 ? $" {totalDailyMessages} notable update{(totalDailyMessages == 1 ? string.Empty : "s")} triggered." : string.Empty;

        return new TimeAdvanceResultVm(
            true,
            totalDaysAdvanced,
            totalWeeksProcessed,
            finalDate,
            headline,
            $"Advanced {totalDaysAdvanced} {dayLabel} to {headline ?? finalDate}.{weekSuffix}{eventSuffix}");
    }

    private async Task<AgendaItemVm?> LoadNextMilestoneAsync(string currentDate, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ScheduledDate, EventType, Headline, Subtitle, Priority
FROM TimeQueue
WHERE COALESCE(Status, 'Pending') = 'Pending'
  AND COALESCE(ScheduledDate, '') > $currentDate
ORDER BY ScheduledDate, Priority DESC, Id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new AgendaItemVm(
            ScheduledDate: reader["ScheduledDate"]?.ToString() ?? "",
            EventType: reader["EventType"]?.ToString() ?? "",
            Headline: reader["Headline"]?.ToString() ?? "",
            Subtitle: reader["Subtitle"] == DBNull.Value ? null : reader["Subtitle"]?.ToString(),
            Priority: Convert.ToInt32(reader["Priority"]));
    }

    private async Task<DateTime?> ResolveTargetDateAsync(int days, CancellationToken cancellationToken)
    {
        var state = await _gameStateRepository.GetAsync();
        if (state is null)
            return null;

        var currentDate = ParseDate(state.CurrentDate, fallback: state.CurrentDate);
        return currentDate.AddDays(days);
    }

    private static DateTime ParseDate(string? value, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }

        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return parsed.Date;
        }

        if (!string.IsNullOrWhiteSpace(fallback) &&
            DateTime.TryParseExact(fallback, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return parsed.Date;
        }

        return DateTime.UtcNow.Date;
    }

    private static int GetDaysIntoWeek(DateTime startDate, DateTime currentDate)
    {
        var totalDays = Math.Max(0, (int)(currentDate.Date - startDate.Date).TotalDays);
        return totalDays % 7;
    }
}
