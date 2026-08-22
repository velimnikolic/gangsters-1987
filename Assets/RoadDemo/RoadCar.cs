using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public enum Turn { Straight, Left, Right }

    /// <summary>
    /// A car on the lane network - every car: the traffic, the outfit's, the law's.
    /// It lives in its carriageway's frame, (s, d), and moves parametrically: s runs
    /// along the road at its speed, d follows a lateral profile (a smoothstep from
    /// where it was to where it is going - a lane change, a pull-in, a swing round
    /// something), junctions are crossed on a connector's polyline, and a turn in
    /// the road is an arc. Nothing here steers toward a point and hopes.
    ///
    /// Two rules keep the cars apart. ONE: the car's box of road - its body, and
    /// the stretch of whatever manoeuvre it is in the middle of - is entered in the
    /// carriageway's occupant list every frame, and its throttle answers to the
    /// nearest occupant ahead in the band it stands in or is moving into; nobody
    /// starts into a band (a lane change, the crown, the far lane, a turn-round, a
    /// reverse) without first finding it free of claims ahead, free of anyone
    /// behind who would have to brake hard, and free of anyone coming the other
    /// way within the driver's margin - and from that moment the band is claimed,
    /// so everyone else plans round it. TWO: a junction is entered only on a
    /// connector that crosses none in use, with room to leave it on the far side,
    /// and the car is in the box's list until its tail is out. Under both, the belt
    /// (RoadSpace) refuses the last few centimetres if the arithmetic ever lets two
    /// bodies meet - and counts it, because it should not happen.
    ///
    /// Who is driving is a DriverProfile: thresholds and permissions only.
    /// </summary>
    public class RoadCar : IRoadUser
    {
        public enum Manoeuvre { None, Pass, Crown, UTurn, PullIn, PullOut, Reverse, Aside }

        // ------------------------------------------------------------------ body

        public Transform Tf;
        public float HalfLen = 2.3f;
        public float HalfWide = 0.95f;
        /// <summary>Metres from the body's origin back to the rear axle: the point that
        /// follows the line, the front swinging into the bend ahead of it (a car steers
        /// with its front wheels). Unset: read as six tenths of the half length.</summary>
        public float AxleBack = float.NaN;
        float Axle => float.IsNaN(AxleBack) || AxleBack <= 0f ? HalfLen * 0.6f : Mathf.Min(AxleBack, HalfLen);
        /// <summary>The road surface's height: where the body sits.</summary>
        public float RoadY;
        public DriverProfile Profile = DriverProfile.Traffic;
        public LaneNet Net;

        static int _ids;
        public readonly int Id = ++_ids;

        /// <summary>How many times the belt had to refuse a step, all cars: a
        /// planning bug counter - it should read nought.</summary>
        public static int BeltHits;

        /// <summary>Stopped for good where it should not be - across a junction, the
        /// driver gone. It holds nothing against anybody: the street drives round it.</summary>
        public bool Derelict { get; private set; }

        /// <summary>Blown apart where it stood (CarShatter): the body is being torn into
        /// loose debris and the husk is off the network for good - it holds no lane, no
        /// box, and Tick does nothing for it. Unlike a wreck's cousin Derelict (a car
        /// that merely stopped and is driven round), a wreck is GONE from the model.</summary>
        public bool Wrecked { get; private set; }

        /// <summary>Take the car out of the traffic model, for good: off its lane, out
        /// of any junction box, speed nil, never ticked again. Called the instant a
        /// bomb goes off under it, before its shell is pulled to pieces - so nobody
        /// queues behind a car that is no longer there.</summary>
        public void Wreck()
        {
            if (Wrecked) return;
            Wrecked = true;
            Speed = 0f;
            Leave();
        }

        /// <summary>What held the car back this frame, for the overlay and the sim.</summary>
        public string Why = "";
        public string PassWhy = "";

        /// <summary>Held by a junction ahead - the light, the box - or stood behind
        /// somebody who is: a queue, not a jam; nobody goes round a queue.</summary>
        public bool InQueue { get; private set; }
        public static string LastBeltHit = "";

        protected DriverNerve _nerve;
        protected virtual bool Fearless => Profile.Fearless;

        // ------------------------------------------------------------------ where

        public Carriageway Road { get; private set; }
        public RoadEdge Lane { get; private set; }
        /// <summary>Along the carriageway (metres from its A end) and across it
        /// (metres right of its axis); the way the nose points along the axis.</summary>
        public float S { get; private set; }
        public float D { get; private set; }
        public int Heading { get; private set; } = 1;
        /// <summary>In a junction: the connector and the metres along it.</summary>
        public Connector Via { get; private set; }
        public float ViaS { get; private set; }
        /// <summary>Metres a second along the travel direction; negative in reverse.</summary>
        public float Speed { get; protected set; }
        /// <summary>Stood at the kerb, engine off, not in anyone's way in the lane.</summary>
        public bool Parked { get; private set; }
        public bool OnRoad => Road != null || Via != null;
        public Manoeuvre Doing => _man;

        /// <summary>Progress along the lane the car belongs to (for those that think
        /// in lanes: the patrol car's kerb, a spawn).</summary>
        public float Progress => Lane != null ? Lane.Progress(S) : 0f;
        public RoadEdge CurrentEdge => Lane;

        Vector3 _pos, _fwd = Vector3.forward;
        public Vector3 RoadPosition => _pos;
        public Vector3 RoadForward => _fwd;
        public float RoadSpeed => Mathf.Abs(Speed);
        public float HalfLength => HalfLen;
        public float HalfWidth => HalfWide;
        public Vector3 Position => _pos;
        public Vector3 Forward => _fwd;

        // ------------------------------------------------------------------ occupancy

        RoadOccupant _occ;       // on Road
        RoadOccupant _occNext;   // on the road beyond the junction while crossing it
        NodeOccupant _inNode;    // in a junction box
        RoadNode _nodeOf;        // whose box
        float _boxEntryS;        // road-s on the far road where we left the box
        bool _boxLeft;           // out of the box onto the far road, tail still to clear

        // ------------------------------------------------------------------ lateral profile

        float _dFrom, _dTo, _sFrom, _sLen;    // d runs dFrom -> dTo over sLen metres of travel from sFrom
        public bool Sliding => _sLen > 0f;
        float _viaD0;                          // lateral offset off the connector, eased out along it
        Connector _tailVia;                    // the connector just left, while the axle is still on it
        float _tailViaEndS;                    // the road s its end (the lane's start) is at

        // ------------------------------------------------------------------ route

        RoadEdge _next;
        Turn _turn;
        Connector _via;
        bool _committed;
        // Where we mean to come to rest this frame (road-s of the centre), or NaN.
        // Published on the band so whoever is behind brakes for the same place
        // rather than for our bumper - a queue at a red stops together that way.
        float _stopAt = float.NaN;
        // who the road says is in front of us this frame, and how far off - the one
        // thing a following fault cannot be read back from anything else
        int _leadId = -1; float _leadGap = -1f;
        // inside a box: the car on a crossing line we are standing for, and how
        // long we have stood - a standoff nothing else can end
        RoadCar _gaveWay; float _boxStuck;
        float _heldAtLine;
        /// <summary>The next lane after each lane toward wherever the car is
        /// going (LaneNet.RouteToward); null: the driver wanders.</summary>
        public Dictionary<RoadEdge, RoadEdge> Route;

        // the goal: a stop on a road
        bool _hasGoal;
        Carriageway _goalRoad;
        float _goalS, _goalD;
        int _goalHeading;
        bool _goalPark;
        bool _goalStop = true;   // brake to a stop at the goal (else just pass it and say so)
        RoadEdge _goalLane;
        bool _halted, _haltHard; // told to stop where it stands

        // ------------------------------------------------------------------ manoeuvre

        Manoeuvre _man;
        float _laneD;            // the lane's own lateral, what a swing returns to
        float _manD;             // the band being driven
        float _manPastS;         // road-s the thing being passed ends at (travel sense)
        float _claimS0 = 1f, _claimS1 = -1f, _claimD0, _claimD1; // the claim beyond the body (S0 > S1: none)
        RoadOccupant _blocker;
        float _blockedFor, _jammed, _standoffFor;
        IRoadUser _backedFor;
        IRoadUser _jamLeader;      // what the jam was behind: the far lane stays allowed against it
        float _backLeft;
        float _giveUntil, _yieldUntil;
        bool _asideDone;
        // the arc of a turn in the road
        float _arcS0, _arcR, _arcAng;
        int _arcSide;
        float _stuckFor;

        // ------------------------------------------------------------------ the black box

        /// <summary>Who this car is in the trace: traffic, crew, police, lorry.</summary>
        public string Tag = "car";
        float _want;              // the speed the throttle was asked for this frame
        bool _wantHard;           // and whether it was asked for with both feet
        float _prevSpeed, _nextSample, _quietFor, _saidStall;
        Vector3 _prevPos;
        Manoeuvre _prevMan;
        bool _prevVia, _prevParked, _traced;
        int _traceEvents;

        // ------------------------------------------------------------------ setup

        /// <summary>Onto this lane at this progress, stood in the lane.</summary>
        public void Spawn(RoadEdge lane, float progress)
        {
            Net ??= lane.Road?.Net ?? LaneNet.Shared;
            if (lane.Road == null) Net.Adopt(lane);
            Leave();
            Road = lane.Road;
            Heading = lane.Heading;
            S = lane.RoadS(progress);
            D = lane.Offset;
            _laneD = D;
            SetLane(lane);
            Speed = Mathf.Min(Cruise(), Road.SpeedLimit) * 0.5f;
            Parked = false;
            _occ = NewOccupant(Road);
            _lastPlaced = false;
            Place(0f);
        }

        /// <summary>Onto the road nearest this point, facing this way - stood where
        /// it is (parked if it stands at a kerb). False: no road there; the car is
        /// on open ground and drives straight lines (CrewCar's free mode).</summary>
        public bool PlaceAt(Vector3 pos, Vector3 fwd)
        {
            Leave();
            _pos = pos;
            fwd.y = 0f;
            _fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            if (Net == null) return false;
            var road = Net.Locate(pos, out float s, out float d, within: 3f);
            if (road == null) return false;
            Road = road;
            S = Mathf.Clamp(s, 0f, road.Length);
            D = d;
            Heading = Vector3.Dot(_fwd, road.Axis) >= 0f ? 1 : -1;
            var lane = road.LaneFor(Heading, d) ?? road.LaneFor(-Heading, d);
            if (lane != null && lane.Heading != Heading) Heading = lane.Heading;
            _laneD = lane != null ? lane.Offset : d;
            SetLane(lane);
            Speed = 0f;
            Parked = lane == null || Mathf.Abs(d) > Mathf.Abs(lane.Offset) + 0.9f;
            _occ = NewOccupant(Road);
            _lastPlaced = false;
            Place(0f);
            return true;
        }

        /// <summary>Off the network altogether (a patrol car swinging into its stall).
        /// The owner must not Tick it until the next Spawn/PlaceAt.</summary>
        public void Despawn() => Leave();

        void Leave()
        {
            if (_occ != null) { _occ.Road.Occupants.Remove(_occ); _occ = null; }
            if (_occNext != null) { _occNext.Road.Occupants.Remove(_occNext); _occNext = null; }
            LeaveBox();
            SetLane(null);
            Road = null;
            Via = null;
            _tailVia = null;
            _next = null;
            _via = null;
            _committed = false;
            _man = Manoeuvre.None;
            _sLen = 0f;
            ClearClaim();
        }

        void SetLane(RoadEdge lane)
        {
            if (Lane == lane) return;
            Lane?.Cars.Remove(this);
            Lane = lane;
            Lane?.Cars.Add(this);
        }

        RoadOccupant NewOccupant(Carriageway road)
        {
            var o = new RoadOccupant { Who = this, Car = this, Road = road, Priority = Profile.Priority };
            road.Occupants.Add(o);
            return o;
        }

        void LeaveBox()
        {
            if (_inNode != null && _nodeOf != null) _nodeOf.Inside.Remove(_inNode);
            _inNode = null;
            _nodeOf = null;
            _boxLeft = false;
        }

        /// <summary>The hold on the lane BEYOND a junction, given up.
        ///
        /// It is taken when the car commits to a crossing, so that the traffic on the
        /// far side keeps off the mouth we are coming out of, and it was only ever let
        /// go coming out the far side. Every other way of dropping the crossing - the
        /// car parks instead, the route is thought again, it turns round, it picks a
        /// different exit - left a car-shaped hole standing in that lane for the rest
        /// of the run: it belongs to a car that is no longer coming, so it never moves,
        /// and being nobody's parked car it does not read as one either, so a car in
        /// the junction waits behind it for ever and the whole quarter queues behind
        /// HIM. (168 seconds of that, in the run that found it: the crew parked its car
        /// at a kerb and left its hold on a lane two streets away.)</summary>
        void DropNext()
        {
            if (_occNext == null) return;
            _occNext.Road.Occupants.Remove(_occNext);
            _occNext = null;
        }

        float Cruise() => Profile.ObeysLimit && Road != null ? Mathf.Min(Profile.Cruise, Road.SpeedLimit) : Profile.Cruise;

        protected static float Allowed(float endSpeed, float dist, float brake)
            => Mathf.Sqrt(endSpeed * endSpeed + 2f * brake * Mathf.Max(0f, dist));

        /// <summary>The speed from which the profile's brake stops in this distance, or
        /// slows to endSpeed: the one curve every stop here is made on.</summary>
        protected float Allowed(float endSpeed, float dist) => Allowed(endSpeed, dist, Profile.Brake);

        /// <summary>The speed to hold behind something <paramref name="gap"/> metres
        /// ahead going <paramref name="vLead"/> our way: brake to its pace with the
        /// standing gap kept, and under the standing gap slower than it, to open the
        /// gap again - never merely its speed with no room.</summary>
        protected float Follow(float vLead, float gap, float leadSlowing = 0f)
        {
            vLead = Mathf.Max(0f, vLead);
            float room = gap - Profile.FollowGap;
            if (room <= 0f) return Mathf.Max(0f, vLead - (0.5f - room) * 2.5f);
            // What must be stopped short of is not where he IS but where he can COME TO
            // REST - while he is rolling, his own braking distance further on, which is
            // the ordinary give and take of a queue and the same arithmetic as before.
            // The moment he STANDS ON THE BRAKES that room collapses, and a follower who
            // is still only keeping his pace is inside him before he can do anything
            // about it: the barrier must move back the same instant his does, so we brake
            // WITH him instead of half a second after him. (A motorcycle five metres
            // behind a car that stopped dead at a junction line needed ninety metres a
            // second squared to stay off it; the belt refused the step.)
            float b = Mathf.Max(1f, leadSlowing > 0.1f ? leadSlowing : Profile.Brake);
            float his = vLead * vLead * 0.5f / b;
            float v = Allowed(0f, room + his);
            float byTime = vLead + room / Mathf.Max(0.2f, Profile.TimeGap);
            return Mathf.Min(v, Mathf.Max(vLead * 0.8f, byTime));
        }

        /// <summary>We mean to come to rest here (road-s of the centre) - the nearest
        /// such place this frame wins, and it is what everybody behind us brakes for.</summary>
        void MeanToStop(float s)
        {
            if (float.IsNaN(_stopAt)) _stopAt = s;
            else _stopAt = Heading > 0 ? Mathf.Min(_stopAt, s) : Mathf.Max(_stopAt, s);
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Stop at this point on the road: pulled in at the kerb on the side
        /// the point lies (<paramref name="park"/>), or in the lane (a cruiser at a
        /// scene). Off the road the nearest road is meant. Routes there; turns round
        /// in the road when the profile lets it and the spot is behind or across.</summary>
        public bool GoTo(Vector3 point, bool park, float standOff = 0f, bool stopAtGoal = true)
        {
            if (Net == null) return false;
            _parkTrying = 0f;
            _halted = false;
            _freeGoal = null;
            var road = Net.Locate(point, out float s, out float d, within: 12f);
            if (road == null)
            {
                var lane = Net.NearestLane(point, out float prog, 12f);
                if (lane == null) return false;
                road = lane.Road;
                s = lane.RoadS(prog);
                d = lane.Offset;
            }
            int heading = d >= 0f ? 1 : -1;                   // the kerb on that side is that way's
            if (road.LaneFor(heading, d) == null) heading = -heading;
            if (standOff > 0f) s -= heading * standOff;
            s = Mathf.Clamp(s, 6f, road.Length - 6f);
            float kerb = road.KerbD(heading, HalfWide);
            var goalLane = road.LaneFor(heading, d);
            if (goalLane == null) return false;
            _hasGoal = true;
            _goalRoad = road;
            _goalS = s;
            _goalHeading = heading;
            _goalD = park ? kerb : goalLane.Offset;
            _goalPark = park;
            _goalStop = stopAtGoal || park;
            _goalLane = goalLane;
            _spotFrom = float.NaN;
            _turnBackFor = 0f;
            if (park) ChooseKerbSpot();
            if (Parked) PullOut();
            Replan();
            return true;
        }

        float _spotFrom;      // road-s where the free kerb stretch the car is parking in begins (travel sense)
        float _spotCheck;

        // The free stretch of kerb nearest the spot the car was sent to, long enough
        // to stand in: the claims in the kerb band (cars parked there, a prop) leave
        // gaps; the nearest gap within reach has the spot moved into it. Nothing near:
        // the car will stop in the lane at the spot (that is as near as it gets).
        bool ChooseKerbSpot()
        {
            var road = _goalRoad;
            if (road == null || !_goalPark) return false;
            float kerb = road.KerbD(_goalHeading, HalfWide);
            int h = _goalHeading;
            float lo = kerb - HalfWide - 0.3f, hi = kerb + HalfWide + 0.3f;
            float need = 2f * HalfLen + 1.6f;
            var taken = _taken;
            taken.Clear();
            foreach (var o in road.Occupants)
            {
                if (ReferenceEquals(o.Who, this)) continue;
                if (!o.Overlaps(lo, hi)) continue;
                taken.Add(new Vector2(o.S0 - 0.5f, o.S1 + 0.5f));
            }
            float margin = 4f;
            taken.Add(new Vector2(-1000f, margin));
            taken.Add(new Vector2(road.Length - margin, road.Length + 1000f));
            taken.Sort((a, b) => a.x.CompareTo(b.x));
            // merge, and walk the gaps
            float bestDist = float.MaxValue, bestS = float.NaN, bestFrom = float.NaN;
            float end = taken[0].y;
            for (int i = 1; i < taken.Count; i++)
            {
                var t = taken[i];
                if (t.x > end)
                {
                    float a = end, b = t.x;
                    if (b - a >= need)
                    {
                        float centre = Mathf.Clamp(_goalS, a + HalfLen + 0.8f, b - HalfLen - 0.8f);
                        float dist = Mathf.Abs(centre - _goalS);
                        if (dist < bestDist) { bestDist = dist; bestS = centre; bestFrom = h > 0 ? a : b; }
                    }
                }
                end = Mathf.Max(end, t.y);
            }
            if (float.IsNaN(bestS) || bestDist > 45f)
            {
                _goalD = _goalLane != null ? _goalLane.Offset : kerb;  // no kerb to be had: the lane it is
                _spotFrom = float.NaN;
                return false;
            }
            _goalS = bestS;
            _goalD = kerb;
            _spotFrom = bestFrom;
            return true;
        }

        static readonly List<Vector2> _taken = new List<Vector2>();

        /// <summary>Nothing more to do: the goal dropped, the car drives on as the
        /// traffic does (a patrol car released from a call).</summary>
        public void Stop()
        {
            _hasGoal = false;
            Route = null;
            if (_man != Manoeuvre.UTurn) { _man = Manoeuvre.None; ClearClaim(); }
        }

        /// <summary>Stop where it stands and stay stopped - gently, or both feet on
        /// the brake - until the next order. Stood in the lane it is in everyone's
        /// way, and they go round it; that is the caller's choice.</summary>
        bool _haltWhenClear;

        public void Halt(bool hard)
        {
            // Never dead in the middle of a junction. The box is everybody else's road,
            // and a car left standing across one queues a whole quarter behind it - the
            // lab found six cars nose to tail for four minutes behind one whose driver
            // had been shot. Told to stop while crossing, the car finishes the crossing
            // (a few metres) and stops on the far side.
            if (Via != null)
            {
                _haltWhenClear = true;
                _haltHard = hard;
                _hasGoal = false;
                Route = null;
                _freeGoal = null;
                return;
            }
            _haltWhenClear = false;
            _hasGoal = false;
            Route = null;
            _freeGoal = null;
            _halted = true;
            _haltHard = hard;
            if (_man != Manoeuvre.UTurn && _man != Manoeuvre.Reverse) { _man = Manoeuvre.None; ClearClaim(); }
        }

        public bool Halted => _halted;

        /// <summary>Is the car where it was sent, stood still?</summary>
        public bool AtGoal => !_hasGoal && Mathf.Abs(Speed) < 0.05f;
        public bool HasGoal => _hasGoal;

        /// <summary>The goal reached and the car stopped: for a subclass to hear.</summary>
        protected virtual void OnArrived() { }

        // The route to the goal from wherever the car is now.
        float _retry;
        float _parkTrying;   // seconds spent looking for somewhere to pull in
        float _heldInBox;    // seconds held inside a junction by something stood beyond it

        float _turnBackFor;     // seconds spent looking for the turn-round on this road

        void Replan()
        {
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "replan", ManFields());
            Route = null;
            if (!_hasGoal || Road == null) return;
            if (Road == _goalRoad && Heading == _goalHeading && (_goalS - S) * Heading > -3f) return; // straight down this road
            // on the right road the wrong way round, or past the spot: the turn in the
            // road is the way back when the driver may make one - the route round the
            // block is only for failing that (TickRoad gives up on the turn near the junction)
            if (Road == _goalRoad && Road.TwoWay && Profile.UTurnsInRoad && Road.MedianHalf <= 0f && _turnBackFor < 12f) return;
            Route = Net.RouteToward(_goalLane);
            _next = null; // think the next turn over again
            _committed = false;
        }

        // ------------------------------------------------------------------ frame

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (Wrecked) return;   // a blown car drives nowhere and holds nothing
            WatchDerelict(dt);
            if (!OnRoad) { TickFree(dt); return; }
            if (Via != null) TickNode(dt);
            else TickRoad(dt);
        }

        bool _lastPlaced;
        float _derelictFor;

        /// <summary>A car that has stopped FOR GOOD with its nose in a junction - the
        /// driver shot, the crew out on the pavement, the plan torn up - still holds the
        /// box against everyone who would cross it, and a whole quarter queues behind a
        /// body nobody is coming back for. (A run of the lab found six cars nose to tail
        /// for four minutes behind one abandoned car.) A few seconds of that and the
        /// claim is given up: the wreck is still there to be driven round like any other
        /// stopped car, but the junction is a junction again.
        ///
        /// Only a car that was TOLD to stop, or parked - never one merely waiting its
        /// turn in a queue, which still means to go and must keep its place.</summary>
        void WatchDerelict(float dt)
        {
            bool derelict = Mathf.Abs(Speed) < 0.15f && _halted && !Parked;
            if (!derelict) { _derelictFor = 0f; Derelict = false; return; }
            _derelictFor += dt;
            if (_derelictFor < 6f) return;
            if (Derelict) return;
            Derelict = true;
            LeaveBox();
            DropNext();
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "gave up the box it had stopped in", ManFields());
        }

        void TickRoad(float dt)
        {
            var road = Road;
            _stopAt = float.NaN;
            UpdateOccupant();
            TickBoxExit();
            // and the same rule wherever else a car ends up off its line: doing nothing
            // in particular, it belongs on its lane, and it steers back onto it. A car
            // out in the middle of the street is invisible to the lane the traffic reads
            // and to the lines the junction is planned by.
            if (_man == Manoeuvre.None && !Sliding && !Parked && !_halted && Lane != null &&
                Mathf.Abs(D - _laneD) > 0.5f)
                Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Abs(Speed)));
            // Not crossing anything (the plan dropped, or stood at a kerb): whatever was
            // held on the far side of a junction goes back. Here, where every way of
            // giving up a crossing ends, rather than at each of them.
            if (!_committed || Parked) DropNext();
            if (Parked)
            {
                Speed = 0f;
                Place(dt);
                return;
            }

            // LOOKING TOO LONG for somewhere to pull in. Every time the spot is thought
            // over it can move on a little - somebody took it, somebody else pulled out
            // - and a car can follow that up the street for ever; worse, a spot on
            // another street it never manages to reach keeps it driving with its crew
            // sat waiting to get out (two minutes of it, in the lab, twice). A driver
            // gives up looking and takes the kerb he is beside. Counted wherever the car
            // is, not only on the street the spot is on - it is the not-arriving that
            // has to be caught.
            if (_hasGoal && _goalPark && _man == Manoeuvre.None)
            {
                _parkTrying += dt;
                if (_parkTrying > 25f)
                {
                    // far enough on to actually PULL IN: a spot three metres ahead
                    // leaves no room to come off the lane, so the car stops where it
                    // stands - in the running lane - and everything behind it queues
                    // (156 seconds of it, in the run that found this).
                    float room = PullInLength() + 4f;
                    // and NOT at the mouth of the street: a car left standing where the
                    // traffic comes out of a junction stops everything crossing it (the
                    // run that found this queued four cars for nearly three minutes)
                    const float offJunction = 22f;
                    float lo = Mathf.Min(offJunction, road.Length * 0.4f);
                    float hi = Mathf.Max(road.Length - offJunction, road.Length * 0.6f);
                    float want = S + Heading * room;
                    // AHEAD, or not at all. Clamping the spot into this street's usable
                    // stretch can put it BEHIND a car that is already near the end of the
                    // street - and a spot behind is a lap of the block to reach, at which
                    // point the driver gives up again, picks another spot behind him, and
                    // drives the quarter with his crew sat in the back for the rest of the
                    // run (a crew ordered out on to the pavement rode round for a minute
                    // and the job timed out with them still in their seats). No room on
                    // this street: keep going and take the next one, a few seconds on.
                    if (Heading > 0 ? want > hi : want < lo)
                    {
                        _parkTrying = 20f;
                        if (DriveTrace.On)
                            DriveTrace.Event("man", "car " + Id, "no kerb ahead on this street - trying the next", ManFields());
                    }
                    else
                    {
                        if (DriveTrace.On)
                            DriveTrace.Event("man", "car " + Id, $"gave up looking - taking the kerb at s={Mathf.Clamp(want, lo, hi):F0}", ManFields());
                        _parkTrying = 0f;   // and if this kerb cannot be had either, try again
                        _goalRoad = road;
                        _goalHeading = Heading;
                        _goalLane = Lane;
                        _goalS = Mathf.Clamp(want, lo, hi);
                        _goalD = road.KerbDOnSide(D, HalfWide);
                        _goalStop = true;
                        Route = null;
                        _next = null;
                        _committed = false;
                    }
                }
            }

            // out of the lane ahead - the junction (or the end of the road)
            var node = road.NodeAhead(Heading);
            if (_next == null && node != null && _man != Manoeuvre.UTurn) PlanNext(node);

            // THE CLAIM MUST SAY WHERE WE ARE ACTUALLY GOING. It names the way through
            // the box, and every other driver plans against that name: a car whose line
            // crosses ours waits, one whose line does not comes on. Think the turn again
            // after taking the claim - the route redrawn, an exit that would not clear,
            // the kerb given up on - and the claim still names the OLD way through, so
            // the cars whose path crosses the NEW one are told there is nothing in their
            // way. Two left turns were let into the same boulevard box that way, sharing
            // nine metres of the same line, and the belt was the only thing between them.
            // And a claim on a box we are no longer going through is nobody's to hold.
            if (_inNode != null && Via == null)
            {
                // turning round in the road ends the crossing, tail or no tail: the car
                // is not going that way any more, and its body is the road's business now
                if (Parked || _man == Manoeuvre.UTurn) LeaveBox();
                else if (!_boxLeft)
                {
                    if (_nodeOf != node) LeaveBox();
                    else if (_via != null) _inNode.Via = _via;
                }
            }

            float noseS = S + Heading * HalfLen;
            float tailS = S - Heading * HalfLen;
            float endS = road.EndS(Heading);
            float toEnd = (endS - noseS) * Heading;             // nose to the box edge

            // ---- the throttle
            float v = Cruise();
            // BOLTING FROM GUNFIRE lifts the CEILING, and nothing else. A frightened
            // driver drives faster than he otherwise would - past the limit, over the
            // cruise - but he does not drive into the back of the car in front, and this
            // used to: the lift was put on at the END of the throttle, on top of the room
            // he was keeping to the man ahead, so a bolting car closed thirteen metres a
            // second onto a queue standing at a red and the belt was the only thing
            // between them. Put on here, everything ahead still clamps it.
            if (!Fearless && _nerve.Bolting && !_nerve.Approaching) v *= 1.3f;
            if (Sliding) v = Mathf.Min(v, LateralCap(_sLen, Mathf.Abs(_dTo - _dFrom)));
            bool hard = false;

            // the right road, the wrong way: turn round here as soon as the sweep is clear
            // (slowed right down for it); only near the junction, or after long enough,
            // is the long way round taken instead
            if (_hasGoal && road == _goalRoad && Heading != _goalHeading && _man != Manoeuvre.UTurn && Route == null &&
                road.TwoWay && Profile.UTurnsInRoad && road.MedianHalf <= 0f)
            {
                _turnBackFor += dt;
                v = Mathf.Min(v, Profile.UTurnSpeed + 2f);
                _retry -= dt;
                if (_retry <= 0f && _man == Manoeuvre.None)
                {
                    _retry = 0.3f;
                    if (!TryUTurn() && (toEnd < 22f || _turnBackFor > 12f)) { _turnBackFor = 99f; Replan(); }
                }
            }
            else _turnBackFor = 0f;

            // the goal on this road
            if (_hasGoal && road == _goalRoad && Heading == _goalHeading && _man != Manoeuvre.UTurn)
            {
                float toGoal = (_goalS - S) * Heading;
                if (toGoal < -3f)
                {
                    // overshot it: round, if the road lets us, else the long way. The
                    // throttle comes down first - a turn is refused above the speed its
                    // arc can carry, so charging at it only means never being allowed one.
                    bool mayTurn = Profile.UTurnsInRoad && road.TwoWay && road.MedianHalf <= 0f;
                    if (mayTurn && _man == Manoeuvre.None) v = Mathf.Min(v, Profile.UTurnSpeed + 2f);
                    _retry -= dt;
                    if (_man == Manoeuvre.None && _retry <= 0f)
                    {
                        _retry = 0.5f;
                        // still braking for it: ask again in a moment rather than give up
                        // on the turn and send the car round the block
                        if (!mayTurn || Mathf.Abs(Speed) <= Profile.UTurnSpeed + 2.5f)
                            if (!TryUTurn() && Route == null) Replan();
                    }
                }
                else
                {
                    // the spot looked at again on the way in: somebody may have taken it
                    if (_goalPark && _man == Manoeuvre.None && toGoal < 80f)
                    {
                        _spotCheck -= dt;
                        if (_spotCheck <= 0f) { _spotCheck = 0.7f; ChooseKerbSpot(); toGoal = (_goalS - S) * Heading; }
                    }
                    if (_goalStop) v = Mathf.Min(v, Allowed(0f, toGoal));
                    if (_goalPark && _man == Manoeuvre.None && toGoal < PullInLength() + 4f && toGoal > 0f && Mathf.Abs(_goalD - D) > 0.3f) BeginPullIn();
                    if (!_goalStop && toGoal <= 0f)
                    {
                        // past the mark on the move: done, and the next order may come at once
                        _hasGoal = false;
                        Route = null;
                        OnArrived();
                    }
                    else if (toGoal <= 0.25f && Mathf.Abs(Speed) < 0.3f)
                    {
                        Speed = 0f;
                        _hasGoal = false;
                        Route = null;
                        if (_goalPark) { Parked = true; _man = Manoeuvre.None; _sLen = 0f; D = _goalD; ClearClaim(); LeaveBox(); DropNext(); }
                        OnArrived();
                        UpdateOccupant();
                        Place(dt);
                        return;
                    }
                }
            }

            // the junction ahead
            bool heldByNode = false;
            if (node == null)
            {
                v = Mathf.Min(v, Allowed(0f, toEnd - 0.5f));
                MeanToStop(S + Heading * (toEnd - 0.5f));
                if (toEnd < 1f && Mathf.Abs(Speed) < 0.1f && Profile.UTurnsInRoad && road.TwoWay && _man == Manoeuvre.None) TryUTurn();
            }
            else if (_man != Manoeuvre.UTurn)
            {
                float stopDist = toEnd - node.StopSetback;
                if (_next == null) { v = Mathf.Min(v, Allowed(0f, stopDist)); MeanToStop(S + Heading * stopDist); }
                else
                {
                    float turnV = _via != null && _via.UTurn ? Profile.UTurnSpeed
                        : _turn == Turn.Straight ? Mathf.Min(Cruise(), Profile.ObeysLimit ? _next.SpeedLimit : Profile.Cruise) : TurnSpeed(_via);
                    v = Mathf.Min(v, Allowed(turnV, toEnd));
                    if (!_committed)
                    {
                        // the decision is made where the car can still stop: from there on
                        // it is in the box's list, and everybody plans round it
                        float commitAt = Mathf.Max(1.6f, Speed * Speed / (2f * Profile.Brake) + 1.0f);
                        bool may = CanEnter(node, stopDist);
                        if (!may)
                        {
                            v = Mathf.Min(v, Allowed(0f, stopDist));
                            MeanToStop(S + Heading * stopDist);
                            _heldAtLine += dt;
                            heldByNode = true;
                            // held on a full exit long enough: pick another way out of
                            // this junction (a wanderer only - a car on a route has
                            // somewhere to be and waits its turn)
                            if (_exitFull != null && Route == null)
                            {
                                _fullFor += dt;
                                if (_fullFor > 5f) { _fullFor = 0f; PlanNext(node); _exitFull = null; }
                            }
                            else _fullFor = 0f;
                            // late: both feet on the brake; past the point of no return even
                            // so - in we go, and on the list, so everybody plans round us
                            if (stopDist < Speed * Speed / (2f * Profile.Brake)) hard = true;
                            if (stopDist < Speed * Speed / (2f * Profile.HardBrake) - 0.3f && Speed > 1f) EnterBox(node);
                        }
                        else
                        {
                            _heldAtLine = 0f;
                            _exitFull = null;
                            _fullFor = 0f;
                            if (stopDist <= commitAt) EnterBox(node);
                        }
                    }
                }
            }

            // through the box ahead: whoever is on our way across it, or just out of it
            if (node != null && _via != null && toEnd < 40f && _man != Manoeuvre.UTurn)
            {
                if (_committed && _occNext != null) RefreshNextOccupant(-(toEnd + _via.Length) - HalfLen);
                float vb = BoxFollow(node, v);
                if (vb < v) { v = vb; if (v < 0.5f) Why = "box: following ahead"; }
            }

            // whoever is ahead in the band we stand in or are moving into
            float d0 = BandLo(), d1 = BandHi();
            var leader = road.Ahead(_occ, Heading, noseS, tailS, d0, d1, out float gap);
            _leadId = leader == null ? -1 : leader.Car != null ? leader.Car.Id : -2;
            _leadGap = leader == null ? -1f : gap;
            float vLead = 0f;
            if (leader != null)
            {
                vLead = Mathf.Max(0f, leader.Vel * Heading);
                if (leader.Vel * Heading < -0.5f) vLead = 0f;  // coming at us
                // he is on us, in the band we are sliding INTO and not against our body: the
                // slide is off (somebody took the gap); the thing we are sliding away from is
                // no reason to stop alongside it
                bool inTarget = Sliding && leader.Overlaps(_dTo - HalfWide - SideAir, _dTo + HalfWide + SideAir);
                bool onBody = leader.BodyOverlaps(BodyLo() - SideAir, BodyHi() + SideAir);
                if (gap < 0.5f && Sliding && inTarget && !onBody && _man != Manoeuvre.UTurn) AbortLateral();
                else if (Sliding && !inTarget && vLead < 0.5f && _man == Manoeuvre.Pass)
                {
                    // what we are sliding away from: no standing gap wanted off it, only
                    // the slide finished before we reach it (the slide was laid so)
                    v = Mathf.Min(v, Allowed(0f, gap - 0.4f));
                }
                else v = Mathf.Min(v, Follow(vLead, gap, leader.Slowing));
                // AND WHERE HE MEANS TO STOP, if he means to. Braking for his bumper is
                // braking for a thing that is still moving away: at a red light he begins
                // his stop before we begin ours - he is nearer the line - and those three
                // tenths of a second are the whole gap between us (two cars nose to tail
                // at eleven metres a second, and the belt had to separate them). Braking
                // for the PLACE he is going to stand puts the whole queue on the brakes
                // at once, and it says nothing at all on an open road.
                if (!float.IsNaN(leader.StopAt))
                {
                    float behind = leader.StopAt -
                                   Heading * (leader.Length * 0.5f + Profile.FollowGap + HalfLen);
                    float vq = Allowed(0f, (behind - S) * Heading);
                    if (vq < v) { v = vq; if (v < 0.5f) Why = DriveTrace.On ? "queue: behind car " + (leader.Car != null ? leader.Car.Id.ToString() : "?") : "queue"; }
                    MeanToStop(behind);
                }
                // something stood at the kerb or dead in the road that we mean to go round:
                // held back far enough that the swing out is still possible from here
                bool queue = leader.Car != null && leader.Car.InQueue && !Profile.RunsRed;
                bool roundIt = leader.Parked || leader.Car == null || (Profile.Patience <= 1f && !queue);
                if (roundIt && !leader.Moving && !IsOurParkingSpot(leader) && !Sliding && _man == Manoeuvre.None)
                    v = Mathf.Min(v, Allowed(0f, gap - (leader.Parked ? KerbHoldBack : PassHoldBack)));
            }
            _blocker = leader;
            InQueue = heldByNode || (leader != null && leader.Car != null && leader.Car.InQueue && !leader.Moving);
            // the full reason is a trace string and nothing but the trace reads it (the
            // sim only ever tests Why for "red"/"yellow", which are set as constants
            // elsewhere), so it is built only when the trace is open - untraced play was
            // allocating one of these a frame for every car in a queue, the bulk of the
            // crowd's garbage and the ten-second GC hitch it fed
            if (leader != null && v < 0.5f)
                Why = DriveTrace.On
                    ? "behind " + (leader.Car != null ? "car " + leader.Car.Id : "static") + $" gap {gap:F1} band[{d0:F1},{d1:F1}] his[{leader.D0:F1},{leader.D1:F1}] s[{leader.S0:F1},{leader.S1:F1}] me s={S:F1}"
                    : "behind";

            // people in the road
            v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
            v = Mathf.Min(v, BodiesAhead(dt));

            // gunfire
            if (!Fearless)
            {
                float cap = _nerve.Limit(_pos, _fwd, Profile.Brake, out hard);
                v = Mathf.Min(v, cap);
            }

            // giving way: stood aside, or dead still
            if (Time.time < _giveUntil) v = Mathf.Min(v, _man == Manoeuvre.Aside ? 2.5f : 0f);
            // waiting at the kerb for a gap to pull out into
            if (_man == Manoeuvre.PullOut && _pullOutWanted) v = 0f;
            // told to stop where it stands
            if (_halted)
            {
                v = 0f;
                if (_haltHard) hard = true;
                if (Mathf.Abs(Speed) < 0.05f && Lane != null && Mathf.Abs(Mathf.Abs(D) - Mathf.Abs(Lane.Offset)) > 0.9f) Parked = true;
            }
            // stood back from something, waiting for the way round to open
            if (_holdFor > 0f) { _holdFor -= dt; if (_man == Manoeuvre.None) v = 0f; }

            v = LimitTarget(v);

            // ---- the driver's tactics: round what is stopped, the crown, the turn, the reverse
            Decide(dt, leader, gap, vLead, node, toEnd);
            if (Road == null) return; // the decision took us off the road (should not)

            // ---- move
            if (_man == Manoeuvre.Reverse) { TickReverse(dt); Place(dt); return; }
            if (_man == Manoeuvre.UTurn) { TickArc(dt, v); Place(dt); return; }

            float rate = v < Speed ? (hard || v <= 0.01f ? Profile.HardBrake : Profile.Brake) : Profile.Accel;
            if (v < Speed && v > 0.01f && !hard) rate = Profile.Brake;
            _want = v; _wantHard = hard;
            Speed = Mathf.MoveTowards(Speed, Mathf.Max(0f, v), rate * dt);
            float step = Speed * dt;
            float prevS = S, prevD = D;
            S += Heading * step;
            D = LateralAt(S);

            // over the end of the road into the box
            bool atEnd = (S - endS) * Heading >= -0.01f;
            if (atEnd && _next != null && _committed && node != null)
            {
                float overshoot = Mathf.Max(0f, (S - endS) * Heading);
                EnterNode(node, overshoot);
                Place(dt);
                return;
            }
            if (atEnd)
            {
                // the end with nowhere to go: stood at it
                S = endS - Heading * 0.01f;
                Speed = 0f;
            }
            Place(dt, prevS, prevD);
        }

        // A step that the belt (RoadSpace) may cut short: the car never enters another.
        void Place(float dt, float prevS = float.NaN, float prevD = float.NaN)
        {
            Vector3 pos, fwd;
            Pose(out pos, out fwd);
            float steer = 0f;
            if (_lastPlaced && dt > 0f)
            {
                var to = pos;
                var moved = RoadSpace.Advance(this, _pos, to, fwd, HalfLen, HalfWide, out var hit);
                if (hit != null)
                {
                    BeltHits++;
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Int(sb, "id", Id);
                        DriveTrace.Str(sb, "tag", Tag);
                        DriveTrace.Str(sb, "hit", hit is RoadCar bc ? "car " + bc.Id : "static");
                        DriveTrace.Num(sb, "v", Speed);
                        DriveTrace.Str(sb, "why", Why);
                        DriveTrace.Vec(sb, "p", pos);
                        DriveTrace.Row("belt", sb.ToString());
                    }
                    // a write-only debug field (nothing reads it): built only when the
                    // trace is open, or every one of the thousands of belt hits a run
                    // sees was assembling this whole string for no reader
                    if (DriveTrace.On)
                        LastBeltHit = $"car {Id} {Describe()} v={Speed:F1} fwd={fwd} hit {(hit is RoadCar hc ? "car " + hc.Id + " " + hc.Describe() + " v=" + hc.Speed.ToString("F1") : "static at " + hit.RoadPosition + " fwd " + hit.RoadForward + " hl " + hit.HalfLength + " hw " + hit.HalfWidth)} at {pos} from {_pos} sliding={Sliding} slope={(Sliding ? LateralSlope(S) : 0f):F2}";
                    // stood where we were: the arithmetic let two bodies meet, the belt did not
                    if (!float.IsNaN(prevS)) { S = prevS; D = prevD; Pose(out pos, out fwd); }
                    else pos = moved;
                    Speed = 0f;
                    _stuckFor += dt;
                }
                float yawRate = Vector3.SignedAngle(_fwd, fwd, Vector3.up) * Mathf.Deg2Rad / Mathf.Max(dt, 1e-3f);
                steer = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.6f * yawRate / Mathf.Max(Mathf.Abs(Speed), 1f)), -35f, 35f);
            }
            if (DriveTrace.On) TraceStep(dt, pos, steer);
            _pos = pos;
            _fwd = fwd;
            _lastPlaced = true;
            if (Tf != null) Tf.SetPositionAndRotation(new Vector3(pos.x, RoadY + SurfaceLift(), pos.z), Quaternion.LookRotation(fwd, Vector3.up));
            OnPlaced(dt, Speed, steer);
        }

        /// <summary>How much higher than the road level the surface under the car
        /// stands: the carriageway's own - which on a slip road or a freeway's run down
        /// off its pillars CLIMBS along the road, so it is asked where the car actually
        /// is - or, in a junction, eased from the end of the road it came off to the
        /// start of the one it is going onto.</summary>
        float SurfaceLift()
        {
            if (Road != null) return Road.SurfaceOn(S);
            var via = Via;
            if (via == null) return 0f;
            // the height at the end of the lane we came off, and at the start of the
            // one we are joining: a lane runs A to B (heading +1) or B to A (-1)
            float a = EndLift(via.From, leaving: true), b = EndLift(via.To, leaving: false);
            if (Mathf.Approximately(a, b)) return a;
            float t = via.Length > 0.01f ? Mathf.Clamp01(ViaS / via.Length) : 1f;
            return Mathf.Lerp(a, b, t);
        }

        static float EndLift(RoadEdge lane, bool leaving)
        {
            var road = lane?.Road;
            if (road == null) return 0f;
            // leaving: the far end of that lane; joining: its near end
            bool atB = leaving == (lane.Heading > 0);
            return atB ? road.SurfaceB : road.SurfaceA;
        }

        /// <summary>The wheels, the doors - a subclass with a body animates it here.</summary>
        protected virtual void OnPlaced(float dt, float speed, float steerDegrees) { }

        /// <summary>A derived driver's last word on the target speed.</summary>
        protected virtual float LimitTarget(float target) => target;

        // The body's pose from the line it is on. The REAR AXLE is the point that
        // follows the line - where it is, which way the line runs there - and the
        // body's centre rides the axle's length ahead of it along the heading: the nose
        // points into a bend before the body has moved across, the tail follows, the
        // way a front-steered car goes (no crabbing sideways into a lane).
        void Pose(out Vector3 pos, out Vector3 fwd)
        {
            float a = Axle;
            if (Via != null)
            {
                Vector3 axle;
                float sa = ViaS - a;
                if (sa >= 0f) Via.Pose(sa, out axle, out fwd);
                else
                {
                    // the axle still on the lane behind the box
                    axle = Via.Pts[0] + Via.From.Dir * sa;
                    fwd = Via.From.Dir;
                }
                pos = axle + fwd * a;
                if (Mathf.Abs(_viaD0) > 0.01f)
                {
                    float t = Mathf.Clamp01(ViaS / Mathf.Max(1f, Via.Length * 0.6f));
                    var right = Vector3.Cross(Vector3.up, fwd);
                    pos += right * (_viaD0 * (1f - Mathf.SmoothStep(0f, 1f, t)));
                }
                return;
            }
            if (_man == Manoeuvre.UTurn)
            {
                // the axle on the arc about (arcS0, 0), a little behind the body's angle
                float angBack = _arcAng - a / _arcR;
                Vector3 axle;
                if (angBack >= 0f)
                {
                    axle = Road.Pose(_arcS0 + _arcHeading0 * _arcR * Mathf.Sin(angBack), _arcSide * _arcR * Mathf.Cos(angBack));
                    var tan = Road.Axis * (_arcHeading0 * Mathf.Cos(angBack)) + Road.Right * (-_arcSide * Mathf.Sin(angBack));
                    fwd = tan.normalized;
                }
                else
                {
                    // not yet into the arc: the axle on the straight behind its start
                    axle = Road.Pose(_arcS0 + _arcHeading0 * _arcR * angBack, _arcSide * _arcR);
                    fwd = Road.Axis * _arcHeading0;
                }
                pos = axle + fwd * a;
                return;
            }
            {
                float sa = S - Heading * a;
                if (_tailVia != null)
                {
                    // just out of a box: the axle still on the connector behind the lane's
                    // start (the body turns out of the corner, it does not snap straight)
                    float behind = (_tailViaEndS - sa) * Heading;
                    if (behind > 0f && behind <= _tailVia.Length)
                    {
                        _tailVia.Pose(_tailVia.Length - behind, out var axle, out fwd);
                        pos = axle + fwd * a;
                        return;
                    }
                    _tailVia = null;
                }
                float da = LateralValue(sa);
                // the lateral slope is per metre travelled; the heading turns toward the
                // side the line moves to, whichever way the road's axis runs
                float slope = Sliding ? LateralSlope(sa) : 0f;
                var f = Road.Axis * Heading + Road.Right * slope;
                fwd = f.normalized;
                pos = Road.Pose(sa, da) + fwd * a;
            }
        }

        // The lateral position the line has at s, read without moving anything.
        float LateralValue(float s)
        {
            if (!Sliding) return D;
            float p = (s - _sFrom) * Heading / _sLen;
            if (p >= 1f) return _dTo;
            if (p <= 0f) return _dFrom;
            return Mathf.Lerp(_dFrom, _dTo, Mathf.SmoothStep(0f, 1f, p));
        }

        // ------------------------------------------------------------------ lateral

        void Slide(float toD, float length)
        {
            _dFrom = D;
            _dTo = toD;
            _sFrom = S;
            _sLen = Mathf.Max(2f, length);
        }

        float LateralAt(float s)
        {
            if (!Sliding) return D;
            float p = (s - _sFrom) * Heading / _sLen;
            // the slide is over when the AXLE is past its end, not the body's centre: the
            // axle follows the curve and would otherwise jump to the line at the end
            if (p >= 1f + Axle / _sLen) { _sLen = 0f; return _dTo; }
            if (p >= 1f) return _dTo;
            if (p <= 0f) return _dFrom;
            return Mathf.Lerp(_dFrom, _dTo, Mathf.SmoothStep(0f, 1f, p));
        }

        float LateralSlope(float s)
        {
            if (!Sliding) return 0f;
            float p = Mathf.Clamp01((s - _sFrom) * Heading / _sLen);
            return (_dTo - _dFrom) * 6f * p * (1f - p) / _sLen;
        }

        // The speed a smoothstep slide of this length and lateral reach takes at the
        // profile's lateral acceleration: peak curvature is 6*dd/L^2.
        float LateralCap(float len, float dd)
        {
            if (dd < 0.05f) return float.MaxValue;
            return len * Mathf.Sqrt(Profile.LateralG / (6f * dd));
        }

        // And the length that slide needs to be taken at this speed.
        float SlideLength(float dd, float speed)
        {
            float len = speed * Mathf.Sqrt(6f * dd / Profile.LateralG);
            // no shorter than the turning circle lets: peak curvature 6dd/L^2 <= 1/2.2
            float byCircle = Mathf.Sqrt(6f * 2.2f * dd);
            return Mathf.Clamp(len, Mathf.Max(3f, byCircle), 40f);
        }

        float PullInLength() => Mathf.Clamp(Mathf.Abs(Speed) * 1.6f + 8f, 10f, 26f);

        // the band the car's body covers, and the band it covers together with its plan
        float BodyLo() => D - HalfWide;
        float BodyHi() => D + HalfWide;
        const float SideAir = 0.3f;   // metres of air the car wants off anything it passes
        float PassHoldBack => SlideLength(5f, 0f) + 2f;       // metres kept back from something stood in the lane: room to swing right across
        float KerbHoldBack => SlideLength(1.2f, 0f) + 2.5f;   // and from a car at the kerb: room for the little swing round it
        float BandLo() => (Sliding ? Mathf.Min(D, _dTo) : D) - HalfWide - SideAir;
        float BandHi() => (Sliding ? Mathf.Max(D, _dTo) : D) + HalfWide + SideAir;

        void AbortLateral()
        {
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "abort " + _man, ManFields());
            if (_man == Manoeuvre.PullIn || _man == Manoeuvre.PullOut) return;
            _man = Manoeuvre.None;
            ClearClaim();
            // GIVING UP A PASS MEANS COMING BACK IN. Only a car still crossing over was
            // brought back before, and one that had already ARRIVED on its passing line
            // was simply left there: manoeuvre over, three metres off its lane, driving
            // down a part of the street where no lane is, for the rest of the run. It
            // reaches the junction alongside a car that is properly in the lane, both
            // are let into the box on lines that do not cross, and the one in the lane
            // turns straight into it - two cars locked together for 154 seconds, and six
            // thousand refused steps, in the run that found this.
            if (Sliding && Mathf.Abs(D - _dFrom) < 0.4f) { _sLen = 0f; D = _dFrom; }
            else if (Mathf.Abs(D - _laneD) > 0.3f) Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Abs(Speed)));
            _yieldUntil = Time.time + 2.5f;
        }

        // ------------------------------------------------------------------ occupancy

        void UpdateOccupant()
        {
            if (_occ == null || Road == null) return;
            var o = _occ;
            float slope = Sliding ? LateralSlope(S) : 0f;
            float ang = Mathf.Atan(Mathf.Abs(slope));
            float along = Mathf.Cos(ang) * HalfLen + Mathf.Sin(ang) * HalfWide;
            float across = Mathf.Sin(ang) * HalfLen + Mathf.Cos(ang) * HalfWide;
            float s = S, d = D;
            if (_man == Manoeuvre.UTurn)
            {
                s = _arcS0 + _arcHeading0 * _arcR * Mathf.Sin(_arcAng);
                d = _arcSide * _arcR * Mathf.Cos(_arcAng);
                along = across = Mathf.Max(HalfLen, HalfWide);
            }
            o.BodyS0 = s - along; o.BodyS1 = s + along;
            o.BodyD0 = d - across; o.BodyD1 = d + across;
            o.S0 = o.BodyS0; o.S1 = o.BodyS1; o.D0 = o.BodyD0; o.D1 = o.BodyD1;
            if (_claimS1 > _claimS0)
            {
                o.S0 = Mathf.Min(o.S0, _claimS0); o.S1 = Mathf.Max(o.S1, _claimS1);
                o.D0 = Mathf.Min(o.D0, _claimD0); o.D1 = Mathf.Max(o.D1, _claimD1);
            }
            if (Sliding)
            {
                o.D0 = Mathf.Min(o.D0, Mathf.Min(D, _dTo) - HalfWide);
                o.D1 = Mathf.Max(o.D1, Mathf.Max(D, _dTo) + HalfWide);
                float slideEnd = _sFrom + Heading * _sLen;
                o.S0 = Mathf.Min(o.S0, Mathf.Min(S, slideEnd) - HalfLen);
                o.S1 = Mathf.Max(o.S1, Mathf.Max(S, slideEnd) + HalfLen);
            }
            o.Vel = Speed * Heading;
            o.Slowing = _want < Speed - 0.05f ? (_wantHard ? Profile.HardBrake : Profile.Brake) : 0f;
            o.StopAt = Parked || Derelict ? S : _stopAt;
            o.Heading = _man == Manoeuvre.UTurn ? 0 : Heading;
            // A car nobody is coming back for reads to the street as a PARKED one, which
            // is what it is: everybody plans round it instead of queueing behind it for
            // ever. Without this a crew shot in its car left eight minutes of traffic
            // nose to tail behind a body in the lane.
            o.Parked = Parked || Derelict;
            o.Priority = Profile.Priority;
        }

        void Claim(float s0, float s1, float d0, float d1)
        {
            _claimS0 = Mathf.Min(s0, s1); _claimS1 = Mathf.Max(s0, s1);
            _claimD0 = Mathf.Min(d0, d1); _claimD1 = Mathf.Max(d0, d1);
            UpdateOccupant();
        }

        void ClearClaim()
        {
            _claimS0 = 1f; _claimS1 = -1f;
            if (_occ != null) UpdateOccupant();
        }

        // Is this band free to move into over this stretch ahead: nobody's claim on it
        // ahead within `needAhead`, nobody alongside in it, nobody behind in it who
        // would have to brake hard for us, nobody coming down it within the margin.
        bool BandFree(float dLo, float dHi, float needAhead, float seconds, out float freeAhead, bool allowParkedBeyond = false)
        {
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            freeAhead = Road.FreeAhead(_occ, Heading, noseS, tailS, dLo, dHi, needAhead + 40f, allowParkedBeyond);
            if (freeAhead < needAhead) return false;
            var behind = Road.Behind(_occ, Heading, tailS, dLo, dHi, out float gapBehind);
            if (behind != null && behind.Car != null && !behind.Parked)
            {
                float vb = behind.Vel * Heading;
                if (vb > 0.5f)
                {
                    float closing = vb - Mathf.Max(0f, Speed);
                    float need = Profile.FollowGap + vb * 0.8f + (closing > 0f ? closing * closing / (2f * 3f) : 0f);
                    if (gapBehind < need) return false;
                }
                else if (gapBehind < 0.5f) return false;
            }
            float farS = noseS + Heading * needAhead;
            if (Road.OncomingWithin(_occ, Heading, noseS, farS, dLo, dHi, seconds, Mathf.Abs(Speed))) return false;
            return true;
        }

        // ------------------------------------------------------------------ the tactics

        void Decide(float dt, RoadOccupant leader, float gap, float vLead, RoadNode node, float toEnd)
        {
            float now = Time.time;
            bool stopped = Mathf.Abs(Speed) < 0.4f;

            // behind something that is not moving (or, hot, crawling)
            float slowUnder = Profile.Patience <= 0f ? Cruise() * 0.6f : 2.5f;
            bool blocked = leader != null && !leader.Parked && vLead < slowUnder && leader.Vel * Heading > -0.5f &&
                           (gap < Profile.FollowGap + Mathf.Abs(Speed) * Mathf.Abs(Speed) / (2f * Profile.Brake) + 6f ||
                            (_holdFor > 0f && gap < 16f));
            bool behindParked = leader != null && leader.Parked && gap < Profile.FollowGap + Mathf.Abs(Speed) * 1.5f + 8f;
            bool headOn = leader != null && leader.Vel * Heading < -0.5f && gap < 12f;
            bool standoff = leader != null && stopped && !leader.Moving && leader.Car != null && gap < 6f &&
                            (leader.Heading != Heading) && !leader.Parked;
            _blockedFor = blocked ? _blockedFor + dt : 0f;
            _standoffFor = standoff ? _standoffFor + dt : 0f;
            bool jam = stopped && leader != null && !leader.Moving && gap < PassHoldBack + 1.5f && !_nerve.Frightened &&
                       !InQueue && toEnd > 30f;
            _jammed = jam ? _jammed + dt : 0f;
            _stuckFor = stopped && leader != null && gap < PassHoldBack + 1.5f ? _stuckFor + dt : 0f;

            switch (_man)
            {
                case Manoeuvre.None:
                    if (Parked) return;
                    // nose to nose with a car that is not going anywhere either: the lower
                    // priority gives way - pulls over to his kerb where it is free, or holds
                    // dead still - and the other comes through
                    if (standoff && _standoffFor > Profile.StandoffPatience)
                    {
                        _standoffFor = 0f;
                        bool iYield = leader.Priority < Profile.Priority ? false
                            : leader.Priority > Profile.Priority ? true
                            : (leader.Car != null && leader.Car.Id < Id);
                        if (iYield && Profile.GivesWay)
                        {
                            _giveUntil = now + Profile.GiveWayFor;
                            _jammed = 0f;
                            if (!TryAside()) { /* dead still where we stand */ }
                            return;
                        }
                        if (!iYield && Profile.Reverses && stopped && _stuckFor > 2.5f)
                        {
                            // he cannot move; back off and find a way round him
                            if (TryReverse(leader)) return;
                        }
                    }
                    if (now < _giveUntil) return;

                    // gunfire ahead and a way out: round and away (the traffic's escape)
                    if (!Fearless && _nerve.Approaching && Mathf.Abs(Speed) < 5f && Road.TwoWay && node != null && toEnd > 20f)
                    {
                        if (TryUTurn(escape: true)) { _nerve.SlowUntil = 0f; _nerve.BoltUntil = now + 8f; return; }
                    }

                    // the crown between the lanes, with the guns out: a queue in our lane and
                    // the crown open - drive it for as long as it is open
                    if (Profile.UsesCrown && Profile.Patience <= 0f && LaneBusyAhead(45f) && now >= _yieldUntil)
                    {
                        if (TryCrown()) return;
                    }

                    // a car at the kerb ahead sticking into our lane: swing round it, a
                    // little over the crown when nothing is coming
                    if (behindParked && Profile.PassesAtKerb && now >= _yieldUntil && !IsOurParkingSpot(leader))
                    {
                        if (TryPass(leader, kerbOnly: true)) return;
                        // too close behind it to swing out: a few metres back first
                        if (Profile.Reverses && stopped && gap < 4f && !ReferenceEquals(_backedFor, leader.Who) && TryReverse(leader, gap)) return;
                    }

                    // something stopped in the lane: round it through whatever the profile
                    // allows, after the patience; wedged too close to swing - back off first
                    if (blocked && _blockedFor > Profile.Patience && now >= _yieldUntil && !IsOurParkingSpot(leader) &&
                        (!InQueue || Profile.RunsRed))
                    {
                        if (TryPass(leader, kerbOnly: false, desperate: ReferenceEquals(_jamLeader, leader.Who))) return;
                        if (Profile.Reverses && stopped && gap < 5f && !ReferenceEquals(_backedFor, leader.Who) && TryReverse(leader, gap)) return;
                    }

                    // a jam that does not clear: the traffic uses the far lane when it is
                    // empty, and failing that turns round
                    if (_jammed > 5f && now >= _yieldUntil)
                    {
                        _jammed = 0f;
                        _jamLeader = leader.Who;
                        if (TryPass(leader, kerbOnly: false, desperate: true)) return;
                        if (Profile.Reverses && stopped && gap < 5f && TryReverse(leader, gap)) return;
                        if (Road.TwoWay && toEnd > 12f && TryUTurn(escape: true)) return;
                    }
                    break;

                case Manoeuvre.Pass:
                case Manoeuvre.Crown:
                    TickPass(dt, leader, gap, node, toEnd);
                    break;

                case Manoeuvre.Aside:
                    if (!Sliding && _claimS1 > _claimS0) ClearClaim();   // stood aside: the lane is theirs
                    if (now >= _giveUntil && !Sliding)
                    {
                        // back into the lane once it is free behind and beside us
                        float lo = _laneD - HalfWide - 0.3f, hi = _laneD + HalfWide + 0.3f;
                        if (BandFree(lo, hi, 10f, 2.5f, out _))
                        {
                            _man = Manoeuvre.None;
                            ClearClaim();
                            Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Max(Speed, 3f)));
                        }
                    }
                    break;

                case Manoeuvre.PullOut:
                    if (_pullOutWanted) TickPullOut();
                    else if (!Sliding) { _man = Manoeuvre.None; ClearClaim(); }
                    // A SLIDE ONLY HAPPENS WHILE THE THING IS MOVING - it is measured in
                    // metres of road travelled, not in seconds. So a vehicle that got its
                    // gap, began to pull out, and then stopped for any reason is Sliding
                    // for ever: the slide cannot finish, the manoeuvre is never dropped,
                    // and because the manoeuvre stands it may not pass the junction line
                    // either (CanEnter), which is what keeps it stopped. The crew's
                    // motorcycle sat in that knot for eight minutes of one monkey run.
                    // Nothing is going to break it from the inside; break it from here.
                    else if (Mathf.Abs(Speed) < 0.2f && now - _pullOutAsked > PullOutGiveUp)
                    {
                        DriveTrace.Event("man", "car " + Id, "gave up a slide that was not moving", ManFields());
                        _sLen = 0f;
                        _man = Manoeuvre.None;
                        ClearClaim();
                    }
                    break;

                case Manoeuvre.PullIn:
                    if (!Sliding) { _man = Manoeuvre.None; ClearClaim(); }
                    break;
            }
        }

        // Is the thing ahead stood at the kerb where we mean to pull in ourselves?
        // Then it is not gone round: we stop behind it, and that is where we park.
        bool IsOurParkingSpot(RoadOccupant o)
        {
            if (!_hasGoal || !_goalPark || Road != _goalRoad || Heading != _goalHeading || o == null || !o.Parked) return false;
            float oEnd = (Heading > 0 ? o.S1 : o.S0);
            return (oEnd - _goalS) * Heading > -8f;
        }

        // Something stood or crawling in our lane within sight, or a car going our way
        // we will be on the bumper of within a few seconds.
        bool LaneBusyAhead(float look)
        {
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float lo = _laneD - HalfWide, hi = _laneD + HalfWide;
            var o = Road.Ahead(_occ, Heading, noseS, tailS, lo, hi, out float gap);
            if (o == null || gap > look) return false;
            float his = o.Vel * Heading;
            if (his < -0.5f) return false;
            if (his < 2.5f) return true;
            float closing = Cruise() - his;
            return closing > 0.5f && gap / closing < 3f;
        }

        // The way round what is stopped ahead: the bands the profile allows, nearest
        // our line first - our own lane shifted, the crown, the far lane - the first
        // that is free from here to past it, with the margin off oncoming; then the
        // slide out, the run past, and the return laid as one claim.
        bool TryPass(RoadOccupant blocker, bool kerbOnly, bool desperate = false)
        {
            if (blocker == null || Road == null) return false;
            float noseS = S + Heading * HalfLen;
            float bFar = Heading > 0 ? blocker.S1 : blocker.S0;
            float bNear = Heading > 0 ? blocker.S0 : blocker.S1;
            float past = (bFar - noseS) * Heading + HalfLen * 2f + 3f;     // metres of travel to be clear of it
            if (past < 0f) return false;
            if (blocker.Moving) past += Mathf.Abs(blocker.Vel) * 3f;
            var node = Road.NodeAhead(Heading);
            float toEnd = (Road.EndS(Heading) - noseS) * Heading;
            float room = node != null ? toEnd - node.StopSetback - 2f : toEnd - 2f;
            float needBack = SlideLength(1.0f, Mathf.Max(Speed, 4f));     // the least shift's return; per candidate below
            if (past + needBack > room) { PassWhy = $"no room: past {past:F1} back {needBack:F1} room {room:F1}"; return false; }
            PassWhy = "";

            float w = HalfWide + 0.3f;
            // candidate lateral positions, nearest our lane first
            float crownSide = Mathf.Sign(_laneD);                          // our side of the axis
            var cands = _cands;
            cands.Clear();
            // 1. the least shift that clears him, staying mostly in our lane (a parked car)
            float clearD = crownSide > 0f ? blocker.D0 - w - 0.5f : blocker.D1 + w + 0.5f;
            float minOut = crownSide > 0f ? Mathf.Min(clearD, _laneD) : Mathf.Max(clearD, _laneD);
            bool overCrown = crownSide > 0f ? minOut - HalfWide < 0f : minOut + HalfWide > 0f;
            float over = crownSide > 0f ? -(minOut - HalfWide) : (minOut + HalfWide);
            if (!overCrown || over <= Profile.OverCrown) cands.Add(minOut);
            if (!kerbOnly || desperate)
            {
                if (Profile.UsesCrown) cands.Add(crownSide * Mathf.Min(HalfWide * 0.5f, 0.6f));
                if (Profile.UsesCrown) cands.Add(0f);
                if (Profile.UsesOpposite || desperate)
                {
                    var opp = Road.LaneFor(-Heading, -_laneD);
                    if (opp != null) cands.Add(opp.Offset);
                }
                // the kerb side of him, if he stands out in the road
                float kerbSide = crownSide * (Road.HalfRoad - HalfWide - 0.4f);
                cands.Add(kerbSide);
            }
            foreach (float cand in cands)
            {
                if (!Road.Drivable(cand, HalfWide)) { PassWhy += $"cand {cand:F1}: not drivable; "; continue; }
                needBack = SlideLength(Mathf.Abs(cand - _laneD), Mathf.Max(Speed, 4f));
                if (past + needBack > room) { PassWhy += $"cand {cand:F1}: no room for the return; "; continue; }
                float lo = cand - w, hi = cand + w;
                // the band must clear the blocker himself
                if (blocker.Overlaps(lo, hi)) { PassWhy += $"cand {cand:F1}: blocker in band; "; continue; }
                float tOccupy = (past + needBack) / Mathf.Max(Mathf.Abs(Speed), 5f) + 1f;
                bool opposite = Mathf.Sign(cand) != crownSide && Mathf.Abs(cand) > 0.8f;
                float margin = opposite ? Profile.OncomingMargin : Profile.OncomingMargin * 0.6f;
                if (!BandFree(lo, hi, past + needBack + 2f, tOccupy + margin, out float fa)) { PassWhy += $"cand {cand:F1}: band not free (free {fa:F1} need {past + needBack + 2f:F1}, {tOccupy + margin:F1}s); "; continue; }
                // the slide out must be done before the first thing in what it sweeps
                float sweepLo = Mathf.Min(D, cand) - w, sweepHi = Mathf.Max(D, cand) + w;
                float swept = Road.FreeAhead(_occ, Heading, noseS, S - Heading * HalfLen, sweepLo, sweepHi, past);
                float outLen = SlideLength(Mathf.Abs(cand - D), Mathf.Max(Mathf.Abs(Speed), 3f));
                float outMin = SlideLength(Mathf.Abs(cand - D), 0f);     // the turning circle's least
                if (swept < 0f) { PassWhy += $"cand {cand:F1}: sweep blocked alongside; "; continue; }
                // the slide out must be complete a length and a half before what it sweeps past
                if (Mathf.Abs(cand - D) > 0.3f && swept < outMin + 1.5f) { PassWhy += $"cand {cand:F1}: swept {swept:F1} < {outMin + 1.5f:F1}; "; continue; }
                outLen = Mathf.Min(outLen, Mathf.Max(outMin, swept - 1.5f));
                // go
                _jamLeader = null;
                _man = Manoeuvre.Pass;
                _manD = cand;
                _manPastS = bFar + Heading * (HalfLen * 2f + 3f);
                Slide(cand, outLen);
                float farS = noseS + Heading * (past + needBack + 2f);
                Claim(S - Heading * HalfLen, farS, Mathf.Min(lo, _laneD - HalfWide), Mathf.Max(hi, _laneD + HalfWide));
                _blockedFor = 0f;
                return true;
            }
            return false;
        }

        static readonly List<float> _cands = new List<float>();

        // The crown between the lanes, held while the lane is busy and the crown open.
        bool TryCrown()
        {
            float cand = Mathf.Sign(_laneD) * Mathf.Min(HalfWide * 0.5f, 0.6f);
            if (!Road.Drivable(cand, HalfWide)) return false;
            float w = HalfWide + 0.3f;
            float need = 25f;
            if (!BandFree(cand - w, cand + w, need, Profile.OncomingMargin, out _)) return false;
            _man = Manoeuvre.Crown;
            _manD = cand;
            Slide(cand, SlideLength(Mathf.Abs(cand - D), Mathf.Max(Mathf.Abs(Speed), 5f)));
            float noseS = S + Heading * HalfLen;
            Claim(S - Heading * HalfLen, noseS + Heading * need, Mathf.Min(cand - w, _laneD - HalfWide), Mathf.Max(cand + w, _laneD + HalfWide));
            return true;
        }

        // On the pass / the crown: keep the claim rolling ahead of us, and come back
        // into the lane once it is clear ahead and we are past what we went round.
        void TickPass(float dt, RoadOccupant leader, float gap, RoadNode node, float toEnd)
        {
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float w = HalfWide + 0.3f;
            float laneLo = _laneD - HalfWide, laneHi = _laneD + HalfWide;
            float laneFree = Road.FreeAhead(_occ, Heading, noseS, tailS, laneLo, laneHi, 60f);
            float bandFree = Road.FreeAhead(_occ, Heading, noseS, tailS, _manD - w, _manD + w, 60f);
            bool pastIt = _man == Manoeuvre.Crown || (_manPastS - noseS) * Heading <= 0f;
            float backLen = SlideLength(Mathf.Abs(_manD - _laneD), Mathf.Max(Mathf.Abs(Speed), 3f));
            float backMin = SlideLength(Mathf.Abs(_manD - _laneD), 3f);     // slowed right down
            float room = toEnd - (node != null ? node.StopSetback : 0f) - 2f;
            bool mustReturn = room < backMin + 6f;
            if (room < backLen + 4f) backLen = Mathf.Max(backMin, room - 4f);  // a shorter, slower return fits
            bool laneClear = laneFree >= backLen + 6f || laneFree >= 60f;
            bool wantBack = (pastIt && laneClear) || mustReturn ||
                            (_man == Manoeuvre.Crown && (bandFree < 12f && laneFree > bandFree));
            if (_man == Manoeuvre.Crown && !wantBack && !LaneBusyAhead(45f) && laneFree > 30f) wantBack = true;

            if (!Sliding && !wantBack)
            {
                // roll the claim on ahead, re-checking the oncoming each time
                float ahead = Mathf.Min(bandFree, 30f);
                float farS = noseS + Heading * Mathf.Max(8f, ahead);
                float seconds = Mathf.Max(8f, ahead) / Mathf.Max(Mathf.Abs(Speed), 5f) + Profile.OncomingMargin;
                bool opposite = Mathf.Sign(_manD) != Mathf.Sign(_laneD) && Mathf.Abs(_manD) > 0.8f;
                if (opposite && Road.OncomingWithin(_occ, Heading, noseS, farS, _manD - w, _manD + w, seconds, Mathf.Abs(Speed)))
                {
                    // somebody coming down it: back into our lane if we can, else hold the claim short
                    if (laneFree >= backLen + 2f) wantBack = true;
                    else Claim(tailS, noseS + Heading * Mathf.Max(2f, Mathf.Min(ahead, gap)), Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
                }
                else Claim(tailS, farS, Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
            }
            if (wantBack && !Sliding && Mathf.Abs(D - _laneD) > 0.2f)
            {
                if (laneFree >= Mathf.Min(backLen, 6f) || mustReturn)
                {
                    Slide(_laneD, Mathf.Min(backLen, Mathf.Max(4f, laneFree - 1f)));
                    Claim(tailS, noseS + Heading * (backLen + 2f), Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
                }
            }
            if (!Sliding && Mathf.Abs(D - _laneD) <= 0.2f)
            {
                _man = Manoeuvre.None;
                ClearClaim();
            }
        }

        // Over to our own kerb and a stand there: the whole road left to whoever is
        // coming through. Only with the kerb ahead empty.
        bool TryAside()
        {
            float kerb = Road.KerbDOnSide(_laneD, HalfWide);
            float w = HalfWide + 0.2f;
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float free = Road.FreeAhead(_occ, Heading, noseS, tailS, kerb - w, kerb + w, 20f);
            if (free < 12f) return false;
            _man = Manoeuvre.Aside;
            _manD = kerb;
            Slide(kerb, 9f);
            Claim(tailS, noseS + Heading * 12f, Mathf.Min(kerb - w, _laneD - HalfWide), Mathf.Max(kerb + w, _laneD + HalfWide));
            return true;
        }

        // ------------------------------------------------------------------ the turn in the road

        int _arcHeading0;

        /// <summary>Turn round inside the carriageway, here or as soon as the sweep is
        /// clear: the arc from this side to the mirror lane, claimed whole, only when
        /// nothing stands on it and nothing is coming down either band in time.</summary>
        public bool TryUTurn(bool escape = false)
        {
            if (Road == null || !Road.TwoWay || Via != null || _man == Manoeuvre.UTurn) return false;
            if (!Profile.UTurnsInRoad && !escape) return false;
            if (Road.MedianHalf > 0f) return false;
            float r = Mathf.Clamp(Mathf.Abs(D), 2.2f, Road.HalfRoad - HalfWide - 0.45f);
            int side = D >= 0f ? 1 : -1;
            if (Mathf.Abs(Mathf.Abs(D) - r) > 0.3f)
            {
                // not on a radius we can turn from: over to it first, and ask again
                if (!Sliding && Road.Drivable(side * r, HalfWide)) Slide(side * r, 8f);
                return false;
            }
            // The arc is a couple of metres across. Taken at cruising speed it is a
            // pirouette - half a turn in half a second, the body slewing round on the
            // spot - which is what a car doing it at fifteen metres a second looked
            // like. The driver slows FOR the turn (TickRoad holds the throttle down
            // while it means to make one); until he has, the answer is no.
            float arcSpeed = Mathf.Min(Profile.UTurnSpeed, Mathf.Sqrt(Profile.LateralG * r));
            if (Mathf.Abs(Speed) > arcSpeed + 1.5f) return false;

            float sweepLo = -r - HalfWide - 0.3f, sweepHi = r + HalfWide + 0.3f;
            float s0 = S + Heading * 1f;
            float sweepS0 = Mathf.Min(s0 - Heading * HalfLen, s0 + Heading * (r + HalfLen + 0.5f));
            float sweepS1 = Mathf.Max(s0 - Heading * HalfLen, s0 + Heading * (r + HalfLen + 0.5f));
            var node = Road.NodeAhead(Heading);
            float endS = Road.EndS(Heading);
            if ((endS - (s0 + Heading * (r + HalfLen + 1f))) * Heading < (node != null ? node.StopSetback : 0f)) return false;
            if (Road.Busy(_occ, sweepS0, sweepS1, sweepLo, sweepHi)) return false;
            float seconds = Mathf.PI * r / Mathf.Max(1f, Profile.UTurnSpeed) + Profile.OncomingMargin;
            // coming down our band, or down the other one toward where we end up
            if (Road.OncomingWithin(_occ, Heading, S + Heading * HalfLen, sweepS1, sweepLo, sweepHi, seconds, Mathf.Abs(Speed))) return false;
            if (Road.OncomingWithin(_occ, -Heading, s0 - Heading * HalfLen, Heading > 0 ? sweepS0 : sweepS1, sweepLo, sweepHi, seconds, 0f)) return false;
            // anyone behind us in the far band who would run into the sweep
            var behind = Road.Behind(_occ, -Heading, Heading > 0 ? sweepS1 : sweepS0, -side * r - HalfWide, -side * r + HalfWide, out float gb);
            if (behind != null && behind.Moving && gb < Mathf.Abs(behind.Vel) * seconds) return false;
            _man = Manoeuvre.UTurn;
            _tailVia = null;
            _arcS0 = s0;
            _arcR = r;
            _arcSide = side;
            _arcAng = 0f;
            _arcHeading0 = Heading;
            _sLen = 0f;
            Claim(sweepS0, sweepS1, sweepLo, sweepHi);
            _next = null;
            _via = null;
            _committed = false;
            return true;
        }

        void TickArc(float dt, float vCap)
        {
            float v = Mathf.Min(vCap, Profile.UTurnSpeed, Mathf.Sqrt(Profile.LateralG * _arcR));
            // a man in the sweep, a car that rolled into it: the belt and the walkers already cap v
            _want = v; _wantHard = false;
            Speed = Mathf.MoveTowards(Speed, v, (v < Speed ? Profile.Brake : Profile.Accel) * dt);
            _arcAng += Speed * dt / _arcR;
            if (_arcAng >= Mathf.PI)
            {
                _arcAng = Mathf.PI;
                Heading = -_arcHeading0;
                S = _arcS0;
                D = -_arcSide * _arcR;
                _man = Manoeuvre.None;
                ClearClaim();
                var lane = Road.LaneFor(Heading, D);
                SetLane(lane);
                _laneD = lane != null ? lane.Offset : D;
                if (Mathf.Abs(D - _laneD) > 0.2f) Slide(_laneD, 10f);
                _next = null;
                _via = null;
                _committed = false;
                if (_hasGoal) Replan();
            }
        }

        // ------------------------------------------------------------------ reverse

        bool TryReverse(RoadOccupant blocker, float gap = 0f)
        {
            if (!Profile.Reverses || Via != null) return false;
            float room = ClearBehind();
            if (room < 3f) return false;
            _backedFor = blocker?.Who;
            _jamLeader = blocker?.Who;
            // far enough back to swing right across the road if need be
            float wanted = Mathf.Clamp(SlideLength(5f, 0f) + 2.5f - Mathf.Max(0f, gap), 4f, 10f);
            _backLeft = Mathf.Min(wanted, room - 0.5f);
            _man = Manoeuvre.Reverse;
            Speed = 0f;
            float tailS = S - Heading * HalfLen;
            Claim(tailS, tailS - Heading * _backLeft, D - HalfWide, D + HalfWide);
            return true;
        }

        // Metres of free road straight behind the rear bumper, to ten.
        float ClearBehind()
        {
            float tailS = S - Heading * HalfLen;
            var o = Road.Behind(_occ, Heading, tailS, D - HalfWide - 0.2f, D + HalfWide + 0.2f, out float gap);
            float free = o != null ? Mathf.Min(10f, gap) : 10f;
            // the end of the road behind us
            float back = (tailS - Road.EndS(-Heading)) * Heading;
            free = Mathf.Min(free, Mathf.Max(0f, back - 0.5f));
            var backDir = -_fwd;
            for (int list = 0; list < 2; list++)
                foreach (var body in list == 0 ? Behind(StreetTraffic.Bodies) : StreetTraffic.Walkers)
                {
                    var d = body - _pos;
                    d.y = 0f;
                    float behind = Vector3.Dot(d, backDir) - HalfLen;
                    if (behind < -0.5f || behind > free) continue;
                    if (Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, _fwd))) > HalfWide + 0.6f) continue;
                    free = Mathf.Min(free, Mathf.Max(0f, behind - 1f));
                }
            return free;
        }

        // Backing up gives way to everybody, whatever the fight: a man behind a car is
        // not in its way, he is under its boot, and nothing about a gunfight makes
        // reversing over somebody a manoeuvre.
        static readonly List<Vector3> _behind = new List<Vector3>();

        static List<Vector3> Behind(List<StreetTraffic.Body> bodies)
        {
            _behind.Clear();
            for (int i = 0; i < bodies.Count; i++) _behind.Add(bodies[i].At);
            return _behind;
        }

        void TickReverse(float dt)
        {
            float room = ClearBehind();
            float step = 0f;
            if (room > 0.6f && _backLeft > 0.01f)
            {
                Speed = Mathf.MoveTowards(Speed, -2.5f, Profile.Accel * dt);
                step = Mathf.Min(-Speed * dt, Mathf.Min(_backLeft, room - 0.5f));
                S -= Heading * step;
                _backLeft -= step;
            }
            else _backLeft = 0f;
            if (_backLeft > 0.01f && step > 0f) return;
            _backLeft = 0f;
            Speed = 0f;
            _man = Manoeuvre.None;
            ClearClaim();
            _blockedFor = Profile.Patience + 1f;  // and another look from here at once
            _holdFor = 6f;                        // stood here looking for the gap, not creeping back up
        }

        float _holdFor;

        // ------------------------------------------------------------------ parking

        void BeginPullIn()
        {
            _man = Manoeuvre.PullIn;
            float len = Mathf.Min(PullInLength(), (_goalS - S) * Heading - 1f);
            // the slide starts past whatever stands at the kerb before the spot
            if (!float.IsNaN(_spotFrom)) len = Mathf.Min(len, (_goalS - _spotFrom) * Heading - HalfLen - 0.3f);
            len = Mathf.Max(4f, len);
            Slide(_goalD, len);
            float w = HalfWide + 0.2f;
            Claim(S - Heading * HalfLen, _goalS + Heading * HalfLen, Mathf.Min(_goalD - w, _laneD - HalfWide), Mathf.Max(_goalD + w, _laneD + HalfWide));
        }

        /// <summary>Out of the kerb into the lane, when the lane is free behind.</summary>
        public void PullOut()
        {
            _halted = false;
            if (Road == null) { Parked = false; return; }
            Parked = false;
            var lane = Road.LaneFor(Heading, D);
            if (lane == null) return;
            SetLane(lane);
            _laneD = lane.Offset;
            if (Mathf.Abs(D - _laneD) < 0.3f) return;
            _man = Manoeuvre.PullOut;
            _pullOutWanted = true;
            _pullOutAsked = Time.time;
        }

        bool _pullOutWanted;
        float _pullOutAsked;

        /// <summary>How long a car waits for a clear lane before it stops counting the
        /// PARKED as a reason to sit there. Long enough that it takes a real gap when
        /// one is coming, short enough that it is not a wait a player watches.</summary>
        const float PullOutPatience = 6f;

        /// <summary>And how long before he stops asking and simply goes.</summary>
        const float PullOutGiveUp = 20f;

        // ------------------------------------------------------------------ junctions

        void PlanNext(RoadNode node)
        {
            if (Net != null) Net.Prepare(node);
            RoadEdge straight = null;
            var lefts = _lefts; var rights = _rights;
            lefts.Clear(); rights.Clear();
            var lane = Lane;
            if (lane == null) { lane = Road.LaneFor(Heading, D); SetLane(lane); }
            if (lane == null) return;
            for (int i = 0; i < node.Outgoing.Count; i++)
            {
                var e = node.Outgoing[i];
                if (node.ConnectorFor(lane, e) == null) continue;
                float dot = Vector3.Dot(e.Dir, lane.Dir);
                if (dot > 0.5f)
                {
                    if (straight == null || (e.Start - lane.End).sqrMagnitude < (straight.Start - lane.End).sqrMagnitude) straight = e;
                }
                else if (dot < -0.5f) { /* the dead-end turn-round: taken only when nothing else */ }
                else if (Vector3.Cross(lane.Dir, e.Dir).y > 0f) rights.Add(e);
                else lefts.Add(e);
            }
            // The way on he had chosen is full and has been for a while: a man with
            // nowhere particular to be turns instead. Junctions gridlock when four
            // queues all wait on each other's full exits, and one car choosing another
            // way out is what breaks the ring - which is what a driver does anyway.
            if (_exitFull != null && Route == null)
            {
                if (straight == _exitFull) straight = null;
                lefts.Remove(_exitFull);
                rights.Remove(_exitFull);
            }
            // The route is a table of "from this lane, take that one". A car that comes
            // off the table - a turn taken to get round something stopped, a junction
            // whose exit was full, a set-down that put it on a street the table never
            // covered - would go on picking at random with its goal still set: the lab
            // watched one drive a quarter of the city for two minutes and never come
            // within forty metres of where it had been sent. Off the table, the table is
            // drawn again from where the car actually is.
            if (_hasGoal && Route != null && _goalLane != null && Net != null && !Route.ContainsKey(lane))
                Route = Net.RouteToward(_goalLane);

            RoadEdge next = null;
            if (Route != null && Route.TryGetValue(lane, out var toward) && toward != null && node.ConnectorFor(lane, toward) != null) next = toward;
            else if (straight != null || lefts.Count > 0 || rights.Count > 0) next = PickNext(straight, lefts, rights);
            if (next == null)
            {
                // the turn-round at a dead end, or anything at all
                foreach (var c in node.Connectors) if (c.From == lane) { next = c.To; break; }
            }
            _next = next;
            if (next == null) return;
            _via = node.ConnectorFor(lane, next);
            _turn = _via != null ? _via.Kind : Turn.Straight;
            _committed = false;
            _heldAtLine = 0f;
        }

        /// <summary>The exit that was refused for want of room on the far side.</summary>
        RoadEdge _exitFull;
        float _fullFor;

        static readonly List<RoadEdge> _lefts = new List<RoadEdge>(), _rights = new List<RoadEdge>();

        /// <summary>The default driver: random wander biased to straight, then right;
        /// frightened, whichever way leads farthest from the shooting. A derived
        /// driver substitutes a routed choice.</summary>
        protected virtual RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            if (!Fearless && _nerve.Frightened)
            {
                RoadEdge best = null;
                float bestD = -1f;
                void Consider(RoadEdge e)
                {
                    if (e == null) return;
                    float d = (e.End - _nerve.Threat).sqrMagnitude;
                    if (d > bestD) { bestD = d; best = e; }
                }
                Consider(straight);
                for (int i = 0; i < lefts.Count; i++) Consider(lefts[i]);
                for (int i = 0; i < rights.Count; i++) Consider(rights[i]);
                if (best != null) return best;
            }
            float roll = Random.value;
            if (straight != null && (roll < 0.55f || (lefts.Count == 0 && rights.Count == 0))) return straight;
            if (rights.Count > 0 && (roll < 0.8f || lefts.Count == 0)) return rights[Random.Range(0, rights.Count)];
            if (lefts.Count > 0) return lefts[Random.Range(0, lefts.Count)];
            return straight;
        }

        // May we go into the box now: the signal, the connectors in use, the room to
        // leave it on the far side.
        bool CanEnter(RoadNode node, float stopDist)
        {
            if (_via == null || _next == null) { Why = "no way on"; return false; }
            // the lane we are leaving from: the connector wants us in it
            if (!Sliding && Mathf.Abs(D - _laneD) > 1.2f && _man != Manoeuvre.None) { Why = "off lane at the line"; return false; }
            var sig = node.Signal;
            if (sig != null)
            {
                bool green = sig.GreenFor(Lane.NorthSouth);
                bool yellow = sig.YellowFor(Lane.NorthSouth);
                if (green)
                {
                    if (_turn == Turn.Left && OncomingPriority(node)) { Why = "left: oncoming"; return false; }
                }
                else if (yellow)
                {
                    if (stopDist > Speed * Speed / (2f * Profile.Brake) + 2f) { Why = "yellow"; return false; }
                }
                else
                {
                    if (!Profile.RunsRed) { Why = "red"; return false; }
                    if (ConflictApproaching(node, 3f)) { Why = "red: traffic"; return false; }
                }
            }
            else
            {
                // no lights: give way to what is already coming at the box on a crossing path
                if (_turn == Turn.Left && OncomingPriority(node)) { Why = "left: oncoming"; return false; }
            }
            // the connectors in use
            for (int i = 0; i < node.Inside.Count; i++)
            {
                var o = node.Inside[i];
                if (o.Car == this) continue;
                if (o.Via == _via || o.Via.From == _via.From)
                {
                    // following him across: room behind him (he may still be on the approach)
                    if (FollowBody(o.Car, _fwd, Vector3.Cross(Vector3.up, _fwd)) < 1f) { Why = "box: following " + o.Car.Id; return false; }
                    continue;
                }
                if (_via.Conflicts[o.Via.Index]) { Why = "box: crossing " + o.Car.Id; return false; }
            }
            // room to leave the box: the far lane's start clear for our length and a gap
            var far = _next.Road;
            float farS0 = _next.S0;
            float need = 2f * HalfLen + Profile.FollowGap + 0.5f;
            float free = far.FreeAhead(null, _next.Heading, farS0, farS0 - _next.Heading * 0.1f,
                _next.Offset - HalfWide - 0.2f, _next.Offset + HalfWide + 0.2f, need + 1f);
            if (free >= 0f && free < need && !(_heldAtLine > 12f && free > 2f * HalfLen))
            {
                Why = "box: no room beyond";
                _exitFull = _next;   // the way on is full: a wanderer takes another
                return false;
            }
            Why = "";
            return true;
        }

        // The pace through a turn: the profile's, and no more than the bend's radius
        // lets at the profile's lateral acceleration.
        float TurnSpeed(Connector via)
        {
            float v = Profile.TurnSpeed;
            if (via != null && via.MinRadius < 1000f) v = Mathf.Min(v, Mathf.Sqrt(Profile.LateralG * via.MinRadius));
            return Mathf.Max(2.5f, v);
        }

        // Following through the box, by where the bodies actually are: anyone in the
        // box who left the same lane we did (on our connector or one diverging from
        // it - they run together at the start), and anyone just out of it on the lane
        // we are making for. His box projected on our heading: how far ahead his
        // tail is, and whether he is across our line at all.
        float BoxFollow(RoadNode node, float v)
        {
            if (_via == null) return v;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float mine = Via != null && _via.Length > 0.5f ? ViaS / _via.Length : 0f;
            for (int i = 0; i < node.Inside.Count; i++)
            {
                var o = node.Inside[i];
                if (o.Car == this || o.Car == null) continue;
                if (o.Via == _via || o.Via.From == _via.From)
                {
                    v = Mathf.Min(v, FollowBody(o.Car, f, r));   // following him across
                    continue;
                }
                // NOBODY ON A CROSSING LINE SHOULD BE IN HERE WITH US - the claim taken at
                // the line sees to that. One door is left open on purpose: a car that
                // cannot pull up at a late red goes in rather than stopping across the
                // line, and it goes in without asking anybody. When two of those meet,
                // neither is watching the other: each drives at the other until the belt
                // refuses its step, and then again the next frame, for as long as the
                // scene runs (a motorcycle and a car stood nose to nose for forty-eight
                // seconds and a thousand refused steps between them).
                // So they watch each other after all. Whoever is FURTHER ACROSS the box
                // has the right of way and the other brakes for his body like any other
                // thing in the road. The order is strict - how far across, then priority,
                // then the lower id - so of any two exactly one gives way, and a circle
                // of three all waiting for each other cannot form.
                if (o.Via.Index >= _via.Conflicts.Length || !_via.Conflicts[o.Via.Index]) continue;
                float his = o.S > 0f && o.Via.Length > 0.5f ? o.S / o.Via.Length : 0f;
                bool yield = his > mine + 0.02f ||
                             (his > mine - 0.02f &&
                              (o.Car.Profile.Priority > Profile.Priority ||
                               (o.Car.Profile.Priority == Profile.Priority && o.Car.Id < Id)));
                if (!yield) continue;
                float vy = FollowBody(o.Car, f, r);
                if (vy < v) { v = vy; _gaveWay = o.Car; if (v < 0.5f) Why = "box: giving way to " + o.Car.Id; }
            }
            if (_next != null)
                for (int i = 0; i < _next.Cars.Count; i++)
                {
                    var c = _next.Cars[i];
                    if (c == this || c.Via != null || c.Progress > 14f) continue;
                    v = Mathf.Min(v, FollowBody(c, f, r));
                }
            return v;
        }

        float FollowBody(RoadCar c, Vector3 f, Vector3 r)
        {
            var d = c._pos - _pos;
            d.y = 0f;
            float along = Vector3.Dot(d, f);
            if (along <= 0f) return float.MaxValue;
            float side = Vector3.Dot(d, r);
            var cf = c._fwd;
            float hisAlong = Mathf.Abs(Vector3.Dot(cf, f)) * c.HalfLen + Mathf.Abs(Vector3.Dot(cf, r)) * c.HalfWide;
            float hisAcross = Mathf.Abs(Vector3.Dot(cf, r)) * c.HalfLen + Mathf.Abs(Vector3.Dot(cf, f)) * c.HalfWide;
            // one of us turning: his corners swing - the whole of him counts, both ways
            if (Via != null || c.Via != null) { hisAlong = Mathf.Max(hisAlong, c.HalfLen * 0.8f); hisAcross = Mathf.Max(hisAcross, c.HalfLen * 0.8f); }
            if (Mathf.Abs(side) > HalfWide + hisAcross + 0.3f) return float.MaxValue;
            float gap = along - HalfLen - hisAlong;
            float his = Vector3.Dot(cf, f) * Mathf.Abs(c.Speed);
            return Follow(his, gap);
        }

        // Somebody coming the other way, straight across, within reach of the box.
        bool OncomingPriority(RoadNode node)
        {
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (Vector3.Dot(e.Dir, Lane.Dir) > -0.5f) continue;
                for (int k = 0; k < e.Cars.Count; k++)
                {
                    var c = e.Cars[k];
                    if (c == this || c.Via != null || c._turn != Turn.Straight) continue;
                    if (c.Speed < 0.3f && c.Profile.Priority <= Profile.Priority && c._heldAtLine > 3f) continue;
                    float dist = e.Length - c.Progress;
                    // the longer we have waited, the smaller the gap we take (nobody waits for ever)
                    float reach = 28f * Mathf.Clamp(1f - _heldAtLine / 25f, 0.35f, 1f);
                    if (dist < reach) return true;
                }
            }
            return false;
        }

        // Anyone on an approach whose movement would cross ours, arriving within seconds.
        bool ConflictApproaching(RoadNode node, float seconds)
        {
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (e == Lane) continue;
                for (int k = 0; k < e.Cars.Count; k++)
                {
                    var c = e.Cars[k];
                    if (c == this || c.Via != null) continue;
                    float dist = e.Length - c.Progress - c.HalfLen;
                    if (dist > Mathf.Max(c.Speed, 4f) * seconds + 4f) continue;
                    var his = c._via ?? (c._next != null ? node.ConnectorFor(e, c._next) : null);
                    if (his == null || _via.Conflicts[his.Index]) return true;
                }
            }
            return false;
        }

        void EnterBox(RoadNode node)
        {
            if (Derelict) { _committed = true; return; }   // a wreck claims nothing: it is driven round
            // ONE CLAIM AT A TIME, and neither half of that is optional.
            //
            // Taking a second claim while the first still stands leaves the first in ITS
            // junction's list for ever - nothing gives it back - and every car whose way
            // crosses that phantom waits at a green light until the scene is closed (the
            // lab found six cars queued six minutes behind an empty junction). Simply
            // dropping the old claim is no better: the car is still IN that junction, and
            // dropping it lets somebody in on top of it (the belt then refuses both their
            // steps, which is how this was found the second time).
            //
            // So: still in the box behind us, the commitment is not made at all. The car
            // holds at the line until its tail is out of the one it is crossing, which is
            // a rule of the road anyway - you do not enter a junction you cannot leave.
            if (_inNode != null && _nodeOf != node) return;
            _committed = true;
            if (_inNode != null) return;   // this one is already ours
            _inNode = new NodeOccupant { Car = this, Via = _via, S = -1f };
            _nodeOf = node;
            _boxLeft = false;
            // Going in while somebody else is crossing it, and the table says their line
            // and ours never meet: either it is right and they pass each other, or this
            // is the pair the belt is about to have to separate. Said out loud, with both
            // lines named, because it cannot be read back from anything else.
            if (DriveTrace.On)
                for (int i = 0; i < node.Inside.Count; i++)
                {
                    var o = node.Inside[i];
                    if (o.Car == this || o.Car == null || o.Via == _via || o.Via.From == _via.From) continue;
                    if (o.Via.Index < _via.Conflicts.Length && _via.Conflicts[o.Via.Index]) continue;
                    DriveTrace.Event("man", "car " + Id,
                        $"in with car {o.Car.Id} ({Line(o.Via)}) - the table says {Line(_via)} does not meet it",
                        ManFields());
                }
            node.Inside.Add(_inNode);
            // The hold must be on the road we are ACTUALLY leaving by. A plan thought
            // again between taking one and arriving here - the route redrawn, another
            // exit picked, in the same frame - leaves a hold on a road we are no longer
            // bound for, and coming out of the box the car takes THAT as its place on
            // the street: it then stands in a lane it is not in anybody's list for, and
            // the first car down that lane drives into it at full speed. (The belt
            // caught one at 9 m/s, bumper to bumper, in the run that found this.)
            if (_occNext != null && (_next == null || _occNext.Road != _next.Road)) DropNext();
            // the lane beyond, claimed from its start so its traffic keeps off our exit
            if (_occNext == null && _next != null)
            {
                _occNext = NewOccupant(_next.Road);
                float toEdge = Road != null ? (Road.EndS(Heading) - (S + Heading * HalfLen)) * Heading : 0f;
                RefreshNextOccupant(-(Mathf.Max(0f, toEdge) + (_via != null ? _via.Length : 10f)) - HalfLen);
            }
        }

        void RefreshNextOccupant(float progressOnNext)
        {
            if (_occNext == null || _next == null) return;
            var o = _occNext;
            float s = _next.RoadS(progressOnNext);
            int h = _next.Heading;
            o.BodyS0 = Mathf.Min(s - h * HalfLen, s + h * HalfLen);
            o.BodyS1 = Mathf.Max(s - h * HalfLen, s + h * HalfLen);
            o.BodyD0 = _next.Offset - HalfWide;
            o.BodyD1 = _next.Offset + HalfWide;
            o.S0 = o.BodyS0; o.S1 = o.BodyS1; o.D0 = o.BodyD0; o.D1 = o.BodyD1;
            o.Vel = Mathf.Abs(Speed) * h;
            o.Slowing = _want < Speed - 0.05f ? (_wantHard ? Profile.HardBrake : Profile.Brake) : 0f;
            o.Heading = h;
            o.Parked = Parked || Derelict;
        }

        void EnterNode(RoadNode node, float overshoot)
        {
            if (_via == null) return;
            _viaD0 = D - _laneD;
            Via = _via;
            ViaS = overshoot;
            if (_occ != null) { _occ.Road.Occupants.Remove(_occ); _occ = null; }
            Road = null;
            _sLen = 0f;
            ClearClaim();
            // The claim must be on THIS junction. Still holding the last one - the tail
            // not yet clear of it when the next was reached - the old entry was written
            // to instead, and the car crossed this box invisible to everybody planning
            // through it: two cars on crossing lines, and the belt refusing both their
            // steps for minutes at a time. Whatever is held, it is given up here and the
            // box we are actually in is claimed.
            if (_inNode != null && _nodeOf != node)
            {
                if (DriveTrace.On)
                    DriveTrace.Event("man", "car " + Id, "crossed a box on the last one's claim", ManFields());
                LeaveBox();
            }
            if (_inNode == null) EnterBox(node);
            if (_inNode != null) _inNode.S = ViaS;
        }

        void TickNode(float dt)
        {
            var via = Via;

            // A STANDOFF IN THE MIDDLE OF A JUNCTION. We gave way to a car on a crossing
            // line (neither of us should be in here with the other - one of us came in
            // unable to stop) and we have stood for him ever since. Giving way is not
            // enough: he cannot get past our body either, and the belt refuses HIS step
            // now instead of ours, every frame, for as long as the scene runs. So we get
            // out of his way the only way there is - backwards, off the line we came in
            // on - and take our turn again from the line.
            _boxStuck = _gaveWay != null && !_gaveWay.Parked && Mathf.Abs(Speed) < 0.15f
                ? _boxStuck + dt : 0f;
            _gaveWay = null;
            if (_boxStuck > 3f && _man != Manoeuvre.UTurn)
            {
                float back = Mathf.Min(2.5f * dt, ViaS);
                Speed = 0f;
                _want = 0f;
                if (back > 0.001f)
                {
                    ViaS -= back;
                    if (_inNode != null) _inNode.S = ViaS;
                    RefreshNextOccupant(ViaS - via.Length);
                    Why = "box: backing out";
                    Place(dt);
                    return;
                }
                BackOutOfBox(dt);
                return;
            }

            float remaining = via.Length - ViaS;
            RefreshNextOccupant(ViaS - via.Length);
            if (_inNode != null) _inNode.S = ViaS;

            float v = via.UTurn ? Profile.UTurnSpeed : _turn == Turn.Straight
                ? Mathf.Min(Cruise(), Profile.ObeysLimit ? _next.SpeedLimit : Profile.Cruise) : TurnSpeed(via);
            // the cars ahead of us through the box, and just out of it
            float vb = BoxFollow(via.Node, v);
            if (vb < v) { v = vb; if (v < 0.5f) Why = "box: following"; }
            // and on the lane beyond, from where we will come out
            var far = _next.Road;
            float farNose = _next.RoadS(ViaS - via.Length) + _next.Heading * HalfLen;
            float farTail = farNose - _next.Heading * 2f * HalfLen;
            var lead = far.Ahead(_occNext, _next.Heading, farNose, farTail, _next.Offset - HalfWide - SideAir, _next.Offset + HalfWide + SideAir, out float fgap);
            // Something STOOD in the lane we come out into - parked, or left there - is a
            // thing to drive round once we are out, not a reason to sit in the middle of
            // a junction waiting for it to move, which it never will. Waiting here blocks
            // everybody crossing (the lab watched a quarter queue for five minutes behind
            // one car parked at the mouth of a street). Out first, round it after: the
            // road's own following and passing take it from there. Only if it is right on
            // the exit is holding better than coming out on top of it.
            // Held in the box by something STOOD on the far side: out anyway, in the end.
            // Waiting in a junction for a parked car to move is waiting for ever, and
            // every crossing car waits behind us while we do it; better a tight stop on
            // the road, where the belt is the last word and the thing can be driven round.
            _heldInBox = lead != null && (lead.Parked || (lead.Car != null && lead.Car.Derelict))
                ? _heldInBox + Time.deltaTime : 0f;
            float wantsGap = _heldInBox > 12f ? HalfLen + 1f : 2f * HalfLen + 2f;
            bool standing = lead != null && (lead.Parked || (lead.Car != null && lead.Car.Derelict)) &&
                            fgap > wantsGap;
            if (lead != null && standing)
            {
                // out of the box and stopped short of it ON THE ROAD, where it can be
                // driven round - not stopped across the junction, where nobody can pass
                v = Mathf.Min(v, Allowed(0f, fgap - HalfLen - 1.2f));
            }
            else if (lead != null)
            {
                float vl = Mathf.Max(0f, lead.Vel * _next.Heading);
                if (lead.Vel * _next.Heading < -0.5f) vl = 0f;
                v = Mathf.Min(v, Follow(vl, fgap, lead.Slowing));
                if (v < 0.5f) Why = DriveTrace.On
                    ? "box: far lane " + (lead.Car != null ? "car " + lead.Car.Id : "static") + $" gap {fgap:F1} his s[{lead.S0:F1},{lead.S1:F1}] d[{lead.D0:F1},{lead.D1:F1}] farNose {farNose:F1}"
                    : "box: far lane";
            }
            v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
            v = Mathf.Min(v, BodiesAhead(dt));
            bool hard = false;
            if (!Fearless) v = Mathf.Min(v, _nerve.Limit(_pos, _fwd, Profile.Brake, out hard));
            v = LimitTarget(v);
            if (_halted) { v = 0f; if (_haltHard) hard = true; }
            InQueue = v < 0.5f;
            _want = v; _wantHard = hard;
            Speed = Mathf.MoveTowards(Speed, Mathf.Max(0f, v), (v < Speed ? (hard ? Profile.HardBrake : Profile.Brake) : Profile.Accel) * dt);
            ViaS += Speed * dt;
            if (ViaS >= via.Length)
            {
                float over = ViaS - via.Length;
                var lane = _next;
                Road = lane.Road;
                Heading = lane.Heading;
                S = lane.RoadS(over);
                D = lane.Offset;
                _laneD = D;
                _tailVia = via;
                _tailViaEndS = lane.RoadS(0f);
                SetLane(lane);
                // and the same rule where it matters most: the car's place on the street
                // it comes out onto is a place in THAT street's list, never another's
                if (_occNext != null && _occNext.Road != Road) DropNext();
                _occ = _occNext ?? NewOccupant(Road);
                _occNext = null;
                _boxEntryS = S;
                _boxLeft = true;
                Via = null;
                _next = null;
                _via = null;
                _committed = false;
                _viaD0 = 0f;
                Place(dt);
                return;
            }
            Place(dt);
        }

        /// <summary>Off the box altogether, back onto the lane we came in on, and the
        /// junction is somebody else's again. The car stands where it stood when it
        /// crossed the line, its claim given back, its way through thought again from
        /// scratch - which is what a driver who has backed out of a junction does.</summary>
        void BackOutOfBox(float dt)
        {
            var lane = _via != null ? _via.From : null;
            if (lane == null) { _boxStuck = 0f; return; }
            LeaveBox();
            DropNext();
            Road = lane.Road;
            Heading = lane.Heading;
            D = lane.Offset;
            _laneD = D;
            S = lane.RoadS(Mathf.Max(0f, lane.Length - 0.4f));
            SetLane(lane);
            _occ ??= NewOccupant(Road);
            Via = null;
            ViaS = 0f;
            _via = null;
            _next = null;
            _committed = false;
            _boxLeft = false;
            _sLen = 0f;
            Speed = 0f;
            _want = 0f;
            _boxStuck = 0f;
            _boxEntryS = S;
            Why = "backed out of the box";
            if (DriveTrace.On)
                DriveTrace.Event("man", "car " + Id, "backed out of a box it could not cross", ManFields());
            UpdateOccupant();
            Place(dt);
        }

        // The tail out of the box: off the node's list.
        void TickBoxExit()
        {
            if (_inNode == null || Road == null || !_boxLeft) return;
            // HOW FAR FROM THE BOX, not how far ON from it. Measured along the way the
            // car was going, a car that turns round the moment it is out - a U-turn in
            // the road, a reverse - counts backwards for ever and never clears the
            // junction it is standing well clear of. It then holds that box against
            // everybody for the rest of the run, and holds it under the name of the turn
            // it made half a minute ago, so two cars are let into it on lines that cross.
            if (Mathf.Abs(S - _boxEntryS) <= HalfLen + 0.8f) return;
            LeaveBox();
            // the stop that was asked for while we were crossing: here, clear of the box
            if (_haltWhenClear) { _haltWhenClear = false; Halt(_haltHard); }
        }

        // ------------------------------------------------------------------ people

        // The speed that stops short of the nearest person stood in the car's way
        // (within a car's width of its line, up to fourteen metres on).
        float _bodyHeld;   // seconds stood still for a man lying in the road

        /// <summary>The cap a body in the road puts on us. A BODY DOES NOT GET UP.
        /// Stopping for a man in the road is right; standing behind him until the scene
        /// is closed is not - a car stood 248 seconds behind one in the run that found
        /// this, with the whole street queued behind the car and the lights going green
        /// and red over an empty junction. A few seconds of it and the driver edges
        /// past at walking pace, which is what a driver does.</summary>
        float BodiesAhead(float dt)
        {
            float cap = BodiesAhead();
            _bodyHeld = cap < 0.5f ? _bodyHeld + dt : 0f;
            return _bodyHeld > 6f ? Mathf.Max(cap, 1.5f) : cap;
        }

        /// <summary>The same reach as WalkersAhead, over the men whose OWNER matters:
        /// one this vehicle will not give way to is not in its way (GivesWayTo).</summary>
        float BodiesAhead()
        {
            var people = StreetTraffic.Bodies;
            if (people.Count == 0) return float.MaxValue;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float best = float.MaxValue;
            for (int i = 0; i < people.Count; i++)
            {
                if (!GivesWayTo(people[i].Faction)) continue;
                var d = people[i].At - _pos;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                if (ahead < 0f || ahead > 14f) continue;
                if (Mathf.Abs(Vector3.Dot(d, r)) > 1.6f) continue;
                best = Mathf.Min(best, Allowed(0f, ahead - HalfLen - 1.5f));
            }
            return best;
        }

        /// <summary>Whether this vehicle brakes for a man of that faction stood in front
        /// of it. Everything brakes for everybody, and the one thing that does not is a
        /// crew's car with a fight on (CrewCar.GivesWayTo) - see the run-down.</summary>
        protected virtual bool GivesWayTo(int faction) => true;

        float WalkersAhead(List<Vector3> people)
        {
            if (people.Count == 0) return float.MaxValue;
            var p = _pos;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float best = float.MaxValue;
            for (int i = 0; i < people.Count; i++)
            {
                var d = people[i] - p;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                if (ahead < 0f || ahead > 14f) continue;
                if (Mathf.Abs(Vector3.Dot(d, r)) > 1.6f) continue;
                best = Mathf.Min(best, Allowed(0f, ahead - HalfLen - 1.5f));
            }
            return best;
        }

        // ------------------------------------------------------------------ open ground

        Vector3? _freeGoal;

        /// <summary>Off any road: drive a straight line to this point and stop.</summary>
        public void GoFree(Vector3 point) { _freeGoal = point; _halted = false; _hasGoal = false; Route = null; }

        void TickFree(float dt)
        {
            if (!_freeGoal.HasValue || _halted) { Speed = Mathf.MoveTowards(Speed, 0f, (_haltHard ? Profile.HardBrake : Profile.Brake) * dt); }
            else
            {
                var to = _freeGoal.Value - _pos;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.3f) { _freeGoal = null; Speed = 0f; OnArrived(); }
                else
                {
                    var dir = to / dist;
                    _fwd = Vector3.RotateTowards(_fwd, dir, Mathf.Deg2Rad * 90f * dt, 0f).normalized;
                    float v = Mathf.Min(Cruise(), Allowed(0f, dist));
                    v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
                    _want = v; _wantHard = false;
            Speed = Mathf.MoveTowards(Speed, v, (v < Speed ? Profile.Brake : Profile.Accel) * dt);
                }
            }
            var next = _pos + _fwd * Speed * dt;
            if (_lastPlaced)
            {
                next = RoadSpace.Advance(this, _pos, next, _fwd, HalfLen, HalfWide, out var hit);
                if (hit != null) Speed = 0f;
            }
            _pos = next;
            _lastPlaced = true;
            if (Tf != null) Tf.SetPositionAndRotation(new Vector3(_pos.x, RoadY + SurfaceLift(), _pos.z), Quaternion.LookRotation(_fwd, Vector3.up));
            OnPlaced(dt, Speed, 0f);
        }

        public Vector3? FreeGoal => _freeGoal;

        // ------------------------------------------------------------------ the pull-out, ticked

        /// <summary>Called by TickRoad through Decide: the pull-out waits for the lane.</summary>
        void TickPullOut()
        {
            if (!_pullOutWanted) return;
            float lo = _laneD - HalfWide - 0.3f, hi = _laneD + HalfWide + 0.3f;
            // A CAR WAITING FOR A PARKED CAR TO MOVE WAITS FOR EVER, and this one did:
            // fifty monkey runs of the crew demo put a vehicle in a permanent deadlock in
            // thirty-nine of them, one of them for the whole ten minutes. The knot is
            // that a car stopped off its lane may not enter a junction (CanEnter's "off
            // lane at the line"), and it cannot get back into the lane because the gap it
            // asks for is fouled by something that is never going to move. So: for the
            // first few seconds it waits for a proper gap, as it should - and after that
            // the parked stop counting. Rolling, a parked car ahead is a thing to go
            // round, which the tactics already do (Decide's behindParked).
            bool ignoreParked = Time.time - _pullOutAsked > PullOutPatience;

            // AND IF EVEN THAT DOES NOT COME, he takes the lane. There is a second way
            // to wait for ever: parked on the far kerb of a wide street, the lane for
            // his heading is six or seven metres across the road, so the gap he is
            // asking about is in the oncoming stream and a busy street never gives him
            // two clear seconds of it. One monkey run held a crew car at s=11, d=6.7
            // for the whole ten minutes that way, with the mission waiting on it. A
            // driver in that spot edges out; so does this one - the manoeuvre is
            // dropped, which also lets him past the junction line again (CanEnter), and
            // the ordinary lane-keeping takes him the rest of the way over.
            if (!BandFree(lo, hi, 8f, 2f, out _, ignoreParked) &&
                Time.time - _pullOutAsked > PullOutGiveUp)
            {
                DriveTrace.Event("man", "car " + Id, "took the lane after waiting", ManFields());
                _pullOutWanted = false;
                _man = Manoeuvre.None;
                ClearClaim();
                Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Max(Speed, 3f)));
                return;
            }
            if (BandFree(lo, hi, 8f, 2f, out _, ignoreParked))
            {
                _pullOutWanted = false;
                _man = Manoeuvre.PullOut;
                Slide(_laneD, 10f);
                float noseS = S + Heading * HalfLen;
                Claim(S - Heading * HalfLen, noseS + Heading * 12f, Mathf.Min(lo, D - HalfWide), Mathf.Max(hi, D + HalfWide));
            }
        }

        /// <summary>Where the car is, for a log line.</summary>
        /// <summary>One frame of this car in the trace: what it was asked to do, what it
        /// did, and anything that reads as a fault - a stop nobody asked for, braking
        /// harder than the profile allows, a step longer than the speed, a speed over
        /// the profile. Called from Place, which every path through the tick ends in.</summary>
        void TraceStep(float dt, Vector3 pos, float steer)
        {
            // whatever changed about what the car is doing: a swing out, a turn in the
            // road, a pull-in, a junction entered or left - the shape of the run
            if (_man != _prevMan) { DriveTrace.Event("man", "car " + Id, _prevMan + " -> " + _man, ManFields()); _prevMan = _man; }
            if ((Via != null) != _prevVia) { _prevVia = Via != null; DriveTrace.Event("man", "car " + Id, _prevVia ? "into the box" : "out of the box", ManFields()); }
            if (Parked != _prevParked) { _prevParked = Parked; DriveTrace.Event("man", "car " + Id, Parked ? "parked" : "away", ManFields()); }

            // the first frame of the trace has no frame before it: the speed and the
            // place it would be compared against are nought, and every car would come
            // out of the gate braking from ten and teleporting from the origin
            bool first = !_traced;
            _traced = true;
            float acc = !first && dt > 1e-4f ? (Speed - _prevSpeed) / dt : 0f;
            float step = !first && _lastPlaced ? Vector3.Distance(pos, _prevPos) : 0f;
            float cruise = Cruise();

            // a stop nobody asked for: the car is not moving and not parked. Whether it
            // was ASKED to move is in the row (want), so a queue at a light reads apart
            // from a car that has simply died in the road.
            if (Mathf.Abs(Speed) < 0.3f && !Parked) _quietFor += dt; else { _quietFor = 0f; _saidStall = 0f; }

            // every branch here ends in a Fault(), whose message is a trace string and
            // whose only consumer is the trace - so the whole ladder is skipped when the
            // trace is closed, and none of the $"..." args (one per car per frame under a
            // stall or a hard brake) are built for a reader that is not listening
            if (DriveTrace.On)
            {
            if (first) { }
            // a car asked for nought is standing because it was told to (halted, parked,
            // waiting for its crew): only one that WANTS to move and is not moving is stuck
            else if (_want > 0.5f && _quietFor > 4f && _quietFor - _saidStall > 6f)
            {
                _saidStall = _quietFor;
                Fault("stall", $"still for {_quietFor:F1}s", acc, step, steer);
            }
            // braking harder than the profile is meant to allow
            else if (acc < -Profile.HardBrake * 1.15f && dt > 1e-4f)
                Fault("overbrake", $"{acc:F1} m/s2, hard={_wantHard}", acc, step, steer);
            // asked for a stop from cruising speed with nothing in front to explain it.
            // A throttle asked for nought brakes hard by design (TickRoad), so that is
            // not the fault being looked for here - an unexplained one is.
            else if (acc < -Profile.Brake * 1.05f && !_wantHard && _want > 0.01f &&
                     string.IsNullOrEmpty(Why) && Via == null)
                Fault("brake", $"{acc:F1} m/s2 with no reason given", acc, step, steer);
            // faster than the driver is allowed to go
            else if (Mathf.Abs(Speed) > cruise * 1.25f + 0.5f)
                Fault("speeding", $"{Speed:F1} over {cruise:F1}", acc, step, steer);
            // a step the speed does not account for: the car was moved, not driven
            else if (_lastPlaced && step > Mathf.Abs(Speed) * dt * 1.6f + 0.12f)
                Fault("jump", $"{step:F2} m in one frame at {Speed:F1} m/s", acc, step, steer);
            // the wheel wound right over AT SPEED: the line the car is following has a
            // kink in it. Slowly, full lock is just a tight corner - every junction has one.
            else if (Mathf.Abs(steer) > 33f && Mathf.Abs(Speed) > 7f)
                Fault("steer", $"{steer:F0} deg at {Speed:F1} m/s", acc, step, steer);
            }

            _prevSpeed = Speed;
            _prevPos = pos;

            if (DriveTrace.Now < _nextSample) return;
            _nextSample = DriveTrace.Now + DriveTrace.SampleEvery;
            var sb = Sample(acc, step, steer);
            DriveTrace.Row("car", sb.ToString());
        }

        System.Text.StringBuilder Sample(float acc, float step, float steer)
        {
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", Id);
            DriveTrace.Str(sb, "tag", Tag);
            DriveTrace.Str(sb, "prof", Profile.Name);
            DriveTrace.Num(sb, "v", Speed);
            DriveTrace.Num(sb, "want", _want);
            DriveTrace.Num(sb, "acc", acc);
            DriveTrace.Num(sb, "step", step, "F3");
            DriveTrace.Num(sb, "steer", steer, "F0");
            DriveTrace.Int(sb, "road", Road != null ? Road.Index : -1);
            DriveTrace.Num(sb, "s", S);
            DriveTrace.Num(sb, "d", D);
            DriveTrace.Int(sb, "h", Heading);
            DriveTrace.Str(sb, "man", _man.ToString());
            DriveTrace.Bool(sb, "via", Via != null);
            DriveTrace.Bool(sb, "box", _inNode != null);
            DriveTrace.Bool(sb, "queue", InQueue);
            DriveTrace.Bool(sb, "parked", Parked);
            DriveTrace.Bool(sb, "goal", _hasGoal);
            DriveTrace.Num(sb, "quiet", _quietFor, "F1");
            DriveTrace.Num(sb, "held", _heldAtLine, "F1");
            DriveTrace.Int(sb, "lead", _leadId);
            DriveTrace.Num(sb, "lgap", _leadGap, "F1");
            DriveTrace.Str(sb, "why", Why);
            DriveTrace.Vec(sb, "p", _pos);
            return sb;
        }

        /// <summary>Something the driving code should not have done. The whole state
        /// goes down with it, so the run can be read without guessing.</summary>
        void Fault(string kind, string what, float acc, float step, float steer)
        {
            if (_traceEvents > 4000) return;
            _traceEvents++;
            var sb = Sample(acc, step, steer);
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
        }

        /// <summary>Where the car was when a manoeuvre began, ended or was given up on.</summary>
        string ManFields()
        {
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "tag", Tag);
            DriveTrace.Num(sb, "v", Speed);
            DriveTrace.Int(sb, "road", Road != null ? Road.Index : -1);
            DriveTrace.Num(sb, "s", S);
            DriveTrace.Num(sb, "d", D);
            DriveTrace.Str(sb, "man", _man.ToString());
            // which box he holds, and which way he means to go through it - the two
            // have to be the same thing, and a run where they were not is how a pair
            // of cars is let into one junction (they plan against the claim's name)
            if (_inNode != null) DriveTrace.Str(sb, "claim", Line(_inNode.Via));
            if (_via != null) DriveTrace.Str(sb, "plan", Line(_via));
            DriveTrace.Str(sb, "why", Why);
            DriveTrace.Str(sb, "passwhy", PassWhy);
            DriveTrace.Vec(sb, "p", _pos);
            return sb.ToString();
        }

        static string Line(Connector via) =>
            via == null ? "" :
            $"{via.From.Road.Index}/{via.From.Heading}->{via.To.Road.Index}/{via.To.Heading}#{via.Index}";

        public string Describe()
        {
            if (Via != null) return $"[box {Via.From.Road.Index}/{Via.From.Heading}->{Via.To.Road.Index}/{Via.To.Heading} {Via.Kind} viaS={ViaS:F1}/{Via.Length:F1} inNode={(_inNode != null)} why={Why}]";
            return $"[road {(Road != null ? Road.Index : -1)} s={S:F1} d={D:F1} h={Heading} {DoingLine} committed={_committed} inNode={(_inNode != null ? _inNode.Via.From.Road.Index + "/" + _inNode.Via.From.Heading + "->" + _inNode.Via.To.Road.Index + "/" + _inNode.Via.To.Heading + " " + _inNode.Via.Kind + (_boxLeft ? " left" : " approaching") : "no")} why={Why}]";
        }

        /// <summary>Read by the traffic's spawner and the overlay: what the driver is doing.</summary>
        public string DoingLine => _man switch
        {
            Manoeuvre.Pass => "Going round",
            Manoeuvre.Crown => "On the crown",
            Manoeuvre.UTurn => "Turning round",
            Manoeuvre.PullIn => "Pulling in",
            Manoeuvre.PullOut => "Pulling out",
            Manoeuvre.Reverse => "Backing off",
            Manoeuvre.Aside => "Giving way",
            _ => Via != null ? "Crossing" : Parked ? "Parked" : "Driving",
        };
    }
}
