using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Timed wrapper around the demo's runtime asset loads.
    ///
    /// This demo is editor-only and pulls its prefabs straight out of the
    /// AssetDatabase, which is fine at build time - but several call sites are LAZY:
    /// they fire the first time a system needs its prop, minutes into Play. An
    /// AssetDatabase load is native editor code, so it appears in NO profiler marker;
    /// a slow one reads as a frame that simply stopped, with no CPU in it and nothing
    /// allocated. Loading one prefab pulls its whole dependency chain (materials,
    /// textures, meshes) and, on a machine whose page cache has been swapped out, that
    /// chain comes off disk.
    ///
    /// So every lazy load goes through here and says so when it is dear. Costs one
    /// Stopwatch per load; the threshold keeps the log quiet.
    /// </summary>
    public static class DemoAssetLoad
    {
        const long ReportMs = 50;

        static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        static readonly System.Collections.Generic.Dictionary<(System.Type, string), Object> PlayAssets =
            new System.Collections.Generic.Dictionary<(System.Type, string), Object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlayCache() => PlayAssets.Clear();

        public static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var key = (typeof(T), path);
            if (Application.isPlaying && PlayAssets.TryGetValue(key, out var cached))
                return cached as T;
            Clock.Restart();
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Clock.Stop();
            if (Application.isPlaying && asset != null) PlayAssets[key] = asset;
            if (Clock.ElapsedMilliseconds > ReportMs)
                Debug.Log($"[assetload] {Clock.ElapsedMilliseconds} ms  {typeof(T).Name}  {path}");
            return asset;
#else
            return null;
#endif
        }

        /// <summary>The folder-scoped search form.</summary>
        public static string[] Find(string filter, string[] folders)
        {
#if UNITY_EDITOR
            Clock.Restart();
            var guids = UnityEditor.AssetDatabase.FindAssets(filter, folders);
            Clock.Stop();
            if (Clock.ElapsedMilliseconds > ReportMs)
                Debug.Log($"[assetload] {Clock.ElapsedMilliseconds} ms  FindAssets(\"{filter}\", {folders.Length} folders) -> {guids.Length} hits");
            return guids;
#else
            return new string[0];
#endif
        }

        /// <summary>The project-wide search form - far dearer than a path load, because
        /// it walks the whole asset index (this project holds ~137,000 assets).</summary>
        public static string[] Find(string filter)
        {
#if UNITY_EDITOR
            Clock.Restart();
            var guids = UnityEditor.AssetDatabase.FindAssets(filter);
            Clock.Stop();
            if (Clock.ElapsedMilliseconds > ReportMs)
                Debug.Log($"[assetload] {Clock.ElapsedMilliseconds} ms  FindAssets(\"{filter}\") -> {guids.Length} hits");
            return guids;
#else
            return new string[0];
#endif
        }
    }
}
