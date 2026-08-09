using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// What one park block contains and where: archetype, entrances, path spines, zones and
    /// every planting and prop station. Pure geometry - this class instantiates nothing, the
    /// discipline HedgeLayout, ParkingLayout and IndustrialLayout are written under, and for
    /// IndustrialLayout's exact reason: the dresser spawns prefabs over this plan, the ground
    /// painter draws the walks from the SAME polylines, and the nav builder hangs the pedestrian
    /// path graph on them. Three consumers, one answer - a walk whose paint, lamps and nav nodes
    /// each came from their own idea of the curve would shear apart at the first retune.
    ///
    /// Deterministic in (seed, blockId) ALONE - the BlockLots contract, on its own
    /// SeedOffsets.Park stream. The old ParkDresser drew from BlockBuilder's shared Buildings
    /// stream, so retuning a shrub count re-laid every block built after the park.
    ///
    /// Every figure in this class is WORLD-space. Constants derived from the tile module are
    /// written authored-times-CityGrid.TileScale like the rest of the codebase; Tuning values
    /// are plain world metres because they are design knobs, not tile measurements.
    ///
    /// The spines it plans run ANCHOR to ANCHOR, not gate to gate. An anchor is a point where
    /// the road network's own sidewalk paths start or end - the only places the pack's tile
    /// linking can join two graphs (Tile.GetNextPaths matches a path's last node to a NEIGHBOUR
    /// tile's first nodes, never its own tile's). tile-park's walks ended at cell-edge midpoints
    /// where an ordinary straight has no node at all - its pavement endpoints sit at the tile
    /// corners, sidewalk-offset in - so the old park only ever joined the city where a crosswalk
    /// happened to be rolled. Planning to the measured anchor positions is what fixes that.
    /// The gate is where a spine crosses the hedge line, recorded per entrance so the hedge can
    /// open exactly there; on a crosswalk side the anchor lies INSIDE the hedge (the crossing
    /// walk already pierces the block line) and the gate sits outward of it on the same line.
    /// </summary>
    public static class ParkLayout
    {
        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        /// <summary>
        /// Sample step for every spine polyline. Below the pack's own coarsest node spacing and
        /// far above HumanBehavior's 0.75m arrival radius, so a curve reads as a curve without
        /// the follower consuming nodes faster than it can turn.
        /// </summary>
        public const float SampleStep = 3f;

        public enum Archetype { Formal, Informal, Civic }

        public enum SpineKind { Main, Secondary, PlazaRing }

        public enum ZoneKind { Lawn, Grove, Feature, ScreenBelt, Parterre, Pond }

        public enum StationKind
        {
            Tree, AccentTree, DeadTree, Shrub, Flower,
            Lamp, Bench, Bin, Boulder, GatePost,
            Centrepiece, Monument, Knoll, Carousel, PondRim,
        }

        public enum SecondaryFeature { None, Parterre, Pond, Carousel }

        /// <summary>
        /// A world-XZ rectangle. Its own struct rather than UnityEngine.Rect because every
        /// consumer works in the XZ plane with Vector2 and because it rides into the saved scene
        /// on the ParkGrounds marker - see IndustrialLayout.Rect for why [Serializable] matters.
        /// </summary>
        [System.Serializable]
        public struct Rect
        {
            public Vector2 Min;
            public Vector2 Max;

            public Rect(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
            }

            public Vector2 Centre => (Min + Max) * 0.5f;
            public Vector2 Size => Max - Min;
            public float Area => Mathf.Max(0f, Max.x - Min.x) * Mathf.Max(0f, Max.y - Min.y);

            /// <summary>Margin is the room the thing at <paramref name="p"/> needs around it.</summary>
            public bool Contains(Vector2 p, float margin = 0f) =>
                p.x - margin >= Min.x && p.x + margin <= Max.x &&
                p.y - margin >= Min.y && p.y + margin <= Max.y;

            public bool Overlaps(Rect other) =>
                Min.x < other.Max.x && other.Min.x < Max.x &&
                Min.y < other.Max.y && other.Min.y < Max.y;

            public Rect Deflated(float by) =>
                new(new Vector2(Min.x + by, Min.y + by), new Vector2(Max.x - by, Max.y - by));
        }

        /// <summary>
        /// A point where the road network's sidewalk graph can be joined - the world position of
        /// an actual first/last path node on a facing road tile. OnBoundary marks a crosswalk's
        /// crossing endpoint, which lands exactly on the shared cell boundary; everything else
        /// is a tile-corner node a sidewalk-offset in from the kerb line.
        /// </summary>
        public struct EntranceAnchor
        {
            public Vector2 Pos;
            public int Side;
            public bool OnBoundary;
            public bool Avenue;
        }

        [System.Serializable]
        public struct Entrance
        {
            /// <summary>0 East, 1 North, 2 West, 3 South - HedgeLayout's leg order.</summary>
            public int Side;

            /// <summary>Where the hedge opens, on the block line.</summary>
            public Vector2 Gate;

            /// <summary>The road-graph node the spine starts at - see the class doc.</summary>
            public Vector2 Anchor;

            public bool AnchorOnBoundary;
            public bool Avenue;
        }

        [System.Serializable]
        public struct Spine
        {
            /// <summary>Sampled at ~SampleStep. A PlazaRing repeats its first point last.</summary>
            public Vector2[] Points;

            public float Width;
            public SpineKind Kind;
        }

        [System.Serializable]
        public struct Zone
        {
            public Rect Area;
            public ZoneKind Kind;
        }

        [System.Serializable]
        public struct Station
        {
            public Vector2 Pos;
            public float Yaw;
            public float Scale;

            /// <summary>Footprint radius - what the overlap sweep and the gizmos read.</summary>
            public float Radius;

            public StationKind Kind;

            /// <summary>0 primary species, 1 secondary, 2 accent. Meaningless off tree kinds.</summary>
            public int SpeciesSlot;

            /// <summary>Higher survives the overlap sweep and the density trim.</summary>
            public int Priority;
        }

        /// <summary>
        /// Measured size of one station kind: how much ground it reserves and how tall the
        /// category may be. Defaults are educated guesses; the dresser overwrites them from
        /// PrefabBounds before planning, so the plan spaces things by what the prefabs really
        /// measure. Height is the MEASURED tallest prefab of the kind; a kind whose Height
        /// exceeds MaxHeight is dropped whole, with a warning - the spec's scale sanity check.
        /// </summary>
        [System.Serializable]
        public struct Footprint
        {
            public float Radius;
            public float Height;
            public float MaxHeight;
        }

        /// <summary>
        /// Every knob, in world metres. Lives inside ParkConfig; the generator runs on Default
        /// when no asset is wired, the IndustrialLotConfig contract.
        /// </summary>
        [System.Serializable]
        public struct Tuning
        {
            [Header("Archetype weights")]
            public float formalWeight;
            public float informalWeight;
            public float civicWeight;

            [Header("Paths")]
            public float mainPathWidth;
            public float secondaryPathWidth;
            public float plazaRadius;

            [Header("Entrances")]
            public int minEntrances;
            public int maxEntrances;
            public float gateHalfWidth;

            [Header("Lamps")]
            public float lampSpacingMin;
            public float lampSpacingMax;
            public float lampMinSeparation;

            [Header("Planting")]
            public float beltDepth;
            public float beltTreeSpacing;
            public int groveMin;
            public int groveMax;
            public float groveRadius;
            public float treeScaleMin;
            public float treeScaleMax;
            public float alleeSpacing;
            public float accentShare;
            public int maxDeadTrees;

            [Header("Benches and bins")]
            public int benchMin;
            public int benchMax;
            public int maxBins;

            [Header("Secondary features")]
            public float knollChance;
            public float knollScaleMin;
            public float knollScaleMax;
            public float carouselChance;

            [Header("Density")]
            public float densityMinPer100;
            public float densityMaxPer100;
            public int maxStations;

            public static Tuning Default => new()
            {
                formalWeight = 1f,
                informalWeight = 1.2f,
                civicWeight = 0.8f,

                mainPathWidth = 3.5f,
                secondaryPathWidth = 2f,
                plazaRadius = 7f,

                minEntrances = 2,
                maxEntrances = 4,
                gateHalfWidth = 3f,

                lampSpacingMin = 12f,
                lampSpacingMax = 16f,
                lampMinSeparation = 8f,

                beltDepth = 4f,
                beltTreeSpacing = 6.5f,
                groveMin = 5,
                groveMax = 12,
                groveRadius = 8f,
                treeScaleMin = 0.85f,
                treeScaleMax = 1.15f,
                alleeSpacing = 7.8f,
                accentShare = 0.1f,
                maxDeadTrees = 2,

                benchMin = 2,
                benchMax = 4,
                maxBins = 4,

                knollChance = 0.5f,
                knollScaleMin = 0.15f,
                knollScaleMax = 0.2f,
                carouselChance = 0.12f,

                densityMinPer100 = 0.5f,
                densityMaxPer100 = 1.8f,
                maxStations = 220,
            };
        }

        /// <summary>Default footprints by StationKind ordinal - the dresser overwrites these.</summary>
        public static Footprint[] DefaultFootprints()
        {
            var footprints = new Footprint[15];
            footprints[(int)StationKind.Tree] = new Footprint { Radius = 2.6f, Height = 7f, MaxHeight = 99f };
            footprints[(int)StationKind.AccentTree] = new Footprint { Radius = 2.2f, Height = 6f, MaxHeight = 99f };
            footprints[(int)StationKind.DeadTree] = new Footprint { Radius = 1.6f, Height = 5f, MaxHeight = 99f };
            footprints[(int)StationKind.Shrub] = new Footprint { Radius = 0.9f, Height = 1.6f, MaxHeight = 3f };
            footprints[(int)StationKind.Flower] = new Footprint { Radius = 0.45f, Height = 0.8f, MaxHeight = 1.5f };
            footprints[(int)StationKind.Lamp] = new Footprint { Radius = 0.5f, Height = 6.7f, MaxHeight = 8f };
            footprints[(int)StationKind.Bench] = new Footprint { Radius = 1.1f, Height = 1.1f, MaxHeight = 2f };
            footprints[(int)StationKind.Bin] = new Footprint { Radius = 0.45f, Height = 1.2f, MaxHeight = 2f };
            footprints[(int)StationKind.Boulder] = new Footprint { Radius = 1.4f, Height = 1.8f, MaxHeight = 3f };
            footprints[(int)StationKind.GatePost] = new Footprint { Radius = 0.6f, Height = 3f, MaxHeight = 7f };
            footprints[(int)StationKind.Centrepiece] = new Footprint { Radius = 3.2f, Height = 3.5f, MaxHeight = 6f };
            footprints[(int)StationKind.Monument] = new Footprint { Radius = 1.6f, Height = 5.5f, MaxHeight = 8f };
            footprints[(int)StationKind.Knoll] = new Footprint { Radius = 3f, Height = 1.2f, MaxHeight = 2f };
            footprints[(int)StationKind.Carousel] = new Footprint { Radius = 5.5f, Height = 9f, MaxHeight = 12f };
            footprints[(int)StationKind.PondRim] = new Footprint { Radius = 0.8f, Height = 1f, MaxHeight = 2f };
            return footprints;
        }

        public sealed class Plan
        {
            public Archetype Archetype;
            public SecondaryFeature Secondary;
            public Rect Interior;
            public Vector2 PlazaCentre;
            public float PlazaRadius;
            public int PrimarySpecies;
            public int SecondarySpecies;
            public int AccentSpecies;
            public readonly List<Entrance> Entrances = new();
            public readonly List<Spine> Spines = new();
            public readonly List<Zone> Zones = new();
            public readonly List<Station> Stations = new();
            public readonly List<string> Warnings = new();
        }

        /// <summary>The four outward directions - the exact order HedgeLayout walks.</summary>
        static readonly Vector2Int[] Legs =
        {
            new(1, 0), new(0, 1), new(-1, 0), new(0, -1),
        };

        /// <summary>
        /// Synthesizes anchors from the measured road-tile cross-section, for tests and for a
        /// build with no road instances to read: an ordinary straight's pavement endpoints sit at
        /// the tile corners, SidewalkOffset in from the road centreline. What it cannot know
        /// offline is which straight was rolled as a crosswalk, so it never emits OnBoundary
        /// anchors - the nav builder's live read does that.
        /// </summary>
        public static List<EntranceAnchor> FallbackAnchors(
            CityGrid grid, List<Vector2Int> cells, float clearance, float mainClearance)
        {
            var anchors = new List<EntranceAnchor>();
            if (cells == null || cells.Count == 0)
                return anchors;

            var inBlock = new HashSet<Vector2Int>(cells);

            foreach (var cell in cells)
            {
                for (var side = 0; side < 4; side++)
                {
                    var leg = Legs[side];
                    var neighbour = cell + leg;
                    if (inBlock.Contains(neighbour) || !grid.IsRoad(neighbour.x, neighbour.y))
                        continue;

                    var avenue = grid.IsMainRoad(neighbour.x, neighbour.y);
                    var offset = CityGrid.CellSize
                        - (avenue ? CityGrid.MainSidewalkOffset : CityGrid.SidewalkOffset);

                    var centre = To2(grid.CellToWorld(cell));
                    var outward = new Vector2(leg.x, leg.y);
                    var tangent = new Vector2(-leg.y, leg.x);
                    var half = CityGrid.CellSize * 0.5f;

                    foreach (var sign in new[] { -1f, 1f })
                    {
                        var pos = centre + outward * offset + tangent * (sign * half);
                        if (ContainsAnchor(anchors, pos))
                            continue;
                        anchors.Add(new EntranceAnchor
                        {
                            Pos = pos, Side = side, OnBoundary = false, Avenue = avenue,
                        });
                    }
                }
            }

            return anchors;
        }

        /// <summary>
        /// Plans one park block. <paramref name="anchors"/> may be null or empty - the fallback
        /// set is derived from the grid. <paramref name="speciesPool"/> is how many tree species
        /// buckets the palette offers; the plan deals in slot indices, never prefabs.
        /// </summary>
        public static Plan ForBlock(
            CityGrid grid, List<Vector2Int> cells,
            float clearance, float mainClearance, float mapEdgeOffset,
            IReadOnlyList<EntranceAnchor> anchors,
            int speciesPool, int seed, int blockId,
            Tuning tuning, Footprint[] footprints = null)
        {
            var plan = new Plan();
            if (cells == null || cells.Count == 0)
                return plan;

            footprints ??= DefaultFootprints();
            if (anchors == null || anchors.Count == 0)
                anchors = FallbackAnchors(grid, cells, clearance, mainClearance);

            var rng = new System.Random(seed + SeedOffsets.Park + blockId * BlockStride);
            var inBlock = new HashSet<Vector2Int>(cells);

            // ---- boundary: hedge lines per side, most conservative where cells disagree ----
            var side = MeasureSides(grid, cells, inBlock, clearance, mainClearance, mapEdgeOffset);
            plan.Interior = side.Interior;

            // ---- fixed draw block: every roll that must not depend on geometry outcomes ----
            var archetypeRoll = (float)rng.NextDouble();
            var entranceRoll = rng.Next(tuning.minEntrances, tuning.maxEntrances + 1);
            var sideJitter = new float[4];
            for (var i = 0; i < 4; i++)
                sideJitter[i] = (float)rng.NextDouble() * 0.5f;
            var bendRolls = new float[4];
            for (var i = 0; i < 4; i++)
                bendRolls[i] = (float)rng.NextDouble();
            var secondaryRoll = (float)rng.NextDouble();
            var knollRoll = (float)rng.NextDouble();
            var primaryRoll = rng.Next(Mathf.Max(1, speciesPool));
            var secondarySpeciesRoll = rng.Next(Mathf.Max(1, speciesPool));
            var accentRoll = rng.Next(Mathf.Max(1, speciesPool));

            plan.Archetype = RollArchetype(archetypeRoll, cells.Count, tuning);
            AssignSpecies(plan, speciesPool, primaryRoll, secondarySpeciesRoll, accentRoll);

            // ---- entrances ----
            ChooseEntrances(plan, side, anchors, entranceRoll, sideJitter, tuning);
            if (plan.Entrances.Count == 0)
            {
                plan.Warnings.Add("park has no road-facing side with an anchor; no entrances");
                return plan;
            }

            // ---- spines ----
            switch (plan.Archetype)
            {
                case Archetype.Formal:
                    BuildFormalSpines(plan, tuning);
                    break;
                case Archetype.Informal:
                    BuildInformalSpines(plan, bendRolls, tuning);
                    break;
                default:
                    BuildCivicSpines(plan, tuning);
                    break;
            }

            // ---- secondary feature ----
            plan.Secondary = RollSecondary(plan.Archetype, secondaryRoll, tuning);

            // ---- zones ----
            BuildZones(plan, tuning);

            // ---- stations ----
            var tallestTree = Mathf.Max(
                footprints[(int)StationKind.Tree].Height,
                footprints[(int)StationKind.AccentTree].Height);
            PlaceCentrepiece(plan, rng, footprints);
            PlaceGatePosts(plan, footprints, tuning);
            PlaceBeltTrees(plan, rng, side, footprints, tuning);
            PlaceAllees(plan, footprints, tuning);
            PlaceGrove(plan, rng, footprints, tuning);
            PlaceAccentAndDead(plan, rng, footprints, tuning);
            PlaceSecondaryFeature(plan, rng, footprints, tuning, knollRoll);
            PlaceBenchesAndBins(plan, rng, footprints, tuning);
            PlaceLamps(plan, rng, footprints, tuning);

            // ---- sanity ----
            SanityHeight(plan, footprints, tallestTree);
            SanityOverlaps(plan);
            SanityPathExclusion(plan);
            SanityLawn(plan);
            SanityDensity(plan, rng, footprints, tuning);

            return plan;
        }

        // ================================================================== boundary

        struct SideInfo
        {
            /// <summary>Absolute world coordinate of the hedge line, indexed E,N,W,S.</summary>
            public float[] Line;

            public bool[] HasRoad;
            public bool[] Avenue;
            public Rect Interior;

            /// <summary>Along-axis extent of road-facing edge cells per side (min, max).</summary>
            public Vector2[] RoadSpan;
        }

        static SideInfo MeasureSides(
            CityGrid grid, List<Vector2Int> cells, HashSet<Vector2Int> inBlock,
            float clearance, float mainClearance, float mapEdgeOffset)
        {
            var info = new SideInfo
            {
                Line = new float[4],
                HasRoad = new bool[4],
                Avenue = new bool[4],
                RoadSpan = new Vector2[4],
            };

            for (var s = 0; s < 4; s++)
            {
                info.Line[s] = float.NaN;
                info.RoadSpan[s] = new Vector2(float.MaxValue, float.MinValue);
            }

            foreach (var cell in cells)
            {
                var centre = To2(grid.CellToWorld(cell));
                for (var s = 0; s < 4; s++)
                {
                    var leg = Legs[s];
                    var neighbour = cell + leg;
                    if (inBlock.Contains(neighbour))
                        continue;

                    var offset = HedgeLayout.SideOffset(
                        grid, neighbour.x, neighbour.y, clearance, mainClearance, mapEdgeOffset);
                    var axisCentre = s % 2 == 0 ? centre.x : centre.y;
                    var line = axisCentre + (s < 2 ? offset : -offset);

                    // Most conservative wins: smallest reach outward on +sides, largest on -sides.
                    if (float.IsNaN(info.Line[s]))
                        info.Line[s] = line;
                    else
                        info.Line[s] = s < 2
                            ? Mathf.Min(info.Line[s], line)
                            : Mathf.Max(info.Line[s], line);

                    if (!grid.IsRoad(neighbour.x, neighbour.y))
                        continue;

                    info.HasRoad[s] = true;
                    info.Avenue[s] |= grid.IsMainRoad(neighbour.x, neighbour.y);

                    var along = s % 2 == 0 ? centre.y : centre.x;
                    var half = CityGrid.CellSize * 0.5f;
                    info.RoadSpan[s] = new Vector2(
                        Mathf.Min(info.RoadSpan[s].x, along - half),
                        Mathf.Max(info.RoadSpan[s].y, along + half));
                }
            }

            info.Interior = new Rect(
                new Vector2(info.Line[2], info.Line[3]),
                new Vector2(info.Line[0], info.Line[1]));
            return info;
        }

        // ================================================================== rolls

        static Archetype RollArchetype(float roll, int cellCount, Tuning tuning)
        {
            // The civic square's ring wants a compact block; on a sprawling park it reads as a
            // running track. Weight rather than forbid the others.
            var civic = cellCount <= 2 ? tuning.civicWeight : 0f;
            var total = tuning.formalWeight + tuning.informalWeight + civic;
            if (total <= 0f)
                return Archetype.Informal;

            var at = roll * total;
            if (at < tuning.formalWeight)
                return Archetype.Formal;
            return at < tuning.formalWeight + tuning.informalWeight
                ? Archetype.Informal
                : Archetype.Civic;
        }

        static void AssignSpecies(Plan plan, int pool, int primary, int secondary, int accent)
        {
            pool = Mathf.Max(1, pool);
            plan.PrimarySpecies = primary % pool;
            plan.SecondarySpecies = pool > 1 && secondary % pool == plan.PrimarySpecies
                ? (secondary + 1) % pool
                : secondary % pool;
            plan.AccentSpecies = accent % pool;
            if (pool > 2 && (plan.AccentSpecies == plan.PrimarySpecies
                             || plan.AccentSpecies == plan.SecondarySpecies))
                plan.AccentSpecies = (plan.AccentSpecies + 1) % pool;
            if (pool > 2 && (plan.AccentSpecies == plan.PrimarySpecies
                             || plan.AccentSpecies == plan.SecondarySpecies))
                plan.AccentSpecies = (plan.AccentSpecies + 1) % pool;
        }

        static SecondaryFeature RollSecondary(Archetype archetype, float roll, Tuning tuning)
        {
            switch (archetype)
            {
                case Archetype.Civic:
                    // Parterre beds are the civic square's signature, not an option.
                    return SecondaryFeature.Parterre;
                case Archetype.Formal:
                    return roll < 0.4f ? SecondaryFeature.None : SecondaryFeature.Parterre;
                default:
                    if (roll < tuning.carouselChance)
                        return SecondaryFeature.Carousel;
                    if (roll < 0.45f)
                        return SecondaryFeature.Pond;
                    return roll < 0.7f ? SecondaryFeature.None : SecondaryFeature.Parterre;
            }
        }

        // ================================================================== entrances

        static void ChooseEntrances(
            Plan plan, SideInfo side, IReadOnlyList<EntranceAnchor> anchors,
            int count, float[] jitter, Tuning tuning)
        {
            var scored = new List<(int side, float score)>();
            for (var s = 0; s < 4; s++)
            {
                if (!side.HasRoad[s])
                    continue;
                var hasAnchor = false;
                var hasBoundary = false;
                foreach (var anchor in anchors)
                {
                    if (anchor.Side != s)
                        continue;
                    hasAnchor = true;
                    hasBoundary |= anchor.OnBoundary;
                }
                if (!hasAnchor)
                    continue;

                var score = (hasBoundary ? 2f : 0f) + (side.Avenue[s] ? 1f : 0f) + jitter[s];
                scored.Add((s, score));
            }

            scored.Sort((a, b) =>
                a.score != b.score ? b.score.CompareTo(a.score) : a.side.CompareTo(b.side));

            var take = Mathf.Min(count, scored.Count);
            if (take < tuning.minEntrances && scored.Count > 0)
                plan.Warnings.Add($"only {take} road-facing side(s) with anchors available");

            for (var i = 0; i < take; i++)
            {
                var s = scored[i].side;
                var entrance = BuildEntrance(plan, side, anchors, s, tuning);
                plan.Entrances.Add(entrance);
            }
        }

        static Entrance BuildEntrance(
            Plan plan, SideInfo side, IReadOnlyList<EntranceAnchor> anchors, int s, Tuning tuning)
        {
            // The gate wants the middle of the side; the anchor decides how close it gets. On a
            // crosswalk side the gate must align with the crossing exactly - the crossing walk
            // pierces the block line and the hedge has to open where it does.
            var mid = s % 2 == 0
                ? plan.Interior.Centre.y
                : plan.Interior.Centre.x;
            var span = side.RoadSpan[s];
            var targetAlong = Mathf.Clamp(mid, span.x + tuning.gateHalfWidth + 2f,
                span.y - tuning.gateHalfWidth - 2f);

            EntranceAnchor best = default;
            var bestCost = float.MaxValue;
            foreach (var anchor in anchors)
            {
                if (anchor.Side != s)
                    continue;
                var along = s % 2 == 0 ? anchor.Pos.y : anchor.Pos.x;
                var cost = Mathf.Abs(along - targetAlong) - (anchor.OnBoundary ? 1000f : 0f);
                if (cost >= bestCost)
                    continue;
                bestCost = cost;
                best = anchor;
            }

            var line = side.Line[s];
            var gateAlong = best.OnBoundary
                ? (s % 2 == 0 ? best.Pos.y : best.Pos.x)
                : targetAlong;
            var gate = s % 2 == 0
                ? new Vector2(line, gateAlong)
                : new Vector2(gateAlong, line);

            return new Entrance
            {
                Side = s,
                Gate = gate,
                Anchor = best.Pos,
                AnchorOnBoundary = best.OnBoundary,
                Avenue = best.Avenue,
            };
        }

        /// <summary>
        /// The points a spine takes from its interior end out to the road graph: through the
        /// gate, then - when the anchor is a tile-corner node - an L along the pavement line to
        /// it. A crosswalk anchor sits inward of the gate on the same line, so the tail is the
        /// straight gate-to-anchor stub and the crossing walk does the rest.
        /// </summary>
        static List<Vector2> TailOutward(Entrance entrance)
        {
            var tail = new List<Vector2> { entrance.Gate };
            if (entrance.AnchorOnBoundary)
            {
                tail.Insert(0, entrance.Anchor);
                return tail; // anchor -> gate, read inward-out as gate <- anchor
            }

            var s = entrance.Side;
            var elbow = s % 2 == 0
                ? new Vector2(entrance.Anchor.x, entrance.Gate.y)
                : new Vector2(entrance.Gate.x, entrance.Anchor.y);
            tail.Add(elbow);
            tail.Add(entrance.Anchor);
            return tail; // gate -> elbow -> anchor
        }

        /// <summary>Anchor-first ordering of the tail: how a spine ENTERS the park.</summary>
        static List<Vector2> TailInward(Entrance entrance)
        {
            var tail = TailOutward(entrance);
            if (!entrance.AnchorOnBoundary)
                tail.Reverse();
            return tail;
        }

        // ================================================================== spines

        static void BuildFormalSpines(Plan plan, Tuning tuning)
        {
            // Pair opposite sides first; whatever is left pairs across the centre anyway. The
            // plaza centre is the mean of the gates so the axes meet where the arms are shortest.
            var entrances = plan.Entrances;
            var centre = Vector2.zero;
            foreach (var entrance in entrances)
                centre += entrance.Gate;
            centre /= entrances.Count;
            centre = ClampInto(centre, plan.Interior.Deflated(tuning.plazaRadius + 4f));

            plan.PlazaCentre = centre;
            plan.PlazaRadius = tuning.plazaRadius;

            var used = new bool[entrances.Count];
            for (var i = 0; i < entrances.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;

                var partner = -1;
                for (var j = i + 1; j < entrances.Count; j++)
                {
                    if (used[j])
                        continue;
                    if (partner < 0 || Opposite(entrances[i].Side, entrances[j].Side))
                        partner = j;
                    if (Opposite(entrances[i].Side, entrances[j].Side))
                        break;
                }

                var points = new List<Vector2>();
                points.AddRange(TailInward(entrances[i]));
                points.Add(centre);
                if (partner >= 0)
                {
                    used[partner] = true;
                    points.AddRange(TailOutward(entrances[partner]));
                }
                else
                {
                    // A lone arm may not dead-end mid-park: run it through to the first
                    // entrance's gate, overlapping that axis half. Paint overdraw is coplanar
                    // same-material quads in one mesh; nav gets a second route, not a stub.
                    points.AddRange(TailOutward(entrances[0]));
                }

                plan.Spines.Add(MakeSpine(points, tuning.mainPathWidth, SpineKind.Main));
            }
        }

        static void BuildInformalSpines(Plan plan, float[] bendRolls, Tuning tuning)
        {
            var entrances = plan.Entrances;

            // The main path runs between the two most distant gates.
            int a = 0, b = entrances.Count > 1 ? 1 : 0;
            var bestApart = -1f;
            for (var i = 0; i < entrances.Count; i++)
            for (var j = i + 1; j < entrances.Count; j++)
            {
                var apart = (entrances[i].Gate - entrances[j].Gate).sqrMagnitude;
                if (apart <= bestApart)
                    continue;
                bestApart = apart;
                a = i;
                b = j;
            }

            var gateA = entrances[a].Gate;
            var gateB = entrances[b].Gate;
            var chord = gateB - gateA;
            var bendLimit = plan.Interior.Deflated(4f);

            Vector2 normal, control1, control2;
            var bendSum = 0f;
            if (a == b)
            {
                // One entrance: the main path is a loop out into the park and back. A cubic
                // whose ends coincide draws a teardrop as long as the controls stand apart.
                normal = Perp(plan.Interior.Centre - gateA);
                var reach = Vector2.Lerp(gateA, plan.Interior.Centre, 1.2f);
                control1 = ClampInto(reach + normal * 10f, bendLimit);
                control2 = ClampInto(reach - normal * 10f, bendLimit);
            }
            else
            {
                normal = new Vector2(-chord.y, chord.x).normalized;
                var bend1 = (bendRolls[0] - 0.5f) * 0.5f * chord.magnitude;
                var bend2 = (bendRolls[1] - 0.5f) * 0.5f * chord.magnitude;
                bendSum = bend1 + bend2;
                control1 = ClampInto(gateA + chord * 0.33f + normal * bend1, bendLimit);
                control2 = ClampInto(gateA + chord * 0.66f + normal * bend2, bendLimit);
            }

            var main = new List<Vector2>();
            main.AddRange(TailInward(entrances[a]));
            AppendBezier(main, gateA, control1, control2, gateB);
            main.AddRange(TailOutward(entrances[b]));
            plan.Spines.Add(MakeSpine(main, tuning.mainPathWidth, SpineKind.Main));

            // Secondary: from a third gate to a junction on the main curve, or - with only two
            // entrances - a loop trail off the main and back onto it. Either way both ends are
            // on a walkable thing, never on the fence.
            var mainPoints = plan.Spines[0].Points;
            if (entrances.Count > 2)
            {
                var c = 0;
                while (c == a || c == b)
                    c++;
                var join = PointAlong(mainPoints, 0.45f + bendRolls[2] * 0.15f);
                var gateC = entrances[c].Gate;
                var mid = (gateC + join) * 0.5f
                          + Perp(join - gateC) * ((bendRolls[3] - 0.5f) * 12f);
                var secondary = new List<Vector2>();
                secondary.AddRange(TailInward(entrances[c]));
                AppendBezier(secondary, gateC, ClampInto(mid, bendLimit), join);
                plan.Spines.Add(MakeSpine(secondary, tuning.secondaryPathWidth, SpineKind.Secondary));
            }
            else
            {
                var from = PointAlong(mainPoints, 0.3f);
                var to = PointAlong(mainPoints, 0.7f);
                var away = (from + to) * 0.5f
                           - normal * Mathf.Sign(bendSum + 0.001f)
                           * (8f + bendRolls[2] * 6f);
                var loop = new List<Vector2> { from };
                AppendBezier(loop, from, ClampInto(away, bendLimit), to);
                plan.Spines.Add(MakeSpine(loop, tuning.secondaryPathWidth, SpineKind.Secondary));
            }
        }

        static void BuildCivicSpines(Plan plan, Tuning tuning)
        {
            var ringRect = plan.Interior.Deflated(tuning.beltDepth + 2f);
            plan.PlazaCentre = ringRect.Centre;
            plan.PlazaRadius = tuning.plazaRadius * 0.8f;

            // The ring, closed - first point repeated last. The monument stands inside it with
            // its parterre beds; the ring is the walk, not a cross through the centrepiece.
            var ring = new List<Vector2>
            {
                ringRect.Min,
                new(ringRect.Max.x, ringRect.Min.y),
                ringRect.Max,
                new(ringRect.Min.x, ringRect.Max.y),
                ringRect.Min,
            };
            plan.Spines.Add(MakeSpine(ring, tuning.secondaryPathWidth, SpineKind.PlazaRing));

            // One connector per entrance, straight in from the gate to the ring edge.
            foreach (var entrance in plan.Entrances)
            {
                var points = TailInward(entrance);
                var gate = entrance.Gate;
                Vector2 onRing;
                switch (entrance.Side)
                {
                    case 0:
                        onRing = new Vector2(ringRect.Max.x,
                            Mathf.Clamp(gate.y, ringRect.Min.y + 2f, ringRect.Max.y - 2f));
                        break;
                    case 1:
                        onRing = new Vector2(
                            Mathf.Clamp(gate.x, ringRect.Min.x + 2f, ringRect.Max.x - 2f),
                            ringRect.Max.y);
                        break;
                    case 2:
                        onRing = new Vector2(ringRect.Min.x,
                            Mathf.Clamp(gate.y, ringRect.Min.y + 2f, ringRect.Max.y - 2f));
                        break;
                    default:
                        onRing = new Vector2(
                            Mathf.Clamp(gate.x, ringRect.Min.x + 2f, ringRect.Max.x - 2f),
                            ringRect.Min.y);
                        break;
                }
                points.Add(onRing);
                plan.Spines.Add(MakeSpine(points, tuning.mainPathWidth, SpineKind.Main));
            }
        }

        // ================================================================== zones

        static void BuildZones(Plan plan, Tuning tuning)
        {
            var interior = plan.Interior;
            var beltInner = interior.Deflated(tuning.beltDepth);

            // The screening belt, one band per side.
            plan.Zones.Add(new Zone
            {
                Kind = ZoneKind.ScreenBelt,
                Area = new Rect(new Vector2(beltInner.Max.x, interior.Min.y), interior.Max),
            });
            plan.Zones.Add(new Zone
            {
                Kind = ZoneKind.ScreenBelt,
                Area = new Rect(new Vector2(interior.Min.x, beltInner.Max.y),
                    new Vector2(beltInner.Max.x, interior.Max.y)),
            });
            plan.Zones.Add(new Zone
            {
                Kind = ZoneKind.ScreenBelt,
                Area = new Rect(interior.Min, new Vector2(beltInner.Min.x, beltInner.Max.y)),
            });
            plan.Zones.Add(new Zone
            {
                Kind = ZoneKind.ScreenBelt,
                Area = new Rect(new Vector2(beltInner.Min.x, interior.Min.y),
                    new Vector2(beltInner.Max.x, beltInner.Min.y)),
            });

            if (plan.PlazaRadius > 0f)
            {
                var r = plan.PlazaRadius + 2f;
                plan.Zones.Add(new Zone
                {
                    Kind = ZoneKind.Feature,
                    Area = new Rect(plan.PlazaCentre - new Vector2(r, r),
                        plan.PlazaCentre + new Vector2(r, r)),
                });
            }

            // The open lawn: the largest square of ground that touches no path, no plaza and no
            // belt. Emptiness as a feature needs an actual region to defend, not an accident.
            var lawn = LargestClearSquare(plan, beltInner, tuning, exclude: null);
            if (lawn.HasValue && Mathf.Min(lawn.Value.Size.x, lawn.Value.Size.y) >= 8f)
                plan.Zones.Add(new Zone { Kind = ZoneKind.Lawn, Area = lawn.Value });
            else
                plan.Warnings.Add("no clear ground large enough for an open lawn");

            // The grove: the best clear ground that is not the lawn.
            var grove = LargestClearSquare(plan, beltInner, tuning, exclude: lawn);
            if (grove.HasValue)
            {
                var half = Mathf.Min(tuning.groveRadius,
                    Mathf.Min(grove.Value.Size.x, grove.Value.Size.y) * 0.5f);
                var area = new Rect(grove.Value.Centre - new Vector2(half, half),
                    grove.Value.Centre + new Vector2(half, half));
                plan.Zones.Add(new Zone { Kind = ZoneKind.Grove, Area = area });
            }

            if (plan.Secondary == SecondaryFeature.Parterre)
                AddParterres(plan, tuning);
            if (plan.Secondary == SecondaryFeature.Pond)
                AddPond(plan, tuning);
        }

        static void AddParterres(Plan plan, Tuning tuning)
        {
            // Beds flank the plaza (or the civic centre) on the two sides the axes do not take.
            var centre = plan.PlazaRadius > 0f ? plan.PlazaCentre : plan.Interior.Centre;
            var r = Mathf.Max(plan.PlazaRadius, 4f);
            var bed = new Vector2(6f, 3.5f);
            foreach (var sign in new[] { -1f, 1f })
            {
                var at = centre + new Vector2(0f, sign * (r + 4f));
                var area = new Rect(at - bed * 0.5f, at + bed * 0.5f);
                if (!plan.Interior.Deflated(tuning.beltDepth).Contains(area.Centre, 2f))
                    continue;
                if (ClearOfSpines(plan, area.Centre, Mathf.Max(bed.x, bed.y) * 0.5f))
                    plan.Zones.Add(new Zone { Kind = ZoneKind.Parterre, Area = area });
            }
        }

        static void AddPond(Plan plan, Tuning tuning)
        {
            var beltInner = plan.Interior.Deflated(tuning.beltDepth);
            Rect? lawn = null, grove = null;
            foreach (var zone in plan.Zones)
            {
                if (zone.Kind == ZoneKind.Lawn)
                    lawn = zone.Area;
                if (zone.Kind == ZoneKind.Grove)
                    grove = zone.Area;
            }

            var spot = LargestClearSquare(plan, beltInner, tuning, exclude: lawn, alsoExclude: grove);
            if (!spot.HasValue || Mathf.Min(spot.Value.Size.x, spot.Value.Size.y) < 9f)
            {
                plan.Warnings.Add("no clear ground for the pond; skipped");
                return;
            }

            var centre = spot.Value.Centre;
            var half = new Vector2(5.5f, 3.5f);
            plan.Zones.Add(new Zone
            {
                Kind = ZoneKind.Pond,
                Area = new Rect(centre - half, centre + half),
            });
        }

        /// <summary>
        /// The largest axis-aligned clear square inside <paramref name="within"/> - clear of
        /// every spine (by half-width plus a margin), the plaza, and up to two excluded rects.
        /// Grid-sampled rather than exact; deterministic and draw-free.
        /// </summary>
        static Rect? LargestClearSquare(
            Plan plan, Rect within, Tuning tuning, Rect? exclude, Rect? alsoExclude = null)
        {
            if (within.Size.x < 6f || within.Size.y < 6f)
                return null;

            const int Samples = 15;
            var bestHalf = 0f;
            var bestAt = within.Centre;
            for (var ix = 0; ix < Samples; ix++)
            for (var iz = 0; iz < Samples; iz++)
            {
                var p = new Vector2(
                    Mathf.Lerp(within.Min.x, within.Max.x, (ix + 0.5f) / Samples),
                    Mathf.Lerp(within.Min.y, within.Max.y, (iz + 0.5f) / Samples));

                var half = Mathf.Min(
                    Mathf.Min(p.x - within.Min.x, within.Max.x - p.x),
                    Mathf.Min(p.y - within.Min.y, within.Max.y - p.y));

                foreach (var spine in plan.Spines)
                    half = Mathf.Min(half,
                        DistanceToPolyline(p, spine.Points) - spine.Width * 0.5f - 1f);

                if (plan.PlazaRadius > 0f)
                    half = Mathf.Min(half,
                        (p - plan.PlazaCentre).magnitude - plan.PlazaRadius - 2f);

                if (exclude.HasValue)
                    half = Mathf.Min(half, DistanceToRect(p, exclude.Value));
                if (alsoExclude.HasValue)
                    half = Mathf.Min(half, DistanceToRect(p, alsoExclude.Value));

                if (half <= bestHalf)
                    continue;
                bestHalf = half;
                bestAt = p;
            }

            if (bestHalf < 3f)
                return null;
            var extent = new Vector2(bestHalf, bestHalf);
            return new Rect(bestAt - extent, bestAt + extent);
        }

        // ================================================================== stations

        static void PlaceCentrepiece(Plan plan, System.Random rng, Footprint[] footprints)
        {
            var yaw = 90f * rng.Next(4);
            switch (plan.Archetype)
            {
                case Archetype.Formal:
                    AddStation(plan, footprints, StationKind.Centrepiece, plan.PlazaCentre,
                        yaw, 1f, priority: 10);
                    break;
                case Archetype.Civic:
                    AddStation(plan, footprints, StationKind.Monument, plan.PlazaCentre,
                        yaw, 1f, priority: 10);
                    break;
            }
        }

        static void PlaceGatePosts(Plan plan, Footprint[] footprints, Tuning tuning)
        {
            foreach (var entrance in plan.Entrances)
            {
                var leg = Legs[entrance.Side];
                var tangent = new Vector2(-leg.y, leg.x);
                var offset = tuning.gateHalfWidth + footprints[(int)StationKind.GatePost].Radius;
                var yaw = Mathf.Atan2(leg.x, leg.y) * Mathf.Rad2Deg;
                AddStation(plan, footprints, StationKind.GatePost,
                    entrance.Gate + tangent * offset, yaw, 1f, priority: 8);
                AddStation(plan, footprints, StationKind.GatePost,
                    entrance.Gate - tangent * offset, yaw, 1f, priority: 8);
            }
        }

        static void PlaceBeltTrees(
            Plan plan, System.Random rng, SideInfo side, Footprint[] footprints, Tuning tuning)
        {
            var radius = footprints[(int)StationKind.Tree].Radius;
            var interior = plan.Interior;
            var inset = tuning.beltDepth * 0.5f;

            for (var s = 0; s < 4; s++)
            {
                var line = side.Line[s] + (s < 2 ? -inset : inset);
                Vector2 from, to;
                if (s % 2 == 0)
                {
                    from = new Vector2(line, interior.Min.y + 3f);
                    to = new Vector2(line, interior.Max.y - 3f);
                }
                else
                {
                    from = new Vector2(interior.Min.x + 3f, line);
                    to = new Vector2(interior.Max.x - 3f, line);
                }

                var length = (to - from).magnitude;
                var direction = (to - from).normalized;
                for (var at = 0f; at <= length; at += tuning.beltTreeSpacing)
                {
                    var pos = from + direction * at;
                    var yaw = (float)rng.NextDouble() * 360f; // draw first, house rule

                    if (NearAGate(plan, pos, tuning.gateHalfWidth + 3f))
                        continue;
                    if (!ClearOfSpines(plan, pos, radius))
                        continue;

                    // A single species at a single scale: the belt's job is a uniform screen.
                    AddStation(plan, footprints, StationKind.Tree, pos, yaw, 1f,
                        priority: 7, species: 0);
                }
            }
        }

        static void PlaceAllees(Plan plan, Footprint[] footprints, Tuning tuning)
        {
            if (plan.Archetype != Archetype.Formal)
                return;

            var radius = footprints[(int)StationKind.Tree].Radius;
            var offset = tuning.mainPathWidth * 0.5f + 2.2f;

            // Mirrored rows along each arm: identical species, identical scale, exact spacing.
            foreach (var entrance in plan.Entrances)
            {
                var from = entrance.Gate;
                var to = plan.PlazaCentre;
                var direction = (to - from).normalized;
                var normal = Perp(direction);
                var length = (to - from).magnitude - plan.PlazaRadius - 3f;
                var yaw = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

                for (var at = 4f; at <= length; at += tuning.alleeSpacing)
                foreach (var sign in new[] { -1f, 1f })
                {
                    var pos = from + direction * at + normal * (sign * offset);
                    if (!plan.Interior.Contains(pos, radius))
                        continue;
                    if (!ClearOfSpines(plan, pos, radius))
                        continue;
                    AddStation(plan, footprints, StationKind.Tree, pos, yaw, 1f,
                        priority: 7, species: 1);
                }
            }
        }

        static void PlaceGrove(Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning)
        {
            Rect? area = null;
            foreach (var zone in plan.Zones)
                if (zone.Kind == ZoneKind.Grove)
                    area = zone.Area;
            if (!area.HasValue)
                return;

            var radius = footprints[(int)StationKind.Tree].Radius;
            var count = rng.Next(tuning.groveMin, tuning.groveMax + 1);
            var placed = new List<Vector2>();
            var attempts = count * 4;

            for (var i = 0; i < attempts && placed.Count < count; i++)
            {
                // Draw the whole candidate before any test - the spend-the-draw discipline.
                var pos = new Vector2(
                    Mathf.Lerp(area.Value.Min.x, area.Value.Max.x, (float)rng.NextDouble()),
                    Mathf.Lerp(area.Value.Min.y, area.Value.Max.y, (float)rng.NextDouble()));
                var scale = Range(rng, tuning.treeScaleMin, tuning.treeScaleMax);
                var yaw = (float)rng.NextDouble() * 360f;

                if (!ClearOfSpines(plan, pos, radius * scale))
                    continue;
                var tooClose = false;
                foreach (var other in placed)
                    if ((pos - other).magnitude < radius * 1.7f)
                    {
                        tooClose = true;
                        break;
                    }
                if (tooClose)
                    continue;

                placed.Add(pos);
                AddStation(plan, footprints, StationKind.Tree, pos, yaw, scale,
                    priority: 7, species: 0);
            }
        }

        static void PlaceAccentAndDead(
            Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning)
        {
            var treeCount = 0;
            foreach (var station in plan.Stations)
                if (station.Kind == StationKind.Tree)
                    treeCount++;

            var inner = plan.Interior.Deflated(tuning.beltDepth);
            var accentTarget = Mathf.FloorToInt(treeCount * tuning.accentShare);
            var accentRadius = footprints[(int)StationKind.AccentTree].Radius;
            var placedAccents = 0;
            for (var i = 0; i < accentTarget * 3 && placedAccents < accentTarget; i++)
            {
                var pos = RandomIn(rng, inner);
                var yaw = (float)rng.NextDouble() * 360f;
                if (!ClearOfSpines(plan, pos, accentRadius) || InLawn(plan, pos))
                    continue;
                AddStation(plan, footprints, StationKind.AccentTree, pos, yaw, 1f,
                    priority: 7, species: 2);
                placedAccents++;
            }

            // Dead and bare trees belong to the unkempt corners of the informal park only -
            // never a formal allee, never the screening row.
            if (plan.Archetype != Archetype.Informal)
                return;
            var deadCount = rng.Next(0, tuning.maxDeadTrees + 1);
            var deadRadius = footprints[(int)StationKind.DeadTree].Radius;
            var placedDead = 0;
            for (var i = 0; i < deadCount * 4 && placedDead < deadCount; i++)
            {
                var pos = RandomIn(rng, inner);
                var yaw = (float)rng.NextDouble() * 360f;
                if (!ClearOfSpines(plan, pos, deadRadius) || InLawn(plan, pos))
                    continue;
                AddStation(plan, footprints, StationKind.DeadTree, pos, yaw, 1f, priority: 4);
                placedDead++;
            }

            // Boulders, the same corners; naturally small prefabs at authored scale, tinted by
            // the shared atlas like everything else - never a lone saturated one-off.
            var boulderRadius = footprints[(int)StationKind.Boulder].Radius;
            for (var i = 0; i < 4; i++)
            {
                var pos = RandomIn(rng, inner);
                var yaw = (float)rng.NextDouble() * 360f;
                if (!ClearOfSpines(plan, pos, boulderRadius) || InLawn(plan, pos))
                    continue;
                AddStation(plan, footprints, StationKind.Boulder, pos, yaw, 1f, priority: 2);
            }
        }

        static void PlaceSecondaryFeature(
            Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning, float knollRoll)
        {
            foreach (var zone in plan.Zones)
            {
                switch (zone.Kind)
                {
                    case ZoneKind.Parterre:
                    {
                        // A bed is rows of one flower, not a meadow mix.
                        var area = zone.Area;
                        var yaw = 90f * rng.Next(4);
                        for (var x = area.Min.x + 0.8f; x <= area.Max.x - 0.8f; x += 1.3f)
                        for (var z = area.Min.y + 0.8f; z <= area.Max.y - 0.8f; z += 1.3f)
                            AddStation(plan, footprints, StationKind.Flower,
                                new Vector2(x, z), yaw, 1f, priority: 0);
                        break;
                    }
                    case ZoneKind.Pond:
                    {
                        var area = zone.Area;
                        var perimeter = 2f * (area.Size.x + area.Size.y);
                        var steps = Mathf.Max(8, Mathf.RoundToInt(perimeter / 2.4f));
                        for (var i = 0; i < steps; i++)
                        {
                            var pos = AlongRectPerimeter(area, i / (float)steps);
                            var yaw = (float)rng.NextDouble() * 360f;
                            AddStation(plan, footprints, StationKind.PondRim, pos, yaw, 1f,
                                priority: 2);
                        }
                        break;
                    }
                }
            }

            if (plan.Secondary == SecondaryFeature.Carousel)
            {
                var inner = plan.Interior.Deflated(tuning.beltDepth);
                var spot = LargestClearSquare(plan, inner, tuning, exclude: LawnRect(plan));
                if (spot.HasValue
                    && Mathf.Min(spot.Value.Size.x, spot.Value.Size.y)
                    >= footprints[(int)StationKind.Carousel].Radius * 2f)
                    AddStation(plan, footprints, StationKind.Carousel, spot.Value.Centre,
                        90f * rng.Next(4), 1f, priority: 9);
                else
                    plan.Warnings.Add("no clear ground for the carousel; skipped");
            }

            if (plan.Archetype == Archetype.Informal && knollRoll < tuning.knollChance)
            {
                var scale = Range(rng, tuning.knollScaleMin, tuning.knollScaleMax);
                var inner = plan.Interior.Deflated(tuning.beltDepth);
                var spot = LargestClearSquare(plan, inner, tuning,
                    exclude: LawnRect(plan), alsoExclude: GroveRect(plan));
                // tile-plain-hump is a 30m authored dome; its footprint is 15 * scale.
                var radius = 15f * scale;
                if (spot.HasValue && Mathf.Min(spot.Value.Size.x, spot.Value.Size.y) >= radius * 2f)
                    plan.Stations.Add(new Station
                    {
                        Kind = StationKind.Knoll,
                        Pos = spot.Value.Centre,
                        Yaw = 90f * rng.Next(4),
                        Scale = scale,
                        Radius = radius,
                        Priority = 8,
                    });
            }
        }

        static void PlaceBenchesAndBins(
            Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning)
        {
            var benches = new List<Vector2>();
            var benchTangents = new List<Vector2>();

            if (plan.Archetype == Archetype.Formal)
            {
                // Facing pairs across the plaza, off the axis crossings.
                var r = plan.PlazaRadius + 1.6f;
                foreach (var degrees in new[] { 45f, 135f, 225f, 315f })
                {
                    var radians = degrees * Mathf.Deg2Rad;
                    var pos = plan.PlazaCentre
                              + new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * r;
                    var toCentre = (plan.PlazaCentre - pos).normalized;
                    var yaw = Mathf.Atan2(toCentre.x, toCentre.y) * Mathf.Rad2Deg;
                    AddStation(plan, footprints, StationKind.Bench, pos, yaw, 1f, priority: 5);
                    benches.Add(pos);
                    benchTangents.Add(Perp(toCentre));
                }
            }

            // Along the main walk: snapped to the edge, facing the path - yaw from the tangent,
            // never from a roll.
            var spine = plan.Spines.Count > 0 ? plan.Spines[0] : default;
            if (spine.Points != null && spine.Points.Length >= 2)
            {
                var count = rng.Next(tuning.benchMin, tuning.benchMax + 1);
                for (var i = 0; i < count; i++)
                {
                    var t = (i + 1f) / (count + 1f);
                    var sideRoll = rng.Next(2) == 0 ? -1f : 1f;
                    var at = PointAlong(spine.Points, t);
                    var tangent = TangentAlong(spine.Points, t);
                    var normal = Perp(tangent);
                    var pos = at + normal * (sideRoll * (spine.Width * 0.5f + 1.2f));
                    var facing = -normal * sideRoll;
                    var yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
                    if (!plan.Interior.Contains(pos, 1f) || InLawn(plan, pos))
                        continue;

                    // Not on a ring corner, a junction or the plaza rim, where the nearest
                    // stretch of walk is someone else's and the bench would sit on it or turn
                    // its back to it. The nearest-point test is the one the walkers live by.
                    if (plan.PlazaRadius > 0f
                        && (pos - plan.PlazaCentre).magnitude < plan.PlazaRadius + 3f)
                        continue;
                    var nearestDistance = float.MaxValue;
                    var nearestPoint = pos;
                    foreach (var other in plan.Spines)
                    foreach (var point in other.Points)
                    {
                        var d = (point - pos).magnitude;
                        if (d >= nearestDistance)
                            continue;
                        nearestDistance = d;
                        nearestPoint = point;
                    }
                    if (nearestDistance < 0.9f
                        || Vector2.Dot(facing, (nearestPoint - pos).normalized) < 0.5f)
                        continue;

                    AddStation(plan, footprints, StationKind.Bench, pos, yaw, 1f, priority: 5);
                    benches.Add(pos);
                    benchTangents.Add(tangent);
                }
            }

            // A bin serves a pair of benches, capped - a park is not a depot.
            var bins = Mathf.Min(tuning.maxBins, benches.Count / 2);
            for (var i = 0; i < bins; i++)
            {
                var pos = benches[i * 2] + benchTangents[i * 2] * 1.8f;
                AddStation(plan, footprints, StationKind.Bin, pos, 0f, 1f, priority: 3);
            }
        }

        static void PlaceLamps(Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning)
        {
            var lamps = new List<Vector2>();
            var paired = plan.Archetype == Archetype.Formal;

            foreach (var spine in plan.Spines)
            {
                if (spine.Kind == SpineKind.Secondary)
                    continue; // lamps light the main walks and the ring, not every trail

                var length = PolylineLength(spine.Points);
                var flip = 1f;
                for (var at = Range(rng, tuning.lampSpacingMin, tuning.lampSpacingMax);
                     at < length;
                     at += Range(rng, tuning.lampSpacingMin, tuning.lampSpacingMax))
                {
                    var t = at / length;
                    var point = PointAlong(spine.Points, t);
                    var tangent = TangentAlong(spine.Points, t);
                    var normal = Perp(tangent);
                    var offset = spine.Width * 0.5f + 0.6f
                                 + footprints[(int)StationKind.Lamp].Radius;

                    var sides = paired
                        ? new[] { -1f, 1f }
                        : new[] { flip };
                    foreach (var s in sides)
                    {
                        var pos = point + normal * (s * offset);
                        var yaw = Mathf.Atan2(-normal.x * s, -normal.y * s) * Mathf.Rad2Deg;
                        if (!plan.Interior.Contains(pos, 0.5f))
                            continue;
                        if (NearAGate(plan, pos, tuning.gateHalfWidth + 2f))
                            continue;
                        var crowded = false;
                        foreach (var other in lamps)
                            if ((pos - other).magnitude < tuning.lampMinSeparation)
                            {
                                crowded = true;
                                break;
                            }
                        if (crowded)
                            continue;
                        lamps.Add(pos);
                        AddStation(plan, footprints, StationKind.Lamp, pos, yaw, 1f, priority: 6);
                    }
                    flip = -flip;
                }
            }
        }

        // ================================================================== sanity

        static void SanityHeight(Plan plan, Footprint[] footprints, float tallestTree)
        {
            // Two rules in one pass: a kind whose measured height beats its own category cap is
            // mis-wired and dropped whole; and nothing generic may top the tallest tree. The
            // landmark kinds are exempt from the tree line - a fountain or the rare carousel is
            // a landmark, not a prop - but never from their own cap.
            for (var i = plan.Stations.Count - 1; i >= 0; i--)
            {
                var kind = plan.Stations[i].Kind;
                var footprint = footprints[(int)kind];
                var exemptFromTreeLine = kind is StationKind.Centrepiece or StationKind.Monument
                    or StationKind.Carousel or StationKind.Tree or StationKind.AccentTree
                    or StationKind.DeadTree;
                var over = footprint.Height > footprint.MaxHeight
                           || (!exemptFromTreeLine && footprint.Height > tallestTree);
                if (!over)
                    continue;
                plan.Warnings.Add(
                    $"{kind} measures {footprint.Height:0.0}m against cap "
                    + $"{Mathf.Min(footprint.MaxHeight, exemptFromTreeLine ? float.MaxValue : tallestTree):0.0}m; skipped");
                plan.Stations.RemoveAt(i);
            }
        }

        static void SanityOverlaps(Plan plan)
        {
            // Greedy by priority: the important thing stands, the later shrub moves on.
            var order = new List<int>();
            for (var i = 0; i < plan.Stations.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
                plan.Stations[a].Priority != plan.Stations[b].Priority
                    ? plan.Stations[b].Priority.CompareTo(plan.Stations[a].Priority)
                    : a.CompareTo(b));

            var kept = new List<Station>();
            var dropped = new HashSet<int>();
            foreach (var index in order)
            {
                var station = plan.Stations[index];
                var collides = false;
                foreach (var other in kept)
                    if ((station.Pos - other.Pos).magnitude < station.Radius + other.Radius)
                    {
                        collides = true;
                        break;
                    }
                if (collides)
                    dropped.Add(index);
                else
                    kept.Add(station);
            }

            if (dropped.Count == 0)
                return;
            for (var i = plan.Stations.Count - 1; i >= 0; i--)
                if (dropped.Contains(i))
                    plan.Stations.RemoveAt(i);
        }

        static void SanityPathExclusion(Plan plan)
        {
            for (var i = plan.Stations.Count - 1; i >= 0; i--)
            {
                var station = plan.Stations[i];
                var isTree = station.Kind is StationKind.Tree or StationKind.AccentTree
                    or StationKind.DeadTree;
                if (!isTree)
                    continue;
                if (ClearOfSpines(plan, station.Pos, station.Radius * station.Scale))
                    continue;
                plan.Stations.RemoveAt(i);
            }
        }

        static void SanityLawn(Plan plan)
        {
            var lawn = LawnRect(plan);
            if (!lawn.HasValue)
                return;
            for (var i = plan.Stations.Count - 1; i >= 0; i--)
                if (lawn.Value.Contains(plan.Stations[i].Pos, -0.5f))
                    plan.Stations.RemoveAt(i);
        }

        static void SanityDensity(
            Plan plan, System.Random rng, Footprint[] footprints, Tuning tuning)
        {
            var area = plan.Interior.Area;
            if (area <= 0f)
                return;
            var hundreds = area / 100f;

            // Over: shed lowest priority first, insertion order breaking ties.
            var max = Mathf.RoundToInt(tuning.densityMaxPer100 * hundreds);
            max = Mathf.Min(max, tuning.maxStations);
            while (plan.Stations.Count > max)
            {
                var lowest = 0;
                for (var i = 1; i < plan.Stations.Count; i++)
                    if (plan.Stations[i].Priority < plan.Stations[lowest].Priority)
                        lowest = i;
                plan.Stations.RemoveAt(lowest);
            }

            // Under: shrubs into the belt and the grove until it reads planted.
            var min = Mathf.RoundToInt(tuning.densityMinPer100 * hundreds);
            var radius = footprints[(int)StationKind.Shrub].Radius;
            var belt = new List<Rect>();
            foreach (var zone in plan.Zones)
                if (zone.Kind is ZoneKind.ScreenBelt or ZoneKind.Grove)
                    belt.Add(zone.Area);
            if (belt.Count == 0)
                return;

            var attempts = 0;
            while (plan.Stations.Count < min && attempts < 250)
            {
                attempts++;
                var zone = belt[rng.Next(belt.Count)];
                var pos = RandomIn(rng, zone);
                var yaw = (float)rng.NextDouble() * 360f;
                if (!ClearOfSpines(plan, pos, radius) || InLawn(plan, pos))
                    continue;
                var collides = false;
                foreach (var other in plan.Stations)
                    if ((pos - other.Pos).magnitude < radius + other.Radius)
                    {
                        collides = true;
                        break;
                    }
                if (collides)
                    continue;
                AddStation(plan, footprints, StationKind.Shrub, pos, yaw, 1f, priority: 1);
            }

            if (plan.Stations.Count < min)
                plan.Warnings.Add(
                    $"density fill ran dry at {plan.Stations.Count}/{min} stations");
        }

        // ================================================================== helpers

        static void AddStation(
            Plan plan, Footprint[] footprints, StationKind kind, Vector2 pos,
            float yaw, float scale, int priority, int species = 0)
        {
            plan.Stations.Add(new Station
            {
                Kind = kind,
                Pos = pos,
                Yaw = yaw,
                Scale = scale,
                Radius = footprints[(int)kind].Radius * scale,
                Priority = priority,
                SpeciesSlot = species,
            });
        }

        static bool ContainsAnchor(List<EntranceAnchor> anchors, Vector2 pos)
        {
            foreach (var anchor in anchors)
                if ((anchor.Pos - pos).sqrMagnitude < 0.01f)
                    return true;
            return false;
        }

        static bool Opposite(int sideA, int sideB) => (sideA + 2) % 4 == sideB;

        static bool NearAGate(Plan plan, Vector2 pos, float within)
        {
            foreach (var entrance in plan.Entrances)
                if ((entrance.Gate - pos).magnitude < within)
                    return true;
            return false;
        }

        static bool ClearOfSpines(Plan plan, Vector2 pos, float margin)
        {
            foreach (var spine in plan.Spines)
                if (DistanceToPolyline(pos, spine.Points) < spine.Width * 0.5f + margin)
                    return false;
            if (plan.PlazaRadius > 0f
                && (pos - plan.PlazaCentre).magnitude < plan.PlazaRadius + margin)
                return false;
            return true;
        }

        static Rect? LawnRect(Plan plan)
        {
            foreach (var zone in plan.Zones)
                if (zone.Kind == ZoneKind.Lawn)
                    return zone.Area;
            return null;
        }

        static Rect? GroveRect(Plan plan)
        {
            foreach (var zone in plan.Zones)
                if (zone.Kind == ZoneKind.Grove)
                    return zone.Area;
            return null;
        }

        static bool InLawn(Plan plan, Vector2 pos)
        {
            var lawn = LawnRect(plan);
            return lawn.HasValue && lawn.Value.Contains(pos, -0.5f);
        }

        static Vector2 To2(Vector3 v) => new(v.x, v.z);

        static Vector2 Perp(Vector2 v)
        {
            var n = new Vector2(-v.y, v.x);
            var m = n.magnitude;
            return m > 1e-6f ? n / m : Vector2.right;
        }

        static Vector2 ClampInto(Vector2 p, Rect rect) => new(
            Mathf.Clamp(p.x, rect.Min.x, rect.Max.x),
            Mathf.Clamp(p.y, rect.Min.y, rect.Max.y));

        static Vector2 RandomIn(System.Random rng, Rect rect) => new(
            Mathf.Lerp(rect.Min.x, rect.Max.x, (float)rng.NextDouble()),
            Mathf.Lerp(rect.Min.y, rect.Max.y, (float)rng.NextDouble()));

        static float Range(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        static Spine MakeSpine(List<Vector2> raw, float width, SpineKind kind) => new()
        {
            Points = Resample(raw, SampleStep),
            Width = width,
            Kind = kind,
        };

        /// <summary>Even resample at close to <paramref name="step"/>, endpoints preserved.</summary>
        public static Vector2[] Resample(List<Vector2> raw, float step)
        {
            if (raw == null || raw.Count < 2)
                return raw?.ToArray() ?? System.Array.Empty<Vector2>();

            var total = 0f;
            for (var i = 1; i < raw.Count; i++)
                total += (raw[i] - raw[i - 1]).magnitude;
            if (total < 1e-3f)
                return new[] { raw[0], raw[^1] };

            var segments = Mathf.Max(1, Mathf.CeilToInt(total / step));
            var spacing = total / segments;
            var points = new List<Vector2> { raw[0] };
            var carried = 0f;
            for (var i = 1; i < raw.Count; i++)
            {
                var from = raw[i - 1];
                var to = raw[i];
                var length = (to - from).magnitude;
                if (length < 1e-6f)
                    continue;
                var direction = (to - from) / length;
                var at = spacing - carried;
                while (at <= length + 1e-4f)
                {
                    points.Add(from + direction * at);
                    at += spacing;
                }
                carried = (carried + length) % spacing;
            }
            if ((points[^1] - raw[^1]).sqrMagnitude > 1e-4f)
                points.Add(raw[^1]);
            return points.ToArray();
        }

        static void AppendBezier(List<Vector2> into, Vector2 a, Vector2 c1, Vector2 c2, Vector2 b)
        {
            const int Steps = 24;
            for (var i = 1; i <= Steps; i++)
            {
                var t = i / (float)Steps;
                var u = 1f - t;
                into.Add(u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b);
            }
        }

        static void AppendBezier(List<Vector2> into, Vector2 a, Vector2 c, Vector2 b)
        {
            const int Steps = 18;
            for (var i = 1; i <= Steps; i++)
            {
                var t = i / (float)Steps;
                var u = 1f - t;
                into.Add(u * u * a + 2f * u * t * c + t * t * b);
            }
        }

        public static float PolylineLength(Vector2[] points)
        {
            var total = 0f;
            for (var i = 1; i < points.Length; i++)
                total += (points[i] - points[i - 1]).magnitude;
            return total;
        }

        /// <summary>The point a fraction <paramref name="t"/> of the arclength along.</summary>
        public static Vector2 PointAlong(Vector2[] points, float t)
        {
            var target = PolylineLength(points) * Mathf.Clamp01(t);
            for (var i = 1; i < points.Length; i++)
            {
                var length = (points[i] - points[i - 1]).magnitude;
                if (target <= length)
                    return points[i - 1] + (points[i] - points[i - 1]).normalized * target;
                target -= length;
            }
            return points[^1];
        }

        public static Vector2 TangentAlong(Vector2[] points, float t)
        {
            var target = PolylineLength(points) * Mathf.Clamp01(t);
            for (var i = 1; i < points.Length; i++)
            {
                var length = (points[i] - points[i - 1]).magnitude;
                if (target <= length)
                    return (points[i] - points[i - 1]).normalized;
                target -= length;
            }
            return (points[^1] - points[^2]).normalized;
        }

        public static float DistanceToPolyline(Vector2 p, Vector2[] points)
        {
            if (points == null || points.Length == 0)
                return float.MaxValue;
            if (points.Length == 1)
                return (p - points[0]).magnitude;
            var best = float.MaxValue;
            for (var i = 1; i < points.Length; i++)
                best = Mathf.Min(best, DistancePointSegment(p, points[i - 1], points[i]));
            return best;
        }

        static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f)
                return (p - a).magnitude;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Distance from a point to a rect's interior - 0 inside.</summary>
        static float DistanceToRect(Vector2 p, Rect rect)
        {
            var dx = Mathf.Max(rect.Min.x - p.x, 0f, p.x - rect.Max.x);
            var dy = Mathf.Max(rect.Min.y - p.y, 0f, p.y - rect.Max.y);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Vector2 AlongRectPerimeter(Rect rect, float t)
        {
            var w = rect.Size.x;
            var h = rect.Size.y;
            var perimeter = 2f * (w + h);
            var at = Mathf.Repeat(t, 1f) * perimeter;
            if (at < w)
                return new Vector2(rect.Min.x + at, rect.Min.y);
            at -= w;
            if (at < h)
                return new Vector2(rect.Max.x, rect.Min.y + at);
            at -= h;
            if (at < w)
                return new Vector2(rect.Max.x - at, rect.Max.y);
            at -= w;
            return new Vector2(rect.Min.x, rect.Max.y - at);
        }
    }
}
