using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using Microsoft.Data.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Repositories
{
    public sealed class InboxRepository : IInboxRepository
    {
        private readonly ISavePathProvider _savePathProvider;

        public InboxRepository(ISavePathProvider savePathProvider)
        {
            _savePathProvider = savePathProvider;
        }

        public async Task CreateAsync(InboxMessage message, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
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
);";

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

        public async Task<IReadOnlyList<InboxMessage>> ListAsync(
            int agentId,
            string? messageType,
            bool includeArchived,
            bool archivedOnly,
            CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            var result = new List<InboxMessage>();

            if (string.IsNullOrWhiteSpace(dbPath))
                return result;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();

            var sql = @"
SELECT
    Id,
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead,
    COALESCE(IsArchived, 0) AS IsArchived,
    COALESCE(IsDeleted, 0)  AS IsDeleted,
    DeletedAt
FROM InboxMessages
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
";

            if (!string.IsNullOrWhiteSpace(messageType))
            {
                sql += "  AND MessageType = $messageType\n";
                cmd.Parameters.AddWithValue("$messageType", messageType);
            }

            if (archivedOnly)
            {
                sql += "  AND COALESCE(IsArchived, 0) = 1\n";
            }
            else if (!includeArchived)
            {
                sql += "  AND COALESCE(IsArchived, 0) = 0\n";
            }

            sql += "ORDER BY Id DESC;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$agentId", agentId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(MapMessage(reader));
            }

            return result;
        }

        public async Task<InboxMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT
    Id,
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead,
    COALESCE(IsArchived, 0) AS IsArchived,
    COALESCE(IsDeleted, 0)  AS IsDeleted,
    DeletedAt
FROM InboxMessages
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return MapMessage(reader);

            return null;
        }

        public async Task MarkAllReadAsync(int agentId, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsRead = 1
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0;",
                cancellationToken,
                ("$agentId", agentId));
        }

        public async Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsRead = 1
WHERE Id = $id;",
                cancellationToken,
                ("$id", id));
        }

        public async Task ArchiveAsync(int id, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 1
WHERE Id = $id;",
                cancellationToken,
                ("$id", id));
        }

        public async Task RestoreAsync(int id, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 0,
    IsDeleted = 0,
    DeletedAt = NULL
WHERE Id = $id;",
                cancellationToken,
                ("$id", id));
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsDeleted = 1,
    DeletedAt = $deletedAt
WHERE Id = $id;",
                cancellationToken,
                ("$id", id),
                ("$deletedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        public async Task ArchiveReadAsync(int agentId, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsArchived = 1
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsRead, 0) = 1;",
                cancellationToken,
                ("$agentId", agentId));
        }

        public async Task DeleteReadAsync(int agentId, CancellationToken cancellationToken = default)
        {
            await ExecuteNonQueryAsync(@"
UPDATE InboxMessages
SET IsDeleted = 1,
    DeletedAt = $deletedAt
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsRead, 0) = 1;",
                cancellationToken,
                ("$agentId", agentId),
                ("$deletedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        public async Task<int> CountUnreadAsync(int agentId, CancellationToken cancellationToken = default)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                return 0;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(*)
FROM InboxMessages
WHERE AgentId = $agentId
  AND COALESCE(IsDeleted, 0) = 0
  AND COALESCE(IsArchived, 0) = 0
  AND COALESCE(IsRead, 0) = 0;";
            cmd.Parameters.AddWithValue("$agentId", agentId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }

        private async Task ExecuteNonQueryAsync(
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] parameters)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken);

            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static InboxMessage MapMessage(SqliteDataReader reader)
        {
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
    }
}