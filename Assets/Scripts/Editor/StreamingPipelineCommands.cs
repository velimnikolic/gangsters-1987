using System.Linq;
using LivingCity.Gameplay;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Tests;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace GangstersTools
{
    public static class StreamingPipelineCommands
    {
        sealed class Point3
        {
            public float x, y, z;
            public Point3(Vector3 value) { x = value.x; y = value.y; z = value.z; }
        }

        static Point3 Point(Vector3 value) => new Point3(value);

        [CliCommand("gangsters_streaming_stress",
                    "Pan CoreDemo through a deterministic route at the same speed as held WASD.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object Stress(
            [CliArg("turf", "Open the full TurfMap at its canonical Core pivot before measuring.")]
            bool turf = false)
        {
            if (!Application.isPlaying) return new { started = false, reason = "Play Mode is not running." };
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            if (rig == null) return new { started = false, reason = "No DemoCamera is live." };
            var prior = Object.FindAnyObjectByType<DemoCameraStreamingStress>();
            if (prior != null) Object.Destroy(prior.gameObject);
            if (turf)
            {
                rig.pivot = new Vector3(482.5f, rig.pivot.y, 885f);
                rig.distance = Mathf.Min(rig.mapCeiling, rig.mapAt + 30f);
            }
            var go = new GameObject("Streaming WASD stress (temporary)");
            var run = go.AddComponent<DemoCameraStreamingStress>();
            run.Begin(rig);
            return new { started = true, metresPerSecond = rig.distance * 0.55f };
        }

        [CliCommand("gangsters_streaming_snapshot",
                    "Report live recycler/map/cutaway state without running generator tests.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object Snapshot()
        {
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            var minimap = Object.FindAnyObjectByType<TurfMinimap>();
            var cutaway = Object.FindAnyObjectByType<StreetCutaway>();
            var turfHud = Object.FindAnyObjectByType<TurfMapHud>();
            var stress = Object.FindAnyObjectByType<DemoCameraStreamingStress>();
            var buildingLayer = Object.FindObjectsByType<TurfMapBuildingLayer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            var recyclers = Object.FindObjectsByType<CityBlockRecycler>()
                .Select(one => new
                {
                    one.RecipeCount,
                    one.ActiveViews,
                    one.CachedViews,
                    one.PooledHolders,
                    one.PendingViews,
                    one.ComposingViews,
                    one.AttachingViews,
                    one.PendingRendererAttachments,
                    one.SourceObjects,
                    one.SourceRenderers,
                    one.BuiltViews,
                    one.EvictedViews,
                    one.LastBuildMs,
                    one.WorstBuildMs,
                    one.LastBuildStepMs,
                    one.WorstBuildStepMs,
                    one.PrefabPoolCapacity,
                    one.AvailablePrefabParts,
                    one.PendingPrewarmParts,
                    one.PendingPoolRetirements,
                    one.PendingAssetWarm,
                    one.ReusedPrefabParts,
                    one.RuntimePrefabMisses,
                    one.RuntimePrefabMissTypes,
                    one.LargestRuntimeMissPrefab,
                    one.LargestRuntimeMissRenderers,
                    one.VisibleFallbackBlocks,
                }).ToArray();
            return new
            {
                playing = Application.isPlaying,
                frame = Time.frameCount,
                unscaledFrameMs = Time.unscaledDeltaTime * 1000f,
                smoothFrameMs = Time.smoothDeltaTime * 1000f,
                distance = rig != null ? rig.distance : (float?)null,
                pivot = rig != null ? Point(rig.pivot) : null,
                mapOut = rig != null ? rig.MapOut : (bool?)null,
                mapOpen = TurfMapHud.IsOpen,
                stressRunning = stress != null,
                stressFrames = stress != null ? stress.Frames : 0,
                stressElapsedSeconds = stress != null ? stress.ElapsedSeconds : 0f,
                stressWorstFrameMs = stress != null ? stress.WorstFrameMs : 0f,
                lastStress = DemoCameraStreamingStress.Last,
                minimapDraws = minimap != null ? minimap.Draws : 0,
                minimapUploads = minimap != null ? minimap.Uploads : 0,
                minimapLastDrawMs = minimap != null ? minimap.LastDrawMs : 0,
                mapBuildingMasses = buildingLayer != null ? buildingLayer.TotalMasses : 0,
                mapVisibleBuildingMasses = buildingLayer != null ? buildingLayer.VisibleMasses : 0,
                mapBuildingChunks = buildingLayer != null ? buildingLayer.VisibleChunks : 0,
                mapBuildingViewRebuilds = buildingLayer != null ? buildingLayer.ViewRebuilds : 0,
                mapBuildingTiles = buildingLayer != null ? buildingLayer.TotalTiles : 0,
                mapBuildingPooledTiles = buildingLayer != null ? buildingLayer.PooledTiles : 0,
                mapBuildingTileRebinds = buildingLayer != null ? buildingLayer.TileRebinds : 0,
                mapBuildingMeshBuilds = buildingLayer != null ? buildingLayer.MeshBuilds : 0,
                turfStaticUploads = turfHud != null ? turfHud.StaticUploads : 0,
                turfLastPublishMs = turfHud != null ? turfHud.LastPublishMs : 0f,
                turfWorstPublishMs = turfHud != null ? turfHud.WorstPublishMs : 0f,
                cutawayHidden = cutaway != null ? cutaway.HiddenBuildings : 0,
                cutawayColliderCache = cutaway != null ? cutaway.CachedColliderAnswers : 0,
                recyclers,
            };
        }

        [CliCommand("gangsters_core_runtime_audit",
                    "Report live Outfit anchors, ledger vehicles, pump parcel and gameplay state.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object CoreRuntimeAudit()
        {
            var crews = Object.FindAnyObjectByType<DemoCrews>();
            var front = DemoCrews.PlayerFront();
            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var pump = GameObject.Find("Core Filling Station 01 (PumpDemo)");
            var monkey = Object.FindAnyObjectByType<MonkeyRunner>();

            var outfitUnits = crews != null
                ? crews.Units.Where(unit => unit != null && unit.Faction == 0).Select(unit => new
                {
                    unit.CrewId,
                    unit.Name,
                    position = Point(unit.Position),
                    distanceToFront = front != null
                        ? Vector3.Distance(unit.Position, front.Outside) : -1f,
                    bossHasOrder = unit.Boss != null && unit.Boss.HasOrder,
                    boarding = unit.Boarding != null,
                    inCar = unit.Car != null,
                    target = unit.TargetUnit != null ? unit.TargetUnit.GangName : null,
                    standing = unit.Standing(),
                }).ToArray()
                : System.Array.Empty<object>();

            var cars = crews != null
                ? crews.Cars.Where(car => car != null && car.ItemId >= 0).Select(car => new
                {
                    car.ItemId,
                    car.DisplayName,
                    position = Point(car.Position),
                    distanceToFront = front != null
                        ? Vector3.Distance(car.Position, front.Outside) : -1f,
                    ownerCrew = car.Owner != null ? car.Owner.CrewId : -1,
                    occupantCrew = car.Occupant != null ? car.Occupant.CrewId : -1,
                    aboard = car.Aboard.Count,
                    state = car.State.ToString(),
                    car.Speed,
                }).ToArray()
                : System.Array.Empty<object>();

            var ledgerVehicles = roster != null
                ? roster.Equipment.Where(item => item.Kind == EquipmentKind.Vehicle).Select(item => new
                {
                    item.Id,
                    item.DisplayName,
                    item.OwnerId,
                    item.HolderId,
                    spawned = crews != null && crews.Cars.Any(car => car != null && car.ItemId == item.Id),
                }).ToArray()
                : System.Array.Empty<object>();

            return new
            {
                playing = Application.isPlaying,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                playerFront = front != null ? new
                {
                    front.GangName,
                    door = Point(front.Door),
                    outside = Point(front.Outside),
                    exactEntry = front.EntryLink != null,
                } : null,
                outfitUnits,
                ledgerVehicles,
                cars,
                pump = pump != null ? new
                {
                    found = true,
                    urbanCourt = pump.transform.Find("Urban Forecourt") != null,
                    generatedPavement = pump.transform.Find("Generated Station Pavement") != null,
                    oldRoadSlab = pump.transform.Find("Compact Forecourt") != null,
                    childCount = pump.transform.childCount,
                } : null,
                activeCutawayFootprint = GameObject.Find("Cutaway footprint") != null,
                monkey = monkey != null ? new
                {
                    monkey.Orders,
                    monkey.Wars,
                    monkey.FootFights,
                    monkey.DriveBys,
                    monkey.MotoPasses,
                    monkey.Marches,
                    monkey.Deaths,
                    monkey.Faults,
                } : null,
            };
        }

        [CliCommand("gangsters_buy_vehicle",
                    "Buy one armory vehicle through the same account and roster gates as the Ledger.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object BuyVehicle(
            [CliArg("name", "Armory display name.")] string name = "Armoured Wagon")
        {
            if (!Application.isPlaying)
                return new { bought = false, reason = "Play Mode is not running." };
            var director = PersonnelDirector.Instance;
            var outfit = OutfitDirector.Instance;
            if (director == null || director.Roster == null || outfit == null)
                return new { bought = false, reason = "The live ledger is not ready." };
            var listing = ArmoryCatalog.Vehicles.FirstOrDefault(item => item.DisplayName == name);
            if (string.IsNullOrEmpty(listing.DisplayName))
                return new { bought = false, reason = "No such vehicle listing." };

            var paid = outfit.Purchase(listing.Price, listing.DisplayName);
            if (!paid.Ok)
                return new { bought = false, reason = paid.Reason, safe = outfit.Accounts.Safe };
            var item = director.AddEquipment(listing.Kind, listing.DisplayName, listing.Price);
            return new
            {
                bought = item != null,
                itemId = item != null ? item.Id : -1,
                name = listing.DisplayName,
                safe = outfit.Accounts.Safe,
            };
        }

        [CliCommand("gangsters_move_probe",
                    "Issue one real long ground order and report the synchronous input cost.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay", "performance" })]
        public static object MoveProbe()
        {
            if (!TryOutfit(out var crews, out var ours, out var reason))
                return new { ordered = false, reason };
            var target = crews.Units.Where(unit => unit != null && unit != ours && !unit.Wiped)
                .OrderBy(unit => (unit.Position - ours.Position).sqrMagnitude).FirstOrDefault();
            if (target == null)
                return new { ordered = false, reason = "No live destination crew." };

            var start = ours.Position;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ordered = crews.OrderUnit(ours, target.Position, out var destination);
            watch.Stop();
            return new
            {
                ordered,
                elapsedMs = watch.Elapsed.TotalMilliseconds,
                start = Point(start),
                destination = Point(destination),
                distance = Vector3.Distance(start, destination),
            };
        }

        [CliCommand("gangsters_board_probe",
                    "Assign the first ledger car to the Outfit lieutenant and order the crew aboard.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object BoardProbe()
        {
            if (!TryOutfit(out var crews, out var ours, out var reason))
                return new { ordered = false, reason };
            var director = PersonnelDirector.Instance;
            var rosterCrew = director?.Roster?.FindCrew(ours.CrewId);
            var item = director?.Roster?.Equipment.FirstOrDefault(one =>
                one.Kind == EquipmentKind.Vehicle);
            if (rosterCrew == null || item == null)
                return new { ordered = false, reason = "No crew or ledger car." };

            var assigned = director.MoveEquipment(item.Id, rosterCrew.LieutenantId);
            var car = crews.Cars.FirstOrDefault(one => one != null && one.ItemId == item.Id);
            if (car == null)
                return new { ordered = false, reason = "The ledger car has not synced to the street yet." };
            car.Owner = ours; // the next normal BindCars tick derives this same answer
            crews.Select(ours);
            bool ordered = assigned.Ok && crews.OrderCar(car);
            return new { ordered, reason = assigned.Ok ? crews.CarRefusal : assigned.Reason,
                itemId = item.Id, car = item.DisplayName, distance = Vector3.Distance(ours.Position, car.Position) };
        }

        [CliCommand("gangsters_drive_probe",
                    "Order the boarded Outfit car toward the nearest rival crew.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object DriveProbe()
        {
            if (!TryOutfit(out var crews, out var ours, out var reason))
                return new { ordered = false, reason };
            if (ours.Car == null)
                return new { ordered = false, reason = "The crew is not fully aboard yet." };
            var target = crews.Units.Where(unit => unit != null && unit != ours && !unit.Wiped)
                .OrderBy(unit => (unit.Position - ours.Position).sqrMagnitude).FirstOrDefault();
            if (target == null)
                return new { ordered = false, reason = "No rival destination." };
            var start = ours.Car.Position;
            bool ordered = crews.OrderUnit(ours, target.Position, out var destination);
            return new { ordered, car = ours.Car.DisplayName, start = Point(start),
                destination = Point(destination), distance = Vector3.Distance(start, destination) };
        }

        [CliCommand("gangsters_combat_probe",
                    "Order the Outfit against its nearest live rival using normal combat semantics.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object CombatProbe()
        {
            if (!TryOutfit(out var crews, out var ours, out var reason))
                return new { ordered = false, reason };
            var target = crews.Units.Where(unit => unit != null && unit.Faction != ours.Faction &&
                                                   !unit.Wiped && !unit.IsPolice)
                .OrderBy(unit => (unit.Position - ours.Position).sqrMagnitude).FirstOrDefault();
            if (target == null)
                return new { ordered = false, reason = "No live rival." };
            crews.Select(ours);
            bool ordered = crews.OrderAttack(target);
            return new { ordered, target = target.GangName,
                distance = Vector3.Distance(ours.Position, target.Position),
                driveBy = ours.Car != null };
        }

        [CliCommand("gangsters_door_audit",
                    "Audit every live residential bay doorstep against its measured door; report the nearest rows.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object DoorAudit(
            [CliArg("count", "How many of the nearest shops to report.")] int count = 6)
        {
            if (!Application.isPlaying)
                return new { ok = false, reason = "Play Mode is not running." };
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime == null)
                return new { ok = false, reason = "No territory runtime." };

            TryOutfit(out _, out var ours, out _);
            var here = ours != null ? ours.Position : Vector3.zero;
            var audited = LivingCity.Business.CityBusinesses.All
                .Select(row =>
                {
                    LivingCity.Business.BusinessSite site = null;
                    bool physical = LivingCity.Business.BusinessRuntime.Instance != null &&
                        LivingCity.Business.BusinessRuntime.Instance.TryGetSite(row.Id, out site) &&
                        (site.Role == LivingCity.Business.ResidentialBusinessSites.FrontageRole ||
                         site.Role == LivingCity.Business.ResidentialBusinessSites.ExtraFrontageRole);
                    Vector3 approach;
                    var hasApproach = runtime.TryGetBusinessApproach(row.Id, out approach);
                    LivingCity.Entities.BusinessMarker marker;
                    var bound = LivingCity.Business.BusinessViewBindings.TryGet(
                        row.Id, out marker) && marker != null;
                    var storefront = bound
                        ? marker.GetComponent<RoadDemo.Storefront>() ??
                          marker.GetComponentInParent<RoadDemo.Storefront>()
                        : null;
                    bool activeView = physical && site != null &&
                        RoadDemo.CityBlockRecycler.IsViewActive(site.SourcePlanId);
                    return new { row, physical, approach, hasApproach,
                                 marker, bound, storefront, activeView };
                })
                // Missing approach/view rows are failures too. Filtering them here made
                // the audit claim success precisely when an exact facade failed to bind.
                // Off-screen sites correctly have no view, so only standing recipes count.
                .Where(one => one.physical && (one.bound || one.activeView))
                .Select(one =>
                {
                    float frontage = 0f;
                    var entrance = one.bound
                        ? RoadDemo.ShopDoors.Of(one.marker, out frontage)
                        : null;
                    var facing = entrance != null ? entrance.Facing : Vector3.forward;
                    var door = entrance != null ? entrance.DoorWorld : Vector3.zero;
                    var expected = door + facing *
                        (0.85f + LivingCity.Business.CityBusinesses.DoorstepClearanceMetres);
                    float error = entrance != null && one.hasApproach
                        ? Vector3.Distance(expected, one.approach)
                        : float.PositiveInfinity;
                    bool doorless = one.storefront == null || one.storefront.LeafCount == 0;
                    bool onDoor = one.hasApproach && one.bound && entrance != null &&
                                  !doorless && error <= 0.15f;
                    bool onGlass = one.hasApproach && one.bound && entrance != null &&
                                   !doorless && !onDoor;

                    return new
                    {
                        shop = one.row.Name,
                        one.hasApproach,
                        hasView = one.bound,
                        hasDoor = entrance != null,
                        doorLeaves = one.storefront != null ? one.storefront.LeafCount : 0,
                        doorstepOnDoor = onDoor,
                        doorstepOnGlass = onGlass,
                        doorlessBay = doorless,
                        bad = !one.hasApproach || !one.bound || entrance == null ||
                              !onDoor || onGlass || doorless,
                        measuredFrontage = frontage,
                        doorToApproach = entrance != null
                            ? Vector3.Distance(entrance.DoorWorld, one.approach)
                            : -1f,
                        thresholdError = float.IsPositiveInfinity(error) ? -1f : error,
                        approach = Point(one.approach),
                        door = Point(door),
                        boardsAt = Point(entrance != null
                            ? entrance.DoorWorld + entrance.Facing * 0.1f
                            : Vector3.zero),
                        distance = one.hasApproach
                            ? (one.approach - here).sqrMagnitude
                            : float.MaxValue,
                    };
                })
                .ToArray();

            // Put failures first so a bounded --count cannot hide the reason for a red audit.
            var rows = audited.OrderByDescending(one => one.bad).ThenBy(one => one.distance)
                .Take(Mathf.Clamp(count, 1, 40)).ToArray();
            int badSites = audited.Count(one => one.bad);
            return new
            {
                ok = badSites == 0,
                auditedSites = audited.Length,
                badSites,
                rows,
            };
        }

        [CliCommand("gangsters_racket_probe",
                    "Drive the doorstep chain with nobody at the keyboard: two SMASH IT UP " +
                    "orders and a DEMAND PROTECTION, or one BEAT/KILL THE OWNER acceptance, " +
                    "watching what lands and whether any man teleports or vanishes.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object RacketProbe(
            [CliArg("run", "True starts a fresh probe; false reads the verdict of the last one.")] bool run = true,
            [CliArg("patience", "Sim seconds one order is given to come off.")] float patience = 90f,
            [CliArg("after", "Sim seconds before the first order.")] float after = 4f,
            [CliArg("overlap", "File the second smash without waiting for the first to land.")] bool overlap = false,
            [CliArg("far", "Only pick doors at least this many metres from the crew.")] float far = 0f,
            [CliArg("beating", "Run BEAT THE OWNER instead of the smash/torch ladder.")] bool beating = false,
            [CliArg("killing", "Run KILL THE OWNER instead of the smash/torch ladder.")] bool killing = false)
        {
            var prior = Object.FindAnyObjectByType<RoadDemo.RacketProbe>();
            if (!run)
                return prior == null
                    ? new { running = false, finished = false, verdict = "no probe has been run" }
                    : new { running = !prior.Finished, prior.Finished, prior.Verdict };

            if (!Application.isPlaying)
                return new { running = false, finished = false, verdict = "Play Mode is not running." };
            if (prior != null) Object.Destroy(prior.gameObject);

            var probe = new GameObject("Racket probe (temporary)")
                .AddComponent<RoadDemo.RacketProbe>();
            probe.patience = Mathf.Max(5f, patience);
            probe.startAfter = Mathf.Max(0f, after);
            probe.overlap = overlap;
            probe.atLeastMetres = Mathf.Max(0f, far);
            probe.beating = beating && !killing;
            probe.killing = killing;
            return new { running = true, finished = false,
                verdict = "running - poll gangsters_racket_probe --run false" };
        }

        [CliCommand("gangsters_monkey",
                    "Start or stop the existing unattended movement/driving/combat soak.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object Monkey(
            [CliArg("run", "True starts a fresh soak; false stops and prints its verdict.")] bool run = true,
            [CliArg("every", "Seconds between orders.")] float every = 1.25f,
            [CliArg("seed", "Deterministic action seed.")] int seed = 1987)
        {
            var prior = Object.FindAnyObjectByType<MonkeyRunner>();
            if (!run)
            {
                if (prior != null) prior.enabled = false;
                return new { running = false, stopped = prior != null };
            }
            if (!Application.isPlaying)
                return new { running = false, reason = "Play Mode is not running." };
            if (prior != null) Object.Destroy(prior);
            var runner = new GameObject("Gameplay smoke monkey (temporary)").AddComponent<MonkeyRunner>();
            runner.startAfter = 0f;
            runner.orderEvery = Mathf.Max(0.5f, every);
            runner.warPatience = 30f;
            runner.seed = seed;
            return new { running = true, runner.orderEvery, runner.seed };
        }

        static bool TryOutfit(out DemoCrews crews, out DemoCrews.Unit ours, out string reason)
        {
            crews = Object.FindAnyObjectByType<DemoCrews>();
            ours = crews?.Units.FirstOrDefault(unit => unit != null && unit.Faction == 0 && !unit.Wiped);
            reason = crews == null ? "No live DemoCrews." : ours == null ? "No live Outfit crew." : null;
            return ours != null;
        }

        [CliCommand("gangsters_streaming_audit",
                    "Run the pure block-recipe/catalog/viewport contracts and report live recycler counts when playing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object Audit()
        {
            var failures = ResidentialBlockStreamingTests.Run();
            var config = CityViewConfig.Resolve();
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            var live = Object.FindObjectsByType<CityBlockRecycler>()
                .Select(one => new
                {
                    one.RecipeCount,
                    one.ActiveViews,
                    one.CachedViews,
                    one.PooledHolders,
                    one.PendingViews,
                    one.ComposingViews,
                    one.AttachingViews,
                    one.PendingRendererAttachments,
                    one.SourceObjects,
                    one.SourceRenderers,
                    one.BuiltViews,
                    one.EvictedViews,
                    one.LastBuildMs,
                    one.WorstBuildMs,
                    one.LastBuildStepMs,
                    one.WorstBuildStepMs,
                    one.PrefabPoolCapacity,
                    one.AvailablePrefabParts,
                    one.PrewarmedPrefabParts,
                    one.PendingPrewarmParts,
                    one.PendingPoolRetirements,
                    one.PendingAssetWarm,
                    one.FallbackBlocks,
                    one.VisibleFallbackBlocks,
                    one.ReusedPrefabParts,
                    one.RuntimePrefabMisses,
                    one.RuntimePrefabMissTypes,
                    one.LargestRuntimeMissPrefab,
                    one.LargestRuntimeMissRenderers,
                    one.RuntimeMissSummary,
                }).ToArray();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                generatorVersion = ResidentialBlockRecipe.GeneratorVersion,
                max3DDefault = CityViewConfig.DefaultMax3DDistance,
                max3DConfigured = config.Max3DDistance,
                max3DCamera = rig != null ? rig.mapAt : (float?)null,
                streetPitchConfigured = config.StreetPitch,
                pitchFreedomConfigured = config.PitchFreedom,
                streetCutawayConfigured = config.StreetCutaway,
                minimapViewHeightConfigured = config.MinimapViewHeight,
                prefetchConfigured = config.Prefetch,
                renderLeadConfigured = config.RenderHysteresis,
                compositionStepsPerFrameConfigured = config.CompositionStepsPerFrame,
                rendererAttachBudgetConfigured = config.RendererAttachBudget,
                prefabPoolLimitConfigured = config.PrewarmPartLimit,
                prefabVariantReserveConfigured = config.PrewarmVariantReserve,
                cameraPitch = rig != null ? rig.pitch : (float?)null,
                cameraPitchMinimum = rig != null ? rig.MinimumPitch : (float?)null,
                cameraPitchMaximum = rig != null ? rig.MaximumPitch : (float?)null,
                pitchLocked = rig != null ? rig.PitchLocked : (bool?)null,
                mapOut = rig != null ? rig.MapOut : (bool?)null,
                mapOpen = TurfMapHud.IsOpen,
                stressRunning = Object.FindAnyObjectByType<DemoCameraStreamingStress>() != null,
                coreBlocks = CoreBlockCatalog.Count,
                playing = Application.isPlaying,
                recyclers = live,
            };
        }

        [CliCommand("gangsters_turf_map_audit",
                    "Report shared TurfMap source coverage, names, model footprints and local minimap framing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "map" })]
        public static object TurfMapAudit()
        {
            var builder = Object.FindAnyObjectByType<RoadDemoBuilder>();
            var hud = Object.FindAnyObjectByType<TurfMapHud>();
            var minimap = Object.FindAnyObjectByType<TurfMinimap>();
            var survey = hud != null ? hud.Survey : minimap != null ? minimap.Survey : null;
            var config = CityViewConfig.Resolve();
            var usedProxyUnits = builder != null
                ? builder.ResidentialMapSources
                    .Where(source => source.Model != null)
                    .SelectMany(source => source.Model.Blocks)
                    .Where(recipe => recipe?.Plan?.Spots != null)
                    .SelectMany(recipe => recipe.Plan.Spots)
                    .Where(spot => spot?.Unit != null &&
                                   spot.Unit.Kind != ResidentialKind.Park)
                    .GroupBy(spot => spot.Unit.Name)
                    .Select(group => new
                    {
                        name = group.Key,
                        buildings = group.Count(),
                        prepared = ResidentialTurfCatalog.TryGet(
                            group.Key, out var masses) && masses.Length > 0,
                    })
                    .OrderBy(entry => entry.name)
                    .ToArray()
                : null;

            int endpoints = 0, uncoveredEndpoints = 0, intersections = 0;
            if (builder != null && survey != null)
            {
                foreach (var road in builder.QuarterRoads)
                {
                    endpoints += 2;
                    if (!survey.Streets.Any(street => Covers(street.World, road.a)))
                        uncoveredEndpoints++;
                    if (!survey.Streets.Any(street => Covers(street.World, road.b)))
                        uncoveredEndpoints++;
                }

                foreach (var vertical in survey.Streets.Where(street => street.Vertical))
                    intersections += survey.Streets.Count(across =>
                        !across.Vertical && vertical.World.Overlaps(across.World));
            }

            var requested = minimap != null ? minimap.RequestedView : default;
            var city = survey != null ? survey.CityView : default;
            var buildingLayer = hud != null ? hud.BuildingLayer : null;
            var buildingProxy = buildingLayer != null ? buildingLayer.Report : default;
            return new
            {
                ready = survey != null && survey.Ready,
                primaryStructure = builder != null && builder.HasPrimaryStructure,
                residentialSources = builder != null ? builder.ResidentialMapSources.Count : 0,
                residentialGeometryVersion = builder != null
                    ? builder.ResidentialGeometryVersion : -1,
                recipes = builder != null
                    ? builder.ResidentialMapSources.Sum(source => source.Model?.Count ?? 0) : 0,
                streets = survey?.Streets.Count ?? 0,
                namedStreets = survey?.Streets.Count(street =>
                    !string.IsNullOrEmpty(street.Name)) ?? 0,
                roadEndpoints = endpoints,
                uncoveredRoadEndpoints = uncoveredEndpoints,
                intersections,
                buildings = survey?.Buildings.Count ?? 0,
                buildingProxyInstalled = buildingLayer != null,
                buildingProxyVersion = buildingLayer != null ? buildingLayer.GeometryVersion : -1,
                buildingProxyBuildings = buildingProxy.Buildings,
                buildingProxyMasses = buildingProxy.Masses,
                buildingProxyPrefabDerived = buildingProxy.PrefabDerived,
                buildingProxySceneDerived = buildingProxy.SceneDerived,
                buildingProxyFallback = buildingProxy.Fallback,
                preparedProxyBuildings = usedProxyUnits != null
                    ? usedProxyUnits.Where(entry => entry.prepared).Sum(entry => entry.buildings) : 0,
                missingPreparedProxyUnits = usedProxyUnits != null
                    ? usedProxyUnits.Where(entry => !entry.prepared)
                        .Select(entry => entry.name + ":" + entry.buildings).ToArray()
                    : System.Array.Empty<string>(),
                buildingProxyTallest = buildingProxy.Tallest,
                buildingProxyChunks = buildingLayer != null ? buildingLayer.VisibleChunks : 0,
                buildingProxyTiles = buildingLayer != null ? buildingLayer.TotalTiles : 0,
                buildingProxyPooledTiles = buildingLayer != null ? buildingLayer.PooledTiles : 0,
                buildingProxyTileRebinds = buildingLayer != null ? buildingLayer.TileRebinds : 0,
                buildingProxyMeshBuilds = buildingLayer != null ? buildingLayer.MeshBuilds : 0,
                landmarks = survey?.Landmarks.Count ?? 0,
                gyms = survey?.Landmarks.Count(mark => mark.Kind == TurfLandmarkKind.Gym) ?? 0,
                fuelStations = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.FuelStation) ?? 0,
                carYards = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.CarYard) ?? 0,
                skateparks = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Skatepark) ?? 0,
                parking = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Parking) ?? 0,
                cafes = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Cafe) ?? 0,
                subways = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Transit) ?? 0,
                residentialGreens = survey?.ResidentialGreenCount ?? 0,
                parkSurfaces = survey?.ParkSurfaceCount ?? 0,
                corePaving = survey?.CorePavingCount ?? 0,
                coreWater = survey?.CoreWaterCount ?? 0,
                corePromenades = survey?.CorePromenadeCount ?? 0,
                labels = survey?.Labels.Count ?? 0,
                minimapInstalled = minimap != null,
                minimapPrinted = minimap != null && minimap.Printed,
                minimapConfiguredHeight = config.MinimapViewHeight,
                minimapRequestedHeight = minimap != null ? requested.height : 0f,
                cityViewHeight = survey != null ? city.height : 0f,
                minimapZoom = minimap != null && requested.height > 0f
                    ? city.height / requested.height : 0f,
                playing = Application.isPlaying,
            };
        }

        [CliCommand("gangsters_turf_map_view",
                    "Move the shared camera above or below the 3D-to-map line for visual auditing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "map" })]
        public static object TurfMapView(
            [CliArg("full", "True opens the full TurfMap; false returns to the street/minimap.")]
            bool full = true)
        {
            if (!Application.isPlaying)
                return new { changed = false, reason = "Play Mode is not running." };
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            if (rig == null)
                return new { changed = false, reason = "No DemoCamera is live." };

            rig.distance = full
                ? Mathf.Min(rig.mapCeiling, rig.mapAt + 30f)
                : Mathf.Max(20f, rig.mapAt - 25f);
            return new { changed = true, full, distance = rig.distance, mapAt = rig.mapAt };
        }

        static bool Covers(Rect world, Vector2 point)
        {
            const float epsilon = 0.05f;
            return point.x >= world.xMin - epsilon && point.x <= world.xMax + epsilon &&
                   point.y >= world.yMin - epsilon && point.y <= world.yMax + epsilon;
        }
    }
}
