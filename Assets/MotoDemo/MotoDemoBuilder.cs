using System.Collections.Generic;
using System.Text;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace MotoDemo
{
    // EVERY TWO-WHEELER THE PACKS SHIP, STOOD IN ONE LINE.
    //
    // The bike bench (Assets/BikeDemo) answers "does a man sit right on THIS machine";
    // this scene answers the question before it - WHICH machines are there at all, and
    // how do they measure against one another. The two are deliberately different
    // shapes: the bench is one machine with every proportion on the Inspector, this is
    // the whole rank with a plaque under each one.
    //
    // Nothing here is a list typed out by hand, and that is the point. The packs are
    // scanned for anything two-wheeled (MotoDemoBuilder.Machines), so a pack added
    // tomorrow turns up in the line without this file being touched. What the scan
    // finds today is nine machines out of two packs, plus one this project cut for
    // itself (Moped_01_NoBox - the pack moped with its delivery box off, because the
    // box sits where a pillion does; PortraitStudio.VehicleModelFor):
    //
    //   MOTORCYCLES   Motorbike_01 (palm city), Motorbike_01 and Motorbike_02 (police)
    //   MOPEDS        Moped_01, Scooter_01
    //   QUAD          Quad_Bike_01
    //   ELECTRIC      E_Bike_01, E_Scooter_01   - barred, another decade (1987)
    //   PEDAL         Bike_01 (police, pedals and a battery)
    //
    // THE TRAP THE PLAQUES EXIST FOR: two of those are called SM_Veh_Motorbike_01 and
    // they are different machines - one out of Palm City, one out of the police pack.
    // Every list in this project that keys a vehicle by BARE NAME (VehicleCatalog,
    // StreetBikes.Body) therefore cannot tell them apart; it takes whichever folder it
    // asks first. So the line is keyed by PATH and every plaque names its pack, which
    // is the only way to say out loud which of the two you are looking at.
    //
    // The plaque also carries what BikeBody measured off the model - wheelbase, wheel
    // radius, whether the thing is long enough to seat two - because those are the
    // numbers that decide whether a machine can carry a drive-by at all, and they are
    // measured, not authored. And it says where the machine stands with the game:
    // RIDDEN (VehicleCatalog.Motorcycles), LAW (the liveried pack) or BARRED.
    //
    // Editor only, like every demo here: it pulls prefabs straight out of the
    // AssetDatabase. It builds itself in edit mode as well as in Play, and nothing it
    // builds may ever be saved into the scene file.
    [ExecuteAlways]
    public class MotoDemoBuilder : MonoBehaviour
    {
        // ------------------------------------------------------------------ the knobs

        [Header("Who is in the line")]
        [Tooltip("The proper motorcycles: anything the packs call a Motorbike.")]
        public bool motorcycles = true;
        [Tooltip("Mopeds and the petrol scooter - a motor and two wheels, just a smaller one.")]
        public bool mopeds = true;
        [Tooltip("The quad. Four wheels, but it is drawn and ridden as a bike and it reads " +
                 "through BikeBody like one, so it stands in the line.")]
        public bool quads = true;
        [Tooltip("The electric pair (E_Bike, E_Scooter). BARRED by VehicleCatalog - the game " +
                 "is 1987 - and here only so the bar can be seen rather than taken on trust.")]
        public bool electric = true;
        [Tooltip("The police pedal bike. Not a motor at all; in the line because the question " +
                 "was every two-wheeler in the project.")]
        public bool pedal = true;

        [Header("The stand")]
        [Tooltip("Machines to a row before it wraps.")]
        [Range(1, 12)] public int perRow = 5;
        [Tooltip("Metres of air between two machines, and between two rows.")]
        public float gap = 1.6f;
        public float rowGap = 4.5f;
        [Tooltip("Which way each machine is turned. 180 is nose-on to the camera; 215 stands " +
                 "it three-quarters, where the nose and the flank both read - which is the " +
                 "difference between a moped and a scooter at a glance.")]
        [Range(0f, 360f)] public float yaw = 215f;
        [Tooltip("Turn them all slowly on the spot. Play only - an editor turntable would " +
                 "spin whenever Unity felt like ticking, which is not a turntable.")]
        public bool spin;
        [Range(0f, 60f)] public float spinSpeed = 18f;

        [Header("Show")]
        [Tooltip("The plaque over each machine: its number, its name, its pack, what BikeBody " +
                 "measured off it, and where it stands with the game.")]
        public bool plaques = true;
        [Tooltip("Sit a man on every machine - the whole rank posed at once, which is how a " +
                 "proportion that suits the city bike and ruins the moped is caught. " +
                 "OFF PLAY THIS IS NOISY: seating a body destroys its colliders, and Unity " +
                 "will not have Destroy in edit mode, so it complains once per part. Press " +
                 "Play for a clean rank of riders.")]
        public bool riders;
        [Tooltip("And a mate behind him, wherever the machine measures long enough to seat " +
                 "two (BikeBody.SeatsTwo). A machine that is not is left with its rider only " +
                 "and says so on its plaque.")]
        public bool pillions;
        [Tooltip("Print the whole line to the console - name, pack, measurements, standing. " +
                 "L does the same in Play. This is the list to paste into a note; the scene " +
                 "is the one to look at.")]
        public bool logNow;
        [Tooltip("Build the line in the EDITOR too, so the scene is not an empty box until " +
                 "Play is pressed. Nothing built this way is ever saved into the scene.")]
        public bool preview = true;

        // ------------------------------------------------------------------ what a machine is

        public enum Family { Motorcycle, Moped, Quad, Electric, Pedal }

        /// <summary>One machine as the scan found it. The PATH is the identity - two
        /// packs ship a prefab called SM_Veh_Motorbike_01 and they are not the same
        /// machine - and the pack is carried along so a plaque can say which.</summary>
        public readonly struct Machine
        {
            public readonly string Path, Pack, Name;
            public readonly Family Kind;

            public Machine(string path, string pack, string name, Family kind)
            {
                Path = path; Pack = pack; Name = name; Kind = kind;
            }
        }

        /// <summary>What is asked of the asset database. Names, not folders: a pack that
        /// keeps its vehicles somewhere new still answers to "Motorbike", and asking by
        /// name costs four small queries instead of a walk of every prefab under
        /// Assets/Synty (this project holds ~137,000 assets).</summary>
        static readonly string[] Hunt = { "Motorbike", "Moped", "Scooter", "Bike" };

        /// <summary>Where the scan looks. The packs, and this project's own vehicle
        /// folder - a machine the outfit had remade out of a pack body (the boxless
        /// moped) is a machine in the project and belongs in the line beside the one it
        /// was cut from, which is the comparison the showroom exists to make.</summary>
        static readonly string[] SearchRoots = { "Assets/Synty", "Assets/Prefabs" };

        /// <summary>Two-wheeled by name and not a machine: the parts a machine is dressed
        /// with, the rack you lean one against, the sign that says where to leave it, and
        /// the gym's exercise bike. The pavement robot answers to nothing here but is
        /// named anyway - it used to reach every scan that looked for "bot".</summary>
        static readonly string[] NotAMachine =
        {
            "_Attach_", "Stand", "Rack", "Sign", "Gym", "Collision", "Delivery_Bot",
            "Back_Seat", "Exhaust", "Steering_Wheel",
        };

        /// <summary>The two packs that ship a two-wheeler at all, and the machines the
        /// game names by hand (VehicleCatalog's civilian pair and the law's tourer).
        /// Only the safety net under the scan uses these - the scan is what finds a
        /// machine nobody has named yet.</summary>
        static readonly string[] KnownFolders =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/",
            "Assets/Prefabs/Vehicles/",
        };

        static readonly string[] KnownMachines =
        {
            "SM_Veh_Motorbike_01", "SM_Veh_Motorbike_02", "SM_Veh_Moped_01",
            "SM_Veh_Moped_01_NoBox",
        };

        /// <summary>Every two-wheeler in the project, in the order the line stands them:
        /// the motorcycles first, then the small stuff, then the ones that are here to be
        /// looked at rather than ridden. Empty outside the editor - this is an editor
        /// scene, and it says so rather than quietly building nothing.</summary>
        public static List<Machine> Machines()
        {
            var found = new List<Machine>();
#if UNITY_EDITOR
            var seen = new HashSet<string>();
            foreach (var token in Hunt)
            {
                foreach (var guid in DemoAssetLoad.Find(token + " t:Prefab", SearchRoots))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (!path.EndsWith(".prefab") || !seen.Add(path)) continue;

                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    // the packs keep bumpers and number plates in an Attachments folder,
                    // and a back seat is not a machine you can stand in a line
                    if (path.Contains("/Attachments/") || path.Contains("/Collision/")) continue;
                    bool part = false;
                    foreach (var mark in NotAMachine) if (name.Contains(mark)) { part = true; break; }
                    if (part) continue;
                    // "SM_Veh_" or it is somebody's prop that happens to be called a bike
                    if (!name.StartsWith("SM_Veh_")) continue;

                    var prefab = DemoAssetLoad.Load<GameObject>(path);
                    if (prefab == null) continue;
                    if (prefab.GetComponentInChildren<MeshRenderer>() == null &&
                        prefab.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;

                    found.Add(new Machine(path, PackOf(path), name, KindOf(name)));
                }
            }

            // THE NET UNDER THE SCAN. Everything above depends on the asset database
            // matching a token inside a prefab name, which is a search index and not a
            // contract - a reindex, a renamed folder, a pack imported while the editor
            // was busy, and a query comes back short with nothing to say it did. The
            // machines the GAME rides are too important to lose that way, so they are
            // asked for by name as well (the same names, out of the same folders, that
            // StreetBikes asks for), and anything the scan already has is skipped.
            foreach (var folder in KnownFolders)
                foreach (var name in KnownMachines)
                {
                    var path = folder + name + ".prefab";
                    if (seen.Contains(path)) continue;
                    if (DemoAssetLoad.Load<GameObject>(path) == null) continue;
                    seen.Add(path);
                    found.Add(new Machine(path, PackOf(path), name, KindOf(name)));
                }

            found.Sort((a, b) =>
            {
                int byKind = a.Kind.CompareTo(b.Kind);
                if (byKind != 0) return byKind;
                int byName = string.CompareOrdinal(a.Name, b.Name);
                return byName != 0 ? byName : string.CompareOrdinal(a.Pack, b.Pack);
            });
#endif
            return found;
        }

        /// <summary>Which family a machine belongs to, off its name. Order matters: the
        /// electric pair is called E_Bike and E_Scooter, so it has to be asked about
        /// before "Scooter" and before "Bike" catch them.</summary>
        static Family KindOf(string name)
        {
            if (name.Contains("_E_")) return Family.Electric;
            if (name.Contains("Quad")) return Family.Quad;
            if (name.Contains("Motorbike")) return Family.Motorcycle;
            if (name.Contains("Moped") || name.Contains("Scooter")) return Family.Moped;
            return Family.Pedal;
        }

        /// <summary>The pack a path came out of - "PALM CITY", "POLICE STATION". The one
        /// thing that tells the two SM_Veh_Motorbike_01s apart.</summary>
        static string PackOf(string path)
        {
            const string root = "Assets/Synty/";
            // not out of a pack at all: this project's own body, cut from one
            if (path.StartsWith("Assets/Prefabs/")) return "OUTFIT";
            if (!path.StartsWith(root)) return "?";
            int cut = path.IndexOf('/', root.Length);
            var folder = cut < 0 ? path.Substring(root.Length) : path.Substring(root.Length, cut - root.Length);
            if (folder.StartsWith("Polygon")) folder = folder.Substring("Polygon".Length);
            // "PalmCity" reads as one word on a plaque a metre wide, so break it up
            var sb = new StringBuilder(folder.Length + 4);
            for (int i = 0; i < folder.Length; i++)
            {
                if (i > 0 && char.IsUpper(folder[i]) && !char.IsUpper(folder[i - 1])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(folder[i]));
            }
            return sb.ToString();
        }

        bool Wanted(Family kind) => kind switch
        {
            Family.Motorcycle => motorcycles,
            Family.Moped => mopeds,
            Family.Quad => quads,
            Family.Electric => electric,
            Family.Pedal => pedal,
            _ => true,
        };

        // ------------------------------------------------------------------ the line

        /// <summary>A machine as it stands in the scene: the instance, what was measured
        /// off it, and who is sitting on it.</summary>
        sealed class Stood
        {
            public Machine What;
            public Transform Tf;
            public BikeBody Body;
            public BikeOccupant Rider, Pillion;
            public float BaseYaw;
        }

        readonly List<Stood> _line = new List<Stood>();
        /// <summary>The materials the stand made for itself, taken down with it.</summary>
        readonly List<Material> _mats = new List<Material>();
        Transform _stage;
        DemoCamera _cam;
        Bounds _field;
        Switch _built;          // the switches the line standing now was built from
        string _guiText;        // the footer, rebuilt only when the count it quotes moves
        int _guiCount = -1;
        GUIStyle _guiStyle;

        /// <summary>Every knob a change of which re-deals the rank. Compared as fields
        /// rather than through a string built every frame to be thrown away.</summary>
        struct Switch
        {
            public bool Motorcycles, Mopeds, Quads, Electric, Pedal, Plaques, Riders, Pillions, Spin;
            public int PerRow;
            public float Gap, RowGap, Yaw;

            public bool Same(in Switch o) =>
                Motorcycles == o.Motorcycles && Mopeds == o.Mopeds && Quads == o.Quads &&
                Electric == o.Electric && Pedal == o.Pedal && Plaques == o.Plaques &&
                Riders == o.Riders && Pillions == o.Pillions && Spin == o.Spin &&
                PerRow == o.PerRow && Gap == o.Gap && RowGap == o.RowGap && Yaw == o.Yaw;
        }

        const string StageName = "Moto Showroom";

        void Awake()
        {
#if UNITY_EDITOR
            Stand();
#else
            Debug.LogError("[MotoDemo] This showroom loads Synty prefabs through the " +
                           "AssetDatabase and only runs in the editor.");
#endif
        }

        void OnDisable()
        {
            // the scene being torn down takes everything with it, and destroying by hand
            // at that moment is console noise at best
            if (!gameObject.scene.isLoaded) { Forget(); return; }
            Clear();
        }

        void Update()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Keys();
            // the preview switched on or off, or the scripts reloaded out from under it
            if (!Application.isPlaying && preview == (_stage == null)) { Clear(); Stand(); }
            if (_stage == null) return;
            if (!Switches().Same(_built)) { Clear(); Stand(); }
            if (spin && Application.isPlaying) Spin(Time.deltaTime);
            Ride();
            if (logNow) { logNow = false; Debug.Log(Sheet()); }
#endif
        }

        /// <summary>The whole stand: ground, sun, camera, and the rank itself.</summary>
        void Stand()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !preview) return;
            // A line left over from before the scripts reloaded: the reference to it is
            // gone (nothing here is serialised) but the objects are not, and without
            // this the editor quietly fills up with motorcycles, one rank per recompile.
            // Edit mode only, and deliberately: off Play the kill is immediate, so the
            // loop terminates. In Play Destroy is DEFERRED and Find would keep handing
            // back the same doomed object for ever.
            if (!Application.isPlaying)
                for (var old = GameObject.Find(StageName); old != null; old = GameObject.Find(StageName))
                    DestroyImmediate(old);

            _stage = MakeRoot(StageName);
            _built = Switches();
            BuildLine();
            BuildGround();
            BuildLight();
            // THE CAMERA IS BUILT IN EDIT MODE TOO. The bench gets away without one
            // because the Scene view is the only view anybody points at it; a showroom
            // is looked at through the GAME view as well, and a Game view with no
            // camera in the scene says "No cameras rendering" and nothing else.
            BuildCamera();
            if (!Application.isPlaying)
            {
                Frame();
                Debug.Log(Sheet(), this);
            }
#endif
        }

        void BuildLine()
        {
#if UNITY_EDITOR
            var machines = Machines();
            if (machines.Count == 0)
            {
                Debug.LogWarning("[MotoDemo] no two-wheeler found under Assets/Synty - are the " +
                                 "packs still in the project?");
                return;
            }

            var sit = riders ? SitClip() : null;
            float cursorX = 0f, rowZ = 0f, rowDepth = 0f;
            int inRow = 0;
            bool any = false;

            foreach (var machine in machines)
            {
                if (!Wanted(machine.Kind)) continue;
                var prefab = DemoAssetLoad.Load<GameObject>(machine.Path);
                if (prefab == null) continue;

                var go = Instantiate(prefab, _stage);
                go.name = machine.Name + " (" + machine.Pack + ")";
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                foreach (var col in go.GetComponentsInChildren<Collider>()) KillComp(col);
                Unsaved(go);

                // The bay is as wide as the machine turned this way - and, when it is
                // going to be turned on the spot, as wide as its longest side, so a
                // turntable never swings one machine through the next.
                var box = WorldBounds(go);
                float wide = spin ? Mathf.Max(box.size.x, box.size.z) : box.size.x;
                float deep = spin ? Mathf.Max(box.size.x, box.size.z) : box.size.z;

                if (inRow >= perRow)
                {
                    cursorX = 0f;
                    rowZ += rowDepth + rowGap;
                    rowDepth = 0f;
                    inRow = 0;
                }

                // The FOOTPRINT'S CENTRE onto the bay's centre and the tyres onto the
                // ground - never the prefab's own pivot. The packs put a vehicle's
                // origin on its contact line but not always in the middle of it, and a
                // rank laid out by pivot stands crooked with no obvious reason why.
                var centre = new Vector3(cursorX + wide * 0.5f, 0f, rowZ + deep * 0.5f);
                go.transform.position += centre - new Vector3(box.center.x, box.min.y, box.center.z);

                var stood = new Stood
                {
                    What = machine,
                    Tf = go.transform,
                    Body = new BikeBody(go.transform),
                    BaseYaw = yaw,
                };
                _line.Add(stood);

                if (plaques) Plaque(stood, centre, box.size.y);
                if (sit != null) Seat(stood, sit);

                cursorX += wide + gap;
                rowDepth = Mathf.Max(rowDepth, deep);
                inRow++;
                any = true;
            }

            if (!any)
                Debug.LogWarning("[MotoDemo] every family is switched off - nothing to stand.");
            _field = FieldBounds();
#endif
        }

        /// <summary>The seated clip the whole city rides on (CrewKit.Ride), or the bench
        /// sit when the library has none. A SEATED base matters: everything the pose does
        /// not write - the breathing, the head - comes from underneath it.</summary>
        static AnimationClip SitClip()
        {
            var clips = CrewKit.Clips();
            var sit = CrewKit.Ride != null ? CrewKit.Ride : clips.SitLoop;
            if (sit == null)
                Debug.LogWarning("[MotoDemo] no seated clip under Assets/Animations - nobody " +
                                 "can be sat on these machines.");
            return sit;
        }

        void Seat(Stood stood, AnimationClip sit)
        {
#if UNITY_EDITOR
            var (driver, mate) = Bodies();
            if (driver == null)
            {
                Debug.LogWarning("[MotoDemo] no gang body to put on the machines.");
                return;
            }

            stood.Rider = BikeOccupant.Seat(stood.Body, driver, sit, pillion: false);
            if (pillions && stood.Body.SeatsTwo)
            {
                stood.Pillion = BikeOccupant.Seat(stood.Body, mate != null ? mate : driver, sit, pillion: true);
                // the pillion holds ON TO the rider, so he has to be told who that is -
                // and he poses the rider first, because two LateUpdates run in whatever
                // order Unity likes and hips that have not been seated yet are thin air
                if (stood.Pillion != null && stood.Rider != null)
                    stood.Pillion.Pose.Rider = stood.Rider.Pose;
            }
            if (stood.Rider != null) Unsaved(stood.Rider.gameObject);
            if (stood.Pillion != null) Unsaved(stood.Pillion.gameObject);
#endif
        }

        /// <summary>Two faces off the gang table, and never the same one twice - a rank
        /// of one man copied nine times tells you nothing about how he sits.</summary>
        static (GameObject rider, GameObject mate) Bodies()
        {
            GameObject rider = null, mate = null;
            var looks = LivingCity.Gangs.GangLooks.Hoods;
            for (int i = 0; i < looks.Length && (rider == null || mate == null); i++)
            {
                var body = LivingCity.UI.LedgerModelSet.PersonNamed(looks[i]) ??
                           LivingCity.UI.PortraitStudio.FindPeoplePrefab(looks[i]);
                if (body == null) continue;
                if (rider == null) rider = body;
                else if (mate == null && body != rider) mate = body;
            }
            return (rider, mate);
        }

        /// <summary>The men, posed. In Play their own LateUpdate does it; off Play the
        /// editor ticks the player loop when it feels like it, so it is asked for by
        /// hand. Apply() refuses to run twice in a frame, so asking costs nothing when
        /// the LateUpdate did tick - and the pillion poses the rider first, which is why
        /// he is the one asked.</summary>
        void Ride()
        {
            foreach (var stood in _line)
            {
                if (stood.Rider == null) continue;
                stood.Rider.Pose.Speed = 0f;
                stood.Rider.Pose.FootDown = true;
                if (stood.Pillion != null) stood.Pillion.Pose.Speed = 0f;
                if (Application.isPlaying) continue;
                if (stood.Pillion != null && stood.Pillion.Pose != null) stood.Pillion.Pose.Apply();
                else stood.Rider.Pose.Apply();
            }
        }

        void Spin(float dt)
        {
            foreach (var stood in _line)
            {
                if (stood.Tf == null) continue;
                stood.BaseYaw += spinSpeed * dt;
                // about its own footprint centre, which is where it was stood - so a
                // machine turns on the spot instead of walking round the bay
                stood.Tf.localRotation = Quaternion.Euler(0f, stood.BaseYaw, 0f);
            }
        }

        // ------------------------------------------------------------------ the plaques

        static readonly Color MotorInk = new Color(0.95f, 0.86f, 0.55f);
        static readonly Color MopedInk = new Color(0.70f, 0.88f, 0.95f);
        static readonly Color QuadInk = new Color(0.78f, 0.90f, 0.70f);
        static readonly Color BarredInk = new Color(1f, 0.45f, 0.38f);
        static readonly Color PedalInk = new Color(0.75f, 0.75f, 0.80f);

        static Color InkFor(Machine m) => m.Kind switch
        {
            Family.Motorcycle => MotorInk,
            Family.Moped => MopedInk,
            Family.Quad => QuadInk,
            Family.Electric => BarredInk,
            _ => PedalInk,
        };

        void Plaque(Stood stood, Vector3 centre, float height)
        {
            var go = new GameObject(stood.What.Name + " plaque");
            go.transform.SetParent(_stage, false);
            // tipped back the way the cast catalogue's are, so it reads from the camera
            // rather than from directly overhead
            go.transform.SetPositionAndRotation(
                centre + Vector3.up * (height + 0.35f) + Vector3.left * 0.55f,
                Quaternion.Euler(30f, 0f, 0f));
            Unsaved(go);

            var text = go.AddComponent<TextMesh>();
            text.text = PlaqueText(stood);
            text.fontSize = 64;
            text.characterSize = 0.045f;
            text.lineSpacing = 0.95f;
            text.anchor = TextAnchor.LowerLeft;
            text.alignment = TextAlignment.Left;
            text.color = InkFor(stood.What);
        }

        string PlaqueText(Stood stood)
        {
            var m = stood.What;
            var body = stood.Body;
            var sb = new StringBuilder();
            sb.Append(Short(m.Name)).Append('\n');
            sb.Append(m.Pack).Append("   ").Append(Standing(m)).Append('\n');
            sb.Append("wheelbase ").Append(body.Wheelbase.ToString("0.00"))
              .Append(" m   wheel r ").Append(body.WheelRadius.ToString("0.00")).Append('\n');
            sb.Append(body.SeatsTwo ? "seats two" : "RIDER ONLY (too short for a pillion)");
            return sb.ToString();
        }

        /// <summary>Where a machine stands with the game, which is the whole reason a
        /// showroom beats a folder listing: BARRED is the calendar (1987), LAW is the
        /// liveried pack, RIDDEN is VehicleCatalog.Motorcycles - what a street actually
        /// deals - and everything else is a body the packs ship that nothing asks
        /// for.</summary>
        static string Standing(Machine m)
        {
            if (LivingCity.Gameplay.VehicleCatalog.IsBarred(m.Name)) return "BARRED";
            if (LivingCity.Gameplay.VehicleCatalog.IsPoliceVehicle(m.Path)) return "LAW";

            bool ridden = false, sold = false;
            foreach (var name in LivingCity.Gameplay.VehicleCatalog.Motorcycles)
                if (name == m.Name) ridden = true;
            // The counter is a SECOND question and the two answers differ on purpose:
            // the traffic rides the pack's delivery moped, the outfit buys the boxless
            // one. Asked through the same table the ledger and CrewCars ask through, so
            // a plaque cannot drift from what actually turns up at the kerb.
            foreach (var item in LivingCity.Outfit.ArmoryCatalog.Motorcycles)
                if (LivingCity.UI.PortraitStudio.VehicleModelFor(item.DisplayName) == m.Name)
                    sold = true;

            if (ridden && sold) return "RIDDEN / SOLD";
            if (ridden) return "RIDDEN";
            if (sold) return "SOLD";
            return "not asked for";
        }

        static string Short(string name) =>
            name.StartsWith("SM_Veh_") ? name.Substring("SM_Veh_".Length) : name;

        // ------------------------------------------------------------------ the sheet

        /// <summary>The line as a table, for the console. A scene is the thing to look
        /// at; a table is the thing to quote.</summary>
        string Sheet()
        {
            var sb = new StringBuilder("[MotoDemo] ").Append(_line.Count)
                .Append(" two-wheelers on show (of ").Append(Machines().Count)
                .Append(" in the project)\n");
            sb.Append("  name                       pack            standing        wheelbase  wheel r  seats two\n");
            foreach (var stood in _line)
            {
                sb.Append("  ").Append(Short(stood.What.Name).PadRight(26))
                  .Append(stood.What.Pack.PadRight(16))
                  .Append(Standing(stood.What).PadRight(16))
                  .Append(stood.Body.Wheelbase.ToString("0.00").PadLeft(8)).Append(" m")
                  .Append(stood.Body.WheelRadius.ToString("0.00").PadLeft(9))
                  .Append(stood.Body.SeatsTwo ? "     yes" : "      no")
                  .Append('\n');
            }
            return sb.ToString();
        }

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.lKey.wasPressedThisFrame) logNow = true;
            if (kb.tKey.wasPressedThisFrame) { spin = !spin; }
        }

        void OnGUI()
        {
            if (!Application.isPlaying || _line.Count == 0) return;
            // GUI.skin is only there inside OnGUI, so the style is made on the first
            // pass and kept; the text only moves when the count it quotes does
            if (_guiStyle == null)
            {
                _guiStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                _guiStyle.normal.textColor = new Color(0.85f, 0.9f, 0.96f);
            }
            if (_guiCount != _line.Count)
            {
                _guiCount = _line.Count;
                _guiText = _line.Count + " machines   amber: motorcycle   blue: moped/scooter   " +
                           "green: quad   red: BARRED (1987)   grey: pedal\n" +
                           "L: print the list   T: turntable";
            }
            GUI.Label(new Rect(12f, Screen.height - 40f, 1200f, 34f), _guiText, _guiStyle);
        }

        // ------------------------------------------------------------------ the set

        void BuildGround()
        {
            var area = _field;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(_stage, false);
            Unsaved(floor);
            KillComp(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(area.center.x, -0.01f, area.center.z);
            floor.transform.localScale = new Vector3(area.size.x / 10f + 1.2f, 1f,
                                                     area.size.z / 10f + 1.2f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Moto Showroom Floor" };
            mat.SetColor("_BaseColor", new Color(0.21f, 0.22f, 0.24f));
            mat.SetFloat("_Smoothness", 0.12f);
            _mats.Add(mat);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildLight()
        {
            // the ternary, never ?? - AddComponent on a NATIVE component is skipped
            // outright by the coalescing operator, which is how a "Directional Light"
            // once ended up in this project with no Light on it
            var go = new GameObject("Sun");
            go.transform.SetParent(_stage, false);
            Unsaved(go);
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(48f, 200f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.5f);
        }

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(_stage, false);
            Unsaved(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.5f, 0.6f, 0.7f);
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            _cam = camGo.AddComponent<DemoCamera>();
            _cam.pivot = new Vector3(_field.center.x, 0.8f, _field.center.z);
            // far enough back that the widest row fills the frame at 45 degrees, and
            // never closer than a machine's own length
            _cam.distance = Mathf.Max(6f, _field.size.x * 0.75f);
            _cam.yaw = 0f;        // from the south, the way the rank is turned to be read
            _cam.pitch = 24f;
            _cam.mapAt = 10000f;  // there is no map here; the boom must never turn into one
            _cam.showHint = true;
            _cam.hintTopPx = 8f;
            _cam.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "L: print the list   T: turntable";

            // DemoCamera moves itself in LateUpdate, and off Play there is no LateUpdate -
            // it is not ExecuteAlways, deliberately, because a free-look camera that
            // ran in the editor would fight the Scene view for the arrow keys. So the
            // boom is placed by hand here, with the camera's OWN arithmetic (pitch is
            // clamped the way it clamps it), and the moment Play starts it takes the
            // same numbers over and carries on from exactly this shot.
            if (!Application.isPlaying) Aim(camGo.transform, _cam);
        }

        /// <summary>DemoCamera's own placement, done once: the rig on a boom of
        /// <paramref name="rig"/>.distance behind the pivot at its yaw and pitch.</summary>
        static void Aim(Transform tf, DemoCamera rig)
        {
            var rot = Quaternion.Euler(Mathf.Clamp(rig.pitch, 22f, 82f), rig.yaw, 0f);
            tf.SetPositionAndRotation(rig.pivot + rot * new Vector3(0f, 0f, -rig.distance), rot);
        }

        /// <summary>Point the Scene view at the rank - a scene that builds itself is no
        /// use if it builds itself off the edge of the view.</summary>
        public void Frame()
        {
#if UNITY_EDITOR
            var view = UnityEditor.SceneView.lastActiveSceneView;
            if (view == null) return;
            var box = _field;
            box.Expand(1.5f);
            view.Frame(box, false);
#endif
        }

        /// <summary>Put the line up again from scratch - what the menu calls when the
        /// scene has been open a while and something has got out of step.</summary>
        public void RebuildPreview()
        {
            Clear();
            Stand();
        }

        // ------------------------------------------------------------------ housekeeping

        /// <summary>Every switch that changes what is STANDING, as one string. A knob
        /// that only changes how it is drawn is left out on purpose - the rank is
        /// rebuilt when it must be and not on every repaint.</summary>
        Switch Switches() => new Switch
        {
            Motorcycles = motorcycles, Mopeds = mopeds, Quads = quads, Electric = electric, Pedal = pedal,
            PerRow = perRow, Gap = gap, RowGap = rowGap, Yaw = yaw,
            Plaques = plaques, Riders = riders, Pillions = pillions, Spin = spin,
        };

        Bounds FieldBounds()
        {
            var box = new Bounds(Vector3.zero, Vector3.one * 2f);
            bool any = false;
            foreach (var stood in _line)
            {
                if (stood.Tf == null) continue;
                var b = WorldBounds(stood.Tf.gameObject);
                if (any) box.Encapsulate(b);
                else { box = b; any = true; }
            }
            return box;
        }

        /// <summary>Only what is on show. A pack prefab can carry parts switched off -
        /// a sleeping mesh must not widen the bay the waking one stands in.</summary>
        static Bounds WorldBounds(GameObject go)
        {
            var box = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (any) box.Encapsulate(r.bounds);
                else { box = r.bounds; any = true; }
            }
            return box;
        }

        Transform MakeRoot(string name)
        {
            var go = new GameObject(name);
            Unsaved(go);
            return go.transform;
        }

        /// <summary>Flag one of the showroom's own objects so no save can ever pick it
        /// up. A preview that leaks nine motorcycles into a scene file is a worse bug
        /// than an empty scene ever was.</summary>
        static void Unsaved(GameObject go)
        {
            DemoScratch.Unsaved(go);
        }

        void Clear()
        {
            if (_stage) Kill(_stage.gameObject);
            TestBench.DestroyAll(_mats);
            Forget();
        }

        void Forget()
        {
            _line.Clear();
            _stage = null;
            _cam = null;
            _built = default;
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
    }
}
