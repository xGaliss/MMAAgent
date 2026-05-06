namespace MMAAgent.Web.Infrastructure;

public interface ISavePersistenceService
{
    Task<bool> EnsureLocalSaveAvailableAsync(SaveRecord record, CancellationToken cancellationToken = default);
    Task<bool> PersistCurrentSaveAsync(string reason, CancellationToken cancellationToken = default);
    Task DeletePersistedSaveAsync(SaveRecord record, CancellationToken cancellationToken = default);
}

public sealed class SavePersistenceService : ISavePersistenceService
{
    private sealed record PersistedStamp(long FileSizeBytes, DateTime LastWriteTimeUtc);

    private readonly ISaveSessionContext _saveSessionContext;
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly ISaveSnapshotStore _snapshotStore;
    private readonly DatabaseOptions _databaseOptions;
    private readonly Dictionary<string, PersistedStamp> _lastPersisted = new(StringComparer.OrdinalIgnoreCase);

    public SavePersistenceService(
        ISaveSessionContext saveSessionContext,
        ISaveCatalogService saveCatalogService,
        ISaveSnapshotStore snapshotStore,
        Microsoft.Extensions.Options.IOptions<DatabaseOptions> databaseOptions)
    {
        _saveSessionContext = saveSessionContext;
        _saveCatalogService = saveCatalogService;
        _snapshotStore = snapshotStore;
        _databaseOptions = databaseOptions.Value;
    }

    public async Task<bool> EnsureLocalSaveAvailableAsync(SaveRecord record, CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveLocalPath(record, _databaseOptions.SaveRootDirectory);
        if (File.Exists(targetPath))
            return true;

        if (!_snapshotStore.IsEnabled)
            return false;

        var restored = await _snapshotStore.TryRestoreAsync(record.SaveId, targetPath, cancellationToken);
        if (!restored)
            return false;

        await _saveCatalogService.RegisterOrUpdateLocalAsync(
            targetPath,
            record.OwnerUserId,
            record.DisplayName,
            record.TemplateSource,
            markOpened: false,
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<bool> PersistCurrentSaveAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!_snapshotStore.IsEnabled)
            return false;

        if (string.IsNullOrWhiteSpace(_saveSessionContext.CurrentSaveId)
            || string.IsNullOrWhiteSpace(_saveSessionContext.CurrentPath)
            || !File.Exists(_saveSessionContext.CurrentPath))
        {
            return false;
        }

        var record = await _saveCatalogService.GetBySaveIdAsync(_saveSessionContext.CurrentSaveId, cancellationToken);
        if (record is null)
            return false;

        var fileInfo = new FileInfo(_saveSessionContext.CurrentPath);
        var currentStamp = new PersistedStamp(fileInfo.Length, fileInfo.LastWriteTimeUtc);
        if (_lastPersisted.TryGetValue(record.SaveId, out var lastStamp)
            && lastStamp == currentStamp)
        {
            return false;
        }

        await _snapshotStore.UpsertAsync(record, _saveSessionContext.CurrentPath, reason, cancellationToken);
        _lastPersisted[record.SaveId] = currentStamp;
        return true;
    }

    public async Task DeletePersistedSaveAsync(SaveRecord record, CancellationToken cancellationToken = default)
    {
        _lastPersisted.Remove(record.SaveId);
        await _snapshotStore.DeleteAsync(record.SaveId, cancellationToken);
    }

    private static string ResolveLocalPath(SaveRecord record, string? configuredRootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(record.LocalPath))
            return record.LocalPath;

        if (!string.IsNullOrWhiteSpace(record.StorageLocator)
            && string.Equals(record.StorageKind, SaveStorageKinds.LocalSqliteFile, StringComparison.OrdinalIgnoreCase))
        {
            return record.StorageLocator;
        }

        var baseDir = string.IsNullOrWhiteSpace(configuredRootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MMAAgent",
                "Saves")
            : Path.GetFullPath(configuredRootDirectory.Trim());
        Directory.CreateDirectory(baseDir);

        var fileName = string.IsNullOrWhiteSpace(record.FileName)
            ? $"save_{record.SaveId}.db"
            : record.FileName;
        return Path.Combine(baseDir, fileName);
    }
}
