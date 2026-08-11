using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The hand-made route into the city: assemble Synty modules under one root in any
    /// scene, select the root, run Tools/City/Bake Selected As City Building. The root's
    /// NAME becomes the building's name, and the bake is the same one every kit building
    /// goes through - one mesh per material, footprint-centred pivot, mirrored pieces
    /// winding-fixed, a BoxCollider round the lot.
    ///
    /// Output lands in Assets/CityKit/Handmade, which the bootstrap folds into the
    /// residential street bag WHOLESALE on every refresh - drop a building in, refresh,
    /// and it is on the street. Deliberately a separate folder from the generated kit:
    /// the extractor and kit-bash passes overwrite THEIR outputs on every version bump,
    /// and a hand-made building must never be collateral of that.
    ///
    /// Conventions for the assembler:
    ///  - build at the world origin, ground at y = 0;
    ///  - the street facade faces +Z (the blue axis) - the generator yaws the building
    ///    onto its lot by that convention;
    ///  - the 2.5 m module grid is your friend: Edit > Grid and Snap > 2.5;
    ///  - mirrored pieces (negative scale) are fine - the bake corrects their winding.
    /// </summary>
    public static class BakeSelectedBuilding
    {
        public const string HandmadeDir = SyntyKitExtractor.KitDir + "/Handmade";
        const string HandmadeMeshDir = HandmadeDir + "/Meshes";

        [MenuItem("Tools/City/Bake Selected As City Building", priority = 5)]
        public static void Bake()
        {
            var selected = Selection.activeGameObject;
            if (!selected || !selected.scene.IsValid())
            {
                EditorUtility.DisplayDialog("Bake Selected As City Building",
                    "Select the ROOT GameObject of an assembled building in the scene first.", "OK");
                return;
            }

            if (!selected.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled))
            {
                EditorUtility.DisplayDialog("Bake Selected As City Building",
                    $"'{selected.name}' contains no visible mesh renderers - nothing to bake.", "OK");
                return;
            }

            EnsureFolders();
            SyntyBakeUtil.ClearCache();

            var copy = Object.Instantiate(selected);
            copy.name = selected.name;
            copy.transform.SetPositionAndRotation(selected.transform.position, selected.transform.rotation);
            try
            {
                SyntyKitExtractor.BakeGroup(copy, selected.name, yaw: 0f, HandmadeDir, HandmadeMeshDir);
            }
            finally
            {
                Object.DestroyImmediate(copy);
                SyntyBakeUtil.ClearCache();
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Bake Selected As City Building",
                $"'{selected.name}' baked to {HandmadeDir}. Run Tools/City/Create or Refresh " +
                "Config Assets and it joins the residential street stock; regenerate the city " +
                "(or the one-block scene) to see it placed.", "OK");
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(HandmadeDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Handmade");
            if (!AssetDatabase.IsValidFolder(HandmadeMeshDir))
                AssetDatabase.CreateFolder(HandmadeDir, "Meshes");
        }
    }
}
