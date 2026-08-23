using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlockDemo
{
    // The artist's own downtown, standing in our scene.
    //
    // Assets/CityKit/Downtown/synty-downtown.prefab is the POLYGON City demo scene
    // lifted out whole (SyntyDemoBlockRip): four and a half thousand pieces, laid on
    // a 5 m grid by the person who made the models - alleys a car can drive down,
    // parking in front and behind, fire escapes down the side returns, hoardings on
    // the roofs. This scene stands it up with our sun, our clock, our grade and our
    // camera, so what a screenshot shows is their arrangement under our light.
    //
    // It is the strict centre of the city. What goes round it is OUR grid carrying
    // the blocks cut out of that same demo (Assets/CityKit/Blocks/synty_*), dealt
    // over the lots at random - which is why the two halves are separate scenes for
    // now: this one is the thing being copied, the lab is the thing copying it.
    //
    // This class may set fields and stand one prefab. It may not build a city.
    public class SyntyTownBuilder : MonoBehaviour
    {
        const string TownPath = "Assets/CityKit/Downtown/synty-downtown.prefab";

        [Header("The look")]
        [Tooltip("PolygonCity's own demo sun: pitch 50, yaw 212, intensity 1.5, shadows 0.8.")]
        public Vector3 sunAngles = new Vector3(50f, 212f, 0f);
        public float sunIntensity = 1.5f;
        [Range(0f, 1f)] public float sunShadowStrength = 0.8f;
        public DemoGrade.Look look = DemoGrade.Look.PolygonCity;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 11f;
        public float realSecondsPerGameHour = 15f;

        void Awake()
        {
#if UNITY_EDITOR
            var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(TownPath);
            if (prefab == null) { Debug.LogError("[SyntyTown] missing " + TownPath); return; }

            var town = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            town.name = "Downtown";

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = sunIntensity;
            sun.color = new Color(1f, 0.9569f, 0.8392f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Mathf.Clamp01(sunShadowStrength);
            sunGo.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, sunAngles.z);

            var day = new GameObject("DayNight");
            var clock = day.AddComponent<DemoClock>();
            clock.secondsPerGameHour = Mathf.Max(0.02f, realSecondsPerGameHour);
            clock.startHour = startHour;
            var sky = day.AddComponent<DemoSky>();
            sky.clock = clock;
            sky.sun = sun;
            var grade = day.AddComponent<DemoGrade>();
            grade.clock = clock;
            grade.look = look;

            // the town as it really stands: the pack's painted skyline flat and its
            // skydome are scenery on the horizon and either one, measured, puts the
            // camera half a kilometre out with the city a smudge in the middle
            var box = Ground(town.transform);

            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 4000f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            var rig = camGo.AddComponent<DemoCamera>();
            rig.pivot = new Vector3(box.center.x, 0f, box.center.z);
            rig.FrameSpan(Mathf.Max(box.size.x, box.size.z), fill: 0.6f);
            rig.pitch = 48f;
            rig.yaw = 30f;
            rig.showHint = true;

            Debug.Log($"[SyntyTown] {town.transform.childCount} pieces standing, " +
                      $"{box.size.x:F0} x {box.size.z:F0} m of ground, boom {rig.distance:F0} m");
#else
            Debug.LogError("[SyntyTown] loads through the AssetDatabase and only runs in the editor.");
#endif
        }

        /// <summary>The ground the town covers, ignoring the horizon scenery: anything
        /// wider than a block of the city is a painted backdrop, not a building.</summary>
        static Bounds Ground(Transform town)
        {
            bool any = false;
            var box = new Bounds();
            foreach (Transform child in town)
            {
                var rs = child.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                if (b.size.x > 400f || b.size.z > 400f) continue;
                if (!any) { box = b; any = true; } else box.Encapsulate(b);
            }
            return any ? box : new Bounds(Vector3.zero, new Vector3(350f, 0f, 310f));
        }
    }
}
