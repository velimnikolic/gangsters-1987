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
            RingMemberEscapesTheClampEventually(failures);
            QueueNeverLosesItsClearance(failures);
            EscapeFloorIsBoundedAndMonotone(failures);
            StoppedCarStillSeesTheJunction(failures);
            ExitProbeGeometry(failures);

            return failures;
        }

        /// <summary>Below this the escape bookkeeping counts the car as motionless, m/s. Mirrors
        /// TrafficRegistry.StalledSpeed, which is rightly private - the tests model the loop, not
        /// reach into it.</summary>
        const float StalledSpeed = 0.2f;

        /// <summary>
        /// One frame of the REAL control loop: the speed model, then the movement clamp, then the
        /// stuck bookkeeping - the combination CarBehavior.Update and TrafficRegistry run between
        /// them. NoPermanentTrap above simulates the model plus creep and passes; the freeze it
        /// missed lived one line further down, where the clamp zeroes whatever the creep granted.
        /// This helper is that missing line, kept in one place so every co-simulation below
        /// exercises the same loop.
        /// </summary>
        static float StepClampedCar(ref float gap, ref float speed, ref float stuckSeconds,
                                    ref float escapeProgress, bool crossingLike)
        {
            speed = Entities.CarFollowing.NextSpeed(speed, 13.9f, gap, 0f,
                Entities.CarFollowing.DefaultHeadway, true, Dt);

            var clearance = Entities.CarFollowing.ClearanceFor(crossingLike, stuckSeconds);
            var allowance = Entities.CarFollowing.AllowedAdvance(gap, clearance);
            var delta = Mathf.Min(speed * Dt, allowance);

            // The write-back the real clamp does: next frame's model starts from what happened.
            speed = delta / Dt;
            gap -= delta;

            // TrafficRegistry.UpdateStall's escalation half, faithfully: travel resets the stuck
            // clock, being wedged (motionless with the clamp binding) advances it.
            if (speed >= StalledSpeed)
            {
                escapeProgress += speed * Dt;
                if (escapeProgress >= Entities.CarFollowing.EscapeResetDistance)
                {
                    stuckSeconds = 0f;
                    escapeProgress = 0f;
                }
            }
            else
            {
                escapeProgress = 0f;
                if (allowance <= Entities.CarFollowing.MinClearance)
                    stuckSeconds += Dt;
            }

            return delta;
        }

        /// <summary>
        /// THE gridlock. A ring member - stopped at exactly MinClearance behind a crossing car,
        /// which is where the clamp parks it - must eventually move, and must never be granted
        /// overlap to do it.
        ///
        /// Part one pins the stuck clock at zero and demands the car NOT move: that is the trap
        /// as built, and it doubles as proof that a car which is not escalating (all of normal
        /// traffic) keeps the full clearance. Part two lets the clock run and demands real
        /// displacement - the escalation must beat the clamp that the creep alone could not.
        /// </summary>
        static void RingMemberEscapesTheClampEventually(List<string> failures)
        {
            // Part one: no escalation, no movement. 5 simulated seconds is several release cycles.
            var gap = Entities.CarFollowing.MinClearance;
            var speed = 0f;
            var moved = 0f;
            var progress = 0f;

            for (var frame = 0; frame < 60 * 5; frame++)
            {
                var pinnedStuck = 0f;
                moved += StepClampedCar(ref gap, ref speed, ref pinnedStuck, ref progress, true);
            }

            if (moved > 1e-4f)
            {
                failures.Add($"un-escalated car at MinClearance moved {moved:0.###}m - the clamp is "
                           + "no longer holding ordinary traffic at full clearance");
                return;
            }

            // Part two: the clock runs, the car must escape - and the gap must never go below the
            // floor on the way.
            gap = Entities.CarFollowing.MinClearance;
            speed = 0f;
            moved = 0f;
            progress = 0f;
            var stuck = 0f;

            for (var frame = 0; frame < 60 * 30; frame++)
            {
                moved += StepClampedCar(ref gap, ref speed, ref stuck, ref progress, true);

                if (gap < Entities.CarFollowing.EscapeFloor - 1e-3f)
                {
                    failures.Add($"escaping car closed the gap to {gap:0.###}m, below the "
                               + $"EscapeFloor {Entities.CarFollowing.EscapeFloor:0.###} - the escape "
                               + "is producing overlap through the clamp");
                    return;
                }
            }

            if (moved < 0.3f)
            {
                failures.Add($"ring member moved only {moved:0.###}m in 30 simulated seconds - "
                           + "the escalation cannot beat the clamp and rings stay permanent");
            }
        }

        /// <summary>
        /// The escape must not leak into car following. A queue leader is stuck by the same
        /// bookkeeping - blocked, motionless - and if its follower's clearance decayed the same
        /// way, every long red light would end with the queue fused bumper to bumper, and the
        /// hard-escape rung would eventually wave cars THROUGH the leader.
        /// </summary>
        static void QueueNeverLosesItsClearance(List<string> failures)
        {
            foreach (var stuckSeconds in new[] { 0f, 10f, 60f })
            {
                var clearance = Entities.CarFollowing.ClearanceFor(false, stuckSeconds);
                if (Mathf.Abs(clearance - Entities.CarFollowing.MinClearance) > 1e-4f)
                {
                    failures.Add($"same-direction clearance at {stuckSeconds}s stuck is {clearance:0.###}, "
                               + $"expected MinClearance {Entities.CarFollowing.MinClearance:0.###} always");
                    return;
                }
            }

            // Co-simulate a follower wedged close behind a stopped leader, clock running.
            var gap = Entities.CarFollowing.MinClearance + 0.1f;
            var speed = 0f;
            var stuck = 0f;
            var progress = 0f;

            for (var frame = 0; frame < 60 * 60; frame++)
            {
                StepClampedCar(ref gap, ref speed, ref stuck, ref progress, false);

                if (gap < Entities.CarFollowing.MinClearance - 1e-3f)
                {
                    failures.Add($"follower closed to {gap:0.###}m on a same-direction leader while stuck - "
                               + "the escape ladder is eroding queue clearance");
                    return;
                }
            }
        }

        /// <summary>
        /// The shape of the decay itself: starts at the full clearance, never rises as stuck time
        /// grows, and bottoms out at a floor that is strictly positive - the escape shaves the
        /// margin, it never abolishes it.
        /// </summary>
        static void EscapeFloorIsBoundedAndMonotone(List<string> failures)
        {
            if (Entities.CarFollowing.EscapeFloor <= 0f)
                failures.Add("EscapeFloor is not positive - the escape can produce contact");

            var atZero = Entities.CarFollowing.ClearanceFor(true, 0f);
            if (Mathf.Abs(atZero - Entities.CarFollowing.MinClearance) > 1e-4f)
                failures.Add($"crossing clearance at 0s stuck is {atZero:0.###}, expected the full "
                           + $"MinClearance {Entities.CarFollowing.MinClearance:0.###}");

            var previous = float.PositiveInfinity;
            for (var t = 0f; t <= 60f; t += 0.5f)
            {
                var clearance = Entities.CarFollowing.ClearanceFor(true, t);

                if (clearance > previous + 1e-5f)
                {
                    failures.Add($"crossing clearance rose from {previous:0.###} to {clearance:0.###} at {t}s stuck");
                    return;
                }
                if (clearance < Entities.CarFollowing.EscapeFloor - 1e-5f)
                {
                    failures.Add($"crossing clearance {clearance:0.###} at {t}s stuck fell below the floor");
                    return;
                }
                previous = clearance;
            }
        }

        /// <summary>
        /// A stopped car must keep sight of the largest vehicle that can be lying across its
        /// line. Lookahead used to collapse to StandstillGap = 1.5m at rest, and 1.5m of sight
        /// is how junctions filled up: a car that stopped to yield went blind to the crosser,
        /// read the empty 1.5m as clear road, and nosed into the box before stopping again -
        /// INSIDE the junction, as one arc of the next ring.
        /// </summary>
        static void StoppedCarStillSeesTheJunction(List<string> failures)
        {
            const float busHalfLength = 5.65f;

            var atRest = Entities.CarFollowing.Lookahead(0f, Entities.CarFollowing.DefaultHeadway);
            if (atRest < busHalfLength + Entities.CarFollowing.StandstillGap)
                failures.Add($"Lookahead(0) = {atRest:0.###}m cannot cover a crossing bus "
                           + $"({busHalfLength} + {Entities.CarFollowing.StandstillGap}m) - "
                           + "a stopped yielder goes blind and noses into the junction");

            if (Entities.CarFollowing.Lookahead(10f, Entities.CarFollowing.DefaultHeadway) < atRest
                || Entities.CarFollowing.Lookahead(30f, Entities.CarFollowing.DefaultHeadway)
                   < Entities.CarFollowing.Lookahead(10f, Entities.CarFollowing.DefaultHeadway))
                failures.Add("Lookahead is not monotone in speed");

            if (Entities.CarFollowing.Lookahead(100f, Entities.CarFollowing.DefaultHeadway)
                > Entities.CarFollowing.MaxLookahead + 1e-4f)
                failures.Add("Lookahead exceeds MaxLookahead");
        }

        /// <summary>
        /// The geometry of the exit-clearance probe behind "don't block the box": a virtual car
        /// placed at the junction's far side. A stopped car straddling that spot blocks; a car
        /// well beyond the required room does not; a car on the CROSS street, laterally clear of
        /// the exit corridor, does not - that last one is the throughput case, since flagging it
        /// would hold cars back for traffic that is not in their way at all.
        /// </summary>
        static void ExitProbeGeometry(List<string> failures)
        {
            var exit = new Entities.TrafficBox(Vector3.zero, Vector3.forward, 2.25f, 1.05f);
            var requiredRoom = 2f * 2.25f + Entities.CarFollowing.StandstillGap;

            var straddling = new Entities.TrafficBox(new Vector3(0f, 0f, 2f), Vector3.forward, 2.25f, 1.05f);
            if (!ExitBlockedGeometry(exit, straddling, requiredRoom))
                failures.Add("a stopped car straddling the junction exit does not read as blocking it");

            var wellBeyond = new Entities.TrafficBox(new Vector3(0f, 0f, 12f), Vector3.forward, 2.25f, 1.05f);
            if (ExitBlockedGeometry(exit, wellBeyond, requiredRoom))
                failures.Add($"a stopped car {12f - 4.5f:0.#}m past the exit reads as blocking it "
                           + $"(required room is only {requiredRoom:0.#}m)");

            var crossStreet = new Entities.TrafficBox(new Vector3(6f, 0f, 1f), Vector3.right, 2.25f, 1.05f);
            if (ExitBlockedGeometry(exit, crossStreet, requiredRoom))
                failures.Add("a car on the cross street, laterally clear of the exit corridor, "
                           + "reads as blocking the exit - junctions would timidly hold for traffic "
                           + "that is not in the way");

            var oncoming = new Entities.TrafficBox(new Vector3(2.5f, 0f, 8f), Vector3.back, 2.25f, 1.05f);
            if (ExitBlockedGeometry(exit, oncoming, requiredRoom))
                failures.Add("an oncoming car beyond the exit reads as a blocker - the opposite "
                           + "carriageway is not our problem there any more than anywhere else");
        }

        /// <summary>The pure-geometry half of TrafficRegistry.IsExitBlocked, mirrored: overlap
        /// blocks outright; otherwise a measured, non-oncoming body inside the required room
        /// blocks. The speed filter is registry state and is exercised in play mode.</summary>
        static bool ExitBlockedGeometry(in Entities.TrafficBox exit, in Entities.TrafficBox other,
                                        float requiredRoom)
        {
            if (Entities.TrafficGeometry.Overlaps(exit, other))
                return true;

            if (!Entities.TrafficGeometry.TryMeasure(exit, other, requiredRoom, out var gap, out _, out var facing))
                return false;

            if (facing < Entities.TrafficGeometry.OncomingDot)
                return false;

            return gap < requiredRoom;
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
