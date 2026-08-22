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
        System.Collections.IEnumerator _mergeSteps;
        long _mergeMs;
        int _mergeFrames;

        /// <summary>How long one frame may spend folding the city into merged meshes. The
        /// merge is a few seconds of work; at this budget it is spread over a second or two
        /// of play - the city runs and draws its own un-merged pieces meanwhile - instead
        /// of the single locked frame it used to be. The look is identical throughout; only
        /// the draw-call count is briefly higher while the fold-in runs.</summary>
        const int MergeBudgetMs = 24;

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
            if (_mergeSteps == null)
            {
                _mergedRoot = new GameObject("Merged").transform;
                _mergeSteps = ScenePerf.MergeSteps(BuildMergeRoots(), _mergedRoot, "RoadDemo");
            }
            // pump the fold-in for a bounded slice of this frame; the sim ticks as usual
            // around it, on geometry that is only ever whole (merged or its own pieces)
            var frame = System.Diagnostics.Stopwatch.StartNew();
            bool more;
            do { more = _mergeSteps.MoveNext(); }
            while (more && frame.ElapsedMilliseconds < MergeBudgetMs);
            _mergeMs += frame.ElapsedMilliseconds;
            _mergeFrames++;
            if (!more)
            {
                _merged = true;
                _mergeSteps = null;
                Debug.Log($"[RoadDemo] the merge folded in over {_mergeMs} ms across {_mergeFrames} frames");
            }
        }

        List<ScenePerf.MergeRoot> BuildMergeRoots()
        {
            // The island and the flora are NOT in here, and that is the point of the
            // list. Merging copies geometry: a tree that stands thirty-two thousand
            // times over the wilderness is thirty-two thousand copies of its trunk in
            // the merged buffers, where unmerged it is ONE mesh drawn thirty-two
            // thousand times. That one difference was most of a hundred and eighty
            // million vertices - gigabytes of card memory for scenery the camera holds
            // a corner of - and it is what put the play session over what a laptop can
            // allocate. What stays merged is the town, where the geometry really is all
            // different: the street kit, the block bakes, the seams and the districts.
            //
            // The draw calls this gives back are the ones the cull distances already
            // handle: AssignCullLayers puts the props on 230 m and the trees on 480 m,
            // and it now bites on the props THEMSELVES rather than on a merged chunk
            // that is only as cullable as its 120 m cell.
            var roots = new List<ScenePerf.MergeRoot>
            {
                ScenePerf.MergeRoot.Of(_geometry),
                ScenePerf.MergeRoot.Of(_blocks, perChildChunk: true),
                ScenePerf.MergeRoot.Of(_seamsRoot, salt: 500),
            };
            for (int i = 0; i < _districtStatic.Count; i++)
            {
                // and a district's foliage is left out for the same reason the island's
                // is, because it is the same thing: ten suburbs' worth of trees measured
                // forty-six million vertices - a third of the whole merge - and they are
                // a dozen prefabs standing over and over. A district names its own roots
                // (IDistrictHost.StaticRoot), so the ones that hold trees say so.
                if (IsFoliageRoot(_districtStatic[i])) continue;
                roots.Add(ScenePerf.MergeRoot.Of(_districtStatic[i], salt: 1000 + i * 500));
            }
            // the merge is not in Awake's table - it waits for the first Update, so every
            // Start has seen the pieces - and from there it folds in across frames (see
            // MergeStaticGeometry) rather than in one locked frame
            return roots;
        }

        /// <summary>A district root that holds nothing but planting. Merging one costs
        /// a copy of every tree in it and buys back draw calls the 480 m tree cull was
        /// already going to save.</summary>
        static bool IsFoliageRoot(Transform t) =>
            t != null && t.name.EndsWith("Flora", System.StringComparison.Ordinal);

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
