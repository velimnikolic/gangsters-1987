using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>
    /// Turf arithmetic over the outfit's holdings - one entry per gang-held BUILDING,
    /// because ground is taken premise by premise, never block by block. The ownership
    /// fact itself lives on BusinessMarker.GangId - the Personnel layer's single-source
    /// rule applied to ground - so there is nothing here to seed or store: callers
    /// collect the live holdings (OutfitDirector.CollectHoldings) and this class only
    /// counts them. Day one every family holds exactly its own front premise, the
    /// player included; more ground is earned building by building, and that mechanic
    /// belongs to a later iteration.
    /// </summary>
    public static class Turf
    {
        public readonly struct Holding
        {
            public readonly int GangId;
            public readonly int BlockId;

            public Holding(int gangId, int blockId)
            {
                GangId = gangId;
                BlockId = blockId;
            }
        }

        /// <summary>Buildings the family holds city-wide.</summary>
        public static int CountOf(IReadOnlyList<Holding> holdings, int gangId)
        {
            var count = 0;
            for (var i = 0; i < holdings.Count; i++)
                if (holdings[i].GangId == gangId)
                    count++;
            return count;
        }

        /// <summary>Buildings the family holds on one block.</summary>
        public static int CountIn(IReadOnlyList<Holding> holdings, int blockId, int gangId)
        {
            var count = 0;
            for (var i = 0; i < holdings.Count; i++)
                if (holdings[i].BlockId == blockId && holdings[i].GangId == gangId)
                    count++;
            return count;
        }

        /// <summary>The family holding the most buildings on the block; -1 when nobody
        /// holds one or the lead is shared - contested ground has no controller.</summary>
        public static int DominantIn(IReadOnlyList<Holding> holdings, int blockId)
        {
            var best = -1;
            var bestCount = 0;
            var tied = false;

            for (var i = 0; i < holdings.Count; i++)
            {
                if (holdings[i].BlockId != blockId || holdings[i].GangId < 0)
                    continue;
                if (holdings[i].GangId == best)
                    continue;

                var count = CountIn(holdings, blockId, holdings[i].GangId);
                if (count > bestCount)
                {
                    best = holdings[i].GangId;
                    bestCount = count;
                    tied = false;
                }
                else if (count == bestCount)
                {
                    tied = true;
                }
            }

            return tied ? -1 : best;
        }
    }
}
