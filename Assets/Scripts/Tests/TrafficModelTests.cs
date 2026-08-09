using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

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
            DockingCurveTerminatesAtTheStall(failures);
            DockingStepNeverBeatsTheCap(failures);
            DockingFinalApproachIsMonotone(failures);
            PoliceIntentionCoversEveryState(failures);
            SchoolIntentionCoversEveryState(failures);
            ForecourtIntentionCoversEveryState(failures);
            SchoolLineNamesEveryPupil(failures);
            StallClaimsAreExclusiveAndReleasable(failures);
            NoCrosswalkHoldIsPermanent(failures);
            CrossingOccupancyGoesStale(failures);
            SchoolRunWindowWrapsMidnight(failures);
            SchoolRunWindowDegeneratesCleanly(failures);
            SchoolStopRejectsInnerLanes(failures);
            SchoolQueueSlotsNeverOverlap(failures);
            SchoolAfternoonOrderIsTheMorningReversed(failures);
            OpposingLeftTurnsClearTheWidestVehicle(failures);
            TurnExitsLineUpWithTheLaneTheyJoin(failures);

            return failures;
        }

        // ------------------------------------------------ junction turn geometry
        //
        // The gap in this file that let "na svako skretanje se zamalo udare kola" ship: 25 checks
        // on following, yielding and crossing, and not one on the SHAPE of a turn. The polylines
        // below are transcribed from the tile prefabs' Path components in the tile's own LOCAL
        // frame (authored metres, +z north, the tile spanning -15..+15), so this is a table
        // checked against a measurement rather than against itself. Anything compared with a
        // vehicle has to be multiplied by CityGrid.TileScale first - the tiles are placed
        // stretched and the cars are not, which is the entire point of the stretch.

        /// <summary>tile-road-intersection, Line Turn Left 1: southbound arm turning west.</summary>
        static readonly Vector2[] MinorLeftA =
        {
            new(1.5f, -15f), new(1.5f, -1.5f), new(-15f, 1.5f),
        };

        /// <summary>tile-road-intersection, Line Turn Left 2: northbound arm turning east.</summary>
        static readonly Vector2[] MinorLeftB =
        {
            new(-1.5f, 15f), new(-1.5f, 1.5f), new(15f, -1.5f),
        };

        /// <summary>
        /// tile-road-mainroad-intersection, Line Turn Left 1 - the avenue's southbound inner lane
        /// turning west. This is the tile the generator actually places at the boulevard's
        /// crossings, not the all-mainroad piece: only the depth-0 cut is a dual carriageway, so
        /// every one of its junctions is the MIXED shape.
        /// </summary>
        static readonly Vector2[] AvenueLeftA =
        {
            new(1.75f, -15f), new(1.2f, -2.0f), new(-6.0f, 1.5f), new(-15f, 1.5f),
        };

        /// <summary>tile-road-mainroad-intersection, Line Turn Left 2.</summary>
        static readonly Vector2[] AvenueLeftB =
        {
            new(-1.75f, 15f), new(-1.2f, 2.0f), new(7.0f, -1.5f), new(15f, -1.5f),
        };

        /// <summary>
        /// The widest body on the road, off bus-school_AI's solid BoxCollider (2.9832 on local x).
        /// armored-truck_AI is 2.287, truck_AI 2.262 and car-passenger_AI 2.097. Restated here
        /// rather than read from a prefab so this stays runnable in a bare .NET host.
        ///
        /// This was 2.854 - bus-passenger_AI - for as long as that bus was in the traffic buckets,
        /// and it was wrong for just as long: the school bus is 12.9cm WIDER, and it has always
        /// driven the same junctions, SchoolBusDirector steering it between the school and its
        /// stops. Taking the passenger bus out of the fleet is what made the omission visible. If
        /// a wider body is ever added, this number moves with it - the point of the check is that
        /// nothing on the road is wider than the gap two opposing left turns leave.
        /// </summary>
        const float WidestVehicle = 2.9832f;

        /// <summary>
        /// Two cars turning left from opposite arms at the same time must not occupy the same
        /// ground. Nothing else in the system stops them: their heading dot is about -1 both
        /// before and after the corner, and TrafficRegistry.Probe skips anything below OncomingDot
        /// unless the bodies ALREADY intersect - so the first time it notices is once they are
        /// inside each other, at which point Overlapping fires and the pair is allowed to drive
        /// through and untangle. The separation has to come from the geometry or from nowhere.
        ///
        /// At authored size it came from nowhere. The two exit legs are antiparallel 2.41m apart
        /// on a crossroads and 2.55m on the avenue's, while two cars need 2.10m and the bus and a
        /// car 2.54m - and at closest approach they are not even parallel, so the real footprint
        /// is a half-LENGTH. CityGrid.TileScale is what buys the clearance, which is why the
        /// premise below asserts that BOTH families fail without it: drop the scale back to 1 and
        /// this test goes red rather than quietly passing on a city that has the bug again.
        /// </summary>
        static void OpposingLeftTurnsClearTheWidestVehicle(List<string> failures)
        {
            foreach (var (name, a, b) in new (string, Vector2[], Vector2[])[]
                     {
                         ("crossroads", MinorLeftA, MinorLeftB),
                         ("avenue crossing", AvenueLeftA, AvenueLeftB),
                     })
            {
                var authored = PolylineDistance(a, b);
                var placed = authored * CityGrid.TileScale;

                if (placed < WidestVehicle)
                    failures.Add($"Junction turns: opposing left turns on the {name} pass " +
                                 $"{placed:0.###}m apart as placed ({authored:0.###} authored x " +
                                 $"{CityGrid.TileScale}), inside the widest vehicle's " +
                                 $"{WidestVehicle}m. Two of them turning at once share the " +
                                 "same ground.");

                if (authored >= WidestVehicle)
                    failures.Add($"Junction turns: the {name} already clears {authored:0.###}m " +
                                 "unstretched, so this check no longer shows that " +
                                 "CityGrid.TileScale is what makes the turn safe.");
            }
        }

        /// <summary>
        /// A turn should hand the car to its destination lane pointing DOWN it, and the avenue's
        /// turns do: their last segment runs straight along the lane they join. The crossroads'
        /// is a chord that reaches the tile edge 10.3 degrees off axis, so the car makes a second
        /// correction the instant it crosses into the next tile.
        ///
        /// Angles, unlike distances, are scale-invariant - stretching the city cannot fix this
        /// one, which is exactly why it is worth stating. Asserted on the avenue and MEASURED on
        /// the crossroads: the minor tile is the pack's and we are not going to reshape it, so
        /// the check records what it costs rather than pretending it does not.
        ///
        /// The vertex cap is relative for the same reason. A junction turn is a polyline, not an
        /// arc, and CarBehavior.UpdateCheckpoint does not retarget until the front axle is abeam
        /// the node - so whatever a vertex asks for is spent AFTER it, at approach speed. The
        /// crossroads asks for 79.7 degrees in one go; the avenue splits it.
        /// </summary>
        static void TurnExitsLineUpWithTheLaneTheyJoin(List<string> failures)
        {
            const float exitTolerance = 1f;

            foreach (var (name, turn, laneAxis) in new (string, Vector2[], Vector2)[]
                     {
                         ("avenue S->W", AvenueLeftA, new Vector2(-1f, 0f)),
                         ("avenue N->E", AvenueLeftB, new Vector2(1f, 0f)),
                     })
            {
                var last = turn[turn.Length - 1] - turn[turn.Length - 2];
                var off = Vector2.Angle(last, laneAxis);
                if (off > exitTolerance)
                    failures.Add($"Junction turns: the {name} exit leg leaves {off:0.##} degrees " +
                                 $"off its lane axis, so the car corrects again on the tile " +
                                 "boundary beside oncoming traffic.");
            }

            var minorExit = MinorLeftA[MinorLeftA.Length - 1] - MinorLeftA[MinorLeftA.Length - 2];
            var minorOff = Vector2.Angle(minorExit, new Vector2(-1f, 0f));
            if (minorOff <= exitTolerance)
                failures.Add($"Junction turns: the crossroads exit leg is now only {minorOff:0.##} " +
                             "degrees off axis, so this check no longer discriminates.");

            var minorVertex = SharpestVertex(MinorLeftA);
            var avenueVertex = SharpestVertex(AvenueLeftA);
            if (avenueVertex >= minorVertex)
                failures.Add($"Junction turns: the avenue's sharpest vertex is {avenueVertex:0.##} " +
                             $"degrees against the crossroads' {minorVertex:0.##} - its extra " +
                             "shaping node has stopped splitting the corner.");
        }

        /// <summary>Largest heading change at any interior vertex of a polyline, in degrees.</summary>
        static float SharpestVertex(Vector2[] line)
        {
            var worst = 0f;
            for (var i = 1; i < line.Length - 1; i++)
                worst = Mathf.Max(worst, Vector2.Angle(line[i] - line[i - 1], line[i + 1] - line[i]));
            return worst;
        }

        /// <summary>Closest approach between two polylines.</summary>
        static float PolylineDistance(Vector2[] a, Vector2[] b)
        {
            var best = float.MaxValue;
            for (var i = 0; i < a.Length - 1; i++)
            for (var j = 0; j < b.Length - 1; j++)
                best = Mathf.Min(best, SegmentDistance(a[i], a[i + 1], b[j], b[j + 1]));
            return best;
        }

        /// <summary>
        /// Closest approach between two segments. In the plane, two segments that do not cross
        /// attain their minimum at an endpoint of one of them, so four point-to-segment distances
        /// settle it - no parametric solve, and no degenerate case when they are parallel, which
        /// the two exit legs here are.
        /// </summary>
        static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (SegmentsCross(a, b, c, d))
                return 0f;

            return Mathf.Min(
                Mathf.Min(PointToSegment(a, c, d), PointToSegment(b, c, d)),
                Mathf.Min(PointToSegment(c, a, b), PointToSegment(d, a, b)));
        }

        static float PointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-9f)
                return (p - a).magnitude;

            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
            return (p - (a + ab * t)).magnitude;
        }

        static float Cross(Vector2 o, Vector2 p, Vector2 q) =>
            (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);

        /// <summary>
        /// Strict crossing only. Touching and collinear cases fall through to the endpoint
        /// distances above, which answer 0 for them anyway.
        /// </summary>
        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var d1 = Cross(a, b, c);
            var d2 = Cross(a, b, d);
            var d3 = Cross(c, d, a);
            var d4 = Cross(c, d, b);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        /// <summary>
        /// The jam this file exists to make impossible: one pedestrian standing on a crossing
        /// used to stop a car for the rest of the session, and TrafficRegistry queued everybody
        /// behind it. Two independent bounds now, and the properties are what matter - a crossing
        /// that reports nothing must release at the watchdog, and a crossing that reports people
        /// for ever must still release.
        /// </summary>
        static void NoCrosswalkHoldIsPermanent(List<string> failures)
        {
            var wasHeld = true;

            for (var held = 0f; held <= 60f; held += 0.1f)
            {
                var stillHeld = !PolyPerfect.City.CarBehavior.CrosswalkHoldExpired(true, held);

                if (held > PolyPerfect.City.CarBehavior.CrosswalkPatience && stillHeld)
                {
                    failures.Add($"Crosswalk hold: still holding after {held}s of a crossing " +
                                 "that never clears.");
                    return;
                }

                // Monotone: once a car may go, it may not be told to wait again.
                if (stillHeld && !wasHeld)
                {
                    failures.Add($"Crosswalk hold: released and then re-held at {held}s.");
                    return;
                }
                wasHeld = stillHeld;

                var clearedHold = !PolyPerfect.City.CarBehavior.CrosswalkHoldExpired(false, held);
                if (held > PolyPerfect.City.CarBehavior.HoldWatchdog && clearedHold)
                {
                    failures.Add($"Crosswalk hold: a crossing reporting nobody still held a " +
                                 $"car at {held}s.");
                    return;
                }
            }

            // And the yield is real: a car must not drive off the instant it arrives.
            if (PolyPerfect.City.CarBehavior.CrosswalkHoldExpired(true, 0f))
                failures.Add("Crosswalk hold: a car released immediately, so nobody is yielded to.");
        }

        /// <summary>
        /// The other half of the same bound, on the crossing's side: somebody who is walking
        /// holds the traffic for as long as they keep walking, and somebody who has stopped
        /// stops counting. The occupancy rule is the mechanism and the car's cap is the backstop,
        /// so the rule has to fire first or the cap becomes the mechanism.
        /// </summary>
        static void CrossingOccupancyGoesStale(List<string> failures)
        {
            const float now = 100f;

            if (!Crosswalk.CountsAsCrossing(now, now))
                failures.Add("Crossing: somebody who just moved is not crossing.");

            if (!Crosswalk.CountsAsCrossing(now, now - Crosswalk.StaleAfter * 0.5f))
                failures.Add("Crossing: a walker between paces stopped counting as crossing.");

            if (Crosswalk.CountsAsCrossing(now, now - Crosswalk.StaleAfter - 0.01f))
                failures.Add("Crossing: somebody motionless past StaleAfter still holds the traffic.");

            // Monotone in time: a stationary occupant cannot come back.
            var wasCrossing = true;
            for (var since = 0f; since <= 30f; since += 0.05f)
            {
                var crossing = Crosswalk.CountsAsCrossing(now, now - since);
                if (crossing && !wasCrossing)
                {
                    failures.Add($"Crossing: stopped counting and then started again at {since}s.");
                    return;
                }
                wasCrossing = crossing;
            }

            if (Crosswalk.StaleAfter >= PolyPerfect.City.CarBehavior.CrosswalkPatience)
                failures.Add("Crossing: StaleAfter does not beat the car's patience, so the cap " +
                             "is doing the work the occupancy rule is supposed to do.");
        }

        /// <summary>
        /// Two cars must never be sent to the same bay. StallHost is the one piece of state the
        /// patrol fleet and the bank's customers now share, and it hands out bays from separate
        /// coroutines - a claim that is not exclusive, or a release that does not put the bay
        /// back, shows up as two cars interpenetrating in a forecourt and nowhere earlier.
        ///
        /// This is the one assertion in the file that touches a UnityEngine.Object. It gets
        /// away with it because the claim path reads nothing but its own managed fields: the
        /// instance is allocated without a constructor and never has a Unity API called on it,
        /// so no native peer is needed. Anything here that started touching transform would
        /// have to move to a Play-mode test instead.
        /// </summary>
        static void StallClaimsAreExclusiveAndReleasable(List<string> failures)
        {
            const int Bays = 4;

            var host = (Entities.StallHost)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(Entities.PoliceStation));

            var field = typeof(Entities.StallHost).GetField(
                "stallLocalPositions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (field == null)
            {
                failures.Add("StallHost: stallLocalPositions is gone - the marker's serialized " +
                             "field was renamed, which silently empties every baked forecourt.");
                return;
            }

            field.SetValue(host, new Vector3[Bays]);

            if (host.StallCount != Bays)
                failures.Add($"StallHost: {Bays} bays in, StallCount says {host.StallCount}.");

            var claimed = new HashSet<int>();
            for (var i = 0; i < Bays; i++)
            {
                if (!host.TryClaimStall(out var stall))
                {
                    failures.Add($"StallHost: bay {i} of {Bays} refused a claim while free.");
                    return;
                }

                if (!claimed.Add(stall))
                    failures.Add($"StallHost: bay {stall} was handed out twice.");
            }

            if (host.TryClaimStall(out var overflow))
                failures.Add($"StallHost: a full forecourt still handed out bay {overflow}.");
            else if (overflow != -1)
                failures.Add($"StallHost: a refused claim must report -1, reported {overflow}.");

            // A car leaving has to give its bay back, and only its own - the release that put
            // every bay back would let the next claimant into an occupied one.
            host.ReleaseStall(2);
            if (!host.TryClaimStall(out var reused) || reused != 2)
                failures.Add($"StallHost: released bay 2, next claim got {reused}.");
            if (host.TryClaimStall(out _))
                failures.Add("StallHost: releasing one bay freed more than one.");

            // Out-of-range releases arrive from real code paths - an agent destroyed before it
            // ever claimed carries stall -1 - and must not throw.
            host.ReleaseStall(-1);
            host.ReleaseStall(Bays + 5);
        }

        /// <summary>
        /// Every state a police unit can be in has a sentence and a colour. Nothing at
        /// runtime would complain about a gap - a state missing from PoliceIntention ships
        /// as a white diamond over an empty popup - so exhaustiveness is asserted here,
        /// where adding a state to either enum fails the build's tests instead of the
        /// player's eye. White is the map's own "unmapped" fallback, which is what makes it
        /// assertable.
        /// </summary>
        static void PoliceIntentionCoversEveryState(List<string> failures)
        {
            foreach (Entities.PolicePatrolAgent.State state in
                     System.Enum.GetValues(typeof(Entities.PolicePatrolAgent.State)))
            {
                if (string.IsNullOrEmpty(UI.PoliceIntention.CarIntention(state, 2)))
                    failures.Add($"PoliceIntention: car state {state} has no sentence.");
                if (UI.PoliceIntention.CarColor(state) == Color.white)
                    failures.Add($"PoliceIntention: car state {state} has no colour.");
            }

            foreach (Entities.PoliceOfficerAgent.State state in
                     System.Enum.GetValues(typeof(Entities.PoliceOfficerAgent.State)))
            {
                if (string.IsNullOrEmpty(UI.PoliceIntention.OfficerIntention(state, 2)))
                    failures.Add($"PoliceIntention: officer state {state} has no sentence.");
                if (UI.PoliceIntention.OfficerColor(state) == Color.white)
                    failures.Add($"PoliceIntention: officer state {state} has no colour.");
            }
        }

        /// <summary>
        /// The same guarantee for the school run, and it matters more here than for the police:
        /// SchoolBusAgent's enum grew from five states to eight in one edit, and three silently
        /// unmapped states would have shipped as three white diamonds with empty popups.
        ///
        /// The bus's sentence is checked in BOTH run directions, because two of its states word
        /// themselves off that flag and a map that answered only one of them would pass a test
        /// that asked only once.
        /// </summary>
        static void SchoolIntentionCoversEveryState(List<string> failures)
        {
            foreach (Entities.SchoolBusAgent.State state in
                     System.Enum.GetValues(typeof(Entities.SchoolBusAgent.State)))
            {
                foreach (var toSchool in new[] { true, false })
                    if (string.IsNullOrEmpty(
                            UI.SchoolIntention.BusIntention(state, 2, 3, toSchool)))
                        failures.Add($"SchoolIntention: bus state {state} has no sentence " +
                                     $"(toSchool {toSchool}).");

                // Off the road, where the stop counters are both zero - the Driving branch has
                // to survive it, because a run whose stops were all unreachable passes through.
                if (string.IsNullOrEmpty(UI.SchoolIntention.BusIntention(state, 0, 0, true)))
                    failures.Add($"SchoolIntention: bus state {state} has no sentence off-route.");

                if (UI.SchoolIntention.BusColor(state) == Color.white)
                    failures.Add($"SchoolIntention: bus state {state} has no colour.");
            }

            foreach (Entities.SchoolChildAgent.State state in
                     System.Enum.GetValues(typeof(Entities.SchoolChildAgent.State)))
            {
                if (string.IsNullOrEmpty(UI.SchoolIntention.ChildIntention(state)))
                    failures.Add($"SchoolIntention: child state {state} has no sentence.");
                if (UI.SchoolIntention.ChildColor(state) == Color.white)
                    failures.Add($"SchoolIntention: child state {state} has no colour.");
            }
        }

        /// <summary>
        /// Forecourt drivers, across both errands. The errand is the reason this is not one
        /// loop: two of the four states word themselves off it, so a map missing the school's
        /// half would still pass a check that only ever asked about the bank.
        /// </summary>
        static void ForecourtIntentionCoversEveryState(List<string> failures)
        {
            foreach (Entities.ForecourtVisitorAgent.State state in
                     System.Enum.GetValues(typeof(Entities.ForecourtVisitorAgent.State)))
            {
                if (UI.ForecourtIntention.VisitorColor(state) == Color.white)
                    failures.Add($"ForecourtIntention: visitor state {state} has no colour.");

                foreach (UI.ForecourtErrand errand in
                         System.Enum.GetValues(typeof(UI.ForecourtErrand)))
                {
                    if (string.IsNullOrEmpty(UI.ForecourtIntention.VisitorIntention(state, errand)))
                        failures.Add($"ForecourtIntention: {errand} visitor state {state} has " +
                                     "no sentence.");
                    if (string.IsNullOrEmpty(UI.ForecourtIntention.VisitorTitle(errand)))
                        failures.Add($"ForecourtIntention: errand {errand} has no title.");
                }
            }
        }

        /// <summary>
        /// The two building lines are composed rather than looked up, so exhaustiveness says
        /// nothing about them - the failure mode is an empty clause or a lost pupil, not a
        /// missing case. Both are checked by counting: whatever the school says, it must never
        /// say nothing, and it must never report a state of the roster that cannot happen.
        /// </summary>
        static void SchoolLineNamesEveryPupil(List<string> failures)
        {
            foreach (Entities.SchoolBusAgent.State state in
                     System.Enum.GetValues(typeof(Entities.SchoolBusAgent.State)))
                if (string.IsNullOrEmpty(UI.SchoolIntention.SchoolLine(3, 2, 1, true, state)))
                    failures.Add($"SchoolIntention: school line empty with bus state {state}.");

            // The four ways a roster can be distributed, including the empty one - a school
            // whose children are all mid-journey still has something true to say.
            if (string.IsNullOrEmpty(UI.SchoolIntention.SchoolLine(0, 0, 0, false, default)))
                failures.Add("SchoolIntention: school line empty with an empty roster.");
            if (string.IsNullOrEmpty(UI.SchoolIntention.SchoolLine(0, 0, 4, true, default)))
                failures.Add("SchoolIntention: school line empty with everyone travelling.");

            // Singular and plural are separate branches and both have shipped wrong elsewhere.
            if (!UI.SchoolIntention.SchoolLine(1, 0, 0, false, default).Contains("1 child "))
                failures.Add("SchoolIntention: one waiting child is not reported in the singular.");
            if (!UI.SchoolIntention.SchoolLine(2, 0, 0, false, default).Contains("2 children"))
                failures.Add("SchoolIntention: two waiting children are not reported in the plural.");

            // The bank's line, same shape: a full forecourt and an empty one both say something.
            if (string.IsNullOrEmpty(UI.ForecourtIntention.BankLine(0, 0, 0)))
                failures.Add("ForecourtIntention: bank line empty with no bays at all.");
            if (!UI.ForecourtIntention.BankLine(2, 0, 5).Contains("full"))
                failures.Add("ForecourtIntention: a bank with no free bays does not say so.");
        }

        /// <summary>
        /// The dock/undock geometries a patrol car actually flies, restated here rather than
        /// imported: a kerb roughly a stall-depth-and-a-street out from the bay, approaches
        /// from either side along the lane, and the degenerate everything-coincident case
        /// that must terminate rather than divide to a NaN.
        /// </summary>
        static readonly (Vector3 from, Vector3 stall, Vector3 stallOut)[] DockCases =
        {
            (new Vector3(10f, 0f, 8f), Vector3.zero, Vector3.forward),
            (new Vector3(-12f, 0f, 9f), Vector3.zero, Vector3.forward),
            (new Vector3(2f, 0f, 10f), Vector3.zero, Vector3.forward),
            (new Vector3(0.5f, 0f, 0.5f), Vector3.zero, Vector3.forward),
            (Vector3.zero, Vector3.zero, Vector3.forward),
        };

        /// <summary>
        /// Advance must reach t = 1 in bounded steps for every realistic geometry, and the
        /// endpoints must be exact: t 0 is where the car stands, t 1 is the stall centre.
        /// The degenerate all-points-coincident curve is the case the tangent floor exists
        /// for - without it this loop never ends.
        /// </summary>
        static void DockingCurveTerminatesAtTheStall(List<string> failures)
        {
            foreach (var (from, stall, stallOut) in DockCases)
            {
                var curve = Entities.PoliceDocking.Dock(from, stall, stallOut);

                if ((Entities.PoliceDocking.Point(curve, 0f) - from).magnitude > 1e-4f)
                    failures.Add($"Docking: curve from {from} does not start at the car.");
                if ((Entities.PoliceDocking.Point(curve, 1f) - stall).magnitude > 1e-4f)
                    failures.Add($"Docking: curve from {from} does not end in the stall.");

                var t = 0f;
                var steps = 0;
                while (t < 1f && steps < 10_000)
                {
                    t = Entities.PoliceDocking.Advance(curve, t, Entities.PoliceDocking.Speed * Dt);
                    steps++;
                }

                if (t < 1f)
                    failures.Add($"Docking: curve from {from} never terminates " +
                                 $"({steps} steps, t {t:F3}).");
            }
        }

        /// <summary>
        /// The cap is stated in metres per second, so the property is about DISPLACEMENT: no
        /// single Advance step may move the car meaningfully further than Speed * Dt. The
        /// step is first-order in the tangent, so a curved segment can overrun by its
        /// curvature error - 20% headroom accepts that and still catches a parameter-space
        /// step (which overruns by whole multiples on a long curve).
        /// </summary>
        static void DockingStepNeverBeatsTheCap(List<string> failures)
        {
            foreach (var (from, stall, stallOut) in DockCases)
            {
                var curve = Entities.PoliceDocking.Dock(from, stall, stallOut);
                var ds = Entities.PoliceDocking.Speed * Dt;

                var t = 0f;
                while (t < 1f)
                {
                    var next = Entities.PoliceDocking.Advance(curve, t, ds);
                    var moved = (Entities.PoliceDocking.Point(curve, next)
                               - Entities.PoliceDocking.Point(curve, t)).magnitude;
                    if (moved > ds * 1.2f)
                    {
                        failures.Add($"Docking: one step from {from} moved {moved:F3}m " +
                                     $"against a cap of {ds:F3}m at t {t:F3}.");
                        break;
                    }
                    t = next;
                }
            }
        }

        /// <summary>
        /// The visually critical half: once past the mouth (t 0.5, the control point's side
        /// of the curve), the car must close on the stall monotonically - an oscillation at
        /// the bay is exactly the "parking by binary search" artefact that would read as a
        /// bug from the camera. The first half owes no such promise: a car standing nearly
        /// in the mouth balloons outward before it can swing in, and that is the manoeuvre
        /// working, not failing.
        /// </summary>
        static void DockingFinalApproachIsMonotone(List<string> failures)
        {
            foreach (var (from, stall, stallOut) in DockCases)
            {
                var curve = Entities.PoliceDocking.Dock(from, stall, stallOut);

                var previous = float.PositiveInfinity;
                for (var t = 0.5f; t <= 1f; t += 0.01f)
                {
                    var distance = (Entities.PoliceDocking.Point(curve, t) - stall).magnitude;
                    if (distance > previous + 1e-4f)
                    {
                        failures.Add($"Docking: the final approach from {from} backs away " +
                                     $"from the stall at t {t:F2}.");
                        break;
                    }
                    previous = distance;
                }
            }
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

        /// <summary>
        /// The school run's window has to survive midnight. It is written as start + LENGTH for
        /// exactly this reason - with an end hour, a window from 23:30 to 01:30 is the one case
        /// every hand-rolled comparison gets wrong, and it gets it wrong silently: the bus
        /// simply never leaves.
        /// </summary>
        static void SchoolRunWindowWrapsMidnight(List<string> failures)
        {
            if (!Entities.SchoolRun.InWindow(7.5f, 7.5f, 1.5f))
                failures.Add("SchoolRun: the window's own start hour reads as outside it");

            if (Entities.SchoolRun.InWindow(9f, 7.5f, 1.5f))
                failures.Add("SchoolRun: the hour the window closes at reads as inside it");

            if (!Entities.SchoolRun.InWindow(8.9f, 7.5f, 1.5f))
                failures.Add("SchoolRun: an hour inside the window reads as outside it");

            // 23:30 for two hours: 00:30 is in, 01:30 is the close, 12:00 is nowhere near.
            if (!Entities.SchoolRun.InWindow(0.5f, 23.5f, 2f))
                failures.Add("SchoolRun: a window crossing midnight excludes 00:30");

            if (Entities.SchoolRun.InWindow(1.5f, 23.5f, 2f))
                failures.Add("SchoolRun: a window crossing midnight includes its own close");

            if (Entities.SchoolRun.InWindow(12f, 23.5f, 2f))
                failures.Add("SchoolRun: a window crossing midnight includes midday");
        }

        /// <summary>
        /// The two ends of the dial. A zero-length window must be empty rather than ambiguous,
        /// and a full-day one must be always-open rather than a floating-point coin toss at the
        /// wrap - both are reachable from the inspector, where the field is a plain float.
        /// </summary>
        static void SchoolRunWindowDegeneratesCleanly(List<string> failures)
        {
            for (var hour = 0f; hour < 24f; hour += 0.37f)
            {
                if (Entities.SchoolRun.InWindow(hour, 7.5f, 0f))
                    failures.Add($"SchoolRun: a zero-length window is open at {hour:0.##}");

                if (!Entities.SchoolRun.InWindow(hour, 7.5f, 24f))
                    failures.Add($"SchoolRun: a full-day window is shut at {hour:0.##}");
            }
        }

        /// <summary>
        /// A bus halted in a boulevard's INNER lane puts its door on the far side of a live
        /// outer lane, and every child walks across it. Nothing at runtime would complain -
        /// the children would simply be run over off camera - so the rule is asserted here.
        /// The numbers are CityGrid's own: a minor street carries lanes at +/-1.5, a boulevard
        /// at +/-1.75 and +/-4.75.
        /// </summary>
        static void SchoolStopRejectsInnerLanes(List<string> failures)
        {
            if (!Entities.SchoolRun.IsBoardableLane(1.5f, 1.5f))
                failures.Add("SchoolRun: a minor street's only lane is rejected as unboardable");

            if (!Entities.SchoolRun.IsBoardableLane(-1.5f, -1.5f))
                failures.Add("SchoolRun: a minor street's other side is rejected as unboardable");

            if (!Entities.SchoolRun.IsBoardableLane(4.75f, 4.75f))
                failures.Add("SchoolRun: a boulevard's outer lane is rejected as unboardable");

            if (Entities.SchoolRun.IsBoardableLane(1.75f, 4.75f))
                failures.Add("SchoolRun: a boulevard's INNER lane is accepted - children would "
                           + "cross a live outer lane to board");

            if (Entities.SchoolRun.IsBoardableLane(-1.75f, 4.75f))
                failures.Add("SchoolRun: a lane on the far side of the road is accepted");

            if (Entities.SchoolRun.IsBoardableLane(1.5f, 0f))
                failures.Add("SchoolRun: a tile with no road lanes accepts a stop");
        }

        /// <summary>
        /// Two children may not be given the same square metre to stand in. PedestrianRegistry
        /// would push them apart, but the queue would then dissolve into the scrum the fan-out
        /// exists to prevent - and at the kerb, dissolving means stepping into the road.
        /// </summary>
        static void SchoolQueueSlotsNeverOverlap(List<string> failures)
        {
            var clearance = 2f * Entities.PedestrianSteering.DefaultRadius;

            const int count = 8;
            var line = new Vector3[count];
            var fan = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                line[i] = Entities.SchoolRun.QueueSlot(i, Vector3.zero, Vector3.back);
                fan[i] = Entities.SchoolRun.FanSlot(i, count, Vector3.zero, Vector3.right);
            }

            for (var a = 0; a < count; a++)
            for (var b = a + 1; b < count; b++)
            {
                if ((line[a] - line[b]).magnitude < clearance)
                    failures.Add($"SchoolRun: queue slots {a} and {b} are closer than two bodies");

                if ((fan[a] - fan[b]).magnitude < clearance)
                    failures.Add($"SchoolRun: fan slots {a} and {b} are closer than two bodies");
            }

            // The fan is about the door, not off to one side of it, or half the class queues
            // through a wall.
            var centre = Vector3.zero;
            foreach (var slot in fan)
                centre += slot;

            if (centre.magnitude / count > 1e-3f)
                failures.Add("SchoolRun: the door fan is not centred on the door");
        }

        /// <summary>
        /// The afternoon serves the stops in the exact reverse of the morning, so the child
        /// picked up first is put down last and the day reads as one round trip. Asserted as an
        /// involution because that is the property, and it is the one an off-by-one in either
        /// direction breaks.
        /// </summary>
        static void SchoolAfternoonOrderIsTheMorningReversed(List<string> failures)
        {
            for (var count = 0; count <= 5; count++)
            {
                var morning = Entities.SchoolRun.StopOrder(count, outbound: true);
                var afternoon = Entities.SchoolRun.StopOrder(count, outbound: false);

                if (morning.Length != count || afternoon.Length != count)
                {
                    failures.Add($"SchoolRun: {count} stops produced orders of "
                               + $"{morning.Length} and {afternoon.Length}");
                    continue;
                }

                var seen = new HashSet<int>();
                for (var i = 0; i < count; i++)
                {
                    if (morning[i] != i)
                        failures.Add($"SchoolRun: the morning order is not outward at {i}");

                    if (afternoon[i] != morning[count - 1 - i])
                        failures.Add($"SchoolRun: the afternoon order is not the morning "
                                   + $"reversed at {i}");

                    if (!seen.Add(afternoon[i]))
                        failures.Add($"SchoolRun: stop {afternoon[i]} is served twice");
                }
            }
        }
    }
}
