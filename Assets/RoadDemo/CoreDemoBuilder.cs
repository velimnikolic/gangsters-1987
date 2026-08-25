using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The core's own scene: one component that stands <see cref="CoreDistrict"/> up at
    /// the origin and hands it a <see cref="StandaloneDistrictHost"/> for the sun, the
    /// camera, the pause keys and the perf pass. The quarter ITSELF is the same object
    /// the city builds, so what is changed here is what the city gets.
    ///
    /// The fields are the district's own, out on the inspector for trying things - and
    /// for the play harness, which writes them before the scene wakes up.
    /// </summary>
    public class CoreDemoBuilder : MonoBehaviour
    {
        public int seed = 1987;

        [Header("Traffic")]
        public int carCount = 24;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        public float alleySpeed = 5f;

        void Awake()
        {
#if UNITY_EDITOR
            // a sketch left in the scene from the editor menu would stand under the quarter
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == CoreLayout.SketchRoot) Destroy(root);

            var district = new CoreDistrict
            {
                carCount = carCount,
                streetSpeed = streetSpeed,
                boulevardSpeed = boulevardSpeed,
                alleySpeed = alleySpeed,
            };

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            host.cameraDistance = 320f;
            host.cameraYaw = 20f;
            host.cameraPitch = 55f;
            host.cameraFar = 2500f;
            host.skyboxSky = true;
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(district, seed);
#else
            Debug.LogError("[CoreDemo] The core loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
