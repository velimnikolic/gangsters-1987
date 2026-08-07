using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The walking-pace separation maths, kept free of every UnityEngine.Object so it can be
    /// compiled and exercised headlessly alongside <see cref="CarFollowing"/>. Nothing here
    /// logs, allocates or touches the scene.
    ///
    /// People are circles, not boxes: a pedestrian is about as wide as it is deep, so the
    /// oriented-box machinery the cars need collapses to plain distances. Two mechanisms and
    /// only two:
    ///
    /// - a SEPARATION push that bends the walking direction away from anyone inside personal
    ///   space, plus a rightward bias against oncoming walkers so a meeting pair drifts apart
    ///   instead of mirroring each other's dodges forever;
    /// - a hard ADVANCE CLAMP that bounds how far the frame may move toward a body in the way,
    ///   so however the steering is tuned, two people never occupy the same ground.
    ///
    /// The clamp deliberately ignores bodies that already overlap - it cannot un-close a
    /// closed gap, and including them would freeze the pair at the overlap forever (the exact
    /// lesson TrafficRegistry.Probe learned). The separation push is what untangles them.
    /// </summary>
    public static class PedestrianSteering
    {
        /// <summary>Body radius when a prefab has no capsule to measure. Pack capsules are 0.32.</summary>
        public const float DefaultRadius = 0.3f;

        /// <summary>
        /// How strongly the separation push bends the walking direction, relative to the unit
        /// goal vector. Above ~2 a crowded pavement dissolves into milling; below ~1 people
        /// lean on the hard clamp instead of walking around each other.
        /// </summary>
        public const float SeparationWeight = 1.6f;

        /// <summary>Heading dot below which a neighbour counts as coming the other way.</summary>
        public const float OncomingDot = -0.5f;

        /// <summary>
        /// Sideways nudge added against an oncoming walker, as a fraction of the goal vector.
        /// Both members of a meeting pair compute it in their OWN frame, so both step to their
        /// own right and the pair passes shoulder to shoulder - the same trick as real
        /// pavements. Without it the separation push is symmetric along the meeting line and
        /// the two dodge into each other's dodges.
        /// </summary>
        public const float RightBiasWeight = 0.6f;

        /// <summary>Extra lateral slack on the advance corridor beyond the bodies' radii.</summary>
        public const float CorridorPad = 0.15f;

        /// <summary>Below this a pedestrian counts as standing for the stall valve, m/s.</summary>
        public const float StalledSpeed = 0.15f;

        /// <summary>Seconds of being held still before the clearance starts to decay.</summary>
        public const float StallStartsAfter = 3f;

        /// <summary>Stalled seconds at which the decay reaches <see cref="ClearanceFloor"/>.</summary>
        public const float StallFullAfter = 8f;

        /// <summary>
        /// The clearance the clamp never surrenders, however long the jam. Not zero: bodies
        /// stay visibly apart even while squeezing past a blocked doorway.
        /// </summary>
        public const float ClearanceFloor = 0.05f;

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Horizontal right of a horizontal forward: cross(up, forward).</summary>
        public static Vector3 RightOf(Vector3 flatForward) =>
            new Vector3(flatForward.z, 0f, -flatForward.x);

        /// <summary>
        /// Push away from one neighbour, zero outside <paramref name="personalSpace"/>, rising
        /// linearly to a full unit vector as the bodies touch. Exactly coincident bodies give
        /// no direction to push in and return zero - the registry adds a per-id nudge for that
        /// case so a coincident pair still separates deterministically.
        /// </summary>
        public static Vector3 SeparationPush(Vector3 selfPos, Vector3 otherPos, float personalSpace)
        {
            var away = Flat(selfPos - otherPos);
            var dist = away.magnitude;

            if (dist >= personalSpace || dist < 1e-4f)
                return Vector3.zero;

            return away * ((1f - dist / personalSpace) / dist);
        }

        /// <summary>
        /// The rightward nudge against an oncoming neighbour, zero for everyone else. Only
        /// neighbours AHEAD and within <paramref name="range"/> count - someone walking away
        /// behind us is not being met, and someone approaching from across the square is not
        /// being met YET; without the range gate every walker on a long straight pavement
        /// drifts kerbward the whole way.
        /// </summary>
        public static Vector3 OncomingBias(Vector3 desiredDir, Vector3 toOther, Vector3 otherForward,
                                           float range)
        {
            var d = Flat(desiredDir);
            if (d.sqrMagnitude < 1e-6f)
                return Vector3.zero;
            d = d.normalized;

            var offset = Flat(toOther);
            if (offset.magnitude >= range || Vector3.Dot(offset, d) <= 0f)
                return Vector3.zero;

            var facing = Flat(otherForward);
            if (facing.sqrMagnitude < 1e-6f || Vector3.Dot(facing.normalized, d) > OncomingDot)
                return Vector3.zero;

            return RightOf(d) * RightBiasWeight;
        }

        /// <summary>
        /// How far the frame may move toward one neighbour: infinite when the neighbour is
        /// behind, clear to the side, or already overlapping (see the class doc); otherwise the
        /// surface-to-surface gap minus the clearance owed, floored at zero - a gap inside the
        /// clearance forbids advance, it does not command retreat.
        /// </summary>
        public static float AdvanceCap(Vector3 selfPos, Vector3 desiredDir, Vector3 otherPos,
                                       float radiusSum, float clearance)
        {
            var d = Flat(desiredDir);
            if (d.sqrMagnitude < 1e-6f)
                return float.PositiveInfinity;
            d = d.normalized;

            var delta = Flat(otherPos - selfPos);

            if (Vector3.Dot(delta, d) <= 0f)
                return float.PositiveInfinity;

            if (Mathf.Abs(Vector3.Dot(delta, RightOf(d))) >= radiusSum + CorridorPad)
                return float.PositiveInfinity;

            var dist = delta.magnitude;
            if (dist < radiusSum)
                return float.PositiveInfinity;

            return Mathf.Max(0f, dist - radiusSum - clearance);
        }

        /// <summary>
        /// The clearance owed to a blocker, given how long this walker has been held still.
        /// Decays so a blocked pavement leaks instead of deadlocking: people squeeze past a
        /// standing crowd the way cars rotate out of a ring - see CarFollowing.ClearanceFor.
        /// </summary>
        public static float EffectiveClearance(float minSeparation, float stalledFor)
        {
            var t = Mathf.InverseLerp(StallStartsAfter, StallFullAfter, stalledFor);
            return Mathf.Lerp(minSeparation, ClearanceFloor, t);
        }

        /// <summary>
        /// The direction actually walked: the unit goal vector plus the weighted push,
        /// renormalised. A push that exactly cancels the goal returns zero - stand still this
        /// frame rather than flip to a random heading.
        /// </summary>
        public static Vector3 Blend(Vector3 desiredDir, Vector3 push)
        {
            var d = Flat(desiredDir);
            if (d.sqrMagnitude < 1e-6f)
                return Vector3.zero;

            var blended = d.normalized + push * SeparationWeight;
            return blended.sqrMagnitude < 1e-4f ? Vector3.zero : blended.normalized;
        }
    }
}
