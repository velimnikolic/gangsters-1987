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
    /// tile_{x}_{y}_{RoadTileKind} (RoadNetworkBuilder), sits on the 30m cell lattice, and its
    /// cross-section is fixed by the prefab family. So the kind is recovered from the name,
    /// the tile from a cell dictionary (the JunctionMap approach: derive from Tile, cache
    /// forever), and the asphalt test runs in tile-local space where the numbers are constants.
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

        /// <summary>The curve's road centreline arc: half the cell, around the shared corner.</summary>
        public const float CurveRadius = CityGrid.CellSize * 0.5f;

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
        public static bool OnAsphalt(RoadShape shape, float localX, float localZ)
        {
            switch (shape.Shape)
            {
                case SurfaceShape.Bands:
                    return (shape.BandX > 0f && Mathf.Abs(localX) <= shape.BandX)
                           || (shape.BandZ > 0f && Mathf.Abs(localZ) <= shape.BandZ);

                case SurfaceShape.Curve:
                {
                    var dx = localX + CurveRadius;
                    var dz = localZ - CurveRadius;
                    var fromCorner = Mathf.Sqrt(dx * dx + dz * dz);
                    return Mathf.Abs(fromCorner - CurveRadius) <= shape.BandX
                           || Mathf.Abs(localX) <= shape.BandX
                           || Mathf.Abs(localZ) <= shape.BandZ;
                }

                case SurfaceShape.DeadEnd:
                    // The arm reaches the connected (+z) edge only - past the turning circle
                    // the sidewalk ring wraps round the bottom of the tile and must stay free.
                    return (Mathf.Abs(localX) <= shape.BandX && localZ >= 0f)
                           || Mathf.Sqrt(localX * localX + localZ * localZ) <= TurningCircle;

                default:
                    return false;
            }
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
        public static bool IsOnRoad(Vector3 world)
        {
            var tiles = Tile.Tiles;
            if (tiles == null || tiles.Count == 0)
                return false;

            // The road network is built once, synchronously, before anyone walks (the
            // JunctionMap precondition) - but a Play-mode rebuild replaces every tile, so a
            // changed count or a dead cached transform invalidates the lot.
            if (tiles.Count != builtFromTileCount)
                Rebuild(tiles);

            if (!Cells.TryGetValue(CellKey(world), out var entry))
                return false;

            if (!entry.Tile)
            {
                builtFromTileCount = -1;
                return false;
            }

            var local = entry.Tile.InverseTransformPoint(world);
            if (Mathf.Abs(local.y) > MaxHeightDelta)
                return false;

            return OnAsphalt(entry.Shape, local.x, local.z);
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
