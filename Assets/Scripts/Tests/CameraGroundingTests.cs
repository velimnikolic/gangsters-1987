using System;
using System.Collections.Generic;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>Camera height contracts that can also run outside Unity.</summary>
    public static class CameraGroundingTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            foreach (float pitch in new[] { 22f, 55f, 82f })
            {
                float threshold = CameraGrounding.BoomForHeight(180f, pitch);
                Near(failures, "180 m street/map boundary", 180f,
                    CameraGrounding.HeightForBoom(threshold, pitch));
                if (CameraGrounding.HeightForBoom(threshold * 0.91f, pitch) >= 180f ||
                    CameraGrounding.HeightForBoom(threshold * 1.09f, pitch) <= 180f)
                    failures.Add("one wheel step did not cross the ground-relative map boundary");
            }

            // Pan up to a summit and down to the city. Retaining the last pivot Y
            // would leave the camera hundreds of metres above the city on return.
            float focus = 0f;
            foreach (float ground in new[] { 0f, 120f, 350f, 90f, 0f })
            {
                focus = CameraGrounding.FocusHeight(focus, ground, false);
                Near(failures, "free pan follows local ground", ground, focus);
                Near(failures, "zoom stays 180 m above a plateau", ground + 180f,
                    CameraGrounding.LensHeight(focus, ground, 180f));
            }

            // The lens can orbit onto higher ground even while looking downhill.
            Near(failures, "uphill lens clearance", 530f,
                CameraGrounding.LensHeight(100f, 350f, 180f));
            Near(failures, "downhill focus clearance", 530f,
                CameraGrounding.LensHeight(350f, 100f, 180f));
            Near(failures, "close zoom above the mountain", 362f,
                CameraGrounding.LensHeight(0f, 350f, 12f));

            Near(failures, "ride keeps elevated bridge focus", 25f,
                CameraGrounding.FocusHeight(25f, 0f, true));
            Near(failures, "ride cannot pull focus underground", 350f,
                CameraGrounding.FocusHeight(25f, 350f, true));
            Near(failures, "dropping a ride restores local ground", 0f,
                CameraGrounding.FocusHeight(350f, 0f, false));
            Near(failures, "map return ignores stale low pivot", 350f,
                CameraGrounding.FocusHeight(0f, 350f, false));

            float settling = CameraGrounding.SettleHeight(350f, 0f, 1f / 60f);
            if (settling <= 0f || settling >= 350f)
                failures.Add("descent snapped or failed to approach local ground");
            Near(failures, "ascent never damps into a hill", 350f,
                CameraGrounding.SettleHeight(0f, 350f, 1f / 60f));
            Near(failures, "descent is frame-rate independent",
                CameraGrounding.SettleHeight(350f, 0f, 1f / 30f),
                CameraGrounding.SettleHeight(settling, 0f, 1f / 60f));

            // Preserve the zoom/map ratio while changing preferred pitch.
            float original = CameraGrounding.BoomForHeight(175f, 55f);
            foreach (float angle in new[] { 45f, 65f })
            {
                float tilted = CameraGrounding.BoomForHeight(
                    CameraGrounding.HeightForBoom(original, 55f), angle);
                Near(failures, "tilt preserves selected height", 175f,
                    CameraGrounding.HeightForBoom(tilted, angle));
                if (tilted > CameraGrounding.BoomForHeight(180f, angle))
                    failures.Add("tilting below the map boundary opened the map");
            }
            return failures;
        }

        static void Near(List<string> failures, string label, float expected, float actual)
        {
            if (Math.Abs(expected - actual) > 0.001f)
                failures.Add($"{label}: expected {expected}, got {actual}");
        }
    }
}
