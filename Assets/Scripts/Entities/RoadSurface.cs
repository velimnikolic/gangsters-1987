using System;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;
using PolyPerfect.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// Answers one question: is this world position on the carriageway? The interaction layer
    /// asks it before letting a pedestrian stop - crossings are ordinary Sidewalk paths that
    /// run straight across the asphalt, so without this check a chat pair meeting mid-crossing
    /// is indistinguishable from a pair meeting on a pavement, and they park themselves in
    /// front of the traffic for the whole conversation.
    ///
    /// There is no surface data to read at Play time - CityGrid is not serialized, so a saved
    /// generated scene has no grid - but every road tile the generator places is named
    /// tile_{x}_{y}_{RoadTileKind} (RoadNetworkBuilder), sits on the CityGrid.CellSize lattice,
    /// and its cross-section is fixed by the prefab family. So the kind is recovered from the
    /// name, the tile from a cell dictionary (the JunctionMap approach: derive from Tile, cache
    /// forever), and the asphalt test runs in tile-local space where the numbers are constants.
    ///
    /// TILE-LOCAL is load-bearing: the tiles are placed at CityGrid.TileScale, and
    /// InverseTransformPoint divides that back out. So every constant below is the prefab's own
    /// authored metre - unscaled - and must never be taken from CityGrid's world-space block.
    ///
    /// The cross-sections are measured off the prefabs, not tuned: a street tile is asphalt
    /// out to |x| = 3 with pavement 3..5 (CityGrid.PavementEdge); the dual carriageway
    /// reaches 6.25 with pavement to 8.5 (CityGrid.MainPavementEdge). The curve's road mesh
    /// is an annulus of radius 15 +/- 3 around the corner shared by its two connected edges
    /// (local (-15, +15) unrotated) - its sidewalk arcs sit at radii 11 and 19, one metre off
    /// the asphalt like everywhere else. The curve ALSO keeps the straight cross bands: the
    /// pack's car nav polylines take the corner as a square (through (-1.5, 1.5)), briefly
    /// leaving the drawn asphalt, and a pedestrian must not be allowed to stand where cars
    /// actually drive just because the paint says pavement.
    /// </summary>
    public static class RoadSurface
    {
        // ------------------------------------------------------------------ pure core

        public enum SurfaceShape
        {
            NotRoad,

            /// <summary>Axis-aligned asphalt bands: straights (one band) and junctions (two).</summary>
            Bands,

            /// <summary>The corner annulus plus the square nav-polyline bands.</summary>
            Curve,

            /// <summary>A straight arm plus the turning circle at the tile centre.</summary>
            DeadEnd,
        }

        /// <summary>Asphalt half-widths, measured to the kerb face.</summary>
        public const float StreetAsphalt = 3f;
        public const float MainAsphalt = 6.25f;

        /// <summary>
        /// Someone straddling the kerb still counts as on the road - their partner would have
        /// to stand in the gutter to face them.
        /// </summary>
        public const float KerbMargin = 0.25f;

        /// <summary>
        /// The curve's road centreline arc: half the AUTHORED tile, around the shared corner.
        ///
        /// AuthoredCellSize and not CellSize, and the difference is the whole frame contract of
        /// this class. Every number in this pure core is compared against coordinates that came
        /// out of Transform.InverseTransformPoint, which divides CityGrid.TileScale back out - so
        /// they are the prefab's own metres, not the world's. Tracking CellSize would put the arc
        /// at 19.5 while the mesh it describes is still at 15.
        /// </summary>
        public const float CurveRadius = CityGrid.AuthoredCellSize * 0.5f;

        /// <summary>tile-road-end's turning circle: its sidewalk ring walks at radius ~4.2.</summary>
        public const float TurningCircle = 3.5f;

        public readonly struct RoadShape
        {
            public readonly SurfaceShape Shape;

            /// <summary>Half-width of the |x| band (road running along local z), 0 for none.</summary>
            public readonly float BandX;

            /// <summary>Half-width of the |z| band (road running along local x), 0 for none.</summary>
            public readonly float BandZ;

            public RoadShape(SurfaceShape shape, float bandX, float bandZ)
            {
                Shape = shape;
                BandX = bandX;
                BandZ = bandZ;
            }
        }

        /// <summary>
        /// The band layout follows RoadTileTable's rotation contract: straights run along
        /// local z; the plain T's through-road connects left-right (local x) with the branch
        /// at +z; MainTJunction is the boulevard east-west with a minor street teeing north;
        /// MainCross is the boulevard north-south crossing a minor street.
        /// </summary>
        public static RoadShape Classify(RoadTileKind kind)
        {
            const float street = StreetAsphalt + KerbMargin;
            const float main = MainAsphalt + KerbMargin;

            switch (kind)
            {
                case RoadTileKind.Straight: return new RoadShape(SurfaceShape.Bands, street, 0f);
                case RoadTileKind.MainStraight: return new RoadShape(SurfaceShape.Bands, main, 0f);
                case RoadTileKind.TJunction: return new RoadShape(SurfaceShape.Bands, street, street);
                case RoadTileKind.Cross: return new RoadShape(SurfaceShape.Bands, street, street);
                case RoadTileKind.MainTJunction: return new RoadShape(SurfaceShape.Bands, street, main);
                case RoadTileKind.MainCross: return new RoadShape(SurfaceShape.Bands, main, street);
                case RoadTileKind.MainMainCross: return new RoadShape(SurfaceShape.Bands, main, main);
                case RoadTileKind.Curve: return new RoadShape(SurfaceShape.Curve, street, street);
                case RoadTileKind.End: return new RoadShape(SurfaceShape.DeadEnd, street, 0f);
                default: return new RoadShape(SurfaceShape.NotRoad, 0f, 0f);
            }
        }

        /// <summary>
        /// Recovers the kind from a generated tile's name, tile_{x}_{y}_{Kind}. Anything else
        /// (pack demo tiles, parks, hand-placed props) fails the parse and is not a road.
        /// </summary>
        public static bool TryParseKind(string tileName, out RoadTileKind kind)
        {
            kind = RoadTileKind.None;
            if (string.IsNullOrEmpty(tileName) || !tileName.StartsWith("tile_", StringComparison.Ordinal))
                return false;

            var underscore = tileName.LastIndexOf('_');
            if (underscore < 0 || underscore == tileName.Length - 1)
                return false;

            // Enum.TryParse happily reads "4" as (RoadTileKind)4, so a bare coordinate pair
            // like "tile_3_4" must be rejected before it impersonates a Cross.
            var suffix = tileName.Substring(underscore + 1);
            if (!char.IsLetter(suffix[0]))
                return false;

            return Enum.TryParse(suffix, out kind) && kind != RoadTileKind.None;
        }

        /// <summary>Is this tile-local point on the asphalt (kerb margin included)?</summary>
        public static bool OnAsphalt(RoadShape shape, float localX, float localZ) =>
            OnAsphalt(shape, localX, localZ, 0f);

        /// <summary>
        /// The same test with every band, arc and circle widened by <paramref name="margin"/>.
        /// A margin of zero is the asphalt itself; a positive one asks the stronger question the
        /// escape below needs - "is this point CLEAR of the asphalt", not merely off it.
        /// </summary>
        public static bool OnAsphalt(RoadShape shape, float localX, float localZ, float margin)
        {
            switch (shape.Shape)
            {
                case SurfaceShape.Bands:
                    return (shape.BandX > 0f && Mathf.Abs(localX) <= shape.BandX + margin)
                           || (shape.BandZ > 0f && Mathf.Abs(localZ) <= shape.BandZ + margin);

                case SurfaceShape.Curve:
                {
                    var dx = localX + CurveRadius;
                    var dz = localZ - CurveRadius;
                    var fromCorner = Mathf.Sqrt(dx * dx + dz * dz);
                    return Mathf.Abs(fromCorner - CurveRadius) <= shape.BandX + margin
                           || Mathf.Abs(localX) <= shape.BandX + margin
                           || Mathf.Abs(localZ) <= shape.BandZ + margin;
                }

                case SurfaceShape.DeadEnd:
                    // The arm reaches the connected (+z) edge only - past the turning circle
                    // the sidewalk ring wraps round the bottom of the tile and must stay free.
                    return (Mathf.Abs(localX) <= shape.BandX + margin && localZ >= 0f)
                           || Mathf.Sqrt(localX * localX + localZ * localZ) <= TurningCircle + margin;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Is this tile-local point on the DRAWN carriageway? Narrower than
        /// <see cref="OnAsphalt"/>, and the two differ on exactly one shape.
        ///
        /// OnAsphalt answers "may somebody CHOOSE to stop here", so the curve's square nav bands
        /// count: cars cut that corner as a square whatever the paint says, and nobody should be
        /// seated in the wheel path. This answers "is somebody standing where the road is", which
        /// is what the escape below acts on - and a walker on the curve's outer sidewalk arc near
        /// the diagonal (local ~(-1.56, 1.56)) is inside those nav bands while standing on real
        /// pavement. Under the wider test the nearest clear point is six metres away across the
        /// carriageway, so the recovery would march people across a street to fix nothing.
        /// </summary>
        public static bool OnCarriageway(RoadShape shape, float localX, float localZ) =>
            OnCarriageway(shape, localX, localZ, 0f);

        public static bool OnCarriageway(RoadShape shape, float localX, float localZ, float margin)
        {
            if (shape.Shape != SurfaceShape.Curve)
                return OnAsphalt(shape, localX, localZ, margin);

            var dx = localX + CurveRadius;
            var dz = localZ - CurveRadius;
            return Mathf.Abs(Mathf.Sqrt(dx * dx + dz * dz) - CurveRadius) <= shape.BandX + margin;
        }

        // ------------------------------------------------------------------ getting off it

        /// <summary>
        /// How far past the asphalt a cleared pedestrian is put. One metre lands mid-pavement on
        /// both families (street band 3.25 -> 4.25, pavement 3..5, props from 5.5; avenue 6.5 ->
        /// 7.5, pavement 6.25..8.5, walk line 7.25) and it has two jobs beyond looking right.
        ///
        /// It must exceed HumanBehavior's 0.75m avoidance arrival radius, or a walker that
        /// "arrives" at the clear point while being steered around somebody is still on the road.
        ///
        /// And it must clear the crosswalk TRIGGER, which is the whole point of the exercise and
        /// is wider than the kerb test: the boxes on tile-road-straight-crosswalk reach |x| =
        /// 3.09 (6.32 on the avenue) and a pedestrian capsule is 0.3 across, so contact ends at
        /// 3.39 / 6.62 - past the 3.25 / 6.5 kerb margin. A walker recovered to 4.25 / 7.5 stands
        /// 0.86 / 0.88 clear of it and cannot hold the traffic.
        /// </summary>
        public const float ClearMargin = 1f;

        /// <summary>Keeps a candidate placed exactly at the margin from failing its own test.</summary>
        const float ClearEpsilon = 0.01f;

        /// <summary>
        /// The nearest tile-local point that is clear of this tile's carriageway by
        /// <see cref="ClearMargin"/>.
        ///
        /// Candidates rather than a formula: a junction is two crossing bands, a curve is an
        /// annulus, and a dead end is an arm plus a turning circle - so the escape from one piece
        /// routinely lands inside another (the dead end is the sharp case: pushing radially out
        /// of the turning circle along the centre line lands back on the arm). Offering every
        /// escape a shape has and keeping the nearest one that clears them ALL is both shorter
        /// and harder to get wrong than chaining pushes.
        ///
        /// False means there is nothing to do - either the point is already clear, or nothing
        /// offered clears, in which case the caller stays put rather than walking somewhere
        /// worse. The outputs are left at the input in both cases.
        /// </summary>
        public static bool TryOffAsphalt(RoadShape shape, float localX, float localZ,
                                         out float outX, out float outZ)
        {
            outX = localX;
            outZ = localZ;

            if (!OnCarriageway(shape, localX, localZ))
                return false;

            var bestSqr = float.MaxValue;
            var bestX = localX;
            var bestZ = localZ;
            var found = false;

            void Consider(float cx, float cz)
            {
                if (OnCarriageway(shape, cx, cz, ClearMargin - ClearEpsilon))
                    return;

                var d = (cx - localX) * (cx - localX) + (cz - localZ) * (cz - localZ);
                if (d >= bestSqr)
                    return;

                bestSqr = d;
                bestX = cx;
                bestZ = cz;
                found = true;
            }

            // The dead end's arm is only as wide as its band, but stepping sideways out of it
            // can still leave you inside the turning circle - so its sideways escape clears
            // both at once.
            var reachX = shape.Shape == SurfaceShape.DeadEnd
                ? Mathf.Max(shape.BandX, TurningCircle) + ClearMargin
                : shape.BandX + ClearMargin;
            var reachZ = shape.BandZ + ClearMargin;

            if (shape.BandX > 0f)
            {
                Consider(reachX, localZ);
                Consider(-reachX, localZ);
            }

            if (shape.BandZ > 0f)
            {
                Consider(localX, reachZ);
                Consider(localX, -reachZ);
            }

            // A junction's corner quadrants: the only ground on the tile that is clear of both
            // carriageways, and where its pavement actually is.
            if (shape.BandX > 0f && shape.BandZ > 0f)
            {
                Consider(reachX, reachZ);
                Consider(reachX, -reachZ);
                Consider(-reachX, reachZ);
                Consider(-reachX, -reachZ);
            }

            switch (shape.Shape)
            {
                case SurfaceShape.Curve:
                {
                    // Radially off the bend, to either side of it. These land on the pack's own
                    // sidewalk arcs: 15 - 4.25 = 10.75 and 15 + 4.25 = 19.25, against walk lines
                    // measured at 11 and 19.
                    var dx = localX + CurveRadius;
                    var dz = localZ - CurveRadius;
                    var r = Mathf.Sqrt(dx * dx + dz * dz);
                    if (r > 1e-3f)
                    {
                        var ux = dx / r;
                        var uz = dz / r;
                        var inner = CurveRadius - reachX;
                        var outer = CurveRadius + reachX;
                        Consider(-CurveRadius + ux * inner, CurveRadius + uz * inner);
                        Consider(-CurveRadius + ux * outer, CurveRadius + uz * outer);
                    }
                    break;
                }

                case SurfaceShape.DeadEnd:
                {
                    // Straight out of the turning circle, past the sidewalk ring at ~4.2.
                    var r = Mathf.Sqrt(localX * localX + localZ * localZ);
                    var ring = TurningCircle + ClearMargin;
                    if (r > 1e-3f)
                        Consider(localX / r * ring, localZ / r * ring);
                    break;
                }
            }

            outX = bestX;
            outZ = bestZ;
            return found;
        }

        // ------------------------------------------------------------------ runtime adapter

        struct Entry
        {
            public Transform Tile;
            public RoadShape Shape;
        }

        static readonly Dictionary<long, Entry> Cells = new Dictionary<long, Entry>();
        static int builtFromTileCount = -1;
        static float latticeOffsetX, latticeOffsetZ;

        /// <summary>
        /// A query more than this far above or below the tile is on another level (a bridge
        /// deck over a ground road), not on this tile's asphalt.
        /// </summary>
        const float MaxHeightDelta = 6f;

        /// <summary>
        /// Is this world position on a carriageway? False wherever there is no generated road
        /// tile - block interiors, parks, pack demo scenes.
        /// </summary>
        public static bool IsOnRoad(Vector3 world) =>
            TryLocal(world, out var entry, out var local)
            && OnAsphalt(entry.Shape, local.x, local.z);

        /// <summary>
        /// The nearest world point clear of the carriageway, for anyone who has to STOP and finds
        /// themselves standing on it. Crossing the road is normal; coming to rest on it is what
        /// stops the traffic.
        ///
        /// True only when there is somewhere to go, so the whole "am I standing in the road, and
        /// if so where should I step" question is one call: false covers already being clear,
        /// there being no road here at all (block interiors, parks, a bridge deck over a road,
        /// pack demo scenes), and the rare case where nothing clears.
        /// </summary>
        public static bool TryNearestOffRoad(Vector3 world, out Vector3 point)
        {
            point = world;

            if (!TryLocal(world, out var entry, out var local))
                return false;

            if (!TryOffAsphalt(entry.Shape, local.x, local.z, out var offX, out var offZ))
                return false;

            point = entry.Tile.TransformPoint(new Vector3(offX, local.y, offZ));
            return true;
        }

        /// <summary>
        /// Which generated road tile covers this world point, and where the point sits in its
        /// local frame. False when no road tile does - which is the common case city-wide.
        /// </summary>
        static bool TryLocal(Vector3 world, out Entry entry, out Vector3 local)
        {
            entry = default;
            local = default;

            var tiles = Tile.Tiles;
            if (tiles == null || tiles.Count == 0)
                return false;

            // The road network is built once, synchronously, before anyone walks (the
            // JunctionMap precondition) - but a Play-mode rebuild replaces every tile, so a
            // changed count or a dead cached transform invalidates the lot.
            if (tiles.Count != builtFromTileCount)
                Rebuild(tiles);

            if (!Cells.TryGetValue(CellKey(world), out entry))
                return false;

            if (!entry.Tile)
            {
                builtFromTileCount = -1;
                return false;
            }

            local = entry.Tile.InverseTransformPoint(world);
            return Mathf.Abs(local.y) <= MaxHeightDelta;
        }

        static long CellKey(Vector3 world)
        {
            var cx = Mathf.RoundToInt((world.x - latticeOffsetX) / CityGrid.CellSize);
            var cz = Mathf.RoundToInt((world.z - latticeOffsetZ) / CityGrid.CellSize);
            return ((long)cx << 32) ^ (uint)cz;
        }

        static void Rebuild(List<Tile> tiles)
        {
            Cells.Clear();
            builtFromTileCount = tiles.Count;
            var haveOffset = false;

            foreach (var tile in tiles)
            {
                if (!tile || !TryParseKind(tile.name, out var kind))
                    continue;

                // The lattice is anchored to where the tiles actually are, not to the world
                // origin - a city built under a moved parent must still land on exact cells.
                if (!haveOffset)
                {
                    var p = tile.transform.position;
                    latticeOffsetX = p.x - Mathf.Round(p.x / CityGrid.CellSize) * CityGrid.CellSize;
                    latticeOffsetZ = p.z - Mathf.Round(p.z / CityGrid.CellSize) * CityGrid.CellSize;
                    haveOffset = true;
                }

                var key = CellKey(tile.transform.position);

                // Two tiles on one cell only happens with a bridge deck stacked over a ground
                // road; keep the ground one - pedestrians walk at street level.
                if (Cells.TryGetValue(key, out var existing) && existing.Tile
                    && Mathf.Abs(existing.Tile.position.y) <= Mathf.Abs(tile.transform.position.y))
                    continue;

                Cells[key] = new Entry { Tile = tile.transform, Shape = Classify(kind) };
            }
        }
    }
}
