using System.Collections.Generic;

namespace LivingCity.Gangs
{
    /// <summary>
    /// Which commercial door each gang operates behind. Pure arithmetic over candidates
    /// the caller has ALREADY filtered (cafes and restaurants only) and ALREADY sorted by
    /// PropertyDirector's world-position comparator - that ordering is the whole
    /// determinism of this pass, so the caller re-sorts rather than trusting registry
    /// order.
    ///
    /// The player picks first, off his seeded roll - NOT nearest-to-spawn, because the
    /// spawn point derives from the editor camera and would tie the front to whatever the
    /// user last looked at. The AI gangs then take turns at the remaining candidate
    /// farthest from everyone already seated (preferring untaken blocks), so the families
    /// spread across the city instead of clustering on one street. Zero rng draws here;
    /// ties resolve to the lowest candidate index.
    /// </summary>
    public static class GangFronts
    {
        public readonly struct FrontCandidate
        {
            public readonly int BlockId;
            public readonly float X;
            public readonly float Z;

            public FrontCandidate(int blockId, float x, float z)
            {
                BlockId = blockId;
                X = x;
                Z = z;
            }
        }

        /// <summary>Candidate index per gang id; -1 means that gang goes without (fewer
        /// qualifying buildings than gangs - the director logs and spawns nobody).</summary>
        public static int[] Select(
            IReadOnlyList<FrontCandidate> candidates, int playerFrontRoll, int gangCount)
        {
            var result = new int[gangCount];
            for (var i = 0; i < gangCount; i++)
                result[i] = -1;

            if (candidates == null || candidates.Count == 0 || gangCount == 0)
                return result;

            var chosen = new List<int>();
            result[0] = (playerFrontRoll & int.MaxValue) % candidates.Count;
            chosen.Add(result[0]);

            for (var gang = 1; gang < gangCount && chosen.Count < candidates.Count; gang++)
            {
                var pick = Farthest(candidates, chosen, distinctBlocks: true);
                if (pick < 0)
                    pick = Farthest(candidates, chosen, distinctBlocks: false);
                if (pick < 0)
                    break;

                result[gang] = pick;
                chosen.Add(pick);
            }

            return result;
        }

        /// <summary>Argmax over unchosen candidates of the min squared distance to every
        /// chosen one. A strict greater-than keeps ties on the lowest index. BlockId -1
        /// (a building the slabs could not place) never blocks on "same block".</summary>
        static int Farthest(
            IReadOnlyList<FrontCandidate> candidates, List<int> chosen, bool distinctBlocks)
        {
            var best = -1;
            var bestScore = -1f;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (chosen.Contains(i))
                    continue;
                if (distinctBlocks && SharesBlock(candidates, chosen, candidates[i].BlockId))
                    continue;

                var score = float.MaxValue;
                foreach (var seat in chosen)
                {
                    var dx = candidates[i].X - candidates[seat].X;
                    var dz = candidates[i].Z - candidates[seat].Z;
                    var sqr = dx * dx + dz * dz;
                    if (sqr < score)
                        score = sqr;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        static bool SharesBlock(
            IReadOnlyList<FrontCandidate> candidates, List<int> chosen, int blockId)
        {
            if (blockId < 0)
                return false;

            foreach (var seat in chosen)
                if (candidates[seat].BlockId == blockId)
                    return true;

            return false;
        }
    }
}
