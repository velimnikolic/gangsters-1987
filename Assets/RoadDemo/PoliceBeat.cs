using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// One foot beat: a real two-man police crew plus the small brain that walks it.
    /// Combat, health, targeting and weapons all remain DemoCrews truth; this class
    /// owns only duty, pavement destinations and the station/door transitions.
    /// </summary>
    public sealed class PoliceBeat : IPoliceUnit, IPatrolMarker
    {
        const float LoseFightAfter = 8f;

        public enum Mode
        {
            Inside, WalkOut, Patrolling, Returning, Responding, OnScene,
            Arresting, Ritual, Doorway,
        }

        readonly DemoCrews _crews;
        readonly List<PedNode> _nodes;
        readonly List<PedNode> _ring;
        readonly Vector3 _stationDoor;
        readonly Vector2 _restRange;
        readonly bool _hasStation;
        int _ringAt;
        int _ringDirection;
        float _restUntil;
        float _ritualUntil;
        Vector3 _scene;
        float _standOff;
        float _provokedAt = -1000f;
        float _fightSeenAt = -1000f;
        float _routeRetryAt;
        DemoCrews.Unit _fightTarget;
        CrewWalker _collar;

        public readonly DemoCrews.Unit Unit;
        public int UnitNumber;
        public int Precinct { get; set; }
        public Mode State { get; private set; }
        public bool OffWatch { get; private set; }
        public DemoCrews.Unit ProvokedBy { get; private set; }

        /// <summary>Raised once for each fresh attack on this beat. Dispatch owns the
        /// city-wide response; the beat merely reports what its crew already knows.</summary>
        public Action<PoliceBeat, DemoCrews.Unit> Provoked;

        public PoliceBeat(DemoCrews crews, DemoCrews.Unit unit, int unitNumber,
            List<PedNode> nodes, List<PedNode> ring, Vector3? stationDoor,
            Vector2 restRange, float firstRest)
        {
            _crews = crews;
            Unit = unit;
            UnitNumber = unitNumber;
            _nodes = nodes ?? new List<PedNode>();
            _ring = ring ?? new List<PedNode>();
            _hasStation = stationDoor.HasValue;
            _stationDoor = stationDoor ?? Vector3.zero;
            _restRange = restRange;
            _ringDirection = UnityEngine.Random.value < 0.5f ? 1 : -1;
            _ringAt = NearestRing(Unit != null ? Unit.Position : Vector3.zero);

            if (Unit != null)
                foreach (var man in Unit.All())
                {
                    if (man == null) continue;
                    man.ObeysSignals = true;
                    man.RangeFactor = 0.9f;
                    man.Tag = "police";
                }

            if (_hasStation)
            {
                _restUntil = Time.time + Mathf.Max(0f, firstRest);
                State = Mode.Returning;
                GoInside();
            }
            else
            {
                State = Mode.Patrolling;
                OrderNextCorner();
            }
        }

        public CrewWalker Lead => Standing(Unit);
        public Transform Tf => Lead != null ? Lead.Tf : Unit != null ? Unit.Root : null;
        public Vector3 Position => Unit != null ? Unit.Position : Vector3.zero;
        public bool Carries => false;
        public bool OnScene => State == Mode.OnScene;
        public bool Available => Unit != null && !Unit.Wiped && !OffWatch &&
            Unit.TargetUnit == null && !Unit.Surrendered &&
            (State == Mode.Inside || State == Mode.Patrolling || State == Mode.Returning ||
             State == Mode.Ritual);

        public bool StoodOver
        {
            get
            {
                var lead = Lead;
                return lead != null && _collar != null && _collar.Tf != null &&
                       Flat(lead.Tf.position - _collar.Tf.position).sqrMagnitude <=
                       4.6f * 4.6f;
            }
        }

        public void TickPatrol(float dt)
        {
            if (Unit == null || Unit.Wiped) return;
            ReadProvocation();
            if (FightStillLive()) return;

            if (State == Mode.OnScene && Lead != null && DoorBeat.Active(Lead))
                State = Mode.Doorway;
            else if (State == Mode.Doorway && (Lead == null || !DoorBeat.Active(Lead)))
                State = Mode.OnScene;

            switch (State)
            {
                case Mode.Returning:
                    if (!_hasStation)
                    {
                        State = Mode.Patrolling;
                        OrderNextCorner();
                    }
                    else if (CrewQuarters.Inside(Unit))
                    {
                        State = Mode.Inside;
                        _restUntil = Mathf.Max(_restUntil, Time.time + RestSeconds());
                    }
                    else if (!CrewQuarters.Billeted(Unit))
                        GoInside();
                    break;

                case Mode.Inside:
                    if (!OffWatch && Time.time >= _restUntil)
                    {
                        CrewQuarters.BringOut(Unit);
                        State = Mode.WalkOut;
                    }
                    break;

                case Mode.WalkOut:
                    if (CrewQuarters.AllOutside(Unit))
                    {
                        State = Mode.Patrolling;
                        ReseatAll();
                        OrderNextCorner();
                    }
                    break;

                case Mode.Patrolling:
                    if (!AnyoneMoving())
                    {
                        State = Mode.Ritual;
                        _ritualUntil = Time.time + UnityEngine.Random.Range(5f, 14f);
                    }
                    break;

                case Mode.Ritual:
                    if (!OffWatch && Time.time >= _ritualUntil)
                    {
                        State = Mode.Patrolling;
                        OrderNextCorner();
                    }
                    else if (OffWatch && _hasStation)
                        Release();
                    break;

                case Mode.Responding:
                    var reach = Mathf.Max(4f, _standOff);
                    if (Flat(Position - _scene).sqrMagnitude <= reach * reach)
                    {
                        StopWhereTheyStand();
                        State = Mode.OnScene;
                    }
                    else if (!AnyoneMoving() && Time.time >= _routeRetryAt)
                    {
                        TryResponseRoute();
                    }
                    break;
            }
        }

        public void RouteTo(Vector3 scene, float standOff)
        {
            if (Unit == null || Unit.Wiped) return;
            CrewQuarters.CallOut(Unit);
            // An emergency response follows the crew's route semantics all the way:
            // it does not turn the same route back into a slow pedestrian trip by
            // waiting at every light.
            foreach (var man in Unit.All())
                if (man != null) man.ObeysSignals = false;
            _collar = null;
            LowerGuns();
            _scene = scene;
            _standOff = standOff;
            State = Mode.Responding;
            TryResponseRoute();
        }

        /// <summary>The response uses the exact same WalkRoute/formation transaction as
        /// the player's crew. A failed route is retried; it is never mistaken for having
        /// arrived at the shop. Every accepted leg is urgent, so officers run.</summary>
        void TryResponseRoute()
        {
            if (Unit == null || Unit.Wiped || _crews == null) return;
            _routeRetryAt = Time.time + 1.25f;
            var away = Flat(Position - _scene);
            if (away.sqrMagnitude < 0.04f && Tf != null) away = -Flat(Tf.forward);
            if (away.sqrMagnitude < 0.04f) away = Vector3.back;
            var destination = _scene + away.normalized * Mathf.Max(2f, _standOff * 0.7f);
            _crews.MarchTo(Unit, destination,
                run: LivingCity.Police.PoliceProcedure.RunToScene, keepOffRoad: false,
                allowCustody: true);
        }

        public void Release()
        {
            if (Unit == null || Unit.Wiped) return;
            foreach (var man in Unit.All())
                if (man != null) man.ObeysSignals = true;
            _collar = null;
            ClearFight();
            LowerGuns();
            if (_hasStation)
            {
                State = Mode.Returning;
                GoInside();
            }
            else
            {
                State = OffWatch ? Mode.Ritual : Mode.Patrolling;
                ReseatAll();
                if (!OffWatch) OrderNextCorner();
            }
        }

        public void Challenge(CrewWalker collar)
        {
            _collar = collar;
            State = Mode.Arresting;
            if (collar == null || collar.Tf == null) return;
            var lead = Lead;
            if (lead == null || lead.Tf == null) return;
            var back = Flat(lead.Tf.position - collar.Tf.position);
            if (back.sqrMagnitude < 0.04f) back = -Flat(collar.Tf.forward);
            back.Normalize();
            var side = new Vector3(back.z, 0f, -back.x);
            var index = 0;
            foreach (var man in Unit.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                var at = collar.Tf.position + back * (3.2f + index * 1.5f) +
                         side * (index == 0 ? 0f : 1.4f);
                at.y = man.Tf.position.y;
                man.Disengage();
                man.OrderToPoint(at, index * 0.15f);
                man.HoldAtGunpoint(collar);
                index++;
            }
        }

        /// <summary>Custody refreshes this while the prisoner waits and while he is led
        /// to the car. It keeps both partners out of ambient chatter with their pieces
        /// visibly trained on the man.</summary>
        public void HoldAtGunpoint(CrewWalker collar)
        {
            if (Unit == null || Unit.Wiped || Unit.TargetUnit != null || collar == null ||
                collar.Dead || collar.Tf == null)
                return;
            _collar = collar;
            State = Mode.Arresting;
            foreach (var man in Unit.All())
                if (man != null && !man.Dead)
                    man.HoldAtGunpoint(collar);
        }

        public void EndChallenge(bool holster)
        {
            _collar = null;
            ClearFight();
            foreach (var man in Unit.All())
            {
                if (man == null || man.Dead) continue;
                man.LowerGunpoint();
                if (holster) man.Holster();
            }
            State = Mode.OnScene;
        }

        public void BeginDoorway(Vector3 point)
        {
            State = Mode.Doorway;
            if (_crews != null)
                _crews.MarchTo(Unit, point,
                    run: LivingCity.Police.PoliceProcedure.RunToScene,
                    keepOffRoad: false,
                    allowCustody: true);
        }

        public void EndDoorway()
        {
            if (State == Mode.Doorway) State = Mode.OnScene;
        }

        public void StandDown()
        {
            OffWatch = true;
            if (_hasStation && (State == Mode.Patrolling || State == Mode.Ritual)) Release();
        }

        public void StandTo(float firstRest = 0f)
        {
            OffWatch = false;
            if (State == Mode.Inside) _restUntil = Mathf.Min(_restUntil, Time.time + firstRest);
        }

        void ReadProvocation()
        {
            if (Unit.ProvokedAt <= _provokedAt) return;
            _provokedAt = Unit.ProvokedAt;
            DemoCrews.Unit attacker = null;
            var best = float.MaxValue;
            foreach (var other in _crews.Units)
            {
                if (other == null || other == Unit || other.IsPolice || other.Wiped) continue;
                if (other.TargetUnit != Unit &&
                    Time.time - other.PoliceFightOrderedAt > 2f) continue;
                var d = Flat(other.Position - Position).sqrMagnitude;
                if (d < best) { best = d; attacker = other; }
            }
            if (attacker == null) return;
            ProvokedBy = attacker;
            _crews.Sic(Unit, attacker);
            Provoked?.Invoke(this, attacker);
        }

        /// <summary>The dispatcher's attack order is an instruction to answer the
        /// threat in front of this beat, not a permanent player-style KILL order. Once
        /// the target is gone, routed, or out of every officer's sight for the ordinary
        /// combat grace, the pair becomes law again instead of remaining unavailable
        /// for the rest of the scene.</summary>
        bool FightStillLive()
        {
            var target = Unit.TargetUnit;
            if (target == null)
            {
                if (Unit.OrderedFight) ClearFight();
                else _fightTarget = null;
                return false;
            }

            if (target != _fightTarget)
            {
                _fightTarget = target;
                _fightSeenAt = Time.time;
            }

            if (target.Wiped || target.Retreated)
            {
                ClearFight();
                return false;
            }

            if (CanSee(target)) _fightSeenAt = Time.time;
            if (Time.time - _fightSeenAt <= LoseFightAfter) return true;

            ClearFight();
            return false;
        }

        bool CanSee(DemoCrews.Unit target)
        {
            var rangeSq = DemoCrews.SightRange * DemoCrews.SightRange;
            foreach (var officer in Unit.All())
            {
                if (officer == null || officer.Dead || officer.Tf == null ||
                    !officer.Tf.gameObject.activeInHierarchy) continue;
                foreach (var suspect in target.All())
                {
                    if (suspect == null || suspect.Dead || suspect.Tf == null ||
                        !suspect.Tf.gameObject.activeInHierarchy) continue;
                    if ((officer.Tf.position - suspect.Tf.position).sqrMagnitude > rangeSq) continue;
                    if (WalkObstacles.Sees(officer.Tf.position, suspect.Tf.position)) return true;
                }
            }
            return false;
        }

        void ClearFight()
        {
            if (Unit == null) return;
            Unit.TargetUnit = null;
            Unit.OrderedFight = false;
            Unit.Searching = false;
            Unit.LookUntil = 0f;
            Unit.ChaseUntil = 0f;
            Unit.HasLastSeen = false;
            Unit.LastSeenDir = Vector3.zero;
            _fightTarget = null;
            _fightSeenAt = -1000f;
            ProvokedBy = null;
            foreach (var man in Unit.All())
                if (man != null && !man.Dead) man.Disengage();
        }

        void LowerGuns()
        {
            if (Unit == null) return;
            foreach (var man in Unit.All())
                if (man != null) man.LowerGunpoint();
        }

        void GoInside()
        {
            if (!_hasStation) return;
            CrewQuarters.Station(_crews, Unit, _stationDoor, "PRECINCT");
        }

        void OrderNextCorner()
        {
            if (_ring.Count < 2) return;
            _ringAt = (_ringAt + _ringDirection + _ring.Count) % _ring.Count;
            var from = _ring[_ringAt];
            var to = _ring[(_ringAt + _ringDirection + _ring.Count) % _ring.Count];
            var link = Link(from, to) ?? Link(to, from);
            if (link == null) return;
            var t = link.From == from ? link.Length - 0.6f : 0.6f;
            OrderTo(link, t, run: false);
        }

        void OrderTo(PedLink link, float t, bool run)
        {
            var i = 0;
            foreach (var man in Unit.All())
            {
                if (man == null || man.Dead) continue;
                if (!man.OnGraph) Reseat(man);
                man.OrderTo(link, Mathf.Clamp(t - i * 0.8f, 0.3f, link.Length - 0.3f),
                    i * 0.18f);
                man.Urgent = run;
                i++;
            }
        }

        void OrderToPoint(Vector3 point, bool run)
        {
            var dir = Flat(point - Position);
            var side = dir.sqrMagnitude > 0.01f
                ? Vector3.Cross(Vector3.up, dir.normalized)
                : Vector3.right;
            var i = 0;
            foreach (var man in Unit.All())
            {
                if (man == null || man.Dead) continue;
                man.OrderToPoint(point + side * (i == 0 ? -0.8f : 0.8f), i * 0.15f);
                man.Urgent = run;
                i++;
            }
        }

        void StopWhereTheyStand()
        {
            foreach (var man in Unit.All())
                if (man != null && !man.Dead && man.Tf != null)
                    man.OrderToPoint(man.Tf.position);
        }

        bool AnyoneMoving()
        {
            foreach (var man in Unit.All())
                if (man != null && !man.Dead && man.HasOrder) return true;
            return false;
        }

        void ReseatAll()
        {
            foreach (var man in Unit.All()) Reseat(man);
        }

        void Reseat(CrewWalker man)
        {
            if (man == null || man.Dead || man.Tf == null) return;
            var link = NearestLink(man.Tf.position, out var t);
            if (link != null) man.Reseat(link, t);
        }

        PedLink NearestLink(Vector3 point, out float along)
        {
            PedLink best = null;
            along = 0f;
            var bestD = float.MaxValue;
            for (var i = 0; i < _nodes.Count; i++)
                foreach (var link in _nodes[i].Links)
                {
                    if (link == null || link.Length < 0.1f) continue;
                    var ab = link.To.Pos - link.From.Pos;
                    var dir = ab / link.Length;
                    var t = Mathf.Clamp(Vector3.Dot(point - link.From.Pos, dir), 0.3f,
                        link.Length - 0.3f);
                    var d = (link.From.Pos + dir * t - point).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = link; along = t; }
                }
            return best;
        }

        int NearestRing(Vector3 point)
        {
            var at = 0;
            var best = float.MaxValue;
            for (var i = 0; i < _ring.Count; i++)
            {
                var d = Flat(_ring[i].Pos - point).sqrMagnitude;
                if (d < best) { best = d; at = i; }
            }
            return at;
        }

        float RestSeconds() => UnityEngine.Random.Range(
            Mathf.Min(_restRange.x, _restRange.y), Mathf.Max(_restRange.x, _restRange.y));

        static PedLink Link(PedNode from, PedNode to)
        {
            if (from == null || to == null) return null;
            foreach (var link in from.Links) if (link.To == to) return link;
            return null;
        }

        static CrewWalker Standing(DemoCrews.Unit unit)
        {
            if (unit == null) return null;
            if (unit.Boss != null && !unit.Boss.Dead) return unit.Boss;
            foreach (var man in unit.All()) if (man != null && !man.Dead) return man;
            return null;
        }

        static Vector3 Flat(Vector3 value) { value.y = 0f; return value; }

        public static List<PedNode> BeatRing(PedLink forward, PedLink reverse, Vector3 door)
        {
            if (forward == null || reverse == null) return null;
            var dir = Flat(forward.To.Pos - forward.From.Pos);
            if (dir.sqrMagnitude < 1e-4f) return null;
            dir.Normalize();
            var right = new Vector3(dir.z, 0f, -dir.x);
            var first = Vector3.Dot(door - forward.From.Pos, right) > 0f ? forward : reverse;
            var ring = new List<PedNode>();
            var link = first;
            for (var step = 0; step < 512; step++)
            {
                ring.Add(link.From);
                var at = link.To;
                if (at == first.From && ring.Count >= 3) return ring;
                var incoming = Flat(at.Pos - link.From.Pos).normalized;
                PedLink pick = null;
                var best = float.MinValue;
                foreach (var candidate in at.Links)
                {
                    if (candidate.To == link.From && at.Links.Count > 1) continue;
                    var outgoing = Flat(candidate.To.Pos - at.Pos);
                    if (outgoing.sqrMagnitude < 1e-4f) continue;
                    outgoing.Normalize();
                    var turn = Mathf.Atan2(Vector3.Cross(incoming, outgoing).y,
                        Vector3.Dot(incoming, outgoing));
                    if (turn > best) { best = turn; pick = candidate; }
                }
                if (pick == null) return null;
                link = pick;
            }
            return null;
        }

        Transform IPatrolMarker.MarkerTf => Tf;
        float IPatrolMarker.MarkerHeight => Lead != null && Lead.Anthropometry
            ? Lead.Anthropometry.OverlayHeight : 2.1f;
        bool IPatrolMarker.MarkerDimmed => State == Mode.Inside || OffWatch;
        string IPatrolMarker.MarkerTitle => "Officer " + UnitNumber;
        string IPatrolMarker.MarkerLine => State switch
        {
            Mode.Inside => "Inside the station",
            Mode.WalkOut => "Stepping out on patrol",
            Mode.Patrolling => "Walking the beat " + PatrolInfo.Heading(Tf),
            Mode.Returning => "Returning to the station",
            Mode.Responding => "Responding " + PatrolInfo.Toward(Position, _scene) + " of here",
            Mode.OnScene => "At the scene",
            Mode.Arresting => _collar != null ? "Making an arrest — " + _collar.DisplayName : "Making an arrest",
            Mode.Ritual => "Watching the street from the corner",
            Mode.Doorway => "At the doorway",
            _ => "On the beat",
        };
    }
}
