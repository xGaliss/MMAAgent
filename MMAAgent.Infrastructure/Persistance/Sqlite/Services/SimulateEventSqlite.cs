using Microsoft.Data.Sqlite;
using MMAAgent.Application.Simulation;
using MMAAgent.Domain.Common;
using MMAAgent.Infrastructure.Persistence.Sqlite;
using System.Linq;
using System.Text;

namespace MMAAgent.Infrastructure.Persistence.Sqlite.Services
{
    public sealed class SimulateEventSqlite : IEventSimulator
    {
        private readonly SqliteConnectionFactory _factory;
        private readonly IContractServiceSqlite _contracts;

        public double TitleFightChance { get; set; } = 0.20;
        public double Randomness { get; set; } = 0.18;

        public SimulateEventSqlite(SqliteConnectionFactory factory, IContractServiceSqlite contracts)
        {
            _factory = factory;
            _contracts = contracts;
        }

        public async Task SimulatePromotionEventAsync(int promotionId, GameState state)
        {
            var eventDate = state.CurrentDate;
            var derivedAbsoluteWeek = ToAbsoluteWeek(state.CurrentYear, state.CurrentWeek);

            using var conn = _factory.CreateConnection();
            using var tx = conn.BeginTransaction();

            var promotion = await LoadPromotionPlanAsync(conn, tx, promotionId);
            if (promotion is null)
            {
                tx.Commit();
                return;
            }

            var absoluteWeek = ResolveEventWeek(derivedAbsoluteWeek, promotion.NextEventWeek);
            var seed = DeriveEventSeed(state.WorldSeed, state.CurrentYear, absoluteWeek, promotionId);
            var rng = new Random(seed);
            var isTitleWindow = IsIntervalWeek(absoluteWeek, promotion.TitleFightIntervalWeeks);
            var isMajorEvent = isTitleWindow || IsIntervalWeek(absoluteWeek, promotion.MajorEventIntervalWeeks);
            var eventTier = isMajorEvent ? "Major" : "Standard";
            var configuredFightCount = ComputePlannedFightCount(promotion, isMajorEvent);

            var currentEventName = BuildEventName(promotion.Name, absoluteWeek);
            var runnableEvent = await FindRunnableEventAsync(conn, tx, promotionId, currentEventName, eventDate);
            var scheduledBouts = runnableEvent is null
                ? new List<PlannedBout>()
                : await LoadScheduledBoutsAsync(conn, tx, runnableEvent.Id);
            var fightersUsedThisEvent = new HashSet<int>();
            var lineup = new List<PlannedBout>();

            foreach (var bout in scheduledBouts)
            {
                if (!fightersUsedThisEvent.Add(bout.FighterAId))
                    continue;

                if (!fightersUsedThisEvent.Add(bout.FighterBId))
                {
                    fightersUsedThisEvent.Remove(bout.FighterAId);
                    continue;
                }

                lineup.Add(bout);
            }

            var autoCandidates = await BuildAutoBoutCandidatesAsync(
                conn,
                tx,
                promotionId,
                fightersUsedThisEvent,
                isTitleWindow,
                isMajorEvent);

            var remainingSlots = Math.Max(0, configuredFightCount - lineup.Count);
            foreach (var candidate in autoCandidates
                         .OrderByDescending(x => x.Score)
                         .ThenByDescending(x => x.IsTitleFight)
                         .ThenBy(x => x.WeightClass, StringComparer.OrdinalIgnoreCase))
            {
                if (remainingSlots <= 0)
                    break;

                if (fightersUsedThisEvent.Contains(candidate.FighterAId) ||
                    fightersUsedThisEvent.Contains(candidate.FighterBId))
                    continue;

                lineup.Add(candidate);
                fightersUsedThisEvent.Add(candidate.FighterAId);
                fightersUsedThisEvent.Add(candidate.FighterBId);
                remainingSlots--;
            }

            if (lineup.Count == 0)
            {
                tx.Commit();
                return;
            }

            var eventId = runnableEvent?.Id ?? await EnsureWeeklyEventAsync(conn, tx, promotionId, eventDate, currentEventName, "TBD");
            var eventName = runnableEvent?.Name ?? currentEventName;
            var eventDateToUse = string.IsNullOrWhiteSpace(runnableEvent?.EventDate)
                ? eventDate
                : runnableEvent!.EventDate;
            var actualPlannedFightCount = Math.Max(configuredFightCount, lineup.Count);
            var cardAssignments = AssignCardSlots(lineup, promotion);

            await UpdateEventMetadataAsync(conn, tx, eventId, eventDateToUse, eventName, eventTier, actualPlannedFightCount, cardAssignments.Count);

            var touchedWeightClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine($"===== SIM EVENT: {promotion.Name} (EventId={eventId}, Tier={eventTier}, Date={eventDateToUse}, seed={seed}) =====");

            foreach (var card in cardAssignments.OrderBy(x => x.CardOrder))
            {
                var result = await SimFightAsync(conn, tx, rng, eventId, promotionId, card, eventDateToUse, eventTier, absoluteWeek);

                if (card.Bout.ExistingFightId.HasValue)
                {
                    await UpdateScheduledFightAsync(
                        conn,
                        tx,
                        card.Bout.ExistingFightId.Value,
                        result.WinnerId,
                        result.Method,
                        null,
                        eventDateToUse,
                        card.CardSegment,
                        card.CardOrder,
                        card.IsMainEvent,
                        card.IsCoMainEvent);
                }
                else
                {
                    await InsertCompletedFightAsync(
                        conn,
                        tx,
                        card.Bout.FighterAId,
                        card.Bout.FighterBId,
                        result.WinnerId,
                        result.Method,
                        eventDateToUse,
                        eventId,
                        card.Bout.WeightClass,
                        card.Bout.IsTitleFight,
                        card.CardSegment,
                        card.CardOrder,
                        card.IsMainEvent,
                        card.IsCoMainEvent);
                }

                touchedWeightClasses.Add(card.Bout.WeightClass);
                sb.AppendLine($"[{card.CardSegment} #{card.CardOrder}] {result.Summary}");
            }

            foreach (var weightClass in touchedWeightClasses)
            {
                var rankingSize = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(RankingSize, 10)
FROM PromotionWeightClasses
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                    ("$p", promotionId),
                    ("$wc", weightClass));

                await RebuildDivisionRankingsAndEnsureTitleAsync(conn, tx, promotionId, weightClass, Math.Max(2, rankingSize));
            }

            sb.AppendLine("===== END EVENT =====");
            System.Diagnostics.Debug.WriteLine(sb.ToString());

            tx.Commit();
        }

        private static async Task<EventRuntimeSnapshot?> FindRunnableEventAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int promotionId,
            string currentEventName,
            string currentDate)
        {
            using var currentCmd = conn.CreateCommand();
            currentCmd.Transaction = tx;
            currentCmd.CommandText = @"
SELECT Id, COALESCE(Name, '') AS Name, COALESCE(EventDate, '') AS EventDate
FROM Events
WHERE PromotionId = $promotionId
  AND Name = $eventName
LIMIT 1;";
            currentCmd.Parameters.AddWithValue("$promotionId", promotionId);
            currentCmd.Parameters.AddWithValue("$eventName", currentEventName);

            using (var reader = await currentCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    return new EventRuntimeSnapshot(
                        Convert.ToInt32(reader["Id"]),
                        reader["Name"]?.ToString() ?? currentEventName,
                        reader["EventDate"]?.ToString() ?? currentDate);
                }
            }

            using var overdueCmd = conn.CreateCommand();
            overdueCmd.Transaction = tx;
            overdueCmd.CommandText = @"
SELECT e.Id, COALESCE(e.Name, '') AS Name, COALESCE(e.EventDate, '') AS EventDate
FROM Events e
WHERE e.PromotionId = $promotionId
  AND COALESCE(e.EventDate, '') <> ''
  AND e.EventDate <= $currentDate
  AND EXISTS
  (
      SELECT 1
      FROM Fights f
      WHERE f.EventId = e.Id
        AND f.Method = 'Scheduled'
  )
ORDER BY e.EventDate, e.Id
LIMIT 1;";
            overdueCmd.Parameters.AddWithValue("$promotionId", promotionId);
            overdueCmd.Parameters.AddWithValue("$currentDate", currentDate);

            using var overdueReader = await overdueCmd.ExecuteReaderAsync();
            if (!await overdueReader.ReadAsync())
                return null;

            return new EventRuntimeSnapshot(
                Convert.ToInt32(overdueReader["Id"]),
                overdueReader["Name"]?.ToString() ?? currentEventName,
                overdueReader["EventDate"]?.ToString() ?? currentDate);
        }

        private async Task<SimFightResult> SimFightAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            Random rng,
            int eventId,
            int promotionId,
            CardAssignment card,
            string eventDate,
            string eventTier,
            int currentAbsoluteWeek)
        {
            var fighterA = await GetFighterAsync(conn, tx, card.Bout.FighterAId);
            var fighterB = await GetFighterAsync(conn, tx, card.Bout.FighterBId);

            if (fighterA is null || fighterB is null)
                return new SimFightResult(0, 0, "ERROR", card.Bout.IsTitleFight, "ERROR: fighter missing");

            if (fighterA.Retired != 0 || fighterB.Retired != 0)
                return new SimFightResult(0, 0, "SKIP", card.Bout.IsTitleFight, "SKIP: retired fighter");

            var prepA = card.Bout.ExistingFightId.HasValue
                ? await LoadPrepEffectAsync(conn, tx, card.Bout.ExistingFightId.Value, fighterA.Id)
                : PrepEffect.None;
            var prepB = card.Bout.ExistingFightId.HasValue
                ? await LoadPrepEffectAsync(conn, tx, card.Bout.ExistingFightId.Value, fighterB.Id)
                : PrepEffect.None;

            var aPower = CombatPower(fighterA, prepA, rng);
            var bPower = CombatPower(fighterB, prepB, rng);
            var probabilityA = 1.0 / (1.0 + Math.Exp(-(aPower - bPower) / 12.0));
            probabilityA = Clamp01(probabilityA * (1.0 - Randomness) + rng.NextDouble() * Randomness);

            var aWins = rng.NextDouble() < probabilityA;
            var winner = aWins ? fighterA : fighterB;
            var loser = aWins ? fighterB : fighterA;
            var winnerPrep = aWins ? prepA : prepB;
            var loserPrep = aWins ? prepB : prepA;
            var method = DecideMethod(winner, loser, winnerPrep, loserPrep, rng);
            var fightNotes = BuildFightNotes(fighterA, fighterB, prepA, prepB);

            await ApplyResultAsync(conn, tx, rng, winner.Id, loser.Id, method, card.Bout.IsTitleFight);
            await InsertFightHistoryAsync(
                conn,
                tx,
                eventDate,
                promotionId,
                card.Bout.WeightClass,
                fighterA.Id,
                fighterB.Id,
                winner.Id,
                loser.Id,
                method,
                card.Bout.IsTitleFight,
                fightNotes,
                eventId,
                card.CardSegment,
                card.CardOrder,
                card.IsMainEvent,
                card.IsCoMainEvent,
                eventTier);

            await _contracts.PostFightContractTickAsync(conn, tx, winner.Id);
            await _contracts.PostFightContractTickAsync(conn, tx, loser.Id);
            await ApplyRecoveryAsync(conn, tx, winner.Id, loser.Id, method, currentAbsoluteWeek, rng);

            if (card.Bout.IsTitleFight)
            {
                var championId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                    ("$p", promotionId),
                    ("$wc", card.Bout.WeightClass));

                if (championId == loser.Id)
                {
                    await ExecAsync(conn, tx, @"
UPDATE Titles
SET ChampionFighterId = $winnerId
WHERE PromotionId = $p AND WeightClass = $wc;",
                        ("$winnerId", winner.Id),
                        ("$p", promotionId),
                        ("$wc", card.Bout.WeightClass));
                }
            }

            var summary = $"{winner.FullName} def. {loser.FullName} via {method}" + (card.Bout.IsTitleFight ? " (TITLE)" : "");
            return new SimFightResult(winner.Id, loser.Id, method, card.Bout.IsTitleFight, summary);
        }

        private async Task<List<PlannedBout>> BuildAutoBoutCandidatesAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int promotionId,
            HashSet<int> fightersAlreadyUsed,
            bool isTitleWindow,
            bool isMajorEvent)
        {
            var divisions = await QueryAsync(conn, tx, @"
SELECT WeightClass, HasRanking
FROM PromotionWeightClasses
WHERE PromotionId = $p;",
                ("$p", promotionId));

            var candidates = new List<PlannedBout>();

            foreach (var division in divisions)
            {
                if (division.GetInt("HasRanking") != 1)
                    continue;

                var weightClass = division.GetString("WeightClass");
                var ranked = await LoadEligibleRankedDivisionAsync(conn, tx, promotionId, weightClass);
                if (ranked.Count < 2)
                    continue;

                var localReserved = new HashSet<int>();
                var championId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                    ("$p", promotionId),
                    ("$wc", weightClass));

                if (isTitleWindow && championId > 0)
                {
                    var championIsManaged = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM ManagedFighters
WHERE FighterId = $fid
  AND IsActive = 1;",
                        ("$fid", championId)) > 0;

                    if (!championIsManaged && !fightersAlreadyUsed.Contains(championId))
                    {
                        var champion = await LoadEligibleFighterAsync(conn, tx, championId);
                        var challenger = ranked.FirstOrDefault(x => x.FighterId != championId && !fightersAlreadyUsed.Contains(x.FighterId));

                        if (champion is not null && challenger is not null)
                        {
                            candidates.Add(CreatePlannedBout(
                                null,
                                champion.Id,
                                challenger.FighterId,
                                weightClass,
                                true,
                                champion.Skill,
                                champion.Popularity,
                                challenger.Skill,
                                challenger.Popularity));

                            localReserved.Add(champion.Id);
                            localReserved.Add(challenger.FighterId);
                        }
                    }
                }

                var remaining = ranked
                    .Where(x => !fightersAlreadyUsed.Contains(x.FighterId) && !localReserved.Contains(x.FighterId))
                    .ToList();

                var maxPairs = isMajorEvent ? 3 : 2;
                for (var i = 0; i + 1 < remaining.Count && maxPairs > 0; i += 2, maxPairs--)
                {
                    var fighterA = remaining[i];
                    var fighterB = remaining[i + 1];
                    if (fighterA.FighterId == fighterB.FighterId)
                        continue;

                    candidates.Add(CreatePlannedBout(
                        null,
                        fighterA.FighterId,
                        fighterB.FighterId,
                        weightClass,
                        false,
                        fighterA.Skill,
                        fighterA.Popularity,
                        fighterB.Skill,
                        fighterB.Popularity));
                }
            }

            return candidates;
        }

        private static async Task<List<PlannedBout>> LoadScheduledBoutsAsync(SqliteConnection conn, SqliteTransaction tx, int eventId)
        {
            var rows = await QueryAsync(conn, tx, @"
SELECT Id, FighterAId, FighterBId, WeightClass, IsTitleFight
FROM Fights
WHERE EventId = $eventId
  AND Method = 'Scheduled';",
                ("$eventId", eventId));

            var bouts = new List<PlannedBout>();
            foreach (var row in rows)
            {
                var fighterA = await GetFighterAsync(conn, tx, row.GetInt("FighterAId"));
                var fighterB = await GetFighterAsync(conn, tx, row.GetInt("FighterBId"));
                if (fighterA is null || fighterB is null)
                    continue;

                bouts.Add(CreatePlannedBout(
                    row.GetInt("Id"),
                    fighterA.Id,
                    fighterB.Id,
                    row.GetString("WeightClass"),
                    row.GetInt("IsTitleFight") == 1,
                    fighterA.Skill,
                    fighterA.Popularity,
                    fighterB.Skill,
                    fighterB.Popularity));
            }

            return bouts;
        }

        private static PlannedBout CreatePlannedBout(
            int? existingFightId,
            int fighterAId,
            int fighterBId,
            string weightClass,
            bool isTitleFight,
            int fighterASkill,
            int fighterAPopularity,
            int fighterBSkill,
            int fighterBPopularity)
        {
            var score = (fighterASkill + fighterBSkill) * 1.35
                        + (fighterAPopularity + fighterBPopularity) * 1.15
                        + (isTitleFight ? 150 : 0);

            return new PlannedBout(existingFightId, fighterAId, fighterBId, weightClass, isTitleFight, score);
        }

        private static List<CardAssignment> AssignCardSlots(List<PlannedBout> lineup, PromotionPlan promotion)
        {
            if (lineup.Count == 0)
                return new List<CardAssignment>();

            var ordered = lineup.OrderByDescending(x => x.Score).ThenByDescending(x => x.IsTitleFight).ToList();
            var mainCardCount = Math.Min(promotion.MainCardFightCount, ordered.Count);
            var prelimCount = Math.Min(promotion.PrelimFightCount, Math.Max(0, ordered.Count - mainCardCount));
            var earlyCount = Math.Max(0, ordered.Count - mainCardCount - prelimCount);

            var mainCard = ordered.Take(mainCardCount).OrderBy(x => x.Score).ToList();
            var prelims = ordered.Skip(mainCardCount).Take(prelimCount).OrderBy(x => x.Score).ToList();
            var earlyPrelims = ordered.Skip(mainCardCount + prelimCount).Take(earlyCount).OrderBy(x => x.Score).ToList();

            var assignments = new List<CardAssignment>(ordered.Count);
            var cardOrder = 1;

            foreach (var bout in earlyPrelims)
                assignments.Add(new CardAssignment(bout, "EarlyPrelims", cardOrder++, false, false));

            foreach (var bout in prelims)
                assignments.Add(new CardAssignment(bout, "Prelims", cardOrder++, false, false));

            for (var i = 0; i < mainCard.Count; i++)
            {
                var isCoMain = i == Math.Max(0, mainCard.Count - 2) && mainCard.Count >= 2;
                var isMain = i == mainCard.Count - 1;
                assignments.Add(new CardAssignment(mainCard[i], "MainCard", cardOrder++, isMain, isCoMain));
            }

            return assignments;
        }

        private static int ComputePlannedFightCount(PromotionPlan promotion, bool isMajorEvent)
        {
            var earlyPrelims = isMajorEvent ? promotion.EarlyPrelimFightCount : Math.Max(0, promotion.EarlyPrelimFightCount - 1);
            return Math.Max(1, promotion.MainCardFightCount + promotion.PrelimFightCount + earlyPrelims);
        }

        private static bool IsIntervalWeek(int absoluteWeek, int intervalWeeks)
            => intervalWeeks <= 1 || absoluteWeek % Math.Max(1, intervalWeeks) == 0;

        private static int ResolveEventWeek(int currentAbsoluteWeek, int nextEventWeek)
        {
            if (nextEventWeek > 0 && nextEventWeek <= currentAbsoluteWeek)
                return nextEventWeek;

            return currentAbsoluteWeek;
        }

        private static async Task ApplyRecoveryAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int winnerId,
            int loserId,
            string method,
            int currentAbsoluteWeek,
            Random rng)
        {
            await UpdateFighterAvailabilityAsync(conn, tx, winnerId, BuildRecoveryPlan(method, true, rng), currentAbsoluteWeek);
            await UpdateFighterAvailabilityAsync(conn, tx, loserId, BuildRecoveryPlan(method, false, rng), currentAbsoluteWeek);
        }

        private static RecoveryPlan BuildRecoveryPlan(string method, bool winnerWon, Random rng)
        {
            if (method == "KO/TKO")
            {
                var medical = winnerWon ? rng.Next(4, 7) : rng.Next(10, 17);
                var injury = winnerWon ? RollInjuryWeeks(rng, 0.14, 2, 5) : RollInjuryWeeks(rng, 0.45, 8, 18);
                return new RecoveryPlan(medical, injury);
            }

            if (method == "SUB")
            {
                var medical = winnerWon ? rng.Next(3, 6) : rng.Next(6, 10);
                var injury = winnerWon ? RollInjuryWeeks(rng, 0.08, 2, 4) : RollInjuryWeeks(rng, 0.20, 4, 8);
                return new RecoveryPlan(medical, injury);
            }

            var decisionMedical = winnerWon ? rng.Next(2, 5) : rng.Next(3, 6);
            var decisionInjury = winnerWon ? RollInjuryWeeks(rng, 0.03, 1, 3) : RollInjuryWeeks(rng, 0.08, 2, 4);
            return new RecoveryPlan(decisionMedical, decisionInjury);
        }

        private static int RollInjuryWeeks(Random rng, double chance, int minWeeks, int maxWeeks)
            => rng.NextDouble() < chance ? rng.Next(minWeeks, maxWeeks + 1) : 0;

        private static Task UpdateFighterAvailabilityAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int fighterId,
            RecoveryPlan plan,
            int currentAbsoluteWeek)
        {
            var unavailableWeeks = Math.Max(plan.MedicalSuspensionWeeks, plan.InjuryWeeks);

            return ExecAsync(conn, tx, @"
UPDATE Fighters
SET
    IsBooked = 0,
    WeeksUntilAvailable = $weeksUntilAvailable,
    MedicalSuspensionWeeksRemaining = $medicalWeeks,
    InjuryWeeksRemaining = $injuryWeeks,
    IsInjured = CASE WHEN $injuryWeeks > 0 THEN 1 ELSE 0 END,
    AvailableFromWeek = $availableFromWeek
WHERE Id = $id;",
                ("$weeksUntilAvailable", unavailableWeeks),
                ("$medicalWeeks", plan.MedicalSuspensionWeeks),
                ("$injuryWeeks", plan.InjuryWeeks),
                ("$availableFromWeek", currentAbsoluteWeek + unavailableWeeks),
                ("$id", fighterId));
        }

        private static double CombatPower(FighterRow fighter, PrepEffect prep, Random rng)
        {
            var basePower = 0.35 * fighter.Skill
                            + 0.14 * fighter.Striking
                            + 0.14 * fighter.Wrestling
                            + 0.14 * fighter.Grappling
                            + 0.10 * fighter.Cardio
                            + 0.07 * fighter.Chin
                            + 0.06 * fighter.FightIQ;

            var prepAdjustment = 0.0;
            prepAdjustment += prep.CampOutcome switch
            {
                "Excellent" => 4.0,
                "Disrupted" => -3.0,
                "MinorInjury" => -5.5,
                _ => 0.0
            };
            prepAdjustment += prep.FightWeekOutcome switch
            {
                "LockedIn" => 2.5,
                "MediaSwirl" => -2.0,
                "Flat" => -3.5,
                _ => 0.0
            };
            prepAdjustment += prep.WeighInOutcome switch
            {
                "ToughCut" => -3.0,
                "MissedWeight" => -5.5,
                _ => 0.0
            };

            return basePower + prepAdjustment + NextGaussian(rng, 0, 4.5);
        }

        private static string DecideMethod(FighterRow winner, FighterRow loser, PrepEffect winnerPrep, PrepEffect loserPrep, Random rng)
        {
            var koProbability = 0.33;
            var subProbability = 0.22;
            koProbability += Clamp01((winner.Striking - loser.Chin) / 60.0) * 0.25;

            var groundEdge = (winner.Grappling + winner.Wrestling) - (loser.Grappling + loser.Wrestling);
            subProbability += Clamp01(groundEdge / 80.0) * 0.25;

            if (string.Equals(loserPrep.CampOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
                koProbability += 0.05;

            if (string.Equals(loserPrep.FightWeekOutcome, "Flat", StringComparison.OrdinalIgnoreCase))
                koProbability += 0.04;

            if (string.Equals(loserPrep.WeighInOutcome, "ToughCut", StringComparison.OrdinalIgnoreCase))
                koProbability += 0.04;

            if (string.Equals(loserPrep.WeighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
                koProbability += 0.08;

            if (string.Equals(winnerPrep.CampOutcome, "Disrupted", StringComparison.OrdinalIgnoreCase))
            {
                koProbability -= 0.03;
                subProbability -= 0.02;
            }

            if (string.Equals(winnerPrep.FightWeekOutcome, "LockedIn", StringComparison.OrdinalIgnoreCase))
            {
                koProbability += 0.03;
                subProbability += 0.02;
            }

            if (string.Equals(winnerPrep.WeighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
            {
                koProbability -= 0.05;
                subProbability -= 0.03;
            }

            koProbability = Clamp(koProbability, 0.15, 0.70);
            subProbability = Clamp(subProbability, 0.10, 0.60);

            var roll = rng.NextDouble();
            if (roll < koProbability) return "KO/TKO";
            if (roll < koProbability + subProbability) return "SUB";
            return "DEC";
        }

        private static async Task ApplyResultAsync(SqliteConnection conn, SqliteTransaction tx, Random rng, int winnerId, int loserId, string method, bool isTitle)
        {
            if (method == "KO/TKO")
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, KOWins = KOWins + 1 WHERE Id = $id;", ("$id", winnerId));
            else if (method == "SUB")
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, SubWins = SubWins + 1 WHERE Id = $id;", ("$id", winnerId));
            else
                await ExecAsync(conn, tx, "UPDATE Fighters SET Wins = Wins + 1, DecWins = DecWins + 1 WHERE Id = $id;", ("$id", winnerId));

            await ExecAsync(conn, tx, "UPDATE Fighters SET Losses = Losses + 1 WHERE Id = $id;", ("$id", loserId));

            var winnerDelta = isTitle ? rng.Next(3, 8) : rng.Next(1, 5);
            var loserDelta = isTitle ? rng.Next(2, 6) : rng.Next(1, 4);

            await ExecAsync(conn, tx, "UPDATE Fighters SET Popularity = MIN(100, Popularity + $delta) WHERE Id = $id;", ("$delta", winnerDelta), ("$id", winnerId));
            await ExecAsync(conn, tx, "UPDATE Fighters SET Popularity = MAX(0, Popularity - $delta) WHERE Id = $id;", ("$delta", loserDelta), ("$id", loserId));
        }

        private static async Task RebuildDivisionRankingsAndEnsureTitleAsync(SqliteConnection conn, SqliteTransaction tx, int promotionId, string weightClass, int rankingSize)
        {
            await ExecAsync(conn, tx, "DELETE FROM PromotionRankings WHERE PromotionId = $p AND WeightClass = $wc;", ("$p", promotionId), ("$wc", weightClass));

            var topFighters = await QueryAsync(conn, tx, @"
SELECT Id
FROM Fighters
WHERE PromotionId = $p
  AND WeightClass = $wc
  AND Retired = 0
ORDER BY (Skill * 0.75 + Popularity * 0.25) DESC
LIMIT $take;",
                ("$p", promotionId),
                ("$wc", weightClass),
                ("$take", rankingSize));

            for (var i = 0; i < topFighters.Count; i++)
            {
                await ExecAsync(conn, tx, @"
INSERT INTO PromotionRankings (PromotionId, WeightClass, RankPosition, FighterId)
VALUES ($p, $wc, $rank, $fighterId);",
                    ("$p", promotionId),
                    ("$wc", weightClass),
                    ("$rank", i + 1),
                    ("$fighterId", topFighters[i].GetInt("Id")));
            }

            var titleRowCount = await ScalarIntAsync(conn, tx, @"
SELECT COUNT(*)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc;",
                ("$p", promotionId),
                ("$wc", weightClass));

            if (titleRowCount == 0)
            {
                await ExecAsync(conn, tx, @"
INSERT INTO Titles (PromotionId, WeightClass, ChampionFighterId, InterimChampionFighterId)
VALUES ($p, $wc, NULL, NULL);",
                    ("$p", promotionId),
                    ("$wc", weightClass));
            }

            var championId = await ScalarIntAsync(conn, tx, @"
SELECT COALESCE(ChampionFighterId, 0)
FROM Titles
WHERE PromotionId = $p AND WeightClass = $wc
LIMIT 1;",
                ("$p", promotionId),
                ("$wc", weightClass));

            if (championId <= 0 && topFighters.Count > 0)
            {
                await ExecAsync(conn, tx, @"
UPDATE Titles
SET ChampionFighterId = $championId
WHERE PromotionId = $p AND WeightClass = $wc;",
                    ("$championId", topFighters[0].GetInt("Id")),
                    ("$p", promotionId),
                    ("$wc", weightClass));
            }
        }

        private static async Task UpdateScheduledFightAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int fightId,
            int winnerId,
            string method,
            int? round,
            string eventDate,
            string cardSegment,
            int cardOrder,
            bool isMainEvent,
            bool isCoMainEvent)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE Fights
SET WinnerId = $winnerId,
    Method = $method,
    Round = $round,
    EventDate = $eventDate,
    CardSegment = $cardSegment,
    CardOrder = $cardOrder,
    IsMainEvent = $isMainEvent,
    IsCoMainEvent = $isCoMainEvent
WHERE Id = $fightId;";
            cmd.Parameters.AddWithValue("$winnerId", winnerId);
            cmd.Parameters.AddWithValue("$method", method);
            cmd.Parameters.AddWithValue("$round", round.HasValue ? round.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$eventDate", eventDate);
            cmd.Parameters.AddWithValue("$cardSegment", cardSegment);
            cmd.Parameters.AddWithValue("$cardOrder", cardOrder);
            cmd.Parameters.AddWithValue("$isMainEvent", isMainEvent ? 1 : 0);
            cmd.Parameters.AddWithValue("$isCoMainEvent", isCoMainEvent ? 1 : 0);
            cmd.Parameters.AddWithValue("$fightId", fightId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static Task InsertCompletedFightAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int fighterAId,
            int fighterBId,
            int winnerId,
            string method,
            string eventDate,
            int eventId,
            string weightClass,
            bool isTitleFight,
            string cardSegment,
            int cardOrder,
            bool isMainEvent,
            bool isCoMainEvent)
        {
            return ExecAsync(conn, tx, @"
INSERT INTO Fights
(
    FighterAId,
    FighterBId,
    WinnerId,
    Method,
    Round,
    EventDate,
    EventId,
    WeightClass,
    IsTitleFight,
    CardSegment,
    CardOrder,
    IsMainEvent,
    IsCoMainEvent
)
VALUES
(
    $fighterAId,
    $fighterBId,
    $winnerId,
    $method,
    NULL,
    $eventDate,
    $eventId,
    $weightClass,
    $isTitleFight,
    $cardSegment,
    $cardOrder,
    $isMainEvent,
    $isCoMainEvent
);",
                ("$fighterAId", fighterAId),
                ("$fighterBId", fighterBId),
                ("$winnerId", winnerId),
                ("$method", method),
                ("$eventDate", eventDate),
                ("$eventId", eventId),
                ("$weightClass", weightClass),
                ("$isTitleFight", isTitleFight ? 1 : 0),
                ("$cardSegment", cardSegment),
                ("$cardOrder", cardOrder),
                ("$isMainEvent", isMainEvent ? 1 : 0),
                ("$isCoMainEvent", isCoMainEvent ? 1 : 0));
        }

        private static async Task<int> EnsureWeeklyEventAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int promotionId,
            string eventDate,
            string eventName,
            string location)
        {
            using var findCmd = conn.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = @"
SELECT Id
FROM Events
WHERE PromotionId = $promotionId
  AND Name = $eventName
LIMIT 1;";
            findCmd.Parameters.AddWithValue("$promotionId", promotionId);
            findCmd.Parameters.AddWithValue("$eventName", eventName);

            var existing = await findCmd.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt32(existing);

            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
INSERT INTO Events (PromotionId, EventDate, Name, Location)
VALUES ($promotionId, $eventDate, $eventName, $location);";
            insertCmd.Parameters.AddWithValue("$promotionId", promotionId);
            insertCmd.Parameters.AddWithValue("$eventDate", eventDate);
            insertCmd.Parameters.AddWithValue("$eventName", eventName);
            insertCmd.Parameters.AddWithValue("$location", location);
            await insertCmd.ExecuteNonQueryAsync();

            insertCmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        }

        private static Task UpdateEventMetadataAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            int eventId,
            string eventDate,
            string eventName,
            string eventTier,
            int plannedFightCount,
            int completedFightCount)
        {
            return ExecAsync(conn, tx, @"
UPDATE Events
SET EventDate = $eventDate,
    Name = $eventName,
    EventTier = $eventTier,
    PlannedFightCount = $plannedFightCount,
    CompletedFightCount = $completedFightCount
WHERE Id = $eventId;",
                ("$eventDate", eventDate),
                ("$eventName", eventName),
                ("$eventTier", eventTier),
                ("$plannedFightCount", plannedFightCount),
                ("$completedFightCount", completedFightCount),
                ("$eventId", eventId));
        }

        private static Task InsertFightHistoryAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            string date,
            int promotionId,
            string weightClass,
            int fighterAId,
            int fighterBId,
            int winnerId,
            int loserId,
            string method,
            bool isTitle,
            string? notes,
            int eventId,
            string cardSegment,
            int cardOrder,
            bool isMainEvent,
            bool isCoMainEvent,
            string eventTier)
        {
            return ExecAsync(conn, tx, @"
INSERT INTO FightHistory
(
    FightDate,
    PromotionId,
    WeightClass,
    FighterAId,
    FighterBId,
    WinnerId,
    LoserId,
    Method,
    IsTitle,
    Notes,
    EventId,
    CardSegment,
    CardOrder,
    IsMainEvent,
    IsCoMainEvent,
    EventTier
)
VALUES
(
    $date,
    $promotionId,
    $weightClass,
    $fighterAId,
    $fighterBId,
    $winnerId,
    $loserId,
    $method,
    $isTitle,
    $notes,
    $eventId,
    $cardSegment,
    $cardOrder,
    $isMainEvent,
    $isCoMainEvent,
    $eventTier
);",
                ("$date", date),
                ("$promotionId", promotionId),
                ("$weightClass", weightClass),
                ("$fighterAId", fighterAId),
                ("$fighterBId", fighterBId),
                ("$winnerId", winnerId),
                ("$loserId", loserId),
                ("$method", method),
                ("$isTitle", isTitle ? 1 : 0),
                ("$notes", (object?)notes ?? DBNull.Value),
                ("$eventId", eventId),
                ("$cardSegment", cardSegment),
                ("$cardOrder", cardOrder),
                ("$isMainEvent", isMainEvent ? 1 : 0),
                ("$isCoMainEvent", isCoMainEvent ? 1 : 0),
                ("$eventTier", eventTier));
        }

        private static async Task<PrepEffect> LoadPrepEffectAsync(SqliteConnection conn, SqliteTransaction tx, int fightId, int fighterId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT
    COALESCE(CampOutcome, '') AS CampOutcome,
    COALESCE(CampNotes, '') AS CampNotes,
    COALESCE(FightWeekOutcome, '') AS FightWeekOutcome,
    COALESCE(FightWeekNotes, '') AS FightWeekNotes,
    COALESCE(WeighInOutcome, '') AS WeighInOutcome,
    COALESCE(WeighInNotes, '') AS WeighInNotes
FROM FightPreparations
WHERE FightId = $fightId
  AND FighterId = $fighterId
LIMIT 1;";
            cmd.Parameters.AddWithValue("$fightId", fightId);
            cmd.Parameters.AddWithValue("$fighterId", fighterId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return PrepEffect.None;

            return new PrepEffect(
                reader["CampOutcome"]?.ToString() ?? "",
                reader["CampNotes"]?.ToString() ?? "",
                reader["FightWeekOutcome"]?.ToString() ?? "",
                reader["FightWeekNotes"]?.ToString() ?? "",
                reader["WeighInOutcome"]?.ToString() ?? "",
                reader["WeighInNotes"]?.ToString() ?? "");
        }

        private static string? BuildFightNotes(FighterRow fighterA, FighterRow fighterB, PrepEffect prepA, PrepEffect prepB)
        {
            var fragments = new List<string>();
            AppendPrepNote(fragments, fighterA.FullName, prepA);
            AppendPrepNote(fragments, fighterB.FullName, prepB);
            return fragments.Count == 0 ? null : string.Join(" ", fragments);
        }

        private static void AppendPrepNote(List<string> fragments, string fighterName, PrepEffect prep)
        {
            if (string.Equals(prep.CampOutcome, "Disrupted", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} entered after a disrupted camp.");
            else if (string.Equals(prep.CampOutcome, "MinorInjury", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} fought through a minor camp injury.");

            if (string.Equals(prep.FightWeekOutcome, "LockedIn", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} looked locked in all fight week.");
            else if (string.Equals(prep.FightWeekOutcome, "MediaSwirl", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} dealt with a noisy fight week.");
            else if (string.Equals(prep.FightWeekOutcome, "Flat", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} never looked fully sharp in fight week.");

            if (string.Equals(prep.WeighInOutcome, "ToughCut", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} had a draining weight cut.");
            else if (string.Equals(prep.WeighInOutcome, "MissedWeight", StringComparison.OrdinalIgnoreCase))
                fragments.Add($"{fighterName} missed weight before fight night.");
        }

        private static async Task<PromotionPlan?> LoadPromotionPlanAsync(SqliteConnection conn, SqliteTransaction tx, int promotionId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT
    Name,
    COALESCE(NextEventWeek, 0) AS NextEventWeek,
    COALESCE(TitleFightIntervalWeeks, 6) AS TitleFightIntervalWeeks,
    COALESCE(MajorEventIntervalWeeks, 6) AS MajorEventIntervalWeeks,
    COALESCE(EarlyPrelimFightCount, 0) AS EarlyPrelimFightCount,
    COALESCE(PrelimFightCount, 3) AS PrelimFightCount,
    COALESCE(MainCardFightCount, 3) AS MainCardFightCount
FROM Promotions
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", promotionId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

        return new PromotionPlan(
            reader["Name"]?.ToString() ?? $"Promotion {promotionId}",
            Math.Max(0, Convert.ToInt32(reader["NextEventWeek"])),
            Math.Max(1, Convert.ToInt32(reader["TitleFightIntervalWeeks"])),
            Math.Max(1, Convert.ToInt32(reader["MajorEventIntervalWeeks"])),
            Math.Max(0, Convert.ToInt32(reader["EarlyPrelimFightCount"])),
            Math.Max(1, Convert.ToInt32(reader["PrelimFightCount"])),
            Math.Max(1, Convert.ToInt32(reader["MainCardFightCount"])));
        }

        private static async Task<List<RankedFighter>> LoadEligibleRankedDivisionAsync(SqliteConnection conn, SqliteTransaction tx, int promotionId, string weightClass)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT
    pr.RankPosition,
    f.Id AS FighterId,
    f.Skill,
    f.Popularity
FROM PromotionRankings pr
JOIN Fighters f ON f.Id = pr.FighterId
WHERE pr.PromotionId = $promotionId
  AND pr.WeightClass = $weightClass
  AND f.Retired = 0
  AND COALESCE(f.IsBooked, 0) = 0
  AND COALESCE(f.WeeksUntilAvailable, 0) <= 0
  AND COALESCE(f.InjuryWeeksRemaining, 0) <= 0
  AND COALESCE(f.MedicalSuspensionWeeksRemaining, 0) <= 0
  AND f.Id NOT IN (
      SELECT FighterId
      FROM ManagedFighters
      WHERE IsActive = 1
  )
ORDER BY pr.RankPosition;";
            cmd.Parameters.AddWithValue("$promotionId", promotionId);
            cmd.Parameters.AddWithValue("$weightClass", weightClass);

            var list = new List<RankedFighter>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new RankedFighter(
                    Convert.ToInt32(reader["FighterId"]),
                    Convert.ToInt32(reader["RankPosition"]),
                    Convert.ToInt32(reader["Skill"]),
                    Convert.ToInt32(reader["Popularity"])));
            }

            return list;
        }

        private static async Task<FighterRow?> LoadEligibleFighterAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, FirstName, LastName, Retired,
       Skill, Popularity,
       Striking, Grappling, Wrestling, Cardio, Chin, FightIQ
FROM Fighters
WHERE Id = $id
  AND Retired = 0
  AND COALESCE(IsBooked, 0) = 0
  AND COALESCE(WeeksUntilAvailable, 0) <= 0
  AND COALESCE(InjuryWeeksRemaining, 0) <= 0
  AND COALESCE(MedicalSuspensionWeeksRemaining, 0) <= 0
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", fighterId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return MapFighter(reader);
        }

        private static async Task<FighterRow?> GetFighterAsync(SqliteConnection conn, SqliteTransaction tx, int fighterId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT Id, FirstName, LastName, Retired,
       Skill, Popularity,
       Striking, Grappling, Wrestling, Cardio, Chin, FightIQ
FROM Fighters
WHERE Id = $id
LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", fighterId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return MapFighter(reader);
        }

        private static FighterRow MapFighter(SqliteDataReader reader)
            => new()
            {
                Id = Convert.ToInt32(reader["Id"]),
                FirstName = reader["FirstName"]?.ToString() ?? "",
                LastName = reader["LastName"]?.ToString() ?? "",
                Retired = Convert.ToInt32(reader["Retired"]),
                Skill = Convert.ToInt32(reader["Skill"]),
                Popularity = Convert.ToInt32(reader["Popularity"]),
                Striking = Convert.ToInt32(reader["Striking"]),
                Grappling = Convert.ToInt32(reader["Grappling"]),
                Wrestling = Convert.ToInt32(reader["Wrestling"]),
                Cardio = Convert.ToInt32(reader["Cardio"]),
                Chin = Convert.ToInt32(reader["Chin"]),
                FightIQ = Convert.ToInt32(reader["FightIQ"])
            };

        private sealed class Row
        {
            private readonly Dictionary<string, object?> _data;

            public Row(Dictionary<string, object?> data)
            {
                _data = data;
            }

            public string GetString(string key) => _data[key]?.ToString() ?? "";
            public int GetInt(string key) => Convert.ToInt32(_data[key]);
        }

        private static async Task<List<Row>> QueryAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

            var rows = new List<Row>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                    dict[reader.GetName(i)] = reader.GetValue(i);

                rows.Add(new Row(dict));
            }

            return rows;
        }

        private static async Task ExecAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<int> ScalarIntAsync(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

            var result = await cmd.ExecuteScalarAsync();
            return result is null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        private static int DeriveEventSeed(int worldSeed, int year, int week, int promotionId)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + worldSeed;
                hash = hash * 31 + year;
                hash = hash * 31 + week;
                hash = hash * 31 + promotionId;
                if (hash == 0) hash = 1;
                return Math.Abs(hash);
            }
        }

        private static string BuildEventName(string promotionName, int absoluteWeek)
            => $"{promotionName} Week {absoluteWeek}";

        private static int ToAbsoluteWeek(int year, int week)
            => Math.Max(1, (year - 1) * 52 + week);

        private static double NextGaussian(Random rng, double mean, double stdDev)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : (value > 1 ? 1 : value);
        private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

        private sealed class FighterRow
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public int Retired { get; set; }
            public int Skill { get; set; }
            public int Popularity { get; set; }
            public int Striking { get; set; }
            public int Grappling { get; set; }
            public int Wrestling { get; set; }
            public int Cardio { get; set; }
            public int Chin { get; set; }
            public int FightIQ { get; set; }
            public string FullName => $"{FirstName} {LastName}".Trim();
        }

        private sealed record PromotionPlan(
            string Name,
            int NextEventWeek,
            int TitleFightIntervalWeeks,
            int MajorEventIntervalWeeks,
            int EarlyPrelimFightCount,
            int PrelimFightCount,
            int MainCardFightCount);

        private sealed record EventRuntimeSnapshot(int Id, string Name, string EventDate);
        private sealed record RankedFighter(int FighterId, int RankPosition, int Skill, int Popularity);
        private sealed record RecoveryPlan(int MedicalSuspensionWeeks, int InjuryWeeks);
        private sealed record PlannedBout(int? ExistingFightId, int FighterAId, int FighterBId, string WeightClass, bool IsTitleFight, double Score);
        private sealed record CardAssignment(PlannedBout Bout, string CardSegment, int CardOrder, bool IsMainEvent, bool IsCoMainEvent);
        private sealed record PrepEffect(
            string CampOutcome,
            string CampNotes,
            string FightWeekOutcome,
            string FightWeekNotes,
            string WeighInOutcome,
            string WeighInNotes)
        {
            public static PrepEffect None { get; } = new("", "", "", "", "", "");
        }
        private sealed record SimFightResult(int WinnerId, int LoserId, string Method, bool IsTitle, string Summary);
    }
}
