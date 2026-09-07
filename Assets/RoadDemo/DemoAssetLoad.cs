using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Shared path-based loading: AssetDatabase in the Editor, packaged content in
    /// a Player. Lazy requests keep the same paths and load only their dependencies.
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
            return PlayerAssetBundle.Load<T>(path);
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
            return PlayerAssetBundle.Find(filter, folders);
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
            return PlayerAssetBundle.Find(filter, null);
#endif
        }

        public static string GUIDToAssetPath(string guid)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
#else
            // Player searches return canonical asset paths instead of editor GUIDs.
            return guid;
#endif
        }

        public static Object[] LoadAllAssetRepresentationsAtPath(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
#else
            return PlayerAssetBundle.LoadAll(path);
#endif
        }
    }
}
