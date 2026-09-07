using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The two-wheelers of a street: the ones riding it and the ones stood along it.
    ///
    /// StreetTraffic's sibling, and small for the same reason - the driving is
    /// RoadCar's and the riding is BikePose's, so what is left here is only the two
    /// questions a builder actually asks. Which bodies may be ridden (the catalogue's,
    /// never a folder scan: every scan in the project denies "bike" and "moped" by
    /// name, and rightly, because for years a two-wheeler in the traffic was a thing
    /// that slid along with nobody on it). And where they go.
    ///
    /// Deliberately a handful rather than a fleet. A bike is a LateUpdate per rider on
    /// top of a skinned mesh nobody can cull behind glass, and a street reads as having
    /// motorcycles on it at four of them, not forty.
    /// </summary>
    public sealed class StreetBikes : MonoBehaviour
    {
        // The packs that have a two-wheeler at all, in the order they are asked. Only
        // these two do; the rest of the project's Synty packs have none.
        static readonly string[] Folders =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/",
        };

        /// <summary>The catalogue's civilian two-wheelers as prefabs, in its order.
        /// Empty outside the editor, and empty if the packs are gone - every caller
        /// treats that as "no bikes on this street" and carries on.</summary>
        public static List<GameObject> Bodies()
        {
            var bodies = new List<GameObject>();
            foreach (var name in LivingCity.Gameplay.VehicleCatalog.Motorcycles)
            {
                var body = Body(name, marked: false);
                if (body != null) bodies.Add(body);
            }
            return bodies;
        }

        /// <summary>The law's machine - the liveried tourer. Asked for by name, which
        /// is how anything marked is had (VehicleCatalog).</summary>
        public static GameObject PoliceBody()
        {
            foreach (var name in LivingCity.Gameplay.VehicleCatalog.PoliceMotorcycles)
            {
                var path = LivingCity.Gameplay.VehicleCatalog.PoliceFleetFolder + name + ".prefab";
                var body = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (body != null) return body;
            }
            return null;
        }

        static GameObject Body(string name, bool marked)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var folder in Folders)
            {
                var path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsBarred(path)) continue;
                if (!marked && LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (prefab != null) return prefab;
            }
            return null;
        }

        // ------------------------------------------------------------------ the riding ones

        readonly List<RoadBike> _bikes = new List<RoadBike>();
        public IReadOnlyList<RoadBike> Bikes => _bikes;

        /// <summary>Lays bikes over these roads of the network (all of it when none are
        /// named), each with a rider and now and then a mate behind him. They join
        /// StreetTraffic.Users, so every driver on the street sees them and plans round
        /// them like anything else with a body.</summary>
        public void Init(LaneNet net, int count, float roadY, IList<GameObject> people,
            AnimationClip rideClip, float pillionChance = 0.25f, int layer = -1,
            IList<Carriageway> roads = null, IList<GameObject> bodies = null)
        {
            if (net == null || count <= 0) return;
            var prefabs = bodies != null && bodies.Count > 0 ? bodies : Bodies();
            if (prefabs.Count == 0) return;

            var lanes = new List<RoadEdge>();
            foreach (var r in roads ?? (IList<Carriageway>)net.Roads)
                foreach (var l in r.Lanes) if (l.Length > 24f) lanes.Add(l);
            if (lanes.Count == 0) return;

            var root = new GameObject("Bikes").transform;
            int placed = 0;
            // Down the lanes on a spacing of their own, and never on top of anything
            // already standing there. Off a different stride from the traffic's is not
            // enough on its own: two arithmetic progressions cross, and the first city
            // run of this laid a bike inside a car and left the belt shoving at the two
            // of them for the rest of the run. So ask the road (Carriageway.Busy, the
            // lane net's own occupancy) before putting anything down.
            for (int round = 0; placed < count && round < 40; round++)
            {
                bool any = false;
                foreach (var lane in lanes)
                {
                    if (placed >= count) break;
                    float s = 15f + round * 26f;
                    if (s > lane.Length - 12f) continue;
                    any = true;
                    var prefab = prefabs[Random.Range(0, prefabs.Count)];
                    if (!Free(lane, s, prefab)) continue;
                    var bike = RoadBike.Build(prefab, root,
                        lane.Start + lane.Dir * s + Vector3.up * roadY,
                        Quaternion.LookRotation(lane.Dir, Vector3.up), roadY, net);
                    if (bike == null) continue;
                    // the body is measured on Build; the rider is seated off those
                    // measurements, so this order is not an accident
                    bike.Crew(people, rideClip, pillionChance, layer);
                    if (bike.Empty) { Object.Destroy(bike.Tf.gameObject); continue; } // nobody to ride it
                    bike.Spawn(lane, s);
                    _bikes.Add(bike);
                    StreetTraffic.Users.Add(bike);
                    placed++;
                }
                if (!any) break;
            }
        }

        // Is this length of lane clear for a body of this size? The gap either end is
        // what a car keeps to the one in front, so a bike does not land nose to tail
        // with something and spend its first second braking.
        static bool Free(RoadEdge lane, float progress, GameObject prefab)
        {
            var road = lane?.Road;
            if (road == null) return true;
            if (!CarBody.MeasureFootprint(prefab != null ? prefab.transform : null, out float hl, out float hw))
            {
                hl = 1.3f;
                hw = 0.5f;
            }
            hw = Mathf.Max(0.42f, hw);
            float s = lane.RoadS(progress);
            float d = lane.Offset;
            return !road.Busy(null, s - hl - 2.5f, s + hl + 2.5f, d - hw - 0.3f, d + hw + 0.3f);
        }

        void OnDestroy()
        {
            foreach (var b in _bikes) { b.Despawn(); StreetTraffic.Users.Remove(b); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _bikes.Count; i++) _bikes[i].Tick(dt);
        }

        // ------------------------------------------------------------------ the stood ones

        /// <summary>A bike left at the kerb nearest this point, on its stand, nobody on
        /// it - street furniture with a body, so the traffic goes round it. Returns it
        /// (unticked: it never moves) or null when there is no room.</summary>
        public static RoadBike Park(LaneNet net, Transform parent, GameObject prefab, Vector3 near, float roadY)
        {
            if (net == null || prefab == null) return null;
            if (!CarBody.MeasureFootprint(prefab.transform, out float halfLength, out float halfWidth)) return null;
            halfWidth = Mathf.Max(0.42f, halfWidth);
            if (!CrewCars.KerbSlotNear(net, near, halfLength, halfWidth, out var pos, out var rot)) return null;

            var bike = RoadBike.Build(prefab, parent, new Vector3(pos.x, roadY, pos.z), rot, roadY, net);
            if (bike == null) return null;
            if (!bike.PlaceAt(new Vector3(pos.x, roadY, pos.z), rot * Vector3.forward))
            {
                Object.Destroy(bike.Tf.gameObject);
                return null;
            }
            bike.Halt(hard: true);
            bike.SettleStand();
            StreetTraffic.Users.Add(bike);
            // AND INTO THE LANE NET'S OCCUPANCY, which is the half that was missing.
            // StreetTraffic.Users is what a driver BRAKES for; the occupancy is what
            // CrewCars.KerbSlotNear reads when it hands out a length of kerb. Without
            // this a stood bike was invisible to the parking, so a car was sent to park
            // exactly where one stood, drove up to it, braked for it - and sat there for
            // the rest of the run. A machine on its stand is furniture: it takes its
            // ground the same way a parked car does.
            net.AddStatic(bike);
            return bike;
        }

        /// <summary>A few of them along a stretch of street, out of the catalogue's
        /// bodies - what a kerb outside a bar or a tenement looks like. Returns how
        /// many actually found room.</summary>
        public static int ParkSeveral(LaneNet net, Transform parent, IList<Vector3> spots, float roadY,
            IList<GameObject> bodies = null)
        {
            var prefabs = bodies != null && bodies.Count > 0 ? bodies : Bodies();
            if (prefabs.Count == 0 || spots == null) return 0;
            int done = 0;
            foreach (var spot in spots)
                if (Park(net, parent, prefabs[Random.Range(0, prefabs.Count)], spot, roadY) != null) done++;
            return done;
        }
    }
}
