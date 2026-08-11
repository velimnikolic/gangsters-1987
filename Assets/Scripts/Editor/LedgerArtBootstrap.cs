using LivingCity.UI;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes LedgerModelSet.asset (the armory page's gun references) on every editor
    /// load - the SoundAssetBootstrap discipline: no menu item to re-run, idempotent,
    /// and it only fills slots that are EMPTY, so a hand-picked override in the
    /// Inspector is never fought.
    ///
    /// The bodies come from Assets/Weapons/GunPack by fixed name. A missing file (the
    /// pack renamed, the import still running in another session) leaves that slot
    /// null and the catalogue row photo-less - the next domain reload picks it up.
    /// </summary>
    [InitializeOnLoad]
    static class LedgerArtBootstrap
    {
        const string AssetPath = "Assets/Configs/UI/Resources/LedgerModelSet.asset";
        const string PackPath = "Assets/Weapons/GunPack/";

        static LedgerArtBootstrap()
        {
            // delayCall: the asset database is not safe to touch from a static
            // constructor running mid-refresh.
            EditorApplication.delayCall += Bake;
        }

        static void Bake()
        {
            var set = AssetDatabase.LoadAssetAtPath<LedgerModelSet>(AssetPath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<LedgerModelSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            var changed = false;
            changed |= Wire(ref set.pistol, "Pistol_1");
            changed |= Wire(ref set.shotgun, "Shotgun_1");
            changed |= Wire(ref set.rifle, "SniperRifle_1");
            changed |= Wire(ref set.tommyGun, "SubmachineGun_1");

            // The photographs' model source for city-less scenes.
            if (!set.database)
            {
                set.database = AssetDatabase.LoadAssetAtPath<LivingCity.Data.PrefabDatabase>(
                    "Assets/Configs/PrefabDatabase.asset");
                changed |= set.database != null;
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
        }

        static bool Wire(ref GameObject slot, string modelName)
        {
            if (slot)
                return false;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                PackPath + modelName + ".fbx");
            if (!model)
                return false;

            slot = model;
            return true;
        }
    }
}
