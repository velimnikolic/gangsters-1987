using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>
    /// Plays a scene with nobody watching.
    ///
    /// Unity is started headless on the project (Tools/play/run.ps1), this method is
    /// the entry point, and what comes out the far end is a directory: the trace
    /// (every driver, every man on foot, every shot, a line each - DriveTrace), the
    /// editor log, a summary, and a few PNGs of what the camera saw. The point is a
    /// LOOP: run, read the numbers, change the driving code, run again, without a
    /// person having to sit through it.
    ///
    /// Time is stepped in fixed slices (Time.captureDeltaTime), so the run neither
    /// waits on the wall clock nor varies with how fast the machine happens to be:
    /// ninety seconds of city is ninety seconds of city, and two runs of one seed
    /// line up row for row.
    ///
    /// Command line, all optional but the scene:
    ///   -hScene Assets/Scenes/BlockDemo.unity
    ///   -hOut   [directory]         where the run is written
    ///   -hSeconds 90                sim seconds to play
    ///   -hStep  0.0333              the fixed slice
    ///   -hSample 0.1                seconds between per-car samples
    ///   -hWarm  2                   sim seconds before the trace opens
    ///   -hShot  20                  a PNG every so many sim seconds (0: none)
    ///   -hWall  900                 real seconds before the run is abandoned
    ///   -hSet   BlockDemoBuilder.carCount=12    (repeatable) fields set before Play
    /// </summary>
    public static class PlayHarness
    {
        const string CfgKey = "PlayHarness.Cfg";
        const string ArmedKey = "PlayHarness.Armed";

        [Serializable]
        public class Cfg
        {
            public string scene = "Assets/Scenes/BlockDemo.unity";
            public string outDir = "";
            public float seconds = 90f;
            public float step = 1f / 30f;
            public float sample = 0.1f;
            public float warm = 2f;
            public float shot = 0f;
            public float wall = 900f;
            public int width = 1600;
            public int height = 900;
            public List<string> sets = new List<string>();
            /// <summary>Batch runs leave through EditorApplication.Exit, which is the only
            /// way out of -batchmode. A run driven from a live editor (the CLI's
            /// gangsters_play) must not take the editor down with it, so it clears this
            /// and leaves play mode instead.</summary>
            public bool quit = true;
        }

        // ------------------------------------------------------------------ entry

        public static void Run() => RunWith(Parse(Environment.GetCommandLineArgs()));

        /// <summary>The run itself, given a config rather than a command line: the batch
        /// entry above and the editor command both come through here, and the only
        /// difference between them is <see cref="Cfg.quit"/>.</summary>
        public static void RunWith(Cfg cfg)
        {
            if (string.IsNullOrEmpty(cfg.outDir))
                cfg.outDir = Path.Combine(Path.GetTempPath(), "playharness");
            Directory.CreateDirectory(cfg.outDir);

            Say(cfg, $"[harness] scene {cfg.scene}, {cfg.seconds:F0}s at {cfg.step * 1000f:F0}ms, out {cfg.outDir}");

            try
            {
                var scene = EditorSceneManager.OpenScene(cfg.scene, OpenSceneMode.Single);
                if (!scene.IsValid()) { Fail(cfg, "the scene would not open"); return; }
                foreach (var set in cfg.sets) ApplySet(cfg, set);
            }
            catch (Exception e)
            {
                Fail(cfg, "opening the scene threw: " + e);
                return;
            }

            SessionState.SetString(CfgKey, JsonUtility.ToJson(cfg));
            SessionState.SetBool(ArmedKey, true);
            Hold();
            _armedAt = DateTime.UtcNow;
            EditorApplication.update -= Watchdog;
            EditorApplication.update += Watchdog;
            EditorApplication.EnterPlaymode();
            // and the editor loop takes it from here: OnPlayMode below fires once the
            // domain has reloaded and the scene is live
        }

        // Batchmode shuts the editor down the moment the -executeMethod call returns,
        // and a run has barely begun by then: the quit is refused until the driver says
        // it is finished (it leaves through EditorApplication.Exit, which nothing can
        // refuse). Re-hung after every domain reload, since a delegate does not survive one.
        static bool _letGo;
        static DateTime _armedAt;

        public static void LetGo() => _letGo = true;

        /// <summary>How a run ends. In batch that is the editor's exit code; in a live
        /// editor it is only the end of play mode - killing the user's editor because a
        /// soak finished would be a poor trade.</summary>
        internal static void Leave(int code)
        {
            var cfg = JsonUtility.FromJson<Cfg>(SessionState.GetString(CfgKey, "{}")) ?? new Cfg();
            if (cfg.quit) EditorApplication.Exit(code);
            else { Debug.Log($"[harness] the run is over (code {code}); the editor stays up"); EditorApplication.isPlaying = false; }
        }

        static void Hold()
        {
            EditorApplication.wantsToQuit -= Stay;
            EditorApplication.wantsToQuit += Stay;
        }

        static bool Stay()
        {
            if (_letGo || !SessionState.GetBool(ArmedKey, false)) return true;
            Debug.Log("[harness] a quit was asked for, and refused: the run is not over");
            return false;
        }

        /// <summary>If the driver never comes up at all - a scene that will not play, a
        /// reload that went wrong - the run is abandoned rather than left hanging.</summary>
        static void Watchdog()
        {
            if (!SessionState.GetBool(ArmedKey, false)) { EditorApplication.update -= Watchdog; return; }
            if (UnityEngine.Object.FindAnyObjectByType<HarnessDriver>() != null) { EditorApplication.update -= Watchdog; return; }
            if ((DateTime.UtcNow - _armedAt).TotalSeconds < 600) return;
            Debug.LogError("[harness] the play driver never came up - giving in");
            _letGo = true;
            SessionState.SetBool(ArmedKey, false);
            Leave(5);
        }

        [InitializeOnLoadMethod]
        static void Rearm()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            Debug.Log("[harness] rearmed after a domain reload");
            Hold();
            if (_armedAt == default) _armedAt = DateTime.UtcNow;
            EditorApplication.update -= Watchdog;
            EditorApplication.update += Watchdog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (Application.isPlaying) StartDriver();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) StartDriver();
        }

        static void StartDriver()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            if (UnityEngine.Object.FindAnyObjectByType<HarnessDriver>() != null) return;
            var cfg = JsonUtility.FromJson<Cfg>(SessionState.GetString(CfgKey, "{}")) ?? new Cfg();
            Debug.Log("[harness] the run has begun");
            var go = new GameObject("~PlayHarness");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<HarnessDriver>().Begin(cfg);
        }

        // ------------------------------------------------------------------ arguments

        static Cfg Parse(string[] argv)
        {
            var cfg = new Cfg();
            for (int i = 0; i < argv.Length; i++)
            {
                string a = argv[i];
                string next = i + 1 < argv.Length ? argv[i + 1] : null;
                switch (a)
                {
                    case "-hScene": cfg.scene = next; i++; break;
                    case "-hOut": cfg.outDir = next; i++; break;
                    case "-hSeconds": cfg.seconds = F(next, cfg.seconds); i++; break;
                    case "-hStep": cfg.step = F(next, cfg.step); i++; break;
                    case "-hSample": cfg.sample = F(next, cfg.sample); i++; break;
                    case "-hWarm": cfg.warm = F(next, cfg.warm); i++; break;
                    case "-hShot": cfg.shot = F(next, cfg.shot); i++; break;
                    case "-hWall": cfg.wall = F(next, cfg.wall); i++; break;
                    case "-hWidth": cfg.width = (int)F(next, cfg.width); i++; break;
                    case "-hHeight": cfg.height = (int)F(next, cfg.height); i++; break;
                    case "-hSet": if (next != null) cfg.sets.Add(next); i++; break;
                }
            }
            return cfg;
        }

        static float F(string s, float fallback)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        /// <summary>"BlockDemoBuilder.carCount=12": the field on every component of that
        /// type in the open scene, before anything has woken up.</summary>
        static void ApplySet(Cfg cfg, string set)
        {
            int eq = set.IndexOf('=');
            int dot = set.IndexOf('.');
            if (eq < 0 || dot < 0 || dot > eq) { Say(cfg, $"[harness] cannot read -hSet {set}"); return; }
            string type = set.Substring(0, dot);
            string field = set.Substring(dot + 1, eq - dot - 1);
            string value = set.Substring(eq + 1);

            int hits = 0;
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != type) continue;
                var f = mb.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) { Say(cfg, $"[harness] {type} has no field {field}"); return; }
                object parsed = Coerce(f.FieldType, value);
                if (parsed == null) { Say(cfg, $"[harness] cannot read {value} as {f.FieldType.Name}"); return; }
                f.SetValue(mb, parsed);
                EditorUtility.SetDirty(mb);
                hits++;
            }
            Say(cfg, hits > 0 ? $"[harness] set {set} on {hits}" : $"[harness] nothing of type {type} in the scene");
        }

        static object Coerce(Type t, string v)
        {
            try
            {
                if (t == typeof(int)) return int.Parse(v, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(bool)) return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (t == typeof(string)) return v;
                // an enum by name, so a run can be told which gun the outfit carries
                if (t.IsEnum) return Enum.Parse(t, v, true);
                if (t == typeof(int[]))
                {
                    if (v.Length == 0) return new int[0];
                    var parts = v.Split(',');
                    var a = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++) a[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
                    return a;
                }
                if (t == typeof(float[]))
                {
                    if (v.Length == 0) return new float[0];
                    var parts = v.Split(',');
                    var a = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++) a[i] = float.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                    return a;
                }
            }
            catch { }
            return null;
        }

        static void Say(Cfg cfg, string line)
        {
            Debug.Log(line);
            try { File.AppendAllText(Path.Combine(cfg.outDir, "harness.log"), line + "\n"); } catch { }
        }

        static void Fail(Cfg cfg, string why)
        {
            Say(cfg, "[harness] FAILED: " + why);
            SessionState.SetBool(ArmedKey, false);
            if (cfg.quit) EditorApplication.Exit(4);
        }
    }

    /// <summary>The thing that actually sits through the run: steps the clock, keeps
    /// the trace clock with it, takes the pictures, and shuts Unity down when the run
    /// is over (or when it has plainly hung).</summary>
    public class HarnessDriver : MonoBehaviour
    {
        PlayHarness.Cfg _cfg;
        float _sim, _startedReal, _nextShot, _lastReport;
        int _frames, _shots, _errors, _exceptions;
        StreamWriter _log;
        bool _traceOpen, _done;

        public void Begin(PlayHarness.Cfg cfg)
        {
            _cfg = cfg;
            _startedReal = Time.realtimeSinceStartup;
            Directory.CreateDirectory(cfg.outDir);
            _log = new StreamWriter(Path.Combine(cfg.outDir, "play.log"), false) { AutoFlush = false };
            Application.logMessageReceived += OnLog;
            // fixed slices: the run is sim time, not machine time
            Time.captureDeltaTime = Mathf.Max(0.001f, cfg.step);
            Spare();
            Line($"[harness] play begun, step {Time.captureDeltaTime:F4}");
        }

        /// <summary>Nobody is watching, so nothing is drawn: the cameras are switched
        /// off (a picture still renders on demand - Camera.Render does not need the
        /// camera enabled), the shadows and the far LODs go, and the street is silent.
        /// Roughly three sim seconds for one real second becomes ten or more, which is
        /// the difference between four runs an hour and forty.</summary>
        void Spare()
        {
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.lodBias = 0.3f;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            Application.targetFrameRate = -1;
            AudioListener.volume = 0f;
            AudioListener.pause = true;
            _cams.Clear();
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.enabled && cam.targetTexture == null) { _cams.Add(cam); cam.enabled = false; }
            Line($"[harness] {_cams.Count} cameras off, shadows off, sound off");
        }

        readonly List<Camera> _cams = new List<Camera>();

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert) _errors++;
            if (type == LogType.Exception) _exceptions++;
            Line($"{_sim:F2} {type}: {message}");
            if (type == LogType.Exception || type == LogType.Error)
            {
                Line(stack);
                DriveTrace.Event("log", type.ToString(), message);
            }
        }

        void Line(string s)
        {
            if (_log == null) return;
            _log.WriteLine(s);
            if (Time.frameCount % 60 == 0) _log.Flush();
        }

        void Update()
        {
            if (_done) return;
            _frames++;
            _sim += Time.deltaTime;
            DriveTrace.Now = _sim;

            if (!_traceOpen && _sim >= _cfg.warm)
            {
                DriveTrace.Open(Path.Combine(_cfg.outDir, "trace.jsonl"), _cfg.sample);
                DriveTrace.Now = _sim;
                _traceOpen = true;
                Line($"[harness] trace open at {_sim:F1}s");
                _nextShot = _cfg.shot > 0f ? _sim : float.MaxValue;
            }

            if (_sim >= _nextShot) { Shot(); _nextShot = _sim + Mathf.Max(1f, _cfg.shot); }

            if (_sim - _lastReport >= 10f)
            {
                _lastReport = _sim;
                Line($"[harness] {_sim:F0}s sim, {_frames} frames, {Time.realtimeSinceStartup - _startedReal:F0}s real, " +
                     $"{DriveTrace.Rows} rows, belt {RoadCar.BeltHits}, errors {_errors}/{_exceptions}");
                DriveTrace.Flush();
                _log.Flush();
            }

            // a run throwing every frame is telling us something, and it is not worth
            // another ten minutes of sim to hear it again
            if (_exceptions > 300) Finish(6, $"{_exceptions} exceptions - the run is broken");
            else if (_sim >= _cfg.seconds) Finish(0, "done");
            else if (Time.realtimeSinceStartup - _startedReal > _cfg.wall) Finish(3, "wall clock ran out");
        }

        void Shot()
        {
            try
            {
                Camera cam = _cams.Count > 0 ? _cams[0] : Camera.main;
                if (cam == null)
                {
                    var all = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                    foreach (var c in all) if (c.targetTexture == null) { cam = c; break; }
                }
                if (cam == null) return;
                var rt = new RenderTexture(_cfg.width, _cfg.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                var was = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = was;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(_cfg.width, _cfg.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, _cfg.width, _cfg.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(_cfg.outDir, $"shot-{_shots:D2}-{_sim:F0}s.png"), tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
                Destroy(rt);
                _shots++;
            }
            catch (Exception e) { Line("[harness] no picture: " + e.Message); }
        }

        void Finish(int code, string why)
        {
            _done = true;
            Line($"[harness] {why} at {_sim:F1}s sim / {Time.realtimeSinceStartup - _startedReal:F0}s real, " +
                 $"{_frames} frames, {DriveTrace.Rows} rows, belt hits {RoadCar.BeltHits}, errors {_errors}, exceptions {_exceptions}");
            var summary = "{" +
                $"\"why\":\"{why}\",\"sim\":{_sim.ToString("F1", CultureInfo.InvariantCulture)}," +
                $"\"real\":{(Time.realtimeSinceStartup - _startedReal).ToString("F1", CultureInfo.InvariantCulture)}," +
                $"\"frames\":{_frames},\"rows\":{DriveTrace.Rows},\"beltHits\":{RoadCar.BeltHits}," +
                $"\"timesReal\":{(_sim / Mathf.Max(0.01f, Time.realtimeSinceStartup - _startedReal)).ToString("F1", CultureInfo.InvariantCulture)}," +
                $"\"errors\":{_errors},\"exceptions\":{_exceptions},\"shots\":{_shots}" + "}";
            try { File.WriteAllText(Path.Combine(_cfg.outDir, "summary.json"), summary); } catch { }
            DriveTrace.Close();
            Application.logMessageReceived -= OnLog;
            _log?.Flush();
            _log?.Dispose();
            _log = null;
            Time.captureDeltaTime = 0f;
            SessionState.SetBool("PlayHarness.Armed", false);
            PlayHarness.LetGo();
            Debug.Log($"[harness] {why} - {_sim:F0}s played, {_errors} errors, {_exceptions} exceptions");
            PlayHarness.Leave(code);
        }
    }
}
