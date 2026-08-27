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
        Vector3 _strideDir;  // the line he stepped along last frame; zero at the start of a leg
        float _blockedFor;   // seconds stood on this leg with nowhere to step
        bool _detouring;     // this frame's step was off the line to the spot, round something
        bool _strideJog;     // was he jogging the stride last frame (the gait's own hysteresis)

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
            bool keepOffRoad = false)
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
            float held = Mathf.Max(CrowdHold, CrowdFloor);
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
            if (sprint && pace < SprintRateMin * (_strideJog ? 1f : 1.1f) * sprintClip)
            {
                sprint = false;
                pace = JogSpeed * held;
            }
            if (jog && pace < RunRateMin * (_strideJog ? 1f : 1.1f) * ClipPace(PoseJog, JogClipPace))
            {
                jog = false;
                pace = (hurry ? Speed * HurryFactor : Speed) * PaceScale * held *
                       (_keepingLow ? CrouchFactor : 1f);
            }
            _strideJog = jog;

            Vector3 dir;
            float clear;
            if (WalkObstacles.Occupied(to, WalkObstacles.Radius))
            {
                dir = line;
                clear = WalkObstacles.Clear(Tf.position, line, WalkObstacles.Radius, dist);
            }
            else
                // a runner reads the ground further out - at the walk's three metres
                // his corrections come late and hard and he zig-zags thing to thing
                dir = WalkObstacles.Steer(Tf.position, line, _strideDir, WalkObstacles.Radius,
                    Mathf.Min(jog ? Lookahead * 2f : Lookahead, dist), ref _steerSide, out clear);
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

            if (step > 1e-4f)
            {
                Tf.position += dir * step;
                _strideDir = dir;
                _blockedFor = 0f;
            }
            else _blockedFor += dt;

            // he turns to the line he is walking; boxed in, he at least faces the spot.
            // A join owns his heading while it runs - the 90 and 180 starts ARE the
            // turn, and a man swung round to the new line before the clip has played
            // its first step is a man who stumbles on the spot.
            if (!Joining)
                Tf.rotation = Quaternion.Slerp(Tf.rotation,
                    Quaternion.LookRotation(step > 1e-4f ? dir : want), 8f * dt);

            bool moving = step > 1e-4f;
            if (jog && moving)
            {
                LocomotionPose = sprint ? PoseSprint : PoseJog;
                BlendLocomotion(dt, true);
                // the run keeps step with the ground he actually covers: the crowd
                // takes pace off him, and a jog played at its own rate over a
                // shortened step is a man skating. Held inside the rates a run clip
                // reads at (RunRateMin/Max) - past those it is a moon-walk - and his
                // own hair off the beat kept, so a crew never runs in lockstep.
                SetPoseSpeed(LocomotionPose, Mathf.Clamp(
                    pace / (sprint ? sprintClip : ClipPace(PoseJog, JogClipPace)),
                    sprint ? SprintRateMin : RunRateMin, RunRateMax) * _runJitter);
            }
            else if (!moving) Loco(dt, false);
            else
            {
                Loco(dt, true);
                // the gait clip keeps step with the pace: quicker feet for the hurried
                // walk, and the crouched shuffle keyed to its own much shorter stride
                if (LocomotionPose == PoseCrouchWalk)
                    SetPoseSpeed(PoseCrouchWalk,
                        Mathf.Clamp(pace / ClipPace(PoseCrouchWalk, 1.3f), 0.7f, 1.4f));
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
            return _stall > 0.7f || _wander > 8f;
        }
    }
}
