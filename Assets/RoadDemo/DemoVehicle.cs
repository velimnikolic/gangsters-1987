using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public enum Turn { Straight, Left, Right }

    // A car riding the lane graph. Progress s runs from -curveLength (crossing the
    // intersection that leads onto the current edge) up to edge.Length, and the car
    // is listed in the target edge's Cars from the moment it enters the intersection,
    // so the same gap arithmetic keeps followers apart across intersections too.
    public class DemoVehicle
    {
        const float Accel = 3.5f;
        const float Brake = 6.5f;
        const float MinGap = 2.2f;
        const float TurnSpeed = 6f;
        const float StopBeforeBox = 5.7f; // zebra band plus a little air

        public Transform Tf;
        public float HalfLen;

        protected RoadEdge _edge;
        protected float _s;
        protected float _speed;
        Turn _turn;
        RoadEdge _next;

        public RoadEdge CurrentEdge => _edge;
        public float Progress => _s;

        /// <summary>Metres a second, for whoever is listening (DemoAudio pitches
        /// the engine on it, and only cars stopped dead lean on the horn).</summary>
        public float Speed => _speed;

        Vector3 _p0, _p1, _p2; // curve through the intersection that led onto _edge
        float _curveLen = 1f;
        float _curveSpeed;

        public void Spawn(RoadEdge edge, float s)
        {
            _edge = edge;
            _s = s;
            _speed = edge.SpeedLimit * 0.5f;
            _p0 = _p1 = _p2 = edge.Start;
            _curveSpeed = TurnSpeed;
            edge.Cars.Add(this);
            PlanNext();
            Place();
        }

        // scratch for PlanNext, shared: it is read out synchronously in PickNext, and
        // two hundred cars entering junctions must not each hand the GC two lists
        static readonly List<RoadEdge> Lefts = new List<RoadEdge>();
        static readonly List<RoadEdge> Rights = new List<RoadEdge>();

        void PlanNext()
        {
            var node = _edge.To;
            RoadEdge straight = null;
            var lefts = Lefts;
            var rights = Rights;
            lefts.Clear();
            rights.Clear();
            for (int i = 0; i < node.Outgoing.Count; i++)
            {
                var e = node.Outgoing[i];
                float dot = Vector3.Dot(e.Dir, _edge.Dir);
                if (dot < -0.5f) continue; // no U-turns
                if (dot > 0.5f)
                {
                    // several lanes may continue straight; keep the closest one
                    if (straight == null ||
                        (e.Start - _edge.End).sqrMagnitude < (straight.Start - _edge.End).sqrMagnitude)
                        straight = e;
                }
                else if (Vector3.Cross(_edge.Dir, e.Dir).y > 0f) rights.Add(e);
                else lefts.Add(e);
            }

            _next = PickNext(straight, lefts, rights);
            _turn = _next == null || Vector3.Dot(_next.Dir, _edge.Dir) > 0.5f
                ? Turn.Straight
                : Vector3.Cross(_edge.Dir, _next.Dir).y > 0f ? Turn.Right : Turn.Left;
        }

        // The default driver: random wander biased to straight, then right. A derived
        // driver (the patrol car) substitutes a routed choice; candidates are already
        // filtered for U-turns and the turn geometry is derived from whatever it picks.
        protected virtual RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            float roll = Random.value;
            if (straight != null && (roll < 0.55f || (lefts.Count == 0 && rights.Count == 0)))
                return straight;
            if (rights.Count > 0 && (roll < 0.8f || lefts.Count == 0))
                return rights[Random.Range(0, rights.Count)];
            if (lefts.Count > 0)
                return lefts[Random.Range(0, lefts.Count)];
            return straight;
        }

        protected static float Allowed(float endSpeed, float dist)
            => Mathf.Sqrt(endSpeed * endSpeed + 2f * Brake * Mathf.Max(0f, dist));

        public void Tick(float dt)
        {
            float target = _s < 0f ? Mathf.Max(_curveSpeed, 2f) : _edge.SpeedLimit;

            if (_s >= 0f && _next != null)
            {
                float nextCurveSpeed = _turn == Turn.Straight ? _edge.SpeedLimit : TurnSpeed;
                target = Mathf.Min(target, Allowed(nextCurveSpeed, _edge.Length - _s));

                float stopS = _edge.Length - StopBeforeBox - HalfLen;
                float commitS = stopS + 1.6f;
                if (_s < commitS)
                {
                    var sig = _edge.To.Signal;
                    bool hold = false;
                    if (sig != null)
                    {
                        if (sig.GreenFor(_edge.NorthSouth))
                            hold = _turn == Turn.Left && OncomingPriority();
                        else if (sig.YellowFor(_edge.NorthSouth))
                            hold = stopS - _s > _speed * _speed / (2f * Brake) + 2f;
                        else
                            hold = true;
                    }
                    if (hold) target = Mathf.Min(target, Allowed(0f, stopS - _s));
                }

                // a car still crossing our intersection towards our next edge has
                // already left this edge's list; keep the gap to it anyway
                var ahead = _next.Cars;
                for (int i = 0; i < ahead.Count; i++)
                {
                    var c = ahead[i];
                    if (c == this || c._s >= 0f) continue;
                    float leadDist = (_edge.Length - _s) + (c._s + c._curveLen);
                    float gap = leadDist - c.HalfLen - HalfLen - MinGap;
                    target = Mathf.Min(target, Allowed(0f, gap));
                }
            }

            DemoVehicle lead = null;
            float leadS = float.MaxValue;
            var cars = _edge.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != this && c._s > _s && c._s < leadS) { lead = c; leadS = c._s; }
            }
            if (lead != null)
            {
                float gap = leadS - _s - lead.HalfLen - HalfLen - MinGap;
                target = Mathf.Min(target, Allowed(0f, gap));
            }

            target = LimitTarget(target);

            _speed = Mathf.MoveTowards(_speed, target, (target < _speed ? Brake : Accel) * dt);
            _s += _speed * dt;

            if (_s >= _edge.Length && _next != null) EnterNode();
            Place();
        }

        /// A derived driver's last word on the target speed - how the patrol car
        /// rolls to a stop at its kerb, with the same braking curve signals use.
        protected virtual float LimitTarget(float target) => target;

        // Leave the lane graph (a patrol car swinging into its forecourt). The
        // owner must stop calling Tick until the next Spawn.
        public void Despawn()
        {
            if (_edge != null) _edge.Cars.Remove(this);
        }

        bool OncomingPriority()
        {
            var node = _edge.To;
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (Vector3.Dot(e.Dir, _edge.Dir) > -0.5f) continue;
                var cars = e.Cars;
                for (int k = 0; k < cars.Count; k++)
                {
                    var c = cars[k];
                    if (c._turn != Turn.Straight) continue;
                    float dist = e.Length - c._s;
                    if (dist < 28f) return true;
                }
            }
            return false;
        }

        void EnterNode()
        {
            float overshoot = _s - _edge.Length;
            _edge.Cars.Remove(this);

            _p0 = _edge.End;
            _p2 = _next.Start;
            _p1 = _turn == Turn.Straight
                ? (_p0 + _p2) * 0.5f
                : _edge.NorthSouth
                    ? new Vector3(_edge.End.x, 0f, _next.Start.z)
                    : new Vector3(_next.Start.x, 0f, _edge.End.z);
            _curveLen = (_p1 - _p0).magnitude + (_p2 - _p1).magnitude;
            _curveSpeed = _turn == Turn.Straight ? _next.SpeedLimit : TurnSpeed;

            _edge = _next;
            _edge.Cars.Add(this);
            _s = -_curveLen + overshoot;
            PlanNext();
        }

        void Place()
        {
            Vector3 pos, fwd;
            if (_s < 0f)
            {
                float t = Mathf.Clamp01((_s + _curveLen) / _curveLen);
                Vector3 a = Vector3.Lerp(_p0, _p1, t);
                Vector3 b = Vector3.Lerp(_p1, _p2, t);
                pos = Vector3.Lerp(a, b, t);
                Vector3 tangent = b - a;
                fwd = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : _edge.Dir;
            }
            else
            {
                pos = _edge.Start + _edge.Dir * _s;
                fwd = _edge.Dir;
            }
            Tf.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
        }
    }
}
