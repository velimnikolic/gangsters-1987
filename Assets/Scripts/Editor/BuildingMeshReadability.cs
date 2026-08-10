using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// PlayerOcclusionHider clips building meshes on the CPU to build its
    /// bottom-fifth stubs, but the LPEC pack imports every model with Read/Write
    /// off, and in Play mode Unity answers a read on such a mesh with an error
    /// and empty arrays. This flips isReadable on exactly the models the
    /// building prefabs use - once; on every later domain reload the scan finds
    /// nothing to do. InitializeOnLoad rather than a menu item because menu
    /// steps do not get re-run in this project: pressing Play must be enough.
    /// </summary>
    [InitializeOnLoad]
    static class BuildingMeshReadability
    {
        const string BuildingsFolder =
            "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/Buildings_T";

        static BuildingMeshReadability() => EditorApplication.delayCall += Ensure;

        static void Ensure()
        {
            // Reimporting while entering Play would stall the very Play press
            // this exists to serve; the next edit-mode reload catches it.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var modelPaths = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { BuildingsFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (!prefab)
                    continue;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh && !filter.sharedMesh.isReadable)
                        modelPaths.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
            }
            if (modelPaths.Count == 0)
                return;

            var flipped = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in modelPaths)
                {
                    if (AssetImporter.GetAtPath(path) is not ModelImporter importer ||
                        importer.isReadable)
                        continue;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    flipped++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (flipped > 0)
                Debug.Log($"[BuildingMeshReadability] Enabled Read/Write on {flipped} building " +
                          "model(s) so occlusion stubs can clip their meshes.");
        }
    }
}
