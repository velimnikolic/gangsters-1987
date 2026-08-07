using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Populates the pavements. Spawns onto the sidewalk line rather than the tile centre so
    /// HumanBehavior's initial GetClocestPoint() snaps to a sensible checkpoint instead of
    /// dragging the character across the carriageway on its first step.
    /// </summary>
    public sealed class PedestrianSpawner : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Header("Idle behaviour")]
        [Tooltip("Fraction of pedestrians that occasionally stop and idle.")]
        [SerializeField, Range(0f, 1f)] float idlerFraction = 0.35f;

        readonly List<GameObject> active = new();
        System.Random rng;

        IEnumerator Start()
        {
            if (!config || !prefabs || prefabs.aiPedestrians == null || prefabs.aiPedestrians.Length == 0)
            {
                Debug.LogWarning("[PedestrianSpawner] Needs a CityConfig and a PrefabDatabase with AI people.", this);
                yield break;
            }

            rng = new System.Random(config.seed + SeedOffsets.Pedestrians);

            // The registry's tuning is global on purpose - the probes run inside a patched
            // pack script with no config reference. Handed down once, before the first spawn.
            PedestrianRegistry.PersonalSpace = config.pedestrianPersonalSpace;
            PedestrianRegistry.MinSeparation = config.pedestrianMinSeparation;

            // Same reason as VehicleSpawner: sidewalk paths are linked in Tile.Start().
            yield return null;

            for (var i = 0; i < config.pedestrianCount; i++)
            {
                SpawnOne();
                if (config.entitySpawnInterval > 0f)
                    yield return new WaitForSeconds(config.entitySpawnInterval);
            }
        }

        void SpawnOne()
        {
            var tile = RandomWalkableTile();
            if (!tile)
                return;

            // Put them on a pavement, then a little along it so they do not stack up on tile
            // centres.
            //
            // The pavement line is READ OFF THE TILE rather than assumed. It used to be the
            // constant x = +/-4 measured from tile-road-straight, which stopped being true the
            // moment the city gained a second kind of road: the dual carriageway walks at
            // +/-7.25, so at 4 pedestrians would spawn in its outer traffic lane. Asking the
            // tile for its own sidewalkPaths is right for every tile in the pack - avenue,
            // street, park path - and leaves nothing to keep in sync.
            var side = rng.Next(2) == 0 ? 1f : -1f;
            var alongStreet = ((float)rng.NextDouble() - 0.5f) * CityGrid.CellSize * 0.6f;

            var position = SidewalkPoint(tile, side, alongStreet);

            var prefab = prefabs.aiPedestrians[rng.Next(prefabs.aiPedestrians.Length)];
            var person = Instantiate(prefab, position, Quaternion.Euler(0f, rng.Next(4) * 90f, 0f), transform);

            var behaviour = person.GetComponent<HumanBehavior>();
            if (behaviour)
                behaviour.randomDestination = true;

            if (behaviour && config.pedestrianInteractions)
            {
                // The interaction controller is swapped in on the INSTANCE, so the pack's AI
                // prefabs and demo scenes keep their own; empty (bootstrap could not build
                // it) leaves the pack controller and the agent simply never finds the
                // activity parameter.
                var animator = person.GetComponent<Animator>();
                if (animator && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;

                // The agent subsumes PedestrianIdler: idling is one of its activities, with
                // its odds owned by CityConfig rather than a per-spawn fraction here.
                var agent = person.AddComponent<PedestrianAgent>();
                agent.Configure(config, rng.Next());
            }
            else if (behaviour && rng.NextDouble() < idlerFraction)
            {
                person.AddComponent<PedestrianIdler>();
            }

            active.Add(person);
        }

        /// <summary>
        /// A point on one of this tile's pavements, `alongStreet` metres down it.
        ///
        /// Works in the tile's LOCAL frame, so it is independent of how the tile was rotated:
        /// the pavement lines always run parallel to local Z, and which one you get is the sign
        /// of the local X. The widest |x| on the requested side is the outer pavement - on a
        /// crosswalk tile some sidewalk paths cut ACROSS the carriageway, and their nodes sit
        /// near x = 0, which is the one place a pedestrian must not be put.
        ///
        /// Falls back to the old constant when a tile has no sidewalk paths at all, which keeps
        /// this working for any tile the pack adds later rather than dropping the spawn.
        /// </summary>
        Vector3 SidewalkPoint(Tile tile, float side, float alongStreet)
        {
            var offset = new Vector3(CityGrid.SidewalkOffset * side, 0f, alongStreet);

            if (tile.sidewalkPaths != null)
            {
                var best = 0f;

                foreach (var path in tile.sidewalkPaths)
                {
                    if (!path || path.pathPositions == null)
                        continue;

                    foreach (var node in path.pathPositions)
                    {
                        if (!node)
                            continue;

                        var local = tile.transform.InverseTransformPoint(node.position);
                        if (Mathf.Sign(local.x) != side || Mathf.Abs(local.x) <= best)
                            continue;

                        best = Mathf.Abs(local.x);
                        offset = new Vector3(local.x, local.y, alongStreet);
                    }
                }
            }

            return tile.transform.TransformPoint(offset);
        }

        Tile RandomWalkableTile()
        {
            if (Tile.Tiles.Count == 0)
                return null;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var tile = Tile.Tiles[rng.Next(Tile.Tiles.Count)];
                if (tile && (tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.OnlyPathwalk))
                    return tile;
            }

            return null;
        }
    }
}
