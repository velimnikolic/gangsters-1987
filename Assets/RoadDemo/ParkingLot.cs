using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Runtime half of a generated parking block. Each car owns one stall, while the lot owns
    /// the shared drive from the street: one car may cross it at a time. Cars are ordinary
    /// DemoVehicles while on the lane graph and are hand-driven only between the kerb and bay.
    /// </summary>
    public sealed class ParkingLot
    {
        readonly ParkingBlockSite _site;
        readonly RoadEdge _home;
        readonly float _joinProgress;
        readonly Vector3 _join;
        readonly List<ParkingCar> _cars = new List<ParkingCar>();
        readonly List<ParkingCar> _requests = new List<ParkingCar>();
        ParkingCar _moving;
        static readonly Dictionary<GameObject, bool> FitsStall = new Dictionary<GameObject, bool>();

        public int CarCount => _cars.Count;
        public RoadEdge HomeLane => _home;
        public IReadOnlyList<ParkingCar> Cars => _cars;

        public ParkingLot(
            ParkingBlockSite site, LaneNet net, int carCount, int seed, Transform liveRoot)
        {
            _site = site;
            if (site == null || net == null || liveRoot == null || site.Plan.Stalls.Count == 0)
                return;

            var gate = site.Root.TransformPoint(site.Plan.Gate);
            // ParkingDemo's road is close to every gate. CoreDemo's urban option adds its
            // ten-metre pavement inside the parcel and a boulevard can put the nearest lane
            // another seventeen metres outside it, so the shared lot needs the full city
            // frontage reach here as well.
            _home = net.NearestLane(gate, out float joinProgress, 35f);
            if (_home == null)
            {
                Debug.LogWarning("[Parking] No usable road lane beside the generated block.");
                return;
            }

            _joinProgress = Mathf.Clamp(joinProgress, 8f, _home.Length - 8f);
            _join = _home.Start + _home.Dir * _joinProgress;

            var dice = new System.Random(seed);
            int wanted = Mathf.Clamp(carCount, 0, site.Plan.Stalls.Count);
            if (wanted == 0) return;

            // Spread the live cars across the plan instead of filling the first painted row.
            float stride = (float)site.Plan.Stalls.Count / wanted;
            for (int i = 0; i < wanted; i++)
            {
                int stallIndex = Mathf.Min(site.Plan.Stalls.Count - 1,
                                           Mathf.FloorToInt((i + 0.5f) * stride));
                var prefab = CoreRoads.PickCar(dice, FitsParkingStall);
                if (prefab == null) break;

                var go = Object.Instantiate(prefab, liveRoot);
                go.name = $"Parking Car {i + 1}";
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var body in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(body);
                foreach (var collider in go.GetComponentsInChildren<Collider>()) Object.Destroy(collider);

                CarBody.MeasureTrafficFootprint(go.transform, out float halfLength, out float halfWidth);

                var car = new ParkingCar
                {
                    Tf = go.transform,
                    Net = net,
                    HalfLen = halfLength,
                    HalfWide = halfWidth,
                };
                car.Bind(this, site.Plan.Stalls[stallIndex], i,
                         firstWait: 2f + i * 3.5f,
                         driveSeconds: new Vector2(16f, 30f),
                         parkedSeconds: new Vector2(10f, 22f), dice);
                _cars.Add(car);
                StreetTraffic.Users.Add(car);
            }
        }

        internal Vector3 World(Vector3 local) => _site.Root.TransformPoint(local);
        internal Vector3 Direction(Vector3 local) => _site.Root.TransformDirection(local).normalized;
        internal Vector3 Join => _join;
        internal Vector3 GateOutside => World(_site.Plan.GateOutside);
        internal Vector3 GateInside => World(_site.Plan.GateInside);
        internal float JoinProgress => _joinProgress;
        internal RoadEdge Home => _home;

        internal ParkingManeuver Plan(ParkingCar car, Vector3 from, Vector3 forward,
            Vector3 goal, Vector3 goalForward) =>
            new ParkingManeuver(_site, _cars, car, from, forward, goal, goalForward);

        static bool FitsParkingStall(GameObject prefab)
        {
            if (FitsStall.TryGetValue(prefab, out bool fits)) return fits;
            bool measured = CarBody.MeasureTrafficFootprint(prefab.transform, out float halfLength, out float halfWidth);
            // The traffic catalogue includes seven-metre pickups. Those belong in
            // larger bays; placing them in this plan fills its manoeuvring aisle.
            fits = measured && halfLength * 2f + ParkingManeuver.Clearance <= ParkingBlockPlan.StallDepth &&
                halfWidth * 2f + ParkingManeuver.Clearance <= ParkingBlockPlan.StallWidth;
            FitsStall[prefab] = fits;
            return fits;
        }

        internal bool TryUseDrive(ParkingCar car)
        {
            if (ReferenceEquals(_moving, car)) return true;
            if (!_requests.Contains(car)) _requests.Add(car);
            if (_moving != null || !ReferenceEquals(_requests[0], car)) return false;
            _requests.RemoveAt(0);
            _moving = car;
            return true;
        }

        internal bool RequestReturn(ParkingCar car) => TryUseDrive(car);

        internal void ReleaseDrive(ParkingCar car)
        {
            if (ReferenceEquals(_moving, car)) _moving = null;
        }

        /// <summary>Nothing standing on the home road between this car and the join:
        /// the sweep in to the gate starts where the car is and is driven by hand, so
        /// a car stopped on that stretch would be driven through, not round.</summary>
        /// <summary>A gate this close to the junction sends the sweep in through the
        /// corner of its box.</summary>
        const float GateOnTheCorner = 20f;

        internal bool EntryClear(ParkingCar self)
        {
            if (_home == null || self.CurrentEdge != _home) return true;
            if (_joinProgress < GateOnTheCorner)
            {
                float sweep = (Vector3.Distance(self.Tf.position, GateOutside) +
                               Vector3.Distance(GateOutside, GateInside)) / PatrolDocking.Speed + 1.5f;
                if (!LaneGate.BoxClear(_home.From, sweep, self)) return false;
            }
            float mine = self.Progress;
            float lo = Mathf.Min(mine, _joinProgress) - self.HalfLen;
            float hi = Mathf.Max(mine, _joinProgress) + self.HalfLen;
            for (int i = 0; i < _home.Cars.Count; i++)
            {
                var other = _home.Cars[i];
                if (ReferenceEquals(other, self)) continue;
                float progress = other.Progress;
                if (progress > lo && progress < hi) return false;
            }
            return true;
        }

        /// <summary>Room to come out onto the road. The way from the gate to the join
        /// is walked at PatrolDocking.Speed and takes its seconds, so the window behind
        /// the join is TIME, not metres - a car that would reach the join inside the
        /// sweep, at the speed it is doing, meets us on the way out - and it reaches
        /// back onto the roads that feed this one. The same rule as
        /// PolicePatrolCar.KerbClear, for the same reason.</summary>
        internal bool RejoinClear(ParkingCar self)
        {
            if (_home == null) return false;
            float sweep = (Vector3.Distance(GateInside, GateOutside) +
                           Vector3.Distance(GateOutside, _join)) / PatrolDocking.Speed + 1.5f;
            if (!LaneGate.Clear(_home, _joinProgress, sweep, self,
                                behindMin: self.HalfLen + 18f, aheadMax: self.HalfLen + 7f))
                return false;
            return true;
        }

        public void Tick(float dt)
        {
            if (_moving != null && !_moving.CanCommute) _moving = null;
            _requests.RemoveAll(car => !car.CanCommute);
            for (int i = 0; i < _cars.Count; i++) _cars[i].TickParking(dt);

            // Each lot boom is real scene state, not a permanently lowered prop. It opens
            // only while the car holding the shared driveway is at the entrance and
            // closes again once that car is safely on either side of it.
            if (_site.GateArm != null)
            {
                float open = 0f;
                if (_moving != null && _moving.Tf != null)
                {
                    var gate = World(_site.Plan.Gate);
                    if (Vector3.Distance(_moving.Tf.position, gate) < 11f) open = 1f;
                }
                _site.GateArm.Toward(open, dt);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                StreetTraffic.Users.Remove(car);
                car.Despawn();
                if (car.Tf != null) Object.Destroy(car.Tf.gameObject);
            }
            _cars.Clear();
            _requests.Clear();
            _moving = null;
        }
    }

    /// <summary>One commuter cycling between its reserved bay and the public road.</summary>
    public sealed class ParkingCar : DemoVehicle
    {
        public enum Mode { Parked, Exiting, Driving, Returning, Entering }

        readonly struct Motion
        {
            public readonly PatrolDocking.Curve Curve;
            public readonly bool FollowTangent;
            public readonly Quaternion EndRotation;

            public Motion(PatrolDocking.Curve curve, bool followTangent, Quaternion endRotation)
            {
                Curve = curve;
                FollowTangent = followTangent;
                EndRotation = endRotation;
            }
        }

        protected override bool VanishesWhenStuck => false;

        ParkingLot _lot;
        ParkingBlockPlan.Stall _stall;
        readonly List<Motion> _motions = new List<Motion>();
        System.Random _dice;
        Vector2 _driveSeconds, _parkedSeconds;
        float _timer, _t;
        int _motion;
        int _laneMotion = -1;   // the exit motion that first puts the body on the road
        Quaternion _motionStart;
        ParkingManeuver _query;
        bool _planningExit;
        readonly List<ParkingManeuver.Pose> _entryPath = new List<ParkingManeuver.Pose>();

        public Mode State { get; private set; }
        internal bool CanCommute => Tf != null && !Gone && !Wrecked && !EngineDead;

        internal void Bind(
            ParkingLot lot, ParkingBlockPlan.Stall stall, int index, float firstWait,
            Vector2 driveSeconds, Vector2 parkedSeconds, System.Random dice)
        {
            _lot = lot;
            _stall = stall;
            _dice = dice;
            _driveSeconds = driveSeconds;
            _parkedSeconds = parkedSeconds;
            State = Mode.Parked;
            _timer = firstWait;
            Tag = "parking";
            Profile = DriverProfile.Traffic;
            EngineOff = true;

            var position = lot.World(stall.Stand);
            var forward = lot.Direction(stall.Forward);
            Tf.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            PlaceAt(position, forward); // establishes the road-user pose even when no road is near
            Despawn();
            Slid(position);
        }

        public void TickParking(float dt)
        {
            if (dt <= 0f) return;
            if (!CanCommute) { _query = null; Speed = 0f; return; }
            AdvancePlan();
            Tick(dt);
        }

        internal override void TickStep(float dt)
        {
            if (!CanCommute || dt <= 0f) return;
            switch (State)
            {
                case Mode.Parked:
                    _timer -= dt;
                    if (_timer <= 0f && _lot.TryUseDrive(this))
                        BeginExit();
                    break;

                case Mode.Exiting:
                case Mode.Entering:
                    TickMotion(dt);
                    break;

                case Mode.Driving:
                    base.TickStep(dt);
                    _timer -= dt;
                    if (_timer <= 0f) BeginReturn();
                    break;

                case Mode.Returning:
                    base.TickStep(dt);
                    TickReturn();
                    break;
            }
        }

        void BeginExit()
        {
            State = Mode.Exiting;
            EngineOff = false;
            _planningExit = true;
            _query = _lot.Plan(this, Tf.position, Tf.forward,
                _lot.GateInside, _lot.Direction(Vector3.back));
        }

        void BuildExit(IReadOnlyList<ParkingManeuver.Pose> path)
        {
            _motions.Clear();
            var gateInside = _lot.GateInside;
            var gateOutside = _lot.GateOutside;
            var outward = _lot.Direction(Vector3.back);
            AddPath(path);
            // the motion that takes the body out of the gate: held at ITS start, inside
            // the lot, until the road is clear (TickMotion). If the gate has no depth
            // the index falls on the sweep to the join, which starts at the gate anyway
            _laneMotion = _motions.Count;
            AddSweep(gateInside, outward, gateOutside, outward);
            AddSweep(gateOutside, outward, _lot.Join, _lot.Home.Dir);
            // a gate ON the lane adds neither sweep (AddSweep drops anything under
            // half a metre); the hold then belongs to the last motion there is, as it
            // always did, and never to an index past the end of the list
            _laneMotion = Mathf.Min(_laneMotion, _motions.Count - 1);
            StartMotions();
        }

        void BeginReturn()
        {
            if (_query != null) return;
            // Reserve the driveway before approaching it. Otherwise returners park
            // across an exiting car's path while waiting for the drive it owns.
            // Departures and returns share a FIFO so early cars cannot monopolise
            // the lot while later bays never get their first departure.
            if (!_lot.RequestReturn(this)) { _timer = 2f; return; }
            _planningExit = false;
            _entryPath.Clear();
            _query = _lot.Plan(this, _lot.GateInside, _lot.Direction(Vector3.forward),
                _lot.World(_stall.Stand), _lot.Direction(_stall.Forward));
        }

        void AdvancePlan()
        {
            if (_query == null) return;
            // Work once per rendered frame, independently of the number of 16x
            // driving substeps. A difficult three-point turn must not stall the city.
            _query.Step(256);
            if (!_query.Finished) return;
            var query = _query;
            _query = null;
            if (!query.Found)
            {
                _lot.ReleaseDrive(this);
                _timer = 10f;
                if (_planningExit) { State = Mode.Parked; EngineOff = true; }
                return;
            }
            if (_planningExit) { BuildExit(query.Path); return; }
            _entryPath.AddRange(query.Path);
            if (!GoTo(_lot.Join, park: false, standOff: 0f, stopAtGoal: true,
                      wantHeading: _lot.Home.Heading))
            {
                _lot.ReleaseDrive(this);
                _timer = 2f;
                return;
            }
            State = Mode.Returning;
        }

        void TickReturn()
        {
            float distance = Vector3.Distance(Tf.position, _lot.Join);
            // Road goals can finish up to three metres beyond the mark. The entry
            // sweep starts at the physical pose and still checks every movement.
            bool atEntrance = CurrentEdge == _lot.Home && distance <= 3f;
            if (atEntrance && Mathf.Abs(Speed) < 1.2f)
            {
                if (!_lot.EntryClear(this)) return;
                BeginEnter();
                return;
            }

            if (!HasGoal)
                GoTo(_lot.Join, park: false, standOff: 0f, stopAtGoal: true,
                     wantHeading: _lot.Home.Heading);
        }

        protected override void OnArrived()
        {
            // Hold at the actual entrance while its turning sweep clears, never
            // abandon a parking search somewhere along the public running lane.
            if (State == Mode.Returning) Halt(false);
        }

        void BeginEnter()
        {
            // THE ROAD GOAL DIES AT THE GATE. Leaving the graph keeps the goal (GoTo the
            // join, park at the kerb), and the car came back out of the lot with it
            // still set: it spawned at the join and parked at the kerb on the spot,
            // square across the lot's mouth, and the next commuter home swept through
            // it (DEPOT-004 S2 seed 101, 99 belt refusals, 5 s). Out of the lot it
            // drives as traffic does until BeginReturn gives it a goal of its own.
            Stop();
            Despawn();
            State = Mode.Entering;
            EngineOff = false;
            _motions.Clear();

            var gateOutside = _lot.GateOutside;
            var gateInside = _lot.GateInside;
            var inward = _lot.Direction(Vector3.forward);

            AddSweep(Tf.position, Tf.forward, gateOutside, inward);
            AddSweep(gateOutside, inward, gateInside, inward);
            AddPath(_entryPath);
            StartMotions();
        }

        void AddPath(IReadOnlyList<ParkingManeuver.Pose> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1].Position;
                var to = path[i].Position;
                Add(new PatrolDocking.Curve { A = from, B = to,
                    ControlA = Vector3.Lerp(from, to, 1f / 3f),
                    ControlB = Vector3.Lerp(from, to, 2f / 3f) }, false, path[i].Rotation);
            }
        }

        void AddSweep(Vector3 from, Vector3 fromWay, Vector3 to, Vector3 toWay)
        {
            if (Flat(to - from).sqrMagnitude < 0.25f) return;
            Add(PatrolDocking.Sweep(from, fromWay, to, toWay), true, Quaternion.identity);
        }

        void Add(PatrolDocking.Curve curve, bool tangent, Quaternion end)
            => _motions.Add(new Motion(curve, tangent, end));

        void StartMotions()
        {
            _motion = 0;
            _t = 0f;
            _motionStart = Tf.rotation;
            Speed = 0f;
        }

        void TickMotion(float dt)
        {
            if (_query != null) return;
            if (_motion >= _motions.Count) { FinishMotion(); return; }
            // HELD INSIDE THE GATE, where no part of the body is in the lane yet. The
            // hold used to be at the start of the last sweep, at the kerb line, with the
            // nose already in the running lane: a car came along at nine metres a
            // second, met it, and the two stood waiting for each other for a minute
            // (DEPOT-004 S2 seed 102, 1 203 belt refusals).
            bool atGate = State == Mode.Exiting && _motion == _laneMotion;
            if (atGate && _t < 0.2f && !_lot.RejoinClear(this))
            {
                Speed = 0f;
                return;
            }

            var motion = _motions[_motion];
            Speed = PatrolDocking.Speed;
            float next = PatrolDocking.Advance(motion.Curve, _t, PatrolDocking.Speed * dt);
            var position = PatrolDocking.Point(motion.Curve, next);
            var rotation = motion.FollowTangent
                ? PatrolDocking.Heading(motion.Curve, next, _motionStart)
                : Quaternion.Slerp(_motionStart, motion.EndRotation, next);
            if (!MotionClear(position, rotation)) { Speed = 0f; return; }
            _t = next;
            Tf.SetPositionAndRotation(position, rotation);
            Slid(position, Tf.forward);
            if (_t < 1f) return;

            _motion++;
            _t = 0f;
            _motionStart = Tf.rotation;
            if (_motion >= _motions.Count) FinishMotion();
        }

        bool MotionClear(Vector3 position, Quaternion rotation)
        {
            // Translation and yaw happen together. Testing the new yaw at the old
            // position can falsely trap a tail that clears its neighbour as it moves.
            float travel = Vector3.Distance(Tf.position, position) +
                Quaternion.Angle(Tf.rotation, rotation) * Mathf.Deg2Rad * (HalfLen + HalfWide);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travel / 0.1f));
            for (int i = 0; i <= samples; i++)
            {
                float fraction = i / (float)samples;
                var at = Vector3.Lerp(Tf.position, position, fraction);
                var forward = Quaternion.Slerp(Tf.rotation, rotation, fraction) * Vector3.forward;
                if (RoadSpace.Inside(this, at, forward, HalfLen, HalfWide, out _) != null) return false;
            }
            return true;
        }

        void FinishMotion()
        {
            Speed = 0f;
            _lot.ReleaseDrive(this);
            if (State == Mode.Exiting)
            {
                Spawn(_lot.Home, _lot.JoinProgress);
                State = Mode.Driving;
                _timer = Range(_driveSeconds);
                return;
            }

            var position = _lot.World(_stall.Stand);
            var forward = _lot.Direction(_stall.Forward);
            Tf.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            Slid(position, forward);
            State = Mode.Parked;
            EngineOff = true;
            _timer = Range(_parkedSeconds);
        }

        float Range(Vector2 range)
        {
            float lo = Mathf.Min(range.x, range.y);
            float hi = Mathf.Max(range.x, range.y);
            return lo + (float)_dice.NextDouble() * (hi - lo);
        }

        static Vector3 Flat(Vector3 value) { value.y = 0f; return value; }
    }
}
