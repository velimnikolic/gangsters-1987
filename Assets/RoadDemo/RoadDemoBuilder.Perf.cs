using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // What keeps a city of ninety-odd blocks - and the districts hanging off it - at
    // a playable frame rate. Nothing here changes what the city looks like; it
    // changes how many draw calls and shadow passes the renderer pays for it.
    //
    // The work itself is ScenePerf's, shared with whatever else hosts a scene (a
    // district's own demo). This file only says which of the city's roots go into
    // it: the block bakes chunk one bake at a time so a block culls as a block, the
    // seams are salted apart from the street kit in the same 120 m cell, and the
    // traffic and the cars are left out altogether because they move.
    public partial class RoadDemoBuilder
    {
        /// <summary>Renderers flatter than this cast no shadow.</summary>
        const float FlatCasterHeight = ScenePerf.FlatCasterHeight;

        public const int PropLayer = ScenePerf.PropLayer, CrowdLayer = ScenePerf.CrowdLayer, MidLayer = ScenePerf.MidLayer;
        public const float PropCullDistance = ScenePerf.PropCullDistance,
                           CrowdCullDistance = ScenePerf.CrowdCullDistance,
                           MidCullDistance = ScenePerf.MidCullDistance;

        bool _merged;
        Transform _mergedRoot;

        /// <summary>Roots the districts asked for: still geometry that merges and culls
        /// with the city's own, filled by RoadDemoBuilder.Districts.cs.</summary>
        readonly List<Transform> _districtStatic = new List<Transform>();

        List<Transform> MergeableRoots()
        {
            var roots = new List<Transform> { _blocks, _seamsRoot, _islandRoot, _flora };
            roots.AddRange(_districtStatic);
            return roots;
        }

        void OptimiseScene()
        {
            // _geometry is the street kit, already one tile per cell: only its flat
            // pieces are looked at, for their shadows
            ScenePerf.Optimise(MergeableRoots(), new List<Transform> { _geometry }, "RoadDemo");
        }

        void MergeStaticGeometry()
        {
            if (_merged) return;
            _merged = true;
            _mergedRoot = new GameObject("Merged").transform;

            var roots = new List<ScenePerf.MergeRoot>
            {
                ScenePerf.MergeRoot.Of(_geometry),
                ScenePerf.MergeRoot.Of(_blocks, perChildChunk: true),
                ScenePerf.MergeRoot.Of(_seamsRoot, salt: 500),
                ScenePerf.MergeRoot.Of(_islandRoot),
                ScenePerf.MergeRoot.Of(_flora),
            };
            for (int i = 0; i < _districtStatic.Count; i++)
                roots.Add(ScenePerf.MergeRoot.Of(_districtStatic[i], salt: 1000 + i * 500));

            ScenePerf.Merge(roots, _mergedRoot, "RoadDemo");
        }

        void AssignCullLayers()
        {
            var roots = new List<Transform> { _geometry, _flora, _seamsRoot, _islandRoot };
            roots.AddRange(_districtStatic);
            ScenePerf.AssignCullLayers(roots, "RoadDemo");
        }

        static void SetLayerDeep(GameObject go, int layer) => ScenePerf.SetLayerDeep(go, layer);

        static bool Animated(Transform t) => ScenePerf.Animated(t);

        static bool SwaysOrFlows(Renderer r) => ScenePerf.SwaysOrFlows(r);
    }
}
