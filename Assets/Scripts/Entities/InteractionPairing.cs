using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Pure pair selection for the chat director, kept free of UnityEngine.Object so it can
    /// be exercised headlessly. Greedy nearest-neighbour in index order: deterministic for a
    /// given input, antisymmetric by construction (each index lands in at most one pair, and
    /// a pair is emitted exactly once), and O(n^2) over a candidate list that is at most the
    /// pedestrian count.
    /// </summary>
    public static class InteractionPairing
    {
        /// <summary>
        /// Fills <paramref name="result"/> with index pairs of candidates standing within
        /// <paramref name="maxDistance"/> of each other, measured flat - a walker on a bridge
        /// is not in earshot of one underneath it, but nothing in this city overlaps in plan
        /// except the bridge, and the vertical check below handles that one.
        /// </summary>
        public static void Pairs(IReadOnlyList<Vector3> candidates, float maxDistance,
                                 List<(int a, int b)> result)
        {
            result.Clear();
            if (candidates == null || candidates.Count < 2)
                return;

            var used = new bool[candidates.Count];
            var maxSq = maxDistance * maxDistance;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (used[i])
                    continue;

                var best = -1;
                var bestSq = maxSq;

                for (var j = i + 1; j < candidates.Count; j++)
                {
                    if (used[j])
                        continue;

                    var delta = candidates[j] - candidates[i];
                    if (Mathf.Abs(delta.y) > 2f)
                        continue;

                    delta.y = 0f;
                    var distSq = delta.sqrMagnitude;
                    if (distSq > bestSq)
                        continue;

                    best = j;
                    bestSq = distSq;
                }

                if (best < 0)
                    continue;

                used[i] = true;
                used[best] = true;
                result.Add((i, best));
            }
        }
    }
}
