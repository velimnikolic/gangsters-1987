using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Pure pair selection for the chat director, kept free of UnityEngine.Object so it can
    /// be exercised headlessly. Greedy nearest-neighbour in index order: deterministic for a
    /// given input, antisymmetric by construction (each index lands in at most one pair, and
    /// a pair is emitted exactly once).
    ///
    /// Candidates are bucketed into a one-shot spatial hash of maxDistance-sized cells, so
    /// each walker inspects its 3x3 neighbourhood instead of every other candidate - the
    /// selection rule is the exact one the old full scan used (nearest forward neighbour,
    /// equal distances resolved to the higher index), so the output is identical, but the
    /// cost is O(n x local density) instead of O(n^2) in one frame.
    /// </summary>
    public static class InteractionPairing
    {
        // Scratch state reused across calls - the director ticks on the main thread only,
        // and a 10k-candidate tick must not allocate a fresh grid every second.
        static readonly Dictionary<long, List<int>> Grid = new Dictionary<long, List<int>>();
        static bool[] used = new bool[256];

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

            var count = candidates.Count;
            var maxSq = maxDistance * maxDistance;

            if (used.Length < count)
                used = new bool[Mathf.NextPowerOfTwo(count)];
            System.Array.Clear(used, 0, count);

            foreach (var cell in Grid.Values)
                cell.Clear();

            for (var i = 0; i < count; i++)
            {
                var key = KeyFor(candidates[i], maxDistance);
                if (!Grid.TryGetValue(key, out var cell))
                    Grid[key] = cell = new List<int>();
                cell.Add(i);
            }

            for (var i = 0; i < count; i++)
            {
                if (used[i])
                    continue;

                var cx = Mathf.FloorToInt(candidates[i].x / maxDistance);
                var cz = Mathf.FloorToInt(candidates[i].z / maxDistance);

                var best = -1;
                var bestSq = float.PositiveInfinity;

                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!Grid.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                        continue;

                    for (var n = 0; n < cell.Count; n++)
                    {
                        var j = cell[n];
                        // Forward neighbours only, matching the old triangular scan - pair
                        // (i, j) is considered exactly once, from its lower index.
                        if (j <= i || used[j])
                            continue;

                        var delta = candidates[j] - candidates[i];
                        if (Mathf.Abs(delta.y) > 2f)
                            continue;

                        delta.y = 0f;
                        var distSq = delta.sqrMagnitude;
                        if (distSq > maxSq)
                            continue;

                        // The old scan replaced on <=, walking j upward, so among equal
                        // distances the HIGHEST index won. Preserve that exactly - cell
                        // visit order differs from index order here.
                        if (best < 0 || distSq < bestSq || (distSq == bestSq && j > best))
                        {
                            best = j;
                            bestSq = distSq;
                        }
                    }
                }

                if (best < 0)
                    continue;

                used[i] = true;
                used[best] = true;
                result.Add((i, best));
            }
        }

        static long KeyFor(Vector3 position, float cellSize) =>
            Key(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));

        static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    }
}
