using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

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

            underworld.Think(gameHour, mindConfig.ThinkEveryHours, house =>
            {
                var view = Look(house, gameHour);
                var tier = HouseMind.Think(view, mindConfig, intents);
                Thinks++;

                var refused = Refusals(house.GangId);
                refused.Clear();

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
                StanceLook = other => LivingCity.Outfit.Stance.Peace,
                Incidents = incidentScratch,
                Defiances = defianceScratch,
                LastRefusals = Refusals(house.GangId),
                GameHour = gameHour,
                Day = (int)(gameHour / 24.0) + 1,
            };
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

                built.Add(new HouseDoor(
                    businessId, TierOf(businessId), WeeklyRateOf(businessId), protector,
                    racket.StateOf(businessId, mine), dues.OwedOf(businessId, mine),
                    !RacketCanAccrueAt(businessId, gameHour),
                    IsRacketable(businessId), tenure));
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
            }
            return "nothing to do";
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
