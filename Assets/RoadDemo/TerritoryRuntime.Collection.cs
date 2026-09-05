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
        int lastAccruedDay = -1;

        public TerritoryDuesLedger Dues => dues;
        public TerritoryReputationLedger Reputation => reputation;

        /// <summary>The round's rules and its money, for every house. This file is the
        /// PHYSICAL clock over it: men march, DoorBeat counts the seconds at a counter,
        /// the bag comes home. Nothing here computes what a door pays.</summary>
        TerritoryRoundLedger roundLedger;

        /// <summary>The other clock - the same rounds walked by the hour with no bodies
        /// at all, for a house the city has not stood up.</summary>
        TerritoryPaperClock paperClock;

        TerritoryRoundScheduler roundScheduler;

        public TerritoryRoundLedger Rounds => roundLedger;
        public TerritoryPaperClock PaperClock => paperClock;

        readonly List<RoundBody> bodies = new List<RoundBody>();
        readonly List<TerritoryRoundStop> stopScratch = new List<TerritoryRoundStop>();

        /// <summary>GAME HOURS, not real seconds (AI-003 review finding C2): the
        /// harness clock and the user's Play must agree on how long a detail is given
        /// to clear its door. Twenty seconds at sixty seconds to the hour, as before.
        /// </summary>
        const float PendingBagRoundTimeoutHours = 20f / 60f;
        const float PendingBagRoundReassertHours = 1f / 60f;
        const float BagDefenceInterval = 0.25f;

        /// <summary>How far the men have to make on the ground for the round's own
        /// clock to count it as movement (AI-002 S2).</summary>
        const float RoundMoveMetres = 1.5f;

        /// <summary>Quiet seconds on the headquarters block before the detail files
        /// back inside.</summary>
        const float BagStandDownAfter = 20f;

        sealed class PendingBagRound
        {
            public CollectDuesCommand Command;
            public double DeadlineHour;
            public double ReassertAtHour;
            public int ScheduledDay = -1;

            /// <summary>
            /// EVERY HISTORY ROW WAITING ON THIS ONE ROUND. The gateway hands each
            /// submission its own receipt, and a second SEND while the detail is still
            /// in the doorway is answered Pending - so a retry used to leave a row that
            /// nothing would ever make terminal, and the reader could not tell whether
            /// his order had started or died. They all resolve together now.
            /// </summary>
            public readonly List<long> Receipts = new List<long>();
        }

        readonly List<PendingBagRound> pendingBagRounds =
            new List<PendingBagRound>();

        /// <summary>WHO IS SUBMITTING RIGHT NOW (AI-002, ruling A2). The gateway hands
        /// a command with no word of who built it, so the two doors that are not the
        /// player's - the schedule and a mind - set this around their submit, the way
        /// scheduledSubmitDay is set, and a round opened inside reads it. Player is
        /// the resting value: a key press is the only thing left.</summary>
        TerritoryRoundOrigin submittingOrigin = TerritoryRoundOrigin.Player;
        /// <summary>Last real HQ threat per bag detail. Only these timestamps buy the
        /// post-fight street grace; an idle detail with no billet goes straight home.</summary>
        readonly Dictionary<int, float> bagThreatSeenAt = new Dictionary<int, float>();
        int scheduledSubmitDay = -1;
        float nextBagDefenceAt;

        internal bool BagRoundPending(int crewId) => PendingBagRoundOf(crewId) != null;

        /// <summary>Whether autonomous HQ defence still owns this detail, including
        /// the quiet stand-down after its last threat. Roster sync asks this instead of
        /// inferring lifecycle state from TargetUnit, which is cleared as soon as the
        /// threat leaves.</summary>
        internal bool BagDefenceActive(int crewId) =>
            bagThreatSeenAt.TryGetValue(crewId, out var threatAt) &&
            Time.time - threatAt < BagStandDownAfter;

        PendingBagRound PendingBagRoundOf(int crewId)
        {
            for (var i = 0; i < pendingBagRounds.Count; i++)
                if (pendingBagRounds[i].Command.GroupId.Value == crewId)
                    return pendingBagRounds[i];
            return null;
        }

        /// <summary>Closes every receipt waiting on one deferred round.</summary>
        void ResolveBagRound(PendingBagRound pending, TerritoryCommandStatus status,
            string reason)
        {
            if (pending == null || Commands == null)
                return;
            for (var i = 0; i < pending.Receipts.Count; i++)
                Commands.Resolve(pending.Receipts[i], status, reason);
            pending.Receipts.Clear();
        }

        /// <summary>A scheduled detail walks back through the headquarters door before
        /// it receives its first route. The filed command waits here; the line and its
        /// independent billet are untouched.</summary>
        void TendPendingBagRounds()
        {
            if (crews == null || pendingBagRounds.Count == 0)
                return;
            for (var i = pendingBagRounds.Count - 1; i >= 0; i--)
            {
                var pending = pendingBagRounds[i];
                var command = pending.Command;
                var bag = crews.BagUnitOf(command.GroupId.Value);

                if (bag == null)
                {
                    pendingBagRounds.RemoveAt(i);
                    FailPendingBagRound(pending, "The bag detail could not come out.");
                    continue;
                }

                if (lastGameHour >= pending.DeadlineHour)
                {
                    pendingBagRounds.RemoveAt(i);
                    FailPendingBagRound(pending, "The bag detail could not clear the door.");
                    continue;
                }

                if (!CrewQuarters.AllOutside(bag))
                {
                    // Roster sync may have re-stationed a newly projected detail while
                    // its command was waiting. Keep the exit intent authoritative and
                    // bounded instead of leaving the crew pending forever.
                    if (lastGameHour >= pending.ReassertAtHour)
                    {
                        if (CrewQuarters.Billeted(bag))
                            CrewQuarters.BringOut(bag);
                        pending.ReassertAtHour = lastGameHour + PendingBagRoundReassertHours;
                    }
                    continue;
                }

                pendingBagRounds.RemoveAt(i);
                TerritoryCommandExecution result;
                var previousScheduledDay = scheduledSubmitDay;
                scheduledSubmitDay = pending.ScheduledDay;
                try
                {
                    result = Execute(command);
                }
                finally
                {
                    scheduledSubmitDay = previousScheduledDay;
                }

                if (RoundRunning(command.GroupId.Value))
                {
                    ResolveBagRound(pending, TerritoryCommandStatus.Succeeded,
                        "The round is walking.");
                    ConfirmScheduledBagRound(command, pending.ScheduledDay);
                    continue;
                }

                // A same-frame billet reappearing can legitimately queue the command
                // again. The receipts waiting on THIS attempt move to that one rather
                // than being closed under a round that is still trying to start.
                var requeued = PendingBagRoundOf(command.GroupId.Value);
                if (requeued != null)
                {
                    for (var r = 0; r < pending.Receipts.Count; r++)
                        if (!requeued.Receipts.Contains(pending.Receipts[r]))
                            requeued.Receipts.Add(pending.Receipts[r]);
                    continue;
                }
                var reason = !string.IsNullOrEmpty(result.Reason)
                    ? result.Reason
                    : "The bag detail could not start the round.";
                FailPendingBagRound(pending, reason);
            }
        }

        void FailPendingBagRound(PendingBagRound pending, string reason)
        {
            ResolveBagRound(pending, TerritoryCommandStatus.Failed, reason);
            CrewOverlay.Announce(reason.ToUpperInvariant(), 4f,
                new Color(1f, 0.55f, 0.45f));
        }

        void ConfirmScheduledBagRound(CollectDuesCommand command, int scheduledDay)
        {
            if (scheduledDay < 0 || roundScheduler == null)
                return;
            var house = LivingCity.Outfit.Underworld.Current?.Of(command.House.Value);
            var crew = house?.Roster?.FindCrew(command.GroupId.Value);
            if (house != null && crew != null)
                roundScheduler.Confirm(house, crew, command.BlockId, scheduledDay);
        }

        /// <summary>The autonomous bag detail answers a threat on the headquarters
        /// block, then returns inside after twenty quiet seconds. It never accepts a
        /// player unit order and never abandons a round to do this.</summary>
        void TendBagDefence()
        {
            if (crews == null)
                return;

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var bag = crews.Units[i];
                if (bag == null || bag.Faction < 0 || !bag.IsDetachment || bag.Wiped)
                    continue;
                if (TryGetRound(bag.CrewId, out _, out _, out _) ||
                    BagRoundPending(bag.CrewId))
                {
                    bagThreatSeenAt.Remove(bag.CrewId);
                    continue;
                }

                // EVERY HOUSE'S DETAIL DEFENDS ITS OWN DOORSTEP (AI-003, A9): the
                // player's at his headquarters, a family's at its front.
                var threat = ThreatAtHome(new TerritoryGangId(bag.Faction));

                if (threat != null)
                {
                    // Start the quiet clock at the last real threat. A detail that has
                    // merely lost its billet is idle, not standing down from a fight,
                    // and must go straight back inside instead of waiting in the street.
                    bagThreatSeenAt[bag.CrewId] = Time.time;
                    if (CrewQuarters.Billeted(bag))
                        CrewQuarters.CallOut(bag);
                    bag.TargetUnit = threat;
                    bag.ProvokedAt = Time.time;
                    continue;
                }

                bag.TargetUnit = null;
                if (CrewQuarters.Billeted(bag))
                {
                    bagThreatSeenAt.Remove(bag.CrewId);
                    continue;
                }
                if (BagDefenceActive(bag.CrewId))
                    continue;

                if (crews.StationBagAtHeadquarters(bag))
                    bagThreatSeenAt.Remove(bag.CrewId);
            }
        }

        /// <summary>
        /// Somebody a house's bag detail has to come out for: a crew of another house
        /// standing on the home block, or a fight already under way there between the
        /// house's own line and anybody - the law included. Both sides must be on the
        /// home block; the detail defends the doorstep and never chases a fight into
        /// the next street.
        /// </summary>
        DemoCrews.Unit ThreatAtHome(TerritoryGangId house)
        {
            var home = HomeDoor(house);
            if (home == Vector3.zero || !TryGetBlockAtWorld(home, out var homeBlock))
                return null;

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.Faction < 0 || unit.Faction == house.Value ||
                    unit.IsPolice || unit.Wiped || CrewQuarters.Inside(unit) ||
                    !TryGetBlockAtWorld(unit.Position, out var block) || block != homeBlock)
                    continue;
                // AND ONLY IF WE MAY FIGHT THEM AT ALL. Men of a house we are at peace
                // with may walk down our street; the detail comes out for them at war,
                // and on our own ground at truce - the three sentences the FAMILIES
                // card prints, and nothing else (Engagement.May). Without this the
                // detail every house now keeps started fights during peace with any
                // crew that crossed the block (Codex adversarial review, 2026-09-04).
                if (!LivingCity.Outfit.Engagement.May(
                        Relations != null
                            ? Relations.StanceBetween(house.Value, unit.Faction)
                            : LivingCity.Outfit.Stance.Peace,
                        oursIsTheGround: true, provoked: false))
                    continue;
                return unit;
            }

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var ours = crews.Units[i];
                var target = ours?.TargetUnit;
                if (ours == null || ours.Faction != house.Value || ours.IsDetachment ||
                    ours.Wiped || CrewQuarters.Inside(ours) ||
                    target == null || target.Wiped || CrewQuarters.Inside(target) ||
                    !TryGetBlockAtWorld(ours.Position, out var ourBlock) ||
                    ourBlock != homeBlock ||
                    !TryGetBlockAtWorld(target.Position, out var targetBlock) ||
                    targetBlock != homeBlock)
                    continue;
                return target;
            }
            return null;
        }

        /// <summary>The physical half of a round: the men walking it, the man who
        /// carries the bag, and where on the ground each of its stops actually is. Same
        /// order as the round's own stops - the ledger holds the doors as flat XZ points
        /// and the street needs a place to march to.</summary>
        sealed class RoundBody
        {
            public TerritoryRound Round;
            public CrewWalker Collector;
            public bool LeaveBagOnGround;
            public string FallenName = "";

            /// <summary>The men walking it (GAN-262): the crew's bag unit when it has a
            /// bag man, else the crew's own line. Every leg marches THIS unit; the
            /// crew stays where it stands while its bag man walks.</summary>
            public DemoCrews.Unit Walkers;
            public readonly List<Vector3> Doors = new List<Vector3>();

            /// <summary>Where the walkers last stood when the watchdog looked, and when
            /// the next re-march may go (AI-002 S2). Real ground made is what keeps a
            /// round's own clock moving between doors.</summary>
            public Vector3 LastAnchor;
            public bool AnchorKnown;
            public double NextRemarchAt;

            public Vector3 Door(int index) =>
                index >= 0 && index < Doors.Count ? Doors[index] : Vector3.zero;
        }

        /// <summary>One door on a walk, with the pavement spot outside it - what the
        /// planner orders before a round is opened.</summary>
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

        /// <summary>Stands the round machine up and hangs the street off its two
        /// callbacks. Called once, with the racket, from the runtime's own wake-up.
        /// </summary>
        void InstallRounds()
        {
            roundLedger = new TerritoryRoundLedger(racket, dues);
            roundLedger.Settled = OnStopSettled;
            roundLedger.Ended = OnRoundEnded;
            paperClock = new TerritoryPaperClock(roundLedger);
            roundScheduler = new TerritoryRoundScheduler
            {
                Owed = (gang, blockId) =>
                    TryGetCollectibleDues(blockId, gang, out var owed) ? owed : 0,
                StopsOwing = (gang, blockId) => StopsOwing(blockId, gang),
                Filed = OnRoundFiled,
            };
        }

        RoundBody BodyOf(TerritoryRound round)
        {
            for (var i = 0; i < bodies.Count; i++)
                if (bodies[i].Round == round)
                    return bodies[i];
            return null;
        }

        /// <summary>Opens a walk in the ledger and gives it a body on the street.
        /// <paramref name="walkers"/> is the unit that actually marches it - a crew's
        /// bag detachment on a collection, the whole line on a shakedown (GAN-262).
        /// </summary>
        TerritoryRound OpenRound(
            DemoCrews.Unit walkers, TerritoryGangId gang, TerritoryBlockId blockId,
            TerritoryRoundKind kind, List<RoundStop> ordered, CrewWalker collector)
        {
            stopScratch.Clear();
            for (var i = 0; i < ordered.Count; i++)
                stopScratch.Add(new TerritoryRoundStop(
                    ordered[i].BusinessId,
                    new TerritoryPoint(ordered[i].Door.x, ordered[i].Door.z)));

            var round = roundLedger.Open(
                gang, walkers.CrewId, collector != null ? collector.CharacterId : -1,
                blockId, kind, stopScratch, lastGameHour);
            if (round == null)
                return null;

            var body = new RoundBody
            {
                Round = round, Collector = collector, Walkers = walkers,
                // The opening march is the first leg; the watchdog re-issues it only
                // once a re-march interval has passed, not on its first look.
                NextRemarchAt = lastGameHour + mindConfig.RoundRemarchHours,
                // AND THE CLOCK STARTS WHERE THE MEN STAND (Codex adversarial review,
                // 2026-09-04). The watchdog used to take its first anchor on its first
                // look and reset LastMoveAt with it, so a round opened between two
                // looks was given that much longer before anybody called it stalled.
                LastAnchor = UnitAnchor(walkers),
                AnchorKnown = true,
            };
            for (var i = 0; i < ordered.Count; i++)
                body.Doors.Add(ordered[i].Door);
            bodies.Add(body);
            // THE JOB'S ROUTE IS SPENT. Whatever CrewJobs had these men marching to,
            // they are walking a round now; forgetting the stamp is what makes the job
            // re-issue its own march when the round ends, instead of sitting dispatched
            // for ever behind a walk that took its men (Codex adversarial review).
            CrewJobs.ForgetDispatch(walkers.CrewId);
            return round;
        }

        // ------------------------------------------------------------------ owners

        /// <summary>The man behind this counter, dealt once from the city seed
        /// (ECON-002) and remembered - hashing is cheap, but the same question a frame
        /// should not cost the same hash a frame.</summary>
        /// <summary>
        /// EVERY COUNTER IN THE CITY, ONE KIND OF MAN - for the forced scenarios and
        /// nothing else (EPIC 31 NIGHT-013). Null is the city as it deals itself, and
        /// that is the default: a scene that never sets it is the scene as it was.
        ///
        /// It is set before the city stands and read here, at the one place a profile
        /// is dealt, so nothing downstream has to know the difference.
        /// </summary>
        public static TerritoryOwnerTrait? OwnerTraitOverride { get; set; }

        public TerritoryOwnerProfile OwnerProfileOf(TerritoryBusinessId businessId)
        {
            if (!businessId.IsValid)
                return TerritoryOwnerProfile.Neutral;
            if (ownerProfiles.TryGetValue(businessId, out var profile))
                return profile;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            var seed = business != null && business.Populated ? business.CitySeed : 1987;
            var generation = business != null
                ? business.OwnerGenerationOf(businessId) : 0;
            profile = TerritoryOwnerProfile.Deal(
                seed, businessId, generation, OwnerTraitOverride);
            ownerProfiles[businessId] = profile;
            return profile;
        }

        /// <summary>A successor is a different man. Drop only his cached character;
        /// fear and racket standing remain keyed to the door and are untouched.</summary>
        public void ForgetOwnerProfile(TerritoryBusinessId businessId) =>
            ownerProfiles.Remove(businessId);

        /// <summary>The order gate's reading of a paying door. It uses the same dues
        /// and newest-slip inputs as the block file, so BEAT cannot disagree by surface.</summary>
        public bool DoorInGoodStanding(
            TerritoryBusinessId businessId, TerritoryGangId gangId)
        {
            if (racket == null || !businessId.IsValid || !gangId.IsValid)
                return false;
            TerritoryDoorDispatch? last = null;
            var slips = racket.Dispatches;
            for (var i = slips.Count - 1; i >= 0; i--)
                if (slips[i].BusinessId == businessId && slips[i].GangId == gangId)
                {
                    last = slips[i];
                    break;
                }

            TerritoryDuesAccount account = default;
            var hasDues = dues != null && dues.TryGet(businessId, out account) &&
                          account.GangId == gangId;
            return TerritoryDoorStandings.InGoodStanding(
                racket.StateOf(businessId, gangId), last, hasDues,
                hasDues ? dues.OwedOf(businessId, gangId) : 0,
                hasDues ? account.WeeklyRate : 0,
                hasDues ? account.LastCollectedDay : -1,
                hasDues ? account.MissedInARow : 0,
                (int)(lastGameHour / 24.0) + 1);
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

        // ------------------------------------------------------------------ GAN-245

        /// <summary>Game hours a door the police have been round stays out of the
        /// racket's reach. A collector who walks into a shop the afternoon a uniform
        /// stood in it gets the excuse and nothing else - which is what makes ringing
        /// the precinct worth a shopkeeper's while.</summary>
        public const double ProtectedHours = 8.0;

        /// <summary>The doors an officer has stood at, and the game hour each stops
        /// being able to say so. Small and short-lived on purpose: it is a window, not
        /// a state of the world.</summary>
        readonly Dictionary<TerritoryBusinessId, double> underTheLaw =
            new Dictionary<TerritoryBusinessId, double>();

        /// <summary>An officer took a statement here. For the next few hours this shop
        /// has an answer for anybody who puts a hand out.</summary>
        public void MarkUnderTheLaw(TerritoryBusinessId businessId)
        {
            if (businessId.IsValid)
                underTheLaw[businessId] = lastGameHour + ProtectedHours;
        }

        /// <summary>Whether that window is still open.</summary>
        public bool UnderTheLaw(TerritoryBusinessId businessId) =>
            businessId.IsValid && underTheLaw.TryGetValue(businessId, out var until) &&
            lastGameHour < until;

        /// <summary>
        /// WHETHER HE RINGS. The one door a lean reaches the police through: his own
        /// connections against his own fear, off the business's own stream mixed with
        /// the day and the incident - never UnityEngine.Random, so the same city on the
        /// same morning answers the same way twice.
        ///
        /// The call itself goes down StreetAlarm, which is the one channel; what
        /// happens next is the dispatcher's (PoliceDispatch).
        /// </summary>
        void MaybeRingThePrecinct(
            TerritoryGangId gangId, TerritoryBusinessId businessId)
            => RingAbout(gangId, businessId, LivingCity.Personnel.Deed.Extortion);

        /// <summary>Give the owner one telephone roll about a named deed. The caller
        /// chooses whether pavement witnesses could see it; the roll always reads the
        /// standing that exists at the instant this method is called.</summary>
        public bool RingAbout(
            TerritoryGangId gangId, TerritoryBusinessId businessId,
            LivingCity.Personnel.Deed deed, bool indoors = false)
        {
            if (!businessId.IsValid || !gangId.IsValid)
                return false;
            if (!TryGetBusinessApproach(businessId, out var door))
                return false;

            var owner = OwnerProfileOf(businessId);
            // THE FAMILY'S STANDING ON HIS STREET: what the block fears of it, or how
            // much of the block already pays it - whichever is the larger. A stranger
            // gets the telephone picked up on him; a house the street answers to does
            // not, and the man who has watched every other door pay needs no shot fired
            // to know which of the two he is looking at.
            var businessFear = 0f;
            var cap = 100f;
            var payingShare = 0f;
            if (geography != null &&
                geography.TryGetBusinessBlock(businessId, out var blockId))
            {
                if (fear != null)
                {
                    businessFear = fear.BusinessFear(blockId, businessId, gangId, lastGameHour);
                    cap = fear.Config.FearCap;
                }
                if (racket != null)
                {
                    BlockBusinesses(blockId);
                    payingShare = racket.ComplianceOf(blockBusinessScratch, gangId);
                }
            }

            var chance = LivingCity.Police.ComplaintRoll.Chance(
                owner.Connections,
                LivingCity.Police.ComplaintRoll.Standing(businessFear, cap, payingShare),
                owner.Trait == TerritoryOwnerTrait.Connected,
                owner.Trait == TerritoryOwnerTrait.Cowardly);

            var business = LivingCity.Business.BusinessRuntime.Instance;
            var citySeed = business != null && business.Populated ? business.CitySeed : 1987;
            var day = (int)(lastGameHour / 24.0);
            // A NUMBER THAT MOVES ON EVERY ATTEMPT, not on every complaint. The alarm's
            // own ComplaintNumber only goes up when a complaint actually RINGS
            // (StreetAlarm.Complain), so a roll that came back quiet handed the next
            // lean on that shop the very same sample - and since leaning raises fear and
            // lowers the chance, one quiet roll made the rest of the day
            // deterministically safe unless some unrelated shop rang and moved the
            // global counter. The count is per shop and per day, so one shop's leaning
            // cannot shift another's stream either.
            if (!LivingCity.Police.ComplaintRoll.Rings(chance,
                    LivingCity.Police.ComplaintRoll.StreamFor(
                        citySeed, businessId.Value, day,
                        NextComplaintTry(businessId.Value, day))))
                return false;

            var name = businessId.Value;
            if (TryGetBusinessView(businessId, out var view))
                name = view.BusinessName;
            StreetAlarm.Complain(
                door, gangId.Value, businessId.Value, name, lastGameHour, deed, indoors);
            return true;
        }

        /// <summary>How many times each shop has been leaned on today, and which day
        /// that is. Cleared when the day turns: the stream is keyed on the day already,
        /// so the count only has to be unique within one.</summary>
        readonly Dictionary<string, int> _complaintTries = new Dictionary<string, int>();
        int _complaintTriesDay = int.MinValue;

        /// <summary>The next attempt number for this shop today - 0, 1, 2 ... - which is
        /// what the complaint roll's stream is drawn on.</summary>
        int NextComplaintTry(string businessId, int day)
        {
            if (day != _complaintTriesDay)
            {
                _complaintTries.Clear();
                _complaintTriesDay = day;
            }
            _complaintTries.TryGetValue(businessId, out var tries);
            _complaintTries[businessId] = tries + 1;
            return tries;
        }

        /// <summary>What one shop feels about one family right now. The trial's Fear
        /// gate reads it (GAN-245) and so does anything else that has to ask what a
        /// month of men in the doorway did to the man behind the counter.</summary>
        public float BusinessFearOf(TerritoryBusinessId businessId, TerritoryGangId gangId)
        {
            if (fear == null || geography == null ||
                !geography.TryGetBusinessBlock(businessId, out var blockId))
                return 0f;
            return fear.BusinessFear(blockId, businessId, gangId, lastGameHour);
        }

        /// <summary>Police eyes on the block this door stands in, by the hour. The one
        /// door the STREET puts attention on a block through - an officer standing in a
        /// shop is the only thing on the map that does it without a shot fired.</summary>
        public void NotePoliceAttentionAt(TerritoryBusinessId businessId, float amount)
        {
            if (fear == null || geography == null || amount <= 0f ||
                !geography.TryGetBusinessBlock(businessId, out var blockId))
                return;
            fear.NotePoliceAttention(blockId, amount, lastGameHour);
        }

        /// <summary>
        /// Police eyes on a BLOCK, named directly. The flats put attention on the ground
        /// they stand on (EPIC 27) and have no business id to go through: a card room is
        /// not a shop and never appears in the business directory.
        /// </summary>
        public void AddPoliceAttention(TerritoryBlockId blockId, float amount)
        {
            if (fear == null || amount <= 0f || !blockId.IsValid)
                return;
            fear.NotePoliceAttention(blockId, amount, lastGameHour);
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

            var word = KeptOff(command.House, command.BlockId);
            if (word != null)
                return TerritoryCommandExecution.Reject(word);

            var unit = FindUnit(command.House, command.GroupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);
            // One round to a crew. The bag man's round outlives an order to the line
            // (AbandonRound), so a second SEND while he is out has to be refused here
            // rather than quietly stacked - in the seam's own words, so the key and
            // the order never disagree.
            if (RoundRunning(unit.CrewId))
                return TerritoryCommandExecution.Reject("a round is already out");
            var waiting = PendingBagRoundOf(unit.CrewId);
            if (waiting != null)
            {
                // The same errand, asked for twice. This receipt joins the one already
                // in the doorway rather than becoming a row nothing can close.
                if (command.CommandId > 0 && !waiting.Receipts.Contains(command.CommandId))
                    waiting.Receipts.Add(command.CommandId);
                return TerritoryCommandExecution.Pending(
                    "The bag detail is coming out of the house.");
            }

            // The stops: every shop on the block that pays THIS family and owes
            // anything. The order follows the street - nearest first from where the
            // men stand, then nearest from each door - never the id list.
            var gang = command.House;
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

            // NO LINE WALKS A COLLECTION, whose ever it is (AI-003, ruling A9): the
            // crew's bag detail comes out of its own front and owns the route, for
            // every house alike. A house whose crew has no man on the bag is refused
            // in the same words the player is, and its mind marks one (tier 4).
            {
                var roster = LivingCity.Outfit.Underworld.Current?
                    .Of(command.House.Value)?.Roster;
                var assignedId = LivingCity.Personnel.RosterOps.CollectorOf(
                    roster, unit.CrewId);
                var assigned = roster?.Find(assignedId);
                if (assigned == null ||
                    assigned.Status != LivingCity.Personnel.CharacterStatus.Active)
                    return TerritoryCommandExecution.Reject(
                        "The crew's collector is not available to walk the round.");
            }
            var walkers = crews.BagUnitOf(unit.CrewId);
            if (walkers == null)
                return TerritoryCommandExecution.Reject(
                    "The crew has no bag detail on the street.");
            var collector = CollectorOf(walkers);
            if (collector == null)
                return TerritoryCommandExecution.Reject(
                    "The crew has no hood who can carry the collection bag.");

            var ordered = new List<RoundStop>();
            OrderStops(candidates, UnitAnchor(walkers), ordered);

            // One errand at a time: the old doorstep order and any old round go.
            DropPendingApproaches(unit.CrewId);

            // The detail comes through the door on its feet. The filed round begins
            // only once the doorway beat has put every survivor back on the pavement.
            if (CrewQuarters.Billeted(walkers))
            {
                CrewQuarters.BringOut(walkers);
                var queued = new PendingBagRound
                {
                    Command = command,
                    DeadlineHour = lastGameHour + PendingBagRoundTimeoutHours,
                    ReassertAtHour = lastGameHour + PendingBagRoundReassertHours,
                    ScheduledDay = scheduledSubmitDay,
                };
                if (command.CommandId > 0) queued.Receipts.Add(command.CommandId);
                pendingBagRounds.Add(queued);
                return TerritoryCommandExecution.Pending(
                    "The bag detail is coming out of the house.");
            }

            // THE WALK IS TAKEN BEFORE THE ROUND IS OPENED. A crew that refuses to march
            // never had a round at all - opening one first would file a lost round for a
            // bag nobody ever picked up.
            if (!crews.MarchTo(walkers, ordered[0].Door))
                return TerritoryCommandExecution.Reject(
                    "The physical crew refused the round.");

            var round = OpenRound(
                walkers, gang, command.BlockId, TerritoryRoundKind.Collect, ordered,
                collector);
            if (round == null)
                return TerritoryCommandExecution.Reject(
                    "Nothing on that block owes us anything yet.");
            round.Origin = submittingOrigin;
            BumpRacketSeam();
            // The duffel is the collection job's equipment, not loot spawned by the
            // first shop. This exact hood carries it from departure until the round
            // banks, is abandoned, or he can no longer continue.
            BagCarry.Give(round.CrewId, collector);

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

            var roster = LivingCity.Outfit.Underworld.Current?
                .Of(unit.Faction)?.Roster;
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

        /// <summary>
        /// THE ROUNDS THAT GO OUT BY THEMSELVES, for every house - the pure scheduler's
        /// verdict, put through the command gateway.
        ///
        /// Which crew is due where and on what day is TerritoryRoundScheduler; this is
        /// only the scene edge that submits it and says so on the wire.
        /// </summary>
        void TendScheduledRounds(double gameHour)
        {
            if (crews == null || geography == null || Commands == null ||
                roundScheduler == null)
                return;

            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null)
                return;

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var day = outfit != null ? outfit.Campaign.Day : 1;
            var dayOfWeek = outfit != null
                ? outfit.Campaign.DayOfWeek
                : (day > 1 ? day - 1 : 0) % 7;
            var hourOfDay = (int)(gameHour - (int)(gameHour / 24.0) * 24.0);

            // EVERY house's paper, not only ours. A family's rounds go out on their own
            // days off their own lieutenants' blocks, and the money walks home to their
            // own front - the same schedule, the same refusals, the same wire.
            var previousScheduledDay = scheduledSubmitDay;
            scheduledSubmitDay = day;
            try
            {
                for (var g = 0; g < underworld.Count; g++)
                    roundScheduler.Tend(
                        underworld.Of(g), day, dayOfWeek, hourOfDay, roundLedger,
                        SubmitScheduledRound);
            }
            finally
            {
                scheduledSubmitDay = previousScheduledDay;
            }
        }

        /// <summary>The gateway is the mutation boundary and it records the command, so
        /// a standing round goes through it exactly as an ordered one does. Only a round
        /// that was TAKEN counts as sent: a crew in a fight or in a car is refused, and
        /// the next Business tick asks again the same day.</summary>
        bool SubmitScheduledRound(
            LivingCity.Outfit.House house, LivingCity.Personnel.Crew crew,
            TerritoryBlockId blockId)
        {
            // A STANDING ROUND NEEDS THE BAG MAN ON THE STREET (GAN-262), in his own
            // unit: a mark on the books with nobody standing under it sends nothing.
            // Every house deals its bag men out since AI-003 (A9); a house the city
            // never stood up has no unit at all and its round is the paper clock's.
            if (crews.BagUnitOf(crew.Id) == null &&
                Stands(new TerritoryGangId(house.GangId)))
                return false;

            var previousOrigin = submittingOrigin;
            submittingOrigin = TerritoryRoundOrigin.Schedule;
            try
            {
                Commands.Submit(new CollectDuesCommand(
                        TerritoryCommandNodeId.Crew(crew.Id), blockId)
                    { House = new TerritoryGangId(house.GangId) });
            }
            finally
            {
                submittingOrigin = previousOrigin;
            }
            // Pending can mean either "the route is walking" or only "the detail is
            // crossing the door". The physical ledger is the distinction. Scheduler
            // filing waits for the former; the deferred path confirms it on OpenRound.
            return RoundRunning(crew.Id);
        }

        /// <summary>
        /// A BOOK JOB TOOK THE CREW (AI-002, ruling A2). CrewJobs says so the moment it
        /// sends a crew on its first travel leg; every round of that crew the player
        /// did not start with a key is abandoned, the bag with it. A detachment's round
        /// is the bag man's own and an order to the line was never an order to him
        /// (GAN-262), so it is left alone here as it is everywhere.
        /// </summary>
        public void BookJobTookTheCrew(int crewId)
        {
            if (roundLedger == null)
                return;
            var walking = roundLedger.Rounds;
            for (var i = walking.Count - 1; i >= 0; i--)
            {
                var round = walking[i];
                if (round.CrewId != crewId || !round.Cancellable)
                    continue;
                var body = BodyOf(round);
                if (body != null && body.Walkers != null && body.Walkers.IsDetachment)
                    continue;
                roundLedger.Abandon(round, lastGameHour);
                roundScheduler?.Release(crewId, round.BlockId);
            }
        }

        /// <summary>One round on the street as the probe reads it (AI-000): the round
        /// itself, whether its bag man is still of the men walking it, where the men
        /// physically are, where they are walking to, and the metres between.</summary>
        public readonly struct RoundReading
        {
            public RoundReading(TerritoryRound round, int crewId, bool carrierWalks,
                bool walkersStand, Vector3 walkersAt, Vector3 walkingTo, float metres,
                bool billeted)
            {
                Round = round;
                CrewId = crewId;
                CarrierWalks = carrierWalks;
                WalkersStand = walkersStand;
                WalkersAt = walkersAt;
                WalkingTo = walkingTo;
                Metres = metres;
                Billeted = billeted;
            }

            public TerritoryRound Round { get; }
            public int CrewId { get; }
            public bool CarrierWalks { get; }
            public bool WalkersStand { get; }
            public Vector3 WalkersAt { get; }
            public Vector3 WalkingTo { get; }
            public float Metres { get; }
            public bool Billeted { get; }
        }

        /// <summary>Every round with a body on the street, described. Rounds on the
        /// paper clock have no body and are read off <see cref="Rounds"/> directly.
        /// </summary>
        public void DescribeRounds(List<RoundReading> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                var round = body.Round;
                var walkers = body.Walkers;
                var stand = walkers != null && !walkers.Wiped && crews != null &&
                            crews.Units.Contains(walkers);
                var at = stand ? UnitAnchor(walkers) : Vector3.zero;
                var to = round.Stage == TerritoryRoundStage.Walking && round.HasStop
                    ? body.Door(round.StopIndex)
                    : HomeDoor(round.House);
                var gap = stand ? Vector3.Distance(at, to) : -1f;
                into.Add(new RoundReading(
                    round, round.CrewId,
                    body.Collector != null && !body.Collector.Dead &&
                    Holds(walkers, body.Collector),
                    stand, at, to, gap,
                    walkers != null && CrewQuarters.Billeted(walkers)));
            }
        }

        /// <summary>A ROUND THAT GOES OUT BY ITSELF HAS TO SAY SO. It is the one thing in
        /// the racket the player did not order, and without a line on the wire he learns
        /// it happened only when the money arrives - or never, if it does not. The street
        /// gets a word too: his men just walked off.</summary>
        void OnRoundFiled(
            LivingCity.Outfit.House house, LivingCity.Personnel.Character lieutenant,
            TerritoryBlockId blockId, int owed, int stops)
        {
            var mine = new TerritoryGangId(house.GangId);
            racket?.FileRound(
                blockId, mine, TerritoryDoorNews.RoundOut, lastGameHour, owed, stops, 0);

            // Only OUR rounds are news on our wire; a family's own round going out is
            // their business, and the player learns of it by seeing it.
            if (!house.IsPlayer)
                return;
            CrewOverlay.Announce(
                (lieutenant != null ? lieutenant.Surname.ToUpperInvariant() + "'S" : "OUR") +
                " ROUND IS OUT ON " + BlockWord(blockId), 4f,
                new Color(0.85f, 0.9f, 1f));
        }

        /// <summary>How many of the block's doors owe us anything - what the round's own
        /// slip prints as its stop count.</summary>
        int StopsOwing(TerritoryBlockId blockId) =>
            StopsOwing(blockId, LivingCity.Gameplay.PlayerCommands.House);

        int StopsOwing(TerritoryBlockId blockId, TerritoryGangId gang)
        {
            if (geography == null || racket == null || dues == null)
                return 0;
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
        bool RoundRunning(int crewId) => roundLedger != null &&
                                         roundLedger.RoundRunning(crewId);

        /// <summary>Whether this man is one of the men of this unit right now. The bag
        /// can change hands between deals (the boss names another man), and the unit
        /// object is REUSED across deals - so a carrier who is merely alive is not
        /// proof that he is still the man walking this round.</summary>
        static bool Holds(DemoCrews.Unit unit, CrewWalker man)
        {
            if (unit == null || man == null)
                return false;
            if (unit.Boss == man)
                return true;
            for (var i = 0; i < unit.Hoods.Count; i++)
                if (unit.Hoods[i] == man)
                    return true;
            return false;
        }

        static CrewWalker EnsureCollector(RoundBody body, DemoCrews.Unit unit)
        {
            var collector = body.Collector;
            // DoorBeat temporarily hides the collector while he is inside a shop. That
            // is not a lost carrier and must never move the bag to a hood outside.
            // He must still be one of the men WALKING it, though: a man dealt back into
            // the crew's line mid-round is standing somewhere else entirely, and the
            // round would have gone on settling doors through him while another man
            // walked to them.
            if (collector != null && !collector.Dead && collector.Tf != null &&
                Holds(unit, collector))
                return collector;

            collector = CollectorOf(unit);
            body.Collector = collector;
            if (collector != null)
            {
                body.Round.CollectorId = collector.CharacterId;
                BagCarry.Give(body.Round.CrewId, collector);
            }
            return collector;
        }

        /// <summary>The round the street card marks, if this crew is walking one.</summary>
        public bool TryGetRound(
            int crewId, out int carried, out int stopsLeft, out Vector3 nextDoor)
        {
            carried = 0;
            stopsLeft = 0;
            nextDoor = default;
            for (var i = 0; i < bodies.Count; i++)
            {
                var round = bodies[i].Round;
                if (round.CrewId != crewId)
                    continue;
                var walking = round.Stage == TerritoryRoundStage.Walking;
                carried = round.Carried;
                stopsLeft = walking ? round.StopsLeft : 0;
                nextDoor = walking
                    ? bodies[i].Door(round.StopIndex)
                    : HomeDoor(round.House);
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
            // What the PLAYER's ledger surfaces read. A family's own dues are its
            // own business and are read through its own house.
            if (!dues.TryGet(businessId, out var account) ||
                account.GangId != LivingCity.Gameplay.PlayerCommands.House)
                return false;
            owed = account.Owed;
            lastPaidDay = account.LastCollectedDay;
            return true;
        }

        /// <summary>What the player's paying doors on a block can yield right now.
        /// Every order surface reads this so collection stays closed until the first
        /// daily dues tick has actually put money on the ledger.</summary>
        public bool TryGetCollectibleDues(TerritoryBlockId blockId, out int owed) =>
            TryGetCollectibleDues(blockId, LivingCity.Gameplay.PlayerCommands.House,
                out owed);

        /// <summary>The same, for whichever house is asking - what ITS paying doors on
        /// a block can yield right now.</summary>
        public bool TryGetCollectibleDues(
            TerritoryBlockId blockId, TerritoryGangId gang, out int owed)
        {
            owed = 0;
            if (!blockId.IsValid || geography == null || racket == null)
                return false;

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
            // Any house's round, arriving at any house's door. The round names its own
            // crew, and crew numbers are unique across all twenty-one books.
            if (bodies.Count == 0 || actor?.Tf == null || unit.Faction < 0)
                return;

            for (var i = bodies.Count - 1; i >= 0; i--)
            {
                var body = bodies[i];
                var round = body.Round;
                if (round.CrewId != unit.CrewId)
                    continue;

                // Only the hood who visibly owns the collection bag settles this
                // round. If he is lost, ownership visibly transfers to one survivor -
                // of the men WALKING it (the bag unit, or the line), never of a line
                // standing on its block while its bag man is out.
                var collector = EnsureCollector(body, body.Walkers ?? unit);
                if (collector == null || actor != collector)
                    return;

                if (round.Stage == TerritoryRoundStage.Walking)
                {
                    // He is already through this door; the sampling pass runs on its own
                    // cadence and would otherwise open the same stop again every tick of
                    // the conversation.
                    if (round.InTheDoor || !round.HasStop)
                        return;

                    var door = body.Door(round.StopIndex);
                    if ((actor.Tf.position - door).sqrMagnitude >
                        approachRadiusMetres * approachRadiusMetres)
                        return;

                    // THE HAND GOES OUT AT THE COUNTER. The money used to be settled and
                    // called over the street the instant the men came within reach of the
                    // door - the visit that followed was a mime of a stop that had
                    // already happened. He goes in, the shop pays him inside, and the
                    // round only moves on when he is back on the pavement with the bag.
                    if (!roundLedger.Arrive(round, gameHour))
                        return;
                    var walking = round;
                    var here = round.Stop;
                    var who = unit;
                    var seen = observation;
                    DoorBeat.VisitBusiness(
                        actor, here.BusinessId, door,
                        whenInside: () => SettleDoor(
                            walking, here, who, seen, lastGameHour),
                        whenOut: () => NextStop(walking, who));
                }
                else
                {
                    var home = HomeDoor(round.House);
                    if ((actor.Tf.position - home).sqrMagnitude > HomeRadius * HomeRadius)
                        return;
                    BankRound(round, gameHour);
                }

                return;
            }
        }

        const float HomeRadius = 18f;

        /// <summary>Where a house's round walks the bag to: that family's own door.
        /// The player's is his headquarters, which the outfit director already
        /// answers; everybody else's is the front the city seated them.</summary>
        Vector3 HomeDoor(TerritoryGangId house)
        {
            if (house == LivingCity.Gameplay.PlayerCommands.House)
            {
                var director = LivingCity.Gameplay.OutfitDirector.Instance;
                if (director != null && director.TryGetHeadquarters(out var hq, out _))
                    return hq;
            }

            var front = house.IsValid ? DemoCrews.FrontOf(house.Value) : null;
            return front != null ? front.Outside : Vector3.zero;
        }

        bool HasHome(TerritoryGangId house) =>
            HomeDoor(house) != Vector3.zero;

        /// <summary>
        /// The hand goes out (ECON-003/005/007). The owner pays, pays part with a
        /// story, or does not pay; the crew's policy and the lieutenant's own hand say
        /// what actually changes pockets, what fear the stop leaves and what heat it
        /// draws; and two misses running let the arrangement lapse.
        /// </summary>
        void SettleStop(
            TerritoryRound round, TerritoryRoundStop stop, DemoCrews.Unit unit,
            TerritoryActorObservation observation, double gameHour)
        {
            PolicyAndArchetype(unit, out var policyLevel, out var archetype);

            // Who is at this counter, for the callback that files what the stop left
            // behind. A paper round leaves it empty and the same callback skips the
            // things only a body can do.
            standingAtTheDoor = observation;
            try
            {
                roundLedger.Settle(
                    round, StopInputs(round, stop, policyLevel, archetype, gameHour),
                    gameHour);
            }
            finally
            {
                standingAtTheDoor = default;
            }
        }

        /// <summary>Who is physically at the counter right now, or nobody. Read by
        /// <see cref="OnStopSettled"/>, which is called by both clocks.</summary>
        TerritoryActorObservation standingAtTheDoor;

        /// <summary>What the world says about one door at one moment: whether it is
        /// open, what it owes, who is behind the counter, what the block feels, how the
        /// crew asks - and whether the police have been round (GAN-245). The ledger
        /// decides everything from these and nothing else, so a door under the law's
        /// eye pays nobody on either clock.
        /// </summary>
        public TerritoryStopInputs StopInputs(
            TerritoryRound round, TerritoryRoundStop stop, int policyLevel,
            int archetype, double gameHour)
        {
            var businessId = stop.BusinessId;
            geography.TryGetBusinessBlock(businessId, out var blockId);
            var business = LivingCity.Business.BusinessRuntime.Instance;
            return new TerritoryStopInputs(
                RacketCanAccrueAt(businessId, gameHour),
                dues.OwedOf(businessId, round.House),
                OwnerProfileOf(businessId),
                fear != null ? fear.FearOf(blockId, round.House, gameHour) : 0f,
                fear != null ? fear.BlockFear(blockId, gameHour) : 0f,
                policyLevel, archetype,
                business != null && business.Populated ? business.CitySeed : 1987,
                (int)(gameHour / 24.0),
                UnderTheLaw(businessId));
        }

        /// <summary>The crew's policy and its lieutenant's trade, for whichever house's
        /// crew this is.</summary>
        void PolicyAndArchetypeOf(
            TerritoryGangId house, int crewId, out int policyLevel, out int archetype)
        {
            policyLevel = (int)LivingCity.Personnel.CrewPolicy.Normal;
            archetype = (int)LivingCity.Personnel.LieutenantArchetype.Soldier;
            var roster = LivingCity.Outfit.Underworld.Current?.Of(house.Value)?.Roster;
            if (roster == null)
                return;
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                if (roster.Crews[i].Id != crewId)
                    continue;
                policyLevel = (int)roster.Crews[i].Policy;
                archetype = (int)LivingCity.Personnel.LieutenantArchetypes.Of(
                    roster.Find(roster.Crews[i].LieutenantId));
                return;
            }
        }

        /// <summary>
        /// WHAT A SETTLED DOOR LEAVES BEHIND. The money is already decided - the ledger
        /// did that and this cannot change it. What is left is the world's business: the
        /// fear on the block, the heat on the meter, the name the man made, the practice
        /// he banked, and the word over the street.
        ///
        /// Both clocks come through here, so a paper round and a walked round leave the
        /// same marks on the city.
        /// </summary>
        void OnStopSettled(
            TerritoryRound round, TerritoryRoundStop stop,
            TerritoryStopSettlement settlement)
        {
            var businessId = stop.BusinessId;
            var mouth = standingAtTheDoor.CharacterId;

            if (!settlement.Settled)
            {
                // The shutters are down. Only a man standing in front of them says so.
                if (mouth.IsValid && round.House == LivingCity.Gameplay.PlayerCommands.House)
                {
                    var shut = businessId.Value;
                    if (TryGetBusinessView(businessId, out var closed))
                        shut = closed.BusinessName;
                    CrewOverlay.Announce(
                        shut.ToUpperInvariant() + " IS CLOSED - NOTHING TO COLLECT", 3f,
                        new Color(1f, 0.75f, 0.45f));
                }
                return;
            }

            // A door that paid in full files nothing, so the racket's version does not
            // move - but what it owes just changed, and the block file reads that.
            BumpRacketSeam();
            geography.TryGetBusinessBlock(businessId, out var blockId);
            if (settlement.Lapsed)
                PublishRacket(blockId);

            // What the stop leaves behind: the policy's fear and heat, the lieutenant's
            // own hand on both, and the tier's heat on the money itself.
            if (settlement.FearLeft > 0.01f)
            {
                RecordResolvedThreat(round.House, businessId, settlement.FearLeft,
                    TerritoryFearVisibility.Seen, mouth);
                NoteConnectedHeat(businessId);
            }
            if (settlement.Heat > 0f && fear != null && blockId.IsValid)
                fear.NotePoliceAttention(blockId, settlement.Heat, round.LastMoveAt);

            if (settlement.Paid > 0)
                NoteReputationAt(businessId, mouth,
                    2f + Mathf.Min(6f, settlement.Paid / 150f));

            // XP-003. The man who actually stood at this door banks the practice for
            // it, the same table the ordered shakedown banks through - one lesson a
            // day, so a long round does not turn into a training ground.
            if (mouth.IsValid)
                CrewSkill.Collected(mouth.Value, settlement.Paid > 0);

            // What he came out with (or didn't), said over the door - and only for our
            // own men. A family's round is theirs; the player learns of it by seeing it.
            if (mouth.IsValid && round.House == LivingCity.Gameplay.PlayerCommands.House)
                AnnounceStop(businessId, settlement);
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
        void NextStop(TerritoryRound round, DemoCrews.Unit unit)
        {
            var body = BodyOf(round);
            if (body == null)
                return;

            // Every leg is marched by the men WALKING it (GAN-262) - the bag unit when
            // the crew has one out, the line when it does not.
            unit = body.Walkers ?? unit;

            // A SHAKEDOWN HAS NOTHING TO CARRY HOME. Only a collection walks to the
            // front: the men who have just been down a block asking for money stay on
            // the block they asked on. Marching them across the city would take the
            // presence off the ground the asking was for, and Bank would file a round
            // slip for a bag that was never picked up. The ledger knows that rule.
            if (roundLedger.Advance(round, lastGameHour))
            {
                if (unit != null && !unit.Wiped)
                    crews.MarchTo(unit, body.Door(round.StopIndex));
                return;
            }

            if (round.Stage != TerritoryRoundStage.HeadingHome)
                return;

            if (HasHome(round.House))
            {
                if (unit != null && !unit.Wiped)
                    crews.MarchTo(unit, HomeDoor(round.House));
                return;
            }

            // A scene with no front to walk to banks on the spot - the bench rigs have
            // no city and no home, and a round that can never finish is worse than one
            // that skips the walk there.
            BankRound(round, lastGameHour);
        }

        void AnnounceStop(
            TerritoryBusinessId businessId, TerritoryStopSettlement settlement)
        {
            var name = businessId.Value;
            if (TryGetBusinessView(businessId, out var view))
                name = view.BusinessName;
            name = name.ToUpperInvariant();

            switch (settlement.Outcome)
            {
                case TerritoryPaymentOutcome.Paid:
                    CrewOverlay.Announce(
                        "$" + settlement.Paid + " COLLECTED AT " + name, 3f,
                        new Color(0.75f, 0.95f, 0.7f));
                    break;
                case TerritoryPaymentOutcome.Short:
                    CrewOverlay.Announce(
                        name + " CAME UP SHORT — $" + settlement.Paid + " OF $" +
                        settlement.Owed + " · " + ExcuseWord(settlement.Excuse), 4f,
                        new Color(1f, 0.85f, 0.55f));
                    break;
                default:
                    CrewOverlay.Announce(
                        name + " DID NOT PAY · " + ExcuseWord(settlement.Excuse), 4f,
                        new Color(1f, 0.6f, 0.45f));
                    break;
            }
        }

        /// <summary>The story, as the owner tells it - the wire's own words, so the
        /// toast over the street and the slip in the book cannot differ about what the
        /// man said (TerritoryStandingVocabulary.ExcuseWord).</summary>
        static string ExcuseWord(TerritoryPaymentExcuse excuse) =>
            TerritoryStandingVocabulary.ExcuseWord(excuse);

        void PolicyAndArchetype(
            DemoCrews.Unit unit, out int policyLevel, out int archetype) =>
            PolicyAndArchetypeOf(
                new TerritoryGangId(unit.Faction), unit.CrewId, out policyLevel,
                out archetype);

        /// <summary>THE OTHER CLOCK, ticked. A round nobody is standing up is walked by
        /// the hour instead: the same ledger, the same doors, the same money. The take
        /// reaches the safe through OnRoundEnded, exactly as a walked round's does.
        /// </summary>
        void TickPaperRounds(double gameHour)
        {
            if (paperClock == null || paperClock.Walking == 0)
                return;
            paperClock.Tick(gameHour, AskPaperStop, null);
        }

        TerritoryStopInputs AskPaperStop(TerritoryRound round, TerritoryRoundStop stop)
        {
            PolicyAndArchetypeOf(
                round.House, round.CrewId, out var policyLevel, out var archetype);
            return StopInputs(round, stop, policyLevel, archetype, lastGameHour);
        }

        /// <summary>The take reaches the front. The ledger files the slip and answers
        /// what came home; the money itself moves in <see cref="OnRoundEnded"/>, which
        /// is the ONLY place round money becomes a house's money.</summary>
        void BankRound(TerritoryRound round, double gameHour) =>
            roundLedger.Bank(round, gameHour);

        /// <summary>
        /// A ROUND IS OVER, banked or lost, on whichever clock walked it. The body goes,
        /// the bag goes, the money moves and the wire hears it.
        /// </summary>
        void OnRoundEnded(TerritoryRound round)
        {
            var body = BodyOf(round);
            if (body != null)
                bodies.Remove(body);
            paperClock?.Forget(round);
            BumpRacketSeam();

            // A shakedown and a lean carry nothing: the walk IS the errand, and there is
            // no bag, no banking and no slip.
            if (round.Kind != TerritoryRoundKind.Collect)
                return;

            var banked = round.Stage == TerritoryRoundStage.Banked;
            var ours = round.House == LivingCity.Gameplay.PlayerCommands.House;
            // Where it lies if the carrier had no bag on him to drop: the collector's
            // own last position, else the door he was walking to. A take with nowhere
            // to fall is a take that vanishes (BagCarry.Drop).
            Vector3? fellAt = null;
            if (body != null)
            {
                if (body.Collector != null && body.Collector.Tf != null)
                    fellAt = body.Collector.Tf.position;
                else if (body.Doors.Count > 0)
                    fellAt = body.Door(round.StopIndex);
            }
            BagCarry.Drop(round.CrewId, banked, round.Carried, round.House.Value,
                body != null ? body.FallenName : "",
                body != null && body.LeaveBagOnGround, crews, fellAt);

            if (banked)
            {
                // THE ONLY PLACE ROUND MONEY BECOMES A HOUSE'S MONEY - and it is the
                // house whose round it was, not ours.
                var house = LivingCity.Outfit.Underworld.Current?.Of(round.House.Value);
                if (round.Carried > 0 && house != null)
                {
                    // Ours goes through the director, which prints the line on the wire
                    // and moves the ledger's dirty key; theirs goes onto their books.
                    var director = LivingCity.Gameplay.OutfitDirector.Instance;
                    if (house.IsPlayer && director != null)
                        director.BankCollection(round.Carried);
                    else
                        house.Runner.BankCollection(round.Carried);
                    house.Touch();
                }

                if (ours)
                    NoteRoundBanked(round.BlockId, round.Carried, round.Missed,
                        LivingCity.Gameplay.OutfitDirector.Instance != null
                            ? LivingCity.Gameplay.OutfitDirector.Instance.Campaign.Day
                            : 1);

                if (body?.Walkers != null && body.Walkers.IsDetachment &&
                    !body.Walkers.Wiped)
                    crews.StationBagAtHeadquarters(body.Walkers);
            }
            else if (round.Carried > 0)
            {
                CountToday(round.House.Value, "lost");
                // A BAG TAKEN OFF OUR MEN IS OWED FOR (D14) - if anybody was seen. The
                // most recent house to put hands on somebody on that street is who the
                // family blames, which is what a family would do.
                var took = LastThreatOn(round.BlockId, round.House);
                if (took.IsValid)
                    LivingCity.Outfit.Underworld.Current?.Relations.Note(
                        round.House.Value, took.Value,
                        LivingCity.Outfit.GrievanceKind.RoundLost);
            }

            if (!banked && round.Carried > 0 && ours)
            {
                // The loudest money event on the wire was the quietest one on the
                // street: every ordinary stop calls itself over the door and a bag
                // going missing said nothing at all.
                var line = body != null && body.LeaveBagOnGround
                    ? (string.IsNullOrEmpty(body.FallenName)
                        ? "OUR COLLECTOR" : body.FallenName.ToUpperInvariant()) +
                      " FELL ON THE ROUND · $" + round.Carried + " LIES ON " +
                      BlockWord(round.BlockId)
                    : "THE BAG IS GONE · $" + round.Carried +
                      " OFF " + BlockWord(round.BlockId);
                CrewOverlay.Announce(line, 4f, new Color(1f, 0.55f, 0.45f));
            }

            events.Publish(new CollectionRoundSettled(
                round.BlockId, round.House, round.CrewId, round.Carried,
                banked ? round.Stops.Count : round.StopIndex, round.Missed,
                banked ? TerritoryRoundEnd.Banked : TerritoryRoundEnd.Lost,
                lastGameHour));
        }

        /// <summary>A crew retasked mid-round walked away from its own route; whatever
        /// it was carrying never reaches the books. An order countermanded is an order
        /// countermanded.
        ///
        /// An order to the CREW is not an order to its bag man (GAN-262), though: the
        /// line can be sent anywhere while a detachment finishes its doors.</summary>
        void AbandonRound(int crewId)
        {
            if (roundLedger == null)
                return;
            var walking = roundLedger.Rounds;
            for (var i = walking.Count - 1; i >= 0; i--)
            {
                var round = walking[i];
                if (round.CrewId != crewId)
                    continue;
                var body = BodyOf(round);
                if (body != null && body.Walkers != null && body.Walkers.IsDetachment)
                    continue;
                roundLedger.Abandon(round, lastGameHour);
            }
        }

        /// <summary>The risk that makes a route a route (ECON-004): a round whose crew
        /// is scattered or wiped loses its take on the street where it fell.</summary>
        void WatchRounds(double gameHour)
        {
            for (var i = bodies.Count - 1; i >= 0; i--)
            {
                var body = bodies[i];
                var round = body.Round;
                var walkers = body.Walkers;
                var standing = walkers != null && !walkers.Wiped &&
                               crews != null && crews.Units.Contains(walkers);

                // THE BAG FALLS WITH THE MAN. A carrier who is dead loses what he was
                // carrying where he fell - that is the risk the whole walk is built on
                // (ECON-004), and handing his take to men standing streets away would
                // be money teleporting off a corpse. Only a bag unit dealt away under a
                // LIVING carrier - the man taken off the bag, or moved to another crew -
                // hands the round on to the crew's new bag man, else to the line.
                var carrier = body.Collector;
                var carrierDown = carrier == null || carrier.Dead || carrier.Tf == null;
                if (carrierDown && standing)
                {
                    body.Collector = null;
                    if (EnsureCollector(body, walkers) != null)
                        continue;
                }
                if (!standing && !carrierDown)
                {
                    var next = crews.BagUnitOf(round.CrewId)
                               ?? FindUnit(TerritoryCommandNodeId.Crew(round.CrewId));
                    if (next != null && !next.Wiped && next != walkers)
                    {
                        body.Walkers = next;
                        body.Collector = null;
                        EnsureCollector(body, next);
                        crews.MarchTo(next,
                            round.Stage == TerritoryRoundStage.HeadingHome ||
                            !round.HasStop
                                ? HomeDoor(round.House)
                                : body.Door(round.StopIndex));
                        continue;
                    }
                }

                if (standing)
                {
                    WatchTheWalk(body, walkers, gameHour);
                    continue;
                }
                if (carrierDown && round.Carried > 0)
                {
                    body.LeaveBagOnGround = true;
                    body.FallenName = carrier != null ? carrier.DisplayName : "our collector";
                }
                roundLedger.Abandon(round, gameHour);
            }
        }

        /// <summary>
        /// THE WATCHDOG AND THE RE-MARCH (AI-002 S2, ruling A3). The measured rounds
        /// died on the way: NextStop marched the walkers once per leg, and a walk that
        /// failed - pathing, a fight, the police, a retask - was never repeated,
        /// measured or ended, while the scheduler refused to send the block's next
        /// collection because a round was "running".
        ///
        /// Ground the men make keeps the round's own clock moving. A leg whose walkers
        /// are alive, standing and not yet at the door is marched again every
        /// RoundRemarchHours. A round that has not moved at all for RoundStallHours is
        /// abandoned, the block goes back on the schedule, and the ledger says a round
        /// was lost if the bag had anything in it. A man inside a shop is DoorBeat's
        /// business and is left alone.
        /// </summary>
        void WatchTheWalk(RoundBody body, DemoCrews.Unit walkers, double gameHour)
        {
            var round = body.Round;
            if (round.Finished || round.InTheDoor)
                return;

            var anchor = UnitAnchor(walkers);
            if (!body.AnchorKnown ||
                (anchor - body.LastAnchor).sqrMagnitude > RoundMoveMetres * RoundMoveMetres)
            {
                body.AnchorKnown = true;
                body.LastAnchor = anchor;
                round.LastMoveAt = gameHour;
            }

            if (gameHour - round.LastMoveAt > mindConfig.RoundStallHours)
            {
                roundLedger.Abandon(round, gameHour);
                roundScheduler?.Release(round.CrewId, round.BlockId);
                return;
            }

            if (gameHour < body.NextRemarchAt)
                return;
            body.NextRemarchAt = gameHour + mindConfig.RoundRemarchHours;

            var to = round.Stage == TerritoryRoundStage.HeadingHome
                ? HomeDoor(round.House)
                : round.HasStop ? body.Door(round.StopIndex) : Vector3.zero;
            if (to == Vector3.zero)
                return;
            var reach = round.Stage == TerritoryRoundStage.HeadingHome
                ? HomeRadius
                : approachRadiusMetres;
            if ((anchor - to).sqrMagnitude <= reach * reach)
                return;

            // Do not re-open the stop; only re-issue the walk. A refused march is a
            // stalled round the watchdog above ends in its own time.
            crews.MarchTo(walkers, to);
        }
    }
}
