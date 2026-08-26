using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // One beat officer cycling Inside -> WalkOut -> Patrolling -> Returning ->
    // Homing -> WalkIn forever, the foot mirror of PolicePatrolCar. The walking
    // halves ride PedestrianAgent's own sidewalk graph logic (the same crossing
    // discipline as the civilians); the two door legs are hand-lerped straight
    // across the station forecourt, which is kept clear of props for exactly this.
    //
    // A beat is a run of WAYPOINT corners drawn within BeatRadius of the station,
    // each reached by BFS-routed links - a random wander statistically never
    // leaves the home block. The radius keeps the beat on foot-sized ground while
    // the car fleet covers the far districts.
    public class PoliceFootPatrol : PedestrianAgent, IPatrolMarker, IPoliceUnit
    {
        public enum Mode { Inside, WalkOut, Patrolling, Returning, Homing, WalkIn, Responding, OnScene, Arresting, Ritual }

        /// <summary>Metres from the station door a beat waypoint may reach.</summary>
        const float BeatRadius = 180f;

        const int WaypointRetries = 11;

        public Mode State { get; private set; }

        /// <summary>1-based, set by the builder - "Officer 2" on the popup.</summary>
        public int UnitNumber;

        Vector3 _door;
        Quaternion _doorRot;
        PedLink _homeFwd, _homeBack; // the sidewalk stretch fronting the station
        float _entryT;               // where along it the forecourt path meets it
        Vector3 _entryPos;
        List<PedNode> _nodes;        // every walkable corner, the waypoint pool
        Dictionary<PedNode, PedLink> _routeHome; // next link toward home, per node
        Vector2 _restRange;
        Vector2Int _waypointRange;
        float _restTimer;
        float _sceneWave;   // until his next wave-along at a scene he is holding
        float _targetT;

        PedNode _waypoint;
        Dictionary<PedNode, PedLink> _routeToWaypoint;
        int _waypointsLeft;

        Vector3 _legFrom, _legTo;
        float _legT, _legLen;

        // THE COLLAR: the man he is stood over, the piece in his fist while he stands
        // there, and whether he has got there yet. A beat officer carries his sidearm
        // under the coat like everybody else in this town (CrewWalker's rule) and it
        // comes out for one thing only - a man being told he is under arrest.
        CrewWalker _collar;
        Transform _sidearm;
        bool _stoodOver;

        /// <summary>Metres he stops short of the man he is taking. Near enough that the
        /// gun is not a threat shouted across a street, far enough that a man who bolts
        /// does not run through him.</summary>
        const float CollarGap = 3.2f;

        // the call: the corner nearest the scene, the scene itself, a call that came
        // while he was indoors, and his beat pace to go back to
        PedNode _sceneNode;
        Vector3 _scenePos;
        bool _sceneWanted;
        float _beatSpeed;

        // THE PAIR. Beat officers walk two abreast - the lead runs the whole machine
        // and stands on the dispatcher's books; his wingman has no beat and no book
        // entry of his own, he walks his lead's steps (the breadcrumb trail below),
        // stands when he stands, runs when he runs, and covers him over an arrest.
        // One unit, two bodies.
        public PoliceFootPatrol Partner;   // set on the lead
        public PoliceFootPatrol Lead;      // set on the wingman
        readonly List<PedNode> _trail = new List<PedNode>();
        const int TrailKeeps = 8;
        /// <summary>Metres of pavement the wingman holds behind his lead's shoulder.</summary>
        const float FollowGap = 1.7f;
        bool _running;   // the wingman's own record of whether he is at the jog
        GameObject _sidearmKind;   // what a sidearm IS here, kept so the cover can draw one too

        // THE BEAT IS THE BLOCK. The station's block has a pavement ring - its four
        // (or more) corners joined by stretches - and the beat is that ring walked
        // corner to corner in order, the way a beat is actually walked, not a random
        // wander over the quarter. Null where no ring closed (a torn graph): the old
        // wander then stands in.
        Dictionary<PedNode, PedLink> _ringNext, _ringPrev;
        Vector3 _ringCentre;
        int _ringDir = 1;

        // the corner ritual: stood at the junction, hands folded, watching the
        // street - and a word with his partner while they stand
        const float RitualChance = 0.45f;
        Vector3 _ritualFace;
        PedNode _ritualNode;
        float _ritualUntil, _chatAt;

        // A BEAT WITHOUT A STATION. Most pairs are dealt straight onto a block of
        // their own, already walking when the player first looks: no door, no rest
        // indoors, no way "home" - the long stand at a corner is their rest, and a
        // scene released hands them back to the round instead of to a station they
        // do not have.
        bool _endlessBeat;

        public void Configure(Vector3 door, PedLink homeFwd, PedLink homeBack, float entryT,
            List<PedNode> nodes, Dictionary<PedNode, PedLink> routeHome,
            Vector2 restRange, Vector2Int waypointRange, float firstRest)
        {
            _door = door;
            _homeFwd = homeFwd;
            _homeBack = homeBack;
            _entryT = entryT;
            _entryPos = Vector3.Lerp(homeFwd.From.Pos, homeFwd.To.Pos, entryT / homeFwd.Length);
            _nodes = nodes;
            _routeHome = routeHome;
            _restRange = restRange;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));

            State = Mode.Inside;
            _restTimer = firstRest;

            var face = _entryPos - door;
            face.y = 0f;
            _doorRot = face.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(face.normalized)
                : Quaternion.identity;
            Tf.SetPositionAndRotation(door, _doorRot);
        }

        /// <summary>The stationless pair's whole setup: the waypoint pool (a call still
        /// has to find its corner), the corner budget, and straight onto the round -
        /// the officer is mid-beat on the player's first frame, not stepping out of
        /// anywhere. Pair it with <see cref="SetBeat"/> for his block's ring.</summary>
        public void ConfigureBeat(List<PedNode> nodes, Vector2Int waypointRange)
        {
            _nodes = nodes;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));
            _endlessBeat = true;
            State = Mode.Patrolling;
            _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
            _ringDir = Random.value < 0.5f ? 1 : -1;
        }

        public void TickPatrol(float dt)
        {
            if (Lead != null) { TickWing(dt); return; }
            switch (State)
            {
                case Mode.Inside:
                    BlendLocomotion(dt, false);
                    _restTimer -= dt;
                    if (_restTimer <= 0f || _sceneWanted)
                    {
                        _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
                        // the round is walked one way or the other, drawn at the door -
                        // never flipped mid-round, which reads as a man who forgot something
                        _ringDir = Random.value < 0.5f ? 1 : -1;
                        // THE WAY TO THE SHOOTING IS NOT A BEAT'S CLEAN SLATE. A call that
                        // comes while he is at his post has already drawn him a route
                        // (RouteTo, which found the corner nearest the scene and the way to
                        // it); wiping it here - which is right for a man simply setting off
                        // on his rounds - left him stepping out of the door with no route at
                        // all, and ChooseLink then fell through to the wander. Watched: an
                        // officer 80 m from the shots answered the call and walked away up
                        // the far side of the block.
                        if (!_sceneWanted)
                        {
                            _waypoint = null;
                            _routeToWaypoint = null;
                        }
                        BeginLeg(_door, _entryPos, Mode.WalkOut);
                    }
                    break;

                case Mode.WalkOut:
                case Mode.WalkIn:
                    BlendLocomotion(dt, true);
                    if (!TickLeg(dt)) break;
                    if (State == Mode.WalkOut)
                    {
                        State = Mode.Patrolling;
                        _link = _homeFwd;
                        _t = _entryT;
                        _cameFrom = _homeFwd.From;
                        if (_sceneWanted) BeginResponding();
                    }
                    else
                    {
                        State = Mode.Inside;
                        _restTimer = Random.Range(_restRange.x, _restRange.y);
                        Tf.SetPositionAndRotation(_door, _doorRot);
                    }
                    break;

                case Mode.Patrolling:
                case Mode.Returning:
                case Mode.Homing:
                case Mode.Responding:
                    // on the beat he keeps his partner at his shoulder - held at a red
                    // across the way, the lead drifts rather than marching off alone.
                    // Never on a call: a man answering shots does not wait for anybody.
                    PaceScale = State == Mode.Patrolling ? PartnerDrag() : 1f;
                    Tick(dt);
                    if (State == Mode.Homing && _t >= _targetT)
                        BeginLeg(Tf.position, _door, Mode.WalkIn);
                    break;

                case Mode.Ritual:
                {
                    // stood at the corner watching the street - the beat's oldest
                    // posture - with a word for his partner now and then
                    BlendLocomotion(dt, false);
                    TurnToward(_ritualFace, 90f, dt);
                    if (!Acting && !Joining && (_chatAt -= dt) <= 0f)
                    {
                        _chatAt = Random.Range(4f, 9f);
                        PlayAction(Random.value < 0.6f ? CrewKit.SpeakGestures : CrewKit.Waves);
                    }
                    if (Time.time >= _ritualUntil)
                    {
                        State = Mode.Patrolling;
                        Reroute(_ritualNode);
                    }
                    break;
                }

                case Mode.Arresting:
                {
                    if (!_stoodOver)
                    {
                        BlendLocomotion(dt, true);
                        if (TickLeg(dt)) _stoodOver = true;
                        break;
                    }
                    // stood over him with the piece out. The pose is the pistol idle and
                    // not the empty-handed stand, or the gun hangs in a slack fist at
                    // his side and the whole thing reads as a man holding a spanner.
                    if (HasPose(PosePistolIdle)) { SetPose(PosePistolIdle); TickBlend(dt); }
                    else BlendLocomotion(dt, false);
                    if (_collar != null && _collar.Tf != null)
                        TurnToward(_collar.Tf.position - Tf.position, 120f, dt);
                    break;
                }

                case Mode.OnScene:
                {
                    // stood at the corner, looking at it - and turning to it properly,
                    // in steps, because an officer holding a scene is a body the player
                    // walks right up to and stands beside
                    BlendLocomotion(dt, false);
                    TurnToward(_scenePos - Tf.position, 90f, dt);
                    // and working the crowd back off it: an arm out, a wave along,
                    // a shake of the head at whoever will not move
                    if (!Acting && !Joining && (_sceneWave -= dt) <= 0f)
                    {
                        _sceneWave = Random.Range(3.5f, 8f);
                        PlayAction(Random.value < 0.55f ? CrewKit.SpeakGestures : CrewKit.Waves);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------ the call

        Transform IPoliceUnit.Tf => Tf;
        Vector3 IPoliceUnit.Position => Tf.position;
        bool IPoliceUnit.Available => Lead == null && !_sceneWanted &&
            (State == Mode.Inside || State == Mode.Patrolling || State == Mode.Returning ||
             State == Mode.Homing || State == Mode.Ritual);
        bool IPoliceUnit.OnScene => State == Mode.OnScene;
        bool IPoliceUnit.Carries => false;

        /// <summary>Sent to a shooting: the corner nearest it, at a quick march.</summary>
        public void RouteTo(Vector3 scene, float standOff)
        {
            _scenePos = scene;
            PedNode best = null;
            float bestD = float.MaxValue;
            foreach (var n in _nodes)
            {
                float d = (n.Pos - scene).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best == null) return;
            _sceneNode = best;
            _routeToWaypoint = RouteToward(best);
            _waypoint = best;
            if (_beatSpeed <= 0f) _beatSpeed = Speed;
            switch (State)
            {
                case Mode.Inside:
                case Mode.WalkOut:
                case Mode.WalkIn:
                    _sceneWanted = true;
                    break;
                default:
                    BeginResponding();
                    break;
            }
        }

        /// <summary>HE RUNS TO IT. A beat is a walk and everything else about this man
        /// is a walk, but shots are the one thing on a beat that is not walked to - an
        /// officer who strolls to gunfire reads as an officer who did not believe it.
        /// The jog, never the sprint: the sprint is the gait of a man running FROM
        /// something (CivilianAgent's bolt), and he is going the other way.</summary>
        int _runPose = PoseJog;

        /// <summary>Metres a second the chosen run clip covers at playback 1.</summary>
        float RunNatural => ClipPace(_runPose, 3f);

        void BeginResponding()
        {
            _sceneWanted = false;
            State = Mode.Responding;
            StartRun();
        }

        /// <summary>The jog itself, apart from the dispatch bookkeeping: the wingman
        /// breaks into it the frame his lead does, without ever having taken a call.</summary>
        void StartRun()
        {
            _runPose = HasPose(PoseJog) ? PoseJog : PoseWalk;
            LocomotionPose = _runPose;
            if (_beatSpeed <= 0f) _beatSpeed = Speed;
            // the run clip's own pace where there is one; without a jog in his wardrobe
            // he goes at the quick march he always did
            Speed = _runPose != PoseWalk ? RunNatural : _beatSpeed * 1.4f;
            TieRun();
        }

        // The pace at the run, tied to the clip so the feet do not skate: the clip's
        // own metres a second, within the band it reads right in.
        void TieRun()
        {
            if (_runPose != PoseWalk && HasPose(_runPose))
            {
                float natural = RunNatural;
                float rate = Mathf.Clamp(Speed / natural, 0.85f, 1.25f);
                Speed = rate * natural;
                SetPoseSpeed(_runPose, rate);
            }
            HoldWalkRate(Speed);
        }

        /// <summary>Back to the beat's gait: at the scene, and after it. Held apart from
        /// <see cref="Release"/> because arriving ends the run too - he stops running
        /// the moment he is stood at the tape, not when the dispatcher is done with
        /// him.</summary>
        void EndRun()
        {
            LocomotionPose = PoseWalk;
            if (_beatSpeed > 0f) Speed = _beatSpeed;
            HoldWalkRate(Speed);
        }

        /// <summary>The crowd brakes a runner - a man stuck behind a slower back strode
        /// at full rate over a quarter of the pace is the overstriding skate - so the
        /// rate is re-tied every frame to the ground actually covered, and under the
        /// band the jog reads in he drops to the walk for those strides.</summary>
        protected override void GearLocomotion(float speed)
        {
            if (State == Mode.Responding && _runPose != PoseWalk && HasPose(_runPose))
            {
                float natural = RunNatural;
                bool running = speed >= (LocomotionPose == _runPose ? 0.85f : 0.95f) * natural;
                LocomotionPose = running ? _runPose : PoseWalk;
                if (running) SetPoseSpeed(_runPose, Mathf.Clamp(speed / natural, 0.85f, 1.25f));
                else HoldWalkRate(speed);
                return;
            }
            base.GearLocomotion(speed);
        }

        /// <summary>A running man reads the pavement further out, for the same reason a
        /// bolting civilian does: the walk's figures at a run's pace are a man weaving
        /// lamp to lamp.</summary>
        protected override float FreeLineAhead =>
            State == Mode.Responding ? 4f : base.FreeLineAhead;

        /// <summary>Done at the scene: back on the beat, home first.</summary>
        public void Release()
        {
            _sceneWanted = false;
            if (State != Mode.Responding && State != Mode.OnScene && State != Mode.Arresting) return;
            EndChallenge();
            // a station pair goes home; a block pair has no home to go to - back on
            // the round, and RouteBackToRing walks it to its own block from wherever
            // the call dragged it
            State = _endlessBeat ? Mode.Patrolling : Mode.Returning;
            _waypoint = null;
            _routeToWaypoint = null;
            EndRun();
        }

        // ------------------------------------------------------------ the collar

        /// <summary>Walk up to this man with the sidearm out and stand over him. What is
        /// SAID there, and what comes of it, is the dispatcher's (PoliceDispatch.Arrest):
        /// this is the walk, the gun and the stance, which is all a body can do.
        ///
        /// The leg is hand-lerped rather than routed, deliberately: the man is a few
        /// metres away across a pavement he is already stood on, and a graph route to
        /// him would walk the officer round his own corner to reach a man he can see.</summary>
        public void Challenge(CrewWalker man, GameObject sidearm)
        {
            if (man == null || man.Tf == null || Tf == null) return;
            _collar = man;
            _stoodOver = false;
            _sidearmKind = sidearm;   // kept so the wingman's cover can draw the same piece
            DrawSidearm(sidearm);
            var back = Tf.position - man.Tf.position;
            back.y = 0f;
            back = back.sqrMagnitude > 0.04f ? back.normalized : Tf.forward;
            var standAt = man.Tf.position + back * CollarGap;
            standAt.y = Tf.position.y;
            BeginLeg(Tf.position, standAt, Mode.Arresting);
        }

        /// <summary>He is stood over his man with the gun out - the question can be put
        /// to him now, and not a step before.</summary>
        public bool StoodOver => State == Mode.Arresting && _stoodOver;

        /// <summary>The man he is stood over, or null.</summary>
        public CrewWalker Collar => _collar;

        /// <summary>Done with him, either way: the piece goes away and he holds the
        /// scene as he did before. A refusal keeps it OUT - a man who has just been told
        /// no does not put his gun back under his coat.</summary>
        public void EndChallenge(bool holster = true)
        {
            _collar = null;
            _stoodOver = false;
            if (holster) HolsterSidearm();
            if (State == Mode.Arresting) State = Mode.OnScene;
        }

        void DrawSidearm(GameObject prefab)
        {
            if (_sidearm != null || prefab == null || Tf == null) return;
            _sidearm = CrewArms.Attach(Tf.GetComponentInChildren<Animator>(), prefab);
        }

        void HolsterSidearm()
        {
            if (_sidearm == null) return;
            Object.Destroy(_sidearm.gameObject);
            _sidearm = null;
        }

        void BeginLeg(Vector3 from, Vector3 to, Mode mode)
        {
            State = mode;
            _legFrom = from;
            _legTo = to;
            _legLen = Vector3.Distance(from, to);
            _legT = 0f;
        }

        bool TickLeg(float dt)
        {
            // GaitGain is the join's: he eases up to his pace over the start clip
            // rather than gliding off at full speed with his feet still planted
            _legT += Speed * GaitGain * dt;
            float f = _legLen < 0.01f ? 1f : Mathf.Clamp01(_legT / _legLen);
            var dir = _legTo - _legFrom;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f && !Joining)
                Tf.rotation = Quaternion.Slerp(
                    Tf.rotation, Quaternion.LookRotation(dir.normalized), 8f * dt);
            Tf.position = Vector3.Lerp(_legFrom, _legTo, f);
            return f >= 1f;
        }

        /// <summary>The line the start clip is chosen against: the leg he is about to
        /// walk, not the stretch he happens to be stood on.</summary>
        protected override Vector3 JoinHeading
        {
            get
            {
                if (State == Mode.WalkOut || State == Mode.WalkIn)
                {
                    var to = _legTo - Tf.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) return to;
                }
                return base.JoinHeading;
            }
        }

        // Reaching the waypoint corner draws the next one - or, with the budget
        // spent, flips to Returning. Reaching either end of the home stretch while
        // Returning steps onto it by hand, aimed at the forecourt entry point.
        protected override bool OnArrived(PedNode node)
        {
            // the wingman walks his lead's steps and nothing else: no corners of his
            // own to count, no waypoints of his own to draw
            if (Lead != null) return true;
            if (Partner != null) Breadcrumb(node);

            // a long frame can push Homing past the entry point and off the far
            // node in one step - that still counts as home
            if (State == Mode.Homing)
            {
                BeginLeg(Tf.position, _door, Mode.WalkIn);
                return false;
            }

            if (State == Mode.Responding && node == _sceneNode)
            {
                State = Mode.OnScene;
                EndRun();
                return false;
            }

            if (State == Mode.Patrolling && _ringNext != null)
            {
                if (_ringNext.ContainsKey(node))
                {
                    // back on (or round) the ring: a corner of the round, counted -
                    // and now and then stood at, which is what a beat looks like
                    _waypoint = null;
                    _routeToWaypoint = null;
                    if (_waypointsLeft <= 0)
                    {
                        if (_endlessBeat)
                        {
                            // no station to turn back to: the round's end is a LONG
                            // stand at this corner, then a fresh round, either way about
                            _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
                            _ringDir = Random.value < 0.5f ? 1 : -1;
                            BeginRitual(node);
                            _ritualUntil = Time.time + Random.Range(20f, 40f);
                            return false;
                        }
                        State = Mode.Returning;
                    }
                    else
                    {
                        _waypointsLeft--;
                        if (Random.value < RitualChance)
                        {
                            BeginRitual(node);
                            return false;
                        }
                    }
                }
                else if (_routeToWaypoint == null || node == _waypoint)
                    RouteBackToRing(node);
            }
            else if (State == Mode.Patrolling && (node == _waypoint || _waypoint == null))
            {
                if (_waypointsLeft <= 0)
                    State = Mode.Returning;
                else
                {
                    _waypointsLeft--;
                    DrawWaypoint(node);
                }
            }

            if (State == Mode.Returning && _homeFwd != null &&
                (node == _homeFwd.From || node == _homeFwd.To))
            {
                bool fromNear = node == _homeFwd.From;
                _link = fromNear ? _homeFwd : _homeBack;
                _t = 0f;
                _targetT = fromNear ? _entryT : _homeFwd.Length - _entryT;
                State = Mode.Homing;
                return false;
            }

            return true;
        }

        void DrawWaypoint(PedNode from)
        {
            for (int attempt = 0; attempt <= WaypointRetries; attempt++)
            {
                var target = _nodes[Random.Range(0, _nodes.Count)];
                if (target == from) continue;
                // the last tries take whatever draws - a beat cut short beats none
                if (attempt < WaypointRetries - 3 &&
                    (target.Pos - _door).sqrMagnitude > BeatRadius * BeatRadius)
                    continue;

                var map = RouteToward(target);
                if (!map.ContainsKey(from)) continue;

                _waypoint = target;
                _routeToWaypoint = map;
                return;
            }

            _waypoint = null;
            _routeToWaypoint = null;
        }

        protected override PedLink ChooseLink(PedNode node, PedNode keepAwayFrom)
        {
            // the wingman: his lead's trail first, then a route to where it starts,
            // then straight onto the stretch the lead is walking this minute
            if (Lead != null)
            {
                var step = Lead.TrailFrom(node);
                if (step != null) return step;
                var anchor = Lead.TrailAnchor();
                if (anchor != null && anchor != node)
                {
                    var route = RouteToward(anchor);
                    if (route.TryGetValue(node, out var back) && back != null) return back;
                }
                if (Lead._link != null && Lead._link.From == node) return Lead._link;
                return base.ChooseLink(node, keepAwayFrom);
            }

            if (State == Mode.Patrolling && _ringNext != null)
            {
                var round = _ringDir >= 0 ? _ringNext : _ringPrev;
                if (round.TryGetValue(node, out var along) && along != null) return along;
            }

            if ((State == Mode.Patrolling || State == Mode.Responding) && _routeToWaypoint != null &&
                _routeToWaypoint.TryGetValue(node, out var toward) && toward != null)
                return toward;

            if (State == Mode.Returning && _routeHome != null &&
                _routeHome.TryGetValue(node, out var homeward) && homeward != null)
                return homeward;

            return base.ChooseLink(node, keepAwayFrom);
        }

        /// <summary>THE WAY HOME, from every corner of the graph: BFS out from BOTH
        /// ends of the stretch the officer is posted on, then the link toward the nearer
        /// end per corner. Handed to <see cref="Configure"/>, and it lives here rather
        /// than in whichever builder stood the man up so that the city's officers and a
        /// demo's cannot be given two different ways home - the same reason the cars ask
        /// <see cref="PolicePatrolCar.RouteToward"/> for theirs.</summary>
        public static Dictionary<PedNode, PedLink> RouteHome(PedLink home)
        {
            var dist = new Dictionary<PedNode, int> { [home.From] = 0, [home.To] = 0 };
            var queue = new Queue<PedNode>();
            queue.Enqueue(home.From);
            queue.Enqueue(home.To);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var l in n.Links)
                {
                    if (dist.ContainsKey(l.To)) continue;
                    dist[l.To] = dist[n] + 1;
                    queue.Enqueue(l.To);
                }
            }

            var next = new Dictionary<PedNode, PedLink>();
            foreach (var kv in dist)
            {
                PedLink best = null;
                int bestD = int.MaxValue;
                foreach (var l in kv.Key.Links)
                    if (dist.TryGetValue(l.To, out int d) && d < bestD) { bestD = d; best = l; }
                if (best != null) next[kv.Key] = best;
            }
            return next;
        }

        /// <summary>BFS from the target corner over the (symmetric) ped graph,
        /// then the link toward the nearer neighbour per node. Built once per
        /// waypoint draw - the graph is a few hundred nodes.</summary>
        static Dictionary<PedNode, PedLink> RouteToward(PedNode target)
        {
            var dist = new Dictionary<PedNode, int> { [target] = 0 };
            var queue = new Queue<PedNode>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var l in n.Links)
                {
                    if (dist.ContainsKey(l.To)) continue;
                    dist[l.To] = dist[n] + 1;
                    queue.Enqueue(l.To);
                }
            }

            var next = new Dictionary<PedNode, PedLink>();
            foreach (var kv in dist)
            {
                PedLink best = null;
                int bestD = int.MaxValue;
                foreach (var l in kv.Key.Links)
                    if (dist.TryGetValue(l.To, out int d) && d < bestD) { bestD = d; best = l; }
                if (best != null) next[kv.Key] = best;
            }
            return next;
        }

        // ------------------------------------------------------------ the pair

        /// <summary>Make this officer the wingman of <paramref name="lead"/>: he gives
        /// up his own beat and walks his lead's steps a pace to the left instead. Stood
        /// a step aside inside the door too, not in the lead's boots.</summary>
        public void FollowLead(PoliceFootPatrol lead)
        {
            if (lead == null) return;
            Lead = lead;
            lead.Partner = this;
            // the lead was dealt a lane on the right of the walk (Setup deals
            // everybody one); the wingman takes the left, and the pair walk abreast
            Lane = -0.4f;
            // a station pair stands inside its door - a step aside, not in the
            // lead's boots. A block pair has no door and is already on the pavement.
            if (_homeFwd == null) return;
            var dir = _entryPos - _door;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                var n = dir.normalized;
                _door += new Vector3(n.z, 0f, -n.x) * 0.8f;
                Tf.SetPositionAndRotation(_door, _doorRot);
            }
        }

        // The wingman's whole machine. The door legs are his own (two men do not
        // share one doorway pace for pace); everything on the graph is his lead's.
        void TickWing(float dt)
        {
            if (Lead == null || Lead.Tf == null) { Tick(dt); return; }
            switch (State)
            {
                case Mode.Inside:
                    // a block pair has no door to be inside of - never parked here,
                    // but a guard is cheaper than the null it would cost
                    if (_homeFwd == null) { State = Mode.Patrolling; break; }
                    BlendLocomotion(dt, false);
                    if (Lead.State != Mode.Inside && Lead.State != Mode.WalkIn)
                        BeginLeg(_door, _entryPos, Mode.WalkOut);
                    break;

                case Mode.WalkOut:
                case Mode.WalkIn:
                    BlendLocomotion(dt, true);
                    if (!TickLeg(dt)) break;
                    if (State == Mode.WalkOut)
                    {
                        State = Mode.Patrolling;
                        if (_homeFwd != null)
                        {
                            _link = _homeFwd;
                            _t = _entryT;
                            _cameFrom = _homeFwd.From;
                        }
                    }
                    else
                    {
                        State = Mode.Inside;
                        Tf.SetPositionAndRotation(_door, _doorRot);
                    }
                    break;

                case Mode.Arresting:
                {
                    // walked off to stand cover, and stood it: the piece out, the
                    // eyes on the man his lead is taking
                    if (!_stoodOver)
                    {
                        BlendLocomotion(dt, true);
                        if (TickLeg(dt)) _stoodOver = true;
                        break;
                    }
                    if (Lead.State != Mode.Arresting) { EndCover(); break; }
                    if (HasPose(PosePistolIdle)) { SetPose(PosePistolIdle); TickBlend(dt); }
                    else BlendLocomotion(dt, false);
                    if (Lead.Collar != null && Lead.Collar.Tf != null)
                        TurnToward(Lead.Collar.Tf.position - Tf.position, 120f, dt);
                    break;
                }

                default:
                    TickFollow(dt);
                    break;
            }
        }

        void TickFollow(float dt)
        {
            // turning in: the lead is home (or heading in) and the entry is at hand.
            // Only a station pair has an entry at all.
            if (_homeFwd != null &&
                (Lead.State == Mode.WalkIn || Lead.State == Mode.Inside) &&
                (Tf.position - _entryPos).sqrMagnitude < 9f)
            {
                BeginLeg(Tf.position, _door, Mode.WalkIn);
                return;
            }

            // the lead has walked over to make an arrest: walk over too, off to one
            // side, and stand the cover
            if (Lead.State == Mode.Arresting)
            {
                BeginCover();
                return;
            }

            // the lead is stood somewhere - a corner, a scene: stand with him,
            // facing what he faces, with a word back when they are only watching
            if (Lead.State == Mode.Ritual || Lead.State == Mode.OnScene)
            {
                if (_running) { _running = false; EndRun(); }
                BlendLocomotion(dt, false);
                var watch = Lead.State == Mode.Ritual
                    ? Lead._ritualFace
                    : Lead._scenePos - Tf.position;
                TurnToward(watch, 90f, dt);
                if (!Acting && !Joining && Lead.State == Mode.Ritual && (_chatAt -= dt) <= 0f)
                {
                    _chatAt = Random.Range(5f, 10f);
                    PlayAction(CrewKit.SpeakGestures);
                }
                return;
            }

            bool leadRuns = Lead.State == Mode.Responding;
            if (leadRuns != _running)
            {
                _running = leadRuns;
                if (leadRuns) StartRun(); else EndRun();
            }
            State = leadRuns ? Mode.Responding : Mode.Patrolling;

            // station keeping: a shade quicker when he has fallen back (a red light,
            // a knot of people), a shade slower when he is treading on the lead's heels
            float gap = Vector3.Distance(Tf.position, Lead.Tf.position);
            PaceScale = Mathf.Clamp(gap / FollowGap, 0.8f, 1.35f);
            Tick(dt);
        }

        /// <summary>The wingman's half of the arrest: a couple of metres behind his
        /// lead's shoulder, off the line between lead and collar, gun out.</summary>
        void BeginCover()
        {
            var man = Lead.Collar;
            if (man == null || man.Tf == null) return;   // asked again next frame
            DrawSidearm(Lead._sidearmKind);
            if (_running) { _running = false; EndRun(); }
            var back = Tf.position - man.Tf.position;
            back.y = 0f;
            back = back.sqrMagnitude > 0.04f ? back.normalized : -Tf.forward;
            var standAt = man.Tf.position + back * (CollarGap + 2.2f)
                        + new Vector3(back.z, 0f, -back.x) * 1.4f;
            standAt.y = Tf.position.y;
            _stoodOver = false;
            BeginLeg(Tf.position, standAt, Mode.Arresting);
        }

        void EndCover()
        {
            HolsterSidearm();
            State = Mode.Patrolling;
        }

        /// <summary>How hard the lead brakes for his partner: held apart - a red the
        /// wingman is stuck at, a crowd - he drifts rather than marching off alone.
        /// Slowed, never stopped: a stop mid-stretch reads as a stall to the audit.</summary>
        float PartnerDrag()
        {
            if (Partner == null || Partner.Tf == null) return 1f;
            return Vector3.Distance(Tf.position, Partner.Tf.position) > 6f ? 0.3f : 1f;
        }

        // the lead's last few corners, oldest first - the steps his wingman walks
        void Breadcrumb(PedNode node)
        {
            if (_trail.Count > 0 && _trail[_trail.Count - 1] == node) return;
            _trail.Add(node);
            if (_trail.Count > TrailKeeps) _trail.RemoveAt(0);
        }

        /// <summary>The trail's next step out of <paramref name="node"/>, or the very
        /// stretch the lead is on when the trail ends here, or null off the trail.</summary>
        PedLink TrailFrom(PedNode node)
        {
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                if (_trail[i] != node) continue;
                if (i + 1 >= _trail.Count)
                    return _link != null && _link.From == node ? _link : null;
                var next = _trail[i + 1];
                foreach (var l in node.Links)
                    if (l.To == next) return l;
                return null;
            }
            return null;
        }

        PedNode TrailAnchor() => _trail.Count > 0 ? _trail[0] : null;

        // ------------------------------------------------------------ the beat ring

        /// <summary>Hand the officer his block: the pavement ring the beat is walked
        /// round. Anything short of a closed ring is refused and he keeps the wander.</summary>
        public void SetBeat(List<PedNode> ring)
        {
            if (ring == null || ring.Count < 3) return;
            _ringNext = new Dictionary<PedNode, PedLink>();
            _ringPrev = new Dictionary<PedNode, PedLink>();
            _ringCentre = Vector3.zero;
            for (int i = 0; i < ring.Count; i++)
            {
                _ringCentre += ring[i].Pos;
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                foreach (var l in a.Links) if (l.To == b) { _ringNext[a] = l; break; }
                foreach (var l in b.Links) if (l.To == a) { _ringPrev[b] = l; break; }
            }
            _ringCentre /= ring.Count;
        }

        void BeginRitual(PedNode node)
        {
            State = Mode.Ritual;
            _ritualNode = node;
            _ritualUntil = Time.time + Random.Range(5f, 14f);
            _chatAt = Random.Range(2f, 6f);
            // he watches the STREET: outward, away from the block at his back
            _ritualFace = node.Pos - _ringCentre;
            _ritualFace.y = 0f;
        }

        /// <summary>Pushed off his ring (a call took him across the quarter): the way
        /// back to its nearest corner, by the same BFS the waypoints used.</summary>
        void RouteBackToRing(PedNode from)
        {
            PedNode best = null;
            float bestD = float.MaxValue;
            foreach (var corner in _ringNext.Keys)
            {
                float d = (corner.Pos - from.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = corner; }
            }
            if (best == null) return;
            _waypoint = best;
            _routeToWaypoint = RouteToward(best);
        }

        /// <summary>THE BLOCK'S RING, read off the graph itself: start on the stretch
        /// the station fronts, oriented so the block lies on the right hand, and take
        /// the sharpest right turn at every corner until the walk closes. That is the
        /// face of the planar graph the station's block is - no lot arithmetic, no
        /// radius guess. Null when the walk never closes (a torn graph, a waterfront):
        /// the caller then leaves the officer his old wander. Static and here, like
        /// <see cref="RouteHome"/>, so the city and the demos cannot disagree on what
        /// a beat is.</summary>
        public static List<PedNode> BeatRing(PedLink homeFwd, PedLink homeBack, Vector3 door)
        {
            if (homeFwd == null || homeBack == null) return null;
            var dir = homeFwd.To.Pos - homeFwd.From.Pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return null;
            dir.Normalize();
            var right = new Vector3(dir.z, 0f, -dir.x);
            var first = Vector3.Dot(door - homeFwd.From.Pos, right) > 0f ? homeFwd : homeBack;

            var ring = new List<PedNode>();
            var link = first;
            for (int step = 0; step < 64; step++)
            {
                ring.Add(link.From);
                var at = link.To;
                if (at == first.From && ring.Count >= 3) return ring;
                var inDir = at.Pos - link.From.Pos;
                inDir.y = 0f;
                inDir.Normalize();
                PedLink pick = null;
                float best = float.MinValue;
                foreach (var l in at.Links)
                {
                    if (l.To == link.From && at.Links.Count > 1) continue;
                    var outDir = l.To.Pos - at.Pos;
                    outDir.y = 0f;
                    if (outDir.sqrMagnitude < 1e-4f) continue;
                    outDir.Normalize();
                    // signed turn, clockwise positive: the rightmost wins
                    float turn = Mathf.Atan2(Vector3.Cross(inDir, outDir).y,
                                             Vector3.Dot(inDir, outDir));
                    if (turn > best) { best = turn; pick = l; }
                }
                if (pick == null) return null;
                link = pick;
            }
            return null;
        }

        // ------------------------------------------------------------ the marker

        Transform IPatrolMarker.MarkerTf => Tf;
        float IPatrolMarker.MarkerHeight => 2.1f;
        bool IPatrolMarker.MarkerDimmed => State == Mode.Inside;
        string IPatrolMarker.MarkerTitle => "Officer " + UnitNumber;

        string IPatrolMarker.MarkerLine => Lead != null ? WingLine : State switch
        {
            Mode.Inside => "Inside the station",
            Mode.WalkOut => "Stepping out on patrol",
            Mode.Ritual => "Watching the street from the corner",
            Mode.Patrolling => _waypoint != null
                ? "Walking the beat - making for the corner "
                    + PatrolInfo.Toward(Tf.position, _waypoint.Pos) + " of here - "
                    + (_waypointsLeft == 0 ? "last stop, then home"
                                           : _waypointsLeft + " more stops until return")
                : "Walking the beat heading " + PatrolInfo.Heading(Tf),
            Mode.Returning => "Returning to the station",
            Mode.Homing => "Back on the home stretch",
            Mode.WalkIn => "Heading in to the station",
            Mode.Responding => "Responding - shots fired " + PatrolInfo.Toward(Tf.position, _scenePos) + " of here",
            Mode.OnScene => "At the scene",
            Mode.Arresting => _collar != null ? "Making an arrest - " + _collar.DisplayName
                                              : "Making an arrest",
            _ => string.Empty,
        };

        // the wingman's words: everything he does, he does beside his lead
        string WingLine => State switch
        {
            Mode.Inside => "Inside the station",
            Mode.WalkOut => "Stepping out on patrol",
            Mode.WalkIn => "Heading in to the station",
            Mode.Arresting => "Covering the arrest",
            Mode.Responding => "Responding behind Officer " + Lead.UnitNumber,
            _ => Lead.State == Mode.Ritual
                ? "Watching the street with Officer " + Lead.UnitNumber
                : "On the beat with Officer " + Lead.UnitNumber,
        };
    }
}
