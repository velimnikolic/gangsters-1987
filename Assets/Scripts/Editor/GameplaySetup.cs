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
    /// tuned values survive every refresh) and AUTHORS the player prefab from man-mafia_Rig
    /// - the rig, not the _AI prefab, because the rig carries no HumanBehavior baggage to
    /// strip and is the exact skeleton WeaponSocket's grip constants were calibrated on.
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
        const string RigPath = "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/People_T/" +
                               "Rigs_T/man-mafia_Rig.prefab";

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

            AuthorPlayerPrefab();

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameplaySetup] Configs and player prefab ready under {GameplayConfigDir}.");
        }

        /// <summary>
        /// The player prefab, built the way AuthorPedestrians builds walkers: instantiate,
        /// unpack completely, strip what does not belong, add what does, save. Re-run to
        /// rebuild after a change; the prefab is a build product, not a place to hand-tune.
        /// </summary>
        static void AuthorPlayerPrefab()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (!rig)
            {
                Debug.LogError($"[GameplaySetup] No man-mafia rig at {RigPath} - is the Epic " +
                               "City pack imported?");
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (!controller)
            {
                Debug.LogError("[GameplaySetup] No People Interaction Controller. Run " +
                               "Tools/City/Create or Refresh Config Assets first - it authors " +
                               "the controller, including the pistol states the player needs.");
                return;
            }

            var revolver = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPath);
            if (revolver)
                ShootingDemoSetup.RemapRevolverMaterials();
            else
                Debug.LogWarning($"[GameplaySetup] No revolver at {RevolverPath} - the player " +
                                 "will aim an empty hand.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            instance.name = "PlayerMafioso";

            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                // The AuthorPedestrians elimination sweep, in dependency order: scripts
                // first (Common_WanderScript would RequireComponent-pin the controller it
                // rides), then the movers and colliders they pinned. The rig prefabs are
                // normally bare, so this is a guard, not a workload.
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script)
                        Object.DestroyImmediate(script);

                foreach (var agent in instance.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                    Object.DestroyImmediate(agent);

                foreach (var mover in instance.GetComponentsInChildren<CharacterController>(true))
                    Object.DestroyImmediate(mover);

                // The man-mafia trap: the pack keeps its capsule on the _Rig CHILD. Ours
                // goes on the root, where the picker and the crosswalk triggers expect it,
                // so every authored collider goes first.
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);

                var animator = instance.GetComponent<Animator>();
                if (!animator)
                {
                    Debug.LogError("[GameplaySetup] The rig has no Animator on its root - " +
                                   "cannot author a player from it.");
                    return;
                }

                animator.runtimeAnimatorController = controller;
                // The single line most likely to be the cause if the player drifts.
                animator.applyRootMotion = false;

                var capsule = instance.AddComponent<CapsuleCollider>();
                capsule.radius = 0.2f;
                capsule.height = 2f;
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.direction = 1;

                var body = instance.AddComponent<Rigidbody>();
                body.mass = 1f;
                body.useGravity = false;
                body.isKinematic = true;

                instance.AddComponent<PathFinding>();
                instance.AddComponent<GunmanAim>();

                var socket = instance.AddComponent<WeaponSocket>();
                if (revolver)
                {
                    CityEditorUtils.SetField(socket, "weaponPrefab", revolver);
                    // Adopted here and saved INTO the prefab, so the gun is visible in the
                    // editor and Play never has to equip - the Shooting Demo's reasoning.
                    socket.Adopt(((GameObject)PrefabUtility.InstantiatePrefab(
                        revolver, socket.Hand)).transform);
                }

                instance.AddComponent<WeaponController>();

                var player = instance.AddComponent<PlayerMafioso>();
                CityEditorUtils.SetField(player, "config",
                    AssetDatabase.LoadAssetAtPath<PlayerConfig>(PlayerConfigPath));

                // Counted by the crosswalk triggers like everybody on two legs - a player
                // waiting at the kerb stops cars exactly as a civilian does.
                instance.tag = "Human";

                PrefabUtility.SaveAsPrefabAsset(instance, PlayerPrefabPath);
                Debug.Log($"[GameplaySetup] Authored {PlayerPrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [MenuItem("Tools/City/Add Player To Scene", priority = 22)]
        public static void AddPlayerToScene()
        {
            // Assets first, and idempotently - the one-click habit from CitySceneSetup.
            CreateAssets();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(CombatConfigPath);
            var playerConfig = AssetDatabase.LoadAssetAtPath<PlayerConfig>(PlayerConfigPath);
            var wantedConfig = AssetDatabase.LoadAssetAtPath<WantedConfig>(WantedConfigPath);
            var policeConfig = AssetDatabase.LoadAssetAtPath<PoliceConfig>(PoliceConfigPath);

            if (!prefab)
            {
                Debug.LogError("[GameplaySetup] Player prefab missing - the authoring step " +
                               "above failed; see its error.");
                return;
            }

            // The host object for the layer's scene-side components.
            var gameplay = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");

            var runtime = gameplay.GetComponent<GameplayRuntime>()
                       ?? gameplay.AddComponent<GameplayRuntime>();
            CityEditorUtils.SetField(runtime, "combat", combat);
            CityEditorUtils.SetField(runtime, "player", playerConfig);
            CityEditorUtils.SetField(runtime, "wanted", wantedConfig);
            CityEditorUtils.SetField(runtime, "police", policeConfig);

            if (!gameplay.GetComponent<ContextMenuUI>())
                gameplay.AddComponent<ContextMenuUI>();

            var interaction = gameplay.GetComponent<InteractionController>()
                           ?? gameplay.AddComponent<InteractionController>();

            // Phases 2-3: witnesses, heat, the response force and its readouts.
            if (!gameplay.GetComponent<WantedSystem>())
                gameplay.AddComponent<WantedSystem>();
            if (!gameplay.GetComponent<WitnessSystem>())
                gameplay.AddComponent<WitnessSystem>();
            if (!gameplay.GetComponent<WantedHud>())
                gameplay.AddComponent<WantedHud>();

            var response = gameplay.GetComponent<PoliceResponseDirector>()
                        ?? gameplay.AddComponent<PoliceResponseDirector>();
            var database = AssetDatabase.LoadAssetAtPath<LivingCity.Data.PrefabDatabase>(
                PrefabDatabasePath);
            if (database)
            {
                CityEditorUtils.SetField(response, "officerPrefab", database.policeOfficerPrefab);
                CityEditorUtils.SetField(response, "carPrefab", database.policeCarPrefab);
                CityEditorUtils.SetField(response, "animatorController", database.pedestrianController);
            }
            else
            {
                Debug.LogWarning("[GameplaySetup] No PrefabDatabase - the response force has " +
                                 "no officer/car prefabs. Run Tools/City/Create or Refresh " +
                                 "Config Assets.");
            }
            CityEditorUtils.SetField(response, "weaponPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPath));

            // One player. Re-running repositions the existing one instead of twinning him.
            var existing = Object.FindAnyObjectByType<PlayerMafioso>(FindObjectsInactive.Include);
            var player = existing
                ? existing.gameObject
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = SpawnPoint();

            PedestrianSpawner.SetLayerRecursively(
                player.transform, PedestrianSpawner.PedestrianLayer);

            CityEditorUtils.SetField(interaction, "player",
                player.GetComponent<PlayerMafioso>());

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = player;

            Debug.Log("[GameplaySetup] Player in the scene. Press Play: left-click ground " +
                      "to walk, left-click the player for his popup, right-click a " +
                      "pedestrian for the menu. Save the scene to keep him.");
        }

        /// <summary>
        /// A pavement point near where the camera is looking - the same SidewalkPoint rule
        /// every civilian spawns by, so the player starts on a walkable kerb, not inside a
        /// building. Falls back to the camera's ground focus, then the origin.
        /// </summary>
        static Vector3 SpawnPoint()
        {
            var focus = Vector3.zero;
            var camera = Camera.main;
            if (camera)
            {
                // Where the view axis meets the ground plane.
                var ray = new Ray(camera.transform.position, camera.transform.forward);
                if (Mathf.Abs(ray.direction.y) > 1e-4f)
                {
                    var t = -ray.origin.y / ray.direction.y;
                    if (t > 0f)
                        focus = ray.GetPoint(t);
                }
            }

            // Edit mode, so the static Tile.Tiles registry has never been filled - ask the
            // scene directly.
            Tile best = null;
            var bestSqr = float.MaxValue;
            foreach (var tile in Object.FindObjectsByType<Tile>(FindObjectsSortMode.None))
            {
                if (tile.tileType != Tile.TileType.Road &&
                    tile.tileType != Tile.TileType.OnlyPathwalk)
                    continue;

                var sqr = (tile.transform.position - focus).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = tile;
                }
            }

            return best ? PedestrianSpawner.SidewalkPoint(best, 1f, 0f) : focus;
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
