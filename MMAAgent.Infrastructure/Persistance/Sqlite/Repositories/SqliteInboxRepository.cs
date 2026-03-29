using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Repositories;

public sealed class SqliteInboxRepository : IInboxRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteInboxRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task CreateAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO InboxMessages
(
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead,
    IsArchived,
    IsDeleted,
    DeletedAt
)
VALUES
(
    $agentId,
    $messageType,
    $subject,
    $body,
    $createdDate,
    $isRead,
    $isArchived,
    $isDeleted,
    $deletedAt
);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$agentId", message.AgentId);
        cmd.Parameters.AddWithValue("$messageType", message.MessageType);
        cmd.Parameters.AddWithValue("$subject", message.Subject);
        cmd.Parameters.AddWithValue("$body", message.Body);
        cmd.Parameters.AddWithValue("$createdDate", message.CreatedDate);
        cmd.Parameters.AddWithValue("$isRead", message.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("$isArchived", message.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$isDeleted", message.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$deletedAt", (object?)message.DeletedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InboxMessage>> ListAsync(int agentId, string? messageType = null, bool includeArchived = false, bool archivedOnly = false, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        var filters = new List<string>
        {
            "AgentId = $agentId",
            "COALESCE(IsDeleted, 0) = 0"
        };

        if (!string.IsNullOrWhiteSpace(messageType))
            filters.Add("MessageType = $messageType");

        if (archivedOnly)
            filters.Add("COALESCE(IsArchived, 0) = 1");
        else if (!includeArchived)
            filters.Add("COALESCE(IsArchived, 0) = 0");

        cmd.CommandText = $@"
SELECT Id, AgentId, MessageType, Subject, Body, CreatedDate,
       COALESCE(IsRead, 0) AS IsRead,
       COALESCE(IsArchived, 0) AS IsArchived,
       COALESCE(IsDeleted, 0) AS IsDeleted,
       DeletedAt
FROM InboxMessages
WHERE {string.Join(" AND ", filters)}
ORDER BY Id DESC;";

        cmd.Parameters.AddWithValue("$agentId", agentId);
        if (!string.IsNullOrWhiteSpace(messageType))
            cmd.Parameters.AddWithValue("$messageType", messageType);

        var list = new List<InboxMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new InboxMessage
            {
                Id = Convert.ToInt32(reader["Id"]),
                AgentId = Convert.ToInt32(reader["AgentId"]),
                MessageType = reader["MessageType"]?.ToString() ?? "",
                Subject = reader["Subject"]?.ToString() ?? "",
                Body = reader["Body"]?.ToString() ?? "",
                CreatedDate = reader["CreatedDate"]?.ToString() ?? "",
                IsRead = Convert.ToInt32(reader["IsRead"]) == 1,
                IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1,
                IsDeleted = Convert.ToInt32(reader["IsDeleted"]) == 1,
                DeletedAt = reader["DeletedAt"] == DBNull.Value ? null : reader["DeletedAt"]?.ToString()
            });
        }

        return list;
    }

    public async Task<InboxMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, AgentId, MessageType, Subject, Body, CreatedDate,
       COALESCE(IsRead, 0) AS IsRead,
       COALESCE(IsArchived, 0) AS IsArchived,
       COALESCE(IsDeleted, 0) AS IsDeleted,
       DeletedAt
FROM InboxMessages
WHERE Id = $id
LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new InboxMessage
        {
            Id = Convert.ToInt32(reader["Id"]),
            AgentId = Convert.ToInt32(reader["AgentId"]),
            MessageType = reader["MessageType"]?.ToString() ?? "",
            Subject = reader["Subject"]?.ToString() ?? "",
            Body = reader["Body"]?.ToString() ?? "",
            CreatedDate = reader["CreatedDate"]?.ToString() ?? "",
            IsRead = Convert.ToInt32(reader["IsRead"]) == 1,
            IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1,
            IsDeleted = Convert.ToInt32(reader["IsDeleted"]) == 1,
            DeletedAt = reader["DeletedAt"] == DBNull.Value ? null : reader["DeletedAt"]?.ToString()
        };
    }

    public Task MarkAllReadAsync(int agentId, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsRead = 1
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0;", ("$agentId", agentId), cancellationToken);

    public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsRead = 1
WHERE Id = $id;", ("$id", id), cancellationToken);

    public Task ArchiveAsync(int id, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 1
WHERE Id = $id
  AND COALESCE(IsDeleted, 0) = 0;", ("$id", id), cancellationToken);

    public Task RestoreAsync(int id, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 0
WHERE Id = $id
  AND COALESCE(IsDeleted, 0) = 0;", ("$id", id), cancellationToken);

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsDeleted = 1,
    DeletedAt = $deletedAt
WHERE Id = $id;", ("$id", id), ("$deletedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")), cancellationToken);

    public Task ArchiveReadAsync(int agentId, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 1
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsRead, 0) = 1;", ("$agentId", agentId), cancellationToken);

    public Task DeleteReadAsync(int agentId, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsDeleted = 1,
    DeletedAt = $deletedAt
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsRead, 0) = 1;", ("$agentId", agentId), ("$deletedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")), cancellationToken);

    public async Task<int> CountUnreadAsync(int agentId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0
  AND COALESCE(IsRead, 0) = 0;";
        cmd.Parameters.AddWithValue("$agentId", agentId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ExecuteNonQueryAsync(string sql, params object[] args)
    {
        var cancellationToken = CancellationToken.None;
        if (args.LastOrDefault() is CancellationToken ct)
        {
            cancellationToken = ct;
            args = args.Take(args.Length - 1).ToArray();
        }

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var arg in args)
        {
            var tuple = ((string Name, object? Value))arg;
            cmd.Parameters.AddWithValue(tuple.Name, tuple.Value ?? DBNull.Value);
        }
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
