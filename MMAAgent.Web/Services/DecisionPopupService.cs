using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class DecisionPopupService : IAsyncDisposable
{
    private readonly WebInboxService _inboxService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<int> _dismissedIds = new();

    private Task? _pollingTask;
    private bool _started;

    public DecisionPopupService(WebInboxService inboxService)
    {
        _inboxService = inboxService;
    }

    public event Action? Changed;

    public IReadOnlyList<DecisionEventVm> PendingItems { get; private set; } = Array.Empty<DecisionEventVm>();

    public DecisionEventVm? ActiveDecision { get; private set; }

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
            var pending = (await _inboxService.LoadPendingDecisionsAsync()).ToArray();

            _dismissedIds.RemoveWhere(id => pending.All(x => x.Id != id));
            PendingItems = pending;

            if (ActiveDecision is not null && pending.All(x => x.Id != ActiveDecision.Id))
                ActiveDecision = null;

            if (ActiveDecision is null)
                ActiveDecision = pending.FirstOrDefault(x => !_dismissedIds.Contains(x.Id));

            Changed?.Invoke();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task DismissAsync(int decisionId, CancellationToken cancellationToken = default)
    {
        _dismissedIds.Add(decisionId);
        if (ActiveDecision?.Id == decisionId)
            ActiveDecision = PendingItems.FirstOrDefault(x => x.Id != decisionId && !_dismissedIds.Contains(x.Id));

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async Task ResolveAsync(int decisionId, string optionKey, CancellationToken cancellationToken = default)
    {
        await _inboxService.ResolveDecisionAsync(decisionId, optionKey);
        _dismissedIds.Remove(decisionId);
        await RefreshAsync(cancellationToken);
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
