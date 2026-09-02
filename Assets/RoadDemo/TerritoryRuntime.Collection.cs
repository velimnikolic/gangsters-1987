using System.Collections.Generic;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// EPIC 9 — the money side of territory. The dues meter (ECON-001), the owners it
    /// is collected from (ECON-002/003), the rounds that physically walk it home
    /// (ECON-004), policy and archetype at the door (ECON-005), the names men make on
    /// their own streets (ECON-006), and the tier guard's heat (ECON-007). The pure
    /// arithmetic lives in TerritoryEconomy.cs; this partial is the scene's drive.
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        readonly TerritoryDuesLedger dues = new TerritoryDuesLedger();
        readonly TerritoryReputationLedger reputation = new TerritoryReputationLedger();
        readonly Dictionary<TerritoryBusinessId, TerritoryOwnerProfile> ownerProfiles =
            new Dictionary<TerritoryBusinessId, TerritoryOwnerProfile>();
        readonly List<CollectionRound> rounds = new List<CollectionRound>();
        int lastAccruedDay = -1;

        public TerritoryDuesLedger Dues => dues;
        public TerritoryReputationLedger Reputation => reputation;

        enum RoundStage
        {
            Walking,
            HeadingHome,
        }

        /// <summary>What a walk down a block's doors is FOR. The route, the arrival and
        /// the abandon are one machine; only what happens at the counter differs.</summary>
        internal enum RoundKind
        {
            /// <summary>Money: the paying doors, the bag, the front.</summary>
            Collect,

            /// <summary>The ask: every door that does not pay us yet.</summary>
            ShakeDown,

            /// <summary>The threat: every door holding out on us.</summary>
            Lean,
        }

        sealed class CollectionRound
        {
            public RoundKind Kind = RoundKind.Collect;
            public int CrewId;
            public TerritoryGangId GangId;
            public TerritoryBlockId BlockId;
            public CrewWalker Collector;
            public readonly List<RoundStop> Stops = new List<RoundStop>();
            public int Cursor;
            public int Carried;
            public int Missed;
            public RoundStage Stage;

            /// <summary>He is inside this stop's shop. The arrival sampling runs several
            /// times a second and the conversation takes seconds, so without this the
            /// same door would be entered again and again while he stood at its counter.
            /// </summary>
            public bool InTheDoor;
        }

        readonly struct RoundStop
        {
            public RoundStop(TerritoryBusinessId businessId, Vector3 door)
            {
                BusinessId = businessId;
                Door = door;
            }

            public TerritoryBusinessId BusinessId { get; }
            public Vector3 Door { get; }
        }

        // ------------------------------------------------------------------ owners

        /// <summary>The man behind this counter, dealt once from the city seed
        /// (ECON-002) and remembered - hashing is cheap, but the same question a frame
        /// should not cost the same hash a frame.</summary>
        public TerritoryOwnerProfile OwnerProfileOf(TerritoryBusinessId businessId)
        {
            if (!businessId.IsValid)
                return TerritoryOwnerProfile.Neutral;
            if (ownerProfiles.TryGetValue(businessId, out var profile))
                return profile;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            var seed = business != null && business.Populated ? business.CitySeed : 1987;
            profile = TerritoryOwnerProfile.Deal(seed, businessId);
            ownerProfiles[businessId] = profile;
            return profile;
        }

        /// <summary>What this place pays a week, off the price table - never a flat
        /// constant. The unknown shop is the smallest shopfront, never free money.</summary>
        public int WeeklyRateOf(TerritoryBusinessId businessId)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.Populated &&
                business.Directory.TryGet(businessId, out var record))
                return LivingCity.Outfit.EconomyPrices.ProtectionPerWeek(record.Archetype);
            return LivingCity.Outfit.EconomyPrices.Unknown.ProtectionPerWeek;
        }

        int TierOf(TerritoryBusinessId businessId)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.Populated &&
                business.Directory.TryGet(businessId, out var record))
                return (int)LivingCity.Outfit.EconomyPrices.Of(record.Archetype).Tier;
            return 1;
        }

        /// <summary>The two threshold shifts a demand at this door carries: the owner's
        /// own nerve (ECON-002) and the tier guard (ECON-007).</summary>
        void DemandShifts(
            TerritoryBusinessId businessId, out float ownerShift, out float tierBar)
        {
            ownerShift = OwnerProfileOf(businessId).NerveShift;
            tierBar = TerritoryTierGuard.AcceptBar(TierOf(businessId));
        }

        /// <summary>A Connected owner turns police eyes on the family that leans on him
        /// (ECON-002). Quiet men draw nothing.</summary>
        void NoteConnectedHeat(TerritoryBusinessId businessId)
        {
            if (fear == null || geography == null ||
                !geography.TryGetBusinessBlock(businessId, out var blockId))
                return;
            var connections = OwnerProfileOf(businessId).Connections;
            if (connections > 0.55f)
                fear.NotePoliceAttention(blockId, (connections - 0.55f) * 2f, lastGameHour);
        }

        // -------------------------------------------------------------- reputation

        float ReputationScale(
            TerritoryCharacterId characterId, TerritoryBlockId blockId, double gameHour)
        {
            if (!characterId.IsValid || geography == null ||
                !geography.TryGetBlock(blockId, out var definition))
                return 1f;
            return reputation.PresenceScale(
                characterId.Value, definition.NeighborhoodName, gameHour);
        }

        /// <summary>The act happened at this door and this man did it: his name grows
        /// on THIS street (ECON-006), nowhere else.</summary>
        void NoteReputationAt(
            TerritoryBusinessId businessId, TerritoryCharacterId actorId, float amount)
        {
            if (!actorId.IsValid || geography == null ||
                !geography.TryGetBusinessBlock(businessId, out var blockId) ||
                !geography.TryGetBlock(blockId, out var definition))
                return;
            reputation.Note(
                actorId.Value, definition.NeighborhoodName, amount, lastGameHour);
        }

        // ----------------------------------------------------------------- accrual

        /// <summary>One day of every meter (ECON-001), on the campaign-day boundary of
        /// the territory clock. Compliant shops accrue their rate; a shop no family is
        /// paid by any more has its account dropped - a lapse stops the meter rather
        /// than building a debt nobody can collect.</summary>
        void AccrueDues(double gameHour)
        {
            if (racket == null)
                return;

            var day = (int)(gameHour / 24.0);
            if (lastAccruedDay < 0)
            {
                lastAccruedDay = day;
                return;
            }
            if (day <= lastAccruedDay)
                return;
            var previousDay = lastAccruedDay;
            var days = Mathf.Min(day - lastAccruedDay, 14);
            lastAccruedDay = day;

            var ids = racket.Businesses;
            for (var i = ids.Count - 1; i >= 0; i--)
            {
                var businessId = ids[i];
                if (racket.TryGetProtector(businessId, out var protector))
                {
                    var rate = WeeklyRateOf(businessId);
                    for (var d = 0; d < days; d++)
                    {
                        var boundaryHour = (previousDay + d + 1) * 24d;
                        if (RacketCanAccrueAt(businessId, boundaryHour))
                            dues.AccrueDay(businessId, protector, rate);
                    }
                }
                else if (dues.TryGet(businessId, out _))
                {
                    dues.Drop(businessId);
                }
            }
        }

        // ------------------------------------------------------------------ rounds

        public TerritoryCommandExecution Execute(CollectDuesCommand command)
        {
            if (!command.BlockId.IsValid)
                return TerritoryCommandExecution.Reject("Unknown territory block.");
            if (racket == null || geography == null)
                return TerritoryCommandExecution.Reject(
                    "The racket is not running in this scene.");

            var unit = FindPlayerUnit(command.GroupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);

            // The stops: every shop on the block that pays THIS family and owes
            // anything. The order follows the street - nearest first from where the
            // men stand, then nearest from each door - never the id list.
            var gang = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            var candidates = new List<RoundStop>();
            var here = geography.BusinessesOf(command.BlockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (racket.StateOf(businessId, gang) != TerritoryProtectionState.Compliant)
                    continue;
                if (!RacketCanAccrueAt(businessId, lastGameHour))
                    continue;
                if (dues.OwedOf(businessId, gang) <= 0)
                    continue;
                if (!TryGetBusinessApproach(businessId, out var door))
                    continue;
                candidates.Add(new RoundStop(businessId, door));
            }

            if (candidates.Count == 0)
                return TerritoryCommandExecution.Reject(
                    "Nothing on that block owes us anything yet.");

            var round = new CollectionRound
            {
                CrewId = unit.CrewId,
                GangId = gang,
                BlockId = command.BlockId,
                Collector = CollectorOf(unit),
            };
            if (round.Collector == null)
                return TerritoryCommandExecution.Reject(
                    "The crew has no hood who can carry the collection bag.");
            OrderStops(candidates, UnitAnchor(unit), round.Stops);

            // One errand at a time: the old doorstep order and any old round go.
            DropPendingApproaches(unit.CrewId);
            rounds.Add(round);
            BumpRacketSeam();
            // The duffel is the collection job's equipment, not loot spawned by the
            // first shop. This exact hood carries it from departure until the round
            // banks, is abandoned, or he can no longer continue.
            BagCarry.Give(round.CrewId, round.Collector);

            if (!crews.MarchTo(unit, round.Stops[0].Door))
            {
                rounds.Remove(round);
                BagCarry.Drop(round.CrewId, banked: true);
                return TerritoryCommandExecution.Reject(
                    "The physical crew refused the round.");
            }

            return TerritoryCommandExecution.Pending(
                "The round is walking; the take banks at the front.");
        }

        /// <summary>The walk order, from the one shared planner (ECON-004) - the same
        /// arithmetic the headless suite asserts.</summary>
        static void OrderStops(List<RoundStop> candidates, Vector3 from, List<RoundStop> into)
        {
            into.Clear();
            var seeds = new List<TerritoryRoundStopSeed>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
                seeds.Add(new TerritoryRoundStopSeed(
                    candidates[i].BusinessId.Value,
                    candidates[i].Door.x, candidates[i].Door.z));

            var order = new List<int>(candidates.Count);
            TerritoryRoundPlanner.Order(seeds, from.x, from.z, order);
            for (var i = 0; i < order.Count; i++)
                into.Add(candidates[order[i]]);
        }

        static Vector3 UnitAnchor(DemoCrews.Unit unit)
        {
            if (unit == null)
                return Vector3.zero;
            if (unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null)
                return unit.Boss.Tf.position;
            for (var i = 0; i < unit.Hoods.Count; i++)
                if (unit.Hoods[i] != null && !unit.Hoods[i].Dead && unit.Hoods[i].Tf != null)
                    return unit.Hoods[i].Tf.position;
            return unit.Root != null ? unit.Root.position : Vector3.zero;
        }

        /// <summary>
        /// WHO CARRIES THE BAG. A man his lieutenant marked for it first - the duty is a
        /// standing instruction on the books (Character.Duty), and the whole point of
        /// marking a man is that the sim then picks him without being told again.
        ///
        /// Failing that, the old rule: the lieutenant himself, then the first hood on
        /// his feet. A crew with nobody marked still collects - the mark is an
        /// arrangement, not a requirement.
        /// </summary>
        static CrewWalker CollectorOf(DemoCrews.Unit unit)
        {
            if (unit == null)
                return null;

            var roster = LivingCity.Gameplay.PersonnelDirector.Instance != null
                ? LivingCity.Gameplay.PersonnelDirector.Instance.Roster
                : null;
            if (roster != null)
                for (var i = 0; i < unit.Hoods.Count; i++)
                {
                    var hood = unit.Hoods[i];
                    if (hood == null || hood.Dead || hood.Tf == null)
                        continue;
                    // A character id of 0 is a REAL id in this project; a man the roster
                    // does not know is a null lookup, never a zero.
                    var man = roster.Find(hood.CharacterId);
                    if (man != null && !man.Gone &&
                        man.Duty == LivingCity.Personnel.Duty.Collector)
                        return hood;
                }

            // Match DemoCrews.MarchTo's lead choice exactly. A boarded hood may be
            // temporarily hidden before MarchTo unboards him, but he is still the man
            // assigned to this job and the bag appears with him when he steps out.
            if (unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null)
                return unit.Boss;
            for (var i = 0; i < unit.Hoods.Count; i++)
            {
                var hood = unit.Hoods[i];
                if (hood != null && !hood.Dead && hood.Tf != null)
                    return hood;
            }
            return null;
        }

        // ------------------------------------------------------- the standing round

        /// <summary>Which crew has already been sent to which block on which day, so a
        /// Business tick every four hours does not send the same round three times.
        /// </summary>
        readonly HashSet<(int crewId, string blockId, int day)> roundsSentToday =
            new HashSet<(int, string, int)>();

        readonly List<LivingCity.Personnel.Character> collectorScratch =
            new List<LivingCity.Personnel.Character>();

        /// <summary>
        /// THE ROUNDS THAT GO OUT BY THEMSELVES.
        ///
        /// Every block on a lieutenant's paper has a collection weekday of its own
        /// (TerritoryCollectionSchedule). On that day, once the shops are open, a crew of
        /// his that has a man on the bag walks the block's paying doors without being
        /// told. Nothing else is automatic: a DEMAND is still an order the player gives,
        /// which is the rule DriveRivalDemands is built on as well.
        ///
        /// Everything goes through the command gateway rather than straight into the
        /// executor - the gateway is the mutation boundary and it records the command.
        /// </summary>
        void TendScheduledRounds(double gameHour)
        {
            if (crews == null || geography == null || dues == null || Commands == null)
                return;

            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return;

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var day = outfit != null ? outfit.Campaign.Day : 1;
            var dayOfWeek = outfit != null
                ? outfit.Campaign.DayOfWeek
                : (day > 1 ? day - 1 : 0) % 7;
            var hourOfDay = (int)(gameHour - (int)(gameHour / 24.0) * 24.0);

            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
            {
                var blockId = paper[i].BlockId;
                if (!blockId.IsValid)
                    continue;

                // The crew whose lieutenant answers for this block. No crew, no round -
                // paper alone does not collect anything.
                LivingCity.Personnel.Crew crew = null;
                for (var c = 0; c < roster.Crews.Count && crew == null; c++)
                    if (roster.Crews[c].LieutenantId == paper[i].LeaderId)
                        crew = roster.Crews[c];
                if (crew == null)
                    continue;

                var key = (crew.Id, blockId.Value, day);
                if (roundsSentToday.Contains(key))
                    continue;

                LivingCity.Personnel.RosterOps.CollectorsOf(
                    roster, crew.Id, collectorScratch);
                if (!TryGetCollectibleDues(blockId, out var owed))
                    owed = 0;

                if (!TerritoryCollectionSchedule.ShouldSend(
                        dayOfWeek, hourOfDay, blockId, owed,
                        collectorScratch.Count > 0, RoundRunning(crew.Id), false))
                    continue;

                var result = Commands.Submit(new CollectDuesCommand(
                    TerritoryCommandNodeId.Crew(crew.Id), blockId));
                // Only a round that was TAKEN counts as sent. A crew in a fight or in a
                // car is refused, and the next Business tick asks again the same day.
                if (result.Status != TerritoryCommandStatus.Accepted &&
                    result.Status != TerritoryCommandStatus.Pending &&
                    result.Status != TerritoryCommandStatus.Succeeded)
                    continue;

                roundsSentToday.Add(key);

                // A ROUND THAT GOES OUT BY ITSELF HAS TO SAY SO. It is the one thing in
                // the racket the player did not order, and without a line on the wire he
                // learns it happened only when the money arrives - or never, if it does
                // not. The street gets a word too: his men just walked off.
                racket?.FileRound(blockId, new TerritoryGangId(
                        LivingCity.Gangs.GangCatalog.PlayerGangId),
                    TerritoryDoorNews.RoundOut, gameHour, owed,
                    StopsOwing(blockId), 0);
                var lieutenant = roster.Find(paper[i].LeaderId);
                CrewOverlay.Announce(
                    (lieutenant != null ? lieutenant.Surname.ToUpperInvariant() + "'S" : "OUR") +
                    " ROUND IS OUT ON " + BlockWord(blockId), 4f,
                    new Color(0.85f, 0.9f, 1f));
            }

            // The book only has to remember today; anything older can never match again.
            if (roundsSentToday.Count > 64)
                roundsSentToday.RemoveWhere(entry => entry.day != day);
        }

        /// <summary>How many of the block's doors owe us anything - what the round's own
        /// slip prints as its stop count.</summary>
        int StopsOwing(TerritoryBlockId blockId)
        {
            if (geography == null || racket == null || dues == null)
                return 0;
            var gang = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            var stops = 0;
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
                if (racket.StateOf(here[i].BusinessId, gang) ==
                        TerritoryProtectionState.Compliant &&
                    dues.OwedOf(here[i].BusinessId, gang) > 0)
                    stops++;
            return stops;
        }

        /// <summary>The block's own name for a line the player reads, or its id where
        /// the city cannot name it.</summary>
        string BlockWord(TerritoryBlockId blockId) =>
            PlayerQuery != null && PlayerQuery.TryGetBlock(blockId, out var view) &&
            view != null
                ? view.BlockName.ToUpperInvariant()
                : blockId.Value.ToUpperInvariant();

        /// <summary>Whether this crew already has a round out - manual or standing.
        /// </summary>
        bool RoundRunning(int crewId)
        {
            for (var i = 0; i < rounds.Count; i++)
                if (rounds[i].CrewId == crewId)
                    return true;
            return false;
        }

        static CrewWalker EnsureCollector(CollectionRound round, DemoCrews.Unit unit)
        {
            var collector = round.Collector;
            // DoorBeat temporarily hides the collector while he is inside a shop. That
            // is not a lost carrier and must never move the bag to a hood outside.
            if (collector != null && !collector.Dead && collector.Tf != null)
                return collector;

            collector = CollectorOf(unit);
            round.Collector = collector;
            if (collector != null)
                BagCarry.Give(round.CrewId, collector);
            return collector;
        }

        /// <summary>The round the street card marks, if this crew is walking one.</summary>
        public bool TryGetRound(
            int crewId, out int carried, out int stopsLeft, out Vector3 nextDoor)
        {
            carried = 0;
            stopsLeft = 0;
            nextDoor = default;
            for (var i = 0; i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round.CrewId != crewId)
                    continue;
                carried = round.Carried;
                stopsLeft = round.Stage == RoundStage.Walking
                    ? round.Stops.Count - round.Cursor
                    : 0;
                nextDoor = round.Stage == RoundStage.Walking
                    ? round.Stops[round.Cursor].Door
                    : HomeDoor();
                return true;
            }

            return false;
        }

        /// <summary>What a shop owes us and when it last paid - the ledger surfaces
        /// read it (ECON-008); nothing invented, nothing when nothing is owed.</summary>
        public bool TryGetDues(
            TerritoryBusinessId businessId, out int owed, out int lastPaidDay)
        {
            owed = 0;
            lastPaidDay = -1;
            if (!dues.TryGet(businessId, out var account) ||
                account.GangId.Value != LivingCity.Gangs.GangCatalog.PlayerGangId)
                return false;
            owed = account.Owed;
            lastPaidDay = account.LastCollectedDay;
            return true;
        }

        /// <summary>What the player's paying doors on a block can yield right now.
        /// Every order surface reads this so collection stays closed until the first
        /// daily dues tick has actually put money on the ledger.</summary>
        public bool TryGetCollectibleDues(TerritoryBlockId blockId, out int owed)
        {
            owed = 0;
            if (!blockId.IsValid || geography == null || racket == null)
                return false;

            var gang = new TerritoryGangId(
                LivingCity.Gangs.GangCatalog.PlayerGangId);
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (racket.StateOf(businessId, gang) ==
                    TerritoryProtectionState.Compliant &&
                    RacketCanAccrueAt(businessId, lastGameHour))
                    owed += dues.OwedOf(businessId, gang);
            }
            return owed > 0;
        }

        /// <summary>Men on a round who have reached the door they were walking to. The
        /// same sampling pass that notices an approach notices a stop.</summary>
        void NoteRoundArrival(
            DemoCrews.Unit unit, CrewWalker actor,
            TerritoryActorObservation observation, double gameHour)
        {
            if (rounds.Count == 0 || actor?.Tf == null || unit.Faction != 0)
                return;

            for (var i = rounds.Count - 1; i >= 0; i--)
            {
                var round = rounds[i];
                if (round.CrewId != unit.CrewId)
                    continue;

                // Only the hood who visibly owns the collection bag settles this
                // round. If he is lost, ownership visibly transfers to one survivor.
                var collector = EnsureCollector(round, unit);
                if (collector == null || actor != collector)
                    return;

                if (round.Stage == RoundStage.Walking)
                {
                    // He is already through this door; the sampling pass runs on its own
                    // cadence and would otherwise open the same stop again every tick of
                    // the conversation.
                    if (round.InTheDoor)
                        return;

                    var stop = round.Stops[round.Cursor];
                    if ((actor.Tf.position - stop.Door).sqrMagnitude >
                        approachRadiusMetres * approachRadiusMetres)
                        return;

                    // THE HAND GOES OUT AT THE COUNTER. The money used to be settled and
                    // called over the street the instant the men came within reach of the
                    // door - the visit that followed was a mime of a stop that had
                    // already happened. He goes in, the shop pays him inside, and the
                    // round only moves on when he is back on the pavement with the bag.
                    round.InTheDoor = true;
                    var walking = round;
                    var here = stop;
                    var who = unit;
                    var seen = observation;
                    DoorBeat.VisitBusiness(
                        actor, stop.BusinessId, stop.Door,
                        whenInside: () => SettleDoor(
                            walking, here, who, seen, lastGameHour),
                        whenOut: () => NextStop(walking, who));
                }
                else
                {
                    var home = HomeDoor();
                    if ((actor.Tf.position - home).sqrMagnitude > HomeRadius * HomeRadius)
                        return;
                    Bank(round, gameHour);
                }

                return;
            }
        }

        const float HomeRadius = 18f;

        Vector3 HomeDoor()
        {
            var director = LivingCity.Gameplay.OutfitDirector.Instance;
            if (director != null && director.TryGetHeadquarters(out var hq, out _))
                return hq;
            return Vector3.zero;
        }

        bool HasHome() =>
            LivingCity.Gameplay.OutfitDirector.Instance != null &&
            LivingCity.Gameplay.OutfitDirector.Instance.TryGetHeadquarters(out _, out _);

        /// <summary>
        /// The hand goes out (ECON-003/005/007). The owner pays, pays part with a
        /// story, or does not pay; the crew's policy and the lieutenant's own hand say
        /// what actually changes pockets, what fear the stop leaves and what heat it
        /// draws; and two misses running let the arrangement lapse.
        /// </summary>
        void SettleStop(
            CollectionRound round, RoundStop stop, DemoCrews.Unit unit,
            TerritoryActorObservation observation, double gameHour)
        {
            var businessId = stop.BusinessId;
            if (!RacketCanAccrueAt(businessId, gameHour))
            {
                var name = businessId.Value;
                if (TryGetBusinessView(businessId, out var closed))
                    name = closed.BusinessName;
                CrewOverlay.Announce(
                    name.ToUpperInvariant() + " IS CLOSED - NOTHING TO COLLECT", 3f,
                    new Color(1f, 0.75f, 0.45f));
                return;
            }
            var day = (int)(gameHour / 24.0);
            var owed = dues.OwedOf(businessId, round.GangId);

            geography.TryGetBusinessBlock(businessId, out var blockId);
            var protectorFear = fear != null
                ? fear.FearOf(blockId, round.GangId, gameHour)
                : 0f;
            var trouble = fear != null ? fear.BlockFear(blockId, gameHour) : 0f;

            PolicyAndArchetype(unit, out var policyLevel, out var archetype);
            var style = TerritoryCollectionStyle.OfPolicy(policyLevel);
            TerritoryCollectionStyle.ArchetypeScales(
                archetype, out var takeScale, out var fearScale, out var heatScale);

            var business = LivingCity.Business.BusinessRuntime.Instance;
            var citySeed = business != null && business.Populated ? business.CitySeed : 1987;
            var result = TerritoryPaymentRoll.Roll(
                owed, OwnerProfileOf(businessId), protectorFear, trouble,
                style.ShortAcceptedShare, citySeed, day, businessId);

            var paid = Mathf.Min(owed, Mathf.RoundToInt(result.Paid * takeScale));
            round.Carried += paid;
            var missed = result.Outcome == TerritoryPaymentOutcome.Missed;
            if (missed)
                round.Missed++;

            // MONEY ON THE WIRE. A door that pays in full says nothing - the round's
            // own slip covers it, and one line per paying door per week would bury the
            // book in good news. A short and a miss are the two the player has to be
            // able to react to, so each files with the sum and the owner's story.
            if (missed)
                racket.FileMoney(businessId, round.GangId, TerritoryDoorNews.Missed,
                    gameHour, owed, owed, result.Excuse);
            else if (paid < owed)
                racket.FileMoney(businessId, round.GangId, TerritoryDoorNews.PaidShort,
                    gameHour, paid, owed, result.Excuse);

            var runs = dues.Settle(businessId, round.GangId, day, paid, missed);
            // A door that paid in full files nothing, so the racket's version does not
            // move - but what it owes just changed, and the block file reads that.
            BumpRacketSeam();
            if (runs >= 2)
            {
                // Twice running and nobody answered it: the arrangement lapses back
                // toward Hesitant (ECON-003), and the meter stops with it.
                racketChanges.Clear();
                if (racket.Lapse(businessId, round.GangId, gameHour, racketChanges))
                {
                    dues.Drop(businessId);
                    PublishRacket(blockId);
                }
            }

            // What the stop leaves behind: the policy's fear and heat, the
            // lieutenant's own hand on both, and the tier's heat on the money itself.
            var fearLeft = style.FearLeft * fearScale;
            if (fearLeft > 0.01f)
            {
                RecordResolvedThreat(round.GangId, businessId, fearLeft,
                    TerritoryFearVisibility.Seen, observation.CharacterId);
                NoteConnectedHeat(businessId);
            }
            var heat = style.HeatLeft * heatScale +
                       paid / 100f * TerritoryTierGuard.HeatPerHundredWeekly;
            if (heat > 0f && fear != null && blockId.IsValid)
                fear.NotePoliceAttention(blockId, heat, gameHour);

            if (paid > 0)
                NoteReputationAt(businessId, observation.CharacterId,
                    2f + Mathf.Min(6f, paid / 150f));

            // XP-003. The man who actually stood at this door banks the practice for
            // it, the same table the ordered shakedown banks through - one lesson a
            // day, so a long round does not turn into a training ground.
            if (observation.CharacterId.IsValid)
                CrewSkill.Collected(observation.CharacterId.Value, paid > 0);

            // What he came out with (or didn't), said over the door. This whole method
            // runs FROM INSIDE the shop now, once the conversation has had its seconds
            // (DoorBeat), so the wire can no longer call a stop that nobody has been
            // through the door for.
            AnnounceStop(businessId, result, paid);
        }

        bool RacketCanAccrueAt(TerritoryBusinessId businessId, double gameHour)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            return business?.Shutdowns == null ||
                   business.Shutdowns.ShouldAccrueRacketAt(businessId, gameHour);
        }

        /// <summary>He is back on the pavement with the bag: on to the next door, or
        /// home. Never while he is switched off inside a shop - a crew marched off
        /// mid-visit leaves its collector standing in somebody's back room.</summary>
        void NextStop(CollectionRound round, DemoCrews.Unit unit)
        {
            if (round == null || !rounds.Contains(round))
                return;
            round.InTheDoor = false;
            round.Cursor++;
            if (round.Cursor < round.Stops.Count)
            {
                if (unit != null && !unit.Wiped)
                    crews.MarchTo(unit, round.Stops[round.Cursor].Door);
                return;
            }

            // A SHAKEDOWN HAS NOTHING TO CARRY HOME. Only a collection walks to the
            // front: the men who have just been down a block asking for money stay on
            // the block they asked on. Marching them across the city would take the
            // presence off the ground the asking was for, and Bank would file a round
            // slip for a bag that was never picked up.
            if (round.Kind != RoundKind.Collect)
            {
                rounds.Remove(round);
                BumpRacketSeam();
                return;
            }

            round.Stage = RoundStage.HeadingHome;
            if (HasHome())
            {
                if (unit != null && !unit.Wiped)
                    crews.MarchTo(unit, HomeDoor());
            }
            else
            {
                // A scene with no front to walk to banks on the spot - the bench rigs
                // have no city and no home, and a round that can never finish is worse
                // than one that skips the walk there.
                Bank(round, lastGameHour);
            }
        }

        void AnnounceStop(
            TerritoryBusinessId businessId, TerritoryPaymentResult result, int paid)
        {
            var name = businessId.Value;
            if (TryGetBusinessView(businessId, out var view))
                name = view.BusinessName;
            name = name.ToUpperInvariant();

            switch (result.Outcome)
            {
                case TerritoryPaymentOutcome.Paid:
                    CrewOverlay.Announce("$" + paid + " COLLECTED AT " + name, 3f,
                        new Color(0.75f, 0.95f, 0.7f));
                    break;
                case TerritoryPaymentOutcome.Short:
                    CrewOverlay.Announce(
                        name + " CAME UP SHORT — $" + paid + " OF $" + result.Owed +
                        " · " + ExcuseWord(result.Excuse), 4f,
                        new Color(1f, 0.85f, 0.55f));
                    break;
                default:
                    CrewOverlay.Announce(
                        name + " DID NOT PAY · " + ExcuseWord(result.Excuse), 4f,
                        new Color(1f, 0.6f, 0.45f));
                    break;
            }
        }

        /// <summary>The story, as the owner tells it - the wire's own words, so the
        /// toast over the street and the slip in the book cannot differ about what the
        /// man said (TerritoryStandingVocabulary.ExcuseWord).</summary>
        static string ExcuseWord(TerritoryPaymentExcuse excuse) =>
            TerritoryStandingVocabulary.ExcuseWord(excuse);

        void PolicyAndArchetype(DemoCrews.Unit unit, out int policyLevel, out int archetype)
        {
            policyLevel = (int)LivingCity.Personnel.CrewPolicy.Normal;
            archetype = (int)LivingCity.Personnel.LieutenantArchetype.Soldier;
            var roster = LivingCity.Gameplay.PersonnelDirector.Instance != null
                ? LivingCity.Gameplay.PersonnelDirector.Instance.Roster
                : null;
            if (roster == null)
                return;
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                if (roster.Crews[i].Id != unit.CrewId)
                    continue;
                policyLevel = (int)roster.Crews[i].Policy;
                archetype = (int)LivingCity.Personnel.LieutenantArchetypes.Of(
                    roster.Find(roster.Crews[i].LieutenantId));
                return;
            }
        }

        /// <summary>The take reaches the front and enters the books (ECON-004/007) -
        /// the ONLY place round money becomes outfit money.</summary>
        void Bank(CollectionRound round, double gameHour)
        {
            rounds.Remove(round);
            BumpRacketSeam();
            BagCarry.Drop(round.CrewId, banked: true);
            var director = LivingCity.Gameplay.OutfitDirector.Instance;
            if (round.Carried > 0 && director != null)
                director.BankCollection(round.Carried);

            racket?.FileRound(round.BlockId, round.GangId,
                TerritoryDoorNews.RoundBanked, gameHour, round.Carried,
                round.Stops.Count, round.Missed);
            NoteRoundBanked(round.BlockId, round.Carried, round.Missed,
                LivingCity.Gameplay.OutfitDirector.Instance != null
                    ? LivingCity.Gameplay.OutfitDirector.Instance.Campaign.Day
                    : 1);

            events.Publish(new CollectionRoundSettled(
                round.BlockId, round.GangId, round.CrewId, round.Carried,
                round.Stops.Count, round.Missed, TerritoryRoundEnd.Banked, gameHour));
        }

        /// <summary>A crew retasked mid-round walked away from its own route; whatever
        /// it was carrying never reaches the books. An order countermanded is an order
        /// countermanded.</summary>
        void AbandonRound(int crewId)
        {
            for (var i = rounds.Count - 1; i >= 0; i--)
            {
                if (rounds[i].CrewId != crewId)
                    continue;
                var round = rounds[i];
                rounds.RemoveAt(i);
                BumpRacketSeam();
                BagCarry.Drop(round.CrewId, banked: false);
                // Only a round that was CARRYING something is worth a line: a crew
                // called off before its first door lost nothing.
                if (round.Carried > 0)
                {
                    racket?.FileRound(round.BlockId, round.GangId,
                        TerritoryDoorNews.RoundLost, lastGameHour, round.Carried,
                        round.Cursor, round.Missed);
                    // The loudest money event on the wire was the quietest one on the
                    // street: every ordinary stop calls itself over the door and a bag
                    // going missing said nothing at all.
                    CrewOverlay.Announce(
                        "THE BAG IS GONE · $" + round.Carried +
                        " OFF " + BlockWord(round.BlockId), 4f,
                        new Color(1f, 0.55f, 0.45f));
                }
                events.Publish(new CollectionRoundSettled(
                    round.BlockId, round.GangId, round.CrewId, round.Carried,
                    round.Cursor, round.Missed, TerritoryRoundEnd.Lost, lastGameHour));
            }
        }

        /// <summary>The risk that makes a route a route (ECON-004): a round whose crew
        /// is scattered or wiped loses its take on the street where it fell.</summary>
        void WatchRounds(double gameHour)
        {
            for (var i = rounds.Count - 1; i >= 0; i--)
            {
                var round = rounds[i];
                var unit = FindUnit(TerritoryCommandNodeId.Crew(round.CrewId));
                if (unit != null && !unit.Wiped)
                    continue;

                rounds.RemoveAt(i);
                BagCarry.Drop(round.CrewId, banked: false);
                events.Publish(new CollectionRoundSettled(
                    round.BlockId, round.GangId, round.CrewId, round.Carried,
                    round.Cursor, round.Missed, TerritoryRoundEnd.Lost, gameHour));
            }
        }
    }
}
