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

        sealed class CollectionRound
        {
            public int CrewId;
            public TerritoryGangId GangId;
            public TerritoryBlockId BlockId;
            public CrewWalker Collector;
            public readonly List<RoundStop> Stops = new List<RoundStop>();
            public int Cursor;
            public int Carried;
            public int Missed;
            public RoundStage Stage;
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
                        dues.AccrueDay(businessId, protector, rate);
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

        static CrewWalker CollectorOf(DemoCrews.Unit unit)
        {
            if (unit == null)
                return null;
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
                    var stop = round.Stops[round.Cursor];
                    if ((actor.Tf.position - stop.Door).sqrMagnitude >
                        approachRadiusMetres * approachRadiusMetres)
                        return;
                    SettleStop(round, stop, unit, actor, observation, gameHour);
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
            CrewWalker actor, TerritoryActorObservation observation, double gameHour)
        {
            var businessId = stop.BusinessId;
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

            var runs = dues.Settle(businessId, round.GangId, day, paid, missed);
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

            // The world's side of the stop: the man steps inside, and what he came out
            // with (or didn't) is said over the door.
            DoorBeat.VisitBusiness(actor, businessId, stop.Door);
            AnnounceStop(businessId, result, paid);

            round.Cursor++;
            if (round.Cursor < round.Stops.Count)
            {
                crews.MarchTo(unit, round.Stops[round.Cursor].Door);
                return;
            }

            round.Stage = RoundStage.HeadingHome;
            if (HasHome())
                crews.MarchTo(unit, HomeDoor());
            else
                // A scene with no front to walk to banks on the spot - the bench rigs
                // have no city and no home, and a round that can never finish is worse
                // than one that skips the walk there.
                Bank(round, gameHour);
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

        /// <summary>The story, as the owner tells it. Whether it is true is never
        /// printed - a crew that knows its street knows.</summary>
        static string ExcuseWord(TerritoryPaymentExcuse excuse)
        {
            switch (excuse)
            {
                case TerritoryPaymentExcuse.BadWeek: return "\"A BAD WEEK\"";
                case TerritoryPaymentExcuse.WasRobbed: return "\"WE WERE ROBBED\"";
                case TerritoryPaymentExcuse.PoliceWereRound: return "\"THE POLICE WERE ROUND\"";
                default: return "";
            }
        }

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
            BagCarry.Drop(round.CrewId, banked: true);
            var director = LivingCity.Gameplay.OutfitDirector.Instance;
            if (round.Carried > 0 && director != null)
                director.BankCollection(round.Carried);

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
                BagCarry.Drop(round.CrewId, banked: false);
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
