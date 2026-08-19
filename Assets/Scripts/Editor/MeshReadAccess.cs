using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Opens Read/Write on the pack meshes the road demo needs to merge. The demo
    /// folds its still geometry into a few hundred meshes on its first frame
    /// (RoadDemoBuilder.MergeStaticGeometry); a mesh whose import forbids CPU access
    /// cannot be combined, so the demo notes such meshes in Logs/unreadable-meshes.txt
    /// and this, on the editor's next reload (or Tools/City/Open Mesh Read Access),
    /// finds their FBX files and turns readability on. One reimport, once.
    /// </summary>
    public static class MeshReadAccess
    {
        static string ListPath => Path.Combine(Application.dataPath, "..", "Logs", "unreadable-meshes.txt");

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            if (!File.Exists(ListPath)) return;
            EditorApplication.delayCall += () => Open(quiet: true);
        }

        [MenuItem("Tools/City/Open Mesh Read Access")]
        static void OpenMenu() => Open(quiet: false);

        static void Open(bool quiet)
        {
            if (!File.Exists(ListPath))
            {
                if (!quiet) Debug.Log("[MeshReadAccess] nothing listed in " + ListPath);
                return;
            }
            var names = File.ReadAllLines(ListPath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();

            // every FBX under the packs, by file name; a mesh is found under its own
            // name or, for a sub-part (SM_Prop_Trash_Bin_02_Lid_01), the longest
            // prefix that is a file
            var byName = new Dictionary<string, List<string>>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Synty" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var stem = Path.GetFileNameWithoutExtension(path);
                if (!byName.TryGetValue(stem, out var list)) byName[stem] = list = new List<string>();
                list.Add(path);
            }

            var files = new HashSet<string>();
            var unmatched = new List<string>();
            foreach (var n in names)
            {
                var parts = n.Split('_');
                bool hit = false;
                for (int k = parts.Length; k >= 2 && !hit; k--)
                {
                    var cand = string.Join("_", parts.Take(k));
                    if (byName.TryGetValue(cand, out var paths)) { files.UnionWith(paths); hit = true; }
                }
                if (!hit) unmatched.Add(n);
            }

            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var path in files)
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            File.Delete(ListPath);
            Debug.Log($"[MeshReadAccess] {names.Count} mesh names -> {files.Count} model files, {changed} opened for reading" +
                      (unmatched.Count > 0 ? $"; no file found for {unmatched.Count}: {string.Join(", ", unmatched.Take(12))}" : ""));
        }
    }
}
