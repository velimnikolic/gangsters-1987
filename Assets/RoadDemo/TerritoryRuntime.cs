using System;
using System.Collections.Generic;
using LivingCity.Ambient;
using LivingCity.Gameplay;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Scene adapter for the Phase-1 territory foundation. Core's immutable plan supplies
    /// identity, DemoCrews supplies physical truth, Personnel supplies organization truth,
    /// and this component supplies one command/query/event/scheduler boundary between them.
    /// </summary>
    public sealed class TerritoryRuntime : MonoBehaviour,
        ITerritoryCommandExecutor,
        ITerritoryActorSource,
        ITerritoryResponsibilityNameSource
    {
        [Header("Territory cadence (game time)")]
        [SerializeField, Min(0.05f)] float physicalPresenceMinutes = 1f;
        [SerializeField, Min(0.01f)] float residualPresenceHours = 0.25f;
        [SerializeField, Min(0.01f)] float fearHours = 1f;
        [SerializeField, Min(0.01f)] float businessHours = 4f;
        [SerializeField, Min(0.01f)] float controlHours = 0.25f;

        public static TerritoryRuntime Instance { get; private set; }

        RoadDemoBuilder builder;
        DemoCrews crews;
        TerritorySimulationState state;
        TerritoryGeography geography;
        TerritoryTruthQuery truth;
        TerritoryPlayerQuery player;
        TerritorySimulationScheduler scheduler;
        TerritoryEventStream events;
        TerritoryCommandGateway commands;
        double debugTimeOffset;
        bool organizationBlocksRegistered;

        Dictionary<ActorKey, ActorLocation> actorLocations =
            new Dictionary<ActorKey, ActorLocation>();
        Dictionary<ActorKey, ActorLocation> sampledLocations =
            new Dictionary<ActorKey, ActorLocation>();
        readonly HashSet<TerritoryBlockId> occupiedBlocks = new HashSet<TerritoryBlockId>();

        public ITerritoryTruthQuery DebugTruth => truth;

        /// <summary>The canonical geography: blocks, neighborhoods, the block neighbor
        /// graph, road-space resolution and business membership. Every consumer that used
        /// to reach into CoreTerritoryPlan - the maps, the ledger, the debug overlays -
        /// asks this instead, so one physical block has one canonical id everywhere.</summary>
        public ITerritoryGeography Geography => geography;
        public ITerritoryPlayerQuery PlayerQuery => player;
        public TerritoryEventStream Events => events;
        public TerritoryCommandGateway Commands => commands;
        public TerritorySimulationScheduler Scheduler => scheduler;
        public int StateVersion => state?.Version ?? 0;
        public int ObservationVersion { get; private set; }

        /// <summary>Men standing on road space that belongs to no block at this tick -
        /// the middle of a boulevard, the freeway, the ground between quarters. Reported
        /// (the geography overlay prints it) rather than smoothed away.</summary>
        public int BlocklessActors { get; private set; }

        /// <summary>Does anybody stand on this block as of the last Presence tick? Read
        /// off the sampling that already ran, so a view can ask it of every block on
        /// screen without walking every crew again for each one.</summary>
        public bool Occupied(TerritoryBlockId blockId) => occupiedBlocks.Contains(blockId);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Territory] A second runtime was ignored.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        public void Init(RoadDemoBuilder city, DemoCrews streetCrews)
        {
            if (state != null)
                return;

            builder = city;
            crews = streetCrews;

            var definitions = new List<TerritoryBlockDefinition>();
            var plan = builder?.Territories?.Plan;
            if (plan != null)
            {
                for (var i = 0; i < plan.Blocks.Count; i++)
                {
                    var block = plan.Blocks[i];
                    var quarter = plan.Quarter(block.QuarterId);
                    var bounds = builder.Territories.WorldBounds(block.Id);
                    definitions.Add(new TerritoryBlockDefinition(
                        TerritoryIdentity.ExistingBlock(block.StableId),
                        block.Id,
                        TerritoryIdentity.CoreNeighborhood(plan.Seed, (int)block.QuarterId),
                        quarter?.Name ?? block.QuarterId.ToString(),
                        block.Name,
                        new TerritoryBounds(bounds.xMin, bounds.yMin, bounds.width, bounds.height),
                        "CoreTerritoryPlan.StableId",
                        block.Kind));
                }
            }

            geography = new TerritoryGeography(definitions, GeographySettings(), OffGridAreas());
            BindBusinessGeography();
            state = new TerritorySimulationState(definitions);
            truth = new TerritoryTruthQuery(state, this, this);
            RegisterOrganizationBlocks();
            player = new TerritoryPlayerQuery(
                truth,
                new TerritoryGangId(0),
                TerritoryPresentationProfile.Default,
                FullTerritoryKnowledgeFilter.Instance);
            events = new TerritoryEventStream();
            commands = new TerritoryCommandGateway(this);

            scheduler = new TerritorySimulationScheduler();
            scheduler.SetCadence(
                TerritoryTickChannel.PhysicalPresence,
                Math.Max(0.05f, physicalPresenceMinutes) / 60.0);
            scheduler.SetCadence(
                TerritoryTickChannel.ResidualPresence,
                Math.Max(0.01f, residualPresenceHours));
            scheduler.SetCadence(
                TerritoryTickChannel.Fear,
                Math.Max(0.01f, fearHours));
            scheduler.SetCadence(
                TerritoryTickChannel.Business,
                Math.Max(0.01f, businessHours));
            scheduler.SetCadence(
                TerritoryTickChannel.DerivedControl,
                Math.Max(0.01f, controlHours));
            scheduler.Ticked += OnTerritoryTick;
        }

        /// <summary>
        /// The city's own street widths, handed to the geography so adjacency and road
        /// space are measured against the streets that were actually laid rather than a
        /// constant. Core publishes them; a host with no Core falls back to Core's
        /// measures, which every district in this project is built on the beat of.
        /// </summary>
        static TerritoryGeographySettings GeographySettings() =>
            new TerritoryGeographySettings(
                CoreLayout.AlleyWidth, CoreLayout.StreetWidth, CoreLayout.BoulevardWidth);

        /// <summary>
        /// The ground that carries no canonical block, named rather than left silent: the
        /// port, the field and the suburbs are quarters of the CITY, not of the territory
        /// plan, and Phase 1 says so out loud instead of letting a failed block lookup
        /// stand in for the statement. The primary structure (DistrictKind.Pad) IS the
        /// territory, so it is not among them.
        /// </summary>
        List<TerritoryOffGridArea> OffGridAreas()
        {
            var areas = new List<TerritoryOffGridArea>();
            var plans = builder?.DistrictPlans;
            if (plans == null)
                return areas;

            for (var i = 0; i < plans.Count; i++)
            {
                var district = plans[i];
                if (district.Kind == DistrictKind.Pad)
                    continue;
                areas.Add(new TerritoryOffGridArea(
                    district.Name,
                    district.Kind.ToString(),
                    new TerritoryBounds(district.World.xMin, district.World.yMin,
                                        district.World.width, district.World.height),
                    "outside the territory plan; no canonical blocks in Phase 1"));
            }

            return areas;
        }

        /// <summary>
        /// Resolve every simulated business to its canonical block, once, off plan data.
        /// The business pass runs before this one (RoadDemoBuilder.BuildBusinessSimulation),
        /// so the catalogue is complete and the mapping cannot depend on which blocks
        /// happen to be streamed in.
        /// </summary>
        void BindBusinessGeography()
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (geography == null || business == null || !business.Populated)
                return;

            geography.BindBusinesses(new LivingCity.Business.BusinessGeographySites(
                business.Catalog, business.Directory));
        }

        void Update()
        {
            RegisterOrganizationBlocks();
            if (scheduler == null)
                return;

            var clock = DayClock.Current;
            if (clock == null)
                return;

            scheduler.AdvanceTo(clock.Day * 24.0 + clock.Hour + debugTimeOffset);
        }

        void RegisterOrganizationBlocks()
        {
            if (organizationBlocksRegistered || truth == null || PersonnelDirector.Instance == null)
                return;
            PersonnelDirector.Instance.RegisterOrganizationBlocks(truth.BlockIds);
            organizationBlocksRegistered = true;
        }

        public void ForceEvaluation(TerritoryTickChannel channel) => scheduler?.Force(channel);

        public void DebugAdvance(float gameHours)
        {
            if (scheduler == null || gameHours <= 0f)
                return;

            var clock = DayClock.Current;
            if (clock == null)
            {
                scheduler.AdvanceBy(gameHours);
                return;
            }

            debugTimeOffset += gameHours;
            scheduler.AdvanceTo(clock.Day * 24.0 + clock.Hour + debugTimeOffset);
        }

        /// <summary>The canonical block a world point is on. One implementation, the
        /// geography's, so UI and simulation cannot resolve the same point differently.</summary>
        public bool TryGetBlockAtWorld(Vector3 world, out TerritoryBlockId blockId)
        {
            blockId = default;
            return geography != null &&
                   geography.TryGetBlockAt(new TerritoryPoint(world.x, world.z), out blockId);
        }

        /// <summary>
        /// Where a body standing here belongs, given where it stood last tick: on a block
        /// that block, on the street the block it just left while it is within half the
        /// widest street, and otherwise nowhere. Road space belongs to nobody, and a man
        /// in the middle of a boulevard is never quietly handed to the nearest block.
        /// </summary>
        public bool TryResolveStanding(
            Vector3 world, TerritoryBlockId previous, out TerritoryBlockId blockId)
        {
            blockId = default;
            return geography != null && geography.TryResolveStanding(
                new TerritoryPoint(world.x, world.z), previous, out blockId);
        }

        public bool TryGetBlock(int legacyBlockId, out TerritoryBlockId blockId)
        {
            var block = builder?.Territories?.Block(legacyBlockId);
            if (block == null)
            {
                blockId = default;
                return false;
            }

            blockId = TerritoryIdentity.ExistingBlock(block.StableId);
            return true;
        }

        void OnTerritoryTick(TerritorySimulationTick tick)
        {
            // SIM-006 schedules all future subsystems independently. This foundation only
            // samples physical block transitions; it does not aggregate Presence or run
            // Fear/compliance/control mechanics ahead of their tickets.
            if (tick.Channel == TerritoryTickChannel.PhysicalPresence)
                SampleActorBlocks(tick.GameHour);
        }

        void SampleActorBlocks(double gameHour)
        {
            sampledLocations.Clear();
            var changed = false;
            var blockless = 0;
            VisitActors((unit, actor, observation, blockId) =>
            {
                if (!blockId.IsValid)
                {
                    blockless++;
                    return;
                }

                sampledLocations[new ActorKey(observation.GangId, observation.CharacterId)] =
                    new ActorLocation(blockId, observation);
            });
            BlocklessActors = blockless;

            foreach (var pair in actorLocations)
            {
                if (sampledLocations.TryGetValue(pair.Key, out var current) &&
                    current.BlockId == pair.Value.BlockId)
                    continue;

                var actor = pair.Value.Actor;
                changed = true;
                events.Publish(new ActorLeftBlock(
                    pair.Value.BlockId,
                    actor.CharacterId,
                    actor.GangId,
                    actor.GroupId,
                    gameHour));
            }

            foreach (var pair in sampledLocations)
            {
                if (actorLocations.TryGetValue(pair.Key, out var previous) &&
                    previous.BlockId == pair.Value.BlockId)
                    continue;

                var actor = pair.Value.Actor;
                changed = true;
                events.Publish(new ActorEnteredBlock(
                    pair.Value.BlockId,
                    actor.CharacterId,
                    actor.GangId,
                    actor.GroupId,
                    gameHour));
            }

            var swap = actorLocations;
            actorLocations = sampledLocations;
            sampledLocations = swap;

            occupiedBlocks.Clear();
            foreach (var pair in actorLocations)
                occupiedBlocks.Add(pair.Value.BlockId);

            if (changed)
                ObservationVersion++;
        }

        public void CollectActors(
            TerritoryBlockId blockId, List<TerritoryActorObservation> into)
        {
            if (into == null)
                return;

            VisitActors((unit, actor, observation, actorBlockId) =>
            {
                if (actorBlockId == blockId)
                    into.Add(observation);
            });
        }

        /// <summary>
        /// Every actor Presence will need - the outfit's men and the rivals', crews and
        /// the lieutenants who lead them - with the block each stands on. The block is
        /// resolved AGAINST WHERE THE MAN STOOD LAST TICK, so a man on a pavement or
        /// crossing a street keeps the block he came from and produces exactly one
        /// leave/enter pair per crossing rather than a flutter at every kerb. Police
        /// units stay out: they are the city's, not a gang's.
        /// </summary>
        void VisitActors(
            Action<DemoCrews.Unit, CrewWalker, TerritoryActorObservation, TerritoryBlockId> visit)
        {
            if (crews == null || geography == null || visit == null)
                return;

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.IsPolice)
                    continue;

                foreach (var actor in unit.All())
                {
                    if (actor == null || actor.Dead || actor.Tf == null ||
                        !actor.Tf.gameObject.activeInHierarchy)
                        continue;

                    var observation = Observation(unit, actor);
                    var key = new ActorKey(observation.GangId, observation.CharacterId);
                    var previous = actorLocations.TryGetValue(key, out var last)
                        ? last.BlockId
                        : default;
                    TryResolveStanding(actor.Tf.position, previous, out var blockId);
                    visit(unit, actor, observation, blockId);
                }
            }
        }

        static TerritoryActorObservation Observation(DemoCrews.Unit unit, CrewWalker actor) =>
            new TerritoryActorObservation(
                new TerritoryCharacterId(actor.CharacterId),
                new TerritoryGangId(unit.Faction),
                TerritoryCommandNodeId.Crew(unit.CrewId),
                actor.DisplayName,
                unit.GangName,
                actor.IsLieutenant);

        public string CharacterName(TerritoryCharacterId characterId)
        {
            if (!characterId.IsValid)
                return "";
            var roster = PersonnelDirector.Instance?.Roster;
            return roster?.Find(characterId.Value)?.FullName ?? "";
        }

        // ---------------------------------------------------------- command executor

        public TerritoryCommandExecution Execute(AssignHoodToBossCommand command)
        {
            var director = PersonnelDirector.Instance;
            if (director?.Roster == null || !command.HoodId.IsValid || !command.BossId.IsValid)
                return TerritoryCommandExecution.Reject("Personnel identity is unavailable.");

            var result = director.AssignToBoss(command.HoodId.Value, command.BossId.Value);
            return result.Ok
                ? TerritoryCommandExecution.Succeed()
                : TerritoryCommandExecution.Reject(result.Reason);
        }

        public TerritoryCommandExecution Execute(AssignHoodToLieutenantCommand command)
        {
            var director = PersonnelDirector.Instance;
            var roster = director?.Roster;
            if (roster == null || !command.HoodId.IsValid || !command.LieutenantId.IsValid)
                return TerritoryCommandExecution.Reject("Personnel identity is unavailable.");

            var hood = roster.Find(command.HoodId.Value);
            var lieutenant = roster.Find(command.LieutenantId.Value);
            if (hood == null || hood.Gone || hood.Rank != Rank.Hood)
                return TerritoryCommandExecution.Reject("The requested hood is unavailable.");
            if (lieutenant == null || lieutenant.Gone || lieutenant.Rank != Rank.Lieutenant)
                return TerritoryCommandExecution.Reject("The requested lieutenant is unavailable.");

            var crew = roster.CrewOf(lieutenant.Id);
            if (crew == null || crew.LieutenantId != lieutenant.Id)
                return TerritoryCommandExecution.Reject("The lieutenant has no command node.");

            var result = director.AssignToLieutenant(hood.Id, lieutenant.Id);
            return result.Ok
                ? TerritoryCommandExecution.Succeed()
                : TerritoryCommandExecution.Reject(result.Reason);
        }

        public TerritoryCommandExecution Execute(AssignBlockResponsibilityCommand command)
        {
            if (state == null || !command.BlockId.IsValid ||
                !state.TryGetDefinition(command.BlockId, out _))
                return TerritoryCommandExecution.Reject("Unknown territory block.");
            if (!command.GangId.IsValid || !KnownGang(command.GangId))
                return TerritoryCommandExecution.Reject("Unknown gang.");
            if (!command.CommandNodeId.IsValid)
                return TerritoryCommandExecution.Reject("A stable command node is required.");

            if (command.CommandNodeId.Kind == TerritoryCommandNodeKind.Crew)
            {
                var unit = FindUnit(command.CommandNodeId);
                if (unit == null || unit.Faction != command.GangId.Value)
                    return TerritoryCommandExecution.Reject(
                        "The crew does not belong to the requested gang.");
            }

            var director = PersonnelDirector.Instance;
            var roster = director?.Roster;
            if (command.GangId.Value != GangCatalog.PlayerGangId)
                return TerritoryCommandExecution.Reject(
                    "Only the player's organization can assign administrative responsibility.");
            if (roster == null)
                return TerritoryCommandExecution.Reject("Personnel identity is unavailable.");

            var leaderId = -1;
            if (command.BossId.IsValid)
                leaderId = command.BossId.Value;
            else if (command.LieutenantId.IsValid)
                leaderId = command.LieutenantId.Value;
            else if (command.CommandNodeId.Kind == TerritoryCommandNodeKind.Boss ||
                     command.CommandNodeId.Kind == TerritoryCommandNodeKind.Lieutenant)
                leaderId = command.CommandNodeId.Value;
            else if (command.CommandNodeId.Kind == TerritoryCommandNodeKind.Crew)
                leaderId = FindUnit(command.CommandNodeId)?.CommandParentId ?? -1;

            var leader = roster.Find(leaderId);
            if (leader == null || (leader.Rank != Rank.Boss && leader.Rank != Rank.Lieutenant))
                return TerritoryCommandExecution.Reject("Unknown organization command parent.");

            var assigned = director.AssignBlockResponsibility(command.BlockId, leaderId);
            if (!assigned.Ok)
                return TerritoryCommandExecution.Reject(assigned.Reason);

            var bossId = leader.Rank == Rank.Boss
                ? new TerritoryCharacterId(leader.Id)
                : default;
            var lieutenantId = leader.Rank == Rank.Lieutenant
                ? new TerritoryCharacterId(leader.Id)
                : default;
            var nodeId = leader.Rank == Rank.Boss
                ? TerritoryCommandNodeId.Boss(leader.Id)
                : TerritoryCommandNodeId.Lieutenant(leader.Id);
            var responsibility = new TerritoryResponsibility(
                command.GangId, bossId, lieutenantId, nodeId);
            return state.AssignResponsibility(command.BlockId, responsibility)
                ? TerritoryCommandExecution.Succeed()
                : TerritoryCommandExecution.Reject("The responsibility assignment was refused.");
        }

        public OpResult RemoveBlockResponsibility(TerritoryBlockId blockId, int leaderId)
        {
            if (state == null || !state.TryGetDefinition(blockId, out _))
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonUnknownBlock);
            var director = PersonnelDirector.Instance;
            if (director == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            var result = director.RemoveBlockResponsibility(blockId, leaderId);
            if (result.Ok)
                state.ClearResponsibility(blockId);
            return result;
        }

        public TerritoryCommandExecution Execute(MoveTacticalGroupCommand command)
        {
            if (!command.Destination.IsFinite)
                return TerritoryCommandExecution.Reject("The destination is invalid.");
            if (command.DestinationBlockId.IsValid &&
                (state == null || !state.TryGetDefinition(command.DestinationBlockId, out _)))
                return TerritoryCommandExecution.Reject("The destination block is unknown.");

            var unit = FindPlayerUnit(command.GroupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);

            var world = new Vector3(command.Destination.X, crews.GroundY, command.Destination.Z);
            bool issued;
            if (command.Mode == TacticalMovementMode.DirectMarch)
            {
                issued = crews.MarchTo(unit, world, command.Run);
            }
            else
            {
                issued = crews.OrderUnit(unit, world, out _, command.Run);
            }

            return issued
                ? TerritoryCommandExecution.Pending("The group is travelling; no territory result was applied.")
                : TerritoryCommandExecution.Reject("The physical crew refused the move order.");
        }

        public TerritoryCommandExecution Execute(OperateInBlockCommand command)
        {
            if (state == null || !command.BlockId.IsValid ||
                !state.TryGetDefinition(command.BlockId, out var block))
                return TerritoryCommandExecution.Reject("Unknown territory block.");

            var unit = FindPlayerUnit(command.GroupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);

            var bounds = builder.Territories.WorldBounds(block.LegacyBlockId);
            var destination = new Vector3(bounds.center.x, crews.GroundY, bounds.center.y);
            return crews.MarchTo(unit, destination)
                ? TerritoryCommandExecution.Pending(
                    "The group is moving into the block; operation success is unresolved.")
                : TerritoryCommandExecution.Reject("The physical crew refused the order.");
        }

        public TerritoryCommandExecution Execute(ApproachBusinessCommand command)
        {
            if (!command.BusinessId.IsValid)
                return TerritoryCommandExecution.Reject("Unknown business.");

            var unit = FindPlayerUnit(command.GroupId, out var refusal);
            if (unit == null)
                return TerritoryCommandExecution.Reject(refusal);

            // The doorstep comes from the simulated site, so an order can be given to a
            // business whose block is streamed out - which is most of the city most of the
            // time. A live marker is used only for the ground height under it.
            if (!LivingCity.Business.CityBusinesses.TryApproachPoint(
                    command.BusinessId, out var door))
                return TerritoryCommandExecution.Reject("No such business in this city.");

            return crews.MarchTo(unit, door)
                ? TerritoryCommandExecution.Pending(
                    "The group is approaching; the business state is unchanged.")
                : TerritoryCommandExecution.Reject("The physical crew refused the order.");
        }

        public TerritoryCommandExecution Execute(DemandProtectionCommand command) =>
            TerritoryCommandExecution.Reject(
                "Protection demands are outside the simulation-foundation scope.");

        public TerritoryCommandExecution Execute(ThreatenBusinessOwnerCommand command) =>
            TerritoryCommandExecution.Reject(
                "Business intimidation is outside the simulation-foundation scope.");

        DemoCrews.Unit FindPlayerUnit(
            TerritoryCommandNodeId groupId, out string refusal)
        {
            refusal = "Unknown tactical group.";
            if (crews == null || groupId.Kind != TerritoryCommandNodeKind.Crew)
                return null;

            var unit = FindUnit(groupId);
            if (unit == null)
                return null;
            if (unit.Faction != 0)
            {
                refusal = "The player cannot command a rival tactical group.";
                return null;
            }
            if (unit.Wiped)
            {
                refusal = "The tactical group has nobody standing.";
                return null;
            }
            return unit;
        }

        DemoCrews.Unit FindUnit(TerritoryCommandNodeId nodeId)
        {
            if (crews == null || nodeId.Kind != TerritoryCommandNodeKind.Crew)
                return null;
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit != null && unit.CrewId == nodeId.Value)
                    return unit;
            }
            return null;
        }

        bool KnownGang(TerritoryGangId gangId)
        {
            if (!gangId.IsValid)
                return false;
            if (gangId.Value == 0)
                return true;

            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i] != null && gangs[i].Id == gangId.Value)
                    return true;

            if (crews == null)
                return false;
            for (var i = 0; i < crews.Units.Count; i++)
                if (crews.Units[i] != null && crews.Units[i].Faction == gangId.Value)
                    return true;
            return false;
        }

        void OnDestroy()
        {
            if (scheduler != null)
                scheduler.Ticked -= OnTerritoryTick;
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        readonly struct ActorKey : IEquatable<ActorKey>
        {
            public ActorKey(TerritoryGangId gangId, TerritoryCharacterId characterId)
            {
                GangId = gangId;
                CharacterId = characterId;
            }

            public TerritoryGangId GangId { get; }
            public TerritoryCharacterId CharacterId { get; }

            public bool Equals(ActorKey other) =>
                GangId == other.GangId && CharacterId == other.CharacterId;

            public override bool Equals(object obj) => obj is ActorKey other && Equals(other);
            public override int GetHashCode() =>
                GangId.GetHashCode() * 397 ^ CharacterId.GetHashCode();
        }

        readonly struct ActorLocation
        {
            public ActorLocation(
                TerritoryBlockId blockId, TerritoryActorObservation actor)
            {
                BlockId = blockId;
                Actor = actor;
            }

            public TerritoryBlockId BlockId { get; }
            public TerritoryActorObservation Actor { get; }
        }
    }
}
