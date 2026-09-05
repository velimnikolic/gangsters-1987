using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// EPIC 25 / RIVAL-005 — where the twenty families think.
    ///
    /// The rules are <see cref="HouseMind"/>'s and are pure. This file is the scene edge:
    /// it reads the real ledgers into a <see cref="HouseView"/>, hands the view to the
    /// mind, and puts what comes back through the SAME doors the player's own buttons use
    /// - the command gateway, <see cref="Underworld.Issue"/>, <see cref="HouseOps"/>.
    ///
    /// It decides nothing. A family cannot do anything here the player could not do from
    /// his own ledger, and a refusal refuses them both alike.
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        readonly HouseMindConfig mindConfig = new HouseMindConfig();
        readonly List<HouseIntent> intents = new List<HouseIntent>();
        readonly Dictionary<int, List<string>> refusals = new Dictionary<int, List<string>>();
        readonly Dictionary<TerritoryBlockId, List<HouseDoor>> doorScratch =
            new Dictionary<TerritoryBlockId, List<HouseDoor>>();
        readonly List<HouseIncident> incidentScratch = new List<HouseIncident>();
        readonly List<HouseThreat> threatScratch = new List<HouseThreat>();
        readonly List<StreetThreat> streetThreats = new List<StreetThreat>();
        readonly List<HouseDefiance> defianceScratch = new List<HouseDefiance>();
        readonly List<TerritoryBlockId> viewBlocks = new List<TerritoryBlockId>();

        public HouseMindConfig MindConfig => mindConfig;

        /// <summary>How many thinks have run since the scene woke - the trace's own
        /// count, and what a test waits on.</summary>
        public int Thinks { get; private set; }

        /// <summary>What the last turn of mind cost, in milliseconds. The measurement
        /// RIVAL-008's physical count is decided from.</summary>
        public float ThinkMilliseconds { get; private set; }

        /// <summary>One intent of one think, as the probe prints it: what was asked,
        /// why (or why it was refused), and whether the gateway took it (AI-000).
        /// </summary>
        public readonly struct HouseThinkLine
        {
            public HouseThinkLine(string intent, string reason, bool carried)
            {
                Intent = intent;
                Reason = reason;
                Carried = carried;
            }

            public string Intent { get; }
            public string Reason { get; }
            public bool Carried { get; }
        }

        /// <summary>One turn of mind, remembered: when, which tier acted, what it cost,
        /// and every intent with the gateway's own verdict on it.</summary>
        public sealed class HouseThinkRecord
        {
            public double Hour;
            public int Day;
            public int Tier;
            public float Milliseconds;
            public readonly List<HouseThinkLine> Lines = new List<HouseThinkLine>();

            /// <summary>How many of the lines were taken. A house whose every think
            /// is refused reads green on every other column (AI-001's measure).</summary>
            public int Accepted
            {
                get
                {
                    var taken = 0;
                    for (var i = 0; i < Lines.Count; i++)
                        if (Lines[i].Carried)
                            taken++;
                    return taken;
                }
            }
        }

        /// <summary>
        /// THE LAST FIFTY THINKS OF EVERY HOUSE (AI-000). Memory only, never saved
        /// (review finding C5): QuietThinks is already left out of the file, and a
        /// history that came back empty after a load would read as a house that had
        /// stopped. The harness trace writes the same lines to disk; this is what an
        /// ordinary Play can be asked afterwards.
        /// </summary>
        public const int ThinksKept = 50;

        readonly Dictionary<int, List<HouseThinkRecord>> thinkHistory =
            new Dictionary<int, List<HouseThinkRecord>>();

        static readonly List<HouseThinkRecord> NoThinks = new List<HouseThinkRecord>();

        public IReadOnlyList<HouseThinkRecord> ThinkHistory(int gangId) =>
            thinkHistory.TryGetValue(gangId, out var list) ? list : NoThinks;

        /// <summary>How many thinks each house has taken since the scene woke.</summary>
        readonly Dictionary<int, int> thinksOf = new Dictionary<int, int>();

        public int ThinksOf(int gangId) =>
            thinksOf.TryGetValue(gangId, out var count) ? count : 0;

        /// <summary>
        /// THE FAMILIES TAKE THEIR TURN. Every game hour (A19) a house reads the street
        /// and files what it wants; at most three of its intents are executed, in order.
        /// </summary>
        void DriveHouseMinds(double gameHour)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null || racket == null || geography == null ||
                Commands == null)
                return;

            underworld.Think(gameHour, mindConfig.ThinkEveryHours, house =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var view = Look(house, gameHour);
                var tier = HouseMind.Think(
                    view, mindConfig, Relations?.Config, intents);
                Thinks++;
                thinksOf[house.GangId] = ThinksOf(house.GangId) + 1;

                var refused = Refusals(house.GangId);
                refused.Clear();

                // A think that only spent money, or found nothing at all, is a quiet
                // one. Three of them running are what tier 8 waits for (D22).
                house.NoteThink(tier > 0 && tier < HouseMind.TierInvest);

                var record = new HouseThinkRecord
                {
                    Hour = gameHour,
                    Day = view.Day,
                    Tier = tier,
                };

                var done = 0;
                for (var i = 0; i < intents.Count && done < mindConfig.MaxIntentsPerThink;
                     i++)
                {
                    var intent = intents[i];
                    var refusal = Carry(house, intent);
                    done++;
                    var taken = string.IsNullOrEmpty(refusal);
                    if (!taken)
                    {
                        refused.Add(intent + ": " + refusal);
                        // P4 (AI-005, ruling A24): a refused intent is not proposed
                        // again for twelve game hours, keyed by what it was and what
                        // it was aimed at. The memory is the runtime's; the mind only
                        // reads it through the view.
                        Backoffs(house.GangId).Note(
                            intent.Key, refusal, gameHour, mindConfig);
                    }
                    record.Lines.Add(new HouseThinkLine(
                        intent.ToString(), taken ? intent.Reason : refusal, taken));
                    DriveTrace.House(house.GangId, intent.Tier, intent.ToString(),
                        taken ? intent.Reason : refusal,
                        house.Runner.Accounts.Safe, view.DailyPayroll,
                        (float)clock.Elapsed.TotalMilliseconds);
                }

                if (done == 0)
                    DriveTrace.House(house.GangId, tier, "-", "no candidate",
                        house.Runner.Accounts.Safe, view.DailyPayroll,
                        (float)clock.Elapsed.TotalMilliseconds);
                ThinkMilliseconds = (float)clock.Elapsed.TotalMilliseconds;
                record.Milliseconds = ThinkMilliseconds;
                Remember(house.GangId, record);
            });
        }

        void Remember(int gangId, HouseThinkRecord record)
        {
            if (!thinkHistory.TryGetValue(gangId, out var list))
            {
                list = new List<HouseThinkRecord>(ThinksKept + 1);
                thinkHistory.Add(gangId, list);
            }
            list.Add(record);
            if (list.Count > ThinksKept)
                list.RemoveAt(0);
        }

        /// <summary>
        /// THE DAY'S COUNTS PER HOUSE (AI-008's live table): arrests and rounds lost,
        /// keyed by (house, day) so a count never has to be reset. Memory only.
        /// </summary>
        readonly Dictionary<(int gang, int day, string what), int> dayCounts =
            new Dictionary<(int, int, string), int>();

        public void CountToday(int gangId, string what)
        {
            var key = (gangId, (int)(lastGameHour / 24.0) + 1, what);
            dayCounts.TryGetValue(key, out var count);
            dayCounts[key] = count + 1;
        }

        public int CountedToday(int gangId, string what) =>
            dayCounts.TryGetValue((gangId, (int)(lastGameHour / 24.0) + 1, what),
                out var count)
                ? count
                : 0;

        /// <summary>The refused-intent memory of one house (P4). Memory only, like the
        /// think history: a back-off that came back from a file would be a house
        /// refusing to try something for reasons nobody can see any more.</summary>
        readonly Dictionary<int, HouseBackoffs> backoffs = new Dictionary<int, HouseBackoffs>();

        HouseBackoffs Backoffs(int gangId)
        {
            if (!backoffs.TryGetValue(gangId, out var book))
            {
                book = new HouseBackoffs();
                backoffs.Add(gangId, book);
            }
            return book;
        }

        /// <summary>
        /// SOMEBODY PUT HANDS ON SOMEBODY, here, at this hour. Kept raw - who did it,
        /// where and when - because what it MEANS depends on which family is asking:
        /// the same shot is an attack to one house and an answer to another.
        /// </summary>
        readonly struct StreetThreat
        {
            public StreetThreat(
                TerritoryGangId by, TerritoryBlockId blockId, Vector3 where, double at)
            {
                By = by;
                BlockId = blockId;
                Where = where;
                At = at;
            }

            public TerritoryGangId By { get; }
            public TerritoryBlockId BlockId { get; }
            public Vector3 Where { get; }
            public double At { get; }
        }

        /// <summary>A violent act on a street, remembered for as long as a mind is
        /// allowed to be about it. The list is short by construction - anything past the
        /// memory window is dropped as it is written.</summary>
        void NoteStreetThreat(
            TerritoryGangId by, TerritoryBlockId blockId, Vector3 where, double gameHour)
        {
            if (!blockId.IsValid)
                return;
            for (var i = streetThreats.Count - 1; i >= 0; i--)
                if (gameHour - streetThreats[i].At > mindConfig.ThreatMemoryHours)
                    streetThreats.RemoveAt(i);
            streetThreats.Add(new StreetThreat(by, blockId, where, gameHour));
        }

        /// <summary>
        /// A family is told at once that somebody has hit what it is paid to protect.
        /// Four hours is a cadence for deciding what to do next; it is not a delay a
        /// house is willing to sit through while its shops are being wrecked (D7).
        /// </summary>
        void WakeHouse(TerritoryGangId house, double gameHour) =>
            LivingCity.Outfit.Underworld.Current?.Of(house.Value)?.WakeNow(gameHour);

        /// <summary>
        /// THE FORCED SCENARIOS' TWO DIALS (EPIC 31 NIGHT-013), both left alone by
        /// default so a scene that sets neither is the scene as it was.
        ///
        /// The cadence: twenty houses think ONE AT A TIME in rota, so a run that wants
        /// every family to have had a turn needs either a short interval or a long
        /// clock. Above 0 replaces the model's own four game hours.
        ///
        /// The safe: what the player's house starts with. At or above 0 it replaces the
        /// ledger's $25,000, and 0 is the broke-player scenario. They are statics
        /// because the runtime is made at Play and has nothing in the scene to write to.
        /// </summary>
        public static float MindThinkEveryHoursOverride { get; set; }

        /// <summary>See <see cref="MindThinkEveryHoursOverride"/>. Below 0 leaves the
        /// ledger's own starting figure.</summary>
        public static int PlayerSafeAtStartOverride { get; set; } = -1;

        /// <summary>
        /// NO SCENARIO SURVIVES ITS OWN RUN. A static outlives Play, and the scene that
        /// sets these is the core's - so a forced run and then a plain BlockDemo in the
        /// same editor session would have played the second one under the first one's
        /// rules, silently. They are put back to their defaults before every scene
        /// wakes, and the builder that wants them writes them again in its own Awake.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ForgetTheLastScenario()
        {
            MindThinkEveryHoursOverride = 0f;
            PlayerSafeAtStartOverride = -1;
            OwnerTraitOverride = null;
        }

        /// <summary>
        /// Hangs the book off the street. The only wire between them is one question -
        /// "is anybody sitting on this door?" - and the answer is the guard lieutenant's
        /// own hand (D10 iii). Called once, with the rest of the runtime's wake-up.
        /// </summary>
        void InstallMinds()
        {
            if (MindThinkEveryHoursOverride > 0f)
                mindConfig.ThinkEveryHours = MindThinkEveryHoursOverride;
            Debug.Log($"[Core] the houses think every {mindConfig.ThinkEveryHours} game hours.");
            if (PlayerSafeAtStartOverride >= 0)
            {
                var accounts = LivingCity.Outfit.Underworld.Current?.Player?.Runner?.Accounts;
                if (accounts != null)
                {
                    accounts.Safe = PlayerSafeAtStartOverride;
                    Debug.Log("[Core] the player's safe starts at $" +
                              PlayerSafeAtStartOverride);
                }
            }

            // THE LEDGER'S TABLE ANSWERS WITH THE RUNTIME'S OWN LOOK (EPIC 42), so a
            // truce the player offers from a button reaches the same desk a mind's does.
            HouseOps.Look = house => Look(house, lastGameHour);

            // THE CONNECTION'S TWO QUESTIONS OF THE STREET (EPIC 40): how watched the
            // door is, and who takes the men when the seller was a cop. And how a
            // door is called on a card.
            LivingCity.Outfit.CampaignRunner.WatchOnTheDoor = WatchOn;
            LivingCity.Outfit.CampaignRunner.StungOnTheStreet = Sting;
            ConnectionEvents.DoorNamer = door =>
                TryGetBusinessView(door, out var named) ? named.BusinessName : door.Value;

            LivingCity.Outfit.CampaignRunner.GuardOnTheDoor = job =>
            {
                if (job == null || crews == null ||
                    string.IsNullOrEmpty(job.TargetBusinessId))
                    return 0;

                var guards = CrewJobs.GuardsAt(
                    crews, new TerritoryBusinessId(job.TargetBusinessId), job.GangId);
                if (guards == null)
                    return 0;

                var roster = LivingCity.Outfit.Underworld.Current?
                    .Of(guards.Faction)?.Roster;
                var crew = roster?.FindCrew(guards.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                return lieutenant != null
                    ? lieutenant.GetHalfSteps(CharacterAttribute.Combat)
                    : 0;
            };
        }

        /// <summary>
        /// WHOSE GROUND IS THIS? The block under a point, and the house the control
        /// ledger says leads it. Invalid when the point is on no block - the road
        /// between two of them belongs to nobody, and a truce holds there.
        /// </summary>
        public TerritoryGangId LeaderAt(Vector3 world)
        {
            if (control == null || !TryGetBlockForAct(world, out var blockId))
                return default;
            return control.LeaderOf(blockId);
        }

        /// <summary>The last house to put hands on somebody on this street, other than
        /// the one asking. Invalid when the street has been quiet.</summary>
        public TerritoryGangId LastThreatOn(TerritoryBlockId blockId, TerritoryGangId mine)
        {
            for (var i = streetThreats.Count - 1; i >= 0; i--)
            {
                var threat = streetThreats[i];
                if (threat.BlockId != blockId || !threat.By.IsValid || threat.By == mine)
                    continue;
                if (lastGameHour - threat.At > mindConfig.ThreatMemoryHours)
                    continue;
                return threat.By;
            }
            return default;
        }

        static LivingCity.Outfit.HouseRelations Relations =>
            LivingCity.Outfit.Underworld.Current?.Relations;

        // ------------------------------------------------------------------- the table

        /// <summary>
        /// THE LEDGER'S TWO DOORS TO THE TABLE (EPIC 42). The player's TABLE files
        /// through the same HouseOps call a mind's intent does, with the runtime's
        /// own Look handed over so a mind on the other side answers at the desk.
        /// </summary>
        public OpResult Propose(House from, Proposal proposal) =>
            HouseOps.Propose(LivingCity.Outfit.Underworld.Current, from, proposal,
                other => Look(other, lastGameHour));

        public OpResult Reply(House to, int proposalId, bool accept) =>
            HouseOps.Reply(LivingCity.Outfit.Underworld.Current, to, proposalId, accept);

        /// <summary>
        /// A STREET UNDER OUR WORD (EPIC 42). A house that complied with a warning, or
        /// agreed a line, has said it keeps off this block; the racket's choke points
        /// ask here before any order of that house is worked on it. Answers the
        /// refusal, or null when the house may go there.
        /// </summary>
        string KeptOff(TerritoryGangId house, TerritoryBlockId blockId)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var ours = underworld?.Of(house.Value);
            if (ours == null || !blockId.IsValid)
                return null;
            return underworld.Diplomacy.IsKeptOff(
                house.Value, blockId, ours.Runner.Campaign.Day)
                ? HouseDiplomacy.ReasonUnderOurWord
                : null;
        }

        /// <summary>A door taken or hit across a line is owed for on top (EPIC 42,
        /// DIPL-006) - asked beside the ordinary note at both sites.</summary>
        void NoteLineCrossed(TerritoryGangId aggrieved, TerritoryGangId offender,
            TerritoryBlockId blockId)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var wronged = underworld?.Of(aggrieved.Value);
            if (wronged == null || !offender.IsValid)
                return;
            underworld.Diplomacy.NoteCrossing(underworld.Relations, aggrieved.Value,
                offender.Value, blockId, wronged.Runner.Campaign.Day);
        }

        readonly List<(int house, TerritoryBlockId block, int untilDay)> keepOffScratch =
            new List<(int, TerritoryBlockId, int)>();
        readonly List<int> keepOffCrews = new List<int>();
        int lastKeepOffHour = -1;

        /// <summary>
        /// THE WORD IS KEPT ON THE STREET, once an hour: a crew of a house standing on
        /// a block that house has given its word to keep off walks home, the way a
        /// crew whose book emptied does, and a paper posting there is forgotten.
        /// </summary>
        void SweepKeepOffs(double gameHour)
        {
            var hour = (int)gameHour;
            if (hour == lastKeepOffHour)
                return;
            lastKeepOffHour = hour;

            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null)
                return;
            underworld.Diplomacy.CollectKeepOffs(keepOffScratch);
            for (var i = 0; i < keepOffScratch.Count; i++)
            {
                var (gangId, blockId, _) = keepOffScratch[i];
                var ours = underworld.Of(gangId);
                if (ours == null ||
                    !underworld.Diplomacy.IsKeptOff(gangId, blockId, ours.Runner.Campaign.Day))
                    continue;

                keepOffCrews.Clear();
                foreach (var posting in postings)
                    if (posting.Value.House.Value == gangId && posting.Value.Block == blockId)
                        keepOffCrews.Add(posting.Key);
                for (var c = 0; c < keepOffCrews.Count; c++)
                    ForgetPosting(keepOffCrews[c]);

                if (crews == null)
                    continue;
                for (var u = 0; u < crews.Units.Count; u++)
                {
                    var unit = crews.Units[u];
                    if (unit == null || unit.Wiped || unit.IsDetachment || unit.IsPolice ||
                        unit.Faction != gangId || unit.CrewId < 0)
                        continue;
                    if (CrewBlockOf(ours, unit.CrewId) != blockId)
                        continue;
                    if (CrewQuarters.Billeted(unit))
                        continue;
                    if (CrewJobs.Home(ours, out var home))
                        crews.MarchTo(unit, home);
                }
            }
        }

        readonly List<TerritoryGangId> rivalScratch = new List<TerritoryGangId>();

        /// <summary>Every other family in the city. A mind reads its own side of each
        /// pair and nothing else about them.</summary>
        IReadOnlyList<TerritoryGangId> Rivals(House house)
        {
            rivalScratch.Clear();
            var underworld = LivingCity.Outfit.Underworld.Current;
            for (var g = 0; underworld != null && g < underworld.Count; g++)
            {
                var other = underworld.Of(g);
                if (other == null || other.Finished || other.GangId == house.GangId)
                    continue;
                rivalScratch.Add(new TerritoryGangId(other.GangId));
            }
            return rivalScratch;
        }

        /// <summary>What this house owes that one a cycle, and what that one owes it
        /// - both as the STREET prices them off the holdings, never the pinned figure
        /// (EPIC 42, DIPL-004; Codex: a discount must not halve again off itself).
        /// </summary>
        static (int owe, int owed) TributeBetween(House house, TerritoryGangId other) =>
            (house.Runner.DerivedTribute(house.GangId, other.Value),
                house.Runner.DerivedTribute(other.Value, house.GangId));

        /// <summary>The most men any one war has cost this house - the one figure the
        /// view's plain LossesThisWar seat carries; the per-house look is the real
        /// reading (EPIC 42, wired from the runner's tally; it was 0 for ever).</summary>
        static int WorstLosses(House house)
        {
            var worst = 0;
            var underworld = LivingCity.Outfit.Underworld.Current;
            for (var g = 0; underworld != null && g < underworld.Count; g++)
            {
                var lost = house.Runner.LossesThisWar(g);
                if (lost > worst)
                    worst = lost;
            }
            return worst;
        }

        /// <summary>What this house BELIEVES another could last, never the truth
        /// (D15).</summary>
        static int Estimate(House house, TerritoryGangId other, double gameHour)
        {
            var theirs = LivingCity.Outfit.Underworld.Current?.Of(other.Value);
            if (theirs == null)
                return 0;
            var truth = LivingCity.Outfit.HouseRelations.Endurance(
                theirs.Runner.Accounts.Safe,
                LivingCity.Outfit.Wages.DailyPayroll(theirs.Roster));
            return LivingCity.Outfit.HouseRelations.Estimate(
                truth, house.Runner.Seed, (int)(gameHour / 24.0), house.GangId,
                other.Value);
        }

        List<string> Refusals(int gangId)
        {
            if (!refusals.TryGetValue(gangId, out var list))
            {
                list = new List<string>();
                refusals.Add(gangId, list);
            }
            return list;
        }

        // ------------------------------------------------------------------ the border

        int lastBorderDay = -1;
        readonly List<(TerritoryGangId neighbour, int blocks)> borderScratch =
            new List<(TerritoryGangId, int)>();

        /// <summary>
        /// THE BORDER IS A GRIEVANCE (AI-007, rulings A13/A18). Once a day, every house
        /// with a mind that has nowhere open left to take files BorderPressure against
        /// each neighbour leading a block beside its own, per bordering block, capped
        /// at the retake rung. The player's own grudges are his to hold; the twenty
        /// houses hold theirs against him like against anybody.
        /// </summary>
        void PressBorders(double gameHour)
        {
            var day = (int)(gameHour / 24.0);
            if (day == lastBorderDay)
                return;
            var first = lastBorderDay < 0;
            lastBorderDay = day;
            if (first || Relations == null || geography == null || racket == null)
                return;

            var underworld = LivingCity.Outfit.Underworld.Current;
            for (var g = 0; underworld != null && g < underworld.Count; g++)
            {
                var house = underworld.Of(g);
                if (house == null || house.IsPlayer || house.Finished)
                    continue;
                HouseMind.Borders(Look(house, gameHour), mindConfig, borderScratch);
                for (var i = 0; i < borderScratch.Count; i++)
                    Relations.NoteBorder(house.GangId, borderScratch[i].neighbour.Value,
                        borderScratch[i].blocks);
            }
        }

        // -------------------------------------------------------------------- the view

        /// <summary>The view as the mind would read it this instant, for the probe
        /// (AI-000). Reads and repairs nothing; the scratch lists are the same ones a
        /// think fills, so it is not to be held across a frame.</summary>
        public HouseView Peek(House house) =>
            house == null || geography == null || racket == null
                ? null
                : Look(house, lastGameHour);

        // ------------------------------------------------------------------ EPIC 40

        /// <summary>What trades behind a door, for the cards that name a bar.</summary>
        static LivingCity.Business.BusinessArchetypeId TradeOf(TerritoryBusinessId businessId)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.Populated &&
                business.Directory.TryGet(businessId, out var record))
                return record.Archetype;
            return (LivingCity.Business.BusinessArchetypeId)(-1);
        }

        /// <summary>The street's book, read into the view: the card on the table and
        /// why it waits, so the mind answers it before it walks (STREET-003).</summary>
        void ReadTheStreet(House house, HouseView view)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            view.Events = house.Runner.Events;
            view.Connection = house.Runner.Connection;
            var ctx = StreetEvents.ContextFor(underworld, house, mindConfig);
            if (ctx == null)
                return;
            view.Card = StreetEvents.CardOf(house.Runner.Events, view, ctx, ConnectionEvents.Defs);
            view.CardHold = StreetEvents.HoldOf(house.Runner.Events, view, ctx, ConnectionEvents.Defs);
        }

        readonly List<EventFiring> firingScratch = new List<EventFiring>();
        readonly HashSet<(int gang, int day, string text)> wireFiled =
            new HashSet<(int, int, string)>();

        /// <summary>
        /// THE ONE DAY PASS (PRE-002). Called by the outfit director after the day
        /// tick, beside the flats: every house, the player included, is looked at
        /// through this runtime's own Look and rolled by the pure book. The doors a
        /// rumour named are learnt for that house, and a line the police made a
        /// record of reaches the paper.
        /// </summary>
        public int RunStreetEvents()
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null || geography == null || racket == null)
                return 0;
            firingScratch.Clear();
            var rolled = StreetEvents.DayPass(underworld,
                h => Look(h, lastGameHour),
                h => StreetEvents.ContextFor(underworld, h, mindConfig),
                ConnectionEvents.Defs, firingScratch);
            for (var i = 0; i < firingScratch.Count; i++)
            {
                var firing = firingScratch[i];
                if (!string.IsNullOrEmpty(firing.DoorToLearn))
                    TurfKnowledge.LearnDoor(firing.DoorToLearn, firing.GangId);
                DriveTrace.House(firing.GangId, HouseMind.TierCollect,
                    (firing.Wire ? "Wire:" : "Card:") + firing.Card, firing.Text,
                    underworld.Of(firing.GangId)?.Runner.Accounts.Safe ?? 0, 0, 0f);
            }
            for (var g = 0; g < underworld.Count; g++)
            {
                var house = underworld.Of(g);
                if (house == null)
                    continue;
                var wire = house.Runner.Events.Wire;
                for (var i = 0; i < wire.Count; i++)
                {
                    var line = wire[i];
                    if (!line.Public || !wireFiled.Add((g, line.Day, line.Text)))
                        continue;
                    PressDesk.Instance?.Seizure(g, house.Front, line.Text);
                }
            }
            return rolled;
        }

        /// <summary>The player's own answer to a card, through the same Carry a mind's
        /// goes through (STREET-002): the house is named on every order it builds.</summary>
        public string CarryForPlayer(HouseIntent intent)
        {
            var house = LivingCity.Outfit.Underworld.Current?.Player;
            return house == null ? "no house" : Carry(house, intent);
        }

        /// <summary>The player's card, spoken, for the HUD; null with nothing pending.</summary>
        public EventCard PlayerCard(out HoldReason hold)
        {
            hold = HoldReason.None;
            var house = LivingCity.Outfit.Underworld.Current?.Player;
            if (house == null || geography == null || racket == null)
                return null;
            var view = Look(house, lastGameHour);
            hold = view.CardHold;
            return view.Card;
        }

        /// <summary>The player's own context, for the page.</summary>
        public EventContext PlayerContext() =>
            StreetEvents.ContextFor(LivingCity.Outfit.Underworld.Current,
                LivingCity.Outfit.Underworld.Current?.Player, mindConfig);

        // -------------------------------------------------------------- the sting

        /// <summary>
        /// THE COLLAR AT THE DOOR (CONN-003). The seller was the police: the men are
        /// kept at the table by a standing job on that door, and the precinct is rung
        /// with a trafficking complaint against the family - EPIC 34's own walk-up,
        /// surrender roll and station booking take it from there, and the case the
        /// telephone opens carries Deed.Trafficking.
        /// </summary>
        bool Sting(int gangId, Job job)
        {
            var door = new TerritoryBusinessId(job.TargetBusinessId);
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (!door.IsValid || crews == null || underworld == null ||
                !geography.TryGetDoorstep(door, out var step))
                return false;
            var name = TryGetBusinessView(door, out var view) ? view.BusinessName : job.TargetLabel;
            // The complaint is EPIC 34's own: if no unit can come, the call expires and
            // the men walk - the book's line says the precinct is coming, not that they
            // were taken (the Codex review's finding). The standing job keeps them at the
            // table for the officer.
            var hold = new Job
            {
                Type = OrderType.Guard,
                CrewId = job.CrewId,
                GangId = gangId,
                Men = job.Men,
                TargetBusinessId = door.Value,
                TargetLabel = name,
            };
            Place(hold);
            underworld.Issue(hold);
            StreetAlarm.Complain(new Vector3(step.X, crews.GroundY, step.Z), gangId,
                door.Value, name, lastGameHour, Deed.Trafficking);
            return true;
        }

        float WatchOn(Job job)
        {
            var door = new TerritoryBusinessId(job.TargetBusinessId);
            if (!door.IsValid || fear == null ||
                !geography.TryGetBusinessBlock(door, out var blockId))
                return 0f;
            return fear.PoliceAttention(blockId, lastGameHour);
        }

        /// <summary>A flat for a mind (PRE-001): the first vacant unit in a building on
        /// a block the house holds, its front block first.</summary>
        LivingCity.Property.ApartmentUnitId FirstVacantRoom(House house)
        {
            geography.TryGetBusinessBlock(house.Front, out var frontBlock);
            var unit = VacantOn(frontBlock);
            if (unit.IsValid)
                return unit;
            var paper = house.Roster?.Organization.BlockResponsibilities;
            for (var i = 0; paper != null && i < paper.Count; i++)
            {
                unit = VacantOn(paper[i].BlockId);
                if (unit.IsValid)
                    return unit;
            }
            return default;
        }

        readonly List<LivingCity.Property.ApartmentUnitId> vacantScratch =
            new List<LivingCity.Property.ApartmentUnitId>();

        LivingCity.Property.ApartmentUnitId VacantOn(TerritoryBlockId blockId)
        {
            if (!blockId.IsValid)
                return default;
            var buildings = LivingCity.Property.ApartmentBuildings.OnBlock(blockId);
            for (var b = 0; b < buildings.Count; b++)
            {
                LivingCity.Property.Apartments.VacantIn(buildings[b], vacantScratch);
                if (vacantScratch.Count > 0)
                    return vacantScratch[0];
            }
            return default;
        }

        string Leased(House house, HouseIntent intent)
        {
            var day = house.Runner.Campaign.Day;
            var unit = FirstVacantRoom(house);
            if (!unit.IsValid)
                return "no vacant room on our ground";
            var bought = HouseOps.BuyFlat(house, unit, day);
            if (!bought.Ok)
                return bought.Reason;
            if (intent.Role != LivingCity.Property.UnitRole.Empty)
            {
                var fit = HouseOps.FitOut(house, unit, intent.Role);
                if (!fit.Ok)
                    return fit.Reason;
            }
            if (intent.CharacterId >= 0)
            {
                var kept = HouseOps.SetKeeper(house, unit, intent.CharacterId);
                if (!kept.Ok)
                    return kept.Reason;
            }
            house.Runner.RosterMoved?.Invoke();
            return "";
        }

        string Answered(House house, HouseIntent intent)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var ctx = StreetEvents.ContextFor(underworld, house, mindConfig);
            var book = house.Runner.Events;
            var card = intent.Card;
            if (card == null && ctx != null)
                card = StreetEvents.CardOf(book, Look(house, lastGameHour), ctx,
                    ConnectionEvents.Defs);
            if (card == null || book.Pending == null || book.Pending.Id != card.Id)
                return "no card on the table";
            // THE ACTION FIRST, THE ANSWER AFTER: a refused signing leaves the card on
            // the table; a row that is its own answer (WALK AWAY) is committed and done.
            var inner = StreetEvents.IntentOf(card, intent.CharacterId);
            if (StreetEvents.HasAction(inner))
            {
                var refusal = Carry(house, inner);
                if (!string.IsNullOrEmpty(refusal))
                    return refusal;
            }
            StreetEvents.Answer(book, card, intent.CharacterId, ctx);
            return "";
        }

        /// <summary>What a house's refusals are holding back right now (P4), for the
        /// probe.</summary>
        public void CollectBackoffs(int gangId, List<(string key, double until)> into) =>
            Backoffs(gangId).Collect(into);

        /// <summary>
        /// The street as this family can see it. Its own books, and then only what a man
        /// standing on the corner could work out: who holds a door, what a week there is
        /// worth, how much ground is held, how frightened the block is, how much law is
        /// on it. Nothing about anybody else's roster, safe or shopkeeper.
        /// </summary>
        HouseView Look(House house, double gameHour)
        {
            var mine = new TerritoryGangId(house.GangId);
            viewBlocks.Clear();
            doorScratch.Clear();

            // The ground the family stands on, holds doors on, or has its front on -
            // and, through Neighbours, whatever is next to it.
            geography.TryGetBusinessBlock(house.Front, out var frontBlock);
            if (frontBlock.IsValid)
                viewBlocks.Add(frontBlock);

            var paper = house.Roster != null
                ? house.Roster.Organization.BlockResponsibilities
                : null;
            for (var i = 0; paper != null && i < paper.Count; i++)
                if (paper[i].BlockId.IsValid && !viewBlocks.Contains(paper[i].BlockId))
                    viewBlocks.Add(paper[i].BlockId);

            var standing = presence != null ? presence.Blocks : null;
            for (var i = 0; standing != null && i < standing.Count; i++)
            {
                var blockId = standing[i];
                if (viewBlocks.Contains(blockId))
                    continue;
                if (presence.TotalOf(blockId, mine) > 0f)
                    viewBlocks.Add(blockId);
            }

            incidentScratch.Clear();
            defianceScratch.Clear();
            threatScratch.Clear();

            // What the street did to us lately, as this family would have heard it: an
            // act by anybody but us, on ground we can see, recently enough to be about.
            geography.TryGetDoorstep(house.Front, out var frontDoor);
            for (var i = 0; i < streetThreats.Count; i++)
            {
                var threat = streetThreats[i];
                if (threat.By == mine ||
                    gameHour - threat.At > mindConfig.ThreatMemoryHours)
                    continue;
                threatScratch.Add(new HouseThreat(
                    threat.By, threat.BlockId, threat.At,
                    OursNear(mine, threat.Where),
                    frontDoor.IsFinite && threat.BlockId == frontBlock &&
                    Metres(frontDoor, threat.Where) <= mindConfig.HqAlarmMetres));
            }
            for (var i = 0; i < viewBlocks.Count; i++)
            {
                var blockId = viewBlocks[i];
                if (power != null)
                {
                    // THE LEDGER'S OWN HOURS, never the hour of the think (S1). An
                    // incident is in the view while the street remembers it at all,
                    // so a guard can measure its day from the LAST one; the count of
                    // unanswered is what tier 5 still has a window on.
                    power.Collect(blockId, mine, gameHour, out var total,
                        out var overdue, out var open, out var newestOpen, out var lastAt);
                    if (total > 0)
                        incidentScratch.Add(new HouseIncident(
                            blockId, open,
                            double.IsNaN(newestOpen) ? lastAt : newestOpen, lastAt,
                            overdue));
                }
                CollectDefiances(blockId, mine, gameHour);
            }

            var backoff = Backoffs(house.GangId);
            backoff.Sweep(gameHour);
            CollectCells(house, gameHour);

            var view = new HouseView
            {
                House = mine,
                Roster = house.Roster,
                Accounts = house.Runner.Accounts,
                Book = house.Runner.Book,
                Front = house.Front,
                FrontBlock = frontBlock,
                Blocks = viewBlocks,
                NeighbourLook = blockId => geography.Neighbours(blockId),
                DoorLook = blockId => Doors(blockId, mine, gameHour),
                BackoffLook = key => backoff.Blocked(key, gameHour),
                RoundLook = crewId => RoundRunning(crewId) || BagRoundPending(crewId),
                CrewBlockLook = crewId => CrewBlockOf(house, crewId),
                OpenProposalLook = (other, kind) =>
                    LivingCity.Outfit.Underworld.Current != null &&
                    LivingCity.Outfit.Underworld.Current.Diplomacy.HasOpen(
                        house.GangId, other.Value, kind, house.Runner.Campaign.Day),
                KeepOffLook = blockId => KeptOff(mine, blockId) != null,
                LastAskedLook = (other, kind) =>
                    LivingCity.Outfit.Underworld.Current != null
                        ? LivingCity.Outfit.Underworld.Current.Diplomacy.LastFiled(
                            house.GangId, other.Value, kind)
                        : -1,
                GrievanceLook = other => Relations != null
                    ? Relations.Grievance(house.GangId, other.Value)
                    : 0f,
                LossesLook = other => house.Runner.LossesThisWar(other.Value),
                TributeLook = other => TributeBetween(house, other),
                ClearableLook = other => Relations != null
                    ? Relations.Clearable(house.GangId, other.Value,
                        house.Runner.Campaign.Day,
                        LivingCity.Outfit.Underworld.Current.Diplomacy.Config.CompensationCapPerDay,
                        Relations.Config.ThreatAt,
                        LivingCity.Outfit.Underworld.Current.Diplomacy.Config.KillingFloorDays)
                    : 0,
                WalkedLook = blockId => LastWalked(mine, blockId),
                Cells = cellScratch,
                HasCounsel = Lawyer.OnBooks(house.Roster) != null,
                CounselPrice = CounselPriceFor(house, gameHour),
                PresenceLook = blockId =>
                    presence != null ? presence.TotalOf(blockId, mine) : 0f,
                FearLook = blockId =>
                    fear != null ? fear.FearOf(blockId, mine, gameHour) : 0f,
                AttentionLook = blockId =>
                    fear != null ? fear.PoliceAttention(blockId, gameHour) : 0f,
                ControlLook = blockId =>
                    control != null
                        ? control.StateOf(blockId)
                        : TerritoryControlState.Unknown,
                LeaderLook = blockId => control != null ? control.LeaderOf(blockId) : default,
                StanceLook = other => Relations != null
                    ? Relations.StanceBetween(house.GangId, other.Value)
                    : LivingCity.Outfit.Stance.Peace,
                LadderLook = other => Relations != null
                    ? Relations.StepOf(house.GangId, other.Value)
                    : LivingCity.Outfit.LadderStep.Ignore,
                EnduranceLook = other => Estimate(house, other, gameHour),
                Rivals = Rivals(house),
                LossesThisWar = WorstLosses(house),
                Incidents = incidentScratch,
                Threats = threatScratch,
                Defiances = defianceScratch,
                QuietThinks = house.QuietThinks,
                LastRefusals = Refusals(house.GangId),
                GameHour = gameHour,
                Day = (int)(gameHour / 24.0) + 1,
            };
            ReadTheStreet(house, view);
            return view;
        }

        /// <summary>One of this family's crews is close enough to be sicced on
        /// whatever happened here (CrewJobs.MarkRadius).</summary>
        bool OursNear(TerritoryGangId mine, Vector3 where)
        {
            if (crews == null)
                return false;
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.Wiped || unit.Faction != mine.Value)
                    continue;
                var anchor = UnitAnchor(unit);
                if ((anchor - where).sqrMagnitude <= CrewJobs.MarkRadius * CrewJobs.MarkRadius)
                    return true;
            }
            return false;
        }

        static float Metres(TerritoryPoint from, Vector3 to)
        {
            var dx = to.x - from.X;
            var dz = to.z - from.Z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        IReadOnlyList<HouseDoor> Doors(
            TerritoryBlockId blockId, TerritoryGangId mine, double gameHour)
        {
            if (!blockId.IsValid)
                return null;

            // One list per block per think: the mind asks the same block several times
            // walking its tiers, and rebuilding it each time would be four lookups a door.
            if (doorScratch.TryGetValue(blockId, out var built))
                return built;

            built = new List<HouseDoor>();
            doorScratch[blockId] = built;

            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (!businessId.IsValid)
                    continue;
                racket.TryGetProtector(businessId, out var protector);
                var deed = LivingCity.Business.BusinessDeeds.GangOf(businessId);
                var tenure = deed == mine.Value
                    ? DoorTenure.Ours
                    : protector == mine
                        ? DoorTenure.Paying
                        : protector.IsValid || (deed > 0 && deed != mine.Value)
                            ? DoorTenure.Rival
                            : DoorTenure.Open;

                var owed = dues.OwedOf(businessId, mine);
                var rate = WeeklyRateOf(businessId);
                var late = protector == mine && dues.TryGet(businessId, out var account) &&
                           TerritoryCollectionSchedule.IsLate(
                               owed, rate, (int)(gameHour / 24.0),
                               account.LastCollectedDay);
                // The door's own history with us, off the relationship row (AI-003):
                // when we last stood at its counter and how often we have asked.
                var lastInteraction = -1.0;
                var demands = 0;
                if (racket.TryGetRelationship(businessId, mine, out var row))
                {
                    lastInteraction = row.LastInteraction;
                    demands = row.Demands;
                }
                built.Add(new HouseDoor(
                    businessId, TierOf(businessId), rate, protector,
                    racket.StateOf(businessId, mine), owed,
                    !RacketCanAccrueAt(businessId, gameHour),
                    IsRacketable(businessId), tenure, late, lastInteraction, demands,
                    TradeOf(businessId)));
            }
            return built;
        }

        /// <summary>Doors on this block that told this family no, or would not say yes,
        /// and have not been answered - what the mind's threat and lean steps read.
        /// </summary>
        void CollectDefiances(
            TerritoryBlockId blockId, TerritoryGangId mine, double gameHour)
        {
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (!racket.TryGetRelationship(businessId, mine, out var row) ||
                    row.State == TerritoryProtectionState.Compliant)
                    continue;
                // A door that has EVER refused us and does not pay us. The threat that
                // follows moves it off Defiant, and a man who has said no once is still a
                // man who has said no.
                if (row.RefusedAt >= 0.0)
                {
                    defianceScratch.Add(new HouseDefiance(
                        businessId, blockId, row.RefusedAt, row.Threats));
                    continue;
                }
                // A HESITANT DOOR IS ON THE LADDER TOO (Z4, ruling A8): one threat a
                // day after the ask, then it is left until there is a war. A
                // hesitation never writes RefusedAt, so it is opened at the hour we
                // last stood there.
                if (row.State == TerritoryProtectionState.Hesitant)
                    defianceScratch.Add(new HouseDefiance(
                        businessId, blockId, row.LastInteraction, row.Threats));
            }
        }

        // ------------------------------------------------------------------ the law

        readonly List<HouseCell> cellScratch = new List<HouseCell>();

        /// <summary>
        /// Men of this house in the city's hands, with the court's own answer on each
        /// (AI-005). Read through LawDesk - the same pipe and the same refusals the
        /// player's POST BAIL row gets - so a mind is told no in exactly the words the
        /// ledger would print.
        /// </summary>
        void CollectCells(House house, double gameHour)
        {
            cellScratch.Clear();
            var roster = house?.Roster;
            var pipeline = LawDesk.Pipeline;
            if (roster == null || pipeline == null)
                return;
            var skill = Lawyer.SkillOf(roster);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Gone || man.Status != CharacterStatus.Jailed || man.OutOfTown)
                    continue;
                var prisoner = pipeline.Find(man.Id);
                if (prisoner == null)
                    continue;
                cellScratch.Add(new HouseCell(
                    man.Id, man.Rank, LivingCity.Police.PrisonPipeline.BailPrice(prisoner),
                    pipeline.BailRefusal(prisoner, skill), prisoner.TakenOnDay));
            }
        }

        /// <summary>What counsel costs this house this morning: the market's own
        /// figure for a lawyer dealt against its books, or 0 when it already keeps
        /// one.</summary>
        static int CounselPriceFor(House house, double gameHour)
        {
            if (house?.Roster == null || Lawyer.OnBooks(house.Roster) != null)
                return 0;
            // The campaign's own day, the same one Carry deals the man on, so the
            // price the mind weighed is the price the signing pays.
            var ad = HireMarket.CounselFor(
                house.Roster, house.Runner.Seed, house.Runner.Campaign.Day);
            return ad != null ? ad.Down : 0;
        }

        /// <summary>
        /// WHICH STREET ONE OF THIS HOUSE'S CREWS IS STANDING ON. The men themselves
        /// where the city stood them up, and the posting where it did not - a house on
        /// the paper clock has no bodies, and OPERATE IN THIS BLOCK is the whole of
        /// where its crew is. Invalid when the crew is on no block: the road between
        /// two of them belongs to nobody.
        /// </summary>
        TerritoryBlockId CrewBlockOf(House house, int crewId)
        {
            if (crews != null)
                for (var i = 0; i < crews.Units.Count; i++)
                {
                    var unit = crews.Units[i];
                    if (unit == null || unit.Wiped || unit.IsDetachment ||
                        unit.IsPolice || unit.Faction != house.GangId ||
                        unit.CrewId != crewId)
                        continue;
                    var where = unit.Position;
                    if (CrewQuarters.Inside(unit) &&
                        CrewQuarters.TryGetDoorstep(unit, out var doorstep))
                        where = doorstep;
                    return TryGetBlockAtWorld(where, out var standing) ? standing : default;
                }

            return postings.TryGetValue(crewId, out var posted) &&
                   posted.House.Value == house.GangId
                ? posted.Block
                : default;
        }

        // ----------------------------------------------------------------- the walks

        /// <summary>When each house last walked each block door to door (A21). The
        /// mind reads it; the player's own key does not.</summary>
        readonly Dictionary<(int gang, string block), double> walked =
            new Dictionary<(int, string), double>();

        double LastWalked(TerritoryGangId mine, TerritoryBlockId blockId) =>
            blockId.IsValid && walked.TryGetValue((mine.Value, blockId.Value), out var at)
                ? at
                : -1.0;

        void NoteWalked(TerritoryGangId gang, TerritoryBlockId blockId, double gameHour)
        {
            if (gang.IsValid && blockId.IsValid)
                walked[(gang.Value, blockId.Value)] = gameHour;
        }

        // ---------------------------------------------------------------- the doing

        /// <summary>
        /// One intent, through the door the player uses for the same thing. Answers the
        /// refusal, or empty when it was taken.
        /// </summary>
        string Carry(House house, HouseIntent intent)
        {
            var mine = new TerritoryGangId(house.GangId);
            switch (intent.Kind)
            {
                case HouseIntentKind.Command:
                    return Order(house, mine, intent);

                case HouseIntentKind.Job:
                    if (intent.Job == null)
                        return "no order";
                    intent.Job.GangId = house.GangId;
                    Place(intent.Job);
                    var issued = LivingCity.Outfit.Underworld.Current.Issue(intent.Job);
                    return issued.Ok ? "" : issued.Reason;

                case HouseIntentKind.SetDuty:
                    return HouseOps.SetDuty(house, intent.CharacterId, intent.Duty).Reason;

                case HouseIntentKind.AssignToCrew:
                    return HouseOps.AssignToCrew(
                        house, intent.CharacterId, intent.CrewId).Reason;

                case HouseIntentKind.Promote:
                    return HouseOps.Promote(house, intent.CharacterId, out _).Reason;

                case HouseIntentKind.Demote:
                    return HouseOps.Demote(house, intent.CharacterId).Reason;

                case HouseIntentKind.SetPolicy:
                    return HouseOps.SetPolicy(house, intent.CrewId, intent.Policy).Reason;

                case HouseIntentKind.AssignBlock:
                    return HouseOps.AssignBlock(
                        house, intent.BlockId, intent.CharacterId,
                        geography.TryGetBlock(intent.BlockId, out _)).Reason;

                case HouseIntentKind.Buy:
                    return Bought(house, intent);

                case HouseIntentKind.SetStance:
                    if (Relations == null)
                        return "there is no city to fall out in";
                    Relations.SetPending(
                        house.GangId, intent.Other.Value, intent.Stance);
                    return "";

                case HouseIntentKind.Warn:
                    // A word is a proposal since DIPL-003 (Warn, Threaten, Bill through
                    // Propose); the old intent kind stays in the enum for saved values.
                    return "a word is a proposal now";

                // THE TABLE (EPIC 42). The same two calls the ledger's TABLE makes;
                // the receiver, when it is a mind, answers at the desk through the
                // same Look its own think reads.
                case HouseIntentKind.Propose:
                    return HouseOps.Propose(
                        LivingCity.Outfit.Underworld.Current, house, intent.Proposal,
                        other => Look(other, lastGameHour)).Reason;

                case HouseIntentKind.Reply:
                    return HouseOps.Reply(
                        LivingCity.Outfit.Underworld.Current, house, intent.ProposalId,
                        intent.Accept).Reason;

                case HouseIntentKind.Cancel:
                    // The player's own key: CampaignRunner.Cancel. The street's watch
                    // table is cleared when the crew's book empties (CrewJobs.SendHome).
                    return house.Runner.Cancel(house.Roster, intent.CharacterId).Reason;

                case HouseIntentKind.Bail:
                    return LawDesk.PostBail(house, intent.CharacterId).Reason;

                case HouseIntentKind.Retain:
                    return HouseOps.Retain(house, HireMarket.CounselFor(
                        house.Roster, house.Runner.Seed,
                        house.Runner.Campaign.Day)).Reason;

                // EPIC 40: the flats, the man, the card, the terms, the kilos - each
                // through the same HouseOps door the ledger's own buttons use.
                case HouseIntentKind.Lease:
                    return Leased(house, intent);

                case HouseIntentKind.FitOut:
                    return LivingCity.Property.Apartments.TryParseKey(intent.Listing, out var fitUnit)
                        ? HouseOps.FitOut(house, fitUnit, intent.Role).Reason
                        : "no such room";

                case HouseIntentKind.SetKeeper:
                    return LivingCity.Property.Apartments.TryParseKey(intent.Listing, out var keptUnit)
                        ? HouseOps.SetKeeper(house, keptUnit, intent.CharacterId).Reason
                        : "no such room";

                case HouseIntentKind.Sign:
                {
                    var signed = HouseOps.Sign(house, LivingCity.Outfit.Underworld.Current,
                        intent.Card, intent.CrewId, house.Runner.Campaign.Day);
                    if (signed.Ok)
                        house.Runner.RosterMoved?.Invoke();
                    return signed.Reason;
                }

                case HouseIntentKind.Card:
                    return Answered(house, intent);

                case HouseIntentKind.AcceptTerms:
                    return HouseOps.AcceptTerms(house, LivingCity.Outfit.Underworld.Current,
                        house.Runner.Campaign.Day).Reason;

                case HouseIntentKind.Sell:
                    return HouseOps.Sell(house, house.Runner.Campaign.Day, out _, out _).Reason;
            }
            return "nothing to do";
        }

        /// <summary>
        /// Gives a mind-built street job the same canonical doorstep and block that a
        /// job built from the player's map already carries. The pure mind only names a
        /// business/block; resolving that name into world coordinates belongs here at
        /// the scene edge. Without this, an Assault, Guard or wrecking order marched to
        /// the default point at world origin.
        /// </summary>
        /// <summary>The ledger's own door to the placing below (EPIC 42): a sit-down
        /// the player sends is walked to the other house's real doorstep.</summary>
        public void PlaceJob(Job job) => Place(job);

        void Place(Job job)
        {
            if (job == null || geography == null)
                return;

            var businessId = new TerritoryBusinessId(job.TargetBusinessId);
            if (businessId.IsValid && geography.TryGetDoorstep(businessId, out var door))
            {
                job.TargetX = door.X;
                job.TargetZ = door.Z;
                if (geography.TryGetBusinessBlock(businessId, out var doorBlock) &&
                    geography.TryGetBlock(doorBlock, out var doorDefinition))
                    job.TargetBlockId = doorDefinition.LegacyBlockId;
                return;
            }

            var blockId = new TerritoryBlockId(job.TargetLabel);
            if (!blockId.IsValid || !geography.TryGetBlock(blockId, out var block))
                return;

            job.TargetBlockId = block.LegacyBlockId;
            job.TargetX = block.Center.X;
            job.TargetZ = block.Center.Z;
        }

        /// <summary>
        /// THE GUARDS WENT AT THEM (D10 iv). A house that puts its men between an
        /// attacker and a door it is paid for has answered for that street, whatever the
        /// fight then decides.
        /// </summary>
        public void NoteGuardsEngaged(TerritoryBusinessId door, TerritoryGangId house)
        {
            if (power == null || geography == null || !house.IsValid)
                return;
            if (!geography.TryGetBusinessBlock(door, out var blockId))
                return;
            power.Answered(blockId, house, lastGameHour);
            RecordRetaliation(blockId, house);
        }

        /// <summary>
        /// THE WATCH IS STOOD (ruling A22b). A crew has reached the door it was sent to
        /// sit on: every incident against its house on that block still inside the
        /// answer window is answered by it - a guard posted an hour after the shooting
        /// is the house coming when called, not the house arriving late.
        /// </summary>
        public void NoteGuardStanding(TerritoryBusinessId door, TerritoryGangId house)
        {
            if (power == null || geography == null || !house.IsValid)
                return;
            if (!geography.TryGetBusinessBlock(door, out var blockId))
                return;
            power.Answered(blockId, house, lastGameHour);
        }

        /// <summary>
        /// A KILLING THAT HAPPENED ON PAPER (D16). No body was met, so no street event
        /// fired; the block still hears it, and it hears whose men did it.
        /// </summary>
        public void RecordKilling(TerritoryGangId by, Vector3 where)
        {
            if (fear == null || !by.IsValid || !TryGetBlockForAct(where, out var blockId))
                return;
            RecordFear(new TerritoryFearEvent(
                by, blockId, TerritoryFearCategory.Killing, 1f,
                TerritoryFearVisibility.Public, lastGameHour));
        }

        /// <summary>
        /// A HOUSE CAME WHEN IT WAS CALLED. The street learns what that family is worth
        /// on it - the one Fear act nobody files by hitting anybody (FEAR: successful
        /// retaliation), and it is filed for every house, the player's included.
        /// </summary>
        public void RecordRetaliation(TerritoryBlockId blockId, TerritoryGangId house)
        {
            if (fear == null || !blockId.IsValid || !house.IsValid)
                return;
            RecordFear(new TerritoryFearEvent(
                house, blockId, TerritoryFearCategory.SuccessfulRetaliation, 1f,
                TerritoryFearVisibility.Public, lastGameHour));
        }

        /// <summary>
        /// Money out of the safe and a thing into a man's hands, through the same two
        /// calls the ledger's shop uses: HouseOps.Purchase, then the quartermaster.
        /// </summary>
        static string Bought(House house, HouseIntent intent)
        {
            var paid = HouseOps.Purchase(house, intent.Price, out var dirtyPart);
            if (!paid.Ok)
                return paid.Reason;

            var item = RosterOps.AddEquipment(
                house.Roster, intent.Kit, intent.Listing, intent.Price);
            if (item == null)
            {
                HouseOps.Refund(house, intent.Price, dirtyPart);
                return "the dealer had nothing";
            }

            var given = RosterOps.GiveEquipment(
                house.Roster, item.Id, intent.CharacterId);
            house.Touch();
            return given.Ok ? "" : given.Reason;
        }

        // A word between houses was printed and swept here until DIPL-003: it is a
        // proposal now (Warn, Threaten, Bill), answered at the desk, and its lapse is
        // the grudge - HouseDiplomacy.Expire, at midnight, by the day and not by a
        // 48-hour sweep that fired whatever the other house had done.

        /// <summary>A territory order, built here and submitted through the gateway - the
        /// mutation boundary every house's orders cross.</summary>
        string Order(House house, TerritoryGangId mine, HouseIntent intent)
        {
            // A HOUSE THE CITY NEVER STOOD UP still works its orders (RIVAL-008). The
            // gateway needs a unit to refuse or accept; a family with no bodies has
            // none, so its orders are worked on paper by the same ledgers.
            if (!Stands(mine))
                return PaperOrder(house, mine, intent);

            var group = TerritoryCommandNodeId.Crew(intent.CrewId);
            TerritoryCommandResult result;
            // A ROUND A MIND FILES IS A MIND'S ROUND (ruling A2): a book job may take
            // the crew off it. Set around the submit like scheduledSubmitDay.
            var previousOrigin = submittingOrigin;
            submittingOrigin = TerritoryRoundOrigin.Mind;
            try
            {
                switch (intent.Order)
                {
                    case HouseOrder.OperateInBlock:
                        result = Commands.Submit(
                            new OperateInBlockCommand(group, intent.BlockId) { House = mine });
                        break;
                    case HouseOrder.ApproachBusiness:
                        result = Commands.Submit(
                            new ApproachBusinessCommand(group, intent.BusinessId,
                                intent.FollowUp) { House = mine });
                        break;
                    case HouseOrder.LeanOnHoldouts:
                        result = Commands.Submit(
                            new LeanOnHoldoutsCommand(group, intent.BlockId) { House = mine });
                        break;
                    case HouseOrder.ShakeDownBlock:
                        result = Commands.Submit(
                            new ShakeDownBlockCommand(group, intent.BlockId) { House = mine });
                        break;
                    case HouseOrder.CollectDues:
                        result = Commands.Submit(
                            new CollectDuesCommand(group, intent.BlockId) { House = mine });
                        break;
                    default:
                        return "no such order";
                }
            }
            finally
            {
                submittingOrigin = previousOrigin;
            }

            return result.Status == TerritoryCommandStatus.Rejected
                ? (string.IsNullOrEmpty(result.Reason) ? "refused" : result.Reason)
                : "";
        }
    }
}
