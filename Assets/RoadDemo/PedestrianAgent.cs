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

        // ---------------------------------------------------- what is left to walk on
        // The pavement this stretch runs down carries palms, bins, benches and
        // hedges, and a walker has to fit between them. The builder samples the
        // laid props once (RoadDemoBuilder.BuildWalkClearance) into one bitmask
        // per station along the stretch: bit k set means a walker's shoulders
        // clear everything at that station on lateral slot k.

        public const float SlotStep = 0.25f;
        public const int Slots = 17;      // +- 2 m off the centre line of the walk
        public const float Station = 1.5f;

        /// <summary>Free slots per station, or null where nothing was sampled.</summary>
        public int[] Free;

        public static float SlotLateral(int k) => (k - (Slots - 1) / 2) * SlotStep;

        /// <summary>Read the laid props into this stretch: which lateral slots a
        /// walker of <paramref name="radius"/> can hold at each station. A station
        /// owns the half-interval to either side of it and a slot is free only if
        /// it is free over the WHOLE interval - sampled every quarter metre, so a
        /// lamp post or a parking meter between two stations is seen and not
        /// walked through. Done once at build; the crowd pays nothing for it.</summary>
        public void SampleClearance(SidewalkPlan plan, float radius)
        {
            var span = To.Pos - From.Pos;
            span.y = 0f;
            float len = span.magnitude;
            if (len < 0.01f || plan == null) { Free = null; return; }
            var dir = span / len;
            var right = new Vector3(dir.z, 0f, -dir.x);

            const float Sub = 0.25f;
            int stations = Mathf.CeilToInt(len / Station) + 1;
            var free = new int[stations];
            for (int s = 0; s < stations; s++)
            {
                float from = Mathf.Max(0f, s * Station - Station * 0.5f);
                float to = Mathf.Min(len, s * Station + Station * 0.5f);
                int mask = (1 << Slots) - 1;
                for (float u = from; u <= to + 0.001f; u += Sub)
                {
                    var at = From.Pos + dir * Mathf.Min(u, len);
                    for (int k = 0; k < Slots; k++)
                    {
                        if ((mask & (1 << k)) == 0) continue;
                        var p = at + right * SlotLateral(k);
                        if (plan.Occupied(new Vector2(p.x, p.z), radius)) mask &= ~(1 << k);
                    }
                    if (mask == 0) break;
                }
                free[s] = mask;
            }
            Free = free;
        }

        /// <summary>The nearest line to <paramref name="want"/> a walker can hold
        /// from here to <paramref name="ahead"/> metres on - every station over
        /// that span at once, so he holds the line PAST the bin instead of cutting
        /// back into it the moment its station is behind him. Returns what was
        /// asked for where nothing is known or nothing is free.</summary>
        public float FreeLine(float t, float ahead, float want)
        {
            if (Free == null || Free.Length == 0) return want;
            // each station owns the half-interval either side of it (SampleClearance):
            // the station he is in, to the one that reaches past the look-ahead
            int s0 = Mathf.Clamp(Mathf.FloorToInt((t + Station * 0.5f) / Station), 0, Free.Length - 1);
            int s1 = Mathf.Clamp(Mathf.CeilToInt((t + ahead - Station * 0.5f) / Station), s0, Free.Length - 1);

            int mask = -1;
            for (int s = s0; s <= s1; s++) mask &= Free[s];
            // no single line clears the whole span (a tree at one station, a bin at
            // the next): take the one he is walking into, then the one he is on
            if (mask == 0) mask = Free[s1];
            if (mask == 0) mask = Free[s0];
            if (mask == 0) return want;

            int centre = (Slots - 1) / 2;
            int wanted = Mathf.Clamp(Mathf.RoundToInt(want / SlotStep) + centre, 0, Slots - 1);

            // a slot with its neighbours free as well: room to walk down, not a
            // gap to squeeze through with a shoulder in the hedge
            int roomy = mask & (mask << 1) & (mask >> 1);
            if (roomy != 0)
            {
                if ((roomy & (1 << wanted)) != 0) return want;
                for (int d = 1; d < Slots; d++)
                {
                    int a = wanted - d, b = wanted + d;
                    if (a >= 0 && (roomy & (1 << a)) != 0) return SlotLateral(a);
                    if (b < Slots && (roomy & (1 << b)) != 0) return SlotLateral(b);
                }
            }

            if ((mask & (1 << wanted)) != 0) return want;
            for (int d = 1; d < Slots; d++)
            {
                int a = wanted - d, b = wanted + d;
                if (a >= 0 && (mask & (1 << a)) != 0) return SlotLateral(a);
                if (b < Slots && (mask & (1 << b)) != 0) return SlotLateral(b);
            }
            return want;
        }
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

        // Down with the head in - the crowd's cower under fire. Optional.
        public AnimationClip Crouch;
    }

    public class PedestrianAgent
    {
        const float CrossHustle = 1.35f;

        // Mixer input per pose; a pose whose clip was not provided stays invalid
        // and SetPose refuses to select it.
        public const int PoseWalk = 0, PoseIdle = 1, PoseSitDown = 2, PoseSit = 3,
            PoseStandUp = 4, PoseTalk = 5, PoseShout = 6,
            PosePistolIdle = 7, PoseAim = 8, PoseShoot = 9, PoseHit = 10, PoseDeath = 11,
            PoseJog = 12, PoseCrouch = 13;
        const int PoseCount = 14;

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
            table[PoseCrouch] = true;
            return table;
        }

        public Transform Tf;
        public float Speed = 1.5f;

        // ---- the black box (DriveTrace): who he is, and whether he is getting anywhere
        static int _ids;
        public readonly int Id = ++_ids;
        /// <summary>What he is in the trace: crowd, crew, police.</summary>
        public string Tag = "crowd";
        int _traceFrame;
        float _traceNext, _traceStill, _traceSaid;
        Vector3 _tracePrev;
        bool _traceStarted;

        protected PedLink _link;
        protected PedNode _cameFrom;
        protected float _t;
        protected bool _waiting;
        /// <summary>Humanoid retarget scale - carries a child rig's sit height.</summary>
        protected float HumanScale = 1f;
        float _repickTimer;
        float _lateral; // the line he is actually holding across the pavement
        float _lane;    // the line he WANTS: his own side of the walk, kept right
        float _push;    // this frame's shove from whoever is in his way
        float _hold = 1f; // 1 clear road ahead, 0 stopped behind somebody
        float _shuffle;   // how far he has edged sideways waiting at a light

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        readonly AnimationClipPlayable[] _poses = new AnimationClipPlayable[PoseCount];
        readonly float[] _weights = new float[PoseCount];
        // clip lengths and paces read once at wiring: TickBlend runs for every
        // agent every frame, and GetAnimationClip() is a native call each time
        readonly float[] _clipLength = new float[PoseCount];
        readonly float[] _clipPace = new float[PoseCount];
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
            // 1987, America: everybody keeps right. Two flows down one pavement
            // that share a centre line walk through each other; two flows that
            // each hold their own side pass each other, which is what they do.
            _lane = Random.Range(0.35f, 0.95f);
            _lateral = _lane;
            if (!Walking.Contains(this)) Walking.Add(this);

            var animator = tf.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                // the walker moves the transform himself, so an off-screen body need not
                // be retargeted every frame - the graph keeps time and he steps back into
                // frame mid-stride, not frozen where the camera left him
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
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
                    _clipLength[pose] = clip.length;
                    _clipPace[pose] = clip.averageSpeed.magnitude;
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
                Wire(PoseCrouch, clips.Crouch);

                // every loop starts somewhere along itself, not at frame one - a line of
                // men breathing, walking or jogging in lockstep reads as a machine
                for (int i = 0; i < PoseCount; i++)
                    if (LoopByHand[i] && _poses[i].IsValid())
                        _poses[i].SetTime(Random.value * _clipLength[i]);
                if (_poses[PoseWalk].IsValid())
                    _poses[PoseWalk].SetSpeed(Speed / ClipPace(PoseWalk, WalkClipPace));
                _weights[PoseWalk] = 1f;
                _mixer.SetInputWeight(PoseWalk, 1f);
                output.SetSourcePlayable(_mixer);
                _graph.Play();
            }
        }

        public void Dispose()
        {
            Walking.Remove(this);
            if (_graph.IsValid()) _graph.Destroy();
        }

        /// <summary>May the walker step onto this link now - a crossing only on a red
        /// with time to finish; a walker running for his life overrides it.</summary>
        protected virtual bool MayEnter(PedLink link)
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

            ReadCrowd(dt);
            // a man walking into the back of the man ahead of him stops instead
            BlendLocomotion(dt, !_waiting && _hold > 0.25f);

            if (_waiting) { Jostle(dt); TracePed(dt); return; }
            _shuffle = 0f;
            Move(dt);
            TracePed(dt);
        }

        /// <summary>What this man is doing, for the trace: the crowd has its errands,
        /// a crew man has his fight.</summary>
        protected virtual string TraceState() => _waiting ? "waiting" : "walking";

        /// <summary>Whether he is trying to get anywhere at all - a man told to stand
        /// still is not stuck, however long he stands.</summary>
        protected virtual bool Moving => _link != null && !_waiting;

        /// <summary>One frame of one man in the trace, and a note when he has plainly
        /// stopped getting anywhere: stood still, not waiting at a kerb, for seconds on
        /// end - a man wedged behind a bin or steering into a wall.</summary>
        public void TracePed(float dt)
        {
            if (!DriveTrace.On || Tf == null) return;
            if (_traceFrame == Time.frameCount) return;   // ticked twice a frame: once is enough
            _traceFrame = Time.frameCount;

            var at = Tf.position;
            float moved = _traceStarted ? Vector3.Distance(at, _tracePrev) : 0f;
            float pace = dt > 1e-4f ? moved / dt : 0f;
            if (_traceStarted && moved < 0.02f && !_waiting) _traceStill += dt; else { _traceStill = 0f; _traceSaid = 0f; }
            _tracePrev = at;
            _traceStarted = true;

            // a man standing because he has been told to stand is not stuck: only one
            // who is trying to get somewhere and not getting there counts
            bool say = Moving && _traceStill > 6f && _traceStill - _traceSaid > 8f;
            if (say) _traceSaid = _traceStill;
            if (!say && DriveTrace.Now < _traceNext) return;
            _traceNext = DriveTrace.Now + DriveTrace.SampleEvery * 3f;   // a man is slower news than a car

            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", Id);
            DriveTrace.Str(sb, "tag", Tag);
            DriveTrace.Str(sb, "state", TraceState());
            DriveTrace.Num(sb, "pace", pace);
            DriveTrace.Num(sb, "want", Speed);
            DriveTrace.Num(sb, "lt", _t, "F3");   // along his link; "t" is the trace's own clock
            DriveTrace.Bool(sb, "wait", _waiting);
            DriveTrace.Num(sb, "hold", _hold, "F2");
            DriveTrace.Num(sb, "still", _traceStill, "F1");
            DriveTrace.Bool(sb, "link", _link != null);
            DriveTrace.Vec(sb, "p", at);
            if (say) { DriveTrace.Str(sb, "fault", "walkstall"); DriveTrace.Str(sb, "what", $"still for {_traceStill:F0}s"); }
            DriveTrace.Row(say ? "fault" : "ped", sb.ToString());
        }

        // ------------------------------------------------- the crowd around him
        // Three hundred walkers on one grid of pavements will occupy the same
        // metre of it unless they are told about each other. They are bucketed
        // once a frame - by whoever ticks first - and each reads the few around
        // him: a shoulder to steer off, a back to slow down behind.

        const float CrowdCell = 2f;
        const float Notice = 1.25f;   // metres at which another walker registers
        const float Touch = 0.4f;     // metres at which he has to stop

        static readonly List<PedestrianAgent> Walking = new List<PedestrianAgent>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetCrowd()
        {
            Walking.Clear();
            Cells.Clear();
            SpareCells.Clear();
            _cellFrame = -1;
        }

        static readonly Dictionary<long, List<PedestrianAgent>> Cells =
            new Dictionary<long, List<PedestrianAgent>>();
        static readonly Stack<List<PedestrianAgent>> SpareCells = new Stack<List<PedestrianAgent>>();
        static int _cellFrame = -1;

        static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        static void BucketCrowd()
        {
            if (_cellFrame == Time.frameCount) return;
            _cellFrame = Time.frameCount;

            foreach (var kv in Cells) { kv.Value.Clear(); SpareCells.Push(kv.Value); }
            Cells.Clear();

            for (int i = Walking.Count - 1; i >= 0; i--)
            {
                var a = Walking[i];
                if (a == null || a.Tf == null) { Walking.RemoveAt(i); continue; }
                if (!a.Tf.gameObject.activeInHierarchy) continue;
                var p = a.Tf.position;
                long key = CellKey(Mathf.FloorToInt(p.x / CrowdCell), Mathf.FloorToInt(p.z / CrowdCell));
                if (!Cells.TryGetValue(key, out var list))
                    Cells[key] = list = SpareCells.Count > 0 ? SpareCells.Pop() : new List<PedestrianAgent>();
                list.Add(a);
            }
        }

        /// <summary>This frame's steer and brake: who is in front of him, and which
        /// way round them. Sets _push (metres of lateral) and _hold (0..1 of pace).</summary>
        void ReadCrowd(float dt)
        {
            _push = 0f;
            _hold = 1f;
            if (Tf == null) return;
            BucketCrowd();

            var me = Tf.position;
            var ahead = LinkDirection;
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            int cx = Mathf.FloorToInt(me.x / CrowdCell), cz = Mathf.FloorToInt(me.z / CrowdCell);

            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!Cells.TryGetValue(CellKey(cx + dx, cz + dz), out var bucket)) continue;
                    for (int k = 0; k < bucket.Count; k++)
                    {
                        var other = bucket[k];
                        if (other == this || other.Tf == null) continue;
                        var d = other.Tf.position - me;
                        d.y = 0f;
                        float dist = d.magnitude;
                        if (dist > Notice || dist < 0.0001f) continue;

                        float front = Vector3.Dot(d, ahead);
                        if (front < -0.3f) continue;             // behind him: not his problem
                        float side = Vector3.Dot(d, right);
                        float weight = 1f - dist / Notice;

                        // step off him - and dead ahead, step to the right, as
                        // everybody else on this pavement is doing
                        _push += (Mathf.Abs(side) < 0.12f ? 1f : -Mathf.Sign(side)) * weight * 0.8f;

                        if (front > 0.05f && Mathf.Abs(side) < 0.42f)
                            _hold = Mathf.Min(_hold, Mathf.InverseLerp(Touch, Notice, dist));
                    }
                }

            _push = Mathf.Clamp(_push, -0.9f, 0.9f);

            // COURTESY HAS A TIME LIMIT. Two men who meet head on, each standing aside
            // for the other, stand there until the scene is closed: each is dead ahead
            // of the other, both brakes go to nothing, and stepping aside cannot break
            // it because neither is moving to step past. (Three of the outfit stood nose
            // to nose with their own man for 107 seconds, on a pavement with nobody else
            // on it, while the car they were walking back to waited two streets away and
            // the job timed out.) A few seconds of getting nowhere and he goes by: at
            // half pace, and to a side that is his own - so two men who are in each
            // other's way do not spend the afternoon stepping the same way together.
            _crowdStuck = _hold < 0.15f ? _crowdStuck + dt : 0f;
            if (_crowdStuck > 3f)
            {
                _hold = Mathf.Max(_hold, 0.5f);
                _push = (Id & 1) == 0 ? 0.9f : -0.9f;
            }
        }

        float _crowdStuck;   // seconds stood still by the people in front of him

        /// A knot of people held at a red light shuffles apart instead of standing
        /// inside one another - a little way along the kerb, never off it.
        void Jostle(float dt)
        {
            if (Tf == null) return;
            var ahead = LinkDirection;
            var right = new Vector3(ahead.z, 0f, -ahead.x);
            if (_push != 0f)
            {
                float step = Mathf.Clamp(_push, -1f, 1f) * 0.35f * dt;
                float moved = Mathf.Clamp(_shuffle + step, -0.7f, 0.7f);
                Tf.position += right * (moved - _shuffle);
                _shuffle = moved;
            }
            Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(ahead), 3f * dt);
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

        /// <summary>Metres a second the crowd's stock walk covers at playback speed 1
        /// (the Mixamo walk is animated in place and says nothing about it itself).</summary>
        public const float WalkClipPace = 1.5f;

        /// <summary>Metres a second this pose's clip covers at playback speed 1: read
        /// off the clip's own root motion when it carries any (the library takes do),
        /// else the caller's figure. Feet keyed to this do not skate - a Walk_Formal
        /// dealt to one man and a Walk_Loop to the next each play at their own rate.</summary>
        protected float ClipPace(int pose, float fallback)
        {
            if (!_poses[pose].IsValid()) return fallback;
            float measured = _clipPace[pose];
            return measured > 0.5f ? measured : fallback;
        }

        /// <summary>Start a loop pose at a random point along itself - so men who
        /// break into the same clip in the same second are not in step.</summary>
        protected void ScatterPhase(int pose)
        {
            if (!_poses[pose].IsValid()) return;
            _poses[pose].SetTime(Random.value * _clipLength[pose]);
        }

        /// <summary>Halt the whole graph (a civilian gone indoors) or let it run
        /// again: a body switched off still costs its animation otherwise.</summary>
        public void Suspend(bool suspended)
        {
            if (!_graph.IsValid()) return;
            if (suspended) { if (_graph.IsPlaying()) _graph.Stop(); }
            else if (!_graph.IsPlaying()) _graph.Play();
        }

        /// <summary>Seconds into the pose's clip, and the clip's length - a one-shot
        /// (the shot, the flinch, the fall) is over when the first passes the second.</summary>
        protected float PoseTime(int pose) => _poses[pose].IsValid() ? (float)_poses[pose].GetTime() : 0f;
        protected float PoseLength(int pose) => _poses[pose].IsValid() ? _clipLength[pose] : 0f;

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
                    float len = _clipLength[i];
                    if (len > 0.01f)
                    {
                        double at = _poses[i].GetTime();
                        if (at >= len) _poses[i].SetTime(at % len);
                    }
                }
                float target = i == _pose ? 1f : 0f;
                if (Mathf.Approximately(_weights[i], target)) continue;
                _weights[i] = Mathf.MoveTowards(_weights[i], target, 4f * dt);
                _mixer.SetInputWeight(i, _weights[i]);
            }
        }

        /// <summary>The pose the walker moves in - the walk, or the jog when he runs;
        /// a derived agent switches it and the crossfade follows.</summary>
        protected int LocomotionPose = PoseWalk;

        /// <summary>Turn round on the current stretch (the shooting is ahead): the
        /// reverse link, the same metre. False when the stretch has no reverse.</summary>
        protected bool ReverseCourse()
        {
            if (_link == null) return false;
            PedLink back = null;
            var links = _link.To.Links;
            for (int k = 0; k < links.Count; k++)
                if (links[k].To == _link.From) { back = links[k]; break; }
            if (back == null) return false;
            _t = Mathf.Clamp(_link.Length - _t, 0f, back.Length);
            _link = back;
            _cameFrom = back.From;
            _waiting = false;
            return true;
        }

        /// <summary>The way the walker is heading along his stretch.</summary>
        protected Vector3 LinkDirection
        {
            get
            {
                if (_link == null) return Tf != null ? Tf.forward : Vector3.forward;
                var d = _link.To.Pos - _link.From.Pos;
                d.y = 0f;
                return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            }
        }

        // The walk/idle crossfade, callable by derived agents that hand-animate
        // legs off the graph (the foot patrol's door walk) without running Tick.
        protected void BlendLocomotion(float dt, bool walking)
        {
            SetPose(walking ? LocomotionPose : PoseIdle);
            TickBlend(dt);
        }

        void Move(float dt)
        {
            float speed = (_link.Gated ? Speed * CrossHustle : Speed) * _hold;
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
                // his own side of the walk, shoved off whoever is in the way, then
                // put on the nearest line that is actually clear of the furniture -
                // read a stride and a half ahead, so he leans round a palm or a bin
                // rather than arriving at it. A crossing keeps him on the zebra.
                float limit = _link.Gated ? 0.9f : 1.9f;
                float want = Mathf.Clamp(_lane + _push, -limit, limit);
                if (!_link.Gated)
                    want = Mathf.Clamp(_link.FreeLine(_t, 2f, want), -limit, limit);
                _lateral = Mathf.MoveTowards(_lateral, want, 2.4f * dt);

                pos += new Vector3(dirN.z, 0f, -dirN.x) * _lateral;
                var rot = Quaternion.Slerp(
                    Tf.rotation, Quaternion.LookRotation(dirN), dt <= 0f ? 1f : 8f * dt);
                Tf.SetPositionAndRotation(pos, rot);
                return;
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
