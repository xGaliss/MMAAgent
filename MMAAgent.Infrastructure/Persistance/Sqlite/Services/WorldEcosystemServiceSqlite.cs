using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class WorldEcosystemServiceSqlite
{
    private readonly SqliteConnectionFactory _factory;

    public WorldEcosystemServiceSqlite(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);
        var agentId = await LoadPrimaryAgentIdAsync(conn, tx, cancellationToken);

        await RecalculateFighterSignalsAsync(conn, tx, currentDate, cancellationToken);
        await RebuildRivalriesAsync(conn, tx, currentDate, cancellationToken);
        await RebuildStorylinesAsync(conn, tx, currentDate, cancellationToken);
        await RebuildLegacyTagsAsync(conn, tx, currentDate, cancellationToken);
        await RebuildContenderQueueAsync(conn, tx, currentDate, cancellationToken);
        if (agentId is int relationAgentId)
            await SynchronizePromotionRelationsAsync(conn, tx, relationAgentId, currentDate, cancellationToken);

        if (agentId is int resolvedAgentId)
            await RebuildScoutKnowledgeAsync(conn, tx, resolvedAgentId, currentDate, cancellationToken);

        tx.Commit();
    }

    public async Task<AnnualWorldShiftSummary> ApplyAnnualEvolutionAsync(int currentYear, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET Age = COALESCE(Age, 18) + 1,
    LastAgedYear = $year
WHERE COALESCE(LastAgedYear, 0) < $year;", cancellationToken,
            ("$year", currentYear));

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET
    Skill = MIN(99, MAX(10, COALESCE(Skill, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27)
                THEN MIN(3, MAX(0, CAST(ROUND((COALESCE(Potential, 50) - COALESCE(Skill, 50)) / 12.0) AS INTEGER)))
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32)
                THEN CASE
                    WHEN COALESCE(DamageMiles, 0) >= 36 THEN -1
                    WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 10 THEN 1
                    ELSE 0
                END
            ELSE -(
                1
                + CASE WHEN COALESCE(Age, 18) - COALESCE(PrimeAgeEnd, 32) >= 3 THEN 1 ELSE 0 END
                + CASE WHEN COALESCE(DamageMiles, 0) >= 28 THEN 1 ELSE 0 END
            )
        END
    ))),
    Striking = MIN(99, MAX(10, COALESCE(Striking, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 1 + CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 14 THEN 1 ELSE 0 END
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 12 THEN 1 ELSE 0 END
            ELSE -(1 + CASE WHEN COALESCE(DamageMiles, 0) >= 30 THEN 1 ELSE 0 END)
        END
    ))),
    Grappling = MIN(99, MAX(10, COALESCE(Grappling, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 1 + CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 14 THEN 1 ELSE 0 END
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 12 THEN 1 ELSE 0 END
            ELSE -(1 + CASE WHEN COALESCE(DamageMiles, 0) >= 34 THEN 1 ELSE 0 END)
        END
    ))),
    Wrestling = MIN(99, MAX(10, COALESCE(Wrestling, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 1 + CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 14 THEN 1 ELSE 0 END
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN CASE WHEN COALESCE(Potential, 50) - COALESCE(Skill, 50) >= 12 THEN 1 ELSE 0 END
            ELSE -(1 + CASE WHEN COALESCE(DamageMiles, 0) >= 34 THEN 1 ELSE 0 END)
        END
    ))),
    Cardio = MIN(99, MAX(10, COALESCE(Cardio, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 1
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN 0
            ELSE -(1 + CASE WHEN COALESCE(Age, 18) - COALESCE(PrimeAgeEnd, 32) >= 4 THEN 1 ELSE 0 END)
        END
    ))),
    Chin = MIN(99, MAX(10, COALESCE(Chin, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 0
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN CASE WHEN COALESCE(DamageMiles, 0) >= 24 THEN -1 ELSE 0 END
            ELSE -(1 + CASE WHEN COALESCE(DamageMiles, 0) >= 20 THEN 1 ELSE 0 END)
        END
    ))),
    FightIQ = MIN(99, MAX(10, COALESCE(FightIQ, 50) + (
        CASE
            WHEN COALESCE(Age, 18) < COALESCE(PrimeAgeStart, 27) THEN 1
            WHEN COALESCE(Age, 18) <= COALESCE(PrimeAgeEnd, 32) THEN 1
            ELSE CASE WHEN COALESCE(Age, 18) - COALESCE(PrimeAgeEnd, 32) >= 6 THEN -1 ELSE 0 END
        END
    ))),
    Popularity = MIN(100, MAX(0,
        COALESCE(Popularity, 50)
        + CASE
            WHEN COALESCE(Age, 18) < 24 AND COALESCE(Momentum, 50) >= 65 THEN 1
            WHEN COALESCE(Age, 18) > 36 AND COALESCE(Momentum, 50) < 45 THEN -1
            ELSE 0
        END
    )),
    Marketability = MIN(99, MAX(10,
        COALESCE(Marketability, 50)
        + CASE
            WHEN COALESCE(Age, 18) < 25 AND COALESCE(Momentum, 50) >= 65 THEN 2
            WHEN COALESCE(Age, 18) >= 34 AND COALESCE(DamageMiles, 0) >= 26 THEN -2
            ELSE 0
        END
    )),
    Momentum = MIN(99, MAX(5,
        COALESCE(Momentum, 50)
        + CASE
            WHEN COALESCE(Age, 18) < 25 AND COALESCE(Potential, 50) > COALESCE(Skill, 50) THEN 1
            WHEN COALESCE(Age, 18) > 35 AND COALESCE(DamageMiles, 0) >= 30 THEN -2
            ELSE 0
        END
    )),
    ReliabilityScore = MIN(99, MAX(15,
        COALESCE(ReliabilityScore, 60)
        + CASE
            WHEN COALESCE(Age, 18) < 25 AND COALESCE(Momentum, 50) >= 65 THEN 2
            WHEN COALESCE(Age, 18) >= 35 AND COALESCE(DamageMiles, 0) >= 26 THEN -4
            ELSE 0
        END
    )),
    MediaHeat = MIN(99, MAX(5,
        COALESCE(MediaHeat, 20)
        + CASE
            WHEN COALESCE(Age, 18) < 25 AND COALESCE(Momentum, 50) >= 68 THEN 4
            WHEN COALESCE(Age, 18) >= 36 AND COALESCE(Momentum, 50) < 45 THEN -4
            ELSE 0
        END
    ));", cancellationToken,
            ("$year", currentYear));

        var veteranDeclineCases = await ExecWithCountAsync(conn, tx, @"
UPDATE Fighters
SET Skill = MAX(10, COALESCE(Skill, 50) - CASE WHEN COALESCE(Age, 18) >= 38 THEN 2 ELSE 1 END),
    Striking = MAX(10, COALESCE(Striking, 50) - 1),
    Grappling = MAX(10, COALESCE(Grappling, 50) - CASE WHEN COALESCE(DamageMiles, 0) >= 30 THEN 1 ELSE 0 END),
    Wrestling = MAX(10, COALESCE(Wrestling, 50) - CASE WHEN COALESCE(DamageMiles, 0) >= 30 THEN 1 ELSE 0 END),
    Cardio = MAX(10, COALESCE(Cardio, 50) - 1),
    Chin = MAX(10, COALESCE(Chin, 50) - CASE WHEN COALESCE(DamageMiles, 0) >= 32 THEN 2 ELSE 1 END),
    Popularity = MAX(0, COALESCE(Popularity, 50) - 2),
    Marketability = MAX(10, COALESCE(Marketability, 50) - 3),
    Momentum = MAX(5, COALESCE(Momentum, 50) - 4),
    ReliabilityScore = MAX(15, COALESCE(ReliabilityScore, 60) - 5),
    MediaHeat = MAX(5, COALESCE(MediaHeat, 20) - 4)
WHERE COALESCE(Retired, 0) = 0
  AND COALESCE(Age, 18) >= 35
  AND COALESCE(DamageMiles, 0) >= 24
  AND (COALESCE(Momentum, 50) < 55 OR COALESCE(Popularity, 50) < 65);", cancellationToken);

        var retirements = await ExecWithCountAsync(conn, tx, @"
UPDATE Fighters
SET Retired = 1,
    PromotionId = NULL,
    Salary = 0,
    ContractFightsRemaining = 0,
    TotalFightsInContract = 0,
    ContractStatus = 'Retired',
    IsBooked = 0
WHERE COALESCE(Retired, 0) = 0
  AND (
      COALESCE(Age, 18) >= 41
      OR (
          COALESCE(Age, 18) >= 38
          AND COALESCE(DamageMiles, 0) >= 30
          AND COALESCE(Momentum, 50) <= 48
      )
      OR (
          COALESCE(Age, 18) >= 36
          AND COALESCE(DamageMiles, 0) >= 38
          AND COALESCE(Popularity, 50) <= 42
      )
  );", cancellationToken);

        await ExecAsync(conn, tx, @"
UPDATE Titles
SET ChampionFighterId = NULL
WHERE COALESCE(ChampionFighterId, 0) IN
(
    SELECT Id
    FROM Fighters
    WHERE COALESCE(Retired, 0) = 1
);", cancellationToken);

        var reshuffledDivisions = await RebuildAnnualRankingsAsync(conn, tx, cancellationToken);

        tx.Commit();
        return new AnnualWorldShiftSummary(retirements, veteranDeclineCases, reshuffledDivisions);
    }

    public async Task<int> PromoteAnnualProspectClassAsync(int newcomerCount, CancellationToken cancellationToken = default)
    {
        if (newcomerCount <= 0)
            return 0;

        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var prospectDebuts = await ExecWithCountAsync(conn, tx, @"
WITH RecentFreeAgents AS
(
    SELECT Id
    FROM Fighters
    WHERE COALESCE(Retired, 0) = 0
      AND PromotionId IS NULL
      AND COALESCE(ContractStatus, '') = 'FreeAgent'
      AND COALESCE(Age, 18) <= 23
    ORDER BY Id DESC
    LIMIT $recentPool
),
ProspectClass AS
(
    SELECT f.Id
    FROM Fighters f
    JOIN RecentFreeAgents rfa ON rfa.Id = f.Id
    ORDER BY
        (COALESCE(f.Potential, 50) - COALESCE(f.Skill, 50)) DESC,
        COALESCE(f.Skill, 50) DESC,
        COALESCE(f.Popularity, 50) DESC,
        f.Id DESC
    LIMIT $prospectCount
)
UPDATE Fighters
SET Popularity = MIN(100, COALESCE(Popularity, 50) + 4),
    Marketability = MIN(99, COALESCE(Marketability, 50) + 5),
    Momentum = MIN(99, COALESCE(Momentum, 50) + 8),
    MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 6),
    ReliabilityScore = MIN(99, COALESCE(ReliabilityScore, 60) + 2)
WHERE Id IN (SELECT Id FROM ProspectClass);", cancellationToken,
            ("$recentPool", Math.Max(newcomerCount, 12)),
            ("$prospectCount", Math.Max(2, Math.Min(6, newcomerCount / 8))));

        tx.Commit();
        return prospectDebuts;
    }

    public async Task<int> AdvanceAmateurCircuitAsync(string currentDate, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var graduates = await ExecWithCountAsync(conn, tx, @"
UPDATE Fighters
SET CareerStage = 'Pro',
    PromotionId = NULL,
    Salary = 0,
    ContractFightsRemaining = 0,
    TotalFightsInContract = 0,
    ContractStatus = 'FreeAgent',
    NegotiationTurnsRemaining = 0,
    Marketability = MIN(99, COALESCE(Marketability, 50) + 2),
    Momentum = MIN(99, COALESCE(Momentum, 50) + 4),
    Popularity = MIN(100, COALESCE(Popularity, 50) + 3),
    MediaHeat = MIN(99, COALESCE(MediaHeat, 20) + 2)
WHERE COALESCE(Retired, 0) = 0
  AND COALESCE(CareerStage, 'Pro') = 'Amateur'
  AND (
      (
          COALESCE(AmateurWins, COALESCE(Wins, 0)) + COALESCE(AmateurLosses, COALESCE(Losses, 0)) + COALESCE(AmateurDraws, COALESCE(Draws, 0))
      ) >= 6
      OR (
          COALESCE(Skill, 50) >= 58
          AND COALESCE(Potential, 50) >= 68
          AND (
              COALESCE(AmateurWins, COALESCE(Wins, 0)) + COALESCE(AmateurLosses, COALESCE(Losses, 0)) + COALESCE(AmateurDraws, COALESCE(Draws, 0))
          ) >= 4
      )
      OR COALESCE(Age, 18) >= 24
      OR COALESCE(Popularity, 50) >= 56
  );", cancellationToken);

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET ContractStatus = 'Amateur'
WHERE COALESCE(Retired, 0) = 0
  AND COALESCE(CareerStage, 'Pro') = 'Amateur'
  AND PromotionId IN
  (
      SELECT Id
      FROM Promotions
      WHERE COALESCE(CircuitType, 'Professional') = 'Amateur'
  )
  AND COALESCE(ContractStatus, '') <> 'Amateur';", cancellationToken);

        tx.Commit();
        return graduates;
    }

    public async Task<int> ProcessProspectWatchAlertsAsync(string currentDate, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var agentId = await LoadPrimaryAgentIdAsync(conn, tx, cancellationToken);
        if (agentId is null)
            return 0;

        var alertsSent = 0;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    w.FighterId,
    (f.FirstName || ' ' || f.LastName) AS FighterName,
    COALESCE(f.CareerStage, 'Pro') AS CareerStage,
    COALESCE(f.AmateurWins, 0) AS AmateurWins,
    COALESCE(f.AmateurLosses, 0) AS AmateurLosses,
    COALESCE(f.AmateurDraws, 0) AS AmateurDraws,
    COALESCE(f.Wins, 0) AS Wins,
    COALESCE(f.Losses, 0) AS Losses,
    COALESCE(f.Draws, 0) AS Draws,
    COALESCE(f.Age, 18) AS Age,
    COALESCE(f.Skill, 50) AS Skill,
    COALESCE(f.Potential, 50) AS Potential,
    COALESCE(f.Popularity, 50) AS Popularity,
    COALESCE(f.WeightClass, '') AS WeightClass,
    COALESCE(p.Name, 'Free Agent') AS PromotionName,
    COALESCE(p.CircuitType, 'Professional') AS PromotionCircuitType,
    COALESCE(f.ContractStatus, 'FreeAgent') AS ContractStatus,
    w.LastAlertDate,
    COALESCE(w.LastAlertType, '') AS LastAlertType
FROM AmateurProspectWatchlist w
JOIN Fighters f ON f.Id = w.FighterId
LEFT JOIN Promotions p ON p.Id = f.PromotionId
WHERE w.AgentId = $agentId
  AND COALESCE(f.Retired, 0) = 0
ORDER BY f.Potential DESC, f.Skill DESC, f.Popularity DESC, f.Id DESC;";
        cmd.Parameters.AddWithValue("$agentId", agentId.Value);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fighterId = Convert.ToInt32(reader["FighterId"]);
            var fighterName = reader["FighterName"]?.ToString() ?? "Prospect";
            var careerStage = reader["CareerStage"]?.ToString() ?? "Pro";
            var amateurWins = Convert.ToInt32(reader["AmateurWins"]);
            var amateurLosses = Convert.ToInt32(reader["AmateurLosses"]);
            var amateurDraws = Convert.ToInt32(reader["AmateurDraws"]);
            var wins = Convert.ToInt32(reader["Wins"]);
            var losses = Convert.ToInt32(reader["Losses"]);
            var draws = Convert.ToInt32(reader["Draws"]);
            var age = Convert.ToInt32(reader["Age"]);
            var skill = Convert.ToInt32(reader["Skill"]);
            var potential = Convert.ToInt32(reader["Potential"]);
            var popularity = Convert.ToInt32(reader["Popularity"]);
            var weightClass = reader["WeightClass"]?.ToString() ?? "Division";
            var promotionName = reader["PromotionName"]?.ToString() ?? "Free Agent";
            var contractStatus = reader["ContractStatus"]?.ToString() ?? "FreeAgent";
            var lastAlertDate = reader["LastAlertDate"] == DBNull.Value ? null : reader["LastAlertDate"]?.ToString();
            var lastAlertType = reader["LastAlertType"]?.ToString() ?? "";

            var amateurTotal = amateurWins + amateurLosses + amateurDraws;
            string? alertType = null;
            string? subject = null;
            string? body = null;

            if (string.Equals(careerStage, "Amateur", StringComparison.OrdinalIgnoreCase)
                && IsReadyForProJump(age, skill, potential, popularity, amateurTotal))
            {
                alertType = "ReadyForProJump";
                subject = $"{fighterName} looks ready for the pro jump";
                body = $"{fighterName} is building real pressure in the amateur circuit at {weightClass}. The profile now looks close to pro-ready and worth tracking aggressively.";
            }
            else if (string.Equals(careerStage, "Pro", StringComparison.OrdinalIgnoreCase)
                && amateurTotal > 0
                && (string.Equals(contractStatus, "FreeAgent", StringComparison.OrdinalIgnoreCase) || string.Equals(promotionName, "Free Agent", StringComparison.OrdinalIgnoreCase)))
            {
                alertType = "GraduatedToProMarket";
                subject = $"{fighterName} just hit the pro market";
                body = $"{fighterName} has moved out of the amateur circuit and is now a professional free agent. Amateur record: {amateurWins}-{amateurLosses}-{amateurDraws}.";
            }
            else if (string.Equals(careerStage, "Pro", StringComparison.OrdinalIgnoreCase)
                && amateurTotal > 0
                && !string.Equals(contractStatus, "FreeAgent", StringComparison.OrdinalIgnoreCase))
            {
                alertType = "GraduatedAndSigned";
                subject = $"{fighterName} already landed a pro deal";
                body = $"{fighterName} has completed the amateur jump and already signed with {promotionName}, so the market window has moved fast.";
            }

            if (alertType is null || subject is null || body is null)
                continue;

            if (!ShouldSendProspectAlert(currentDate, lastAlertDate, alertType, lastAlertType))
                continue;

            await InsertAgentMessageAsync(
                conn,
                tx,
                agentId.Value,
                "ProspectWatchAlert",
                subject,
                body,
                currentDate,
                cancellationToken);

            using var updateWatchCmd = conn.CreateCommand();
            updateWatchCmd.Transaction = tx;
            updateWatchCmd.CommandText = @"
UPDATE AmateurProspectWatchlist
SET LastAlertDate = $currentDate,
    LastAlertType = $alertType,
    Notes = $notes
WHERE AgentId = $agentId
  AND FighterId = $fighterId;";
            updateWatchCmd.Parameters.AddWithValue("$currentDate", currentDate);
            updateWatchCmd.Parameters.AddWithValue("$alertType", alertType);
            updateWatchCmd.Parameters.AddWithValue("$notes", body);
            updateWatchCmd.Parameters.AddWithValue("$agentId", agentId.Value);
            updateWatchCmd.Parameters.AddWithValue("$fighterId", fighterId);
            await updateWatchCmd.ExecuteNonQueryAsync(cancellationToken);

            alertsSent++;
        }

        tx.Commit();
        return alertsSent;
    }

    public async Task<int> ApplyWeeklyExpensesAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var agentId = await LoadPrimaryAgentIdAsync(conn, tx, cancellationToken);
        if (agentId is null)
            return 0;

        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);
        var levels = await LoadAgentInvestmentLevelsAsync(conn, tx, agentId.Value, cancellationToken);

        var managedFighters = await CountAsync(conn, tx, @"
SELECT COUNT(*)
FROM ManagedFighters
WHERE AgentId = $agentId
  AND COALESCE(IsActive, 1) = 1;", cancellationToken, ("$agentId", agentId.Value));

        var bookedFighters = await CountAsync(conn, tx, @"
SELECT COUNT(*)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND COALESCE(f.IsBooked, 0) = 1;", cancellationToken, ("$agentId", agentId.Value));

        var medicalCases = await CountAsync(conn, tx, @"
SELECT COUNT(*)
FROM ManagedFighters mf
JOIN Fighters f ON f.Id = mf.FighterId
WHERE mf.AgentId = $agentId
  AND COALESCE(mf.IsActive, 1) = 1
  AND (
      COALESCE(f.InjuryWeeksRemaining, 0) > 0
      OR COALESCE(f.MedicalSuspensionWeeksRemaining, 0) > 0
  );", cancellationToken, ("$agentId", agentId.Value));

        var officeCost = 1400;
        var staffCost = managedFighters * 350;
        var gymCost = bookedFighters * (175 + (levels.CampInvestmentLevel * 120));
        var medicalCost = medicalCases * (300 + (levels.MedicalInvestmentLevel * 150));
        var scoutingCost = 500 + (levels.ScoutingStaffLevel * 450);
        var mediaCost = 450 + (levels.MediaStaffLevel * 400);
        var negotiationCost = 400 + (levels.NegotiationStaffLevel * 425);
        var performanceCost = 550 + (levels.PerformanceStaffLevel * 475);
        var total = officeCost + staffCost + gymCost + medicalCost + scoutingCost + mediaCost + negotiationCost + performanceCost;

        if (total <= 0)
            return 0;

        await ExecAsync(conn, tx, @"
UPDATE AgentProfile
SET Money = COALESCE(Money, 0) - $amount
WHERE Id = $agentId;", cancellationToken,
            ("$amount", total),
            ("$agentId", agentId.Value));

        var notes = $"Office {officeCost} · Staff {staffCost} · Gyms {gymCost} · Medical {medicalCost} · Scouting {scoutingCost} · PR {mediaCost} · Negotiation {negotiationCost} · Performance {performanceCost} · Camp tier {levels.CampInvestmentLevel} · Medical tier {levels.MedicalInvestmentLevel}";

        await InsertAgentTransactionAsync(
            conn,
            tx,
            agentId.Value,
            currentDate,
            -total,
            "WeeklyExpenses",
            notes,
            cancellationToken);

        tx.Commit();
        return total;
    }

    private static async Task RecalculateFighterSignalsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET ReliabilityScore = MIN(99, MAX(15,
        42
        + CAST(ROUND(COALESCE(Discipline, 50) * 0.42) AS INTEGER)
        + CAST(ROUND(COALESCE(Stability, 50) * 0.22) AS INTEGER)
        - CAST(ROUND(COALESCE(RiskTolerance, 50) * 0.10) AS INTEGER)
        - (COALESCE(WeightMissCount, 0) * 12)
        - (COALESCE(CampWithdrawalCount, 0) * 10)
        - CASE
            WHEN (
                SELECT CAST((julianday($currentDate) - julianday(MAX(fh.FightDate))) / 7 AS INTEGER)
                FROM FightHistory fh
                WHERE fh.FighterAId = Fighters.Id OR fh.FighterBId = Fighters.Id
            ) >= 24 THEN 10
            WHEN (
                SELECT CAST((julianday($currentDate) - julianday(MAX(fh.FightDate))) / 7 AS INTEGER)
                FROM FightHistory fh
                WHERE fh.FighterAId = Fighters.Id OR fh.FighterBId = Fighters.Id
            ) >= 16 THEN 5
            ELSE 0
        END
        - CASE
            WHEN COALESCE(InjuryWeeksRemaining, 0) > 0 OR COALESCE(MedicalSuspensionWeeksRemaining, 0) > 0 THEN 6
            ELSE 0
        END
        + CASE WHEN COALESCE(IsBooked, 0) = 1 THEN 2 ELSE 0 END
    )),
    MediaHeat = MIN(99, MAX(5,
        CAST(ROUND(
            (COALESCE(Popularity, 50) * 0.45)
            + (COALESCE(Marketability, 50) * 0.28)
            + (COALESCE(Momentum, 50) * 0.20)
            + (COALESCE(Showmanship, 40) * 0.22)
            + (COALESCE(RiskTolerance, 50) * 0.05)
            + CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM Titles t
                    WHERE t.ChampionFighterId = Fighters.Id
                      AND t.PromotionId = Fighters.PromotionId
                      AND t.WeightClass = Fighters.WeightClass
                ) THEN 8
                ELSE 0
            END
            + MIN(8, (
                SELECT COUNT(*)
                FROM FightHistory fh
                WHERE (fh.FighterAId = Fighters.Id OR fh.FighterBId = Fighters.Id)
                  AND (
                      COALESCE(fh.IsTitle, 0) = 1
                      OR COALESCE(fh.IsMainEvent, 0) = 1
                      OR COALESCE(fh.IsCoMainEvent, 0) = 1
                  )
            ))
        ) AS INTEGER)
    ));", cancellationToken, ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
UPDATE Fighters
SET Marketability = MIN(99, MAX(15,
    CAST(ROUND((COALESCE(Marketability, 50) * 0.75) + (COALESCE(MediaHeat, 20) * 0.25)) AS INTEGER)
));", cancellationToken);
    }

    private static async Task RebuildScoutKnowledgeAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM ScoutKnowledge WHERE AgentId = $agentId;", cancellationToken, ("$agentId", agentId));

        await ExecAsync(conn, tx, @"
INSERT INTO ScoutKnowledge
(
    AgentId,
    FighterId,
    Confidence,
    EstimatedSkillMin,
    EstimatedSkillMax,
    EstimatedPotentialMin,
    EstimatedPotentialMax,
    EstimatedStrikingMin,
    EstimatedStrikingMax,
    EstimatedGrapplingMin,
    EstimatedGrapplingMax,
    EstimatedWrestlingMin,
    EstimatedWrestlingMax,
    EstimatedCardioMin,
    EstimatedCardioMax,
    EstimatedChinMin,
    EstimatedChinMax,
    EstimatedFightIQMin,
    EstimatedFightIQMax,
    LastUpdatedDate
)
SELECT
    $agentId,
    f.Id,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM ManagedFighters mf
            WHERE mf.FighterId = f.Id
              AND mf.AgentId = $agentId
              AND COALESCE(mf.IsActive, 1) = 1
        ) THEN 96
        WHEN f.PromotionId = (
            SELECT mf2.PromotionId
            FROM ManagedFighters mg
            JOIN Fighters mf2 ON mf2.Id = mg.FighterId
            WHERE mg.AgentId = $agentId
              AND COALESCE(mg.IsActive, 1) = 1
              AND mf2.PromotionId IS NOT NULL
            GROUP BY mf2.PromotionId
            ORDER BY COUNT(*) DESC, mf2.PromotionId
            LIMIT 1
        ) THEN 76
        WHEN COALESCE(f.MediaHeat, 20) >= 70 THEN 70
        WHEN COALESCE(f.Popularity, 0) >= 55 THEN 62
        ELSE 44
    END AS Confidence,
    MAX(1, f.Skill - (
        CASE
            WHEN EXISTS (
                SELECT 1 FROM ManagedFighters mf
                WHERE mf.FighterId = f.Id
                  AND mf.AgentId = $agentId
                  AND COALESCE(mf.IsActive, 1) = 1
            ) THEN 3
            WHEN COALESCE(f.MediaHeat, 20) >= 70 THEN 8
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 11
            ELSE 17
        END
    )),
    MIN(99, f.Skill + (
        CASE
            WHEN EXISTS (
                SELECT 1 FROM ManagedFighters mf
                WHERE mf.FighterId = f.Id
                  AND mf.AgentId = $agentId
                  AND COALESCE(mf.IsActive, 1) = 1
            ) THEN 3
            WHEN COALESCE(f.MediaHeat, 20) >= 70 THEN 8
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 11
            ELSE 17
        END
    )),
    MAX(1, f.Potential - (
        CASE
            WHEN EXISTS (
                SELECT 1 FROM ManagedFighters mf
                WHERE mf.FighterId = f.Id
                  AND mf.AgentId = $agentId
                  AND COALESCE(mf.IsActive, 1) = 1
            ) THEN 4
            WHEN COALESCE(f.MediaHeat, 20) >= 70 THEN 10
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 13
            ELSE 20
        END
    )),
    MIN(99, f.Potential + (
        CASE
            WHEN EXISTS (
                SELECT 1 FROM ManagedFighters mf
                WHERE mf.FighterId = f.Id
                  AND mf.AgentId = $agentId
                  AND COALESCE(mf.IsActive, 1) = 1
            ) THEN 4
            WHEN COALESCE(f.MediaHeat, 20) >= 70 THEN 10
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 13
            ELSE 20
        END
    )),
    MAX(1, f.Striking - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.Striking + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MAX(1, f.Grappling - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.Grappling + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MAX(1, f.Wrestling - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.Wrestling + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MAX(1, f.Cardio - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.Cardio + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MAX(1, f.Chin - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.Chin + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MAX(1, f.FightIQ - (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    MIN(99, f.FightIQ + (
        CASE
            WHEN EXISTS (SELECT 1 FROM ManagedFighters mf WHERE mf.FighterId = f.Id AND mf.AgentId = $agentId AND COALESCE(mf.IsActive, 1) = 1) THEN 4
            WHEN COALESCE(f.Popularity, 0) >= 55 THEN 10
            ELSE 16
        END
    )),
    $currentDate
FROM Fighters f;", cancellationToken,
            ("$agentId", agentId),
            ("$currentDate", currentDate));
    }

    private static async Task RebuildStorylinesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM Storylines;", cancellationToken);

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    t.ChampionFighterId,
    'ChampionSpotlight',
    f.FirstName || ' ' || f.LastName || ' holds the belt',
    f.FirstName || ' ' || f.LastName || ' currently sits atop the ' || t.WeightClass || ' division in ' || p.Name || '.',
    88,
    'Active',
    $currentDate
FROM Titles t
JOIN Fighters f ON f.Id = t.ChampionFighterId
JOIN Promotions p ON p.Id = t.PromotionId
WHERE COALESCE(t.ChampionFighterId, 0) > 0;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
WITH FighterResults AS
(
    SELECT
        FighterAId AS FighterId,
        CASE WHEN WinnerId = FighterAId THEN 1 ELSE 0 END AS Won,
        Id
    FROM FightHistory
    UNION ALL
    SELECT
        FighterBId AS FighterId,
        CASE WHEN WinnerId = FighterBId THEN 1 ELSE 0 END AS Won,
        Id
    FROM FightHistory
),
Recent AS
(
    SELECT
        FighterId,
        Won,
        ROW_NUMBER() OVER (PARTITION BY FighterId ORDER BY Id DESC) AS rn
    FROM FighterResults
),
Agg AS
(
    SELECT
        FighterId,
        SUM(CASE WHEN rn <= 3 AND Won = 1 THEN 1 ELSE 0 END) AS Win3,
        SUM(CASE WHEN rn <= 2 AND Won = 0 THEN 1 ELSE 0 END) AS Loss2,
        SUM(CASE WHEN rn <= 4 AND Won = 1 THEN 1 ELSE 0 END) AS Win4,
        COUNT(CASE WHEN rn <= 3 THEN 1 END) AS RecentCount3,
        COUNT(CASE WHEN rn <= 2 THEN 1 END) AS RecentCount2
    FROM Recent
    WHERE rn <= 4
    GROUP BY FighterId
)
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    a.FighterId,
    CASE WHEN a.Win3 >= 3 THEN 'WinStreak' ELSE 'RedemptionArc' END,
    CASE WHEN a.Win3 >= 3
        THEN f.FirstName || ' ' || f.LastName || ' is surging'
        ELSE f.FirstName || ' ' || f.LastName || ' needs a rebound'
    END,
    CASE WHEN a.Win3 >= 3
        THEN f.FirstName || ' ' || f.LastName || ' has stacked together a strong recent streak and is building pressure on the division.'
        ELSE f.FirstName || ' ' || f.LastName || ' is coming off consecutive setbacks and now fights with redemption pressure.'
    END,
    CASE WHEN a.Win3 >= 3 THEN 78 ELSE 64 END,
    'Active',
    $currentDate
FROM Agg a
JOIN Fighters f ON f.Id = a.FighterId
WHERE (a.RecentCount3 = 3 AND a.Win3 = 3)
   OR (a.RecentCount2 = 2 AND a.Loss2 = 2);", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'MissedWeightRepeat',
    f.FirstName || ' ' || f.LastName || ' is under scale scrutiny',
    f.FirstName || ' ' || f.LastName || ' has now missed weight multiple times, which is starting to define the conversation around future bookings.',
    72,
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.WeightMissCount, 0) >= 2;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'BigFightFighter',
    f.FirstName || ' ' || f.LastName || ' keeps landing on big stages',
    f.FirstName || ' ' || f.LastName || ' has become a reliable part of high-profile cards, with repeated title or headline exposure.',
    74,
    'Active',
    $currentDate
FROM Fighters f
WHERE (
    SELECT COUNT(*)
    FROM FightHistory fh
    WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
      AND (
          COALESCE(fh.IsTitle, 0) = 1
          OR COALESCE(fh.IsMainEvent, 0) = 1
          OR COALESCE(fh.IsCoMainEvent, 0) = 1
      )
) >= 3;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'ShortNoticeWarrior',
    f.FirstName || ' ' || f.LastName || ' keeps saying yes',
    f.FirstName || ' ' || f.LastName || ' has taken multiple short-notice bouts and is building a reputation as someone willing to save a card.',
    70,
    'Active',
    $currentDate
FROM Fighters f
WHERE (
    SELECT COUNT(*)
    FROM Fights sf
    WHERE (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
      AND COALESCE(sf.IsShortNotice, 0) = 1
      AND COALESCE(sf.Method, '') NOT IN ('Scheduled', 'Cancelled', '')
) >= 2;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'ActionFighter',
    f.FirstName || ' ' || f.LastName || ' feels built for the spotlight',
    f.FirstName || ' ' || f.LastName || ' carries an action-friendly style and enough personality that big-stage matchmaking starts to make sense around them.',
    MIN(86, 66 + CAST(ROUND(COALESCE(f.Showmanship, 40) * 0.18) AS INTEGER)),
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.Style, '') IN ('Boxer', 'Kickboxer', 'Brawler', 'Counter Striker')
  AND (COALESCE(f.Showmanship, 40) >= 66 OR COALESCE(f.MediaHeat, 20) >= 68);", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'PressureGame',
    f.FirstName || ' ' || f.LastName || ' is suffocating people',
    f.FirstName || ' ' || f.LastName || ' is building a reputation for making fights ugly, hard and exhausting for opponents, which matters in this division.',
    MIN(82, 62 + CAST(ROUND(COALESCE(f.ReliabilityScore, 60) * 0.16) AS INTEGER)),
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.Style, '') IN ('Pressure Wrestler', 'Control Wrestler')
  AND COALESCE(f.Wrestling, 50) >= 68
  AND COALESCE(f.Cardio, 50) >= 64;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'SubmissionThreat',
    f.FirstName || ' ' || f.LastName || ' is a live submission threat',
    f.FirstName || ' ' || f.LastName || ' has the kind of grappling style that changes how opponents prepare and how matchmakers frame the danger.',
    MIN(84, 64 + CAST(ROUND(COALESCE(f.Grappling, 50) * 0.16) AS INTEGER)),
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.Style, '') IN ('Submission Hunter', 'Scrambler')
  AND COALESCE(f.Grappling, 50) >= 68
  AND (COALESCE(f.Momentum, 50) >= 58 OR COALESCE(f.Potential, 50) >= COALESCE(f.Skill, 50) + 6);", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'MediaHeat',
    f.FirstName || ' ' || f.LastName || ' is drawing attention',
    f.FirstName || ' ' || f.LastName || ' is carrying real media heat right now, which helps the spotlight but also adds pressure in big weeks.',
    68,
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.MediaHeat, 0) >= 75;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'ProspectSurge',
    f.FirstName || ' ' || f.LastName || ' is rising fast',
    f.FirstName || ' ' || f.LastName || ' is young, trending upward and starting to feel like a real future player in the division.',
    67,
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.Age, 18) <= 25
  AND COALESCE(f.Momentum, 50) >= 65
  AND COALESCE(f.Potential, 50) >= COALESCE(f.Skill, 50) + 8;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'VeteranMiles',
    f.FirstName || ' ' || f.LastName || ' is carrying veteran miles',
    f.FirstName || ' ' || f.LastName || ' has enough accumulated wear that every camp and recovery cycle now matters more than it used to.',
    MIN(84, 58 + (COALESCE(f.DamageMiles, 0) / 2)),
    'Active',
    $currentDate
FROM Fighters f
WHERE COALESCE(f.Age, 18) >= 34
  AND COALESCE(f.DamageMiles, 0) >= 22;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
WITH FighterResults AS
(
    SELECT FighterAId AS FighterId, CASE WHEN WinnerId = FighterAId THEN 1 ELSE 0 END AS Won, Id
    FROM FightHistory
    UNION ALL
    SELECT FighterBId AS FighterId, CASE WHEN WinnerId = FighterBId THEN 1 ELSE 0 END AS Won, Id
    FROM FightHistory
),
Recent AS
(
    SELECT FighterId, Won, ROW_NUMBER() OVER (PARTITION BY FighterId ORDER BY Id DESC) AS rn
    FROM FighterResults
),
Agg AS
(
    SELECT FighterId,
           SUM(CASE WHEN rn <= 3 AND Won = 1 THEN 1 ELSE 0 END) AS Win3
    FROM Recent
    WHERE rn <= 3
    GROUP BY FighterId
)
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    f.Id,
    'LateCareerRun',
    f.FirstName || ' ' || f.LastName || ' is making another run',
    f.FirstName || ' ' || f.LastName || ' is putting together meaningful wins deep into the career arc, which changes the tone around the division.',
    76,
    'Active',
    $currentDate
FROM Fighters f
JOIN Agg a ON a.FighterId = f.Id
WHERE COALESCE(f.Age, 18) >= 33
  AND COALESCE(a.Win3, 0) >= 3;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    r.FighterAId,
    'Rivalry',
    fa.FirstName || ' ' || fa.LastName || ' has unfinished business',
    r.Summary,
    r.Intensity,
    'Active',
    $currentDate
FROM Rivalries r
JOIN Fighters fa ON fa.Id = r.FighterAId
WHERE r.Intensity >= 58;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO Storylines (EntityType, EntityId, StoryType, Headline, Body, Intensity, Status, LastUpdatedDate)
SELECT
    'Fighter',
    r.FighterBId,
    'Rivalry',
    fb.FirstName || ' ' || fb.LastName || ' has unfinished business',
    r.Summary,
    r.Intensity,
    'Active',
    $currentDate
FROM Rivalries r
JOIN Fighters fb ON fb.Id = r.FighterBId
WHERE r.Intensity >= 58;", cancellationToken,
            ("$currentDate", currentDate));
    }

    private static async Task RebuildContenderQueueAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM ContenderQueue;", cancellationToken);

        await ExecAsync(conn, tx, @"
WITH Champions AS
(
    SELECT PromotionId, WeightClass, ChampionFighterId
    FROM Titles
    WHERE COALESCE(ChampionFighterId, 0) > 0
),
Candidates AS
(
    SELECT
        f.PromotionId,
        f.WeightClass,
        f.Id AS FighterId,
        pr.RankPosition,
        CAST(ROUND(
            CASE
                WHEN pr.RankPosition IS NULL OR pr.RankPosition <= 0 THEN 18
                ELSE 115 - (pr.RankPosition * 7)
            END
            + (COALESCE(f.Popularity, 50) * 0.35)
            + (COALESCE(f.Momentum, 50) * 0.45)
            + (COALESCE(f.MediaHeat, 20) * 0.20)
            + (COALESCE(f.ReliabilityScore, 60) * 0.25)
            - CASE WHEN COALESCE(f.IsBooked, 0) = 1 THEN 12 ELSE 0 END
            - CASE
                WHEN COALESCE(f.InjuryWeeksRemaining, 0) > 0 OR COALESCE(f.MedicalSuspensionWeeksRemaining, 0) > 0 THEN 18
                ELSE 0
            END
        ) AS INTEGER) AS QueueScore,
        CASE
            WHEN COALESCE(f.IsBooked, 0) = 1 THEN 'Booked but still in the title picture'
            WHEN COALESCE(f.InjuryWeeksRemaining, 0) > 0 OR COALESCE(f.MedicalSuspensionWeeksRemaining, 0) > 0 THEN 'Strong case, but currently unavailable'
            WHEN pr.RankPosition IS NOT NULL AND pr.RankPosition <= 3 THEN 'Near-term title pressure'
            WHEN COALESCE(f.MediaHeat, 20) >= 75 THEN 'Hot profile with real attention'
            ELSE 'Active contender in the mix'
        END AS Notes
    FROM Fighters f
    LEFT JOIN PromotionRankings pr
        ON pr.FighterId = f.Id
       AND pr.PromotionId = f.PromotionId
       AND pr.WeightClass = f.WeightClass
    LEFT JOIN Champions ch
        ON ch.PromotionId = f.PromotionId
       AND ch.WeightClass = f.WeightClass
    WHERE f.PromotionId IS NOT NULL
      AND COALESCE(f.ContractStatus, '') <> 'FreeAgent'
      AND COALESCE(ch.ChampionFighterId, 0) <> f.Id
),
Ranked AS
(
    SELECT
        PromotionId,
        WeightClass,
        FighterId,
        QueueScore,
        ROW_NUMBER() OVER (
            PARTITION BY PromotionId, WeightClass
            ORDER BY QueueScore DESC, COALESCE(RankPosition, 999), FighterId
        ) AS QueueRank,
        Notes
    FROM Candidates
    WHERE PromotionId IS NOT NULL
      AND WeightClass IS NOT NULL
      AND WeightClass <> ''
)
INSERT INTO ContenderQueue
(
    PromotionId,
    WeightClass,
    FighterId,
    QueueScore,
    QueueRank,
    Notes,
    LastUpdatedDate
)
SELECT
    PromotionId,
    WeightClass,
    FighterId,
    QueueScore,
    QueueRank,
    Notes,
    $currentDate
FROM Ranked
WHERE QueueRank <= 10;", cancellationToken,
            ("$currentDate", currentDate));
    }

    private static async Task RebuildLegacyTagsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM LegacyTags;", cancellationToken);

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    t.ChampionFighterId,
    'Champion',
    'Current champion in the division.',
    88,
    $currentDate
FROM Titles t
WHERE COALESCE(t.ChampionFighterId, 0) > 0;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    f.Id,
    'FormerChampion',
    'Held gold at some point in the promotion.',
    78,
    $currentDate
FROM Fighters f
WHERE EXISTS
(
    SELECT 1
    FROM FightHistory fh
    WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
      AND COALESCE(fh.IsTitle, 0) = 1
      AND fh.WinnerId = f.Id
);", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    f.Id,
    'BigFightFighter',
    'Repeatedly appears in title fights or headline slots.',
    74,
    $currentDate
FROM Fighters f
WHERE (
    SELECT COUNT(*)
    FROM FightHistory fh
    WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
      AND (
          COALESCE(fh.IsTitle, 0) = 1
          OR COALESCE(fh.IsMainEvent, 0) = 1
          OR COALESCE(fh.IsCoMainEvent, 0) = 1
      )
) >= 3;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    f.Id,
    'WeightTrouble',
    'Repeated scale issues have become part of the fighter''s reputation.',
    MIN(84, 60 + (COALESCE(f.WeightMissCount, 0) * 6)),
    $currentDate
FROM Fighters f
WHERE COALESCE(f.WeightMissCount, 0) >= 2;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    f.Id,
    'ShortNoticeSpecialist',
    'Known for taking late replacement opportunities.',
    68,
    $currentDate
FROM Fighters f
WHERE (
    SELECT COUNT(*)
    FROM FightHistory fh
    JOIN Fights sf
      ON sf.EventId = fh.EventId
     AND ((sf.FighterAId = fh.FighterAId AND sf.FighterBId = fh.FighterBId) OR (sf.FighterAId = fh.FighterBId AND sf.FighterBId = fh.FighterAId))
    WHERE (fh.FighterAId = f.Id OR fh.FighterBId = f.Id)
      AND COALESCE(sf.IsShortNotice, 0) = 1
) >= 2;", cancellationToken,
            ("$currentDate", currentDate));

        await ExecAsync(conn, tx, @"
INSERT INTO LegacyTags (FighterId, TagCode, Summary, Intensity, LastUpdatedDate)
SELECT
    f.Id,
    'VeteranMiles',
    'Long career with meaningful accumulated wear.',
    MIN(90, 58 + (COALESCE(f.DamageMiles, 0) / 2)),
    $currentDate
FROM Fighters f
WHERE COALESCE(f.DamageMiles, 0) >= 22
   OR COALESCE(f.Age, 18) >= 35;", cancellationToken,
            ("$currentDate", currentDate));
    }

    private static async Task<int> RebuildAnnualRankingsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        var divisions = await CountAsync(conn, tx, @"
SELECT COUNT(*)
FROM
(
    SELECT PromotionId, WeightClass
    FROM Fighters
    WHERE PromotionId IS NOT NULL
      AND COALESCE(Retired, 0) = 0
      AND COALESCE(ContractStatus, '') = 'Active'
      AND WeightClass IS NOT NULL
      AND WeightClass <> ''
    GROUP BY PromotionId, WeightClass
    HAVING COUNT(*) >= 4
);", cancellationToken);

        await ExecAsync(conn, tx, "DELETE FROM PromotionRankings;", cancellationToken);

        await ExecAsync(conn, tx, @"
WITH Champions AS
(
    SELECT PromotionId, WeightClass, ChampionFighterId
    FROM Titles
    WHERE COALESCE(ChampionFighterId, 0) > 0
),
Scored AS
(
    SELECT
        f.PromotionId,
        f.WeightClass,
        f.Id AS FighterId,
        CAST(ROUND(
            CASE
                WHEN ch.ChampionFighterId = f.Id THEN 1000
                ELSE
                    (COALESCE(f.Skill, 50) * 0.42)
                    + (COALESCE(f.Momentum, 50) * 0.22)
                    + (COALESCE(f.Popularity, 50) * 0.14)
                    + (COALESCE(f.Marketability, 50) * 0.10)
                    + (COALESCE(f.MediaHeat, 20) * 0.06)
                    + (COALESCE(f.ReliabilityScore, 60) * 0.06)
                    - CASE WHEN COALESCE(f.IsBooked, 0) = 1 THEN 5 ELSE 0 END
            END
        ) AS INTEGER) AS RankScore
    FROM Fighters f
    LEFT JOIN Champions ch
      ON ch.PromotionId = f.PromotionId
     AND ch.WeightClass = f.WeightClass
    WHERE f.PromotionId IS NOT NULL
      AND COALESCE(f.Retired, 0) = 0
      AND COALESCE(f.ContractStatus, '') = 'Active'
      AND f.WeightClass IS NOT NULL
      AND f.WeightClass <> ''
),
Ranked AS
(
    SELECT
        PromotionId,
        WeightClass,
        FighterId,
        ROW_NUMBER() OVER (
            PARTITION BY PromotionId, WeightClass
            ORDER BY RankScore DESC, FighterId
        ) AS RankPosition
    FROM Scored
)
INSERT INTO PromotionRankings (PromotionId, WeightClass, RankPosition, FighterId)
SELECT PromotionId, WeightClass, RankPosition, FighterId
FROM Ranked
WHERE RankPosition <= 15;", cancellationToken);

        return divisions;
    }

    private static async Task<(int CampInvestmentLevel, int MedicalInvestmentLevel, int ScoutingStaffLevel, int MediaStaffLevel, int NegotiationStaffLevel, int PerformanceStaffLevel)> LoadAgentInvestmentLevelsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (1, 1, 1, 1, 1, 1);

        return (
            Convert.ToInt32(reader["CampInvestmentLevel"]),
            Convert.ToInt32(reader["MedicalInvestmentLevel"]),
            Convert.ToInt32(reader["ScoutingStaffLevel"]),
            Convert.ToInt32(reader["MediaStaffLevel"]),
            Convert.ToInt32(reader["NegotiationStaffLevel"]),
            Convert.ToInt32(reader["PerformanceStaffLevel"]));
    }

    private static async Task SynchronizePromotionRelationsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO AgentPromotionRelations
(
    AgentId,
    PromotionId,
    RelationshipScore,
    LastUpdatedDate,
    Notes
)
SELECT
    $agentId,
    p.Id,
    COALESCE(existing.RelationshipScore, MIN(85, MAX(30, 42 + (COALESCE(p.Prestige, 50) / 5)))),
    COALESCE(existing.LastUpdatedDate, $currentDate),
    COALESCE(existing.Notes, 'Baseline relationship.')
FROM Promotions p
LEFT JOIN AgentPromotionRelations existing
  ON existing.AgentId = $agentId
 AND existing.PromotionId = p.Id
WHERE existing.PromotionId IS NULL;", cancellationToken,
            ("$agentId", agentId),
            ("$currentDate", currentDate));
    }

    private static async Task RebuildRivalriesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM Rivalries;", cancellationToken);

        await ExecAsync(conn, tx, @"
WITH PairHistory AS
(
    SELECT
        CASE WHEN FighterAId < FighterBId THEN FighterAId ELSE FighterBId END AS FighterAId,
        CASE WHEN FighterAId < FighterBId THEN FighterBId ELSE FighterAId END AS FighterBId,
        WinnerId,
        COALESCE(IsTitle, 0) AS IsTitle,
        FightDate
    FROM FightHistory
    WHERE COALESCE(FighterAId, 0) > 0
      AND COALESCE(FighterBId, 0) > 0
),
Agg AS
(
    SELECT
        FighterAId,
        FighterBId,
        COUNT(*) AS FightCount,
        SUM(CASE WHEN WinnerId = FighterAId THEN 1 ELSE 0 END) AS WinsA,
        SUM(CASE WHEN WinnerId = FighterBId THEN 1 ELSE 0 END) AS WinsB,
        SUM(IsTitle) AS TitleFightCount,
        MAX(FightDate) AS LastFightDate
    FROM PairHistory
    GROUP BY FighterAId, FighterBId
)
INSERT INTO Rivalries
(
    FighterAId,
    FighterBId,
    FightCount,
    SplitSeries,
    TitleFightCount,
    Intensity,
    Summary,
    LastFightDate,
    LastUpdatedDate
)
SELECT
    a.FighterAId,
    a.FighterBId,
    a.FightCount,
    CASE WHEN a.WinsA > 0 AND a.WinsB > 0 THEN 1 ELSE 0 END,
    a.TitleFightCount,
    MIN(99, MAX(0,
        (a.FightCount * 22)
        + CASE WHEN a.WinsA > 0 AND a.WinsB > 0 THEN 18 ELSE 0 END
        + (a.TitleFightCount * 8)
        + CASE
            WHEN COALESCE(CAST((julianday($currentDate) - julianday(a.LastFightDate)) AS INTEGER), 9999) <= 120 THEN 8
            ELSE 0
        END
    )),
    fa.FirstName || ' ' || fa.LastName || ' and ' || fb.FirstName || ' ' || fb.LastName || ' keep circling each other after ' || a.FightCount || ' meeting(s).',
    a.LastFightDate,
    $currentDate
FROM Agg a
JOIN Fighters fa ON fa.Id = a.FighterAId
JOIN Fighters fb ON fb.Id = a.FighterBId
WHERE a.FightCount >= 2
   OR (a.WinsA > 0 AND a.WinsB > 0)
   OR a.TitleFightCount >= 1;", cancellationToken,
            ("$currentDate", currentDate));
    }

    public async Task InsertAgentTransactionAsync(
        int agentId,
        string txDate,
        int amount,
        string txType,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        await InsertAgentTransactionAsync(conn, tx, agentId, txDate, amount, txType, notes, cancellationToken);
        tx.Commit();
    }

    private static async Task InsertAgentTransactionAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string txDate,
        int amount,
        string txType,
        string? notes,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO AgentTransactions
(
    AgentId,
    TxDate,
    Amount,
    TxType,
    Notes
)
VALUES
(
    $agentId,
    $txDate,
    $amount,
    $txType,
    $notes
);", cancellationToken,
            ("$agentId", agentId),
            ("$txDate", txDate),
            ("$amount", amount),
            ("$txType", txType),
            ("$notes", notes));
    }

    private static async Task InsertAgentMessageAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int agentId,
        string messageType,
        string subject,
        string body,
        string createdDate,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
INSERT INTO InboxMessages
(
    AgentId,
    MessageType,
    Subject,
    Body,
    CreatedDate,
    IsRead,
    IsArchived,
    IsDeleted
)
VALUES
(
    $agentId,
    $messageType,
    $subject,
    $body,
    $createdDate,
    0,
    0,
    0
);", cancellationToken,
            ("$agentId", agentId),
            ("$messageType", messageType),
            ("$subject", subject),
            ("$body", body),
            ("$createdDate", createdDate));
    }

    private static async Task<string> LoadCurrentDateAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, date('now')) FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
    }

    private static async Task<int?> LoadPrimaryAgentIdAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM AgentProfile ORDER BY Id LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private static bool IsReadyForProJump(int age, int skill, int potential, int popularity, int amateurTotal)
        => amateurTotal >= 5
           || (skill >= 58 && potential >= 68 && amateurTotal >= 4)
           || age >= 24
           || popularity >= 56;

    private static bool ShouldSendProspectAlert(string currentDate, string? lastAlertDate, string alertType, string lastAlertType)
    {
        if (!string.Equals(alertType, lastAlertType, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!DateOnly.TryParse(currentDate, out var today))
            return true;

        if (!DateOnly.TryParse(lastAlertDate, out var lastDate))
            return true;

        return today.DayNumber - lastDate.DayNumber >= 21;
    }

    private static async Task<int> CountAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecWithCountAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public sealed record AnnualWorldShiftSummary(
        int Retirements,
        int VeteranDeclines,
        int DivisionsReshuffled);
}
