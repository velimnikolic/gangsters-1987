using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    // What keeps a city of twenty-odd blocks at a playable frame rate. Nothing
    // here changes what the city looks like; it changes how many draw calls and
    // shadow passes the renderer pays for it.
    //
    // The bakes come in as three to five hundred renderers a block - every Synty
    // piece its own mesh - and the street kit as one 5 m tile per cell, so a big
    // city is twenty-odd thousand renderers, each drawn once for the camera and
    // once more into every shadow cascade. Two things cut that down to size:
    //   - static batching of everything that never moves, blocks and seams
    //     included (the street kit already was), leaving out only what a vertex
    //     shader animates in object space (foliage in the wind, the water) and the
    //     fairground wheel that turns;
    //   - no shadow casting from what cannot throw a shadow anyone would see: the
    //     flat ground tiles, the manholes and road patches, the lawn, the water.
    public partial class RoadDemoBuilder
    {
        /// <summary>Renderers flatter than this cast no shadow: the ground tiles,
        /// the plates, the decals, the chain on the ground.</summary>
        const float FlatCasterHeight = 0.6f;

        void OptimiseScene()
        {
            int batched = 0, flat = 0, kept = 0;
            var toBatch = new List<GameObject>();

            foreach (var root in new[] { _blocks, _seamsRoot, _edgesRoot, _flora })
            {
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mr.gameObject.isStatic) continue;

                    if (Animated(mr.transform)) continue;
                    if (SwaysOrFlows(mr))
                    {
                        // foliage and water keep their own renderers for the vertex shader
                        kept++;
                        if (mr.name == "Water") { mr.shadowCastingMode = ShadowCastingMode.Off; flat++; }
                        continue;
                    }
                    toBatch.Add(mr.gameObject);
                    batched++;
                    if (mr.name == "Lawn" || mr.name == "Far Shore" || mr.name == "Islet")
                    { mr.shadowCastingMode = ShadowCastingMode.Off; flat++; }
                }
            }
            // the street kit was combined already; its flat tiles still cast
            foreach (var mr in _geometry.GetComponentsInChildren<MeshRenderer>())
                if (mr.shadowCastingMode != ShadowCastingMode.Off && mr.bounds.size.y < FlatCasterHeight)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    flat++;
                }
            foreach (var go in toBatch)
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr.shadowCastingMode != ShadowCastingMode.Off && mr.bounds.size.y < FlatCasterHeight)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    flat++;
                }
            }

            if (toBatch.Count > 0)
                StaticBatchingUtility.Combine(toBatch.ToArray(), _blocks.gameObject);

            Debug.Log($"[RoadDemo] batched {batched} block/seam renderers, {kept} foliage left to the wind, " +
                      $"{flat} flat renderers cast no shadow");
        }

        /// <summary>Under something that turns or is otherwise driven at runtime -
        /// the fairground wheel; a batched piece cannot move.</summary>
        static bool Animated(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<DemoFerrisWheel>() != null) return true;
            return false;
        }

        /// <summary>A material whose shader moves the vertices itself - the pack's
        /// foliage in the wind, the water - which static batching would pin to the
        /// batch's pivot instead of the piece's own.</summary>
        static bool SwaysOrFlows(Renderer r)
        {
            if (r.name == "Water") return true;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                var name = m.shader.name;
                if (name.IndexOf("Foliage", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Ocean", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
