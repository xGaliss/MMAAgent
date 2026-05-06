using System.Security.Cryptography;
using Npgsql;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Infrastructure;

public sealed record SaveSnapshotInfo(
    string SaveId,
    string ContentSha256,
    long ContentSizeBytes,
    DateTime UploadedUtc,
    string? SourceLocalPath,
    string? SyncReason,
    int Revision);

public interface ISaveSnapshotStore
{
    bool IsEnabled { get; }
    Task<SaveSnapshotInfo?> GetInfoAsync(string saveId, CancellationToken cancellationToken = default);
    Task<SaveSnapshotInfo> UpsertAsync(
        SaveRecord record,
        string sourcePath,
        string? syncReason = null,
        CancellationToken cancellationToken = default);
    Task<bool> TryRestoreAsync(string saveId, string targetPath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string saveId, CancellationToken cancellationToken = default);
}

public sealed class NoopSaveSnapshotStore : ISaveSnapshotStore
{
    public bool IsEnabled => false;

    public Task<SaveSnapshotInfo?> GetInfoAsync(string saveId, CancellationToken cancellationToken = default)
        => Task.FromResult<SaveSnapshotInfo?>(null);

    public Task<SaveSnapshotInfo> UpsertAsync(
        SaveRecord record,
        string sourcePath,
        string? syncReason = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Remote save snapshot persistence is not enabled.");

    public Task<bool> TryRestoreAsync(string saveId, string targetPath, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task DeleteAsync(string saveId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class PostgresSaveSnapshotStore : ISaveSnapshotStore
{
    private readonly SaveCatalogOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _schemaEnsured;
    private string? _activeConnectionString;

    public PostgresSaveSnapshotStore(IOptions<SaveCatalogOptions> options)
    {
        _options = options.Value;
    }

    public bool IsEnabled
        => string.Equals(_options.Provider, SaveCatalogProviders.SupabasePostgres, StringComparison.OrdinalIgnoreCase)
           && (!string.IsNullOrWhiteSpace(_options.PostgresConnectionString)
               || !string.IsNullOrWhiteSpace(_options.FallbackPostgresConnectionString));

    public async Task<SaveSnapshotInfo?> GetInfoAsync(string saveId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(saveId))
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                select save_id, content_sha256, content_size_bytes, uploaded_utc, source_local_path, sync_reason, revision
                from {Qualified("save_snapshots")}
                where lower(save_id) = lower(@saveId)
                limit 1;
                """;
            cmd.Parameters.AddWithValue("saveId", saveId.Trim());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapInfo(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SaveSnapshotInfo> UpsertAsync(
        SaveRecord record,
        string sourcePath,
        string? syncReason = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Remote save snapshot persistence is not enabled.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Save snapshot source file was not found.", sourcePath);

        var bytes = await ReadAllBytesSharedAsync(sourcePath, cancellationToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var uploadedUtc = DateTime.UtcNow;
        var fileInfo = new FileInfo(sourcePath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                insert into {Qualified("save_snapshots")} (
                    save_id,
                    content,
                    content_sha256,
                    content_size_bytes,
                    uploaded_utc,
                    source_local_path,
                    sync_reason,
                    revision
                )
                values (
                    @saveId,
                    @content,
                    @contentSha256,
                    @contentSizeBytes,
                    @uploadedUtc,
                    @sourceLocalPath,
                    @syncReason,
                    1
                )
                on conflict (save_id) do update
                set
                    content = excluded.content,
                    content_sha256 = excluded.content_sha256,
                    content_size_bytes = excluded.content_size_bytes,
                    uploaded_utc = excluded.uploaded_utc,
                    source_local_path = excluded.source_local_path,
                    sync_reason = excluded.sync_reason,
                    revision = {Qualified("save_snapshots")}.revision + 1
                returning save_id, content_sha256, content_size_bytes, uploaded_utc, source_local_path, sync_reason, revision;
                """;
            cmd.Parameters.AddWithValue("saveId", record.SaveId);
            cmd.Parameters.AddWithValue("content", bytes);
            cmd.Parameters.AddWithValue("contentSha256", sha);
            cmd.Parameters.AddWithValue("contentSizeBytes", fileInfo.Length);
            cmd.Parameters.AddWithValue("uploadedUtc", uploadedUtc);
            cmd.Parameters.AddWithValue("sourceLocalPath", sourcePath);
            cmd.Parameters.AddWithValue("syncReason", (object?)syncReason ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return MapInfo(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryRestoreAsync(string saveId, string targetPath, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(saveId))
            return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                select content
                from {Qualified("save_snapshots")}
                where lower(save_id) = lower(@saveId)
                limit 1;
                """;
            cmd.Parameters.AddWithValue("saveId", saveId.Trim());
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result is not byte[] bytes || bytes.Length == 0)
                return false;

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string saveId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(saveId))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                delete from {Qualified("save_snapshots")}
                where lower(save_id) = lower(@saveId);
                """;
            cmd.Parameters.AddWithValue("saveId", saveId.Trim());
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

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            create table if not exists {Qualified("save_snapshots")} (
                save_id text primary key references {Qualified("save_records")}(save_id) on delete cascade,
                content bytea not null,
                content_sha256 text not null,
                content_size_bytes bigint not null,
                uploaded_utc timestamptz not null,
                source_local_path text null,
                sync_reason text null,
                revision integer not null default 1
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _schemaEnsured = true;
    }

    private static SaveSnapshotInfo MapInfo(NpgsqlDataReader reader)
        => new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6));

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
                    $"Failed to open SaveSnapshot Postgres connection using '{candidate.Label}'.", ex));

                if (string.Equals(_activeConnectionString, candidate.ConnectionString, StringComparison.Ordinal))
                    _activeConnectionString = null;
            }
        }

        throw new InvalidOperationException(
            "Failed to open any configured SaveSnapshot Postgres connection.",
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

    private string Qualified(string tableName)
        => $"{GetSchema()}.{QuoteIdentifier(tableName)}";

    private string GetSchema()
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "mma_agent" : _options.Schema.Trim();
        return $"\"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteIdentifier(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static async Task<byte[]> ReadAllBytesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var bytes = new byte[stream.Length];
        var offset = 0;

        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset, bytes.Length - offset), cancellationToken);
            if (read == 0)
                break;

            offset += read;
        }

        return offset == bytes.Length ? bytes : bytes[..offset];
    }
}
