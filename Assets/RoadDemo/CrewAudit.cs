using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The crews measured against their own rules, every frame of a watched run.
    //
    // The faults this hunts were all seen by eye first - a man snapping a metre
    // sideways when an order lands, a crew threading a street in single file, a
    // round fired with the barrel in the pavement, a hood stood alone on a zebra,
    // the whole mob chasing its routed enemies while the one still shooting picks
    // them apart. An eye at the glass does not scale to thirty seeded runs; an
    // inequality in the code does. Each rule here is one of those sights turned
    // into a measurement, and a breach goes down as a "fault" row in the trace
    // (DriveTrace) for Tools/play/analyze.py --crew to fail the run over.
    //
    // Runs ONLY while the black box is recording (DemoCrews ticks it behind
    // DriveTrace.On), so a normal Play session pays one bool test.
    public static class CrewAudit
    {
        // -------------------------------------------------------------- thresholds

        /// <summary>Metres a man on foot may cover in one frame before it is not a
        /// step but a snap. Roomy: the fastest jog (~3.9 m/s), the Separate ease
        /// (half an elbow) and a long frame all together stay well under it.</summary>
        const float TeleportBound = 1.0f;

        /// <summary>Seconds off the scene's floor before it is called. The stride
        /// slides along the hem, so even a frame out is wrong - the grace only
        /// keeps a boundary-rounding jitter from counting.</summary>
        const float OffCityAfter = 0.5f;

        /// <summary>Metres off his crew, and seconds at it, that make a hood a
        /// stray. The tether (DemoCrews.TickCohesion) starts hauling at 7 m and
        /// hurries at 14 - but a 480 m leg threads four signals, and a front man
        /// held at the far kerb while the boss walks his own lights legitimately
        /// sits near 30 m for a spell. The pathologies this exists for measured
        /// 44-93 m and GREW; 32 m held for twelve seconds clears the lights and
        /// still catches those with room to spare.</summary>
        const float StrayGap = 32f, StrayAfter = 12f;

        /// <summary>Three or more men of one crew walking one stretch spread across
        /// less than this for this long are a queue, not a gang - the dealt lanes
        /// (FormationLane) exist to keep the spread above it.</summary>
        const float FileSpread = 0.8f;
        // long enough that a legitimate funnel passes before the clock runs out: a
        // bin row's one clear line, or a commuter surge on the clock hour that the
        // crew threads in file for ten seconds (same sim-second, different
        // quarters - it was the crowd, not the street). The fault this exists for -
        // the crew WALKING single file because everyone held one lane - held for
        // whole streets, minutes at a time.
        const float FileAfter = 14f;

        /// <summary>Degrees the muzzle may be off the line to the mark's chest on
        /// the frame a round leaves it. The aim overlay fires at blend >= 0.5, so
        /// half a raise is the worst that is legal; past this the man is shooting
        /// the pavement.</summary>
        const float AimOffLimit = 35f;

        /// <summary>Seconds stood at a light before the wait itself is a fault. The
        /// tether crosses a straggler after his crew, so only a whole crew waiting
        /// together waits at all - and no light holds a green this long.</summary>
        const float LightAfter = 40f;

        /// <summary>Seconds STOOD (not waiting - arrived, order done) on a zebra.
        /// No destination is ever dealt on a gated link, so standing on one means
        /// an order ended where none should.</summary>
        const float ZebraStandAfter = 8f;

        /// <summary>Seconds a man not in a fight may spend walking ALONG the carriageway
        /// before it is a fault. Crossing one is not counted at all (a boulevard takes
        /// twenty seconds to cross and that is a man crossing a road); what is counted is
        /// ground made down the road rather than over it, which is a man using the
        /// carriageway as a pavement and standing in the traffic's way.</summary>
        const float RoadWalkAfter = 3f;

        /// <summary>How much of his step has to lie along the road's own axis before the
        /// step counts as walking down it rather than over it. Half is sixty degrees -
        /// a diagonal cut down a street, which is the thing being stopped.</summary>
        const float RoadWalkAlong = 0.5f;

        /// <summary>Is this man on the last stretch to the door of his own seat? The door
        /// of a car standing at a kerb is out on the carriageway and there is no walking
        /// round to a handle, so the metres that reach it are not counted against him.</summary>
        static bool AtHisDoor(DemoCrews.Unit unit, CrewWalker man, Vector3 pos)
        {
            var car = unit != null ? unit.Boarding : null;
            if (car == null || car.Tf == null) return false;
            var door = car.SeatOf.TryGetValue(man, out int seat) ? car.DoorPoint(seat) : car.Position;
            var gap = door - pos;
            gap.y = 0f;
            return gap.sqrMagnitude <= DoorReach * DoorReach;
        }

        /// <summary>Metres of "getting in" - the same figure the walk itself stops routing
        /// at (DemoCrews.DoorRouteFrom).</summary>
        const float DoorReach = 20f;

        /// <summary>The way the road under this point runs, normalised - or zero when the
        /// point is not on a carriageway at all. The same reading CrewWalker.OnCarriageway
        /// makes, kept in one place so the audit and the walking cannot disagree about
        /// where the asphalt is.</summary>
        static Vector3 RoadAxisAt(Vector3 p)
        {
            var net = LaneNet.Active;
            if (net == null) return Vector3.zero;
            var road = net.Locate(p, out _, out float d, 10f);
            if (road == null || Mathf.Abs(d) >= road.HalfRoad) return Vector3.zero;
            var axis = road.Axis;
            axis.y = 0f;
            return axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.zero;
        }

        /// <summary>Seconds a man may keep shooting at a runner while a man of the
        /// same enemy crew still stands his ground in sight. TickCombat retargets
        /// every frame, so anything past a beat of it is the rule not holding.</summary>
        const float ChaseAfter = 2.5f;

        /// <summary>Ground pace below which a playing jog is a skate, and seconds of
        /// it before the fault is called. The gait gates drop a braked runner to the
        /// walk at RunRateMin x the clip's own pace (~2.7 m/s), so anything held a
        /// while under this is a gate not holding - the crossfade out of the jog and
        /// one man squeezing a car's flank both pass well inside the grace.</summary>
        const float SkatePace = 1.8f, SkateAfter = 1.5f;

        /// <summary>Seconds a man may stand on the pavement, with no order and nothing
        /// to shoot at, while HIS OWN CREW rides past in a car. A crew that drives off
        /// without one of its men (a job clicked while they were still climbing in) used
        /// to leave him there for the rest of the run: his walk to the door called off,
        /// his unit counted as riding, so no fight, no tether, no order of any kind.</summary>
        const float LeftBehindAfter = 10f;

        /// <summary>A moving crew may fan briefly while two men round a corner, but a
        /// sustained difference this wide means they are no longer taking the same
        /// line. The grace is longer than an ordinary steering correction and shorter
        /// than the time it takes the divergence to read as three unrelated walkers.</summary>
        const float FormationHeadingLimit = 50f, FormationHeadingAfter = 1.5f;

        /// <summary>The dealt formation fits inside eight metres even with all four
        /// tactical hoods. Ten metres therefore leaves room for a prop/corner funnel;
        /// held beyond it for four seconds is a crew that has come apart.</summary>
        const float FormationSpreadLimit = 10f, FormationSpreadAfter = 4f;

        /// <summary>Only actual ground made contributes a heading. At the harness's
        /// normal 50 ms step this is well below half a walking step, while still
        /// rejecting animation/root jitter from a man who is effectively stopped.</summary>
        const float FormationStepMin = 0.025f;

        /// <summary>A centre may brush a registered prop for a frame while steering.
        /// Twelve 50 ms frames with his centre inside it is penetration, not contact.
        /// The small probe matches WalkObstacles' own "already inside" test.</summary>
        const float PropProbeRadius = 0.1f, PropInsideAfter = 0.6f;

        /// <summary>A routed man must make at least this much recent ground before
        /// receiving a fresh stall grace. Lifetime travel remains separate so a
        /// circling walker still accumulates enough evidence for the orbit check.</summary>
        const float RouteStallTravel = 0.1f, RouteStallAfter = 1.5f;

        // -------------------------------------------------------------- the ledger

        class Watch
        {
            public Vector3 Last;
            public bool Seen, WasCarried;
            public float OffFor, StrayFor, LightFor, ZebraFor, ChaseFor, SkateFor, LeftFor, PropFor;
            public float RoadFor;
            /// <summary>The ground he made last frame, kept because w.Last is rolled
            /// forward before the later checks get to look at it.</summary>
            public Vector3 Step;
            public float PrevGap = float.MaxValue;
            public bool SaidOff;
            public bool RouteWatching, RouteStallSaid, RouteOrbitSaid, RouteOverlapSaid;
            public Vector3 RouteStart, RouteGoal, RouteLastDir;
            public float RouteStartGap, RouteFor, RouteRecentTravel, RouteTravel, RouteTurn;
        }

        sealed class FormationWatch
        {
            public float HeadingFor, SpreadFor;
        }

        static readonly Dictionary<CrewWalker, Watch> Men = new Dictionary<CrewWalker, Watch>();
        static readonly Dictionary<DemoCrews.Unit, float> FileFor = new Dictionary<DemoCrews.Unit, float>();
        static readonly Dictionary<DemoCrews.Unit, FormationWatch> Formations =
            new Dictionary<DemoCrews.Unit, FormationWatch>();
        struct FiredShot
        {
            public CrewWalker Man;
            public CrewWalker Mark;
        }

        static readonly List<FiredShot> FiredThisFrame = new List<FiredShot>();
        static readonly List<CrewWalker> Walkers = new List<CrewWalker>();
        static readonly List<CrewWalker> Stretch = new List<CrewWalker>();
        static readonly List<CrewWalker> Sweep = new List<CrewWalker>();
        static readonly List<Vector3> FormationSteps = new List<Vector3>();
        static readonly List<Vector3> FormationPositions = new List<Vector3>();
        static float _sweepIn = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            Men.Clear();
            FileFor.Clear();
            Formations.Clear();
            FiredThisFrame.Clear();
            _sweepIn = 5f;
        }

        // -------------------------------------------------------------- the frame

        public static void Tick(DemoCrews arena, float dt)
        {
            if (dt <= 0f) return;
            foreach (var unit in arena.Units)
            {
                TickMen(arena, unit, dt);
                TickFormation(unit, dt);
                TickStray(arena, unit, dt);
                TickLeftBehind(arena, unit, dt);
                TickFile(unit, dt);
            }
            if ((_sweepIn -= dt) <= 0f) { _sweepIn = 5f; SweepGone(); }
        }

        /// <summary>A round left this man's gun this frame (DemoCrews.OnFired). The
        /// judgement waits for LateTick - the arm is not posed for the frame until
        /// AimGun has run, and measured before it this would read the raw clip.</summary>
        public static void ShotFired(CrewWalker man)
        {
            if (man != null)
                FiredThisFrame.Add(new FiredShot { Man = man, Mark = man.Target });
        }

        /// <summary>After the frame's arms are posed: every shot fired this frame
        /// must have had its barrel on the mark, near enough.</summary>
        public static void LateTick()
        {
            for (int i = 0; i < FiredThisFrame.Count; i++)
            {
                var fired = FiredThisFrame[i];
                var man = fired.Man;
                if (man == null || man.Tf == null || man.Riding) continue;
                // A lethal round can make TickCombat select the next enemy before
                // LateUpdate poses and audits the arms. Judge the mark captured when
                // this round left, never the next living target inherited afterward.
                var mark = fired.Mark;

                // A ROUND AT NOBODY IS ITS OWN FAULT, and this rule used to be blind to
                // it: "no mark" was folded in with "the mark went down to this very
                // round" and both were waved through. They are not the same thing. A
                // man who drops his target with the round he is firing aimed fine; a man
                // firing with no target at all is firing at the scenery, and the whole
                // class of bug the player keeps reporting - a man who walks away from a
                // finished fight still letting rounds off into the pavement - lives in
                // exactly the gap this let through.
                if (mark == null)
                {
                    Fault(man, "noaim", "fired with no target (" + man.State + ")");
                    continue;
                }
                if (mark.Dead || !mark.Tf) continue;   // dropped by this very round: the aim was good enough

                // AND A ROUND AT A RUN. The authored weapon wardrobes own directional
                // walking takes specifically so a man can keep the sights on his mark
                // during the last hurried strides. A run still throws the gun arm and
                // must never fire. JoggingPose includes both the legacy run and the
                // dynamic weapon-running slots, so this watches the rule rather than
                // rejecting the valid walk-and-aim blend.
                if (man.JoggingPose)
                    Fault(man, "firewalk", "fired while running (" + man.State + ")");

                var to = mark.ChestPosition - man.MuzzlePosition;
                if (to.magnitude < 2f) continue;                       // muzzle at his chest: the angle means nothing
                float off = Vector3.Angle(man.MuzzleForward, to);
                if (off > AimOffLimit)
                    Fault(man, "aimlow",
                        $"fired {off:F0} deg off {mark.DisplayName}, {to.magnitude:F1} m out");
            }
            FiredThisFrame.Clear();
        }

        // -------------------------------------------------------------- per man

        static void TickMen(DemoCrews arena, DemoCrews.Unit unit, float dt)
        {
            foreach (var man in unit.All())
            {
                if (man == null || man.Tf == null) continue;
                if (!Men.TryGetValue(man, out var w)) Men[man] = w = new Watch();

                bool afoot = !man.Dead && man.Tf.gameObject.activeSelf &&
                             !man.Riding && !arena.IsAboard(man);
                var pos = man.Tf.position;

                // THE SNAP. A man on foot moves by steps; a frame that carries him
                // further than the fastest step is a teleport, which is what the
                // player calls a respawn. The frame he is put down out of a seat is
                // the seat's business and excused.
                if (afoot && w.Seen && !w.WasCarried)
                {
                    var moved = pos - w.Last;
                    moved.y = 0f;
                    float bound = Mathf.Max(TeleportBound, 6f * dt + 0.6f);
                    if (moved.magnitude > bound)
                        Fault(man, "teleport", $"{moved.magnitude:F2} m in one frame ({man.State})");

                    // THE SKATE. A man whose legs play the jog covers jog ground -
                    // the gait gates themselves (GearGraphWalk, TickStride) drop him
                    // to the walk under the band, so a jog held over walking pace
                    // for a sustained spell means a gate is not holding.
                    if (man.JoggingPose && dt > 1e-4f && moved.magnitude / dt < SkatePace)
                    {
                        w.SkateFor += dt;
                        if (w.SkateFor > SkateAfter)
                        {
                            Fault(man, "skate", $"jogging at {moved.magnitude / dt:F1} m/s " +
                                                $"for {w.SkateFor:F1}s ({man.State})");
                            w.SkateFor = -30f;   // still watched; said again if it goes on
                        }
                    }
                    else w.SkateFor = 0f;
                }
                w.Step = afoot && w.Seen && !w.WasCarried ? pos - w.Last : Vector3.zero;
                w.Step.y = 0f;
                w.Last = pos;
                w.Seen = true;
                w.WasCarried = !afoot;
                if (!afoot)
                {
                    w.OffFor = 0f; w.SaidOff = false;
                    w.LightFor = 0f; w.ZebraFor = 0f; w.ChaseFor = 0f; w.RoadFor = 0f;
                    w.PropFor = 0f;
                    continue;
                }

                // THE FLOOR. The fence (WalkObstacles.City) is enforced at every
                // level; a man out on the void means one of those levels gave.
                if (WalkObstacles.City.Count > 0)
                {
                    if (!WalkObstacles.InCity(pos))
                    {
                        w.OffFor += dt;
                        if (w.OffFor > OffCityAfter && !w.SaidOff)
                        {
                            w.SaidOff = true;
                            Fault(man, "offcity", $"off the floor for {w.OffFor:F1}s ({man.State})");
                        }
                    }
                    else { w.OffFor = 0f; w.SaidOff = false; }
                }

                // THE PROP. Standing() is the fixed walking map: solids and every
                // registered furniture plan, but no traffic. WallAt() removes the
                // solids again, leaving exactly registered props. A tenth-metre probe
                // asks whether his centre is inside one rather than whether his normal
                // shoulder radius is merely touching it.
                bool insideProp = WalkObstacles.Standing(pos, PropProbeRadius) &&
                                  !WalkObstacles.WallAt(pos, PropProbeRadius);
                if (AdvanceSustained(ref w.PropFor, insideProp, dt, PropInsideAfter))
                {
                    Fault(man, "proppenetration",
                        $"inside a fixed prop for {w.PropFor:F1}s ({man.State})");
                    w.PropFor = -20f;
                }

                TickRoutedMotion(man, w, pos, dt);

                // THE LIGHT. Waiting is legal; waiting longer than any light holds
                // is not - and the tether crosses a straggler long before this.
                if (man.AtLight && man.State == CrewWalker.Mode.Walking)
                {
                    w.LightFor += dt;
                    if (w.LightFor > LightAfter)
                    {
                        Fault(man, "zebrastuck", $"waiting at a light for {w.LightFor:F0}s");
                        w.LightFor = -30f;   // still watched; said again if it goes on
                    }
                }
                else w.LightFor = 0f;

                // THE ZEBRA. Nobody's order ends on a crossing, so a man STOOD on
                // one finished an order nowhere near where it should have ended.
                // His link may be stale (a stride order leaves it behind), so his
                // feet have to actually be on the crossing - and ON THE ASPHALT, by
                // the same predicate the healer uses (a man at the zebra's kerb
                // mouth is on the pavement and fine; measuring him by the link line
                // alone flagged men the healer rightly left alone).
                if (man.State == CrewWalker.Mode.Standing && man.OnGraph &&
                    man.CurrentLink.Gated && NearLink(pos, man.CurrentLink, 2f) &&
                    CrewWalker.OnCarriageway(pos))
                {
                    w.ZebraFor += dt;
                    if (w.ZebraFor > ZebraStandAfter)
                    {
                        Fault(man, "zebrastuck", $"stood on a crossing for {w.ZebraFor:F0}s");
                        w.ZebraFor = -30f;
                    }
                }
                else w.ZebraFor = 0f;

                // THE ROAD IS FOR THE CARS. A crew walks the pavements and crosses at
                // the crossings; the carriageway is a thing to get OVER, not a thing to
                // walk down. A man who does walk down it stands in the traffic's way for
                // as long as his errand lasts, and the town then has to pick its way
                // round him - which is the scene the pavement march exists to stop
                // (DemoCrews.MarchTo).
                //
                // A FIGHT IS EXEMPT, and deliberately so: closing on a mark, running a
                // chase down, or breaking and running are all straight lines by design
                // and a man who stopped at the kerb to do any of them would be absurd.
                // A FIGHT IS EXEMPT, and so is the handle of your own car: a door at a
                // kerb stands ON the asphalt, so the last few metres to it are a man
                // getting in, not a man walking down the road. Only the last few - the
                // WALK to a car parked across the quarter is routed like any other
                // (DemoCrews.SendToDoor) and is judged like any other.
                bool fighting = man.Target != null || (unit != null && unit.TargetUnit != null) ||
                                man.Panicked || man.Retreating || AtHisDoor(unit, man, pos) ||
                                man.FallingIn;
                var roadAxis = fighting ? Vector3.zero : RoadAxisAt(pos);
                if (roadAxis.sqrMagnitude > 1e-6f)
                {
                    // only ground made ALONG the road counts; a man crossing makes none
                    if (w.Step.sqrMagnitude > 1e-6f &&
                        Mathf.Abs(Vector3.Dot(w.Step.normalized, roadAxis)) > RoadWalkAlong)
                    {
                        w.RoadFor += dt;
                        if (w.RoadFor > RoadWalkAfter)
                        {
                            Fault(man, "roadwalk",
                                $"walking down the carriageway for {w.RoadFor:F1}s ({man.State})");
                            w.RoadFor = -30f;   // still watched; said again if it goes on
                        }
                    }
                }
                else w.RoadFor = 0f;

                // THE ROUT. A man shooting at a runner while one of the same crew
                // still stands and fights in his sight has the wrong mark -
                // TickCombat swaps it the same frame, so any run of this is a fault.
                var tgt = man.Target;
                if (tgt != null && !tgt.Dead && (tgt.Panicked || tgt.Retreating) &&
                    unit.TargetUnit != null && FighterInSight(unit.TargetUnit, pos))
                {
                    w.ChaseFor += dt;
                    if (w.ChaseFor > ChaseAfter)
                    {
                        Fault(man, "runnerchase",
                            $"on a runner ({tgt.DisplayName}) with a fighter in sight for {w.ChaseFor:F1}s");
                        w.ChaseFor = -20f;
                    }
                }
                else w.ChaseFor = 0f;
            }
        }

        /// <summary>A route can be broken while the man is moving: the old stall
        /// clock saw distance covered and therefore passed an orbit. Judge useful
        /// progress to the route's terminal errand (stable across A* corner replans), and keep
        /// fixed overlap on the same footprint the planner and feet share.</summary>
        static void TickRoutedMotion(CrewWalker man, Watch w, Vector3 pos, float dt)
        {
            if (!man.TryRoutedStrideIntent(out var goal))
            {
                ResetRouteWatch(w);
                return;
            }

            goal.y = pos.y;
            var toGoal = goal - pos;
            toGoal.y = 0f;
            float gap = toGoal.magnitude;
            var goalShift = goal - w.RouteGoal;
            goalShift.y = 0f;
            // A live combat mark can shuffle while the route remains the same errand.
            // Only a material four-metre move starts a new watch; one-to-two metre
            // replans must not launder a stall/orbit into a fresh clean window.
            if (!w.RouteWatching || goalShift.sqrMagnitude > 4f * 4f)
                BeginRouteWindow(w, pos, goal, gap);

            float step = w.Step.magnitude;
            if (step > 0.002f)
            {
                var dir = w.Step / step;
                if (w.RouteLastDir.sqrMagnitude > 0.5f)
                    w.RouteTurn += Vector3.Angle(w.RouteLastDir, dir);
                w.RouteLastDir = dir;
                w.RouteTravel += step;
            }
            w.RouteGoal = goal;
            bool stalled = AdvanceRouteStall(
                ref w.RouteFor, ref w.RouteRecentTravel,
                step > 0.002f ? step : 0f, dt);

            float gain = w.RouteStartGap - gap;
            if (gain >= 0.4f)
            {
                BeginRouteWindow(w, pos, goal, gap);
                return;
            }

            if (!w.RouteStallSaid && stalled)
            {
                Fault(man, "routestall",
                    $"no route progress for {w.RouteFor:F1}s, {gap:F1} m from terminal ({man.State})");
                w.RouteStallSaid = true;
            }

            var net = pos - w.RouteStart;
            net.y = 0f;
            if (!w.RouteOrbitSaid && RouteOrbitModel(
                    w.RouteTravel, w.RouteTurn, net.magnitude))
            {
                Fault(man, "routeorbit",
                    $"walked {w.RouteTravel:F1} m / turned {w.RouteTurn:F0} deg " +
                    $"without gaining on terminal {gap:F1} m away ({man.State})");
                w.RouteOrbitSaid = true;
            }

            float overlapRadius = Mathf.Max(0.01f, WalkRoute.ClearanceRadius - 0.01f);
            if (!w.RouteOverlapSaid && WalkObstacles.Standing(pos, overlapRadius))
            {
                Fault(man, "routeoverlap",
                    $"routed footprint overlaps fixed geometry ({man.State})");
                w.RouteOverlapSaid = true;
            }
        }

        /// <summary>A loop must lose most of the ground it paid for. Summed steering
        /// angles alone are not enough: a clean thirty-metre chase can accumulate a
        /// full turn from tiny left/right corrections while its live mark outruns it.
        /// A real prop orbit either returns near its start or has poor net efficiency.</summary>
        internal static bool RouteOrbitModel(float travel, float turn, float net)
        {
            if (travel < 2.5f) return false;
            if (net <= 0.75f) return true;
            return turn >= 330f && net <= travel * 0.65f;
        }

        static void BeginRouteWindow(Watch w, Vector3 pos, Vector3 goal, float gap)
        {
            w.RouteWatching = true;
            w.RouteStart = pos;
            w.RouteGoal = goal;
            w.RouteStartGap = gap;
            w.RouteFor = 0f;
            w.RouteRecentTravel = 0f;
            w.RouteTravel = 0f;
            w.RouteTurn = 0f;
            w.RouteLastDir = Vector3.zero;
            w.RouteStallSaid = false;
            w.RouteOrbitSaid = false;
            w.RouteOverlapSaid = false;
        }

        static void ResetRouteWatch(Watch w)
        {
            w.RouteWatching = false;
            w.RouteFor = 0f;
            w.RouteRecentTravel = 0f;
            w.RouteTravel = 0f;
            w.RouteTurn = 0f;
            w.RouteLastDir = Vector3.zero;
            w.RouteStallSaid = false;
            w.RouteOrbitSaid = false;
            w.RouteOverlapSaid = false;
        }

        /// <summary>Advance the rolling no-movement window without erasing lifetime
        /// route travel used by the independent orbit detector.</summary>
        internal static bool AdvanceRouteStall(ref float elapsed,
            ref float recentTravel, float step, float dt)
        {
            recentTravel += Mathf.Max(0f, step);
            elapsed += Mathf.Max(0f, dt);
            if (recentTravel >= RouteStallTravel)
            {
                recentTravel = 0f;
                elapsed = 0f;
                return false;
            }
            return elapsed >= RouteStallAfter;
        }

        static bool FighterInSight(DemoCrews.Unit enemy, Vector3 from)
        {
            foreach (var m in enemy.All())
            {
                if (m == null || m.Dead || !m.Tf || m.Panicked || m.Retreating) continue;
                if ((m.Tf.position - from).sqrMagnitude <= DemoCrews.SightRange * DemoCrews.SightRange)
                    return true;
            }
            return false;
        }

        // -------------------------------------------------------------- formation

        /// <summary>A crew on one open-ground order makes one visible body. Measure
        /// only simultaneous Striding men: fights, boarding, panic and a hood merely
        /// falling in have their own rules and are deliberately outside this one.</summary>
        static void TickFormation(DemoCrews.Unit unit, float dt)
        {
            if (unit == null || unit.Wiped || unit.TargetUnit != null || unit.Boarding != null)
            {
                ResetFormation(unit);
                return;
            }

            FormationSteps.Clear();
            FormationPositions.Clear();
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null || man.Riding ||
                    man.Panicked || man.Retreating || man.FallingIn ||
                    man.State != CrewWalker.Mode.Striding)
                    continue;
                FormationPositions.Add(man.Tf.position);
                if (Men.TryGetValue(man, out var watch) &&
                    watch.Step.sqrMagnitude >= FormationStepMin * FormationStepMin)
                    FormationSteps.Add(watch.Step);
            }

            if (FormationPositions.Count < 2)
            {
                ResetFormation(unit);
                return;
            }

            if (!Formations.TryGetValue(unit, out var formed))
                Formations[unit] = formed = new FormationWatch();
            var lead = unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null
                ? unit.Boss : FirstStanding(unit);

            float heading = FormationHeadingSpread(FormationSteps, FormationStepMin);
            bool headingApart = FormationSteps.Count >= 2 && heading > FormationHeadingLimit;
            if (AdvanceSustained(ref formed.HeadingFor, headingApart, dt, FormationHeadingAfter))
            {
                if (lead != null)
                    Fault(lead, "formationheading",
                        $"{FormationSteps.Count} moving men differed by {heading:F0} deg " +
                        $"for {formed.HeadingFor:F1}s");
                formed.HeadingFor = -20f;
            }

            float spread = FormationPositionSpread(FormationPositions);
            if (AdvanceSustained(ref formed.SpreadFor, spread > FormationSpreadLimit,
                                 dt, FormationSpreadAfter))
            {
                if (lead != null)
                    Fault(lead, "formationspread",
                        $"{FormationPositions.Count} striding men spread {spread:F1} m " +
                        $"for {formed.SpreadFor:F1}s");
                formed.SpreadFor = -20f;
            }
        }

        static void ResetFormation(DemoCrews.Unit unit)
        {
            if (unit == null || !Formations.TryGetValue(unit, out var formed)) return;
            formed.HeadingFor = 0f;
            formed.SpreadFor = 0f;
        }

        /// <summary>Largest pairwise ground-heading difference, ignoring steps too
        /// small to establish a direction. Pure so the contract can run outside Play.</summary>
        internal static float FormationHeadingSpread(IList<Vector3> steps, float minimumStep)
        {
            if (steps == null || steps.Count < 2) return 0f;
            float minimum = Mathf.Max(0f, minimumStep);
            float minimum2 = minimum * minimum;
            float widest = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                var a = steps[i];
                a.y = 0f;
                if (a.sqrMagnitude < minimum2) continue;
                for (int j = i + 1; j < steps.Count; j++)
                {
                    var b = steps[j];
                    b.y = 0f;
                    if (b.sqrMagnitude < minimum2) continue;
                    widest = Mathf.Max(widest, Vector3.Angle(a, b));
                }
            }
            return widest;
        }

        /// <summary>Largest pairwise ground separation. Pure counterpart of the live
        /// formation-spread measurement.</summary>
        internal static float FormationPositionSpread(IList<Vector3> positions)
        {
            if (positions == null || positions.Count < 2) return 0f;
            float widest2 = 0f;
            for (int i = 0; i < positions.Count; i++)
            for (int j = i + 1; j < positions.Count; j++)
            {
                var gap = positions[i] - positions[j];
                gap.y = 0f;
                widest2 = Mathf.Max(widest2, gap.sqrMagnitude);
            }
            return Mathf.Sqrt(widest2);
        }

        /// <summary>One sustained-condition clock. Clearing the condition clears the
        /// clock; a negative value deliberately acts as the existing audit cooldown.</summary>
        internal static bool AdvanceSustained(ref float held, bool breached, float dt, float grace)
        {
            if (!breached)
            {
                held = 0f;
                return false;
            }
            held += Mathf.Max(0f, dt);
            return held > Mathf.Max(0f, grace);
        }

        // -------------------------------------------------------------- the crew

        // A hood far off his crew for long: the tether's whole job is that this
        // never accumulates. Fights, boardings, panics and the law are all theirs
        // to be spread out in and excused.
        static void TickStray(DemoCrews arena, DemoCrews.Unit unit, float dt)
        {
            if (unit.Wiped || unit.IsPolice || unit.TargetUnit != null || unit.Boarding != null) return;
            var lead = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : FirstStanding(unit);
            if (lead == null || lead.Tf == null || lead.Panicked || lead.Riding || arena.IsAboard(lead)) return;
            foreach (var man in unit.Hoods)
            {
                if (man == null || man == lead || man.Dead || man.Tf == null) continue;
                if (man.Panicked || man.Retreating || man.Riding || arena.IsAboard(man)) continue;
                if (!Men.TryGetValue(man, out var w)) continue;   // TickMen has not met him yet
                var gap = man.Tf.position - lead.Tf.position;
                gap.y = 0f;
                float d = gap.magnitude;
                // a man CLOSING the gap is the tether doing its work - clipped off
                // by a light, cut across after the crew, on his way back - and a man
                // the arena is actively REINING (dawdled, lingered) is managed, not
                // lost. So is a man whose ORDER aims at the crew: a post-fight
                // regroup threads city blocks, and around a corner the bare gap
                // grows for a spell while the walk is exactly right (four brawl
                // seeds failed on that alone). Only a gap held or growing on a man
                // nobody is handling is a stray.
                bool closing = d < w.PrevGap - 0.02f;
                w.PrevGap = d;
                var aim = man.Destination - lead.Tf.position;
                aim.y = 0f;
                bool coming = man.HasOrder && aim.sqrMagnitude < 15f * 15f;
                // ...and a man on the SAME ERRAND as his lead - both orders aimed at
                // one far place (a war march re-dealt to the whole crew) - is
                // coordinated, however stretched the column runs mid-march; the
                // holds and hustles are what keep it tight, and the mission's own
                // stall watchdogs catch a march that never arrives
                if (!coming && man.HasOrder && lead.HasOrder)
                {
                    var errand = man.Destination - lead.Destination;
                    errand.y = 0f;
                    coming = errand.sqrMagnitude < 20f * 20f;
                }
                if (d > StrayGap && !closing && !man.ReinedIn && !coming)
                {
                    w.StrayFor += dt;
                    if (w.StrayFor > StrayAfter)
                    {
                        Fault(man, "strayman",
                            $"{d:F0} m off {lead.DisplayName} for {w.StrayFor:F0}s ({man.State})");
                        w.StrayFor = -20f;
                    }
                }
                else if (d <= StrayGap) w.StrayFor = 0f;
            }
        }

        // A crew that is RIDING and has men on the pavement: each of them wants an
        // order of his own - a fight, a walk, a seat still to reach. Standing there
        // with none is the fault this watches for.
        static void TickLeftBehind(DemoCrews arena, DemoCrews.Unit unit, float dt)
        {
            // the law rides to its own orders - a dismounted officer is PoliceDispatch's
            if (unit.Wiped || unit.IsPolice) return;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                if (!Men.TryGetValue(man, out var w)) continue;   // TickMen has not met him yet
                bool adrift = unit.Car != null && unit.Boarding == null &&
                              !arena.IsAboard(man) && !man.Riding &&
                              !man.Panicked && !man.Retreating &&
                              !man.HasOrder && man.Target == null;
                if (!adrift) { w.LeftFor = 0f; continue; }
                w.LeftFor += dt;
                if (w.LeftFor > LeftBehindAfter)
                {
                    Fault(man, "leftbehind",
                        $"stood on the pavement for {w.LeftFor:F0}s while his crew rides ({man.State})");
                    w.LeftFor = -20f;
                }
            }
        }

        static CrewWalker FirstStanding(DemoCrews.Unit unit)
        {
            foreach (var m in unit.All())
                if (m != null && !m.Dead && m.Tf != null) return m;
            return null;
        }

        // Three or more of one crew walking the same stretch pressed onto one line:
        // a queue. The spread is measured ACROSS the stretch, over the men on it.
        static void TickFile(DemoCrews.Unit unit, float dt)
        {
            Walkers.Clear();
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null && man.OnGraph && !man.AtLight &&
                    !man.ReinedIn &&   // held level with his crew: stood, not walking any line
                    (man.State == CrewWalker.Mode.Walking || man.State == CrewWalker.Mode.Homing))
                    Walkers.Add(man);

            bool filed = false;
            if (Walkers.Count >= 3)
            {
                for (int i = 0; i < Walkers.Count && !filed; i++)
                {
                    Stretch.Clear();
                    var a = Walkers[i].CurrentLink;
                    for (int j = 0; j < Walkers.Count; j++)
                    {
                        var b = Walkers[j].CurrentLink;
                        if (SameStretch(a, b)) Stretch.Add(Walkers[j]);
                    }
                    if (Stretch.Count < 3) continue;
                    var dir = a.To.Pos - a.From.Pos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    // a stretch that only OFFERS one line is not a crew walking
                    // single file - ask the pavement's own clearance: when the two
                    // flank lanes both come back to the same free line, the
                    // furniture forced the file and the men had no width to hold
                    float tMid = Walkers[i].CurrentT;
                    if (Mathf.Abs(a.FreeLine(tMid, 2f, -1.2f) - a.FreeLine(tMid, 2f, 1.2f)) < FileSpread)
                        continue;
                    var right = new Vector3(dir.z, 0f, -dir.x).normalized;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int j = 0; j < Stretch.Count; j++)
                    {
                        float s = Vector3.Dot(Stretch[j].Tf.position, right);
                        lo = Mathf.Min(lo, s);
                        hi = Mathf.Max(hi, s);
                    }
                    filed = hi - lo < FileSpread;
                }
            }

            FileFor.TryGetValue(unit, out float held);
            held = filed ? held + dt : 0f;
            if (held > FileAfter)
            {
                var front = unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null
                    ? unit.Boss : FirstStanding(unit);
                if (front != null)
                    Fault(front, "singlefile",
                        $"{Stretch.Count} of the crew walking one line for {held:F0}s");
                held = -15f;
            }
            FileFor[unit] = held;
        }

        static bool SameStretch(PedLink a, PedLink b)
        {
            if (a == null || b == null) return false;
            return (a.From == b.From && a.To == b.To) || (a.From == b.To && a.To == b.From);
        }

        // -------------------------------------------------------------- plumbing

        static bool NearLink(Vector3 p, PedLink link, float within)
        {
            var ab = link.To.Pos - link.From.Pos;
            ab.y = 0f;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            var rel = p - link.From.Pos;
            rel.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(rel, ab) / len2);
            var q = link.From.Pos + ab * t;
            q.y = p.y;
            return (p - q).sqrMagnitude < within * within;
        }

        static void SweepGone()
        {
            Sweep.Clear();
            foreach (var kv in Men)
                if (kv.Key == null || kv.Key.Tf == null) Sweep.Add(kv.Key);
            for (int i = 0; i < Sweep.Count; i++) Men.Remove(Sweep[i]);
        }

        static void Fault(CrewWalker man, string kind, string what)
        {
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", man.Id);
            DriveTrace.Str(sb, "tag", "crew");
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "who", man.DisplayName);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Vec(sb, "p", man.Tf.position);
            DriveTrace.Row("fault", sb.ToString());
        }
    }
}
