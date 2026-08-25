using UnityEngine;

namespace RoadDemo
{
    // The geometry of a patrol car leaving the lane graph: a cubic Bezier off the
    // stall mouth, driven at a fixed pace. Demo-local on purpose (the road demo
    // recycles no main-game code); the chord-bisection Advance keeps the cap in
    // metres where a Bezier's own parameter pace varies - a tangent-scaled Euler
    // step overshoots near the cusp.
    //
    // The two legs use the curve differently. INBOUND is the old single-control
    // arc, written as its exact cubic equivalent so the shape is unchanged, with
    // the endpoint rotations slerped across it: the car BACKS into its bay and
    // ends nose-out, so its heading is deliberately not its direction of travel.
    // OUTBOUND carries a second control point pinned upstream along the LANE.
    // Without it the curve ran stall -> mouth -> kerb, near enough a straight line
    // perpendicular to the street, while the yaw slerped through the ninety
    // degrees onto the lane - the car travelled one way, pointed another, and
    // crabbed sideways out of the forecourt. Ending along the lane makes the
    // tangent a heading worth steering by; see Heading.
    public static class PatrolDocking
    {
        /// <summary>Speed cap through the manoeuvre, m/s - walking pace, the
        /// forecourt crosses the pavement band.</summary>
        public const float Speed = 3f;

        /// <summary>How far out of the stall mouth the control point sits -
        /// enough that the arc visibly swings through the mouth.</summary>
        public const float MouthOffset = 5f;

        public struct Curve
        {
            public Vector3 A;
            public Vector3 ControlA;
            public Vector3 ControlB;
            public Vector3 B;
        }

        /// <summary>Kerb to stall; stallOut is the bay's nose-out direction. The
        /// same one-control arc it always was, degree-elevated: a quadratic
        /// through C is exactly the cubic with handles two thirds of the way to C
        /// from each end, so path, length and pacing are untouched.</summary>
        public static Curve Dock(Vector3 from, Vector3 stall, Vector3 stallOut)
        {
            var control = stall + stallOut * MouthOffset;
            return new Curve
            {
                A = from,
                ControlA = from + (control - from) * (2f / 3f),
                ControlB = stall + (control - stall) * (2f / 3f),
                B = stall,
            };
        }

        /// <summary>Stall to kerb: out of the bay along stallOut and onto the kerb
        /// along kerbForward, the lane's direction there - which is the direction
        /// the car has to be travelling when DemoVehicle takes the wheel back. Not
        /// the inbound arc driven backwards; see the class comment. Both handles
        /// clip to half the stall-to-kerb span, so a bay opening almost onto the
        /// lane gets a short swing rather than a loop past the kerb and back.</summary>
        public static Curve Undock(
            Vector3 stall, Vector3 stallOut, Vector3 kerb, Vector3 kerbForward)
            => Sweep(stall, stallOut, kerb, kerbForward);

        /// <summary>The two-control curve the undock is: away from A along
        /// <paramref name="fromWay"/> and into B along <paramref name="toWay"/>, so a
        /// car driving it forwards leaves and arrives pointing the way it is going.
        /// Both handles clip to half the span, so a short hop gets a short swing
        /// rather than a loop past the far end and back.
        ///
        /// It is the same curve a car turning OFF a road onto a forecourt wants, with
        /// the road's direction as fromWay and the bay's as toWay - which is why it is
        /// named for its shape rather than for the patrol car's errand.</summary>
        public static Curve Sweep(Vector3 from, Vector3 fromWay, Vector3 to, Vector3 toWay)
        {
            var span = Flat(to - from);
            float handle = Mathf.Min(MouthOffset, span.magnitude * 0.5f);

            return new Curve
            {
                A = from,
                ControlA = from + Flat(fromWay).normalized * handle,
                ControlB = to - Flat(toWay).normalized * handle,
                B = to,
            };
        }

        public static Vector3 Point(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            float inverse = 1f - t;
            return inverse * inverse * inverse * curve.A
                 + 3f * inverse * inverse * t * curve.ControlA
                 + 3f * inverse * t * t * curve.ControlB
                 + t * t * t * curve.B;
        }

        /// <summary>dPoint/dt - the (unnormalised) direction of travel at t.</summary>
        public static Vector3 Tangent(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            float inverse = 1f - t;
            return 3f * inverse * inverse * (curve.ControlA - curve.A)
                 + 6f * inverse * t * (curve.ControlB - curve.ControlA)
                 + 3f * t * t * (curve.B - curve.ControlB);
        }

        /// <summary>Which way a car DRIVING the curve forwards points at t: along
        /// the flattened tangent, since a car yaws and does not pitch. The
        /// fallback covers a degenerate curve, where there is no travel to read.</summary>
        public static Quaternion Heading(in Curve curve, float t, Quaternion fallback)
        {
            var forward = Flat(Tangent(curve, t));
            return forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : fallback;
        }

        /// <summary>The parameter after travelling ds metres from t, solved on
        /// the chord by bisection so true speed never exceeds the cap.</summary>
        public static float Advance(in Curve curve, float t, float ds)
        {
            var origin = Point(curve, t);
            if ((Point(curve, 1f) - origin).magnitude <= ds)
                return 1f;

            float low = t, high = 1f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (low + high) * 0.5f;
                if ((Point(curve, mid) - origin).magnitude < ds)
                    low = mid;
                else
                    high = mid;
            }

            return high;
        }

        /// <summary>Every manoeuvre here is on the ground plane; height rides along
        /// in the positions and has no business in the directions.</summary>
        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
