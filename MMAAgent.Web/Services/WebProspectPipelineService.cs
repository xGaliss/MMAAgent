using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using MMAAgent.Web.Helpers;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebProspectPipelineService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteActionBridge _bridge;
    private readonly WebRosterService _rosterService;

    public WebProspectPipelineService(
        SqliteConnectionFactory factory,
        SqliteActionBridge bridge,
        WebRosterService rosterService)
    {
        _factory = factory;
        _bridge = bridge;
        _rosterService = rosterService;
    }

    public async Task<ProspectPipelineVm> LoadAsync()
    {
        using var conn = _factory.CreateConnection();
        var items = new List<ProspectItemVm>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    f.Id,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    COALESCE(f.Age, 18) AS Age,
    COALESCE(f.WeightClass, '') AS WeightClass,
    COALESCE(c.Name, '') AS CountryName,
    COALESCE(p.Name, 'Free Agent') AS PromotionName,
    COALESCE(f.CareerStage, 'Pro') AS CareerStage,
    COALESCE(p.CircuitType, CASE WHEN COALESCE(f.CareerStage, 'Pro') = 'Amateur' THEN 'Amateur' ELSE 'Professional' END) AS CircuitType,
    COALESCE(f.Wins, 0) AS Wins,
    COALESCE(f.Losses, 0) AS Losses,
    COALESCE(f.Draws, 0) AS Draws,
    COALESCE(f.AmateurWins, 0) AS AmateurWins,
    COALESCE(f.AmateurLosses, 0) AS AmateurLosses,
    COALESCE(f.AmateurDraws, 0) AS AmateurDraws,
    COALESCE(f.Skill, 50) AS Skill,
    COALESCE(f.Potential, 50) AS Potential,
    COALESCE(f.Popularity, 50) AS Popularity,
    COALESCE(f.Marketability, 50) AS Marketability,
    COALESCE(f.Momentum, 50) AS Momentum,
    COALESCE(f.MediaHeat, 20) AS MediaHeat,
    COALESCE(sk.Confidence, 40) AS Confidence,
    COALESCE(sa.Status, '') AS ScoutAssignmentStatus,
    CASE WHEN apw.FighterId IS NULL THEN 0 ELSE 1 END AS IsWatched
FROM Fighters f
LEFT JOIN Countries c ON c.Id = f.CountryId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
LEFT JOIN ScoutKnowledge sk
    ON sk.FighterId = f.Id
   AND sk.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
LEFT JOIN ScoutAssignments sa
    ON sa.FighterId = f.Id
   AND sa.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
   AND sa.Status = 'InProgress'
LEFT JOIN AmateurProspectWatchlist apw
    ON apw.FighterId = f.Id
   AND apw.AgentId = (SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1)
WHERE COALESCE(f.Retired, 0) = 0
  AND (
        COALESCE(f.CareerStage, 'Pro') = 'Amateur'
        OR (
            COALESCE(f.CareerStage, 'Pro') = 'Pro'
            AND (COALESCE(f.AmateurWins, 0) + COALESCE(f.AmateurLosses, 0) + COALESCE(f.AmateurDraws, 0)) > 0
            AND COALESCE(f.Age, 18) <= 27
        )
      )
ORDER BY
    CASE WHEN COALESCE(f.CareerStage, 'Pro') = 'Amateur' THEN 0 ELSE 1 END,
    COALESCE(f.Potential, 50) DESC,
    COALESCE(f.Skill, 50) DESC,
    COALESCE(f.Popularity, 50) DESC,
    f.Id DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var age = Convert.ToInt32(reader["Age"]);
            var skill = Convert.ToInt32(reader["Skill"]);
            var potential = Convert.ToInt32(reader["Potential"]);
            var popularity = Convert.ToInt32(reader["Popularity"]);
            var amateurWins = Convert.ToInt32(reader["AmateurWins"]);
            var amateurLosses = Convert.ToInt32(reader["AmateurLosses"]);
            var amateurDraws = Convert.ToInt32(reader["AmateurDraws"]);
            var amateurTotal = amateurWins + amateurLosses + amateurDraws;
            var careerStage = reader["CareerStage"]?.ToString() ?? "Pro";
            var isReady = string.Equals(careerStage, "Amateur", StringComparison.OrdinalIgnoreCase)
                && IsReadyForProJump(age, skill, potential, popularity, amateurTotal);

            items.Add(new ProspectItemVm(
                Id: Convert.ToInt32(reader["Id"]),
                Name: reader["FighterName"]?.ToString() ?? "",
                Age: age,
                WeightClass: reader["WeightClass"]?.ToString() ?? "",
                CountryName: reader["CountryName"]?.ToString() ?? "",
                CountryFlagUrl: CountryFlagHelper.GetFlagImageUrl(reader["CountryName"]?.ToString()),
                PromotionName: reader["PromotionName"]?.ToString() ?? "Free Agent",
                CareerStage: careerStage,
                CircuitType: reader["CircuitType"]?.ToString() ?? "Professional",
                Wins: Convert.ToInt32(reader["Wins"]),
                Losses: Convert.ToInt32(reader["Losses"]),
                Draws: Convert.ToInt32(reader["Draws"]),
                AmateurWins: amateurWins,
                AmateurLosses: amateurLosses,
                AmateurDraws: amateurDraws,
                Skill: skill,
                Potential: potential,
                Popularity: popularity,
                Marketability: Convert.ToInt32(reader["Marketability"]),
                Momentum: Convert.ToInt32(reader["Momentum"]),
                MediaHeat: Convert.ToInt32(reader["MediaHeat"]),
                ScoutStatus: DescribeScoutStatus(
                    Convert.ToInt32(reader["Confidence"]),
                    reader["ScoutAssignmentStatus"]?.ToString() ?? ""),
                ConfidenceLabel: DescribeConfidence(Convert.ToInt32(reader["Confidence"])),
                IsWatched: Convert.ToInt32(reader["IsWatched"]) == 1,
                IsReadyForProJump: isReady,
                Summary: BuildSummary(
                    careerStage,
                    reader["PromotionName"]?.ToString() ?? "Free Agent",
                    amateurWins,
                    amateurLosses,
                    amateurDraws,
                    skill,
                    potential,
                    popularity,
                    isReady)));
        }

        var watched = items.Where(x => x.IsWatched).OrderByDescending(x => x.IsReadyForProJump).ThenByDescending(x => x.Potential).ThenByDescending(x => x.Skill).Take(12).ToArray();
        var ready = items.Where(x => x.IsReadyForProJump).OrderByDescending(x => x.Potential).ThenByDescending(x => x.Skill).Take(12).ToArray();
        var hotAmateurs = items.Where(x => string.Equals(x.CareerStage, "Amateur", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Potential).ThenByDescending(x => x.Skill).ThenByDescending(x => x.Popularity).Take(18).ToArray();
        var newPros = items.Where(x => string.Equals(x.CareerStage, "Pro", StringComparison.OrdinalIgnoreCase) && (x.AmateurWins + x.AmateurLosses + x.AmateurDraws) > 0 && string.Equals(x.PromotionName, "Free Agent", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Potential).ThenByDescending(x => x.Skill).Take(16).ToArray();

        return new ProspectPipelineVm(
            AmateurCount: items.Count(x => string.Equals(x.CareerStage, "Amateur", StringComparison.OrdinalIgnoreCase)),
            WatchedCount: watched.Length,
            ReadyCount: ready.Length,
            GraduatingProFreeAgentsCount: newPros.Length,
            WatchedProspects: watched,
            ReadyForProJump: ready,
            HotAmateurs: hotAmateurs,
            NewProFreeAgents: newPros);
    }

    public Task StartScoutAsync(int fighterId, string focus = "General")
        => _rosterService.StartScoutAsync(fighterId, focus);

    public Task<bool> SetWatchAsync(int fighterId, bool watch, CancellationToken cancellationToken = default)
        => _bridge.SetProspectWatchAsync(fighterId, watch, cancellationToken);

    private static bool IsReadyForProJump(int age, int skill, int potential, int popularity, int amateurTotal)
        => amateurTotal >= 5
           || (skill >= 58 && potential >= 68 && amateurTotal >= 4)
           || age >= 24
           || popularity >= 56;

    private static string BuildSummary(
        string careerStage,
        string promotionName,
        int amateurWins,
        int amateurLosses,
        int amateurDraws,
        int skill,
        int potential,
        int popularity,
        bool isReady)
    {
        if (string.Equals(careerStage, "Amateur", StringComparison.OrdinalIgnoreCase))
        {
            if (isReady)
                return $"Amateur record {amateurWins}-{amateurLosses}-{amateurDraws}. The profile now reads close to pro-ready.";

            return $"Amateur record {amateurWins}-{amateurLosses}-{amateurDraws}. Skill {skill}, potential {potential}, popularity {popularity}.";
        }

        return $"Now operating as a pro asset. Current market state: {promotionName}. Amateur foundation {amateurWins}-{amateurLosses}-{amateurDraws}.";
    }

    private static string DescribeConfidence(int confidence)
        => confidence switch
        {
            >= 90 => "Very High",
            >= 75 => "High",
            >= 60 => "Medium",
            >= 45 => "Low",
            _ => "Very Low"
        };

    private static string DescribeScoutStatus(int confidence, string assignmentStatus)
    {
        if (string.Equals(assignmentStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
            return "Scouting";

        return confidence switch
        {
            >= 90 => "Known",
            >= 70 => "Tracked",
            _ => "Unscouted"
        };
    }
}
