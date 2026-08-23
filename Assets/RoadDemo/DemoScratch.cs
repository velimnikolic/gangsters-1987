using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace RoadDemo
{
    /// <summary>
    /// Everything a demo builds for itself and nobody must ever save: showroom ranks,
    /// benches, ground planes, plaques, spilled fires.
    ///
    /// The flag those objects carry, HideFlags.DontSaveInEditor, does two things and
    /// only one of them was wanted. It keeps the preview out of the scene file - good -
    /// and it ALSO tells Unity not to destroy the object when the scene it was built in
    /// is closed. So the rank outlived its own scene: open a new scene and nine
    /// motorcycles are still standing there, off every hierarchy, on a magenta floor
    /// (the ground's material was made in code without the flag, so THAT was unloaded
    /// on the way out and the mesh is left with nothing to render with).
    ///
    /// Hence this class. Unsaved() stamps as well as flags, and the editor sweeps every
    /// stamped object at the moments a scene changes hands. The builders keep their own
    /// Clear() on the ordinary path; this is the net under it, for the one moment they
    /// deliberately do nothing.
    /// </summary>
    public static class DemoScratch
    {
        /// <summary>Flag one of a demo's own objects so no save can pick it up - and
        /// stamp it so the sweep below can still find it after its scene is gone.</summary>
        public static void Unsaved(GameObject go)
        {
            if (!go) return;
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
#if UNITY_EDITOR
            if (!go.TryGetComponent<DemoScratchMark>(out _)) go.AddComponent<DemoScratchMark>();
#endif
        }

#if UNITY_EDITOR
        /// <summary>Names the demos gave their stages before the stamp existed. Strays
        /// already standing in a session are only reachable by name.</summary>
        static readonly string[] OldStages =
        {
            "Moto Showroom", "Bike Bench", "Bike Spill Show",
        };

        /// <summary>Destroy every scratch object that is no longer part of a live scene.
        /// Called when a scene is opened, created or closed, and around Play.</summary>
        public static void Sweep()
        {
            foreach (var mark in Resources.FindObjectsOfTypeAll<DemoScratchMark>())
            {
                if (!mark) continue;
                var go = mark.gameObject;
                if (EditorUtility.IsPersistent(go)) continue;   // a prefab on disk, never ours
                if (Alive(go)) continue;
                Kill(go);
            }
            SweepOldStages();
        }

        /// <summary>The same sweep by name, for ranks built by the code that had no
        /// stamp. BY NAME AND NOTHING WIDER, deliberately: "any unparented object with
        /// DontSaveInEditor and no scene" also describes the Scene view's own camera.</summary>
        public static void SweepOldStages()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go || EditorUtility.IsPersistent(go)) continue;
                if (go.transform.parent != null) continue;
                if ((go.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
                if (System.Array.IndexOf(OldStages, go.name) < 0) continue;
                if (Alive(go)) continue;
                Kill(go);
            }
        }

        /// <summary>True while the object still belongs to a scene somebody has open -
        /// the builder that made it is running and will clear it itself.</summary>
        static bool Alive(GameObject go)
        {
            var scene = go.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        [MenuItem("Tools/Demos/Sweep stray demo objects")]
        static void SweepFromMenu()
        {
            Sweep();
            Debug.Log("[DemoScratch] swept every demo object left over from a closed scene.");
        }

        /// <summary>The scene-change hooks. A builder's OnDisable cannot do this itself:
        /// at that moment its own scene is already going, and the objects it would have
        /// to reach are the very ones Unity has decided to keep.</summary>
        [InitializeOnLoad]
        static class Watch
        {
            static Watch()
            {
                // AFTER the change, never before: a stage is still part of its own live
                // scene while that scene is closing, and the sweep leaves live scenes
                // alone. The object only becomes scratch the instant the scene is gone.
                EditorSceneManager.sceneOpened -= Opened;
                EditorSceneManager.sceneOpened += Opened;
                EditorSceneManager.newSceneCreated -= Created;
                EditorSceneManager.newSceneCreated += Created;
                EditorSceneManager.sceneClosed -= Closed;
                EditorSceneManager.sceneClosed += Closed;
                EditorApplication.playModeStateChanged -= Play;
                EditorApplication.playModeStateChanged += Play;
                // a domain reload can land with strays already standing from before it
                EditorApplication.delayCall += Sweep;
            }

            static void Opened(Scene scene, OpenSceneMode mode) => Sweep();
            static void Created(Scene scene, NewSceneSetup setup, NewSceneMode mode) => Sweep();
            static void Closed(Scene scene) => Sweep();

            // entering Play: the preview would be duplicated by the rebuild that starts a
            // moment later; leaving it: whatever Play spilled is now scene-less scratch
            static void Play(PlayModeStateChange change)
            {
                if (change == PlayModeStateChange.EnteredPlayMode ||
                    change == PlayModeStateChange.EnteredEditMode) Sweep();
            }
        }
#endif
    }
}
