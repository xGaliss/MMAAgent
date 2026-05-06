using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MMAAgent.Web.Infrastructure;

public sealed class PostgresSaveCatalogService : ISaveCatalogService
{
    private static readonly Regex SchemaNameRegex = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private readonly SaveCatalogOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _schemaEnsured;
    private string? _activeConnectionString;

    public PostgresSaveCatalogService(IOptions<SaveCatalogOptions> options)
    {
        _options = options.Value;
    }

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
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            var existing = await GetByLocalPathAsync(conn, normalizedPath, cancellationToken);

            if (existing is not null)
            {
                var updated = existing with
                {
                    OwnerUserId = ownerUserId,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim(),
                    StorageKind = SaveStorageKinds.LocalSqliteFile,
                    StorageLocator = normalizedPath,
                    LifecycleState = fileInfo.Exists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
                    TemplateSource = string.IsNullOrWhiteSpace(templateSource) ? existing.TemplateSource : templateSource.Trim(),
                    BackendInstance = ResolveBackendInstance(),
                    FileName = fileInfo.Name,
                    LocalPath = normalizedPath,
                    LastOpenedUtc = markOpened ? utcNow : existing.LastOpenedUtc,
                    LastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : existing.LastWriteTimeUtc,
                    FileSizeBytes = fileInfo.Exists ? fileInfo.Length : existing.FileSizeBytes
                };

                await UpsertAsync(conn, updated, cancellationToken);
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
                BackendInstance: ResolveBackendInstance(),
                FileName: fileInfo.Name,
                LocalPath: normalizedPath,
                CreatedUtc: utcNow,
                LastOpenedUtc: markOpened ? utcNow : DateTime.MinValue,
                LastWriteTimeUtc: fileInfo.Exists ? fileInfo.LastWriteTimeUtc : utcNow,
                FileSizeBytes: fileInfo.Exists ? fileInfo.Length : 0);

            await UpsertAsync(conn, created, cancellationToken);
            return created;
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
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            return await GetBySaveIdAsync(conn, saveId.Trim(), cancellationToken);
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
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            return await GetByLocalPathAsync(conn, normalizedPath, cancellationToken);
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
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                select {SelectColumns()}
                from {Qualified("save_records")} r
                join {Qualified("save_ownership")} o on o.save_id = r.save_id and o.is_primary = true
                where lower(o.owner_user_id) = lower(@ownerUserId)
                order by coalesce(nullif(r.last_opened_utc, '-infinity'::timestamptz), r.last_write_time_utc) desc;
                """;
            cmd.Parameters.AddWithValue("ownerUserId", ownerUserId);

            var results = new List<SaveRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var mapped = Map(reader);
                if (!string.Equals(mapped.LifecycleState, SaveLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase))
                    results.Add(RefreshComputedState(mapped));
            }

            return results;
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
            await RegisterOrUpdateLocalAsync(
                newPath,
                ownerUserId,
                templateSource: SaveTemplateSources.DefaultTemplateDb,
                cancellationToken: cancellationToken);
            return;
        }

        var fileInfo = new FileInfo(newPath);
        var updated = existing with
        {
            OwnerUserId = ownerUserId,
            StorageKind = SaveStorageKinds.LocalSqliteFile,
            StorageLocator = NormalizePath(newPath),
            LifecycleState = fileInfo.Exists ? SaveLifecycleStates.Ready : SaveLifecycleStates.Missing,
            FileName = fileInfo.Name,
            LocalPath = NormalizePath(newPath),
            DisplayName = Path.GetFileNameWithoutExtension(fileInfo.Name),
            LastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : existing.LastWriteTimeUtc,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : existing.FileSizeBytes,
            BackendInstance = ResolveBackendInstance()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await UpsertAsync(conn, updated, cancellationToken);
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
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                delete from {Qualified("save_records")}
                where lower(coalesce(local_path, storage_locator)) = lower(@path);
                """;
            cmd.Parameters.AddWithValue("path", normalizedPath);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured)
            return;

        var schema = GetSchema();
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            create schema if not exists {QuoteIdentifier(schema)};

            create table if not exists {Qualified("save_records")} (
                save_id text primary key,
                display_name text not null,
                storage_kind text not null,
                storage_locator text not null,
                lifecycle_state text not null,
                template_source text not null,
                backend_instance text null,
                file_name text not null,
                local_path text null,
                created_utc timestamptz not null,
                last_opened_utc timestamptz not null,
                last_write_time_utc timestamptz not null,
                file_size_bytes bigint not null
            );

            create table if not exists {Qualified("save_ownership")} (
                save_id text not null references {Qualified("save_records")}(save_id) on delete cascade,
                owner_user_id text not null,
                is_primary boolean not null default true,
                assigned_utc timestamptz not null default timezone('utc', now()),
                primary key (save_id, owner_user_id)
            );

            create unique index if not exists ix_save_ownership_primary
                on {Qualified("save_ownership")}(save_id)
                where is_primary = true;

            create index if not exists ix_save_ownership_owner_user_id
                on {Qualified("save_ownership")}(owner_user_id);

            create index if not exists ix_save_records_local_path
                on {Qualified("save_records")}(local_path);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _schemaEnsured = true;
    }

    private async Task<SaveRecord?> GetBySaveIdAsync(NpgsqlConnection conn, string saveId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            select {SelectColumns()}
            from {Qualified("save_records")} r
            left join {Qualified("save_ownership")} o on o.save_id = r.save_id and o.is_primary = true
            where lower(r.save_id) = lower(@saveId)
            limit 1;
            """;
        cmd.Parameters.AddWithValue("saveId", saveId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? RefreshComputedState(Map(reader)) : null;
    }

    private async Task<SaveRecord?> GetByLocalPathAsync(NpgsqlConnection conn, string normalizedPath, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            select {SelectColumns()}
            from {Qualified("save_records")} r
            left join {Qualified("save_ownership")} o on o.save_id = r.save_id and o.is_primary = true
            where lower(r.storage_kind) = lower(@storageKind)
              and lower(coalesce(r.local_path, r.storage_locator)) = lower(@path)
            limit 1;
            """;
        cmd.Parameters.AddWithValue("storageKind", SaveStorageKinds.LocalSqliteFile);
        cmd.Parameters.AddWithValue("path", normalizedPath);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? RefreshComputedState(Map(reader)) : null;
    }

    private async Task UpsertAsync(NpgsqlConnection conn, SaveRecord record, CancellationToken cancellationToken)
    {
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var saveCmd = conn.CreateCommand())
        {
            saveCmd.Transaction = tx;
            saveCmd.CommandText = $"""
                insert into {Qualified("save_records")} (
                    save_id,
                    display_name,
                    storage_kind,
                    storage_locator,
                    lifecycle_state,
                    template_source,
                    backend_instance,
                    file_name,
                    local_path,
                    created_utc,
                    last_opened_utc,
                    last_write_time_utc,
                    file_size_bytes
                )
                values (
                    @saveId,
                    @displayName,
                    @storageKind,
                    @storageLocator,
                    @lifecycleState,
                    @templateSource,
                    @backendInstance,
                    @fileName,
                    @localPath,
                    @createdUtc,
                    @lastOpenedUtc,
                    @lastWriteTimeUtc,
                    @fileSizeBytes
                )
                on conflict (save_id) do update
                set
                    display_name = excluded.display_name,
                    storage_kind = excluded.storage_kind,
                    storage_locator = excluded.storage_locator,
                    lifecycle_state = excluded.lifecycle_state,
                    template_source = excluded.template_source,
                    backend_instance = excluded.backend_instance,
                    file_name = excluded.file_name,
                    local_path = excluded.local_path,
                    last_opened_utc = excluded.last_opened_utc,
                    last_write_time_utc = excluded.last_write_time_utc,
                    file_size_bytes = excluded.file_size_bytes;
                """;

            AddSaveParameters(saveCmd, record);
            await saveCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var ownershipCmd = conn.CreateCommand())
        {
            ownershipCmd.Transaction = tx;
            ownershipCmd.CommandText = $"""
                update {Qualified("save_ownership")}
                set is_primary = false
                where save_id = @saveId;

                insert into {Qualified("save_ownership")} (
                    save_id,
                    owner_user_id,
                    is_primary,
                    assigned_utc
                )
                values (
                    @saveId,
                    @ownerUserId,
                    true,
                    @assignedUtc
                )
                on conflict (save_id, owner_user_id) do update
                set
                    is_primary = true,
                    assigned_utc = excluded.assigned_utc;
                """;
            ownershipCmd.Parameters.AddWithValue("saveId", record.SaveId);
            ownershipCmd.Parameters.AddWithValue("ownerUserId", record.OwnerUserId);
            ownershipCmd.Parameters.AddWithValue("assignedUtc", DateTime.UtcNow);
            await ownershipCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static void AddSaveParameters(NpgsqlCommand cmd, SaveRecord record)
    {
        cmd.Parameters.AddWithValue("saveId", record.SaveId);
        cmd.Parameters.AddWithValue("displayName", record.DisplayName);
        cmd.Parameters.AddWithValue("storageKind", record.StorageKind);
        cmd.Parameters.AddWithValue("storageLocator", record.StorageLocator);
        cmd.Parameters.AddWithValue("lifecycleState", record.LifecycleState);
        cmd.Parameters.AddWithValue("templateSource", record.TemplateSource);
        cmd.Parameters.AddWithValue("backendInstance", (object?)record.BackendInstance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fileName", record.FileName);
        cmd.Parameters.AddWithValue("localPath", (object?)record.LocalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdUtc", record.CreatedUtc);
        cmd.Parameters.AddWithValue("lastOpenedUtc", record.LastOpenedUtc);
        cmd.Parameters.AddWithValue("lastWriteTimeUtc", record.LastWriteTimeUtc);
        cmd.Parameters.AddWithValue("fileSizeBytes", record.FileSizeBytes);
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

    private static SaveRecord Map(NpgsqlDataReader reader)
        => new(
            SaveId: reader.GetString(0),
            OwnerUserId: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            DisplayName: reader.GetString(2),
            StorageKind: reader.GetString(3),
            StorageLocator: reader.GetString(4),
            LifecycleState: reader.GetString(5),
            TemplateSource: reader.GetString(6),
            BackendInstance: reader.IsDBNull(7) ? null : reader.GetString(7),
            FileName: reader.GetString(8),
            LocalPath: reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedUtc: reader.GetDateTime(10),
            LastOpenedUtc: reader.GetDateTime(11),
            LastWriteTimeUtc: reader.GetDateTime(12),
            FileSizeBytes: reader.GetInt64(13));

    private string SelectColumns()
        => """
           r.save_id,
           o.owner_user_id,
           r.display_name,
           r.storage_kind,
           r.storage_locator,
           r.lifecycle_state,
           r.template_source,
           r.backend_instance,
           r.file_name,
           r.local_path,
           r.created_utc,
           r.last_opened_utc,
           r.last_write_time_utc,
           r.file_size_bytes
           """;

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var candidates = BuildConnectionCandidates();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "SaveCatalog requires PostgresConnectionString or FallbackPostgresConnectionString to be configured.");

        List<Exception>? failures = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var normalizedConnectionString = PostgresConnectionStringHelper.Normalize(candidate.ConnectionString);
                var conn = new NpgsqlConnection(normalizedConnectionString);
                await conn.OpenAsync(cancellationToken);
                _activeConnectionString = candidate.ConnectionString;
                return conn;
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException(
                    $"Failed to open SaveCatalog Postgres connection using '{candidate.Label}'.", ex));

                if (string.Equals(_activeConnectionString, candidate.ConnectionString, StringComparison.Ordinal))
                    _activeConnectionString = null;
            }
        }

        throw new InvalidOperationException(
            "Failed to open any configured SaveCatalog Postgres connection.",
            failures is { Count: > 0 } ? new AggregateException(failures) : null);
    }

    private List<(string Label, string ConnectionString)> BuildConnectionCandidates()
    {
        var candidates = new List<(string Label, string ConnectionString)>();
        AddCandidate(candidates, "active", _activeConnectionString);
        AddCandidate(candidates, "primary", _options.PostgresConnectionString);
        AddCandidate(candidates, "fallback", _options.FallbackPostgresConnectionString);
        return candidates;
    }

    private static void AddCandidate(List<(string Label, string ConnectionString)> candidates, string label, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var normalized = connectionString.Trim();
        if (candidates.Any(x => string.Equals(x.ConnectionString, normalized, StringComparison.Ordinal)))
            return;

        candidates.Add((label, normalized));
    }

    private string GetSchema()
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "mma_agent" : _options.Schema.Trim();
        if (!SchemaNameRegex.IsMatch(schema))
            throw new InvalidOperationException($"Invalid SaveCatalog schema name '{schema}'.");

        return schema;
    }

    private string Qualified(string tableName)
        => $"{QuoteIdentifier(GetSchema())}.{QuoteIdentifier(tableName)}";

    private static string QuoteIdentifier(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim());

    private string ResolveBackendInstance()
        => string.IsNullOrWhiteSpace(_options.BackendInstanceName)
            ? SaveCatalogProviders.SupabasePostgres
            : _options.BackendInstanceName!.Trim();
}
