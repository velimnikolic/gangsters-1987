using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The plain traffic of a small scene (the crew demo's four streets): pack cars
    /// with somebody at the wheel, driving the lane network the way the city's
    /// traffic does (RoadCar with DriverProfile.Traffic - keeps its lane, follows,
    /// wanders at the junctions, swings a little over the crown round a car at the
    /// kerb, gives way to a car of the outfit's or the law's nose to nose, brakes
    /// dead at gunfire, turns round at the dead ends, and only after a long wait
    /// behind a jam uses the far lane or turns round). The point is to have
    /// something real on the road for the crew's car to thread through, brake for
    /// and swing round in front of. Kinematic, no physics.
    ///
    /// It also keeps the three lists every driver and every man on foot reads:
    /// everyone on the road, the crews' men, and the crowd in the road.
    /// </summary>
    public sealed class StreetTraffic : MonoBehaviour
    {
        /// <summary>Everyone on the road this frame - traffic, the crews' cars, the
        /// law's, the props stood at the kerb.</summary>
        public static readonly List<IRoadUser> Users = new List<IRoadUser>();

        /// <summary>People on foot the traffic must not drive through - the crews' men,
        /// refilled every frame by the arena. World positions.</summary>
        public static readonly List<Vector3> Bodies = new List<Vector3>();

        /// <summary>Civilians stood or running in the road this frame - refilled every
        /// frame by the crowd (CivilianAgent.TickCrowd). World positions.</summary>
        public static readonly List<Vector3> Walkers = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Users.Clear();
            Bodies.Clear();
            Walkers.Clear();
        }

        /// <summary>A shot went off here (a caller without a gun to name): every driver
        /// in earshot reacts through StreetAlarm, which is where shots are kept now.</summary>
        public static void Alarm(Vector3 where) => StreetAlarm.Report(where, null, 0, 45f);

        /// <summary>A pack car of the traffic: the shared driving, this body's wheels.</summary>
        public sealed class Car : RoadCar
        {
            public CarBody Body;
            protected override void OnPlaced(float dt, float speed, float steerDegrees) => Body?.TickWheels(dt, speed, steerDegrees);
        }

        readonly List<Car> _cars = new List<Car>();
        public IReadOnlyList<Car> Cars => _cars;
        LaneNet _net;

        /// <summary>Lays <paramref name="count"/> cars over these roads of the network
        /// (all of it when none are named), spread along their lanes, from the given
        /// prefabs (any pack car body) - each with somebody at the wheel out of
        /// <paramref name="people"/> when there are people and a sit clip.</summary>
        public void Init(IList<GameObject> prefabs, LaneNet net, float roadY, int count,
            IList<GameObject> people = null, AnimationClip sitLoop = null, IList<Carriageway> roads = null)
        {
            _net = net;
            if (prefabs == null || prefabs.Count == 0 || count <= 0 || net == null) return;
            var lanes = new List<RoadEdge>();
            foreach (var r in roads ?? (IList<Carriageway>)net.Roads)
                foreach (var l in r.Lanes) if (l.Length > 20f) lanes.Add(l);
            if (lanes.Count == 0) return;

            var root = new GameObject("Traffic").transform;
            int placed = 0;
            for (int round = 0; placed < count && round < 40; round++)
            {
                bool any = false;
                foreach (var lane in lanes)
                {
                    if (placed >= count) break;
                    float s = 8f + round * 22f;
                    if (s > lane.Length - 12f) continue;
                    any = true;
                    var prefab = prefabs[Random.Range(0, prefabs.Count)];
                    var go = Instantiate(prefab, root);
                    go.name = prefab.name;
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                    foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                    go.transform.SetPositionAndRotation(lane.Start + lane.Dir * s + Vector3.up * roadY, Quaternion.LookRotation(lane.Dir, Vector3.up));
                    var body = new CarBody(go.transform);
                    var car = new Car
                    {
                        Tf = go.transform, Body = body, HalfLen = body.HalfLength, HalfWide = body.HalfWidth,
                        AxleBack = body.AxleBack, RoadY = roadY, Net = net, Profile = DriverProfile.Traffic,
                        Tag = "traffic",
                    };
                    // the body is measured first (CarBody reads the renderers); then the driver
                    CarOccupant.Crew(go.transform, people, sitLoop, passengerChance: 0.3f);
                    car.Spawn(lane, s);
                    _cars.Add(car);
                    Users.Add(car);
                    placed++;
                }
                if (!any) break;
            }
        }

        void OnDestroy()
        {
            foreach (var c in _cars) { c.Despawn(); Users.Remove(c); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _cars.Count; i++) _cars[i].Tick(dt);
        }
    }
}
