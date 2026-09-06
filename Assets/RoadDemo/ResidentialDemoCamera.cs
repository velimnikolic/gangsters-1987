using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadDemo
{
    /// <summary>
    /// Thin runtime adapter that gives the generated ResidentialDemo the production
    /// street camera. The review scene is intentionally not edited or regenerated:
    /// entering Play installs <see cref="DemoCamera"/> on its existing main camera and
    /// frames whatever residential roots are currently saved there.
    /// </summary>
    public static class ResidentialDemoCamera
    {
        public const string SceneName = "ResidentialDemo";
        const string RootPrefix = "RESIDENTIAL ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallAfterSceneLoad() => Install(SceneManager.GetActiveScene());

        /// <summary>Install once and leave an explicitly wired camera untouched.</summary>
        public static DemoCamera Install(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.name != SceneName)
                return null;

            var camera = MainCamera(scene);
            if (camera == null)
            {
                Debug.LogWarning("[ResidentialDemo] No camera found; shared controls were not installed.");
                return null;
            }

            var existing = camera.GetComponent<DemoCamera>();
            if (existing != null)
                return existing;

            var rig = camera.gameObject.AddComponent<DemoCamera>();
            var view = CityViewConfig.Resolve();
            rig.mapTransition = false;
            rig.ConfigurePitch(view.StreetPitch, view.PitchFreedom);
            rig.SetMaxGroundHeight(view.Max3DDistance);
            rig.yaw = 20f;
            rig.showHint = false;

            if (ContentBounds(scene, out var bounds))
            {
                rig.pivot = new Vector3(bounds.center.x, 0f, bounds.center.z);
                float span = Mathf.Max(bounds.size.x, bounds.size.z);
                rig.mapCeiling = Mathf.Max(260f, span * 1.35f);
                rig.FrameSpan(span, 0.95f);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, rig.mapCeiling * 3f);
            }
            else
            {
                rig.mapCeiling = 400f;
                rig.distance = 165f;
            }

            camera.fieldOfView = 45f;
            var shadows = camera.GetComponent<DemoShadows>();
            if (shadows == null)
                shadows = camera.gameObject.AddComponent<DemoShadows>();
            shadows.rig = rig;
            return rig;
        }

        static Camera MainCamera(Scene scene)
        {
            Camera fallback = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var cameras = root.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++)
                {
                    var camera = cameras[i];
                    if (camera == null) continue;
                    if (fallback == null) fallback = camera;
                    if (camera.CompareTag("MainCamera")) return camera;
                }
            }
            return fallback;
        }

        static bool ContentBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith(RootPrefix, StringComparison.Ordinal))
                    continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null) continue;
                    var candidate = renderer.bounds;
                    if (!Usable(candidate)) continue;
                    if (!found)
                    {
                        bounds = candidate;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(candidate);
                    }
                }
            }
            return found;
        }

        static bool Usable(Bounds bounds)
        {
            var centre = bounds.center;
            var size = bounds.size;
            return size.sqrMagnitude > 0.0001f &&
                   Finite(centre.x) && Finite(centre.y) && Finite(centre.z) &&
                   Finite(size.x) && Finite(size.y) && Finite(size.z);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
