using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // One patrol car cycling Resting -> Undocking -> Patrolling -> Returning ->
    // Docking forever. The driving halves ride DemoVehicle's own lane-graph logic
    // (the same car-following and signal discipline as the civilian traffic); the
    // parked halves are hand-animated over PatrolDocking's Bezier.
    //
    // A patrol is a run of WAYPOINTS drawn uniformly over the whole lane graph,
    // each reached by BFS-routed turns - not a random wander, which statistically
    // loiters around the station and never sees the far districts. The budget
    // counts waypoints; when it runs dry the same routing brings the car back to
    // the kerb in front of the station and it swings into its stall.
    public class PolicePatrolCar : DemoVehicle, IPatrolMarker
    {
        public enum Mode { Resting, Undocking, Patrolling, Returning, Docking }

        /// <summary>Redraws tolerated when a drawn waypoint has no route from
        /// here - the grid is strongly connected, so one draw normally lands.</summary>
        const int WaypointRetries = 4;

        public Mode State { get; private set; }

        /// <summary>1-based, set by the builder - "Patrol Car 2" on the popup.</summary>
        public int UnitNumber;

        Vector3 _stall;
        Quaternion _stallRot;
        RoadEdge _home;      // the lane in front of the station
        float _kerbS;        // where along it the forecourt driveway meets it
        Vector3 _kerb;
        List<RoadEdge> _allEdges;
        Dictionary<RoadEdge, RoadEdge> _routeHome; // next lane toward _home, per lane
        Vector2 _restRange;
        Vector2Int _waypointRange;
        float _restTimer;

        RoadEdge _waypoint;
        Dictionary<RoadEdge, RoadEdge> _routeToWaypoint;
        int _waypointsLeft;

        PatrolDocking.Curve _curve;
        float _t;
        Quaternion _fromRot, _toRot;

        /// <summary>Set for the leg the car drives FORWARDS - out of the bay - where
        /// the heading is the curve's own tangent instead of a slerp between the
        /// endpoints. Clear for the way in, which is a reversing manoeuvre: the car
        /// ends nose-out, so there its heading is not its direction of travel.</summary>
        bool _steerByTangent;

        public void InitParked(Vector3 stall, Quaternion stallRot, RoadEdge home, float kerbS,
            List<RoadEdge> allEdges, Dictionary<RoadEdge, RoadEdge> routeHome,
            Vector2 restRange, Vector2Int waypointRange, float firstRest)
        {
            _stall = stall;
            _stallRot = stallRot;
            _home = home;
            _kerbS = kerbS;
            _kerb = home.Start + home.Dir * kerbS;
            _allEdges = allEdges;
            _routeHome = routeHome;
            _restRange = restRange;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));

            State = Mode.Resting;
            _restTimer = firstRest;
            Tf.SetPositionAndRotation(stall, stallRot);
        }

        public void TickPatrol(float dt)
        {
            switch (State)
            {
                case Mode.Resting:
                    _restTimer -= dt;
                    if (_restTimer <= 0f && KerbClear()) BeginUndock();
                    break;

                case Mode.Undocking:
                case Mode.Docking:
                    _t = PatrolDocking.Advance(_curve, _t, PatrolDocking.Speed * dt);
                    Tf.SetPositionAndRotation(PatrolDocking.Point(_curve, _t),
                        _steerByTangent
                            ? PatrolDocking.Heading(_curve, _t, _toRot)
                            : Quaternion.Slerp(_fromRot, _toRot, Mathf.SmoothStep(0f, 1f, _t)));
                    if (_t < 1f) break;
                    if (State == Mode.Undocking)
                    {
                        State = Mode.Patrolling;
                        _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
                        _waypoint = null;
                        _routeToWaypoint = null;
                        Spawn(_home, _kerbS);
                    }
                    else
                    {
                        State = Mode.Resting;
                        _restTimer = Random.Range(_restRange.x, _restRange.y);
                        Tf.SetPositionAndRotation(_stall, _stallRot);
                    }
                    break;

                case Mode.Patrolling:
                case Mode.Returning:
                    Tick(dt);
                    if (State == Mode.Returning && CurrentEdge == _home &&
                        Progress >= _kerbS - 0.15f)
                        BeginDock();
                    break;
            }
        }

        // The same footprint test a gate spawn would apply: the kerb is the metre
        // of lane the car is about to occupy, and a busy moment costs only the
        // next tick's retry.
        bool KerbClear()
        {
            var cars = _home.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                float s = cars[i].Progress;
                if (s > _kerbS - 14f && s < _kerbS + 8f) return false;
            }
            return true;
        }

        void BeginUndock()
        {
            State = Mode.Undocking;
            _curve = PatrolDocking.Undock(
                _stall, _stallRot * Vector3.forward, _kerb, _home.Dir);
            _t = 0f;
            _fromRot = _stallRot;
            _toRot = Quaternion.LookRotation(_home.Dir);
            _steerByTangent = true;
        }

        void BeginDock()
        {
            Despawn();
            State = Mode.Docking;
            _curve = PatrolDocking.Dock(Tf.position, _stall, _stallRot * Vector3.forward);
            _t = 0f;
            _fromRot = Tf.rotation;
            _toRot = _stallRot;
            _steerByTangent = false;
        }

        // Reaching the current waypoint edge draws the next one - anywhere on the
        // map - or, with the budget spent, flips to Returning. Both patrol legs
        // and the trip home ride the same BFS next-edge maps.
        protected override RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            if (State == Mode.Patrolling && (_waypoint == null || CurrentEdge == _waypoint))
            {
                if (_waypointsLeft <= 0)
                    State = Mode.Returning;
                else
                {
                    _waypointsLeft--;
                    DrawWaypoint();
                }
            }

            if (State == Mode.Patrolling && _routeToWaypoint != null &&
                _routeToWaypoint.TryGetValue(CurrentEdge, out var toward) && toward != null)
                return toward;

            if (State == Mode.Returning &&
                _routeHome.TryGetValue(CurrentEdge, out var homeward) && homeward != null)
                return homeward;

            return base.PickNext(straight, lefts, rights);
        }

        void DrawWaypoint()
        {
            for (int attempt = 0; attempt <= WaypointRetries; attempt++)
            {
                var target = _allEdges[Random.Range(0, _allEdges.Count)];
                if (target == CurrentEdge || target == _home) continue;

                var map = RouteToward(_allEdges, target);
                if (!map.ContainsKey(CurrentEdge)) continue;

                _waypoint = target;
                _routeToWaypoint = map;
                return;
            }

            // no reachable draw - wander this leg instead of stalling the patrol
            _waypoint = null;
            _routeToWaypoint = null;
        }

        // Rolling to a stop at the kerb is a speed clamp, exactly how the demo's
        // cars already brake for signals.
        protected override float LimitTarget(float target)
        {
            if (State == Mode.Returning && CurrentEdge == _home && Progress <= _kerbS)
                target = Mathf.Min(target, Allowed(0f, _kerbS - Progress));
            return target;
        }

        /// <summary>
        /// Reverse BFS from the target lane over the turn graph (no U-turns), then
        /// one greedy hop per lane: the value is the next lane after the key on the
        /// shortest route to the target. The target itself maps to its shortest
        /// loop, so overshooting comes around again. Cheap - the demo's graph is a
        /// hundred-odd edges - so patrol legs build one per waypoint draw.
        /// </summary>
        public static Dictionary<RoadEdge, RoadEdge> RouteToward(List<RoadEdge> edges, RoadEdge target)
        {
            var dist = new Dictionary<RoadEdge, int> { [target] = 0 };
            var queue = new Queue<RoadEdge>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                foreach (var e in f.From.Incoming)
                {
                    if (Vector3.Dot(f.Dir, e.Dir) < -0.5f) continue; // U-turn
                    if (dist.ContainsKey(e)) continue;
                    dist[e] = dist[f] + 1;
                    queue.Enqueue(e);
                }
            }

            var next = new Dictionary<RoadEdge, RoadEdge>();
            foreach (var e in edges)
            {
                RoadEdge best = null;
                int bestD = int.MaxValue;
                foreach (var f in e.To.Outgoing)
                {
                    if (Vector3.Dot(f.Dir, e.Dir) < -0.5f) continue;
                    if (dist.TryGetValue(f, out int d) && d < bestD) { bestD = d; best = f; }
                }
                if (best != null) next[e] = best;
            }
            return next;
        }

        // ------------------------------------------------------------ the marker

        Transform IPatrolMarker.MarkerTf => Tf;
        float IPatrolMarker.MarkerHeight => 2.8f;
        bool IPatrolMarker.MarkerDimmed => State == Mode.Resting;
        string IPatrolMarker.MarkerTitle => "Patrol Car " + UnitNumber;

        string IPatrolMarker.MarkerLine => State switch
        {
            Mode.Resting => "Resting at the station",
            Mode.Undocking => "Pulling out on patrol",
            Mode.Patrolling => _waypoint != null
                ? "On patrol - making for the "
                    + PatrolInfo.Toward(Tf.position, WaypointPos()) + " of town - "
                    + (_waypointsLeft == 0 ? "last stop, then home"
                                           : _waypointsLeft + " more stops until return")
                : "On patrol heading " + PatrolInfo.Heading(Tf),
            Mode.Returning => "Returning to the station",
            Mode.Docking => "Parking at the station",
            _ => string.Empty,
        };

        Vector3 WaypointPos() => (_waypoint.Start + _waypoint.End) * 0.5f;
    }
}
