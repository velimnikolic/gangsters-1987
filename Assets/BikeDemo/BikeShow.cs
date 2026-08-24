using System.Collections.Generic;
using System.Text;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace BikeDemo
{
    /// <summary>
    /// The spill bench: four machines riding side by side down four lanes, each one
    /// playing out a different way of coming off, over and over until Play is stopped.
    ///
    /// The pose bench next door (BikeDemoBuilder) answers "is he SITTING on it right",
    /// and it answers it standing still because a pose is a still thing. Nothing there
    /// can answer "does he come off it right", because coming off is four seconds of
    /// motion and there is no clip of it anywhere in the project - the flight is the
    /// transform's and the clips only dress it (RiderSpill, BikeSpill). The only way to
    /// judge that is to watch it happen, and the only way to judge it fairly is to watch
    /// it happen NEXT TO the other three, over and over, with nothing else going on.
    ///
    /// So: no traffic, no crews, no shooting back, nobody interacting with anybody. Four
    /// lanes, one act each:
    ///
    ///   1  RIDES ON        - the control. Nothing happens to it. Every fault the other
    ///                        three show has to be a fault this one does not, or it is a
    ///                        fault of the riding and not of the spill.
    ///   2  PILLION SHOT    - the man behind is hit, comes off, and stays in the road;
    ///                        the machine rides on with the driver alone.
    ///   3  RIDER SHOT      - the man at the bars is hit. The machine goes down with him
    ///                        and takes the pillion with it; the pillion gets up.
    ///   4  BURNS           - the machine is hit until it catches, falls on its side and
    ///                        slides away burning while both men get up out of the road,
    ///                        and then the tank goes (BikeSpill.Fuse).
    ///
    /// The loop is synchronised on purpose: every lane is put back at the start line
    /// together, once all four have finished and the wreck has been left standing long
    /// enough to look at (<see cref="hold"/>). Lanes that finish early wait. That way
    /// the four are always at the same moment of their own act and can be compared
    /// across, which is the entire reason they are side by side.
    ///
    /// Editor only, like everything else that loads Synty prefabs through the
    /// AssetDatabase.
    /// </summary>
    public sealed class BikeShow : MonoBehaviour
    {
        public enum Act
        {
            /// <summary>Straight down the lane and nothing else.</summary>
            RideOn,
            /// <summary>The pillion is shot: off the back, dead in the road, the machine
            /// rides on.</summary>
            PillionShot,
            /// <summary>The rider is shot: the whole machine goes down, the pillion gets
            /// up. The player's own rule - a machine whose rider is shot at fifty
            /// kilometres an hour falls over and takes its passenger with it.</summary>
            RiderShot,
            /// <summary>Hit until it burns: down on its side, sliding and alight, both
            /// men up out of the road.</summary>
            Burns,
        }

        [Header("The run")]
        [Tooltip("Which two-wheeler every lane rides. SM_Veh_Motorbike_01 (the city bike), " +
                 "SM_Veh_Moped_01, SM_Veh_Motorbike_02 (the police tourer), SM_Veh_Scooter_01.")]
        public string machine = "SM_Veh_Motorbike_01";
        [Tooltip("Metres of straight road each lane rides before the loop is over.")]
        [Min(10f)] public float runMetres = 60f;
        [Tooltip("Metres a second. Thirteen is the drive-by's own pace (CrewBike.PassSpeed).")]
        [Range(2f, 25f)] public float speed = 13f;
        [Tooltip("How far into the run the act happens, as a share of it. Early enough " +
                 "that the fall and the slide are both inside the run.")]
        [Range(0.1f, 0.9f)] public float actAt = 0.4f;
        [Tooltip("Metres between the lanes.")]
        [Min(2f)] public float laneGap = 5f;
        [Tooltip("Seconds the wrecks are left lying before every lane goes back to the " +
                 "start line. The pause is the point: it is when the last pose is looked at.")]
        [Min(0f)] public float hold = 2.5f;

        [Header("The men")]
        [Tooltip("Put a gun in the pillion's fist, the way the street's drive-by does.")]
        public bool armed = true;
        public string gun = "SM_Wep_Machine_Pistol_01";
        [Tooltip("The pillion holds his gun out at his side of the street while the lane " +
                 "is still riding - the pose a drive-by is actually ridden in.")]
        public bool aiming = true;

        [Header("The spill - the man (RiderSpill)")]
        [Tooltip("Metres a second squared. Heavier than the real thing on purpose: a body " +
                 "thrown off a machine has to be down and still inside a second, or the " +
                 "pass has ridden out of shot while he is still in the air.")]
        public float gravity = 17f;
        [Tooltip("How he leaves the saddle: up off it, out to the side he falls, and the " +
                 "share of the machine's own pace he carries with him. A man does not stop " +
                 "when the bike does - that IS the picture.")]
        public float throwUp = 3.0f, throwOut = 1.8f;
        [Range(0f, 1.2f)] public float throwAhead = 0.9f;
        [Tooltip("Metres a second squared off him once he is in the road. Cloth on tarmac, " +
                 "not a tyre: he does not slide far.")]
        public float manDrag = 6.5f;
        [Tooltip("Degrees a second he turns over in the air - head over heels when he is " +
                 "going to get up, and only about his own axis when the death clip is " +
                 "already crumpling him.")]
        public float tumble = 300f, deadTumble = 90f;
        [Tooltip("Seconds he lies in the road before he starts to get up, and the seconds " +
                 "either side of it drawn per man. Drag it to zero and the old fault is " +
                 "back: a man who touches the road and stands is a machine resetting.")]
        public float lieThere = 1.1f;
        [Min(0f)] public float lieJitter = 0.45f;
        [Tooltip("How much of the death clip is skipped when a body arrives, and how fast " +
                 "the rest is played. The crumple is authored from a man on his feet, and " +
                 "a rider out of a tumble is already half way down: its front half is a " +
                 "stagger he has no business doing.")]
        [Range(0f, 0.9f)] public float sprawlFrom = 0.42f;
        [Range(0.2f, 3f)] public float sprawlSpeed = 1.3f;
        [Tooltip("How fast the landing take is played when he gets up, and the blend into " +
                 "it. Slower than a landing's own numbers: the clip was authored as a drop " +
                 "onto the feet and is being asked to carry a man off the road.")]
        [Range(0.2f, 2f)] public float riseSpeed = 0.7f;
        [Range(0f, 1f)] public float riseFade = 0.3f;
        [Tooltip("The share either side of one a death is played at, so two men do not " +
                 "die at one rate. The clip itself is drawn per man out of CrewKit.Deaths.")]
        [Range(0f, 0.5f)] public float deathJitter = 0.15f;
        [Tooltip("The chance a dead man is limp before he lands rather than after - the " +
                 "round kills him in the saddle, against the road finishing him. Both " +
                 "readings are true and a pass wants both.")]
        [Range(0f, 1f)] public float limpChance = 0.5f;
        [Tooltip("Seconds he takes to come upright once he is down. Zero makes it a snap, " +
                 "which is what a landing looks like when a tumbling body has to be stood " +
                 "up for a clip authored from a man on his feet - drag it to zero and back " +
                 "and the fault is plain.")]
        public float uprightIn = 0.18f;

        [Header("The spill - the machine (BikeSpill)")]
        [Tooltip("Degrees a second it goes over. A machine at speed that loses its rider " +
                 "is on its side in a third of a second, not a second and a half.")]
        public float rollRate = 300f;
        [Tooltip("Metres a second squared once it is down. Metal on tarmac carries: it " +
                 "slides on past the man.")]
        public float machineDrag = 4.2f;
        [Tooltip("Degrees a second it slews round while it slides - a fallen machine does " +
                 "not go straight on, it comes round on the side it fell.")]
        public float slew = 55f;
        [Tooltip("How big the pack's fire is drawn on a motorcycle. The prefabs are " +
                 "authored for a burning car.")]
        public float fireScale = 0.55f;
        [Tooltip("Seconds a burning machine has before the tank goes. The fire is the " +
                 "warning and the bang is what it was warning about - long enough to " +
                 "read as a machine burning, short enough that nobody forgets it.")]
        [Min(0.5f)] public float fuse = 4.5f;

        [Header("Watching")]
        [Tooltip("The camera goes with the pack. Off, or the moment WASD is touched, it " +
                 "is yours - a fixed camera the lanes ride past is the better view of a " +
                 "landing and the worse view of everything else.")]
        public bool follow = true;
        [Tooltip("Lay marks across the lanes every few metres. Without them a machine on " +
                 "a flat grey floor does not read as moving at all.")]
        public bool marks = true;
        [Tooltip("Name the act over each lane, and its state under it.")]
        public bool labels = true;

        // ------------------------------------------------------------------ the lanes

        sealed class Lane
        {
            public Act Act;
            public string Name;
            public Transform Root;        // the lane's start, and what a thrown man is left in
            public Transform Machine;
            public BikeBody Body;
            public BikeOccupant Driver, Shooter;
            public BikeSpill Spill;
            public RiderSpill DriverOff, ShooterOff;
            public float S;               // metres ridden
            public bool Fired, Done;
            public float Since;           // seconds since it finished
            public Vector3 Start;
        }

        readonly List<Lane> _lanes = new List<Lane>();
        Transform _stage;
        DemoCamera _cam;
        RiderSpill.Wardrobe _wardrobe;
        AnimationClip _ride;
        int _watching = -1;               // the lane the camera is on, -1 for the pack
        float _cycle;                     // seconds this run has been going
        int _runs;

        const string StageName = "Bike Spill Show";

        void Awake()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { enabled = false; return; }
            Build();
#else
            Debug.LogError("[BikeShow] loads Synty prefabs through the AssetDatabase and " +
                           "only runs in the editor.");
            enabled = false;
#endif
        }

        void OnDestroy()
        {
            // a bench that leaves the clock at a quarter speed has broken every other
            // scene in the editor until somebody works out why
            Time.timeScale = 1f;
            if (_stage != null) Destroy(_stage.gameObject);
        }

        // ------------------------------------------------------------------ the build

        void Build()
        {
#if UNITY_EDITOR
            for (var old = GameObject.Find(StageName); old != null; old = GameObject.Find(StageName))
                DestroyImmediate(old);

            var stage = new GameObject(StageName);
            DemoScratch.Unsaved(stage);
            _stage = stage.transform;

            Ground();
            Light();
            Camera();

            _wardrobe = RiderSpill.Wardrobe.Stock();
            _ride = CrewKit.Ride != null ? CrewKit.Ride : CrewKit.Clips().SitLoop;
            if (_ride == null)
                Debug.LogWarning("[BikeShow] no seated clip under Assets/Animations - nobody " +
                                 "can be sat on a machine.");
            if (_wardrobe.Fall == null || _wardrobe.Land == null)
                Debug.LogWarning("[BikeShow] the locomotion pack's fall/landing takes are " +
                                 "missing, so a man thrown off will fly in whatever he was " +
                                 "sitting in. Assets/Synty/AnimationBaseLocomotion.");

            var acts = new[] { Act.RideOn, Act.PillionShot, Act.RiderShot, Act.Burns };
            var names = new[] { "1  RIDES ON", "2  PILLION SHOT", "3  RIDER SHOT", "4  BURNS" };
            for (int i = 0; i < acts.Length; i++)
            {
                var root = new GameObject("Lane " + (i + 1) + " - " + acts[i]);
                DemoScratch.Unsaved(root);
                root.transform.SetParent(_stage, false);
                var lane = new Lane
                {
                    Act = acts[i],
                    Name = names[i],
                    Root = root.transform,
                    Start = new Vector3((i - (acts.Length - 1) * 0.5f) * laneGap, 0f, 0f),
                };
                _lanes.Add(lane);
                Deal(lane);
            }
            if (marks) Marks();
            Frame();
#endif
        }

        /// <summary>One lane's machine and its two men, at the start line. Called again
        /// for every run of the loop: the cheapest correct way to put a wreck back on
        /// its wheels is not to - it is thrown away and a fresh machine is stood up.</summary>
        void Deal(Lane lane)
        {
#if UNITY_EDITOR
            if (lane.Machine != null) Destroy(lane.Machine.gameObject);
            // the men are re-parented to the lane when they come off, so they outlive
            // their machine and have to be swept by hand
            for (int i = lane.Root.childCount - 1; i >= 0; i--) Destroy(lane.Root.GetChild(i).gameObject);

            lane.Machine = null;
            lane.Body = null;
            lane.Driver = lane.Shooter = null;
            lane.Spill = null;
            lane.DriverOff = lane.ShooterOff = null;
            lane.S = 0f;
            lane.Fired = lane.Done = false;
            lane.Since = 0f;

            var prefab = BikeDemoBuilder.Machine(machine);
            if (prefab == null)
            {
                Debug.LogWarning("[BikeShow] no two-wheeler called '" + machine + "'.");
                return;
            }
            var go = Instantiate(prefab, lane.Root);
            go.name = prefab.name;
            DemoScratch.Unsaved(go);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            go.transform.localPosition = lane.Start;
            go.transform.localRotation = Quaternion.identity;
            lane.Machine = go.transform;
            lane.Body = new BikeBody(lane.Machine);

            if (_ride == null) return;
            var (driver, shooter) = Bodies(_lanes.IndexOf(lane));
            if (driver == null) { Debug.LogWarning("[BikeShow] no gang body to ride."); return; }

            lane.Driver = BikeOccupant.Seat(lane.Body, driver, _ride, pillion: false);
            lane.Shooter = BikeOccupant.Seat(lane.Body, shooter != null ? shooter : driver,
                _ride, pillion: true);
            if (lane.Shooter != null && lane.Driver != null)
                lane.Shooter.Pose.Rider = lane.Driver.Pose;

            // A man thrown into the road with no shadow is a cardboard cut-out lying on
            // a grey floor - and this bench exists to judge exactly that pose. The crowd
            // turns rider shadows off because a rider is four pixels in a street; here he
            // is the subject.
            Shadows(lane.Driver);
            Shadows(lane.Shooter);
            Arm(lane);
#endif
        }

        static void Shadows(BikeOccupant man)
        {
            if (man == null) return;
            foreach (var r in man.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Arm(Lane lane)
        {
#if UNITY_EDITOR
            if (!armed || lane.Shooter == null) return;
            var weapon = CrewKit.Weapon(gun);
            if (weapon == null) return;
            var animator = lane.Shooter.GetComponentInChildren<Animator>();
            if (animator == null) return;
            CrewArms.Attach(animator, weapon);
#endif
        }

        /// <summary>Two faces for a lane, and never the same face on one machine. The
        /// lanes are dealt off different places in the table so four machines are not
        /// the same two men copied four times - the whole show is a comparison and it
        /// is easier to make when the lanes do not look identical.</summary>
        static (GameObject driver, GameObject shooter) Bodies(int lane)
        {
            var looks = LivingCity.Gangs.GangLooks.Hoods;
            if (looks == null || looks.Length == 0) return (null, null);
            GameObject driver = null, shooter = null;
            for (int k = 0; k < looks.Length && (driver == null || shooter == null); k++)
            {
                var body = BikeDemoBuilder.Cast(looks[(lane * 2 + k) % looks.Length]);
                if (body == null) continue;
                if (driver == null) driver = body;
                else if (body != driver) shooter = body;
            }
            return (driver, shooter);
        }

        // ------------------------------------------------------------------ the frame

        void Update()
        {
#if UNITY_EDITOR
            if (_stage == null) return;
            Push();
            Keys();
            float dt = Time.deltaTime;
            _cycle += dt;

            bool all = true;
            foreach (var lane in _lanes)
            {
                Tick(lane, dt);
                if (!lane.Done) all = false;
            }
            if (!all) return;

            // every lane has played itself out: the wreck stands for a moment and then
            // the whole show goes back to the start line together
            float youngest = float.MaxValue;
            foreach (var lane in _lanes) youngest = Mathf.Min(youngest, lane.Since);
            if (youngest >= hold) Restart();
#endif
        }

        void Tick(Lane lane, float dt)
        {
            if (lane.Machine == null) { lane.Done = true; lane.Since += dt; return; }

            if (lane.Spill == null)
            {
                lane.S += speed * dt;
                var at = lane.Start + Vector3.forward * lane.S;
                lane.Machine.localPosition = at;
                lane.Machine.localRotation = Quaternion.identity;
                lane.Body.Tick(dt, speed, 0f);
                if (!lane.Fired && lane.S >= runMetres * actAt) Fire(lane);
            }
            else
            {
                // the spill drives the machine's own transform now; all this owes it is
                // wheels that spin down with it
                lane.Body.Tick(dt, lane.Spill.Speed, 0f);
                // AND THEN THE TANK GOES. A machine that catches fire and burns quietly
                // for ever is not what a burning machine does, and the bench is where
                // the seconds between the two are argued about (fuse). The street sets
                // the same one off with the same read (DemoCrews.TickBikes) - it just
                // has people standing near it to catch in the blast.
                if (lane.Spill.TakeBlast())
                    Explosion.Blow(lane.Machine.position + Vector3.up * 0.4f, null, null, 0, 0f);
            }

            Wear(lane);

            // Done: the act has played out and there is nothing left moving.
            bool ridden = lane.S >= runMetres;
            switch (lane.Act)
            {
                case Act.RideOn:
                    lane.Done = ridden;
                    break;
                case Act.PillionShot:
                    lane.Done = ridden && Settled(lane.ShooterOff);
                    break;
                case Act.RiderShot:
                    lane.Done = lane.Spill != null && lane.Spill.Settled &&
                                Settled(lane.DriverOff) && Settled(lane.ShooterOff);
                    break;
                case Act.Burns:
                    // and not until it has blown: the bang is the last beat of this act,
                    // and a loop that restarts the lane while the machine is still
                    // burning never shows it
                    lane.Done = lane.Spill != null && lane.Spill.Settled && lane.Spill.Blown &&
                                Settled(lane.DriverOff) && Settled(lane.ShooterOff);
                    break;
            }
            if (lane.Done) lane.Since += dt;
        }

        static bool Settled(RiderSpill spill) => spill == null || spill.Settled;

        /// <summary>The act, at the mark. Everything a scene decides - who dies, which
        /// way they go, whether the machine goes with them - is decided HERE and nowhere
        /// in the spill classes, which only know how to fall.</summary>
        void Fire(Lane lane)
        {
            lane.Fired = true;
            var heading = Vector3.forward;
            var carrying = heading * speed;

            switch (lane.Act)
            {
                case Act.RideOn:
                    break;

                case Act.PillionShot:
                    // he is hit and goes off the back and to the kerb side; the machine
                    // never knows about it
                    lane.ShooterOff = RiderSpill.Throw(lane.Shooter, carrying, dies: true,
                        _wardrobe, lane.Root, side: 1f);
                    break;

                case Act.RiderShot:
                    lane.DriverOff = RiderSpill.Throw(lane.Driver, carrying, dies: true,
                        _wardrobe, lane.Root, side: -1f);
                    lane.ShooterOff = RiderSpill.Throw(lane.Shooter, carrying, dies: false,
                        _wardrobe, lane.Root, side: -1f);
                    lane.Spill = BikeSpill.Begin(lane.Machine, speed, lane.Machine.forward, side: -1f);
                    break;

                case Act.Burns:
                    lane.DriverOff = RiderSpill.Throw(lane.Driver, carrying, dies: false,
                        _wardrobe, lane.Root, side: 1f);
                    lane.ShooterOff = RiderSpill.Throw(lane.Shooter, carrying, dies: false,
                        _wardrobe, lane.Root, side: 1f);
                    lane.Spill = BikeSpill.Begin(lane.Machine, speed, lane.Machine.forward,
                        side: 1f, alight: true);
                    break;
            }
        }

        /// <summary>What the men who are still ON the machine are told each frame - the
        /// same three things RoadBike tells its riders.</summary>
        void Wear(Lane lane)
        {
            Wear(lane.Driver, lane, pillion: false);
            Wear(lane.Shooter, lane, pillion: true);
        }

        void Wear(BikeOccupant man, Lane lane, bool pillion)
        {
            if (man == null || man.Pose == null || !man.Pose.enabled) return;
            man.Pose.Speed = lane.Spill != null ? lane.Spill.Speed : speed;
            man.Pose.FootDown = false;
            // the pillion rides with his gun out at his own side of the street, which is
            // the pose the drive-by is actually ridden in and the one worth looking at
            man.Pose.AimAt = pillion && aiming && armed && lane.Machine != null
                ? lane.Machine.position + lane.Machine.right * 9f + Vector3.up * 1.2f
                : (Vector3?)null;
        }

        /// <summary>Every knob onto the statics the spill actually reads, each frame.
        /// The same contract the pose bench keeps with BikeBody and BikePose: the numbers
        /// live in the shared class, the bench only pushes them, so a value that looks
        /// right here IS the value to paste back.</summary>
        void Push()
        {
            RiderSpill.Gravity = gravity;
            RiderSpill.ThrowUp = throwUp;
            RiderSpill.ThrowOut = throwOut;
            RiderSpill.ThrowAhead = throwAhead;
            RiderSpill.Drag = manDrag;
            RiderSpill.TumbleRate = tumble;
            RiderSpill.DeadTumbleRate = deadTumble;
            RiderSpill.LieThere = lieThere;
            RiderSpill.LieJitter = lieJitter;
            RiderSpill.SprawlFrom = sprawlFrom;
            RiderSpill.SprawlSpeed = sprawlSpeed;
            RiderSpill.RiseSpeed = riseSpeed;
            RiderSpill.RiseFade = riseFade;
            RiderSpill.DeathSpeedJitter = deathJitter;
            RiderSpill.LimpChance = limpChance;
            RiderSpill.UprightIn = uprightIn;
            BikeSpill.RollRate = rollRate;
            BikeSpill.Drag = machineDrag;
            BikeSpill.Slew = slew;
            BikeSpill.FireScale = fireScale;
            BikeSpill.Fuse = fuse;
        }

        void Restart()
        {
            _runs++;
            _cycle = 0f;
            foreach (var lane in _lanes) Deal(lane);
        }

        // ------------------------------------------------------------------ the set

        void Ground()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(_stage, false);
            DemoScratch.Unsaved(floor);
            Destroy(floor.GetComponent<Collider>());
            // a plane is 10 m: wide enough for four lanes, long enough for the run and
            // the slide past the end of it
            floor.transform.localScale = new Vector3(laneGap * 0.7f, 1f, runMetres * 0.2f);
            floor.transform.position = new Vector3(0f, 0f, runMetres * 0.5f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Show Ground" };
            mat.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
            mat.SetFloat("_Smoothness", 0.08f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Marks across the lanes every few metres. A machine on a flat grey
        /// floor is a machine that does not appear to be moving at all, and half of what
        /// this bench is judging is speed.</summary>
        void Marks()
        {
            var mat = Paint(new Color(0.42f, 0.44f, 0.47f));
            for (float z = 0f; z <= runMetres + 12f; z += 4f)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Mark " + z.ToString("0");
                bar.transform.SetParent(_stage, false);
                DemoScratch.Unsaved(bar);
                Destroy(bar.GetComponent<Collider>());
                bar.transform.localScale = new Vector3(_lanes.Count * laneGap + 2f, 0.02f, 0.35f);
                bar.transform.localPosition = new Vector3(0f, 0.005f, z);
                bar.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static Material Paint(Color colour)
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var shader = urp != null ? urp : Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "Show paint" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            return mat;
        }

        void Light()
        {
            var sun = new GameObject("Sun").AddComponent<UnityEngine.Light>();
            sun.transform.SetParent(_stage, false);
            DemoScratch.Unsaved(sun.gameObject);
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(46f, 152f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
        }

        void Camera()
        {
            var camGo = new GameObject("Show Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(_stage, false);
            DemoScratch.Unsaved(camGo);
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.50f, 0.60f, 0.70f);
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            _cam = camGo.AddComponent<DemoCamera>();
            _cam.minDistance = 4f;      // the city's floor is 18 m and this is a bench
            _cam.distance = 22f;
            _cam.yaw = 152f;            // three-quarters on from the right, where the gun arm is
            _cam.pitch = 24f;
            _cam.pivot = new Vector3(0f, 1f, 0f);
            _cam.showHint = true;
            _cam.hintTopPx = 8f;
            _cam.hint = "WASD: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "1-4: watch one lane   0: the pack   F: follow on/off   " +
                        "R: start the run again   [ ]: slow down / speed up   SPACE: pause";
            Frame();
        }

        void Frame()
        {
            if (_cam == null) return;
            if (follow) _cam.Ride(Watching);
            else _cam.Drop();
        }

        /// <summary>Where the camera looks: the middle of whatever is still riding, or
        /// one lane when one is being watched.</summary>
        Vector3? Watching()
        {
            if (_watching >= 0 && _watching < _lanes.Count)
            {
                var one = _lanes[_watching];
                return one.Machine != null ? one.Machine.position + Vector3.up * 0.9f : (Vector3?)null;
            }
            var sum = Vector3.zero;
            int n = 0;
            foreach (var lane in _lanes)
            {
                if (lane.Machine == null) continue;
                sum += lane.Machine.position;
                n++;
            }
            return n == 0 ? (Vector3?)null : sum / n + Vector3.up * 0.9f;
        }

        // ------------------------------------------------------------------ the keys

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.fKey.wasPressedThisFrame) { follow = !follow; Frame(); }
            if (kb.digit0Key.wasPressedThisFrame) { _watching = -1; Frame(); }
            if (kb.digit1Key.wasPressedThisFrame) Watch(0);
            if (kb.digit2Key.wasPressedThisFrame) Watch(1);
            if (kb.digit3Key.wasPressedThisFrame) Watch(2);
            if (kb.digit4Key.wasPressedThisFrame) Watch(3);

            // Slow motion is the whole point of a bench like this: a landing is six
            // frames at full pace. Time.timeScale, so the clips slow with the flight -
            // scaling the flight alone would be a different bench.
            if (kb.leftBracketKey.wasPressedThisFrame)
                Time.timeScale = Mathf.Max(0.05f, Time.timeScale * 0.5f);
            if (kb.rightBracketKey.wasPressedThisFrame)
                Time.timeScale = Mathf.Min(2f, Time.timeScale * 2f);
            if (kb.spaceKey.wasPressedThisFrame)
                Time.timeScale = Time.timeScale > 0.001f ? 0f : 1f;
        }

        void Watch(int lane)
        {
            if (lane < 0 || lane >= _lanes.Count) return;
            _watching = lane;
            follow = true;
            Frame();
        }

        // ------------------------------------------------------------------ the labels

        void OnGUI()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            if (labels)
                foreach (var lane in _lanes)
                {
                    var at = Head(lane);
                    if (!at.HasValue) continue;
                    var p = cam.WorldToScreenPoint(at.Value);
                    if (p.z <= 0f) continue;
                    var style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    };
                    style.normal.textColor = Ink(lane);
                    GUI.Label(new Rect(p.x - 110f, Screen.height - p.y - 30f, 220f, 22f), lane.Name, style);
                    var small = new GUIStyle(style) { fontSize = 12, fontStyle = FontStyle.Normal };
                    small.normal.textColor = new Color(0.88f, 0.90f, 0.94f);
                    GUI.Label(new Rect(p.x - 110f, Screen.height - p.y - 10f, 220f, 20f), State(lane), small);
                }

            var read = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            read.normal.textColor = new Color(0.86f, 0.90f, 0.96f);
            GUI.Label(new Rect(12f, Screen.height - 84f, 1200f, 80f), Readout(), read);
        }

        Vector3? Head(Lane lane)
        {
            if (lane.Machine == null) return null;
            return lane.Machine.position + Vector3.up * 2.1f;
        }

        static Color Ink(Lane lane) => lane.Act switch
        {
            Act.RideOn => new Color(0.60f, 0.90f, 0.65f),
            Act.PillionShot => new Color(1f, 0.62f, 0.35f),
            Act.RiderShot => new Color(1f, 0.40f, 0.35f),
            _ => new Color(1f, 0.82f, 0.30f),
        };

        static string State(Lane lane)
        {
            if (lane.Done) return "done";
            if (!lane.Fired) return "riding  " + lane.S.ToString("0") + " m";
            var sb = new StringBuilder();
            if (lane.Spill != null) sb.Append(lane.Spill.Flat ? "machine down" : "going over").Append("  ");
            Say(sb, "rider", lane.DriverOff);
            Say(sb, "pillion", lane.ShooterOff);
            return sb.Length == 0 ? "riding on" : sb.ToString();
        }

        static void Say(StringBuilder sb, string who, RiderSpill spill)
        {
            if (spill == null) return;
            string what = !spill.Down ? "in the air"
                : spill.Settled ? (spill.Dying ? "dead" : "up")
                : spill.Dying ? "dying" : "getting up";
            sb.Append(who).Append(' ').Append(what).Append("  ");
        }

        string Readout()
        {
            string clock = Time.timeScale < 0.001f ? "PAUSED"
                : Time.timeScale.ToString("0.00") + "x";
            return machine + "   run " + runMetres.ToString("0") + " m at " +
                   speed.ToString("0.0") + " m/s   act at " + (runMetres * actAt).ToString("0") +
                   " m   time " + clock + "   run #" + (_runs + 1) +
                   "   " + _cycle.ToString("0.0") + " s\n" +
                   "throw: up " + RiderSpill.ThrowUp.ToString("0.0") +
                   "  out " + RiderSpill.ThrowOut.ToString("0.0") +
                   "  ahead " + RiderSpill.ThrowAhead.ToString("0.00") +
                   "  gravity " + RiderSpill.Gravity.ToString("0.0") +
                   "  drag " + RiderSpill.Drag.ToString("0.0") +
                   "  tumble " + RiderSpill.TumbleRate.ToString("0") + " deg/s\n" +
                   "machine: roll " + BikeSpill.RollRate.ToString("0") +
                   " deg/s  drag " + BikeSpill.Drag.ToString("0.0") +
                   "  slew " + BikeSpill.Slew.ToString("0") + " deg/s" +
                   "   (every one of these is on this component's Inspector - " +
                   "drag it while it runs)";
        }
    }
}
