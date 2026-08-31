using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LivingCity.City;
using LivingCity.Entities;
using LivingCity.Gameplay;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The playable layer's two menu items, following the CitySceneSetup pattern: assets
    /// first, scene wiring second, both idempotent.
    ///
    /// "Create or Refresh Gameplay Assets" makes the two config assets (GetOrCreate, so
    /// tuned values survive every refresh) and AUTHORS the player prefab from SM_Chr_Gang_Male_01
    /// - the plain Synty character, which carries no behaviour to strip at all. The grip
    /// constants in WeaponSocket were calibrated on the OLD rig and need re-doing in-editor.
    ///
    /// "Add Player To Scene" puts a Gameplay object (GameplayRuntime + InteractionController
    /// + ContextMenuUI) and one player instance on a pavement near the camera into the open
    /// city scene. Everything else the layer needs is created at runtime by the components
    /// themselves (the menu canvas, the EventSystem, the overlay marker).
    /// </summary>
    public static class GameplaySetup
    {
        // A Resources folder ON PURPOSE: GameplayBootstrap and GameplayRuntime self-heal
        // at Play time with Resources.Load, so a scene that was never re-wired (or never
        // saved) still fields the whole gameplay layer.
        const string GameplayConfigDir = "Assets/Configs/Gameplay/Resources";
        const string CombatConfigPath = GameplayConfigDir + "/CombatConfig.asset";
        const string PlayerConfigPath = GameplayConfigDir + "/PlayerConfig.asset";
        const string WantedConfigPath = GameplayConfigDir + "/WantedConfig.asset";
        const string PoliceConfigPath = GameplayConfigDir + "/PoliceConfig.asset";
        const string WeaponCatalogPath = GameplayConfigDir + "/WeaponCatalog.asset";
        const string PlayerPrefabPath = GameplayConfigDir + "/PlayerMafioso.prefab";
        const string PrefabDatabasePath = "Assets/Configs/PrefabDatabase.asset";

        const string ControllerPath = "Assets/Configs/People Interaction Controller.controller";
        const string RevolverPath = "Assets/Weapons/Revolver.obj";
        // A plain Palm City gangster prefab (Humanoid, Avatar, no controller), which
        // suits this pass even better than the old rig did - nothing to strip but the
        // modular-part toggles. The oversized Kingpin is deliberately retired globally.
        // NOTE: WeaponSocket's grip constants were calibrated on the man-mafia skeleton;
        // the Synty hand bone is oriented differently, so the grip needs the in-editor
        // gizmo pass again (WeaponSocket.cs doc) - flagged, cannot be derived offline.
        const string RigPath = "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Gang_Male_01.prefab";

        [MenuItem("Tools/City/Create or Refresh Gameplay Assets", priority = 21)]
        public static void CreateAssets()
        {
            Directory.CreateDirectory(GameplayConfigDir);

            GetOrCreate<CombatConfig>(CombatConfigPath);
            GetOrCreate<PlayerConfig>(PlayerConfigPath);
            GetOrCreate<WantedConfig>(WantedConfigPath);
            GetOrCreate<PoliceConfig>(PoliceConfigPath);
            // Seeded by its own field initializer - the revolver and the shop stock.
            GetOrCreate<WeaponCatalog>(WeaponCatalogPath);

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameplaySetup] Configs ready under {GameplayConfigDir}.");
        }

        static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
