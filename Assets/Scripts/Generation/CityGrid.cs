using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    [Flags]
    public enum Sides
    {
        None = 0,
        North = 1 << 0, // +Z
        East = 1 << 1,  // +X
        South = 1 << 2, // -Z
        West = 1 << 3,  // -X
        All = North | East | South | West,
    }

    public enum CellType
    {
        /// <summary>
        /// Land that is neither street nor building plot. The city generator fills every cell
        /// before it returns, so nothing is Empty in a finished layout - but this is
        /// deliberately the enum's default so a freshly allocated grid reads as empty ground
        /// rather than as one giant block, and so AssignBlockIds has something to mean
        /// "do not build here" if a future layout ever leaves gaps.
        /// </summary>
        Empty,
        Block,
        Road,
    }

    /// <summary>
    /// Pure grid data for the city layout - no scene objects, no prefabs.
    /// Keeping this free of Unity dependencies (beyond Vector types) means the
    /// layout can be generated, validated and unit-tested without entering play mode.
    /// </summary>
    public sealed class CityGrid
    {
        /// <summary>
        /// Tile size in world units. Measured from the prefabs, not guessed:
        ///
        /// - tile-road-straight's road paths run from z = -15 to z = +15, and its sidewalk
        ///   paths likewise. tile-road-curve runs z = +15 to x = -15. So a tile spans
        ///   [-15, +15] on both axes: 30 units.
        /// - Tile.GetNeighborTiles() probes 18 units out with a half-extent of 2, i.e. the
        ///   box covers 16..20. That is PAST this tile's own edge at 15 and INSIDE the
        ///   neighbour's mesh collider, which spans 15..45. The 18 is a probe distance that
        ///   happens to land in the neighbour - it is NOT the tile spacing.
        /// - The proof is the 1.5m link tolerance in Tile.GetNextPaths(): at 30 spacing this
        ///   tile's path end (z=+15) and the neighbour's path start (30-15=15) coincide
        ///   exactly. At 18 spacing they would be 12m apart and nothing would ever connect.
        /// </summary>
        public const float CellSize = 30f;

        /// <summary>Lane centre offset from the tile's centre line (road paths sit at x = +/-1.5).</summary>
        public const float LaneOffset = 1.5f;

        /// <summary>Sidewalk centre offset from the tile's centre line (sidewalk paths sit at x = +/-4).</summary>
        public const float SidewalkOffset = 4f;

        /// <summary>
        /// Outer edge of the road tile's OWN pavement, measured off the centre line. The tile's
        /// cross-section is asphalt out to 3, a kerb face at 3, pavement 3..5, then grass all the
        /// way to the tile edge at 15 - one mesh with one material, so that grass cannot be hidden,
        /// only covered. This is where GroundPlacer's apron starts covering it.
        /// </summary>
        public const float PavementEdge = 5f;

        /// <summary>
        /// The same three measurements for the dual carriageway (tile-mainroad-*), read out of
        /// the prefabs the same way - by resolving each Path component's end nodes, not by
        /// eyeballing the mesh:
        ///
        ///   inner lanes  x = +/-1.75     (MainLaneOffset)
        ///   outer lanes  x = +/-4.75     (MainOuterLaneOffset)
        ///   pavements    x = +/-7.25     (MainSidewalkOffset)
        ///
        /// Every one of them is OUTSIDE its minor-road counterpart, which is the whole reason
        /// the boulevard needs its own set of clearances: a prop line sized for a street at 5.5
        /// stands in the boulevard's outer lane, and a building line at 7 stands on its pavement.
        ///
        /// The tile module is unchanged at 30 - the mainroad tiles span [-15, +15] exactly like
        /// the road tiles, which is what lets them drop into the same grid.
        /// </summary>
        public const float MainLaneOffset = 1.75f;

        /// <inheritdoc cref="MainLaneOffset"/>
        public const float MainOuterLaneOffset = 4.75f;

        /// <inheritdoc cref="MainLaneOffset"/>
        public const float MainSidewalkOffset = 7.25f;

        /// <summary>
        /// PavementEdge's counterpart on the dual carriageway - where its own paving stops and
        /// its grass begins, which is where GroundPlacer's apron has to start.
        ///
        /// Inferred rather than measured directly: the pavement is a mesh, not a path, so there
        /// is no node to read. The road tile gives the relation - a sidewalk path at 4 sitting
        /// in the middle of a 3..5 pavement, i.e. one metre either side of the walking line.
        /// Applying that to the avenue's 7.25 puts its pavement at 6.25..8.25, which the lane
        /// geometry independently agrees with: the outer lane centre is 4.75, so about 3m of
        /// asphalt reaches the kerb at 6.25.
        ///
        /// Rounded OUT to 8.5 rather than to the estimate. Too far out leaves at worst a
        /// hand's width of grass the apron does not cover; too far in lays concrete over the
        /// pavement mesh, and two coplanar surfaces z-fight in every frame.
        /// </summary>
        public const float MainPavementEdge = 8.5f;

        const int NoBlock = -1;

        readonly CellType[,] cells;
        readonly int[,] blockIds;

        /// <summary>
        /// Which road cells belong to the city's one dual carriageway.
        ///
        /// A parallel mask rather than a third CellType on purpose. Every consumer of the grid
        /// asks "is this a road?" - RoadsAreConnected, AssignBlockIds, NeighbourBlocks,
        /// BlockBuilder.RoadSides, MapEdgeGates - and the boulevard IS a road to all of them.
        /// A new CellType would have meant auditing each one to add it back, and any that was
        /// missed would fail silently: a boulevard cell read as unbuildable land, blocks flood
        /// -filled through it, buildings dropped on the avenue. The mask can only be wrong in
        /// the one place that reads it.
        /// </summary>
        readonly bool[,] mainRoad;

        /// <summary>
        /// What each block is for, indexed by block id. Lives here rather than being threaded
        /// through as a parameter because every placer already takes the grid - GroundPlacer,
        /// BlockBuilder and StreetPropPlacer all need the zone, and passing it separately would
        /// mean four signatures that can disagree with each other. A BlockZone is pure data, so
        /// it does not break this class's rule about staying free of Unity and prefabs.
        /// </summary>
        BlockZone[] blockZones = System.Array.Empty<BlockZone>();

        /// <summary>
        /// A landmark this block MUST build, as an index into its palette's landmarks[], or -1
        /// for the usual behaviour of rolling landmarkChance and drawing from the bag.
        ///
        /// An index rather than a prefab, and that is the whole reason this can live here: the
        /// grid stays free of UnityEngine.Object exactly as blockZones does, so the zoning half
        /// of the generator is still exercisable in a bare .NET host with no Unity around it.
        /// ZonePlanner is the only writer - see the bank's route roll for why one block sometimes
        /// has to be told rather than asked.
        /// </summary>
        int[] forcedLandmarks = System.Array.Empty<int>();

        public int Width { get; }
        public int Height { get; }
        public int BlockCount { get; private set; }

        /// <summary>
        /// Which way the boulevard runs. Recorded when it is carved rather than re-derived from
        /// the mask, because the tile lookup needs the axis for the END cells too - and there
        /// the mask alone cannot tell, since a cell with only a North neighbour is the same
        /// shape whether the avenue runs N-S or the map simply sliced an E-W one.
        ///
        /// Meaningless while HasMainRoad is false.
        /// </summary>
        public bool MainRoadNorthSouth { get; private set; }

        /// <summary>False on a map too small to be subdivided at all - see CityGenerator.</summary>
        public bool HasMainRoad { get; private set; }

        public CityGrid(int width, int height)
        {
            Width = width;
            Height = height;
            cells = new CellType[width, height];
            blockIds = new int[width, height];
            mainRoad = new bool[width, height];

            for (var x = 0; x < width; x++)
            for (var z = 0; z < height; z++)
                blockIds[x, z] = NoBlock;
        }

        public CellType this[int x, int z]
        {
            get => cells[x, z];
            set => cells[x, z] = value;
        }

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;

        public bool IsRoad(int x, int z) => InBounds(x, z) && cells[x, z] == CellType.Road;

        /// <summary>
        /// True for a cell on the dual carriageway. Tests the cell type as well as the mask, so
        /// this can never answer yes for something that is not a road - the two are set together
        /// and nothing un-paves a cell afterwards, but a placer acting on a stale yes would put
        /// buildings in a traffic lane, and that is not a failure worth leaving open.
        /// </summary>
        public bool IsMainRoad(int x, int z) =>
            InBounds(x, z) && cells[x, z] == CellType.Road && mainRoad[x, z];

        /// <summary>Marks one cell of the boulevard. CityGenerator carves the whole line in one pass.</summary>
        public void SetMainRoad(int x, int z, bool northSouth)
        {
            if (!InBounds(x, z))
                return;

            mainRoad[x, z] = true;
            MainRoadNorthSouth = northSouth;
            HasMainRoad = true;
        }

        public int BlockIdAt(int x, int z) => InBounds(x, z) ? blockIds[x, z] : NoBlock;

        /// <summary>
        /// Which sides of this cell have a road neighbour. Feeds RoadTileTable.Lookup
        /// to pick the tile prefab and its Y rotation.
        /// </summary>
        public Sides GetNeighborMask(int x, int z)
        {
            var sides = Sides.None;
            if (IsRoad(x, z + 1)) sides |= Sides.North;
            if (IsRoad(x + 1, z)) sides |= Sides.East;
            if (IsRoad(x, z - 1)) sides |= Sides.South;
            if (IsRoad(x - 1, z)) sides |= Sides.West;
            return sides;
        }

        public IEnumerable<Vector2Int> RoadCells()
        {
            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
                if (cells[x, z] == CellType.Road)
                    yield return new Vector2Int(x, z);
        }

        /// <summary>
        /// What a block is for. Defaults to ResidentialHigh - the city's connective tissue - so
        /// a grid that has not been through ZonePlanner still builds something coherent rather
        /// than throwing or producing bare lots.
        /// </summary>
        public BlockZone ZoneOf(int blockId) =>
            blockId >= 0 && blockId < blockZones.Length ? blockZones[blockId] : BlockZone.ResidentialHigh;

        public void SetZone(int blockId, BlockZone zone)
        {
            if (blockId >= 0 && blockId < blockZones.Length)
                blockZones[blockId] = zone;
        }

        /// <summary>
        /// The landmark this block is required to build, as an index into its palette's
        /// landmarks[], or -1 for "roll for it like every other block".
        /// </summary>
        public int ForcedLandmarkOf(int blockId) =>
            blockId >= 0 && blockId < forcedLandmarks.Length ? forcedLandmarks[blockId] : -1;

        public void SetForcedLandmark(int blockId, int landmarkIndex)
        {
            if (blockId >= 0 && blockId < forcedLandmarks.Length)
                forcedLandmarks[blockId] = landmarkIndex;
        }

        /// <summary>
        /// The blocks that face this one across a street.
        ///
        /// Blocks are never orthogonally adjacent - a street always separates them - so the
        /// usual four-neighbour cell test finds nothing at all. Two blocks are neighbours when
        /// exactly one road cell lies between them, which is what the two-cell probe tests.
        ///
        /// Exactly one, never two: CityGenerator.Subdivide leaves at least a block's worth of
        /// land on both sides of every cut, and a block is at least one cell, so no two cuts
        /// can end up side by side.
        ///
        /// Wanted by two callers for opposite reasons: ZonePlanner uses it to keep two parks
        /// apart, and BlockBuilder uses it to let a slot occasionally borrow the neighbouring
        /// block's palette. Both need the same notion of "across the road", so it lives here.
        /// </summary>
        public IEnumerable<int> NeighbourBlocks(int blockId)
        {
            var seen = new HashSet<int>();

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
            {
                if (blockIds[x, z] != blockId)
                    continue;

                foreach (var step in Orthogonal(Vector2Int.zero))
                {
                    if (!IsRoad(x + step.x, z + step.y))
                        continue;

                    var other = BlockIdAt(x + step.x * 2, z + step.y * 2);
                    if (other == NoBlock || other == blockId || !seen.Add(other))
                        continue;

                    yield return other;
                }
            }
        }

        public IEnumerable<Vector2Int> CellsInBlock(int blockId)
        {
            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
                if (blockIds[x, z] == blockId)
                    yield return new Vector2Int(x, z);
        }

        /// <summary>Flood-fills contiguous non-road cells into numbered blocks.</summary>
        public void AssignBlockIds()
        {
            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
                blockIds[x, z] = NoBlock;

            BlockCount = 0;
            var frontier = new Queue<Vector2Int>();

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
            {
                // Only Block cells become buildable blocks - Empty is the land outside the
                // city that the exit roads run through.
                if (cells[x, z] != CellType.Block || blockIds[x, z] != NoBlock)
                    continue;

                var id = BlockCount++;
                blockIds[x, z] = id;
                frontier.Enqueue(new Vector2Int(x, z));

                while (frontier.Count > 0)
                {
                    var cell = frontier.Dequeue();
                    foreach (var next in Orthogonal(cell))
                    {
                        if (!InBounds(next.x, next.y)) continue;
                        if (cells[next.x, next.y] != CellType.Block) continue;
                        if (blockIds[next.x, next.y] != NoBlock) continue;

                        blockIds[next.x, next.y] = id;
                        frontier.Enqueue(next);
                    }
                }
            }

            // Sized here rather than in the constructor because BlockCount is only known once
            // the flood fill has run. Every entry starts at ResidentialHigh (enum default 0).
            blockZones = new BlockZone[BlockCount];

            // -1 is "no forced landmark", so this one cannot lean on the array's zero fill the
            // way blockZones does: 0 is a valid landmark index.
            forcedLandmarks = new int[BlockCount];
            for (var i = 0; i < forcedLandmarks.Length; i++)
                forcedLandmarks[i] = -1;
        }

        /// <summary>
        /// True when every road cell is reachable from every other. An isolated island
        /// would let cars spawn somewhere pathfinding can never route out of, which
        /// surfaces as an endless stream of "Path not found" warnings.
        /// </summary>
        public bool RoadsAreConnected()
        {
            var total = 0;
            var start = new Vector2Int(-1, -1);
            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Height; z++)
                if (cells[x, z] == CellType.Road)
                {
                    total++;
                    if (start.x < 0) start = new Vector2Int(x, z);
                }

            if (total == 0)
                return false;

            var seen = new bool[Width, Height];
            var frontier = new Queue<Vector2Int>();
            seen[start.x, start.y] = true;
            frontier.Enqueue(start);
            var reached = 1;

            while (frontier.Count > 0)
            {
                var cell = frontier.Dequeue();
                foreach (var next in Orthogonal(cell))
                {
                    if (!IsRoad(next.x, next.y) || seen[next.x, next.y]) continue;
                    seen[next.x, next.y] = true;
                    reached++;
                    frontier.Enqueue(next);
                }
            }

            return reached == total;
        }

        public Vector3 CellToWorld(int x, int z) => new Vector3(x * CellSize, 0f, z * CellSize);

        public Vector3 CellToWorld(Vector2Int cell) => CellToWorld(cell.x, cell.y);

        public Bounds WorldBounds
        {
            get
            {
                var size = new Vector3((Width - 1) * CellSize, 0f, (Height - 1) * CellSize);
                return new Bounds(size * 0.5f, size + new Vector3(CellSize, 0f, CellSize));
            }
        }

        static IEnumerable<Vector2Int> Orthogonal(Vector2Int cell)
        {
            yield return new Vector2Int(cell.x, cell.y + 1);
            yield return new Vector2Int(cell.x + 1, cell.y);
            yield return new Vector2Int(cell.x, cell.y - 1);
            yield return new Vector2Int(cell.x - 1, cell.y);
        }
    }
}
