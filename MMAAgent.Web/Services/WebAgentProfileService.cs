using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebAgentProfileService
{
    private readonly IAgentProfileRepository _agentRepository;
    private readonly IManagedFighterRepository _managedFighterRepository;
    private readonly SqliteConnectionFactory _factory;

    public WebAgentProfileService(
        IAgentProfileRepository agentRepository,
        IManagedFighterRepository managedFighterRepository,
        SqliteConnectionFactory factory)
    {
        _agentRepository = agentRepository;
        _managedFighterRepository = managedFighterRepository;
        _factory = factory;
    }

    public async Task<AgentProfileVm?> LoadAsync()
    {
        var agent = await _agentRepository.GetAsync();
        if (agent == null)
            return null;

        var managed = await _managedFighterRepository.GetByAgentAsync(agent.Id);
        var (campInvestmentLevel, medicalInvestmentLevel) = await LoadInvestmentLevelsAsync(agent.Id);

        return new AgentProfileVm(
            agent.Id,
            agent.Name,
            agent.AgencyName,
            agent.Money,
            agent.Reputation,
            agent.CreatedDate,
            managed.Count,
            campInvestmentLevel,
            medicalInvestmentLevel,
            await LoadTransactionsAsync(agent.Id));
    }

    public async Task UpdateCampInvestmentAsync(int level)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent is null)
            return;

        await UpdateInvestmentLevelAsync(agent.Id, "CampInvestmentLevel", level);
    }

    public async Task UpdateMedicalInvestmentAsync(int level)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent is null)
            return;

        await UpdateInvestmentLevelAsync(agent.Id, "MedicalInvestmentLevel", level);
    }

    private async Task<(int CampInvestmentLevel, int MedicalInvestmentLevel)> LoadInvestmentLevelsAsync(int agentId)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(CampInvestmentLevel, 1) AS CampInvestmentLevel,
    COALESCE(MedicalInvestmentLevel, 1) AS MedicalInvestmentLevel
FROM AgentProfile
WHERE Id = $agentId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (1, 1);

        return (
            Convert.ToInt32(reader["CampInvestmentLevel"]),
            Convert.ToInt32(reader["MedicalInvestmentLevel"]));
    }

    private async Task UpdateInvestmentLevelAsync(int agentId, string columnName, int level)
    {
        var safeLevel = Math.Clamp(level, 0, 2);

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
UPDATE AgentProfile
SET {columnName} = $level
WHERE Id = $agentId;";
        cmd.Parameters.AddWithValue("$level", safeLevel);
        cmd.Parameters.AddWithValue("$agentId", agentId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<AgentTransactionVm>> LoadTransactionsAsync(int agentId)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT TxDate, Amount, TxType, Notes
FROM AgentTransactions
WHERE AgentId = $agentId
ORDER BY Id DESC
LIMIT 12;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var items = new List<AgentTransactionVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new AgentTransactionVm(
                reader["TxDate"]?.ToString() ?? "",
                Convert.ToInt32(reader["Amount"]),
                reader["TxType"]?.ToString() ?? "",
                reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString()));
        }

        return items;
    }
}
