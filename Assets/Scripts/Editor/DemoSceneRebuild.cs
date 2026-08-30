using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The two benches are dealt afresh the moment they are opened.
    ///
    /// Both scenes are GENERATED - the core demo builds its quarter at Play and the
    /// residential demo is its review blocks written out by the menu - so what is on disk is
    /// always yesterday's deal. Looking at yesterday's deal is how a whole evening went by
    /// with the user asking why he could not see a single cafe or shop in a block: the
    /// units had been harvested and the recipe changed, and the scene he was looking at had
    /// been stood before either (the user, 2026-08-28: "napravi da kad god otvorim coredemo
    /// ili residentialblocks da se uvek izgenerise od nule sve").
    ///
    /// So: open <c>CoreDemo</c> and the core city is drawn into it from a seed nobody chose;
    /// open <c>ResidentialDemo</c> and its residential set plus functional pump block are dealt again. The core's drawing is
    /// marked <see cref="HideFlags.DontSave"/> and the scene's dirty flag is cleared after
    /// it, so a city of a hundred thousand pieces is never written into a 5 KB scene file
    /// and nothing asks to save on the way out.
    ///
    /// Turned off from Tools/City/Demo/Rebuild Demo Scenes On Open when somebody wants to
    /// look at what is on disk instead.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoSceneRebuild
    {
        const string Pref = "Gangsters.RebuildDemosOnOpen";
        const string Menu = "Tools/City/Demo/Rebuild Demo Scenes On Open";
        const string CoreScene = "Assets/Scenes/CoreDemo.unity";

        /// <summary>One rebuild at a time: the residential bench writes its own scene out,
        /// and that must not be read as somebody opening it again.</summary>
        static bool _busy;

        static DemoSceneRebuild()
        {
            EditorSceneManager.sceneOpened -= Opened;
            EditorSceneManager.sceneOpened += Opened;
            EditorSceneManager.sceneClosing -= Closing;
            EditorSceneManager.sceneClosing += Closing;
        }

        /// <summary>
        /// The core's drawing is taken down with the scene it was drawn into.
        ///
        /// <see cref="HideFlags.DontSave"/> keeps a thing out of the scene FILE and out of the
        /// scene's dirty flag - and also keeps it alive across a scene load, which is the one
        /// thing that was not wanted: the whole core city stayed standing in the editor after
        /// the residential bench was opened over it. So it is destroyed by hand, here and
        /// before every rebuild.
        /// </summary>
        static void Closing(Scene scene, bool removing) => Sweep();

        static void Sweep()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != RoadDemo.CoreLayout.SketchRoot) continue;
                if ((go.hideFlags & HideFlags.DontSave) == 0) continue;
                if (go.transform.parent != null) continue;
                Object.DestroyImmediate(go);
            }
        }

        static bool On => EditorPrefs.GetBool(Pref, true);

        [MenuItem(Menu, priority = 100)]
        static void Toggle() => EditorPrefs.SetBool(Pref, !On);

        [MenuItem(Menu, true)]
        static bool ToggleShown()
        {
            UnityEditor.Menu.SetChecked(Menu, On);
            return true;
        }

        static void Opened(Scene scene, OpenSceneMode mode)
        {
            if (!On || _busy || mode != OpenSceneMode.Single) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isBatchMode) return;

            string path = scene.path;
            if (path != CoreScene && path != ResidentialSketch.DemoScene) return;

            // AFTER the open has finished: a scene rebuilt inside the callback that opened it
            // is a scene the editor is still holding half open
            EditorApplication.delayCall += () =>
            {
                if (_busy) return;
                _busy = true;
                try
                {
                    Sweep();
                    if (path == CoreScene) Core();
                    else Residential();
                }
                finally { _busy = false; }
            };
        }

        /// <summary>The core city, drawn into the demo scene and kept out of its file.</summary>
        static void Core()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CoreScene) return;
            int seed = Random.Range(1, 1000000);
            if (CoreCitySketch.Draw(seed, quiet: true) == null) return;

            foreach (var root in scene.GetRootGameObjects())
                if (root.name == RoadDemo.CoreLayout.SketchRoot) root.hideFlags = HideFlags.DontSave;
            // the drawing itself is not saved (DontSave), but the sketch marked the scene
            // dirty on the way past. ClearSceneDirtiness is not public API in 6000.5, so it
            // is asked for by name and simply not called when it is not there - a scene that
            // stays dirty costs one "don't save" on the way out, and nothing else
            var clear = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            clear?.Invoke(null, new object[] { scene });
            Debug.Log($"[Demo] CoreDemo drawn again from seed {seed}. The drawing is not saved with the " +
                      "scene; Play replaces it with the quarter the builder stands.");
        }

        /// <summary>The residential bench, dealt again and written out - which is
        /// what that scene is for.</summary>
        static void Residential() => Debug.Log("[Demo] " + ResidentialSketch.Demo(Random.Range(1, 90000)));
    }
}
