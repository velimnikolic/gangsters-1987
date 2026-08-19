using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // A man with a job on the quay: walks a round of points - the gangway's foot,
    // the pallet pile, the shed door, a smoke by the drum - stands a while at each,
    // walks on. The road demo's walker base drives the body (walk and idle blended
    // through its playable graph); this only says where to go and moves the
    // transform, the same trick the crews use on their empty floor. A deckhand is
    // the same man with his points in his ship's frame - parented to the model, he
    // rides its heave and goes where it goes.
    public sealed class HarborWorker : RoadDemo.PedestrianAgent
    {
        public Transform Frame;          // null: world points; a ship's model: deck points
        public List<Vector3> Points = new List<Vector3>();
        public Vector2 DwellRange = new Vector2(2.5f, 8f);
        /// <summary>A man who stands at his post (the captain on his wing).</summary>
        public bool Static;

        int _target;
        float _dwell;
        bool _walking;

        /// <summary>After InitAt: stands him at one of his points and starts his round.
        /// (Speed is read by the base at InitAt - set it before.)</summary>
        public void Begin()
        {
            _target = 0;
            _dwell = HarborKit.Range(_rng, 0.5f, 3f);
            if (Points.Count > 0)
            {
                _target = _rng.Next(Points.Count);
                Tf.position = Resolve(Points[_target]);
                _target = (_target + 1) % Points.Count;
            }
        }

        static readonly System.Random _rng = new System.Random(7);

        Vector3 Resolve(Vector3 p) => Frame != null ? Frame.TransformPoint(p) : p;

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
                if (_dwell <= 0f)
                {
                    _walking = true;
                    if (Points.Count > 2 && _rng.NextDouble() < 0.3)
                        _target = _rng.Next(Points.Count);   // not always the next one round
                }
                BlendLocomotion(dt, false);
                return;
            }
            var goal = Resolve(Points[_target]);
            var pos = Tf.position;
            var to = goal - pos;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.25f)
            {
                _walking = false;
                _dwell = HarborKit.Range(_rng, DwellRange.x, DwellRange.y);
                _target = (_target + 1) % Points.Count;
                BlendLocomotion(dt, false);
                return;
            }
            var step = to / dist * Mathf.Min(dist, Speed * dt);
            var next = pos + step;
            next.y = goal.y;
            Tf.position = next;
            var face = Quaternion.LookRotation(to / dist, Vector3.up);
            Tf.rotation = Quaternion.Slerp(Tf.rotation, face, dt * 6f);
            BlendLocomotion(dt, true);
        }
    }
}
