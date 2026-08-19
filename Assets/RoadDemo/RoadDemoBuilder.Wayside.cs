using UnityEngine;

namespace RoadDemo
{
    // The wayside: what stands on the road OUT of town. Between the city's last
    // junction and each district's gate the connecting street crosses a long reach
    // of wild ground now, and halfway along it - where such a road always has one -
    // stands a gas station: the Town demo's own, whole (TownClusters.GasStation) -
    // canopy, pumps, the pickup at the pump, the pole sign, the store behind it
    // with its lit interior. One per district, on the side the district's own seed
    // picks, its forecourt paved in worn asphalt and the island told to hold the
    // ground flat and bare beneath it.
    public partial class RoadDemoBuilder
    {
        Transform _waysideRoot;

        /// <summary>A filling station beside the connecting road, halfway between the
        /// city's edge junction (<paramref name="face"/>) and the district's gate
        /// (<paramref name="portal"/>). Skipped when the drive is too short for a
        /// forecourt, or when both shoulders run into the freeway or a river.</summary>
        void WaysideStation(Vector3 face, Vector3 portal, DistrictSlot slot)
        {
            var along = portal - face;
            along.y = 0f;
            float len = along.magnitude;
            if (len < 90f) return;
            var dir = along / len;
            // a fixed way out of town rather than halfway: clear of the city's last
            // corner AND of the freeway's link road, which crosses the strip further out
            var mid = face + dir * 62f;

            // which shoulder, out of the district's own seed - flipped if that side
            // would push the forecourt into the freeway's run or a river's channel
            var rng = new System.Random(slot.seed * 613 + 29);
            var n = new Vector3(-dir.z, 0f, dir.x) * (rng.Next(2) == 0 ? 1f : -1f);
            Rect Ground(Vector3 side)
            {
                var a = mid + side * 8f - dir * 13f;
                var b = mid + side * 38f + dir * 13f;
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                                       Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
            }
            var rect = Ground(n);
            if (RunsIntoSeam(rect))
            {
                n = -n;
                rect = Ground(n);
                if (RunsIntoSeam(rect)) return;
            }

            // the ground: worn asphalt from the kerb back past the store, the island
            // holding it flat and growing nothing through it
            _reservations.Level(Rect.MinMaxRect(rect.xMin - 4f, rect.yMin - 4f,
                                                rect.xMax + 4f, rect.yMax + 4f), RoadBed);
            _reservations.NoFlora(rect);
            BuildBlockFloor(rect.xMin, rect.xMax, rect.yMin, rect.yMax, null, false);

            // the cluster, anchored on the canopy, its front (+Z) turned to the road
            var anchor = mid + n * (StreetHalf + 10f);
            anchor.y = FloorLevel();
            var rot = Quaternion.Euler(0f, SuburbDemo.TownKit.YawToFace(SuburbDemo.TownKit.Side.PlusZ, -n), 0f);
            if (_waysideRoot == null) _waysideRoot = ((IDistrictHost)this).StaticRoot("Wayside");
            int stood = 0;
            // the anchor itself first - the CANOPY the offsets are measured from; the
            // cluster's piece list holds everything BUT it
            var canopy = SuburbDemo.TownKit.LoadByName(SuburbDemo.TownClusters.GasStation.Anchor);
            if (canopy != null)
            {
                SuburbDemo.TownKit.Prop(canopy, anchor, rot, _waysideRoot);
                stood++;
            }
            foreach (var p in SuburbDemo.TownClusters.GasStation.Pieces)
            {
                var prefab = SuburbDemo.TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                var go = SuburbDemo.TownKit.Prop(prefab, anchor + rot * new Vector3(p.X, p.Y, p.Z),
                                                 rot * p.Rot, _waysideRoot);
                // the same touch the suburb gives it: the pole sign stretched tall
                if (p.Name == "SM_Prop_StreetSign_Pole_01")
                    go.transform.localScale = new Vector3(1.3f, 1.95f, 1.3f);
                if (p.Name.StartsWith("SM_Veh")) SuburbDemo.TownKit.StripForStatic(go);
                stood++;
            }
            if (stood == 0) return;

            // a walker cutting the strip goes round the store and the pump island
            BlockWalkers(anchor, rot, new Vector3(-5.2f, 0f, -17.6f), new Vector3(5f, 5f, -6f));
            BlockWalkers(anchor, rot, new Vector3(-4f, 0f, -1.2f), new Vector3(4f, 3.5f, 1.2f));
            Debug.Log($"[RoadDemo] wayside gas station on the {slot.kind} road ({slot.edge}), {len:F0} m drive");
        }

        /// <summary>Whether the rectangle strays into the run of a seam that goes on
        /// past the grid - the freeway's corridor, a river's channel out to the sea.</summary>
        bool RunsIntoSeam(Rect r)
        {
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (s == null || (s.kind != SeamKind.Highway && s.kind != SeamKind.River)) continue;
                var span = SeamSpan(s);
                float lo = span.lo - 16f, hi = span.hi + 16f;
                if (s.vertical ? (r.xMax > lo && r.xMin < hi) : (r.yMax > lo && r.yMin < hi)) return true;
            }
            return false;
        }

        void BlockWalkers(Vector3 anchor, Quaternion rot, Vector3 lo, Vector3 hi)
        {
            var a = anchor + rot * lo;
            var b = anchor + rot * hi;
            var box = new Bounds();
            box.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b));
            WalkObstacles.Block(box);
        }
    }
}
