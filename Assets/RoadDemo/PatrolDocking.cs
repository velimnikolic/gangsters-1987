using UnityEngine;

namespace RoadDemo
{
    // The geometry of a patrol car leaving the lane graph: a quadratic Bezier
    // through a control point off the stall mouth, driven at a fixed pace. Demo-
    // local on purpose (the road demo recycles no main-game code); the chord-
    // bisection Advance keeps the cap in metres where a Bezier's own parameter
    // pace varies - a tangent-scaled Euler step overshoots near the cusp.
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
            public Vector3 Control;
            public Vector3 B;
        }

        /// <summary>Kerb to stall; stallOut is the bay's nose-out direction.</summary>
        public static Curve Dock(Vector3 from, Vector3 stall, Vector3 stallOut) => new Curve
        {
            A = from,
            Control = stall + stallOut * MouthOffset,
            B = stall,
        };

        /// <summary>Stall to kerb - the same arc driven the other way.</summary>
        public static Curve Undock(Vector3 stall, Vector3 stallOut, Vector3 kerb) => new Curve
        {
            A = stall,
            Control = stall + stallOut * MouthOffset,
            B = kerb,
        };

        public static Vector3 Point(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            float inverse = 1f - t;
            return inverse * inverse * curve.A
                 + 2f * inverse * t * curve.Control
                 + t * t * curve.B;
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
    }
}
