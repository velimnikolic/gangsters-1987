using System.Collections.Generic;
using System.Text;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoverDemo
{
    // One street, no buildings, and furniture every few paces: the bench the cover
    // code is watched on.
    //
    // Nothing here is new fighting. The crews, the guns, the hit rolls and the
    // ducking are the city's own (DemoCrews / CrewWalker), and the cover they find
    // comes out of the one oracle everything asks - DemoCrews.CoverNear, which walks
    // the standing cars and the props' footprints together. All this scene does is
    // take the block, the traffic, the crowd and the shopfronts away and leave the
    // two things that answer that question: a kerb with cars on it, and a pavement
    // with something to get behind.
    //
    // A prop is cover if it is the SIZE of cover - the plan keeps no height and no
    // name, so the policy is: standing higher than the paving, not a trunk or a post,
    // narrow side at least 22 cm, wide side no more than three metres. The bags below
    // are candidates only; every one is measured (PropFootprint) when the scene opens
    // and the ones that fail are dropped with the reason printed. What is laid down
    // is therefore exactly what a pressed man can use, which is the whole point of
    // the bench.
    //
    // Left-click one of ours to select his crew, right-click a rival's man to send
    // the crew at him, I draws what the combat code can see. Editor only, like the
    // demos it is built from: bodies, clips, guns and props come through the
    // AssetDatabase.
    public class CoverDemoBuilder : MonoBehaviour
    {
        [Header("The street")]
        [Tooltip("How long the street runs, end to end. It is laid on the 5 m tile beat.")]
        public float streetLength = 240f;
        [Tooltip("The bare ground either side of it.")]
        public Color floorColour = new Color(0.20f, 0.20f, 0.21f);
        [Tooltip("Lay the road demo's own dressing as well - palms, lamps, the kerb furniture. " +
                 "Off: bare pavement with nothing on it but what this scene scatters.")]
        public bool kitDressing = true;

        [Header("Furniture")]
        [Tooltip("Props on each kerb strip - the asphalt outside the driving lanes, where " +
                 "a man fighting across the street gets his cover.")]
        [Range(0, 120)] public int kerbProps = 40;
        [Tooltip("Props on each pavement, in two strips with a lane left clear between them " +
                 "so a crew can still walk the street.")]
        [Range(0, 160)] public int walkProps = 56;
        [Tooltip("Heaps left in the carriageway itself - a few props together, so the middle " +
                 "of the road is not bare ground to be caught in.")]
        [Range(0, 20)] public int roadHeaps = 8;
        [Tooltip("Print what every candidate prop measured and whether it counts as cover.")]
        public bool reportProps = true;

        [Header("Wheels")]
        [Tooltip("Cars stood at both kerbs. Nothing drives here - they are the other half " +
                 "of the cover oracle, and the thing to compare a bin against.")]
        [Range(0, 40)] public int parkedCars = 12;

        [Header("The mobs")]
        [Tooltip("Rival crews strung down the street, each on the pavement opposite the last.")]
        [Range(1, 4)] public int rivalCrews = 2;
        [Range(0, 6)] public int rivalHoods = 3;
        [Tooltip("Where the first rival stands, in metres east of the middle.")]
        public float rivalFrom = 0f;
        [Tooltip("Metres of street between one rival crew and the next.")]
        public float rivalSpacing = 36f;
        [Tooltip("Metres between neighbouring crews on the outfit's line.")]
        public float crewSpread = 11f;
        public int nameSeed = 1987;

        [Header("The outfit")]
        [Tooltip("How many crews of five the outfit takes the street with. The books' own " +
                 "starting roster is ONE crew of three (a lieutenant and two hoods), which " +
                 "is not a gang - it is a man and his two friends walking into eight. This " +
                 "swaps in the ledger's scale roster (F2 in the book), ten men to the crew " +
                 "of which five are crewed. 0: leave the starting three alone.")]
        [Range(0, 5)] public int outfitCrews = 3;

        [Header("Arms")]
        [Tooltip("Arm the outfit out of the armory when the scene opens - one gun per man. " +
                 "Off: every man carries the .38 in his coat.")]
        public bool armTheOutfit = true;
        [Tooltip("The gun the outfit opens with. Vehicle is not a gun and is ignored.")]
        public EquipmentKind outfitArms = EquipmentKind.Rifle;

        [Header("Mission (headless)")]
        [Tooltip("Send the outfit at the rivals this many seconds into Play, with no click. " +
                 "0: only by hand. The block's own driver runs it - it reads DemoCrews and " +
                 "nothing else, so it drives this street just as well.")]
        [Min(0)] public float missionAfter = 0f;
        [Tooltip("How near the mark a crew has to be before it is told to open up.")]
        [Min(5)] public float missionEngageWithin = 30f;

        [Header("Watch")]
        [Tooltip("Draw what the cover code sees: the boxes it counts as cover, and a line " +
                 "from every man to whatever he is behind. I toggles it in Play.")]
        public bool coverLines = true;

        // ------------------------------------------------------------ the ground plan
        //
        // The street runs along X on the middle of the world, and everything is measured
        // off its centre line. The profile is the road demo's own: two 5 m lanes, a 2.5 m
        // kerb strip either side of them, then 6.5 m of pavement.

        const float Walk = StreetKit.SidewalkWidth;   // 6.5 m of pavement
        const float Half = StreetKit.StreetHalf;      // 7.5 m of asphalt each side of the crown
        const float Lane = StreetKit.RoadHalf;        // 5 m of it is the marked lane
        const float Cell = StreetKit.Cell;            // the 5 m tile
        const float RoadY = -0.08f;                   // the asphalt, which is sunk a tenth
        const float WalkY = 0f;                       // the pavement top, where the men stand

        float StreetXMin => -Mathf.Round(streetLength * 0.5f / Cell) * Cell;
        float StreetXMax => Mathf.Round(streetLength * 0.5f / Cell) * Cell;

        DemoCrews _crews;
        StreetKit _kit;
        CoverWatch _watch;
        Vector3 _muster;

        void Awake()
        {
#if UNITY_EDITOR
            BuildFloor();
            BuildStreet();
            // the town's fence, the same rule every scene lays (the city's FenceCity,
            // CrewDemo's rect): the SET is the street and its pavements - the bare
            // slabs round it are backdrop, and nobody strolls, flees or is stood out
            // on them (WalkObstacles.InCity holds at the step, the spot and the route)
            WalkObstacles.City.Add(Rect.MinMaxRect(StreetXMin, -StreetKit.OuterHalf,
                                                   StreetXMax, StreetKit.OuterHalf));
            BuildLight();
            BuildCamera();
            // the arena's clock (Space holds it, , and . step it): watching a man duck
            // is watching a second and a half of a fight, and the Editor's own pause
            // stops the camera with it
            gameObject.AddComponent<CrewDemo.CrewDemoPace>();

            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null)
                Debug.LogWarning("[CoverDemo] Walk/idle clips missing under Assets/Animations/People - " +
                                 "the men will slide.");

            _crews = gameObject.AddComponent<DemoCrews>();
            _crews.EveryoneArmed = true;
            _crews.MuzzleFlashPrefab = CrewKit.MuzzleFlash;
            _crews.BloodPrefab = CrewKit.Blood;
            _crews.ImpactPrefab = CrewKit.Impact;
            _crews.GunshotSets = CrewKit.GunshotSets();
            _crews.CrackClip = CrewKit.Crack;
            _crews.Net = BuildRoadNet();
            LaneNet.Active = _crews.Net;
            _crews.CarRoadY = RoadY;
            _crews.StreetZ = 0f;

            // The outfit musters at the west end, on the south pavement, well outside
            // anybody's alert range until it is sent. IT FACES ACROSS THE STREET, not
            // down it: DemoCrews strings the crews along the anchor's RIGHT
            // (OutfitSpawnPoint), so a line facing down the street is a line laid
            // across it - the second crew in the carriageway and the third on the far
            // pavement. Facing north puts the row along the kerb, where a crew stands.
            _muster = new Vector3(StreetXMin + 34f, WalkY, -(Half + Walk * 0.45f));
            _crews.InitFree(clips, null, anchor: _muster, facing: Vector3.forward,
                spread: crewSpread, groundY: WalkY);

            // the cars go down first: they claim their ground in the plan as a
            // reservation, so nothing is scattered into a car - and a reservation is
            // skipped by the cover query, which knows cars by another road entirely
            // (StreetTraffic.Users), so no flank is offered twice
            BuildParkedCars();
            ReserveMusters();
            Furnish();
            SpawnRivals();

            _watch = gameObject.AddComponent<CoverWatch>();
            _crews.IntentOverlay.SetVisible(coverLines);
            _watch.Init(_crews, _camera);

            if (missionAfter > 0f)
            {
                var mission = gameObject.AddComponent<BlockDemo.BlockDemoMission>();
                mission.startAfter = missionAfter;
                mission.onFoot = true;
                mission.engageWithin = missionEngageWithin;
            }
#else
            Debug.LogError("[CoverDemo] This demo loads Synty prefabs through the AssetDatabase " +
                           "and only runs in the editor.");
#endif
        }

        void Update()
        {
            // the scale roster FIRST: it replaces the roster whole, and guns issued into
            // the old one would go with it
            ScaleTheOutfit();
            ArmTheOutfit();
        }

        // The outfit the player is given. The books deal a starting six - one crew of
        // three of them - which is the campaign's opening hand and is right there; on
        // this street it is three men against two mobs of four, over before it starts.
        // So the bench asks for the ledger's own scale roster instead (the same call F2
        // makes in the almanac, deterministic off the same seed), and the player gets
        // crews enough to send one round the far side while another holds the kerb.
        // Once, when the roster is in.
        bool _scaled;

        void ScaleTheOutfit()
        {
            if (_scaled || outfitCrews <= 0) return;
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return;
            _scaled = true;
            if (director.Roster.Crews.Count >= outfitCrews) return;
            director.DebugSeedLarge(outfitCrews * 10);
        }

        // ------------------------------------------------------------------- the set

        void BuildFloor()
        {
            var mat = FloorMaterial();
            float halfX = Mathf.Max(StreetXMax + 40f, 80f);
            float halfZ = StreetKit.OuterHalf + 60f;
            // the street corridor carries its own tiles and is left out; the ground is
            // the four pieces round it
            Slab(-halfX, StreetKit.OuterHalf, halfX, halfZ, mat);
            Slab(-halfX, -halfZ, halfX, -StreetKit.OuterHalf, mat);
            Slab(-halfX, -StreetKit.OuterHalf, StreetXMin, StreetKit.OuterHalf, mat);
            Slab(StreetXMax, -StreetKit.OuterHalf, halfX, StreetKit.OuterHalf, mat);
        }

        Material FloorMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return null;
            var mat = new Material(shader) { name = "Cover Demo Floor" };
            mat.SetColor("_BaseColor", floorColour);
            mat.SetFloat("_Smoothness", 0.12f);
            return mat;
        }

        void Slab(float xFrom, float zFrom, float xTo, float zTo, Material mat)
        {
            if (xTo <= xFrom || zTo <= zFrom) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3((xFrom + xTo) * 0.5f, 0f, (zFrom + zTo) * 0.5f);
            floor.transform.localScale = new Vector3((xTo - xFrom) / 10f, 1f, (zTo - zFrom) / 10f);
            if (mat) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildStreet()
        {
#if UNITY_EDITOR
            var root = new GameObject("Streetscape").transform;
            _kit = new StreetKit(root, y: -0.1f);
            if (!_kit.Load()) return;
            _kit.LayAlongX(0f, StreetXMin, StreetXMax, true, true, kitDressing);
#endif
        }

        // A street a car may drive from end to end, so the ledger's car has somewhere
        // to stand and the crews' own driving still works here. Two dead ends and one
        // carriageway between them - there is no junction on this street to take.
        LaneNet BuildRoadNet()
        {
            var net = new LaneNet();
            var west = net.AddNode(StreetXMin + 1f, 0f, 0.5f, 0.5f, stopSetback: 0.5f);
            var east = net.AddNode(StreetXMax - 1f, 0f, 0.5f, 0.5f, stopSetback: 0.5f);
            net.AddRoad(new Vector3(west.XMax, 0f, 0f), new Vector3(east.XMin, 0f, 0f),
                Half, new[] { 2.5f }, 10f, west, east, false);
            net.Finish();
            return net;
        }

        // ---------------------------------------------------------------- the props
        //
        // Two bags, because the street reads wrong otherwise: what is left standing on
        // the asphalt is a works' rubbish - barrels, pallets, crates, a dumpster - and
        // what stands on the pavement is the city's own - bins, planters, a cabinet, a
        // bench, a phone. They are candidates only; the measuring below decides.

        static readonly string[] KerbCandidates =
        {
            "SM_Prop_Dumpster_01", "SM_Prop_Barrel_Metal_01", "SM_Prop_Barrel_Plastic_01",
            "SM_Prop_Barrel_Plastic_02", "SM_Prop_Barrel_Plastic_03", "SM_Prop_Barrel_Burn_01",
            "SM_Prop_CardboardBox_Stack_01", "SM_Prop_CardboardBox_Stack_02", "SM_Prop_Box_01",
            "SM_Prop_Crate_01", "SM_Prop_Pallet_01", "SM_Prop_Woodstack_01", "SM_Prop_PipeStack_01",
            "SM_Prop_Barrier_01", "SM_Prop_Barrier_02", "SM_Prop_Barrier_03", "SM_Prop_Barrier_04",
        };

        static readonly string[] WalkCandidates =
        {
            "SM_Prop_Trash_Bin_01", "SM_Prop_Trash_Bin_02", "SM_Prop_Trash_Bin_03",
            "SM_Prop_Trash_Bin_04", "SM_Prop_Planter_01", "SM_Prop_Planter_02",
            "SM_Prop_Planter_03", "SM_Prop_Planter_04", "SM_Prop_Powerbox_01",
            "SM_Prop_PowerBox_02", "SM_Prop_PowerBox_03", "SM_Prop_PowerBoxes_01",
            "SM_Prop_Bench_Seat_01", "SM_Prop_Bench_Seat_02", "SM_Prop_Newspaper_Stand_01",
            "SM_Prop_Pay_Phone_01", "SM_Prop_ATM_01", "SM_Prop_Mailbox_01",
            "SM_Prop_Crate_01", "SM_Prop_CardboardBox_Stack_01",
        };

        readonly List<GameObject> _kerbBag = new List<GameObject>();
        readonly List<GameObject> _walkBag = new List<GameObject>();

        void Furnish()
        {
#if UNITY_EDITOR
            if (_kit == null) return;
            var report = reportProps ? new StringBuilder("[CoverDemo] what the cover oracle can use:\n") : null;
            Gather(KerbCandidates, _kerbBag, report);
            Gather(WalkCandidates, _walkBag, report);
            if (report != null) Debug.Log(report.ToString());
            if (_kerbBag.Count == 0 && _walkBag.Count == 0)
            {
                Debug.LogWarning("[CoverDemo] no prop prefab measured up as cover - the street stays bare.");
                return;
            }

            var root = new GameObject("Cover Props").transform;
            int laid = 0;
            foreach (int side in new[] { -1, 1 })
            {
                // the kerb strip: between the marked lane and the kerb, where the parked
                // cars are, at the asphalt's own level
                laid += Scatter(root, _kerbBag, kerbProps, side, Lane + 0.5f, Half - 0.4f, RoadY, park: true);
                // the pavement, in two strips with a lane left clear between them: props
                // wall to wall would be a street nobody can walk down, and a crew that
                // cannot reach the fight never gets behind anything either
                laid += Scatter(root, _walkBag, Mathf.RoundToInt(walkProps * 0.55f), side,
                    Half + 0.8f, Half + 2.3f, WalkY);
                laid += Scatter(root, _walkBag, walkProps - Mathf.RoundToInt(walkProps * 0.55f), side,
                    Half + 4.4f, Half + Walk - 0.5f, WalkY);
            }
            laid += Heaps(root);
            Debug.Log("[CoverDemo] " + laid + " props laid on " + streetLength + " m of street - " +
                      "one every " + (laid > 0 ? (streetLength / laid).ToString("0.0") : "-") + " m.");
#endif
        }

        // Every candidate measured the way the walkers and the cover query will measure
        // it, and kept only if it is cover. A prop that fails is named with its numbers:
        // that line is the answer to "why does nobody hide behind the bollards".
        void Gather(string[] names, List<GameObject> into, StringBuilder report)
        {
#if UNITY_EDITOR
            foreach (var name in names)
            {
                var prefab = Find(name);
                if (prefab == null)
                {
                    report?.Append("  - ").Append(name).Append(": no prefab in any pack\n");
                    continue;
                }
                var foot = PropFootprint.Of(prefab);
                string verdict = Verdict(foot);
                if (verdict == null) into.Add(prefab);
                report?.Append(verdict == null ? "  + " : "  - ").Append(name)
                       .Append(": ").Append((foot.Half.x * 2f).ToString("0.00")).Append(" x ")
                       .Append((foot.Half.y * 2f).ToString("0.00")).Append(" m")
                       .Append(verdict == null ? " - cover" : " - " + verdict).Append('\n');
            }
#endif
        }

        /// <summary>Why this footprint is not cover, or null when it is. DemoCrews'
        /// own test, asked here so the bench can drop what it cannot use rather than
        /// stand it on the pavement to be walked past.</summary>
        static string Verdict(PropFootprint.Foot foot)
        {
            if (!foot.Known) return "no mesh to measure";
            if (!foot.Solid) return "flat - paving, not an obstacle";
            if (foot.Tall) return "a trunk or a post: the box is knee height and the thing goes on up";
            if (Mathf.Min(foot.Half.x, foot.Half.y) < DemoCrews.PropCoverMinHalf) return "too slim on its short side";
            if (Mathf.Max(foot.Half.x, foot.Half.y) > DemoCrews.PropCoverMaxHalf) return "too wide - a wall, not furniture";
            return null;
        }

        /// <summary>Props strewn down a strip of ground, one at a time, each tried a few
        /// places before it is given up on: the plan says whether the ground is free
        /// (props, the dressing's own furniture, the reserved car bays), and taking the
        /// box is what puts the prop into the walkers' way and into the cover query at
        /// once - the kit entered its plan in WalkObstacles when it was made.</summary>
        int Scatter(Transform root, List<GameObject> bag, int count, int side, float zNear, float zFar, float y,
            bool park = false)
        {
#if UNITY_EDITOR
            if (bag.Count == 0 || count <= 0) return 0;
            var plan = _kit.Plan;
            int laid = 0;
            for (int i = 0; i < count; i++)
            {
                var prefab = bag[Random.Range(0, bag.Count)];
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float x = Random.Range(StreetXMin + 6f, StreetXMax - 6f);
                    float z = side * Random.Range(zNear, zFar);
                    // boxy things sit square with the kerb far more often than not, with
                    // a few shoved round: a street of props at random angles reads as a
                    // spill, not as furniture
                    float yaw = Random.value < 0.7f
                        ? Mathf.Round(Random.Range(0, 4)) * 90f + Random.Range(-7f, 7f)
                        : Random.Range(0f, 360f);
                    var at = new Vector3(x, y, z);
                    if (!SidewalkPlan.Footprint(prefab, at, yaw, out var box)) break;
                    if (!plan.Free(box, 0.25f)) continue;
                    var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), root);
                    go.name = go.name.Replace("(Clone)", "");
                    plan.Take(box);
                    // a prop on the carriageway edge is not just cover for men on foot -
                    // it is a body in a driven car's lane, so it goes among the road's
                    // users too and the traffic plans round it (StoodCar), the same as a
                    // parked car. Off on the far pavement it is left out: no car goes there.
                    if (park) StoodCar.Park(go);
                    laid++;
                    break;
                }
            }
            return laid;
#else
            return 0;
#endif
        }

        /// <summary>A few heaps left in the carriageway itself. The pavements give a man
        /// cover where he already is; these are what he can make for when he is caught
        /// crossing, which is where the walk-to-cover half of the trade shows.</summary>
        int Heaps(Transform root)
        {
#if UNITY_EDITOR
            if (_kerbBag.Count == 0 || roadHeaps <= 0) return 0;
            var plan = _kit.Plan;
            int laid = 0;
            for (int h = 0; h < roadHeaps; h++)
            {
                var centre = new Vector3(Random.Range(StreetXMin + 12f, StreetXMax - 12f), RoadY,
                                         Random.Range(-Lane + 0.8f, Lane - 0.8f));
                int n = Random.Range(2, 5);
                for (int i = 0; i < n; i++)
                {
                    var prefab = _kerbBag[Random.Range(0, _kerbBag.Count)];
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        var at = centre + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-1.6f, 1.6f));
                        at.z = Mathf.Clamp(at.z, -Lane + 0.6f, Lane - 0.6f);
                        float yaw = Random.Range(0f, 360f);
                        if (!SidewalkPlan.Footprint(prefab, at, yaw, out var box)) break;
                        if (!plan.Free(box, 0.2f)) continue;
                        var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), root);
                        go.name = go.name.Replace("(Clone)", "");
                        plan.Take(box);
                        // a heap left in the carriageway is squarely in a driven car's
                        // path: put it among the road's users so the traffic goes round
                        // it instead of through it (StoodCar).
                        StoodCar.Park(go);
                        laid++;
                        break;
                    }
                }
            }
            return laid;
#else
            return 0;
#endif
        }

        // Cars stood at both kerbs. Nothing drives this street, so they are geometry -
        // but geometry the cover oracle knows by name: StoodCar puts them among the
        // road's users, which is the branch of CoverNear that came first and the one a
        // prop is worth comparing against.
        void BuildParkedCars()
        {
#if UNITY_EDITOR
            if (parkedCars <= 0) return;
            var bodies = TrafficBodies();
            if (bodies.Count == 0)
            {
                Debug.LogWarning("[CoverDemo] no pack cars found - the kerbs stay empty.");
                return;
            }
            var root = new GameObject("Parked Cars").transform;
            // the stretch of kerb a car may be left on, both sides. KerbCars measures
            // every body: each stands at the kerb by its own width - the pack's wide
            // ones used to park half on the pavement - and claims its own length of it
            var spans = new[] { new Vector2(StreetXMin + 15f, StreetXMax - 15f) };
            // unpainted, unlike the crew lab's row: nothing drives this street, so the
            // cars have no repainted traffic to stand out against, and the cover oracle
            // is read off screenshots where the pack's own colours tell the bodies apart
            var stood = KerbCars.Park(root, bodies, parkedCars, alongX: true, centre: 0f,
                halfRoad: Half, roadY: RoadY, spans: spans, paint: false);
            foreach (var car in stood)
            {
                // the bay is spoken for, so no barrel is scattered inside a car.
                // A RESERVATION, not a prop: it refuses props and offers no flank, which
                // is right - the car already offers its own through StreetTraffic.Users,
                // and one body must not be two pieces of cover.
                if (_kit == null) continue;
                var bounds = car.Box;
                _kit.Plan.Reserve(new Vector3(bounds.center.x, 0f, bounds.center.z), 0f,
                    new Vector2(bounds.extents.x + 0.3f, bounds.extents.z + 0.3f));
            }
#endif
        }

        static readonly string[] VehicleFolders =
        {
            "Assets/Synty/PolygonCity/Prefabs/Vehicles",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles",
        };

        List<GameObject> TrafficBodies()
        {
            var bodies = new List<GameObject>();
#if UNITY_EDITOR
            foreach (var name in TestBench.StreetCars)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab " + name, VehicleFolders);
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go) { bodies.Add(go); break; }
                }
            }
#endif
            return bodies;
        }

        /// <summary>The pack prefab of this name, wherever in the packs it lives. The
        /// props here are drawn from several - the gang pack's yard rubbish, the palm
        /// city's street furniture - so the lookup is by name, not by folder.</summary>
        static GameObject Find(string name)
        {
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab " + name))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go) return go;
            }
#endif
            return null;
        }

        // ------------------------------------------------------------------ the sky

        void BuildLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
        }

        DemoCamera _camera;
        readonly List<CivilianAgent> _noWalkers = new List<CivilianAgent>();
        const string _hint =
            "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
            "left-click one of ours: select his crew   right-click the road: walk there   " +
            "right-click a rival: attack   I: combat indicators   Space: hold   P: ledger";

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 900f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            _camera = camGo.AddComponent<DemoCamera>();
            gameObject.AddComponent<DemoAudio>().Init(null, _camera, null, null, _noWalkers);
            // opens looking down the street from the west end, with the outfit in the
            // near corner and the first rival away up it
            _camera.pivot = new Vector3((StreetXMin + 34f + rivalFrom) * 0.5f, 0f, 0f);
            _camera.distance = 96f;
            _camera.yaw = 78f;
            _camera.pitch = 42f;
            _camera.hintTopPx = 104f;   // under the crew bar
            _camera.showHint = true;
            _camera.hint = _hint;
        }

        // ------------------------------------------------------------------ the mobs

        static readonly (string, EquipmentKind)[] RivalArms =
        {
            ("SM_Wep_Machine_Pistol_01", EquipmentKind.MachinePistol),
            ("SM_Wep_Shotgun_01", EquipmentKind.Shotgun),
            ("SM_Wep_SubMachineGun_01", EquipmentKind.TommyGun),
            ("SM_Wep_Pistol_Revolver_01", EquipmentKind.Pistol),
        };

        // one crew per pavement, turn and turn about, so the fight is fought ACROSS
        // the street: a man shot at from the far pavement has the kerb strip, the
        // parked cars and his own pavement's furniture between him and the shooter,
        // which is the arrangement the cover code was written for and the one a block
        // never quite produces
        void SpawnRivals()
        {
            TestBench.SpawnRivals(_crews, nameSeed, rivalCrews, rivalHoods, RivalArms,
                i => { var at = RivalAnchor(i, out var facing); return (at, facing); }, "[CoverDemo]");
        }

        /// <summary>Where a rival crew stands and which way it looks: one per pavement,
        /// turn and turn about, back to where a wall would be. Asked twice - once to
        /// keep the ground clear of props, once to put the men on it - so the two can
        /// never drift apart.</summary>
        Vector3 RivalAnchor(int i, out Vector3 facing)
        {
            int side = i % 2 == 0 ? +1 : -1;
            facing = side > 0 ? Vector3.back : Vector3.forward;
            return new Vector3(rivalFrom + i * rivalSpacing, WalkY, side * (Half + Walk * 0.55f));
        }

        /// <summary>The ground the men are dealt onto, spoken for before a single prop
        /// is scattered. A crew is dealt at a POINT, not walked to one, so a barrel
        /// standing there is a man standing in a barrel - and the outfit's row is not
        /// even measured yet when the street is furnished (the roster arrives a frame
        /// or two later), so the reservation covers the whole line a big roster could
        /// use rather than the one this one will.</summary>
        void ReserveMusters()
        {
            if (_kit == null) return;
            // Five crews wide (crewSpread apart) and deep enough for the hoods, who fall
            // in BEHIND their boss - FormationOffset puts the second rank 3 m back, and
            // the boss faces the street, so "behind" is the back of the pavement. Half
            // the depth and the far rank stands in the row of planters.
            _kit.Plan.Reserve(new Vector3(_muster.x, 0f, _muster.z - 1.5f), 0f, new Vector2(26f, 3.8f));
            int count = Mathf.Clamp(rivalCrews, 1, 4);
            for (int i = 0; i < count; i++)
                _kit.Plan.Reserve(RivalAnchor(i, out _), 0f, new Vector2(9f, 2.2f));
        }

        // The outfit opens with long guns rather than the .38 in the coat, the same way
        // the crew demo's bench does it (TestBench.ArmTheOutfit). Once, when the roster
        // is in (the outfit is dealt off it a frame or two later).
        bool _armsGiven;

        void ArmTheOutfit()
        {
            if (_armsGiven || !armTheOutfit) return;
            _armsGiven = TestBench.ArmTheOutfit(outfitArms, "[CoverDemo]");
        }
    }
}
