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
        internal int LiveClearanceVersion = -1;

        public static float SlotLateral(int k) => (k - (Slots - 1) / 2) * SlotStep;

        // one rank of lateral lines, reused: SampleClearance runs at build only,
        // and on one thread
        static readonly Vector2[] _rank = new Vector2[Slots];

        // one list, reused: sampling runs at build only and on one thread
        static readonly List<SidewalkPlan> _one = new List<SidewalkPlan>(1);

        /// <summary>Read the laid props into this stretch: which lateral slots a
        /// walker of <paramref name="radius"/> can hold at each station. A station
        /// owns the half-interval to either side of it and a slot is free only if
        /// it is free over the WHOLE interval - sampled every quarter metre, so a
        /// lamp post or a parking meter between two stations is seen and not
        /// walked through. Done once at build; the crowd pays nothing for it.</summary>
        public void SampleClearance(SidewalkPlan plan, float radius)
        {
            if (plan == null) { Free = null; return; }
            _one.Clear();
            _one.Add(plan);
            SampleClearance(_one, radius);
        }

        /// <summary>The same, read against SEVERAL plans at once - a slot is free only
        /// if every one of them leaves it free.
        ///
        /// A walk laid across ground that more than one pass has furnished needs this:
        /// the street kit knows where its own lamps and bins are, and knows nothing about
        /// the shop wall, the hedge or the parked car that another pass blocked off. Read
        /// against the kit alone, the crowd walks through all three.</summary>
        public void SampleClearance(List<SidewalkPlan> plans, float radius)
        {
            var span = To.Pos - From.Pos;
            span.y = 0f;
            float len = span.magnitude;
            if (len < 0.01f || plans == null || plans.Count == 0) { Free = null; return; }
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
                    // the whole rank at once. Asked line by line this was seventeen
                    // separate walks of the plan's grid over four metres of ground that
                    // buckets the same either way - and at four samples to the metre over
                    // every stretch in the city it was the single dearest thing in the
                    // load. FreeSlots walks the union of those windows once, which is the
                    // same boxes and so the same answer.
                    for (int k = 0; k < Slots; k++)
                    {
                        var p = at + right * SlotLateral(k);
                        _rank[k] = new Vector2(p.x, p.z);
                    }
                    for (int p = 0; p < plans.Count && mask != 0; p++)
                        if (plans[p] != null) mask = plans[p].FreeSlots(_rank, Slots, radius, mask);
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

        // Optional body-specific joins. CoverDemo deals these from the masculine or
        // feminine Mixamo basic pack after it knows which prefab the walker wears.
        // The city leaves them null and keeps CrewKit's shared Synty turns.
        public AnimationClip TurnLeft, TurnRight;
        public bool AuthoredBasicLocomotion;
        public string BasicLocomotionLabel;

        // The gun wardrobe (the outfit's men): gun held low, gun up, the shot, the
        // flinch, the fall. Optional like the rest - an unarmed walker never asks.
        public AnimationClip PistolIdle, Aim, Shoot, Hit, Death;

        // A sidearm has a locomotion wardrobe of its own. The arrays are indexed by
        // RifleStep so the same direction resolver can drive pistol and long-gun
        // motion; they are presentation only and never decide where a walker moves.
        public AnimationClip[] PistolWalks, PistolRuns;
        public AnimationClip PistolCrouch;
        public bool AuthoredSidearmLocomotion;

        // An authored long-gun wardrobe can carry its own visible gaits and firing
        // takes without replacing the city's movement wardrobe. The base Walk/Jog/
        // Sprint remain the authority for pace, routing and joins; these gaits are
        // selected only while the weapon is actually in the man's hand.
        public AnimationClip RifleWalk, RifleJog, RifleSprint, RifleCrouchWalk;
        public AnimationClip RifleIdle, RifleAim, RifleShoot, RifleCrouch;
        public AnimationClip[] RifleWalks, RifleRuns, RifleSprints, RifleCrouchWalks;
        public AnimationClip RifleGunplay, AutomaticShoot, CoverShoot;
        public bool AuthoredLongGun;

        // The run a man breaks into closing on a fight. Optional: without it he walks.
        public AnimationClip Jog;

        // A two-handed still hold laid over a run for long guns. It is not another
        // gait: the legs keep Jog/Sprint and only the arms come from this clip.
        // Optional, and unused by ordinary pedestrians and sidearms.
        public AnimationClip LongGunRunUpper;

        // Flat out, arms pumping - the gait of a man genuinely running for his life,
        // a step above the jog. Optional: without it a bolting man jogs, as he always
        // did. Never dealt as a crew's marching gait (see CrewKit.Runs).
        public AnimationClip Sprint;

        // Down with the head in - the crowd's cower under fire. Optional.
        public AnimationClip Crouch;

        // Down and moving: the last few metres to a bin with rounds in the air.
        // Optional - without it he walks to cover at full height.
        public AnimationClip CrouchWalk;

        // Sat astride something with his hands out in front of him - the library's
        // Driving_Loop. What a man on a motorcycle plays under BikePose, which writes
        // his arms and legs over the top of it; without it he rides a bench sit, which
        // is nearly as good because so little of the clip survives the pose.
        public AnimationClip Ride;
    }

    /// <summary>How near the camera a body has to be before the fine layer of its
    /// animation is worth running at all - the foot-planted joins between gaits, the
    /// turns taken in steps, the fidgets and the gestures.
    ///
    /// NONE OF IT READS FROM THE BOOM. A man taking three steps round a turn instead
    /// of swivelling is the difference between a person and a puppet at four metres,
    /// and is four pixels of nothing at forty. So the whole layer is gated on one
    /// distance, and the gate does the right thing by itself as the player zooms:
    /// the demo camera pulls back as it zooms out, so the same test that asks "is
    /// this man near the eye" also asks "is the player looking at the street or at
    /// the district". A crowd seen from the boom costs exactly what it cost before
    /// any of this existed.</summary>
    public static class PedDetail
    {
        /// <summary>Metres from the camera. Generous, because the camera is a boom
        /// and most of that distance is height even when the player is right down on
        /// the pavement.</summary>
        public static float Radius = 60f;

        /// <summary>Off, and every walker in the town falls back to the plain
        /// crossfade - what the crowd had before this layer. For the soak, which
        /// runs with no camera worth the name and wants the cheap path.</summary>
        public static bool On = true;

        static Vector3 _eye;
        static bool _found;
        static int _frame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget() { _frame = -1; _found = false; }

        public static bool Near(Vector3 p)
        {
            if (!On) return false;
            // A RUN THAT IS BEING RECORDED ANIMATES EVERYBODY. The whole point of the
            // gate is that the player cannot read this layer from the boom - but the
            // point of a harness run is to FIND ITS FAULTS, and a run whose camera
            // happens to sit two hundred metres up exercises none of it and reports a
            // clean street it never animated. So while the black box is open, every
            // walker is on show. It costs the run some frames; frames are what a
            // harness run has and a player does not.
            if (DriveTrace.On) return true;
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                // THE EYE IS THE OBJECT, NOT THE CAMERA COMPONENT. Camera.main only
                // answers for an ENABLED camera, and the play harness switches every
                // camera off to buy its frame rate - so asked that way this whole
                // layer is dark in exactly the runs that would catch its faults, and
                // a soak would report a clean street it never animated. The demo boom
                // is still a transform, still where the run put it, so the eye is
                // taken off the tagged object and the layer runs and gets soaked.
                var cam = Camera.main;
                if (cam != null) { _eye = cam.transform.position; _found = true; }
                else
                {
                    var boom = GameObject.FindGameObjectWithTag("MainCamera");
                    _found = boom != null;
                    if (_found) _eye = boom.transform.position;
                }
            }
            // no eye at all (a scene mid-build): nobody is on show
            if (!_found) return false;
            var d = p - _eye;
            return d.sqrMagnitude < Radius * Radius;
        }
    }

    public class PedestrianAgent
    {
        const float CrossHustle = 1.35f;

        // Mixer input per pose; a pose whose clip was not provided stays invalid
        // and SetPose refuses to select it.
        public const int PoseWalk = 0, PoseIdle = 1, PoseSitDown = 2, PoseSit = 3,
            PoseStandUp = 4, PoseTalk = 5, PoseShout = 6,
            PosePistolIdle = 7, PoseAim = 8, PoseShoot = 9, PoseHit = 10, PoseDeath = 11,
            PoseJog = 12, PoseCrouch = 13, PoseRide = 14, PoseSprint = 15,
            PoseCrouchWalk = 16,
            PoseRifleWalk = 17, PoseRifleJog = 18, PoseRifleSprint = 19,
            PoseRifleCrouchWalk = 20,
            PoseRifleGunplay = 21, PoseAutomaticShoot = 22, PoseCoverShoot = 23,
            PoseRifleIdle = 24, PoseRifleAim = 25, PoseRifleShoot = 26,
            PoseRifleCrouch = 27,
            PosePistolCrouch = 28,
            // Two swappable LOOP ports. Directional pistol/rifle gaits use the quiet
            // port while the visible one blends out, so a route turning a corner does
            // not hard-cut both feet to another take.
            PoseWeaponGaitA = 29, PoseWeaponGaitB = 30,
            // The two SWAPPABLE slots. Every pose above is one clip wired once and
            // kept; these two hold whatever clip was last put in them (PutClip) - the
            // join between gaits, and the little life a man spends standing about.
            // There are dozens of candidate clips for each and no body needs more
            // than one at a time, so they are ports, not a wardrobe.
            PoseJoin = 31, PoseAct = 32;
        const int PoseCount = 33;

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
            table[PoseSprint] = true;
            table[PoseCrouch] = true;
            table[PoseCrouchWalk] = true;
            table[PoseRide] = true;
            table[PoseRifleWalk] = true;
            table[PoseRifleJog] = true;
            table[PoseRifleSprint] = true;
            table[PoseRifleCrouchWalk] = true;
            table[PoseRifleGunplay] = true;
            table[PoseAutomaticShoot] = true;
            table[PoseRifleIdle] = true;
            table[PoseRifleAim] = true;
            table[PoseRifleCrouch] = true;
            table[PosePistolCrouch] = true;
            table[PoseWeaponGaitA] = true;
            table[PoseWeaponGaitB] = true;
            // PoseJoin is a one-shot by definition. PoseAct is either - a fidget runs
            // once, a lean or a drunk sway loops - so it is wrapped per man (_actLoop)
            // rather than by this table.
            return table;
        }

        public Transform Tf;
        /// <summary>The stock civilian pace. Named so game-time audits do not have to
        /// duplicate the literal which drives the actual walker.</summary>
        public const float DefaultSpeedMetresPerSecond = 1.5f;
        public float Speed = DefaultSpeedMetresPerSecond;
        protected bool AuthoredLongGunWardrobe { get; private set; }
        protected bool AuthoredSidearmWardrobe { get; private set; }
        protected bool AuthoredBasicWardrobe { get; private set; }
        protected AnimationClip[] PistolWalkGaits { get; private set; }
        protected AnimationClip[] PistolRunGaits { get; private set; }
        protected AnimationClip[] RifleWalkGaits { get; private set; }
        protected AnimationClip[] RifleRunGaits { get; private set; }
        protected AnimationClip[] RifleSprintGaits { get; private set; }
        protected AnimationClip[] RifleCrouchWalkGaits { get; private set; }
        AnimationClip _bodyTurnLeft, _bodyTurnRight;
        string _locomotionLabel;

        // ---- the black box (DriveTrace): who he is, and whether he is getting anywhere
        static int _ids;
        public readonly int Id = ++_ids;
        /// <summary>What he is in the trace: crowd, crew, police.</summary>
        public string Tag = "crowd";
        /// <summary>The physical group this walker belongs to for close-range crowd
        /// steering. Zero means no group. Members of one crew share a non-zero id so
        /// they recognise only one another as mates and make the same dead-ahead
        /// passing decision.</summary>
        public int CrowdGroupId;
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
        AnimationLayerMixerPlayable _layers;
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
            AuthoredLongGunWardrobe = clips.AuthoredLongGun;
            AuthoredSidearmWardrobe = clips.AuthoredSidearmLocomotion;
            AuthoredBasicWardrobe = clips.AuthoredBasicLocomotion;
            PistolWalkGaits = clips.PistolWalks;
            PistolRunGaits = clips.PistolRuns;
            RifleWalkGaits = clips.RifleWalks;
            RifleRunGaits = clips.RifleRuns;
            RifleSprintGaits = clips.RifleSprints;
            RifleCrouchWalkGaits = clips.RifleCrouchWalks;
            _bodyTurnLeft = clips.TurnLeft;
            _bodyTurnRight = clips.TurnRight;
            _locomotionLabel = clips.BasicLocomotionLabel;
            // 1987, America: everybody keeps right. Two flows down one pavement
            // that share a centre line walk through each other; two flows that
            // each hold their own side pass each other, which is what they do.
            _lane = Random.Range(0.35f, 0.95f);
            _lateral = _lane;
            WalkCadence = Random.Range(0.96f, 1.04f);
            if (!Walking.Contains(this)) Walking.Add(this);

            var animator = tf.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                // the walker moves the transform himself, so an off-screen body need not
                // be retargeted every frame - the graph keeps time and he steps back into
                // frame mid-stride, not frozen where the camera left him.
                //
                // A RECORDED RUN ANIMATES EVERY BODY FOR REAL. The harness switches all
                // cameras off, so under culling not one skeleton in the run is ever
                // evaluated - and then every measurement taken of the animation is a
                // measurement of what the code MEANT, never of what the bones did. A
                // foot that slides is invisible to a run like that by construction.
                // The black box is opened AFTER the city is built (the harness warms up
                // first), so this cannot be decided here - it is decided the first frame
                // the trace is actually recording (TickBlend).
                _animator = animator;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                _footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                HumanScale = animator.avatar != null && animator.avatar.isHuman
                    ? animator.humanScale : 1f;
                // AND SET UP TWICE IS A GRAPH LOST. Setup runs again whenever a body
                // is re-dealt onto an agent, the list above refuses the duplicate, and
                // the field is about to stop pointing at the old graph - unreachable by
                // the sweep or anything else. Let go of it while the handle is in hand.
                if (_graph.IsValid()) _graph.Destroy();
                // Every playable handle belongs to the graph just destroyed. Setup can
                // run twice on one agent when a streamed body is re-dealt; retaining a
                // valid-looking slot/weight from the first graph makes the new wardrobe
                // skip its wire or crossfade against a clip it does not own.
                System.Array.Clear(_poses, 0, _poses.Length);
                System.Array.Clear(_weights, 0, _weights.Length);
                System.Array.Clear(_clipLength, 0, _clipLength.Length);
                System.Array.Clear(_clipPace, 0, _clipPace.Length);
                System.Array.Clear(_slotClip, 0, _slotClip.Length);
                System.Array.Clear(_slotRunning, 0, _slotRunning.Length);
                _pose = PoseWalk;
                _walkRateSet = -1f;
                _longGunRunWeight = 0f;
                _layers = default;
                _mixer = default;
                _graph = PlayableGraph.Create("Pedestrian");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
                // NORMALISED, AND IT HAS TO BE. An AnimationMixerPlayable defaults to
                // NOT normalising, which means the output is the literal weighted SUM
                // of its inputs - and a humanoid pose summed to less than 1 is a pose
                // scaled DOWN: the man squashes toward his own root, which reads as
                // being flattened, or as standing waist-deep in the pavement.
                //
                // The blend below can absolutely produce that. It moves every weight
                // toward its target at ONE rate, which keeps the sum at 1 for the
                // usual two poses in flight (one leaving, one arriving) and breaks
                // the moment there are THREE - two leaving at 4/s against one arriving
                // at 4/s is a sum falling at 4 a second. Three in flight was rare
                // before (a shot, an aim and a flinch inside a quarter second) and is
                // ordinary now that a fidget can be cut by a join which is cut by an
                // order. Normalising makes the sum the mixer's problem, not the
                // blend's, and costs nothing: two poses summing to 1 are untouched.
                _mixer = AnimationMixerPlayable.Create(_graph, PoseCount, true);

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
                Wire(PoseSprint, clips.Sprint);
                Wire(PoseCrouch, clips.Crouch);
                Wire(PoseCrouchWalk, clips.CrouchWalk);
                Wire(PoseRide, clips.Ride);
                Wire(PoseRifleWalk, clips.RifleWalk);
                Wire(PoseRifleJog, clips.RifleJog);
                Wire(PoseRifleSprint, clips.RifleSprint);
                Wire(PoseRifleCrouchWalk, clips.RifleCrouchWalk);
                Wire(PoseRifleGunplay, clips.RifleGunplay);
                Wire(PoseAutomaticShoot, clips.AutomaticShoot);
                Wire(PoseCoverShoot, clips.CoverShoot);
                Wire(PoseRifleIdle, clips.RifleIdle);
                Wire(PoseRifleAim, clips.RifleAim);
                Wire(PoseRifleShoot, clips.RifleShoot);
                Wire(PoseRifleCrouch, clips.RifleCrouch);
                Wire(PosePistolCrouch, clips.PistolCrouch);
                // PoseJoin and PoseAct are left empty: their clip arrives when the
                // man first needs one (PutClip), and a walker who never gets near
                // the camera never pays for either.

                // every loop starts somewhere along itself, not at frame one - a line of
                // men breathing, walking or jogging in lockstep reads as a machine
                for (int i = 0; i < PoseCount; i++)
                    if (LoopByHand[i] && _poses[i].IsValid())
                        _poses[i].SetTime(Random.value * _clipLength[i]);
                if (_poses[PoseWalk].IsValid())
                    HoldWalkRate(Speed);
                _weights[PoseWalk] = 1f;
                _mixer.SetInputWeight(PoseWalk, 1f);
                if (clips.LongGunRunUpper != null)
                {
                    // The base mixer keeps the gait whole. A masked override above it
                    // replaces only the arms with the pack's two-handed long-gun hold,
                    // so the feet, hips, run cadence and root all stay exactly as they
                    // were. One shared graph layer is cheaper and cleaner than a second
                    // Animator/controller on every armed man.
                    _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
                    _graph.Connect(_mixer, 0, _layers, 0);
                    _layers.SetInputWeight(0, 1f);
                    var upper = AnimationClipPlayable.Create(_graph, clips.LongGunRunUpper);
                    _graph.Connect(upper, 0, _layers, 1);
                    _layers.SetInputWeight(1, 0f);
                    _layers.SetLayerMaskFromAvatarMask(1, LongGunRunMask());
                    output.SetSourcePlayable(_layers);
                }
                else output.SetSourcePlayable(_mixer);
                _graph.Play();
            }
        }

        static AvatarMask _longGunRunMask;

        /// <summary>Only the two arms belong to the weapon hold. In particular the
        /// body and legs remain the gait's: replacing those with a one-frame idle is
        /// how an upper-body overlay turns a running man into a sliding statue.</summary>
        static AvatarMask LongGunRunMask()
        {
            if (_longGunRunMask != null) return _longGunRunMask;
            var mask = new AvatarMask { name = "Long gun run - arms only" };
            foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
                if (part != AvatarMaskBodyPart.LastBodyPart)
                    mask.SetHumanoidBodyPartActive(part, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            return _longGunRunMask = mask;
        }

        float _longGunRunWeight;

        /// <summary>Blend the two-handed carry over the current gait. The graph is
        /// absent on walkers whose wardrobe did not bring the optional clip.</summary>
        protected void BlendLongGunRun(bool carrying, float dt)
        {
            if (!_layers.IsValid()) return;
            _longGunRunWeight = Mathf.MoveTowards(_longGunRunWeight, carrying ? 1f : 0f, 8f * dt);
            _layers.SetInputWeight(1, _longGunRunWeight);
        }

        public virtual void Dispose()
        {
            Walking.Remove(this);
            CrewGore.Forget(this);
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
            // and a man about to step in front of a car crossing the pavement stops too
            if (StandingBack()) { BlendLocomotion(dt, false); TracePed(dt); return; }
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
            // STILL means still WHILE TRYING TO GET SOMEWHERE. A crew man stands on his
            // corner for half a minute because that is what he was left doing, and the
            // clock used to run the whole time - so the instant an order reached him he
            // was reported "still for 22 seconds" and the soak counted four thousand
            // stalls that were nothing but men being given orders. The clock now runs
            // only while he is under one, which is what the fault was always about.
            if (_traceStarted && moved < 0.02f && !_waiting && Moving) _traceStill += dt;
            else { _traceStill = 0f; _traceSaid = 0f; }
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
            // what his LEGS are doing, which is a different question from what he is
            // doing: the audit already holds the run pose against the ground he covers,
            // and without the pose on the row there is no way to tell a man walking
            // from a man sliding along in a stand - or to tell whether the join layer
            // ran at all in a given scene (it is gated on the eye, PedDetail).
            DriveTrace.Str(sb, "pose", PoseName(_pose));
            if (!string.IsNullOrEmpty(_locomotionLabel))
                DriveTrace.Str(sb, "loco", _locomotionLabel);
            // and how fast the clip playing on him says his FEET are covering ground.
            // A slide is this against "pace": a man whose feet claim half a metre a
            // second while the graph carries him at two is gliding down the street,
            // and no other field on the row can show it.
            DriveTrace.Num(sb, "feet", FeetPace);
            // and WHAT ELSE is on him. A crossfade is meant to be two poses for a
            // quarter second; anything that leaves a second clip standing at real
            // weight is a man wearing two poses at once - which is a half-crouched
            // walk, a barrel that hangs where neither clip put it, and feet that
            // cover less ground than the gait claims. None of that can be seen from
            // the pose alone, because the pose is the one that is winning.
            int other = SecondPose(out float otherWeight);
            DriveTrace.Str(sb, "also", PoseName(other));
            DriveTrace.Num(sb, "alsow", otherWeight, "F2");
            // and what the BONES did: the worst the planted foot slipped since the
            // last row. Near zero is a man walking; near his own pace is a man gliding.
            DriveTrace.Num(sb, "slip", _slipCount > 0 ? _slipSum / _slipCount : 0f);
            _slipSum = 0f;
            _slipCount = 0;
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

        // ------------------------------------------------------ ground a car is crossing
        //
        // A forecourt, a station's stall, a yard gate: the few places where a car leaves
        // the road and drives over ground people walk on. There is no lane there and no
        // signal, so the discipline is the plain one of the street - WHOEVER IS ALREADY
        // MOVING OVER IT HAS IT, and a man about to step into that ground waits until it
        // is his again. A man already standing in it walks straight out rather than
        // freezing under the bumper, which is also what a man does.
        //
        // The claim is a rolling one: the car re-makes it every frame a few metres ahead
        // of itself and it lapses on its own, so nothing has to remember to give it back
        // and a car that stops existing stops claiming.

        readonly struct Crossing
        {
            public readonly Vector3 At;
            public readonly float Radius, Until;

            public Crossing(Vector3 at, float radius, float until) { At = at; Radius = radius; Until = until; }
        }

        static readonly List<Crossing> Crossings = new List<Crossing>();

        /// <summary>Ground a car is about to drive over. Called every frame while it
        /// crosses; <paramref name="seconds"/> is only how long the claim outlives the
        /// call, so a couple of frames is plenty.</summary>
        public static void CarCrossing(Vector3 at, float radius, float seconds)
        {
            for (int i = Crossings.Count - 1; i >= 0; i--)
                if (Crossings[i].Until < Time.time) Crossings.RemoveAt(i);
            Crossings.Add(new Crossing(at, radius, Time.time + seconds));
        }

        static bool InCrossing(Vector3 p)
        {
            float now = Time.time;
            for (int i = 0; i < Crossings.Count; i++)
            {
                var c = Crossings[i];
                if (c.Until < now) continue;
                var d = c.At - p;
                d.y = 0f;
                if (d.sqrMagnitude < c.Radius * c.Radius) return true;
            }
            return false;
        }

        /// <summary>Is his next stride into ground a car is crossing? Only if he is not
        /// standing in it already - a man under the bumper walks out of it.</summary>
        bool StandingBack()
        {
            if (Crossings.Count == 0 || Tf == null) return false;
            var here = Tf.position;
            if (InCrossing(here)) return false;
            var way = LinkDirection;
            way.y = 0f;
            if (way.sqrMagnitude < 1e-4f) return false;
            return InCrossing(here + way.normalized * StandBackStep);
        }

        /// <summary>How far ahead he looks for it - a stride and a half, so he stops at
        /// the edge of the ground rather than on it.</summary>
        const float StandBackStep = 1.6f;

        /// <summary>Everybody on foot in the scene, whoever put them there: the crowd,
        /// the outfit, the mobs and the law all register here for the crowd's own
        /// bucketing, so it is also the one list an overlay can ask "who is out there"
        /// (DemoPeopleDebug). Read only - the list is the crowd's, and a man leaves it
        /// when his body goes.</summary>
        public static IReadOnlyList<PedestrianAgent> Everyone => Walking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetCrowd()
        {
            Crossings.Clear();
            Walking.Clear();
            Cells.Clear();
            SpareCells.Clear();
            _cellFrame = -1;
            // Subscribed here rather than at the first walker, because this runs once a
            // play and the handler must not stack up over a session that never reloads
            // the domain.
            Application.quitting -= DropCrowdGraphs;
            Application.quitting += DropCrowdGraphs;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= DropCrowdGraphsLeavingPlay;
            UnityEditor.EditorApplication.playModeStateChanged += DropCrowdGraphsLeavingPlay;
#endif
        }

#if UNITY_EDITOR
        /// <summary>In the editor a run ends without the application ever quitting, and
        /// Unity takes its count of open graphs BEFORE Application.quitting would have
        /// run - so the editor needs its own moment, and this is the last one at which
        /// the whole crowd is still standing.</summary>
        static void DropCrowdGraphsLeavingPlay(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                DropCrowdGraphs();
        }
#endif

        /// <summary>Every walker's animation graph, freed when the run ends.
        ///
        /// A PlayableGraph is not a Unity object and nothing collects it: it lives until
        /// somebody calls Destroy on it, and PedestrianAgent is a plain class with no
        /// OnDestroy to do that. Dispose frees the graph of a man who dies or despawns,
        /// but the crowd still ON ITS FEET when the run ends is disposed by nobody, and
        /// Unity ends the session complaining about every graph left open ("PlayableGraph
        /// was not destroyed"). This is that sweep: whoever is still walking when the game
        /// stops, stops properly.</summary>
        static void DropCrowdGraphs()
        {
            for (var i = Walking.Count - 1; i >= 0; i--)
                Walking[i]?.Dispose();
            Walking.Clear();
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
                if (a == null || a.Tf == null)
                {
                    // HIS BODY WENT WITHOUT HIM. A walker whose GameObject was destroyed
                    // by somebody who never called Dispose - a purge, a pooled block, a
                    // recycler - was dropped from the list here and nowhere else. His
                    // animation graph is not a Unity object and nothing collects it, and
                    // once he is off this list the sweep at the end of the run cannot see
                    // him either: the graph stays open and Unity counts it on the way out.
                    // The RemoveAt goes first so Dispose's own removal finds nothing and
                    // cannot take a different man out of the list.
                    Walking.RemoveAt(i);
                    a?.Dispose();
                    continue;
                }
                if (!a.Tf.gameObject.activeInHierarchy || !a.InCrowd) continue;
                var p = a.Tf.position;
                long key = CellKey(Mathf.FloorToInt(p.x / CrowdCell), Mathf.FloorToInt(p.z / CrowdCell));
                if (!Cells.TryGetValue(key, out var list))
                    Cells[key] = list = SpareCells.Count > 0 ? SpareCells.Pop() : new List<PedestrianAgent>();
                list.Add(a);
            }
        }

        /// <summary>Is this body one the others have to walk round? A corpse on the
        /// pavement and a man sat in a car seat are both at a position and both in the
        /// walking list, and neither is somebody to step aside for - the seat's man
        /// isn't even where his transform says he is, he is wherever the car has got
        /// to. Overridden by an agent that can be either.</summary>
        protected virtual bool InCrowd => true;

        /// <summary>This frame's shove from whoever is in his way, in metres across
        /// his heading; and what is left of his pace, 0 stopped behind somebody to 1
        /// clear road. Read by an agent that steps by hand (the crews' free stride)
        /// after it has asked <see cref="ReadCrowd(float, Vector3)"/>.</summary>
        protected float CrowdPush => _push;
        protected float CrowdHold => _hold;

        /// <summary>This frame's steer and brake: who is in front of him, and which
        /// way round them. Sets _push (metres of lateral) and _hold (0..1 of pace).</summary>
        void ReadCrowd(float dt) => ReadCrowd(dt, LinkDirection);

        /// <summary>Metres of lateral order a second the shove may be changed by - how
        /// fast a man may change his mind about which flank he is going round somebody
        /// on. Low enough that the answer flipping sides is a lean and not a jerk.</summary>
        const float PushEase = 1.2f;

        /// <summary>The same, for a walker with no stretch under him: the way he means
        /// to go is given rather than read off a link. THE CROWD IS ONE CROWD - the
        /// bucket holds the citizens, the outfit, the mobs and the law alike, so a man
        /// striding over open ground steps round exactly the people a man on the
        /// pavement steps round, and the two systems cannot disagree about who is
        /// where.</summary>
        protected void ReadCrowd(float dt, Vector3 heading)
        {
            float want = 0f;
            _hold = 1f;
            if (Tf == null) { _push = Mathf.MoveTowards(_push, 0f, PushEase * dt); return; }
            BucketCrowd();

            var me = Tf.position;
            var ahead = heading;
            ahead.y = 0f;
            ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.forward;
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
                        // everybody else on this pavement is doing. NOT between
                        // CREWMATES: the herd's same-way bias marched a bunched crew
                        // onto one line (the dawdle packs them close, every push
                        // says "right", the dealt lanes collapse into single file) -
                        // a physical crew chooses one shared side and leans on its own
                        // members only half as hard, so the pack stays a pack.
                        bool mate = CrowdGroupId != 0 && CrowdGroupId == other.CrowdGroupId;
                        float steer = Mathf.Abs(side) < 0.12f
                            ? (mate ? CrowdPassingSide : 1f)
                            : -Mathf.Sign(side);
                        want += steer * weight * (mate ? 0.4f : 0.8f);

                        if (front > 0.05f && Mathf.Abs(side) < 0.42f)
                            _hold = Mathf.Min(_hold, Mathf.InverseLerp(Touch, Notice, dist));
                    }
                }

            want = Mathf.Clamp(want, -0.9f, 0.9f);

            // A SHOVE IS A DECISION, NOT A TWITCH. The answer above is worked out from
            // scratch every frame and it is DISCONTINUOUS at the one place two walkers
            // spend all their time: `-Sign(side)` flips the whole shove from one flank
            // to the other the frame a man crosses from a hair left of the line to a
            // hair right of it. Written straight in, that is a lateral order swinging
            // the full width of the pavement between one frame and the next - the men
            // striding left and right at random the player was watching. Eased in
            // instead, a flip takes about a second and a half to spend, which is what
            // stepping aside for somebody looks like; a man who never crosses the line
            // sees no difference at all.
            _push = Mathf.MoveTowards(_push, want, PushEase * dt);

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
                // the deadlock break is not eased: it exists because nothing else has
                // moved for three seconds, and a gentle one is no break at all
                _push = CrowdPassingSide * 0.9f;
            }
        }

        float _crowdStuck;   // seconds stood still by the people in front of him

        /// <summary>A crew passes a dead-ahead obstruction on one shared side. Walkers
        /// without a group keep the old per-person split, which is what breaks a
        /// head-on deadlock between unrelated pedestrians.</summary>
        protected int CrowdPreferredSide => CrowdGroupId != 0
            ? ((CrowdGroupId & 1) == 0 ? 1 : -1)
            : ((Id & 1) == 0 ? 1 : -1);

        float CrowdPassingSide => CrowdPreferredSide;

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
                var to = Tf.position + right * (moved - _shuffle);
                if (GraphStepClear(Tf.position, to))
                {
                    Tf.position = to;
                    _shuffle = moved;
                }
            }
            if (!Joining)
                Tf.rotation = Quaternion.Slerp(Tf.rotation, Quaternion.LookRotation(ahead), 3f * dt);
        }

        // ------------------------------------------------------------- posing

        protected bool HasPose(int pose) => _poses[pose].IsValid();

        /// <summary>The kerb-strip offset a walker keeps off the link's centre line;
        /// a crew sets it by hand so its men do not all share one line.</summary>
        protected float Lateral { get => _lateral; set => _lateral = value; }

        /// <summary>The line he WANTS across the pavement - everybody's is dealt at
        /// random on the right (Setup), which is why a crew sent down a street used
        /// to thread it in single file: five men, five nearly identical lanes, one
        /// queue. A derived agent deals its men distinct lanes instead.</summary>
        protected float Lane { get => _lane; set => _lane = value; }

        /// <summary>Stood at a crossing waiting on the light (t = 0 on a gated link).</summary>
        protected bool Waiting { get => _waiting; set => _waiting = value; }

        /// <summary>Drop the current link and choose afresh from this node - a walker
        /// given new orders while it stands at a light asks its own ChooseLink again.</summary>
        protected void Reroute(PedNode node)
        {
            _waiting = false;
            PickNext(node, keepAwayFrom: null);
        }

        /// <summary>Keep his feet where they are across a change of link. Move builds
        /// the position from metre-plus-lateral in the LINK'S OWN frame, and that
        /// frame turns with every corner and flips with every turn-round - so a
        /// lateral carried as a bare number mirrors the walker across the walk's
        /// centre line the frame the link changes: a two-lane man jumps almost four
        /// metres, which the player reads as a respawn. Re-reading metre and lateral
        /// off his true position makes the change of frame invisible; the lane he
        /// WANTS is untouched and he eases back onto it as he walks.</summary>
        protected void CarrySeat()
        {
            if (_link == null || Tf == null) return;
            var ab = _link.To.Pos - _link.From.Pos;
            ab.y = 0f;
            float len = ab.magnitude;
            if (len < 0.01f) return;
            var dirN = ab / len;
            var rel = Tf.position - _link.From.Pos;
            rel.y = 0f;
            _t = Mathf.Clamp(Vector3.Dot(rel, dirN), 0f, _link.Length);
            _lateral = Mathf.Clamp(
                Vector3.Dot(rel, new Vector3(dirN.z, 0f, -dirN.x)), -2.6f, 2.6f);
        }

        /// <summary>Select the pose the blend drifts toward; a pose with no clip is
        /// refused.
        ///
        /// A JOIN OR AN ACTION OUTRANKS THIS, and deliberately: every caller in the
        /// town sets its pose every frame, so a join that could be overwritten would
        /// live exactly one frame. What the caller asked for is REMEMBERED instead
        /// and handed to him the moment the join lets go - unless what it asked for
        /// says the join is over (a man setting off mid-turn, a man told to stand
        /// mid-start) or says he has bigger problems (the flinch, the fall).</summary>
        protected void SetPose(int pose)
        {
            if (!_poses[pose].IsValid()) return;
            if (_join != Join.None && pose != PoseJoin)
            {
                _joinInto = pose;
                bool over = BreaksJoin(pose) ||
                            (_join == Join.Starting ? !IsMotion(pose) : IsMotion(pose));
                if (!over) return;
                EndJoin();
            }
            if (_actLeft > 0f && pose != PoseAct)
            {
                // The action IS what he is doing while it lasts - but only over the
                // poses it was laid on top of: a stand, a ready stand, a word. It is
                // a gesture, not a state, so it never survives him being told to walk,
                // sit, shoot or fall.
                if (pose == PoseIdle || pose == PosePistolIdle ||
                    pose == PoseTalk || pose == PoseShout)
                {
                    _actFrom = pose;   // what to hand back to when it runs out
                    return;
                }
                EndAct();
            }
            _pose = pose;
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

        /// <summary>A hair off the cadence his exact pace would key his feet to, dealt
        /// once per man and never changed.
        ///
        /// THE STEP RATE IS WHAT MAKES A CROWD READ AS A MACHINE. The clips are already
        /// started at a random point along themselves, so no two men set off on the same
        /// frame of the same walk - but men keyed to one clip at one pace hold whatever
        /// phase they started with for ever, and a few pairs out of any crew start near
        /// enough together to be in step for the whole run. It is the pairs the eye
        /// finds. A few per cent off the rate makes the phase between any two men drift
        /// instead of hold, so nobody stays in step with anybody; over one stride it is
        /// a couple of centimetres of foot, which is nothing to look at from the boom.</summary>
        protected float WalkCadence { get; private set; } = 1f;

        /// <summary>What to play the walk at to cover <paramref name="pace"/> metres a
        /// second on this man's own clip - the one place the walk's rate is worked out,
        /// so every walker in every scene gets the same treatment.</summary>
        protected float WalkRate(float pace) =>
            pace / ClipPace(PoseWalk, WalkClipPace) * WalkCadence;

        float _walkRateSet = -1f;

        /// <summary>Keep the walk clip in step with the ground he is covering.
        ///
        /// THE GRAPH NEVER RE-TIED IT. The rate was worked out from Speed once, at
        /// wiring, and never again - while the pace a stretch carries him at is geared
        /// twice over every frame, by the man in front of him and by the dawdle a crew's
        /// tether puts on him. A hood held at 0.55 of his pace crossed the ground at
        /// barely half the stride his legs were keeping, which is the moon-walk this
        /// file is careful about everywhere else.
        ///
        /// Written only when it has actually moved, because this is on the path of
        /// every walker in the city every frame and the write is a native call.</summary>
        protected void HoldWalkRate(float pace)
        {
            float rate = WalkRate(pace);
            if (Mathf.Abs(rate - _walkRateSet) < 0.02f) return;
            _walkRateSet = rate;
            SetPoseSpeed(PoseWalk, rate);
        }

        /// <summary>Start a loop pose at a random point along itself - so men who
        /// break into the same clip in the same second are not in step.</summary>
        protected void ScatterPhase(int pose)
        {
            if (!_poses[pose].IsValid()) return;
            _poses[pose].SetTime(Random.value * _clipLength[pose]);
        }

        // ==================================================== the joins and the life
        //
        // Everything from here to TickBlend is the layer that sits between the gait
        // loops: the foot-planted joins (setting off, pulling up, turning on the
        // spot) and the little one-shots a man spends standing about. Both play out
        // of a SWAPPABLE mixer slot, because there are dozens of candidate clips and
        // no body needs two at once.
        //
        // THE WHOLE LAYER IS FOR THE NEAR VIEW. It exists because the camera comes
        // down close, and none of it can be read from the boom - so a walker the
        // player cannot see up close never starts a join, never draws a fidget, and
        // never has a clip put in either slot. That is what keeps a crowd of a
        // thousand costing what it costs today.

        readonly AnimationClip[] _slotClip = new AnimationClip[PoseCount];
        readonly bool[] _slotRunning = new bool[PoseCount];

        /// <summary>Put a clip in one of the swappable slots, ready to be posed.
        /// False when it could not be done - no graph, no clip, or the slot is still
        /// showing the last one (swapping under a visible weight cuts mid-pose). A
        /// refusal is never an error: the caller simply does without, which is the
        /// plain crossfade every walker had before this layer.</summary>
        bool PutClip(int pose, AnimationClip clip, bool force = false)
        {
            if (clip == null || !_graph.IsValid() || !_mixer.IsValid()) return false;
            if (_slotClip[pose] == clip) return true;
            // A SWAP UNDER A VISIBLE WEIGHT CUTS MID-POSE, and for a fidget that is a
            // fault: the refusal is what keeps a man from twitching between two idle
            // takes. A SPILL is the case where the cut is the point - the fall gives way
            // to the sprawl on the frame he touches the road, and there is nothing
            // gradual about arriving - so it may insist (PlayTake).
            if (_weights[pose] > 0.01f && !force) return false;
            if (_poses[pose].IsValid())
            {
                _graph.Disconnect(_mixer, pose);
                _poses[pose].Destroy();
            }
            var playable = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(playable, 0, _mixer, pose);
            _mixer.SetInputWeight(pose, 0f);
            _weights[pose] = 0f;
            _poses[pose] = playable;
            _clipLength[pose] = clip.length;
            _clipPace[pose] = clip.averageSpeed.magnitude;
            _slotClip[pose] = clip;
            return true;
        }

        /// <summary>Put a directional weapon gait into one of two loop ports and keep
        /// its stride phase across a change of direction. The other port carries the
        /// visible clip while this one is replaced, so changing at a route corner is a
        /// normal crossfade rather than a hard cut.</summary>
        protected int DirectionalGaitPose(AnimationClip clip, bool running,
            float pace, float fallbackPace, float cadence = 1f)
        {
            if (clip == null) return -1;

            int slot = _slotClip[PoseWeaponGaitA] == clip ? PoseWeaponGaitA
                     : _slotClip[PoseWeaponGaitB] == clip ? PoseWeaponGaitB
                     : -1;
            if (slot < 0)
            {
                // Prefer the port that is already quiet. On a rapid second turn both
                // may still be fading; replacing the lighter one is the least visible
                // and prevents an old direction from becoming permanently unswappable.
                slot = _weights[PoseWeaponGaitA] <= _weights[PoseWeaponGaitB]
                    ? PoseWeaponGaitA : PoseWeaponGaitB;

                double phase = 0d;
                if (_pose >= 0 && _pose < PoseCount && _poses[_pose].IsValid() &&
                    _clipLength[_pose] > 0.01f)
                    phase = _poses[_pose].GetTime() / _clipLength[_pose];

                if (!PutClip(slot, clip, force: true)) return -1;
                if (_poses[slot].IsValid() && _clipLength[slot] > 0.01f)
                    _poses[slot].SetTime((phase - System.Math.Floor(phase)) * _clipLength[slot]);
            }
            _slotRunning[slot] = running;
            float natural = ClipPace(slot, fallbackPace);
            SetPoseSpeed(slot, Mathf.Clamp(pace / natural, 0.45f, 1.5f) * cadence);
            return slot;
        }

        /// <summary>Seconds this body has been ticking. NOTHING IN THIS LAYER FIRES ON
        /// A FRESH WALKER: the pose a man is wired with is the walk (Setup), so a man
        /// dealt to a corner to STAND reads, on his very first frame, as a man who has
        /// just stopped walking - and a hundred and seventy bodies all planted a foot
        /// in the same frame the city finished loading. He is given a beat to settle
        /// into whatever he was actually dealt first.</summary>
        float _lived;
        const float SettleIn = 0.5f;

        /// <summary>Is this man near enough the camera - and settled enough in his own
        /// life - for any of the fine layer to read?</summary>
        protected bool Detailed =>
            Tf != null && _lived > SettleIn && PedDetail.Near(Tf.position);

        protected enum Join { None, Starting, Stopping, Turning, Stepping }

        Join _join;
        float _joinLeft, _joinLen;
        int _joinInto = PoseIdle;
        float _turnLeft;      // degrees of the turn still to be made, signed
        Vector3 _stepDir;     // world direction of a sidestep, while one runs
        float _stepPace;      // metres a second the shuffle clip carries him at
        float _actLeft;
        bool _actLoop;
        int _actFrom = PoseIdle;   // the pose the action was laid over

        /// <summary>The least turn worth taking a step for. Under it a man simply
        /// swivels, which is what a glance is; over it the swivel is the thing that
        /// gives the whole town away as puppets on a lazy susan.</summary>
        const float TurnStepAbove = 55f;

        /// <summary>What the join does to his ground speed: while one runs, THE CLIP
        /// CARRIES HIM, at the clip's own measured metres a second.
        ///
        /// This started life as a linear ramp - nothing to his pace over the start,
        /// his pace to nothing over the stop - and that was a guess, and the guess is
        /// what the player was watching. A start clip's feet accelerate on their own
        /// schedule, not on a straight line, so through every setting off and every
        /// pulling up the feet and the ground disagreed; measured, the planted foot
        /// was moving FASTER than the man (slip/pace 1.15 against 0.26 for a plain
        /// walk). Men set off and pull up constantly, so what that adds up to is a
        /// town that glides.
        ///
        /// The clip's own `averageSpeed` is the same oracle that keys every gait in
        /// this file. Held over the whole join rather than ramped: the residual is the
        /// acceleration curve inside the clip, which is a second-order error, where a
        /// ramp against the clip is a first-order one.</summary>
        protected float GaitGain => _join == Join.None ? 1f : _joinGain;

        float _joinGain;

        /// <summary>The least ground a start or a stop must carry before it is worth
        /// playing at all. A take that reports no root translation is an IN-PLACE take
        /// whatever its file is called - its feet step and go nowhere - so used as a
        /// join it can only slide. Refused, and the man falls back to the plain
        /// crossfade, which is what he had before this layer and which never slid.
        /// (`A_Idle_ToRun*RootMotion_Masc` measures 0.00 m/s and is refused by this.)</summary>
        const float JoinCarriesAtLeast = 0.3f;

        /// <summary>Whether a start, stop or sidestep take can carry the body it is
        /// joining. Kept here beside the runtime gate so animation benches can list
        /// only transitions a walker can really enter, rather than every similarly
        /// named take present in an imported pack.</summary>
        internal static bool CanCarryLinearJoin(AnimationClip clip) =>
            clip != null && clip.length >= 0.05f &&
            clip.averageSpeed.magnitude >= JoinCarriesAtLeast;

        /// <summary>Whether a turning take carries enough root yaw for the code-driven
        /// turn that uses it. An in-place take would turn the hips as well as the root
        /// and is deliberately refused by the live walker.</summary>
        internal static bool CanCarryTurn(AnimationClip clip, float degrees) =>
            clip != null && clip.length >= 0.05f &&
            Mathf.Abs(clip.averageAngularSpeed) * clip.length * Mathf.Rad2Deg >=
                NearestAuthored(degrees) * 0.4f;

        /// <summary>Is a join holding the body this frame? A derived agent that
        /// steers by hand reads it: while a turn is being taken, the turn owns his
        /// rotation and nothing else may write it.</summary>
        protected bool Joining => _join != Join.None;
        protected bool TurningOnSpot => _join == Join.Turning;

        /// <summary>Are his LEGS in a gait this frame? Not "does he have an order" -
        /// what his legs are actually playing. Anything that wants to move him a
        /// little asks this first: metres given to a man whose legs are already
        /// swinging hide inside his stride, and metres given to a man posed standing
        /// have to be paid for with a step.</summary>
        public bool LegsMoving => IsMotion(_pose);

        protected bool CurrentMotionIsRunning => IsRunningMotion(_pose);

        bool IsMotion(int pose) =>
            pose == PoseWalk || pose == PoseJog || pose == PoseSprint || pose == PoseCrouchWalk ||
            pose == PoseRifleWalk || pose == PoseRifleJog || pose == PoseRifleSprint ||
            pose == PoseRifleCrouchWalk || pose == PoseWeaponGaitA || pose == PoseWeaponGaitB;

        bool IsRunningMotion(int pose) =>
            pose == PoseJog || pose == PoseSprint ||
            pose == PoseRifleJog || pose == PoseRifleSprint ||
            ((pose == PoseWeaponGaitA || pose == PoseWeaponGaitB) && _slotRunning[pose]);

        /// <summary>A pose that cuts a join dead wherever it is: the flinch, the fall,
        /// the shot, the raised gun, going down behind something, a seat. Nobody
        /// finishes a graceful turn on the way down - and every one of these belongs
        /// to a state that turns the man HARD by its own hand (a fight swings him at
        /// 360 degrees a second), so a join left running would be a second writer on
        /// the same rotation.</summary>
        static bool BreaksJoin(int pose) =>
            pose == PoseHit || pose == PoseDeath || pose == PoseShoot || pose == PoseAim ||
            pose == PoseRifleGunplay || pose == PoseAutomaticShoot ||
            pose == PoseCoverShoot || pose == PoseRifleAim || pose == PoseRifleShoot ||
            pose == PoseCrouch || pose == PoseRifleCrouch || pose == PosePistolCrouch ||
            pose == PoseSitDown || pose == PoseSit || pose == PoseStandUp || pose == PoseRide;

        /// <summary>The heading a start clip is chosen against - where he is about to
        /// walk. The graph's stretch by default; an agent striding over open ground
        /// overrides it with the line he is about to take.</summary>
        protected virtual Vector3 JoinHeading => LinkDirection;

        /// <summary>Set off, or pull up, with the feet planted. Called from the
        /// locomotion blend, so every walker in every scene gets it for free.</summary>
        bool WantJoin(bool walking)
        {
            if (_join != Join.None || Tf == null || !Detailed) return false;
            bool moving = IsMotion(_pose);
            if (walking == moving) return false;

            bool running = walking ? IsRunningMotion(LocomotionPose) : IsRunningMotion(_pose);

            if (walking)
            {
                var to = JoinHeading;
                to.y = 0f;
                float off = to.sqrMagnitude > 1e-4f
                    ? Mathf.DeltaAngle(Tf.eulerAngles.y,
                        Quaternion.LookRotation(to.normalized).eulerAngles.y)
                    : 0f;
                return Begin(Join.Starting, CrewKit.StartMove(!running, off),
                    LocomotionPose, off);
            }
            return Begin(Join.Stopping, CrewKit.StopMove(!running), PoseIdle, 0f);
        }

        /// <summary>Take a turn on the spot with steps under it, if it is worth one
        /// and he is near enough to be seen taking it. False and the caller turns him
        /// the old way, which is a swivel.</summary>
        protected bool BeginTurn(float signedDegrees)
        {
            if (_join != Join.None || IsMotion(_pose) || !Detailed) return false;
            if (Mathf.Abs(signedDegrees) < TurnStepAbove) return false;
            var bodyTurn = signedDegrees < 0f ? _bodyTurnLeft : _bodyTurnRight;
            // The delivered masculine/feminine basics contain quarter turns only.
            // A reversal still uses CrewKit's authored 180: stretching a 90-degree
            // body take over 180 turns the transform twice as far as the hips expect.
            if (NearestAuthored(signedDegrees) == 90f && bodyTurn != null &&
                Begin(Join.Turning, bodyTurn, _pose, signedDegrees)) return true;
            return Begin(Join.Turning, CrewKit.TurnOnSpot(signedDegrees), _pose, signedDegrees);
        }

        /// <summary>Turn him toward this rotation: a big turn is TAKEN, in steps,
        /// and drives itself from here on; a small one (or a man too far off for any
        /// of it to read) is eased round at <paramref name="degreesPerSecond"/> as
        /// every walker always was. The one call a derived agent needs - it does the
        /// right thing in both cases and never fights the join for the rotation.</summary>
        protected void TurnToward(Vector3 heading, float degreesPerSecond, float dt)
        {
            if (_join == Join.Turning) return;              // the turn owns his body
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) return;
            var want = Quaternion.LookRotation(heading.normalized);
            if (_join == Join.None &&
                BeginTurn(Mathf.DeltaAngle(Tf.eulerAngles.y, want.eulerAngles.y))) return;
            Tf.rotation = Quaternion.RotateTowards(Tf.rotation, want, degreesPerSecond * dt);
        }

        // Said once if the packs are ever swapped for in-place takes; the turns then
        // simply stop happening instead of turning every man in the town twice.
        static bool _saidInPlace;

        /// <summary>Take a STEP out of somebody's way rather than being slid out of
        /// it. Anything that eases a man who is standing still - two of a crew who
        /// have converged on one spot - hands the metres to this instead of writing
        /// his transform, or what the player sees is a man gliding sideways with his
        /// feet nailed to the pavement. False when he is busy (already stepping, or
        /// in a join) or too far off the camera to be worth it: the caller then
        /// leaves him where he is and asks again next frame.</summary>
        protected bool BeginSidestep(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 1e-4f) return false;
            worldDir.Normalize();

            // ALREADY STEPPING: RE-AIM IT, do not start another. The direction is not
            // committed for the length of the clip, and committing it was a bug with
            // a name: the man he is stepping away from is stepping too, so by the time
            // a fixed two-thirds of a second is spent the reason for the step has moved
            // and a fresh one is wanted the other way. That is a limit cycle, and what
            // it looks like is a crew shuffling left and right for ever instead of
            // standing round its lieutenant. Re-aimed every frame it converges, which
            // is what the transform ease it replaced always did.
            if (_join == Join.Stepping)
            {
                _stepDir = worldDir;
                _stepAsked = Time.frameCount;
                return true;
            }
            if (_join != Join.None || Tf == null || !Detailed) return false;
            var clip = CrewKit.Shuffle(Tf.InverseTransformDirection(worldDir));
            _stepDir = worldDir;
            _stepAsked = Time.frameCount;
            return Begin(Join.Stepping, clip, _pose, 0f);
        }

        // The frame the step was last wanted. A sidestep is not a fixed-length clip to
        // be sat through - it lasts exactly as long as whoever asked for it keeps
        // asking, and ends the moment the overlap is gone. Sitting the clip out is how
        // a man overshoots and needs a step back. The frame BEFORE counts too, because
        // the pass that asks and the tick that spends run in an order nobody promises.
        int _stepAsked = -1;

        bool Begin(Join kind, AnimationClip clip, int into, float degrees)
        {
            if (clip == null || clip.length < 0.05f) return false;

            // THE TURN HAS TO BE IN THE ROOT, NOT THE BODY. This whole layer rests on
            // the joins being the RootMotion takes: their rotation is extracted to the
            // root, root motion is off, so it is thrown away, and the code is free to
            // turn the whole man by exactly the angle it meant. Handed an IN-PLACE
            // take, the same code turns him twice - once in the hips, once on the
            // transform - and snaps him back when the clip ends. So the clip is ASKED
            // how much yaw its root carries, and one that carries none of it is
            // refused: the caller falls back to the plain swivel, which is what every
            // walker had before any of this. A wrong pack is then a layer that does
            // nothing, never a town of men spinning through two right angles.
            if (kind != Join.Stopping && Mathf.Abs(degrees) >= TurnStepAbove)
            {
                float baked = Mathf.Abs(clip.averageAngularSpeed) * clip.length * Mathf.Rad2Deg;
                if (!CanCarryTurn(clip, degrees))
                {
                    if (!_saidInPlace)
                    {
                        _saidInPlace = true;
                        Debug.LogWarning($"[RoadDemo] '{clip.name}' carries {baked:F0} deg " +
                            "of root yaw where a turn take should carry " +
                            $"{NearestAuthored(degrees):F0} - it is an in-place take. " +
                            "The stepped turns are off; walkers swivel as before.");
                    }
                    return false;
                }
            }

            // A start, a stop or a sidestep has to CARRY him; a turn must carry
            // nothing at all.
            float carries = clip.averageSpeed.magnitude;
            if (kind != Join.Turning && !CanCarryLinearJoin(clip)) return false;
            _stepPace = kind == Join.Stepping ? carries : 0f;

            if (!PutClip(PoseJoin, clip)) return false;
            // his ground speed for the whole join is the clip's own, as a fraction of
            // the pace he keeps - so the feet in the clip and the metres under them
            // are the same metres. A turn carries nothing, and stands him still.
            _joinGain = kind == Join.Turning || kind == Join.Stepping || Speed <= 0.05f
                ? 0f : Mathf.Clamp01(carries / Speed);
            // A turn is handed back to whatever he was doing, and what he was doing
            // may itself have been a one-shot out of the other slot - which the join
            // is about to cut short. Handing him back to a finished action would
            // leave him frozen on its last frame until something else posed him.
            if (into == PoseAct || into == PoseJoin) into = PoseIdle;
            _join = kind;
            _joinLen = clip.length;
            _joinLeft = _joinLen;
            _joinInto = into;
            _turnLeft = degrees;
            // A turn clip is authored at a quarter or a half turn and the wanted
            // angle is whatever it is: it is played at the rate that spends the
            // clip over the true angle, so the feet and the body arrive together.
            // Bounded, because a five degree turn stretched over a quarter-turn
            // clip is a man standing still with his shoes twitching.
            float rate = kind == Join.Turning
                ? Mathf.Clamp(Mathf.Abs(degrees) / NearestAuthored(degrees), 0.6f, 1.6f)
                : 1f;
            _joinLen /= rate;
            _joinLeft = _joinLen;
            RestartPose(PoseJoin, 0f, rate);
            _pose = PoseJoin;
            EndAct();
            return true;
        }

        /// <summary>The turn the chosen clip was authored at.</summary>
        static float NearestAuthored(float degrees) => Mathf.Abs(degrees) < 130f ? 90f : 180f;

        void EndJoin()
        {
            if (_join == Join.None && _pose != PoseJoin) return;
            _join = Join.None;
            _turnLeft = 0f;
            // never left holding the join's own slot: the clip in it is a one-shot
            // that has finished, so a man handed back to it stands frozen on its
            // last frame until something else happens to pose him
            _pose = _poses[_joinInto].IsValid() && _joinInto != PoseJoin ? _joinInto
                : _poses[PoseIdle].IsValid() ? PoseIdle : PoseWalk;
        }

        /// <summary>One frame of the join: the turn's own degrees, and the clock. The
        /// hand-over is a shade before the clip's last frame, so the crossfade into
        /// the gait is over by the time the planted foot would leave the ground.</summary>
        void SpendJoin(float dt)
        {
            if (_join == Join.None) return;
            // A START turns him too, and over its own clip - the 90 and 180 starts
            // are a man swinging his hips round onto the new line, and if the walker
            // has already snapped himself round to it (the plain rotation ease is
            // half a turn a frame at this rate) the clip has nothing left to do and
            // reads as a stumble. So while a join runs, the join owns his heading.
            // A SIDESTEP MOVES HIM, and moves him at the clip's own metres a second -
            // which is the whole point of doing it with a clip at all. He stops short
            // of anything he would step into; the pass that asked for the step re-asks
            // next time round if he still needs it.
            if (_join == Join.Stepping)
            {
                if (_stepAsked < Time.frameCount - 1) { EndJoin(); return; }
                if (Tf != null && _stepPace > 0.01f)
                {
                    var to = Tf.position + _stepDir * (_stepPace * dt);
                    if (!WalkObstacles.Occupied(to, WalkObstacles.Radius) && WalkObstacles.InCity(to))
                        Tf.position = to;
                }
            }
            if (_join != Join.Stopping && _join != Join.Stepping && Tf != null && _joinLen > 0.01f)
            {
                float step = _turnLeft * Mathf.Min(dt / _joinLen, 1f);
                Tf.rotation = Quaternion.Euler(0f, step, 0f) * Tf.rotation;
                _turnLeft -= step;
            }
            _joinLeft -= dt;
            if (_joinLeft <= 0.12f)
            {
                // whatever is left of the turn is spent rather than dropped, or a
                // man who was turned 90 degrees ends up facing 84
                if (_join != Join.Stopping && _join != Join.Stepping &&
                    Tf != null && Mathf.Abs(_turnLeft) > 0.01f)
                {
                    Tf.rotation = Quaternion.Euler(0f, _turnLeft, 0f) * Tf.rotation;
                    _turnLeft = 0f;
                }
                if (_joinLeft <= 0f) EndJoin();
            }
        }

        // -------------------------------------------------------------- the actions

        /// <summary>Spend one of the idle pack's one-shots - a look at the watch, a
        /// swig, a shrug at whatever was just said. It plays over his stand and hands
        /// itself back; anything the man is actually told to do cuts it short. False
        /// when he is too far off to be worth it, busy, or the slot is still clearing.</summary>
        protected bool PlayAction(AnimationClip clip, float speed = 1f)
        {
            if (clip == null || _join != Join.None || _actLeft > 0f || !Detailed) return false;
            if (!PutClip(PoseAct, clip)) return false;
            RestartPose(PoseAct, 0f, speed);
            _actLoop = false;
            _actLeft = clip.length / Mathf.Max(speed, 0.05f);
            _actFrom = _pose;
            _pose = PoseAct;
            return true;
        }

        /// <summary>Wear one of the idle pack's stances for a while - leant on the
        /// wall, swaying, down on the knees. Same slot as the one-shots, wrapped
        /// instead of run once.</summary>
        protected bool HoldAction(AnimationClip clip, float seconds)
        {
            if (clip == null || _join != Join.None || !Detailed) return false;
            if (!PutClip(PoseAct, clip)) return false;
            if (!_actLoop || _actLeft <= 0f) ScatterPhase(PoseAct);
            if (_actLeft <= 0f) _actFrom = _pose;
            _actLoop = true;
            _actLeft = seconds;
            _pose = PoseAct;
            return true;
        }

        /// <summary>Is one running? A derived agent holds off its own business while
        /// a man is in the middle of something.</summary>
        protected bool Acting => _actLeft > 0f;

        // ---------------------------------------------------------------- the takes

        /// <summary>What PlayTake put on him, while it is still showing.</summary>
        AnimationClip _take;

        /// <summary>Play this take over everything, and HOLD it until somebody takes it
        /// off him (<see cref="EndTake"/>).
        ///
        /// <see cref="PlayAction"/> is the polite door and every one of its refusals is
        /// right for a fidget: not while he is mid-join, not over another one-shot, not
        /// on a man too far off to read. All of them are wrong for a man who has just
        /// left a motorcycle at fifty kilometres an hour, which is the one thing in the
        /// town that happens TO a body rather than being chosen by it (RiderSpill).
        ///
        /// The hold is the other half. A one-shot handed back when its clock runs out is
        /// what a gesture wants; a man in the air whose fall clip runs out and hands him
        /// back stands bolt upright in mid-flight. So the slot is held on the clip's last
        /// frame - which is also what makes <see cref="TakeFinished"/> a read rather than
        /// a thing anyone has to stop.</summary>
        public bool PlayTake(AnimationClip clip, bool loop, float speed, float at)
        {
            if (clip == null) return false;
            CancelJoin();                       // clears the act slot with it
            if (!PutClip(PoseAct, clip, force: true)) return false;
            RestartPose(PoseAct, at, Mathf.Max(0.05f, speed));
            _actLoop = loop;
            _actLeft = float.MaxValue;          // held: nothing hands this one back
            _actFrom = PoseIdle;
            _pose = PoseAct;
            _take = clip;
            return true;
        }

        /// <summary>The take on him this instant, or null.</summary>
        public AnimationClip Take => _pose == PoseAct ? _take : null;

        /// <summary>A one-shot take that has reached its last frame.</summary>
        public bool TakeFinished =>
            _take != null && _pose == PoseAct && !_actLoop &&
            PoseTime(PoseAct) >= PoseLength(PoseAct) - 0.03f;

        /// <summary>Give him back to himself - the take off, the slot cleared, whatever
        /// he was doing before it resumed. A held take never ends on its own, so this is
        /// not a tidy-up: without it the man goes on standing in the last frame of his
        /// landing for ever, and every SetPose he makes afterwards is swallowed.</summary>
        public void EndTake()
        {
            _take = null;
            _actLeft = 0f;
            EndAct();
        }

        protected void EndAct()
        {
            // THE GUARD IS "IS THE SLOT SHOWING", NOT "IS THERE TIME LEFT". Written as
            // `if (_actLeft <= 0f) return;` this method could not do the one job it is
            // called for most: the countdown in TickBlend takes the clock to zero and
            // THEN calls it, so it bailed out at the first line and left the man posed
            // on the last frame of a finished one-shot. He then slid down the street
            // frozen in a shrug - which is the man the player watched moving as if
            // walking with his legs stopped dead.
            if (_actLeft <= 0f && _pose != PoseAct) return;
            _actLeft = 0f;
            _actLoop = false;
            // back to whatever it was laid over - the stand, or the word he was in
            // the middle of. Handed back rather than dropped on the floor, or a
            // gesture mid-conversation would leave the man standing mute.
            if (_pose != PoseAct) return;
            _pose = _actFrom != PoseAct && _poses[_actFrom].IsValid() ? _actFrom
                : _poses[PoseIdle].IsValid() ? PoseIdle : PoseWalk;
        }

        /// <summary>Draw one of a pool and play it. The pools are the town's, so a
        /// man who has nothing to draw from simply stands, and nothing breaks.</summary>
        protected bool PlayAction(IReadOnlyList<AnimationClip> pool, float speed = 1f)
        {
            if (pool == null || pool.Count == 0) return false;
            return PlayAction(pool[Random.Range(0, pool.Count)], speed);
        }

        /// <summary>Halt the whole graph (a civilian gone indoors) or let it run
        /// again: a body switched off still costs its animation otherwise.</summary>
        public void Suspend(bool suspended)
        {
            // A JOIN MUST NOT SURVIVE THE BODY BEING PUT AWAY. It carries a clock and
            // (for a start or a turn) degrees of rotation still owed, and a man who
            // goes indoors halfway through one comes back out minutes later - at a
            // different door, facing a different way - and spends the rest of a turn
            // he decided to make in another street.
            if (suspended) CancelJoin();
            if (!_graph.IsValid()) return;
            if (suspended) { if (_graph.IsPlaying()) _graph.Stop(); }
            else if (!_graph.IsPlaying()) _graph.Play();
        }

        /// <summary>Drop whatever join or action is running, right now and without
        /// spending what is left of it. For the moments a body stops being where it
        /// was: put in a seat, put away indoors, put down dead.</summary>
        protected void CancelJoin()
        {
            _join = Join.None;
            _turnLeft = 0f;
            EndAct();
            if (_pose == PoseJoin || _pose == PoseAct)
                _pose = _poses[PoseIdle].IsValid() ? PoseIdle : PoseWalk;
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

        // ------------------------------------------------------- the debug tag
        //
        // What a man says about himself when the F3 overlay asks. Three questions,
        // because they are three different things: who he is, what state his own
        // machine is in, and what he means to do about it. The trace already asks
        // the first two of every walker in the town (TraceState), so the tag is that
        // answer put on the screen rather than a second bookkeeping of it.

        /// <summary>What to call him over his head. A man with no name is his tag and
        /// his number, which is what the trace calls him too.</summary>
        public virtual string DebugName => Tag + " #" + Id;

        /// <summary>The state his own machine is in - the enum, near enough.</summary>
        public virtual string DebugState => TraceState();

        /// <summary>What he means to do: where he is going, who he is going at, what
        /// is holding him. Empty for a walker with nothing to say.</summary>
        public virtual string DebugIntent => string.Empty;

        /// <summary>The gait his body is playing this frame - the one place a tag can
        /// be read against the legs, so "walking" over a man in a sprint clip is a
        /// bug the overlay shows rather than hides.</summary>
        public string DebugGait => PoseName(_pose);

        public string DebugLocomotion => _locomotionLabel ?? string.Empty;

        // ------------------------------------------------------------ the foot slip
        //
        // THE ONE HONEST MEASURE OF A SLIDE. Everything else on the row is what the
        // code intended - the pose it chose, the rate it asked for, the metres it
        // meant to cover. This is what the BONES did: at every moment of a real walk
        // one foot is planted, so the SLOWER of the two feet should be very nearly
        // still in the world. A man who glides down the street has both feet moving
        // with his body, and the slower foot's speed is his own speed. Kept only while
        // the black box is open, because it is two bone reads a frame per man.

        Animator _animator;
        Transform _footL, _footR;
        Vector3 _footLWas, _footRWas;
        bool _footSeen, _alwaysAnimate;
        float _slipSum;
        int _slipCount;

        void WatchFeet(float dt)
        {
            // the first recorded frame is where the culling is lifted: a run that is
            // being measured must evaluate every skeleton, or the measurement is of
            // nothing at all
            if (!_alwaysAnimate)
            {
                _alwaysAnimate = true;
                if (_animator != null) _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return;   // this frame's bones are stale; start the clock next frame
            }
            if (_footL == null || _footR == null || dt <= 1e-4f) return;
            var l = _footL.position;
            var r = _footR.position;
            if (_footSeen)
            {
                var dl = l - _footLWas; dl.y = 0f;
                var dr = r - _footRWas; dr.y = 0f;
                // the planted foot is whichever moved less this frame
                // AVERAGED, never the worst frame. A crossfade swings every bone in
                // the body over its quarter second, so the worst frame in any window
                // is a blend transient and not a slide at all - measured that way a
                // man crouching perfectly still "slips" at over a metre a second.
                _slipSum += Mathf.Min(dl.magnitude, dr.magnitude) / dt;
                _slipCount++;
            }
            _footLWas = l;
            _footRWas = r;
            _footSeen = true;
        }

        /// <summary>Metres a second the CLIP now playing carries this man's feet over,
        /// at the rate it is being played at: the gait's own claim about the ground.
        /// Zero for a pose that goes nowhere (a stand, a fidget, a turn), which is the
        /// right answer - a man in one of those should not be moving either.</summary>
        protected float FeetPace
        {
            get
            {
                if (!_poses[_pose].IsValid()) return 0f;
                float natural = _clipPace[_pose];
                if (natural <= 0.5f) return 0f;
                return natural * (float)_poses[_pose].GetSpeed();
            }
        }

        /// <summary>The heaviest pose that is NOT the selected one, and its weight.
        /// In a settled man this is nothing at all; mid-crossfade it is the pose he is
        /// leaving. Anything else standing at weight for any length of time is a
        /// fault, and this is the only field that can show it.</summary>
        int SecondPose(out float weight)
        {
            int best = _pose;
            weight = 0f;
            for (int i = 0; i < PoseCount; i++)
            {
                if (i == _pose || !_poses[i].IsValid()) continue;
                if (_weights[i] <= weight) continue;
                weight = _weights[i];
                best = i;
            }
            return best;
        }

        /// <summary>The trace's word for a pose. Short on purpose - it rides on every
        /// walker's row, and the trace is read a hundred thousand rows at a time.</summary>
        string PoseName(int pose) => pose switch
        {
            PoseWalk => "walk", PoseIdle => "idle", PoseSitDown => "sitdown",
            PoseSit => "sit", PoseStandUp => "standup", PoseTalk => "talk",
            PoseShout => "shout", PosePistolIdle => "gunlow", PoseAim => "aim",
            PoseShoot => "shoot", PoseHit => "hit", PoseDeath => "death",
            PoseJog => "jog", PoseCrouch => "crouch", PoseRide => "ride",
            PoseSprint => "sprint", PoseCrouchWalk => "crouchwalk",
            PoseRifleWalk => "riflewalk", PoseRifleJog => "riflejog",
            PoseRifleSprint => "riflesprint", PoseRifleCrouchWalk => "riflecrouchwalk",
            PoseRifleGunplay => "riflefire", PoseAutomaticShoot => "autofire",
            PoseCoverShoot => "coverfire", PoseRifleIdle => "rifleidle",
            PoseRifleAim => "rifleaim", PoseRifleShoot => "rifleshoot",
            PoseRifleCrouch => "riflecrouch", PosePistolCrouch => "pistolcrouch",
            PoseWeaponGaitA or PoseWeaponGaitB =>
                _slotClip[pose] != null ? _slotClip[pose].name : "weapongait",
            PoseJoin => "join", PoseAct => "act", _ => "?",
        };

        /// <summary>Drift every mixer weight toward the selected pose - and, first,
        /// spend one frame of whatever join or action is running. Every state of
        /// every agent in the town ends by calling this, which is why the join layer
        /// is hung off it: there is no state a man can be in where his turn stops
        /// being spent.</summary>
        protected void TickBlend(float dt)
        {
            if (!_mixer.IsValid()) return;
            if (_lived <= SettleIn) _lived += dt;
            SpendJoin(dt);
            if (_actLeft > 0f)
            {
                _actLeft -= dt;
                if (_actLeft <= 0f) EndAct();
            }
            // A ONE-SHOT SLOT IS NEVER LEFT HOLDING A FINISHED CLIP. Both swappable
            // ports carry clips that end, and a man left posed on the last frame of
            // one goes on standing in it for as long as nothing else happens to pose
            // him - which, in every state that does not re-assert a pose each frame (a
            // chat, a stare, a seat), is for ever. He slides down the street stuck in
            // a shrug. Every route out of a join or an action already hands the body
            // back; this is the net under all of them, and it costs two comparisons.
            if (_pose == PoseAct && _actLeft <= 0f) EndAct();
            else if (_pose == PoseJoin && _join == Join.None) EndJoin();
            if (DriveTrace.On) WatchFeet(dt);
            for (int i = 0; i < PoseCount; i++)
            {
                if (!_poses[i].IsValid()) continue;
                if (LoopByHand[i] || (i == PoseAct && _actLoop))
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
            _link = back;
            _cameFrom = back.From;
            _waiting = false;
            CarrySeat();   // the reverse frame flips the lateral's axis; his feet stay
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
        protected void BlendLocomotion(float dt, bool walking, bool joins = true)
        {
            if (joins) WantJoin(walking);
            else if (Joining) CancelJoin();
            SetPose(walking ? LocomotionPose : PoseIdle);
            TickBlend(dt);
        }

        /// <summary>A gearing on the walker's pace, 1 by default. A crew man ahead of
        /// his boss walks at a fraction of it (the dawdle) - SLOWED, never stopped:
        /// a man stopped dead on the pavement is a bollard his own boss brakes
        /// behind, and the whole crew then crawls at a quarter pace (measured).</summary>
        protected float PaceScale = 1f;

        /// <summary>Keep the playing clip in step with the pace the graph actually
        /// carries him at this frame. The base walker only ever walks a stretch, so
        /// the walk is the one clip to hold; a walker with a run in his wardrobe (a
        /// fleeing civilian, a crew under orders) overrides this to keep the jog
        /// honest too - or to drop out of it where the crowd brakes him below the
        /// band a run clip reads at.</summary>
        protected virtual void GearLocomotion(float speed)
        {
            if (LocomotionPose == PoseWalk) HoldWalkRate(speed);
        }

        /// <summary>Metres ahead the stretch is read for a clear line past the street
        /// furniture. A stride and a half at a walk; a runner covers the same metres
        /// in half the time, so read at the walk's distance his corrections come late
        /// and hard and he zig-zags lamp to lamp - the derived walker stretches it
        /// while a run is on him.</summary>
        protected virtual float FreeLineAhead => 2f;

        /// <summary>How much of the crowd's shove the stretch's line takes - all of
        /// it at a walk. A runner threads the same people at twice the closing speed,
        /// so a full-width shove each pass is a man weaving down the pavement; the
        /// derived walker halves it while a run is on him.</summary>
        protected virtual float PushGain => 1f;

        /// <summary>Metres a second the graph carries him at, before the crowd's brake
        /// and the dawdle. A citizen walks, quicker over a crossing because nobody
        /// dawdles on a zebra; a man of the outfit with somewhere to be RUNS, and the
        /// crossing's hustle is nothing to add to a run.
        ///
        /// This is the seam the sidewalks needed to be able to run at all. The free
        /// stride picks its own pace frame by frame, but the graph's is one number, and
        /// it was Speed - so a crew ordered down four hundred metres of pavement, which
        /// is how a player actually sends one anywhere in this town, could only ever
        /// walk it.</summary>
        protected virtual float GraphPace(bool gated) => gated ? Speed * CrossHustle : Speed;

        /// <summary>
        /// Prove the actual step against the live furniture. Streamed blocks may
        /// arrive after the graph's clearance was sampled, for civilians as well as crews.
        /// </summary>
        protected virtual bool GraphStepClear(Vector3 from, Vector3 to) =>
            !WalkObstacles.Standing(to, WalkObstacles.Radius) &&
            !WalkObstacles.BlocksStanding(from, to, WalkObstacles.Radius);

        /// <summary>Called without committing metre, lateral or transform when the
        /// live proof rejects a graph step. A derived ordered walker may hand the
        /// remaining trip to another route system.</summary>
        protected virtual void GraphStepBlocked(Vector3 wanted)
        {
            if (_link == null || Tf == null) return;
            if (_link.LiveClearanceVersion != WalkObstacles.Version)
            {
                WalkObstacles.SampleWalk(_link, WalkObstacles.Radius);
                _link.LiveClearanceVersion = WalkObstacles.Version;
            }
            // Get onto the newly sampled clear line before advancing into the prop.
            // If the entire corridor is closed, turn back to the last graph junction.
            var lateral = Mathf.Clamp(_link.FreeLine(_t, FreeLineAhead, _lane), -1.9f, 1.9f);
            var right = new Vector3(LinkDirection.z, 0f, -LinkDirection.x);
            var shift = Mathf.Clamp(lateral - _lateral,
                -2.4f * Time.deltaTime, 2.4f * Time.deltaTime);
            var to = Tf.position + right * shift;
            if (Mathf.Abs(shift) > 0.001f && GraphStepClear(Tf.position, to))
            {
                Tf.position = to;
                _lateral += shift;
                _graphBlockedAt = -1f;
                return;
            }
            if (_graphBlockedAt < 0f) _graphBlockedAt = Time.time;
            if (Time.time - _graphBlockedAt >= 2f)
            {
                ReverseCourse();
                _graphBlockedAt = -1f;
            }
        }

        float _graphBlockedAt = -1f;

        /// <summary>Pure commit gate used by Move: rejected geometry cannot advance a
        /// link metre or report arrival. Kept as a model seam so the endpoint regression
        /// is testable without constructing a walker or a Unity scene.</summary>
        internal static bool TryGraphAdvance(float currentT, float proposedT, float length,
                                             bool clear, out float committedT,
                                             out bool arrived)
        {
            committedT = currentT;
            arrived = false;
            if (!clear) return false;
            committedT = proposedT;
            arrived = proposedT >= length;
            return true;
        }

        void Move(float dt)
        {
            // GaitGain is the join's: 1 for a man simply walking, ramping up under a
            // start, ramping off under a stop, nothing at all while he takes a turn.
            float speed = GraphPace(_link.Gated) * _hold * PaceScale * GaitGain;
            GearLocomotion(speed);
            float nextT = _t + speed * dt;
            if (nextT >= _link.Length)
            {
                var nodePosition = _link.To.Pos;
                // dt=0 is Init's one intentional seating snap onto the graph. It is
                // placement, not a walked step, and must not hand a half-initialised
                // derived agent to its runtime reroute hook.
                bool clear = dt <= 0f || GraphStepClear(Tf.position, nodePosition);
                if (!TryGraphAdvance(_t, nextT, _link.Length, clear,
                                     out float committedT, out _))
                {
                    GraphStepBlocked(nodePosition);
                    return;
                }
                _t = committedT;
                _graphBlockedAt = -1f;
                var arrived = _link.To;
                _cameFrom = _link.From;
                if (OnArrived(arrived))
                    PickNext(arrived, keepAwayFrom: _cameFrom);
                return;
            }

            float f = nextT / _link.Length;
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
                float want = Mathf.Clamp(_lane + _push * PushGain, -limit, limit);
                if (!_link.Gated)
                    want = Mathf.Clamp(_link.FreeLine(nextT, FreeLineAhead, want), -limit, limit);
                float nextLateral = Mathf.MoveTowards(_lateral, want, 2.4f * dt);

                pos += new Vector3(dirN.z, 0f, -dirN.x) * nextLateral;
                var step = HoldStep(pos, dt, speed);
                bool clear = dt <= 0f || GraphStepClear(Tf.position, step);
                if (!TryGraphAdvance(_t, nextT, _link.Length, clear,
                                     out float committedT, out _))
                {
                    GraphStepBlocked(step);
                    return;
                }
                _t = committedT;
                _graphBlockedAt = -1f;
                _lateral = nextLateral;
                // a join owns his heading while it runs (SpendJoin); the rest of the
                // time he eases onto the line of the stretch as he always has
                if (Joining) Tf.position = step;
                else
                {
                    var rot = Quaternion.Slerp(
                        Tf.rotation, Quaternion.LookRotation(dirN), dt <= 0f ? 1f : 8f * dt);
                    Tf.SetPositionAndRotation(step, rot);
                }
                return;
            }
            var held = HoldStep(pos, dt, speed);
            bool heldClear = dt <= 0f || GraphStepClear(Tf.position, held);
            if (!TryGraphAdvance(_t, nextT, _link.Length, heldClear,
                                 out float heldT, out _))
            {
                GraphStepBlocked(held);
                return;
            }
            _t = heldT;
            Tf.position = held;
        }

        /// <summary>A hard ceiling on how far any one frame may move a person, whatever
        /// the frame time. The per-frame caps are all dt-scaled, so a frame that hitches
        /// - the scene stalls, dt jumps to a third of a second - would let a man cross a
        /// lane in a single write and read as a teleport (the fault the player reported
        /// when the city stutters: a man sidestepping an obstacle "jumps" that way). A
        /// stalled scene may drop him behind his mark for a frame; it may never fling him
        /// across the street. Set above any true per-frame stride so normal play never
        /// meets it.</summary>
        public const float MaxStepPerFrame = 0.75f;

        /// <summary>THE GRAPH NEVER TELEPORTS A MAN. His position is rebuilt from
        /// metre-plus-lateral every frame, and whenever his true feet do not fit
        /// that point - reseated off a stale link, a lateral past the clamp, slid
        /// aside for a chat - writing it verbatim snapped him metres in one frame
        /// (6.9 m, measured, read by the player as a respawn). The write is capped
        /// at a stride instead: he WALKS onto his line, a touch quicker than his
        /// pace so he always catches it. Capped on the FINISHED point, lateral and
        /// all - capping the centre-line point and adding the lateral after it fed
        /// the lateral back in every frame and launched men out of the city at
        /// twenty-two metres a second. dt = 0 is the one placement that must snap
        /// (Init seating a fresh walker on his stretch).</summary>
        Vector3 HoldStep(Vector3 want, float dt, float speed)
        {
            if (dt <= 0f) return want;
            var to = want - Tf.position;
            to.y = 0f;
            float cap = Mathf.Min((speed + 2f) * dt, MaxStepPerFrame);
            float off = to.magnitude;
            if (off <= cap) return want;
            var held = Tf.position + to * (cap / off);
            return new Vector3(held.x, want.y, held.z);
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
            CarrySeat();   // the new stretch's frame, same feet - no snap at the corner
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
