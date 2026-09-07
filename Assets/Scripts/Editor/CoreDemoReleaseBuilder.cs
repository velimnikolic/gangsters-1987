using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RoadDemo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>Builds the game and its path-addressed, lazily loaded content together.</summary>
    public static class CoreDemoReleaseBuilder
    {
        const string Output = "Builds/CoreDemo-Release";
        const string Content = "Builds/CoreDemoContent";
        const string Status = "Temp/CoreDemoRelease/build-result.json";
        static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".fbx", ".obj", ".mat", ".asset", ".anim", ".controller",
            ".overrideController", ".wav", ".ogg", ".mp3", ".shader", ".shadergraph",
            ".compute", ".png", ".tga", ".jpg", ".jpeg", ".ttf", ".otf", ".json"
        };

        [MenuItem("Tools/Build/CoreDemo Windows Release with FPS")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Status));
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Stop Play and complete script compilation before building.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                    throw new InvalidOperationException("Switch the active build target to StandaloneWindows64 first.");
                Directory.CreateDirectory(Output);
                Directory.CreateDirectory(Content);
                // GPU Resident Drawer requires these variants in both content and Player.
                var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
                var settings = new SerializedObject(graphics);
                settings.FindProperty("m_BrgStripping").intValue =
                    (int)UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll;
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(graphics);
                GenerateShaderLibrary();
                WriteStatus(new { status = "building content", startedUtc = DateTime.UtcNow });

                var paths = AssetDatabase.GetAllAssetPaths()
                    .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                        && !path.Contains("/Editor/") && !path.Contains("/Tests/")
                        && !path.StartsWith("Assets/_Recovery", StringComparison.Ordinal)
                        && !path.StartsWith("Assets/Temp/", StringComparison.Ordinal)
                        && Extensions.Contains(Path.GetExtension(path)))
                    .Where(IsRuntimeAsset).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var bundle = new AssetBundleBuild
                {
                    assetBundleName = PlayerAssetBundle.BundleName,
                    assetNames = paths
                };
                var manifest = BuildPipeline.BuildAssetBundles(Content, new[] { bundle },
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                    BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new InvalidOperationException("CoreDemo content bundle build failed; read the current Editor console.");
                File.WriteAllText(Path.Combine(Content, PlayerAssetBundle.IndexName),
                    JsonUtility.ToJson(new PlayerAssetBundle.Index { paths = paths }));

                WriteStatus(new { status = "building player", startedUtc = DateTime.UtcNow, assetCount = paths.Length });
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/CoreDemo.unity" },
                    locationPathName = Output + "/Gangsters.exe",
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.DetailedBuildReport,
                    extraScriptingDefines = new[] { "GANGSTERS_FPS_COUNTER" },
                    assetBundleManifestPath = Content + "/CoreDemoContent.manifest"
                };
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result == BuildResult.Succeeded)
                {
                    var streaming = Path.Combine(Output, "Gangsters_Data", "StreamingAssets");
                    Directory.CreateDirectory(streaming);
                    foreach (string name in new[] { PlayerAssetBundle.BundleName, PlayerAssetBundle.IndexName })
                        File.Copy(Path.Combine(Content, name), Path.Combine(streaming, name), true);
                }
                var summary = report.summary;
                var result = new
                {
                    status = "completed",
                    result = summary.result.ToString(),
                    platform = summary.platform.ToString(),
                    output = summary.outputPath,
                    scenes = options.scenes,
                    options = summary.options.ToString(),
                    fpsCounter = true,
                    development = (summary.options & BuildOptions.Development) != 0,
                    assetCount = paths.Length,
                    contentBytes = new FileInfo(Path.Combine(Content, PlayerAssetBundle.BundleName)).Length,
                    seconds = summary.totalTime.TotalSeconds,
                    errors = summary.totalErrors,
                    warnings = summary.totalWarnings,
                    completedUtc = DateTime.UtcNow,
                    messages = report.steps.SelectMany(step => step.messages)
                        .Where(message => message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                        .Select(message => new { type = message.type.ToString(), message.content }).ToArray()
                };
                WriteStatus(result);
                File.WriteAllText(Output + "/build-info.json", JsonConvert.SerializeObject(result, Formatting.Indented));
                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"CoreDemo Player build {summary.result}: {summary.totalErrors} errors.");
                Debug.Log("[CoreDemo release] " + Path.GetFullPath(Output + "/Gangsters.exe"));
            }
            catch (Exception exception)
            {
                WriteStatus(new { status = "completed", result = "Failed", error = exception.ToString() });
                throw;
            }
        }

        static bool IsRuntimeAsset(string path)
        {
            if (path.Contains("/PerformanceTestRun")) return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && !typeof(MonoScript).IsAssignableFrom(type)
                && !(type.Namespace ?? "").StartsWith("UnityEditor", StringComparison.Ordinal);
        }

        static void GenerateShaderLibrary()
        {
            const string path = "Assets/Resources/CoreDemoShaders.asset";
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var pattern = new System.Text.RegularExpressions.Regex("Shader\\.Find\\(\\s*\"([^\"]+)\"");
            foreach (string source in Directory.EnumerateFiles("Assets", "*.cs", SearchOption.AllDirectories))
            {
                if (source.Replace('\\', '/').Contains("/Editor/")) continue;
                foreach (System.Text.RegularExpressions.Match match in pattern.Matches(File.ReadAllText(source)))
                    names.Add(match.Groups[1].Value);
            }
            var shaders = names.Select(Shader.Find).Where(shader => shader != null).Distinct().ToArray();
            foreach (string required in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit" })
                if (!shaders.Any(shader => shader.name == required))
                    throw new InvalidOperationException("Required procedural shader is missing: " + required);
            var library = AssetDatabase.LoadAssetAtPath<PlayerShaderLibrary>(path);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<PlayerShaderLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }
            library.shaders = shaders;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
        }

        static void WriteStatus(object result) =>
            File.WriteAllText(Status, JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
