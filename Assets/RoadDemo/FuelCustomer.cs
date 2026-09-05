using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A car of the traffic with an errand: it wants petrol. It drives the lane graph
    /// like anything else (DemoVehicle - the plain commuter at the wheel), and when its
    /// tank tells it to, it books a bay at a <see cref="FuelStation"/>, routes to the
    /// road outside it, and then LEAVES the graph: a forecourt is not a road, so the
    /// two legs across it are hand-driven along a Bezier at walking pace, exactly the
    /// way a patrol car swings into its stall (PatrolDocking).
    ///
    /// The whole errand is here rather than in a scene's builder because it is the same
    /// errand everywhere - the pump bench, the city's wayside station, a station in a
    /// quarter later on. A scene decides HOW MANY cars want petrol and how often; it
    /// does not decide what wanting petrol looks like.
    ///
    /// The man is the point of it. A car that slid up to a pump, waited, and slid away
    /// is a vending machine; what makes a forecourt read is the driver getting out,
    /// walking round the back of his own car, standing there while the tank fills,
    /// going in to pay and coming out again. So there are two bodies, and never both at
    /// once: the one sat in the seat (CarOccupant) and the one on his feet
    /// (FuelDriver).
    /// </summary>
    public sealed class FuelCustomer : DemoVehicle
    {
        // stands at a pump on purpose; the station's own watchdogs mind it
        protected override bool VanishesWhenStuck => false;

        public enum Mode
        {
            Cruising, Wanting, Approaching, TurningIn, Fuelling, Paying, Boarding, PullingOut,
        }

        public Mode State { get; private set; } = Mode.Cruising;

        /// <summary>The wheels and the doors of this body.</summary>
        public CarBody Body;

        /// <summary>The man, in his two halves: sat at the wheel, and on his feet.</summary>
        public CarOccupant Seated;
        public FuelDriver Driver;

        FuelStation _station;
        int _bay = -1;
        float _tank;          // seconds until he wants petrol again
        float _timer;         // whatever the current leg is waiting on
        float _stuck;         // how long a leg has been getting nowhere
        float _approach;      // how long the drive to the station has taken

        PatrolDocking.Curve _curve, _curveNext;
        bool _second;      // the second half of the crossing: mouth to bay, or mouth to lane
        float _t;
        Quaternion _fromRot;

        /// <summary>How long the errand's standing parts take. A tankful is the better
        /// part of a minute at a 1987 pump, and the queue at the till is another; both
        /// are cut down here to what a bench can stand and watch.</summary>
        public Vector2 FillSeconds = new Vector2(11f, 18f);
        public Vector2 PaySeconds = new Vector2(7f, 14f);

        /// <summary>Seconds of driving between one tankful and the next.</summary>
        public Vector2 TankSeconds = new Vector2(60f, 200f);

        /// <summary>A leg that gets nowhere for this long is given up on and the car is
        /// put back on the road: a demo that quietly wedges one car at one pump has
        /// lost a bay for the rest of the run, and nothing says so.</summary>
        public const float Patience = 45f;

        /// <summary>How long the last fifty metres up to the kerb may take before the
        /// bay is handed back. Short, because by then the car is on the right lane with
        /// the mouth in front of it and nothing but the traffic between: anything longer
        /// than this is a jam, not a drive (the errand only books a bay once it is that
        /// close - see TickWanting).</summary>
        public const float ApproachPatience = 120f;

        /// <summary>Filled by whoever built the car, so an audit can name it.</summary>
        public string Plate = "car";

        public FuelStation Station => _station;
        public int Bay => _bay;

        /// <summary>Where the errand stands, for the trace and the overlay.</summary>
        public string Doing => State switch
        {
            Mode.Cruising => "driving",
            Mode.Wanting => "wants petrol",
            Mode.Approaching => "pulling up at the pumps",
            Mode.TurningIn => "turning onto the forecourt",
            Mode.Fuelling => "at the pump",
            Mode.Paying => "paying inside",
            Mode.Boarding => "getting back in",
            Mode.PullingOut => "pulling off the forecourt",
            _ => "driving",
        };

        public void SetStation(FuelStation station, float firstTank)
        {
            _station = station;
            _tank = firstTank;
        }

        // ------------------------------------------------------------------- the tick

        public void TickErrand(float dt)
        {
            Body?.TickDoors(dt);
            Driver?.TickDriver(dt);

            switch (State)
            {
                case Mode.Cruising:
                    Tick(dt);
                    _tank -= dt;
                    if (_tank <= 0f && _station != null && _station.Lane != null) State = Mode.Wanting;
                    break;

                case Mode.Wanting:
                    Tick(dt);
                    TickWanting();
                    break;

                case Mode.Approaching:
                    Tick(dt);
                    if (ParkingFailed) { GiveUp("no reachable pump approach"); break; }
                    Watch(dt);
                    // and a bay booked is a bay nobody else can have, so the drive to it
                    // is on a clock of its own: the stuck timer only counts a car that is
                    // standing still, and a car going round and round is not
                    _approach += dt;
                    if (_approach > ApproachPatience) { GiveUp("still not at the pumps"); break; }
                    TickApproach(dt);
                    break;

                case Mode.TurningIn:
                case Mode.PullingOut:
                    TickSweep(dt);
                    break;

                case Mode.Fuelling:
                    TickFuelling(dt);
                    break;

                case Mode.Paying:
                    TickPaying(dt);
                    break;

                case Mode.Boarding:
                    TickBoarding(dt);
                    break;
            }
        }

        // ---------------------------------------------------------------- the errand

        /// <summary>
        /// He wants petrol, and he is watching for the station rather than being routed
        /// to it. THIS IS THE WHOLE OF THE APPROACH and it took three rewrites to get
        /// here, so it is worth saying why.
        ///
        /// Sending the car to a point on the kerb (GoTo, across the whole lane graph)
        /// looks like the obvious thing and is not. A two-way road is TWO ONE-WAY LANES
        /// in the graph, and a junction carries no connector from a lane to its own
        /// opposite number, so which way round a car is going is a property it cannot
        /// change except by turning in the road. On a plain circuit that leaves two
        /// separate cycles, and a car on the wrong one drove laps for two minutes at a
        /// goal it could not reach - and did it at speed, so no stuck-timer ever saw it.
        ///
        /// A driver does not do that either. He drives where he was going, and when the
        /// station comes up on his side of the road he pulls in. So: no route, no goal,
        /// nothing booked - just the traffic's own wandering until the car finds itself
        /// on the station's lane with the mouth ahead of it, and only THEN a bay, the
        /// kerb slot, and a fifty-metre run to the kerb that cannot fail.
        /// </summary>
        void TickWanting()
        {
            if (CurrentEdge != _station.Lane) return;
            float togo = _station.KerbInS - Progress;
            if (togo < PullInFrom || togo > PullInSight) return;
            if (!_station.AnyFree || !_station.TryApproach(this)) return;
            // THE APRON IS TAKEN NOW, before he even slows down, and not when he gets to
            // the kerb. A customer that pulls up and only then finds the forecourt busy
            // stands at the kerb for half a minute with the traffic pressing into the
            // back of him - four thousand refused steps in eight minutes, all in the
            // fifty metres outside the station. Taken first, the pull-in and the turn
            // are one movement, and whoever else wants petrol keeps driving until it is
            // free, which is what the road wants him to do anyway.
            if (!_station.TryCross(this)) { _station.DoneApproaching(this); return; }
            if (!_station.TryTake(out _bay))
            {
                _station.DoneApproaching(this);
                _station.DoneCrossing(this);
                return;
            }

            State = Mode.Approaching;
            _stuck = 0f;
            _approach = 0f;
            // PARKED, not stopped in the lane. A car waiting to turn in stands in the
            // kerb strip - the 2.5 m outside the marked lane that this road, like every
            // road the demo lays, carries - and the traffic goes by. Stopped in the
            // running lane instead it was a wall: four thousand refused steps in seven
            // minutes, all of them cars pressing into the back of one customer waiting
            // for the apron.
            if (!GoTo(_station.KerbIn, park: true, standOff: 0f, stopAtGoal: true,
                      wantHeading: _station.Lane.Heading) || ParkingFailed)
                GiveUp("no way to the kerb");
        }

        /// <summary>How far short of the kerb he starts pulling up, and how near he has
        /// to be before he bothers. The near figure is his stopping distance and a bit;
        /// the far one is what a driver can see is a filling station.</summary>
        const float PullInFrom = 8f, PullInSight = 60f;

        /// <summary>Anybody on foot within <paramref name="berth"/> of this point. The
        /// way onto the forecourt and off it crosses the pavement, and a car that drives
        /// through the crowd is the one thing nobody watching would forgive.</summary>
        static bool NobodyNear(Vector3 at, float berth, PedestrianAgent except)
        {
            var walkers = PedestrianAgent.Everyone;
            float berth2 = berth * berth;
            for (int i = 0; i < walkers.Count; i++)
            {
                var man = walkers[i];
                if (man == null || man == except || man.Tf == null) continue;
                if (!man.Tf.gameObject.activeInHierarchy) continue;
                var d = man.Tf.position - at;
                d.y = 0f;
                if (d.sqrMagnitude < berth2) return false;
            }
            return true;
        }

        /// <summary>How much of the mouth a car turning in wants to itself. Wider than
        /// the swept check below, because this is the decision to leave the road at all.</summary>
        const float MouthBerth = 4.2f;

        bool MouthClear(Vector3 mouth) => NobodyNear(mouth, MouthBerth, Driver);

        /// <summary>The last few metres of the approach, which is the whole of the
        /// difficulty. The car has been ROUTED to a point on the lane outside the
        /// station and stops there - and once RoadCar has put it there the goal is
        /// spent, so a car that could not turn in at that moment drove off and came
        /// round the circuit again, at cruising speed, for ever. Three things follow:
        ///
        /// it HOLDS the kerb (Halt) while the apron is busy, where the traffic can see
        /// it and queue behind it, rather than halfway across the pavement where nothing
        /// can - a car in the middle of the hand-driven curve has left the lane graph
        /// and is invisible to every driver on it;
        ///
        /// it CLAIMS the mouth while it holds, so the crowd stands back and the way
        /// actually clears instead of being walked over by the next man along;
        ///
        /// and if it is anywhere else with no goal left, it asks for the kerb again.</summary>
        void TickApproach(float dt)
        {
            // MEASURED OFF THE GROUND, not off the lane's parameter. A car sent to a
            // point stops with its NOSE there, so its middle is a body-length short of
            // it, and a pickup is longer than a saloon: judged by lane progress within a
            // few metres, the long bodies never counted as arrived, were re-sent, drove
            // round the whole circuit and came back - which is a customer that never
            // stops "driving to the pumps" and never trips the stuck timer either,
            // because it is moving the whole time.
            // and a car parking takes the nearest free stretch of kerb, which may not be
            // the metre it was sent to (RoadCar.ChooseKerbSpot), so the mark is loose -
            // and looser still once it has actually STOPPED at a kerb on the station's
            // own lane, because wherever that is, it is where the turn starts from. The
            // curve is drawn from the car's real pose, so nothing downstream cares.
            float off = Vector3.Distance(Tf.position, _station.KerbIn);
            bool atSpot = off < HalfLen + 11f
                       || (Parked && CurrentEdge == _station.Lane && off < 32f);
            if (atSpot && Mathf.Abs(Speed) < 1.5f)
            {
                // waiting at the kerb for the car in front to finish crossing the apron
                // is a QUEUE, not a fault: neither clock runs while he is in one
                if (!_station.TryCross(this))
                {
                    Halt(false);
                    _stuck = 0f;
                    _approach = 0f;
                    _waited = 0f;
                    return;
                }
                PedestrianAgent.CarCrossing(_station.MouthIn, MouthBerth, 0.25f);
                if (MouthClear(_station.MouthIn)) { BeginTurnIn(); return; }
                // and he does not wait on the pavement for ever either: two people who
                // have stopped for a word by the mouth are not going to move for him,
                // and the crossing itself creeps past whoever is left (WaitForWalkers)
                _waited += dt;
                if (_waited > WaitForWalkers) BeginTurnIn();
                else Halt(false);
                return;
            }
            // still driving to the kerb, or still shuffling into it: the approach has a
            // clock of its own (_approach) and the stuck timer has no business running
            // while the car is doing exactly as it was told
            if (HasGoal) { _stuck = 0f; return; }
            if (!Halted)
                GoTo(_station.KerbIn, park: true, standOff: 0f, stopAtGoal: true,
                     wantHeading: _station.Lane.Heading);
        }

        /// <summary>The next few metres of the curve, swept: what the car is about to
        /// drive over rather than a circle round a fixed point, so a man standing on the
        /// far side of the apron does not hold a car that was never going near him.
        ///
        /// It also CLAIMS that ground as it goes (PedestrianAgent.CarCrossing), which is
        /// the half that makes the whole thing terminate: without it the car waits for a
        /// crowd that has no idea it is there, walkers keep arriving, and the wait never
        /// ends - which is exactly what the first run of this bench did, one car sat on
        /// the pavement for seventy seconds with the queue behind it.</summary>
        bool PathClear()
        {
            float ahead = PatrolDocking.Advance(_curve, _t, HalfLen + 3f);
            var mid = PatrolDocking.Point(_curve, (_t + ahead) * 0.5f);
            var far = PatrolDocking.Point(_curve, ahead);
            PedestrianAgent.CarCrossing(mid, HalfWide + 1.4f, 0.25f);
            PedestrianAgent.CarCrossing(far, HalfWide + 1.4f, 0.25f);
            return NobodyNear(mid, HalfWide + 0.8f, Driver) && NobodyNear(far, HalfWide + 0.8f, Driver);
        }

        /// <summary>How long the car will wait for somebody who is in its way and not
        /// getting out of it - a man stood still in the middle of the crossing, which is
        /// nobody's fault and cannot be waited out. After this it creeps on: 3 m/s past a
        /// man's elbow is rude, and it is a great deal better than a forecourt that
        /// deadlocks the moment a walker stops on it.</summary>
        const float WaitForWalkers = 4f;
        float _waited;

        void BeginTurnIn()
        {
            var bay = _station.Bays[_bay];
            // the kerb outside is free for the next customer the moment this one
            // starts across the apron
            _station.DoneApproaching(this);
            // NOT Despawn - not yet. The car is still standing in the lane, and its
            // claim there is the only thing telling the traffic behind that it is. It
            // gives the lane up in TickSweep, the moment its body is off it.
            State = Mode.TurningIn;
            // THROUGH THE MOUTH, in two. A single curve from the kerb to the bay is a
            // car crossing the footway wherever the arithmetic puts it - which was over
            // twenty metres of pavement, in front of anybody walking down it. The
            // crossover is a real place with the pavement left out of it, so the car
            // aims for that first and for its bay second.
            _curve = PatrolDocking.Sweep(Tf.position, Tf.forward, _station.MouthIn, _station.IntoApron);
            _curveNext = PatrolDocking.Sweep(_station.MouthIn, _station.IntoApron, bay.Stand, bay.Forward);
            _second = false;
            _t = 0f;
            _left = false;
            _waited = 0f;
            _fromRot = Tf.rotation;
            _stuck = 0f;
        }

        bool _left;   // the lane has been given up for this sweep

        /// <summary>Is the body still standing on the carriageway? Everything the sweep
        /// may pause for - a walker, a gap in the traffic - it pauses for OFF the road;
        /// a car that stops astride a lane it has no claim on is a car nobody can plan
        /// round, and the belt spends the rest of the run refusing steps into it.</summary>
        bool OnCarriageway(Vector3 at)
        {
            var road = _station.Lane?.Road;
            if (road == null) return false;
            road.Project(at, out _, out float d);
            return Mathf.Abs(d) < road.HalfRoad + HalfLen;
        }

        void TickSweep(float dt)
        {
            bool onRoad = OnCarriageway(Tf.position);
            // ONE car crosses the apron at a time. Neither leg is driven by the belt
            // that keeps cars off each other on the road (RoadSpace works in a
            // carriageway's frame and there is no carriageway here), so two cars whose
            // arcs cross would drive straight through one another.
            //
            // Off the road it also waits for anybody walking over the next few metres,
            // and - on the way out - for a gap in the traffic to come back into. On the
            // road it waits for nothing: it gets off it.
            bool clear = PathClear();      // called for its claim as well as its answer
            // the walker clock is cleared by MOVING, not by a frame in which nobody
            // happened to be in the way: on a busy forecourt "clear" flickers, and
            // reset on the flicker the four-second cap never came due at all
            if (!clear) _waited += dt;
            bool crowded = !onRoad && !clear && _waited < WaitForWalkers;
            // waiting off the road for a gap to come back into is not being stuck, it is
            // giving way, and a car may do it for a good while on a busy road
            bool noGap = !onRoad && State == Mode.PullingOut && _second
                      && _t > 0.15f && !RejoinClear();
            bool mine = _station.TryCross(this);
            if (!mine || crowded || noGap)
            {
                Speed = 0f;
                // EVERY reason a crossing pauses is a reason to wait, not a fault: the
                // apron is busy, the traffic has no gap in it, somebody is walking over
                // the ground. All three happen off the road, where a stopped car is in
                // nobody's way, so all three run on one long clock and none of them
                // touches the stuck timer - which is for a car that has met something it
                // cannot get past, and none of these is that.
                _stuck = 0f;
                _gapWait += dt;
                if (_gapWait > GapPatience)
                    GiveUp(noGap ? "no way back onto the road"
                         : !mine ? "the apron never cleared" : "the way over never cleared");
                return;
            }
            _gapWait = 0f;
            _waited = 0f;
            // and the crossing is GETTING SOMEWHERE this frame, so the stuck timer goes
            // back to nought. Without this it only ever counted up - the held frames
            // added to it and the moving frames did not clear it - so a crossing that
            // paused often enough for people ran the clock out while making perfectly
            // good progress.
            _stuck = 0f;
            Speed = PatrolDocking.Speed;
            _t = PatrolDocking.Advance(_curve, _t, PatrolDocking.Speed * dt);
            var at = PatrolDocking.Point(_curve, _t);
            at.y = RoadY;   // the forecourt is at road level; the curve carries no height
            Tf.SetPositionAndRotation(at, PatrolDocking.Heading(_curve, _t, _fromRot));
            // the street reads where a car IS off its road position, and a hand-driven
            // transform never writes it: without this the body stood at the kerb, in
            // the traffic's way, for the whole of the fuelling (CrewBike does the same
            // for a spill)
            Slid(at);
            Body?.TickWheels(dt, PatrolDocking.Speed, 0f);
            // clear of the lane at last: the claim on it goes back
            if (!_left && State == Mode.TurningIn && !OnCarriageway(at)) { _left = true; Despawn(); }
            if (_t < 1f) return;
            // the mouth reached: on to the second half of the crossing
            if (!_second) { _second = true; _curve = _curveNext; _t = 0f; return; }

            _station.DoneCrossing(this);
            if (State == Mode.TurningIn) BeginFuelling();
            else BackOnTheRoad();
        }

        /// <summary>A gap on the lane at the point the car comes back onto it - a real
        /// one, not a car's length. The last few metres of the way out are driven ALONG
        /// the lane while the car is still off the graph and invisible to everybody on
        /// it, so the gap has to cover that as well as the join: at ten metres a second
        /// a car twelve metres back arrives inside two. Judged short, the belt spent the
        /// run refusing steps into a body nobody could see coming.</summary>
        bool RejoinClear()
        {
            var lane = _station.Lane;
            if (lane == null) return true;
            float want = _station.KerbOutS;
            var cars = lane.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                if (ReferenceEquals(cars[i], this)) continue;
                float s = cars[i].Progress;
                if (s > want - (HalfLen + RejoinBehind) && s < want + (HalfLen + 5f)) return false;
            }
            return true;
        }

        /// <summary>Metres of clear lane wanted behind the join - two and a half seconds
        /// at the road's limit.</summary>
        const float RejoinBehind = 18f;

        /// <summary>How long it will wait at the mouth for that gap before something is
        /// plainly wrong with the road rather than busy.</summary>
        const float GapPatience = 90f;
        float _gapWait;

        void BeginFuelling()
        {
            State = Mode.Fuelling;
            EngineOff = true;
            Speed = 0f;
            _timer = 0.9f;              // the beat between stopping and the door opening
            _stuck = 0f;
            Body?.OpenDoorFor(0);
        }

        // He is out of the car for the whole of the middle of the errand, and the legs
        // differ only in where he is going and what he does when he gets there.
        enum Leg { Waiting, Out, ToNozzle, Filling, ToShop, Inside, Back, ToDoor, In }
        Leg _leg = Leg.Waiting;

        void TickFuelling(float dt)
        {
            switch (_leg)
            {
                case Leg.Waiting:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    // no body to get out (no crowd prefabs, no clips): the tank still
                    // fills, so a scene missing its people is a quiet forecourt rather
                    // than a car stood at a pump for ever
                    if (Driver?.Tf == null)
                    {
                        _leg = Leg.Filling;
                        _timer = Random.Range(FillSeconds.x, FillSeconds.y);
                        break;
                    }
                    StepOut();
                    break;

                case Leg.Out:
                    // round the back of his own car rather than through it: the parked
                    // bodies are not on the walkers' map (WalkObstacles is laid at build
                    // time and a car at a pump is not), so the way round is given as a
                    // corner to walk to and not left to the steering to discover
                    if (!Driver.Arrived) { Watch(dt); break; }
                    Body?.CloseDoorFor(0);
                    _leg = Leg.ToNozzle;
                    Driver.Doing = "walking to the pump";
                    Driver.WalkTo(BehindTheCar());
                    _stuck = 0f;
                    break;

                case Leg.ToNozzle:
                    if (!Driver.Arrived) { Watch(dt); break; }
                    var bay = _station.Bays[_bay];
                    if (!_atNozzle)
                    {
                        _atNozzle = true;
                        Driver.WalkTo(bay.Nozzle, bay.Pump - bay.Nozzle);
                        _stuck = 0f;
                        break;
                    }
                    _leg = Leg.Filling;
                    _timer = Random.Range(FillSeconds.x, FillSeconds.y);
                    Driver.Doing = "filling the tank";
                    _stuck = 0f;
                    break;

                case Leg.Filling:
                    _timer -= dt;
                    if (Driver != null && Random.value < dt * 0.25f) Driver.Fidget();
                    if (_timer > 0f) break;
                    if (Driver?.Tf == null) { BeginPullOut(); break; }
                    State = Mode.Paying;
                    _leg = Leg.ToShop;
                    Driver.Doing = "walking in to pay";
                    Driver.WalkTo(_station.ShopStep, _station.ShopDoor - _station.ShopStep);
                    _stuck = 0f;
                    break;
            }
        }

        void TickPaying(float dt)
        {
            switch (_leg)
            {
                case Leg.ToShop:
                    if (!Driver.Arrived) { Watch(dt); break; }
                    _leg = Leg.Inside;
                    _timer = Random.Range(PaySeconds.x, PaySeconds.y);
                    Driver.Doing = "at the till";
                    Driver.Show(false);
                    _stuck = 0f;
                    break;

                case Leg.Inside:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    Driver.Tf.SetPositionAndRotation(_station.ShopStep,
                        Quaternion.LookRotation(Flat(_station.ShopStep - _station.ShopDoor), Vector3.up));
                    Driver.Show(true);
                    State = Mode.Boarding;
                    _leg = Leg.Back;
                    Driver.Doing = "walking back to the car";
                    Driver.WalkTo(BehindTheCar());
                    _stuck = 0f;
                    break;
            }
        }

        void TickBoarding(float dt)
        {
            switch (_leg)
            {
                case Leg.Back:
                    if (!Driver.Arrived) { Watch(dt); break; }
                    _leg = Leg.ToDoor;
                    Driver.WalkTo(DoorSpot(), Tf.position - DoorSpot());
                    _stuck = 0f;
                    break;

                case Leg.ToDoor:
                    if (!Driver.Arrived) { Watch(dt); break; }
                    _leg = Leg.In;
                    _timer = 1.1f;
                    Body?.OpenDoorFor(0);
                    Driver.Doing = "getting in";
                    _stuck = 0f;
                    break;

                case Leg.In:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    Driver.Show(false);
                    Seated?.Show(true);
                    Body?.CloseDoorFor(0);
                    BeginPullOut();
                    break;
            }
        }

        /// <summary>The corner off his car's back quarter, on the door's side of it: the
        /// one place a man can stand that is neither in the car nor in the island, and
        /// the hinge both his walks turn on.</summary>
        Vector3 BehindTheCar()
        {
            var bay = _station.Bays[_bay];
            // the bays point along the station's local +X, so the driver's door - the
            // car's left - is on its local +Z side
            return _station.At(bay.X - (HalfLen + 1f), bay.Z + 2.2f);
        }

        bool _atNozzle;

        void StepOut()
        {
            _leg = Leg.Out;
            _atNozzle = false;
            Seated?.Show(false);
            var spot = DoorSpot();
            Driver.Tf.SetPositionAndRotation(spot, Quaternion.LookRotation(Flat(spot - Tf.position), Vector3.up));
            Driver.Show(true);
            Driver.Doing = "getting out";
            // one pace clear of his own door, so the door has room to shut
            Driver.WalkTo(spot + Flat(spot - Tf.position).normalized * 0.5f);
        }

        /// <summary>Where the driver's door puts him down. The body's own measured door
        /// point when it has doors, else a pace out from the driver's seat.</summary>
        Vector3 DoorSpot()
        {
            var spot = Body != null
                ? Body.DoorPoint(0)
                : Tf.position - Tf.right * 1.6f;
            spot.y = _station.GroundY;
            return spot;
        }

        void BeginPullOut()
        {
            State = Mode.PullingOut;
            EngineOff = false;
            Body?.CloseAllDoors();
            _curve = PatrolDocking.Sweep(Tf.position, Tf.forward, _station.MouthOut, _station.OutToRoad);
            _curveNext = PatrolDocking.Sweep(_station.MouthOut, _station.OutToRoad,
                                             _station.KerbOut, _station.Lane.Dir);
            _second = false;
            _t = 0f;
            _waited = 0f;
            _fromRot = Tf.rotation;
            _stuck = 0f;
        }

        void BackOnTheRoad()
        {
            _station.Give(_bay);
            _bay = -1;
            State = Mode.Cruising;
            _leg = Leg.Waiting;
            _tank = Random.Range(TankSeconds.x, TankSeconds.y);
            Spawn(_station.Lane, Mathf.Clamp(_station.KerbOutS, 2f, _station.Lane.Length - 2f));
        }

        // ------------------------------------------------------------------- the watch

        /// <summary>A leg that is getting nowhere. Every waiting branch calls it, and a
        /// car that is plainly still driving is not waiting on anything: the approach
        /// can be three hundred metres and a turn-round, which is minutes, and counting
        /// that as being stuck would give up on every customer at the far end.</summary>
        void Watch(float dt)
        {
            if (Mathf.Abs(Speed) > 0.5f) { _stuck = 0f; return; }
            _stuck += dt;
            if (_stuck > Patience) GiveUp(Doing);
        }

        /// <summary>Whatever went wrong, the car goes back on the road and the bay is
        /// handed back. Said out loud: this is the line an audit reads.</summary>
        void GiveUp(string why)
        {
            Debug.LogWarning($"[Fuel] {Plate} gave up: {why}");
            ClearParkingFailure();
            if (_station != null)
            {
                _station.Give(_bay);
                _station.DoneCrossing(this);
                _station.DoneApproaching(this);
                if (Driver != null) Driver.Show(false);
                Seated?.Show(true);
                Body?.CloseAllDoors();
                EngineOff = false;
                _bay = -1;
                State = Mode.Cruising;
                _leg = Leg.Waiting;
                _tank = Random.Range(TankSeconds.x, TankSeconds.y);
                if (!OnRoad && _station.Lane != null)
                    Spawn(_station.Lane, Mathf.Clamp(_station.KerbOutS, 2f, _station.Lane.Length - 2f));
            }
            _stuck = 0f;
            _approach = 0f;
            _waited = 0f;
            _gapWait = 0f;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
        }

        protected override void OnPlaced(float dt, float speed, float steerDegrees)
            => Body?.TickWheels(dt, speed, steerDegrees);

        /// <summary>At a junction, a driver looking for petrol takes the turning that
        /// gets him nearer the station. Everything else about him is the plain commuter
        /// (DemoVehicle) - he keeps his lane, follows, waits at the lights - and once he
        /// is on the station's own road there is nothing left to choose.</summary>
        protected override RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            if (State == Mode.Wanting && _station?.Route != null && CurrentEdge != null
                && _station.Route.TryGetValue(CurrentEdge, out var want) && want != null
                && (want == straight || lefts.Contains(want) || rights.Contains(want)))
                return want;
            return base.PickNext(straight, lefts, rights);
        }
    }
}
