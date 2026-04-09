using Microsoft.Data.Sqlite;
using MMAAgent.Application.Abstractions;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistance.Sqlite.Services;

public sealed class FighterWorldServiceSqlite : IFighterWorldService
{
    private readonly SqliteConnectionFactory _factory;

    public FighterWorldServiceSqlite(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        var absoluteWeek = await LoadAbsoluteWeekAsync(conn, tx, cancellationToken);
        var currentDate = await LoadCurrentDateAsync(conn, tx, cancellationToken);

        await EnsureRowsAsync(conn, tx, cancellationToken);
        var snapshots = await LoadSnapshotsAsync(conn, tx, currentDate, cancellationToken);
        await PersistStylesAsync(conn, tx, snapshots, absoluteWeek, cancellationToken);
        await PersistStatesAsync(conn, tx, snapshots, currentDate, absoluteWeek, cancellationToken);

        tx.Commit();
    }

    public async Task AdvanceWeekAsync(int absoluteWeek, string currentDate, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var tx = conn.BeginTransaction();

        await EnsureRowsAsync(conn, tx, cancellationToken);
        var snapshots = await LoadSnapshotsAsync(conn, tx, currentDate, cancellationToken);
        await PersistStylesAsync(conn, tx, snapshots, absoluteWeek, cancellationToken);
        await PersistStatesAsync(conn, tx, snapshots, currentDate, absoluteWeek, cancellationToken);

        tx.Commit();
    }

    private static async Task EnsureRowsAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, @"
DELETE FROM FighterStates
WHERE FighterId NOT IN (SELECT Id FROM Fighters);", cancellationToken);

        await ExecAsync(conn, tx, @"
DELETE FROM FighterStyles
WHERE FighterId NOT IN (SELECT Id FROM Fighters);", cancellationToken);

        await ExecAsync(conn, tx, @"
DELETE FROM FighterTraits
WHERE FighterId NOT IN (SELECT Id FROM Fighters);", cancellationToken);

        await ExecAsync(conn, tx, @"
INSERT INTO FighterStates
(
    FighterId,
    Form,
    Energy,
    Sharpness,
    Morale,
    CampQuality,
    WeightCutReadiness,
    InjuryRisk,
    CurrentPhase,
    NextMilestoneType,
    NextMilestoneDate,
    LastFightDate,
    LastFightResult,
    LastUpdatedWeek
)
SELECT
    f.Id,
    50,
    70,
    50,
    50,
    50,
    55,
    20,
    'Idle',
    NULL,
    NULL,
    NULL,
    NULL,
    COALESCE((SELECT AbsoluteWeek FROM GameState LIMIT 1), 0)
FROM Fighters f
WHERE NOT EXISTS (
    SELECT 1
    FROM FighterStates fs
    WHERE fs.FighterId = f.Id
);", cancellationToken);

        await ExecAsync(conn, tx, @"
INSERT INTO FighterStyles
(
    FighterId,
    BaseStyle,
    TacticalStyle,
    StyleSummary,
    LastRecomputedWeek
)
SELECT
    f.Id,
    'All-Rounder',
    'Measured',
    '',
    COALESCE((SELECT AbsoluteWeek FROM GameState LIMIT 1), 0)
FROM Fighters f
WHERE NOT EXISTS (
    SELECT 1
    FROM FighterStyles fs
    WHERE fs.FighterId = f.Id
);", cancellationToken);
    }

    private static async Task<List<FighterSnapshot>> LoadSnapshotsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string currentDate,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT
    f.Id,
    COALESCE(f.Age, 18) AS Age,
    COALESCE(f.Wins, 0) AS Wins,
    COALESCE(f.Losses, 0) AS Losses,
    COALESCE(f.Draws, 0) AS Draws,
    COALESCE(f.KOWins, 0) AS KOWins,
    COALESCE(f.SubWins, 0) AS SubWins,
    COALESCE(f.DecWins, 0) AS DecWins,
    COALESCE(f.Skill, 50) AS Skill,
    COALESCE(f.Potential, 50) AS Potential,
    COALESCE(f.Popularity, 50) AS Popularity,
    COALESCE(f.Striking, 50) AS Striking,
    COALESCE(f.Grappling, 50) AS Grappling,
    COALESCE(f.Wrestling, 50) AS Wrestling,
    COALESCE(f.Cardio, 50) AS Cardio,
    COALESCE(f.Chin, 50) AS Chin,
    COALESCE(f.FightIQ, 50) AS FightIQ,
    COALESCE(f.ContractStatus, '') AS ContractStatus,
    COALESCE(f.IsBooked, 0) AS IsBooked,
    COALESCE(f.WeeksUntilAvailable, 0) AS WeeksUntilAvailable,
    COALESCE(f.InjuryWeeksRemaining, 0) AS InjuryWeeksRemaining,
    COALESCE(f.MedicalSuspensionWeeksRemaining, 0) AS MedicalSuspensionWeeksRemaining,
    (
        SELECT fh.FightDate
        FROM FightHistory fh
        WHERE fh.FighterAId = f.Id OR fh.FighterBId = f.Id
        ORDER BY fh.FightDate DESC, fh.Id DESC
        LIMIT 1
    ) AS LastFightDate,
    (
        SELECT CASE
            WHEN fh.WinnerId IS NULL THEN 'D'
            WHEN fh.WinnerId = f.Id THEN 'W'
            ELSE 'L'
        END
        FROM FightHistory fh
        WHERE fh.FighterAId = f.Id OR fh.FighterBId = f.Id
        ORDER BY fh.FightDate DESC, fh.Id DESC
        LIMIT 1
    ) AS LastFightResult,
    (
        SELECT sf.EventDate
        FROM Fights sf
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > $currentDate
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS NextFightDate,
    (
        SELECT CASE
            WHEN COALESCE(sf.IsTitleFight, 0) = 1 THEN COALESCE(p.TitleCampWeeks, 8)
            WHEN COALESCE(e.EventTier, 'Standard') = 'Major' THEN COALESCE(p.MajorCampWeeks, 6)
            ELSE COALESCE(p.StandardCampWeeks, 4)
        END
        FROM Fights sf
        LEFT JOIN Events e ON e.Id = sf.EventId
        LEFT JOIN Promotions p ON p.Id = COALESCE(e.PromotionId, f.PromotionId)
        WHERE sf.Method = 'Scheduled'
          AND (sf.FighterAId = f.Id OR sf.FighterBId = f.Id)
          AND COALESCE(sf.EventDate, '9999-12-31') > $currentDate
        ORDER BY sf.EventDate, sf.Id
        LIMIT 1
    ) AS NextFightCampWeeks
FROM Fighters f
ORDER BY f.Id;";
        cmd.Parameters.AddWithValue("$currentDate", currentDate);

        var list = new List<FighterSnapshot>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FighterSnapshot(
                Id: Convert.ToInt32(reader["Id"]),
                Age: Convert.ToInt32(reader["Age"]),
                Wins: Convert.ToInt32(reader["Wins"]),
                Losses: Convert.ToInt32(reader["Losses"]),
                Draws: Convert.ToInt32(reader["Draws"]),
                KOWins: Convert.ToInt32(reader["KOWins"]),
                SubWins: Convert.ToInt32(reader["SubWins"]),
                DecWins: Convert.ToInt32(reader["DecWins"]),
                Skill: Convert.ToInt32(reader["Skill"]),
                Potential: Convert.ToInt32(reader["Potential"]),
                Popularity: Convert.ToInt32(reader["Popularity"]),
                Striking: Convert.ToInt32(reader["Striking"]),
                Grappling: Convert.ToInt32(reader["Grappling"]),
                Wrestling: Convert.ToInt32(reader["Wrestling"]),
                Cardio: Convert.ToInt32(reader["Cardio"]),
                Chin: Convert.ToInt32(reader["Chin"]),
                FightIQ: Convert.ToInt32(reader["FightIQ"]),
                ContractStatus: reader["ContractStatus"]?.ToString() ?? "",
                IsBooked: Convert.ToInt32(reader["IsBooked"]) == 1,
                WeeksUntilAvailable: Convert.ToInt32(reader["WeeksUntilAvailable"]),
                InjuryWeeksRemaining: Convert.ToInt32(reader["InjuryWeeksRemaining"]),
                MedicalSuspensionWeeksRemaining: Convert.ToInt32(reader["MedicalSuspensionWeeksRemaining"]),
                LastFightDate: reader["LastFightDate"]?.ToString(),
                LastFightResult: reader["LastFightResult"]?.ToString(),
                NextFightDate: reader["NextFightDate"]?.ToString(),
                NextFightCampWeeks: reader["NextFightCampWeeks"] == DBNull.Value ? null : Convert.ToInt32(reader["NextFightCampWeeks"])));
        }

        return list;
    }

    private static async Task PersistStylesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<FighterSnapshot> snapshots,
        int absoluteWeek,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, tx, "DELETE FROM FighterTraits;", cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var baseStyle = DetermineBaseStyle(snapshot);
            var tacticalStyle = DetermineTacticalStyle(snapshot);
            var styleSummary = BuildStyleSummary(baseStyle, tacticalStyle, snapshot);
            var traits = DetermineTraits(snapshot);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO FighterStyles (FighterId, BaseStyle, TacticalStyle, StyleSummary, LastRecomputedWeek)
VALUES ($fighterId, $baseStyle, $tacticalStyle, $styleSummary, $week)
ON CONFLICT(FighterId) DO UPDATE SET
    BaseStyle = excluded.BaseStyle,
    TacticalStyle = excluded.TacticalStyle,
    StyleSummary = excluded.StyleSummary,
    LastRecomputedWeek = excluded.LastRecomputedWeek;";
                cmd.Parameters.AddWithValue("$fighterId", snapshot.Id);
                cmd.Parameters.AddWithValue("$baseStyle", baseStyle);
                cmd.Parameters.AddWithValue("$tacticalStyle", tacticalStyle);
                cmd.Parameters.AddWithValue("$styleSummary", styleSummary);
                cmd.Parameters.AddWithValue("$week", absoluteWeek);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var trait in traits)
            {
                using var traitCmd = conn.CreateCommand();
                traitCmd.Transaction = tx;
                traitCmd.CommandText = @"
INSERT INTO FighterTraits (FighterId, TraitCode, Intensity, Source)
VALUES ($fighterId, $traitCode, $intensity, 'Derived');";
                traitCmd.Parameters.AddWithValue("$fighterId", snapshot.Id);
                traitCmd.Parameters.AddWithValue("$traitCode", trait.TraitCode);
                traitCmd.Parameters.AddWithValue("$intensity", trait.Intensity);
                await traitCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task PersistStatesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<FighterSnapshot> snapshots,
        string currentDate,
        int absoluteWeek,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in snapshots)
        {
            var state = BuildState(snapshot, currentDate, absoluteWeek);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO FighterStates
(
    FighterId,
    Form,
    Energy,
    Sharpness,
    Morale,
    CampQuality,
    WeightCutReadiness,
    InjuryRisk,
    CurrentPhase,
    NextMilestoneType,
    NextMilestoneDate,
    LastFightDate,
    LastFightResult,
    LastUpdatedWeek
)
VALUES
(
    $fighterId,
    $form,
    $energy,
    $sharpness,
    $morale,
    $campQuality,
    $weightCutReadiness,
    $injuryRisk,
    $currentPhase,
    $nextMilestoneType,
    $nextMilestoneDate,
    $lastFightDate,
    $lastFightResult,
    $lastUpdatedWeek
)
ON CONFLICT(FighterId) DO UPDATE SET
    Form = excluded.Form,
    Energy = excluded.Energy,
    Sharpness = excluded.Sharpness,
    Morale = excluded.Morale,
    CampQuality = excluded.CampQuality,
    WeightCutReadiness = excluded.WeightCutReadiness,
    InjuryRisk = excluded.InjuryRisk,
    CurrentPhase = excluded.CurrentPhase,
    NextMilestoneType = excluded.NextMilestoneType,
    NextMilestoneDate = excluded.NextMilestoneDate,
    LastFightDate = excluded.LastFightDate,
    LastFightResult = excluded.LastFightResult,
    LastUpdatedWeek = excluded.LastUpdatedWeek;";
            cmd.Parameters.AddWithValue("$fighterId", snapshot.Id);
            cmd.Parameters.AddWithValue("$form", state.Form);
            cmd.Parameters.AddWithValue("$energy", state.Energy);
            cmd.Parameters.AddWithValue("$sharpness", state.Sharpness);
            cmd.Parameters.AddWithValue("$morale", state.Morale);
            cmd.Parameters.AddWithValue("$campQuality", state.CampQuality);
            cmd.Parameters.AddWithValue("$weightCutReadiness", state.WeightCutReadiness);
            cmd.Parameters.AddWithValue("$injuryRisk", state.InjuryRisk);
            cmd.Parameters.AddWithValue("$currentPhase", state.CurrentPhase);
            cmd.Parameters.AddWithValue("$nextMilestoneType", (object?)state.NextMilestoneType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$nextMilestoneDate", (object?)state.NextMilestoneDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lastFightDate", (object?)state.LastFightDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lastFightResult", (object?)state.LastFightResult ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lastUpdatedWeek", state.LastUpdatedWeek);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            await UpdatePresentationColumnsAsync(conn, tx, snapshot, state, cancellationToken);
        }
    }

    private static async Task UpdatePresentationColumnsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        FighterSnapshot snapshot,
        FighterStateRow state,
        CancellationToken cancellationToken)
    {
        var finishRate = snapshot.Wins > 0
            ? ((snapshot.KOWins + snapshot.SubWins) * 100.0) / snapshot.Wins
            : 0;

        var marketability = Clamp(
            20
            + snapshot.Popularity
            + (int)Math.Round(finishRate * 0.15)
            + (snapshot.Age is >= 24 and <= 32 ? 6 : 0)
            + (snapshot.IsBooked ? 4 : 0),
            15,
            99);

        var momentum = Clamp(
            50
            + (state.LastFightResult == "W" ? 12 : state.LastFightResult == "L" ? -10 : 0)
            + (snapshot.IsBooked ? 6 : 0)
            - Math.Max(snapshot.InjuryWeeksRemaining, snapshot.MedicalSuspensionWeeksRemaining) * 4,
            10,
            99);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE Fighters
SET Marketability = $marketability,
    Momentum = $momentum
WHERE Id = $fighterId;";
        cmd.Parameters.AddWithValue("$marketability", marketability);
        cmd.Parameters.AddWithValue("$momentum", momentum);
        cmd.Parameters.AddWithValue("$fighterId", snapshot.Id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static FighterStateRow BuildState(FighterSnapshot snapshot, string currentDate, int absoluteWeek)
    {
        var recoveryBurden = Math.Max(snapshot.WeeksUntilAvailable,
            Math.Max(snapshot.InjuryWeeksRemaining, snapshot.MedicalSuspensionWeeksRemaining));

        var inactivityWeeks = WeeksBetween(snapshot.LastFightDate, currentDate) ?? 12;
        var phase = DeterminePhase(snapshot, currentDate);
        var daysUntilFight = DaysBetween(currentDate, snapshot.NextFightDate);
        var upcomingWeeks = WeeksBetween(currentDate, snapshot.NextFightDate);
        var disciplineProxy = (snapshot.Cardio + snapshot.FightIQ + snapshot.Potential) / 3;
        var inCamp = phase.CurrentPhase is "Training Camp" or "Fight Week" or "Weight Cut";
        var inImmediateFightWindow = phase.CurrentPhase is "Fight Week" or "Weight Cut" or "Fight Night";

        var form = Clamp(
            50
            + (snapshot.LastFightResult == "W" ? 12 : snapshot.LastFightResult == "L" ? -6 : 0)
            + (phase.CurrentPhase == "Training Camp" ? 8 : 0)
            + (phase.CurrentPhase == "Fight Week" ? 10 : 0)
            + (phase.CurrentPhase == "Weight Cut" ? 6 : 0)
            + (phase.CurrentPhase == "Scheduled" ? 4 : 0)
            - recoveryBurden * 5
            + (snapshot.Potential - snapshot.Skill > 10 ? 4 : 0),
            15,
            95);

        var energy = Clamp(
            82
            - (phase.CurrentPhase == "Training Camp" && upcomingWeeks.HasValue ? Math.Max(6, 18 - (upcomingWeeks.Value * 2)) : 0)
            - (phase.CurrentPhase == "Fight Week" ? 18 : 0)
            - (phase.CurrentPhase == "Weight Cut" ? 24 : 0)
            - recoveryBurden * 6
            + (!snapshot.IsBooked ? 4 : 0),
            10,
            95);

        var sharpness = Clamp(
            46
            + (inCamp && upcomingWeeks.HasValue
                ? Math.Max(12, 32 - (upcomingWeeks.Value * 3))
                : Math.Max(-8, 20 - (inactivityWeeks * 2)))
            + (phase.CurrentPhase == "Fight Week" ? 8 : 0)
            + (phase.CurrentPhase == "Weight Cut" ? 6 : 0)
            + (snapshot.FightIQ / 8),
            10,
            95);

        var morale = Clamp(
            50
            + (snapshot.LastFightResult == "W" ? 12 : snapshot.LastFightResult == "L" ? -10 : 0)
            + (string.Equals(snapshot.ContractStatus, "Active", StringComparison.OrdinalIgnoreCase) ? 6 : 0)
            + (snapshot.IsBooked ? 3 : 0)
            + (inImmediateFightWindow ? 5 : 0)
            + (snapshot.Popularity >= 70 ? 4 : 0)
            - recoveryBurden * 4,
            10,
            95);

        var campQuality = inCamp
            ? Clamp(
                45
                + (disciplineProxy / 4)
                - (upcomingWeeks.HasValue ? Math.Max(0, 2 - upcomingWeeks.Value) * 6 : 0)
                - (phase.CurrentPhase == "Weight Cut" ? 4 : 0),
                15,
                90)
            : phase.CurrentPhase == "Scheduled" ? 58 : 50;

        var weightCutReadiness = snapshot.IsBooked
            ? Clamp(
                48
                + (snapshot.Cardio / 3)
                + (snapshot.FightIQ / 5)
                - (daysUntilFight.HasValue ? Math.Max(0, 10 - daysUntilFight.Value) * 2 : 0)
                - recoveryBurden * 4,
                10,
                95)
            : 55;

        var injuryRisk = Clamp(
            12
            + Math.Max(0, snapshot.Age - 30) * 2
            + recoveryBurden * 8
            + (inCamp ? 6 : 0)
            + (phase.CurrentPhase == "Weight Cut" ? 6 : 0)
            - (snapshot.Cardio / 10)
            - (snapshot.Chin / 14),
            5,
            95);

        return new FighterStateRow(
            snapshot.Id,
            form,
            energy,
            sharpness,
            morale,
            campQuality,
            weightCutReadiness,
            injuryRisk,
            phase.CurrentPhase,
            phase.NextMilestoneType,
            phase.NextMilestoneDate,
            snapshot.LastFightDate,
            snapshot.LastFightResult,
            absoluteWeek);
    }

    private static FighterPhaseRow DeterminePhase(FighterSnapshot snapshot, string currentDate)
    {
        if (!DateTime.TryParse(currentDate, out var current))
            current = DateTime.UtcNow.Date;

        if (snapshot.MedicalSuspensionWeeksRemaining > 0)
        {
            return new FighterPhaseRow(
                "Medical Suspension",
                "Medical Clearance",
                AddWeeks(current, snapshot.MedicalSuspensionWeeksRemaining));
        }

        if (snapshot.InjuryWeeksRemaining > 0)
        {
            return new FighterPhaseRow(
                "Recovery",
                "Recovery Check",
                AddWeeks(current, snapshot.InjuryWeeksRemaining));
        }

        if (snapshot.WeeksUntilAvailable > 0)
        {
            return new FighterPhaseRow(
                "Recovery",
                "Available Again",
                AddWeeks(current, snapshot.WeeksUntilAvailable));
        }

        if (snapshot.IsBooked && DateTime.TryParse(snapshot.NextFightDate, out var fightDate))
        {
            var campWeeks = Math.Max(1, snapshot.NextFightCampWeeks ?? 4);
            var campStartDate = fightDate.AddDays(-(campWeeks * 7));
            var fightWeekDate = fightDate.AddDays(-7);
            var weighInDate = fightDate.AddDays(-1);

            if (current >= fightDate.Date)
                return new FighterPhaseRow("Fight Night", "Aftermath", fightDate.ToString("yyyy-MM-dd"));

            if (current >= weighInDate.Date)
                return new FighterPhaseRow("Weight Cut", "Fight Night", fightDate.ToString("yyyy-MM-dd"));

            if (current >= fightWeekDate.Date)
                return new FighterPhaseRow("Fight Week", "Weigh-In", weighInDate.ToString("yyyy-MM-dd"));

            if (current >= campStartDate.Date)
                return new FighterPhaseRow("Training Camp", "Fight Week", fightWeekDate.ToString("yyyy-MM-dd"));

            return new FighterPhaseRow("Scheduled", "Camp Start", campStartDate.ToString("yyyy-MM-dd"));
        }

        return new FighterPhaseRow("Idle", null, null);
    }

    private static string DetermineBaseStyle(FighterSnapshot snapshot)
    {
        var balancedSpread = Max(snapshot.Striking, snapshot.Grappling, snapshot.Wrestling) - Min(snapshot.Striking, snapshot.Grappling, snapshot.Wrestling);
        if (balancedSpread <= 8 && snapshot.FightIQ >= 60)
            return "All-Rounder";

        if (snapshot.Grappling >= 75 && snapshot.SubWins >= Math.Max(2, snapshot.KOWins) && snapshot.Grappling - snapshot.Striking >= 8)
            return "Submission Grappler";

        if (snapshot.Wrestling >= 76 && snapshot.DecWins >= snapshot.KOWins && snapshot.DecWins >= snapshot.SubWins)
            return "Control Wrestler";

        if (snapshot.Wrestling >= 72 && snapshot.Striking >= 60 && snapshot.Cardio >= 60)
            return "Pressure Wrestler";

        if (snapshot.Striking >= 78 && snapshot.KOWins >= Math.Max(2, snapshot.SubWins))
            return "Knockout Striker";

        if (snapshot.Striking >= 70 && snapshot.Cardio >= 65 && snapshot.FightIQ >= 60)
            return "Kickboxer";

        return snapshot.Grappling >= snapshot.Striking
            ? "Grappling Specialist"
            : "Stand-Up Specialist";
    }

    private static string DetermineTacticalStyle(FighterSnapshot snapshot)
    {
        var finishRate = snapshot.Wins > 0
            ? (double)(snapshot.KOWins + snapshot.SubWins) / snapshot.Wins
            : 0;

        if (snapshot.Cardio >= 76 && snapshot.FightIQ >= 70)
            return "Attritional";

        if (finishRate >= 0.60 && snapshot.FightIQ < 60)
            return "Risky Finisher";

        if (snapshot.DecWins >= (snapshot.KOWins + snapshot.SubWins) && snapshot.FightIQ >= 70)
            return "Patient";

        if (snapshot.KOWins >= Math.Max(2, snapshot.DecWins) && snapshot.Cardio < 60)
            return "Fast Starter";

        if (snapshot.Popularity >= 70 || finishRate >= 0.50)
            return "Opportunistic";

        return "Measured";
    }

    private static string BuildStyleSummary(string baseStyle, string tacticalStyle, FighterSnapshot snapshot)
    {
        var finishRate = snapshot.Wins > 0
            ? ((snapshot.KOWins + snapshot.SubWins) * 100.0) / snapshot.Wins
            : 0;

        if (finishRate >= 55)
            return $"{baseStyle} with a {tacticalStyle.ToLowerInvariant()} edge and real finishing upside.";

        if (snapshot.DecWins >= snapshot.KOWins + snapshot.SubWins)
            return $"{baseStyle} who tends to build control through a {tacticalStyle.ToLowerInvariant()} tempo.";

        return $"{baseStyle} built around a {tacticalStyle.ToLowerInvariant()} approach.";
    }

    private static IReadOnlyList<FighterTraitRow> DetermineTraits(FighterSnapshot snapshot)
    {
        var finishRate = snapshot.Wins > 0
            ? ((snapshot.KOWins + snapshot.SubWins) * 100.0) / snapshot.Wins
            : 0;

        var traits = new List<FighterTraitRow>();

        if (snapshot.Cardio >= 78)
            traits.Add(new FighterTraitRow("Cardio Machine", snapshot.Cardio));

        if (snapshot.Chin >= 75)
            traits.Add(new FighterTraitRow("Durable", snapshot.Chin));

        if (snapshot.Striking >= 75 && snapshot.KOWins >= Math.Max(3, snapshot.Wins / 3))
            traits.Add(new FighterTraitRow("KO Threat", snapshot.Striking));

        if (snapshot.Grappling >= 75 && snapshot.SubWins >= Math.Max(3, snapshot.Wins / 3))
            traits.Add(new FighterTraitRow("Submission Hunter", snapshot.Grappling));

        if (snapshot.FightIQ >= 75)
            traits.Add(new FighterTraitRow("Technician", snapshot.FightIQ));

        if (snapshot.Popularity >= 70 || finishRate >= 60)
            traits.Add(new FighterTraitRow("Action Magnet", Math.Max(snapshot.Popularity, (int)Math.Round(finishRate))));

        if (snapshot.Potential - snapshot.Skill >= 14)
            traits.Add(new FighterTraitRow("Blue-Chip Prospect", snapshot.Potential - snapshot.Skill + 70));

        if (snapshot.Age >= 33 && snapshot.Wins >= 10)
            traits.Add(new FighterTraitRow("Seasoned Veteran", Math.Min(95, snapshot.Age + snapshot.Wins)));

        if (traits.Count == 0)
            traits.Add(new FighterTraitRow("Developing", Math.Max(45, snapshot.Potential)));

        return traits
            .OrderByDescending(x => x.Intensity)
            .ThenBy(x => x.TraitCode, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    private static int Max(int a, int b, int c) => Math.Max(a, Math.Max(b, c));
    private static int Min(int a, int b, int c) => Math.Min(a, Math.Min(b, c));

    private static int? WeeksBetween(string? startDate, string? endDate)
    {
        if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
            return null;

        var delta = end.Date - start.Date;
        return (int)Math.Floor(delta.TotalDays / 7d);
    }

    private static int? DaysBetween(string? startDate, string? endDate)
    {
        if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
            return null;

        return (int)Math.Floor((end.Date - start.Date).TotalDays);
    }

    private static string AddWeeks(DateTime current, int weeks)
        => current.AddDays(Math.Max(1, weeks) * 7).ToString("yyyy-MM-dd");

    private static async Task<int> LoadAbsoluteWeekAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(AbsoluteWeek, 0) FROM GameState LIMIT 1;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<string> LoadCurrentDateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(CurrentDate, '2026-01-01') FROM GameState LIMIT 1;";
        return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "2026-01-01";
    }

    private static async Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record FighterSnapshot(
        int Id,
        int Age,
        int Wins,
        int Losses,
        int Draws,
        int KOWins,
        int SubWins,
        int DecWins,
        int Skill,
        int Potential,
        int Popularity,
        int Striking,
        int Grappling,
        int Wrestling,
        int Cardio,
        int Chin,
        int FightIQ,
        string ContractStatus,
        bool IsBooked,
        int WeeksUntilAvailable,
        int InjuryWeeksRemaining,
        int MedicalSuspensionWeeksRemaining,
        string? LastFightDate,
        string? LastFightResult,
        string? NextFightDate,
        int? NextFightCampWeeks);

    private sealed record FighterTraitRow(string TraitCode, int Intensity);

    private sealed record FighterStateRow(
        int FighterId,
        int Form,
        int Energy,
        int Sharpness,
        int Morale,
        int CampQuality,
        int WeightCutReadiness,
        int InjuryRisk,
        string CurrentPhase,
        string? NextMilestoneType,
        string? NextMilestoneDate,
        string? LastFightDate,
        string? LastFightResult,
        int LastUpdatedWeek);

    private sealed record FighterPhaseRow(
        string CurrentPhase,
        string? NextMilestoneType,
        string? NextMilestoneDate);
}
