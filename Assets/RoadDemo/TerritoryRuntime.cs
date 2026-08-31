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

        public ITerritoryTruthQuery DebugTruth => truth;

        /// <summary>Who is really standing on which block, family by family. Simulation
        /// writes it on the scheduler's ticks; the inspector and the audit only read it.</summary>
        public TerritoryPresenceLedger Presence => presence;

        /// <summary>What each street is afraid of, and how hard the law is looking at it.
        /// Written only by the Fear channel and the street's own violence.</summary>
        public TerritoryFearLedger Fear => fear;

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
        public int ObservationVersion { get; private set; }

        /// <summary>Men standing on road space that belongs to no block at this tick -
        /// the middle of a boulevard, the freeway, the ground between quarters. Reported
        /// (the geography overlay prints it) rather than smoothed away.</summary>
        public int BlocklessActors { get; private set; }

        /// <summary>Does anybody stand on this block as of the last Presence tick? Read
        /// off the sampling that already ran, so a view can ask it of every block on
        /// screen without walking every crew again for each one.</summary>
        public bool Occupied(TerritoryBlockId blockId) => occupiedBlocks.Contains(blockId);

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
                defianceWindowHours: defianceWindowHours,
                policeAttentionCap: policeAttentionCap,
                policeAttentionHalfLifeHours: policeAttentionHalfLifeHours,
                policeEscalation: policeEscalation,
                presenceFloor: presenceFloorUnderHeat));
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
                SweepDefiance(tick.GameHour);
            else if (tick.Channel == TerritoryTickChannel.DerivedControl)
                DeriveControl();
        }

        /// <summary>
        /// Publishes who holds each block, read off the deeds standing on it. This is the
        /// DerivedControl channel's whole job: the arithmetic is pure
        /// (TerritoryControlDerivation) and the only thing written back is the block's
        /// control and each family's standing - fear and business compliance are carried
        /// forward untouched, because they belong to their own tickets.
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

            // Every block is read, not just the ones with deeds: a block whose last
            // premise changed hands has to stop saying it is held. The change guard is
            // what keeps that from bumping the state version of the whole city every
            // quarter hour - after the first pass almost nothing is written.
            var ids = state.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var blockId = ids[i];
                controlTallies.TryGetValue(blockId, out var tally);
                var current = state.SignalsOf(blockId);
                var next = TerritoryControlDerivation.Signals(tally, current, controlScratch);
                if (!TerritoryControlDerivation.Same(current, next))
                    state.SetSignals(blockId, next);
            }
        }

        void SampleActorBlocks(double gameHour, double cadenceHours)
        {
            sampledLocations.Clear();
            presence?.BeginSample();
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

                // The same pass that reports who crossed a kerb is the pass that counts
                // Presence: one walk over the city's bodies, one truth out of it. A block
                // the police are watching is worth less to stand on (FEAR-013).
                presence?.Contribute(
                    blockId, observation,
                    fear == null ? 1f : fear.PresenceScale(blockId, gameHour));
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
            fearDirty.Add(value.BlockId);
            events.Publish(new FearEventRecorded(
                value.BlockId, value.GangId, value.SourceActorId, impact, value.GameHour));
            return impact;
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
