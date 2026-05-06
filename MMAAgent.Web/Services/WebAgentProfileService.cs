using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Helpers;
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
        var levels = await LoadAgentLevelsAsync(agent.Id);

        return new AgentProfileVm(
            agent.Id,
            agent.Name,
            agent.AgencyName,
            string.IsNullOrWhiteSpace(agent.Nationality) ? "Spain" : agent.Nationality,
            CountryFlagHelper.GetFlagImageUrl(agent.Nationality),
            string.IsNullOrWhiteSpace(agent.AvatarKey) ? "Promoter" : agent.AvatarKey,
            agent.Money,
            agent.Reputation,
            agent.PublicReputation,
            agent.FighterTrust,
            agent.PromotionLeverage,
            agent.CreatedDate,
            managed.Count,
            levels.CampInvestmentLevel,
            levels.MedicalInvestmentLevel,
            levels.ScoutingStaffLevel,
            levels.MediaStaffLevel,
            levels.NegotiationStaffLevel,
            levels.PerformanceStaffLevel,
            BuildStaffImpacts(levels),
            await LoadTransactionsAsync(agent.Id),
            await LoadPromotionRelationsAsync(agent.Id));
    }

    public async Task UpdateCampInvestmentAsync(int level) => await UpdateLevelAsync("CampInvestmentLevel", level);
    public async Task UpdateMedicalInvestmentAsync(int level) => await UpdateLevelAsync("MedicalInvestmentLevel", level);
    public async Task UpdateScoutingStaffAsync(int level) => await UpdateLevelAsync("ScoutingStaffLevel", level);
    public async Task UpdateMediaStaffAsync(int level) => await UpdateLevelAsync("MediaStaffLevel", level);
    public async Task UpdateNegotiationStaffAsync(int level) => await UpdateLevelAsync("NegotiationStaffLevel", level);
    public async Task UpdatePerformanceStaffAsync(int level) => await UpdateLevelAsync("PerformanceStaffLevel", level);

    private async Task UpdateLevelAsync(string columnName, int level)
    {
        var agent = await _agentRepository.GetAsync();
        if (agent is null)
            return;

        var safeLevel = Math.Clamp(level, 0, 2);
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
UPDATE AgentProfile
SET {columnName} = $level
WHERE Id = $agentId;";
        cmd.Parameters.AddWithValue("$level", safeLevel);
        cmd.Parameters.AddWithValue("$agentId", agent.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(int CampInvestmentLevel, int MedicalInvestmentLevel, int ScoutingStaffLevel, int MediaStaffLevel, int NegotiationStaffLevel, int PerformanceStaffLevel)> LoadAgentLevelsAsync(int agentId)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(CampInvestmentLevel, 1) AS CampInvestmentLevel,
    COALESCE(MedicalInvestmentLevel, 1) AS MedicalInvestmentLevel,
    COALESCE(ScoutingStaffLevel, 1) AS ScoutingStaffLevel,
    COALESCE(MediaStaffLevel, 1) AS MediaStaffLevel,
    COALESCE(NegotiationStaffLevel, 1) AS NegotiationStaffLevel,
    COALESCE(PerformanceStaffLevel, 1) AS PerformanceStaffLevel
FROM AgentProfile
WHERE Id = $agentId
LIMIT 1;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (1, 1, 1, 1, 1, 1);

        return (
            Convert.ToInt32(reader["CampInvestmentLevel"]),
            Convert.ToInt32(reader["MedicalInvestmentLevel"]),
            Convert.ToInt32(reader["ScoutingStaffLevel"]),
            Convert.ToInt32(reader["MediaStaffLevel"]),
            Convert.ToInt32(reader["NegotiationStaffLevel"]),
            Convert.ToInt32(reader["PerformanceStaffLevel"]));
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

    private async Task<IReadOnlyList<AgentPromotionRelationVm>> LoadPromotionRelationsAsync(int agentId)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    apr.PromotionId,
    COALESCE(p.Name, 'Promotion') AS PromotionName,
    COALESCE(apr.RelationshipScore, 50) AS RelationshipScore,
    COALESCE(apr.LastUpdatedDate, '') AS LastUpdatedDate,
    apr.Notes
FROM AgentPromotionRelations apr
LEFT JOIN Promotions p ON p.Id = apr.PromotionId
WHERE apr.AgentId = $agentId
ORDER BY COALESCE(apr.RelationshipScore, 50) DESC, PromotionName
LIMIT 8;";
        cmd.Parameters.AddWithValue("$agentId", agentId);

        var items = new List<AgentPromotionRelationVm>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var score = Convert.ToInt32(reader["RelationshipScore"]);
            items.Add(new AgentPromotionRelationVm(
                Convert.ToInt32(reader["PromotionId"]),
                reader["PromotionName"]?.ToString() ?? "Promotion",
                score,
                score switch
                {
                    >= 78 => "Strong",
                    >= 62 => "Good",
                    >= 45 => "Neutral",
                    >= 30 => "Shaky",
                    _ => "Cold"
                },
                reader["LastUpdatedDate"]?.ToString() ?? "",
                reader["Notes"] == DBNull.Value ? null : reader["Notes"]?.ToString()));
        }

        return items;
    }

    private static IReadOnlyList<StaffImpactVm> BuildStaffImpacts(
        (int CampInvestmentLevel, int MedicalInvestmentLevel, int ScoutingStaffLevel, int MediaStaffLevel, int NegotiationStaffLevel, int PerformanceStaffLevel) levels)
        => new[]
        {
            new StaffImpactVm(
                "Camp Investment",
                FormatLevel(levels.CampInvestmentLevel),
                $"+{(levels.CampInvestmentLevel * 4) + (levels.PerformanceStaffLevel * 3)} prep quality into live camps.",
                levels.CampInvestmentLevel switch
                {
                    2 => "More excellent camps and fewer flat openings when the room is healthy.",
                    0 => "Cheap weeks, but prep swings harder when the camp gets messy.",
                    _ => "Balanced support that keeps the room steady without premium overhead."
                },
                "Feeds sharper camp starts and steadier fight-week arrivals."),
            new StaffImpactVm(
                "Medical Support",
                FormatLevel(levels.MedicalInvestmentLevel),
                $"+{(levels.MedicalInvestmentLevel * 4) + (levels.PerformanceStaffLevel * 3)} cut safety and body management.",
                levels.MedicalInvestmentLevel switch
                {
                    2 => "Better protection against rough cuts, injury spirals and late-week chaos.",
                    0 => "Lower burn rate, but a bad cut or scare can snowball faster.",
                    _ => "A stable middle lane for cuts, recovery and fight-week risk."
                },
                "Most visible around weigh-ins, recovery windows and medical volatility."),
            new StaffImpactVm(
                "Scouting Department",
                FormatLevel(levels.ScoutingStaffLevel),
                $"+{levels.ScoutingStaffLevel + 1} scouting progress each day.",
                levels.ScoutingStaffLevel switch
                {
                    2 => "Reports tighten quickly and prospect reads land sooner.",
                    0 => "Assignments crawl more slowly, especially on deeper reads.",
                    _ => "Healthy pace for general reads without elite overhead."
                },
                "Turns into cleaner ranges, better confidence and faster pipeline discovery."),
            new StaffImpactVm(
                "PR Team",
                FormatLevel(levels.MediaStaffLevel),
                levels.MediaStaffLevel switch
                {
                    2 => "High media churn: more sponsor spots, press asks and controlled buzz.",
                    0 => "Low media churn: fewer commercial angles, but less weekly noise.",
                    _ => "Medium media churn: enough visibility without flooding the room."
                },
                "Stronger PR helps convert showmen into money and keeps headlines warmer.",
                "Shows up most in sponsor decisions, interviews and marketability growth."),
            new StaffImpactVm(
                "Negotiation Team",
                FormatLevel(levels.NegotiationStaffLevel),
                $"+{levels.NegotiationStaffLevel * 3} edge in contract talks and promotion asks.",
                levels.NegotiationStaffLevel switch
                {
                    2 => "You can push harder for money, security and leverage without burning as much trust.",
                    0 => "You save money, but promotions stonewall more often when you reach.",
                    _ => "A steady middle lane for most deals and matchmaking pressure."
                },
                "Also helps when asking for title shots, eliminators and bigger opportunities."),
            new StaffImpactVm(
                "Performance Staff",
                FormatLevel(levels.PerformanceStaffLevel),
                $"+{levels.PerformanceStaffLevel * 3} support layered into prep quality and cut safety.",
                levels.PerformanceStaffLevel switch
                {
                    2 => "Matchup prep lands cleaner and camp focus choices pay off more often.",
                    0 => "The room still functions, but focused camps and body management lose bite.",
                    _ => "Solid transfer from planning into actual fight-week execution."
                },
                "Most visible in camps, weigh-ins and how well the game plan survives pressure.")
        };

    private static string FormatLevel(int level)
        => level switch
        {
            2 => "Premium",
            0 => "Lean",
            _ => "Balanced"
        };
}
