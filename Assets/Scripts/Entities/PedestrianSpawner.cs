using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Gameplay;
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
        /// <summary>
        /// The layer every spawned pedestrian lives on. Unnamed in TagManager is fine - the
        /// number is what the physics matrix keys on. Pedestrian-pedestrian collisions are
        /// switched off there: avoidance is PedestrianRegistry's job and PhysX never
        /// generated contacts between two kinematic bodies anyway, but at crowd scale even
        /// tracking the candidate pairs in the broadphase costs real time. Collisions with
        /// everything else stay on, which is what the crosswalk triggers need.
        /// </summary>
        public const int PedestrianLayer = 8;

        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Header("Idle behaviour")]
        [Tooltip("Fraction of pedestrians that occasionally stop and idle.")]
        [SerializeField, Range(0f, 1f)] float idlerFraction = 0.35f;

        readonly List<GameObject> active = new();
        System.Random rng;

        /// <summary>
        /// Null when the database predates pedestrianGroups, in which case the old flat
        /// aiPedestrians draw is used instead. See PickPrefab.
        /// </summary>
        PedestrianPicker picker;

        IEnumerator Start()
        {
            var hasFlatList = prefabs && prefabs.aiPedestrians is { Length: > 0 };
            var hasGroups = prefabs && prefabs.pedestrianGroups is { Length: > 0 };

            if (!config || !prefabs || (!hasFlatList && !hasGroups))
            {
                Debug.LogWarning("[PedestrianSpawner] Needs a CityConfig and a PrefabDatabase with AI people.", this);
                yield break;
            }

            rng = new System.Random(config.seed + SeedOffsets.Pedestrians);

            // Built off the same stream, before the first draw: the picker's own rolls and
            // shuffles are part of the pedestrian sequence, which is what keeps one seed
            // reproducing one city. An empty result is left null so PickPrefab falls back
            // rather than spawning nothing.
            var candidate = new PedestrianPicker(prefabs.pedestrianGroups, rng);
            picker = candidate.IsEmpty ? null : candidate;

            if (picker == null && hasFlatList)
                Debug.LogWarning("[PedestrianSpawner] No usable pedestrianGroups - falling back to " +
                                 "the flat aiPedestrians list. Re-run the City Asset Bootstrap.", this);

            // The registry's tuning is global on purpose - the probes run inside a patched
            // pack script with no config reference. Handed down once, before the first spawn.
            PedestrianRegistry.PersonalSpace = config.pedestrianPersonalSpace;
            PedestrianRegistry.MinSeparation = config.pedestrianMinSeparation;
            PedestrianRegistry.ProbeInterval = config.pedestrianProbeInterval;
            PedestrianRepathQueue.Active = config.pedestrianInteractions;
            PedestrianRepathQueue.BudgetPerFrame = config.pedestrianRepathBudget;

            Physics.IgnoreLayerCollision(PedestrianLayer, PedestrianLayer, true);

            // A fixed step that overruns otherwise triggers up to 16 catch-up steps in one
            // rendered frame (0.02 fixed vs the project's 0.333 max) - each a full crowd
            // pass, which is the hitch death-spiral. Capping at 0.1 bounds it at 5: the
            // simulation slows down instead of the frame exploding.
            Time.maximumDeltaTime = Mathf.Min(Time.maximumDeltaTime, 0.1f);

            var lod = gameObject.AddComponent<PedestrianLodSystem>();
            lod.Configure(config);

            // Same reason as VehicleSpawner: sidewalk paths are linked in Tile.Start().
            yield return null;

            var interval = config.entitySpawnInterval > 0f
                ? new WaitForSeconds(config.entitySpawnInterval)
                : null;

            for (var i = 0; i < config.pedestrianCount; i++)
            {
                SpawnOne();
                // Batched: one per tick reads nicely at 50 pedestrians and takes a thousand
                // seconds at 10000. Null interval still yields, so a big population ramps
                // over frames rather than landing in one.
                if ((i + 1) % Mathf.Max(1, config.pedestrianSpawnBatch) == 0)
                    yield return interval;
            }
        }

        /// <summary>
        /// One pedestrian model, weighted when the database has groups and uniform when it does
        /// not.
        ///
        /// The fallback is not dead code: the bootstrap owns pedestrianGroups, so any scene
        /// whose PrefabDatabase was written before they existed has only the flat list, and an
        /// unpopulated pavement is a much louder failure than an unweighted one.
        /// </summary>
        GameObject PickPrefab(out string groupLabel)
        {
            if (picker != null)
                return picker.Next(out groupLabel);

            // The flat list knows no groups; a null label sends the identity to its
            // generic townsperson pool rather than an empty title.
            groupLabel = null;
            var flat = prefabs.aiPedestrians;
            return flat is { Length: > 0 } ? flat[rng.Next(flat.Length)] : null;
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
            // alongStreet is handed to SidewalkPoint, which works in the tile's LOCAL frame, so
            // it has to be an AUTHORED distance - CellSize carries CityGrid.TileScale and would
            // scatter people a further TileScale down the street, off the end of the tile.
            var side = rng.Next(2) == 0 ? 1f : -1f;
            var alongStreet = ((float)rng.NextDouble() - 0.5f) * CityGrid.AuthoredCellSize * 0.6f;

            var position = SidewalkPoint(tile, side, alongStreet);

            var prefab = PickPrefab(out var groupLabel);
            if (!prefab)
                return;

            var prefabName = prefab.name;
            var person = Instantiate(prefab, position, Quaternion.Euler(0f, rng.Next(4) * 90f, 0f), transform);
            PedestrianAnthropometry.Apply(
                person,
                PedestrianAnthropometry.Seed(config.seed, active.Count, PedestrianAnthropometry.CivilianSalt),
                PedestrianIdentity.IsFemale(prefabName),
                PedestrianAnthropometry.CohortFor(groupLabel, prefabName),
                prefabName);

            SetLayerRecursively(person.transform, PedestrianLayer);
            PedestrianLodSystem.Register(person);

            // The gameplay layer's handle: context-menu target, hover subject, hit points.
            // RequireComponent pulls in NpcHealth and PedestrianDeath with it. Civilians
            // only - the police, bank and school directors spawn their own people and are
            // deliberately not interactable in Phase 1.
            person.AddComponent<InteractableNpc>();

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
                // Same single rng.Next() as before the identity existed - the pedestrian
                // stream must not shift under an old seed.
                agent.Configure(config, rng.Next(), groupLabel, prefabName);
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
        /// this working for any tile the pack adds later rather than dropping the spawn. That
        /// fallback divides CityGrid.TileScale back out: CityGrid's constants are world-space and
        /// everything here is in the tile's own frame, which is exactly the frame the measured
        /// branch below reads with InverseTransformPoint.
        ///
        /// Public and static for any director that places persistent walkers on the
        /// pavements exactly the way civilians are placed - one pavement-point rule, not two.
        /// </summary>
        public static Vector3 SidewalkPoint(Tile tile, float side, float alongStreet)
        {
            var offset = new Vector3(CityGrid.SidewalkOffset / CityGrid.TileScale * side, 0f, alongStreet);

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

        public static void SetLayerRecursively(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            for (var i = 0; i < node.childCount; i++)
                SetLayerRecursively(node.GetChild(i), layer);
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
