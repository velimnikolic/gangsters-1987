using LivingCity.Personnel;
using System.Collections.Generic;
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
            if (string.IsNullOrEmpty(name)) return null;
            // Authored body names differ from their file names. The same baked
            // reference feeds the catalogue photograph and the purchased vehicle.
            if (LivingCity.Gameplay.CivilianVehicleCatalog.IsAuthored(name))
                return LivingCity.UI.LedgerModelSet.OwnBodyNamed(name);
            foreach (var folder in Folders)
            {
                var path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                if (LivingCity.Gameplay.VehicleCatalog.IsBarred(path)) continue;
                var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (prefab != null) return prefab;
            }
            return null;
        }

        /// <summary>The same traffic footprint used after the body is instantiated,
        /// independent of the prefab's heading.</summary>
        public static bool MeasurePrefab(GameObject prefab, out float halfLength, out float halfWidth)
            => CarBody.MeasureTrafficFootprint(prefab != null ? prefab.transform : null,
                out halfLength, out halfWidth);

        /// <summary>Metres of kerb searched either way from the man before giving up.</summary>
        const float Reach = 60f;

        /// <summary>How far apart the tries are along the kerb.</summary>
        const float Step = 3f;

        /// <summary>Metres of empty kerb left at each end of the slot.
        ///
        /// It was a metre and a half, on the reasoning that a metre and a half is room to
        /// pull out. It is not. Leaving a slot is a DIAGONAL - the flank has to be past
        /// what is parked ahead by the time the nose is level with it - and on a wide
        /// street the swing from the kerb to the lane is four metres of lateral, which
        /// the turning circle spreads over seven metres of road. Parked a metre and a
        /// half behind the outfit's own motorcycle, a crew car ordered across town never
        /// got out of its slot at all: it reversed, crept up, reversed again, and held
        /// the running lane beside it for a minute and a half of the run that found it.
        /// Four metres is the swing's first three plus a margin - still a tight kerb, and
        /// one a car can actually leave.</summary>
        const float PullOutRoom = 4f;

        const float NearbyRoadReach = 80f;
        const int MaxRoadCandidates = 12;

        struct RoadCandidate
        {
            public Carriageway Road;
            public float S, D, DistanceSq;
        }

        static readonly List<RoadCandidate> Candidates = new List<RoadCandidate>();

        static int CompareCandidate(RoadCandidate a, RoadCandidate b) =>
            a.DistanceSq.CompareTo(b.DistanceSq);

        /// <summary>A free length of kerb beside this man to leave a car of this size
        /// on: nose along the traffic on that side, its flank a hand over the stone,
        /// clear of both junctions and of everything already standing there.
        ///
        /// The search walks OUT from where he stands, nearest first, so the car turns
        /// up where he left it rather than at the end of the street.</summary>
        public static bool KerbSlotNear(LaneNet net, Vector3 near, float halfLength, float halfWidth,
            out Vector3 pos, out Quaternion rot)
            => KerbSlot(net, near, halfLength, halfWidth,
                NearbyRoadReach, MaxRoadCandidates, null, out pos, out rot);

        /// <summary>The nearby free kerb, subject also to a caller's claim book. Road
        /// occupancy can only see cars that are already there; a convoy choosing its
        /// destinations in one frame uses this overload so it does not hand every car
        /// the same still-empty piece of kerb.</summary>
        public static bool KerbSlotNear(LaneNet net, Vector3 near, float halfLength, float halfWidth,
            System.Predicate<Vector3> accepts, out Vector3 pos, out Quaternion rot)
            => KerbSlot(net, near, halfLength, halfWidth,
                NearbyRoadReach, MaxRoadCandidates, accepts, out pos, out rot);

        /// <summary>The closest legal free kerb in the road network. A police response
        /// uses this only when every normal nearby candidate is occupied: continuing to
        /// search real kerbs is preferable to declaring a stopped traffic lane a park.</summary>
        public static bool NearestLegalKerbSlot(LaneNet net, Vector3 near,
            float halfLength, float halfWidth, out Vector3 pos, out Quaternion rot)
            => KerbSlot(net, near, halfLength, halfWidth,
                float.PositiveInfinity, int.MaxValue, null, out pos, out rot);

        public static bool NearestLegalKerbSlot(LaneNet net, Vector3 near,
            float halfLength, float halfWidth, System.Predicate<Vector3> accepts,
            out Vector3 pos, out Quaternion rot)
            => KerbSlot(net, near, halfLength, halfWidth,
                float.PositiveInfinity, int.MaxValue, accepts, out pos, out rot);

        static bool KerbSlot(LaneNet net, Vector3 near, float halfLength, float halfWidth,
            float nearbyRoadReach, int maxRoadCandidates, System.Predicate<Vector3> accepts,
            out Vector3 pos, out Quaternion rot)
        {
            pos = near;
            rot = Quaternion.identity;
            if (net == null) return false;

            // A shop can face a short no-parking segment, a full kerb, or the wrong side
            // of a divided road. The old nearest-road-only rule then gave up even though
            // the next corner had an empty legal kerb. Rank the nearby surface roads and
            // try both sides, still choosing the closest free physical slot.
            Candidates.Clear();
            float reachSq = nearbyRoadReach * nearbyRoadReach;
            for (int i = 0; i < net.Roads.Count; i++)
            {
                var road = net.Roads[i];
                if (road == null || road.Elevated || (!road.ParkingA && !road.ParkingB)) continue;
                road.Project(near, out float projectedS, out float d);
                float s = Mathf.Clamp(projectedS, 0f, road.Length);
                var q = road.Pose(s, 0f);
                float dx = q.x - near.x, dz = q.z - near.z;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > reachSq) continue;
                Candidates.Add(new RoadCandidate
                {
                    Road = road, S = s, D = d, DistanceSq = distanceSq,
                });
            }
            if (Candidates.Count == 0) return false;
            Candidates.Sort(CompareCandidate);

            bool found = false;
            float bestDistanceSq = float.MaxValue;
            int roadCount = Mathf.Min(maxRoadCandidates, Candidates.Count);
            for (int i = 0; i < roadCount; i++)
            {
                var candidate = Candidates[i];
                int preferred = candidate.D >= 0f ? 1 : -1;
                for (int which = 0; which < 2; which++)
                {
                    int side = which == 0 ? preferred : -preferred;
                    if (!TrySide(candidate.Road, candidate.S, side, near,
                            halfLength, halfWidth, accepts, out var at, out var facing))
                        continue;
                    float dx = at.x - near.x, dz = at.z - near.z;
                    float distanceSq = dx * dx + dz * dz;
                    if (distanceSq >= bestDistanceSq) continue;
                    bestDistanceSq = distanceSq;
                    pos = at;
                    rot = facing;
                    found = true;
                }
            }
            return found;
        }

        static bool TrySide(Carriageway road, float s0, int side, Vector3 near,
            float halfLength, float halfWidth, System.Predicate<Vector3> accepts,
            out Vector3 pos, out Quaternion rot)
        {
            pos = near;
            rot = Quaternion.identity;
            if (road == null || side == 0) return false;
            if (!(side > 0 ? road.ParkingB : road.ParkingA)) return false;

            float kerb = side * (road.HalfRoad - halfWidth + 0.38f);
            if (!road.Drivable(kerb, halfWidth)) return false;
            float d0 = kerb - halfWidth - 0.3f, d1 = kerb + halfWidth + 0.3f;
            float lo = halfLength + 7f, hi = road.Length - halfLength - 7f;
            if (hi <= lo) return false;

            for (float outward = 0f; outward <= Reach; outward += Step)
                for (int direction = 0; direction < 2; direction++)
                {
                    if (direction == 1 && outward <= 0f) continue;
                    float s = Mathf.Clamp(
                        s0 + (direction == 0 ? outward : -outward), lo, hi);
                    if (road.Busy(null, s - halfLength - PullOutRoom,
                            s + halfLength + PullOutRoom, d0, d1))
                        continue;
                    var p = road.Pose(s, kerb);
                    p.y = near.y;
                    if (accepts != null && !accepts(p)) continue;
                    var axis = road.DirAt(s) * side;
                    pos = p;
                    rot = Quaternion.LookRotation(axis, Vector3.up);
                    return true;
                }
            return false;
        }
    }
}
