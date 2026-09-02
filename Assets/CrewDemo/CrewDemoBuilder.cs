using System.Collections.Generic;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CrewDemo
{
    // A whole city block and the four streets round it: the block itself is a
    // catalogue bake - one of the rolled blocks composed on the lot pad of this
    // size (Assets/CityKit/Blocks) - ringed by the road demo's own street profile,
    // pavements and dressing, with a crossroads at each of its four corners and the
    // streets running on past them. The outfit's crews come out of the ledger and
    // muster on the far pavement of the south street; a rival lieutenant and his
    // hoods hold a frontage of the block each - the south face, then the east, the
    // north, the west - so a crew sent at the second rival has to go round a corner
    // to reach him, and neither rival can hear the other's fight.
    //
    // Left-click one of ours to select his crew, right-click the floor to walk him
    // there (his hoods fall in behind), right-click a rival's man to send the crew
    // at that rival - and the two crews shoot it out until one is down. The rivals
    // answer fire on their own, and open up on any crew that walks up to them.
    //
    // Built at Play from this one component, the road demo's way: ground, light,
    // camera, and the crews out of the same DemoCrews/CrewWalker classes the city
    // uses - so what is proven here on one block is what the city gets. Editor
    // only, like that demo: the bodies, clips and guns come through the AssetDatabase.
    public class CrewDemoBuilder : MonoBehaviour
    {
        [Header("Floor")]
        [Tooltip("The bare ground the block and its streets stand on. Grown to fit them if it is set too small.")]
        public float floorSize = 260f;
        public Color floorColour = new Color(0.20f, 0.20f, 0.21f);

        [Header("The block")]
        [Tooltip("The block's frontage along X. A catalogue lot pad width (70, 85 or 100 m), so a block composed for that pad can stand on it.")]
        public float blockWidth = 100f;
        [Tooltip("Its depth along Z. A catalogue lot pad depth (50, 70 or 95 m).")]
        public float blockDepth = 70f;
        [Tooltip("The bake that fills it, by name (Assets/CityKit/Blocks). Empty: one of the blocks rolled for this pad size, a different one every Play.")]
        public string blockBake = "";

        [Header("Streets")]
        [Tooltip("How far the four streets run on past the block's corners.")]
        public float streetReach = 35f;

        [System.Serializable]
        public struct FacadeOverride
        {
            [Tooltip("Prefab name, e.g. building-coffeeshop.")] public string building;
            [Tooltip("Which local side of the prefab its shopfront is on.")] public FacadeFinder.Side front;
        }
        [Tooltip("When the mesh reading gets a building's front wrong, say so here by name.")]
        public FacadeOverride[] facadeOverrides = System.Array.Empty<FacadeOverride>();

        [Header("Wheels")]
        [Tooltip("The outfit's car, parked at the kerb by the crews. Empty: a different car a gangster would drive every time the scene starts (GangsterCars).")]
        public string crimeCar = "";
        [Tooltip("Repaint a police-pack body flat black (those come in livery).")]
        public bool blackSedan = true;

        /// <summary>The wheels a mob drives - the approved pair
        /// (LivingCity.Gameplay.VehicleCatalog.GangsterCars). One is drawn at random
        /// each Play.</summary>
        static string[] GangsterCars => LivingCity.Gameplay.VehicleCatalog.GangsterCars;
        [Tooltip("Pack cars on each of the two streets along the block's long sides, both ways, for the outfit's car to thread through.")]
        [Range(0, 30)] public int trafficCars = 2;
        [Tooltip("Cars stood at the kerb of the two side streets - nothing drives those; they are there to be walked round and shot over.")]
        [Range(0, 20)] public int parkedCars = 6;
        [Tooltip("Two-wheelers in the traffic, each with a rider (and sometimes a mate behind him) posed onto the machine by BikePose.")]
        [Range(0, 12)] public int trafficBikes = 2;
        [Tooltip("Bikes left on their stands at the kerbs - nobody on them, the traffic goes round them.")]
        [Range(0, 12)] public int parkedBikes = 2;
        [Tooltip("A motorcycle of the outfit's, stood by the muster with two hoods on it. Press B in Play to send it on a drive-by at the first rival; press it again to call it off.")]
        public bool outfitBike = true;
        [Tooltip("The outfit's machine by name. Empty: the first of VehicleCatalog.Motorcycles the packs actually have.")]
        public string outfitBikeBody = "";
        [Tooltip("Only used when headlessAutomation is on: send the bike at the first rival this many seconds into Play, with no key pressed. 0: only on B.")]
        [Min(0)] public float bikeAttackAfter = 0f;

        [Header("Manual test mode")]
        [Tooltip("Optional Space/,/. time controls. Off by default so pressing Play is always normal-speed manual testing.")]
        public bool paceHotkeys = false;

        [Header("Headless automation")]
        [Tooltip("Off for manual testing. When true, missionAfter and bikeAttackAfter may issue automatic commands for unattended harness runs.")]
        public bool headlessAutomation = false;

        [Tooltip("Only used when headlessAutomation is on: send the outfit at the rivals this many seconds into Play, with no click. " +
                 "0: only by hand. The lab's own driver is BlockDemoMission, which wants " +
                 "nothing of the block - it reads DemoCrews and nothing else - so it drives " +
                 "this scene just as well, and every crew it is given goes at whatever mob " +
                 "is still standing until none are.")]
        [Min(0)] public float missionAfter = 0f;

        [Tooltip("Straight out on foot, no car at all.")]
        public bool missionOnFoot = true;

        [Tooltip("THE CAR BOMB instead of the fight: the crew walks up to a car belonging " +
                 "to a rival, lays a charge under it, walks clear, and the rival is then " +
                 "sent for his own car - he gets in, drives off, and it blows under him. " +
                 "Overrides the on-foot mission.")]
        public bool missionCarBomb;
        [Tooltip("Car bomb: metres the crew walks away from the charge before the rival is " +
                 "sent for his car. Must clear the blast (6 m) and the range a rival opens " +
                 "fire at (24 m).")]
        [Min(10f)] public float missionCarBombClearBy = 45f;
        [Tooltip("Car bomb: seconds any one leg may take - the walk up, the walk clear, " +
                 "the rival's walk to his car, the drive - before the run fails.")]
        [Min(10f)] public float missionCarBombPatience = 90f;
        [Tooltip("Car bomb: seconds to let the rest of the rival's crew climb in after the " +
                 "first man is seated, before the car is driven off.")]
        [Min(0f)] public float missionCarBombSettle = 8f;
        [Tooltip("Car bomb: swing the camera onto the car so a headless --shot frames the " +
                 "blast instead of the whole block.")]
        public bool missionCarBombShot;

        [Tooltip("Open with a drive-by on the machine, then finish on foot.")]
        public bool missionMoto = false;

        [Tooltip("Passes ridden when the drive-by opens it.")]
        [Range(1, 6)] public int missionPassesRidden = 1;

        [Tooltip("Seconds of car passes before the crew is put down and sent in on foot. " +
                 "Only read when missionOnFoot is off.")]
        [Min(0)] public float missionPasses = 45f;

        [Tooltip("How near the mark a crew has to be before it is told to open up.")]
        [Min(5)] public float missionEngageWithin = 30f;

        [Header("Arms")]
        [Tooltip("Arm the outfit out of the armory when the scene opens - one gun per man into each crew's deck. Off: every man carries the .38 in his coat.")]
        public bool armTheOutfit = true;
        [Tooltip("The gun the outfit opens with. Vehicle is not a gun and is ignored.")]
        public EquipmentKind outfitArms = EquipmentKind.Rifle;

        [Header("Street life")]
        [Tooltip("Passers-by on the pavements - the crowd that runs when the shooting starts.")]
        [Range(0, 120)] public int pedestrians = 36;
        [Tooltip("A police cruiser parked at the far end of the south street, two officers in it, that answers the shooting. Off for now: the crews settle it between themselves.")]
        public bool policeCruiser = false;
        [Tooltip("Empty: the marked cruiser out of VehicleCatalog.PoliceCars.")]
        public string policeCar = "";
        [Tooltip("Beat officers walking the pavements, posted at a shopfront on the block. " +
                 "They are the law that is simply THERE when the guns go off: the nearest " +
                 "one walks to the shooting and holds the scene, with or without a cruiser.")]
        [Range(0, 6)] public int policeBeat = 2;

        [Header("Layout")]
        [Tooltip("Metres between neighbouring crews on the outfit's line.")]
        public float crewSpread = 11f;
        [Tooltip("How many of the block's four frontages a rival crew holds: the south face first, then the east, the north, the west.")]
        [Range(1, 4)] public int rivalCrews = 2;
        [Range(0, 6)] public int rivalHoods = 3;
        public int nameSeed = 1987;

        [Header("Grip (live - drag in Play to seat the gun)")]
        [Tooltip("Metres from the wrist along the fingers / off the palm / toward the thumb.")]
        public Vector3 gripNudge = new Vector3(0.075f, -0.015f, 0.01f);
        [Tooltip("Degrees of extra turn on the gun in the fist.")]
        public Vector3 gripTilt = Vector3.zero;

        DemoCrews _crews;
        Transform _blockRoot;
        Vector3 _seenNudge, _seenTilt;
        const float NormalFixedDeltaTime = 0.02f;

        // ------------------------------------------------------------ the ground plan
        //
        // The block sits on the origin; each of the four streets is laid off one of
        // its faces, a pavement and half a carriageway out, and each pair meets at a
        // crossroads on the block's corner. Everything else here is measured off these.

        const float Walk = StreetKit.SidewalkWidth;   // 6.5 m of pavement each side
        const float Half = StreetKit.StreetHalf;      // 5 m of carriageway each side of the centre line
        const float Cell = StreetKit.Cell;            // the 5 m tile

        float BlockXMin => -blockWidth * 0.5f;
        float BlockXMax => blockWidth * 0.5f;
        float BlockZMin => -blockDepth * 0.5f;
        float BlockZMax => blockDepth * 0.5f;

        float WestX => BlockXMin - Walk - Half;       // the four centre lines
        float EastX => BlockXMax + Walk + Half;
        float SouthZ => BlockZMin - Walk - Half;
        float NorthZ => BlockZMax + Walk + Half;

        float StreetXMin => WestX - Half - streetReach;   // where the streets stop
        float StreetXMax => EastX + Half + streetReach;
        float StreetZMin => SouthZ - Half - streetReach;
        float StreetZMax => NorthZ + Half + streetReach;

        /// <summary>Half the ground the set needs: the streets and their far
        /// pavements, with a little bare floor beyond.</summary>
        float FloorHalf => Mathf.Max(floorSize * 0.5f, StreetXMax + Walk + 15f, StreetZMax + Walk + 15f);

        void Awake()
        {
#if UNITY_EDITOR
            ResetManualClock();
            CrewArms.GripNudge = _seenNudge = gripNudge;
            CrewArms.GripTilt = _seenTilt = gripTilt;

            BuildFloor();
            BuildStreets();
            // the town's fence: this set is the block, its four streets and their
            // pavements - the bare floor beyond is the wilderness, and nobody who
            // picks his own walk (CrewWalker.TryRoam) picks a spot out on it
            WalkObstacles.City.Add(Rect.MinMaxRect(StreetXMin - Walk, StreetZMin - Walk,
                                                   StreetXMax + Walk, StreetZMax + Walk));
            BuildBlock();
            BuildLight();
            BuildCamera();
            if (paceHotkeys)
                gameObject.AddComponent<CrewDemoPace>();

            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null)
                Debug.LogWarning("[CrewDemo] Walk/idle clips missing under Assets/Animations/People - " +
                                 "the men will slide.");

            _crews = gameObject.AddComponent<DemoCrews>();
            _crews.EveryoneArmed = true;
            _crews.MuzzleFlashPrefab = CrewKit.MuzzleFlash;
            _crews.GunSmokePrefab = CrewKit.GunSmoke;
            _crews.BloodPrefab = CrewKit.Blood;
            _crews.ImpactPrefab = CrewKit.Impact;
            _crews.GunshotSets = CrewKit.GunshotSets();
            _crews.CrackClip = CrewKit.Crack;
            // the four streets as a lane network, so a car can take a corner: the
            // ledger's cars are given it, and each starts on whichever street it is
            // parked on; it is the scene's active net for whatever else is stood on it
            _crews.Net = BuildRoadNet();
            LaneNet.Active = _crews.Net;
            // this street is sunk a tenth: a car the ledger stands here sits on the
            // asphalt with the rest, not on the pavement's level
            _crews.CarRoadY = -0.08f;
            // the outfit gathers on the far pavement of the south street, on the stretch
            // west of the crossroads - the crews strung along it either side of the
            // anchor, facing the block. Seventy-odd metres of street from the nearest
            // rival: a walk, and well outside any rival's alert range until it is sent
            var muster = new Vector3((StreetXMin + WestX - Half - Walk) * 0.5f, 0f,
                                     SouthZ - Half - Walk * 0.35f);
            _crews.StreetZ = SouthZ; // the lane a drive-by keeps to
            _crews.InitFree(clips, null, anchor: muster, facing: Vector3.forward,
                spread: crewSpread, groundY: 0f);

            SpawnRivals();
            ParkCar(muster);
            BuildTraffic(clips);
            BuildParkedCars();
            BuildBikes(clips);
            BuildPavementLife(clips);
            BuildPolice(clips);

            // The mission, for a run nobody is sat at. Everything it needs is the crews
            // themselves, so the block's driver serves here unchanged - and what it does
            // is exactly what a player does with the mouse: pick a crew, send it at a
            // mob, and when that mob is down send it at the next one, until the street
            // is ours or the outfit is not.
            if (headlessAutomation && missionAfter > 0f)
            {
                var mission = gameObject.AddComponent<BlockDemo.BlockDemoMission>();
                mission.startAfter = missionAfter;
                mission.onFoot = missionOnFoot;
                mission.motoDriveBy = missionMoto;
                mission.passes = missionPassesRidden;
                mission.passesBefore = missionPasses;
                mission.engageWithin = missionEngageWithin;
                mission.carBombRun = missionCarBomb;
                mission.carBombClearBy = missionCarBombClearBy;
                mission.carBombPatience = missionCarBombPatience;
                mission.carBombSettle = missionCarBombSettle;
                mission.bombShotCam = missionCarBombShot;
            }
#else
            Debug.LogError("[CrewDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        static void ResetManualClock()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = NormalFixedDeltaTime;
            Time.captureDeltaTime = 0f;
            Time.captureFramerate = 0;
        }

        bool _armsGiven, _bikeOwned;

        /// <summary>Lines in the stock book the last deal was made against. The book is
        /// re-read when that count moves, and not otherwise.</summary>
        int _wheelsSeen = -1;

        /// <summary>WHOSE MACHINE IT IS matters to anything but the B key. The key holds
        /// the bike in a field and sends it itself, so a scene bike never needed an
        /// owner; a mission asks the crews instead (DemoCrews.BikeOf answers by Owner),
        /// and a headless drive-by here died at once on "the outfit has no motorcycle on
        /// the street" while one stood at the kerb the whole run. It cannot be done as
        /// the bike is built, either: the outfit's crew is dealt off the roster a frame
        /// or two later, so at that moment the only units in the scene are the rivals'.
        /// The same shape as the car and the guns, which wait for the roster too.</summary>
        void OwnTheOutfitBike()
        {
            if (_bikeOwned || _outfitBike == null || _crews == null) return;
            foreach (var unit in _crews.Units)
                if (unit.Faction == 0 && !unit.IsPolice && !unit.Wiped)
                {
                    _outfitBike.Owner = unit;
                    _bikeOwned = true;
                    return;
                }
        }

        void Update()
        {
            DealTheOutfitsWheels();
            ArmTheOutfit();
            OwnTheOutfitBike();
            TickOutfitBike();
            TickPavementLife(Time.deltaTime);

            // the grip is set by eye: change the numbers in the Inspector during Play
            // and every gun re-seats itself on the spot
            if (_crews == null || (gripNudge == _seenNudge && gripTilt == _seenTilt)) return;
            _seenNudge = CrewArms.GripNudge = gripNudge;
            _seenTilt = CrewArms.GripTilt = gripTilt;
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                    if (man.Armed) man.Arm(man.WeaponPrefab, man.WeaponKind);
        }

        // The demo hands the outfit's WHEELS to its first lieutenant itself - through
        // the director, so the books agree - rather than making the player open the
        // ledger and click GIVE before he can drive anywhere.
        //
        // AND IT KEEPS READING THE BOOK, which the car alone never needed. A car is on
        // the books before the scene opens; a MACHINE is bought at the armory counter
        // in the middle of Play, and a machine nobody has been dealt is a machine in the
        // lock-up - the street stands what a man HOLDS (DemoCrews.StandLedgerBikes), and
        // this scene stands no front for the rest to wait outside of. So two machines
        // bought off the counter put nothing whatever beside the crew. The stock book is
        // re-read whenever its count moves, which is the only time it can matter.
        //
        // One car, every machine: the crew drives one car and the rest of the yard is
        // the ledger's business, but a drive-by sends every machine the crew can crew
        // (DemoCrews.OrderDriveBy) and a second one left in the lock-up is precisely the
        // thing this bench exists to try.
        void DealTheOutfitsWheels()
        {
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return;
            var roster = director.Roster;
            if (roster.Crews.Count == 0) return;
            if (roster.Equipment.Count == _wheelsSeen) return;
            _wheelsSeen = roster.Equipment.Count;

            int lieutenant = roster.Crews[0].LieutenantId;
            bool carDealt = false;
            foreach (var item in roster.Equipment)
            {
                bool machine = item.Kind == EquipmentKind.Motorcycle;
                if (!machine && item.Kind != EquipmentKind.Vehicle) continue;
                if (!machine)
                {
                    if (carDealt) continue;
                    carDealt = true;
                }
                // OwnerId, not HolderId: the boss signs a thing out to a lieutenant and
                // the lieutenant deals it to a hand himself (RosterOps.NormalizeArms).
                // Asking after the hand would re-give a machine that is already the
                // crew's every time the book grew, for a refusal each time.
                if (item.OwnerId == RosterEquipment.Unheld)
                    director.GiveEquipment(item.Id, lieutenant);
            }
        }

        // The outfit opens this demo with long guns rather than the .38 in the coat:
        // the ledger takes in one per pair of hands for each crew and the boss hands
        // the lot to its lieutenant, who deals them out himself (NormalizeArms) - so
        // the armory page and the street show the same guns, and a man who dies or
        // changes crews leaves his rifle to the crew, as the books require. Free,
        // like the car: this is a bench, and the money half of a purchase lives with
        // the outfit's accounts. Once, when the roster is in.
        void ArmTheOutfit()
        {
            if (_armsGiven || !armTheOutfit) return;
            _armsGiven = TestBench.ArmTheOutfit(outfitArms, "[CrewDemo]");
        }

        // ------------------------------------------------------------------ the set

        const string BlocksDir = "Assets/CityKit/Blocks/";
        const string BuildingsDir = "Assets/CityKit/Buildings/";
        // The catalogue's lot pad palette (RoadDemoBuilder.blockWidths/blockDepths):
        // a block is composed ON a pad, so only a pad size has blocks to stand on it.
        static readonly float[] PadWidths = { 70f, 85f, 100f };   // columns A, B, C
        static readonly float[] PadDepths = { 50f, 70f, 95f };    // rows 1, 2, 3

        SidewalkPlan _pavementPlan;   // what the street kit's props have claimed of the pavement

        // The floor: the demo's square of ground less the four street corridors, which
        // carry their own tiles and sit a tenth lower. Cut into rows at every corridor
        // edge, and each row into the stretches the corridors crossing it leave over -
        // so the block's own interior, the ground beyond the street ends and the four
        // outer quarters all come out of the one pass.
        void BuildFloor()
        {
            var mat = FloorMaterial();
            float half = FloorHalf;
            var corridors = StreetCorridors();

            var cuts = new List<float> { -half, half };
            foreach (var r in corridors)
            {
                if (r.yMin > -half && r.yMin < half) cuts.Add(r.yMin);
                if (r.yMax > -half && r.yMax < half) cuts.Add(r.yMax);
            }
            cuts.Sort();

            var spans = new List<Vector2>();
            for (int j = 0; j + 1 < cuts.Count; j++)
            {
                float z0 = cuts[j], z1 = cuts[j + 1];
                if (z1 - z0 < 0.05f) continue;
                float zc = (z0 + z1) * 0.5f;
                spans.Clear();
                foreach (var r in corridors)
                    if (r.yMin < zc && r.yMax > zc)
                        spans.Add(new Vector2(Mathf.Max(r.xMin, -half), Mathf.Min(r.xMax, half)));
                spans.Sort((a, b) => a.x.CompareTo(b.x));

                float x = -half;
                foreach (var span in spans)
                {
                    if (span.x > x + 0.05f) Slab(x, z0, span.x, z1, mat);
                    x = Mathf.Max(x, span.y);
                }
                if (x < half - 0.05f) Slab(x, z0, half, z1, mat);
            }
        }

        /// <summary>The four streets as the ground they take: kerb to kerb and both
        /// pavements, end to end.</summary>
        List<Rect> StreetCorridors() => new List<Rect>
        {
            Rect.MinMaxRect(StreetXMin, SouthZ - Half - Walk, StreetXMax, SouthZ + Half + Walk),
            Rect.MinMaxRect(StreetXMin, NorthZ - Half - Walk, StreetXMax, NorthZ + Half + Walk),
            Rect.MinMaxRect(WestX - Half - Walk, StreetZMin, WestX + Half + Walk, StreetZMax),
            Rect.MinMaxRect(EastX - Half - Walk, StreetZMin, EastX + Half + Walk, StreetZMax),
        };

        Material FloorMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return null;
            var mat = new Material(shader) { name = "Crew Demo Floor" };
            mat.SetColor("_BaseColor", floorColour);
            mat.SetFloat("_Smoothness", 0.12f);
            return mat;
        }

        void Slab(float xFrom, float zFrom, float xTo, float zTo, Material mat)
        {
            if (xTo <= xFrom || zTo <= zFrom) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            // the ground inside the block goes under the bake's own court floor rather
            // than through it: what shows here is the block's gaps, not a second paving
            bool inside = xFrom > BlockXMin - 0.1f && xTo < BlockXMax + 0.1f &&
                          zFrom > BlockZMin - 0.1f && zTo < BlockZMax + 0.1f;
            floor.transform.position = new Vector3((xFrom + xTo) * 0.5f, inside ? -0.15f : 0f, (zFrom + zTo) * 0.5f);
            floor.transform.localScale = new Vector3((xTo - xFrom) / 10f, 1f, (zTo - zFrom) / 10f);
            if (mat) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // The four streets, laid tile by tile the road demo's way (sidewalk, two lanes
        // of yellow-lined asphalt, sidewalk) and sunk a tenth so the pavement tops sit
        // level with the floor the men walk on. Each is laid in three stretches - the
        // one down the block's frontage, and the two running on past the crossroads -
        // because between two crossings the carriageway stops at the zebra bands while
        // the pavement stops at the corner slabs, which is the block's frontage exactly.
        void BuildStreets()
        {
#if UNITY_EDITOR
            var root = new GameObject("Streetscape").transform;
            var kit = new StreetKit(root, y: -0.1f);
            if (!kit.Load()) return;

            foreach (float cz in new[] { SouthZ, NorthZ })
            {
                kit.LayAlongX(cz, BlockXMin - Walk + Cell, BlockXMax + Walk - Cell,
                    BlockXMin, BlockXMax, true, true, true);
                kit.LayAlongX(cz, StreetXMin, WestX - Half - Cell,
                    StreetXMin, WestX - Half - Walk, true, true, true);
                kit.LayAlongX(cz, EastX + Half + Cell, StreetXMax,
                    EastX + Half + Walk, StreetXMax, true, true, true);
            }
            foreach (float cx in new[] { WestX, EastX })
            {
                kit.LayAlongZ(cx, BlockZMin - Walk + Cell, BlockZMax + Walk - Cell,
                    BlockZMin, BlockZMax, true, true, true);
                kit.LayAlongZ(cx, StreetZMin, SouthZ - Half - Cell,
                    StreetZMin, SouthZ - Half - Walk, true, true, true);
                kit.LayAlongZ(cx, NorthZ + Half + Cell, StreetZMax,
                    NorthZ + Half + Walk, StreetZMax, true, true, true);
            }
            foreach (float cx in new[] { WestX, EastX })
                foreach (float cz in new[] { SouthZ, NorthZ })
                    kit.LayCrossroads(cx, cz);

            // what the dressing laid is what the men walk round - the crews on their
            // strides (WalkObstacles, which the kit enters itself) and the crowd on its
            // stretches (clearance below)
            _pavementPlan = kit.Plan;
#endif
        }

        // The four streets a car may drive, as the lane network (LaneNet): the two
        // along the block's long sides and the two down its ends, two lanes each, meeting
        // at the four crossroads on the block's corners and running on past them to dead
        // ends (where a car turns round). A car handed this can be sent anywhere on any
        // of them and takes the corners to get there; the traffic wanders it.
        LaneNet _net;
        Carriageway _southMiddle;   // the south street between the crossroads (where the outfit parks)

        LaneNet BuildRoadNet()
        {
            var net = new LaneNet();
            // the crossroads: their box is the carriageways' crossing, no zebra band here
            var corners = new RoadNode[2, 2];
            float[] xs = { WestX, EastX };
            float[] zs = { SouthZ, NorthZ };
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    corners[i, j] = net.AddNode(xs[i], zs[j], Half, Half, stopSetback: 1.5f);
            // the dead ends, a hair past where the tarmac stops
            RoadNode End(float x, float z) => net.AddNode(x, z, 0.5f, 0.5f, stopSetback: 0.5f);
            var offs = new[] { 2.5f };   // the lane centres, either side of the crown (RoadHalf is two 5 m lanes)
            const float limit = 10f;
            // the two streets along X: west stub, middle, east stub
            for (int j = 0; j < 2; j++)
            {
                float cz = zs[j];
                var w = End(StreetXMin + 1f, cz);
                var e = End(StreetXMax - 1f, cz);
                net.AddRoad(new Vector3(w.XMax, 0f, cz), new Vector3(corners[0, j].XMin, 0f, cz), Half, offs, limit, w, corners[0, j], false);
                var mid = net.AddRoad(new Vector3(corners[0, j].XMax, 0f, cz), new Vector3(corners[1, j].XMin, 0f, cz), Half, offs, limit, corners[0, j], corners[1, j], false);
                if (j == 0) _southMiddle = mid;
                net.AddRoad(new Vector3(corners[1, j].XMax, 0f, cz), new Vector3(e.XMin, 0f, cz), Half, offs, limit, corners[1, j], e, false);
            }
            // the two down Z: south stub, middle, north stub
            for (int i = 0; i < 2; i++)
            {
                float cx = xs[i];
                var sEnd = End(cx, StreetZMin + 1f);
                var nEnd = End(cx, StreetZMax - 1f);
                net.AddRoad(new Vector3(cx, 0f, sEnd.ZMax), new Vector3(cx, 0f, corners[i, 0].ZMin), Half, offs, limit, sEnd, corners[i, 0], true);
                net.AddRoad(new Vector3(cx, 0f, corners[i, 0].ZMax), new Vector3(cx, 0f, corners[i, 1].ZMin), Half, offs, limit, corners[i, 0], corners[i, 1], true);
                net.AddRoad(new Vector3(cx, 0f, corners[i, 1].ZMax), new Vector3(cx, 0f, nEnd.ZMin), Half, offs, limit, corners[i, 1], nEnd, true);
            }
            net.Finish();
            _net = net;
            return net;
        }

        /// <summary>The kerb of the south street's middle stretch at this x, on the side
        /// traffic heading <paramref name="heading"/> keeps to (+1 east, -1 west), for a
        /// body of this half width.</summary>
        Vector3 SouthKerb(float x, int heading, float halfWidth)
        {
            var road = _southMiddle;
            if (road == null) return new Vector3(x, -0.08f, SouthZ - heading * (Half - 1.4f));
            road.Project(new Vector3(x, 0f, SouthZ), out float s, out _);
            s = Mathf.Clamp(s, 6f, road.Length - 6f);
            var p = road.Pose(s, road.KerbD(heading, halfWidth));
            p.y = -0.08f;
            return p;
        }

        // What stands inside the ring: one of the catalogue's blocks, composed on a lot
        // pad of this very size and set down whole (nothing is scattered into a block
        // by hand here - the catalogue is the only thing that fills one). Nobody walks
        // through it: a man off the sidewalk graph goes round the block, not into it.
        void BuildBlock()
        {
#if UNITY_EDITOR
            WalkObstacles.Block(BlockXMin, BlockXMax, BlockZMin, BlockZMax);
            _blockRoot = new GameObject("Block").transform;

            var bake = FindBlockBake();
            if (bake == null) { FillFrontage(); return; }

            // sunk the same tenth the street kit is, because a composed block carries
            // its own court floor of concrete plates laid to the ROAD DEMO's pavement
            // top (BlockFloorFiller, plates at -0.034 in the bake): dropped with the
            // street, the block's paving comes out flush with the pavement round it
            var go = Instantiate(bake, new Vector3(0f, -0.1f, 0f), Quaternion.identity, _blockRoot);
            go.name = bake.name;
            // a bake's pivot is not its footprint centre (RoadDemoBuilder.BlockPivotToCentre):
            // measured and moved onto the pad, its own ground level left alone
            var b = BoundsOf(go);
            go.transform.position += new Vector3(-b.center.x, 0f, -b.center.z);
            Debug.Log("[CrewDemo] The block is " + bake.name + " (" +
                      b.size.x.ToString("F0") + " x " + b.size.z.ToString("F0") + " m on a " +
                      blockWidth + " x " + blockDepth + " m pad).");
#endif
        }

#if UNITY_EDITOR
        /// <summary>The block bake to stand on this pad: the one named, else one of the
        /// blocks the catalogue rolled for this pad size, drawn afresh every Play.</summary>
        GameObject FindBlockBake()
        {
            GameObject L(string name) =>
                UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BlocksDir + name + ".prefab");

            if (!string.IsNullOrEmpty(blockBake))
            {
                var named = L(blockBake);
                if (named == null)
                    Debug.LogWarning("[CrewDemo] No block bake " + blockBake + " in " + BlocksDir + ".");
                return named;
            }

            string code = PadCode();
            if (code == null)
            {
                Debug.LogWarning("[CrewDemo] " + blockWidth + " x " + blockDepth +
                                 " m is no catalogue lot pad size (70/85/100 by 50/70/95) - " +
                                 "no composed block fits it, so the frontage gets kit storefronts.");
                return null;
            }

            var stock = new List<GameObject>();
            for (int i = 1; i <= 9; i++)
            {
                var rolled = L("auto_" + code + "_" + i);
                if (rolled != null) stock.Add(rolled);
            }
            if (stock.Count == 0)
            {
                Debug.LogWarning("[CrewDemo] no block rolled for pad " + code + " in " + BlocksDir +
                                 " - run Tools/City/Catalog/Randomise Blocks For Every Lot.");
                return null;
            }
            return stock[Random.Range(0, stock.Count)];
        }

        /// <summary>The catalogue pad code for this block's size ("C2"), or null when it
        /// is not a pad size at all.</summary>
        string PadCode()
        {
            int w = PaletteIndex(PadWidths, blockWidth), d = PaletteIndex(PadDepths, blockDepth);
            return w < 0 || d < 0 ? null : ((char)('A' + w)).ToString() + (d + 1);
        }

        static int PaletteIndex(float[] palette, float size)
        {
            for (int k = 0; k < palette.Length; k++)
                if (Mathf.Abs(palette[k] - size) <= 1f) return k;
            return -1;
        }

        // No composed block for this pad: a short row of the kit's storefronts along the
        // south frontage, fronts to the kerb, so the block still reads as a block rather
        // than as an empty lot. The catalogue is meant to fill it - this is the fallback.
        void FillFrontage()
        {
            var kit = LivingCity.Generation.BlockFabric.Commerce;
            float cursor = BlockXMin + 6f;
            for (int i = 0; i < kit.Length && cursor < BlockXMax - 12f; i++)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingsDir + kit[i] + ".prefab");
                if (prefab == null) continue;

                // the shopfront to the street: the mesh says which side it is on (an
                // override by name wins), that side is turned to +Z, then the whole
                // building about-faces so +Z looks south across the kerb
                var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, _blockRoot);
                go.name = prefab.name;
                var front = FacadeFinder.FrontOf(go, out var reading);
                foreach (var o in facadeOverrides)
                    if (o.building == prefab.name) { front = o.front; reading += " (override)"; }
                Debug.Log("[CrewDemo] " + prefab.name + ": " + reading);
                go.transform.rotation = Quaternion.Euler(0f, FacadeFinder.YawToPlusZ(front) + 180f, 0f);

                var b = BoundsOf(go);
                if (b.size.sqrMagnitude <= 0f) { Destroy(go); continue; }
                if (cursor + b.size.x > BlockXMax - 2f) { Destroy(go); break; }
                // footprint edge on the block's frontage line; the bake's own ground
                // stays at 0 - these are built with the ground at y = 0 and some carry
                // a slab or a cellar under it (the cafe reaches 2 m down)
                go.transform.position += new Vector3(cursor + b.size.x * 0.5f, 0f, BlockZMin) -
                                         new Vector3(b.center.x, 0f, b.min.z);
                cursor += b.size.x + 8f;
            }
        }
#endif

        // The outfit's wheels: a Gang Warfare low car pulled in at the kerb of the south
        // street a few strides east of the muster, nose along the street. The body for
        // the ledger's car - whichever crew the book assigns it to can get in and drive
        // it (CrewCar).
        void ParkCar(Vector3 muster)
        {
#if UNITY_EDITOR
            // a named body, or a different gangster's car every time the scene starts
            GameObject prefab = null;
            if (!string.IsNullOrEmpty(crimeCar)) prefab = FindVehicle(crimeCar);
            else
            {
                var start = Random.Range(0, GangsterCars.Length);
                for (int i = 0; i < GangsterCars.Length && prefab == null; i++)
                    prefab = FindVehicle(GangsterCars[(start + i) % GangsterCars.Length]);
            }
            if (!prefab)
            {
                Debug.LogWarning("[CrewDemo] No car prefab " + (string.IsNullOrEmpty(crimeCar) ? "(any gangster car)" : crimeCar) + " in any Synty vehicle folder.");
                return;
            }
            Debug.Log("[CrewDemo] The outfit drives a " + prefab.name + " today.");
            // the south kerb's parking strip, clear of the lane (the street is sunk a
            // tenth: asphalt at -0.08); traffic heading east keeps to this side.
            // DemoCrews owns it from here - binds it to the ledger's car, and lets the
            // crew the book gives it to get in and drive
            var at = SouthKerb(muster.x + 12f, +1, 0.95f);
            var car = _crews.AddCar(prefab, at, Quaternion.Euler(0f, 90f, 0f), roadY: -0.08f);
            // the police pack's bodies come in livery: black for the mob
            bool livery = UnityEditor.AssetDatabase.GetAssetPath(prefab).Contains("PolygonPoliceStation");
            if (car != null && blackSedan && livery) DressBlack(car.Tf);
#endif
        }

        // Pack cars on the two streets that run the length of the block, both ways -
        // something for the crew's car to follow, brake for, swing round in front of
        // and squeeze past when parked. They drive the whole net from there (corners,
        // the side streets, the turn-rounds at the dead ends), the same way the city's
        // traffic does.
        void BuildTraffic(PedClips clips)
        {
            if (trafficCars <= 0 || _net == null) return;
            var bodies = TrafficBodies();
            if (bodies.Count == 0)
            {
                Debug.LogWarning("[CrewDemo] No pack cars found for traffic.");
                return;
            }
            var along = new List<Carriageway>();
            foreach (var r in _net.Roads)
                if (Mathf.Abs(r.Axis.x) > 0.5f) along.Add(r);   // the streets along X
            var traffic = gameObject.AddComponent<StreetTraffic>();
            // and somebody at every wheel, out of the same passers-by the pavements draw on
            traffic.Init(bodies, _net, -0.08f, trafficCars * 2, PassersBy(), clips.SitLoop, along);
        }

        List<GameObject> TrafficBodies() => TestBench.WeightedCars(FindCivilianVehicle);

        // Cars stood at the kerb of the two side streets, which nothing drives down.
        // They are geometry, not traffic: something to walk round, take cover behind
        // and shoot over on the way round the block.
        void BuildParkedCars()
        {
#if UNITY_EDITOR
            if (parkedCars <= 0) return;
            var bodies = TrafficBodies();
            if (bodies.Count == 0) return;
            var root = new GameObject("Parked Cars").transform;
            // the two stretches of each side street a car may be left on: clear of the
            // crossing in the middle of it and of both junctions
            var spans = new[]
            {
                new Vector2(-(BlockZMax - 4f), -14f),
                new Vector2(14f, BlockZMax - 4f),
            };
            // KerbCars measures every body: it stands each one at the kerb by its own
            // width, and claims its own length of kerb before it is put down, so no two
            // are dealt the same yard of street
            int half = parkedCars / 2;
            KerbCars.Park(root, bodies, parkedCars - half, alongX: false, centre: WestX,
                halfRoad: Half, roadY: -0.08f, spans: spans);
            KerbCars.Park(root, bodies, half, alongX: false, centre: EastX,
                halfRoad: Half, roadY: -0.08f, spans: spans);
#endif
        }

        // ------------------------------------------------------------ the pavements

        // Two loops of pavement round the block: the block's own frontage on the inside,
        // the far pavements of the four streets on the outside, joined by a crossing in
        // the middle of each street - and a crowd of passers-by walking them, which is
        // what scatters when the shooting starts. The same CivilianAgent the city uses.
        readonly List<CivilianAgent> _walkers = new List<CivilianAgent>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        float _chatScan;

        // The bodies the street's nobodies wear - the passers-by on the pavements and
        // the drivers in the traffic: the city packs' humanoids, less the coppers and
        // the mob's own coats (a crew's body is never a passer-by - the city's rule,
        // RoadDemoBuilder). Gathered once.
        List<GameObject> _passersBy;

        // ------------------------------------------------------------------- bikes
        //
        // The bench for the whole two-wheeler business, all of it at once: bikes riding
        // the streets with a rider posed onto them, bikes stood on their stands at a
        // kerb, and one of the outfit's own with a hood at the bars and his mate behind
        // him - press B and it goes hunting the first rival, which is the only way to
        // see whether a pillion can actually hit anything at speed.
        //
        // None of it comes out of a folder scan. Every scan in the project denies
        // "bike" and "moped" by name, and they stay denied; a machine reaches this
        // street the way a marked cruiser does, by being asked for out of the
        // catalogue (VehicleCatalog.Motorcycles).
        void BuildBikes(PedClips clips)
        {
#if UNITY_EDITOR
            var bodies = StreetBikes.Bodies();
            if (bodies.Count == 0)
            {
                if (trafficBikes > 0 || parkedBikes > 0 || outfitBike)
                    Debug.LogWarning("[CrewDemo] No two-wheeler out of VehicleCatalog.Motorcycles - no bikes today.");
                return;
            }

            if (trafficBikes > 0 && _net != null)
            {
                var along = new List<Carriageway>();
                foreach (var r in _net.Roads)
                    if (Mathf.Abs(r.Axis.x) > 0.5f) along.Add(r);   // the streets along X
                gameObject.AddComponent<StreetBikes>().Init(_net, trafficBikes, -0.08f,
                    PassersBy(), clips.Ride != null ? clips.Ride : clips.SitLoop,
                    pillionChance: 0.4f, layer: -1, roads: along, bodies: bodies);
            }

            if (parkedBikes > 0 && _net != null)
            {
                var root = new GameObject("Parked Bikes").transform;
                var spots = new List<Vector3>(parkedBikes);
                for (int i = 0; i < parkedBikes; i++)
                {
                    // along the two side streets, which nothing drives down
                    float x = (i % 2 == 0) ? WestX : EastX;
                    float z = Mathf.Lerp(BlockZMin + 6f, BlockZMax - 6f, (i + 0.5f) / parkedBikes);
                    spots.Add(new Vector3(x, 0f, z));
                }
                StreetBikes.ParkSeveral(_net, root, spots, -0.08f, bodies);
            }

            if (outfitBike) BuildOutfitBike(bodies);
#endif
        }

        CrewBike _outfitBike;

        void BuildOutfitBike(List<GameObject> bodies)
        {
#if UNITY_EDITOR
            if (_crews == null) return;
            GameObject prefab = null;
            if (!string.IsNullOrEmpty(outfitBikeBody)) prefab = FindCivilianVehicle(outfitBikeBody);
            if (prefab == null) prefab = bodies[0];

            // two gang bodies, and neither of them anybody else's face: the crews on the
            // pavement are drawn from the same tables, so a pair off the far end of the
            // hood table keeps the bike from being two men who are already standing
            // over there (GangLooks, and the no-twins rule the cast is built on)
            var looks = LivingCity.Gangs.GangLooks.Hoods;
            GameObject riderBody = null, pillionBody = null;
            for (int i = looks.Length - 1; i >= 0 && (riderBody == null || pillionBody == null); i--)
            {
                var body = Cast(looks[i]);
                if (body == null) continue;
                if (riderBody == null) riderBody = body;
                else if (body != riderBody) pillionBody = body;
            }
            if (riderBody == null)
            {
                Debug.LogWarning("[CrewDemo] No gang body for the bike - the outfit walks.");
                return;
            }

            // the south kerb, but the FAR end of it: the outfit's car is left by the
            // muster at the west end, and both builders asking for "the south kerb,
            // about here" put the bike inside the car for a whole headless run
            var at = SouthKerb(BlockXMax - 8f, +1, 0.5f);
            _outfitBike = _crews.AddBike(prefab, at, Quaternion.Euler(0f, 90f, 0f), roadY: -0.08f,
                riderPrefab: riderBody, pillionPrefab: pillionBody,
                weapon: CrewKit.Weapon("SM_Wep_Machine_Pistol_01"), kind: EquipmentKind.MachinePistol,
                riderName: "The rider", pillionName: "The pillion");
            if (_outfitBike != null)
            {
                Debug.Log("[CrewDemo] The outfit keeps a " + prefab.name + " as well - press B to send it at a rival.");
            }
#endif
        }

        // B sends the bike at the first rival still standing, and calls it off again.
        // A key rather than the overlay on purpose: the overlay's orders go through a
        // crew's CAR (DemoCrews.OrderAttack), and a bike is not a crew's car - it is two
        // men and a machine nobody sold them.
        bool _bikeSent;

        void TickOutfitBike()
        {
            if (_outfitBike == null || _crews == null) return;

            // the clock, for a run nobody is sat at: the harness sets the seconds and
            // watches what the pass does
            if (headlessAutomation && !_bikeSent && bikeAttackAfter > 0f &&
                Time.timeSinceLevelLoad >= bikeAttackAfter)
            {
                _bikeSent = true;
                SendBike();
                return;
            }

            // the project runs InputSystem-only: UnityEngine.Input throws here
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.bKey.wasPressedThisFrame) return;
            if (_outfitBike.DriveByTarget != null)
            {
                _outfitBike.EndDriveBy();
                Debug.Log("[CrewDemo] The bike breaks off.");
                return;
            }
            SendBike();
        }

        void SendBike()
        {
            foreach (var unit in _crews.Units)
            {
                if (unit == null || unit.Faction == 0 || DemoCrews.Finished(unit)) continue;
                _outfitBike.DriveBy(unit);
                Debug.Log("[CrewDemo] The bike goes after " + unit.GangName + ".");
                return;
            }
            Debug.Log("[CrewDemo] Nobody left to shoot at.");
        }

        List<GameObject> PassersBy()
        {
            if (_passersBy != null) return _passersBy;
            _passersBy = new List<GameObject>();
#if UNITY_EDITOR
            foreach (var folder in new[] { "Assets/Synty/PolygonCity/Prefabs/Characters", "Assets/Synty/PolygonPalmCity/Prefabs/Characters" })
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var file = System.IO.Path.GetFileNameWithoutExtension(path);
                    var name = file.ToLowerInvariant();
                    if (name.Contains("police") || name.Contains("attach")) continue;
                    if (LivingCity.Gangs.GangLooks.IsGangBody(file)) continue;
                    if (LivingCity.Entities.CrowdLooks.IsBarred(file)) continue;
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var animator = go ? go.GetComponentInChildren<Animator>() : null;
                    if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                    _passersBy.Add(go);
                }
#endif
            return _passersBy;
        }

        void BuildPavementLife(PedClips crewClips)
        {
#if UNITY_EDITOR
            if (pedestrians <= 0 || crewClips.Walk == null || crewClips.Idle == null) return;
            var prefabs = PassersBy();
            if (prefabs.Count == 0) return;
            EnsurePedGraph();

            var crowd = new PedClips
            {
                Walk = crewClips.Walk, Idle = crewClips.Idle, Talk = crewClips.Talk, Shout = crewClips.Shout,
            };
            var variety = new System.Random(nameSeed + 7);
            var root = new GameObject("Passers-by").transform;
            var pavements = _pedLinks.FindAll(l => !l.Gated);
            for (int k = 0; k < pedestrians; k++)
            {
                var link = pavements[Random.Range(0, pavements.Count)];
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                foreach (var animator in go.GetComponentsInChildren<Animator>()) animator.runtimeAnimatorController = null;
                var agent = new CivilianAgent { Speed = Random.Range(1.25f, 1.85f) };
                agent.Init(go.transform, CrewKit.ForCrowd(crowd, variety), link, Random.value * link.Length * 0.9f);
                agent.Setup(null); // no doors, no benches on this street: they walk, chat, and run
                _walkers.Add(agent);
            }
#endif
        }

        /// <summary>The pavements as a graph, once. The crowd wants it and so do the
        /// beat officers, and whichever of them is built first pays for it - a scene with
        /// no passers-by at all still has pavements for the law to walk.</summary>
        void EnsurePedGraph()
        {
            if (_innerRing != null) return;

            // the two loops, walked down the middle of their pavement band, and the
            // four crossings between them - one across the middle of each street
            _innerRing = Ring(BlockXMin - Walk * 0.5f, BlockXMax + Walk * 0.5f,
                              BlockZMin - Walk * 0.5f, BlockZMax + Walk * 0.5f, out var innerMid);
            Ring(WestX - Half - Walk * 0.5f, EastX + Half + Walk * 0.5f,
                 SouthZ - Half - Walk * 0.5f, NorthZ + Half + Walk * 0.5f, out var outerMid);
            for (int s = 0; s < 4; s++) Join(innerMid[s], outerMid[s], gated: true);

            // and what the kit's props have left of the pavement to walk on, read into
            // every stretch the way the city does it (BuildWalkClearance) - else the
            // passers-by walk straight through the lamp posts and the bins
            if (_pavementPlan != null)
                foreach (var link in _pedLinks)
                    link.SampleClearance(_pavementPlan, SidewalkDressing.WalkRadius);
        }

        /// <summary>The pavement round the block itself - the officers' post is on it,
        /// because it is the one loop with a building at its back instead of a road.</summary>
        List<PedNode> _innerRing;

        /// <summary>A closed walk round a rectangle: corners, a corner every twenty-odd
        /// metres between them, joined all the way round. <paramref name="middles"/>
        /// comes back with the node halfway down each side, south side first, then the
        /// east, the north and the west - where the crossings hang off.</summary>
        List<PedNode> Ring(float xMin, float xMax, float zMin, float zMax, out PedNode[] middles)
        {
            var corners = new[]
            {
                new Vector3(xMin, 0f, zMin), new Vector3(xMax, 0f, zMin),
                new Vector3(xMax, 0f, zMax), new Vector3(xMin, 0f, zMax),
            };
            var nodes = new List<PedNode>();
            middles = new PedNode[4];
            for (int s = 0; s < 4; s++)
            {
                var a = corners[s];
                var b = corners[(s + 1) % 4];
                // an even number of them, so the node halfway down the side is exactly
                // halfway - that is where the crossing to the other loop hangs off
                int steps = 2 * Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 44f));
                int first = nodes.Count;
                for (int k = 0; k < steps; k++)
                    nodes.Add(new PedNode { Pos = Vector3.Lerp(a, b, k / (float)steps) });
                middles[s] = nodes[first + steps / 2];
            }
            for (int i = 0; i < nodes.Count; i++)
                Join(nodes[i], nodes[(i + 1) % nodes.Count], gated: false);
            return nodes;
        }

        void Join(PedNode a, PedNode b, bool gated) => TestBench.Join(a, b, gated, _pedLinks);

        void TickPavementLife(float dt) => TestBench.TickPavementLife(_walkers, _beat, dt, ref _chatScan);

        void OnDestroy()
        {
            ResetManualClock();
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Dispose();
            for (int i = 0; i < _beat.Count; i++) _beat[i].Dispose();
        }

        // ------------------------------------------------------------ the law

        // A cruiser at the kerb at the far (east) end of the south street, nose west, two
        // officers in it - the dispatcher's one unit here. It answers the shooting
        // the way the city's patrol cars will: siren on, up the street, in at the
        // kerb short of it, men out.
        void BuildPolice(PedClips clips)
        {
#if UNITY_EDITOR
            if (!policeCruiser && policeBeat <= 0) return;
            var officers = OfficerBodies();
            if (officers.Count == 0)
            {
                Debug.LogWarning("[CrewDemo] No officer bodies in the police station pack.");
                return;
            }
            var dispatch = gameObject.AddComponent<PoliceDispatch>();
            dispatch.Init(_crews, clips, officers, CrewKit.Weapon(CrewArms.DefaultSidearm));

            if (policeCruiser) BuildCruiser(dispatch);
            if (policeBeat > 0) BuildBeat(dispatch, clips, officers);
#endif
        }

        // The bodies the law is worn by, the police pack's own - the cruiser's two men
        // are dealt out of them and so is every officer walking the pavements.
        List<GameObject> OfficerBodies()
        {
            var officers = new List<GameObject>();
#if UNITY_EDITOR
            foreach (var name in new[] { "SM_Chr_Officer_Male_01", "SM_Chr_Officer_Male_02", "SM_Chr_Officer_Male_03" })
            {
                var body = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/" + name + ".prefab") ?? Cast(name);
                if (body) officers.Add(body);
            }
#endif
            return officers;
        }

        void BuildCruiser(PoliceDispatch dispatch)
        {
#if UNITY_EDITOR
            var marked = LivingCity.Gameplay.VehicleCatalog.PoliceCars;
            var prefab = !string.IsNullOrEmpty(policeCar) ? FindVehicle(policeCar) : null;
            for (int i = 0; i < marked.Length && prefab == null; i++)
                prefab = FindVehicle(marked[i]);
            if (!prefab)
            {
                Debug.LogWarning("[CrewDemo] No police car prefab " +
                                 (string.IsNullOrEmpty(policeCar) ? "(any marked cruiser)" : policeCar) + ".");
                return;
            }

            // the east end, north kerb, nose west (that side's traffic runs west)
            var at = SouthKerb(EastX - 20f, -1, 0.95f);
            var car = _crews.AddCar(prefab, at, Quaternion.Euler(0f, -90f, 0f), roadY: -0.08f);
            if (car == null) return;
            car.Tf.name = "Police Cruiser";
            dispatch.AddCruiser(car, at);
#endif
        }

        // ------------------------------------------------------------ the beat
        //
        // Officers on foot, walking the block's own pavement and answering a shooting
        // the way the city's do - the same PoliceFootPatrol the station spawns, given a
        // shopfront to stand at instead of a station door. Nothing about how they walk,
        // what they do when they are sent, or how long they hold a scene is decided
        // here: this stands them up and hands them to the dispatcher.
        readonly List<PoliceFootPatrol> _beat = new List<PoliceFootPatrol>();

        /// <summary>Metres off the pavement's centre line the post stands - the officer's
        /// "station door", far enough in to read as a man stood at a shopfront rather
        /// than one planted in the middle of the walk.</summary>
        const float PostOffPavement = 1.8f;

        /// <summary>The body a beat officer is worn in. ONE of the pack's three, by the
        /// user's own reading of them on the street: the other two are not men you would
        /// put on a pavement. The cruiser's squad still deals from all three - they are
        /// sat in a car.</summary>
        const string BeatBody = "SM_Chr_Officer_Male_01";

        void BuildBeat(PoliceDispatch dispatch, PedClips clips, List<GameObject> officers)
        {
#if UNITY_EDITOR
            if (clips.Walk == null || clips.Idle == null) return;
            var beatBody = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/" + BeatBody + ".prefab")
                ?? Cast(BeatBody);
            if (beatBody == null)
            {
                Debug.LogWarning("[CrewDemo] No " + BeatBody + " - nobody to walk the beat.");
                return;
            }
            // the officer's wardrobe: the walk and the stand, the JOG he answers a call
            // at, and the PISTOL IDLE he stands over an arrest in (CrewKit.WithArms
            // brings the gun set with it)
            var beatClips = CrewKit.WithArms(new PedClips { Walk = clips.Walk, Idle = clips.Idle });
            EnsurePedGraph();

            // the south-east of the block, which is a walk away from the outfit's muster
            // at the west end: the officers are not stood in the first exchange of the
            // scene, and their walk to it is one the camera can follow
            var post = new Vector3(BlockXMax - 6f, 0f, BlockZMin - Walk * 0.5f);
            var homeFwd = PostStretch(post);
            if (homeFwd == null)
            {
                Debug.LogWarning("[CrewDemo] No pavement to post the beat on.");
                return;
            }
            PedLink homeBack = null;
            foreach (var l in homeFwd.To.Links)
                if (l.To == homeFwd.From) { homeBack = l; break; }
            if (homeBack == null) return;

            var routeHome = PoliceFootPatrol.RouteHome(homeFwd);
            // every walkable corner of both loops, the officers' waypoint pool - the
            // same pool the city's beat draws from, read the same way
            var corners = new HashSet<PedNode>();
            foreach (var l in _pedLinks) { corners.Add(l.From); corners.Add(l.To); }
            var nodes = new List<PedNode>(corners);

            var along = (homeFwd.To.Pos - homeFwd.From.Pos).normalized;
            // which way the block is from the pavement: the door is pushed THAT way, so
            // an officer standing at his post has a building at his back and the road in
            // front of him, never the other way about
            var mid = Vector3.Lerp(homeFwd.From.Pos, homeFwd.To.Pos, 0.5f);
            var inward = Vector3.Cross(Vector3.up, along);
            if (Vector3.Dot(inward, Flat(BlockCentre - mid)) < 0f) inward = -inward;

            float entryT = Mathf.Clamp(Vector3.Dot(post - homeFwd.From.Pos, along), 2f, homeFwd.Length - 2f);
            var root = new GameObject("Beat Officers").transform;
            for (int i = 0; i < policeBeat; i++)
            {
                var go = Instantiate(beatBody, root);
                go.name = "Beat Officer " + (i + 1);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                foreach (var animator in go.GetComponentsInChildren<Animator>()) animator.runtimeAnimatorController = null;

                // each man his own few metres of the same stretch, so two officers do not
                // stand in one another
                float t = Mathf.Clamp(entryT + i * 3f, 2f, homeFwd.Length - 2f);
                var door = Vector3.Lerp(homeFwd.From.Pos, homeFwd.To.Pos, t / homeFwd.Length)
                           + inward * PostOffPavement;

                var officer = new PoliceFootPatrol
                    { Speed = Random.Range(1.3f, 1.5f), UnitNumber = i + 1 };
                officer.Init(go.transform, beatClips, homeFwd, t);
                officer.Configure(door, homeFwd, homeBack, t, nodes, routeHome,
                    BeatRest, BeatStops, Random.Range(2f, 6f) + i * 4f);
                _beat.Add(officer);
                dispatch.Register(officer);
            }
#endif
        }

        /// <summary>Seconds an officer stands at his post between beats, and how many
        /// corners a beat takes in. Both short: the block is a hundred metres round, and
        /// what is being watched here is the answer to a shooting, not the rounds.</summary>
        static readonly Vector2 BeatRest = new Vector2(4f, 10f);
        static readonly Vector2Int BeatStops = new Vector2Int(2, 4);

        Vector3 BlockCentre => new Vector3((BlockXMin + BlockXMax) * 0.5f, 0f, (BlockZMin + BlockZMax) * 0.5f);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The stretch of the block's own pavement the post sits on: of the ring
        /// round the block, the link whose middle is nearest. The outer loop is no good
        /// for a post - its back is to the void beyond the streets - so only the inner
        /// one is looked at.</summary>
        PedLink PostStretch(Vector3 post)
        {
            if (_innerRing == null) return null;
            PedLink best = null;
            float bestD = float.MaxValue;
            foreach (var node in _innerRing)
                foreach (var l in node.Links)
                {
                    if (l.Gated || !_innerRing.Contains(l.To)) continue;
                    float d = (Vector3.Lerp(l.From.Pos, l.To.Pos, 0.5f) - post).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = l; }
                }
            return best;
        }

        static readonly string[] VehicleFolders =
        {
            "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/",
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
        };

        static GameObject FindVehicle(string name)
        {
#if UNITY_EDITOR
            foreach (var folder in VehicleFolders)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + ".prefab");
                if (p) return p;
            }
#endif
            return null;
        }

        /// <summary>The same walk, but it will not hand back anybody's marked vehicle.
        /// The police pack is searched BEFORE the palm city (the mob sedan is repainted
        /// out of one of its four-doors, DressBlack), and it has a "SM_Veh_Pickup_01" of
        /// its own - so asking for that name plainly used to put a liveried police pickup
        /// in the traffic. Whatever wants a civilian body asks through here.</summary>
        static GameObject FindCivilianVehicle(string name)
        {
#if UNITY_EDITOR
            foreach (var folder in VehicleFolders)
            {
                var path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p) return p;
            }
#endif
            return null;
        }

        // The police pack's four-door is the only sedan in the packs with doors and
        // windows as parts of their own - the very things a drive-by car needs - and
        // it comes in a police livery. Repainted: every body part goes flat black (a
        // mob sedan), the glass keeps its glass, and the light bar on the roof is
        // switched off. A property block would not do here - the livery is in the
        // texture, so the material itself is swapped for a plain black one.
        void DressBlack(Transform car)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var black = new Material(shader) { name = "Mob Sedan Black" };
            black.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f));
            black.SetFloat("_Smoothness", 0.55f);
            black.SetFloat("_Metallic", 0.25f);
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.IndexOf("Light", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    r.name.IndexOf("Lights", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    r.gameObject.SetActive(false); // the roof bar
                    continue;
                }
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    mats[i] = black;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }


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

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 900f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var dc = camGo.AddComponent<DemoCamera>();
            // the road demo's mix, for the crowd's screams, the horns and the siren
            // (the shots stay the arena's own 2D sources); it parks the ear on the focus
            gameObject.AddComponent<DemoAudio>().Init(null, dc, null, null, _walkers);
            // opens on the south street with the outfit at one end of it and the block's
            // south frontage - the first rival's - at the other
            dc.pivot = new Vector3((StreetXMin + WestX) * 0.25f, 0f, SouthZ - 6f);
            dc.distance = 104f;
            dc.yaw = 0f;
            dc.pitch = 46f;
            dc.hintTopPx = 104f; // under the crew bar
            dc.showHint = true;  // the road demo hides its key line; this scene keeps one
            dc.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                      "left-click one of ours: select his crew   right-click floor: walk there   " +
                      "right-click a rival: attack   P: ledger";
        }

        // ------------------------------------------------------------------ rivals

        /// <summary>The middle of one of the block's four frontages, and the way a man
        /// stood there looks: out over the pavement he is on, his back to the wall.
        /// South face first, then the east, the north, the west - the order the rival
        /// crews take them in, so two rivals are always a corner apart.</summary>
        (Vector3 at, Vector3 facing) Frontage(int side)
        {
            float band = Walk * 0.55f;   // out of the shop doorway, short of the kerb strip
            switch (side & 3)
            {
                case 0: return (new Vector3(0f, 0f, BlockZMin - band), Vector3.back);
                case 1: return (new Vector3(BlockXMax + band, 0f, 0f), Vector3.right);
                case 2: return (new Vector3(0f, 0f, BlockZMax + band), Vector3.forward);
                default: return (new Vector3(BlockXMin - band, 0f, 0f), Vector3.left);
            }
        }

        // one crew per frontage of the block - each holds his own face of it, and no
        // two faces are within either crew's alert range of the other: on the pavement
        // in front of his own frontage, back to the wall, facing the street
        static readonly (string, EquipmentKind)[] RivalArms =
        {
            ("SM_Wep_Pistol_Revolver_01", EquipmentKind.Pistol),
            ("SM_Wep_Machine_Pistol_01", EquipmentKind.MachinePistol),
            ("SM_Wep_Shotgun_01", EquipmentKind.Shotgun),
            ("SM_Wep_SubMachineGun_01", EquipmentKind.TommyGun),
        };

        void SpawnRivals() =>
            TestBench.SpawnRivals(_crews, nameSeed, rivalCrews, rivalHoods, RivalArms, Frontage, "[CrewDemo]");

        static GameObject Cast(string name) => TestBench.Cast(name);
    }
}
