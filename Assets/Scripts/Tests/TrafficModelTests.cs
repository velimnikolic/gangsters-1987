using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of the traffic model that a car crashing into another car, or a whole city
    /// freezing, would violate.
    ///
    /// It is a plain static class with no UnityEngine.Object in it, which is the same discipline
    /// <see cref="Entities.TrafficGeometry"/> and <see cref="Entities.CarFollowing"/> are written
    /// under and the reason this can run at all: load the built Assembly-CSharp.dll into a bare
    /// .NET host, call <see cref="Run"/> by reflection, read the returned list. No Editor, no
    /// domain reload, no Play mode. See the offline-Roslyn notes.
    ///
    /// Nothing here logs. UnityEngine.Debug.Log is an internal call and throws SecurityException
    /// outside the Unity runtime, so a failure has to come back as data.
    ///
    /// Every assertion below corresponds to a defect that actually shipped. They are written
    /// against the properties rather than the constants, so retuning StandstillGap or CreepSpeed
    /// does not require editing the test - only breaking the property does.
    /// </summary>
    public static class TrafficModelTests
    {
        const float Dt = 1f / 60f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            PriorityIsReachableAtRest(failures);
            NoPermanentTrap(failures);
            QueueStillSettlesAtTheComfortableGap(failures);
            CreepIsNotAFreePass(failures);
            PerpendicularCarIsNotAnOverlap(failures);
            LongCrossingVehicleStaysVisible(failures);
            FollowerBehindStaysInvisible(failures);
            OncomingCarIsNotAnObstacle(failures);

            return failures;
        }

        /// <summary>
        /// A stopped car with right of way has to be able to exercise it.
        ///
        /// TrafficRegistry.Probe exempts a blocker it has priority over only while
        /// gap > StoppingDistance(speed). Flooring StoppingDistance at StandstillGap made that
        /// 1.5m at rest, so the exemption was unobtainable for a stationary car - which is every
        /// car the stall breaker is trying to release, since the stall breaker's whole mechanism
        /// is to hand out priority. The valve opened onto a wall.
        /// </summary>
        static void PriorityIsReachableAtRest(List<string> failures)
        {
            var atRest = Entities.CarFollowing.StoppingDistance(0f);

            if (atRest > Entities.CarFollowing.MinClearance + 1e-4f)
                failures.Add($"StoppingDistance(0) = {atRest:0.###}, expected the clamp's floor "
                           + $"{Entities.CarFollowing.MinClearance:0.###} - a stopped car cannot claim right of way inside it");

            if (atRest >= Entities.CarFollowing.StandstillGap)
                failures.Add($"StoppingDistance(0) = {atRest:0.###} is not below StandstillGap "
                           + $"{Entities.CarFollowing.StandstillGap:0.###}; priority stays unobtainable at rest");

            // The quadratic term must survive: at speed this still has to be a real braking distance.
            if (Entities.CarFollowing.StoppingDistance(20f) <= atRest)
                failures.Add("StoppingDistance is not increasing with speed");
        }

        /// <summary>
        /// THE freeze. A car stopped anywhere inside the standstill gap must be able to move again.
        ///
        /// IDM at rest wants StandstillGap, so for any real gap below it the acceleration is
        /// strictly negative and Max(0, ...) pins the speed at zero. Nothing in the simulation ever
        /// pushes cars apart, so that state was permanent - and the clamp deliberately parks
        /// stopped cars at MinClearance, i.e. inside the trap. Every stop under 1.5m was for good.
        ///
        /// Swept from an overlap (negative gap) outwards, because the crashed-pair case is the one
        /// where IDM's interaction term saturates at its hard cap and the model is most certain
        /// the car should not move.
        /// </summary>
        static void NoPermanentTrap(List<string> failures)
        {
            for (var gap = -0.5f; gap <= 3.0f; gap += 0.05f)
            {
                var speed = 0f;
                var moved = 0f;

                // One second of frames is generous; the release lasts longer than that.
                for (var frame = 0; frame < 60; frame++)
                {
                    speed = Entities.CarFollowing.NextSpeed(speed, 13.9f, gap, 0f,
                        Entities.CarFollowing.DefaultHeadway, true, Dt);
                    moved += speed * Dt;
                }

                if (moved <= 0f)
                {
                    failures.Add($"stalled car at gap {gap:0.##} never moved in a second - permanent deadlock");
                    return;
                }
            }
        }

        /// <summary>
        /// The creep must not become the normal following distance.
        ///
        /// Gating it on the stall release is what keeps ordinary traffic behaving like traffic. If
        /// it applied unconditionally, a queue at a red light would compact until the clamp stopped
        /// it and every car in the city would sit at MinClearance, touching bumpers.
        /// </summary>
        static void QueueStillSettlesAtTheComfortableGap(List<string> failures)
        {
            // Approach a stopped leader from 30m at 50km/h, no release, and see where it settles.
            var gap = 30f;
            var speed = 13.9f;

            for (var frame = 0; frame < 60 * 30; frame++)
            {
                speed = Entities.CarFollowing.NextSpeed(speed, 13.9f, gap, 0f,
                    Entities.CarFollowing.DefaultHeadway, false, Dt);
                gap -= speed * Dt;

                if (gap < Entities.CarFollowing.MinClearance)
                {
                    failures.Add($"follower closed to {gap:0.###}m on a stopped leader - inside the clamp's floor, "
                               + "so the speed model is relying on the clamp to do its braking");
                    return;
                }
            }

            if (Mathf.Abs(gap - Entities.CarFollowing.StandstillGap) > 0.25f)
                failures.Add($"queue settled at {gap:0.###}m, expected about "
                           + $"{Entities.CarFollowing.StandstillGap:0.###}m");
        }

        /// <summary>
        /// Creeping is an escape, not an override. A car with a licence to creep still must not
        /// accelerate away as though the road were clear, or the release would turn every jam into
        /// a car driving off into whatever stopped it.
        /// </summary>
        static void CreepIsNotAFreePass(List<string> failures)
        {
            var speed = 0f;
            for (var frame = 0; frame < 60 * 5; frame++)
            {
                speed = Entities.CarFollowing.NextSpeed(speed, 13.9f, 0.8f, 0f,
                    Entities.CarFollowing.DefaultHeadway, true, Dt);
            }

            if (speed > Entities.CarFollowing.CreepSpeed + 1e-3f)
                failures.Add($"creeping car reached {speed:0.###} m/s against a blocker 0.8m ahead; "
                           + $"the floor is {Entities.CarFollowing.CreepSpeed:0.###} and IDM should hold it there");
        }

        /// <summary>
        /// THE crash. A car crossing the junction ahead of us is not an overlap.
        ///
        /// Overlapping used to be inferred from a negative gap, which is a projection onto ONE axis.
        /// This arrangement - a car 2.5m ahead and 3.5m to the side, crossing at right angles -
        /// measures a negative gap while the bodies are comfortably apart, so it set the flag; and
        /// the caller read the flag as "the anti-overlap clamp does not apply this frame", against
        /// every other car too. One car crossing a junction let this car drive through the car it
        /// was following.
        /// </summary>
        static void PerpendicularCarIsNotAnOverlap(List<string> failures)
        {
            var self = new Entities.TrafficBox(Vector3.zero, Vector3.forward, 2.25f, 1.05f);
            var crossing = new Entities.TrafficBox(new Vector3(3.5f, 0f, 2.5f), Vector3.right, 2.25f, 1.05f);

            if (Entities.TrafficGeometry.Overlaps(self, crossing))
            {
                failures.Add("SAT reports an overlap for a car 2.5m ahead and 3.5m to the side - "
                           + "it is clear by 0.2m on the lateral axis");
                return;
            }

            // The point of the test: the old inference would have fired here. If the gap is no
            // longer negative the arrangement has stopped exercising the defect and the test is
            // silently passing for the wrong reason.
            Entities.TrafficGeometry.TryMeasure(self, crossing, 40f, out var gap, out _, out _);
            if (gap >= 0f)
                failures.Add($"regression fixture is stale: gap is {gap:0.###}, so this no longer "
                           + "reproduces the negative-gap-means-overlap inference it exists to guard");
        }

        /// <summary>
        /// A bus is 11.3m long. Halfway across a junction its centre is behind ours while its back
        /// half is still lying across our nose, and the probe rejected anything whose centre was
        /// behind us - so it vanished at exactly the moment it was in the way.
        /// </summary>
        static void LongCrossingVehicleStaysVisible(List<string> failures)
        {
            var self = new Entities.TrafficBox(Vector3.zero, Vector3.forward, 2.25f, 1.05f);
            var bus = new Entities.TrafficBox(new Vector3(0f, 0f, -0.5f), Vector3.right, 5.65f, 1.43f);

            if (!Entities.TrafficGeometry.TryMeasure(self, bus, 40f, out _, out _, out _))
                failures.Add("an 11.3m bus lying across our nose, centre 0.5m behind ours, is invisible to the probe");
        }

        /// <summary>
        /// The half of that relaxation which must NOT happen.
        ///
        /// The fixture is a same-direction car teleported INTO us from behind - centres 2m apart,
        /// so it is deeply interpenetrated - and not a car merely following, because a following
        /// car cannot exercise the risk: two 4.5m cars are half-lengths apart before their bodies
        /// touch, so the follower's rear edge stays behind us under either version of the guard
        /// and the test would pass for the wrong reason. Interpenetration is the only arrangement
        /// that puts a same-direction body's far edge in front of our centre, and it is a real
        /// one - the re-path teleport creates it.
        ///
        /// Reporting it would return a gap near -6.5m: not a distance to anything, describing a
        /// car we cannot drive into, handed to a model that reads negative gaps as an emergency.
        /// </summary>
        static void FollowerBehindStaysInvisible(List<string> failures)
        {
            var self = new Entities.TrafficBox(Vector3.zero, Vector3.forward, 2.25f, 1.05f);
            var behind = new Entities.TrafficBox(new Vector3(0f, 0f, -2f), Vector3.forward, 2.25f, 1.05f);

            if (!Entities.TrafficGeometry.Overlaps(self, behind))
                failures.Add("regression fixture is stale: the rear car is supposed to be interpenetrating");

            if (Entities.TrafficGeometry.TryMeasure(self, behind, 40f, out var gap, out _, out _))
                failures.Add($"a same-direction car BEHIND us is being measured as an obstacle ahead (gap {gap:0.###})");
        }

        /// <summary>
        /// Traffic in the other carriageway is not an obstacle. Getting this wrong is what jammed
        /// the city the first time: approach speeds add up, so IDM read every oncoming car as an
        /// emergency. Heading is what separates the carriageways - 3m apart with a 2.85m bus in
        /// each is not something a width test can resolve.
        /// </summary>
        static void OncomingCarIsNotAnObstacle(List<string> failures)
        {
            var self = new Entities.TrafficBox(Vector3.zero, Vector3.forward, 2.25f, 1.05f);
            var oncoming = new Entities.TrafficBox(new Vector3(3f, 0f, 10f), Vector3.back, 2.25f, 1.05f);

            Entities.TrafficGeometry.TryMeasure(self, oncoming, 40f, out _, out _, out var facing);

            if (facing >= Entities.TrafficGeometry.OncomingDot)
                failures.Add($"a car in the opposite carriageway reads facing {facing:0.###}, "
                           + $"which is not below OncomingDot {Entities.TrafficGeometry.OncomingDot:0.###} - "
                           + "Probe would treat it as something to brake for");

            if (Entities.TrafficGeometry.Overlaps(self, oncoming))
                failures.Add("cars 3m apart in opposite carriageways report as overlapping");
        }
    }
}
