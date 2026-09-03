using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The stride and the leg: how a man on open ground steps at a spot,
    /// round whatever is in the way (WalkObstacles, the crowd, the city's hem), and
    /// when he gives a leg up as done. Moved out of CrewWalker.cs whole; nothing
    /// changed in the move.</summary>
    public partial class CrewWalker
    {
        /// <summary>Metres looked down the line for what is in the way - a car's
        /// length, near enough: he leans off it early and passes it in one curve.</summary>
        const float Lookahead = 3f;

        int _steerSide;      // which way round the last thing in his way he went (WalkObstacles)
        int _preferredSteerSide; // a shared crew route's stable first choice, even between obstacles
        Vector3 _strideDir;  // the line he stepped along last frame; zero at the start of a leg
        int _strideMoveFrame = -1000; // last frame on which that line actually carried him
        float _blockedFor;   // seconds stood on this leg with nowhere to step
        bool _detouring;     // this frame's step was off the line to the spot, round something
        bool _strideJog;     // was he jogging the stride last frame (the gait's own hysteresis)
        int _routedStrideFrame = -1000;
        Vector3 _routedStrideGoal;
        bool _routedStrideRequested;

        /// <summary>The route audit's read-only view of this frame's actual stride
        /// intent. A timestamp, rather than sticky state, keeps standing/aiming frames
        /// from inheriting yesterday's waypoint.</summary>
        internal bool TryRoutedStrideIntent(out Vector3 goal)
        {
            goal = _routedStrideGoal;
            return _routedStrideFrame == Time.frameCount && _routedStrideRequested;
        }

        /// <summary>Publish the terminal errand, not the current A* corner. Combat can
        /// redraw a corner every second while making no useful progress; an audit which
        /// follows that corner would begin a fresh clean window on every redraw.</summary>
        void NoteRoutedStrideIntent(Vector3 terminalGoal, bool requested)
        {
            _routedStrideFrame = Time.frameCount;
            _routedStrideGoal = terminalGoal;
            _routedStrideRequested = requested;
        }

        // A combat approach is a route too. Local steering is for the moving last
        // metre around a person or car; asked to solve a whole furnished street it
        // can choose a side of one prop and immediately be contradicted by the next,
        // which is the orbit/stall the player sees. WalkRoute owns the static part and
        // TickStride still owns every actual step along its handful of taut corners.
        readonly List<Vector3> _combatWay = new List<Vector3>();
        int _combatWayAt;
        int _combatWayVersion = -1;
        Vector3 _combatWayTarget;
        Vector3 _combatWayEnd;
        bool _combatWayEndsAtTarget;
        float _combatReplanAt;
        float _combatPlanTraceAt;
        float _combatBestGap = float.MaxValue;
        float _combatNoProgress;

        const float CombatCornerReach = 0.35f;
        const float CombatProgressGain = 0.15f;
        const float CombatProgressGrace = 1.25f;
        // A route remains authoritative, but its local car/prop pass may need one
        // exactly perpendicular step at a tangent. A tiny negative epsilon admits the
        // 90-degree entry in WalkObstacles' angle table despite cosine rounding while
        // still rejecting the next 110-degree (genuinely backwards) fallback.
        const float RoutedForwardDot = -0.001f;

        internal static bool RoutedHeadingAllowedModel(float forwardDot) =>
            forwardDot >= RoutedForwardDot;

        void ClearCombatWay()
        {
            _combatWay.Clear();
            _combatWayAt = 0;
            _combatWayVersion = -1;
            _combatWayEndsAtTarget = false;
            _combatReplanAt = 0f;
            _combatPlanTraceAt = 0f;
            _combatBestGap = float.MaxValue;
            _combatNoProgress = 0f;
        }

        void BeginCombatCorner()
        {
            _combatBestGap = float.MaxValue;
            _combatNoProgress = 0f;
            _blockedFor = 0f;
            _steerSide = 0;
            // `going` is a useful anti-U-turn hint while passing one obstacle, but the
            // previous corner's heading is not a vote against the new route segment.
            // Keeping it made the local steer carry on past a corner, then orbit back.
            _strideDir = Vector3.zero;
        }

        /// <summary>A moving man can still be stuck: orbiting a point leaves
        /// _blockedFor at zero. Judge useful progress against the best distance reached
        /// on this corner and force a fresh route if he only circles around it.</summary>
        internal static bool CombatCornerStalledModel(float gap, float dt,
            ref float bestGap, ref float noProgress)
        {
            if (gap < bestGap - CombatProgressGain)
            {
                bestGap = gap;
                noProgress = 0f;
                return false;
            }
            noProgress += Mathf.Max(0f, dt);
            return noProgress >= CombatProgressGrace;
        }

        /// <summary>A later route corner may be taken only when the exact chord from
        /// the man's current feet is proven clear. Near a prop tangent, being inside a
        /// waypoint's broad arrival circle does not mean he has crossed to the safe
        /// side of it; advancing on distance alone can leave the next chord inside the
        /// prop on every replan.</summary>
        internal static bool CombatCornerCanAdvanceModel(float gap, bool nextChordClear) =>
            nextChordClear;

        /// <summary>A required tangent is reached exactly. Leaving even a centimetre
        /// before it can leave the following chord on the blocked side forever; the
        /// stride still pays that last distance at its ordinary animated pace.</summary>
        internal static float CombatCornerStopModel(bool last, bool endsAtTarget,
            float terminalStop) =>
            last && endsAtTarget ? terminalStop : last ? CombatCornerReach : 0f;

        /// <summary>A cover approach must be able to observe the same failure which
        /// forces the combat path to redraw. Resetting corner bookkeeping must not
        /// hide that result from its caller.</summary>
        internal static bool CombatRouteFailedModel(float blockedFor, bool circling) =>
            blockedFor > 0.8f || circling;

        bool RecoverCombatOverlap(Vector3 toward)
        {
            if (!WalkObstacles.Standing(
                    Tf.position, WalkObstacles.OverlapProbeRadius))
                return false;
            var from = Tf.position;
            if (!WalkObstacles.TryClearStandingSpot(
                    from, WalkRoute.ClearanceRadius, toward, out var free, 2.5f))
            {
                ClearCombatWay();
                return true;
            }
            free.y = from.y;
            Tf.position = free;
            ClearCombatWay();
            if (DriveTrace.On)
                DriveTrace.Event("walk", DisplayName,
                    $"recovered {Vector3.Distance(from, free):F1} m from fixed geometry during combat");
            return true;
        }

        /// <summary>A route can reject the 22.5 cm travel footprint after a tangent
        /// step left it only brushing a prop. It can also leave him radius-clear in a
        /// corner pocket with no visible lattice centre. Repair either state only after
        /// planning has actually failed, and only over a proved centre-clear chord to a
        /// point which can join the route lattice.</summary>
        bool TryRecoverRouteStart(Vector3 toward, string context)
        {
            var from = Tf.position;
            if (!WalkObstacles.TryClearRouteStart(from, WalkRoute.ClearanceRadius,
                    toward, out var free, 2.5f,
                    candidate => WalkRoute.CanAnchor(candidate))) return false;
            free.y = from.y;
            Tf.position = free;
            if (DriveTrace.On)
                DriveTrace.Event("walk", DisplayName,
                    $"recovered route start {Vector3.Distance(from, free):F2} m ({context})");
            return true;
        }

        static readonly float[] CombatEnvelopeAngles =
        {
            0f, 22.5f, -22.5f, 45f, -45f, 67.5f, -67.5f, 90f,
            -90f, 112.5f, -112.5f, 135f, -135f, 157.5f, -157.5f, 180f,
        };

        /// <summary>An enemy's exact feet can legally be tight against cover. A shooter
        /// needs a reachable point inside his firing envelope, not permission to occupy
        /// those same feet. Prefer the near-side edge, require wall sight to the mark,
        /// and let the ordinary route planner prove every candidate.</summary>
        bool TryPlanCombatEnvelope(Vector3 target, float stopWithin,
            out Vector3 endpoint)
        {
            endpoint = target;
            float outer = stopWithin - 0.15f;
            if (outer <= WalkRoute.ClearanceRadius + 0.05f) return false;

            var approach = Tf.position - target;
            approach.y = 0f;
            if (approach.sqrMagnitude < 1e-5f) approach = Vector3.forward;
            else approach.Normalize();

            float inner = Mathf.Min(outer,
                Mathf.Max(WalkRoute.ClearanceRadius + 0.1f, outer * 0.45f));
            for (int band = 0; band < 3; band++)
            {
                float ring = Mathf.Lerp(outer, inner, band * 0.5f);
                for (int i = 0; i < CombatEnvelopeAngles.Length; i++)
                {
                    var dir = Quaternion.AngleAxis(
                        CombatEnvelopeAngles[i], Vector3.up) * approach;
                    var candidate = target + dir * ring;
                    candidate.y = Tf.position.y;
                    if (!WalkObstacles.InCity(candidate) ||
                        WalkObstacles.Standing(candidate, WalkRoute.ClearanceRadius) ||
                        !WalkObstacles.Sees(candidate, target)) continue;
                    if (!WalkRoute.Plan(Tf.position, candidate, _combatWay, false) ||
                        _combatWay.Count == 0) continue;
                    endpoint = candidate;
                    return true;
                }
            }
            _combatWay.Clear();
            return false;
        }

        bool PlanCombatWay(Vector3 target, float stopWithin, bool attackEnvelope)
        {
            _combatWay.Clear();
            _combatWayAt = 0;
            _combatWayTarget = target;
            _combatWayEnd = target;
            _combatWayEndsAtTarget = false;
            _combatWayVersion = WalkObstacles.Version;
            _combatReplanAt = Time.time + 0.4f;

            bool exact = WalkRoute.Plan(Tf.position, target, _combatWay, false) &&
                         _combatWay.Count > 0;
            // Only a failed route earns a relocation. This closes the 10-22.5 cm
            // clearance dead-zone without making every ordinary wall-brush teleport.
            if (!exact && TryRecoverRouteStart(target, "combat plan"))
                exact = WalkRoute.Plan(Tf.position, target, _combatWay, false) &&
                        _combatWay.Count > 0;

            if (exact)
                _combatWayEndsAtTarget = true;
            else if (!attackEnvelope ||
                     !TryPlanCombatEnvelope(target, stopWithin, out _combatWayEnd))
            {
                if (DriveTrace.On && Time.time >= _combatPlanTraceAt)
                {
                    _combatPlanTraceAt = Time.time + 2f;
                    DriveTrace.Event("walk", DisplayName,
                        "combat plan failed " +
                        $"startAnchor={WalkRoute.CanAnchor(Tf.position)} " +
                        $"targetAnchor={WalkRoute.CanAnchor(target)} " +
                        $"startBlocked={WalkObstacles.Standing(Tf.position, WalkRoute.ClearanceRadius)} " +
                        $"targetBlocked={WalkObstacles.Standing(target, WalkRoute.ClearanceRadius)} " +
                        $"envelope={attackEnvelope}");
                }
                _combatWay.Clear();
                return false;
            }
            // The planner may retain a near start anchor. Skip it only when the chord
            // to the following point is proven from the ACTUAL feet. A 35 cm proximity
            // test can put those feet on the wrong side of a prop tangent and make
            // every later replan repeat the same blocked second corner.
            while (_combatWayAt < _combatWay.Count - 1)
            {
                var gap = _combatWay[_combatWayAt] - Tf.position;
                gap.y = 0f;
                bool nextClear = StaticChordClear(
                    Tf.position, _combatWay[_combatWayAt + 1]);
                if (!CombatCornerCanAdvanceModel(gap.magnitude, nextClear)) break;
                _combatWayAt++;
            }
            BeginCombatCorner();
            return true;
        }

        /// <summary>A route retry is still part of a fight. An armed man keeps the
        /// aiming stand authored for the weapon in his hands; feeding this frame
        /// through the generic locomotion stop would lower both hands into body idle.</summary>
        void CombatStand(float dt)
        {
            RunWhile(false);
            if (State == Mode.Fleeing)
            {
                // A transient route retry is not a combat stance for a man who has
                // deliberately dropped his target. Hold the interrupted run neutrally;
                // the bounded flee replan decides his next leg.
                Loco(dt, false);
                return;
            }
            if (!Armed)
            {
                Loco(dt, false);
                return;
            }
            if (Joining) CancelJoin();
            SetPose(VisibleAimPose);
            TickBlend(dt);
        }

        /// <summary>Close on a live mark over routed static ground. The target may
        /// move, so the final corner is refreshed when it has shifted materially;
        /// props do not move, so a stable target pays for the route only once.</summary>
        bool TickCombatStride(float dt, Vector3 target, float stopWithin,
            bool hurry, bool run, bool attackEnvelope = false)
        {
            target.y = Tf.position.y;
            var toTarget = target - Tf.position;
            toTarget.y = 0f;
            bool closing = toTarget.magnitude > stopWithin;
            NoteRoutedStrideIntent(target, closing);
            // A tangent step can finish a few floating-point hairs inside an inflated
            // prop footprint. A normal free-ground order already repairs that state;
            // combat used to keep asking A* to start from an illegal point forever.
            if (RecoverCombatOverlap(target))
            {
                CombatStand(dt);
                return false;
            }
            if (!closing)
            {
                ClearCombatWay();
                TickStride(dt, target, stopWithin, hurry, run);
                return false;
            }

            var moved = target - _combatWayTarget;
            moved.y = 0f;
            bool stale = _combatWay.Count == 0 ||
                         _combatWayVersion != WalkObstacles.Version ||
                         moved.sqrMagnitude > 2f * 2f ||
                         _combatWayAt >= _combatWay.Count;
            if (!stale && _combatWayEndsAtTarget)
            {
                // The final corner follows a living target rather than the old planned
                // coordinate. Even a small move can put a prop across that replacement
                // chord; redraw it instead of handing an unproved line to steering.
                _combatWayEnd = target;
                if (_combatWayAt == _combatWay.Count - 1 &&
                    !StaticChordClear(Tf.position, _combatWayEnd)) stale = true;
            }
            else if (!stale)
            {
                var envelope = _combatWayEnd - target;
                envelope.y = 0f;
                if (envelope.magnitude > stopWithin ||
                    (attackEnvelope && !WalkObstacles.Sees(_combatWayEnd, target)) ||
                    (_combatWayAt == _combatWay.Count - 1 &&
                     !StaticChordClear(Tf.position, _combatWayEnd))) stale = true;
            }
            if (stale && Time.time >= _combatReplanAt &&
                !PlanCombatWay(target, stopWithin, attackEnvelope))
            {
                // A temporary planning miss is not permission to cross a prop. Stand
                // and retry shortly; dynamic people are still handled inside stride.
                CombatStand(dt);
                _blockedFor += dt;
                return CombatRouteFailedModel(_blockedFor, false);
            }

            if (_combatWay.Count == 0)
            {
                CombatStand(dt);
                // Count every frame of a failed plan, not just the 0.4-second retry
                // frame. A cover caller otherwise never observes enough blocked time
                // to abandon an unreachable flank.
                _blockedFor += dt;
                return CombatRouteFailedModel(_blockedFor, false);
            }

            while (_combatWayAt < _combatWay.Count - 1)
            {
                var gap = _combatWay[_combatWayAt] - Tf.position;
                gap.y = 0f;
                bool nextClear = StaticChordClear(
                    Tf.position, _combatWay[_combatWayAt + 1]);
                if (!CombatCornerCanAdvanceModel(gap.magnitude, nextClear)) break;
                _combatWayAt++;
                BeginCombatCorner();
            }

            bool last = _combatWayAt >= _combatWay.Count - 1;
            var waypoint = last ? _combatWayEnd : _combatWay[_combatWayAt];
            // A non-terminal tangent is walked right onto its proved point. Stopping
            // twelve centimetres short can leave the following chord just inside the
            // inflated prop even though the authored corner itself connects cleanly.
            float cornerStop = CombatCornerStopModel(
                last, _combatWayEndsAtTarget, stopWithin);
            TickStride(dt, waypoint, cornerStop,
                hurry, run, terminal: last, routed: true);

            var left = waypoint - Tf.position;
            left.y = 0f;
            bool circling = CombatCornerStalledModel(left.magnitude, dt,
                ref _combatBestGap, ref _combatNoProgress);

            // Something dynamic may have closed a once-valid chord. Give local
            // steering a short chance, then redraw from the feet. A man can also keep
            // taking full steps in a circle, so lack of forward progress is the second
            // half of this test rather than `_blockedFor` alone.
            bool routeFailed = CombatRouteFailedModel(_blockedFor, circling);
            if (routeFailed)
            {
                if (DriveTrace.On)
                    DriveTrace.Event("walk", DisplayName,
                        $"combat route dropped: blocked={_blockedFor:F2}, " +
                        $"circling={circling}, left={left.magnitude:F2}, " +
                        $"terminal={Vector3.Distance(Tf.position, target):F2}, " +
                        $"corner={_combatWayAt + 1}/{_combatWay.Count}, " +
                        $"waypoint=({waypoint.x:F2},{waypoint.z:F2})");
                _combatWay.Clear();
                _combatWayAt = 0;
                _combatReplanAt = Time.time;
                BeginCombatCorner();
            }
            return routeFailed;
        }

        /// <summary>A moving shooter may aim only into the forward sixty-degree cone
        /// of his actual stride. His body can still be facing the mark for a frame while
        /// obstacle steering carries him sideways or back round a car; that is precisely
        /// when keeping the arm on the mark reads as looking and firing the other way.</summary>
        bool StrideAllowsAim(Vector3 toMark)
        {
            // These wardrobes have an authored clip for travel relative to the mark:
            // forward arcs, strafes and backwards steps. Their purpose is to keep the
            // sights up while the route is not straight at the target. The legacy
            // forward-only gait below retains its sixty-degree safety cone.
            if (State == Mode.Engaging &&
                (ShowsAuthoredLongGun || ShowsAuthoredSidearm)) return true;
            // TickEngage asks before this frame's TickStride, while AimGun asks after
            // it in LateUpdate. Accept both the current and immediately previous frame
            // so the trigger and the rendered arm judge the same completed stride.
            if (Time.frameCount - _strideMoveFrame > 1) return true;
            toMark.y = 0f;
            if (_strideDir.sqrMagnitude < 1e-4f || toMark.sqrMagnitude < 1e-4f) return true;
            return Vector3.Dot(_strideDir.normalized, toMark.normalized) >= 0.5f;
        }

        // The stride: at the point, turning as it goes - at a walk, at the hurried
        // walk when there is a fight to get to, or flat out when he is running from
        // one (the jog clip, if there is one). Not through anything: the line he
        // takes is the one the ground allows (WalkObstacles) - straight at the spot
        // while that is clear, else the nearest line off it round the car, the bin,
        // the wall, and straight again once he is past. A spot inside something (the
        // click landed on a car; a hood's place in the formation fell on a bench) is
        // walked straight at and stopped short of - there is no way round to it.
        /// <summary>How near the mark a chase has to be before it will step off the
        /// pavement and cut across the road. Further out than this he runs along the
        /// kerb to get level with the man first.</summary>
        const float CrossWithin = 12f;

        /// <summary>A chase kept on the pavement, and ONLY while a drive-by is being
        /// ridden. The rest of the time the outfit and the mobs cut across the road as
        /// they always have (WalkRoute says so in as many words) - it is the drive-by
        /// that turns that into a bad scene, because the machine draws the whole rival
        /// crew out onto the main road after it and the traffic then has a gunfight to
        /// pick its way round.
        ///
        /// A man walking straight at somebody
        /// across the street walks into the carriageway and finishes the fight standing
        /// in it, which is what the drive-bys looked like: the rival crew out on the main
        /// road, the traffic picking its way round a gunfight. So while the mark is still
        /// a way off, a step that would put him on the road is turned along it instead -
        /// he runs the kerb until he is level, and crosses at the end, which is what a man
        /// does. Close in, or when the mark is himself out on the road (a rider, a man in
        /// a car), nothing is kept from him.</summary>
        Vector3 KeepToPavement(Vector3 from, Vector3 dir, float lookahead)
        {
            var net = LaneNet.Active;
            if (net == null) return dir;
            var here = net.Locate(from, out _, out float dHere, 10f);
            if (here != null && Mathf.Abs(dHere) < here.HalfRoad) return dir;   // already crossing: finish it
            var ahead = net.Locate(from + dir * lookahead, out _, out float dAhead, 10f);
            if (ahead == null || Mathf.Abs(dAhead) >= ahead.HalfRoad) return dir;
            var axis = ahead.Axis;
            axis.y = 0f;
            if (axis.sqrMagnitude < 1e-4f) return dir;
            axis.Normalize();
            return Vector3.Dot(axis, dir) >= 0f ? axis : -axis;
        }

        /// <summary>How hard the people in his way bend his line, in metres across it
        /// per unit of shove. The crowd's own reader hands out at most 0.9 of shove, so
        /// this is a lean of about a quarter turn at the very worst and a couple of
        /// degrees for somebody merely passing.</summary>
        const float CrowdLean = 0.6f;

        /// <summary>The least of his pace the crowd may leave him. NEVER ZERO: a man
        /// stopped dead behind another is a bollard his own boss then brakes behind,
        /// and the leg's stall clocks would read the wait as a man wedged in a bin. He
        /// crawls and leans off instead, and gets by.</summary>
        const float CrowdFloor = 0.25f;

        void TickStride(float dt, Vector3 to, float stopWithin, bool hurry = false, bool run = false,
            bool keepOffRoad = false, bool terminal = true, bool routed = false)
        {
            var delta = to - Tf.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= stopWithin)
            {
                Loco(dt, false);
                _blockedFor = 0f;
                _detouring = false;
                return;
            }
            // A MAN BEING HELD BACK DOES NOT RUN. The dawdle (PaceScale) is how the
            // tether keeps a quick hood level with his boss, and it gears the walk
            // only - so a crew that broke into a run had no brake on it at all and
            // strung out down the street exactly as it used to. Gearing the RUN
            // instead would be worse: the clip's rate is dealt per man and a jog
            // played over a walking step is a man skating. He drops out of the run
            // and walks until the dawdle is lifted, which is what a man does when
            // he is told to wait for the others.
            bool jog = run && HasPose(PoseJog) && PaceScale > 0.95f;
            // and a man running for his life is never dawdled anyway - the tether
            // leaves a panicked man alone
            bool sprint = jog && _sprinting && HasPose(PoseSprint);
            float sprintClip = ClipPace(PoseSprint, SprintClipPace);
            float pace = jog ? (sprint ? _sprintSpeed : JogSpeed)
                : (hurry ? Speed * HurryFactor : Speed) * PaceScale *
                  (_keepingLow ? CrouchFactor : 1f);
            var want = delta / dist;
            float fixedRadius = routed
                ? WalkRoute.ClearanceRadius
                : WalkObstacles.Radius;
            const float trafficRadius = WalkObstacles.Radius;

            // THE PEOPLE ARE PART OF THE GROUND. WalkObstacles knows the walls, the
            // furniture and the traffic, and nothing whatever about anybody on foot -
            // so a man striding over open ground walked clean through his own crew,
            // which is what the player was watching. The crowd's own reader answers
            // who is in the way (one bucket, every walker in the scene: citizens, the
            // outfit, the mobs, the law), and it answers it for the pavements too, so
            // the two halves of the town cannot disagree about who is standing where.
            //
            // He leans off them and comes off his pace behind them, and the obstacle
            // steer below then runs on the line he has actually chosen - so the
            // clearance it reports belongs to the step he takes, and not to a line
            // he has already turned off.
            ReadCrowd(dt, want);
            var line = want;
            if (Mathf.Abs(CrowdPush) > 0.001f)
            {
                var across = new Vector3(want.z, 0f, -want.x);
                // a runner threads the same people at twice the closing speed, so a
                // full shove each pass is a man weaving down the street - half of it
                // buys the same clearance over the extra metres his pace covers
                line = (want + across * (CrowdPush * CrowdLean * (jog ? 0.5f : 1f))).normalized;
            }
            // A DOORWAY IS NOT A CROWDED PAVEMENT EITHER. The people braking him at a
            // shop door are his own crew standing round it; braked to the crowd floor,
            // a three-metre passage takes a dozen seconds and the beat has to finish it
            // for him. Through the door he walks at his own pace.
            float held = Crossing ? 1f : Mathf.Max(CrowdHold, CrowdFloor);
            pace *= held;
            // THE CROWD'S BRAKE OUTRANKS THE RUN here too: braked under the band the
            // run clip reads at (RunRateMin), his feet cannot follow the ground - he
            // walks those strides instead (the walk's rate follows any pace), and the
            // run comes back when the way opens. A touch more asked to re-enter than
            // to stay, so the gait does not flicker at the band's edge.
            // THE LADDER HAS A MIDDLE RUNG. A man dropping out of the sprint drops
            // into the JOG, not into a walk: the sprint's own band is the widest of
            // the three and a single test against it turned every braked flee into a
            // stroll - which is what the player watched a beaten mob do.
            if (sprint && !GaitPaceAllowedModel(
                    pace, sprintClip, SprintRateMin, _strideJog))
            {
                sprint = false;
                pace = JogSpeed * held;
            }
            if (jog && !GaitPaceAllowedModel(pace,
                    ClipPace(PoseJog, JogClipPace), RunRateMin, _strideJog))
            {
                jog = false;
                pace = (hurry ? Speed * HurryFactor : Speed) * PaceScale * held *
                       (_keepingLow ? CrouchFactor : 1f);
            }
            _strideJog = jog;

            Vector3 dir;
            float clear;
            if (Crossing)
            {
                // A DOORWAY IS NOT AN OBSTACLE. He is going through the front of a shop
                // on the doorway's own order: the way is the line, the whole line, and
                // the walls are not asked (OrderThroughDoorway). Everything else about
                // the step is the ordinary walk - the pace, the clip, the turn - so what
                // is seen is a man walking in, not a man sliding through a wall.
                dir = want;
                clear = dist;
            }
            else if (terminal && WalkObstacles.Occupied(to, WalkObstacles.Radius))
            {
                dir = routed ? want : line;
                clear = WalkObstacles.Clear(
                    Tf.position, dir, fixedRadius, trafficRadius, dist);
            }
            else
            {
                // A runner reads the ground further out - at the walk's three metres
                // his corrections come late and hard and he zig-zags thing to thing.
                // WalkObstacles clears its committed side whenever the line is open.
                // A crew preference is passed as an equal-angle tie-break only; it is
                // never installed as an already-committed obstacle side.
                float probe = Mathf.Min(jog ? Lookahead * 2f : Lookahead, dist);
                // A route corner is authoritative. The route has already solved the
                // fixed furniture; letting the crowd rewrite its desired line before
                // obstacle steering made that solver pick a second, contradictory way
                // around the same prop. Steer from the corner and forbid reverse
                // fallbacks. A lateral crowd lean is accepted afterwards only when its
                // whole look-ahead remains clear and it still gains on the corner.
                var steerLine = routed ? want : line;
                dir = WalkObstacles.Steer(Tf.position, steerLine, _strideDir,
                    fixedRadius, probe, ref _steerSide, out clear,
                    _preferredSteerSide, trafficRadius,
                    routed ? RoutedForwardDot : -1f);
                if (routed && Mathf.Abs(CrowdPush) > 0.001f &&
                    Vector3.Dot(line, want) >= RoutedForwardDot)
                {
                    float crowdClear = WalkObstacles.Clear(
                        Tf.position, line, fixedRadius, trafficRadius, probe);
                    if (crowdClear >= probe - 1e-3f)
                    {
                        dir = line;
                        clear = crowdClear;
                    }
                }
            }
            if (keepOffRoad && CrewBike.AnyPassOn && dist > CrossWithin)
                dir = KeepToPavement(Tf.position, dir, Mathf.Max(pace * dt, 1.2f));
            // stepping round somebody counts as going round something: the leg's stall
            // clock must not read a man giving way on a busy pavement as a man wedged
            _detouring = Vector3.Dot(dir, want) < 0.995f || held < 0.9f;

            // capped at the hitch ceiling too: a stalled frame (large dt) must not fling
            // him a lane sideways as he steers round something - it reads as a teleport.
            // GaitGain is the join's: a man setting off eases up to this over the start
            // clip, and one pulling up eases off over the stop clip. Applied HERE and
            // not to `pace` above, so the gait's own thresholds (is he running, at what
            // clip rate) are judged on the pace he is actually settling to.
            float step = Mathf.Min(Mathf.Min(pace * GaitGain * dt, MaxStepPerFrame),
                Mathf.Min(dist, clear));

            // THE FLOOR IS THE WORLD. A stride, wherever it was ordered from - a walk,
            // a march, a man running for his life - never carries a foot off the
            // ground the scene laid (WalkObstacles.City). At the hem he slides along
            // it rather than stopping dead; a man already OUT there (dealt badly, or
            // fenced in after the fact) is let walk freely, because the way back in is
            // a step like any other and freezing him out in the void helps nobody.
            if (step > 1e-4f && WalkObstacles.InCity(Tf.position) &&
                !WalkObstacles.InCity(Tf.position + dir * step))
            {
                var ax = new Vector3(dir.x, 0f, 0f);
                var az = new Vector3(0f, 0f, dir.z);
                if (ax.sqrMagnitude > 1e-6f &&
                    WalkObstacles.InCity(Tf.position + ax.normalized * step))
                    dir = ax.normalized;
                else if (az.sqrMagnitude > 1e-6f &&
                         WalkObstacles.InCity(Tf.position + az.normalized * step))
                    dir = az.normalized;
                else step = 0f;
            }

            // The pavement keeper and city-edge slide above may replace `dir` after
            // the longer look-ahead was measured. The final write gets its own short
            // proof on the ACTUAL heading, so neither rewrite can spend clearance that
            // belonged to a different line and step into a cafe, table or car. Doorway
            // crossings intentionally own their authored straight line through a wall.
            if (!Crossing && step > 0f)
                step = Mathf.Min(step, WalkObstacles.Clear(
                    Tf.position, dir, fixedRadius, trafficRadius, step));

            // Finish a proved non-terminal route corner at its exact coordinate. The
            // general movement epsilon below deliberately ignores sub-millimetre noise,
            // but leaving that residue at a prop tangent can keep the next chord on the
            // blocked side forever. This is still an ordinary speed-limited step: it is
            // exact only when the whole remaining distance survived the final proof.
            bool completesRoutedCorner = routed && !terminal && step > 0f &&
                                         step >= dist;
            if (step > 1e-4f || completesRoutedCorner)
            {
                if (completesRoutedCorner)
                    Tf.position = new Vector3(to.x, Tf.position.y, to.z);
                else
                    Tf.position += dir * step;
                _strideDir = dir;
                _strideMoveFrame = Time.frameCount;
                _blockedFor = 0f;
            }
            else _blockedFor += dt;

            bool moving = step > 1e-4f;
            bool tacticalFacing = TryTacticalFacing(out var faceTarget);

            // With a weapon up, a directional pack keeps the sights on the mark and
            // lets its forward/arc/strafe/backward clip describe the actual travel.
            // Holstered movement still turns to the route line exactly as before.
            // A join owns his heading while it runs - the 90 and 180 starts ARE the
            // turn, and a man swung round to the new line before the clip has played
            // its first step is a man who stumbles on the spot.
            if (tacticalFacing)
            {
                if (Joining) CancelJoin();
                TurnCombat(faceTarget, 360f, dt);
            }
            else if (!Joining)
            {
                Tf.rotation = Quaternion.Slerp(Tf.rotation,
                    Quaternion.LookRotation(moving ? dir : want), 8f * dt);
            }
            if (!tacticalFacing) _weaponStepKnown = false;

            if (jog && moving)
            {
                if (!TryWeaponGait(true, sprint, dir, pace, out var weaponPose))
                    LocomotionPose = sprint ? VisibleSprintPose : VisibleJogPose;
                else LocomotionPose = weaponPose;
                BlendLocomotion(dt, true, joins: !tacticalFacing);
                // the run keeps step with the ground he actually covers: the crowd
                // takes pace off him, and a jog played at its own rate over a
                // shortened step is a man skating. Held inside the rates a run clip
                // reads at (RunRateMin/Max) - past those it is a moon-walk - and his
                // own hair off the beat kept, so a crew never runs in lockstep.
                if (LocomotionPose == PoseWeaponGaitA || LocomotionPose == PoseWeaponGaitB)
                {
                    // DirectionalGaitPose already keyed this exact clip to the ground.
                }
                else if (LocomotionPose == PoseRifleSprint || LocomotionPose == PoseRifleJog)
                    GearVisibleRifleGait(LocomotionPose, pace,
                        sprint ? SprintClipPace : JogClipPace, _runJitter);
                else
                    SetPoseSpeed(LocomotionPose, Mathf.Clamp(
                        pace / (sprint ? sprintClip : ClipPace(PoseJog, JogClipPace)),
                        sprint ? SprintRateMin : RunRateMin, RunRateMax) * _runJitter);
            }
            else if (!moving)
            {
                if (tacticalFacing)
                {
                    SetPose(VisibleAimPose);
                    TickBlend(dt);
                }
                else Loco(dt, false);
            }
            else
            {
                if (!TryWeaponGait(false, false, dir, pace, out var weaponPose))
                    LocomotionPose = _keepingLow && HasPose(PoseCrouchWalk)
                        ? VisibleCrouchWalkPose : VisibleWalkPose;
                else LocomotionPose = weaponPose;
                BlendLocomotion(dt, true, joins: !tacticalFacing);
                // the gait clip keeps step with the pace: quicker feet for the hurried
                // walk, and the crouched shuffle keyed to its own much shorter stride
                if (LocomotionPose == PoseWeaponGaitA || LocomotionPose == PoseWeaponGaitB)
                {
                    // DirectionalGaitPose already keyed this exact clip to the ground.
                }
                else if (LocomotionPose == PoseCrouchWalk)
                    SetPoseSpeed(PoseCrouchWalk,
                        Mathf.Clamp(pace / ClipPace(PoseCrouchWalk, 1.3f), 0.7f, 1.4f));
                else if (LocomotionPose == PoseRifleWalk)
                    GearVisibleRifleGait(LocomotionPose, pace, WalkClipPace);
                else if (LocomotionPose == PoseRifleCrouchWalk)
                    GearVisibleRifleGait(LocomotionPose, pace, 1.3f);
                else HoldWalkRate(pace);
            }
        }

        /// <summary>How much of his walk a man keeps while he is crossing ground bent
        /// double. A crouched shuffle is not a walk with the head down; it is slow,
        /// and looking slow is most of what says he is under fire.</summary>
        const float CrouchFactor = 0.6f;

        /// <summary>Running for his life rather than to somewhere - the one thing in
        /// the town that reaches for the sprint. Cleared by every order, like every
        /// other thing about the last one.</summary>
        bool _sprinting;
        float _sprintSpeed = 5.2f;

        /// <summary>The pack's sprint covers about this much ground a second at
        /// playback 1 - the figure used only when the clip does not say.</summary>
        const float SprintClipPace = 5.2f;

        /// <summary>The band a man may sprint in, the run band's rule one storey up:
        /// the clip sets the look, the town sets how fast a man can be.</summary>
        const float SprintSlowest = 4.2f, SprintQuickest = 6.2f;

        /// <summary>How far off its natural rate the SPRINT clip may be played - and it
        /// is not the jog's figure, because the two bands do not sit the same way round
        /// their clips. The pack's sprint covers 7.27 m a second and the town will not
        /// carry a man faster than SprintQuickest, so the only rate the band can ever
        /// ask for is 6.2/7.27 = 0.85 - under the jog's floor of 0.9. Judged by that
        /// floor the sprint failed its own gait test on an EMPTY STREET: every flee in
        /// the game fell straight through to the walk, which is a beaten man strolling
        /// away from a gunfight. A flat-out run read 15 per cent slow is a man tiring;
        /// it is not a man skating, which is what the floor exists to catch.</summary>
        const float SprintRateMin = 0.72f;

        /// <summary>Break into the flat-out run, if he has one. The pace comes off the
        /// clip and is then held to the town's band, the same trade as SetJog.</summary>
        void BreakIntoSprint()
        {
            _sprinting = HasPose(PoseSprint);
            if (!_sprinting) return;
            float natural = ClipPace(PoseSprint, SprintClipPace);
            _sprintSpeed = Mathf.Clamp(natural * Random.Range(0.94f, 1.06f),
                SprintSlowest, SprintQuickest);
            SetPoseSpeed(PoseSprint, _sprintSpeed / natural);
            if (CurrentPose != PoseSprint) ScatterPhase(PoseSprint);
        }

        // ------------------------------------------------------------------ the leg

        // Has this leg come to its end short of the spot? A leg ends at the spot, or
        // as near as he can get: stood still with nowhere to step (boxed in by cars
        // and walls), walking straight at it and getting no nearer (another man is
        // stood on it; the crowd will not let him through), or round something for so
        // long that he has plainly lost it. While he is going round a thing and still
        // moving he is given his time: a car's flank takes a while to pass.
        float _bestLegDist = float.MaxValue, _stall, _wander;

        /// <summary>A hair off his own run rate, re-dealt whenever he sets off. Two men
        /// who break into a run in the same second are otherwise in step to the frame,
        /// which is the thing that reads as a machine rather than as men.</summary>
        float _runJitter = 1f;

        void BeginLeg()
        {
            _bestLegDist = float.MaxValue;
            _stall = 0f;
            _wander = 0f;
            _blockedFor = 0f;
            _steerSide = 0;
            _strideDir = Vector3.zero;
        }

        bool LegStalled(float left, float dt)
        {
            if (left < _bestLegDist - 0.03f)
            {
                _bestLegDist = left;
                _stall = 0f;
                _wander = 0f;
                return false;
            }
            _wander += dt;
            if (!_detouring || _blockedFor > 0f) _stall += dt;
            return _stall > 0.7f || _wander > 20f;
        }
    }
}
