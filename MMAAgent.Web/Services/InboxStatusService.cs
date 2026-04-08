using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Web.Services;

public sealed class InboxStatusService : IAsyncDisposable
{
    private readonly IAgentProfileRepository _agentProfileRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IContractOfferRepository _contractOfferRepository;
    private readonly SqliteConnectionFactory _factory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private Task? _pollingTask;
    private bool _started;

    public InboxStatusService(
        IAgentProfileRepository agentProfileRepository,
        IInboxRepository inboxRepository,
        IContractOfferRepository contractOfferRepository,
        SqliteConnectionFactory factory)
    {
        _agentProfileRepository = agentProfileRepository;
        _inboxRepository = inboxRepository;
        _contractOfferRepository = contractOfferRepository;
        _factory = factory;
    }

    public event Action? Changed;

    public int UnreadMessages { get; private set; }
    public int PendingFightOffers { get; private set; }
    public int PendingContractOffers { get; private set; }
    public int AttentionCount => UnreadMessages + PendingFightOffers + PendingContractOffers;
    public bool HasAttention => AttentionCount > 0;

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

            var unreadMessages = 0;
            var pendingFightOffers = 0;
            var pendingContractOffers = 0;

            if (agent is not null)
            {
                unreadMessages = await _inboxRepository.CountUnreadAsync(agent.Id, cancellationToken);
                pendingContractOffers = await _contractOfferRepository.CountPendingByAgentAsync(agent.Id, cancellationToken);
                pendingFightOffers = await CountPendingFightOffersAsync(agent.Id, cancellationToken);
            }

            var changed = unreadMessages != UnreadMessages
                          || pendingFightOffers != PendingFightOffers
                          || pendingContractOffers != PendingContractOffers;

            UnreadMessages = unreadMessages;
            PendingFightOffers = pendingFightOffers;
            PendingContractOffers = pendingContractOffers;

            if (changed)
                Changed?.Invoke();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<int> CountPendingFightOffersAsync(int agentId, CancellationToken cancellationToken)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*)
FROM FightOffers fo
JOIN ManagedFighters mf ON mf.FighterId = fo.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND fo.Status = 'Pending';";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private async Task RunPollingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(cancellationToken);
            }
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
