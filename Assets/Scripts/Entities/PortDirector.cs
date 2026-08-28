using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Owns the port's shift: a fixed roster of dockers who work the quay, the stacks and the
    /// warehouse doors for as long as the city runs. Modelled on SchoolBusDirector -
    /// everything is spawned here, parented here and PERSISTENT, outside PedestrianSpawner's
    /// population, counts and sweeps, and none of it ever despawns.
    ///
    /// Home is the PortMarker PortDresser attached at generation. The port is CHANCE-based -
    /// a weighted roll gated to map-edge blocks, neither promised nor rescued - so a seed with
    /// no port at all is ordinary rather than exceptional, and this system standing down is
    /// the common path rather than an error. The same posture as the school run, for the same
    /// reason, minus the bus.
    /// </summary>
    public sealed class PortDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        PortMarker[] ports = System.Array.Empty<PortMarker>();
        System.Random rng;

        public IReadOnlyList<PortMarker> Ports => ports;

        IEnumerator Start()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning("[PortDirector] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            // Self-bootstrap the shipping on a scene saved before PortShipDirector existed:
            // a regenerated city keeps its old Spawners object, so a new director that only
            // CitySceneSetup can add would silently require a re-run of Set Up Scene - which
            // is exactly how the first cut shipped a port whose ships never moved.
            if (!GetComponent<PortShipDirector>())
                gameObject.AddComponent<PortShipDirector>().Configure(config, prefabs);

            // A disabled feature is not a warning - PoliceDirector's rule.
            if (config.dockWorkerCount <= 0)
                yield break;

            rng = new System.Random(config.seed + SeedOffsets.PortWorkers);

            // The same one-frame wait as every other spawner, kept for discipline even though
            // the dockers never touch the path graphs: the registry and the LOD system are
            // also settling in that frame.
            yield return null;

            // The port takes a whole map side now, one marker per block along it - so this is
            // a list, and each compound gets its own shift. Sorted by BlockId because the rng
            // hand-offs below follow marker order, and Find promises none.
            ports = FindObjectsByType<PortMarker>();
            System.Array.Sort(ports, (a, b) => a.BlockId.CompareTo(b.BlockId));
            if (ports.Length == 0)
            {
                Debug.LogWarning(
                    "[PortDirector] No PortMarker in the scene - this seed's city has no port " +
                    "side. The port is a roll of the dice, not a promise (see BlockZone.Port). " +
                    "The shift stands down; regenerate the city (Tools/City/Set Up Scene) for " +
                    "another seed.", this);
                yield break;
            }

            if (prefabs.dockWorkerPrefabs == null || prefabs.dockWorkerPrefabs.Length == 0)
            {
                Debug.LogWarning("[PortDirector] No dock worker prefabs in the database - " +
                                 "run the asset bootstrap.", this);
                yield break;
            }

            foreach (var port in ports)
                SpawnShift(port);
        }

        void SpawnShift(PortMarker port)
        {
            var points = port.WorkPoints;
            if (points == null || points.Length < 2)
            {
                Debug.LogWarning("[PortDirector] A port marker has no walk graph - " +
                                 "regenerate the city.", this);
                return;
            }

            for (var i = 0; i < config.dockWorkerCount; i++)
            {
                var prefab = prefabs.dockWorkerPrefabs[rng.Next(prefabs.dockWorkerPrefabs.Length)];
                if (!prefab)
                    continue;

                // Scattered over the graph, not queued at the gate: the shift is mid-shift
                // when the city starts, the way the patrol fleet starts mid-patrol.
                var start = rng.Next(points.Length);
                var position = points[start].Position;
                var rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);

                var person = Instantiate(prefab, position, rotation, transform);
                PedestrianAnthropometry.Apply(
                    person,
                    PedestrianAnthropometry.Seed(config.seed, i, PedestrianAnthropometry.DockWorkerSalt),
                    PedestrianIdentity.IsFemale(prefab.name),
                    PedestrianAgeCohort.Adult,
                    prefab.name);

                PedestrianSpawner.SetLayerRecursively(person.transform,
                                                      PedestrianSpawner.PedestrianLayer);
                PedestrianLodSystem.Register(person);

                var human = person.GetComponent<HumanBehavior>();
                if (!human)
                {
                    Debug.LogWarning("[PortDirector] A dock worker prefab has no " +
                                     "HumanBehavior - re-run the asset bootstrap.", this);
                    Destroy(person);
                    // The prefab is drawn per docker, so one bad draw costs one docker, not
                    // the whole shift.
                    continue;
                }

                // Same instance-level controller swap as PedestrianSpawner: the interaction
                // controller carries the speed parameter the agent writes.
                var animator = person.GetComponent<Animator>();
                if (animator && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;

                var agent = person.AddComponent<DockWorkerAgent>();
                agent.Bind(port, rng.Next(), start);
            }
        }
    }
}
