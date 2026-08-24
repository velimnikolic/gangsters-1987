using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// A turntable photograph of one vehicle prefab, for looking at a bake without
    /// opening a scene. Four views - front three-quarter, side, rear three-quarter and
    /// top - into one PNG, lit by a single key so the flat Synty faces still separate.
    ///
    /// It works in a scene of its own, opened ADDITIVE and closed again, because the
    /// editor usually has the city loaded and a photograph is not worth losing it.
    /// </summary>
    public static class VehicleShot
    {
        public static void Shoot(string prefabPath, string outPath, int side = 640, Vector3[] views = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogError("[VehicleShot] nothing at " + prefabPath); return; }

            // In play mode the editor refuses NewScene, and a photograph is not worth
            // stopping a run for, so the runtime path builds the same empty scene.
            var playing = Application.isPlaying;
            var scene = playing
                ? UnityEngine.SceneManagement.SceneManager.CreateScene("VehicleShot")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Vector3.zero;

            var lightGo = new GameObject("Key");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            lightGo.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var camGo = new GameObject("Shot");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.66f, 0.70f);
            cam.orthographic = true;

            var angles = views ?? new[]
            {
                new Vector3(18f, 215f, 0f),   // front three-quarter
                new Vector3(4f, 270f, 0f),    // side
                new Vector3(18f, 330f, 0f),   // rear three-quarter
                new Vector3(78f, 215f, 0f),   // top
            };

            // THE SHEET IS AS BIG AS THE VIEWS ASKED FOR. It was a fixed two-by-two and
            // the quadrant was worked out as (i % 2, 1 - i / 2), so a caller who passed
            // five views wrote off the bottom of the texture and a caller who passed
            // three left a quarter of it uninitialised. `views` is a parameter; it has
            // to answer for any length.
            int cols = angles.Length <= 1 ? 1 : 2;
            int rows = Mathf.CeilToInt(angles.Length / (float)cols);

            // AND THE FRAME IS AS BIG AS THE VEHICLE. A fixed 2.4 fits a saloon and cuts
            // the ends off a bus, which is the one thing a photograph of a bake must not
            // do quietly. Measured off the prefab, exactly as gangsters_measure reads it.
            var bounds = Measure(go);
            var centre = bounds.center;
            cam.orthographicSize = Mathf.Max(0.5f, bounds.extents.magnitude * 1.05f);

            var sheet = new Texture2D(side * cols, side * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < angles.Length; i++)
            {
                camGo.transform.rotation = Quaternion.Euler(angles[i]);
                camGo.transform.position = centre - camGo.transform.forward * (cam.orthographicSize * 6f);

                var rt = RenderTexture.GetTemporary(side, side, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var was = RenderTexture.active;
                RenderTexture.active = rt;
                var shot = new Texture2D(side, side, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, side, side), 0, 0);
                shot.Apply();
                RenderTexture.active = was;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);

                int x = (i % cols) * side;
                int y = (rows - 1 - i / cols) * side;
                sheet.SetPixels(x, y, side, side, shot.GetPixels());
                Object.DestroyImmediate(shot);
            }
            sheet.Apply();

            var folder = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            if (playing)
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(camGo);
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            }
            else EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[VehicleShot] " + prefabPath + " -> " + outPath);
        }

        /// <summary>Everything the prefab draws, in its own space. Renderers only: a
        /// collider or an empty marker is not part of the picture, and one of those left
        /// out at the far end of the yard would frame the vehicle as a speck.</summary>
        static Bounds Measure(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(new Vector3(0f, 1.2f, 0f), Vector3.one * 4.8f);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
