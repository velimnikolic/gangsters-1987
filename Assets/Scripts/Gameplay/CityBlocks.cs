using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;
using LivingCity.Territory;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The city's blocks as runtime data: id, zone, world-XZ rects, centre.
    ///
    /// This is a COMPATIBILITY SHIM over the canonical geography, not a second survey of
    /// the city. Where a territory plan exists (every Core scene, which is the game), the
    /// table is projected from <see cref="ITerritoryGeography"/>: same rectangles, same
    /// ids, so the ledger's orders, the strategic map and the simulation all name one
    /// physical block the same way. Only a scene with NO canonical plan - the older
    /// CityBuilder-generated city - falls back to the historical ground-slab name parse
    /// ("ground_{zone}_{blockId}[_{x}_{y}]" under Generated City/Ground), which is what
    /// runtime identity there has always been.
    ///
    /// New code should read canonical geography directly; this exists for the consumers
    /// that speak the legacy integer block id (PersonnelAlmanac orders, StrategicMapHud).
    /// </summary>
    public static class CityBlocks
    {
        public sealed class BlockInfo
        {
            public int Id;
            public BlockZone Zone;

            /// <summary>Union of the block's slab rects, world XZ (x = worldX, y = worldZ).</summary>
            public Rect Union;

            public readonly List<Rect> Slabs = new List<Rect>();

            public Vector2 Center => Union.center;
        }

        static readonly List<BlockInfo> Known = new List<BlockInfo>();
        static readonly Dictionary<int, BlockInfo> ById = new Dictionary<int, BlockInfo>();
        static bool collected;
        static ITerritoryGeography canonical;

        // A uniform grid over the city, so At() reads a handful of candidate blocks
        // instead of walking all of them. The fog of war asks At() for close to three
        // thousand actors a frame in the full city; against 193 blocks that walk was
        // 4.5 ms of every frame (measured 2026-09-06). Cells hold, in Known order, every
        // block whose union rect overlaps them, so the first match in a cell is the
        // first match the walk would have found.
        const float CellSize = 48f;
        const int MaxCells = 1 << 16;
        static List<BlockInfo>[] cells = System.Array.Empty<List<BlockInfo>>();
        static float cellSize = CellSize;
        static float gridMinX, gridMinY;
        static int gridCols, gridRows;

        public static IReadOnlyList<BlockInfo> Blocks
        {
            get
            {
                EnsureCollected();
                return Known;
            }
        }

        public static BlockInfo Get(int blockId)
        {
            EnsureCollected();
            return ById.TryGetValue(blockId, out var block) ? block : null;
        }

        /// <summary>Nearest block by centre distance; null in a city with no ground.</summary>
        public static BlockInfo Nearest(Vector2 worldXZ)
        {
            EnsureCollected();
            BlockInfo best = null;
            var bestSqr = float.MaxValue;
            foreach (var block in Known)
            {
                var d = block.Center - worldXZ;
                var sqr = d.x * d.x + d.y * d.y;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = block;
                }
            }
            return best;
        }

        /// <summary>The block whose slabs contain the point, else null - the point may
        /// be on a street, which belongs to nobody. Fog can include inter-slab gaps
        /// to keep an unrevealed block's courtyards private as well.</summary>
        public static BlockInfo At(Vector2 worldXZ, bool includeInterSlabGaps = false)
        {
            EnsureCollected();
            if (gridCols == 0)
                return null;
            int cx = (int)((worldXZ.x - gridMinX) / cellSize);
            int cy = (int)((worldXZ.y - gridMinY) / cellSize);
            if (cx < 0 || cy < 0 || cx >= gridCols || cy >= gridRows)
                return null;
            var candidates = cells[cy * gridCols + cx];
            if (candidates == null)
                return null;
            for (var i = 0; i < candidates.Count; i++)
            {
                var block = candidates[i];
                if (!block.Union.Contains(worldXZ))
                    continue;
                if (includeInterSlabGaps) return block;
                foreach (var slab in block.Slabs)
                    if (slab.Contains(worldXZ))
                        return block;
            }
            return null;
        }

        static void BuildGrid()
        {
            gridCols = gridRows = 0;
            cells = System.Array.Empty<List<BlockInfo>>();
            if (Known.Count == 0)
                return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var block in Known)
            {
                var r = block.Union;
                if (r.xMin < minX) minX = r.xMin;
                if (r.yMin < minY) minY = r.yMin;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMax > maxY) maxY = r.yMax;
            }
            if (!(maxX > minX) || !(maxY > minY))
                return;

            // a city too wide for the default cell simply gets a coarser one
            cellSize = CellSize;
            while ((long)(Mathf.CeilToInt((maxX - minX) / cellSize) + 1) *
                   (Mathf.CeilToInt((maxY - minY) / cellSize) + 1) > MaxCells)
                cellSize *= 2f;

            gridMinX = minX;
            gridMinY = minY;
            gridCols = Mathf.CeilToInt((maxX - minX) / cellSize) + 1;
            gridRows = Mathf.CeilToInt((maxY - minY) / cellSize) + 1;
            cells = new List<BlockInfo>[gridCols * gridRows];
            foreach (var block in Known)
            {
                var r = block.Union;
                int x0 = Mathf.Clamp((int)((r.xMin - minX) / cellSize), 0, gridCols - 1);
                int x1 = Mathf.Clamp((int)((r.xMax - minX) / cellSize), 0, gridCols - 1);
                int y0 = Mathf.Clamp((int)((r.yMin - minY) / cellSize), 0, gridRows - 1);
                int y1 = Mathf.Clamp((int)((r.yMax - minY) / cellSize), 0, gridRows - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = y * gridCols + x;
                        (cells[k] ??= new List<BlockInfo>(4)).Add(block);
                    }
            }
        }

        static void EnsureCollected()
        {
            // The canonical plan appears when the city finishes building, which can be
            // after a first, empty question. Rebuild once when it does, and never after.
            var geography = RoadDemo.TerritoryRuntime.Instance?.Geography;
            if (collected && (geography == null || ReferenceEquals(geography, canonical)))
                return;

            Known.Clear();
            ById.Clear();
            collected = true;
            canonical = geography;

            Collect(geography);
            BuildGrid();
        }

        static void Collect(ITerritoryGeography geography)
        {
            if (CollectCanonical(geography))
                return;

            var city = GameObject.Find("Generated City");
            var ground = city ? city.transform.Find("Ground") : null;
            if (!ground)
            {
                Debug.LogWarning("[CityBlocks] No Generated City/Ground in the scene - " +
                                 "the block table is empty.");
                return;
            }

            foreach (Transform child in ground)
            {
                var parts = child.name.Split('_');
                if (parts.Length < 3 || parts[0] != "ground")
                    continue;
                if (!System.Enum.TryParse(parts[1], out BlockZone zone))
                    continue;
                if (!int.TryParse(parts[2], out var blockId))
                    continue;

                var renderer = child.GetComponent<Renderer>();
                if (!renderer)
                    continue;

                var bounds = renderer.bounds;
                var rect = new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);

                if (!ById.TryGetValue(blockId, out var block))
                {
                    block = new BlockInfo { Id = blockId, Zone = zone, Union = rect };
                    ById[blockId] = block;
                    Known.Add(block);
                }

                block.Slabs.Add(rect);
                var union = block.Union;
                union.xMin = Mathf.Min(union.xMin, rect.xMin);
                union.yMin = Mathf.Min(union.yMin, rect.yMin);
                union.xMax = Mathf.Max(union.xMax, rect.xMax);
                union.yMax = Mathf.Max(union.yMax, rect.yMax);
                block.Union = union;
            }
        }

        /// <summary>The canonical projection: one entry per plan block, its world bounds
        /// as its single slab. A block the plan gives no legacy number is skipped rather
        /// than renumbered - a made-up integer id is exactly the divergence this shim
        /// exists to end.</summary>
        static bool CollectCanonical(ITerritoryGeography geography)
        {
            if (geography == null)
                return false;

            var ids = geography.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (!geography.TryGetBlock(ids[i], out var definition) ||
                    definition.LegacyBlockId < 0 || ById.ContainsKey(definition.LegacyBlockId))
                    continue;

                var bounds = definition.WorldBounds;
                var rect = new Rect(bounds.XMin, bounds.ZMin, bounds.Width, bounds.Depth);
                var block = new BlockInfo
                {
                    Id = definition.LegacyBlockId,
                    Zone = ZoneOf(definition.SourceKind),
                    Union = rect,
                };
                block.Slabs.Add(rect);
                ById.Add(block.Id, block);
                Known.Add(block);
            }

            return Known.Count > 0;
        }

        /// <summary>The plan's word for what a block is, in the zone vocabulary the
        /// legacy consumers colour and label with. Presentation only - no rule of the
        /// simulation reads it.</summary>
        static BlockZone ZoneOf(string sourceKind)
        {
            if (string.IsNullOrEmpty(sourceKind))
                return BlockZone.ResidentialHigh;
            if (sourceKind == "park")
                return BlockZone.Park;
            if (sourceKind == "apron")
                return BlockZone.Parking;
            if (sourceKind == "quay")
                return BlockZone.Port;
            if (sourceKind == "bank")
                return BlockZone.Bank;
            if (sourceKind.StartsWith("yard-"))
                return BlockZone.Industrial;
            return BlockZone.ResidentialHigh;
        }

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Known.Clear();
            ById.Clear();
            collected = false;
            canonical = null;
            gridCols = gridRows = 0;
            cells = System.Array.Empty<List<BlockInfo>>();
        }
    }
}
