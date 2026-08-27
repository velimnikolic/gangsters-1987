using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The city's blocks as runtime data: id, zone, world-XZ rects, centre. CityGrid is
    /// generation-time only (null at Play in a saved scene), so runtime block identity
    /// has always been the ground-slab name parse - "ground_{zone}_{blockId}[_{x}_{y}]"
    /// under Generated City/Ground. That parse now lives in three private collectors
    /// (BlockOverlayHud, PropertyDirector, StrategicMapHud); this registry is the fourth
    /// consumer and the first SHARED one, built for the strategy layer (territory,
    /// orders, distances). The three existing private collectors are left untouched on
    /// purpose - they belong to other passes and reworking them buys nothing but risk;
    /// new code should read from here.
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
        /// be on a street, which belongs to nobody.</summary>
        public static BlockInfo At(Vector2 worldXZ)
        {
            EnsureCollected();
            foreach (var block in Known)
            {
                if (!block.Union.Contains(worldXZ))
                    continue;
                foreach (var slab in block.Slabs)
                    if (slab.Contains(worldXZ))
                        return block;
            }
            return null;
        }

        static void EnsureCollected()
        {
            if (collected)
                return;
            collected = true;

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

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Known.Clear();
            ById.Clear();
            collected = false;
        }
    }
}
