using System.Text.Json;

namespace MMAAgent.Web.Infrastructure;

public static class SaveStorageKinds
{
    public const string LocalSqliteFile = "local-sqlite-file";
    public const string SupabasePostgres = "supabase-postgres";
}

public static class SaveLifecycleStates
{
    public const string Ready = "ready";
    public const string Missing = "missing";
    public const string Provisioning = "provisioning";
    public const string TemplateOnly = "template-only";
    public const string Archived = "archived";
}

public static class SaveTemplateSources
{
    public const string DefaultTemplateDb = "mma-agent-template-db";
}

public sealed record SaveRecord(
    string SaveId,
    string OwnerUserId,
    string DisplayName,
    string StorageKind,
    string StorageLocator,
    string LifecycleState,
    string TemplateSource,
    string? BackendInstance,
    string FileName,
    string? LocalPath,
    DateTime CreatedUtc,
    DateTime LastOpenedUtc,
    DateTime LastWriteTimeUtc,
    long FileSizeBytes);

public interface ISaveCatalogService
{
    Task<SaveRecord> RegisterOrUpdateLocalAsync(
        string path,
        string ownerUserId,
        string? displayName = null,
        string? templateSource = null,
        bool markOpened = false,
        CancellationToken cancellationToken = default);

    Task<SaveRecord?> GetBySaveIdAsync(string saveId, CancellationToken cancellationToken = default);
    Task<SaveRecord?> GetByLocalPathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaveRecord>> ListByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);
    Task<SaveRecord?> GetLastOpenedAsync(string ownerUserId, CancellationToken cancellationToken = default);
    Task RenameLocalPathAsync(string oldPath, string newPath, string ownerUserId, CancellationToken cancellationToken = default);
    Task RemoveByLocalPathAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class JsonSaveCatalogService : ISaveCatalogService
{
    private sealed class SaveCatalogDocument
    {
        public List<SaveRecord> Saves { get; set; } = new();
    }

    private sealed class LegacySaveRegistryDocument
    {
        public List<LegacySaveRegistryEntry> Saves { get; set; } = new();
    }

    private sealed class LegacySaveRegistryEntry
    {
        public string SaveId { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime LastOpenedUtc { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public long FileSizeBytes { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SaveRecord> RegisterOrUpdateLocalAsync(
        string path,
        string ownerUserId,
        string? displayName = null,
        string? templateSource = null,
        bool markOpened = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        var fileInfo = new FileInfo(normalizedPath);
        var utcNow = DateTime.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            var existingIndex = document.Saves.FindIndex(x =>
                string.Equals(x.StorageKind, SaveStorageKinds.LocalSqliteFile, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizePath(x.LocalPath ?? x.StorageLocator), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                var existing = document.Saves[existingIndex];
                var updated = existing with
                {
                    OwnerUserId = ownerUserId,
                    DisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? existing.DisplayName
                        : displayName.Trim(),
                    StorageKind = SaveStorageKinds.LocalSqliteFile,
                    StorageLocator = normalizedPath,
                    LifecycleState = fileInfo.Exists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
                    TemplateSource = string.IsNullOrWhiteSpace(templateSource)
                        ? existing.TemplateSource
                        : templateSource.Trim(),
                    BackendInstance = null,
                    FileName = fileInfo.Name,
                    LocalPath = normalizedPath,
                    LastOpenedUtc = markOpened ? utcNow : existing.LastOpenedUtc,
                    LastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : existing.LastWriteTimeUtc,
                    FileSizeBytes = fileInfo.Exists ? fileInfo.Length : existing.FileSizeBytes
                };

                document.Saves[existingIndex] = updated;
                await WriteAsync(document, cancellationToken);
                return updated;
            }

            var created = new SaveRecord(
                SaveId: Guid.NewGuid().ToString("n"),
                OwnerUserId: ownerUserId,
                DisplayName: string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(fileInfo.Name)
                    : displayName.Trim(),
                StorageKind: SaveStorageKinds.LocalSqliteFile,
                StorageLocator: normalizedPath,
                LifecycleState: fileInfo.Exists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
                TemplateSource: string.IsNullOrWhiteSpace(templateSource)
                    ? SaveTemplateSources.DefaultTemplateDb
                    : templateSource.Trim(),
                BackendInstance: null,
                FileName: fileInfo.Name,
                LocalPath: normalizedPath,
                CreatedUtc: utcNow,
                LastOpenedUtc: markOpened ? utcNow : DateTime.MinValue,
                LastWriteTimeUtc: fileInfo.Exists ? fileInfo.LastWriteTimeUtc : utcNow,
                FileSizeBytes: fileInfo.Exists ? fileInfo.Length : 0);

            document.Saves.Add(created);
            await WriteAsync(document, cancellationToken);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveRecord> MirrorAsync(
        SaveRecord record,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            var existingIndex = document.Saves.FindIndex(x =>
                string.Equals(x.SaveId, record.SaveId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                document.Saves[existingIndex] = record;
            }
            else
            {
                document.Saves.Add(record);
            }

            await WriteAsync(document, cancellationToken);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveRecord?> GetBySaveIdAsync(string saveId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            return document.Saves.FirstOrDefault(x =>
                string.Equals(x.SaveId, saveId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveRecord?> GetByLocalPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalizedPath = NormalizePath(path);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            return document.Saves.FirstOrDefault(x =>
                string.Equals(x.StorageKind, SaveStorageKinds.LocalSqliteFile, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizePath(x.LocalPath ?? x.StorageLocator), normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SaveRecord>> ListByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            return document.Saves
                .Where(x => string.Equals(x.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
                .Select(RefreshComputedState)
                .Where(x => !string.Equals(x.LifecycleState, SaveLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastOpenedUtc == DateTime.MinValue ? x.LastWriteTimeUtc : x.LastOpenedUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveRecord?> GetLastOpenedAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        var entries = await ListByOwnerAsync(ownerUserId, cancellationToken);
        return entries
            .Where(x => string.Equals(x.LifecycleState, SaveLifecycleStates.Ready, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastOpenedUtc == DateTime.MinValue ? x.LastWriteTimeUtc : x.LastOpenedUtc)
            .FirstOrDefault();
    }

    public async Task RenameLocalPathAsync(string oldPath, string newPath, string ownerUserId, CancellationToken cancellationToken = default)
    {
        var existing = await GetByLocalPathAsync(oldPath, cancellationToken);
        if (existing is null)
        {
            await RegisterOrUpdateLocalAsync(newPath, ownerUserId, templateSource: SaveTemplateSources.DefaultTemplateDb, cancellationToken: cancellationToken);
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            var index = document.Saves.FindIndex(x =>
                string.Equals(x.SaveId, existing.SaveId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;

            var fileInfo = new FileInfo(newPath);
            document.Saves[index] = existing with
            {
                OwnerUserId = ownerUserId,
                StorageKind = SaveStorageKinds.LocalSqliteFile,
                StorageLocator = NormalizePath(newPath),
                LifecycleState = fileInfo.Exists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
                FileName = fileInfo.Name,
                LocalPath = NormalizePath(newPath),
                DisplayName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                LastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : existing.LastWriteTimeUtc,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : existing.FileSizeBytes
            };

            await WriteAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveByLocalPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            document.Saves.RemoveAll(x =>
                string.Equals(x.StorageKind, SaveStorageKinds.LocalSqliteFile, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizePath(x.LocalPath ?? x.StorageLocator), normalizedPath, StringComparison.OrdinalIgnoreCase));
            await WriteAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SaveRecord RefreshComputedState(SaveRecord record)
    {
        if (!string.Equals(record.StorageKind, SaveStorageKinds.LocalSqliteFile, StringComparison.OrdinalIgnoreCase))
            return record;

        var path = record.LocalPath ?? record.StorageLocator;
        if (string.IsNullOrWhiteSpace(path))
            return record with { LifecycleState = SaveLifecycleStates.Missing };

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            return record with { LifecycleState = SaveLifecycleStates.Missing };

        return record with
        {
            LifecycleState = SaveLifecycleStates.Ready,
            FileName = fileInfo.Name,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            FileSizeBytes = fileInfo.Length
        };
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim());

    private static string GetCatalogPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MMAAgent");

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "save-catalog.json");
    }

    private static string GetLegacyRegistryPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MMAAgent");

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "save-registry.json");
    }

    private static async Task<SaveCatalogDocument> ReadAsync(CancellationToken cancellationToken)
    {
        var catalogFile = GetCatalogPath();
        if (File.Exists(catalogFile))
        {
            await using var stream = File.OpenRead(catalogFile);
            var document = await JsonSerializer.DeserializeAsync<SaveCatalogDocument>(stream, JsonOptions, cancellationToken);
            return document ?? new SaveCatalogDocument();
        }

        var legacyFile = GetLegacyRegistryPath();
        if (!File.Exists(legacyFile))
            return new SaveCatalogDocument();

        await using var legacyStream = File.OpenRead(legacyFile);
        var legacy = await JsonSerializer.DeserializeAsync<LegacySaveRegistryDocument>(legacyStream, JsonOptions, cancellationToken)
                     ?? new LegacySaveRegistryDocument();

        return new SaveCatalogDocument
        {
            Saves = legacy.Saves
                .Select(MapLegacyEntry)
                .ToList()
        };
    }

    private static SaveRecord MapLegacyEntry(LegacySaveRegistryEntry legacy)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(legacy.Path)
            ? string.Empty
            : NormalizePath(legacy.Path);

        var fileInfo = string.IsNullOrWhiteSpace(normalizedPath) ? null : new FileInfo(normalizedPath);
        var fileExists = fileInfo?.Exists == true;

        return new SaveRecord(
            SaveId: string.IsNullOrWhiteSpace(legacy.SaveId) ? Guid.NewGuid().ToString("n") : legacy.SaveId,
            OwnerUserId: string.IsNullOrWhiteSpace(legacy.OwnerUserId) ? "local:unknown" : legacy.OwnerUserId,
            DisplayName: string.IsNullOrWhiteSpace(legacy.DisplayName)
                ? Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(legacy.FileName) ? normalizedPath : legacy.FileName)
                : legacy.DisplayName,
            StorageKind: SaveStorageKinds.LocalSqliteFile,
            StorageLocator: normalizedPath,
            LifecycleState: fileExists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
            TemplateSource: SaveTemplateSources.DefaultTemplateDb,
            BackendInstance: null,
            FileName: string.IsNullOrWhiteSpace(legacy.FileName)
                ? (fileInfo?.Name ?? string.Empty)
                : legacy.FileName,
            LocalPath: normalizedPath,
            CreatedUtc: legacy.CreatedUtc == default ? DateTime.UtcNow : legacy.CreatedUtc,
            LastOpenedUtc: legacy.LastOpenedUtc,
            LastWriteTimeUtc: fileExists ? fileInfo!.LastWriteTimeUtc : legacy.LastWriteTimeUtc,
            FileSizeBytes: fileExists ? fileInfo!.Length : legacy.FileSizeBytes);
    }

    private static async Task WriteAsync(SaveCatalogDocument document, CancellationToken cancellationToken)
    {
        var file = GetCatalogPath();
        await using var stream = File.Create(file);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }
}
