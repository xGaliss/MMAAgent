using System;
using System.Collections.Generic;
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

        public async Task<int> CreateAsync(InboxMessage message)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO InboxMessages
(
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead
)
VALUES
(
    $agentId,
    $messageType,
    $subject,
    $body,
    $createdDate,
    $isRead
);

SELECT last_insert_rowid();
";

            cmd.Parameters.AddWithValue("$agentId", message.AgentId);
            cmd.Parameters.AddWithValue("$messageType", message.MessageType);
            cmd.Parameters.AddWithValue("$subject", message.Subject);
            cmd.Parameters.AddWithValue("$body", message.Body);
            cmd.Parameters.AddWithValue("$createdDate", message.CreatedDate);
            cmd.Parameters.AddWithValue("$isRead", message.IsRead ? 1 : 0);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<IReadOnlyList<InboxMessage>> GetByAgentAsync(int agentId)
        {
            var dbPath = _savePathProvider.CurrentPath;
            var result = new List<InboxMessage>();

            if (string.IsNullOrWhiteSpace(dbPath))
                return result;

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT Id,
       AgentId,
       MessageType,
       Subject,
       Body,
       CreatedDate,
       IsRead
FROM InboxMessages
WHERE AgentId = $agentId
ORDER BY Id DESC;
";

            cmd.Parameters.AddWithValue("$agentId", agentId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new InboxMessage
                {
                    Id = reader.GetInt32(0),
                    AgentId = reader.GetInt32(1),
                    MessageType = reader.GetString(2),
                    Subject = reader.GetString(3),
                    Body = reader.GetString(4),
                    CreatedDate = reader.GetString(5),
                    IsRead = reader.GetInt32(6) == 1
                });
            }

            return result;
        }

        public async Task MarkAsReadAsync(int messageId)
        {
            var dbPath = _savePathProvider.CurrentPath;
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("No hay DB activa.");

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE InboxMessages
SET IsRead = 1
WHERE Id = $id;
";

            cmd.Parameters.AddWithValue("$id", messageId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}