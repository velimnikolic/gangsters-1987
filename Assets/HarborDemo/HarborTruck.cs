using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // A lorry on her round: in off the approach road through one gate, round the
    // port's loop, drawn up at the kerb in front of a shed door or at a loading dock,
    // doors open while she is worked, then out through the other gate and away up
    // the road - and nothing for a minute or two before the next one comes.
    //
    // She is not an empty shell: a driver sits in her cab (the traffic's CarOccupant,
    // on the bench sit loop), and her box is stowed with pallets of goods that can be
    // seen through the open doors - and what she is doing there is told by them: a
    // lorry delivering stands while the pallets inside her go one by one and the
    // stack on the forecourt grows, one collecting the other way round, both a pallet
    // at a time over the length of the stop. Waypoints and a braking curve (the crew
    // car's), the box doors swung on their hinges by name. Plain class ticked by the
    // builder.
    public sealed class HarborTruck
    {
        const float RoadSpeed = 9f, YardSpeed = 4.5f;
        const float Accel = 2.5f, Brake = 3.5f;
        const float TurnRate = 90f;

        /// <summary>The body - or bodies, one picked per trip: the gang pack's box
        /// lorry, the town's box lorry in its own colours, the town's flatbed.</summary>
        public GameObject Prefab;
        public List<GameObject> Prefabs;
        public Transform Parent;
        public List<Vector3> Route = new List<Vector3>();
        public int DockNode = -1;            // where it stops and opens up
        public float DockStay = 22f;
        public Vector2 GapRange = new Vector2(80f, 150f);
        public int YardFrom = 1;             // nodes from here on are yard speed
        public int YardTo = int.MaxValue;    // and up to here - past it she is on the road again

        /// <summary>The goods at the bay she works, in the order they are handled.</summary>
        public List<GameObject> Load;
        /// <summary>True if she brings them (the stack grows while she stands), false if
        /// she takes them away (it goes).</summary>
        public bool Delivering;

        /// <summary>What she is stowed with and who drives her.</summary>
        public GameObject Pallet;
        public List<GameObject> Freight;
        public List<GameObject> People;
        public AnimationClip SitLoop;

        /// <summary>Standing at her bay with the doors open - the forklift and the hands
        /// at the door work while this is true.</summary>
        public bool Docked => _docked;
        public Transform Tf => _tf;

        Transform _tf, _doorL, _doorR;
        readonly RoadDemo.WheelSpin _wheels = new RoadDemo.WheelSpin();
        readonly List<GameObject> _inside = new List<GameObject>();
        int _node;
        float _speed, _timer, _doorOpen;
        bool _docked;
        readonly System.Random _rng = new System.Random(19);

        /// <summary>How long before she first shows - so a yard's lorries do not all
        /// come through the gate together.</summary>
        public void Begin(float delay) => _timer = delay;

        public void Tick(float dt)
        {
            if (Route.Count < 2 || (Prefab == null && (Prefabs == null || Prefabs.Count == 0))) return;
            if (_tf == null)
            {
                _timer -= dt;
                if (_timer > 0f) return;
                Spawn();
                return;
            }
            // doors
            float wantDoor = _docked ? 1f : 0f;
            _doorOpen = Mathf.MoveTowards(_doorOpen, wantDoor, dt * 0.6f);
            if (_doorL != null) _doorL.localRotation = Quaternion.Euler(0f, -95f * _doorOpen, 0f);
            if (_doorR != null) _doorR.localRotation = Quaternion.Euler(0f, 95f * _doorOpen, 0f);

            if (_docked)
            {
                _timer -= dt;
                // the goods handled a pallet at a time over the length of the stop
                ShowLoad(Mathf.Clamp01(1f - _timer / Mathf.Max(0.01f, DockStay)));
                if (_timer > 0f) return;
                if (_doorOpen > 0.01f) { _docked = false; return; }
                _docked = false;
            }
            if (_node >= Route.Count - 1)
            {
                Object.Destroy(_tf.gameObject);
                _tf = null;
                _inside.Clear();
                _timer = HarborKit.Range(_rng, GapRange.x, GapRange.y);
                return;
            }
            var target = Route[_node + 1];
            var pos = _tf.position;
            var to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            bool stopHere = _node + 1 == DockNode || _node + 1 == Route.Count - 1;
            float cap = _node + 1 >= YardFrom && _node + 1 <= YardTo ? YardSpeed : RoadSpeed;
            float want = stopHere ? Mathf.Min(cap, Mathf.Sqrt(2f * Brake * Mathf.Max(0f, dist - 0.4f))) : cap;
            // a waypoint reached - or overshot: a lorry that has swung past one (the
            // point is behind her, and near) takes the next rather than circling back
            bool passed = !stopHere && dist < 7f && Vector3.Dot(to, _tf.forward) < 0f;
            if (dist < (stopHere ? 0.5f : 1.8f) || passed)
            {
                _node++;
                if (_node == DockNode)
                {
                    _docked = true;
                    _timer = DockStay;
                    _speed = 0f;
                }
                return;
            }
            _speed = Mathf.MoveTowards(_speed, want, (want > _speed ? Accel : Brake) * dt);
            var face = Quaternion.LookRotation(to / dist, Vector3.up);
            _tf.rotation = Quaternion.RotateTowards(_tf.rotation, face, TurnRate * dt * Mathf.Clamp01(_speed / 2f + 0.3f));
            _tf.position = pos + _tf.forward * Mathf.Min(dist, _speed * dt);
            // six wheels, each rolling on its own hub, the front pair into the corner
            _wheels.Tick(dt, _speed, Vector3.SignedAngle(_tf.forward, to / dist, Vector3.up));
        }

        void Spawn()
        {
            var prefab = Prefabs != null && Prefabs.Count > 0 ? HarborKit.Pick(_rng, Prefabs) : Prefab;
            var go = Object.Instantiate(prefab, Route[0], Quaternion.LookRotation((Route[1] - Route[0]).normalized, Vector3.up), Parent);
            go.name = "Truck";
            HarborKit.StripBehaviours(go, keepAnimator: false);
            _tf = go.transform;
            _doorL = HarborKit.FindDeep(_tf, "Door_L");
            _doorR = HarborKit.FindDeep(_tf, "Door_R");
            _wheels.Read(_tf);
            _node = 0;
            _speed = 0f;
            _docked = false;
            _doorOpen = 0f;
            _inside.Clear();
            _inside.AddRange(Stow(_tf, Pallet, Freight, Load != null ? Load.Count : 3, _rng));
            Drive(_tf, People, SitLoop, _rng, mate: _rng.NextDouble() < 0.35);
            ShowLoad(0f);
        }

        /// <summary>The goods at a point through the stop: outside, the stack at her
        /// tail grown this far if she is delivering (gone this far if collecting);
        /// inside, the opposite - what is not yet out is still aboard.</summary>
        void ShowLoad(float through)
        {
            int n = Load != null ? Load.Count : 0;
            int shown = Mathf.RoundToInt((Delivering ? through : 1f - through) * Mathf.Max(n, _inside.Count));
            for (int i = 0; i < n; i++)
                if (Load[i] != null) Load[i].SetActive(i < shown);
            for (int i = 0; i < _inside.Count; i++)
                if (_inside[i] != null) _inside[i].SetActive(i >= shown);
        }

        // ------------------------------------------------------------ stowing, driving

        /// <summary>Pallets of goods in a lorry's box - or on her flatbed - from the tail
        /// forward, children of the body so they ride with her. The box is found by
        /// name (the pack's Delivery attachment; the flatbed where that is switched
        /// off), its floor measured off its own renderers: the bottom of the box, the
        /// top of the bed. Returns the pallets, tail first.</summary>
        public static List<GameObject> Stow(Transform truck, GameObject pallet, IList<GameObject> freight, int count, System.Random rng)
        {
            var list = new List<GameObject>();
            if (truck == null || pallet == null || count <= 0) return list;
            var box = HarborKit.FindDeep(truck, "Delivery_01");
            bool inBox = box != null && box.gameObject.activeInHierarchy;
            var bed = inBox ? box : HarborKit.FindDeep(truck, "Flatbed_01");
            if (bed == null || !bed.gameObject.activeInHierarchy) return list;
            var rs = bed.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return list;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var fwd = truck.forward; fwd.y = 0f; fwd.Normalize();
            float along = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), 0f, Mathf.Abs(fwd.z)));
            float floorY = inBox ? b.min.y + 0.08f : b.max.y;
            var pb = HarborKit.PrefabBounds(pallet);
            float yaw = truck.eulerAngles.y;
            const float First = 1.0f, Step = 1.45f;
            for (int i = 0; i < count; i++)
            {
                float back = First + i * Step;                 // from the tail
                if (back + 0.7f > along * 2f) break;          // no more room forward
                var at = b.center - fwd * (along - back);
                at.y = floorY;
                var slot = new GameObject("Inside");
                slot.transform.SetParent(truck, false);
                slot.transform.position = at;
                slot.transform.rotation = truck.rotation;
                HarborKit.Sit(pallet, at, yaw + HarborKit.Range(rng, -6f, 6f), slot.transform, "Pallet");
                if (freight != null && freight.Count > 0)
                    HarborKit.Sit(HarborKit.Pick(rng, freight), at + Vector3.up * pb.size.y, HarborKit.Range(rng, 0f, 360f), slot.transform, "Freight");
                list.Add(slot);
            }
            return list;
        }

        /// <summary>A driver in the cab - and now and then a mate beside him - sat the
        /// way the traffic's drivers are (CarOccupant, bench sit loop, legs folded
        /// away). The seat is found off the steering wheel: the pack pivots its wheel
        /// at the body's origin, but its renderer knows where it is, and a driver sits
        /// half a metre below it and a little back.</summary>
        public static void Drive(Transform truck, IList<GameObject> people, AnimationClip sit, System.Random rng, bool mate)
        {
            if (truck == null || people == null || people.Count == 0 || sit == null) return;
            var seat = new Vector3(-0.55f, 0.8f, 2.0f);
            var wheel = HarborKit.FindDeep(truck, "SteeringW");
            var wr = wheel != null ? wheel.GetComponentInChildren<Renderer>() : null;
            if (wr != null)
            {
                var c = truck.InverseTransformPoint(wr.bounds.center);
                seat = c + new Vector3(0f, -0.52f, -0.55f);
            }
            RoadDemo.CarOccupant.Seat(truck, people[rng.Next(people.Count)], sit, seat);
            if (mate)
                RoadDemo.CarOccupant.Seat(truck, people[rng.Next(people.Count)], sit, new Vector3(-seat.x, seat.y, seat.z));
        }
    }
}
