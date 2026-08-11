using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadDemo
{
    public class PedNode
    {
        public Vector3 Pos;
        public readonly List<PedLink> Links = new List<PedLink>();
    }

    // One walkable direction between two pedestrian nodes. Gated links are zebra
    // crossings: they may only be entered while the blocking car axis shows red
    // and enough red time remains to finish (or reach the median refuge).
    public class PedLink
    {
        public PedNode From, To;
        public float Length;
        public bool Gated;
        public bool BlocksNorthSouth; // axis of the cars driving over this crossing
        public TrafficSignal Signal;
    }

    public class PedestrianAgent
    {
        const float CrossHustle = 1.35f;

        public Transform Tf;
        public float Speed = 1.5f;

        PedLink _link;
        PedNode _cameFrom;
        float _t;
        bool _waiting;
        float _repickTimer;
        float _lateral; // keeps opposing walkers off each other's line

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        float _walkWeight = 1f;

        public void Init(Transform tf, AnimationClip walk, AnimationClip idle, PedLink start, float t)
        {
            Tf = tf;
            _link = start;
            _cameFrom = start.From;
            _t = t;
            _lateral = Random.Range(-0.7f, 0.7f); // kerb strip now carries palms/lamps at half+1.15

            var animator = tf.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                _graph = PlayableGraph.Create("Pedestrian");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
                _mixer = AnimationMixerPlayable.Create(_graph, 2);
                var walkP = AnimationClipPlayable.Create(_graph, walk);
                walkP.SetTime(Random.value * walk.length);
                walkP.SetSpeed(Speed / 1.5f);
                var idleP = AnimationClipPlayable.Create(_graph, idle);
                _graph.Connect(walkP, 0, _mixer, 0);
                _graph.Connect(idleP, 0, _mixer, 1);
                _mixer.SetInputWeight(0, 1f);
                _mixer.SetInputWeight(1, 0f);
                output.SetSourcePlayable(_mixer);
                _graph.Play();
            }
            Move(0f);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        bool MayEnter(PedLink link)
        {
            if (!link.Gated || link.Signal == null) return true;
            float speed = Speed * CrossHustle;
            return link.Signal.RedRemaining(link.BlocksNorthSouth) > link.Length / speed + 1f;
        }

        public void Tick(float dt)
        {
            if (_waiting)
            {
                if (MayEnter(_link))
                {
                    _waiting = false;
                }
                else
                {
                    _repickTimer -= dt;
                    if (_repickTimer <= 0f)
                    {
                        _repickTimer = 4f;
                        if (Random.value < 0.3f) PickNext(_link.From, keepAwayFrom: null);
                    }
                }
            }

            float target = _waiting ? 0f : 1f;
            if (_mixer.IsValid() && !Mathf.Approximately(_walkWeight, target))
            {
                _walkWeight = Mathf.MoveTowards(_walkWeight, target, 4f * dt);
                _mixer.SetInputWeight(0, _walkWeight);
                _mixer.SetInputWeight(1, 1f - _walkWeight);
            }

            if (_waiting) return;
            Move(dt);
        }

        void Move(float dt)
        {
            float speed = _link.Gated ? Speed * CrossHustle : Speed;
            _t += speed * dt;
            if (_t >= _link.Length)
            {
                var arrived = _link.To;
                _cameFrom = _link.From;
                PickNext(arrived, keepAwayFrom: _cameFrom);
                return;
            }

            float f = _t / _link.Length;
            Vector3 pos = Vector3.Lerp(_link.From.Pos, _link.To.Pos, f);
            if (_link.Gated) pos.y -= 0.08f * Mathf.Sin(Mathf.PI * f); // dip onto the asphalt
            Vector3 dir = _link.To.Pos - _link.From.Pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Vector3 dirN = dir.normalized;
                pos += new Vector3(dirN.z, 0f, -dirN.x) * _lateral;
                Tf.rotation = Quaternion.Slerp(
                    Tf.rotation, Quaternion.LookRotation(dirN), dt <= 0f ? 1f : 8f * dt);
            }
            Tf.position = pos;
        }

        void PickNext(PedNode node, PedNode keepAwayFrom)
        {
            PedLink pick = null;
            float total = 0f;
            for (int i = 0; i < node.Links.Count; i++)
            {
                var l = node.Links[i];
                float w = l.Gated ? 0.35f : 1f;
                if (keepAwayFrom != null && l.To == keepAwayFrom) w *= 0.15f;
                total += w;
                if (Random.value * total <= w) pick = l; // reservoir pick
            }
            if (pick == null) return;

            _link = pick;
            _t = 0f;
            if (!MayEnter(pick))
            {
                _waiting = true;
                _repickTimer = 4f;
            }
        }
    }
}
