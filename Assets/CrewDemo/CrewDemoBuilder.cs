using System.Collections.Generic;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CrewDemo
{
    // An empty floor and two lines of men: the outfit's crews out of the ledger on
    // the near side, a couple of rival lieutenants with their hoods on the far one,
    // a standoff apart. Left-click one of ours to select his crew, right-click the
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

        [Header("Layout")]
        [Tooltip("Metres between the outfit's line and the rivals'.")]
        public float standoff = 44f;
        [Tooltip("Metres between neighbouring crews on the outfit's line.")]
        public float crewSpread = 11f;
        [Tooltip("Metres between the rival crews - wide, so each fight can be watched on its own.")]
        public float rivalSpread = 26f;
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
            _crews.InitFree(clips, null,
                anchor: new Vector3(0f, 0f, -standoff * 0.5f), facing: Vector3.forward,
                spread: crewSpread, groundY: 0f);

            SpawnRivals();
#else
            Debug.LogError("[CrewDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        void Update()
        {
            // the grip is set by eye: change the numbers in the Inspector during Play
            // and every gun re-seats itself on the spot
            if (_crews == null || (gripNudge == _seenNudge && gripTilt == _seenTilt)) return;
            _seenNudge = CrewArms.GripNudge = gripNudge;
            _seenTilt = CrewArms.GripTilt = gripTilt;
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                    if (man.Armed) man.Arm(man.WeaponPrefab, man.WeaponKind);
        }

        // ------------------------------------------------------------------ the set

        void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(floorSize / 10f, 1f, floorSize / 10f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader)
            {
                var mat = new Material(shader) { name = "Crew Demo Floor" };
                mat.SetColor("_BaseColor", floorColour);
                mat.SetFloat("_Smoothness", 0.12f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
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
            dc.pivot = Vector3.zero;
            dc.distance = 62f;
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
            int count = Mathf.Clamp(rivalCrews, 1, gangNames.Length - 1);
            var right = Vector3.right;
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

                float x = (i - (count - 1) * 0.5f) * rivalSpread;
                var anchor = new Vector3(0f, 0f, standoff * 0.5f) + right * x;
                _crews.AddRival(gang, gangNames[gang], DrawName(rng), bossPrefab, hoodNames,
                    hoodPrefab != null ? new List<GameObject> { hoodPrefab } : new List<GameObject>(),
                    anchor, Vector3.back, weapon, kind);
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
