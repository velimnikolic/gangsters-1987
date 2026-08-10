using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>
    /// Which gang holds which block. One dictionary, exclusive by construction - a
    /// block has one owner or none - the Personnel layer's single-source rule applied
    /// to ground. Unclaimed blocks simply have no entry.
    /// </summary>
    public sealed class TerritoryMap
    {
        readonly Dictionary<int, int> ownerByBlock = new Dictionary<int, int>();

        public bool Seeded => ownerByBlock.Count > 0;

        /// <summary>-1 = unclaimed ground.</summary>
        public int OwnerOf(int blockId) =>
            ownerByBlock.TryGetValue(blockId, out var gangId) ? gangId : -1;

        public void Claim(int blockId, int gangId) => ownerByBlock[blockId] = gangId;

        public int CountOf(int gangId)
        {
            var count = 0;
            foreach (var entry in ownerByBlock)
                if (entry.Value == gangId)
                    count++;
            return count;
        }

        /// <summary>The map tint enumerates every claim in one pass.</summary>
        public IReadOnlyDictionary<int, int> Claims => ownerByBlock;
    }

    /// <summary>
    /// Day-one turf, derived - no rng. Every gang's front block is its first claim;
    /// each rival then grows to <see cref="RivalBlocks"/> by repeatedly taking the
    /// nearest unclaimed block to its front, ties to the lower block id. The player
    /// starts with the front block alone: territory is earned at the planning table,
    /// not dealt. Deterministic in the inputs, which are deterministic in the seed.
    /// </summary>
    public static class TerritorySeeder
    {
        public const int RivalBlocks = 4;

        public readonly struct BlockPoint
        {
            public readonly int Id;
            public readonly float X;
            public readonly float Z;

            public BlockPoint(int id, float x, float z)
            {
                Id = id;
                X = x;
                Z = z;
            }
        }

        public readonly struct FrontPoint
        {
            public readonly int GangId;
            public readonly int BlockId;
            public readonly float X;
            public readonly float Z;

            public FrontPoint(int gangId, int blockId, float x, float z)
            {
                GangId = gangId;
                BlockId = blockId;
                X = x;
                Z = z;
            }
        }

        public static void Seed(TerritoryMap map, IReadOnlyList<BlockPoint> blocks,
            IReadOnlyList<FrontPoint> fronts, int playerGangId)
        {
            // Front blocks first, in list order - a front block is never contested
            // because GangFronts spreads the families across distinct blocks.
            foreach (var front in fronts)
                if (front.BlockId >= 0 && map.OwnerOf(front.BlockId) < 0)
                    map.Claim(front.BlockId, front.GangId);

            foreach (var front in fronts)
            {
                if (front.GangId == playerGangId)
                    continue;

                while (map.CountOf(front.GangId) < RivalBlocks)
                {
                    var best = -1;
                    var bestSqr = float.MaxValue;
                    foreach (var block in blocks)
                    {
                        if (map.OwnerOf(block.Id) >= 0)
                            continue;
                        var dx = block.X - front.X;
                        var dz = block.Z - front.Z;
                        var sqr = dx * dx + dz * dz;
                        if (sqr < bestSqr || (sqr == bestSqr && best >= 0 && block.Id < best))
                        {
                            bestSqr = sqr;
                            best = block.Id;
                        }
                    }

                    if (best < 0)
                        return; // city smaller than the families - claim what exists
                    map.Claim(best, front.GangId);
                }
            }
        }
    }
}
