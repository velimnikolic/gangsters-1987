using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Shared functional adapter for <see cref="FuelStationBlock"/>. A city district may bind
    /// its graph explicitly before activation; otherwise it finds the active compatible lane.
    /// When ResidentialDemo is watched alone it may supply an invisible compact lane circuit;
    /// the visual block still contains only the station parcel and generated pavement.
    /// The actual errand remains the shared <see cref="FuelCustomer"/> used by PumpDemo
    /// and the city's wayside stations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FuelStationBlockRuntime : MonoBehaviour
    {
        [Header("Functional preview")]
        [Range(1, 2)] public int fuelCustomers = 2;
        [Range(0, 6)] public int stationWalkers = 2;
        [Range(0f, 1f)] public float enterChance = 0.65f;
        public Vector2 insideSeconds = new Vector2(7f, 18f);
        public int nameSeed = 1987;
        [Tooltip("Use an invisible logical circuit only when no city LaneNet is active. " +
                 "It keeps ResidentialDemo functional without adding PumpDemo's road visuals.")]
        public bool standaloneRoadHarness = true;

        [Header("Watch")]
        public bool audit = true;
        public float auditEvery = 10f;

        LaneNet _net;
        LaneNet _cityNet;
        FuelStation _station;
        Transform _liveRoot;
        CityLife _life;
        bool _ownsNet;
        readonly List<FuelCustomer> _customers = new List<FuelCustomer>();
        readonly List<CivilianAgent> _walkers = new List<CivilianAgent>();
        readonly List<PedLink> _forecourtLinks = new List<PedLink>();
        List<GameObject> _cars;
        List<GameObject> _people;
        float _chatScan;
        float _auditAt;

        /// <summary>Bind a generated block to the district graph before its GameObject is
        /// activated. CoreDemo composes the block inactive, moves and turns it onto its
        /// reserved parcel, then wakes it against the graph it just built.</summary>
        public void BindCityRoad(LaneNet net)
        {
            _cityNet = net;
            standaloneRoadHarness = false;
        }

        void Awake()
        {
            if (!Application.isPlaying) return;
#if UNITY_EDITOR
            if (!ValidFrame()) return;

            _liveRoot = new GameObject("Live Fuel Activity").transform;
            _liveRoot.SetParent(transform, false);

            var anchor = transform.position;
            float groundY = transform.position.y + FuelStationBlock.ApronY;
            anchor.y = groundY;
            var rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            _station = FuelStation.Wire(
                anchor, rotation, groundY, FuelStationBlock.CrossZ);
            _station.BlockWalkers();
            BlockDressing();

            bool wired = false;
            if (_cityNet != null)
            {
                _net = _cityNet;
                wired = WireTheRoad();
            }
            else if (LaneNet.Active != null)
            {
                _net = LaneNet.Active;
                wired = WireTheRoad();
            }
            else if (standaloneRoadHarness)
            {
                _net = BuildRoadNet();
                _ownsNet = true;
                LaneNet.Active = _net;
                wired = WireTheRoad();
            }
            if (!wired)
                Debug.LogWarning("[FuelBlock] The station stands with open entry/exit " +
                                 "connectors, but no compatible city lane is beside them.");

            WalkObstacles.City.Add(WorldFootprint());

            var clips = CrewKit.Clips();
            BuildStationWalkers(clips);
            BuildCustomers(clips);
            _auditAt = 1f;
            Debug.Log($"[FuelBlock] functional full PumpDemo block: " +
                      $"{_customers.Count} fuel customer(s), {_walkers.Count} shop walker(s), " +
                      $"{_station.Bays.Length} pump bay(s), " +
                      $"{(_ownsNet ? "standalone logical road" : "city road") }.");
#else
            Debug.LogError("[FuelBlock] The preview loads Synty bodies in the editor only.");
#endif
        }

        void Update()
        {
            if (!Application.isPlaying || _station == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _customers.Count; i++) _customers[i].TickErrand(dt);
            TestBench.TickPavementLife(_walkers, null, dt, ref _chatScan);
            TickAudit(dt);
        }

        void OnDestroy()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Dispose();
            for (int i = 0; i < _customers.Count; i++)
            {
                var car = _customers[i];
                car.Despawn();
                StreetTraffic.Users.Remove(car);
                car.Driver?.Dispose();
            }
            if (_ownsNet && LaneNet.Active == _net) LaneNet.Active = null;
        }

        bool ValidFrame()
        {
            var scale = transform.lossyScale;
            if ((scale - Vector3.one).sqrMagnitude >= 1e-5f)
            {
                Debug.LogError("[FuelBlock] The full fuel block may be translated and " +
                               "rotated, but not scaled.");
                return false;
            }
            if (_cityNet != null || LaneNet.Active != null || !standaloneRoadHarness ||
                Quaternion.Angle(transform.rotation, Quaternion.identity) < 0.01f)
                return true;
            Debug.LogError("[FuelBlock] A rotated fuel block needs a city LaneNet; " +
                           "the standalone preview circuit is axis-aligned.");
            return false;
        }

        Rect WorldFootprint()
        {
            var area = FuelStationBlock.PreviewBounds;
            var corners = new[]
            {
                transform.TransformPoint(new Vector3(area.xMin, 0f, area.yMin)),
                transform.TransformPoint(new Vector3(area.xMin, 0f, area.yMax)),
                transform.TransformPoint(new Vector3(area.xMax, 0f, area.yMin)),
                transform.TransformPoint(new Vector3(area.xMax, 0f, area.yMax)),
            };
            float x0 = corners[0].x, x1 = corners[0].x;
            float z0 = corners[0].z, z1 = corners[0].z;
            for (int i = 1; i < corners.Length; i++)
            {
                x0 = Mathf.Min(x0, corners[i].x);
                x1 = Mathf.Max(x1, corners[i].x);
                z0 = Mathf.Min(z0, corners[i].z);
                z1 = Mathf.Max(z1, corners[i].z);
            }
            return Rect.MinMaxRect(x0, z0, x1, z1);
        }

        LaneNet BuildRoadNet()
        {
            var net = new LaneNet();
            const float half = StreetKit.StreetHalf;
            const float limit = 12f;
            float run = FuelStationBlock.HarnessHalfRun;
            var offset = transform.position;
            float mainZ = offset.z + FuelStationBlock.CityRoadZ;
            float backZ = offset.z + FuelStationBlock.HarnessBackRoadZ;
            float roadY = offset.y + FuelStationBlock.RoadY;
            var lanes = new[] { 2.5f };

            var nw = net.AddNode(offset.x - run, mainZ, half, half, stopSetback: 1.5f);
            var ne = net.AddNode(offset.x + run, mainZ, half, half, stopSetback: 1.5f);
            var sw = net.AddNode(offset.x - run, backZ, half, half, stopSetback: 1.5f);
            var se = net.AddNode(offset.x + run, backZ, half, half, stopSetback: 1.5f);

            net.AddRoad(
                new Vector3(nw.XMax, roadY, mainZ), new Vector3(ne.XMin, roadY, mainZ),
                half, lanes, limit, nw, ne, false);
            net.AddRoad(
                new Vector3(sw.XMax, roadY, backZ), new Vector3(se.XMin, roadY, backZ),
                half, lanes, limit, sw, se, false);
            net.AddRoad(
                new Vector3(offset.x - run, roadY, sw.ZMax),
                new Vector3(offset.x - run, roadY, nw.ZMin),
                half, lanes, limit, sw, nw, true);
            net.AddRoad(
                new Vector3(offset.x + run, roadY, se.ZMax),
                new Vector3(offset.x + run, roadY, ne.ZMin),
                half, lanes, limit, se, ne, true);
            net.Finish();
            return net;
        }

        bool WireTheRoad()
        {
            if (_station == null || _net == null) return false;
            var along = _station.Way(1f, 0f);
            var roadCentre = transform.TransformPoint(
                new Vector3(0f, 0f, FuelStationBlock.CityRoadZ));
            RoadEdge lane = null;
            float best = 12f;
            foreach (var road in _net.Roads)
                foreach (var candidate in road.Lanes)
                {
                    if (Vector3.Dot(candidate.Dir, along) < 0.9f) continue;
                    float at = Vector3.Dot(roadCentre - candidate.Start, candidate.Dir);
                    if (at < FuelStationBlock.KerbRun + 6f ||
                        at > candidate.Length - FuelStationBlock.KerbRun - 6f) continue;
                    var off = roadCentre - (candidate.Start + candidate.Dir * at);
                    off.y = 0f;
                    if (off.magnitude >= best) continue;
                    best = off.magnitude;
                    lane = candidate;
                }
            if (lane == null)
            {
                return false;
            }

            _station.Lane = lane;
            var inPoint = _station.Anchor + _station.Way(-FuelStationBlock.KerbRun, 0f);
            var outPoint = _station.Anchor + _station.Way(FuelStationBlock.KerbRun, 0f);
            _station.KerbInS = Along(lane, inPoint);
            _station.KerbOutS = Along(lane, outPoint);
            _station.KerbIn = lane.Start + lane.Dir * _station.KerbInS;
            _station.KerbOut = lane.Start + lane.Dir * _station.KerbOutS;
            _station.MapRoads(_net);
            return true;
        }

        static float Along(RoadEdge lane, Vector3 point) =>
            Mathf.Clamp(Vector3.Dot(point - lane.Start, lane.Dir), 6f, lane.Length - 6f);

        /// <summary>The authored solid dressing nearest the shop-front walk.  The shop,
        /// island and parking row are already registered by FuelStation.BlockWalkers.</summary>
        void BlockDressing()
        {
            float yaw = _station.Rot.eulerAngles.y;
            var cageCentre = _station.At(
                (FuelStation.CageXMin + FuelStation.CageXMax) * 0.5f,
                (FuelStation.CageZFront + FuelStation.CageZBack) * 0.5f);
            WalkObstacles.Block(cageCentre, yaw,
                new Vector2((FuelStation.CageXMax - FuelStation.CageXMin) * 0.5f,
                            (FuelStation.CageZFront - FuelStation.CageZBack) * 0.5f));
            WalkObstacles.Block(_station.At(0f, FuelStation.GreenZ), yaw,
                new Vector2(FuelStation.GreenBushX + 1f, 0.8f));
        }

        // ---------------------------------------------------------------- customers

        void BuildCustomers(PedClips clips)
        {
#if UNITY_EDITOR
            if (_station?.Lane == null || fuelCustomers <= 0) return;
            var cars = CarBodies();
            var people = PassersBy();
            if (cars.Count == 0 || people.Count == 0) return;

            var root = new GameObject("Fuel Customers").transform;
            root.SetParent(_liveRoot, false);
            var rng = new System.Random(nameSeed * 613 + 131);
            var crowd = CrewKit.ForCrowd(clips, rng);

            for (int i = 0; i < fuelCustomers; i++)
            {
                var prefab = cars[rng.Next(cars.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                Strip(go);

                var body = new CarBody(go.transform);
                var car = new FuelCustomer
                {
                    Tf = go.transform,
                    Body = body,
                    HalfLen = body.TrafficHalfLength,
                    HalfWide = body.TrafficHalfWidth,
                    AxleBack = body.AxleBack,
                    RoadY = transform.position.y + FuelStationBlock.RoadY,
                    Net = _net,
                    Tag = "traffic",
                    Plate = $"residential pump {i + 1}",
                };

                var face = people[rng.Next(people.Count)];
                car.Seated = CarOccupant.Seat(
                    go.transform, face, clips.SitLoop, body.SeatLocalPoint(0));
                car.Driver = MakeDriver(face, crowd, root);

                float progress = i == 0
                    ? _station.KerbInS - 14f
                    : _station.KerbInS + 35f;
                progress = Mathf.Clamp(progress, 3f, _station.Lane.Length - 8f);
                car.Spawn(_station.Lane, progress);
                car.SetStation(_station, 0.8f + i * 1.7f);
                StreetTraffic.Users.Add(car);
                _customers.Add(car);
            }
#endif
        }

        FuelDriver MakeDriver(GameObject prefab, PedClips clips, Transform root)
        {
            var go = Instantiate(prefab, root);
            go.name = prefab.name + " (fuel driver)";
            Strip(go);
            foreach (var animator in go.GetComponentsInChildren<Animator>())
                animator.runtimeAnimatorController = null;
            var driver = new FuelDriver
            {
                Speed = 1.25f + Random.value * 0.3f,
                Tag = "driver",
            };
            driver.InitAt(go.transform, clips, _station.ShopStep, Quaternion.identity);
            driver.Show(false);
            return driver;
        }

        // ------------------------------------------------------------ shop walkers

        void BuildStationWalkers(PedClips clips)
        {
#if UNITY_EDITOR
            if (_station == null || stationWalkers <= 0 || clips.Walk == null || clips.Idle == null)
                return;

            float shopFront = FuelStation.ShopZ + FuelStation.ShopDoorOut + 1.6f;
            var east = new PedNode { Pos = _station.At(13f, shopFront) };
            var west = new PedNode { Pos = _station.At(-13f, shopFront) };
            TestBench.Join(east, west, false, _forecourtLinks);
            for (int i = 0; i < _forecourtLinks.Count; i++)
                WalkObstacles.SampleWalk(_forecourtLinks[i], SidewalkDressing.WalkRadius);

            _life = new CityLife
            {
                SitChance = 0f,
                EnterChance = enterChance,
                InsideSeconds = insideSeconds,
                CanSit = false,
                CanChat = true,
            };
            WireDoor(_station.ShopDoor, _station.Way(0f, 1f));
            _life.SortStops();

            var people = PassersBy();
            if (people.Count == 0) return;
            var root = new GameObject("Shop Walkers").transform;
            root.SetParent(_liveRoot, false);
            var rng = new System.Random(nameSeed * 613 + 173);
            var links = _forecourtLinks.FindAll(link => !link.Gated);
            if (links.Count == 0) return;

            for (int i = 0; i < stationWalkers; i++)
            {
                var prefab = people[rng.Next(people.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                Strip(go);
                foreach (var animator in go.GetComponentsInChildren<Animator>())
                    animator.runtimeAnimatorController = null;
                var link = links[rng.Next(links.Count)];
                var agent = new CivilianAgent
                {
                    Speed = 1.2f + (float)rng.NextDouble() * 0.6f,
                };
                if (!agent.Init(go.transform, CrewKit.ForCrowd(clips, rng), link,
                    (float)rng.NextDouble() * link.Length * 0.9f))
                { Destroy(go); continue; }
                agent.Setup(_life);
                _walkers.Add(agent);
            }
#endif
        }

        void WireDoor(Vector3 pos, Vector3 outward)
        {
            PedLink forward = null;
            float t = 0f, best = 14f * 14f;
            for (int i = 0; i < _forecourtLinks.Count; i++)
            {
                var link = _forecourtLinks[i];
                if (link.Gated || link.Length < 6f) continue;
                var dir = (link.To.Pos - link.From.Pos) / link.Length;
                float along = Mathf.Clamp(Vector3.Dot(pos - link.From.Pos, dir), 2f, link.Length - 2f);
                var near = link.From.Pos + dir * along;
                var delta = near - pos;
                delta.y = 0f;
                if (delta.sqrMagnitude >= best) continue;
                best = delta.sqrMagnitude;
                forward = link;
                t = along;
            }
            if (forward == null)
            {
                Debug.LogWarning("[FuelBlock] The shop door reaches no forecourt walk.");
                return;
            }

            PedLink back = null;
            for (int i = 0; i < forward.To.Links.Count; i++)
                if (forward.To.Links[i].To == forward.From) back = forward.To.Links[i];
            if (back == null) return;

            var door = new DemoDoor
            {
                Pos = pos,
                Outward = outward,
                LinkFwd = forward,
                LinkBack = back,
                EntryT = t,
                EntryPos = Vector3.Lerp(forward.From.Pos, forward.To.Pos, t / forward.Length),
            };
            _life.Doors.Add(door);
            _life.AddStop(forward, t, door, null);
            _life.AddStop(back, forward.Length - t, door, null);
        }

        // ------------------------------------------------------------------- assets

        static void Strip(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>())
                if (!CarBody.IsVisualRig(behaviour)) Destroy(behaviour);
        }

        List<GameObject> CarBodies() => _cars ??= TestBench.WeightedCars();

        static readonly string[] VehicleFolders =
        {
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/",
        };

        static GameObject FindVehicle(string name)
        {
#if UNITY_EDITOR
            for (int i = 0; i < VehicleFolders.Length; i++)
            {
                string path = VehicleFolders[i] + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) return prefab;
            }
#endif
            return null;
        }

        List<GameObject> PassersBy()
        {
            if (_people != null) return _people;
            _people = new List<GameObject>();
#if UNITY_EDITOR
            foreach (var folder in new[]
            {
                "Assets/Synty/PolygonCity/Prefabs/Characters",
                "Assets/Synty/PolygonPalmCity/Prefabs/Characters",
            })
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    string file = System.IO.Path.GetFileNameWithoutExtension(path);
                    string lower = file.ToLowerInvariant();
                    if (lower.Contains("police") || lower.Contains("attach")) continue;
                    if (LivingCity.Gangs.GangLooks.IsGangBody(file)) continue;
                    if (LivingCity.Entities.CrowdLooks.IsBarred(file)) continue;
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var animator = go != null ? go.GetComponentInChildren<Animator>() : null;
                    if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                    _people.Add(go);
                }
#endif
            return _people;
        }

        // -------------------------------------------------------------------- watch

        void TickAudit(float dt)
        {
            if (!audit) return;
            _auditAt -= dt;
            if (_auditAt > 0f) return;
            _auditAt = Mathf.Max(1f, auditEvery);

            int busy = 0;
            for (int i = 0; i < _station.Bays.Length; i++)
                if (_station.Taken(i)) busy++;
            int inside = 0;
            for (int i = 0; i < _walkers.Count; i++)
                if (_walkers[i].State == CivilianAgent.Mode.Inside) inside++;

            var line = new System.Text.StringBuilder();
            line.Append($"[FuelBlock] {busy}/{_station.Bays.Length} bays taken");
            for (int i = 0; i < _customers.Count; i++)
                line.Append($" | {_customers[i].Plate}: {_customers[i].Doing}");
            line.Append($" | {inside}/{_walkers.Count} ambient walker(s) inside shop");
            Debug.Log(line.ToString());
        }
    }
}
