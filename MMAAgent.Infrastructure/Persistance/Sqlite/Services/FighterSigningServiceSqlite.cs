using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Domain.Agents;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class FighterSigningServiceSqlite : IFighterSigningService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IInboxRepository _inboxRepository;

    public FighterSigningServiceSqlite(
        SqliteConnectionFactory factory,
        IAgentProfileRepository agentRepository,
        IInboxRepository inboxRepository)
    {
        _factory = factory;
        _agentRepository = agentRepository;
        _inboxRepository = inboxRepository;
    }

    public async Task<SignFighterResult> AttemptSignAsync(int fighterId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return new SignFighterResult(false, "No active agent profile found.", null, fighterId);

        string inboxSubject;
        string inboxBody;
        bool success;

        using (var conn = _factory.CreateConnection())
        using (var tx = conn.BeginTransaction())
        {
            if (await IsAlreadyManagedAsync(conn, tx, fighterId, cancellationToken))
            {
                tx.Commit();
                return new SignFighterResult(false, "This fighter is already managed.", agent.Id, fighterId);
            }

            var fighter = await LoadFighterAsync(conn, tx, fighterId, cancellationToken);
            if (fighter is null)
            {
                tx.Commit();
                return new SignFighterResult(false, "Fighter not found.", agent.Id, fighterId);
            }

            var chance = 55;
            chance += Math.Max(0, agent.Reputation / 2);
            chance -= fighter.Popularity / 3;
            chance -= fighter.Skill / 5;
            chance += fighter.Potential < 75 ? 8 : 0;
            chance = Math.Clamp(chance, 10, 90);

            var accepted = Random.Shared.Next(1, 101) <= chance;

            if (!accepted)
            {
                inboxSubject = $"Signing rejected: {fighter.Name}";
                inboxBody = $"{fighter.Name} rejected your representation offer.";
                success = false;

                tx.Commit();
            }
            else
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO ManagedFighters
(AgentId, FighterId, SignedDate, ManagementPercent, IsActive)
VALUES
($agentId, $fighterId, $signedDate, $percent, 1);";
                    cmd.Parameters.AddWithValue("$agentId", agent.Id);
                    cmd.Parameters.AddWithValue("$fighterId", fighterId);
                    cmd.Parameters.AddWithValue("$signedDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$percent", 10);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                inboxSubject = $"You signed {fighter.Name}";
                inboxBody = $"{fighter.Name} accepted your representation offer.";
                success = true;

                tx.Commit();
            }
        }

        // Escribir inbox DESPUÉS del commit para evitar database locked
        await _inboxRepository.CreateAsync(new InboxMessage
        {
            AgentId = agent.Id,
            MessageType = "SigningResponse",
            Subject = inboxSubject,
            Body = inboxBody,
            CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            IsRead = false
        });

        return success
            ? new SignFighterResult(true, inboxBody, agent.Id, fighterId)
            : new SignFighterResult(false, inboxBody, agent.Id, fighterId);
    }

    private static async Task<bool> IsAlreadyManagedAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM ManagedFighters WHERE FighterId = $fighterId;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<FighterSnapshot?> LoadFighterAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    (FirstName || ' ' || LastName) AS FighterName,
    Skill,
    Potential,
    Popularity
FROM Fighters
WHERE Id = $fighterId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$fighterId", fighterId);

        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return null;

        return new FighterSnapshot(
            r["FighterName"]?.ToString() ?? "",
            Convert.ToInt32(r["Skill"]),
            Convert.ToInt32(r["Potential"]),
            Convert.ToInt32(r["Popularity"]));
    }

    private sealed record FighterSnapshot(string Name, int Skill, int Potential, int Popularity);
}