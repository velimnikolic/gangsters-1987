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
    public sealed partial class TerritoryRuntime : MonoBehaviour,
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

        [Header("Presence (GAN-79)")]
        [Tooltip("What one ordinary body standing on a block is worth.")]
        [SerializeField, Min(0f)] float pointsPerContributor = 10f;
        [SerializeField, Min(0f)] float hoodWeight = 1f;
        [SerializeField, Min(0f)] float lieutenantWeight = 2f;
        [SerializeField, Min(0f)] float bossWeight = 3f;
        [Tooltip("A crew riding through contributes a fraction of a crew standing there.")]
        [SerializeField, Min(0f)] float transitWeight = 0.2f;
        [SerializeField, Min(0f)] float movingWeight = 0.6f;
        [SerializeField, Min(0f)] float stationedWeight = 1f;
        [SerializeField, Min(1f)] float presenceCap = 100f;
        [Tooltip("How much of the current Presence a block remembers per game hour worked.")]
        [SerializeField, Min(0f)] float residualDepositPerHour = 0.5f;
        [SerializeField, Min(0f)] float residualCap = 30f;
        [Tooltip("Game hours for half of what a block remembers to fade.")]
        [SerializeField, Min(0.01f)] float residualHalfLifeHours = 6f;

        [Header("Fear (GAN-90)")]
        [Tooltip("How much of an incident at one premise the whole block feels.")]
        [SerializeField, Range(0f, 1f)] float fearPropagationFraction = 0.35f;
        [Tooltip("How much of it the streets that TOUCH that block hear (GEO-008). " +
                 "It goes one street out and stops - fear is not a fluid.")]
        [SerializeField, Range(0f, 1f)] float fearNeighbourFraction = 0.10f;
        [Tooltip("Game hours a house has to answer an open refusal.")]
        [SerializeField, Min(0.01f)] float defianceWindowHours = 12f;
        [SerializeField, Min(1f)] float policeAttentionCap = 100f;
        [SerializeField, Min(0.01f)] float policeAttentionHalfLifeHours = 8f;
        [Tooltip("How much dearer violence gets while the police are already looking.")]
        [SerializeField, Min(0f)] float policeEscalation = 0.5f;
        [Tooltip("How far police attention can hold Presence down on a block.")]
        [SerializeField, Range(0f, 1f)] float presenceFloorUnderHeat = 0.25f;
        [Tooltip("Shots in one incident before the street calls it a public gunfight.")]
        [SerializeField, Min(1)] int publicIncidentShots = 3;
        [Tooltip("How far off a block an act still belongs to it. Most shooting happens " +
                 "in the road, which belongs to no block; the street beside it still hears it.")]
        [SerializeField, Min(0f)] float violenceReachMetres = 30f;

        [Header("The racket (GAN-103)")]
        [Tooltip("How close a man must stand to the door for a demand to be a real one.")]
        [SerializeField, Min(1f)] float approachRadiusMetres = 14f;
        [SerializeField, Min(0f)] float complianceFearWeight = 0.55f;
        [SerializeField, Min(0f)] float compliancePresenceWeight = 0.35f;
        [Tooltip("What the street having just seen trouble is worth to a demand.")]
        [SerializeField, Min(0f)] float complianceTroubleWeight = 0.15f;
        [Tooltip("How heavily another family's claim counts against the one asking.")]
        [SerializeField, Min(0f)] float complianceRivalWeight = 0.5f;
        [Tooltip("What a family must be worth on the street before an owner says yes. " +
                 "Lowered from 40 on 2026-09-01: at 40 the whole violence ladder - a " +
                 "threat is worth 3 points, a wrecked front 7, a robbery 13 - could not " +
                 "carry a demand on its own and every shop stayed wavering. Keep it in " +
                 "step with TerritoryRacketConfig's own default; THIS is the number the " +
                 "live city uses, and the two disagreeing is how the class default came " +
                 "to be dead code.")]
        [SerializeField] float complianceAcceptAt = 30f;
        [SerializeField] float complianceHesitateAt = 16f;
        [Tooltip("How far ahead a challenger must be to take a shop, and for how many " +
                 "business ticks running.")]
        [SerializeField, Min(0f)] float switchMargin = 18f;
        [SerializeField, Min(1)] int switchTicks = 3;
        [Tooltip("The Presence a family needs on a block before it leans on the shops there.")]
        [SerializeField, Min(0f)] float rivalDemandPresence = 25f;
        [SerializeField, Min(0)] int rivalDemandsPerTick = 2;

        [Header("Derived control (GAN-120)")]
        [SerializeField, Min(0f)] float controlPresenceWeight = 0.35f;
        [SerializeField, Min(0f)] float controlFearWeight = 0.25f;
        [SerializeField, Min(0f)] float controlComplianceWeight = 0.4f;
        [Tooltip("Where a family stops being on the street and starts running it. " +
                 "CONTROLLED must sit above what any one pillar can reach alone " +
                 "(presence caps at 100 x 0.35 = 35): bodies alone hold a street, " +
                 "they do not run it.")]
        [SerializeField] float influencedAt = 12f;
        [SerializeField] float controlledAt = 38f;
        [SerializeField] float dominatedAt = 65f;
        [Tooltip("How close the second family must be for the street to be a fight, and " +
                 "how far it must fall behind for the fight to be over.")]
        [SerializeField, Min(0f)] float contestedMargin = 10f;
        [SerializeField, Min(0f)] float contestedExitMargin = 16f;
        [SerializeField, Min(0f)] float contestedFloor = 12f;
        [Tooltip("Readings running that must agree before a street changes hands.")]
        [SerializeField, Min(1)] int controlHoldTicks = 2;
        [Tooltip("How far a house that never answers for its ground falls.")]
        [SerializeField, Range(0f, 1f)] float powerFloor = 0.5f;
        [SerializeField, Min(0f)] float powerPenalty = 0.5f;
        [SerializeField, Min(1f)] float powerMemoryHours = 72f;
        [SerializeField, Min(0.01f)] float powerAnswerWindowHours = 12f;

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

        /// <summary>One premise tally per block that has deeds on it, reused between
        /// control passes so the quarter-hour sweep over the whole city allocates
        /// nothing.</summary>
        readonly Dictionary<TerritoryBlockId, TerritoryControlDerivation.Tally> controlTallies =
            new Dictionary<TerritoryBlockId, TerritoryControlDerivation.Tally>();
        readonly List<TerritoryGangSignals> controlScratch = new List<TerritoryGangSignals>();

        TerritoryPresenceLedger presence;
        readonly List<TerritoryPresenceChange> presenceChanges =
            new List<TerritoryPresenceChange>();
        readonly List<TerritoryGangPresence> presenceGangs = new List<TerritoryGangPresence>();
        readonly List<TerritoryGangSignals> presenceScratch = new List<TerritoryGangSignals>();
        readonly HashSet<TerritoryBlockId> presenceDirty = new HashSet<TerritoryBlockId>();

        TerritoryFearLedger fear;
        readonly List<TerritoryFearChange> fearChanges = new List<TerritoryFearChange>();
        readonly List<TerritoryGangValue> fearGangs = new List<TerritoryGangValue>();
        readonly List<TerritoryGangSignals> fearScratch = new List<TerritoryGangSignals>();
        readonly HashSet<TerritoryBlockId> fearDirty = new HashSet<TerritoryBlockId>();
        readonly List<PendingIncident> pendingIncidents = new List<PendingIncident>();
        readonly List<RecentShot> recentShots = new List<RecentShot>();
        readonly List<TerritoryFearEvent> defianceEmitted = new List<TerritoryFearEvent>();
        double lastGameHour;

        TerritoryRacketLedger racket;
        readonly List<TerritoryProtectionChange> racketChanges =
            new List<TerritoryProtectionChange>();
        readonly List<PendingApproach> pendingApproaches = new List<PendingApproach>();
        readonly List<TerritoryProtectionRelationship> relationScratch =
            new List<TerritoryProtectionRelationship>();
        readonly List<TerritoryBusinessId> blockBusinessScratch = new List<TerritoryBusinessId>();
        int rivalDemandCursor;

        TerritoryControlLedger control;
        TerritoryPowerLedger power;
        readonly List<TerritoryControlScore> controlScores = new List<TerritoryControlScore>();
        readonly List<TerritoryGangId> controlGangs = new List<TerritoryGangId>();
        readonly List<TerritoryGangId> racketGangScratch = new List<TerritoryGangId>();

        public ITerritoryTruthQuery DebugTruth => truth;

        /// <summary>Who is really standing on which block, family by family. Simulation
        /// writes it on the scheduler's ticks; the inspector and the audit only read it.</summary>
        public TerritoryPresenceLedger Presence => presence;

        /// <summary>What each street is afraid of, and how hard the law is looking at it.
        /// Written only by the Fear channel and the street's own violence.</summary>
        public TerritoryFearLedger Fear => fear;

        /// <summary>Who pays whom, shop by shop. Written only by resolved interactions -
        /// a click never reaches it, and neither does a marker.</summary>
        public TerritoryRacketLedger Racket => racket;

        /// <summary>What each street currently reads as, and who leads it. DERIVED on the
        /// control channel from what every family has going for it there - there is no
        /// owner field anywhere in this project, and no command that sets one.</summary>
        public TerritoryControlLedger Control => control;

        /// <summary>Whether a family answers for what happens to the ground it is paid to
        /// protect. Scales everything else it is worth on that block.</summary>
        public TerritoryPowerLedger Power => power;

        /// <summary>The last game hour territory was ticked at - the stamp every Fear
        /// record is filed under, so an act that happens between ticks is not filed at a
        /// time the scheduler has never seen.</summary>
        public double GameHour => lastGameHour;

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

        /// <summary>Moves whenever a shopkeeper's standing with anybody changes, which
        /// the block signals do NOT: wavering and shaken are the same fraction of a yes.
        /// A page showing what one owner said watches this.</summary>
        public int RacketVersion => racket?.Version ?? 0;
        public int ObservationVersion { get; private set; }

        /// <summary>Men standing on road space that belongs to no block at this tick -
        /// the middle of a boulevard, the freeway, the ground between quarters. Reported
        /// (the geography overlay prints it) rather than smoothed away.</summary>
        public int BlocklessActors { get; private set; }

        /// <summary>Does anybody stand on this block as of the last Presence tick? Read
        /// off the sampling that already ran, so a view can ask it of every block on
        /// screen without walking every crew again for each one.</summary>
        public bool Occupied(TerritoryBlockId blockId) => occupiedBlocks.Contains(blockId);

        static readonly List<TerritoryBlockId> QuarterMembers = new List<TerritoryBlockId>();

        /// <summary>
        /// A quarter's standing, counted off the streets that actually belong to it
        /// (CTRL-013). ONE aggregation, served here: a page that counted blocks itself -
        /// by rectangle, say - would disagree with this one wherever a quarter's
        /// boundary and its block membership differ, and two answers to the same
        /// question is the fault. Nothing is stored; a neighbourhood is not a thing
        /// anybody takes, it is what its streets add up to.
        /// </summary>
        public bool TryReadNeighborhood(
            TerritoryNeighborhoodId neighborhoodId, out TerritoryNeighborhoodStatus status)
        {
            status = default;
            if (geography == null || control == null ||
                !geography.TryGetNeighborhood(neighborhoodId, out var hood))
                return false;

            QuarterMembers.Clear();
            for (var i = 0; i < hood.BlockIds.Count; i++)
                QuarterMembers.Add(hood.BlockIds[i]);
            status = TerritoryNeighborhoodReading.Read(neighborhoodId, QuarterMembers, control);
            return true;
        }

        /// <summary>
        /// The canonical neighbourhood id of one Core quarter. The blocks were registered
        /// under exactly this identity at Init, so the map's quarter and the simulation's
        /// neighbourhood are the same object rather than two things with the same name.
        /// </summary>
        public bool TryGetNeighborhoodOf(CoreQuarterId quarterId, out TerritoryNeighborhoodId id)
        {
            var plan = builder?.Territories?.Plan;
            if (plan == null)
            {
                id = default;
                return false;
            }
            id = TerritoryIdentity.CoreNeighborhood(plan.Seed, (int)quarterId);
            return geography != null && geography.TryGetNeighborhood(id, out _);
        }

        /// <summary>Every block one family has a man standing on, as of the last Presence
        /// tick. Collected in one pass so a view can ask the question once per repaint
        /// instead of once per block.</summary>
        public void CollectOccupiedBlocks(int gangId, HashSet<TerritoryBlockId> into)
        {
            if (into == null)
                return;
            into.Clear();
            foreach (var pair in actorLocations)
                if (pair.Value.Actor.GangId.IsValid && pair.Value.Actor.GangId.Value == gangId)
                    into.Add(pair.Value.BlockId);
        }

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
            presence = new TerritoryPresenceLedger(new TerritoryPresenceConfig(
                pointsPerContributor, hoodWeight, lieutenantWeight, bossWeight,
                transitWeight, movingWeight, stationedWeight, presenceCap,
                residualDepositPerHour, residualCap, residualHalfLifeHours));
            fear = new TerritoryFearLedger(new TerritoryFearConfig(
                propagationFraction: fearPropagationFraction,
                neighbourFraction: fearNeighbourFraction,
                defianceWindowHours: defianceWindowHours,
                policeAttentionCap: policeAttentionCap,
                policeAttentionHalfLifeHours: policeAttentionHalfLifeHours,
                policeEscalation: policeEscalation,
                presenceFloor: presenceFloorUnderHeat));
            // The block graph is what makes a street have neighbours at all (GEO-008):
            // without it fear stops at the kerb, which is what it did until now.
            fear.Geography = geography;
            racket = new TerritoryRacketLedger(new TerritoryRacketConfig(
                complianceFearWeight, compliancePresenceWeight, complianceTroubleWeight,
                complianceRivalWeight, complianceAcceptAt, complianceHesitateAt,
                switchMargin: switchMargin, switchTicks: switchTicks,
                rivalDemandPresence: rivalDemandPresence,
                rivalDemandsPerTick: rivalDemandsPerTick,
                approachRadiusMetres: approachRadiusMetres));
            var controlConfig = new TerritoryControlConfig(
                controlPresenceWeight, controlFearWeight, controlComplianceWeight,
                influencedAt, controlledAt, dominatedAt,
                contestedMargin, contestedFloor, contestedExitMargin, controlHoldTicks,
                powerFloor, powerPenalty, powerMemoryHours, powerAnswerWindowHours);
            control = new TerritoryControlLedger(controlConfig);
            power = new TerritoryPowerLedger(controlConfig);
            StreetAlarm.OnShot += OnStreetShot;
            StreetAlarm.OnDeath += OnStreetDeath;

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

            lastGameHour = clock.Day * 24.0 + clock.Hour + debugTimeOffset;
            scheduler.AdvanceTo(lastGameHour);
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
            // SIM-006 schedules every subsystem independently: physical sampling and its
            // Presence arithmetic on one channel, what the block remembers on another,
            // control on a third. Fear and compliance still belong to their own tickets.
            if (tick.Channel == TerritoryTickChannel.PhysicalPresence)
                SampleActorBlocks(tick.GameHour, tick.CadenceHours);
            else if (tick.Channel == TerritoryTickChannel.ResidualPresence)
                DecayPresence(tick.GameHour, tick.CadenceHours);
            else if (tick.Channel == TerritoryTickChannel.Fear)
                SettleFear(tick.GameHour);
            else if (tick.Channel == TerritoryTickChannel.Business)
                SettleBusinesses(tick.GameHour);
            else if (tick.Channel == TerritoryTickChannel.DerivedControl)
                DeriveControl();
        }

        /// <summary>
        /// Publishes what every street reads as. Control is DERIVED and re-derived: from
        /// the men standing there, what the street fears, which of its shops are paying
        /// whom, and whether the family answers for the ground it is paid to protect.
        /// There is no owner field to set and no command that could set one - a street
        /// changes hands because the things behind it changed, or it does not change.
        ///
        /// The deed share each family holds is still published beside it (the Influence
        /// channel), because the maps and the ledger read it - but it is a FACT about the
        /// premises now, not the authority on the block.
        /// </summary>
        void DeriveControl()
        {
            if (state == null)
                return;

            foreach (var tally in controlTallies.Values)
                tally.Clear();

            var deeds = PropertyRegistry.Businesses;
            for (var i = 0; i < deeds.Count; i++)
            {
                var business = deeds[i];
                if (!business || business.GangId < 0 || !business.CanonicalBlockId.IsValid)
                    continue;
                if (!controlTallies.TryGetValue(business.CanonicalBlockId, out var tally))
                {
                    tally = new TerritoryControlDerivation.Tally();
                    controlTallies[business.CanonicalBlockId] = tally;
                }
                tally.Add(business.GangId);
            }

            // Every block is read, not just the ones with deeds: a block whose last man
            // walked off has to stop saying it is held. The change guard is what keeps
            // that from bumping the whole city's version every quarter hour.
            var ids = state.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var blockId = ids[i];
                controlTallies.TryGetValue(blockId, out var tally);
                var current = state.SignalsOf(blockId);
                var next = TerritoryControlDerivation.Signals(tally, current, controlScratch);

                var reading = ReadControl(blockId, next);
                if (!TerritoryControlDerivation.Same(current, reading) ||
                    current.Control != reading.Control ||
                    current.LeadingGangId != reading.LeadingGangId)
                    state.SetSignals(blockId, reading);
            }

            power?.Forget(lastGameHour);
        }

        /// <summary>
        /// Score every family that has anything going for it here, let the control ledger
        /// decide whether that is enough to change what the block says, and announce it.
        /// </summary>
        TerritoryBlockSignals ReadControl(
            TerritoryBlockId blockId, TerritoryBlockSignals deedSignals)
        {
            if (control == null)
                return deedSignals;

            CollectControlGangs(blockId);
            controlScores.Clear();
            for (var i = 0; i < controlGangs.Count; i++)
                controlScores.Add(control.Config.Score(ControlInputsFor(blockId, controlGangs[i])));

            if (control.Read(blockId, controlScores, lastGameHour, out var change))
            {
                events.Publish(new BlockControlChanged(
                    blockId, change.PreviousLeader, change.Leader, change.Current,
                    change.GameHour));
                if (change.BecameContested)
                {
                    // The event names the two CONTENDERS - the leader and the actual
                    // runner-up by score - not whoever led before the fight started.
                    var second = default(TerritoryGangId);
                    var secondScore = float.MinValue;
                    for (var s = 0; s < controlScores.Count; s++)
                    {
                        if (controlScores[s].GangId == change.Leader ||
                            controlScores[s].Total <= secondScore)
                            continue;
                        secondScore = controlScores[s].Total;
                        second = controlScores[s].GangId;
                    }
                    events.Publish(new BlockBecameContested(
                        blockId, change.Leader, second, change.GameHour));
                }
                if (change.LostControl && change.PreviousLeader.IsValid)
                    events.Publish(new BlockControlLost(
                        blockId, change.PreviousLeader, change.GameHour));
            }

            return new TerritoryBlockSignals(
                deedSignals.LocalFear,
                deedSignals.BusinessCompliance,
                deedSignals.CompliantBusinesses,
                deedSignals.TotalBusinesses,
                control.StateOf(blockId),
                control.LeaderOf(blockId),
                deedSignals.Gangs);
        }

        /// <summary>Every family with men, a name or a shop on this street.</summary>
        void CollectControlGangs(TerritoryBlockId blockId)
        {
            controlGangs.Clear();
            if (presence != null)
            {
                presence.CollectGangs(blockId, presenceGangs);
                for (var i = 0; i < presenceGangs.Count; i++)
                    if (!controlGangs.Contains(presenceGangs[i].GangId))
                        controlGangs.Add(presenceGangs[i].GangId);
            }

            if (fear != null)
            {
                fear.CollectGangs(blockId, lastGameHour, fearGangs);
                for (var i = 0; i < fearGangs.Count; i++)
                    if (!controlGangs.Contains(fearGangs[i].GangId))
                        controlGangs.Add(fearGangs[i].GangId);
            }

            if (racket == null || geography == null)
                return;

            BlockBusinesses(blockId);
            racket.CollectGangsOn(blockBusinessScratch, racketGangScratch);
            for (var i = 0; i < racketGangScratch.Count; i++)
                if (!controlGangs.Contains(racketGangScratch[i]))
                    controlGangs.Add(racketGangScratch[i]);
        }

        /// <summary>What one family has going for it here, gathered from the systems that
        /// own each number. Nothing is copied into the control reading's own store.</summary>
        public TerritoryControlInputs ControlInputsFor(
            TerritoryBlockId blockId, TerritoryGangId gangId)
        {
            var standing = presence?.TotalOf(blockId, gangId) ?? 0f;
            var feared = fear?.FearOf(blockId, gangId, lastGameHour) ?? 0f;
            var paying = 0f;
            if (racket != null && geography != null)
            {
                BlockBusinesses(blockId);
                paying = racket.ComplianceOf(blockBusinessScratch, gangId);
            }

            return new TerritoryControlInputs(
                gangId, standing, feared, paying,
                power?.Coefficient(blockId, gangId, lastGameHour) ?? 1f);
        }

        void BlockBusinesses(TerritoryBlockId blockId)
        {
            blockBusinessScratch.Clear();
            var here = geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
                blockBusinessScratch.Add(here[i].BusinessId);
        }

        void SampleActorBlocks(double gameHour, double cadenceHours)
        {
            // Before the arrivals: a walk that has stopped closing on its door is sent
            // out again here, because nothing else in the game ever looks at it.
            TendApproaches();
            sampledLocations.Clear();
            presence?.BeginSample();
            var changed = false;
            var blockless = 0;
            VisitActors((unit, actor, observation, blockId) =>
            {
                // Men at a door are men who have arrived: an approach order becomes a real
                // interaction here, off the same sampling, rather than in a command. It is
                // asked before the block test, because a compound's gate stands on the road.
                NoteApproachArrival(unit, actor, observation, gameHour);
                NoteRoundArrival(unit, actor, observation, gameHour);

                if (!blockId.IsValid)
                {
                    blockless++;
                    return;
                }

                sampledLocations[new ActorKey(observation.GangId, observation.CharacterId)] =
                    new ActorLocation(blockId, observation);

                // The same pass that reports who crossed a kerb is the pass that counts
                // Presence: one walk over the city's bodies, one truth out of it. A block
                // the police are watching is worth less to stand on (FEAR-013), and a
                // man is worth what his commander extracts from him (RANK-004) - the
                // same five men hold more ground under a better lieutenant.
                var ground = fear == null ? 1f : fear.PresenceScale(blockId, gameHour);
                var roster = LivingCity.Gameplay.PersonnelDirector.Instance != null
                    ? LivingCity.Gameplay.PersonnelDirector.Instance.Roster
                    : null;
                presence?.Contribute(
                    blockId, observation,
                    ground * LivingCity.Personnel.Command.PresenceFactorFor(
                        roster, observation.CharacterId.IsValid
                            ? observation.CharacterId.Value
                            : -1) *
                    // ECON-006: a man with a NAME on this street is worth more standing
                    // on it - the PRES-003 rank weight, extended, and only while he is
                    // physically here.
                    ReputationScale(observation.CharacterId, blockId, gameHour));
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

            CommitPresence(gameHour, cadenceHours);
        }

        /// <summary>
        /// Close the Presence sample and publish what moved. Presence is written ONLY
        /// here and in the residual pass - never by a command, never by a view - and it
        /// assigns nothing: control is still read off the deeds on the block.
        /// </summary>
        void CommitPresence(double gameHour, double cadenceHours)
        {
            if (presence == null)
                return;

            presenceChanges.Clear();
            presence.CommitSample(cadenceHours, presenceChanges);
            PublishPresence(gameHour);
        }

        void DecayPresence(double gameHour, double cadenceHours)
        {
            if (presence == null)
                return;

            presenceChanges.Clear();
            presence.DecayResidual(cadenceHours, presenceChanges);
            PublishPresence(gameHour);
        }

        void PublishPresence(double gameHour)
        {
            if (presenceChanges.Count == 0)
                return;

            presenceDirty.Clear();
            for (var i = 0; i < presenceChanges.Count; i++)
            {
                var change = presenceChanges[i];
                presenceDirty.Add(change.BlockId);
                events.Publish(new PresenceChanged(
                    change.BlockId, change.GangId, change.Previous, change.Current, gameHour));
            }

            foreach (var blockId in presenceDirty)
            {
                var current = state.SignalsOf(blockId);
                presence.CollectGangs(blockId, presenceGangs);
                var next = TerritoryPresenceSignals.Merge(current, presenceGangs, presenceScratch);
                if (!TerritoryControlDerivation.Same(current, next))
                    state.SetSignals(blockId, next);
            }

            presenceChanges.Clear();
        }

        // ------------------------------------------------------------------ fear
        //
        // The street already reports its own violence: StreetAlarm fires on every shot
        // and every death. This is the adapter that turns those into territory acts -
        // it redesigns no combat, no police and no alarm, it only asks where it happened,
        // whose it was, and how loud it was.

        /// <summary>
        /// One bullet is not one act. Shots are gathered per StreetAlarm incident, per
        /// family, per block, and filed as a single act on the Fear tick - so a gunfight
        /// is one thing the street remembers rather than thirty.
        /// </summary>
        void OnStreetShot(StreetAlarm.Shot shot)
        {
            if (fear == null || geography == null)
                return;

            RememberShot(shot);
            if (!TryGetBlockForAct(shot.Pos, out var blockId))
                return;

            // The law's own shots frighten a street and bring more law, but they make no
            // family feared: an unattributed act stays unattributed.
            var gangId = shot.Faction == StreetAlarm.PoliceFaction
                ? default
                : new TerritoryGangId(shot.Faction);

            for (var i = 0; i < pendingIncidents.Count; i++)
            {
                var pending = pendingIncidents[i];
                if (pending.Incident != StreetAlarm.IncidentNumber ||
                    pending.BlockId != blockId || pending.GangId != gangId)
                    continue;
                pending.Shots++;
                pendingIncidents[i] = pending;
                return;
            }

            pendingIncidents.Add(new PendingIncident(
                StreetAlarm.IncidentNumber, blockId, gangId, 1, lastGameHour));
        }

        /// <summary>
        /// A killing is filed against the house that was shooting here, and against
        /// nobody at all when more than one was. A guess would be worse than a blank.
        /// </summary>
        void OnStreetDeath(Vector3 position, StreetAlarm.DeathOf who)
        {
            if (fear == null || geography == null)
                return;
            if (!TryGetBlockForAct(position, out var blockId))
                return;

            var severity = who == StreetAlarm.DeathOf.Officer ? 1.6f
                : who == StreetAlarm.DeathOf.Civilian ? 1.3f
                : 1f;
            RecordFear(new TerritoryFearEvent(
                AttributeRecentViolence(position),
                blockId,
                TerritoryFearCategory.Killing,
                severity,
                // A body in the street is a public fact whoever saw the shot.
                TerritoryFearVisibility.Public,
                lastGameHour));
        }

        /// <summary>
        /// Where an ACT belongs. Not where a body stands: almost all shooting happens in
        /// the road, and road space belongs to no block, so an act is allowed to land on
        /// the street beside it. Presence is never resolved this way - a man standing in
        /// a boulevard still holds nothing.
        /// </summary>
        public bool TryGetBlockForAct(Vector3 world, out TerritoryBlockId blockId)
        {
            blockId = default;
            return geography != null && geography.TryGetBlockNear(
                new TerritoryPoint(world.x, world.z), violenceReachMetres, out blockId);
        }

        void RememberShot(StreetAlarm.Shot shot)
        {
            recentShots.Add(new RecentShot(shot.Pos, shot.Faction, shot.Time));
            // A short window: only what was being fired around this body, moments ago.
            for (var i = recentShots.Count - 1; i >= 0; i--)
                if (shot.Time - recentShots[i].Time > AttributionSeconds)
                    recentShots.RemoveAt(i);
        }

        const float AttributionSeconds = 6f;
        const float AttributionRange = 40f;

        /// <summary>The one faction that was shooting here just now, or nobody.</summary>
        TerritoryGangId AttributeRecentViolence(Vector3 position)
        {
            var now = Time.time;
            var found = -1;
            for (var i = recentShots.Count - 1; i >= 0; i--)
            {
                var shot = recentShots[i];
                if (now - shot.Time > AttributionSeconds)
                {
                    recentShots.RemoveAt(i);
                    continue;
                }

                if ((shot.Position - position).sqrMagnitude >
                    AttributionRange * AttributionRange)
                    continue;
                if (shot.Faction == StreetAlarm.PoliceFaction)
                    continue;
                if (found >= 0 && found != shot.Faction)
                    return default;   // two houses were shooting; the street cannot say
                found = shot.Faction;
            }

            return found < 0 ? default : new TerritoryGangId(found);
        }

        /// <summary>
        /// The authoritative way an act enters territory. Everything - the street's own
        /// violence, a resolved threat, an unanswered refusal - comes through here, and
        /// the event stream hears about it in the same breath.
        /// </summary>
        float RecordFear(TerritoryFearEvent value)
        {
            if (fear == null || !value.BlockId.IsValid)
                return 0f;

            var impact = fear.Record(value);
            NotePower(value);
            fearDirty.Add(value.BlockId);
            events.Publish(new FearEventRecorded(
                value.BlockId, value.GangId, value.SourceActorId, impact, value.GameHour));
            return impact;
        }

        /// <summary>
        /// Violence on a street is a bill for whoever is paid to keep the peace on it.
        /// Every family with a shop here that did NOT do this has an incident against its
        /// name; the family that DID it has answered for its own ground. That is the whole
        /// of Power in Phase 1: a house that never comes when its shops are hit is worth
        /// less on that street than one that does, whatever else it has going for it.
        /// </summary>
        void NotePower(TerritoryFearEvent value)
        {
            if (power == null || racket == null || geography == null || !value.BlockId.IsValid)
                return;
            if (value.Category != TerritoryFearCategory.Assault &&
                value.Category != TerritoryFearCategory.PropertyDamage &&
                value.Category != TerritoryFearCategory.Shot &&
                value.Category != TerritoryFearCategory.Killing)
                return;

            BlockBusinesses(value.BlockId);
            for (var i = 0; i < blockBusinessScratch.Count; i++)
            {
                if (!racket.TryGetProtector(blockBusinessScratch[i], out var protector))
                    continue;
                if (protector == value.GangId)
                    power.Answered(value.BlockId, protector, value.GameHour);
                else
                    power.Incident(value.BlockId, protector, value.GameHour);
            }
        }

        /// <summary>
        /// A threat that actually landed: a crew stood in front of an owner and said it.
        /// This is the ONLY door into Fear for the intimidation flow - a command that was
        /// merely issued, or a button that was merely clicked, never reaches it, because
        /// the resolution that calls this happens after the physical encounter.
        /// </summary>
        public bool RecordResolvedThreat(
            TerritoryGangId gangId,
            TerritoryBusinessId businessId,
            float severity = 1f,
            TerritoryFearVisibility visibility = TerritoryFearVisibility.Seen,
            TerritoryCharacterId sourceActorId = default)
        {
            if (fear == null || geography == null || !gangId.IsValid)
                return false;
            if (!geography.TryGetBusinessBlock(businessId, out var blockId))
                return false;

            RecordFear(new TerritoryFearEvent(
                gangId, blockId, TerritoryFearCategory.Threat, severity, visibility,
                lastGameHour, businessId, sourceActorId));
            // Whatever was owed for an earlier refusal, the house has now answered it.
            fear.AnswerDefiance(gangId, businessId);
            return true;
        }

        /// <summary>A premise has openly refused a house; the clock on that starts now.</summary>
        public bool RecordOpenRefusal(TerritoryGangId gangId, TerritoryBusinessId businessId)
        {
            if (fear == null || geography == null || !gangId.IsValid)
                return false;
            if (!geography.TryGetBusinessBlock(businessId, out var blockId))
                return false;
            fear.OpenDefiance(gangId, blockId, businessId, lastGameHour);
            return true;
        }

        /// <summary>Violence that landed somewhere other than a shot or a body - a window
        /// put in, a car burned, a beating. Reported by whoever did it.</summary>
        public float RecordViolence(
            TerritoryGangId gangId,
            TerritoryBlockId blockId,
            TerritoryFearCategory category,
            float severity = 1f,
            TerritoryFearVisibility visibility = TerritoryFearVisibility.Seen,
            TerritoryBusinessId businessId = default) =>
            RecordFear(new TerritoryFearEvent(
                gangId, blockId, category, severity, visibility, lastGameHour, businessId));

        /// <summary>
        /// File the gunfights that finished, fade what every street remembers, and
        /// publish. Presence is never consulted: a house whose men have gone home is
        /// still the house that did this here.
        /// </summary>
        void SettleFear(double gameHour)
        {
            if (fear == null || state == null)
                return;

            for (var i = pendingIncidents.Count - 1; i >= 0; i--)
            {
                var pending = pendingIncidents[i];
                pendingIncidents.RemoveAt(i);
                // Diminishing: the twentieth round of an exchange does not frighten a
                // street the way the first did, and the config caps it besides.
                RecordFear(new TerritoryFearEvent(
                    pending.GangId,
                    pending.BlockId,
                    TerritoryFearCategory.Shot,
                    (float)Math.Sqrt(Math.Max(1, pending.Shots)),
                    pending.Shots >= publicIncidentShots
                        ? TerritoryFearVisibility.Public
                        : TerritoryFearVisibility.Seen,
                    pending.GameHour));
            }

            fearChanges.Clear();
            fear.Evaluate(gameHour, fearChanges);
            for (var i = 0; i < fearChanges.Count; i++)
                fearDirty.Add(fearChanges[i].BlockId);

            PublishFear(gameHour);
        }

        void SweepDefiance(double gameHour)
        {
            if (fear == null)
                return;

            defianceEmitted.Clear();
            fear.SweepDefiance(gameHour, defianceEmitted);
            for (var i = 0; i < defianceEmitted.Count; i++)
            {
                var value = defianceEmitted[i];
                fearDirty.Add(value.BlockId);
                events.Publish(new FearEventRecorded(
                    value.BlockId, value.GangId, value.SourceActorId,
                    fear.Config.ImpactOf(value), value.GameHour));
            }

            if (defianceEmitted.Count > 0)
                PublishFear(gameHour);
        }

        /// <summary>
        /// Write what the streets feel into the one store. The per-family numbers are the
        /// Fear channel; the block's own LocalFear is the strongest of them, so a page
        /// that only wants "is this street frightened" has one number to read.
        /// </summary>
        void PublishFear(double gameHour)
        {
            if (fearDirty.Count == 0)
                return;

            foreach (var blockId in fearDirty)
            {
                var current = state.SignalsOf(blockId);
                fear.CollectGangs(blockId, gameHour, fearGangs);
                var merged = TerritoryPresenceSignals.Merge(
                    current, fearGangs, TerritorySignalChannel.Fear, fearScratch);
                var ambient = fear.BlockFear(blockId, gameHour);
                var next = new TerritoryBlockSignals(
                    ambient > 0f ? ambient : (float?)null,
                    merged.BusinessCompliance,
                    merged.CompliantBusinesses,
                    merged.TotalBusinesses,
                    merged.Control,
                    merged.LeadingGangId,
                    merged.Gangs);

                if (SameFear(current, next))
                    continue;

                var previous = current.LocalFear ?? 0f;
                state.SetSignals(blockId, next);
                if (Math.Abs((next.LocalFear ?? 0f) - previous) >= 0.01f)
                    events.Publish(new FearChanged(
                        blockId, previous, next.LocalFear ?? 0f, gameHour));
            }

            fearDirty.Clear();
            fearChanges.Clear();
        }

        static bool SameFear(TerritoryBlockSignals left, TerritoryBlockSignals right) =>
            TerritoryControlDerivation.Same(left, right) &&
            Math.Abs((left?.LocalFear ?? 0f) - (right?.LocalFear ?? 0f)) < 0.0001f;

        readonly struct RecentShot
        {
            public RecentShot(Vector3 position, int faction, float time)
            {
                Position = position;
                Faction = faction;
                Time = time;
            }

            public Vector3 Position { get; }
            public int Faction { get; }
            public float Time { get; }
        }

        struct PendingIncident
        {
            public PendingIncident(
                int incident, TerritoryBlockId blockId, TerritoryGangId gangId,
                int shots, double gameHour)
            {
                Incident = incident;
                BlockId = blockId;
                GangId = gangId;
                Shots = shots;
                GameHour = gameHour;
            }

            public int Incident { get; }
            public TerritoryBlockId BlockId { get; }
            public TerritoryGangId GangId { get; }
            public int Shots { get; set; }
            public double GameHour { get; }
        }

        // --------------------------------------------------------------- the racket
        //
        // A demand is a thing men do standing in a doorway, so every path in here asks the
        // same two questions first: is this a real business, and is one of that family's
        // men actually at its door. A click on a map is an intent; this is the interaction.

        /// <summary>Where the door is, as the plan describes it - not as a streamed view
        /// happens to stand.</summary>
        public bool TryGetBusinessApproach(TerritoryBusinessId businessId, out Vector3 point) =>
            LivingCity.Business.CityBusinesses.TryApproachPoint(businessId, out point);

        /// <summary>
        /// Whether this family has a man standing close enough to the door to be having a
        /// conversation. The one precondition every racket interaction shares.
        /// </summary>
        public bool HasManAt(TerritoryGangId gangId, Vector3 point, float radius)
        {
            if (crews == null || !gangId.IsValid)
                return false;

            var r2 = radius * radius;
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.IsPolice || unit.Faction != gangId.Value)
                    continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null ||
                        !man.Tf.gameObject.activeInHierarchy)
                        continue;
                    if ((man.Tf.position - point).sqrMagnitude <= r2)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The street's own handle on a crew the ledger names. The outfit's units carry
        /// the roster crew's id (DemoCrews builds them that way), so the ledger can order
        /// a crew about without knowing anything about the men standing in the road.
        /// </summary>
        public bool TryGetCrewNode(int crewId, out TerritoryCommandNodeId node)
        {
            node = default;
            if (crews == null)
                return false;

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.IsPolice || unit.Faction != 0 || unit.Wiped)
                    continue;
                if (unit.CrewId != crewId)
                    continue;
                node = TerritoryCommandNodeId.Crew(unit.CrewId);
                return true;
            }

            return false;
        }

        /// <summary>Which family a named man belongs to, read off the street he is on.</summary>
        public bool TryGetActorGang(TerritoryCharacterId actorId, out TerritoryGangId gangId)
        {
            gangId = default;
            if (crews == null || !actorId.IsValid)
                return false;

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.IsPolice)
                    continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.CharacterId != actorId.Value)
                        continue;
                    gangId = new TerritoryGangId(unit.Faction);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Everything an owner weighs, gathered from the block his shop stands on: how
        /// much this street fears the family asking, how heavily it stands here, what the
        /// street has just been through, and whose claim stands against them.
        /// </summary>
        bool TryComplianceInputs(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            out TerritoryComplianceInputs inputs,
            out TerritoryBlockId blockId)
        {
            inputs = default;
            blockId = default;
            if (geography == null || fear == null || presence == null ||
                !geography.TryGetBusinessBlock(businessId, out blockId))
                return false;

            // What was done to HIM weighs heaviest: the shop's own memory of this family,
            // which already carries the street's share of every incident near it
            // (FEAR-007). Reading the block alone made a threat at the counter worth no
            // more to the man behind it than a rumour two doors down.
            var asking = fear.BusinessFear(blockId, businessId, gangId, lastGameHour);
            var standing = presence.TotalOf(blockId, gangId);
            var trouble = fear.BlockFear(blockId, lastGameHour);

            var strongestRival = 0f;
            fear.CollectGangs(blockId, lastGameHour, fearGangs);
            for (var i = 0; i < fearGangs.Count; i++)
            {
                if (fearGangs[i].GangId == gangId)
                    continue;
                strongestRival = Math.Max(strongestRival, Standing(blockId, fearGangs[i].GangId));
            }

            presence.CollectGangs(blockId, presenceGangs);
            for (var i = 0; i < presenceGangs.Count; i++)
            {
                if (presenceGangs[i].GangId == gangId)
                    continue;
                strongestRival = Math.Max(strongestRival, Standing(blockId, presenceGangs[i].GangId));
            }

            var protectorStanding = 0f;
            var protectedByAsker = false;
            if (racket != null && racket.TryGetProtector(businessId, out var protector))
            {
                protectedByAsker = protector == gangId;
                if (!protectedByAsker)
                    protectorStanding = Standing(blockId, protector);
            }

            inputs = new TerritoryComplianceInputs(
                asking, standing, trouble, strongestRival, protectorStanding, protectedByAsker);
            return true;
        }

        /// <summary>What one family is worth on a block: what it is feared for and what it
        /// has standing there, in one number, because an owner does not weigh them apart.</summary>
        float Standing(TerritoryBlockId blockId, TerritoryGangId gangId) =>
            0.5f * fear.FearOf(blockId, gangId, lastGameHour) +
            0.5f * presence.TotalOf(blockId, gangId);

        /// <summary>
        /// What the owner would say if he were asked right now, and out of what. Reads
        /// only - the inspector and the audit explain a standing verdict with it.
        /// </summary>
        public bool TryExplainDemand(
            TerritoryBusinessId businessId,
            TerritoryGangId gangId,
            out TerritoryComplianceTerms terms)
        {
            terms = default;
            if (racket == null || !TryComplianceInputs(businessId, gangId, out var inputs, out _))
                return false;
            DemandShifts(businessId, out var ownerShift, out var tierBar);
            terms = TerritoryComplianceEvaluation.Evaluate(
                inputs, racket.Config, ownerShift, tierBar);
            return true;
        }

        /// <summary>
        /// The demand, resolved. This is the authoritative path: the command executor and
        /// the rival driver both come through here, so a rival leans on a shop by exactly
        /// the rules the player does.
        /// </summary>
        public bool ResolveDemand(
            TerritoryGangId gangId,
            TerritoryBusinessId businessId,
            out TerritoryComplianceVerdict verdict,
            out TerritoryComplianceTerms terms)
        {
            verdict = TerritoryComplianceVerdict.Refuse;
            terms = default;
            if (racket == null || !IsRacketable(businessId) ||
                !TryComplianceInputs(businessId, gangId, out var inputs, out var blockId))
                return false;

            racketChanges.Clear();
            // The owner himself and the tier guard shift the thresholds (ECON-002/007):
            // a cowardly barber folds early, a casino wants near everything a family
            // can be. Neutral shifts leave the pre-economy answer untouched.
            DemandShifts(businessId, out var ownerShift, out var tierBar);
            verdict = racket.Demand(
                businessId, gangId, inputs, lastGameHour, out terms, racketChanges,
                ownerShift, tierBar);
            PublishRacket(blockId);

            // A refusal starts the clock the street judges the family by (FEAR-010).
            if (verdict == TerritoryComplianceVerdict.Refuse)
                fear?.OpenDefiance(gangId, blockId, businessId, lastGameHour);
            else
                fear?.AnswerDefiance(gangId, businessId);
            return true;
        }

        /// <summary>
        /// The threat, resolved: the Fear it causes is filed as an act, the shop is marked
        /// as freshly leaned on, and then the owner is asked again - because that is the
        /// whole point of leaning on him.
        /// </summary>
        public bool ResolveThreat(
            TerritoryGangId gangId,
            TerritoryBusinessId businessId,
            TerritoryCharacterId actorId,
            out TerritoryComplianceVerdict verdict,
            out TerritoryComplianceTerms terms)
        {
            verdict = TerritoryComplianceVerdict.Refuse;
            terms = default;
            if (racket == null || !IsRacketable(businessId))
                return false;

            RecordResolvedThreat(
                gangId, businessId, racket.Config.ThreatSeverity,
                TerritoryFearVisibility.Seen, actorId);
            // A Connected owner turns heat on the family leaning on him (ECON-002),
            // and the man who did the leaning starts to have a name here (ECON-006).
            NoteConnectedHeat(businessId);
            NoteReputationAt(businessId, actorId, 3f);

            racketChanges.Clear();
            racket.Threaten(businessId, gangId, lastGameHour, racketChanges);
            if (geography != null && geography.TryGetBusinessBlock(businessId, out var threatBlock))
                PublishRacket(threatBlock);

            return ResolveDemand(gangId, businessId, out verdict, out terms);
        }

        /// <summary>
        /// Violence landed on a business and a physical system resolved it. The escalation
        /// is recorded against the relationship, the matching act is filed as Fear, and it
        /// counts as the family having answered an earlier refusal.
        /// </summary>
        public bool ResolveEscalation(
            TerritoryGangId gangId,
            TerritoryBusinessId businessId,
            TerritoryEscalationKind kind,
            float severity = 1f,
            TerritoryFearVisibility visibility = TerritoryFearVisibility.Public)
        {
            if (racket == null || fear == null || geography == null || !gangId.IsValid ||
                !IsRacketable(businessId) ||
                !geography.TryGetBusinessBlock(businessId, out var blockId))
                return false;

            racketChanges.Clear();
            racket.Escalate(businessId, gangId, kind, lastGameHour, racketChanges);
            NoteConnectedHeat(businessId);
            RecordFear(new TerritoryFearEvent(
                gangId,
                blockId,
                kind == TerritoryEscalationKind.Assault
                    ? TerritoryFearCategory.Assault
                    : TerritoryFearCategory.PropertyDamage,
                severity,
                visibility,
                lastGameHour,
                businessId));
            fear.AnswerDefiance(gangId, businessId);
            PublishRacket(blockId);
            return true;
        }

        /// <summary>
        /// The seam the street's own violence reports through: a blast, a beating, a window
        /// put in at a place that turns out to be somebody's shop. The position is resolved
        /// to a business rather than the caller having to know about the racket at all.
        /// </summary>
        public static void ReportViolenceAt(
            Vector3 world, int faction, TerritoryEscalationKind kind, float radius = 10f)
        {
            var runtime = Instance;
            if (runtime == null || faction < 0 ||
                !runtime.TryGetBusinessNear(world, radius, out var businessId))
                return;
            runtime.ResolveEscalation(new TerritoryGangId(faction), businessId, kind);
        }

        /// <summary>The nearest business door to a point, within reach.</summary>
        public bool TryGetBusinessNear(
            Vector3 world, float radius, out TerritoryBusinessId businessId)
        {
            businessId = default;
            if (geography == null || !TryGetBlockForAct(world, out var blockId))
                return false;

            var here = geography.BusinessesOf(blockId);
            var best = radius * radius;
            for (var i = 0; i < here.Count; i++)
            {
                if (!TryGetBusinessApproach(here[i].BusinessId, out var door))
                    continue;
                var distance = (door - world).sqrMagnitude;
                if (distance > best)
                    continue;
                best = distance;
                businessId = here[i].BusinessId;
            }

            return businessId.IsValid;
        }

        /// <summary>
        /// Whether a place can carry a racket at all. One test, in one place: a civic
        /// building and a block prefab that never had a business record are not shops, and
        /// no relationship is ever created for them.
        /// </summary>
        public bool IsRacketable(TerritoryBusinessId businessId)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (!businessId.IsValid || business == null || !business.Populated)
                return false;
            if (!business.Directory.TryGet(businessId, out _))
                return false;
            return !business.TryGetSite(businessId, out var site) || site == null || site.Eligible;
        }

        void PublishRacket(TerritoryBlockId blockId)
        {
            for (var i = 0; i < racketChanges.Count; i++)
            {
                var change = racketChanges[i];
                // The event carries how much the shop COMPLIES, not which state it is in:
                // paying is one, wavering is the configured fraction of a yes, everything
                // else is nothing. A listener that wants the state asks the ledger.
                events.Publish(new BusinessComplianceChanged(
                    blockId,
                    change.BusinessId,
                    change.GangId,
                    ComplianceValue(change.Previous),
                    ComplianceValue(change.Current),
                    change.GameHour));
            }

            racketChanges.Clear();
            WriteCompliance(blockId);
        }

        /// <summary>How much of a yes a standing is worth, on the same scale the block's
        /// compliance is counted in.</summary>
        float ComplianceValue(TerritoryProtectionState state)
        {
            switch (state)
            {
                case TerritoryProtectionState.Compliant: return 1f;
                case TerritoryProtectionState.Hesitant:
                case TerritoryProtectionState.Intimidated:
                    return racket?.Config.HesitantComplianceShare ?? 0f;
                default: return 0f;
            }
        }

        /// <summary>
        /// What the street's shops add up to, written into the block's signals. Compliance
        /// is an INPUT to the control reading, never a claim on the block itself.
        /// </summary>
        void WriteCompliance(TerritoryBlockId blockId)
        {
            if (state == null || racket == null || geography == null || !blockId.IsValid)
                return;

            var here = geography.BusinessesOf(blockId);
            blockBusinessScratch.Clear();
            for (var i = 0; i < here.Count; i++)
                blockBusinessScratch.Add(here[i].BusinessId);

            racket.Compliance(blockBusinessScratch, out var compliant, out var total, out var share);
            var current = state.SignalsOf(blockId);
            if (current.CompliantBusinesses == compliant && current.TotalBusinesses == total &&
                Math.Abs((current.BusinessCompliance ?? 0f) - share) < 0.01f)
                return;

            state.SetSignals(blockId, new TerritoryBlockSignals(
                current.LocalFear,
                total > 0 ? share : (float?)null,
                compliant,
                total,
                current.Control,
                current.LeadingGangId,
                current.Gangs));
        }

        /// <summary>
        /// The business channel: what every street's shops now add up to, whether any of
        /// them has been leant on hard enough to change hands, and the rival families
        /// leaning on the shops where they stand.
        /// </summary>
        void SettleBusinesses(double gameHour)
        {
            SweepDefiance(gameHour);
            SweepProtectionSwitches();
            DriveRivalDemands();
            AccrueDues(gameHour);
            WatchRounds(gameHour);
        }

        /// <summary>
        /// A shop changes hands when a challenger has been worth more than the family
        /// being paid for several ticks running. One loud afternoon is not enough, and the
        /// block itself does not change hands with the shop.
        /// </summary>
        void SweepProtectionSwitches()
        {
            if (racket == null || geography == null)
                return;

            var ids = racket.Businesses;
            for (var i = ids.Count - 1; i >= 0; i--)
            {
                var businessId = ids[i];
                if (!racket.TryGetProtector(businessId, out var protector))
                    continue;
                if (!geography.TryGetBusinessBlock(businessId, out var blockId))
                    continue;

                var incumbent = Standing(blockId, protector);
                var challenger = default(TerritoryGangId);
                var best = 0f;
                presence.CollectGangs(blockId, presenceGangs);
                for (var g = 0; g < presenceGangs.Count; g++)
                {
                    var gangId = presenceGangs[g].GangId;
                    if (gangId == protector)
                        continue;
                    var worth = Standing(blockId, gangId);
                    if (worth <= best)
                        continue;
                    best = worth;
                    challenger = gangId;
                }

                var ahead = challenger.IsValid && best - incumbent >= racket.Config.SwitchMargin;
                var ticks = racket.PressTowardSwitch(businessId, challenger, ahead);
                if (!ahead || ticks < racket.Config.SwitchTicks)
                    continue;

                racketChanges.Clear();
                if (racket.Switch(businessId, challenger, lastGameHour, racketChanges))
                {
                    racket.PressTowardSwitch(businessId, challenger, false);
                    PublishRacket(blockId);
                }
            }
        }

        /// <summary>
        /// Families lean on the shops where they stand. No planner and no schedule: a
        /// family with men on a street tries the shops on it, through the same demand the
        /// player uses, and the owner answers by the same rules.
        /// </summary>
        void DriveRivalDemands()
        {
            if (racket == null || presence == null || geography == null ||
                racket.Config.RivalDemandsPerTick <= 0)
                return;

            var made = 0;
            var blocks = presence.Blocks;
            for (var i = 0; i < blocks.Count && made < racket.Config.RivalDemandsPerTick; i++)
            {
                // Start where the last tick left off, so one busy block does not soak up
                // every attempt the city ever makes.
                var blockId = blocks[(i + rivalDemandCursor) % blocks.Count];
                var here = geography.BusinessesOf(blockId);
                if (here.Count == 0)
                    continue;

                presence.CollectGangs(blockId, presenceGangs);
                for (var g = 0; g < presenceGangs.Count && made < racket.Config.RivalDemandsPerTick; g++)
                {
                    var gangId = presenceGangs[g].GangId;
                    // RIVAL demands only. The player's family asks when the player says
                    // so, through the command gateway and a man at the door - the sim
                    // must never open a defiance clock in his name off mere presence.
                    if (gangId.Value == GangCatalog.PlayerGangId)
                        continue;
                    if (presenceGangs[g].Total < racket.Config.RivalDemandPresence)
                        continue;

                    for (var b = 0; b < here.Count; b++)
                    {
                        var businessId = here[b].BusinessId;
                        var standing = racket.StateOf(businessId, gangId);
                        if (standing == TerritoryProtectionState.Compliant ||
                            standing == TerritoryProtectionState.Defiant)
                            continue;
                        if (!ResolveDemand(businessId: businessId, gangId: gangId,
                                verdict: out _, terms: out _))
                            continue;
                        made++;
                        break;
                    }
                }
            }

            rivalDemandCursor++;
        }

        /// <summary>Men who were sent to a door and have got there.</summary>
        void NoteApproachArrival(
            DemoCrews.Unit unit, CrewWalker actor,
            TerritoryActorObservation observation, double gameHour)
        {
            if (racket == null || pendingApproaches.Count == 0 || actor?.Tf == null)
                return;
            // Doorstep errands are the player's; a rival unit that happens to share a
            // crew number must not spring one walking past the door.
            if (unit.Faction != 0)
                return;

            for (var i = pendingApproaches.Count - 1; i >= 0; i--)
            {
                var pending = pendingApproaches[i];
                if (pending.CrewId != unit.CrewId)
                    continue;
                if ((actor.Tf.position - pending.Door).sqrMagnitude >
                    approachRadiusMetres * approachRadiusMetres)
                    continue;

                pendingApproaches.RemoveAt(i);
                racketChanges.Clear();
                racket.Approach(pending.BusinessId, observation.GangId, gameHour, racketChanges);
                if (geography != null &&
                    geography.TryGetBusinessBlock(pending.BusinessId, out var blockId))
                    PublishRacket(blockId);

                // The walk carried an intent: the men are at the door now, so the demand
                // or the threat happens HERE, by the same resolution a standing man's
                // click uses. One order from range, not a walk and a second click - and
                // the man actually STEPS INSIDE for the conversation (DoorBeat).
                if (pending.FollowUp == TerritoryRacketIntent.Demand)
                {
                    if (ResolveDemand(
                            observation.GangId, pending.BusinessId, out var verdict, out _))
                        AnnounceVerdict(pending.BusinessId, threat: false, verdict,
                            observation.CharacterId);
                    DoorBeat.VisitBusiness(actor, pending.BusinessId, pending.Door);
                }
                else if (pending.FollowUp == TerritoryRacketIntent.Threaten)
                {
                    if (ResolveThreat(observation.GangId, pending.BusinessId,
                            observation.CharacterId, out var verdict, out _))
                        AnnounceVerdict(pending.BusinessId, threat: true, verdict,
                            observation.CharacterId);
                    DoorBeat.VisitBusiness(actor, pending.BusinessId, pending.Door);
                }
            }
        }

        /// <summary>What the owner said, put over the street - the demand and the threat
        /// used to resolve in silence, and a player who ordered one watched nothing
        /// happen. Only the player's own conversations come through here; a rival's
        /// demand is his business.</summary>
        void AnnounceVerdict(
            TerritoryBusinessId businessId, bool threat, TerritoryComplianceVerdict verdict,
            TerritoryCharacterId actorId = default)
        {
            // XP-003. Every doorstep lean comes through here - the walked-in one and the
            // one a standing man is clicked into - so this is the one place the man who
            // did the leaning banks what it taught him. An owner who folded is the job
            // done; one who only wavered or refused is the half of it the table already
            // has a word for.
            if (actorId.IsValid)
                CrewSkill.Leaned(actorId.Value,
                    verdict == TerritoryComplianceVerdict.Accept);

            var name = businessId.Value;
            if (TryGetBusinessView(businessId, out var view))
                name = view.BusinessName;
            name = name.ToUpperInvariant();

            switch (verdict)
            {
                case TerritoryComplianceVerdict.Accept:
                    CrewOverlay.Announce(name + (threat ? " FOLDED - THEY PAY" : " AGREED - THEY PAY"),
                        4f, new Color(0.75f, 0.95f, 0.7f));
                    break;
                case TerritoryComplianceVerdict.Hesitate:
                    CrewOverlay.Announce("THE OWNER OF " + name + " IS WAVERING",
                        4f, new Color(1f, 0.85f, 0.55f));
                    break;
                default:
                    CrewOverlay.Announce("THE OWNER OF " + name + " REFUSED" + (threat ? " AGAIN" : ""),
                        4f, new Color(1f, 0.6f, 0.45f));
                    break;
            }
        }

        /// <summary>The body on the street for one of the outfit's characters, if he is
        /// standing on it - the direct demand wants the man who spoke to step inside.</summary>
        CrewWalker FindWalker(TerritoryCharacterId actorId)
        {
            if (!actorId.IsValid || crews == null)
                return null;
            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.Faction != 0)
                    continue;
                foreach (var man in unit.All())
                    if (man != null && !man.Dead && man.CharacterId == actorId.Value)
                        return man;
            }

            return null;
        }

        /// <summary>How long a doorstep errand may go without getting any nearer its
        /// door before the walk is sent out again.</summary>
        const float ApproachStallSeconds = 20f;

        /// <summary>How much closer counts as still walking. Below this the crew is
        /// shuffling on the spot, not covering ground.</summary>
        const float ApproachProgressMetres = 1f;

        /// <summary>How many times a stalled walk is sent out again before the men are
        /// simply put down at the door.</summary>
        const int ApproachAttempts = 3;

        /// <summary>How far each re-issue may look for a spot beside the door, per go.</summary>
        const float ApproachReachMetres = 6f;


        /// <summary>THE MEN ALWAYS GET THERE. An order the player gave is a thing the game
        /// owes him: it may take a while, it may need the walk sending out again, but it
        /// does not quietly die in this list and it does not come back as an apology on
        /// the banner. So this watches one thing - whether the crew is still CLOSING on
        /// its door - and when it stops closing it does something about it, on a ladder:
        ///
        ///   1-3   send the walk out again, each go with more room to find a standing
        ///         spot than the last (MarchTo lifts a man who is stuck in a wall on its
        ///         way through, which is what most stalls turn out to be).
        ///   4     the ground round that door has beaten the lattice three times over:
        ///         put the men down at it. A hard placement is what the game already
        ///         does for a man wedged in geometry, and an order carried out beats an
        ///         order refused.
        ///
        /// The errand only ever leaves this list by being ARRIVED at, by the crew being
        /// retasked, or by the crew ceasing to exist.</summary>
        void TendApproaches()
        {
            if (pendingApproaches.Count == 0 || crews == null)
                return;

            var now = Time.time;
            for (var i = pendingApproaches.Count - 1; i >= 0; i--)
            {
                var pending = pendingApproaches[i];
                var unit = PlayerUnitOfCrew(pending.CrewId);
                if (unit == null)
                {
                    // No crew left to walk it - the men are wiped or gone off the street.
                    pendingApproaches.RemoveAt(i);
                    continue;
                }

                var gap = Vector3.Distance(unit.Position, pending.Door);
                if (gap < pending.Nearest - ApproachProgressMetres)
                {
                    pendingApproaches[i] = pending.Closing(gap, now);
                    continue;
                }

                if (now - pending.NearestAt < ApproachStallSeconds)
                    continue;

                if (pending.Attempts < ApproachAttempts)
                {
                    // More room each time round: a doorway the first pass found no
                    // standing spot beside may have one a few metres further out.
                    var reach = ApproachReachMetres * (pending.Attempts + 1);
                    var spot = WalkObstacles.ClearSpot(
                        pending.Door, WalkObstacles.Radius, reach);
                    crews.MarchTo(unit, spot);
                    pendingApproaches[i] = pending.SentAgain(now);
                    continue;
                }

                SetDownAtDoor(unit, pending.Door);
                pendingApproaches[i] = pending.Closing(
                    Vector3.Distance(unit.Position, pending.Door), now);
            }
        }

        /// <summary>The last rung: the men are placed at the door they were sent to. Only
        /// ever reached when the walk has been tried and re-tried and the ground round
        /// that door will not take a crew - and the alternative is an order the player
        /// gave that the game never carries out.</summary>
        void SetDownAtDoor(DemoCrews.Unit unit, Vector3 door)
        {
            // NEVER FURTHER OUT THAN THE ARRIVAL TEST ITSELF. A crew put down beyond
            // approachRadiusMetres has not arrived, so the next stall would put it down
            // again, and the one after that, for as long as the errand lived. A
            // placement has to land inside the radius that ENDS the errand or it is not
            // a placement, it is a loop.
            var reach = Mathf.Max(2f, approachRadiusMetres * 0.6f);
            var spot = WalkObstacles.ClearSpot(door, WalkObstacles.Radius, reach);
            var k = 0;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null)
                    continue;
                // a crew, not a column: the men land a stride apart round the doorstep
                var ring = k == 0
                    ? Vector3.zero
                    : Quaternion.Euler(0f, k * 120f, 0f) * new Vector3(0f, 0f, 1.4f);
                var at = WalkObstacles.ClearSpot(spot + ring, WalkObstacles.Radius);
                at.y = man.Tf.position.y;
                man.Tf.position = at;
                k++;
            }

            crews.MarchTo(unit, spot);
        }

        /// <summary>The outfit's crew that carries this crew number, if it is still on the
        /// street.</summary>
        DemoCrews.Unit PlayerUnitOfCrew(int crewId)
        {
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.IsPolice || unit.Faction != 0 || unit.Wiped)
                    continue;
                if (unit.CrewId == crewId)
                    return unit;
            }

            return null;
        }

        /// <summary>The crew's doorstep errand, dropped. Called whenever the crew is
        /// retasked - a pending approach must not outlive the order that made it. A
        /// collection round in hand is dropped the same way: the take it was carrying
        /// walks home in the men's pockets only if the round finishes.</summary>
        void DropPendingApproaches(int crewId)
        {
            for (var i = pendingApproaches.Count - 1; i >= 0; i--)
                if (pendingApproaches[i].CrewId == crewId)
                    pendingApproaches.RemoveAt(i);
            AbandonRound(crewId);
        }

        /// <summary>
        /// The door a crew is currently walking to, if it is walking to one. The street
        /// overlay reads it to keep a mark on that doorstep while they are on their way -
        /// the order is visible in the world, not only in the card that issued it.
        /// </summary>
        /// <summary>A direct street order countermands the crew's errand: the doorstep
        /// walk is dropped and a collection round in hand is lost with its take. The
        /// street overlay calls this for orders that never pass the command gateway -
        /// the gateway's own moves already do it.</summary>
        public void CallOffErrands(int crewId) => DropPendingApproaches(crewId);

        public bool TryGetPendingApproach(int crewId, out Vector3 door)
        {
            door = default;
            for (var i = 0; i < pendingApproaches.Count; i++)
            {
                if (pendingApproaches[i].CrewId != crewId)
                    continue;
                door = pendingApproaches[i].Door;
                return true;
            }

            return false;
        }

        /// <summary>What the player may read about a shop: words, and only the ones his
        /// own house could plausibly know.</summary>
        public bool TryGetBusinessView(
            TerritoryBusinessId businessId, out TerritoryBusinessPresentation view)
        {
            view = null;
            if (racket == null || geography == null ||
                !geography.TryGetBusinessBlock(businessId, out var blockId))
                return false;

            var playerGang = new TerritoryGangId(GangCatalog.PlayerGangId);
            var name = businessId.Value;
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.Populated &&
                business.Directory.TryGet(businessId, out var record))
                name = record.DisplayName;

            var protector = "";
            if (racket.TryGetProtector(businessId, out var protectorGang))
                protector = protectorGang == playerGang
                    ? "us"
                    : GangName(protectorGang);

            var situation = "Unknown";
            var tone = TerritoryOwnerTone.Unknown;
            if (player != null && player.TryGetBlock(blockId, out var blockView))
            {
                situation = blockView.LocalFear;
                tone = blockView.OwnerTone;
            }

            var blockName = geography.TryGetBlock(blockId, out var definition)
                ? definition.DisplayName
                : "";
            var trouble = fear != null && fear.BlockFear(blockId, lastGameHour) > 0.5f;

            // The dues meter, in words (ECON-008): what it pays, what it owes, when it
            // last paid. Only for a shop paying US - a rival's books are his own.
            var paysLine = "";
            if (TryGetDues(businessId, out var owed, out var lastPaid))
                paysLine = "pays $" + WeeklyRateOf(businessId) + " a week · owes $" + owed +
                           (lastPaid >= 0 ? " · last paid day " + lastPaid : " · never collected");

            view = new TerritoryBusinessPresentation(
                businessId,
                name,
                blockName,
                TerritoryStandingVocabulary.Default.Describe(
                    racket.StateOf(businessId, playerGang)),
                protector,
                situation,
                tone,
                trouble,
                paysLine);
            return true;
        }

        static string GangName(TerritoryGangId gangId)
        {
            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i] != null && gangs[i].Id == gangId.Value)
                    return gangs[i].Name;
            return "gang #" + gangId.Value;
        }

        readonly struct PendingApproach
        {
            public PendingApproach(
                int crewId, TerritoryBusinessId businessId, Vector3 door,
                TerritoryRacketIntent followUp)
                : this(crewId, businessId, door, followUp, float.MaxValue, Time.time, 0)
            {
            }

            PendingApproach(
                int crewId, TerritoryBusinessId businessId, Vector3 door,
                TerritoryRacketIntent followUp, float nearest, float nearestAt, int attempts)
            {
                CrewId = crewId;
                BusinessId = businessId;
                Door = door;
                FollowUp = followUp;
                Nearest = nearest;
                NearestAt = nearestAt;
                Attempts = attempts;
            }

            public int CrewId { get; }
            public TerritoryBusinessId BusinessId { get; }
            public Vector3 Door { get; }

            /// <summary>What the walk was for: the demand or the threat follows the
            /// arrival, so an order given from range is one order.</summary>
            public TerritoryRacketIntent FollowUp { get; }

            /// <summary>The closest the crew has ever been to that door, and when. A walk
            /// across the city is slow and perfectly legal; a walk that stops CLOSING is a
            /// walk that has failed, and this is the pair that tells them apart.</summary>
            public float Nearest { get; }
            public float NearestAt { get; }

            /// <summary>How many times the walk has been sent out again. Each go is
            /// given more room to find a spot than the last.</summary>
            public int Attempts { get; }

            public PendingApproach Closing(float gap, float at) =>
                new PendingApproach(CrewId, BusinessId, Door, FollowUp, gap, at, Attempts);

            public PendingApproach SentAgain(float at) =>
                new PendingApproach(CrewId, BusinessId, Door, FollowUp, Nearest, at, Attempts + 1);
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
                actor.IsLieutenant,
                RankOf(unit, actor),
                ActivityOf(unit, actor));

        /// <summary>
        /// What this body is, from real personnel identity. The roster holds the outfit's
        /// men and only theirs, so a rival's character id is not a roster id and would
        /// name the wrong man - a rival's rank is read off the street instead, from who
        /// is leading the crew. The RULE is the same for every family (PRES-008): only
        /// what is physically here counts, and command responsibility for the block
        /// counts for nothing.
        /// </summary>
        static TerritoryRank RankOf(DemoCrews.Unit unit, CrewWalker actor)
        {
            if (unit.Faction == GangCatalog.PlayerGangId)
            {
                var character = PersonnelDirector.Instance?.Roster?.Find(actor.CharacterId);
                if (character != null)
                {
                    switch (character.Rank)
                    {
                        case Rank.Boss: return TerritoryRank.Boss;
                        case Rank.Lieutenant: return TerritoryRank.Lieutenant;
                        default: return TerritoryRank.Hood;
                    }
                }
            }

            return actor.IsLieutenant ? TerritoryRank.Lieutenant : TerritoryRank.Hood;
        }

        /// <summary>
        /// What this body is doing here, read off states the street already keeps: a man
        /// in a saddle or a seat is passing through, a man running away is not holding
        /// anything, a man on his feet going somewhere counts for less than a man standing
        /// on the ground or fighting for it. Nothing new is invented and nothing is
        /// written back into the crew.
        /// </summary>
        static TerritoryActorActivity ActivityOf(DemoCrews.Unit unit, CrewWalker actor)
        {
            if (actor.Riding || unit.Car != null || unit.Boarding != null)
                return TerritoryActorActivity.Transit;

            switch (actor.State)
            {
                case CrewWalker.Mode.Fleeing:
                    return TerritoryActorActivity.Transit;
                case CrewWalker.Mode.Walking:
                case CrewWalker.Mode.Homing:
                case CrewWalker.Mode.Striding:
                    return TerritoryActorActivity.Moving;
                default:
                    return TerritoryActorActivity.Stationed;
            }
        }

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

            // A crew sent somewhere else is off its doorstep errand: without this the
            // pending approach sat armed for hours and fired the moment the crew
            // happened to walk past that door on other business.
            if (issued)
                DropPendingApproaches(unit.CrewId);

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
            if (!crews.MarchTo(unit, destination))
                return TerritoryCommandExecution.Reject("The physical crew refused the order.");

            DropPendingApproaches(unit.CrewId);
            return TerritoryCommandExecution.Pending(
                "The group is moving into the block; operation success is unresolved.");
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
            if (!TryGetBusinessApproach(command.BusinessId, out var door))
                return TerritoryCommandExecution.Reject("No such business in this city.");
            if (!IsRacketable(command.BusinessId))
                return TerritoryCommandExecution.Reject("That place carries no business.");

            if (!crews.MarchTo(unit, door))
                return TerritoryCommandExecution.Reject("The physical crew refused the order.");

            // Intent only. The interaction begins when the men are actually at the door -
            // the presence sampling notices that, not this command.
            DropPendingApproaches(unit.CrewId);
            pendingApproaches.Add(new PendingApproach(
                unit.CrewId, command.BusinessId, door, command.FollowUp));

            return TerritoryCommandExecution.Pending(
                command.FollowUp == TerritoryRacketIntent.Approach
                    ? "The group is approaching; the business state is unchanged."
                    : "The group is on its way; the owner is asked when they arrive.");
        }

        /// <summary>
        /// The demand. The UI submits the intent; the state moves only because a man of
        /// that family was standing at the door when it was asked, and only by the answer
        /// the owner actually gave.
        /// </summary>
        public TerritoryCommandExecution Execute(DemandProtectionCommand command)
        {
            if (!TryResolveInteraction(command.ActorId, command.BusinessId,
                    out var gangId, out var refusal))
                return TerritoryCommandExecution.Reject(refusal);

            if (!ResolveDemand(gangId, command.BusinessId, out var verdict, out _))
                return TerritoryCommandExecution.Reject("The demand could not be resolved.");

            // The same beat and the same banner the walked-in demand gets: the man at
            // the door steps inside, and what the owner said is put over the street.
            AnnounceVerdict(command.BusinessId, threat: false, verdict, command.ActorId);
            if (TryGetBusinessApproach(command.BusinessId, out var door))
                DoorBeat.VisitBusiness(
                    FindWalker(command.ActorId), command.BusinessId, door);

            switch (verdict)
            {
                case TerritoryComplianceVerdict.Accept:
                    return TerritoryCommandExecution.Succeed();
                case TerritoryComplianceVerdict.Hesitate:
                    return TerritoryCommandExecution.Pending("The owner is wavering.");
                default:
                    return TerritoryCommandExecution.Fail("The owner refused.");
            }
        }

        /// <summary>
        /// Leaning on the owner. The threat is a Fear act first and a question second: it
        /// is filed, and then the owner is asked again.
        /// </summary>
        public TerritoryCommandExecution Execute(ThreatenBusinessOwnerCommand command)
        {
            if (!TryResolveInteraction(command.ActorId, command.BusinessId,
                    out var gangId, out var refusal))
                return TerritoryCommandExecution.Reject(refusal);

            if (!ResolveThreat(gangId, command.BusinessId, command.ActorId,
                    out var verdict, out _))
                return TerritoryCommandExecution.Reject("The threat could not be resolved.");

            AnnounceVerdict(command.BusinessId, threat: true, verdict, command.ActorId);
            if (TryGetBusinessApproach(command.BusinessId, out var door))
                DoorBeat.VisitBusiness(
                    FindWalker(command.ActorId), command.BusinessId, door);

            switch (verdict)
            {
                case TerritoryComplianceVerdict.Accept:
                    return TerritoryCommandExecution.Succeed();
                case TerritoryComplianceVerdict.Hesitate:
                    return TerritoryCommandExecution.Pending("The owner is still wavering.");
                default:
                    return TerritoryCommandExecution.Fail("The owner refused again.");
            }
        }

        /// <summary>The two questions every racket interaction asks: whose man is this,
        /// and is he standing at the door of a real business.</summary>
        bool TryResolveInteraction(
            TerritoryCharacterId actorId,
            TerritoryBusinessId businessId,
            out TerritoryGangId gangId,
            out string refusal)
        {
            gangId = default;
            refusal = "";
            if (racket == null)
            {
                refusal = "The racket is not running in this scene.";
                return false;
            }

            if (!IsRacketable(businessId))
            {
                refusal = "That place carries no business.";
                return false;
            }

            if (!TryGetActorGang(actorId, out gangId))
            {
                refusal = "That man is not on the street.";
                return false;
            }

            if (!TryGetBusinessApproach(businessId, out var door))
            {
                refusal = "No such business in this city.";
                return false;
            }

            if (!HasManAt(gangId, door, racket.Config.ApproachRadiusMetres))
            {
                refusal = "Nobody of that house is standing at the door.";
                return false;
            }

            return true;
        }

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
            StreetAlarm.OnShot -= OnStreetShot;
            StreetAlarm.OnDeath -= OnStreetDeath;
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
