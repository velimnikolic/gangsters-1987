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
        ParkingCar _moving;

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
                var prefab = CoreRoads.PickCar(dice);
                if (prefab == null) break;

                var go = Object.Instantiate(prefab, liveRoot);
                go.name = $"Parking Car {i + 1}";
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var body in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(body);
                foreach (var collider in go.GetComponentsInChildren<Collider>()) Object.Destroy(collider);

                var bounds = new Bounds(go.transform.position, Vector3.zero);
                bool measured = false;
                foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                {
                    if (!measured) { bounds = renderer.bounds; measured = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }

                var car = new ParkingCar
                {
                    Tf = go.transform,
                    Net = net,
                    HalfLen = measured ? bounds.extents.z + 0.3f : 2.3f,
                    HalfWide = measured ? Mathf.Clamp(bounds.extents.x, 0.7f, 1.3f) : 0.95f,
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

        internal bool TryUseDrive(ParkingCar car)
        {
            if (_moving != null && !ReferenceEquals(_moving, car)) return false;
            _moving = car;
            return true;
        }

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

        public Mode State { get; private set; }

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
            if (Tf == null || Gone || dt <= 0f) return;
            switch (State)
            {
                case Mode.Parked:
                    _timer -= dt;
                    if (_timer <= 0f && _lot.RejoinClear(this) && _lot.TryUseDrive(this))
                        BeginExit();
                    break;

                case Mode.Exiting:
                case Mode.Entering:
                    TickMotion(dt);
                    break;

                case Mode.Driving:
                    Tick(dt);
                    _timer -= dt;
                    if (_timer <= 0f) BeginReturn();
                    break;

                case Mode.Returning:
                    Tick(dt);
                    TickReturn();
                    break;
            }
        }

        void BeginExit()
        {
            State = Mode.Exiting;
            EngineOff = false;
            _motions.Clear();

            var stand = _lot.World(_stall.Stand);
            var forward = _lot.Direction(_stall.Forward);
            var mouth = _lot.World(_stall.Mouth);
            var junction = _lot.World(_stall.Junction);
            var gateInside = _lot.GateInside;
            var gateOutside = _lot.GateOutside;
            var aisle = Flat(junction - mouth).normalized;
            var outward = _lot.Direction(Vector3.back);

            Add(PatrolDocking.Undock(stand, forward, mouth, aisle), true, Quaternion.identity);
            AddSweep(mouth, aisle, junction, outward);
            AddSweep(junction, outward, gateInside, outward);
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
            if (!GoTo(_lot.Join, park: true, standOff: 0f, stopAtGoal: true,
                      wantHeading: _lot.Home.Heading))
            {
                _timer = 2f;
                return;
            }
            State = Mode.Returning;
        }

        void TickReturn()
        {
            float distance = Vector3.Distance(Tf.position, _lot.Join);
            bool atEntrance = distance < HalfLen + 12f
                           || (Parked && CurrentEdge == _lot.Home && distance < 30f);
            if (atEntrance && Mathf.Abs(Speed) < 1.2f)
            {
                // the way in is hand-driven from here to the gate: nobody may be
                // standing on it, and the drive is asked for only once it is clear,
                // so a car held at the mouth does not hold the drive against an exit
                if (!_lot.EntryClear(this) || !_lot.TryUseDrive(this)) { Halt(false); return; }
                BeginEnter();
                return;
            }

            if (!HasGoal && !Halted)
                GoTo(_lot.Join, park: true, standOff: 0f, stopAtGoal: true,
                     wantHeading: _lot.Home.Heading);
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
            var junction = _lot.World(_stall.Junction);
            var mouth = _lot.World(_stall.Mouth);
            var stand = _lot.World(_stall.Stand);
            var inward = _lot.Direction(Vector3.forward);
            var aisle = Flat(mouth - junction).normalized;
            var forward = _lot.Direction(_stall.Forward);

            AddSweep(Tf.position, Tf.forward, gateOutside, inward);
            AddSweep(gateOutside, inward, gateInside, inward);
            AddSweep(gateInside, inward, junction, aisle);
            AddSweep(junction, aisle, mouth, aisle);
            Add(PatrolDocking.Dock(mouth, stand, forward), false, Quaternion.LookRotation(forward));
            StartMotions();
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
            _t = PatrolDocking.Advance(motion.Curve, _t, PatrolDocking.Speed * dt);
            var position = PatrolDocking.Point(motion.Curve, _t);
            var rotation = motion.FollowTangent
                ? PatrolDocking.Heading(motion.Curve, _t, _motionStart)
                : Quaternion.Slerp(_motionStart, motion.EndRotation,
                                   Mathf.SmoothStep(0f, 1f, _t));
            Tf.SetPositionAndRotation(position, rotation);
            Slid(position);
            if (_t < 1f) return;

            _motion++;
            _t = 0f;
            _motionStart = Tf.rotation;
            if (_motion >= _motions.Count) FinishMotion();
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
            Slid(position);
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
