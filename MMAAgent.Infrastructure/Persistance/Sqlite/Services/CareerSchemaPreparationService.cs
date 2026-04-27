using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services;

public sealed class CareerSchemaPreparationService
{
    private readonly SqliteConnectionFactory _factory;

    public CareerSchemaPreparationService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();

        await EnsurePromotionConfigurationAsync(conn, cancellationToken);
        await EnsureAgentConfigurationAsync(conn, cancellationToken);
        await EnsureFighterAvailabilityColumnsAsync(conn, cancellationToken);
        await EnsureFighterWorldColumnsAsync(conn, cancellationToken);
        await EnsureEventColumnsAsync(conn, cancellationToken);
        await EnsureFightColumnsAsync(conn, cancellationToken);
        await EnsureFightOfferColumnsAsync(conn, cancellationToken);
        await EnsureFightHistoryColumnsAsync(conn, cancellationToken);
        await EnsureFightPreparationTablesAsync(conn, cancellationToken);
        await EnsureFighterWorldTablesAsync(conn, cancellationToken);
        await EnsureAgendaTablesAsync(conn, cancellationToken);
        await EnsureEcosystemTablesAsync(conn, cancellationToken);
    }

    private static async Task EnsurePromotionConfigurationAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Promotions", "TitleFightIntervalWeeks", "INTEGER NOT NULL DEFAULT 6", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "MajorEventIntervalWeeks", "INTEGER NOT NULL DEFAULT 6", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "EarlyPrelimFightCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "PrelimFightCount", "INTEGER NOT NULL DEFAULT 3", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "MainCardFightCount", "INTEGER NOT NULL DEFAULT 3", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "StandardCampWeeks", "INTEGER NOT NULL DEFAULT 4", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "MajorCampWeeks", "INTEGER NOT NULL DEFAULT 6", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "TitleCampWeeks", "INTEGER NOT NULL DEFAULT 8", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "ShortNoticeCampWeeks", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "ShortNoticeMaxLeadWeeks", "INTEGER NOT NULL DEFAULT 2", cancellationToken);

        await ExecAsync(conn, @"
UPDATE Promotions
SET TitleFightIntervalWeeks = CASE
        WHEN Prestige >= 90 THEN 4
        WHEN Prestige >= 75 THEN 5
        WHEN Prestige >= 60 THEN 6
        ELSE 8
    END,
    MajorEventIntervalWeeks = CASE
        WHEN Prestige >= 90 THEN 4
        WHEN Prestige >= 75 THEN 6
        ELSE 8
    END,
    EarlyPrelimFightCount = CASE
        WHEN Prestige >= 90 THEN 4
        WHEN Prestige >= 75 THEN 2
        ELSE 0
    END,
    PrelimFightCount = CASE
        WHEN Prestige >= 90 THEN 4
        WHEN Prestige >= 75 THEN 3
        ELSE 2
    END,
    MainCardFightCount = CASE
        WHEN Prestige >= 90 THEN 5
        WHEN Prestige >= 75 THEN 4
        ELSE 3
    END,
    StandardCampWeeks = CASE
        WHEN Prestige >= 90 THEN 5
        WHEN Prestige >= 75 THEN 4
        ELSE 3
    END,
    MajorCampWeeks = CASE
        WHEN Prestige >= 90 THEN 6
        WHEN Prestige >= 75 THEN 5
        ELSE 4
    END,
    TitleCampWeeks = CASE
        WHEN Prestige >= 90 THEN 8
        WHEN Prestige >= 75 THEN 7
        ELSE 6
    END,
    ShortNoticeCampWeeks = CASE
        WHEN Prestige >= 90 THEN 2
        ELSE 1
    END,
    ShortNoticeMaxLeadWeeks = CASE
        WHEN Prestige >= 90 THEN 2
        WHEN Prestige >= 75 THEN 2
        ELSE 3
    END;", cancellationToken);
    }

    private static async Task EnsureAgentConfigurationAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "AgentProfile", "CampInvestmentLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "MedicalInvestmentLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);

        await ExecAsync(conn, @"
UPDATE AgentProfile
SET CampInvestmentLevel = CASE
        WHEN COALESCE(CampInvestmentLevel, -1) < 0 THEN 0
        WHEN COALESCE(CampInvestmentLevel, 1) > 2 THEN 2
        ELSE COALESCE(CampInvestmentLevel, 1)
    END,
    MedicalInvestmentLevel = CASE
        WHEN COALESCE(MedicalInvestmentLevel, -1) < 0 THEN 0
        WHEN COALESCE(MedicalInvestmentLevel, 1) > 2 THEN 2
        ELSE COALESCE(MedicalInvestmentLevel, 1)
    END;", cancellationToken);
    }

    private static async Task EnsureFighterAvailabilityColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fighters", "MedicalSuspensionWeeksRemaining", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFighterWorldColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fighters", "Marketability", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Momentum", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "WeightMissCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "CampWithdrawalCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "ReliabilityScore", "INTEGER NOT NULL DEFAULT 60", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "MediaHeat", "INTEGER NOT NULL DEFAULT 20", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "DamageMiles", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "LastAgedYear", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        await ExecAsync(conn, @"
UPDATE Fighters
SET Marketability = CASE
        WHEN COALESCE(Marketability, 0) <= 0 THEN MIN(99, MAX(20, COALESCE(Popularity, 50)))
        ELSE Marketability
    END,
    Momentum = CASE
        WHEN COALESCE(Momentum, 0) <= 0 THEN 50
        ELSE Momentum
    END,
    WeightMissCount = CASE
        WHEN COALESCE(WeightMissCount, 0) < 0 THEN 0
        ELSE COALESCE(WeightMissCount, 0)
    END,
    CampWithdrawalCount = CASE
        WHEN COALESCE(CampWithdrawalCount, 0) < 0 THEN 0
        ELSE COALESCE(CampWithdrawalCount, 0)
    END,
    DamageMiles = CASE
        WHEN COALESCE(DamageMiles, 0) < 0 THEN 0
        ELSE COALESCE(DamageMiles, 0)
    END,
    ReliabilityScore = CASE
        WHEN COALESCE(ReliabilityScore, 0) <= 0 THEN 60
        ELSE ReliabilityScore
    END,
    MediaHeat = CASE
        WHEN COALESCE(MediaHeat, 0) <= 0 THEN MIN(99, MAX(10, COALESCE(Popularity, 20)))
        ELSE MediaHeat
    END,
    LastAgedYear = CASE
        WHEN COALESCE(LastAgedYear, 0) <= 0 THEN 2026
        ELSE LastAgedYear
    END;", cancellationToken);
    }

    private static async Task EnsureEventColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Events", "EventTier", "TEXT NOT NULL DEFAULT 'Standard'", cancellationToken);
        await EnsureColumnAsync(conn, "Events", "PlannedFightCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Events", "CompletedFightCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFightColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fights", "CardSegment", "TEXT NOT NULL DEFAULT 'Unassigned'", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "CardOrder", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "IsMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "IsCoMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "Purse", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "WinBonus", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "IsShortNotice", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fights", "IsTitleEliminator", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFightOfferColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "FightOffers", "IsShortNotice", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightOffers", "CampWeeksOffered", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightOffers", "IsTitleEliminator", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightOffers", "Notes", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureFightHistoryColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "FightHistory", "CardSegment", "TEXT NOT NULL DEFAULT 'Unassigned'", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "CardOrder", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "IsMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "IsCoMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "EventTier", "TEXT NOT NULL DEFAULT 'Standard'", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "IsTitleEliminator", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFightPreparationTablesAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS FightPreparations
(
    FightId INTEGER NOT NULL,
    FighterId INTEGER NOT NULL,
    CampWeeksPlanned INTEGER NOT NULL DEFAULT 0,
    CampStartProcessed INTEGER NOT NULL DEFAULT 0,
    CampOutcome TEXT NULL,
    CampNotes TEXT NULL,
    FightWeekProcessed INTEGER NOT NULL DEFAULT 0,
    FightWeekOutcome TEXT NULL,
    FightWeekNotes TEXT NULL,
    WeighInProcessed INTEGER NOT NULL DEFAULT 0,
    WeighInOutcome TEXT NULL,
    WeighInNotes TEXT NULL,
    AftermathProcessed INTEGER NOT NULL DEFAULT 0,
    LastUpdatedDate TEXT NULL,
    PRIMARY KEY (FightId, FighterId),
    FOREIGN KEY(FightId) REFERENCES Fights(Id) ON DELETE CASCADE,
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await EnsureColumnAsync(conn, "FightPreparations", "FightWeekOutcome", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "FightPreparations", "ManagerDecisionType", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "FightPreparations", "ManagerDecisionChoice", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "FightPreparations", "PerformanceModifier", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightPreparations", "RiskModifier", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightPreparations", "DecisionNotes", "TEXT NULL", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_FightPreparations_FighterId ON FightPreparations(FighterId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_FightPreparations_FightId ON FightPreparations(FightId);", cancellationToken);
    }

    private static async Task EnsureFighterWorldTablesAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS FighterStates
(
    FighterId INTEGER NOT NULL PRIMARY KEY,
    Form INTEGER NOT NULL DEFAULT 50,
    Energy INTEGER NOT NULL DEFAULT 70,
    Sharpness INTEGER NOT NULL DEFAULT 50,
    Morale INTEGER NOT NULL DEFAULT 50,
    CampQuality INTEGER NOT NULL DEFAULT 50,
    WeightCutReadiness INTEGER NOT NULL DEFAULT 55,
    InjuryRisk INTEGER NOT NULL DEFAULT 20,
    CurrentPhase TEXT NOT NULL DEFAULT 'Idle',
    NextMilestoneType TEXT NULL,
    NextMilestoneDate TEXT NULL,
    LastFightDate TEXT NULL,
    LastFightResult TEXT NULL,
    LastUpdatedWeek INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await EnsureColumnAsync(conn, "FighterStates", "CurrentPhase", "TEXT NOT NULL DEFAULT 'Idle'", cancellationToken);
        await EnsureColumnAsync(conn, "FighterStates", "NextMilestoneType", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "FighterStates", "NextMilestoneDate", "TEXT NULL", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS FighterStyles
(
    FighterId INTEGER NOT NULL PRIMARY KEY,
    BaseStyle TEXT NOT NULL DEFAULT 'All-Rounder',
    TacticalStyle TEXT NOT NULL DEFAULT 'Measured',
    StyleSummary TEXT NOT NULL DEFAULT '',
    LastRecomputedWeek INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS FighterTraits
(
    FighterId INTEGER NOT NULL,
    TraitCode TEXT NOT NULL,
    Intensity INTEGER NOT NULL DEFAULT 0,
    Source TEXT NOT NULL DEFAULT 'Derived',
    PRIMARY KEY(FighterId, TraitCode),
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_FighterTraits_FighterId ON FighterTraits(FighterId);", cancellationToken);
    }

    private static async Task EnsureAgendaTablesAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS TimeQueue
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ScheduledDate TEXT NOT NULL,
    EventType TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NOT NULL DEFAULT 0,
    Priority INTEGER NOT NULL DEFAULT 50,
    Headline TEXT NOT NULL DEFAULT '',
    Subtitle TEXT NULL,
    MetadataJson TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'Pending'
);", cancellationToken);

        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_TimeQueue_ScheduledDate ON TimeQueue(ScheduledDate);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_TimeQueue_Status_Priority ON TimeQueue(Status, Priority DESC);", cancellationToken);
    }

    private static async Task EnsureEcosystemTablesAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS ScoutKnowledge
(
    AgentId INTEGER NOT NULL,
    FighterId INTEGER NOT NULL,
    Confidence INTEGER NOT NULL DEFAULT 50,
    EstimatedSkillMin INTEGER NOT NULL DEFAULT 1,
    EstimatedSkillMax INTEGER NOT NULL DEFAULT 99,
    EstimatedPotentialMin INTEGER NOT NULL DEFAULT 1,
    EstimatedPotentialMax INTEGER NOT NULL DEFAULT 99,
    EstimatedStrikingMin INTEGER NOT NULL DEFAULT 1,
    EstimatedStrikingMax INTEGER NOT NULL DEFAULT 99,
    EstimatedGrapplingMin INTEGER NOT NULL DEFAULT 1,
    EstimatedGrapplingMax INTEGER NOT NULL DEFAULT 99,
    EstimatedWrestlingMin INTEGER NOT NULL DEFAULT 1,
    EstimatedWrestlingMax INTEGER NOT NULL DEFAULT 99,
    EstimatedCardioMin INTEGER NOT NULL DEFAULT 1,
    EstimatedCardioMax INTEGER NOT NULL DEFAULT 99,
    EstimatedChinMin INTEGER NOT NULL DEFAULT 1,
    EstimatedChinMax INTEGER NOT NULL DEFAULT 99,
    EstimatedFightIQMin INTEGER NOT NULL DEFAULT 1,
    EstimatedFightIQMax INTEGER NOT NULL DEFAULT 99,
    LastUpdatedDate TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (AgentId, FighterId),
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS Storylines
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityId INTEGER NOT NULL,
    StoryType TEXT NOT NULL,
    Headline TEXT NOT NULL,
    Body TEXT NOT NULL,
    Intensity INTEGER NOT NULL DEFAULT 50,
    Status TEXT NOT NULL DEFAULT 'Active',
    LastUpdatedDate TEXT NOT NULL DEFAULT ''
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS ContenderQueue
(
    PromotionId INTEGER NOT NULL,
    WeightClass TEXT NOT NULL,
    FighterId INTEGER NOT NULL,
    QueueScore INTEGER NOT NULL DEFAULT 0,
    QueueRank INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    LastUpdatedDate TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (PromotionId, WeightClass, FighterId),
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS AgentTransactions
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AgentId INTEGER NOT NULL,
    TxDate TEXT NOT NULL,
    Amount INTEGER NOT NULL DEFAULT 0,
    TxType TEXT NOT NULL,
    Notes TEXT NULL
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS Rivalries
(
    FighterAId INTEGER NOT NULL,
    FighterBId INTEGER NOT NULL,
    FightCount INTEGER NOT NULL DEFAULT 0,
    SplitSeries INTEGER NOT NULL DEFAULT 0,
    TitleFightCount INTEGER NOT NULL DEFAULT 0,
    Intensity INTEGER NOT NULL DEFAULT 0,
    Summary TEXT NOT NULL DEFAULT '',
    LastFightDate TEXT NULL,
    LastUpdatedDate TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (FighterAId, FighterBId)
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS LegacyTags
(
    FighterId INTEGER NOT NULL,
    TagCode TEXT NOT NULL,
    Summary TEXT NOT NULL DEFAULT '',
    Intensity INTEGER NOT NULL DEFAULT 0,
    LastUpdatedDate TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (FighterId, TagCode),
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS DecisionEvents
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AgentId INTEGER NOT NULL,
    FighterId INTEGER NULL,
    FightId INTEGER NULL,
    DecisionType TEXT NOT NULL,
    Headline TEXT NOT NULL,
    Body TEXT NOT NULL,
    OptionAKey TEXT NOT NULL,
    OptionALabel TEXT NOT NULL,
    OptionADescription TEXT NULL,
    OptionBKey TEXT NOT NULL,
    OptionBLabel TEXT NOT NULL,
    OptionBDescription TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'Pending',
    CreatedDate TEXT NOT NULL DEFAULT '',
    ResolvedDate TEXT NULL,
    OutcomeSummary TEXT NULL
);", cancellationToken);

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS ScoutAssignments
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AgentId INTEGER NOT NULL,
    FighterId INTEGER NOT NULL,
    Focus TEXT NOT NULL DEFAULT 'General',
    Status TEXT NOT NULL DEFAULT 'InProgress',
    ProgressDays INTEGER NOT NULL DEFAULT 0,
    DaysRequired INTEGER NOT NULL DEFAULT 3,
    StartedDate TEXT NOT NULL DEFAULT '',
    CompletedDate TEXT NULL
);", cancellationToken);

        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ScoutKnowledge_AgentId ON ScoutKnowledge(AgentId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Storylines_Entity ON Storylines(EntityType, EntityId, Status);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ContenderQueue_Lookup ON ContenderQueue(PromotionId, WeightClass, QueueRank);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_AgentTransactions_AgentId_Date ON AgentTransactions(AgentId, TxDate DESC);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Rivalries_FighterA ON Rivalries(FighterAId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Rivalries_FighterB ON Rivalries(FighterBId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_LegacyTags_FighterId ON LegacyTags(FighterId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_DecisionEvents_Agent_Status ON DecisionEvents(AgentId, Status, CreatedDate DESC);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ScoutAssignments_Agent_Status ON ScoutAssignments(AgentId, Status);", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection conn,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(conn, tableName, columnName, cancellationToken))
            return;

        await ExecAsync(conn, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};", cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection conn,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
