using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace HarborDemo.EditorTools
{
    public static class HarborReview
    {
        [CliCommand("gangsters_harbor_review", "Inspect the baked fleet and the running shared harbor district.", MainThreadRequired = true)]
        public static object Inspect()
        {
            var failures = new List<string>();
            foreach (var path in HarborKit.Containers)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { failures.Add("Missing " + path); continue; }
                var size = HarborKit.PrefabBounds(prefab).size;
                if (Mathf.Abs(size.x - HarborShipSpec.BoxWidth) > 0.06f ||
                    Mathf.Abs(size.y - HarborShipSpec.BoxHeight) > 0.06f ||
                    Mathf.Abs(size.z - HarborShipSpec.BoxLength) > 0.06f)
                    failures.Add(prefab.name + " envelope " + size);
            }
            var host = UnityEngine.Object.FindAnyObjectByType<StandaloneDistrictHost>();
            var harbor = host?.District as HarborDistrict;
            if (host?.District is HarborIndustrialDistrict combined)
                harbor = Read<HarborDistrict>(combined, "_harbor");
            if (harbor == null)
                return new { fleetFresh = HarborShipKitBash.IsFresh(), failures, harborRunning = false };
            var shipping = Read<HarborShipping>(harbor, "_shipping");
            var trucks = Read<List<HarborTruck>>(harbor, "_trucks");
            var cranes = Read<List<HarborCrane>>(harbor, "_cranes");
            var warehouses = Read<Transform>(harbor, "_warehouseRoot");
            var props = Read<List<GameObject>>(harbor, "_apronProps");
            var doors = Read<List<Vector3>>(harbor, "_shedDoors");
            var occupied = new List<Bounds>();
            foreach (var prop in props)
            {
                if (prop == null) { failures.Add("Missing reserved apron prop"); continue; }
                var bounds = GeometryBounds(prop.transform, warehouses);
                foreach (Transform building in warehouses)
                    if (Overlap(bounds, GeometryBounds(building, warehouses), 0.5f))
                        failures.Add(prop.name + " intersects " + building.name);
                foreach (var previous in occupied)
                    if (Overlap(bounds, previous, 0.4f)) failures.Add(prop.name + " overlaps another apron group");
                foreach (var door in doors)
                {
                    var access = new Bounds(new Vector3(door.x, 0f, (HarborDistrict.ShoulderZ + door.z) * 0.5f),
                        new Vector3(6.4f, 10f, door.z - HarborDistrict.ShoulderZ + 4f));
                    if (Overlap(bounds, access)) failures.Add(prop.name + " blocks a loading door");
                }
                occupied.Add(bounds);
            }
            return new
            {
                fleetFresh = HarborShipKitBash.IsFresh(), failures, harborRunning = Application.isPlaying,
                time = Time.time,
                apron = new { reservedProps = props.Count, loadingCourtDepth = HarborDistrict.ShedFrontZ - HarborDistrict.YardRoadZ1,
                    hallFront = HarborDistrict.ShedFrontZ, street = Read<float>(harbor, "_streetZ") },
                sheds = Read<Transform>(harbor, "_warehouseRoot").Cast<Transform>().Select(t => t.name).ToArray(),
                berths = shipping.Berths.Select(b => new
                {
                    b.Index, phase = b.Phase.ToString(), b.Working, b.Holding,
                    ship = b.Spec?.Name, position = Position(b.Ship?.Model),
                }).ToArray(),
                trucks = trucks.Select(t => new { visible = t.Tf != null, t.Docked,
                    node = Read<int>(t, "_node"), speed = Read<float>(t, "_speed"),
                    position = Position(t.Tf) }).ToArray(),
                cranes = cranes.Select(c => new { position = Position(c.Root),
                    trolley = Position(Read<Transform>(c, "_trolley")),
                    spreader = Position(Read<Transform>(c, "_spreader")),
                    renderers = c.Root.GetComponentsInChildren<Renderer>().Length }).ToArray(),
            };
        }

        static float[] Position(Transform transform) => transform == null ? null :
            new[] { transform.position.x, transform.position.y, transform.position.z };

        static bool Overlap(Bounds a, Bounds b, float gap = 0f) =>
            a.min.x < b.max.x + gap && a.max.x > b.min.x - gap &&
            a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;

        // Read mesh geometry even when a goods group is hidden between deliveries.
        static Bounds GeometryBounds(Transform root, Transform frame)
        {
            bool started = false;
            var result = new Bounds();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!started) { result = new Bounds(p, Vector3.zero); started = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        static T Read<T>(object instance, string field) =>
            (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
}
