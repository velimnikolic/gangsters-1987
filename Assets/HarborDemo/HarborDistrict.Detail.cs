using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The detail that turns a concrete rectangle into a working port: footways for the
    // men, crossings and hazard boards where they belong, rubber on the tarmac
    // where the lorries turn, and lorries standing at the kerb in front of the shed
    // doors being loaded out.
    //
    // Everything painted here is laid on asphalt that BuildYardRoads actually put down
    // - the yard road, the gate roads, the forklift aisles - and nowhere else: an arrow
    // on bare concrete is a sign pointing at a road that was never built.
    //
    // Not one size is written down: every piece is measured off its own prefab at Play
    // and stretched or spaced by what it turns out to be, so a pack that cuts its
    // pavement at two metres and one that cuts it at five both lay the same footway.
    public partial class HarborDistrict
    {
        /// <summary>Painted ground sits a hair over the asphalt, which itself sits a hair
        /// over the concrete, so no two planes fight for the same pixel.</summary>
        const float PaintY = 0.03f;
        /// <summary>The men's way along the quay: its middle line and how wide it is
        /// laid - between the forklifts' pallet piles and the live container row.</summary>
        const float QuayWalkZ = 14.4f, WalkWidth = 2.4f;

        Transform _detailRoot;

        void BuildDetail()
        {
            _detailRoot = Root("Detail");
            LayFootways();
            MarkRoads();
            BuildLoadingBays();
        }

        // ------------------------------------------------------------ laying flat

        /// <summary>A run of flat pieces from A to B along one axis: one row, each
        /// stretched so the row is exactly <paramref name="width"/> across and the last
        /// one lands on B.</summary>
        void Footpath(GameObject slab, float from, float to, float across, float width, bool alongX, string name)
        {
            if (slab == null || Mathf.Abs(to - from) < 0.5f) return;
            var b = HarborKit.PrefabBounds(slab);
            float runSize = Mathf.Max(0.4f, alongX ? b.size.x : b.size.z);
            float crossSize = Mathf.Max(0.4f, alongX ? b.size.z : b.size.x);
            float span = to - from;
            int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(span) / runSize));
            float step = span / n;
            var scale = alongX
                ? new Vector3(Mathf.Abs(step) / runSize, 1f, width / crossSize)
                : new Vector3(width / crossSize, 1f, Mathf.Abs(step) / runSize);
            for (int i = 0; i < n; i++)
            {
                float mid = from + (i + 0.5f) * step;
                var centre = alongX ? new Vector3(mid, 0f, across) : new Vector3(across, 0f, mid);
                var go = HarborKit.Prop(slab, Vector3.zero, 0f, _detailRoot, name);
                go.transform.localScale = scale;
                go.transform.position = new Vector3(
                    centre.x - b.center.x * scale.x,
                    TileTop + PaintY - b.min.y * scale.y,
                    centre.z - b.center.z * scale.z);
            }
        }

        /// <summary>One flat piece laid centred on a point, its underside on the paint
        /// plane whatever its pivot.</summary>
        GameObject Mark(GameObject prefab, Vector3 at, float yaw, string name)
        {
            if (prefab == null) return null;
            var b = HarborKit.PrefabBounds(prefab);
            var offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(b.center.x, 0f, b.center.z);
            return HarborKit.Prop(prefab, new Vector3(at.x - offset.x, TileTop + PaintY - b.min.y, at.z - offset.z),
                                  yaw, _detailRoot, name);
        }

        // ------------------------------------------------------------ footways

        /// <summary>Where a man may walk without being run down: a way the length of the
        /// quay behind the pallet piles, a spur up the side of each gate road - in two
        /// lengths, broken where it meets the service road - and a crossing painted
        /// where the forklifts' aisles step over the footway and the yard road. The
        /// Generic kit's pavement - the Military Warehouse pack's wears an atlas we do
        /// not own, and stands in only if the Generic piece is missing.</summary>
        void LayFootways()
        {
            var slab = HarborKit.TryLoad(HarborKit.Footway) ?? HarborKit.TryLoad(HarborKit.FootwayAlt);
            if (slab == null) return;
            float half = QuayHalf;

            Footpath(slab, -half + 5f, half - 5f, QuayWalkZ, WalkWidth, alongX: true, "Footway");
            // up the west side of each gate road, from the spine out to the wire
            foreach (float gx in new[] { _gateWestX, _gateEastX })
            {
                float wx = gx - GateRoadHalf - WalkWidth * 0.5f - 0.4f;
                Footpath(slab, YardLaneZ + 5f, _serviceRoadZ0 - 0.5f, wx, WalkWidth, alongX: false, "Footway");
                Footpath(slab, _serviceRoadZ1 + 0.5f, _fenceZ - 1f, wx, WalkWidth, alongX: false, "Footway");
            }

            var crossing = HarborKit.TryLoad(HarborKit.RoadCrossing);
            if (crossing == null) return;
            for (int i = 0; i < berths; i++)
            {
                // only where the aisle was actually laid: a crossing painted across a
                // lane that was never built is the same fault as an arrow on bare
                // concrete (BuildYardRoads skips the aisle on a berth with no boxes)
                if (!IsBoxBerth(i)) continue;
                Mark(crossing, new Vector3(AisleX(i), 0f, QuayWalkZ), 90f, "Crossing");
                // the yard road is two cells wide: a stripe across each
                Mark(crossing, new Vector3(AisleX(i), 0f, YardLaneZ - 2.5f), 90f, "Crossing");
                Mark(crossing, new Vector3(AisleX(i), 0f, YardLaneZ + 2.5f), 90f, "Crossing");
            }
        }

        // ------------------------------------------------------------ the roads

        /// <summary>What is painted on the roads besides what their tiles carry (the
        /// loop is the city kit's own yellow-lined carriageway now, so no lines and no
        /// arrows are laid by hand): hazard boards at the gate mouths, bays under the
        /// standing lorries, and the rubber a place like this wears into its tarmac -
        /// on the spine and at the gate mouths, never on the bare concrete.</summary>
        void MarkRoads()
        {
            var warning = HarborKit.TryLoad(HarborKit.RoadWarning);
            var bay = HarborKit.TryLoad(HarborKit.RoadParking);
            var tyres = HarborKit.LoadAll(HarborKit.TyreMarks, quiet: true);
            float half = QuayHalf;

            if (warning != null)
                foreach (float gx in new[] { _gateWestX, _gateEastX })
                    Mark(warning, new Vector3(gx - GateRoadHalf * 0.5f, 0f, _fenceZ - 4f), 0f, "Hazard");
            if (bay != null)
                foreach (var stand in TruckStands())
                    Mark(bay, new Vector3(stand.x, 0f, stand.z), 90f, "Bay");
            if (tyres.Count > 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    float x = HarborKit.Range(_rng, -half + 10f, half - 10f);
                    if (InGateLane(x, 2f)) { i--; continue; }
                    var at = new Vector3(x, 0f, HarborKit.Range(_rng, YardRoadZ0 + 1.2f, YardRoadZ1 - 1.2f));
                    Mark(HarborKit.Pick(_rng, tyres), at, _rng.NextDouble() < 0.5 ? 90f : -90f, "TyreMarks");
                }
                foreach (float gx in new[] { _gateWestX, _gateEastX })
                    Mark(HarborKit.Pick(_rng, tyres), new Vector3(gx + HarborKit.Range(_rng, -1.5f, 1.5f), 0f, YardRoadZ1 + 1.5f),
                         HarborKit.Range(_rng, -20f, 20f), "TyreMarks");
            }
        }

        // ------------------------------------------------------------ loading bays

        /// <summary>Where the lorries that never move stand: at the kerb in front of the
        /// odd shed doors, nose along the road. The even ones are the working bays the
        /// lorries off the approach road pull in at (see FreeBays).</summary>
        IEnumerable<Vector3> TruckStands()
        {
            for (int i = 1; i < _shedDoors.Count; i += 2)
                yield return new Vector3(_shedDoors[i].x + 3f, TileTop, ShoulderZ);
        }

        /// <summary>The standing lorries: drawn up at the kerb in front of their
        /// doorways, nose east, boxes open, a pallet of goods off the tail and the stacks
        /// they are being loaded out of along the forecourt beside them.</summary>
        void BuildLoadingBays()
        {
            var truck = HarborKit.TryLoad(HarborKit.Truck);
            if (truck == null) return;
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var freight = HarborKit.LoadAll(HarborKit.Freight, quiet: true);
            float forecourt = ShedFrontZ - 1.4f;        // the strip between kerb and doors

            int n = 0;
            foreach (var stand in TruckStands())
            {
                if (n++ >= 2) break;
                var go = HarborKit.Prop(truck, new Vector3(stand.x, TileTop, stand.z), 90f, _detailRoot, "Truck");
                HarborKit.StripBehaviours(go, keepAnimator: false);
                OpenBox(go, n % 2 == 0 ? 95f : 78f);
                // what is still aboard, seen through the open doors
                HarborTruck.Stow(go.transform, pallet, freight, 2, _rng);

                var tb = HarborKit.BoundsOf(go);
                float tailX = tb.min.x - 0.9f;
                if (pallet != null)
                {
                    var pb = HarborKit.PrefabBounds(pallet);
                    for (int k = 0; k < 2; k++)
                    {
                        var at = new Vector3(tailX - k * 1.8f, TileTop, stand.z + 0.6f + k * 0.4f);
                        HarborKit.Sit(pallet, at, HarborKit.Range(_rng, -8f, 8f), _detailRoot, "Pallet");
                        if (freight.Count > 0)
                            HarborKit.Sit(HarborKit.Pick(_rng, freight), at + new Vector3(0f, pb.size.y, 0f),
                                          HarborKit.Range(_rng, 0f, 360f), _detailRoot, "Freight");
                    }
                }
                if (freight.Count > 0)
                    for (int k = 0; k < 3; k++)
                        HarborKit.Sit(HarborKit.Pick(_rng, freight),
                                      new Vector3(stand.x - 2f + k * 1.5f, TileTop, forecourt + HarborKit.Range(_rng, -0.4f, 0.4f)),
                                      HarborKit.Range(_rng, 0f, 360f), _detailRoot, "Freight");
            }
        }

        /// <summary>The box doors swung open on their hinges, as HarborTruck swings the
        /// working lorries'.</summary>
        static void OpenBox(GameObject truck, float degrees)
        {
            var l = HarborKit.FindDeep(truck.transform, "Door_L");
            var r = HarborKit.FindDeep(truck.transform, "Door_R");
            if (l != null) l.localRotation = Quaternion.Euler(0f, -degrees, 0f);
            if (r != null) r.localRotation = Quaternion.Euler(0f, degrees, 0f);
        }
    }
}
