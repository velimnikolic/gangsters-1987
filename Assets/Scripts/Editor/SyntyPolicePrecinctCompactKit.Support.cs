using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    public static partial class SyntyPolicePrecinctCompactKit
    {
        /// <summary>Also upgrades an existing prefab without rebuilding its authored rooms.</summary>
        public static void RepairPropSupport(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var station in transforms.Where(t => t.name.EndsWith(" WORKSTATION")))
            {
                var children = station.Cast<Transform>().ToArray();
                var desk = children.FirstOrDefault(t => t.name.StartsWith("SM_Prop_Desk_"));
                if (desk == null) continue;
                foreach (var prop in children)
                {
                    if (prop == desk || prop.name.StartsWith("SM_Prop_Chair_") ||
                        prop.name.StartsWith("SM_Prop_Mouse_")) continue;
                    RestOn(prop.gameObject, desk.gameObject);
                }
                var pad = children.FirstOrDefault(t => t.name.StartsWith("SM_Prop_Mousepad_"));
                var mouse = children.FirstOrDefault(t => t.name.StartsWith("SM_Prop_Mouse_"));
                if (pad != null && mouse != null) RestOn(mouse.gameObject, pad.gameObject);
            }

            var shelves = transforms.Where(t => t.name.Contains(" - Evidence shelf "))
                .OrderBy(t => t.name).ToArray();
            var bags = transforms.Where(t => t.name.Contains(" - Logged evidence bag "))
                .OrderBy(t => t.name).ToArray();
            for (int i = 0; i < bags.Length && shelves.Length > 0; i++)
            {
                var shelf = shelves[i % shelves.Length].gameObject;
                var bounds = BoundsOf(shelf);
                var bag = bags[i].gameObject;
                var own = BoundsOf(bag);
                bag.transform.position += new Vector3(bounds.center.x - own.center.x,
                    0f, bounds.center.z - own.center.z);
                // Two bags per shelf, on separate physical shelf boards.
                RestOn(bag, shelf, i < shelves.Length ? float.PositiveInfinity : bounds.max.y - 0.4f);
            }

            var room = transforms.FirstOrDefault(t => t.name == "WATCH BREAK ROOM");
            if (room == null) return;
            const string counterName = "SM_Prop_Table_01 - Break room appliance counter";
            var counter = room.Find(counterName)?.gameObject;
            if (counter == null)
            {
                counter = SitProp(room, "SM_Prop_Table_01",
                    root.transform.TransformPoint(new Vector3(4.5f, Storey, -5.7f)), 0f,
                    "Break room appliance counter");
                var size = BoundsOf(counter).size;
                counter.transform.localScale = Vector3.Scale(counter.transform.localScale,
                    new Vector3(2.7f / size.x, 1f, 0.9f / size.z));
                var center = root.transform.TransformPoint(new Vector3(4.5f, Storey, -5.7f));
                var resized = BoundsOf(counter);
                counter.transform.position += new Vector3(center.x - resized.center.x,
                    center.y - resized.min.y, center.z - resized.center.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(counter.transform);
            }
            foreach (Transform prop in room)
                if (prop.name.Contains(" - Break room microwave") ||
                    prop.name.Contains(" - Coffee dripper") || prop.name.Contains(" - Coffee pot") ||
                    prop.name.Contains(" - Break room kettle"))
                    RestOn(prop.gameObject, counter);
            var table = room.Cast<Transform>().FirstOrDefault(t => t.name.EndsWith(" - Break table"));
            var donuts = room.Cast<Transform>().FirstOrDefault(t => t.name.EndsWith(" - Night-watch donuts"));
            if (table != null && donuts != null) RestOn(donuts.gameObject, table.gameObject);
            var visual = root.GetComponent<RoadDemo.PolicePrecinctVisual>();
            if (visual != null)
            {
                var data = new SerializedObject(visual);
                data.FindProperty("authoredPropCount").intValue = CountProps(root.transform);
                data.FindProperty("rendererCount").intValue = root.GetComponentsInChildren<Renderer>(true).Length;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void RestOn(GameObject prop, GameObject support, float ceiling = float.PositiveInfinity)
        {
            var bounds = BoundsOf(prop);
            float highest = float.NegativeInfinity;
            // Use the actual top face; a shelf's bounding box includes its uprights.
            foreach (var filter in support.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                    var b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    var c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(det) < 0.000001f) continue;
                    float u = ((b.z - c.z) * (bounds.center.x - c.x) +
                               (c.x - b.x) * (bounds.center.z - c.z)) / det;
                    float v = ((c.z - a.z) * (bounds.center.x - c.x) +
                               (a.x - c.x) * (bounds.center.z - c.z)) / det;
                    if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
                    float y = u * a.y + v * b.y + (1f - u - v) * c.y;
                    if (y <= ceiling) highest = Mathf.Max(highest, y);
                }
            }
            if (float.IsNegativeInfinity(highest))
                throw new InvalidOperationException(prop.name + " has no surface on " + support.name);
            prop.transform.position += Vector3.up * (highest + 0.001f - bounds.min.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
        }
    }
}
