using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Infrastructure;

public sealed class ConfiguredSaveCatalogService : ISaveCatalogService
{
    private readonly SaveCatalogOptions _options;
    private readonly JsonSaveCatalogService _localCatalog;
    private readonly PostgresSaveCatalogService _postgresCatalog;

    public ConfiguredSaveCatalogService(
        IOptions<SaveCatalogOptions> options,
        JsonSaveCatalogService localCatalog,
        PostgresSaveCatalogService postgresCatalog)
    {
        _options = options.Value;
        _localCatalog = localCatalog;
        _postgresCatalog = postgresCatalog;
    }

    public async Task<SaveRecord> RegisterOrUpdateLocalAsync(
        string path,
        string ownerUserId,
        string? displayName = null,
        string? templateSource = null,
        bool markOpened = false,
        CancellationToken cancellationToken = default)
    {
        if (!UseRemoteCatalog())
        {
            return await _localCatalog.RegisterOrUpdateLocalAsync(
                path,
                ownerUserId,
                displayName,
                templateSource,
                markOpened,
                cancellationToken);
        }

        var remote = await _postgresCatalog.RegisterOrUpdateLocalAsync(
            path,
            ownerUserId,
            displayName,
            templateSource,
            markOpened,
            cancellationToken);

        if (_options.MirrorLocalJson)
        {
            await _localCatalog.MirrorAsync(remote, cancellationToken);
        }

        return remote;
    }

    public async Task<SaveRecord?> GetBySaveIdAsync(string saveId, CancellationToken cancellationToken = default)
    {
        if (!UseRemoteCatalog())
            return await _localCatalog.GetBySaveIdAsync(saveId, cancellationToken);

        return await _postgresCatalog.GetBySaveIdAsync(saveId, cancellationToken)
               ?? await _localCatalog.GetBySaveIdAsync(saveId, cancellationToken);
    }

    public async Task<SaveRecord?> GetByLocalPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!UseRemoteCatalog())
            return await _localCatalog.GetByLocalPathAsync(path, cancellationToken);

        return await _postgresCatalog.GetByLocalPathAsync(path, cancellationToken)
               ?? await _localCatalog.GetByLocalPathAsync(path, cancellationToken);
    }

    public async Task<IReadOnlyList<SaveRecord>> ListByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (!UseRemoteCatalog())
            return await _localCatalog.ListByOwnerAsync(ownerUserId, cancellationToken);

        var remote = await _postgresCatalog.ListByOwnerAsync(ownerUserId, cancellationToken);
        if (remote.Count > 0)
            return remote;

        return await _localCatalog.ListByOwnerAsync(ownerUserId, cancellationToken);
    }

    public async Task<SaveRecord?> GetLastOpenedAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (!UseRemoteCatalog())
            return await _localCatalog.GetLastOpenedAsync(ownerUserId, cancellationToken);

        return await _postgresCatalog.GetLastOpenedAsync(ownerUserId, cancellationToken)
               ?? await _localCatalog.GetLastOpenedAsync(ownerUserId, cancellationToken);
    }

    public async Task RenameLocalPathAsync(string oldPath, string newPath, string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (UseRemoteCatalog())
        {
            await _postgresCatalog.RenameLocalPathAsync(oldPath, newPath, ownerUserId, cancellationToken);
            if (_options.MirrorLocalJson)
                await _localCatalog.RenameLocalPathAsync(oldPath, newPath, ownerUserId, cancellationToken);
            return;
        }

        await _localCatalog.RenameLocalPathAsync(oldPath, newPath, ownerUserId, cancellationToken);
    }

    public async Task RemoveByLocalPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (UseRemoteCatalog())
        {
            await _postgresCatalog.RemoveByLocalPathAsync(path, cancellationToken);
            if (_options.MirrorLocalJson)
                await _localCatalog.RemoveByLocalPathAsync(path, cancellationToken);
            return;
        }

        await _localCatalog.RemoveByLocalPathAsync(path, cancellationToken);
    }

    private bool UseRemoteCatalog()
        => string.Equals(_options.Provider, SaveCatalogProviders.SupabasePostgres, StringComparison.OrdinalIgnoreCase)
           && (!string.IsNullOrWhiteSpace(_options.PostgresConnectionString)
               || !string.IsNullOrWhiteSpace(_options.FallbackPostgresConnectionString));
}
