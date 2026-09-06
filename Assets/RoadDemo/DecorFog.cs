using LivingCity.Gameplay;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// DECOR STANDS STILL WHILE NOBODY SEES IT (the user's rule, 2026-09-07). The fog
    /// of war hides most of the island from the player, and the cranes, ships,
    /// forklifts, aircraft and ramp crews behind it were ticking all the same. A
    /// district whose ground is entirely in the fog does not move its decor; it picks
    /// up where it stopped when the fog lifts. Only decor: anything on the city's
    /// roads (trucks, kerb traffic, parking-lot cars) keeps driving, or it would
    /// stand in everybody's way; the crews, the law, the businesses are not asked.
    ///
    /// A district is judged once every half second over a grid of its ground no
    /// coarser than the narrowest thing the maps reveal (a 60 m radius, a 35 m
    /// street depth): a revealed patch anywhere on it makes the whole district move.
    /// A city with no fog sees everything move; a judged harness run sees exactly
    /// what the game does.
    /// </summary>
    static class DecorFog
    {
        const int JudgeEvery = 30;
        // half the smallest reveal, so no revealed patch falls between two samples
        const float Sample = 28f;
        sealed class Verdict { public int Frame; public bool Hidden; }
        // weak keys: a district of an unloaded city is not kept alive by its verdict
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDistrict, Verdict> _verdicts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<IDistrict, Verdict>();

        static bool Fogs() => MapVisionRegistry.FogOfWarEnabled && MapVisionRegistry.HasActiveSources;

        /// <summary>Nothing of this district's ground is in view.</summary>
        public static bool Hidden(IDistrict district)
        {
            if (district == null || !Fogs()) return false;
            int frame = Time.frameCount;
            var verdict = _verdicts.GetOrCreateValue(district);
            if (verdict.Frame > 0 && frame - verdict.Frame < JudgeEvery)
                return verdict.Hidden;
            var r = district.LocalBounds;
            var frameOf = district.Frame;
            int nx = Mathf.Max(2, Mathf.CeilToInt(r.width / Sample) + 1);
            int nz = Mathf.Max(2, Mathf.CeilToInt(r.height / Sample) + 1);
            bool hidden = true;
            for (int i = 0; i < nx && hidden; i++)
                for (int j = 0; j < nz && hidden; j++)
                {
                    var local = new Vector3(Mathf.Lerp(r.xMin, r.xMax, i / (float)(nx - 1)), 0f,
                                            Mathf.Lerp(r.yMin, r.yMax, j / (float)(nz - 1)));
                    if (MapVisionRegistry.IsVisible(frameOf.ToWorld(local))) hidden = false;
                }
            verdict.Frame = frame;
            verdict.Hidden = hidden;
            return hidden;
        }

        /// <summary>This spot is in the fog.</summary>
        public static bool Hidden(Vector3 at) => Fogs() && !MapVisionRegistry.IsVisible(at);
    }
}
