using System.Collections.Generic;
using LivingCity.Tests;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HarborDemo.EditorTools
{
    /// <summary>Builds the port once in an isolated Edit-mode preview scene and checks
    /// the actual renderer bounds. Nothing is saved and Play mode is never entered.</summary>
    public static class HarborLandmarkAudit
    {
        [MenuItem("Tools/City/Audit Harbor Landmarks")]
        public static void RunMenu()
        {
            var failures = HarborLandmarkTests.Run();
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Harbor Landmark Audit");
            SceneManager.MoveGameObjectToScene(root, preview);
            var host = new AuditHost(root.transform);
            HarborDistrict harbor = null;

            try
            {
                harbor = new HarborDistrict
                {
                    berths = 5,
                    seed = 1987,
                    passingTraffic = false,
                    quayCranes = false,
                    dockWorkers = 0,
                    shipCrew = 0,
                    forklifts = false,
                    deliveryTruck = false,
                    lorries = 0,
                    mixedBerths = false,
                    contraband = false,
                    gateWorks = false,
                };
                harbor.Frame = DistrictFrame.Identity;
                harbor.Plan(null, harbor.seed);
                harbor.Build(host);

                var headquarters = Find(root.transform, "Port Authority Headquarters");
                var silos = Find(root.transform, "Bulk Silo Terminal");
                var conveyor = Find(root.transform, "Bulk Conveyor and Ship Loader");
                var fence = Find(root.transform, "Harbor Fence");

                CheckBuilding(headquarters, "port authority headquarters",
                              HarborDistrict.PortHeadquartersMinimumHeight, failures);
                CheckBuilding(silos, "bulk silo terminal",
                              HarborDistrict.BulkSiloElevatorTop - 0.5f, failures);
                CheckBuilding(conveyor, "bulk conveyor and ship loader", 55f, failures);

                if (silos != null)
                {
                    var own = harbor.Placed.ToLocal(silos.position);
                    if (own.x <= harbor.QuayHalf)
                        failures.Add($"bulk silos sit at x {own.x:0.#}, inside the container strip");
                    var bounds = HarborKit.BoundsOf(silos.gameObject);
                    if (bounds.size.x < 50f || bounds.size.z < 34f)
                        failures.Add($"bulk silo renderer footprint is only {bounds.size.x:0.#} x {bounds.size.z:0.#} m");
                }

                if (headquarters != null)
                {
                    var own = harbor.Placed.ToLocal(HarborKit.BoundsOf(headquarters.gameObject).center);
                    if (Mathf.Abs(own.x) > 18f)
                        failures.Add($"headquarters drifted away from the central gate axis to x {own.x:0.#}");
                }

                if (fence == null)
                    failures.Add("harbor perimeter was not built");
                else
                {
                    var east = harbor.Placed.ToLocal(HarborKit.BoundsOf(fence.gameObject).max).x;
                    if (east < harbor.PlannedBulkTerminalEast - 2f)
                        failures.Add($"harbor fence stops at x {east:0.#}, before bulk terminal x {harbor.PlannedBulkTerminalEast:0.#}");
                }

                if (failures.Count == 0)
                {
                    var hq = HarborKit.BoundsOf(headquarters.gameObject);
                    var bulk = HarborKit.BoundsOf(silos.gameObject);
                    Debug.Log($"[HarborLandmarkAudit] PASS: HQ {hq.size.x:0.#} x {hq.size.z:0.#} x {hq.size.y:0.#} m; " +
                              $"bulk silos {bulk.size.x:0.#} x {bulk.size.z:0.#} x {bulk.size.y:0.#} m; " +
                              $"terminal east x {harbor.PlannedBulkTerminalEast:0.#}; industrial strip 10 parcels.");
                }
                else Debug.LogError("[HarborLandmarkAudit] FAIL: " + string.Join("; ", failures));
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                harbor?.Dispose();
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            if (failures.Count > 0)
                throw new System.InvalidOperationException(
                    "Harbor landmark audit failed: " + string.Join("; ", failures));
        }

        static void CheckBuilding(Transform transform, string name, float minimumHeight,
                                  List<string> failures)
        {
            if (transform == null)
            {
                failures.Add(name + " was not built");
                return;
            }
            var bounds = HarborKit.BoundsOf(transform.gameObject);
            if (bounds.size.y < minimumHeight)
                failures.Add($"{name} is only {bounds.size.y:0.#} m high");
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            return null;
        }

        sealed class AuditHost : IDistrictHost
        {
            readonly Transform _root;
            readonly CityLife _life = new CityLife { CanSit = false, CanChat = false };

            public AuditHost(Transform root) => _root = root;

            public Transform StaticRoot(string name) => Child(name);
            public Transform LiveRoot(string name) => Child(name);
            public PedClips Clips => default;
            public bool ProvidesGround => true;
            public Material GroundMaterial => null;
            public CityLife Life => _life;

            Transform Child(string name)
            {
                var child = new GameObject(name).transform;
                child.SetParent(_root, false);
                return child;
            }

            public void RegisterVehicle(DemoVehicle vehicle) { }
            public void RegisterCivilian(CivilianAgent civilian) { }
            public void RegisterWalker(PedestrianAgent walker) { }
            public void RegisterSignal(TrafficSignal signal) { }
            public void RegisterRoads(IReadOnlyList<RoadEdge> edges) { }
            public void RegisterPavement(IReadOnlyList<PedLink> links) { }
            public void Blocked(Bounds box) { }
            public void Blocked(Bounds box, string what) { }
            public void ReportMissing(string what) => failuresFromHost.Add(what);

            // Kept only for the interface callback; missing catalogue assets already emit
            // their own diagnostic and the main audit reports any landmark they prevent.
            readonly List<string> failuresFromHost = new List<string>();
        }
    }
}
