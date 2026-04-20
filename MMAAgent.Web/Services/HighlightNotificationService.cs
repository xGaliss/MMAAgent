using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class HighlightNotificationService : IAsyncDisposable
{
    private static readonly HashSet<string> SpotlightTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CampUpdate",
        "FightWeekNotice",
        "WeighInAlert",
        "FightAftermath",
        "FightOfferShortNotice"
    };

    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly SqliteConnectionFactory _factory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private Task? _pollingTask;
    private bool _started;

    public HighlightNotificationService(
        IAgentProfileRepository agentProfileRepository,
        IInboxRepository inboxRepository,
        SqliteConnectionFactory factory)
    {
        _agentProfileRepository = agentProfileRepository;
        _inboxRepository = inboxRepository;
        _factory = factory;
    }

    public event Action? Changed;

    public IReadOnlyList<HighlightNotificationVm> Items { get; private set; } = Array.Empty<HighlightNotificationVm>();

    public async Task EnsureStartedAsync()
    {
        if (_started)
            return;

        _started = true;
        await RefreshAsync();
        _pollingTask = RunPollingAsync(_cts.Token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            var agent = await _agentProfileRepository.GetAsync();
            var updated = Array.Empty<HighlightNotificationVm>();

            if (agent is not null)
                updated = (await LoadItemsAsync(agent.Id, cancellationToken)).ToArray();

            var changed = Items.Count != updated.Length
                          || Items.Zip(updated, (a, b) => a.Id != b.Id || a.MessageType != b.MessageType).Any(x => x);

            Items = updated;

            if (changed)
                Changed?.Invoke();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task DismissAsync(int messageId, CancellationToken cancellationToken = default)
    {
        await _inboxRepository.MarkAsReadAsync(messageId, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<HighlightNotificationVm>> LoadItemsAsync(int agentId, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, MessageType, Subject, Body, CreatedDate
FROM InboxMessages
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0
  AND COALESCE(IsRead, 0) = 0
  AND MessageType IN ('CampUpdate', 'FightWeekNotice', 'WeighInAlert', 'FightAftermath', 'FightOfferShortNotice')
ORDER BY Id DESC
LIMIT 4;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var list = new List<HighlightNotificationVm>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var messageType = reader["MessageType"]?.ToString() ?? "";
            if (!SpotlightTypes.Contains(messageType))
                continue;

            list.Add(new HighlightNotificationVm(
                Id: Convert.ToInt32(reader["Id"]),
                MessageType: messageType,
                Subject: reader["Subject"]?.ToString() ?? "",
                Body: reader["Body"]?.ToString() ?? "",
                CreatedDate: reader["CreatedDate"]?.ToString() ?? "",
                Tone: ResolveTone(messageType, reader["Subject"]?.ToString(), reader["Body"]?.ToString())));
        }

        return list;
    }

    private static string ResolveTone(string messageType, string? subject, string? body)
    {
        var text = $"{subject} {body}".ToLowerInvariant();

        if (messageType.Equals("WeighInAlert", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("missed weight") || text.Contains("went sideways")))
        {
            return "danger";
        }

        if (messageType.Equals("CampUpdate", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("flying") || text.Contains("clicking") || text.Contains("sharp")))
        {
            return "positive";
        }

        if (text.Contains("hard cut") || text.Contains("took a visible toll") || text.Contains("messy camp"))
            return "warn";

        return messageType switch
        {
            "FightAftermath" => "neutral",
            "FightWeekNotice" => "neutral",
            _ => "neutral"
        };
    }

    private async Task RunPollingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _refreshLock.Dispose();
    }
}
