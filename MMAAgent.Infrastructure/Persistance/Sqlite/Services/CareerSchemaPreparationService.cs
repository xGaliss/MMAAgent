using Microsoft.Data.Sqlite;
using MMAAgent.Infrastructure.Persistence.Sqlite;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services;

public sealed class CareerSchemaPreparationService
{
    private static readonly string[] StandardWeightClasses =
    {
        "Flyweight",
        "Bantamweight",
        "Featherweight",
        "Lightweight",
        "Welterweight",
        "Middleweight",
        "LightHeavyweight",
        "Heavyweight"
    };

    private readonly SqliteConnectionFactory _factory;

    public CareerSchemaPreparationService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();

        await EnsureCountryCultureWeightsAsync(conn, cancellationToken);
        await EnsurePromotionConfigurationAsync(conn, cancellationToken);
        await EnsureAgentConfigurationAsync(conn, cancellationToken);
        await EnsureFighterAvailabilityColumnsAsync(conn, cancellationToken);
        await EnsureFighterWorldColumnsAsync(conn, cancellationToken);
        await EnsureEventColumnsAsync(conn, cancellationToken);
        await EnsureFightColumnsAsync(conn, cancellationToken);
        await EnsureFightOfferColumnsAsync(conn, cancellationToken);
        await EnsureContractOfferColumnsAsync(conn, cancellationToken);
        await EnsureFightHistoryColumnsAsync(conn, cancellationToken);
        await EnsureFightPreparationTablesAsync(conn, cancellationToken);
        await EnsureFighterWorldTablesAsync(conn, cancellationToken);
        await EnsureAgendaTablesAsync(conn, cancellationToken);
        await EnsureEcosystemTablesAsync(conn, cancellationToken);
    }

    private static async Task EnsurePromotionConfigurationAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Promotions", "CircuitType", "TEXT NOT NULL DEFAULT 'Professional'", cancellationToken);
        await EnsureColumnAsync(conn, "Promotions", "ParentPromotionId", "INTEGER NULL", cancellationToken);
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
    END,
    CircuitType = COALESCE(NULLIF(CircuitType, ''), 'Professional');", cancellationToken);

        await EnsureAmateurPromotionsAsync(conn, cancellationToken);
    }

    private static async Task EnsureAgentConfigurationAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "AgentProfile", "Nationality", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "AvatarKey", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "CampInvestmentLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "MedicalInvestmentLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "PublicReputation", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "FighterTrust", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "PromotionLeverage", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "ScoutingStaffLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "MediaStaffLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "NegotiationStaffLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(conn, "AgentProfile", "PerformanceStaffLevel", "INTEGER NOT NULL DEFAULT 1", cancellationToken);

        await ExecAsync(conn, @"
UPDATE AgentProfile
SET CampInvestmentLevel = CASE
        WHEN COALESCE(CampInvestmentLevel, -1) < 0 THEN 0
        WHEN COALESCE(CampInvestmentLevel, 1) > 2 THEN 2
        ELSE COALESCE(CampInvestmentLevel, 1)
    END,
    Nationality = COALESCE(NULLIF(Nationality, ''), 'Spain'),
    AvatarKey = COALESCE(NULLIF(AvatarKey, ''), 'Promoter'),
    PublicReputation = MIN(99, MAX(15, COALESCE(PublicReputation, 50))),
    FighterTrust = MIN(99, MAX(15, COALESCE(FighterTrust, 50))),
    PromotionLeverage = MIN(99, MAX(15, COALESCE(PromotionLeverage, 50))),
    ScoutingStaffLevel = CASE
        WHEN COALESCE(ScoutingStaffLevel, -1) < 0 THEN 0
        WHEN COALESCE(ScoutingStaffLevel, 1) > 2 THEN 2
        ELSE COALESCE(ScoutingStaffLevel, 1)
    END,
    MediaStaffLevel = CASE
        WHEN COALESCE(MediaStaffLevel, -1) < 0 THEN 0
        WHEN COALESCE(MediaStaffLevel, 1) > 2 THEN 2
        ELSE COALESCE(MediaStaffLevel, 1)
    END,
    NegotiationStaffLevel = CASE
        WHEN COALESCE(NegotiationStaffLevel, -1) < 0 THEN 0
        WHEN COALESCE(NegotiationStaffLevel, 1) > 2 THEN 2
        ELSE COALESCE(NegotiationStaffLevel, 1)
    END,
    PerformanceStaffLevel = CASE
        WHEN COALESCE(PerformanceStaffLevel, -1) < 0 THEN 0
        WHEN COALESCE(PerformanceStaffLevel, 1) > 2 THEN 2
        ELSE COALESCE(PerformanceStaffLevel, 1)
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

    private static async Task EnsureCountryCultureWeightsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        // Schema safety only.
        // Canonical country/culture rows live in the web template DB and its SQL seed asset.
        // Countries.CulturalGroup remains the legacy fallback/default used by the generator.
        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS CountryCultureWeights
(
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CountryId INTEGER NOT NULL,
    CulturalGroup TEXT NOT NULL,
    Weight INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(CountryId) REFERENCES Countries(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_CountryCultureWeights_CountryId ON CountryCultureWeights(CountryId);", cancellationToken);
        await ExecAsync(conn, "CREATE UNIQUE INDEX IF NOT EXISTS UX_CountryCultureWeights_Country_Culture ON CountryCultureWeights(CountryId, CulturalGroup);", cancellationToken);

        await EnsureColumnAsync(conn, "Fighters", "CulturalGroup", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureFighterWorldColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "Fighters", "CareerStage", "TEXT NOT NULL DEFAULT 'Pro'", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "AmateurWins", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "AmateurLosses", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "AmateurDraws", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Marketability", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Momentum", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "WeightMissCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "CampWithdrawalCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "ReliabilityScore", "INTEGER NOT NULL DEFAULT 60", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "MediaHeat", "INTEGER NOT NULL DEFAULT 20", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "DamageMiles", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "LastAgedYear", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Ambition", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Discipline", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "RiskTolerance", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Stability", "INTEGER NOT NULL DEFAULT 50", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "Showmanship", "INTEGER NOT NULL DEFAULT 40", cancellationToken);

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
    Ambition = CASE
        WHEN COALESCE(Ambition, 0) <= 0 THEN 50
        ELSE Ambition
    END,
    Discipline = CASE
        WHEN COALESCE(Discipline, 0) <= 0 THEN 50
        ELSE Discipline
    END,
    RiskTolerance = CASE
        WHEN COALESCE(RiskTolerance, 0) <= 0 THEN 50
        ELSE RiskTolerance
    END,
    Stability = CASE
        WHEN COALESCE(Stability, 0) <= 0 THEN 50
        ELSE Stability
    END,
    Showmanship = CASE
        WHEN COALESCE(Showmanship, 0) <= 0 THEN 40
        ELSE Showmanship
    END,
    CareerStage = CASE
        WHEN COALESCE(NULLIF(CareerStage, ''), '') <> '' THEN CareerStage
        WHEN COALESCE(Age, 18) <= 23
         AND (COALESCE(Wins, 0) + COALESCE(Losses, 0) + COALESCE(Draws, 0)) <= 8
         AND COALESCE(Skill, 50) <= 63
            THEN 'Amateur'
        ELSE 'Pro'
    END,
    AmateurWins = CASE
        WHEN COALESCE(AmateurWins, 0) > 0 THEN AmateurWins
        WHEN COALESCE(CareerStage, 'Pro') = 'Amateur' THEN COALESCE(Wins, 0)
        ELSE 0
    END,
    AmateurLosses = CASE
        WHEN COALESCE(AmateurLosses, 0) > 0 THEN AmateurLosses
        WHEN COALESCE(CareerStage, 'Pro') = 'Amateur' THEN COALESCE(Losses, 0)
        ELSE 0
    END,
    AmateurDraws = CASE
        WHEN COALESCE(AmateurDraws, 0) > 0 THEN AmateurDraws
        WHEN COALESCE(CareerStage, 'Pro') = 'Amateur' THEN COALESCE(Draws, 0)
        ELSE 0
    END,
    LastAgedYear = CASE
        WHEN COALESCE(LastAgedYear, 0) <= 0 THEN 2026
        ELSE LastAgedYear
    END;", cancellationToken);
    }

    private static async Task EnsureAmateurPromotionsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new AmateurPromotionSeed("MMA Futures League", 24, 22, 2, 34, 18),
            new AmateurPromotionSeed("Iberian Amateur Combat", 22, 20, 2, 32, 16),
            new AmateurPromotionSeed("Nordic Cage Path", 21, 18, 3, 33, 16),
            new AmateurPromotionSeed("Pan American Amateur Series", 23, 24, 2, 35, 18)
        };

        foreach (var definition in definitions)
        {
            var exists = await ScalarIntAsync(
                conn,
                "SELECT COUNT(*) FROM Promotions WHERE Name = $name LIMIT 1;",
                cancellationToken,
                ("$name", definition.Name));

            if (exists <= 0)
            {
                using var insertPromotion = conn.CreateCommand();
                insertPromotion.CommandText = @"
INSERT INTO Promotions
(
    Name,
    Prestige,
    Budget,
    IsActive,
    EventIntervalWeeks,
    NextEventWeek,
    MinSkillToSign,
    MinPopularityToSign,
    CircuitType,
    ParentPromotionId,
    TitleFightIntervalWeeks,
    MajorEventIntervalWeeks,
    EarlyPrelimFightCount,
    PrelimFightCount,
    MainCardFightCount,
    StandardCampWeeks,
    MajorCampWeeks,
    TitleCampWeeks,
    ShortNoticeCampWeeks,
    ShortNoticeMaxLeadWeeks
)
VALUES
(
    $name,
    $prestige,
    $budget,
    1,
    $eventIntervalWeeks,
    0,
    $minSkillToSign,
    $minPopularityToSign,
    'Amateur',
    NULL,
    99,
    12,
    0,
    2,
    2,
    3,
    4,
    5,
    1,
    2
);";
                insertPromotion.Parameters.AddWithValue("$name", definition.Name);
                insertPromotion.Parameters.AddWithValue("$prestige", definition.Prestige);
                insertPromotion.Parameters.AddWithValue("$budget", definition.Budget);
                insertPromotion.Parameters.AddWithValue("$eventIntervalWeeks", definition.EventIntervalWeeks);
                insertPromotion.Parameters.AddWithValue("$minSkillToSign", definition.MinSkillToSign);
                insertPromotion.Parameters.AddWithValue("$minPopularityToSign", definition.MinPopularityToSign);
                await insertPromotion.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var weightClass in StandardWeightClasses)
            {
                using var insertWeightClass = conn.CreateCommand();
                insertWeightClass.CommandText = @"
INSERT INTO PromotionWeightClasses (PromotionId, WeightClass, HasRanking, RankingSize)
SELECT p.Id, $weightClass, 0, 0
FROM Promotions p
WHERE p.Name = $promotionName
  AND NOT EXISTS
  (
      SELECT 1
      FROM PromotionWeightClasses pwc
      WHERE pwc.PromotionId = p.Id
        AND pwc.WeightClass = $weightClass
  );";
                insertWeightClass.Parameters.AddWithValue("$promotionName", definition.Name);
                insertWeightClass.Parameters.AddWithValue("$weightClass", weightClass);
                await insertWeightClass.ExecuteNonQueryAsync(cancellationToken);
            }
        }
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

    private static async Task EnsureContractOfferColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(conn, "ContractOffers", "SigningBonus", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(conn, "ContractOffers", "ExclusivityType", "TEXT NOT NULL DEFAULT 'Exclusive'", cancellationToken);
        await EnsureColumnAsync(conn, "Fighters", "ContractExclusivityType", "TEXT NOT NULL DEFAULT 'Exclusive'", cancellationToken);
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
        await EnsureColumnAsync(conn, "FightPreparations", "CampFocus", "TEXT NULL", cancellationToken);
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
CREATE TABLE IF NOT EXISTS AgentPromotionRelations
(
    AgentId INTEGER NOT NULL,
    PromotionId INTEGER NOT NULL,
    RelationshipScore INTEGER NOT NULL DEFAULT 50,
    LastUpdatedDate TEXT NULL,
    Notes TEXT NULL,
    PRIMARY KEY (AgentId, PromotionId),
    FOREIGN KEY(AgentId) REFERENCES AgentProfile(Id) ON DELETE CASCADE,
    FOREIGN KEY(PromotionId) REFERENCES Promotions(Id) ON DELETE CASCADE
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

        await ExecAsync(conn, @"
CREATE TABLE IF NOT EXISTS AmateurProspectWatchlist
(
    AgentId INTEGER NOT NULL,
    FighterId INTEGER NOT NULL,
    AddedDate TEXT NOT NULL DEFAULT '',
    LastAlertDate TEXT NULL,
    LastAlertType TEXT NULL,
    Notes TEXT NULL,
    PRIMARY KEY (AgentId, FighterId),
    FOREIGN KEY(AgentId) REFERENCES AgentProfile(Id) ON DELETE CASCADE,
    FOREIGN KEY(FighterId) REFERENCES Fighters(Id) ON DELETE CASCADE
);", cancellationToken);

        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ScoutKnowledge_AgentId ON ScoutKnowledge(AgentId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Storylines_Entity ON Storylines(EntityType, EntityId, Status);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ContenderQueue_Lookup ON ContenderQueue(PromotionId, WeightClass, QueueRank);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_AgentTransactions_AgentId_Date ON AgentTransactions(AgentId, TxDate DESC);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_AgentPromotionRelations_PromotionId ON AgentPromotionRelations(PromotionId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Rivalries_FighterA ON Rivalries(FighterAId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_Rivalries_FighterB ON Rivalries(FighterBId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_LegacyTags_FighterId ON LegacyTags(FighterId);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_DecisionEvents_Agent_Status ON DecisionEvents(AgentId, Status, CreatedDate DESC);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_ScoutAssignments_Agent_Status ON ScoutAssignments(AgentId, Status);", cancellationToken);
        await ExecAsync(conn, "CREATE INDEX IF NOT EXISTS IX_AmateurProspectWatchlist_AgentId ON AmateurProspectWatchlist(AgentId, LastAlertDate DESC);", cancellationToken);
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

    private static async Task<int> ScalarIntAsync(
        SqliteConnection conn,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private sealed record AmateurPromotionSeed(
        string Name,
        int Prestige,
        int Budget,
        int EventIntervalWeeks,
        int MinSkillToSign,
        int MinPopularityToSign);

}
