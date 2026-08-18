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

    // The clip wardrobe a walker carries. Walk and Idle are mandatory; the rest
    // are optional and simply gate the behaviours that use them - an agent
    // without sit clips never sits, one without a talk clip never chats.
    public struct PedClips
    {
        public AnimationClip Walk, Idle, SitDown, SitLoop, StandUp, Talk, Shout;

        // The gun wardrobe (the outfit's men): gun held low, gun up, the shot, the
        // flinch, the fall. Optional like the rest - an unarmed walker never asks.
        public AnimationClip PistolIdle, Aim, Shoot, Hit, Death;

        // The run a man breaks into closing on a fight. Optional: without it he walks.
        public AnimationClip Jog;
    }

    public class PedestrianAgent
    {
        const float CrossHustle = 1.35f;

        // Mixer input per pose; a pose whose clip was not provided stays invalid
        // and SetPose refuses to select it.
        public const int PoseWalk = 0, PoseIdle = 1, PoseSitDown = 2, PoseSit = 3,
            PoseStandUp = 4, PoseTalk = 5, PoseShout = 6,
            PosePistolIdle = 7, PoseAim = 8, PoseShoot = 9, PoseHit = 10, PoseDeath = 11,
            PoseJog = 12;
        const int PoseCount = 13;

        // Clips cut straight out of an FBX (the pistol set) carry no loop flag, so
        // a loop pose is wrapped by hand in TickBlend; the .anim files loop themselves.
        static readonly bool[] LoopByHand = MakeLoopTable();

        static bool[] MakeLoopTable()
        {
            var table = new bool[PoseCount];
            // the .anim loops carry their own flag; wrapping them by hand as well is
            // harmless, and it covers a walk drawn out of the FBX (Walk_Loop) too
            table[PoseWalk] = true;
            table[PoseIdle] = true;
            table[PoseTalk] = true;
            table[PoseShout] = true;
            table[PosePistolIdle] = true;
            table[PoseAim] = true;
            table[PoseJog] = true;
            return table;
        }

        public Transform Tf;
        public float Speed = 1.5f;

        protected PedLink _link;
        protected PedNode _cameFrom;
        protected float _t;
        protected bool _waiting;
        /// <summary>Humanoid retarget scale - carries a child rig's sit height.</summary>
        protected float HumanScale = 1f;
        float _repickTimer;
        float _lateral; // keeps opposing walkers off each other's line

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        readonly AnimationClipPlayable[] _poses = new AnimationClipPlayable[PoseCount];
        readonly float[] _weights = new float[PoseCount];
        int _pose = PoseWalk;

        public void Init(Transform tf, AnimationClip walk, AnimationClip idle, PedLink start, float t)
            => Init(tf, new PedClips { Walk = walk, Idle = idle }, start, t);

        public void Init(Transform tf, PedClips clips, PedLink start, float t)
        {
            Setup(tf, clips);
            _link = start;
            _cameFrom = start.From;
            _t = t;
            Move(0f);
        }

        /// <summary>A walker off the graph entirely: stood at a point, no link under
        /// him. Tick does nothing for him beyond the pose blend - whoever put him
        /// there moves him (the crews' free stride on the empty demo floor).</summary>
        public void InitAt(Transform tf, PedClips clips, Vector3 position, Quaternion rotation)
        {
            Setup(tf, clips);
            _link = null;
            _cameFrom = null;
            _t = 0f;
            Tf.SetPositionAndRotation(position, rotation);
        }

        void Setup(Transform tf, PedClips clips)
        {
            Tf = tf;
            _lateral = Random.Range(-0.7f, 0.7f); // kerb strip now carries palms/lamps at half+1.15

            var animator = tf.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                HumanScale = animator.avatar != null && animator.avatar.isHuman
                    ? animator.humanScale : 1f;
                _graph = PlayableGraph.Create("Pedestrian");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
                _mixer = AnimationMixerPlayable.Create(_graph, PoseCount);

                void Wire(int pose, AnimationClip clip)
                {
                    if (clip == null) return;
                    var playable = AnimationClipPlayable.Create(_graph, clip);
                    _graph.Connect(playable, 0, _mixer, pose);
                    _mixer.SetInputWeight(pose, 0f);
                    _poses[pose] = playable;
                }

                Wire(PoseWalk, clips.Walk);
                Wire(PoseIdle, clips.Idle);
                Wire(PoseSitDown, clips.SitDown);
                Wire(PoseSit, clips.SitLoop);
                Wire(PoseStandUp, clips.StandUp);
                Wire(PoseTalk, clips.Talk);
                Wire(PoseShout, clips.Shout);
                Wire(PosePistolIdle, clips.PistolIdle);
                Wire(PoseAim, clips.Aim);
                Wire(PoseShoot, clips.Shoot);
                Wire(PoseHit, clips.Hit);
                Wire(PoseDeath, clips.Death);
                Wire(PoseJog, clips.Jog);

                // every loop starts somewhere along itself, not at frame one - a line of
                // men breathing, walking or jogging in lockstep reads as a machine
                for (int i = 0; i < PoseCount; i++)
                    if (LoopByHand[i] && _poses[i].IsValid())
                        _poses[i].SetTime(Random.value * _poses[i].GetAnimationClip().length);
                if (_poses[PoseWalk].IsValid())
                    _poses[PoseWalk].SetSpeed(Speed / 1.5f);
                _weights[PoseWalk] = 1f;
                _mixer.SetInputWeight(PoseWalk, 1f);
                output.SetSourcePlayable(_mixer);
                _graph.Play();
            }
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
            if (_link == null)
            {
                BlendLocomotion(dt, false);
                return;
            }

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

            BlendLocomotion(dt, !_waiting);

            if (_waiting) return;
            Move(dt);
        }

        // ------------------------------------------------------------- posing

        protected bool HasPose(int pose) => _poses[pose].IsValid();

        /// <summary>The kerb-strip offset a walker keeps off the link's centre line;
        /// a crew sets it by hand so its men do not all share one line.</summary>
        protected float Lateral { get => _lateral; set => _lateral = value; }

        /// <summary>Stood at a crossing waiting on the light (t = 0 on a gated link).</summary>
        protected bool Waiting { get => _waiting; set => _waiting = value; }

        /// <summary>Drop the current link and choose afresh from this node - a walker
        /// given new orders while it stands at a light asks its own ChooseLink again.</summary>
        protected void Reroute(PedNode node)
        {
            _waiting = false;
            PickNext(node, keepAwayFrom: null);
        }

        /// <summary>Select the pose the blend drifts toward; a pose with no clip is refused.</summary>
        protected void SetPose(int pose)
        {
            if (_poses[pose].IsValid()) _pose = pose;
        }

        /// <summary>Rewind a one-shot (sit down, stand up) before blending it in.</summary>
        protected void RestartPose(int pose, float atTime = 0f, float speed = 1f)
        {
            if (!_poses[pose].IsValid()) return;
            _poses[pose].SetTime(atTime);
            _poses[pose].SetSpeed(speed);
        }

        /// <summary>Playback rate of a pose - the walk's is tied to Speed, and a
        /// derived agent that changes pace mid-life re-ties it here.</summary>
        protected void SetPoseSpeed(int pose, float speed)
        {
            if (_poses[pose].IsValid()) _poses[pose].SetSpeed(speed);
        }

        /// <summary>Seconds into the pose's clip, and the clip's length - a one-shot
        /// (the shot, the flinch, the fall) is over when the first passes the second.</summary>
        protected float PoseTime(int pose) => _poses[pose].IsValid() ? (float)_poses[pose].GetTime() : 0f;
        protected float PoseLength(int pose) =>
            _poses[pose].IsValid() ? _poses[pose].GetAnimationClip().length : 0f;

        /// <summary>Freeze a pose on its current frame - the fall stays fallen.</summary>
        protected void HoldPose(int pose)
        {
            if (_poses[pose].IsValid()) _poses[pose].SetSpeed(0f);
        }

        /// <summary>The selected pose, for derived agents that branch on it.</summary>
        protected int CurrentPose => _pose;

        /// <summary>Drift every mixer weight toward the selected pose.</summary>
        protected void TickBlend(float dt)
        {
            if (!_mixer.IsValid()) return;
            for (int i = 0; i < PoseCount; i++)
            {
                if (!_poses[i].IsValid()) continue;
                if (LoopByHand[i])
                {
                    float len = _poses[i].GetAnimationClip().length;
                    if (len > 0.01f && _poses[i].GetTime() >= len)
                        _poses[i].SetTime(_poses[i].GetTime() % len);
                }
                float target = i == _pose ? 1f : 0f;
                if (Mathf.Approximately(_weights[i], target)) continue;
                _weights[i] = Mathf.MoveTowards(_weights[i], target, 4f * dt);
                _mixer.SetInputWeight(i, _weights[i]);
            }
        }

        // The walk/idle crossfade, callable by derived agents that hand-animate
        // legs off the graph (the foot patrol's door walk) without running Tick.
        protected void BlendLocomotion(float dt, bool walking)
        {
            SetPose(walking ? PoseWalk : PoseIdle);
            TickBlend(dt);
        }

        void Move(float dt)
        {
            float speed = _link.Gated ? Speed * CrossHustle : Speed;
            _t += speed * dt;
            if (_t >= _link.Length)
            {
                var arrived = _link.To;
                _cameFrom = _link.From;
                if (OnArrived(arrived))
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

        /// Called once per node reached; return false to keep the agent off the
        /// next link (a derived agent taking over - the foot patrol turning in).
        protected virtual bool OnArrived(PedNode node) => true;

        void PickNext(PedNode node, PedNode keepAwayFrom)
        {
            var pick = ChooseLink(node, keepAwayFrom);
            if (pick == null) return;

            _link = pick;
            _t = 0f;
            if (!MayEnter(pick))
            {
                _waiting = true;
                _repickTimer = 4f;
            }
        }

        // The default walker: weighted random wander that avoids doubling back
        // and undersells zebra crossings. The foot patrol substitutes a routed
        // choice on its way home.
        protected virtual PedLink ChooseLink(PedNode node, PedNode keepAwayFrom)
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
            return pick;
        }
    }
}
