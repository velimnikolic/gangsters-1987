using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>What every generator draws from its seed the same way.</summary>
    public static class Dice
    {
        /// <summary>
        /// Fisher-Yates from the end down, one <c>rng.Next(i + 1)</c> a step. Every
        /// generator that shuffles does it exactly so: the tally of thirty seeds is judged
        /// on the draw order, and a shuffle that drew differently would deal another block
        /// for the same seed.
        /// </summary>
        public static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
