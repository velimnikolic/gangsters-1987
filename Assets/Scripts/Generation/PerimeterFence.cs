using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// A closed boundary round a rectangle, with one gap in it and a pier either side of the gap.
    ///
    /// Four runs, each walked as a parameter from one corner to the next, with the opening
    /// subtracted from whichever side it faces - so the way in is a HOLE in the fence rather than
    /// a separate object placed on top of one. That distinction is the whole reason this is worth
    /// sharing: laying four full runs and then dropping a gate over the middle of one leaves the
    /// railing visible through the gateway, and it is not obvious from a screenshot until you look
    /// for it.
    ///
    /// Lived inside ParkingLotDresser until the works yard needed the identical thing at a
    /// different scale - a 9m lorry gate in a concrete wall against a 7m car entrance in railing.
    /// Everything that differs between them is an argument here; nothing is.
    ///
    /// Not to be confused with FenceRun, which lays ONE straight stretch and knows nothing about
    /// corners or gates. This calls that.
    /// </summary>
    public static class PerimeterFence
    {
        /// <summary>Half a pier, so a run stops short of the one on its corner.</summary>
        public const float PierHalf = 0.4f;

        /// <summary>
        /// Where the gap goes. <see cref="Outward"/> is the test that decides which of the four
        /// sides is opened - the same unit vector ParkingLayout and IndustrialLayout both publish
        /// as their GateOutward, so a caller passes its layout straight through.
        /// </summary>
        public struct Gate
        {
            public bool Has;
            public Vector3 Centre;
            public Vector3 Outward;
            public float Width;
        }

        /// <summary>
        /// Lays the boundary. <paramref name="min"/> and <paramref name="max"/> are the fence
        /// line itself, already inset by the caller - what that inset should be depends on what
        /// stands outside it, which is the caller's business and not this one's.
        /// </summary>
        public static void Build(
            Vector2 min,
            Vector2 max,
            Gate gate,
            GameObject segment,
            GameObject post,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!segment)
                return;

            if (max.x - min.x < 4f || max.y - min.y < 4f)
                return;

            var corners = new[]
            {
                new Vector3(min.x, 0f, min.y),
                new Vector3(max.x, 0f, min.y),
                new Vector3(max.x, 0f, max.y),
                new Vector3(min.x, 0f, max.y),
            };

            var outwards = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };

            for (var i = 0; i < 4; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % 4];
                var span = to - from;
                var length = span.magnitude;
                var along = span / length;

                var start = PierHalf;
                var end = length - PierHalf;

                // Does the opening go through this side? Its outward normal is the test - the
                // layout picked one side and only that one gets a hole in it.
                var gated = gate.Has && Vector3.Dot(outwards[i], gate.Outward) > 0.9f;

                if (!gated)
                {
                    FenceRun.Lay(segment, from, along, start, end, parent, spawn, placed);
                    continue;
                }

                var centre = Vector3.Dot(gate.Centre - from, along);
                var gapFrom = centre - gate.Width * 0.5f;
                var gapTo = centre + gate.Width * 0.5f;

                FenceRun.Lay(segment, from, along, start, gapFrom - PierHalf, parent, spawn, placed);
                FenceRun.Lay(segment, from, along, gapTo + PierHalf, end, parent, spawn, placed);

                Pier(post, from + along * gapFrom, parent, spawn, placed);
                Pier(post, from + along * gapTo, parent, spawn, placed);
            }

            foreach (var corner in corners)
                Pier(post, corner, parent, spawn, placed);
        }

        static void Pier(
            GameObject prefab, Vector3 position, Transform parent, SpawnPrefab spawn, List<GameObject> placed)
        {
            if (!prefab)
                return;

            placed.Add(spawn(prefab, position, Quaternion.identity, parent));
        }
    }
}
