using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The geometry of a patrol car leaving the lane graph: the curve it follows off the
    /// street into a forecourt stall and back out to the kerb. Pure maths over structs - no
    /// UnityEngine.Object anywhere - so the headless suite can prove the properties that
    /// matter (the car monotonically approaches its target and never moves faster than the
    /// cap) without an Editor. PolicePatrolAgent owns the clock and the transform; this
    /// class only answers "where is t along the curve, and how far does one step advance it".
    ///
    /// A quadratic Bezier through a control point off the stall mouth is the whole model.
    /// With the endpoint rotations slerped alongside, the inbound trip reads as a car
    /// swinging off the road and backing into its bay (it ends nose-out, the way the bays
    /// are laid and the way the static bakes always sat) and the outbound as a plain pull
    /// away - and nothing more elaborate survives contact with a 45-degree orthographic
    /// camera anyway.
    /// </summary>
    public static class PoliceDocking
    {
        /// <summary>Speed cap through the manoeuvre, m/s. Walking pace, on purpose - the
        /// forecourt crosses the pavement band and pedestrians do not steer around cars.</summary>
        public const float Speed = 3f;

        /// <summary>How far out of the stall mouth the curve's control point sits. Around a
        /// stall depth: enough that the arc visibly swings through the mouth rather than
        /// cutting the corner over the kerbline.</summary>
        public const float MouthOffset = 5f;

        public struct Curve
        {
            public Vector3 A;
            public Vector3 Control;
            public Vector3 B;
        }

        /// <summary>Kerb to stall. <paramref name="stallOut"/> is the stall's nose-out
        /// direction - toward the street - so the mouth point sits street-side of the bay.</summary>
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
            var inverse = 1f - t;
            return inverse * inverse * curve.A
                 + 2f * inverse * t * curve.Control
                 + t * t * curve.B;
        }

        /// <summary>dPoint/dt - the (unnormalised) direction of travel at t.</summary>
        public static Vector3 Tangent(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            return 2f * (1f - t) * (curve.Control - curve.A)
                 + 2f * t * (curve.B - curve.Control);
        }

        /// <summary>
        /// The parameter after travelling <paramref name="ds"/> metres from t - the step that
        /// makes the CAP hold in metres rather than in parameter space, where a Bezier's pace
        /// varies along its own length.
        ///
        /// Solved on the CHORD by bisection rather than by a tangent-scaled Euler step. The
        /// Euler form overshot by 2x exactly where it matters most: a car starting nearly in
        /// the stall mouth rides a curve with a cusp, the tangent there shrinks toward zero
        /// and regrows within one step, and dividing the budget by the small entry tangent
        /// buys far more parameter than the budget's metres. The chord solve moves the car
        /// ds metres by construction (arc is never shorter than chord, so true speed can
        /// only be under the cap), and a remaining chord shorter than ds means the stall is
        /// within this step - snap to the end.
        /// </summary>
        public static float Advance(in Curve curve, float t, float ds)
        {
            var origin = Point(curve, t);
            if ((Point(curve, 1f) - origin).magnitude <= ds)
                return 1f;

            var low = t;
            var high = 1f;
            for (var i = 0; i < 24; i++)
            {
                var mid = (low + high) * 0.5f;
                if ((Point(curve, mid) - origin).magnitude < ds)
                    low = mid;
                else
                    high = mid;
            }

            return high;
        }
    }
}
