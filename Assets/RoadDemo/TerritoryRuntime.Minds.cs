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

        /// <summary>
        /// THE FAMILIES TAKE THEIR TURN. Every four game hours a house reads the street
        /// and files what it wants; at most three of its intents are executed, in order.
        /// </summary>
        void DriveHouseMinds(double gameHour)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null || racket == null || geography == null ||
                Commands == null)
                return;

            SweepWarnings(gameHour);
            underworld.Think(gameHour, mindConfig.ThinkEveryHours, house =>
            {
                var view = Look(house, gameHour);
                var tier = HouseMind.Think(
                    view, mindConfig, Relations?.Config, intents);
                Thinks++;

                var refused = Refusals(house.GangId);
                refused.Clear();

                // A think that only spent money, or found nothing at all, is a quiet
                // one. Three of them running are what tier 8 waits for (D22).
                house.NoteThink(tier > 0 && tier < HouseMind.TierInvest);

                var done = 0;
                for (var i = 0; i < intents.Count && done < mindConfig.MaxIntentsPerThink;
                     i++)
                {
                    var intent = intents[i];
                    var refusal = Carry(house, intent);
                    done++;
                    if (!string.IsNullOrEmpty(refusal))
                        refused.Add(intent + ": " + refusal);
                    DriveTrace.House(house.GangId, intent.Tier, intent.ToString(),
                        string.IsNullOrEmpty(refusal) ? intent.Reason : refusal,
                        house.Runner.Accounts.Safe, view.DailyPayroll);
                }

                if (done == 0)
                    DriveTrace.House(house.GangId, tier, "-", "no candidate",
                        house.Runner.Accounts.Safe, view.DailyPayroll);
            });
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
        /// Hangs the book off the street. The only wire between them is one question -
        /// "is anybody sitting on this door?" - and the answer is the guard lieutenant's
        /// own hand (D10 iii). Called once, with the rest of the runtime's wake-up.
        /// </summary>
        void InstallMinds()
        {
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
                if (other == null || other.Extinct || other.GangId == house.GangId)
                    continue;
                rivalScratch.Add(new TerritoryGangId(other.GangId));
            }
            return rivalScratch;
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

        // -------------------------------------------------------------------- the view

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
                    power.Collect(blockId, mine, gameHour, out _, out var unanswered);
                    if (unanswered > 0)
                        incidentScratch.Add(
                            new HouseIncident(blockId, unanswered, gameHour));
                }
                CollectDefiances(blockId, mine, gameHour);
            }

            return new HouseView
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
                LossesThisWar = 0,
                Incidents = incidentScratch,
                Threats = threatScratch,
                Defiances = defianceScratch,
                QuietThinks = house.QuietThinks,
                LastRefusals = Refusals(house.GangId),
                GameHour = gameHour,
                Day = (int)(gameHour / 24.0) + 1,
            };
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
                built.Add(new HouseDoor(
                    businessId, TierOf(businessId), rate, protector,
                    racket.StateOf(businessId, mine), owed,
                    !RacketCanAccrueAt(businessId, gameHour),
                    IsRacketable(businessId), tenure, late));
            }
            return built;
        }

        /// <summary>Doors on this block that told this family no and have not been
        /// answered - what the mind's threat and lean steps read.</summary>
        void CollectDefiances(
            TerritoryBlockId blockId, TerritoryGangId mine, double gameHour)
        {
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                // A door that has EVER refused us and does not pay us. The threat that
                // follows moves it off Defiant, and a man who has said no once is still a
                // man who has said no.
                if (!racket.TryGetRelationship(businessId, mine, out var row) ||
                    row.RefusedAt < 0.0 ||
                    row.State == TerritoryProtectionState.Compliant)
                    continue;
                defianceScratch.Add(
                    new HouseDefiance(businessId, blockId, row.RefusedAt, row.Threats));
            }
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
                    return Order(mine, intent);

                case HouseIntentKind.Job:
                    if (intent.Job == null)
                        return "no order";
                    intent.Job.GangId = house.GangId;
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
                    return Word(house, intent);
            }
            return "nothing to do";
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
            var paid = HouseOps.Purchase(house, intent.Price);
            if (!paid.Ok)
                return paid.Reason;

            var item = RosterOps.AddEquipment(
                house.Roster, intent.Kit, intent.Listing, intent.Price);
            if (item == null)
            {
                HouseOps.Refund(house, intent.Price);
                return "the dealer had nothing";
            }

            var given = RosterOps.GiveEquipment(
                house.Roster, item.Id, intent.CharacterId);
            house.Touch();
            return given.Ok ? "" : given.Reason;
        }

        /// <summary>
        /// A WORD TO ANOTHER FAMILY. It is printed in both books - theirs so they know,
        /// ours so the player can read what his own house said - and it starts a clock:
        /// a warning nobody answers is itself a grievance (D22).
        /// </summary>
        string Word(House house, HouseIntent intent)
        {
            var theirs = LivingCity.Outfit.Underworld.Current?.Of(intent.Other.Value);
            if (theirs == null)
                return "there is nobody to say it to";

            var said = LivingCity.Gangs.GangCatalog.Names[house.GangId] + " " +
                       intent.Listing +
                       (intent.Price > 0 ? " - $" + intent.Price : "");
            var day = house.Runner.Campaign.Day;
            var word = new LivingCity.Personnel.Incident(
                -1, said, LivingCity.Personnel.IncidentKind.AWordBetweenHouses, day, "",
                0, said);
            house.Runner.Incidents.Add(word);
            theirs.Runner.Incidents.Add(word);
            warnings[(house.GangId, intent.Other.Value)] = lastGameHour;
            return "";
        }

        /// <summary>When each house last warned each other house, so a warning that goes
        /// unanswered turns into a grudge after WarningHours (D22).</summary>
        readonly Dictionary<(int by, int at), double> warnings =
            new Dictionary<(int, int), double>();

        /// <summary>A word nobody answered. Swept on the business tick.</summary>
        void SweepWarnings(double gameHour)
        {
            if (Relations == null || warnings.Count == 0)
                return;
            var hours = Relations.Config.WarningHours;
            var stale = new List<(int by, int at)>();
            foreach (var pair in warnings)
                if (gameHour - pair.Value > hours)
                    stale.Add(pair.Key);
            for (var i = 0; i < stale.Count; i++)
            {
                warnings.Remove(stale[i]);
                Relations.Note(stale[i].by, stale[i].at,
                    LivingCity.Outfit.GrievanceKind.WarningIgnored);
            }
        }

        /// <summary>A territory order, built here and submitted through the gateway - the
        /// mutation boundary every house's orders cross.</summary>
        string Order(TerritoryGangId mine, HouseIntent intent)
        {
            var group = TerritoryCommandNodeId.Crew(intent.CrewId);
            TerritoryCommandResult result;
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

            return result.Status == TerritoryCommandStatus.Rejected
                ? (string.IsNullOrEmpty(result.Reason) ? "refused" : result.Reason)
                : "";
        }
    }
}
