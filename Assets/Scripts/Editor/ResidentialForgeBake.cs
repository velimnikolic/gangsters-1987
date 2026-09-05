using RoadDemo;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    public static class ResidentialForgeBake
    {
        [MenuItem("Tools/City/Residential/Bake Forge Replacement")]
        static void BakeMenu() => Bake();

        public static bool Bake()
        {
            string path = "Assets/Prefabs/Residential/" + ResidentialForgeReplacement.Name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sheet = ResidentialForgeReplacement.Roll();
                // Keep the existing root identity so authored scene instances retain their transforms.
                while (root.transform.childCount > 0)
                    Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
                var shell = ResidentialBlocks.StandSheet(sheet, root.transform, 0,
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), false);
                while (shell.transform.childCount > 0)
                    shell.transform.GetChild(0).SetParent(root.transform, false);
                Object.DestroyImmediate(shell);
                var unit = sheet.Unit;
                var tag = root.GetComponent<LivingCity.Generation.BlockLotTag>();
                if (tag == null) tag = root.AddComponent<LivingCity.Generation.BlockLotTag>();
                tag.lotWidth = unit.CW * ResidentialFacade.Cell;
                tag.lotDepth = unit.CD * ResidentialFacade.Cell;
                ResidentialHarvest.PreparePhysics(root, tag.lotWidth, tag.lotDepth, unit.Floor, unit.MaxH);
                var proxy = ResidentialTurfPrefab.BakeInto(root, tag.lotWidth, tag.lotDepth, unit.MaxH);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var catalog = AssetDatabase.LoadAssetAtPath<ResidentialTurfCatalog>(ResidentialTurfCatalog.AssetPath);
                if (catalog != null)
                {
                    catalog.ReplaceEntry(ResidentialForgeReplacement.Name, proxy.CopyMasses());
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
