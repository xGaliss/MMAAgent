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
        await EnsureFighterAvailabilityColumnsAsync(conn, cancellationToken);
        await EnsureFighterWorldColumnsAsync(conn, cancellationToken);
        await EnsureEventColumnsAsync(conn, cancellationToken);
        await EnsureFightColumnsAsync(conn, cancellationToken);
        await EnsureFightOfferColumnsAsync(conn, cancellationToken);
        await EnsureFightHistoryColumnsAsync(conn, cancellationToken);
        await EnsureFighterWorldTablesAsync(conn, cancellationToken);
        await EnsureAgendaTablesAsync(conn, cancellationToken);
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

    private static async Task EnsureFighterAvailabilityColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fighters", "MedicalSuspensionWeeksRemaining", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFighterWorldColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fighters", "Marketability", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Momentum", "INTEGER NOT NULL DEFAULT 50", cancellationToken);

        await ExecAsync(conn, @"
UPDATE Fighters
SET Marketability = CASE
        WHEN COALESCE(Marketability, 0) <= 0 THEN MIN(99, MAX(20, COALESCE(Popularity, 50)))
        ELSE Marketability
    END,
    Momentum = CASE
        WHEN COALESCE(Momentum, 0) <= 0 THEN 50
        ELSE Momentum
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
    }

    private static async Task EnsureFightOfferColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "FightOffers", "IsShortNotice", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightOffers", "CampWeeksOffered", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task EnsureFightHistoryColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "FightHistory", "CardSegment", "TEXT NOT NULL DEFAULT 'Unassigned'", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "CardOrder", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "IsMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "IsCoMainEvent", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "FightHistory", "EventTier", "TEXT NOT NULL DEFAULT 'Standard'", cancellationToken);
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
