using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Scene-lifetime civilian cars at legal kerbs. Owns their views and
    /// obstacle registrations; these ambient props have no campaign ownership.</summary>
    public sealed class ParkedTraffic : System.IDisposable
    {
        readonly List<StoodCar> _cars = new List<StoodCar>();
        readonly SidewalkPlan _walk = new SidewalkPlan();
        readonly Transform _root;
        public int Count => _cars.Count;
        public int Candidates { get; private set; }
        public int RejectedPose { get; private set; }
        public int RejectedFrontage { get; private set; }
        public int RejectedObstacles { get; private set; }
        public int RejectedPrefab { get; private set; }

        struct Candidate
        {
            public Carriageway Road;
            public float S;
            public int Side;
        }

        public ParkedTraffic(Transform parent, LaneNet net, System.Func<System.Random, GameObject> pick,
            int count, int seed, System.Func<Carriageway, float, int, float, bool> allows = null)
        {
            if (parent == null || net == null || pick == null || count <= 0) return;
            _root = new GameObject("Parked NPC Cars").transform;
            _root.SetParent(parent, false);
            WalkObstacles.RegisterPlan(_walk);
            var dice = new System.Random(seed);
            var candidates = new List<Candidate>();
            foreach (var road in net.Roads)
                for (float s = 12f; s < road.Length - 10f; s += 14f)
                    for (int side = -1; side <= 1; side += 2)
                        candidates.Add(new Candidate { Road = road, S = s, Side = side });
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = dice.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // Keep the existing city's random stream intact, including paint draws.
            var randomState = Random.state;
            Random.InitState(seed);
            try
            {
                var measured = new Dictionary<GameObject, Vector2>();
                foreach (var candidate in candidates)
                {
                    if (Count >= count) break;
                    Candidates++;
                    var prefab = pick(dice);
                    if (prefab == null) { RejectedPrefab++; continue; }
                    if (!measured.TryGetValue(prefab, out var size))
                    {
                        CarBody.MeasureFootprint(prefab.transform, out float length, out float width);
                        size = new Vector2(length, width);
                        measured.Add(prefab, size);
                    }
                    float s = candidate.S + (float)dice.NextDouble() * 4f - 2f;
                    if (!ParkingLaneSlots.TryPose(candidate.Road, s, candidate.Side, size.x, size.y,
                            out var pos, out var forward)) { RejectedPose++; continue; }
                    if (allows != null && !allows(candidate.Road, s, candidate.Side, size.x))
                        { RejectedFrontage++; continue; }
                    float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                    var box = SidewalkPlan.Make(new Vector2(pos.x, pos.z), yaw,
                        new Vector2(size.y, size.x), true);
                    bool free = true;
                    foreach (var plan in WalkObstacles.Props)
                        if (!plan.Free(box, 0.2f)) { free = false; break; }
                    // The capsule encloses the whole body. This shared query reads
                    // walls, baked furniture and mutable plans, including _walk.
                    if (!free || WalkObstacles.BlocksStanding(pos - forward * size.x,
                            pos + forward * size.x, size.y + 0.05f) || !ClearOfCars(pos, forward, size))
                        { RejectedObstacles++; continue; }

                    var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(forward, Vector3.up), _root);
                    go.name = prefab.name;
                    LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                    foreach (var body in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(body);
                    foreach (var collider in go.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
                    var stood = StoodCar.Park(go, net);
                    if (stood == null) { RejectedPrefab++; Object.Destroy(go); continue; }
                    var bounds = new Bounds(pos, Vector3.zero);
                    foreach (var renderer in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                    box.Rise = bounds.size.y;
                    box.SourceName = go.name;
                    _walk.Take(box);
                    _cars.Add(stood);
                    ScenePerf.SetLayerDeep(go, ScenePerf.PropLayer);
                }
            }
            finally { Random.state = randomState; }
            string report = $"[ParkedTraffic] {Count}/{count} NPC cars (seed {seed}); " +
                $"{Candidates}/{candidates.Count} candidates checked, rejected: pose {RejectedPose}, " +
                $"frontage {RejectedFrontage}, obstacles {RejectedObstacles}, prefab {RejectedPrefab}.";
            if (Count < count) Debug.LogWarning(report);
            else Debug.Log(report);
        }

        static bool ClearOfCars(Vector3 position, Vector3 forward, Vector2 size)
        {
            foreach (var user in StreetTraffic.Users)
                if (user != null && RoadSpace.Overlap(position, forward, size.x, size.y,
                        user.RoadPosition, user.RoadForward, user.HalfLength, user.HalfWidth,
                        ParkingLaneSlots.Gap, out _)) return false;
            return true;
        }

        public void Dispose()
        {
            foreach (var car in _cars) car.Forget();
            _cars.Clear();
            WalkObstacles.UnregisterPlan(_walk);
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
