using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace RoadDemo
{
    // Where the frame goes. Every few seconds it writes what the profiler
    // recorders say about the last window - frame time spread, the main-thread
    // stages, the render thread, GC - to Logs/perf-probe.txt, so a stutter can be
    // read off a file instead of guessed at from the feel of it. Costs next to
    // nothing itself; remove the component to switch it off.
    public class DemoPerfProbe : MonoBehaviour
    {
        const float WindowSeconds = 5f;

        static readonly (string label, ProfilerCategory cat, string marker)[] Markers =
        {
            ("main thread", ProfilerCategory.Internal, "Main Thread"),
            ("PlayerLoop", ProfilerCategory.Internal, "PlayerLoop"),
            ("scripts Update", ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"),
            ("scripts LateUpdate", ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            ("animators", ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationBegin"),
            ("animators end", ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationEnd"),
            ("canvases", ProfilerCategory.Gui, "PostLateUpdate.PlayerUpdateCanvases"),
            ("render (Camera.Render)", ProfilerCategory.Render, "Camera.Render"),
            ("culling", ProfilerCategory.Render, "CullResults.CreateSharedRendererScene"),
            ("shadows", ProfilerCategory.Render, "Shadows.RenderShadowMap"),
            ("gfx wait for present", ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            ("wait render thread", ProfilerCategory.Render, "Gfx.WaitForRenderThread"),
            ("semaphore wait", ProfilerCategory.Internal, "Semaphore.WaitForSignal"),
            ("GC.Collect", ProfilerCategory.Memory, "GC.Collect"),
            ("physics", ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
            ("audio", ProfilerCategory.Audio, "AudioManager.Update"),
            ("editor loop", ProfilerCategory.Internal, "EditorLoop"),
            ("URP render camera", ProfilerCategory.Render, "UniversalRenderPipeline.RenderSingleCameraInternal"),
            ("render loop", ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop_Internal"),
            ("finish frame rendering", ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering"),
            ("present", ProfilerCategory.Render, "PostLateUpdate.PresentAfterDraw"),
            ("wait target fps", ProfilerCategory.Internal, "WaitForTargetFPS"),
            ("gfx present", ProfilerCategory.Render, "Gfx.PresentFrame"),
            ("shadow draw", ProfilerCategory.Render, "Shadows.Draw"),
            ("main light shadowmap", ProfilerCategory.Render, "MainLightShadow"),
            ("SSAO", ProfilerCategory.Render, "SSAO"),
            ("depth prepass", ProfilerCategory.Render, "DepthPrepass"),
            ("gbuffer", ProfilerCategory.Render, "GBuffer"),
            ("deferred lighting", ProfilerCategory.Render, "Deferred Lighting"),
            ("gc alloc count", ProfilerCategory.Memory, "GC.Alloc"),
            ("mesh skinning", ProfilerCategory.Render, "MeshSkinning.Update"),
            ("particles", ProfilerCategory.Particles, "ParticleSystem.Update"),
            ("dir update", ProfilerCategory.Scripts, "Update.DirectorUpdate"),
            ("input", ProfilerCategory.Input, "InputSystem.Update"),
            ("UI events", ProfilerCategory.Gui, "EventSystem.Update"),
            ("SRP batcher draw", ProfilerCategory.Render, "SRPBatcher.Draw"),
            ("cull scene", ProfilerCategory.Render, "CullScene"),
            ("shadow culling", ProfilerCategory.Render, "CullShadowCasters"),
        };

        // What a frame CREATED. The marker table can say a stall was in "scripts
        // Update" but not WHICH component's Update - that bucket is one name for
        // every MonoBehaviour in the scene. The footprint tells on it instead: a
        // frame that also adds 600 GameObjects, or 400 materials, or a megabyte of
        // meshes, names its own system without anyone guessing. Counts only, all
        // cheap ProfilerRecorders; invalid names are dropped at Start like the rest.
        static readonly string[] CountMarkers =
        {
            "Game Object Count", "Object Count", "Total Object Count",
            "Material Count", "Texture Count", "Mesh Count", "Asset Count",
        };

        // WHERE the memory is. The stalls turned out to be paging, not compute - free RAM
        // never rose above 1 GB while they happened - so the question stopped being "which
        // Update is slow" and became "what is the process holding". These say it in Unity's
        // own accounting, per window, so the next cut is chosen off a number.
        static readonly string[] SizeMarkers =
        {
            "Total Used Memory", "System Used Memory", "GC Used Memory",
            "Gfx Used Memory", "Texture Memory", "Mesh Memory",
        };

        readonly List<(string label, ProfilerRecorder rec)> _sizes = new List<(string, ProfilerRecorder)>();
        readonly List<(string label, ProfilerRecorder rec)> _counts = new List<(string, ProfilerRecorder)>();
        readonly Dictionary<string, long> _prevCount = new Dictionary<string, long>();
        readonly List<(string label, long delta)> _countDeltas = new List<(string, long)>();

        // the slow frames, spelled out: which markers were big in that very frame
        readonly StringBuilder _slow = new StringBuilder();
        int _slowLogged;
        readonly List<(string, double)> _frameMarks = new List<(string, double)>();

        readonly List<(string label, ProfilerRecorder rec)> _recs = new List<(string, ProfilerRecorder)>();
        ProfilerRecorder _gcAlloc, _gcMemory, _drawCalls, _batches, _setPass, _tris, _verts;
        readonly List<float> _frames = new List<float>(1024);
        float _windowStart;
        string _path;
        int _windows;

        void Start()
        {
            _path = Path.Combine(Application.dataPath, "..", "Logs", "perf-probe.txt");
            foreach (var m in Markers)
            {
                var rec = ProfilerRecorder.StartNew(m.cat, m.marker, 1);
                if (rec.Valid) _recs.Add((m.label, rec)); else rec.Dispose();
            }
            foreach (var name in CountMarkers)
            {
                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name);
                if (rec.Valid) _counts.Add((name, rec)); else rec.Dispose();
            }
            foreach (var name in SizeMarkers)
            {
                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name);
                if (rec.Valid) _sizes.Add((name, rec)); else rec.Dispose();
            }
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _gcMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _windowStart = Time.unscaledTime;
            File.WriteAllText(_path, "perf probe\n");
        }

        // per-window accumulation: sum and max per marker (ns), gc bytes
        readonly Dictionary<string, (double sum, double max)> _acc = new Dictionary<string, (double, double)>();
        double _gcSum, _gcMax;

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames.Add(ms);
            _frameMarks.Clear();
            foreach (var (label, rec) in _recs)
            {
                double v = rec.LastValue / 1e6; // ns -> ms
                _acc.TryGetValue(label, out var a);
                _acc[label] = (a.sum + v, Mathf.Max((float)a.max, (float)v));
                _frameMarks.Add((label, v));
            }
            // deltas first, then the slow-frame line can quote them
            _countDeltas.Clear();
            foreach (var (label, rec) in _counts)
            {
                long now = rec.LastValue;
                if (_prevCount.TryGetValue(label, out var before) && now != before)
                    _countDeltas.Add((label, now - before));
                _prevCount[label] = now;
            }

            if (ms > 40f && _slowLogged < 8)
            {
                _slowLogged++;
                _frameMarks.Sort((x, y) => y.Item2.CompareTo(x.Item2));
                _slow.Append($"    slow frame {ms:F0} ms @ {Time.frameCount} (t+{Time.unscaledTime:F0}s): cameras {Camera.allCamerasCount}, gc {(int)(_gcAlloc.Valid ? _gcAlloc.LastValue / 1024 : 0)} KB;");
                for (int k = 0; k < Mathf.Min(6, _frameMarks.Count); k++)
                    if (_frameMarks[k].Item2 > 0.5) _slow.Append($" {_frameMarks[k].Item1} {_frameMarks[k].Item2:F1}");
                _countDeltas.Sort((x, y) => System.Math.Abs(y.delta).CompareTo(System.Math.Abs(x.delta)));
                for (int k = 0; k < Mathf.Min(4, _countDeltas.Count); k++)
                    _slow.Append($" [{_countDeltas[k].label} {_countDeltas[k].delta:+#;-#;0}]");
                _slow.AppendLine();
            }
            if (_gcAlloc.Valid)
            {
                double b = _gcAlloc.LastValue;
                _gcSum += b;
                if (b > _gcMax) _gcMax = b;
            }

            if (Time.unscaledTime - _windowStart < WindowSeconds) return;
            Flush();
        }

        void Flush()
        {
            int n = _frames.Count;
            if (n == 0) return;
            _frames.Sort();
            float avg = 0f;
            foreach (var f in _frames) avg += f;
            avg /= n;
            float p50 = _frames[n / 2], p90 = _frames[(int)(n * 0.9f)], p99 = _frames[(int)(n * 0.99f)], max = _frames[n - 1];
            int spikes = 0;
            foreach (var f in _frames) if (f > 40f) spikes++;

            var sb = new StringBuilder();
            sb.AppendLine($"--- window {++_windows} ({n} frames, {Time.unscaledTime:F0} s) hour {DemoClockHour():F1}");
            sb.AppendLine($"frame ms  avg {avg:F1}  p50 {p50:F1}  p90 {p90:F1}  p99 {p99:F1}  max {max:F1}   frames over 40 ms: {spikes}");
            sb.AppendLine($"gc alloc/frame avg {(_gcSum / n / 1024):F1} KB  max {(_gcMax / 1024):F0} KB   gc heap {(_gcMemory.Valid ? _gcMemory.LastValue / 1048576.0 : 0):F0} MB");
            if (_sizes.Count > 0)
            {
                var mem = new StringBuilder("memory ");
                foreach (var (label, rec) in _sizes)
                    mem.Append($" {label} {rec.LastValue / 1048576} MB;");
                sb.AppendLine(mem.ToString());
            }
            sb.AppendLine($"draw calls {(_drawCalls.Valid ? _drawCalls.LastValue : 0)}  batches {(_batches.Valid ? _batches.LastValue : 0)}  " +
                          $"setpass {(_setPass.Valid ? _setPass.LastValue : 0)}  tris {(_tris.Valid ? _tris.LastValue / 1000 : 0)}k  verts {(_verts.Valid ? _verts.LastValue / 1000 : 0)}k");
            CityBlockRecycler.AppendStats(sb);
            foreach (var (label, _) in _recs)
            {
                if (!_acc.TryGetValue(label, out var a) || a.max < 0.05) continue;
                sb.AppendLine($"  {label,-24} avg {a.sum / n,7:F2} ms   max {a.max,7:F2} ms");
            }
            sb.Append(_slow);
            File.AppendAllText(_path, sb.ToString());

            _slow.Clear();
            _slowLogged = 0;
            _frames.Clear();
            _acc.Clear();
            _gcSum = 0; _gcMax = 0;
            _windowStart = Time.unscaledTime;
        }

        static float DemoClockHour()
        {
            var clock = FindAnyObjectByType<DemoClock>();
            return clock ? clock.Hour : -1f;
        }

        void OnDestroy()
        {
            foreach (var (_, rec) in _recs) rec.Dispose();
            _gcAlloc.Dispose(); _gcMemory.Dispose(); _drawCalls.Dispose(); _batches.Dispose(); _setPass.Dispose(); _tris.Dispose(); _verts.Dispose();
        }
    }
}
