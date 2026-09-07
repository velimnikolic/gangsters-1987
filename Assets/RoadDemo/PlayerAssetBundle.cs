using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadDemo
{
    /// <summary>Lazy access to the content produced by CoreDemoReleaseBuilder.</summary>
    public static class PlayerAssetBundle
    {
        public const string BundleName = "coredemo.assets";
        public const string IndexName = "coredemo-index.json";

        [Serializable]
        public sealed class Index { public string[] paths; }

        static AssetBundle _bundle;
        static string[] _paths;
        static bool _attempted;
        static readonly Dictionary<(Type, string), Object> Cache = new Dictionary<(Type, string), Object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Cache.Clear();
            if (_bundle != null) _bundle.Unload(false);
            _bundle = null;
            _paths = null;
            _attempted = false;
        }

        static bool Open()
        {
            if (_bundle != null) return true;
            if (_attempted) return false;
            _attempted = true;
            string folder = Application.streamingAssetsPath;
            string bundlePath = Path.Combine(folder, BundleName);
            string indexPath = Path.Combine(folder, IndexName);
            if (!File.Exists(bundlePath) || !File.Exists(indexPath))
                throw new FileNotFoundException("CoreDemo content is missing. Keep the complete build folder together.", bundlePath);
            _paths = JsonUtility.FromJson<Index>(File.ReadAllText(indexPath)).paths;
            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null) throw new InvalidOperationException("Unable to load CoreDemo content: " + bundlePath);
            Debug.Log($"[CoreDemo content] {_paths.Length} asset paths available.");
            return true;
        }

        public static T Load<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path) || !Open()) return null;
            var key = (typeof(T), path);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached as T;
            if (!_bundle.Contains(path)) return null;
            T asset;
            if (typeof(T) == typeof(AnimationClip) || typeof(T) == typeof(Mesh))
            {
                var assets = _bundle.LoadAssetWithSubAssets<T>(path);
                asset = Array.Find(assets, value => value != null && !value.name.StartsWith("__preview__", StringComparison.Ordinal));
            }
            else asset = _bundle.LoadAsset<T>(path);
            if (asset != null) Cache[key] = asset;
            return asset;
        }

        public static Object[] LoadAll(string path)
        {
            if (string.IsNullOrEmpty(path) || !Open() || !_bundle.Contains(path)) return Array.Empty<Object>();
            return _bundle.LoadAssetWithSubAssets<Object>(path);
        }

        public static string[] Find(string filter, string[] folders)
        {
            if (!Open()) return Array.Empty<string>();
            var matches = new List<string>();
            var tokens = (filter ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in _paths)
            {
                if (folders != null && folders.Length > 0 && !Array.Exists(folders,
                    folder => path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool match = true;
                foreach (string token in tokens)
                {
                    if (token.Equals("t:Prefab", StringComparison.OrdinalIgnoreCase))
                        match &= path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                    else if (token.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Unpackaged asset search type: " + token);
                    else match &= name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (match) matches.Add(path);
            }
            return matches.ToArray();
        }
    }
}
