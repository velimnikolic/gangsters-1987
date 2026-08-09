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

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");

            Ensure<GameplayRuntime>(host);
            Ensure<ContextMenuUI>(host);
            Ensure<InteractionController>(host);
            Ensure<WantedSystem>(host);
            Ensure<WitnessSystem>(host);
            Ensure<PoliceResponseDirector>(host);
            Ensure<WantedHud>(host);
        }

        static void Ensure<T>(GameObject host) where T : Component
        {
            if (!host.GetComponent<T>())
                host.AddComponent<T>();
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
