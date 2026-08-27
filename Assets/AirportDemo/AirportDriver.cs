using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// A vehicle on a route. Everything that drives in this demo - the bowser, the
    /// baggage tug, the follow-me, the freight lorries, the cars on the kerb loop and
    /// the traffic on the approach road - is one of these: a body, a list of points,
    /// a speed, and the manners to leave a gap to whoever is in front on the same
    /// route.
    ///
    /// The road demo's own lane graph is not used here on purpose: none of what
    /// drives at an airport wants it. A one-way kerb loop, a ramp service road and a
    /// gate run are routes, not a network with junctions to give way at.
    /// </summary>
    public sealed class AirportDriver
    {
        public Transform Tf;
        public float Speed = 8f;
        public float Cruise = 8f;
        public float HalfLength = 2.4f;
        /// <summary>Metres of clear road wanted in front before moving off again.</summary>
        public float Gap = 6f;
        /// <summary>The route, in order. A closed route wraps; an open one either
        /// stops at the end or jumps back to the start, whichever the caller asked.</summary>
        public List<Vector3> Route = new List<Vector3>();
        public bool Closed;
        public bool Wrap;
        /// <summary>Set while the driver is stopped on purpose - at the kerb, at a
        /// stand, at a dock.</summary>
        public float Dwell;
        /// <summary>Who else is on this road, for the gap.</summary>
        public List<AirportDriver> Traffic;
        /// <summary>Called once when the route runs out (an open route).</summary>
        public System.Action<AirportDriver> OnArrive;
        /// <summary>Called at each point, with its index - what a lorry's loading and
        /// a car's drop-off hang off.</summary>
        public System.Action<AirportDriver, int> OnPoint;

        /// <summary>The route is run out and the driver is stood still. Set by the
        /// caller too, to park a vehicle that has nowhere to be yet.</summary>
        public bool Done { get; set; }
        public int Leg => _leg;
        public Vector3 Position => Tf != null ? Tf.localPosition : Vector3.zero;

        int _leg;
        float _speed;
        readonly RoadDemo.WheelSpin _wheels = new RoadDemo.WheelSpin();
        readonly List<GameObject> _tows = new List<GameObject>();
        readonly List<float> _towOffsets = new List<float>();
        readonly List<Vector3> _breadcrumbs = new List<Vector3>();

        const float Accel = 3.2f, Brake = 5.5f, TurnRate = 110f;

        public void Bind(Transform tf, float halfLength = -1f)
        {
            Tf = tf;
            _wheels.Read(tf);
            if (halfLength > 0f) HalfLength = halfLength;
            else
            {
                var b = AirportKit.BoundsOf(tf.gameObject);
                HalfLength = Mathf.Max(1.2f, b.size.z * 0.5f);
            }
        }

        /// <summary>Hangs a trailer (or three) off the back, each following the tug's
        /// own track a fixed distance behind - which is what a baggage train is.</summary>
        public void Tow(GameObject cart, float behind)
        {
            _tows.Add(cart);
            _towOffsets.Add(behind);
        }

        public void Start(IList<Vector3> route, bool closed = false, bool wrap = false, int leg = 0)
        {
            Route = new List<Vector3>(route);
            Closed = closed;
            Wrap = wrap;
            _leg = Mathf.Clamp(leg, 0, Mathf.Max(0, Route.Count - 1));
            Done = false;
            if (Tf != null && Route.Count > 1)
            {
                Tf.localPosition = Route[_leg];
                var d = Route[(_leg + 1) % Route.Count] - Route[_leg];
                if (d.sqrMagnitude > 0.01f) Tf.localRotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z).normalized, Vector3.up);
                _leg = (_leg + 1) % Route.Count;
            }
        }

        /// <summary>Sends the driver somewhere else entirely - a new route from where
        /// it stands, which is how the bowser is despatched to an aeroplane.</summary>
        public void Divert(IList<Vector3> route)
        {
            Route = new List<Vector3>(route);
            Closed = false;
            Wrap = false;
            _leg = 0;
            Done = false;
        }

        public void Tick(float dt)
        {
            if (Tf == null || Route.Count == 0) return;
            TrailBreadcrumbs();

            if (Dwell > 0f)
            {
                Dwell -= dt;
                _speed = Mathf.MoveTowards(_speed, 0f, Brake * dt);
                Roll(dt);
                return;
            }
            if (Done)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, Brake * dt);
                Roll(dt);
                return;
            }

            var goal = Route[_leg];
            var to = goal - Tf.localPosition;
            to.y = 0f;
            float dist = to.magnitude;

            float want = Cruise;
            // ease into a tight corner so a lorry does not scythe across the kerb
            if (Route.Count > 2 && dist < 14f)
            {
                var next = Route[(_leg + 1) % Route.Count] - goal;
                float turn = Vector3.Angle(to, new Vector3(next.x, 0f, next.z));
                if (turn > 35f) want = Mathf.Min(want, Cruise * 0.4f);
            }
            if (!Closed && !Wrap && _leg == Route.Count - 1)
                want = Mathf.Min(want, Mathf.Sqrt(Mathf.Max(0.4f, dist) * 2f * Brake));
            float ahead = ClearAhead();
            if (ahead < Gap + HalfLength) want = 0f;
            else if (ahead < (Gap + HalfLength) * 2.2f) want = Mathf.Min(want, Cruise * 0.45f);

            _speed = _speed < want ? Mathf.MoveTowards(_speed, want, Accel * dt)
                                   : Mathf.MoveTowards(_speed, want, Brake * dt);

            if (dist > 0.05f)
            {
                float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float yaw = Mathf.MoveTowardsAngle(Tf.localEulerAngles.y, targetYaw, TurnRate * dt);
                Tf.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
            var step = (Tf.localRotation * Vector3.forward) * (_speed * dt);
            var p = Tf.localPosition + step;
            p.y = goal.y;
            Tf.localPosition = p;
            Roll(dt);

            if (dist <= Mathf.Max(1.4f, _speed * 0.4f))
            {
                OnPoint?.Invoke(this, _leg);
                int next = _leg + 1;
                if (next >= Route.Count)
                {
                    if (Closed || Wrap) next = 0;
                    else { Done = true; OnArrive?.Invoke(this); return; }
                }
                _leg = next;
            }
        }

        void Roll(float dt) => _wheels.Tick(dt, _speed, 0f);

        /// <summary>Distance to whoever is next in front on the same road, measured
        /// along our own heading - enough for a one-way route, which is all these are.</summary>
        float ClearAhead()
        {
            if (Traffic == null) return 999f;
            float best = 999f;
            var pos = Tf.localPosition;
            var fwd = (Tf.localRotation * Vector3.forward);
            for (int i = 0; i < Traffic.Count; i++)
            {
                var o = Traffic[i];
                if (o == this || o.Tf == null) continue;
                var d = o.Tf.localPosition - pos;
                d.y = 0f;
                float along = Vector3.Dot(d, fwd);
                if (along <= 0f) continue;
                float across = Vector3.Cross(d, fwd).magnitude;
                if (across > 3.2f) continue;
                best = Mathf.Min(best, along - o.HalfLength);
            }
            return best;
        }

        /// <summary>The tug's own track, kept as a short trail, so a towed cart runs
        /// where the tug ran rather than cutting the corner through the aeroplane.</summary>
        void TrailBreadcrumbs()
        {
            if (_tows.Count == 0) return;
            if (_breadcrumbs.Count == 0 || (Tf.localPosition - _breadcrumbs[0]).sqrMagnitude > 0.25f)
            {
                _breadcrumbs.Insert(0, Tf.localPosition);
                if (_breadcrumbs.Count > 120) _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
            }
            for (int i = 0; i < _tows.Count; i++)
            {
                if (_tows[i] == null) continue;
                float want = _towOffsets[i];
                float run = 0f;
                var at = Tf.localPosition;
                var face = -(Tf.localRotation * Vector3.forward);
                for (int k = 1; k < _breadcrumbs.Count; k++)
                {
                    float seg = (_breadcrumbs[k] - _breadcrumbs[k - 1]).magnitude;
                    if (run + seg >= want)
                    {
                        float t = (want - run) / Mathf.Max(seg, 0.0001f);
                        at = Vector3.Lerp(_breadcrumbs[k - 1], _breadcrumbs[k], t);
                        face = (_breadcrumbs[k - 1] - _breadcrumbs[k]).normalized;
                        break;
                    }
                    run += seg;
                    at = _breadcrumbs[k];
                }
                _tows[i].transform.localPosition = at;
                if (face.sqrMagnitude > 0.001f)
                    _tows[i].transform.localRotation = Quaternion.LookRotation(new Vector3(-face.x, 0f, -face.z), Vector3.up);
            }
        }
    }
}
