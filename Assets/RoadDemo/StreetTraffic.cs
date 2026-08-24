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

        /// <summary>One man on foot in the road, and WHOSE he is.
        ///
        /// It used to be a bare position, and that was enough while the only question a
        /// driver asked was "is somebody in my way". It is not enough for the question a
        /// driver in a gunfight asks, which is "is that one of THEIRS" - because the
        /// answer decides whether he brakes or does not (RoadCar.GivesWayTo).</summary>
        public readonly struct Body
        {
            public readonly Vector3 At;

            /// <summary>0 the outfit, else a rival mob; StreetAlarm.PoliceFaction for
            /// the law.</summary>
            public readonly int Faction;

            public Body(Vector3 at, int faction) { At = at; Faction = faction; }
        }

        /// <summary>People on foot the traffic must not drive through - the crews' men,
        /// refilled every frame by the arena.</summary>
        public static readonly List<Body> Bodies = new List<Body>();

        /// <summary>Civilians stood or running in the road this frame - refilled every
        /// frame by the crowd (CivilianAgent.TickCrowd). World positions.</summary>
        public static readonly List<Vector3> Walkers = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Users.Clear();
            Bodies.Clear();
            Walkers.Clear();
            QuietUntil = 0f;
        }

        // ------------------------------------------------------------- the quiet street
        //
        // WHERE A FIGHT IS ABOUT TO HAPPEN, THE TRAFFIC THINS OUT. Not because the
        // drivers know anything - they are told nothing and they never brake for it -
        // but because a street with thirty cars nose to tail on it is a street where a
        // drive-by is a car stuck behind a bus and a fight is three men shooting through
        // traffic. This is a stage direction, and it is deliberate: the arena marks the
        // ground a job has been ordered on and the wandering traffic simply stops
        // TURNING INTO IT (RoadCar.PickNext). Nothing is deleted and nothing pops - the
        // cars already there drive out the way they always would, and over half a minute
        // the street empties itself.

        /// <summary>The middle of the ground being cleared.</summary>
        public static Vector3 QuietAt { get; private set; }

        /// <summary>How wide it reaches.</summary>
        public static float QuietRadius { get; private set; }

        /// <summary>When the traffic may use it again.</summary>
        public static float QuietUntil { get; private set; }

        /// <summary>Is a stretch of town being kept clear at all just now?</summary>
        public static bool QuietOpen => Time.time < QuietUntil && QuietRadius > 1f;

        /// <summary>Keep the traffic off this ground for a while - an ordered job is
        /// about to be done on it. Called again while the job lasts; the widest claim
        /// standing wins, so a second order does not shrink the first one's street.</summary>
        public static void Quiet(Vector3 at, float radius, float seconds)
        {
            if (radius <= 1f || seconds <= 0f) return;
            float until = Time.time + seconds;
            if (QuietOpen && until <= QuietUntil && radius <= QuietRadius) return;
            QuietAt = at;
            QuietRadius = Mathf.Max(radius, QuietOpen ? QuietRadius : 0f);
            QuietUntil = Mathf.Max(until, QuietUntil);
        }

        /// <summary>Is this point on the ground being kept clear?</summary>
        public static bool InQuiet(Vector3 p)
        {
            if (!QuietOpen) return false;
            var d = p - QuietAt;
            d.y = 0f;
            return d.sqrMagnitude < QuietRadius * QuietRadius;
        }

        /// <summary>Does this stretch of road run through the claim at all - not just
        /// end in it? A long avenue crosses the middle of the ground and comes out the
        /// far side, and judged by its far end alone it reads as a way OUT of the
        /// fight, which is how the traffic kept being sent straight through it.</summary>
        public static bool CrossesQuiet(Vector3 a, Vector3 b)
        {
            if (!QuietOpen) return false;
            var ab = b - a;
            ab.y = 0f;
            var ac = QuietAt - a;
            ac.y = 0f;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-4f ? Mathf.Clamp01(Vector3.Dot(ac, ab) / len2) : 0f;
            var near = ac - ab * t;
            return near.sqrMagnitude < QuietRadius * QuietRadius;
        }

        // --------------------------------------------------------------- thinning out

        /// <summary>Seconds between one car being taken off the ground and the next: the
        /// street is emptied over half a minute, not in a frame. Nothing the player is
        /// LOOKING at is ever moved - see Thin.</summary>
        public static float ThinEvery = 0.7f;

        static float _thinAt;
        static int _thinFrom;

        /// <summary>How far behind the camera a car has to be before it may be moved at
        /// all, on top of being out of the frame - a car just off the edge of the screen
        /// is one pan away from being on it.</summary>
        public static float ThinBerth = 25f;

        /// <summary>Take the traffic off the ground a job is being done on - the cars
        /// NOBODY IS LOOKING AT, one every ThinEvery seconds, put back on a lane well
        /// away from the claim.
        ///
        /// Turning the wanderers away at the junctions (RoadCar.PickNext) empties a
        /// street slowly and only empties the streets that HAVE junctions to turn at:
        /// a car halfway down a long avenue drives through the fight because nothing
        /// ever asks it again. This is the other half - and it is a stage direction, so
        /// it is done strictly off camera: a car in the frame, or near enough to the
        /// frame to be panned onto, is left exactly where it is and drives out on its
        /// own.</summary>
        public static void Thin(IReadOnlyList<RoadCar> cars, IReadOnlyList<RoadEdge> edges, Camera eye)
        {
            if (!QuietOpen || cars == null || cars.Count == 0 || edges == null || edges.Count == 0) return;
            if (Time.time < _thinAt) return;
            _thinAt = Time.time + ThinEvery;
            if (eye == null) eye = Camera.main;
            if (eye == null) return;

            var planes = GeometryUtility.CalculateFrustumPlanes(eye);
            var from = eye.transform.position;

            // one pass over the cars, starting where the last pass left off, so a car
            // that cannot be moved this second does not block the ones behind it
            for (int n = 0; n < cars.Count; n++)
            {
                int i = (_thinFrom + n) % cars.Count;
                var car = cars[i];
                if (car == null || car.Parked || car.Derelict || car.Wrecked) continue;
                var at = car.RoadPosition;
                if (!InQuiet(at)) continue;
                if (Vector3.Distance(at, from) < ThinBerth) continue;
                if (GeometryUtility.TestPlanesAABB(planes, new Bounds(at, new Vector3(6f, 4f, 6f)))) continue;

                var lane = FarLane(edges, from);
                if (lane == null) return;
                car.Spawn(lane, lane.Length * 0.5f);
                _thinFrom = i + 1;
                return;
            }
            _thinFrom = 0;
        }

        // A lane well clear of the claim to put a car back on, and not under the
        // camera's nose either - a car appearing in shot is worse than a car in a queue.
        // Counted round the list, never drawn: the arena shares one random stream and a
        // draw here would move every later one (the prop bags' rule).
        static int _laneFrom;

        static RoadEdge FarLane(IReadOnlyList<RoadEdge> edges, Vector3 eye)
        {
            for (int n = 0; n < edges.Count; n++)
            {
                int i = (_laneFrom + n) % edges.Count;
                var e = edges[i];
                if (e == null || e.Length < 30f) continue;
                var mid = (e.Start + e.End) * 0.5f;
                if (InQuiet(mid) || CrossesQuiet(e.Start, e.End)) continue;
                if (Vector3.Distance(mid, eye) < 60f) continue;
                if (Occupied(mid, 9f)) continue;
                _laneFrom = i + 1;
                return e;
            }
            return null;
        }

        static bool Occupied(Vector3 at, float berth)
        {
            for (int i = 0; i < Users.Count; i++)
            {
                var u = Users[i];
                if (u == null) continue;
                var d = u.RoadPosition - at;
                d.y = 0f;
                if (d.sqrMagnitude < berth * berth) return true;
            }
            return false;
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
                    // a colour of its own, unless the body carries somebody's livery
                    LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
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
