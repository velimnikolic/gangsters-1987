using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadDemo
{
    /// <summary>
    /// Every review scene the editor lays out - a showroom, a catalog, a bench - gets
    /// the production street camera the moment Play starts: WASD/arrows pan, Q/E turn,
    /// wheel zoom, right-drag orbit. The saved scene keeps a plain camera and is never
    /// edited for this; the rig is installed on it at Play, framed on the scene's
    /// content root, exactly the way <see cref="ResidentialDemoCamera"/> does it for the
    /// residential review scene. A scene is one row in <see cref="Scenes"/>; adding a
    /// showroom means adding a row, not a camera script.
    /// </summary>
    public static class ReviewSceneCamera
    {
        /// <summary>What a review scene wants from the camera: which root to frame, how
        /// steep to look (the shared 22-82° street policy still bounds it), and how close
        /// the wheel may come - a shopfront is looked at from the pavement, not a roof.</summary>
        public readonly struct Setup
        {
            public readonly string Scene;
            public readonly string ContentRoot;
            public readonly float Pitch;
            public readonly float MinDistance;

            public Setup(string scene, string contentRoot, float pitch, float minDistance)
            {
                Scene = scene;
                ContentRoot = contentRoot;
                Pitch = pitch;
                MinDistance = minDistance;
            }
        }

        public static readonly Setup[] Scenes =
        {
            new Setup("ShopDemo", "SHOPS", pitch: 30f, minDistance: 4f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallAfterSceneLoad() => Install(SceneManager.GetActiveScene());

        /// <summary>Install once; a camera that already carries the rig is left alone.
        /// Returns null for a scene that is not a review scene.</summary>
        public static DemoCamera Install(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            int row = Array.FindIndex(Scenes, s => s.Scene == scene.name);
            if (row < 0)
                return null;
            var setup = Scenes[row];

            var camera = MainCamera(scene);
            if (camera == null)
            {
                Debug.LogWarning($"[{scene.name}] No camera found; shared controls were not installed.");
                return null;
            }

            var existing = camera.GetComponent<DemoCamera>();
            if (existing != null)
                return existing;

            var rig = camera.gameObject.AddComponent<DemoCamera>();
            rig.mapTransition = false;
            rig.minDistance = setup.MinDistance;
            rig.ConfigurePitch(setup.Pitch, 10f);
            rig.yaw = 0f;
            rig.showHint = true;

            if (ContentBounds(scene, setup.ContentRoot, out var bounds))
            {
                // open on the FRONT row - the one the scene was built to show - with
                // the rest of the field behind it; the wheel and WASD reach the rest
                float span = Mathf.Max(bounds.size.x, bounds.size.z);
                rig.pivot = new Vector3(bounds.center.x, 0f, bounds.min.z + 4f);
                rig.mapCeiling = Mathf.Max(260f, span * 1.35f);
                rig.FrameSpan(bounds.size.x, 0.55f, floor: 30f);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, rig.mapCeiling * 3f);
            }
            else
            {
                rig.mapCeiling = 400f;
                rig.distance = 120f;
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

        static bool ContentBounds(Scene scene, string rootName, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != rootName)
                    continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    // the floating captions are not content: framing them would
                    // pull the pivot up to the headers
                    if (renderer == null || renderer.GetComponent<TextMesh>() != null) continue;
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
