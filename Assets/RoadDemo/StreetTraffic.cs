using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Anything on the road other cars must reckon with: where it is, which
    /// way it points, how fast it goes, how long it is. The outfit's car and the
    /// traffic both answer it, so each can keep off the other.</summary>
    public interface IRoadUser
    {
        Vector3 RoadPosition { get; }
        Vector3 RoadForward { get; }
        float RoadSpeed { get; }
        float HalfLength { get; }
    }

    // Plain traffic for a single straight street: a handful of pack cars per lane,
    // each running its lane at its own pace, keeping a following gap off the car
    // ahead (the outfit's car included), easing toward the crown to squeeze past a
    // car pulled in at the kerb, and looping round to the far end when it runs off
    // this one. No signals, no turns - the point is to have something on the road
    // for the crew's car to thread through, brake for and swing round in front of.
    // Self-contained like the rest of the demo: kinematic, no physics.
    public sealed class StreetTraffic : MonoBehaviour
    {
        /// <summary>Everyone on the road this frame - traffic and the crews' cars.</summary>
        public static readonly List<IRoadUser> Users = new List<IRoadUser>();

        /// <summary>People on foot the traffic must not drive through - the crews' men,
        /// refilled every frame by the arena. World positions.</summary>
        public static readonly List<Vector3> Bodies = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Users.Clear();
            Bodies.Clear();
            alarmAt = -100f;
        }

        static Vector3 alarmWhere;
        static float alarmAt = -100f;

        /// <summary>A shot went off here: every driver within earshot stands on the
        /// brake and stays stopped while it lasts, then creeps on. Called by the arena
        /// on every shot, so a running gunfight keeps the street frozen.</summary>
        public static void Alarm(Vector3 where)
        {
            alarmWhere = where;
            alarmAt = Time.time;
        }

        public const float LaneOffset = 2.5f;   // lane centre off the crown, either side
        const float FollowGap = 7f;             // metres of clear road kept to the car ahead
        const float PassOffset = 1.6f;          // where the lane centre moves to squeeze past a parked car

        sealed class Car : IRoadUser
        {
            public Transform Tf;
            public float Dir;          // +1 eastbound (south lane), -1 westbound (north lane)
            public float Cruise, Speed, Lateral, HalfLen;
            public float PanicUntil;   // stopped dead by gunfire until then
            public List<Transform> Wheels = new List<Transform>();
            public List<Quaternion> WheelRest = new List<Quaternion>();
            public float WheelRadius = 0.33f;

            public Vector3 RoadPosition => Tf.position;
            public Vector3 RoadForward => Tf.forward;
            public float RoadSpeed => Speed;
            public float HalfLength => HalfLen;
        }

        readonly List<Car> _cars = new List<Car>();
        float _streetZ, _xFrom, _xTo, _roadY;

        /// <summary>Lays <paramref name="count"/> cars on the street between the two
        /// x's, half a lane each way, from the given prefabs (any pack car body).</summary>
        public void Init(IList<GameObject> prefabs, float streetZ, float xFrom, float xTo, float roadY, int count)
        {
            _streetZ = streetZ;
            _xFrom = xFrom;
            _xTo = xTo;
            _roadY = roadY;
            if (prefabs == null || prefabs.Count == 0 || count <= 0) return;

            var root = new GameObject("Traffic").transform;
            float span = xTo - xFrom;
            for (int i = 0; i < count; i++)
            {
                float dir = i % 2 == 0 ? 1f : -1f;
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                var car = new Car { Tf = go.transform, Dir = dir, Cruise = Random.Range(7f, 10.5f) };
                car.Speed = car.Cruise;
                car.Lateral = -dir * LaneOffset; // right-hand traffic: eastbound in the south lane
                float x = xFrom + span * ((i / 2 + 0.37f * (i % 2)) / Mathf.Max(1, (count + 1) / 2));
                go.transform.SetPositionAndRotation(new Vector3(x, roadY, streetZ + car.Lateral),
                    Quaternion.LookRotation(Vector3.right * dir, Vector3.up));
                var b = Bounds(go);
                car.HalfLen = Mathf.Max(1.5f, b.extents.z);
                FindWheels(car);
                _cars.Add(car);
                Users.Add(car);
            }
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void FindWheels(Car car)
        {
            foreach (var t in car.Tf.GetComponentsInChildren<Transform>())
            {
                if (t == car.Tf || t.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.name.IndexOf("Steering", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                car.Wheels.Add(t);
                car.WheelRest.Add(t.localRotation);
                var r = t.GetComponentInChildren<Renderer>();
                if (r) car.WheelRadius = Mathf.Max(0.2f, r.bounds.extents.y);
            }
        }

        void OnDestroy()
        {
            foreach (var c in _cars) Users.Remove(c);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var car in _cars)
            {
                // gunfire close by: brake dead and sit tight until it has been quiet a while
                if (Time.time - alarmAt < 0.5f && (car.Tf.position - alarmWhere).sqrMagnitude < 40f * 40f)
                    car.PanicUntil = Mathf.Max(car.PanicUntil, Time.time + Random.Range(4f, 7f));
                bool panicked = Time.time < car.PanicUntil;

                // the car ahead in this lane - traffic, the outfit's, a man on foot in the
                // road - sets the pace
                float clear = ClearAhead(car, out var parkedAlongside);
                float want = car.Cruise;
                if (clear < FollowGap + car.HalfLen) want = 0f;
                else if (clear < FollowGap + car.HalfLen + 10f) want = Mathf.Min(want, car.Cruise * (clear - FollowGap - car.HalfLen) / 10f + 1.5f);
                if (panicked) want = 0f;
                car.Speed = Mathf.MoveTowards(car.Speed, want, (want < car.Speed ? (panicked ? 14f : 9f) : 3.5f) * dt);

                // squeeze past a car pulled in at the kerb: ease toward the crown, then
                // back - as a car does, on a slant, the nose leading; the sideways drift is
                // a fraction of the forward speed and the heading follows the motion, so
                // it never slides across the road on rails
                float lateralWant = -car.Dir * (parkedAlongside ? PassOffset : LaneOffset);
                float slide = Mathf.Max(0.3f, car.Speed * 0.16f);
                float before = car.Lateral;
                car.Lateral = Mathf.MoveTowards(car.Lateral, lateralWant, slide * dt);

                var p = car.Tf.position;
                float dx = car.Dir * car.Speed * dt;
                float dz = car.Lateral - before;
                p.x += dx;
                p.z = _streetZ + car.Lateral;
                p.y = _roadY;
                // off the end: round to the far end of the street
                if (car.Dir > 0f && p.x > _xTo + 6f) p.x = _xFrom - 6f;
                if (car.Dir < 0f && p.x < _xFrom - 6f) p.x = _xTo + 6f;
                car.Tf.position = p;
                var heading = new Vector3(dx, 0f, dz);
                if (heading.sqrMagnitude < 1e-8f) heading = Vector3.right * car.Dir;
                car.Tf.rotation = Quaternion.RotateTowards(car.Tf.rotation,
                    Quaternion.LookRotation(heading.normalized, Vector3.up), 90f * dt);

                // wheels: rolling with the road
                float spin = car.Speed * dt / car.WheelRadius * Mathf.Rad2Deg;
                for (int i = 0; i < car.Wheels.Count; i++)
                {
                    var w = car.Wheels[i];
                    if (!w) continue;
                    w.localRotation = w.localRotation * Quaternion.AngleAxis(spin, Vector3.right);
                }
            }
        }

        // Metres of clear road ahead in this car's lane (to the nearest other road user
        // in it, going the same way or stopped), and whether a stopped car sits at the
        // kerb alongside or just ahead - the one it swings out around.
        float ClearAhead(Car car, out bool parkedAlongside)
        {
            parkedAlongside = false;
            float best = float.MaxValue;
            var p = car.Tf.position;
            // a man standing in my lane is a wall
            foreach (var b in Bodies)
            {
                float ahead = (b.x - p.x) * car.Dir;
                if (ahead <= 0f) continue;
                if (Mathf.Abs(b.z - (_streetZ + car.Lateral)) > 1.7f) continue;
                float gap = ahead - 0.5f;
                if (gap < best) best = gap;
            }
            foreach (var u in Users)
            {
                if (ReferenceEquals(u, car)) continue;
                var q = u.RoadPosition;
                float dz = q.z - _streetZ;
                // in my half of the road?
                if (Mathf.Sign(dz) != Mathf.Sign(-car.Dir) && Mathf.Abs(dz) > 0.6f) continue;
                float ahead = (q.x - p.x) * car.Dir;
                bool stopped = u.RoadSpeed < 0.5f;
                bool oncoming = Vector3.Dot(u.RoadForward, Vector3.right * car.Dir) < -0.5f && !stopped;
                if (oncoming) continue;
                // pulled in at the kerb: something to pass, not to queue behind
                bool atKerb = stopped && Mathf.Abs(dz) > LaneOffset + 0.6f;
                if (atKerb)
                {
                    if (ahead > -u.HalfLength - car.HalfLen - 2f && ahead < 14f) parkedAlongside = true;
                    continue;
                }
                if (ahead <= 0f) continue;
                float gap = ahead - u.HalfLength;
                if (gap < best) best = gap;
            }
            return best;
        }
    }
}
