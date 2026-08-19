using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SuburbDemo
{
    // What is the suburb's own in the perf pass. The pass itself is the host's now
    // (RoadDemo.ScenePerf: shadows, cull layers, the merge on the first frame), the
    // same one the city runs over its blocks - so a suburb costs the city what a
    // quarter of the grid costs. What stays here: the click boxes on the buildings,
    // which have to go on AFTER the colliders are stripped, and the stripping.
    public partial class SuburbDistrict
    {

        /// <summary>A house, the church or a shop: what a click opens a card on. These
        /// keep their own renderers out of the merge so the card can tint them.</summary>
        static bool Pickable(Transform t) => t.name.StartsWith("House ") || t.name == "Church" || t.name.StartsWith("Shop ");

        // The road demo's card picker raycasts against a footprint box sat on the
        // building's own root; the pack's mesh colliders went with the rest, so every
        // pickable gets one box from its measured bounds.
        void BuildPickables()
        {
            int n = 0;
            foreach (var root in new[] { _lotRoot, _placeRoot })
            {
                if (root == null) continue;
                foreach (Transform t in root)
                {
                    if (!Pickable(t)) continue;
                    // the box is the building's whole footprint in its own frame (the
                    // church and the shops carry their doors and signs as children)
                    var toRoot = t.worldToLocalMatrix;
                    var b = new Bounds();
                    bool started = false;
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        var lb = mf.sharedMesh.bounds;
                        var m = toRoot * mf.transform.localToWorldMatrix;
                        for (int k = 0; k < 8; k++)
                        {
                            var c = lb.center + new Vector3((k & 1) == 0 ? lb.extents.x : -lb.extents.x, (k & 2) == 0 ? lb.extents.y : -lb.extents.y, (k & 4) == 0 ? lb.extents.z : -lb.extents.z);
                            var p = m.MultiplyPoint3x4(c);
                            if (!started) { b = new Bounds(p, Vector3.zero); started = true; }
                            else b.Encapsulate(p);
                        }
                    }
                    if (!started) continue;
                    var box = t.gameObject.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                    n++;
                }
            }
            Debug.Log($"[SuburbDemo] {n} buildings answer a click");
        }

        /// <summary>The pack's own colliders go: nothing here is physical, and the
        /// click boxes are put on afterwards.</summary>
        void StripColliders()
        {
            int stripped = 0;
            foreach (var root in new[] { _groundRoot, _streetRoot, _lotRoot, _placeRoot, _floraRoot })
            {
                if (root == null) continue;
                foreach (var col in root.GetComponentsInChildren<Collider>()) { Object.Destroy(col); stripped++; }
            }
            BuildPickables();
            Debug.Log($"[Suburb] {stripped} colliders dropped");
        }
    }
}
