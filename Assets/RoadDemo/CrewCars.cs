using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    // The outfit's cars on the outfit's streets. The sibling of CrewArms: which car a
    // man has is the ledger's call (the vehicle item his id holds; RosterOps deals
    // them), which pack body plays it is the ledger's too (the armory listing, the
    // same body the page photographs) - and this is where the book's line becomes a
    // car standing at a kerb.
    //
    // Where it stands is derived, never authored: the road nearest the man it was
    // given to, the kerb on HIS side of it, the first length of that kerb nothing
    // else has claimed (LaneNet's own occupancy, so it never lands on top of the
    // traffic or half over a pavement). That is where a man would have left it.
    public static class CrewCars
    {
        /// <summary>The car the ledger says this man has the keys to, or null. A crew's
        /// wheels belong to its lieutenant, but he may deal them to a hood who drives -
        /// so the HOLDER is asked, exactly as CrewArms asks for a gun.</summary>
        public static RosterEquipment VehicleOf(Roster roster, int id)
        {
            if (roster == null || id < 0) return null;
            foreach (var item in roster.Equipment)
                if (item.Kind == EquipmentKind.Vehicle && item.HolderId == id) return item;
            return null;
        }

        /// <summary>Whose car this is on the street: the man holding the keys, or the
        /// lieutenant whose crew it belongs to when nobody has been dealt them.</summary>
        public static int KeeperOf(RosterEquipment item)
        {
            if (item == null) return -1;
            if (item.HolderId >= 0) return item.HolderId;
            return item.OwnerId >= 0 ? item.OwnerId : -1;
        }

        /// <summary>The pack body that plays this listing - the same one the armory
        /// page photographs, so what the book shows is what stands in the street.</summary>
        public static GameObject BodyFor(RosterEquipment item)
        {
            if (item == null) return null;
            var name = LivingCity.UI.PortraitStudio.VehicleModelFor(item.DisplayName);
            return Body(name);
        }

        // The packs the outfit's cars come out of, in the order they are asked. The
        // police pack goes first for the same reason CrewDemo asks it first - the mob's
        // four-door is one of its bodies repainted - but nothing MARKED is ever handed
        // back: a crew does not drive a liveried cruiser (VehicleCatalog).
        static readonly string[] Folders =
        {
            // The outfit's own bodies first: a machine this project made out of a pack
            // one (the boxless moped) wins over the pack's, and any pack body nobody
            // has remade is found exactly where it always was.
            "Assets/Prefabs/Vehicles/",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/",
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
        };

        /// <summary>A body for a mob car by pack name (VehicleCatalog.GangsterCars):
        /// what the lab stands at the kerb when the ledger has dealt the outfit no car
        /// of its own.</summary>
        public static GameObject BodyNamed(string name) => Body(name);

        static GameObject Body(string name)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var folder in Folders)
            {
                var path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                if (LivingCity.Gameplay.VehicleCatalog.IsBarred(path)) continue;
                var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (prefab != null) return prefab;
            }
#endif
            return null;
        }

        /// <summary>The body's half length and half width, nose along its own +Z.</summary>
        public static bool MeasurePrefab(GameObject prefab, out float halfLength, out float halfWidth)
        {
            halfLength = 2.2f;
            halfWidth = 0.9f;
            if (prefab == null) return false;
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            halfLength = Mathf.Max(1.4f, b.extents.z);
            halfWidth = Mathf.Max(0.7f, b.extents.x);
            return true;
        }

        /// <summary>Metres of kerb searched either way from the man before giving up.</summary>
        const float Reach = 60f;

        /// <summary>How far apart the tries are along the kerb.</summary>
        const float Step = 3f;

        /// <summary>How far off the carriageway the man may stand and still have this
        /// road count as "his": the pavement's width and a stride.</summary>
        const float OffRoad = 14f;

        /// <summary>How far off a road the holder may be standing, tried in turn. The
        /// first is the old rule (a man on the pavement); the rest are the yard, the
        /// forecourt and the far side of a lot.</summary>
        static readonly float[] Widening = { OffRoad, 30f, 60f, 120f };

        /// <summary>A free length of kerb beside this man to leave a car of this size
        /// on: nose along the traffic on that side, its flank a hand over the stone,
        /// clear of both junctions and of everything already standing there.
        ///
        /// The search walks OUT from where he stands, nearest first, so the car turns
        /// up where he left it rather than at the end of the street.</summary>
        public static bool KerbSlotNear(LaneNet net, Vector3 near, float halfLength, float halfWidth,
            out Vector3 pos, out Quaternion rot)
        {
            pos = near;
            rot = Quaternion.identity;
            if (net == null) return false;

            // WHICH ROAD IS HIS - and the search widens rather than giving up. Fourteen
            // metres is a man standing on a pavement; a lieutenant dealt onto a lot, a
            // yard, a forecourt or a district street stands further off than that, and
            // the machine the ledger sold him was then never put on the street at all -
            // one warning line, and a player who owns a motorcycle that does not exist
            // (nineteen runs in twenty-two of the monkey soak). The kerb it ends up at
            // is still a real kerb: everything below this line is unchanged.
            Carriageway road = null;
            float s0 = 0f, d = 0f;
            foreach (var within in Widening)
            {
                road = net.Locate(near, out s0, out d, within);
                if (road != null) break;
            }
            if (road == null) return false;

            float kerb = road.KerbDOnSide(d, halfWidth);
            if (!road.Drivable(kerb, halfWidth)) return false;
            // whichever side of the axis the man is on has to allow standing there
            if (!(kerb >= 0f ? road.ParkingB : road.ParkingA)) return false;

            int heading = kerb >= 0f ? 1 : -1;
            float d0 = kerb - halfWidth - 0.3f, d1 = kerb + halfWidth + 0.3f;
            // clear of the junction boxes at either end, and of the crossings on them
            float lo = halfLength + 7f, hi = road.Length - halfLength - 7f;
            if (hi <= lo) return false;

            for (float out_ = 0f; out_ <= Reach; out_ += Step)
                for (int side = 0; side < 2; side++)
                {
                    if (side == 1 && out_ <= 0f) continue;
                    float s = Mathf.Clamp(s0 + (side == 0 ? out_ : -out_), lo, hi);
                    // a gap of a metre and a half at each end: room to pull out
                    if (road.Busy(null, s - halfLength - 1.5f, s + halfLength + 1.5f, d0, d1)) continue;
                    var p = road.Pose(s, kerb);
                    pos = new Vector3(p.x, near.y, p.z);
                    rot = Quaternion.LookRotation(road.Axis * heading, Vector3.up);
                    return true;
                }
            return false;
        }
    }
}
