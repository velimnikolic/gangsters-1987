using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The decisions of the school run that are pure arithmetic: when a run is due, which lane
    /// a bus may stop in, where a queue of children stands, and what order the stops are served
    /// in. No UnityEngine.Object anywhere - the same discipline TrafficGeometry is written under
    /// and for the same reason: the headless suite can prove these without an Editor, and every
    /// one of them is a rule that is either right or wrong rather than a matter of taste.
    ///
    /// SchoolBusDirector owns the scene queries, SchoolBusAgent owns the clock and the
    /// transform; this class only answers questions.
    /// </summary>
    public static class SchoolRun
    {
        /// <summary>
        /// Metres between two pickup stops. Below this the bus spends the run braking and the
        /// second stop's queue is inside the first stop's, which reads as one long stop rather
        /// than a route.
        /// </summary>
        public const float MinStopSpacing = 45f;

        /// <summary>Metres between two children in a queue. Comfortably over twice
        /// PedestrianSteering's body radius, so PedestrianRegistry.IsClear is satisfiable at
        /// every slot rather than only at the ends.</summary>
        public const float QueueSpacing = 0.9f;

        /// <summary>
        /// Seconds between one child starting for the door and the next. NOT a one-at-a-time
        /// semaphore: eight children single-filing through one door is fifteen seconds of a bus
        /// standing in a live lane, and a lane held that long next to a junction is a citywide
        /// jam. Staggered starts plus the ordinary avoidance clamp clear the same eight in about
        /// four seconds, which is the same way a crowded doorstep already resolves itself.
        /// </summary>
        public const float BoardingStagger = 0.4f;

        /// <summary>
        /// Is <paramref name="hour"/> inside the window that opens at <paramref name="startHour"/>?
        ///
        /// The window is start + LENGTH rather than start + end, and that is the whole reason
        /// this is a function instead of two comparisons: with an end hour, midnight wrap needs
        /// a special case, and a window whose end equals its start is ambiguous between empty
        /// and the whole day. With a length, wrap is what Mathf.Repeat already does - the same
        /// idiom CityWeather uses for the night ramp - a non-positive length is trivially empty,
        /// and a length of 24 is trivially always.
        /// </summary>
        public static bool InWindow(float hour, float startHour, float lengthHours)
        {
            if (lengthHours <= 0f)
                return false;
            if (lengthHours >= Ambient.CityClock.HoursPerDay)
                return true;

            return Mathf.Repeat(hour - startHour, Ambient.CityClock.HoursPerDay) < lengthHours;
        }

        /// <summary>
        /// The i-th place in a queue running back from <paramref name="head"/> along
        /// <paramref name="back"/> - children waiting at a kerb, strung out along the pavement
        /// behind the door rather than bunched at it.
        /// </summary>
        public static Vector3 QueueSlot(int index, Vector3 head, Vector3 back) =>
            head + back * (QueueSpacing * index);

        /// <summary>
        /// The i-th place in a line of <paramref name="count"/> spread symmetrically about
        /// <paramref name="centre"/> - a class fanned out along the school's facade, so that
        /// arriving at the door is a queue rather than a scrum.
        /// </summary>
        public static Vector3 FanSlot(int index, int count, Vector3 centre, Vector3 tangent) =>
            centre + tangent * ((index - (count - 1) * 0.5f) * QueueSpacing);

        /// <summary>
        /// May a bus stop in this lane and let children out onto the pavement beside it?
        ///
        /// Both arguments are the lane's offset across the tile in the TILE's local X, which is
        /// how the pack lays every road: a minor street carries lanes at +/-1.5, a boulevard at
        /// +/-1.75 and +/-4.75. Only the OUTERMOST lane on a side touches that side's pavement -
        /// stopping in a boulevard's inner lane puts the door on the far side of a live outer
        /// lane, and every child crosses it.
        ///
        /// The test is "same side, and as far out as the outermost lane on that side", which
        /// needs no table of road types and keeps working for any tile the pack adds later.
        /// </summary>
        public static bool IsBoardableLane(float laneLocalX, float outermostLaneLocalX)
        {
            if (Mathf.Abs(outermostLaneLocalX) < 1e-3f)
                return false;
            if (Mathf.Sign(laneLocalX) != Mathf.Sign(outermostLaneLocalX))
                return false;

            return Mathf.Abs(laneLocalX) >= Mathf.Abs(outermostLaneLocalX) - 0.25f;
        }

        /// <summary>
        /// The order the stops are served in: outward from the school in the morning, and the
        /// exact reverse in the afternoon, so the child picked up first is dropped off last and
        /// the route reads as one round trip rather than two unrelated drives.
        /// </summary>
        public static int[] StopOrder(int count, bool outbound)
        {
            var order = new int[Mathf.Max(0, count)];
            for (var i = 0; i < order.Length; i++)
                order[i] = outbound ? i : order.Length - 1 - i;

            return order;
        }
    }
}
