using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// ResidentialDemo's thin functional adapter for <see cref="FireStationBlock"/>.
    /// The two authored appliances remain the block's vehicles: this component gives them
    /// the same RoadCar + PatrolDocking journey used by the working forecourt and parking
    /// demos, with one shared apron lease so they never cross through one another.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireStationBlockRuntime : MonoBehaviour
    {
        const float RoadY = 0f;
        const float CityRoadZ = FireStationBlock.BlockDepth * 0.5f + StreetKit.StreetHalf;
        const float HarnessHalfRun = 55f;
        const float HarnessBackRoadZ = CityRoadZ + 60f;
        const float GateX = -10.5f;
        const float GateInsideZ = 16.1f;
        const float GateOutsideZ = 18.6f;
        const float BayMouthZ = 5.2f;
        const float DoorTravelSeconds = 1.35f;
        const float DoorOpenScale = 0.035f;

        static readonly Vector3[] BayStands =
        {
            // Fully behind the front wall: the engines now genuinely live inside the
            // apparatus hall and cross its shutter threshold on every duty cycle.
            new Vector3(-15f, RoadY, -7.2f),
            new Vector3(-6f, RoadY, -7.2f),
        };

        [Header("Functional preview")]
        [Range(1, 2)] public int workingEngines = 2;
        public Vector2 responseSeconds = new Vector2(12f, 20f);
        public Vector2 stationSeconds = new Vector2(9f, 16f);
        public int nameSeed = 1987;
        [Tooltip("Use an invisible logical circuit when no compatible city lane is beside the block.")]
        public bool standaloneRoadHarness = true;

        [Header("Watch")]
        public bool audit = true;
        public float auditEvery = 8f;

        [SerializeField] Transform[] engineBodies = new Transform[0];
        [SerializeField] Transform[] bayDoors = new Transform[0];

        LaneNet _net;
        LaneNet _cityNet;
        RoadEdge _home;
        float _joinProgress;
        Vector3 _join;
        FireEngine _crossing;
        readonly List<FireEngine> _engines = new List<FireEngine>();
        float _auditAt;

        public int ConfiguredEngineCount => engineBodies?.Length ?? 0;
        public int ConfiguredDoorCount => bayDoors?.Length ?? 0;
        public int RunningEngineCount => _engines.Count;

        /// <summary>Called by the editor composer while the ResidentialDemo block is built.</summary>
        public void Configure(
            int seed, IReadOnlyList<GameObject> vehicles, int fireEngineCount,
            IReadOnlyList<GameObject> doors)
        {
            nameSeed = seed;
            int count = Mathf.Clamp(fireEngineCount, 0, vehicles?.Count ?? 0);
            engineBodies = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var vehicle = vehicles[i];
                engineBodies[i] = vehicle != null ? vehicle.transform : null;
                SetDynamic(vehicle);
            }

            int doorCount = Mathf.Min(3, doors?.Count ?? 0);
            bayDoors = new Transform[doorCount];
            for (int i = 0; i < doorCount; i++)
            {
                var door = doors[i];
                bayDoors[i] = door != null ? door.transform : null;
                SetDynamic(door);
            }
        }

        /// <summary>A city host may bind its real network before activating the block.</summary>
        public void BindCityRoad(LaneNet net)
        {
            _cityNet = net;
            standaloneRoadHarness = false;
        }

        void Awake()
        {
            if (!Application.isPlaying || !ValidFrame()) return;

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

            // ResidentialDemo already has PumpDemo's own invisible network. If that
            // circuit is too far away, the station owns a second logical circuit rather
            // than replacing LaneNet.Active and breaking the pump that was there first.
            if (!wired && standaloneRoadHarness)
            {
                _net = BuildRoadNet();
                wired = WireTheRoad();
            }

            if (!wired)
            {
                Debug.LogWarning("[FireStation] The station is visible, but no compatible " +
                                 "road lane is available for its engines.", this);
                return;
            }

            BuildEngines();
            _auditAt = 1f;
            Debug.Log($"[FireStation] functional ResidentialDemo block: {_engines.Count} " +
                      $"engine(s), {ConfiguredDoorCount} working hangar door(s), shared " +
                      "apron, logical street circuit.", this);
        }

        void Update()
        {
            if (!Application.isPlaying || _home == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _engines.Count; i++) _engines[i].TickDuty(dt);
            TickAudit(dt);
        }

        void OnDestroy()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < _engines.Count; i++)
            {
                var engine = _engines[i];
                engine.Despawn();
                StreetTraffic.Users.Remove(engine);
            }
            _engines.Clear();
            _crossing = null;
        }

        bool ValidFrame()
        {
            var scale = transform.lossyScale;
            if ((scale - Vector3.one).sqrMagnitude >= 1e-5f)
            {
                Debug.LogError("[FireStation] The functional block may be translated and " +
                               "rotated, but not scaled.", this);
                return false;
            }
            if (_cityNet != null || LaneNet.Active != null || !standaloneRoadHarness ||
                Quaternion.Angle(transform.rotation, Quaternion.identity) < 0.01f)
                return true;
            Debug.LogError("[FireStation] A rotated block needs a city LaneNet; the " +
                           "ResidentialDemo circuit is axis-aligned.", this);
            return false;
        }

        void BuildEngines()
        {
            int count = Mathf.Min(workingEngines, BayStands.Length, ConfiguredEngineCount);
            var dice = new System.Random(nameSeed * 613 + 211);
            for (int i = 0; i < count; i++)
            {
                var tf = engineBodies[i];
                if (tf == null) continue;

                SetDynamic(tf.gameObject);
                foreach (var body in tf.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
                foreach (var collider in tf.GetComponentsInChildren<Collider>(true)) Destroy(collider);

                var visual = new CarBody(tf);
                var stand = BayStands[i];
                var mouth = new Vector3(stand.x, RoadY, BayMouthZ);
                var doorTf = i < ConfiguredDoorCount ? bayDoors[i] : null;
                if (doorTf != null)
                {
                    SetDynamic(doorTf.gameObject);
                    foreach (var collider in doorTf.GetComponentsInChildren<Collider>(true))
                        Destroy(collider);
                }
                var engine = new FireEngine
                {
                    Tf = tf,
                    Body = visual,
                    HalfLen = visual.HalfLength,
                    HalfWide = visual.HalfWidth,
                    AxleBack = visual.AxleBack,
                    RoadY = transform.position.y + RoadY,
                    Net = _net,
                    Tag = "firestation",
                    Plate = $"fire engine {i + 1}",
                    Profile = DriverProfile.Police,
                    Door = doorTf != null ? new BayDoor(doorTf) : null,
                };
                engine.Bind(
                    this, stand, mouth,
                    firstWait: 2f + i * 11f,
                    responseSeconds, stationSeconds, dice);
                StreetTraffic.Users.Add(engine);
                _engines.Add(engine);
            }
        }

        LaneNet BuildRoadNet()
        {
            var net = new LaneNet();
            const float half = StreetKit.StreetHalf;
            const float limit = 12f;
            var offset = transform.position;
            float nearZ = offset.z + CityRoadZ;
            float farZ = offset.z + HarnessBackRoadZ;
            var lanes = new[] { 2.5f };

            var nearWest = net.AddNode(offset.x - HarnessHalfRun, nearZ, half, half, 1.5f);
            var nearEast = net.AddNode(offset.x + HarnessHalfRun, nearZ, half, half, 1.5f);
            var farWest = net.AddNode(offset.x - HarnessHalfRun, farZ, half, half, 1.5f);
            var farEast = net.AddNode(offset.x + HarnessHalfRun, farZ, half, half, 1.5f);

            net.AddRoad(
                new Vector3(nearWest.XMax, RoadY, nearZ),
                new Vector3(nearEast.XMin, RoadY, nearZ),
                half, lanes, limit, nearWest, nearEast, false);
            net.AddRoad(
                new Vector3(farWest.XMax, RoadY, farZ),
                new Vector3(farEast.XMin, RoadY, farZ),
                half, lanes, limit, farWest, farEast, false);
            net.AddRoad(
                new Vector3(offset.x - HarnessHalfRun, RoadY, nearWest.ZMax),
                new Vector3(offset.x - HarnessHalfRun, RoadY, farWest.ZMin),
                half, lanes, limit, nearWest, farWest, true);
            net.AddRoad(
                new Vector3(offset.x + HarnessHalfRun, RoadY, nearEast.ZMax),
                new Vector3(offset.x + HarnessHalfRun, RoadY, farEast.ZMin),
                half, lanes, limit, nearEast, farEast, true);
            net.Finish();
            return net;
        }

        bool WireTheRoad()
        {
            if (_net == null) return false;
            var along = transform.right;
            along.y = 0f;
            along.Normalize();
            var roadCentre = World(new Vector3(GateX, RoadY, CityRoadZ));
            RoadEdge lane = null;
            float best = 12f;
            foreach (var road in _net.Roads)
                foreach (var candidate in road.Lanes)
                {
                    if (Vector3.Dot(candidate.Dir, along) < 0.9f) continue;
                    float at = Vector3.Dot(roadCentre - candidate.Start, candidate.Dir);
                    if (at < 12f || at > candidate.Length - 12f) continue;
                    var off = roadCentre - (candidate.Start + candidate.Dir * at);
                    off.y = 0f;
                    if (off.magnitude >= best) continue;
                    best = off.magnitude;
                    lane = candidate;
                }
            if (lane == null) return false;

            _home = lane;
            _joinProgress = Mathf.Clamp(
                Vector3.Dot(roadCentre - lane.Start, lane.Dir), 8f, lane.Length - 8f);
            _join = lane.Start + lane.Dir * _joinProgress;
            return true;
        }

        Vector3 World(Vector3 local)
        {
            var world = transform.TransformPoint(local);
            world.y = transform.position.y + RoadY;
            return world;
        }

        Vector3 Direction(Vector3 local)
        {
            var world = transform.TransformDirection(local);
            world.y = 0f;
            return world.sqrMagnitude > 1e-6f ? world.normalized : Vector3.forward;
        }

        Vector3 GateInside => World(new Vector3(GateX, RoadY, GateInsideZ));
        Vector3 GateOutside => World(new Vector3(GateX, RoadY, GateOutsideZ));

        bool TryUseApron(FireEngine engine)
        {
            if (_crossing != null && !ReferenceEquals(_crossing, engine)) return false;
            _crossing = engine;
            return true;
        }

        void ReleaseApron(FireEngine engine)
        {
            if (ReferenceEquals(_crossing, engine)) _crossing = null;
        }

        bool EntryClear(FireEngine self)
        {
            if (_home == null || self.CurrentEdge != _home) return true;
            float mine = self.Progress;
            float lo = Mathf.Min(mine, _joinProgress) - self.HalfLen;
            float hi = Mathf.Max(mine, _joinProgress) + self.HalfLen;
            for (int i = 0; i < _home.Cars.Count; i++)
            {
                var other = _home.Cars[i];
                if (ReferenceEquals(other, self)) continue;
                if (other.Progress > lo && other.Progress < hi) return false;
            }
            return true;
        }

        bool RejoinClear(FireEngine self)
        {
            if (_home == null) return false;
            float sweep = (Vector3.Distance(GateInside, GateOutside) +
                           Vector3.Distance(GateOutside, _join)) /
                          PatrolDocking.Speed + 1.5f;
            return LaneGate.Clear(
                _home, _joinProgress, sweep, self,
                behindMin: self.HalfLen + 18f, aheadMax: self.HalfLen + 7f);
        }

        void TickAudit(float dt)
        {
            if (!audit) return;
            _auditAt -= dt;
            if (_auditAt > 0f) return;
            _auditAt = Mathf.Max(1f, auditEvery);
            var line = new System.Text.StringBuilder("[FireStation]");
            for (int i = 0; i < _engines.Count; i++)
                line.Append($" | {_engines[i].Plate}: {_engines[i].State}" +
                            $" / door {_engines[i].DoorState}");
            Debug.Log(line.ToString(), this);
        }

        static void SetDynamic(GameObject go)
        {
            if (go == null) return;
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.isStatic = false;
        }

        sealed class BayDoor
        {
            readonly Transform _tf;
            readonly Vector3 _closedScale;
            float _open;
            float _target;

            public bool IsOpen => _tf == null || _open >= 0.999f;
            public bool IsClosed => _tf == null || _open <= 0.001f;
            public string State => IsOpen ? "open" : IsClosed ? "closed" : "moving";

            public BayDoor(Transform tf)
            {
                _tf = tf;
                _closedScale = tf != null ? tf.localScale : Vector3.one;
                SnapClosed();
            }

            public void Open() => _target = 1f;
            public void Close() => _target = 0f;

            public void Tick(float dt)
            {
                if (_tf == null || dt <= 0f) return;
                _open = Mathf.MoveTowards(
                    _open, _target, dt / Mathf.Max(0.05f, DoorTravelSeconds));
                Apply();
            }

            public void SnapClosed()
            {
                _open = 0f;
                _target = 0f;
                Apply();
            }

            void Apply()
            {
                if (_tf == null) return;
                float eased = Mathf.SmoothStep(0f, 1f, _open);
                var scale = _closedScale;
                scale.y = _closedScale.y * Mathf.Lerp(1f, DoorOpenScale, eased);
                _tf.localScale = scale;
            }
        }

        /// <summary>One appliance cycling between its inside hangar stand and the road.</summary>
        sealed class FireEngine : DemoVehicle
        {
            public enum Mode
            {
                Parked,
                OpeningToExit,
                Exiting,
                Responding,
                Returning,
                OpeningToEnter,
                Entering,
                ClosingHangar,
            }

            readonly struct Motion
            {
                public readonly PatrolDocking.Curve Curve;
                public readonly bool FollowTangent;
                public readonly Quaternion EndRotation;

                public Motion(
                    PatrolDocking.Curve curve, bool followTangent, Quaternion endRotation)
                {
                    Curve = curve;
                    FollowTangent = followTangent;
                    EndRotation = endRotation;
                }
            }

            protected override bool VanishesWhenStuck => false;

            FireStationBlockRuntime _station;
            Vector3 _stand;
            Vector3 _mouth;
            readonly List<Motion> _motions = new List<Motion>();
            System.Random _dice;
            Vector2 _responseSeconds;
            Vector2 _stationSeconds;
            float _timer;
            float _t;
            int _motion;
            int _laneMotion = -1;
            Quaternion _motionStart;

            public CarBody Body;
            public BayDoor Door;
            public string Plate;
            public Mode State { get; private set; }
            public string DoorState => Door?.State ?? "none";

            public void Bind(
                FireStationBlockRuntime station, Vector3 stand, Vector3 mouth,
                float firstWait, Vector2 responseSeconds, Vector2 stationSeconds,
                System.Random dice)
            {
                _station = station;
                _stand = stand;
                _mouth = mouth;
                _dice = dice;
                _responseSeconds = responseSeconds;
                _stationSeconds = stationSeconds;
                State = Mode.Parked;
                _timer = firstWait;
                EngineOff = true;
                Door?.SnapClosed();

                var position = _station.World(_stand);
                var forward = _station.Direction(Vector3.forward);
                Tf.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
                PlaceAt(position, forward);
                Despawn();
                Slid(position);
            }

            public void TickDuty(float dt)
            {
                if (Tf == null || Gone || dt <= 0f) return;
                Door?.Tick(dt);
                switch (State)
                {
                    case Mode.Parked:
                        _timer -= dt;
                        if (_timer <= 0f && _station.RejoinClear(this) &&
                            _station.TryUseApron(this))
                            BeginOpenForExit();
                        break;

                    case Mode.OpeningToExit:
                        if (Door == null || Door.IsOpen) BeginExit();
                        break;

                    case Mode.Exiting:
                    case Mode.Entering:
                        TickMotion(dt);
                        break;

                    case Mode.Responding:
                        Tick(dt);
                        _timer -= dt;
                        if (_timer <= 0f) BeginReturn();
                        break;

                    case Mode.Returning:
                        Tick(dt);
                        TickReturn();
                        break;

                    case Mode.OpeningToEnter:
                        Halt(false);
                        if (Door == null || Door.IsOpen) BeginEnter();
                        break;

                    case Mode.ClosingHangar:
                        if (Door == null || Door.IsClosed) FinishParking();
                        break;
                }
            }

            void BeginOpenForExit()
            {
                State = Mode.OpeningToExit;
                EngineOff = false;
                Door?.Open();
                Debug.Log($"[FireStation] {Plate}: opening hangar door to exit.", _station);
            }

            void BeginExit()
            {
                State = Mode.Exiting;
                EngineOff = false;
                _motions.Clear();
                Debug.Log($"[FireStation] {Plate}: exiting from inside the hangar.", _station);

                var stand = _station.World(_stand);
                var forward = _station.Direction(Vector3.forward);
                var mouth = _station.World(_mouth);
                var junction = _station.World(new Vector3(GateX, RoadY, 15.1f));
                var gateInside = _station.GateInside;
                var gateOutside = _station.GateOutside;
                var aisle = Flat(junction - mouth).normalized;
                var outward = _station.Direction(Vector3.forward);

                Add(PatrolDocking.Undock(stand, forward, mouth, aisle), true, Quaternion.identity);
                AddSweep(mouth, aisle, junction, outward);
                AddSweep(junction, outward, gateInside, outward);
                _laneMotion = _motions.Count;
                AddSweep(gateInside, outward, gateOutside, outward);
                AddSweep(gateOutside, outward, _station._join, _station._home.Dir);
                _laneMotion = Mathf.Min(_laneMotion, _motions.Count - 1);
                StartMotions();
            }

            void BeginReturn()
            {
                if (!GoTo(_station._join, park: true, standOff: 0f, stopAtGoal: true,
                          wantHeading: _station._home.Heading))
                {
                    _timer = 2f;
                    return;
                }
                State = Mode.Returning;
            }

            void TickReturn()
            {
                float distance = Vector3.Distance(Tf.position, _station._join);
                bool atEntrance = distance < HalfLen + 12f ||
                    (Parked && CurrentEdge == _station._home && distance < 30f);
                if (atEntrance && Mathf.Abs(Speed) < 1.2f)
                {
                    if (!_station.EntryClear(this) || !_station.TryUseApron(this))
                    {
                        Halt(false);
                        return;
                    }
                    BeginOpenForEntry();
                    return;
                }

                if (!HasGoal && !Halted)
                    GoTo(_station._join, park: true, standOff: 0f, stopAtGoal: true,
                         wantHeading: _station._home.Heading);
            }

            void BeginOpenForEntry()
            {
                Halt(false);
                State = Mode.OpeningToEnter;
                EngineOff = false;
                Door?.Open();
                Debug.Log($"[FireStation] {Plate}: opening hangar door to enter.", _station);
            }

            void BeginEnter()
            {
                Stop();
                Despawn();
                State = Mode.Entering;
                EngineOff = false;
                _motions.Clear();
                Debug.Log($"[FireStation] {Plate}: reversing through the hangar threshold.", _station);

                var gateOutside = _station.GateOutside;
                var gateInside = _station.GateInside;
                var junction = _station.World(new Vector3(GateX, RoadY, 15.1f));
                var mouth = _station.World(_mouth);
                var stand = _station.World(_stand);
                var inward = _station.Direction(Vector3.back);
                var aisle = Flat(mouth - junction).normalized;
                var forward = _station.Direction(Vector3.forward);

                AddSweep(Tf.position, Tf.forward, gateOutside, inward);
                AddSweep(gateOutside, inward, gateInside, inward);
                AddSweep(gateInside, inward, junction, aisle);
                AddSweep(junction, aisle, mouth, aisle);
                Add(PatrolDocking.Dock(mouth, stand, forward), false,
                    Quaternion.LookRotation(forward));
                StartMotions();
            }

            void AddSweep(Vector3 from, Vector3 fromWay, Vector3 to, Vector3 toWay)
            {
                if (Flat(to - from).sqrMagnitude < 0.25f) return;
                Add(PatrolDocking.Sweep(from, fromWay, to, toWay), true,
                    Quaternion.identity);
            }

            void Add(
                PatrolDocking.Curve curve, bool followTangent, Quaternion endRotation)
                => _motions.Add(new Motion(curve, followTangent, endRotation));

            void StartMotions()
            {
                _motion = 0;
                _t = 0f;
                _motionStart = Tf.rotation;
                Speed = 0f;
            }

            void TickMotion(float dt)
            {
                if (_motion >= _motions.Count)
                {
                    FinishMotion();
                    return;
                }

                bool atGate = State == Mode.Exiting && _motion == _laneMotion;
                if (atGate && _t < 0.2f && !_station.RejoinClear(this))
                {
                    Speed = 0f;
                    return;
                }

                var motion = _motions[_motion];
                Speed = PatrolDocking.Speed;
                _t = PatrolDocking.Advance(
                    motion.Curve, _t, PatrolDocking.Speed * dt);
                var position = PatrolDocking.Point(motion.Curve, _t);
                position.y = RoadY;
                var rotation = motion.FollowTangent
                    ? PatrolDocking.Heading(motion.Curve, _t, _motionStart)
                    : Quaternion.Slerp(
                        _motionStart, motion.EndRotation, Mathf.SmoothStep(0f, 1f, _t));
                Tf.SetPositionAndRotation(position, rotation);
                Slid(position);
                Body?.TickWheels(
                    dt, motion.FollowTangent ? PatrolDocking.Speed : -PatrolDocking.Speed, 0f);
                if (_t < 1f) return;

                _motion++;
                _t = 0f;
                _motionStart = Tf.rotation;
                if (_motion >= _motions.Count) FinishMotion();
            }

            void FinishMotion()
            {
                Speed = 0f;
                if (State == Mode.Exiting)
                {
                    Door?.Close();
                    _station.ReleaseApron(this);
                    Spawn(_station._home, _station._joinProgress);
                    State = Mode.Responding;
                    _timer = Range(_responseSeconds);
                    Debug.Log($"[FireStation] {Plate}: clear of the hangar; door closing.", _station);
                    return;
                }

                var position = _station.World(_stand);
                var forward = _station.Direction(Vector3.forward);
                Tf.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
                Slid(position);
                EngineOff = true;
                State = Mode.ClosingHangar;
                Door?.Close();
                Debug.Log($"[FireStation] {Plate}: fully inside; hangar door closing.", _station);
                if (Door == null) FinishParking();
            }

            void FinishParking()
            {
                _station.ReleaseApron(this);
                State = Mode.Parked;
                EngineOff = true;
                _timer = Range(_stationSeconds);
                Debug.Log($"[FireStation] {Plate}: parked inside; hangar door closed.", _station);
            }

            float Range(Vector2 range)
            {
                float lo = Mathf.Min(range.x, range.y);
                float hi = Mathf.Max(range.x, range.y);
                return lo + (float)_dice.NextDouble() * (hi - lo);
            }

            protected override void OnPlaced(float dt, float speed, float steerDegrees)
                => Body?.TickWheels(dt, speed, steerDegrees);

            static Vector3 Flat(Vector3 value)
            {
                value.y = 0f;
                return value;
            }
        }
    }
}
