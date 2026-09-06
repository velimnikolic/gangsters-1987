using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// A man with a job on the field, or a passenger with somewhere to be. The road
    /// demo's walker drives the body - walk and idle blended through its playable
    /// graph - and this only says where to go, which is the harbour's dock hand and
    /// the same trick the crews use on the empty floor.
    ///
    /// A passenger's round is the whole of 1987 air travel at a county field: in at
    /// the kerb, across the hall, out of the gate door, across the ramp on his own
    /// two feet, up the steps. No airbridge, no carousel, no security worth the name.
    ///
    /// Like everything that moves on the field, he works in the coordinates of the
    /// Live root he hangs under - the field's own plan - which the city carries onto
    /// a shore; only the world's obstacle field (the parked cars, the sheds) is
    /// turned into those coordinates for the step round it.
    /// </summary>
    public sealed class AirportWalker : PedestrianAgent
    {
        public List<Vector3> Points = new List<Vector3>();
        public Vector2 DwellRange = new Vector2(3f, 12f);
        /// <summary>A man at his post - the marshaller, the man on the gate.</summary>
        public bool Static;
        /// <summary>Walks the round once and stops at the end rather than going round
        /// and round: a passenger boarding.</summary>
        public bool OneWay;
        public System.Action<AirportWalker> OnFinished;

        int _target;
        float _dwell;
        bool _walking;
        /// <summary>Which way round an obstacle he committed to last frame, so he does
        /// not dither left and right in front of it.</summary>
        int _side;
        // THE PROBE EVERY OTHER FRAME. Forty of these extras walk the field and every one
        // of them asked the obstacle field for a fresh line every frame - the single
        // biggest cost of the whole airport (measured 2026-09-06). A man crossing a ramp
        // at walking pace moves five centimetres between frames; the line he was given
        // last frame, with the clearance it came with less the step he took, is the same
        // answer. Odd and even walkers take turns so the load is level.
        Vector3 _steer;
        float _steerClear;
        int _steerFrame = -1;
        static int _steerParity;
        readonly int _steerTurn = _steerParity++ & 1;
        static readonly System.Random Rng = new System.Random(1987);

        public void Begin(bool atFirst = false)
        {
            _target = 0;
            _dwell = AirportKit.Range(Rng, 0.5f, 3f);
            if (Points.Count == 0) return;
            if (!atFirst) _target = Rng.Next(Points.Count);
            Tf.localPosition = Points[_target];
            _target = (_target + 1) % Points.Count;
        }

        public new void Tick(float dt)
        {
            if (Tf == null) return;
            if (Static || Points.Count == 0)
            {
                BlendLocomotion(dt, false);
                return;
            }
            if (!_walking)
            {
                _dwell -= dt;
                if (_dwell <= 0f) _walking = true;
                BlendLocomotion(dt, false);
                return;
            }
            var goal = Points[_target];
            var to = goal - Tf.localPosition;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.3f)
            {
                _walking = false;
                _dwell = AirportKit.Range(Rng, DwellRange.x, DwellRange.y);
                int next = _target + 1;
                if (next >= Points.Count)
                {
                    if (OneWay) { OnFinished?.Invoke(this); Static = true; return; }
                    next = 0;
                }
                _target = next;
                BlendLocomotion(dt, false);
                return;
            }
            // round whatever is in the way - the buildings, the props, the parked cars:
            // the obstacle field is the world's, so the question is asked in the world
            // and the answer turned back into the field's own coordinates
            var want = to / dist;
            var parent = Tf.parent;
            float clear;
            int frame = Time.frameCount;
            if (_steerFrame == frame - 1 && (frame & 1) != _steerTurn && _steerClear > 0.2f)
            {
                want = _steer;
                clear = _steerClear;
            }
            else
            {
                var wantWorld = parent != null ? parent.TransformDirection(want) : want;
                var steer = WalkObstacles.Steer(Tf.position, wantWorld, wantWorld, 0.45f, 4f, ref _side, out clear);
                if (steer.sqrMagnitude > 0.001f) want = parent != null ? parent.InverseTransformDirection(steer) : steer;
            }
            var step = want * Mathf.Min(dist, Mathf.Min(clear, Speed * dt));
            _steer = want;
            _steerClear = clear - step.magnitude;
            _steerFrame = frame;
            var p = Tf.localPosition + step;
            p.y = goal.y;
            Tf.localPosition = p;
            Tf.localRotation = Quaternion.Slerp(Tf.localRotation, Quaternion.LookRotation(want, Vector3.up), dt * 6f);
            BlendLocomotion(dt, true);
        }
    }

    /// <summary>
    /// Everybody on the field: the ramp crew round the aeroplanes, the marshaller on
    /// the commuter stand, the men at the freight shed, the passengers walking out to
    /// board, the people on the kerb, and the law.
    /// </summary>
    public sealed class AirportPeople
    {
        readonly List<AirportWalker> _walkers = new List<AirportWalker>();
        readonly Transform _root;

        public AirportPeople(Transform root) => _root = root;

        public AirportWalker Adopt(AirportWalker w)
        {
            _walkers.Add(w);
            return w;
        }

        public int Count => _walkers.Count;

        public void Tick(float dt)
        {
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Tick(dt);
        }

        public void Dispose()
        {
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Dispose();
            _walkers.Clear();
        }
    }
}
