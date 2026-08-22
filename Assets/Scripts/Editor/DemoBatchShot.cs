using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// A demo scene run from the command line and photographed: opens the scene,
    /// enters Play, lets it settle for a few seconds, renders the main camera to a
    /// PNG and quits. So a builder can be checked without anyone at the keyboard.
    ///
    ///   Unity.exe -projectPath . -batchmode -executeMethod LivingCity.EditorTools.DemoBatchShot.Run
    ///             -shotScene Assets/Scenes/HarborDemo.unity -shotOut out.png
    ///             [-shotSeconds 6] [-shotWidth 1920] [-shotHeight 1080]
    ///             [-shotPitch 40 -shotYaw 0 -shotDistance 200 -shotPivot 0,0,40]
    ///
    /// Play mode reloads the domain, so the request is parked in SessionState and
    /// picked up again by the static constructor after the reload. No -nographics:
    /// the camera has to render.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoBatchShot
    {
        const string Pending = "DemoBatchShot.Pending";
        const string OutKey = "DemoBatchShot.Out";
        const string SecondsKey = "DemoBatchShot.Seconds";
        const string WidthKey = "DemoBatchShot.Width";
        const string HeightKey = "DemoBatchShot.Height";
        const string ViewKey = "DemoBatchShot.View";

        static DemoBatchShot()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying) Arm();
        }

        public static void Run()
        {
            string scene = Arg("-shotScene") ?? "Assets/Scenes/HarborDemo.unity";
            string outPath = Arg("-shotOut") ?? "demo-shot.png";
            SessionState.SetBool(Pending, true);
            SessionState.SetString(OutKey, Path.GetFullPath(outPath));
            SessionState.SetFloat(SecondsKey, float.TryParse(Arg("-shotSeconds"), out var s) ? s : 6f);
            SessionState.SetInt(WidthKey, int.TryParse(Arg("-shotWidth"), out var w) ? w : 1920);
            SessionState.SetInt(HeightKey, int.TryParse(Arg("-shotHeight"), out var h) ? h : 1080);
            SessionState.SetString(ViewKey, string.Join("|", Arg("-shotPitch") ?? "", Arg("-shotYaw") ?? "",
                                                          Arg("-shotDistance") ?? "", Arg("-shotPivot") ?? ""));
            Debug.Log($"[DemoBatchShot] {scene} -> {outPath}");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            // The scene as the BUILDER would make it, not as its inspector happens to be
            // saved: without this the tool photographs whatever the last person left in
            // the fields, which for BlockDemo is a quarter with no crew, no motorcycle
            // and no mission - a picture of nothing, taken to check something.
            // Same shape as the play harness's -hSet (PlayHarness.ApplySet).
            foreach (var set in Args("-shotSet")) ApplySet(set);
            // give the bakes that hang off play-mode changes their moment first
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }


        /// <summary>"Type.field=value" against every MonoBehaviour of that type in the
        /// open scene. Handles the kinds a builder's inspector actually has.</summary>
        static void ApplySet(string set)
        {
            int eq = set.IndexOf('=');
            int dot = set.IndexOf('.');
            if (eq < 0 || dot < 0 || dot > eq) { Debug.LogWarning($"[DemoBatchShot] cannot read {set}"); return; }
            string type = set.Substring(0, dot);
            string field = set.Substring(dot + 1, eq - dot - 1);
            string value = set.Substring(eq + 1);

            int hits = 0;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != type) continue;
                var f = mb.GetType().GetField(field,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (f == null) { Debug.LogWarning($"[DemoBatchShot] {type} has no field {field}"); return; }
                object parsed =
                    f.FieldType == typeof(int) ? (object)int.Parse(value)
                    : f.FieldType == typeof(float) ? (object)float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                    : f.FieldType == typeof(bool) ? (object)(value == "1" || value.ToLowerInvariant() == "true")
                    : f.FieldType == typeof(string) ? (object)value
                    : null;
                if (parsed == null) { Debug.LogWarning($"[DemoBatchShot] cannot read {value} as {f.FieldType.Name}"); return; }
                f.SetValue(mb, parsed);
                EditorUtility.SetDirty(mb);
                hits++;
            }
            Debug.Log(hits > 0 ? $"[DemoBatchShot] set {set} on {hits}"
                               : $"[DemoBatchShot] nothing of type {type} in the scene");
        }

        static System.Collections.Generic.List<string> Args(string name)
        {
            var found = new System.Collections.Generic.List<string>();
            var argv = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length - 1; i++)
                if (argv[i] == name)
                    foreach (var one in argv[i + 1].Split(';'))
                        if (one.Trim().Length > 0) found.Add(one.Trim());
            return found;
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Arm();
        }

        static double _t0 = -1;
        static bool _armed;

        static void Arm()
        {
            if (_armed) return;
            _armed = true;
            _t0 = -1;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_t0 < 0)
            {
                _t0 = EditorApplication.timeSinceStartup;
                PointCamera();
            }
            // the player clock, not the editor's: a batch run may render slowly
            if (Time.timeSinceLevelLoad < SessionState.GetFloat(SecondsKey, 6f)) return;
            EditorApplication.update -= Tick;
            try { Capture(); }
            catch (System.Exception e) { Debug.LogException(e); }
            SessionState.SetBool(Pending, false);
            EditorApplication.Exit(0);
        }

        static void PointCamera()
        {
            var parts = SessionState.GetString(ViewKey, "|||").Split('|');
            var dc = Object.FindAnyObjectByType<RoadDemo.DemoCamera>();
            if (dc == null) return;
            if (parts.Length > 0 && float.TryParse(parts[0], out var pitch)) dc.pitch = pitch;
            if (parts.Length > 1 && float.TryParse(parts[1], out var yaw)) dc.yaw = yaw;
            if (parts.Length > 2 && float.TryParse(parts[2], out var dist)) dc.distance = dist;
            if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
            {
                var v = parts[3].Split(',');
                if (v.Length == 3 && float.TryParse(v[0], out var x) && float.TryParse(v[1], out var y) && float.TryParse(v[2], out var z))
                    dc.pivot = new Vector3(x, y, z);
            }
        }

        static void Capture()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[DemoBatchShot] no main camera"); return; }
            int w = SessionState.GetInt(WidthKey, 1920), h = SessionState.GetInt(HeightKey, 1080);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string outPath = SessionState.GetString(OutKey, "demo-shot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Debug.Log($"[DemoBatchShot] wrote {outPath}");
            Object.DestroyImmediate(tex);
            rt.Release();
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
