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
        public enum Mode { Inside, WalkOut, Patrolling, Returning, Homing, WalkIn, Responding, OnScene }

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

        // the call: the corner nearest the scene, the scene itself, a call that came
        // while he was indoors, and his beat pace to go back to
        PedNode _sceneNode;
        Vector3 _scenePos;
        bool _sceneWanted;
        float _beatSpeed;

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

        public void TickPatrol(float dt)
        {
            switch (State)
            {
                case Mode.Inside:
                    BlendLocomotion(dt, false);
                    _restTimer -= dt;
                    if (_restTimer <= 0f || _sceneWanted)
                    {
                        _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
                        _waypoint = null;
                        _routeToWaypoint = null;
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
                    Tick(dt);
                    if (State == Mode.Homing && _t >= _targetT)
                        BeginLeg(Tf.position, _door, Mode.WalkIn);
                    break;

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
        bool IPoliceUnit.Available => !_sceneWanted &&
            (State == Mode.Inside || State == Mode.Patrolling || State == Mode.Returning || State == Mode.Homing);
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

        void BeginResponding()
        {
            _sceneWanted = false;
            State = Mode.Responding;
            Speed = _beatSpeed * 1.4f;
            HoldWalkRate(Speed);
        }

        /// <summary>Done at the scene: back on the beat, home first.</summary>
        public void Release()
        {
            _sceneWanted = false;
            if (State != Mode.Responding && State != Mode.OnScene) return;
            State = Mode.Returning;
            _waypoint = null;
            _routeToWaypoint = null;
            if (_beatSpeed > 0f) { Speed = _beatSpeed; HoldWalkRate(Speed); }
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
                return false;
            }

            if (State == Mode.Patrolling && (node == _waypoint || _waypoint == null))
            {
                if (_waypointsLeft <= 0)
                    State = Mode.Returning;
                else
                {
                    _waypointsLeft--;
                    DrawWaypoint(node);
                }
            }

            if (State == Mode.Returning && (node == _homeFwd.From || node == _homeFwd.To))
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
            if ((State == Mode.Patrolling || State == Mode.Responding) && _routeToWaypoint != null &&
                _routeToWaypoint.TryGetValue(node, out var toward) && toward != null)
                return toward;

            if (State == Mode.Returning &&
                _routeHome.TryGetValue(node, out var homeward) && homeward != null)
                return homeward;

            return base.ChooseLink(node, keepAwayFrom);
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

        // ------------------------------------------------------------ the marker

        Transform IPatrolMarker.MarkerTf => Tf;
        float IPatrolMarker.MarkerHeight => 2.1f;
        bool IPatrolMarker.MarkerDimmed => State == Mode.Inside;
        string IPatrolMarker.MarkerTitle => "Officer " + UnitNumber;

        string IPatrolMarker.MarkerLine => State switch
        {
            Mode.Inside => "Inside the station",
            Mode.WalkOut => "Stepping out on patrol",
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
            _ => string.Empty,
        };
    }
}
