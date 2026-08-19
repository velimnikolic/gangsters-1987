using UnityEngine;

namespace AirportDemo
{
    // The field's share of the perf pass. There is a great deal of ground here - a
    // kilometre and a half of runway, eighty thousand square metres of ramp - but
    // very little on it, so the work is not in the buildings: it is in the hundred
    // and thirty airfield lights, the three hundred quads of paint, the fence by the
    // panel and the cars in the car park.
    //
    // Small things go on layers the camera stops drawing past a range, with the
    // field's own list of what never goes (a hangar is what tells you what you are
    // looking at from a kilometre away). The shadows and the merge of everything
    // that never moves are the HOST's (ScenePerf), the same pass the city and the
    // other districts go through.
    public partial class AirportDistrict
    {
        public const int PropLayer = RoadDemo.ScenePerf.PropLayer, CrowdLayer = RoadDemo.ScenePerf.CrowdLayer,
                         MidLayer = RoadDemo.ScenePerf.MidLayer;

        /// <summary>What never gets culled by distance, whatever its size: the field's
        /// own furniture, which is what tells you what you are looking at from a
        /// kilometre away.</summary>
        static readonly string[] NeverCulled =
        {
            "Hangar", "Terminal", "Control tower", "Fire station", "Air freight", "FBO",
            "Fuel farm", "Comms mast", "Apron mast", "Windsock", "PAPI", "Fence", "Ground", "Runway", "Taxiway", "Ramp",
        };

        /// <summary>The field's own distance-culling pass, run before the host's generic
        /// one (which leaves anything already moved off the default layer alone and
        /// knows the field's names, ScenePerf.AlwaysVisible).</summary>
        void AssignCullLayers()
        {
            int props = 0, mid = 0;
            foreach (var root in new[] { _airsideRoot, _apronRoot, _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot, _floraRoot, _lightRoot })
            {
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.gameObject.layer != 0) continue;
                    var piece = mr.transform;
                    while (piece.parent != null && piece.parent != root) piece = piece.parent;
                    bool keep = false;
                    foreach (var n in NeverCulled)
                        if (piece.name.StartsWith(n) || piece.name.Contains(n)) keep = true;
                    if (keep) continue;
                    var b = mr.bounds;
                    float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
                    if (h < 0.5f) continue;                        // flat: paint, tiles, plates
                    if (h <= 2.8f && w <= 4.5f) { mr.gameObject.layer = PropLayer; props++; }
                    else if (h <= 12f && w <= 12f) { mr.gameObject.layer = MidLayer; mid++; }
                }
            }
            Debug.Log($"[Airport] distance culling: {props} small props, {mid} poles and lamps");
        }

        /// <summary>Nothing here is driven by physics: the pack colliders only cost.</summary>
        void StripColliders()
        {
            int stripped = 0;
            foreach (var root in _roots)
            {
                if (root == null) continue;
                foreach (var col in root.GetComponentsInChildren<Collider>()) { Destroy(col); stripped++; }
            }
            if (stripped > 0) Debug.Log($"[Airport] {stripped} colliders dropped");
        }
    }
}
