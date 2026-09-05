using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The kerb: finding a free stretch of it to stand in, the swing into
    /// the spot, and the wait for a gap to pull out of it again.</summary>
    public partial class RoadCar
    {
        float _spotCheck;
        const float KerbParkReach = 1.2f;
        const float ParkingSearchReach = 45f;
        const float ParkingAttemptLimit = 35f;
        float _parkRequestedS;
        float _parkEntryS, _parkEntryD, _parkEntryLen;
        bool _parkPlanReady;
        float _parkTrafficCheck;
        bool _parkTrafficClear;
        bool _parkingRetreat;
        readonly List<float> _failedKerbSpots = new List<float>();
        System.Predicate<RoadOccupant> _parkingLeaderFilter;

        // Filtering happens before the nearest leader is selected, so a parked
        // shoulder cannot hide moving traffic farther along the real entry path.
        bool BlocksParkingPath(RoadOccupant other) => !RemainingSlideClearOf(other);

        /// <summary>The last parking order had no reachable kerb. This is not arrival;
        /// a new order clears the result and starts a new search.</summary>
        public bool ParkingFailed { get; private set; }

        protected void ClearParkingFailure() => ParkingFailed = false;

        /// <summary>Autonomous road users release the failed order to their owner
        /// and continue with traffic. A player crew can keep the requested stop.</summary>
        protected virtual void OnParkingFailed()
        {
            // An angled failed entry retains its real curve while stopped. Its
            // owner must issue a new order; releasing the throttle would repeat it.
            if (Sliding) return;
            _halted = false;
            _haltWhenClear = false;
            _keepGoalWhenHaltClear = false;
            if (Parked) PullOut();
        }

        /// <summary>An extra claim book for a specialised car. The road itself knows
        /// bodies and manoeuvre claims, but not destinations selected by another car
        /// that is still across town.</summary>
        protected virtual bool ParkingSpotAvailable(Vector3 at) => true;

        /// <summary>The parking chooser may move a requested point into another gap as
        /// traffic changes. Specialised cars can keep their destination claim in step.</summary>
        protected virtual void ParkingSpotSelected(Vector3 at) { }

        // A destination is a free body footprint AND an entry the real rear axle
        // can drive. Always rank against the original order, so retries cannot
        // gradually move the destination down the street.
        bool ChooseKerbSpot(bool aheadOnly = false)
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
                if (!FixedParkingObstacle(o) || !o.BodyOverlaps(lo, hi)) continue;
                taken.Add(new Vector2(o.BodyS0 - 0.5f, o.BodyS1 + 0.5f));
            }
            float margin = 4f;
            taken.Add(new Vector2(-1000f, margin));
            taken.Add(new Vector2(road.Length - margin, road.Length + 1000f));
            taken.Sort((a, b) => a.x.CompareTo(b.x));
            float fromD = _goalLane.Offset;
            bool here = Road == road && Heading == h;
            if (here && !Sliding && _man == Manoeuvre.None && !Parked &&
                Mathf.Abs(D - fromD) <= .5f) fromD = D;
            bool forwardApproach = here && (aheadOnly || (_parkRequestedS - S) * h >= 0f);
            float earliestEntry = ParkingEntryFloor(fromD);
            float cruise = Profile.CruiseOn(road.Class) * Machine.Top;
            if (Profile.ObeysLimit) cruise = Mathf.Min(cruise, _goalLane.SpeedLimit);
            float parkingPace = Mathf.Min(cruise, Mathf.Clamp(cruise * .6f, 4f, 8f));
            float minimum = SlideLength(Mathf.Abs(kerb - fromD), 0f);
            float comfortable = Mathf.Max(10f, SlideLength(Mathf.Abs(kerb - fromD), parkingPace));
            // Plan every candidate without moving the car. Prefer a smooth arc in
            // a nearby gap before accepting the tightest, walking-speed swing.
            // The first pass preserves a straight move along an already clear kerb.
            float bestDist = float.MaxValue, bestS = float.NaN;
            float bestEntry = 0f, bestLength = 0f;
            for (int shape = -1; shape < 2 && float.IsNaN(bestS); shape++)
            {
                if (shape == -1 && (!here || !ParkingAligned(kerb))) continue;
                float length = shape == -1 ? 0f : shape == 0 ? comfortable : minimum;
                float end = taken[0].y;
                for (int i = 1; i < taken.Count; i++)
                {
                    var t = taken[i];
                    float a = end, b = t.x;
                    end = Mathf.Max(end, t.y);
                    if (b - a < need) continue;
                    float min = a + HalfLen + .8f, max = b - HalfLen - .8f;
                    if (shape >= 0 && forwardApproach)
                    {
                        float entryPace = LateralCap(length, Mathf.Abs(kerb - fromD));
                        float brakingRun = Mathf.Max(0f, (Speed * Speed - entryPace * entryPace) / (2f * Brake));
                        brakingRun += Mathf.Max(.5f, Mathf.Abs(Speed) * .1f);
                        float startRun = Mathf.Max((earliestEntry - S) * h, brakingRun);
                        float firstCentre = S + h * (startRun + length + Axle);
                        if (h > 0) min = Mathf.Max(min, firstCentre);
                        else max = Mathf.Min(max, firstCentre);
                    }
                    if (min > max) continue;
                    _kerbCandidates.Clear();
                    for (int step = 0; step <= 15; step++)
                        for (int direction = 0; direction < 2; direction++)
                        {
                            if (step == 0 && direction == 1) continue;
                            float offset = step * 3f * (direction == 0 ? 1f : -1f);
                            float centre = Mathf.Clamp(_parkRequestedS + offset, min, max);
                            if (!_kerbCandidates.Add(centre)) continue;
                            float dist = Mathf.Abs(centre - _parkRequestedS);
                            if (dist > ParkingSearchReach || dist >= bestDist || FailedKerbSpot(centre)) continue;
                            if (!ParkingSpotAvailable(road.Pose(centre, kerb))) continue;
                            if (!TryKerbEntry(centre, fromD, kerb, length, out float entry)) continue;
                            bestDist = dist;
                            bestS = centre;
                            bestEntry = entry;
                            bestLength = length;
                        }
                }
            }
            if (float.IsNaN(bestS))
            {
                _parkPlanReady = false;
                return false;
            }
            _goalS = bestS;
            _goalD = kerb;
            _parkEntryS = bestEntry;
            _parkEntryD = bestLength == 0f ? kerb : fromD;
            _parkEntryLen = bestLength;
            _parkPlanReady = true;
            _parkTrafficCheck = 0f;
            _parkTrafficClear = false;
            ParkingSpotSelected(road.Pose(bestS, kerb));
            return true;
        }

        static readonly List<Vector2> _taken = new List<Vector2>();
        static readonly HashSet<float> _kerbCandidates = new HashSet<float>();

        static bool FixedParkingObstacle(RoadOccupant o) => o.Car == null ||
            o.Car.Parked || o.Car.Derelict || o.Car.Wrecked;

        bool FailedKerbSpot(float s)
        {
            foreach (float failed in _failedKerbSpots)
                if (Mathf.Abs(s - failed) < 1.5f) return true;
            return false;
        }

        // A parking order can arrive while still stood at the kerb or overtaking.
        // Include that exit/return NOW, not after driving past the selected entry.
        float ParkingEntryFloor(float fromD)
        {
            float travel = 0f;
            float returnD = D;
            if (Sliding)
            {
                travel = Mathf.Max(0f, (_sFrom - S) * Heading + _sLen + Axle);
                returnD = _dTo;
            }
            if (_man == Manoeuvre.Pass)
                travel = Mathf.Max(travel, (_manPastS - S) * Heading);
            if (Mathf.Abs(returnD - fromD) >= .3f)
                travel += SlideLength(Mathf.Abs(returnD - fromD), Mathf.Max(Mathf.Abs(Speed), 3f)) + Axle;
            return S + Heading * (travel + .5f);
        }

        bool TryKerbEntry(float centre, float fromD, float kerb, float length, out float entry)
        {
            entry = 0f;
            var road = _goalRoad;
            int heading = _goalHeading;
            if (length == 0f)
            {
                if (Road != road || Heading != heading || Sliding ||
                    !ParkingAligned(kerb) || (centre - S) * heading < 0f ||
                    road.Busy(_occ, Mathf.Min(S, centre) - HalfLen,
                        Mathf.Max(S, centre) + HalfLen, kerb - HalfWide, kerb + HalfWide)) return false;
                entry = S;
                return true;
            }
            float start = centre - heading * (length + Axle);
            float margin = HalfLen + .5f;
            if (start < margin || start > road.Length - margin ||
                !SlidePathClear(road, heading, start, fromD, kerb, length, 0f, stationaryOnly: true)) return false;
            entry = start;
            return true;
        }

        bool ParkingEntryClear(bool stationaryOnly) => _parkEntryLen == 0f ||
            SlidePathClear(_goalRoad, _goalHeading, _parkEntryS, _parkEntryD,
                _goalD, _parkEntryLen, 0f, stationaryOnly);

        bool ParkingAligned(float kerb)
        {
            if (Road == null) return false;
            Road.Project(_pos, out float s, out float d);
            return Mathf.Abs(d - kerb) < .3f &&
                Vector3.Dot(_fwd, Road.DirAt(s) * Heading) > .999f;
        }

        bool ParkingDestinationTaken()
        {
            if (!ParkingSpotAvailable(_goalRoad.Pose(_goalS, _goalD))) return true;
            foreach (var o in _goalRoad.Occupants)
                if (!ReferenceEquals(o.Who, this) && FixedParkingObstacle(o) &&
                    o.BodyS0 < _goalS + HalfLen + .3f && o.BodyS1 > _goalS - HalfLen - .3f &&
                    o.BodyOverlaps(_goalD - HalfWide - .2f, _goalD + HalfWide + .2f)) return true;
            return false;
        }

        // Only an actual overlap with the chosen destination can mean "our spot".
        // The parked neighbour on the approach must still be passed or backed away from.
        bool IsOurParkingSpot(RoadOccupant o)
        {
            if (!_hasGoal || !_goalPark || Road != _goalRoad || Heading != _goalHeading || o == null || !o.Parked) return false;
            return o.BodyS0 < _goalS + HalfLen && o.BodyS1 > _goalS - HalfLen &&
                o.BodyOverlaps(_goalD - HalfWide, _goalD + HalfWide);
        }

        // ------------------------------------------------------------------ parking

        void RejectKerbApproach()
        {
            if (!FailedKerbSpot(_goalS)) _failedKerbSpots.Add(_goalS);
            _parkPlanReady = false;
            _spotCheck = 0f;
            _parkTrafficCheck = 0f;
        }

        // Retrace the entry that was actually driven. Dropping its curve and
        // starting a fresh slide here would turn a stopped, angled body in place.
        void RetreatFromKerb()
        {
            RejectKerbApproach();
            float travelled = Sliding ? Mathf.Max(0f, (S - _sFrom) * Heading) : 0f;
            if (travelled < .01f)
            {
                if (Sliding) D = _dFrom;
                _sLen = 0f;
                _man = Manoeuvre.None;
                ClearClaim();
                return;
            }
            if (!Profile.Reverses || ClearBehind() < .6f) { FailParking(); return; }
            _parkingRetreat = true;
            _backLeft = travelled;
            _man = Manoeuvre.Reverse;
            Speed = 0f;
            _holdFor = 0f;
            Claim(S + Heading * HalfLen, _sFrom - Heading * HalfLen,
                Mathf.Min(_dFrom, _dTo) - HalfWide, Mathf.Max(_dFrom, _dTo) + HalfWide);
        }

        void FailParking()
        {
            _parkPlanReady = false;
            _pullOutWanted = false;
            if (_man == Manoeuvre.Reverse)
            {
                _backLeft = 0f;
                Speed = 0f;
                _man = Manoeuvre.None;
                ClearClaim();
            }
            _parkingRetreat = false;
            Halt(false);
            ParkingFailed = true;
            Why = "No reachable parking nearby";
            OnParkingFailed();
            // Do not call OnArrived or mark a traffic lane as parked.
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "parking unavailable", ManFields());
        }

        void BeginPullIn()
        {
            _man = Manoeuvre.PullIn;
            _pullInAsked = RoadCarSimulation.Now;
            // Arm before the start, but drive the very same curve that was checked.
            // Negative progress is a straight approach; no rebasing or shorter arc.
            _dFrom = _parkEntryD;
            _dTo = _goalD;
            _sFrom = _parkEntryS;
            _sLen = _parkEntryLen;
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
            _pullOutAsked = RoadCarSimulation.Now;
        }

        /// <summary>Seconds a car holds at the line after backing out of a junction it
        /// could not cross - long enough for whatever it met in there to move on, short
        /// enough that a junction is not given away for nothing.</summary>
        const float BoxHold = 4f;
        float _boxHoldUntil;

        bool _pullOutWanted;
        float _pullOutAsked;
        float _pullInAsked;

        /// <summary>How long a car waits for a clear lane before it stops counting the
        /// PARKED as a reason to sit there. Long enough that it takes a real gap when
        /// one is coming, short enough that it is not a wait a player watches.</summary>
        const float PullOutPatience = 6f;

        /// <summary>And how long before he stops asking and simply goes.</summary>
        const float PullOutGiveUp = 20f;

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
            bool ignoreParked = RoadCarSimulation.Now - _pullOutAsked > PullOutPatience;

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
                RoadCarSimulation.Now - _pullOutAsked > PullOutGiveUp)
            {
                if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "took the lane after waiting", ManFields());
                _pullOutWanted = false;
                _man = Manoeuvre.None;
                ClearClaim();
                Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Max(Speed, 3f)));
                return;
            }
            if (BandFree(lo, hi, 8f, 2f, out _, ignoreParked))
            {
                // A GAP IN THE LANE IS HALF THE QUESTION; the other half is room in FRONT.
                // The swing out of a slot is a diagonal, and a body parked two metres up
                // the kerb fouls it however empty the lane behind is - which is why a car
                // that had its gap sat in the slot anyway, sliding at a lane it could not
                // reach. So the swing is measured before it is begun: the pace's own
                // length first, then the tightest the turning circle allows; and where
                // even that will not fit, the driver does what a driver does with a bumper
                // in front of him - backs up a length and asks again.
                float dd = Mathf.Abs(_laneD - D);
                float len = SlideLength(dd, Mathf.Max(Mathf.Abs(Speed), 3f));
                if (!SlidePathClear(_laneD, len, 4f))
                {
                    len = SlideLength(dd, 0f);
                    if (!SlidePathClear(_laneD, len, 4f))
                    {
                        AskRoomToPullOut();
                        // The swing can clip a parked shoulder even though the band
                        // we already occupy is clear. Waiting for that parked body
                        // never creates a gap. Ease forward under normal following
                        // and collision checks, then ask for the merge again.
                        if (_pullOutWanted && RoadCarSimulation.Now - _pullOutAsked > PullOutGiveUp &&
                            Road.Drivable(D, HalfWide) &&
                            BandFree(D - HalfWide - 0.3f, D + HalfWide + 0.3f,
                                8f, 2f, out _))
                        {
                            _pullOutWanted = false;
                            _man = Manoeuvre.None;
                            ClearClaim();
                            _yieldUntil = RoadCarSimulation.Now + PullOutPatience;
                        }
                        return;
                    }
                }
                _pullOutWanted = false;
                _man = Manoeuvre.PullOut;
                Slide(_laneD, len);
                float noseS = S + Heading * HalfLen;
                Claim(S - Heading * HalfLen, noseS + Heading * (len + 2f), Mathf.Min(lo, D - HalfWide), Mathf.Max(hi, D + HalfWide));
            }
        }

        /// <summary>Boxed in: the lane is open, and the swing out of the slot is fouled by
        /// something standing on the bumper ahead. A couple of metres back is all it takes,
        /// and a slot leaves them - so back up, and the kerb is asked again from there.</summary>
        void AskRoomToPullOut()
        {
            if (!Profile.Reverses || Mathf.Abs(Speed) > 0.3f || Road == null) return;
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            var ahead = Road.Ahead(_occ, Heading, noseS, tailS, BandLo(), BandHi(), out float gap);
            if (ahead == null || ahead.Moving || JustBackedOff(ahead)) return;
            if (!TryReverse(ahead, gap)) return;
            // the manoeuvre is the reverse now; what follows it is another look at the kerb
            _pullOutWanted = false;
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "backing up for room to pull out", ManFields());
        }
    }
}
