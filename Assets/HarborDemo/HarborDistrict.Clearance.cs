using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    public partial class HarborDistrict
    {
        // Bounds are recorded in quay coordinates before MoveIntoPlace. A stack is
        // one reservation, including its pallet and any overhanging freight.
        readonly List<Bounds> _apronReservations = new List<Bounds>();
        readonly List<GameObject> _apronProps = new List<GameObject>();

        static bool FootprintsOverlap(Bounds a, Bounds b, float gap = 0f) =>
            a.min.x < b.max.x + gap && a.max.x > b.min.x - gap &&
            a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;

        static void CentreOnGround(GameObject go, Vector3 at)
        {
            var b = HarborKit.BoundsOf(go);
            go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        }

        bool ReserveApronProp(GameObject go, Vector3 at)
        {
            CentreOnGround(go, at);
            var b = HarborKit.BoundsOf(go);
            bool fits = b.min.z >= ShoulderZ + 2.5f && b.max.z <= ShedFrontZ - 0.6f &&
                        b.min.x >= _gateWestX + GateLaneHalf && b.max.x <= _gateEastX - GateLaneHalf;
            foreach (Transform building in _warehouseRoot)
                if (FootprintsOverlap(b, HarborKit.BoundsOf(building.gameObject), 0.6f)) fits = false;
            foreach (var door in _shedDoors)
            {
                // Keep a full forklift-width corridor from the stand to every door.
                var access = new Bounds(new Vector3(door.x, TileTop, (ShoulderZ + door.z) * 0.5f),
                    new Vector3(6.4f, 10f, door.z - ShoulderZ + 4f));
                if (FootprintsOverlap(b, access)) fits = false;
            }
            foreach (var occupied in _apronReservations)
                if (FootprintsOverlap(b, occupied, 0.5f)) fits = false;
            if (!fits)
            {
                go.SetActive(false);
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
                return false;
            }
            _apronReservations.Add(b);
            _apronProps.Add(go);
            return true;
        }

        GameObject PlaceApronProp(GameObject prefab, Vector3 at, float yaw, Transform parent, string name)
        {
            if (prefab == null) return null;
            var go = HarborKit.Prop(prefab, Vector3.zero, yaw, parent, name);
            return ReserveApronProp(go, at) ? go : null;
        }

        GameObject PlaceApronFreight(GameObject pallet, List<GameObject> freight, Vector3 at, Transform parent)
        {
            if (pallet == null) return null;
            var group = new GameObject("Goods");
            group.transform.SetParent(parent, false);
            var basePallet = HarborKit.Prop(pallet, Vector3.zero, HarborKit.Range(_rng, -8f, 8f), group.transform, "Pallet");
            CentreOnGround(basePallet, Vector3.zero);
            if (freight.Count > 0)
            {
                var cargo = HarborKit.Prop(HarborKit.Pick(_rng, freight), Vector3.zero,
                    HarborKit.Range(_rng, 0f, 360f), group.transform, "Freight");
                CentreOnGround(cargo, new Vector3(0f, HarborKit.BoundsOf(basePallet).max.y, 0f));
            }
            return ReserveApronProp(group, at) ? group : null;
        }
    }
}
