using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LivingCity.Business;
using LivingCity.Property;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GangstersTools
{
    /// <summary>Read-only measurements for NPC-001, kept out of the player assembly.</summary>
    internal static class PeopleCensusAudit
    {
        const int CanonicalSeed = 1987;
        const float DoorReach = 18f;
        const float EndSetback = 7f;
        const float CrossingBuffer = 3f;
        const float HydrantRadius = 4.6f;
        const float BusStopLength = 12f;
        const float ApproachMargin = 2f;
        const float TypicalCarHalfLength = 2.3f;
        // Leave five seconds for command serialization/transport inside the ticket's
        // externally visible 30-second ceiling.
        const double CensusWorkBudgetSeconds = 25d;
        const int BenchmarkFrames = 64;
        const int BenchmarkWarmupFrames = 8;

        // Identity-level manifests for the canonical release gate. Totals alone can stay
        // unchanged when one door regresses and another improves, which is not a pass.
        const string CanonicalBusinessLandingDigest = "0efdd04bbd84c125";
        const string CanonicalDowntownLandingDigest = "37833524b4fe46b7";
        const string CanonicalResidentialLandingDigest = "bdc9f7ed1f9c9012";
        const string CanonicalKerbIntervalDigest = "e1f8d7b986a14c8f";

        static readonly int[] CrowdCounts = { 100, 240, 480 };
        static readonly FieldInfo CrowdFrame = typeof(PedestrianAgent).GetField(
            "_cellFrame", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo CrowdIds = typeof(PedestrianAgent).GetField(
            "_ids", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo CrowdCells = typeof(PedestrianAgent).GetField(
            "Cells", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo CrowdSpareCells = typeof(PedestrianAgent).GetField(
            "SpareCells", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo CivilianListening = typeof(CivilianAgent).GetField(
            "listening", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo CivilianGawkScan = typeof(CivilianAgent).GetField(
            "gawkScan", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo AlarmOnShot = typeof(StreetAlarm).GetField(
            "OnShot", BindingFlags.Static | BindingFlags.NonPublic);

        sealed class FailureTally
        {
            readonly SortedDictionary<string, int> counts =
                new SortedDictionary<string, int>(StringComparer.Ordinal);

            public void Add(string reason)
            {
                reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
                counts.TryGetValue(reason, out int count);
                counts[reason] = count + 1;
            }

            public object[] Rows() => counts.Select(pair => (object)new
            {
                reason = pair.Key,
                count = pair.Value,
            }).ToArray();
        }

        sealed class Interval
        {
            public float From;
            public float To;

            public Interval(float from, float to)
            {
                From = from;
                To = to;
            }

            public float Length => Mathf.Max(0f, To - From);
        }

        sealed class KerbSide
        {
            public Carriageway Road;
            public int Side;
            public readonly List<Interval> Legal = new List<Interval>();
        }

        sealed class StableDigest
        {
            const ulong Offset = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;
            ulong value = Offset;

            public void Add(bool item) => Add(item ? 1 : 0);

            public void Add(int item)
            {
                unchecked
                {
                    AddByte((byte)item);
                    AddByte((byte)(item >> 8));
                    AddByte((byte)(item >> 16));
                    AddByte((byte)(item >> 24));
                }
            }

            public void Add(string item)
            {
                if (item == null) { Add(-1); return; }
                Add(item.Length);
                for (int index = 0; index < item.Length; index++)
                {
                    char character = item[index];
                    AddByte((byte)character);
                    AddByte((byte)(character >> 8));
                }
            }

            void AddByte(byte item)
            {
                unchecked
                {
                    value ^= item;
                    value *= Prime;
                }
            }

            public string Hex => value.ToString("x16");
        }

        public static object Run(int seed, bool includeRows)
        {
            if (seed != CanonicalSeed)
                throw new ArgumentOutOfRangeException(nameof(seed), seed,
                    $"The NPC-001 release census is canonical-only; use seed {CanonicalSeed}. " +
                    "Arbitrary-seed generation is not part of this 30-second gate.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "gangsters_people_census runs with the editor stopped; it never borrows or changes the live crowd.");
            if (CrowdFrame == null || CrowdIds == null || CrowdCells == null ||
                CrowdSpareCells == null || CivilianListening == null ||
                CivilianGawkScan == null || AlarmOnShot == null)
                throw new MissingFieldException(
                    "The people census no longer matches the crowd's measured static state.");

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var gateFailures = new List<string>();
            var core = new CoreDistrict();
            core.Plan(null, seed);
            CheckBudget(elapsed, "core plan");
            core.Frame = DistrictFrame.Identity;
            var links = RasterPedGraph.Build(core.Raster, core.Frame);
            CheckBudget(elapsed, "pedestrian graph");

            var catalog = new BusinessSiteCatalog();
            catalog.Add(new ResidentialBusinessSites(core.ResidentialBlocks, core.Frame));
            catalog.Add(new StandaloneBusinessSites(core));
            catalog.Add(new CompoundBusinessSites(core, null));
            catalog.Build();
            CheckBudget(elapsed, "business catalogue");

            foreach (var problem in catalog.Problems)
                gateFailures.Add("business site catalogue: " + problem);

            var business = MeasureBusinessDoors(
                catalog, links, includeRows, seed, gateFailures, elapsed);
            var downtown = MeasureDowntownDoors(
                core, links, includeRows, seed, gateFailures, elapsed);
            var residential = MeasureResidentialDoors(
                core, links, includeRows, seed, gateFailures, elapsed);
            var kerb = MeasureKerb(core, includeRows, seed, gateFailures, elapsed);
            var frame = MeasureCrowdCurve(links, seed, elapsed);

            CheckBudget(elapsed, "complete census");
            elapsed.Stop();
            var distinctFailures = gateFailures
                .Distinct(StringComparer.Ordinal)
                .OrderBy(failure => failure, StringComparer.Ordinal)
                .ToArray();
            return new
            {
                passed = distinctFailures.Length == 0,
                failures = distinctFailures,
                seed,
                measuredOn = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                elapsedMs = Math.Round(elapsed.Elapsed.TotalMilliseconds, 1),
                metresPerGameHour = PedestrianAgent.DefaultSpeedMetresPerSecond * 60f,
                assumptions = new
                {
                    realSecondsPerGameHour = 60f,
                    doorWiringReachMetres = DoorReach,
                    kerbEndSetbackMetres = EndSetback,
                    hydrantRadiusMetres = HydrantRadius,
                    busStopLengthMetres = BusStopLength,
                    vehicleApproachMarginMetres = ApproachMargin,
                    workBudgetSeconds = CensusWorkBudgetSeconds,
                    crossingBufferMetres = CrossingBuffer,
                    slotPitchMetres = TypicalCarHalfLength * 2f + KerbCars.Gap,
                },
                doors = new { businessSites = business, downtownModules = downtown, residential },
                kerb,
                crowdTick = new
                {
                    source = "TickTimer marks 3 (civilians) and 4 (crowd)",
                    warmupFrames = BenchmarkWarmupFrames,
                    measuredFrames = BenchmarkFrames,
                    unity = Application.unityVersion,
                    processor = SystemInfo.processorType,
                    rows = frame,
                },
                businessRegistry = new
                {
                    sites = catalog.Sites.Count,
                    eligible = catalog.EligibleCount,
                    problems = catalog.Problems.ToArray(),
                },
            };
        }

        static object MeasureBusinessDoors(BusinessSiteCatalog catalog, List<PedLink> links,
                                           bool includeRows, int seed,
                                           List<string> gateFailures,
                                           System.Diagnostics.Stopwatch elapsed)
        {
            int count = 0, landed = 0;
            var failures = new FailureTally();
            var rows = new List<object>();
            var digest = new StableDigest();
            int scanned = 0;
            foreach (var site in catalog.Sites)
            {
                if ((scanned++ & 127) == 0) CheckBudget(elapsed, "business doors");
                if (!site.Eligible) continue;
                count++;
                var point = new Vector3(site.Approach.X, 0f, site.Approach.Z);
                bool ok = TryLand(point, links, out _, out _, out float distance);
                string reason = ok ? "" : "no non-crossing PedLink within 18 m";
                if (ok) landed++; else failures.Add(reason);
                digest.Add(site.SiteId.Value);
                digest.Add(ok);
                if (includeRows)
                    rows.Add(new
                    {
                        id = site.SiteId.Value,
                        provider = site.ProviderId,
                        plan = site.SourcePlanId,
                        x = site.Approach.X,
                        z = site.Approach.Z,
                        landed = ok,
                        distanceMetres = ok ? Math.Round(distance, 3) : (double?)null,
                        reason,
                    });
            }
            if (seed == 1987 &&
                (catalog.Sites.Count != 3581 || count != 3564 || landed != 3209))
                gateFailures.Add(
                    "seed 1987 business-door baseline changed: expected " +
                    $"3581 sites / 3564 eligible / 3209 landed, measured " +
                    $"{catalog.Sites.Count} / {count} / {landed}");
            string landingDigest = digest.Hex;
            if (!string.Equals(landingDigest, CanonicalBusinessLandingDigest,
                               StringComparison.Ordinal))
                gateFailures.Add(
                    "seed 1987 business per-site landing manifest changed: expected " +
                    $"{CanonicalBusinessLandingDigest}, measured {landingDigest}");
            return new
            {
                count,
                landed,
                landingDigest,
                failed = count - landed,
                failures = failures.Rows(),
                rows = includeRows ? rows.ToArray() : null,
            };
        }

        static object MeasureDowntownDoors(CoreDistrict core, List<PedLink> links,
                                           bool includeRows, int seed,
                                           List<string> gateFailures,
                                           System.Diagnostics.Stopwatch elapsed)
        {
            int shops = 0, apartmentDoors = 0, doorCapable = 0, landed = 0;
            int shopCoversExcluded = 0, wallWindowModules = 0,
                apartmentDoorSourceModules = 0;
            var failures = new FailureTally();
            var rows = new List<object>();
            var blocks = new List<object>();
            var empty = new List<string>();
            var digest = new StableDigest();

            for (int number = 1; number <= 16; number++)
            {
                CheckBudget(elapsed, "downtown doors");
                string blockName = "block-" + number.ToString("00");
                var block = core.LayoutBlocks.FirstOrDefault(candidate => candidate.Name == blockName);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CoreLayout.BlocksDir + blockName + ".prefab");
                int blockShops = 0, blockApartments = 0, blockLanded = 0;
                if (block == null || prefab == null)
                {
                    string missing = block == null
                        ? "authored block absent from accepted layout"
                        : "authored block prefab missing";
                    failures.Add(missing);
                    gateFailures.Add(blockName + ": " + missing);
                    empty.Add(blockName);
                    blocks.Add(new
                    {
                        block = blockName,
                        shopModules = 0,
                        apartmentDoorModules = 0,
                        landed = 0,
                        failed = 1,
                        missing = true,
                    });
                    continue;
                }

                // A baked Core block is an instance list: each direct child is one source
                // module. Walking below those roots counts the source again for every mesh
                // and locator inside the nested prefab (and turns 92 shells into 180 hits).
                foreach (Transform instance in prefab.transform)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                    if (source == null) continue;
                    string sourceName = source.name;
                    if (sourceName.IndexOf("SM_Bld_Shop_Cover",
                                           StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        shopCoversExcluded++;
                        continue;
                    }
                    bool shop = IsShopShell(sourceName);
                    bool wallWindow = sourceName.StartsWith(
                        "SM_Bld_Wall_Window", StringComparison.OrdinalIgnoreCase);
                    bool apartmentDoorSource = sourceName.StartsWith(
                        "SM_Bld_Apartment_Door", StringComparison.OrdinalIgnoreCase);
                    bool apartment = wallWindow || apartmentDoorSource;
                    if (!shop && !apartment) continue;

                    if (shop) { shops++; blockShops++; }
                    else
                    {
                        apartmentDoors++;
                        blockApartments++;
                        if (wallWindow) wallWindowModules++;
                        else apartmentDoorSourceModules++;
                    }

                    Vector3 point = default;
                    float distance = 0f;
                    string reason = "";
                    bool hasDoor = true;
                    Vector3 sourcePoint = instance.position;
                    if (shop)
                    {
                        if (!StorefrontDoorCatalog.TryGet(sourceName, out var profile))
                        {
                            hasDoor = false;
                            reason = "shop module has no measured door profile";
                        }
                        else if (profile.Leaves <= 0)
                        {
                            hasDoor = false;
                            reason = "shop module is a window only (no physical door)";
                        }
                        else sourcePoint = instance.TransformPoint(profile.Centre);
                    }
                    if (hasDoor)
                    {
                        doorCapable++;
                        var prefabLocal = prefab.transform.InverseTransformPoint(sourcePoint);
                        var cityLocal = block.Position + block.Rotation * prefabLocal;
                        point = core.Frame.ToWorld(cityLocal);
                    }
                    bool ok = hasDoor &&
                              TryLand(point, links, out _, out _, out distance);
                    if (hasDoor && !ok)
                        reason = "module door has no non-crossing PedLink within 18 m";
                    if (ok) { landed++; blockLanded++; }
                    else failures.Add(reason);
                    digest.Add(blockName);
                    digest.Add(instance.GetSiblingIndex());
                    digest.Add(sourceName);
                    digest.Add(hasDoor);
                    digest.Add(ok);

                    if (includeRows)
                        rows.Add(new
                        {
                            block = blockName,
                            source = sourceName,
                            kind = shop ? "shop" : "apartment-door",
                            hasPhysicalDoor = hasDoor,
                            x = hasDoor ? Math.Round(point.x, 3) : (double?)null,
                            z = hasDoor ? Math.Round(point.z, 3) : (double?)null,
                            landed = ok,
                            distanceMetres = ok ? Math.Round(distance, 3) : (double?)null,
                            reason,
                        });
                }

                int blockCount = blockShops + blockApartments;
                if (blockCount == 0) empty.Add(blockName);
                blocks.Add(new
                {
                    block = blockName,
                    shopModules = blockShops,
                    apartmentDoorModules = blockApartments,
                    landed = blockLanded,
                    failed = blockCount - blockLanded,
                    missing = false,
                });
            }

            int count = shops + apartmentDoors;
            var correctedEmptyBlocks = new[]
            {
                "block-02", "block-03", "block-08",
                "block-14", "block-15", "block-16",
            };
            bool correctedCountConfirmed = shops == 92 && apartmentDoors == 49;
            bool correctedEmptyListConfirmed = empty.SequenceEqual(correctedEmptyBlocks);
            string landingDigest = digest.Hex;
            if (seed == 1987 &&
                (!correctedCountConfirmed || !correctedEmptyListConfirmed ||
                 doorCapable != 135 || landed != 121))
                gateFailures.Add(
                    "seed 1987 downtown-door baseline changed: expected " +
                    "92 shops / 49 apartment modules / 135 physical doors / " +
                    $"121 landed / empty [{string.Join(", ", correctedEmptyBlocks)}], " +
                    $"measured {shops} / {apartmentDoors} / {doorCapable} / {landed} / " +
                    $"[{string.Join(", ", empty)}]");
            if (!string.Equals(landingDigest, CanonicalDowntownLandingDigest,
                               StringComparison.Ordinal))
                gateFailures.Add(
                    "seed 1987 downtown per-module landing manifest changed: expected " +
                    $"{CanonicalDowntownLandingDigest}, measured {landingDigest}");
            return new
            {
                count,
                shopModules = shops,
                apartmentDoorModules = apartmentDoors,
                doorCapable,
                landed,
                landingDigest,
                failed = count - landed,
                failures = failures.Rows(),
                emptyBlocks = empty.ToArray(),
                provisionalReviewCountConfirmed = shops == 84 && apartmentDoors == 46,
                provisionalReviewEmptyListConfirmed = empty.SequenceEqual(new[]
                {
                    "block-02", "block-03", "block-07", "block-08",
                    "block-14", "block-15", "block-16",
                }),
                correctedCountConfirmed,
                correctedEmptyListConfirmed,
                review = new
                {
                    provisionalShopModules = 84,
                    provisionalApartmentDoorModules = 46,
                    provisionalEmptyBlocks = new[]
                    {
                        "block-02", "block-03", "block-07", "block-08",
                        "block-14", "block-15", "block-16",
                    },
                    correctedShopModules = 92,
                    correctedApartmentDoorModules = 49,
                    correctedEmptyBlocks,
                    shopCoversExcluded,
                    wallWindowModules,
                    apartmentDoorSourceModules,
                    correction =
                        "The baked direct-instance list has no SM_Bld_Wall_Window source. " +
                        $"Its {apartmentDoorSourceModules} residential entrances are " +
                        "SM_Bld_Apartment_Door sources; " +
                        "the census includes those under apartmentDoorModules and reports " +
                        "both source counts so the correction stays visible. Block 07 is " +
                        "not empty: it has one shop and nine apartment-door modules.",
                },
                blocks = blocks.ToArray(),
                rows = includeRows ? rows.ToArray() : null,
            };
        }

        static bool IsShopShell(string sourceName) =>
            sourceName.StartsWith("SM_Bld_Shop", StringComparison.OrdinalIgnoreCase) &&
            sourceName.IndexOf("Shop_Cover", StringComparison.OrdinalIgnoreCase) < 0;

        static object MeasureResidentialDoors(CoreDistrict core, List<PedLink> links,
                                               bool includeRows, int seed,
                                               List<string> gateFailures,
                                               System.Diagnostics.Stopwatch elapsed)
        {
            int count = 0, landed = 0;
            var failures = new FailureTally();
            var rows = new List<object>();
            var digest = new StableDigest();

            foreach (var recipe in core.ResidentialBlocks.Blocks)
            {
                CheckBudget(elapsed, "residential doors");
                if (recipe?.Plan?.Spots == null) continue;
                for (int spotIndex = 0; spotIndex < recipe.Plan.Spots.Count; spotIndex++)
                {
                    var spot = recipe.Plan.Spots[spotIndex];
                    if (spot == null || !ApartmentBuildings.IsApartmentBuilding(spot.Unit))
                        continue;

                    count++;
                    bool hasPoint = TryResidentialEntrance(
                        recipe, spot, core.Frame, out var point, out string reason);
                    float distance = 0f;
                    bool ok = hasPoint && TryLand(point, links, out _, out _, out distance);
                    if (hasPoint && !ok)
                        reason = "street entrance has no non-crossing PedLink within 18 m";
                    if (ok) landed++; else failures.Add(reason);
                    digest.Add(recipe.Id);
                    digest.Add(spotIndex);
                    digest.Add(spot.Unit.Name);
                    digest.Add(ok);

                    if (includeRows)
                        rows.Add(new
                        {
                            id = $"flat|{recipe.Id}|spot:{spotIndex}:{spot.Unit.Name}",
                            plan = recipe.Id,
                            unit = spot.Unit.Name,
                            x = hasPoint ? Math.Round(point.x, 3) : (double?)null,
                            z = hasPoint ? Math.Round(point.z, 3) : (double?)null,
                            landed = ok,
                            distanceMetres = ok ? Math.Round(distance, 3) : (double?)null,
                            reason = ok ? "" : reason,
                        });
                }
            }

            if (seed == 1987 && (count != 425 || landed != 425))
                gateFailures.Add(
                    "seed 1987 residential-door baseline changed: expected " +
                    $"425 candidates / 425 landed, measured {count} / {landed}");
            string landingDigest = digest.Hex;
            if (!string.Equals(landingDigest, CanonicalResidentialLandingDigest,
                               StringComparison.Ordinal))
                gateFailures.Add(
                    "seed 1987 residential per-building landing manifest changed: expected " +
                    $"{CanonicalResidentialLandingDigest}, measured {landingDigest}");

            return new
            {
                count,
                landed,
                landingDigest,
                failed = count - landed,
                failures = failures.Rows(),
                rows = includeRows ? rows.ToArray() : null,
            };
        }

        static bool TryResidentialEntrance(
            ResidentialBlockRecipe recipe,
            ResidentialLot.Spot spot,
            DistrictFrame frame,
            out Vector3 point,
            out string reason)
        {
            point = default;
            reason = "";
            if (recipe?.Plan == null)
            {
                reason = "residential recipe missing";
                return false;
            }
            int side = spot.AccessSide;
            int at = spot.EntranceAt;
            if (side < 0 || side > 3 || at < 0)
            {
                reason = "plan has no pedestrian access side";
                return false;
            }
            if (!TryResidentialRingCell(recipe, side, at, out int i, out int j) ||
                recipe.Plan.Ground[i, j] != ResidentialLot.Use.Walkway)
            {
                reason = "pedestrian access does not land on a planned pavement cell";
                return false;
            }

            float cell = ResidentialLot.Cell;
            var local = new Vector3(
                recipe.LocalBounds.xMin + (i + 0.5f) * cell,
                0f,
                recipe.LocalBounds.yMin + (j + 0.5f) * cell);
            point = frame.ToWorld(local);
            return true;
        }

        static bool TryResidentialRingCell(ResidentialBlockRecipe recipe, int side, int at,
                                           out int i, out int j)
        {
            i = j = -1;
            if (recipe?.Plan == null || side < 0 || side > 3 || at < 0) return false;
            switch (side)
            {
                case 0: i = at; j = 0; break;
                case 1: i = recipe.Plan.W - 1; j = at; break;
                case 2: i = at; j = recipe.Plan.D - 1; break;
                default: i = 0; j = at; break;
            }
            return i >= 0 && j >= 0 && i < recipe.Plan.W && j < recipe.Plan.D;
        }

        static bool TryResidentialAccessPoint(ResidentialBlockRecipe recipe, int side, int at,
                                              DistrictFrame frame, out Vector3 point)
        {
            point = default;
            if (!TryResidentialRingCell(recipe, side, at, out int i, out int j))
                return false;
            float cell = ResidentialLot.Cell;
            point = frame.ToWorld(new Vector3(
                recipe.LocalBounds.xMin + (i + 0.5f) * cell,
                0f,
                recipe.LocalBounds.yMin + (j + 0.5f) * cell));
            return true;
        }

        static object MeasureKerb(CoreDistrict core, bool includeRows, int seed,
                                  List<string> gateFailures,
                                  System.Diagnostics.Stopwatch elapsed)
        {
            var net = RasterGraph.Build(core.Raster, core.Frame,
                core.streetSpeed, core.boulevardSpeed, core.alleySpeed);
            var sides = new List<KerbSide>();
            float rawMetres = 0f;
            int roadScan = 0;
            foreach (var road in net.Roads)
            {
                if ((roadScan++ & 127) == 0) CheckBudget(elapsed, "kerb road sides");
                if (road == null || road.Elevated) continue;
                if (road.ParkingA) AddKerbSide(sides, road, -1, ref rawMetres);
                if (road.ParkingB) AddKerbSide(sides, road, +1, ref rawMetres);
            }

            float beforeEnds = Length(sides);
            foreach (var side in sides)
            {
                Remove(side.Legal, 0f, EndSetback);
                Remove(side.Legal, side.Road.Length - EndSetback, side.Road.Length);
            }
            float afterEnds = Length(sides);

            // Crossings meet the road at its ends. Keep this as its own measured rule:
            // today its +3 m lies wholly inside the stricter 7 m end setback, so it removes
            // zero additional metres instead of being silently omitted.
            foreach (var side in sides)
            {
                Remove(side.Legal, 0f, CrossingBuffer);
                Remove(side.Legal, side.Road.Length - CrossingBuffer, side.Road.Length);
            }
            float afterCrossings = Length(sides);

            var sidewalk = ReadAuthoredKerbFurniture(core, gateFailures,
                out int footprintFallbacks);
            CheckBudget(elapsed, "authored kerb furniture");
            int hydrants = 0, stops = 0;
            int furnitureWithoutParking = 0, parkingWithoutParking = 0,
                fuelWithoutParking = 0, residentialWithoutParking = 0;
            float beforeHydrants = Length(sides);
            foreach (var box in sidewalk.Boxes)
            {
                if (!string.Equals(KerbFurnitureTag(box.SourceName), "hydrant",
                                   StringComparison.Ordinal)) continue;
                hydrants++;
                if (!ExcludeAt(sides, box.C, HydrantRadius)) furnitureWithoutParking++;
            }
            float afterHydrants = Length(sides);
            float beforeStops = afterHydrants;
            foreach (var box in sidewalk.Boxes)
            {
                if (!string.Equals(KerbFurnitureTag(box.SourceName), "bus-stop",
                                   StringComparison.Ordinal)) continue;
                stops++;
                if (!ExcludeAt(sides, box.C, BusStopLength * 0.5f))
                    furnitureWithoutParking++;
            }
            float afterStops = Length(sides);

            int parkingApproaches = 0;
            float beforeParking = afterStops;
            foreach (var site in core.ParkingSites)
            {
                parkingApproaches++;
                var point = AmenityFrontagePoint(site, 0f);
                if (!ExcludeAt(sides, point,
                               ParkingBlockPlan.GateWidth * 0.5f + ApproachMargin))
                    parkingWithoutParking++;
            }
            float afterParking = Length(sides);

            int fuelApproaches = 0;
            float beforeFuel = afterParking;
            foreach (var site in core.FuelSites)
                foreach (float along in new[] { -FuelStation.MouthX, FuelStation.MouthX })
                {
                    fuelApproaches++;
                    var point = AmenityFrontagePoint(site, along);
                    if (!ExcludeAt(sides, point,
                                   FuelStation.MouthHalf + ApproachMargin))
                        fuelWithoutParking++;
                }
            float afterFuel = Length(sides);
            CheckBudget(elapsed, "amenity approaches");

            int residentialYardEntrances = 0;
            float beforeResidential = afterFuel;
            foreach (var recipe in core.ResidentialBlocks.Blocks)
            {
                CheckBudget(elapsed, "residential yard entrances");
                if (recipe?.Plan?.Accesses == null) continue;
                foreach (var access in recipe.Plan.Accesses)
                {
                    if (access == null || !access.Vehicle) continue;
                    residentialYardEntrances++;
                    if (!TryResidentialAccessPoint(
                            recipe, access.Side, access.At, core.Frame, out var point))
                    {
                        gateFailures.Add(
                            $"{recipe.Id}: invalid residential vehicle access " +
                            $"side {access.Side}, cell {access.At}");
                        continue;
                    }
                    if (!ExcludeAt(sides, new Vector2(point.x, point.z),
                                   ResidentialLot.Cell * 0.5f))
                        residentialWithoutParking++;
                }
            }
            float legalMetres = Length(sides);

            if (footprintFallbacks != 0)
                gateFailures.Add(
                    $"{footprintFallbacks} kerb-furniture footprint fallback(s) used");
            if (ResidentialBlocks.Dressed)
                gateFailures.Add(
                    "generated residential dressing is enabled, but its composed bus " +
                    "stops cannot be measured from the plan-only census");
            if (seed == 1987 &&
                (sides.Count != 978 || hydrants != 17 || stops != 0 ||
                 parkingApproaches != 3 || fuelApproaches != 0 ||
                 residentialYardEntrances != 162 ||
                 furnitureWithoutParking != 4 || parkingWithoutParking != 0 ||
                 fuelWithoutParking != 0 || residentialWithoutParking != 0 ||
                 Mathf.Abs(rawMetres - 60626f) > 0.01f ||
                 Mathf.Abs((beforeEnds - afterEnds) - 13692f) > 0.01f ||
                 Mathf.Abs((beforeHydrants - afterHydrants) - 86.305f) > 0.01f ||
                 Mathf.Abs((beforeParking - afterParking) - 33f) > 0.01f ||
                 Mathf.Abs((beforeResidential - legalMetres) - 641f) > 0.01f ||
                 Mathf.Abs(legalMetres - 46173.695f) > 0.01f))
                gateFailures.Add(
                    "seed 1987 kerb-source baseline changed: expected 978 parking " +
                    "sides / 17 hydrants / 0 authored bus stops / 3 parking approaches / " +
                    "0 fuel approaches / 162 residential yard entrances / 46173.695 legal m, " +
                    $"measured {sides.Count} / {hydrants} / {stops} / {parkingApproaches} / " +
                    $"{fuelApproaches} / {residentialYardEntrances} / {legalMetres:0.###}");

            float pitch = TypicalCarHalfLength * 2f + KerbCars.Gap;
            int capacity = 0;
            foreach (var side in sides)
                foreach (var interval in side.Legal)
                    capacity += Mathf.FloorToInt(interval.Length / pitch);
            if (seed == 1987 && capacity != 7168)
                gateFailures.Add(
                    $"seed 1987 kerb slot baseline changed: expected 7168, measured {capacity}");

            var intervalManifest = new StableDigest();
            foreach (var side in sides)
            {
                intervalManifest.Add(side.Road.Index);
                intervalManifest.Add(side.Side);
                intervalManifest.Add(side.Legal.Count);
                foreach (var interval in side.Legal)
                {
                    intervalManifest.Add(Mathf.RoundToInt(interval.From * 1000f));
                    intervalManifest.Add(Mathf.RoundToInt(interval.To * 1000f));
                }
            }
            string intervalDigest = intervalManifest.Hex;
            if (!string.Equals(intervalDigest, CanonicalKerbIntervalDigest,
                               StringComparison.Ordinal))
                gateFailures.Add(
                    "seed 1987 per-road legal-kerb manifest changed: expected " +
                    $"{CanonicalKerbIntervalDigest}, measured {intervalDigest}");
            CheckBudget(elapsed, "kerb interval manifest");

            var rows = includeRows
                ? sides.Select(side => (object)new
                {
                    road = side.Road.Index,
                    side = side.Side < 0 ? "A" : "B",
                    lengthMetres = Math.Round(side.Road.Length, 3),
                    legal = side.Legal.Select(interval => new
                    {
                        from = Math.Round(interval.From, 3),
                        to = Math.Round(interval.To, 3),
                        metres = Math.Round(interval.Length, 3),
                    }).ToArray(),
                }).ToArray()
                : null;

            return new
            {
                parkingSides = sides.Count,
                rawMetres = Math.Round(rawMetres, 3),
                excludedBySevenMetreEnds = Math.Round(beforeEnds - afterEnds, 3),
                crossingBufferAdditionalMetres = Math.Round(afterEnds - afterCrossings, 3),
                hydrants,
                hydrantExcludedAdditionalMetres = Math.Round(beforeHydrants - afterHydrants, 3),
                busStops = stops,
                busStopExcludedAdditionalMetres = Math.Round(beforeStops - afterStops, 3),
                parkingApproaches,
                parkingApproachExcludedAdditionalMetres =
                    Math.Round(beforeParking - afterParking, 3),
                fuelApproaches,
                fuelApproachExcludedAdditionalMetres = Math.Round(beforeFuel - afterFuel, 3),
                residentialYardEntrances,
                residentialYardEntranceExcludedAdditionalMetres =
                    Math.Round(beforeResidential - legalMetres, 3),
                sourcesWithoutAdjacentParking = new
                {
                    furniture = furnitureWithoutParking,
                    parkingApproaches = parkingWithoutParking,
                    fuelApproaches = fuelWithoutParking,
                    residentialYardEntrances = residentialWithoutParking,
                },
                footprintFallbacks,
                residentialDressingEnabled = ResidentialBlocks.Dressed,
                legalMetres = Math.Round(legalMetres, 3),
                intervalDigest,
                slotPitchMetres = pitch,
                slotCapacity = capacity,
                slotCountAt60Percent = Mathf.RoundToInt(capacity * 0.6f),
                source = "authored hydrants/stops come from SidewalkPlan; parking/fuel approaches and residential yard entrances come from plan data",
                rows,
            };
        }

        static Vector2 AmenityFrontagePoint(CoreAmenityLayout.Site site, float along)
        {
            switch (site.Entry)
            {
                case ParkingEntrySide.East:
                    return new Vector2(site.Box.xMax, site.Box.center.y + along);
                case ParkingEntrySide.North:
                    return new Vector2(site.Box.center.x - along, site.Box.yMax);
                case ParkingEntrySide.West:
                    return new Vector2(site.Box.xMin, site.Box.center.y - along);
                default:
                    return new Vector2(site.Box.center.x + along, site.Box.yMin);
            }
        }

        static void AddKerbSide(List<KerbSide> sides, Carriageway road, int side,
                                ref float rawMetres)
        {
            var kerb = new KerbSide { Road = road, Side = side };
            kerb.Legal.Add(new Interval(0f, road.Length));
            sides.Add(kerb);
            rawMetres += road.Length;
        }

        static SidewalkPlan ReadAuthoredKerbFurniture(CoreDistrict core,
                                                      List<string> gateFailures,
                                                      out int fallbacks)
        {
            var plan = new SidewalkPlan();
            fallbacks = 0;
            foreach (var block in core.LayoutBlocks)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CoreLayout.BlocksDir + block.Name + ".prefab");
                if (prefab == null)
                {
                    gateFailures.Add(block.Name + ": authored block prefab missing");
                    continue;
                }
                // The direct children are the authored instance list. Descendants are
                // implementation detail inside each source prefab, not more furniture.
                foreach (Transform instance in prefab.transform)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                    if (source == null) continue;
                    string tag = KerbFurnitureTag(source.name);
                    if (tag == null) continue;

                    var prefabLocal = prefab.transform.InverseTransformPoint(instance.position);
                    var cityLocal = block.Position + block.Rotation * prefabLocal;
                    var world = core.Frame.ToWorld(cityLocal);
                    float localYaw = Mathf.DeltaAngle(prefab.transform.eulerAngles.y,
                                                     instance.eulerAngles.y);
                    float yaw = core.Frame.yaw + block.Yaw + localYaw;
                    if (!SidewalkPlan.Footprint(source, world, yaw, out var box))
                    {
                        box = SidewalkPlan.Make(new Vector2(world.x, world.z), yaw,
                                                Vector2.one * 0.1f, true);
                        fallbacks++;
                    }
                    box.SourceName = source.name;
                    plan.Take(box);
                }
            }
            return plan;
        }

        static string KerbFurnitureTag(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (source.IndexOf("Hydrant", StringComparison.OrdinalIgnoreCase) >= 0)
                return "hydrant";
            if (source.IndexOf("BusStop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("Bus_Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                return "bus-stop";
            return null;
        }

        static bool ExcludeAt(List<KerbSide> sides, Vector2 source, float radius)
        {
            KerbSide best = null;
            float bestS = 0f;
            float bestDistance = float.MaxValue;
            var point = new Vector3(source.x, 0f, source.y);
            foreach (var side in sides)
            {
                side.Road.Project(point, out float projected, out float across);
                int pointSide = across >= 0f ? 1 : -1;
                if (pointSide != side.Side) continue;
                float s = Mathf.Clamp(projected, 0f, side.Road.Length);
                var kerb = side.Road.Pose(s, side.Side * side.Road.HalfRoad);
                float distance = Vector3.Distance(point, kerb);
                if (distance > 10f || distance >= bestDistance) continue;
                best = side;
                bestS = s;
                bestDistance = distance;
            }
            if (best == null) return false;
            Remove(best.Legal, bestS - radius, bestS + radius);
            return true;
        }

        static float Length(List<KerbSide> sides)
        {
            float length = 0f;
            foreach (var side in sides)
                foreach (var interval in side.Legal)
                    length += interval.Length;
            return length;
        }

        static void Remove(List<Interval> intervals, float from, float to)
        {
            if (to <= from) return;
            for (int index = intervals.Count - 1; index >= 0; index--)
            {
                var interval = intervals[index];
                float cutFrom = Mathf.Max(interval.From, from);
                float cutTo = Mathf.Min(interval.To, to);
                if (cutTo <= cutFrom) continue;
                intervals.RemoveAt(index);
                bool before = interval.From < cutFrom;
                if (before)
                    intervals.Insert(index, new Interval(interval.From, cutFrom));
                if (cutTo < interval.To)
                    intervals.Insert(index + (before ? 1 : 0),
                                     new Interval(cutTo, interval.To));
            }
        }

        static object[] MeasureCrowdCurve(List<PedLink> links, int seed,
                                          System.Diagnostics.Stopwatch elapsed)
        {
            if (PedestrianAgent.Everyone.Count != 0 || CivilianAgent.All.Count != 0)
                throw new InvalidOperationException(
                    "The crowd benchmark requires an empty stopped editor; live walkers were found.");

            var sidewalks = links.Where(link => link != null && !link.Gated && link.Length >= 3f)
                .ToArray();
            if (sidewalks.Length == 0)
                throw new InvalidOperationException(
                    "The dealt city published no benchmarkable pavement links.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_01.prefab");
            if (prefab == null)
                throw new InvalidOperationException("The census crowd body prefab is missing.");
            var walk = CrewKit.StockWalk;
            var idle = CrewKit.StockIdle;
            if (walk == null || idle == null)
                throw new InvalidOperationException("The census crowd walk/idle clips are missing.");

            var randomState = UnityEngine.Random.state;
            var preview = EditorSceneManager.NewPreviewScene();
            var results = new List<object>(CrowdCounts.Length);
            var root = new GameObject("People census preview")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            SceneManager.MoveGameObjectToScene(root, preview);
            var agents = new List<CivilianAgent>(CrowdCounts[CrowdCounts.Length - 1]);
            var life = new CityLife();
            var clips = new PedClips { Walk = walk, Idle = idle };
            var variety = new System.Random(unchecked(seed * 486187739));
            int idsBefore = (int)CrowdIds.GetValue(null);
            bool listeningBefore = (bool)CivilianListening.GetValue(null);
            float gawkScanBefore = (float)CivilianGawkScan.GetValue(null);
            object alarmOnShotBefore = AlarmOnShot.GetValue(null);
            var roadWalkersBefore = StreetTraffic.Walkers.ToArray();
            // Setup and TickCrowd touch the same global registries the live city uses.
            // Snapshot the non-cache state and invalidate the derived cell cache afterwards,
            // so running a read-only editor audit cannot change the next Play run's IDs,
            // alarm subscription, random stream or crowd picture.
            try
            {
                foreach (int count in CrowdCounts)
                {
                    CheckBudget(elapsed, $"crowd benchmark setup ({count})");
                    GrowCrowd(root.transform, agents, prefab, clips, variety, life,
                              sidewalks, count, seed);
                    results.Add(MeasureCrowdCount(agents, count));
                    CheckBudget(elapsed, $"crowd benchmark ({count})");
                }
            }
            finally
            {
                TickTimer.Reset();
                UnityEngine.Random.state = randomState;
                StreetTraffic.Walkers.Clear();
                for (int index = agents.Count - 1; index >= 0; index--)
                {
                    CivilianAgent.All.Remove(agents[index]);
                    agents[index].Dispose();
                }
                CrowdIds.SetValue(null, idsBefore);
                CivilianListening.SetValue(null, listeningBefore);
                CivilianGawkScan.SetValue(null, gawkScanBefore);
                AlarmOnShot.SetValue(null, alarmOnShotBefore);
                ((System.Collections.IDictionary)CrowdCells.GetValue(null)).Clear();
                CrowdSpareCells.GetValue(null).GetType().GetMethod("Clear")?.Invoke(
                    CrowdSpareCells.GetValue(null), null);
                CrowdFrame.SetValue(null, -1);
                StreetTraffic.Walkers.AddRange(roadWalkersBefore);
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return results.ToArray();
        }

        static void CheckBudget(System.Diagnostics.Stopwatch elapsed, string stage)
        {
            if (elapsed.Elapsed.TotalSeconds < CensusWorkBudgetSeconds) return;
            throw new TimeoutException(
                $"NPC-001 census stopped after {elapsed.Elapsed.TotalSeconds:0.###} s " +
                $"during {stage}; its work budget is {CensusWorkBudgetSeconds:0} s " +
                "inside the 30-second command deadline.");
        }

        static void GrowCrowd(Transform root, List<CivilianAgent> agents,
                              GameObject prefab, PedClips clips,
                              System.Random variety, CityLife life, PedLink[] sidewalks,
                              int count, int seed)
        {
            UnityEngine.Random.InitState(unchecked(seed * 7919 + count * 104729));
            while (agents.Count < count)
            {
                int index = agents.Count;
                var link = sidewalks[UnityEngine.Random.Range(0, sidewalks.Length)];
                var go = UnityEngine.Object.Instantiate(prefab, root);
                go.name = "census pedestrian " + index;
                var agent = new CivilianAgent
                {
                    Speed = UnityEngine.Random.Range(1.25f, 1.85f),
                };
                // Own it before any setup call which could throw, so the caller's finally
                // sweep can remove a half-wired agent from the global crowd as well.
                agents.Add(agent);
                agent.Init(go.transform, CrewKit.ForCrowd(clips, variety), link,
                           UnityEngine.Random.value * link.Length * 0.9f);
                agent.Setup(life);
            }
        }

        static object MeasureCrowdCount(List<CivilianAgent> agents, int count)
        {
            for (int frame = 0; frame < BenchmarkWarmupFrames; frame++)
                TickCrowdFrame(agents);

            TickTimer.Reset();
            for (int frame = 0; frame < BenchmarkFrames; frame++)
            {
                CrowdFrame.SetValue(null, -1);
                TickTimer.Frame();
                for (int index = 0; index < agents.Count; index++)
                    agents[index].TickCivilian(1f / 60f);
                TickTimer.Mark(3, "civilians");
                CivilianAgent.TickCrowd(1f / 60f);
                TickTimer.Mark(4, "crowd");
            }

            double civilians = TickTimer.MillisecondsPerFrame(3);
            double crowd = TickTimer.MillisecondsPerFrame(4);
            return new
            {
                bodies = count,
                civiliansMsPerFrame = Math.Round(civilians, 4),
                crowdMsPerFrame = Math.Round(crowd, 4),
                totalMsPerFrame = Math.Round(civilians + crowd, 4),
            };
        }

        static void TickCrowdFrame(List<CivilianAgent> agents)
        {
            CrowdFrame.SetValue(null, -1);
            for (int index = 0; index < agents.Count; index++)
                agents[index].TickCivilian(1f / 60f);
            CivilianAgent.TickCrowd(1f / 60f);
        }

        static bool TryLand(Vector3 point, List<PedLink> links,
                            out PedLink best, out float bestT, out float distance)
        {
            best = null;
            bestT = 0f;
            distance = 0f;
            float bestDistanceSq = DoorReach * DoorReach;
            foreach (var link in links)
            {
                if (link == null || link.Gated || link.Length < 3f) continue;
                var along = link.To.Pos - link.From.Pos;
                float t = Mathf.Clamp(Vector3.Dot(point - link.From.Pos,
                    along / link.Length), 0.5f, link.Length - 0.5f);
                var landed = link.From.Pos + along * (t / link.Length);
                float distanceSq = (landed - point).sqrMagnitude;
                if (distanceSq >= bestDistanceSq) continue;
                bestDistanceSq = distanceSq;
                best = link;
                bestT = t;
            }
            if (best == null) return false;
            distance = Mathf.Sqrt(bestDistanceSq);
            return true;
        }
    }
}
