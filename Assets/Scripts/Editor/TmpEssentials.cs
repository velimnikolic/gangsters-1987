using TMPro;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// One-shot import of TextMeshPro's runtime resources.
    ///
    /// It used to live in CityHudSetup, which went with the old city generator; the import
    /// itself has nothing to do with that city - any scene with TMP text needs the fonts in
    /// Assets/TextMesh Pro - so it keeps its menu item here.
    /// </summary>
    public static class TmpEssentials
    {
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string EssentialsPackage = "/Package Resources/TMP Essential Resources.unitypackage";

        [MenuItem("Tools/City/Import TMP Essentials", priority = 61)]
        public static void Import()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath))
            {
                Debug.Log("[TMP] TextMeshPro resources are already imported.");
                return;
            }

            var packagePath = TMPro.EditorUtilities.TMP_EditorUtility.packageFullPath + EssentialsPackage;
            Debug.Log($"[TMP] Importing TextMeshPro essential resources from {packagePath}.");

            // The import triggers a domain reload, so anything that needs a font asset has to
            // wait for Unity to come back rather than being built in the same call.
            AssetDatabase.ImportPackage(packagePath, false);
        }
    }
}
