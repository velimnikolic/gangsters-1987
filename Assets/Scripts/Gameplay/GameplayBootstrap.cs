using UnityEngine;
using PolyPerfect.City;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Makes pressing Play sufficient. The editor menu (Tools/City/Add Player To Scene)
    /// wires the gameplay layer into the SAVED scene - but a session where the menu was
    /// never re-run, or the scene never saved, used to mean silently missing systems: a
    /// player who kills without witnesses, police who never hear of it. That exact failure
    /// shipped once; this class exists so it cannot ship twice.
    ///
    /// Runs once per Play, after the scene loads: if this is the city (a CityBuilder
    /// exists) it ensures a player (from Resources, where the authored prefab lives for
    /// exactly this reason) and every gameplay system, creating only what is missing -
    /// a scene the menu DID wire is left untouched. Prefab references the systems need
    /// (police officer/car, the revolver) are self-wired by the components themselves from
    /// what is already in the scene.
    /// </summary>
    public static class GameplayBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGameplay()
        {
            // Only the generated city - the Shooting Demo and pack scenes have no
            // CityBuilder and want none of this.
            if (!Object.FindAnyObjectByType<CityBuilder>())
                return;

            var player = Object.FindAnyObjectByType<PlayerMafioso>(FindObjectsInactive.Include);
            if (!player)
                player = SpawnPlayer();
            if (!player)
                return; // No prefab in Resources either - Phase 1 assets were never built.

            // The arsenal rides the player, not the Gameplay host - WeaponController and
            // PlayerMafioso resolve it with GetComponent on themselves.
            if (!player.GetComponent<PlayerArsenal>())
                player.gameObject.AddComponent<PlayerArsenal>();

            RegisterShopActions();

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");

            Ensure<GameplayRuntime>(host);
            Ensure<ContextMenuUI>(host);
            Ensure<InteractionController>(host);
            Ensure<WantedSystem>(host);
            Ensure<WitnessSystem>(host);
            Ensure<PoliceResponseDirector>(host);
            Ensure<WantedHud>(host);
            Ensure<PropertyDirector>(host);
            Ensure<PlayerOcclusionHider>(host);

            // The pedestrian life layer. Historically added only by the editor menu
            // (Tools/City/Set Up City Scene) - the exact class of silent absence this
            // bootstrap exists for: without the director there are no chats, sits, shop
            // visits or daily routine, and every civilian popup reads "Out for a walk"
            // forever. Scene-wide checks, not host-local ones, because the menu parks
            // these on the Spawners object - a scene the menu DID wire must not get a
            // second director pacing every roll twice.
            EnsureUnique<PedestrianInteractionDirector>(host);
            EnsureUnique<UI.CityOverlayHud>(host);
        }

        static void Ensure<T>(GameObject host) where T : Component
        {
            if (!host.GetComponent<T>())
                host.AddComponent<T>();
        }

        static void EnsureUnique<T>(GameObject host) where T : Component
        {
            if (!Object.FindAnyObjectByType<T>())
                host.AddComponent<T>();
        }

        /// <summary>
        /// One buy row per non-starting catalog entry. Safe to run every Play: the
        /// registry re-seeds itself at SubsystemRegistration, which is before this, so
        /// these are fresh registrations, never duplicates.
        /// </summary>
        static void RegisterShopActions()
        {
            var weapons = WeaponCatalog.Instance.weapons;
            for (var i = 0; i < weapons.Length; i++)
                if (weapons[i] != null && !weapons[i].starting)
                    ContextActionRegistry.Register(new BuyWeaponAction(weapons[i], i));
        }

        static PlayerMafioso SpawnPlayer()
        {
            var prefab = Resources.Load<GameObject>("PlayerMafioso");
            if (!prefab)
            {
                Debug.LogWarning("[GameplayBootstrap] No PlayerMafioso prefab in Resources - " +
                                 "run Tools/City/Create or Refresh Gameplay Assets once.");
                return null;
            }

            var go = Object.Instantiate(prefab, SpawnPoint(), Quaternion.identity);
            go.name = "PlayerMafioso";
            PedestrianSpawner.SetLayerRecursively(go.transform, PedestrianSpawner.PedestrianLayer);

            Debug.Log("[GameplayBootstrap] Player spawned at runtime - the saved scene has " +
                      "none. Run Tools/City/Add Player To Scene and save to make him " +
                      "permanent.");
            return go.GetComponent<PlayerMafioso>();
        }

        /// <summary>A pavement near where the camera is looking - the SidewalkPoint rule,
        /// same as the editor tool, but against the live Tile registry.</summary>
        static Vector3 SpawnPoint()
        {
            var focus = Vector3.zero;
            var camera = Camera.main;
            if (camera)
            {
                var ray = new Ray(camera.transform.position, camera.transform.forward);
                if (Mathf.Abs(ray.direction.y) > 1e-4f)
                {
                    var t = -ray.origin.y / ray.direction.y;
                    if (t > 0f)
                        focus = ray.GetPoint(t);
                }
            }

            Tile best = null;
            var bestSqr = float.MaxValue;
            foreach (var tile in Tile.Tiles)
            {
                if (!tile || (tile.tileType != Tile.TileType.Road &&
                              tile.tileType != Tile.TileType.OnlyPathwalk))
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
    }
}
