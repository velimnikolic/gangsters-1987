using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The geometry of a patrol car leaving the lane graph: the curve it follows off the
    /// street into a forecourt stall and back out to the kerb. Pure maths over structs - no
    /// UnityEngine.Object anywhere - so the headless suite can prove the properties that
    /// matter (the car monotonically approaches its target and never moves faster than the
    /// cap) without an Editor. The CALLER owns the clock and the transform; this class only
    /// answers "where is t along the curve, how far does one step advance it, and which way
    /// is the car pointing there". Named for the patrol car it was written for, and kept
    /// under that name after the generator's police went (GAN-226, ROSTER-005) because the
    /// forecourt visitors and the school bus drive the same curve.
    ///
    /// A cubic Bezier is the whole model, and the two legs use it differently.
    ///
    /// INBOUND is the old quadratic through a single control point off the stall mouth,
    /// written as its exact cubic equivalent so the shape (and the suite's numbers) are
    /// unchanged. With the endpoint rotations slerped alongside it reads as a car swinging
    /// off the road and BACKING into its bay - it ends nose-out, the way the bays are laid
    /// and the way the static bakes always sat - so the heading deliberately does not follow
    /// the direction of travel there.
    ///
    /// OUTBOUND has to earn its heading, and that is why it carries a second control point.
    /// A single-control curve out of a bay ran stall -> mouth -> kerb, which is very nearly a
    /// straight line perpendicular to the street, while the car's yaw slerped through the
    /// ninety degrees onto the lane: the car travelled one way and pointed another, and it
    /// crabbed sideways out of the forecourt. Pinning the second control point upstream along
    /// the LANE makes the curve arrive at the kerb already travelling down it, so the tangent
    /// is a heading worth steering by - out of the bay, swing, straighten onto the lane - and
    /// <see cref="Heading"/> does exactly that.
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

        /// <summary>
        /// The school bus's mouth offset. A 9.77m vehicle swung through a 5m mouth pivots about
        /// a point inside its own body and reads as a bus rotating on the spot; the arc has to
        /// be long enough that the far end of it moves too. Scaled with the vehicle, near enough:
        /// the cars this was measured for are about half the length.
        /// </summary>
        public const float BusMouthOffset = 9f;

        public struct Curve
        {
            public Vector3 A;
            public Vector3 ControlA;
            public Vector3 ControlB;
            public Vector3 B;
        }

        /// <summary>Kerb to stall. <paramref name="stallOut"/> is the stall's nose-out
        /// direction - toward the street - so the mouth point sits street-side of the bay.
        /// <paramref name="mouth"/> defaults to the car figure; see BusMouthOffset.
        ///
        /// Still the one-control curve it always was, degree-elevated: a quadratic through C is
        /// the cubic with handles two thirds of the way to C from each end, exactly, so the
        /// path, its length and its pacing are untouched by the second control point's
        /// arrival.</summary>
        public static Curve Dock(
            Vector3 from, Vector3 stall, Vector3 stallOut, float mouth = MouthOffset)
        {
            var control = stall + stallOut * mouth;
            return new Curve
            {
                A = from,
                ControlA = from + (control - from) * (2f / 3f),
                ControlB = stall + (control - stall) * (2f / 3f),
                B = stall,
            };
        }

        /// <summary>
        /// Stall to kerb. Leaves the bay along <paramref name="stallOut"/> and arrives at the
        /// kerb along <paramref name="kerbForward"/> - the direction of the lane there, which is
        /// the direction the car has to be travelling by the time CarBehavior takes the wheel
        /// back. Not the inbound arc driven backwards: see the class comment for why a car
        /// pulling out needs a curve that ends pointing down the road.
        ///
        /// Both handles are clipped to half the stall-to-kerb span, so a bay that opens almost
        /// onto the lane gets a short swing rather than a loop out past the kerb and back.
        /// </summary>
        public static Curve Undock(
            Vector3 stall, Vector3 stallOut, Vector3 kerb, Vector3 kerbForward,
            float mouth = MouthOffset)
        {
            var span = Flat(kerb - stall);
            var handle = Mathf.Min(mouth, span.magnitude * 0.5f);

            return new Curve
            {
                A = stall,
                ControlA = stall + Flat(stallOut).normalized * handle,
                ControlB = kerb - Flat(kerbForward).normalized * handle,
                B = kerb,
            };
        }

        public static Vector3 Point(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            var inverse = 1f - t;
            return inverse * inverse * inverse * curve.A
                 + 3f * inverse * inverse * t * curve.ControlA
                 + 3f * inverse * t * t * curve.ControlB
                 + t * t * t * curve.B;
        }

        /// <summary>dPoint/dt - the (unnormalised) direction of travel at t.</summary>
        public static Vector3 Tangent(in Curve curve, float t)
        {
            t = Mathf.Clamp01(t);
            var inverse = 1f - t;
            return 3f * inverse * inverse * (curve.ControlA - curve.A)
                 + 6f * inverse * t * (curve.ControlB - curve.ControlA)
                 + 3f * t * t * (curve.B - curve.ControlB);
        }

        /// <summary>
        /// Which way a car DRIVING the curve forwards points at t: along the tangent, flattened,
        /// because a car yaws and does not pitch. <paramref name="fallback"/> covers the
        /// degenerate parameters - a curve whose points all coincide, or the cusp an inbound
        /// arc can carry - where there is no direction of travel to read.
        /// </summary>
        public static Quaternion Heading(in Curve curve, float t, Quaternion fallback)
        {
            var forward = Flat(Tangent(curve, t));
            return forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : fallback;
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

        /// <summary>Everything here is a ground-plane manoeuvre; height comes along for the
        /// ride in the positions and has no business in the directions.</summary>
        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
