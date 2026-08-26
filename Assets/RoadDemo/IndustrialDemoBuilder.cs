using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The industrial quarter's own scene: one component that stands
    /// <see cref="IndustrialDistrict"/> up at the origin and hands it a
    /// <see cref="StandaloneDistrictHost"/> for the sun, the camera, the pause keys and the
    /// perf pass.
    ///
    /// The quarter ITSELF is the same object the city would build, so what is changed here
    /// is what the city gets - the rule this project keeps for every district
    /// (Docs/city-districts-plan.md). The fields are the district's own, out on the
    /// inspector for trying things, and for the play harness, which writes them before the
    /// scene wakes up.
    /// </summary>
    public class IndustrialDemoBuilder : MonoBehaviour
    {
        public int seed = 1987;

        [Header("Traffic")]
        [Tooltip("Lorries, vans and cars on the quarter's roads. Eighteen is what the " +
                 "estate carries without a queue standing at a junction.")]
        public int carCount = 18;
        public float streetSpeed = 9f;
        public float arterySpeed = 13f;
        [Range(0f, 1f)]
        [Tooltip("How much of the traffic is a lorry rather than a van or a car.")]
        public float lorryShare = 0.5f;

        void Awake()
        {
#if UNITY_EDITOR
            // a sketch left in the scene from the editor menu would stand under the quarter
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == IndustrialQuarter.SketchRoot) Destroy(root);

            var district = new IndustrialDistrict
            {
                carCount = carCount,
                streetSpeed = streetSpeed,
                arterySpeed = arterySpeed,
                lorryShare = lorryShare,
            };

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            host.cameraDistance = 380f;
            host.cameraYaw = 20f;
            host.cameraPitch = 55f;
            host.cameraFar = 2500f;
            host.skyboxSky = true;
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(district, seed);
#else
            Debug.LogError("[IndustrialDemo] The quarter loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
