using System.Collections.Generic;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CrewDemo
{
    // A street with two cafes and two lines of men: the outfit's crews out of the
    // ledger on the near side, a rival lieutenant with his hoods loafing in front
    // of each cafe on the far one - each guarding his own door, the doors set well
    // apart so the two mobs never trip each other's alarm - a standoff apart. Left-click one of ours to select his crew, right-click the
    // floor to walk him there (his hoods fall in behind), right-click a rival's man
    // to send the crew at that rival - and the two crews shoot it out until one is
    // down. The rivals answer fire on their own, and open up on any crew that walks
    // up to them.
    //
    // Built at Play from this one component, the road demo's way: ground, light,
    // camera, and the crews out of the same DemoCrews/CrewWalker classes the city
    // uses - so what is proven here on bare ground is what the city gets. Editor
    // only, like that demo: the bodies, clips and guns come through the AssetDatabase.
    public class CrewDemoBuilder : MonoBehaviour
    {
        [Header("Floor")]
        public float floorSize = 160f;
        public Color floorColour = new Color(0.20f, 0.20f, 0.21f);

        [Header("Street")]
        [Tooltip("Where the street runs (its centre line, along X), north of the outfit's line.")]
        public float streetZ = 26f;
        [Tooltip("Half the street's length in metres.")]
        public float streetHalfLength = 196f;

        [System.Serializable]
        public struct FacadeOverride
        {
            [Tooltip("Prefab name, e.g. building-coffeeshop.")] public string building;
            [Tooltip("Which local side of the prefab its shopfront is on.")] public FacadeFinder.Side front;
        }
        [Tooltip("When the mesh reading gets a building's front wrong, say so here by name.")]
        public FacadeOverride[] facadeOverrides = System.Array.Empty<FacadeOverride>();

        [Header("Wheels")]
        [Tooltip("The outfit's car, parked at the kerb by the crews.")]
        public string crimeCar = "SM_Veh_Car_01"; // the police pack's four-door - the one sedan with door and window parts
        [Tooltip("Repaint the body flat black (the police four-door comes in livery).")]
        public bool blackSedan = true;
        [Tooltip("Pack cars on the road, both ways, for the outfit's car to thread through.")]
        [Range(0, 30)] public int trafficCars = 12;
        [Tooltip("Metres between the two cafes on the far sidewalk - a rival crew loafs in front of each.")]
        public float cafeSpread = 44f;

        [Header("Layout")]
        [Tooltip("Metres from the far kerb back to the outfit's line.")]
        public float standoff = 40f;
        [Tooltip("Metres between neighbouring crews on the outfit's line.")]
        public float crewSpread = 11f;
        [Range(1, 4)] public int rivalCrews = 2;
        [Range(0, 6)] public int rivalHoods = 3;
        public int nameSeed = 1987;

        [Header("Grip (live - drag in Play to seat the gun)")]
        [Tooltip("Metres from the wrist along the fingers / off the palm / toward the thumb.")]
        public Vector3 gripNudge = new Vector3(0.075f, -0.015f, 0.01f);
        [Tooltip("Degrees of extra turn on the gun in the fist.")]
        public Vector3 gripTilt = Vector3.zero;

        DemoCrews _crews;
        Vector3 _seenNudge, _seenTilt;

        void Awake()
        {
#if UNITY_EDITOR
            CrewArms.GripNudge = _seenNudge = gripNudge;
            CrewArms.GripTilt = _seenTilt = gripTilt;

            BuildFloor();
            BuildStreet();
            BuildLight();
            BuildCamera();
            gameObject.AddComponent<CrewDemoPace>();

            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null)
                Debug.LogWarning("[CrewDemo] Walk/idle clips missing under Assets/Animations/People - " +
                                 "the men will slide.");

            _crews = gameObject.AddComponent<DemoCrews>();
            _crews.EveryoneArmed = true;
            _crews.MuzzleFlashPrefab = CrewKit.MuzzleFlash;
            _crews.BloodPrefab = CrewKit.Blood;
            _crews.ImpactPrefab = CrewKit.Impact;
            _crews.GunshotClip = CrewKit.Gunshot;
            _crews.CrackClip = CrewKit.Crack;
            // the outfit gathers at the WEST end of the street, on the near sidewalk,
            // facing the road, its crews strung along the kerb - a long walk from the
            // cafes, and well out of any rival's alert range until sent
            var muster = new Vector3(-streetHalfLength + 16f, 0f, streetZ - 7.6f);
            _crews.StreetZ = streetZ; // the lane a drive-by keeps to
            _crews.InitFree(clips, null, anchor: muster, facing: Vector3.forward,
                spread: crewSpread, groundY: 0f);

            SpawnRivals();
            ParkCar(muster);
            BuildTraffic();
#else
            Debug.LogError("[CrewDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        bool _carGiven;

        void Update()
        {
            GiveCarToFirstLieutenant();

            // the grip is set by eye: change the numbers in the Inspector during Play
            // and every gun re-seats itself on the spot
            if (_crews == null || (gripNudge == _seenNudge && gripTilt == _seenTilt)) return;
            _seenNudge = CrewArms.GripNudge = gripNudge;
            _seenTilt = CrewArms.GripTilt = gripTilt;
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                    if (man.Armed) man.Arm(man.WeaponPrefab, man.WeaponKind);
        }

        // The demo hands the outfit's car to its first lieutenant itself - through
        // the director, so the books agree - rather than making the player open the
        // ledger and click GIVE before he can drive anywhere. Once, when the roster is in.
        void GiveCarToFirstLieutenant()
        {
            if (_carGiven) return;
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return;
            var roster = director.Roster;
            if (roster.Crews.Count == 0) { _carGiven = true; return; }
            foreach (var item in roster.Equipment)
            {
                if (item.Kind != EquipmentKind.Vehicle) continue;
                if (item.HolderId == LivingCity.Personnel.RosterEquipment.Unheld)
                    director.GiveEquipment(item.Id, roster.Crews[0].LieutenantId);
                break;
            }
            _carGiven = true;
        }

        // ------------------------------------------------------------------ the set

        /// <summary>The road demo's street profile (StreetKit): two lanes with a 5 m
        /// sidewalk each side - 20 m across outside the kerb strips.</summary>
        const float StreetHalfWidth = StreetKit.OuterHalf;
        const string CafeA = "Assets/CityKit/Buildings/building-cafe.prefab";
        const string CafeB = "Assets/CityKit/Buildings/building-coffeeshop.prefab";

        readonly List<Vector3> _cafeFronts = new List<Vector3>(); // where each cafe's door faces the street

        // Two slabs of floor either side of the street, so the road tiles are the ground there.
        void BuildFloor()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material mat = null;
            if (shader)
            {
                mat = new Material(shader) { name = "Crew Demo Floor" };
                mat.SetColor("_BaseColor", floorColour);
                mat.SetFloat("_Smoothness", 0.12f);
            }
            float half = floorSize * 0.5f;
            Slab("Floor South", -half, streetZ - StreetHalfWidth, mat);
            Slab("Floor North", streetZ + StreetHalfWidth, half, mat);
        }

        void Slab(string name, float zFrom, float zTo, Material mat)
        {
            if (zTo <= zFrom) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = name;
            floor.transform.position = new Vector3(0f, 0f, (zFrom + zTo) * 0.5f);
            floor.transform.localScale = new Vector3(floorSize / 10f, 1f, (zTo - zFrom) / 10f);
            if (mat) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // One straight two-way street along X, laid tile by tile the road demo's way
        // (sidewalk, two lanes of yellow-lined asphalt, sidewalk), sunk a tenth so the
        // pavement tops sit level with the floor the men walk on. Two cafes stand on
        // the far side, fronts to the kerb, a rival crew loafing in front of each.
        void BuildStreet()
        {
#if UNITY_EDITOR
            GameObject Load(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // the road demo's street, tiles and dressing alike (StreetKit), sunk a
            // tenth so its pavement tops sit level with the floor the men walk on
            var root = new GameObject("Streetscape").transform;
            float cz = streetZ;
            var kit = new StreetKit(root, y: -0.1f);
            if (!kit.LayAlongX(cz, -streetHalfLength, streetHalfLength))
                return;

            // the cafes on the north side, fronts (the catalogue's normalised +Z)
            // turned to face the street, footprints flush with the back of the kerb
            _cafeFronts.Clear();
            var cafes = new[] { Load(CafeA), Load(CafeB) };
            for (int i = 0; i < 2; i++)
            {
                var prefab = cafes[i] ?? cafes[1 - i];
                if (!prefab) continue;
                float x = (i - 0.5f) * cafeSpread;
                // the shopfront to the street: the mesh says which side it is on (an
                // override by name wins), that side is turned to +Z, then the whole
                // building about-faces so +Z looks south across the kerb
                var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, root);
                go.name = prefab.name;
                var front = FacadeFinder.FrontOf(go, out var reading);
                foreach (var o in facadeOverrides)
                    if (o.building == prefab.name) { front = o.front; reading += " (override)"; }
                Debug.Log("[CrewDemo] " + prefab.name + ": " + reading);
                go.transform.rotation = Quaternion.Euler(0f, FacadeFinder.YawToPlusZ(front) + 180f, 0f);
                var b = BoundsOf(go);
                if (b.size.sqrMagnitude > 0f)
                {
                    // footprint edge on the back of the kerb; the bake's own ground stays
                    // at 0 - these are built with the ground at y = 0 and some carry a
                    // slab or a cellar under it (the cafe reaches 2 m down), and lifting
                    // that to the floor hangs the whole building in the air
                    var target = new Vector3(x, 0f, cz + StreetHalfWidth) +
                                 new Vector3(-b.center.x, 0f, -b.min.z);
                    go.transform.position += target;
                }
                _cafeFronts.Add(new Vector3(x, 0f, cz + StreetHalfWidth));
            }
#endif
        }

        // The outfit's wheels: a Gang Warfare low car pulled in at the kerb a few
        // strides east of the muster, nose along the street. The body for the ledger's
        // car - whichever crew the book assigns it to can get in and drive it (CrewCar).
        void ParkCar(Vector3 muster)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(crimeCar)) return;
            var prefab = FindVehicle(crimeCar);
            if (!prefab)
            {
                Debug.LogWarning("[CrewDemo] No car prefab " + crimeCar + " in any Synty vehicle folder.");
                return;
            }
            // the near lane, kerb side (the street is sunk a tenth: asphalt at -0.08);
            // DemoCrews owns it from here - binds it to the ledger's car, and lets the
            // crew the book gives it to get in and drive
            var at = new Vector3(muster.x + 16f, -0.08f, streetZ - 3.6f);
            var car = _crews.AddCar(prefab, at, Quaternion.Euler(0f, 90f, 0f), roadY: -0.08f);
            if (car != null && blackSedan) DressBlack(car.Tf);
#endif
        }

        // Pack cars on the street both ways - something for the crew's car to follow,
        // brake for, swing round in front of and squeeze past when parked.
        void BuildTraffic()
        {
            if (trafficCars <= 0) return;
            var bodies = new List<GameObject>();
            foreach (var name in new[]
            {
                "SM_Veh_Car_Sedan_01", "SM_Veh_Car_Medium_01", "SM_Veh_Car_Small_01", "SM_Veh_Car_Muscle_01",
                "SM_Veh_Sedan_01", "SM_Veh_Suv_01", "SM_Veh_Pickup_01", "SM_Veh_LowCar_01", "SM_Veh_LowCar_02",
            })
            {
                var p = FindVehicle(name);
                if (p) bodies.Add(p);
            }
            if (bodies.Count == 0)
            {
                Debug.LogWarning("[CrewDemo] No pack cars found for traffic.");
                return;
            }
            var traffic = gameObject.AddComponent<StreetTraffic>();
            traffic.Init(bodies, streetZ, -streetHalfLength + 10f, streetHalfLength - 10f, -0.08f, trafficCars);
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
            cam.farClipPlane = 800f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var dc = camGo.AddComponent<DemoCamera>();
            dc.pivot = new Vector3(-streetHalfLength + 30f, 0f, streetZ - 6f);
            dc.distance = 74f;
            dc.yaw = 0f;
            dc.pitch = 50f;
            dc.hintTopPx = 104f; // under the crew bar
            dc.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                      "left-click one of ours: select his crew   right-click floor: walk there   " +
                      "right-click a rival: attack   P: ledger";
        }

        // ------------------------------------------------------------------ rivals

        void SpawnRivals()
        {
            var rng = new System.Random(nameSeed);
            var gangNames = LivingCity.Gangs.GangCatalog.Names;
            var bossModels = LivingCity.Gangs.GangCatalog.LieutenantModels;
            var soldierModels = LivingCity.Gangs.GangCatalog.SoldierModels;

            // the rival line: across from ours, facing us, the same spread
            // one crew per cafe - each guards its own door, and the two doors stand
            // a cafeSpread apart, well outside either crew's alert range of the other
            int count = Mathf.Clamp(rivalCrews, 1, Mathf.Min(gangNames.Length - 1, Mathf.Max(1, _cafeFronts.Count)));
            var arms = new[]
            {
                ("SM_Wep_Pistol_Revolver_01", EquipmentKind.Pistol),
                ("SM_Wep_Machine_Pistol_01", EquipmentKind.MachinePistol),
                ("SM_Wep_Shotgun_01", EquipmentKind.Shotgun),
                ("SM_Wep_SubMachineGun_01", EquipmentKind.TommyGun),
            };

            for (int i = 0; i < count; i++)
            {
                int gang = 1 + i; // 0 is the outfit
                var bossPrefab = Cast(bossModels[gang % bossModels.Length]);
                var hoodPrefab = Cast(soldierModels[gang % soldierModels.Length]);
                if (bossPrefab == null)
                {
                    Debug.LogWarning("[CrewDemo] No body for the " + gangNames[gang] + " lieutenant (" +
                                     bossModels[gang % bossModels.Length] + ") - that crew sits out.");
                    continue;
                }

                var hoodNames = new List<string>();
                for (int k = 0; k < rivalHoods; k++) hoodNames.Add(DrawName(rng));
                var (weaponName, kind) = arms[i % arms.Length];
                var weapon = CrewKit.Weapon(weaponName);
                if (weapon == null)
                    Debug.LogWarning("[CrewDemo] Gun " + weaponName + " not found - the " +
                                     gangNames[gang] + " crew comes unarmed.");

                var front = _cafeFronts.Count > i
                    ? _cafeFronts[i]
                    : new Vector3((i - (count - 1) * 0.5f) * cafeSpread, 0f, streetZ + StreetHalfWidth);
                // on the pavement in front of the door, backs to the cafe, facing the street
                // between the kerb strip (lamps, palms at 6.15) and the outer strip (9.1)
                var anchor = front + new Vector3(0f, 0f, -(StreetHalfWidth - 7.6f));
                _crews.AddRival(gang, gangNames[gang], DrawName(rng), bossPrefab, hoodNames,
                    hoodPrefab != null ? new List<GameObject> { hoodPrefab } : new List<GameObject>(),
                    anchor, Vector3.back, weapon, kind, lineUp: true);
            }
        }

        /// <summary>The plain pack body of this name - the ledger's baked cast first (no
        /// crowd scripts to strip), the PrefabDatabase's street copy as the fallback.</summary>
        static GameObject Cast(string name) =>
            LivingCity.UI.LedgerModelSet.PersonNamed(name) ??
            LivingCity.UI.PortraitStudio.FindPeoplePrefab(name);

        static string DrawName(System.Random rng)
        {
            var firsts = LivingCity.Entities.PedestrianIdentity.AllMaleNames;
            var surnames = LivingCity.Entities.PedestrianIdentity.AllSurnames;
            return firsts[rng.Next(firsts.Count)] + " " + surnames[rng.Next(surnames.Count)];
        }
    }
}
