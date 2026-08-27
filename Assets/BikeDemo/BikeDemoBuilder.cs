using System.Text;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace BikeDemo
{
    // Two men on a machine, stood still, with every number that puts them there on
    // the Inspector - the bench for sitting a rider properly.
    //
    // The pose is DERIVED, not animated: there is no riding clip in the project, so
    // BikePose writes the four limbs and the spine over whatever is playing
    // underneath, reaching for points BikeBody measured off the machine itself. That
    // means a rider who sits wrong is wrong in one of two places, and the bench puts
    // both on the same screen:
    //
    //   THE MACHINE'S PROPORTIONS (BikeBody) - where the saddle is off the rear axle,
    //   how far back and how high the pillion sits, where the pegs are. Change one and
    //   every point the pose reaches for is re-derived on the spot (BikeBody.Place).
    //
    //   THE MAN'S REACH (BikePose) - which way the elbow is thrown, which way the knee
    //   bends, how the toes lie on the peg, how wide the pillion holds on. These were
    //   set by eye in the code and were not reachable from anywhere until now.
    //
    // Nothing here is a copy of the pose code: the bench only pushes the same static
    // proportions the city uses, so a number that looks right here IS the number to
    // paste back (press L and it prints the block ready to paste).
    //
    // WHO IS WHO: the man in front is the DRIVER (his fists are on the bars, his boot
    // goes down at a stop), the man behind is the SHOOTER (he holds the driver's waist
    // until he is aiming, then his gun arm goes out at the mark). They are labelled
    // over their heads and their fitting points are marked in their own colour, so the
    // two are never mixed up.
    //
    // Editor only, like the demos it is built from.
    [ExecuteAlways]
    public class BikeDemoBuilder : MonoBehaviour
    {
        // The authored numbers, in ONE place: the field initialisers below and Reset()
        // both read these, so the bench's idea of "default" cannot drift from itself.
        // They mirror BikeBody's and BikePose's own authored values - when one of those
        // is retuned for good, bring the matching const with it.
        const float DefSaddleAhead = 0.72f, DefSaddleBehindBars = 0.56f, DefSaddleBelowBars = 0.30f;
        const float DefSaddleAboveWheel = 0.28f;
        const float DefHipsAbove = 0.09f, DefPillionBehind = 0.48f, DefPillionAbove = 0.07f;
        const float DefPillionShare = 0.25f, DefPegHeight = 0.42f, DefPegWidth = 0.55f;
        const float DefPegAhead = 0.26f, DefPillionPegBack = 0.06f, DefPillionPegLift = 0.11f;
        const float DefCrouchMax = 15f, DefCrouchAt = 16f;
        const float DefElbowOut = 0.55f, DefElbowDown = 0.42f, DefElbowBack = 0.18f;
        const float DefKneeAhead = 0.8f, DefKneeOut = 0.5f, DefKneeOutDown = 0.85f, DefKneeDown = 0.1f;
        const float DefToeAhead = 0.94f, DefToeDown = 0.34f;
        const float DefHoldWide = 0.19f, DefHoldLift = 0.06f, DefHoldForward = 0.05f;

        [Header("Which bench")]
        [Tooltip("PLAY RUNS THE SPILL SHOW: four machines riding four lanes, one coming " +
                 "off each way, looped for ever (BikeShow). Off, and Play is this bench - " +
                 "one machine stood still with every proportion on the Inspector.\n\n" +
                 "The editor is always this bench either way: a pose is a still thing and " +
                 "is looked at standing still, and a spill is four seconds of motion and " +
                 "cannot be looked at at all without Play.")]
        public bool spillShow = true;

        [Header("The machine")]
        [Tooltip("Which two-wheeler is stood here, by name. Change it in Play and the bench " +
                 "rebuilds around the new one. The packs' machines: SM_Veh_Motorbike_01 " +
                 "(the city bike), SM_Veh_Moped_01, SM_Veh_Motorbike_02 (the police tourer).")]
        public string machine = "SM_Veh_Motorbike_01";
        [Tooltip("How hard the bars are over. The driver's fists follow them.")]
        [Range(-32f, 32f)] public float steer;
        [Tooltip("Metres a second. Bends the driver over the tank and spins the wheels; " +
                 "the shooter leans a little later than he does.")]
        [Range(0f, 30f)] public float speed;
        [Tooltip("The machine's lean, in degrees. It leans about its own contact line, so " +
                 "no tyre ever lifts.")]
        [Range(-25f, 25f)] public float lean;
        [Tooltip("Stopped at a light: the driver's left boot comes off the peg and goes to " +
                 "the road. The shooter never puts a foot down.")]
        public bool footDown = true;

        [Header("The men")]
        [Tooltip("The driver's body, by name. Empty: a gang face out of GangLooks.")]
        public string driverBody = "";
        [Tooltip("The shooter's body. Empty: another gang face, never the driver's.")]
        public string shooterBody = "";
        [Tooltip("Put a gun in the shooter's fist (CrewArms, the same seating the street uses).")]
        public bool shooterArmed = true;
        public string gun = "SM_Wep_Machine_Pistol_01";
        [Tooltip("The shooter has the gun out at a mark. Off: both his hands on the driver's waist.")]
        public bool aiming = true;
        [Tooltip("Where the mark is, in degrees round the machine - 90 is straight out to " +
                 "his right, 180 is behind, 0 is over the driver's shoulder.")]
        [Range(-180f, 180f)] public float aimYaw = 90f;
        [Range(-30f, 30f)] public float aimPitch;
        [Min(2f)] public float aimRange = 9f;

        [Header("Saddle, pegs and grips (BikeBody)")]
        [Tooltip("Hips ahead of the REAR AXLE - where a saddle actually is. The one number " +
                 "that decides whether a man sits on the seat or on the tank.")]
        public float saddleAheadOfRearAxle = DefSaddleAhead;
        [Tooltip("Hips back from the grips. Only read on a body whose wheels cannot be found.")]
        public float saddleBehindBars = DefSaddleBehindBars;
        [Tooltip("Saddle below the grips.")]
        public float saddleBelowBars = DefSaddleBelowBars;
        [Tooltip("THE FLOOR UNDER THE SADDLE: how far above the WHEEL'S MEASURED RADIUS the " +
                 "seat may never sink. Suspect this one first when a man floats over his seat - " +
                 "it is the only term driven by something measured off the model rather than " +
                 "authored, so a fat wheel mesh lifts the whole rider. Compare 'wheel r' in the " +
                 "readout against the machine you are looking at; it may go negative.")]
        public float saddleAboveWheel = DefSaddleAboveWheel;
        [Tooltip("Hips above the saddle top - a man sinks into a seat.")]
        public float hipsAboveSaddle = DefHipsAbove;
        [Tooltip("The shooter's hips behind the driver's - a FLOOR in metres. Below this the " +
                 "two of them sit inside one another.")]
        public float pillionBehind = DefPillionBehind;
        [Tooltip("And higher than the driver's.")]
        public float pillionAbove = DefPillionAbove;
        [Tooltip("The shooter's share of the wheelbase - what a long machine offers. The " +
                 "bigger of this and the floor above wins.")]
        public float pillionBehindOfWheelbase = DefPillionShare;
        [Tooltip("Peg height as a share of the saddle's - a proportion, never a clearance off " +
                 "the tyre: the packs draw big wheels and a peg set off one folds a rider up " +
                 "like a jockey.")]
        public float pegHeightOfSaddle = DefPegHeight;
        [Tooltip("Peg width as a share of the flank. A boot out at handlebar width is a boot " +
                 "in mid-air.")]
        public float pegWidthOfFlank = DefPegWidth;
        [Tooltip("Pegs ahead of the hips.")]
        public float pegAhead = DefPegAhead;
        [Tooltip("The shooter's pegs, behind his own hips and higher - his knees fold up round " +
                 "the man in front.")]
        public float pillionPegBack = DefPillionPegBack;
        public float pillionPegLift = DefPillionPegLift;

        [Header("Move them by hand")]
        [Tooltip("Shove the DRIVER off his measured seat, in the machine's own frame: " +
                 "x out to his right, y up, z forward. His pegs follow his hips. Zero is " +
                 "where the proportions put him - this is for the man who is plainly a hand " +
                 "too high and cannot be argued with; drag him down and read the number off.")]
        public Vector3 driverNudge;
        [Tooltip("The same for the SHOOTER. He is the one who usually needs it: he is seated " +
                 "off the driver's saddle plus a lift, so an error in the machine's seat " +
                 "height reaches him twice.")]
        public Vector3 shooterNudge;
        [Tooltip("Force the driver's size instead of the automatic fit. 0: let BikePose fit " +
                 "him to the saddle-to-peg span (it takes a man down to 0.86 at most - the " +
                 "packs draw their men a size big for their vehicles).")]
        [Range(0f, 1.2f)] public float driverScale;
        [Tooltip("The same for the shooter. His span is the short one - saddle to peg with " +
                 "his knees folded up - so the automatic fit shrinks him hardest, and this " +
                 "is how to see whether that is what looks wrong.")]
        [Range(0f, 1.2f)] public float shooterScale;

        [Header("The man's reach (BikePose)")]
        [Tooltip("Degrees over the tank at full speed, and the speed that reaches it.")]
        public float crouchMax = DefCrouchMax;
        public float crouchAt = DefCrouchAt;
        [Tooltip("The elbow's pole: out past the flank, down, and a little back.")]
        public float elbowOut = DefElbowOut;
        public float elbowDown = DefElbowDown;
        public float elbowBack = DefElbowBack;
        [Tooltip("The knee's pole: forward and out, never backwards. The wider figure is the " +
                 "boot that has gone down to the road.")]
        public float kneeAhead = DefKneeAhead;
        public float kneeOut = DefKneeOut;
        public float kneeOutFootDown = DefKneeOutDown;
        public float kneeDown = DefKneeDown;
        [Tooltip("How the toes lie on a peg: ahead, and tipped down.")]
        public float toeAhead = DefToeAhead;
        public float toeDown = DefToeDown;
        [Tooltip("The shooter's hands on the driver's waist: how wide apart, how far lifted, " +
                 "how far forward.")]
        public float holdWide = DefHoldWide;
        public float holdLift = DefHoldLift;
        public float holdForward = DefHoldForward;

        [Header("What the pose is allowed to write")]
        [Tooltip("Off, and the clip underneath is left alone there - the way to see what the " +
                 "pose is actually doing to a limb.")]
        public bool hands = true, feet = true, torso = true;

        [Header("Show")]
        [Tooltip("Mark the points the pose reaches for: amber the driver's, red the shooter's, " +
                 "cyan the bars, green the mark he is aiming at.")]
        public bool markers = true;
        [Tooltip("DRIVER and SHOOTER over their heads.")]
        public bool labels = true;
        [Tooltip("Print the whole tuned block to the console, ready to paste back into " +
                 "BikeBody and BikePose. L does the same in Play.")]
        public bool logNow;
        [Tooltip("Put every number back to what the code is authored with. R does the same in Play.")]
        public bool resetDefaults;
        [Tooltip("Build the stand in the EDITOR as well, so the scene is not an empty box " +
                 "until Play is pressed. The men are posed in edit mode too (the clip " +
                 "underneath does not run there, so they breathe only in Play). Nothing " +
                 "built this way is ever saved into the scene.")]
        public bool preview = true;

        // ------------------------------------------------------------------ the stand

        BikeBody _body;
        BikeOccupant _driver, _shooter;
        Transform _machine, _gun;
        DemoCamera _cam;
        string _standing = "";       // the machine currently built
        LineRenderer _aimLine;
        readonly Transform[] _marks = new Transform[10];
        readonly Vector3[] _markAt = new Vector3[10];   // where each mark goes, refilled every frame

        BikeShow _show;

        // the labels: GUI.skin only exists inside OnGUI, so the styles are made on the
        // first pass and kept, and the readout is only rebuilt when a number in it moved
        GUIStyle _readStyle, _tagStyle;
        string _readout;
        ReadoutKey _readKey;

        struct ReadoutKey
        {
            public string Standing;
            public float Wheelbase, WheelRadius, GripY, DriverSize, ShooterSize;
            public bool SeatsTwo;
            public Vector3 SaddleRider, SaddlePillion, PegRight, PillionPegRight;

            public bool Same(in ReadoutKey o) =>
                Standing == o.Standing && Wheelbase == o.Wheelbase && WheelRadius == o.WheelRadius &&
                GripY == o.GripY && DriverSize == o.DriverSize && ShooterSize == o.ShooterSize &&
                SeatsTwo == o.SeatsTwo && SaddleRider == o.SaddleRider && SaddlePillion == o.SaddlePillion &&
                PegRight == o.PegRight && PillionPegRight == o.PillionPegRight;
        }

        // What BikeBody and BikePose read before this bench wrote its knobs over them,
        // put back when the bench goes: the statics are the game's, and a scene that
        // tuned them is not the scene that runs next.
        Tuning _before;
        bool _captured;

        struct Tuning
        {
            float _saddleAhead, _saddleBehindBars, _saddleBelowBars, _saddleAboveWheel, _hipsAbove;
            float _pillionBehind, _pillionAbove, _pillionShare, _pegHeight, _pegWidth, _pegAhead;
            float _pillionPegBack, _pillionPegLift;
            Vector3 _riderNudge, _pillionNudge;
            float _crouchMax, _crouchAt, _elbowOut, _elbowDown, _elbowBack;
            float _kneeAhead, _kneeOut, _kneeOutFootDown, _kneeDown, _toeAhead, _toeDown;
            float _holdWide, _holdLift, _holdForward;

            public static Tuning Capture() => new Tuning
            {
                _saddleAhead = BikeBody.SaddleAheadOfRearAxle,
                _saddleBehindBars = BikeBody.SaddleBehindBars,
                _saddleBelowBars = BikeBody.SaddleBelowBars,
                _saddleAboveWheel = BikeBody.SaddleAboveWheel,
                _hipsAbove = BikeBody.HipsAboveSaddle,
                _pillionBehind = BikeBody.PillionBehind,
                _pillionAbove = BikeBody.PillionAbove,
                _pillionShare = BikeBody.PillionBehindOfWheelbase,
                _pegHeight = BikeBody.PegHeightOfSaddle,
                _pegWidth = BikeBody.PegWidthOfFlank,
                _pegAhead = BikeBody.PegAhead,
                _pillionPegBack = BikeBody.PillionPegBack,
                _pillionPegLift = BikeBody.PillionPegLift,
                _riderNudge = BikeBody.RiderNudge,
                _pillionNudge = BikeBody.PillionNudge,
                _crouchMax = BikePose.CrouchMax,
                _crouchAt = BikePose.CrouchAt,
                _elbowOut = BikePose.ElbowOut,
                _elbowDown = BikePose.ElbowDown,
                _elbowBack = BikePose.ElbowBack,
                _kneeAhead = BikePose.KneeAhead,
                _kneeOut = BikePose.KneeOut,
                _kneeOutFootDown = BikePose.KneeOutFootDown,
                _kneeDown = BikePose.KneeDown,
                _toeAhead = BikePose.ToeAhead,
                _toeDown = BikePose.ToeDown,
                _holdWide = BikePose.HoldWide,
                _holdLift = BikePose.HoldLift,
                _holdForward = BikePose.HoldForward,
            };

            public void Restore()
            {
                BikeBody.SaddleAheadOfRearAxle = _saddleAhead;
                BikeBody.SaddleBehindBars = _saddleBehindBars;
                BikeBody.SaddleBelowBars = _saddleBelowBars;
                BikeBody.SaddleAboveWheel = _saddleAboveWheel;
                BikeBody.HipsAboveSaddle = _hipsAbove;
                BikeBody.PillionBehind = _pillionBehind;
                BikeBody.PillionAbove = _pillionAbove;
                BikeBody.PillionBehindOfWheelbase = _pillionShare;
                BikeBody.PegHeightOfSaddle = _pegHeight;
                BikeBody.PegWidthOfFlank = _pegWidth;
                BikeBody.PegAhead = _pegAhead;
                BikeBody.PillionPegBack = _pillionPegBack;
                BikeBody.PillionPegLift = _pillionPegLift;
                BikeBody.RiderNudge = _riderNudge;
                BikeBody.PillionNudge = _pillionNudge;
                BikePose.CrouchMax = _crouchMax;
                BikePose.CrouchAt = _crouchAt;
                BikePose.ElbowOut = _elbowOut;
                BikePose.ElbowDown = _elbowDown;
                BikePose.ElbowBack = _elbowBack;
                BikePose.KneeAhead = _kneeAhead;
                BikePose.KneeOut = _kneeOut;
                BikePose.KneeOutFootDown = _kneeOutFootDown;
                BikePose.KneeDown = _kneeDown;
                BikePose.ToeAhead = _toeAhead;
                BikePose.ToeDown = _toeDown;
                BikePose.HoldWide = _holdWide;
                BikePose.HoldLift = _holdLift;
                BikePose.HoldForward = _holdForward;
            }
        }

        void OnEnable()
        {
            if (_captured) return;
            _before = Tuning.Capture();
            _captured = true;
        }

        void Awake()
        {
#if UNITY_EDITOR
            if (Application.isPlaying && spillShow)
            {
                // The user never re-runs a menu step and never saves this scene, so the
                // show has to install itself at Play off the one component the scene
                // does hold. Same rule as every other layer in the project.
                _show = GetComponent<BikeShow>();
                if (_show == null) _show = gameObject.AddComponent<BikeShow>();
                _show.machine = machine;
                return;
            }
            Stand();
#else
            Debug.LogError("[BikeDemo] This bench loads Synty prefabs through the AssetDatabase " +
                           "and only runs in the editor.");
#endif
        }

        /// <summary>The whole stand, ground and light and camera and machine. Called at
        /// Play, and in the editor whenever the scripts reload - the bench is meant to be
        /// LOOKED AT, and a scene that is an empty box until Play is pressed cannot be.</summary>
        void Stand()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !preview) return;
            // A stand left over from before the scripts reloaded: the reference to it is
            // gone (it is not serialised) but the objects are not, and without this the
            // editor quietly fills up with motorcycles, one per recompile.
            // Edit mode only, and deliberately: off Play the kill is immediate, so the
            // loop terminates. In Play, Destroy is DEFERRED - Find would keep handing
            // back the same doomed object and this would spin for ever. Play starts from
            // a freshly loaded scene anyway, so there is nothing left over to sweep.
            if (!Application.isPlaying)
                for (var old = GameObject.Find(StageName); old != null; old = GameObject.Find(StageName))
                    DestroyImmediate(old);
            _stage = MakeRoot(StageName);
            BuildGround();
            BuildLight();
            if (Application.isPlaying) BuildCamera();   // in the editor the Scene view is the camera
            Build();
            if (!Application.isPlaying)
            {
                Frame();
                Debug.Log("[BikeDemo] The bench is standing in the editor - no Play needed. " +
                          "Click BikeDemoBootstrap in the Hierarchy and every number is on the " +
                          "Inspector; they move the men as you drag them. Tools > Bike bench > " +
                          "Look at it brings the view back if you get lost.", this);
            }
#endif
        }

        void Update()
        {
#if UNITY_EDITOR
            if (_show != null) return;   // the show owns Play
            if (Application.isPlaying) Keys();
            if (resetDefaults) { resetDefaults = false; Reset(); }
            // the preview switched on or off, or the scripts reloaded out from under it
            if (!Application.isPlaying && preview == (_stage == null)) { Clear(); Stand(); }
            if (_stage == null) return;
            if (machine != _standing) { Build(); _repose = true; }
            if (Knobs()) _repose = true;
            Push();
            // OFF PLAY THE MEN ARE LEFT ALONE UNLESS SOMETHING CHANGED. The pose used to
            // be written every editor tick, which is correct and completely unusable: a
            // man grabbed by the move handle is put back before the mouse has finished
            // moving, so the drag reads as "the axes do not work". Now the pose is
            // re-applied only when a number moved or the stand was rebuilt - so a drag
            // sticks, and the moment a knob is touched he snaps back to what the code
            // says, which is the comparison worth having anyway.
            if (Application.isPlaying || _repose)
            {
                _repose = false;
                Ride();
            }
            Marks();
            if (logNow) { logNow = false; Debug.Log(Block()); }
#endif
        }

        /// <summary>Whatever the bench is tuned to, printed when Play stops. INSPECTOR
        /// EDITS MADE IN PLAY DO NOT SURVIVE IT - that is Unity, not this scene - so an
        /// evening's tuning would otherwise be gone the moment the button is pressed
        /// again. Nothing is printed if nothing was moved.</summary>
        void OnApplicationQuit()
        {
            if (Tuned()) Debug.Log("[BikeDemo] Play stopped - what you tuned, before it is lost:\n" + Block());
        }

        void OnDisable()
        {
            // the game's numbers back, whichever way the bench goes
            if (_captured) { _before.Restore(); _captured = false; }
            // The scene being torn down - Play stopping, or the editor unloading it -
            // takes everything in it with it, and destroying by hand at that moment is
            // console noise at best. Just forget what was standing here.
            if (!gameObject.scene.isLoaded) { Forget(); return; }
            Clear();
        }

        /// <summary>The stand, taken down. Edit mode will not have Destroy, and leaving
        /// the men standing about after a script reload is how an editor scene fills up
        /// with copies of a motorcycle.</summary>
        void Clear()
        {
            if (_stage) Kill(_stage.gameObject);
            Forget();
        }

        void Forget()
        {
            _stage = null;
            _machine = null;
            _body = null;
            _driver = _shooter = null;
            _gun = null;
            _aimLine = null;
            _standing = "";
            for (int i = 0; i < _marks.Length; i++) _marks[i] = null;
        }

        static void KillComp(Component c)
        {
            if (!c) return;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        static void Kill(GameObject go)
        {
            if (!go) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        /// <summary>A root nothing will ever save. The bench builds real GameObjects in
        /// the editor so it can be looked at without Play; none of them may end up in
        /// the scene file, or the next person opens a scene with a motorcycle baked into
        /// it and no idea where it came from.</summary>
        Transform MakeRoot(string name)
        {
            var go = new GameObject(name);
            Unsaved(go);
            return go.transform;
        }

        /// <summary>Flag one of the bench's own objects so no save can ever pick it up.
        /// The stage's flag covers its whole hierarchy, but each thing built under it is
        /// stamped too - a preview that leaks a motorcycle into a scene file is a worse
        /// bug than an empty scene ever was.</summary>
        static void Unsaved(GameObject go)
        {
            DemoScratch.Unsaved(go);
        }

        Transform _stage;
        bool _repose = true;
        const string StageName = "Bike Bench";

        // every knob as it was last seen, so the editor can tell a frame where something
        // moved from the thousands where nothing did
        readonly float[] _seen = new float[32];

        bool Knobs()
        {
            int i = 0;
            bool changed = false;
            void S(float v)
            {
                if (i >= _seen.Length) return;
                if (!Mathf.Approximately(_seen[i], v)) { _seen[i] = v; changed = true; }
                i++;
            }
            S(saddleAheadOfRearAxle); S(saddleBehindBars); S(saddleBelowBars); S(saddleAboveWheel);
            S(hipsAboveSaddle); S(pillionBehind); S(pillionAbove); S(pillionBehindOfWheelbase);
            S(pegHeightOfSaddle); S(pegWidthOfFlank); S(pegAhead); S(pillionPegBack); S(pillionPegLift);
            S(driverNudge.x); S(driverNudge.y); S(driverNudge.z);
            S(shooterNudge.x); S(shooterNudge.y); S(shooterNudge.z);
            S(driverScale); S(shooterScale);
            S(crouchMax); S(crouchAt); S(elbowOut); S(elbowDown); S(elbowBack);
            S(kneeAhead); S(kneeOut); S(kneeOutFootDown); S(kneeDown); S(toeAhead); S(toeDown);
            // steer, speed, lean, the aim and the hold are read straight by Ride() when it
            // runs; they are left out here on purpose - the array is the tuning, not the
            // whole Inspector, and a bench that reposes on every slider is the old bench
            return changed;
        }

        /// <summary>Put the stand up again from scratch - what the menu calls when the
        /// scene has been open a while and something has got out of step.</summary>
        public void RebuildPreview()
        {
            Clear();
            Stand();
        }

        /// <summary>The two men and the machine, as a box - what a view is framed on.</summary>
        public Bounds StandBounds()
        {
            var box = new Bounds(Vector3.up * 0.8f, Vector3.one * 2f);
            if (_machine == null) return box;
            bool any = false;
            foreach (var r in _machine.GetComponentsInChildren<Renderer>())
            {
                if (any) box.Encapsulate(r.bounds);
                else { box = r.bounds; any = true; }
            }
            return box;
        }

        /// <summary>Point the Scene view at the bench. The whole complaint this answers is
        /// "I cannot find my way round Unity": a scene that builds itself is no use if it
        /// builds itself off the edge of the view.</summary>
        public void Frame()
        {
#if UNITY_EDITOR
            var view = UnityEditor.SceneView.lastActiveSceneView;
            if (view == null) return;
            var box = StandBounds();
            box.Expand(0.6f);
            view.Frame(box, false);
#endif
        }

        // ------------------------------------------------------------------ the build

        void Build()
        {
#if UNITY_EDITOR
            if (_machine) Kill(_machine.gameObject);   // the men are parented to it
            _driver = _shooter = null;
            _gun = null;
            _standing = machine;

            var prefab = Machine(machine);
            if (prefab == null)
            {
                Debug.LogWarning("[BikeDemo] no two-wheeler called '" + machine + "' - the " +
                                 "packs' machines are SM_Veh_Motorbike_01, SM_Veh_Moped_01 " +
                                 "and SM_Veh_Motorbike_02 (police).");
                return;
            }

            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, _stage);
            go.name = prefab.name;
            foreach (var col in go.GetComponentsInChildren<Collider>()) KillComp(col);
            _machine = go.transform;
            Unsaved(go);
            _body = new BikeBody(_machine);

            // the seated clip the whole city rides on: the library's Driving_Loop, or a
            // bench sit when it is missing. Everything the pose does not write over -
            // the breathing, the head - comes from it, which is why a SEATED clip is
            // the right base and an idle stand is not
            var clips = CrewKit.Clips();
            var sit = CrewKit.Ride != null ? CrewKit.Ride : clips.SitLoop;
            if (sit == null)
            {
                Debug.LogWarning("[BikeDemo] no seated clip under Assets/Animations - nobody " +
                                 "can be sat on the machine.");
                return;
            }

            var (driverPrefab, shooterPrefab) = Bodies();
            if (driverPrefab == null)
            {
                Debug.LogWarning("[BikeDemo] no gang body to put on the machine.");
                return;
            }

            _driver = BikeOccupant.Seat(_body, driverPrefab, sit, pillion: false);
            _shooter = BikeOccupant.Seat(_body, shooterPrefab != null ? shooterPrefab : driverPrefab,
                sit, pillion: true);
            // the pillion holds ON TO the rider, so he has to be told who that is -
            // and he poses the rider first, because two LateUpdates run in whatever
            // order Unity likes and hips that have not been seated yet are thin air
            if (_shooter != null && _driver != null) _shooter.Pose.Rider = _driver.Pose;
            if (!_body.SeatsTwo)
                Debug.LogWarning("[BikeDemo] " + go.name + " measures too short to seat two " +
                                 "(wheelbase " + _body.Wheelbase.ToString("0.00") + " m) - the " +
                                 "shooter is sat on it anyway, which is the thing to look at.");

            ArmTheShooter();
            BuildMarks();
            if (_cam) _cam.pivot = _body.Saddle(false);
#endif
        }

        void ArmTheShooter()
        {
#if UNITY_EDITOR
            if (_shooter == null || !shooterArmed) return;
            var weapon = CrewKit.Weapon(gun);
            if (weapon == null)
            {
                Debug.LogWarning("[BikeDemo] gun '" + gun + "' not found - the shooter rides empty-handed.");
                return;
            }
            var animator = _shooter.GetComponentInChildren<Animator>();
            if (animator == null) return;
            // the street's own seating: the gun hangs in the fist along the fingers'
            // axis, which is exactly the axis BikePose points at the mark - so aiming
            // the hand aims the barrel, and no gun offset is authored twice
            _gun = CrewArms.Attach(animator, weapon);
#endif
        }

        /// <summary>The machine by name, out of the catalogue rather than a folder scan:
        /// every scan in the project denies "bike" and "moped", so a two-wheeler is only
        /// ever had by asking for it (VehicleCatalog.Motorcycles, and the law's tourer).</summary>
        internal static GameObject Machine(string name)
        {
#if UNITY_EDITOR
            foreach (var body in StreetBikes.Bodies())
                if (body != null && body.name == name) return body;
            var police = StreetBikes.PoliceBody();
            if (police != null && police.name == name) return police;
            // an unknown name still gets a machine, so the bench is never empty
            var any = StreetBikes.Bodies();
            return any.Count > 0 ? any[0] : police;
#else
            return null;
#endif
        }

        /// <summary>The two faces. Named bodies when they are asked for, otherwise a pair
        /// off the gang table - and never the same face twice, or the bench is one man
        /// riding behind himself and telling driver from shooter gets harder, not
        /// easier.</summary>
        (GameObject driver, GameObject shooter) Bodies()
        {
            GameObject Named(string n) => string.IsNullOrEmpty(n) ? null : Cast(n);
            var driver = Named(driverBody);
            var shooter = Named(shooterBody);
            var looks = LivingCity.Gangs.GangLooks.Hoods;
            for (int i = 0; i < looks.Length && (driver == null || shooter == null); i++)
            {
                var body = Cast(looks[i]);
                if (body == null) continue;
                if (driver == null) driver = body;
                else if (shooter == null && body != driver) shooter = body;
            }
            return (driver, shooter);
        }

        internal static GameObject Cast(string name) =>
            LivingCity.UI.LedgerModelSet.PersonNamed(name) ??
            LivingCity.UI.PortraitStudio.FindPeoplePrefab(name);

        // ------------------------------------------------------------------ the knobs

        /// <summary>Every knob onto the statics the pose actually reads, and the machine's
        /// points re-derived when one of ITS proportions moved. The pose reads its own
        /// numbers fresh every frame, so those need no rebuild; BikeBody's are baked into
        /// measured points, so they do.</summary>
        void Push()
        {
            bool moved =
                Set(ref BikeBody.SaddleAheadOfRearAxle, saddleAheadOfRearAxle) |
                Set(ref BikeBody.SaddleBehindBars, saddleBehindBars) |
                Set(ref BikeBody.SaddleBelowBars, saddleBelowBars) |
                Set(ref BikeBody.SaddleAboveWheel, saddleAboveWheel) |
                Set(ref BikeBody.HipsAboveSaddle, hipsAboveSaddle) |
                Set(ref BikeBody.PillionBehind, pillionBehind) |
                Set(ref BikeBody.PillionAbove, pillionAbove) |
                Set(ref BikeBody.PillionBehindOfWheelbase, pillionBehindOfWheelbase) |
                Set(ref BikeBody.PegHeightOfSaddle, pegHeightOfSaddle) |
                Set(ref BikeBody.PegWidthOfFlank, pegWidthOfFlank) |
                Set(ref BikeBody.PegAhead, pegAhead) |
                Set(ref BikeBody.PillionPegBack, pillionPegBack) |
                Set(ref BikeBody.PillionPegLift, pillionPegLift) |
                Set(ref BikeBody.RiderNudge, driverNudge) |
                Set(ref BikeBody.PillionNudge, shooterNudge);
            if (moved && _body != null) _body.Place();

            BikePose.CrouchMax = crouchMax;
            BikePose.CrouchAt = Mathf.Max(0.1f, crouchAt);
            BikePose.ElbowOut = elbowOut;
            BikePose.ElbowDown = elbowDown;
            BikePose.ElbowBack = elbowBack;
            BikePose.KneeAhead = kneeAhead;
            BikePose.KneeOut = kneeOut;
            BikePose.KneeOutFootDown = kneeOutFootDown;
            BikePose.KneeDown = kneeDown;
            BikePose.ToeAhead = toeAhead;
            BikePose.ToeDown = toeDown;
            BikePose.HoldWide = holdWide;
            BikePose.HoldLift = holdLift;
            BikePose.HoldForward = holdForward;
        }

        /// <summary>Write it and say whether it changed - a bitwise | on the calls above
        /// so every one of them runs, which && would not.</summary>
        static bool Set(ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return false;
            field = value;
            return true;
        }

        static bool Set(ref Vector3 field, Vector3 value)
        {
            if ((field - value).sqrMagnitude < 1e-8f) return false;
            field = value;
            return true;
        }

        void Ride()
        {
            if (_body == null || _machine == null) return;
            // the machine leans about its own origin, which the packs put on the contact
            // line - so a lean never lifts a tyre
            _machine.localRotation = Quaternion.Euler(0f, 0f, -lean);
            // the editor's delta is nothing when nothing is happening, and the bars
            // turn at 160 degrees a second: without a frame's worth of time the steering
            // knob would never reach the grips in preview
            _body.Tick(Application.isPlaying ? Time.deltaTime : 0.05f, speed, steer);

            var mark = AimPoint();
            Wear(_driver, pillion: false, mark: null, size: driverScale);
            Wear(_shooter, pillion: true, mark: aiming ? mark : (Vector3?)null, size: shooterScale);

            // Off Play the editor ticks the player loop only when it feels like it, so a
            // man could sit in his bind pose waiting for a LateUpdate that never comes.
            // Apply() refuses to run twice in a frame, so asking here costs nothing when
            // his own LateUpdate did tick - and the pillion poses the rider first, which
            // is why the shooter is asked and not both.
            // Off Play the editor ticks the player loop when it feels like it, so the
            // pose is asked for by hand rather than waited for. This runs only on the
            // frames Ride() runs at all, which off Play means only when something moved.
            if (!Application.isPlaying)
            {
                if (_shooter != null && _shooter.Pose != null) _shooter.Pose.Apply();
                else if (_driver != null && _driver.Pose != null) _driver.Pose.Apply();
            }

            if (_aimLine)
            {
                bool show = markers && aiming && _shooter != null;
                _aimLine.enabled = show;
                if (show)
                {
                    var from = _gun ? CrewArms.MuzzleOf(_gun) : null;
                    _aimLine.SetPosition(0, from ? from.position : _shooter.Pose.ChestPoint);
                    _aimLine.SetPosition(1, mark);
                }
            }
        }

        void Wear(BikeOccupant man, bool pillion, Vector3? mark, float size)
        {
            if (man == null || man.Pose == null) return;
            // BikePose fits a man to the saddle-to-peg span once, when he is seated, and
            // nothing writes his size again - so an override is simply written over the
            // top of it. Zero leaves the fit alone.
            if (size > 0.01f) man.transform.localScale = Vector3.one * size;
            var pose = man.Pose;
            pose.Speed = speed;
            pose.FootDown = footDown && !pillion;
            pose.AimAt = mark;
            pose.Hands = hands;
            pose.Feet = feet;
            pose.Torso = torso;
        }

        /// <summary>The mark, round the machine: yaw 0 is over the driver's shoulder, 90
        /// straight out to his right, 180 behind. Kept in the machine's frame so it swings
        /// with the bike rather than sitting in the world while the bike turns.</summary>
        Vector3 AimPoint()
        {
            if (_machine == null) return Vector3.up;
            var dir = Quaternion.AngleAxis(aimYaw, Vector3.up) * Vector3.forward;
            dir = Quaternion.AngleAxis(-aimPitch, Vector3.right) * dir;
            return _machine.position + Quaternion.Euler(0f, _machine.eulerAngles.y, 0f) * dir * aimRange
                   + Vector3.up * 1.15f;
        }

        // ------------------------------------------------------------------ the marks

        static readonly Color DriverInk = new Color(1f, 0.72f, 0.20f);
        static readonly Color ShooterInk = new Color(1f, 0.35f, 0.30f);
        static readonly Color BarInk = new Color(0.35f, 0.85f, 0.95f);
        static readonly Color MarkInk = new Color(0.40f, 0.95f, 0.45f);

        void BuildMarks()
        {
            var inks = new[]
            {
                DriverInk, DriverInk, DriverInk, DriverInk,   // saddle, two pegs, the boot down
                ShooterInk, ShooterInk, ShooterInk,           // saddle, two pegs
                BarInk, BarInk,                               // the grips
                MarkInk,                                      // what the shooter is on
            };
            for (int i = 0; i < _marks.Length; i++)
            {
                if (_marks[i]) continue;
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Mark " + i;
                KillComp(ball.GetComponent<Collider>());
                ball.transform.SetParent(_stage, false);
                Unsaved(ball);
                ball.transform.localScale = Vector3.one * (i == 9 ? 0.11f : 0.055f);
                var r = ball.GetComponent<MeshRenderer>();
                r.sharedMaterial = Ink(inks[i]);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _marks[i] = ball.transform;
            }
            if (_aimLine) return;
            var line = new GameObject("Aim", typeof(LineRenderer));
            line.transform.SetParent(_stage, false);
            Unsaved(line);
            _aimLine = line.GetComponent<LineRenderer>();
            _aimLine.useWorldSpace = true;
            _aimLine.widthMultiplier = 0.02f;
            _aimLine.positionCount = 2;
            _aimLine.sharedMaterial = Ink(MarkInk);
            _aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Marks()
        {
            if (_body == null || _marks[0] == null) return;
            var at = _markAt;
            at[0] = _body.Saddle(false);
            at[1] = _body.Peg(false, false);
            at[2] = _body.Peg(true, false);
            at[3] = _body.Ground(false);
            at[4] = _body.Saddle(true);
            at[5] = _body.Peg(false, true);
            at[6] = _body.Peg(true, true);
            at[7] = _body.GripNow(false);
            at[8] = _body.GripNow(true);
            at[9] = AimPoint();
            for (int i = 0; i < _marks.Length; i++)
            {
                bool show = markers &&
                            !(i == 3 && !footDown) &&        // the road boot, only when it is down
                            !(i == 9 && !aiming) &&
                            !(i >= 4 && i <= 6 && _shooter == null);
                if (_marks[i].gameObject.activeSelf != show) _marks[i].gameObject.SetActive(show);
                if (show) _marks[i].position = at[i];
            }
        }

        // the ternary, never ?? - a missing shader and a destroyed one are told apart by
        // Unity's own ==, which the coalescing operator does not ask
        static Material Ink(Color colour)
        {
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            var shader = urp != null ? urp : Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "Bike Mark " + colour };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            return mat;
        }

        // ------------------------------------------------------------------ the set

        void BuildGround()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(_stage, false);
            Unsaved(floor);
            floor.transform.localScale = Vector3.one * 4f;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Bike Demo Ground" };
            mat.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
            mat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(_stage, false);
            Unsaved(sun.gameObject);
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 148f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.50f);
        }

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(_stage, false);
            Unsaved(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.52f, 0.62f, 0.72f);
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            _cam = camGo.AddComponent<DemoCamera>();
            _cam.pivot = new Vector3(0f, 0.85f, 0f);
            _cam.distance = 4.2f;
            _cam.yaw = 128f;      // three-quarters on from the right, where the gun arm is
            _cam.pitch = 14f;
            _cam.showHint = true;
            _cam.hintTopPx = 8f;
            _cam.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "L: print the tuned numbers   R: back to the authored ones   " +
                        "(the men cannot be dragged - the pose re-seats them every frame; " +
                        "move them with driverNudge / shooterNudge)";
        }

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.lKey.wasPressedThisFrame) logNow = true;
            if (kb.rKey.wasPressedThisFrame) resetDefaults = true;
        }

        // ------------------------------------------------------------------ the labels

        void OnGUI()
        {
            if (_show != null) return;   // its own labels and its own readout
            var cam = Camera.main;
            if (cam == null) return;

            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                };
                _readStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                _readStyle.normal.textColor = new Color(0.85f, 0.9f, 0.96f);
            }

            if (labels)
            {
                Tag(cam, _driver, "DRIVER", DriverInk);
                Tag(cam, _shooter, "SHOOTER", ShooterInk);
            }

            GUI.Label(new Rect(12f, Screen.height - 116f, 900f, 110f), Readout(), _readStyle);
        }

        void Tag(Camera cam, BikeOccupant man, string text, Color ink)
        {
            if (man == null || man.Pose == null) return;
            var head = man.Pose.ChestPoint + Vector3.up * 0.45f;
            var p = cam.WorldToScreenPoint(head);
            if (p.z <= 0f) return;   // behind the camera: a label there is a label upside down
            _tagStyle.normal.textColor = ink;
            GUI.Label(new Rect(p.x - 70f, Screen.height - p.y - 12f, 140f, 24f), text, _tagStyle);
        }

        string Readout()
        {
            if (_body == null) return "no machine";
            var key = new ReadoutKey
            {
                Standing = _standing,
                Wheelbase = _body.Wheelbase, WheelRadius = _body.WheelRadius, GripY = _body.GripRight.y,
                SeatsTwo = _body.SeatsTwo,
                DriverSize = _driver == null ? -1f : _driver.transform.localScale.x,
                ShooterSize = _shooter == null ? -1f : _shooter.transform.localScale.x,
                SaddleRider = _body.SaddleRider, SaddlePillion = _body.SaddlePillion,
                PegRight = _body.PegRight, PillionPegRight = _body.PillionPegRight,
            };
            if (_readout != null && key.Same(_readKey)) return _readout;
            _readKey = key;
            _readout = BuildReadout();
            return _readout;
        }

        string BuildReadout()
        {
            string Size(BikeOccupant m) => m == null ? "-" : m.transform.localScale.x.ToString("0.00");
            return _standing + "   wheelbase " + _body.Wheelbase.ToString("0.00") +
                   " m   wheel r " + _body.WheelRadius.ToString("0.00") +
                   "   grip y " + _body.GripRight.y.ToString("0.00") +
                   "   seats two: " + _body.SeatsTwo +
                   "   size driver " + Size(_driver) + " / shooter " + Size(_shooter) + "\n" +
                   "driver  saddle z " + _body.SaddleRider.z.ToString("0.00") +
                   "  y " + _body.SaddleRider.y.ToString("0.00") +
                   "   peg (" + _body.PegRight.x.ToString("0.00") + ", " +
                   _body.PegRight.y.ToString("0.00") + ", " + _body.PegRight.z.ToString("0.00") + ")\n" +
                   "shooter saddle z " + _body.SaddlePillion.z.ToString("0.00") +
                   "  y " + _body.SaddlePillion.y.ToString("0.00") +
                   "   peg (" + _body.PillionPegRight.x.ToString("0.00") + ", " +
                   _body.PillionPegRight.y.ToString("0.00") + ", " +
                   _body.PillionPegRight.z.ToString("0.00") + ")\n" +
                   "gap between them " + (_body.SaddleRider.z - _body.SaddlePillion.z).ToString("0.00") +
                   " m   (a seated man is about 0.45 m deep)";
        }

        /// <summary>The tuned numbers as source, ready to paste back over BikeBody's and
        /// BikePose's own. The bench is only worth anything if what it finds can leave it.</summary>
        string Block()
        {
            var sb = new StringBuilder("[BikeDemo] tuned on ").Append(_standing).Append(":\n");
            sb.Append("BikeBody:\n");
            sb.Append("  SaddleAheadOfRearAxle = ").Append(F(saddleAheadOfRearAxle))
              .Append("; SaddleBehindBars = ").Append(F(saddleBehindBars))
              .Append("; SaddleBelowBars = ").Append(F(saddleBelowBars))
              .Append("; SaddleAboveWheel = ").Append(F(saddleAboveWheel)).Append('\n');
            sb.Append("  HipsAboveSaddle = ").Append(F(hipsAboveSaddle))
              .Append("; PillionBehind = ").Append(F(pillionBehind))
              .Append("; PillionAbove = ").Append(F(pillionAbove))
              .Append("; PillionBehindOfWheelbase = ").Append(F(pillionBehindOfWheelbase)).Append('\n');
            sb.Append("  PegHeightOfSaddle = ").Append(F(pegHeightOfSaddle))
              .Append("; PegWidthOfFlank = ").Append(F(pegWidthOfFlank))
              .Append("; PegAhead = ").Append(F(pegAhead))
              .Append("; PillionPegBack = ").Append(F(pillionPegBack))
              .Append("; PillionPegLift = ").Append(F(pillionPegLift)).Append('\n');
            if (driverNudge != Vector3.zero || shooterNudge != Vector3.zero)
                sb.Append("  RiderNudge = new Vector3(").Append(F(driverNudge.x)).Append(", ")
                  .Append(F(driverNudge.y)).Append(", ").Append(F(driverNudge.z))
                  .Append("); PillionNudge = new Vector3(").Append(F(shooterNudge.x)).Append(", ")
                  .Append(F(shooterNudge.y)).Append(", ").Append(F(shooterNudge.z)).Append(");\n")
                  .Append("  // a nudge is a LAST RESORT - it fits this machine and no other.\n")
                  .Append("  // Where it can, fold it back into the proportions above instead.\n");
            if (driverScale > 0.01f || shooterScale > 0.01f)
                sb.Append("  // sizes forced by hand: driver ").Append(F(driverScale))
                  .Append(", shooter ").Append(F(shooterScale))
                  .Append(" (BikePose fits them automatically - see Setup)\n");
            sb.Append("BikePose:\n");
            sb.Append("  CrouchMax = ").Append(F(crouchMax)).Append("; CrouchAt = ").Append(F(crouchAt)).Append('\n');
            sb.Append("  ElbowOut = ").Append(F(elbowOut)).Append("; ElbowDown = ").Append(F(elbowDown))
              .Append("; ElbowBack = ").Append(F(elbowBack)).Append('\n');
            sb.Append("  KneeAhead = ").Append(F(kneeAhead)).Append("; KneeOut = ").Append(F(kneeOut))
              .Append("; KneeOutFootDown = ").Append(F(kneeOutFootDown))
              .Append("; KneeDown = ").Append(F(kneeDown)).Append('\n');
            sb.Append("  ToeAhead = ").Append(F(toeAhead)).Append("; ToeDown = ").Append(F(toeDown)).Append('\n');
            sb.Append("  HoldWide = ").Append(F(holdWide)).Append("; HoldLift = ").Append(F(holdLift))
              .Append("; HoldForward = ").Append(F(holdForward));
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.###") + "f";

        /// <summary>Has anything been moved off the authored numbers? What decides
        /// whether stopping Play is worth a console line.</summary>
        bool Tuned() =>
            driverNudge != Vector3.zero || shooterNudge != Vector3.zero ||
            driverScale > 0.01f || shooterScale > 0.01f ||
            !Mathf.Approximately(saddleAheadOfRearAxle, DefSaddleAhead) ||
            !Mathf.Approximately(saddleBehindBars, DefSaddleBehindBars) ||
            !Mathf.Approximately(saddleBelowBars, DefSaddleBelowBars) ||
            !Mathf.Approximately(saddleAboveWheel, DefSaddleAboveWheel) ||
            !Mathf.Approximately(hipsAboveSaddle, DefHipsAbove) ||
            !Mathf.Approximately(pillionBehind, DefPillionBehind) ||
            !Mathf.Approximately(pillionAbove, DefPillionAbove) ||
            !Mathf.Approximately(pillionBehindOfWheelbase, DefPillionShare) ||
            !Mathf.Approximately(pegHeightOfSaddle, DefPegHeight) ||
            !Mathf.Approximately(pegWidthOfFlank, DefPegWidth) ||
            !Mathf.Approximately(pegAhead, DefPegAhead) ||
            !Mathf.Approximately(pillionPegBack, DefPillionPegBack) ||
            !Mathf.Approximately(pillionPegLift, DefPillionPegLift) ||
            !Mathf.Approximately(crouchMax, DefCrouchMax) ||
            !Mathf.Approximately(crouchAt, DefCrouchAt) ||
            !Mathf.Approximately(elbowOut, DefElbowOut) ||
            !Mathf.Approximately(elbowDown, DefElbowDown) ||
            !Mathf.Approximately(elbowBack, DefElbowBack) ||
            !Mathf.Approximately(kneeAhead, DefKneeAhead) ||
            !Mathf.Approximately(kneeOut, DefKneeOut) ||
            !Mathf.Approximately(kneeOutFootDown, DefKneeOutDown) ||
            !Mathf.Approximately(kneeDown, DefKneeDown) ||
            !Mathf.Approximately(toeAhead, DefToeAhead) ||
            !Mathf.Approximately(toeDown, DefToeDown) ||
            !Mathf.Approximately(holdWide, DefHoldWide) ||
            !Mathf.Approximately(holdLift, DefHoldLift) ||
            !Mathf.Approximately(holdForward, DefHoldForward);

        /// <summary>Unity's own: called when the component is added and by Reset in the
        /// Inspector's context menu, and by R in Play. Every number back to what the code
        /// is authored with.</summary>
        void Reset()
        {
            saddleAheadOfRearAxle = DefSaddleAhead;
            saddleBehindBars = DefSaddleBehindBars;
            saddleBelowBars = DefSaddleBelowBars;
            saddleAboveWheel = DefSaddleAboveWheel;
            hipsAboveSaddle = DefHipsAbove;
            pillionBehind = DefPillionBehind;
            pillionAbove = DefPillionAbove;
            pillionBehindOfWheelbase = DefPillionShare;
            pegHeightOfSaddle = DefPegHeight;
            pegWidthOfFlank = DefPegWidth;
            pegAhead = DefPegAhead;
            pillionPegBack = DefPillionPegBack;
            pillionPegLift = DefPillionPegLift;
            crouchMax = DefCrouchMax;
            crouchAt = DefCrouchAt;
            elbowOut = DefElbowOut;
            elbowDown = DefElbowDown;
            elbowBack = DefElbowBack;
            kneeAhead = DefKneeAhead;
            kneeOut = DefKneeOut;
            kneeOutFootDown = DefKneeOutDown;
            kneeDown = DefKneeDown;
            toeAhead = DefToeAhead;
            toeDown = DefToeDown;
            holdWide = DefHoldWide;
            holdLift = DefHoldLift;
            holdForward = DefHoldForward;
            driverNudge = shooterNudge = Vector3.zero;
            driverScale = shooterScale = 0f;
            BikeBody.RiderNudge = BikeBody.PillionNudge = Vector3.zero;
            if (_driver != null) _driver.transform.localScale = Vector3.one;
            if (_shooter != null) _shooter.transform.localScale = Vector3.one;
            if (_body != null) _body.Place();
        }
    }
}
