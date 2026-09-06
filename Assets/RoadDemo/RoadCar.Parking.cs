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
        Manoeuvre _failedParkingMan;
        float _parkingRetryBy;
        bool _parkReturnApproach;
        bool _parkReservationBlocked;
        float _parkingRetryAt;
        public string ParkingReason { get; private set; } = "";
        readonly List<Vector2> _failedKerbSpots = new List<Vector2>();
        readonly List<RoadCar> _parkingNeighbours = new List<RoadCar>();
        System.Predicate<RoadOccupant> _parkingLeaderFilter;

        // Filter parked shoulders before selecting the nearest moving leader.
        bool StraightKerbApproach => _hasGoal && _goalPark && _parkPlanReady &&
            _parkEntryLen == 0f && Road == _goalRoad && Heading == _goalHeading && !Sliding;

        bool BlocksParkingPath(RoadOccupant other)
        {
            // A stop before this bumper does not require overtaking that car.
            if (StraightKerbApproach && FixedParkingObstacle(other) &&
                ((Heading > 0 ? other.BodyS0 : other.BodyS1) - _goalS) * Heading > HalfLen + SideAir)
                return false;
            return !RemainingSlideClearOf(other);
        }

        /// <summary>The last parking order failed; a new order clears this result.</summary>
        public bool ParkingFailed { get; private set; }

        /// <summary>Specialised owners can keep an angled failed manoeuvre in place
        /// until they have preserved and redirected its higher-level assignment.</summary>
        protected virtual bool KeepsFailedParkingCurve => false;

        protected void ClearParkingFailure()
        {
            ParkingFailed = false;
            ParkingReason = "";
            _failedParkingMan = Manoeuvre.None;
            _parkingRetryBy = 0f;
            _parkingRetryAt = 0f;
            _parkReturnApproach = false;
        }

        /// <summary>Back out of a failed angled entry before selecting another goal.</summary>
        protected bool PrepareParkingRetry()
        {
            if (!Sliding) return true;
            if (!ParkingFailed || Mathf.Abs(Speed) >= .2f) return false;
            // Non-parking sweeps keep moving forward. A permanently blocked rear
            // also releases the brake after a bounded wait, retaining the real curve
            // and the patrol owner's response/return intent instead of releasing it.
            if ((_failedParkingMan != Manoeuvre.PullIn && _failedParkingMan != Manoeuvre.PullOut) ||
                RoadCarSimulation.Now >= _parkingRetryBy)
            {
                var failedMan = _failedParkingMan;
                ResumeTraffic();
                if (failedMan == Manoeuvre.Pass || failedMan == Manoeuvre.Crown ||
                    failedMan == Manoeuvre.Aside || failedMan == Manoeuvre.LaneChange)
                    _man = failedMan;
                return false;
            }
            if (Profile.Reverses && ClearBehind() >= .6f)
            {
                ParkingFailed = _halted = false;
                RetreatFromKerb();
            }
            return !Sliding;
        }

        /// <summary>Release a failed destination without abandoning the driven curve.</summary>
        protected virtual void OnParkingFailed()
        {
            _halted = false;
            _haltWhenClear = false;
            _keepGoalWhenHaltClear = false;
            if (Sliding && _man != Manoeuvre.Reverse)
            {
                _man = Manoeuvre.PullOut;
                _manD = _dTo;
                _manPastS = _sFrom + Heading * (_sLen + Axle);
                _exitAdvance = false;
            }
            if (Parked) PullOut();
        }

        /// <summary>Specialised claims include destinations of cars still across town.</summary>
        protected virtual bool ParkingSpotAvailable(Vector3 at) => true;

        /// <summary>Keep specialised claims in step when the chooser moves the goal.</summary>
        protected virtual void ParkingSpotSelected(Vector3 at) { }

        bool FindParking(bool aheadOnly = false)
        {
            if (ChooseKerbSpot(aheadOnly)) return true;
            if (!_parkReservationBlocked || _parkTrying >= ParkingAttemptLimit) return false;
            _parkingRetryAt = RoadCarSimulation.Now + .7f;
            ParkingReason = "Waiting for parking reservation";
            return true;
        }

        // A slot needs both an entry and an exit for the real rear axle.
        bool ChooseKerbSpot(bool aheadOnly = false, bool returnApproach = false)
        {
            var road = _goalRoad;
            if (road == null || !_goalPark) return false;
            ParkingReason = "No free parking nearby";
            _parkReservationBlocked = false;
            CollectParkingNeighbours();
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
            if (here && !Sliding && !Parked &&
                (_man == Manoeuvre.Pass || _man == Manoeuvre.None && Mathf.Abs(D - fromD) <= .5f)) fromD = D;
            bool forwardApproach = !returnApproach && here && (aheadOnly || (_parkRequestedS - S) * h >= 0f);
            float earliestEntry = ParkingEntryFloor(fromD);
            float cruise = Profile.CruiseOn(road.Class) * Machine.Top;
            if (Profile.ObeysLimit) cruise = Mathf.Min(cruise, _goalLane.SpeedLimit);
            float parkingPace = Mathf.Min(cruise, Mathf.Clamp(cruise * .6f, 4f, 8f));
            float minimum = SlideLength(Mathf.Abs(kerb - fromD), 0f);
            float comfortable = Mathf.Max(10f, SlideLength(Mathf.Abs(kerb - fromD), parkingPace));
            // Prefer proximity; a longer, faster curve only wins at equal distance.
            // An already aligned car can drive straight along a clear kerb.
            float bestDist = float.MaxValue, bestS = float.NaN;
            float bestEntry = 0f, bestLength = 0f;
            for (int shape = -1; shape < 2; shape++)
            {
                if (returnApproach && shape == 0) continue;
                if (shape == -1 && (!here || !ParkingAligned(kerb))) continue;
                float length = shape == -1 ? 0f : shape == 0 ? comfortable : minimum;
                float end = taken[0].y;
                for (int i = 1; i < taken.Count; i++)
                {
                    var t = taken[i];
                    float a = end, b = t.x;
                    end = Mathf.Max(end, t.y);
                    if (b - a < need) continue;
                    ParkingReason = "No reachable parking approach";
                    float min = a + HalfLen + .8f, max = b - HalfLen - .8f;
                    if (returnApproach)
                    {
                        if (h > 0) max = Mathf.Min(max, S - 4f);
                        else min = Mathf.Max(min, S + 4f);
                    }
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
                    for (int step = 0; step <= 45; step++)
                        for (int direction = 0; direction < 2; direction++)
                        {
                            if (step == 0 && direction == 1) continue;
                            float offset = step * (direction == 0 ? 1f : -1f);
                            float centre = Mathf.Clamp(_parkRequestedS + offset, min, max);
                            if (!_kerbCandidates.Add(centre)) continue;
                            float dist = Mathf.Abs(centre - _parkRequestedS);
                            if (dist > ParkingSearchReach || dist >= bestDist || FailedKerbSpot(centre)) continue;
                            if (!ParkingSpotAvailable(road.Pose(centre, kerb)))
                            { _parkReservationBlocked = true; ParkingReason = "Parking reserved"; continue; }
                            if (!TryKerbEntry(centre, fromD, kerb, length, out float entry)) continue;
                            // Arrival can roll a fraction past the requested centre.
                            if (!KerbExitClear(centre, kerb) || !KerbExitClear(centre + h * .3f, kerb))
                            { ParkingReason = "No clear parking exit"; continue; }
                            if (ParkingReservationTaken(centre, kerb, refresh: false))
                            { _parkReservationBlocked = true; ParkingReason = "Parking or neighbour exit reserved"; continue; }
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
                // Missing the last possible entry is not a full kerb. Find a
                // reachable slot behind us and let normal routing return to it.
                if (forwardApproach && !returnApproach && _goalUTurns < MaxGoalUTurns)
                {
                    bool reserved = _parkReservationBlocked;
                    bool found = ChooseKerbSpot(aheadOnly, returnApproach: true);
                    _parkReservationBlocked |= reserved;
                    if (found) { Replan(); return true; }
                }
                return false;
            }
            _goalS = bestS;
            _goalD = kerb;
            _parkEntryS = bestEntry;
            _parkEntryD = bestLength == 0f ? kerb : fromD;
            _parkEntryLen = bestLength;
            _parkPlanReady = true;
            _parkReturnApproach = returnApproach;
            _parkTrafficCheck = 0f;
            _parkTrafficClear = false;
            ParkingReason = "Approaching parking";
            ParkingSpotSelected(road.Pose(bestS, kerb));
            return true;
        }

        static readonly List<Vector2> _taken = new List<Vector2>();
        static readonly HashSet<float> _kerbCandidates = new HashSet<float>();

        bool ReturnToParkingEntry()
        {
            if (!_parkReturnApproach || !_hasGoal || !_goalPark || Road != _goalRoad || Heading != _goalHeading)
                return false;
            if (_man == Manoeuvre.Reverse) return true;
            float back = (S - _parkEntryS) * Heading + .5f;
            if (back <= .1f) { _parkReturnApproach = false; _holdFor = 0f; return false; }
            // A short missed approach can be recovered without driving to a dead end.
            // The whole straight reverse must be clear, then every step is checked again.
            if (!Profile.Reverses || back > 25f || Sliding || _man != Manoeuvre.None)
            { _parkReturnApproach = false; return false; }
            ParkingReason = "Returning to parking approach";
            if (Mathf.Abs(Speed) > .15f) return true;
            if (ClearBehind(back + 1f) < back + .5f)
            { ParkingReason = "Waiting for parking approach"; return true; }
            _backLeft = back;
            _man = Manoeuvre.Reverse;
            Speed = 0f;
            _holdFor = 0f;
            Claim(S + Heading * HalfLen, _parkEntryS - Heading * (HalfLen + 1f),
                D - HalfWide - .2f, D + HalfWide + .2f);
            return true;
        }

        static bool FixedParkingObstacle(RoadOccupant o) => o.Car == null ||
            o.Car.Parked || o.Car.Derelict || o.Car.Wrecked;

        bool FailedKerbSpot(float s)
        {
            for (int i = _failedKerbSpots.Count - 1; i >= 0; i--)
            {
                var failed = _failedKerbSpots[i];
                if (RoadCarSimulation.Now >= failed.y) { _failedKerbSpots.RemoveAt(i); continue; }
                if (Mathf.Abs(s - failed.x) < 1.5f) return true;
            }
            return false;
        }

        // Include any pending kerb exit or overtaking return before selecting an entry.
        float ParkingEntryFloor(float fromD)
        {
            float travel = 0f;
            float returnD = D;
            if (Sliding)
            {
                travel = Mathf.Max(0f, (_sFrom - S) * Heading + _sLen + Axle);
                returnD = _dTo;
            }
            if (_man == Manoeuvre.Pass && Mathf.Abs(returnD - fromD) >= .3f)
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
                        Mathf.Max(S, centre) + HalfLen, kerb - HalfWide, kerb + HalfWide, stationaryOnly: true)) return false;
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

        bool ParkingDestinationTaken(bool stationaryOnly = false)
        {
            if (!ParkingSpotAvailable(_goalRoad.Pose(_goalS, _goalD))) return true;
            if (ParkingReservationTaken(_goalS, _goalD) ||
                RoadSpace.Inside(this, _goalRoad.Pose(_goalS, _goalD),
                    _goalRoad.DirAt(_goalS) * _goalHeading, HalfLen, HalfWide, out _, stationaryOnly) != null) return true;
            foreach (var o in _goalRoad.Occupants)
                if (!ReferenceEquals(o.Who, this) && FixedParkingObstacle(o) &&
                    o.BodyS0 < _goalS + HalfLen + .3f && o.BodyS1 > _goalS - HalfLen - .3f &&
                    o.BodyOverlaps(_goalD - HalfWide - .2f, _goalD + HalfWide + .2f)) return true;
            return false;
        }

        // Approaching cars reserve their destinations before becoming parked obstacles.
        bool ArrivingNeighbour(RoadCar other) => !other.Wrecked && !other.Derelict &&
            other._hasGoal && other._goalPark && other._parkPlanReady && other._goalRoad == _goalRoad;

        void CollectParkingNeighbours()
        {
            _parkingNeighbours.Clear();
            foreach (var other in Registered)
                if (other != this && !other.Gone &&
                    (ArrivingNeighbour(other) || other.Parked && other.Road == _goalRoad))
                    _parkingNeighbours.Add(other);
        }

        bool ParkingReservationTaken(float centre, float kerb, bool refresh = true)
        {
            if (refresh) CollectParkingNeighbours();
            var position = _goalRoad.Pose(centre, kerb);
            var forward = _goalRoad.DirAt(centre) * _goalHeading;
            foreach (var other in _parkingNeighbours)
            {
                bool arriving = ArrivingNeighbour(other);
                float s = arriving ? other._goalS : other.S;
                float d = arriving ? other._goalD : other.D;
                int heading = arriving ? other._goalHeading : other.Heading;
                var lane = _goalRoad.LaneFor(heading, d);
                if (lane == null) continue;
                float reach = Mathf.Max(SlideLength(Mathf.Abs(kerb) + HalfWide + Profile.OverCrown, 0f) + Axle,
                    other.SlideLength(Mathf.Abs(d) + other.HalfWide + other.Profile.OverCrown, 0f) + other.Axle) + 4f;
                if (Mathf.Abs(centre - s) > reach + HalfLen + other.HalfLen + SideAir) continue;
                // Protect both the reserved body and the neighbour's forward exit.
                if (!other.KerbExitClearOf(_goalRoad, heading, s, d,
                    position, forward, HalfLen, HalfWide)) return true;
                if (arriving && !KerbExitClearOf(_goalRoad, _goalHeading, centre, kerb,
                    _goalRoad.Pose(s, d), _goalRoad.DirAt(s) * heading,
                    other.HalfLen, other.HalfWide)) return true;
            }
            return false;
        }

        bool KerbExitClearOf(Carriageway road, int heading, float s, float d,
            Vector3 obstacle, Vector3 facing, float halfLength, float halfWidth)
            => TryKerbExit(road, heading, s, d, out _, out _,
                obstacle, facing, halfLength, halfWidth);

        // Admission and departure share a checked exit onto the lane or passing line.
        bool TryKerbExit(Carriageway road, int heading, float s, float d,
            out float target, out float length, Vector3? obstacle = null,
            Vector3 facing = default, float halfLength = 0f, float halfWidth = 0f)
        {
            var lane = road.LaneFor(heading, d);
            target = d;
            length = 0f;
            if (lane == null) return false;
            for (int shift = 0; shift <= 8; shift++)
            {
                target = lane.Offset - heading * shift * .5f;
                if (shift > 0 && (!Profile.PassesAtKerb || road.MedianHalf > 0f ||
                    target * heading - HalfWide < -Profile.OverCrown)) break;
                if (!road.Drivable(target, HalfWide)) continue;
                length = SlideLength(Mathf.Abs(target - d), 0f);
                if (SlidePathClear(road, heading, s, d, target, length, 4f,
                    true, obstacle, facing, halfLength, halfWidth)) return true;
            }
            return false;
        }

        bool KerbExitClear(float centre, float kerb) =>
            TryKerbExit(_goalRoad, _goalHeading, centre, kerb, out _, out _);

        bool ParkingCanComplete()
        {
            if (!ParkingDestinationTaken() && KerbExitClear(S, D) &&
                RoadSpace.Inside(this, Position, Forward, HalfLen, HalfWide, out _) == null) return true;
            RejectKerbApproach();
            if (!FindParking(aheadOnly: true)) FailParking();
            return false;
        }

        // Only destination overlap means "our spot"; approach obstacles must be passed.
        bool IsOurParkingSpot(RoadOccupant o)
        {
            if (!_hasGoal || !_goalPark || Road != _goalRoad || Heading != _goalHeading || o == null || !o.Parked) return false;
            return o.BodyS0 < _goalS + HalfLen && o.BodyS1 > _goalS - HalfLen &&
                o.BodyOverlaps(_goalD - HalfWide, _goalD + HalfWide);
        }

        // ------------------------------------------------------------------ parking

        void RejectKerbApproach()
        {
            if (!FailedKerbSpot(_goalS)) _failedKerbSpots.Add(new Vector2(_goalS, RoadCarSimulation.Now + 6f));
            _parkPlanReady = false;
            _spotCheck = 0f;
            _parkTrafficCheck = 0f;
        }

        // Retrace the driven curve; rebasing a slide would rotate the stopped body.
        void RetreatFromKerb()
        {
            if (_man == Manoeuvre.PullIn) RejectKerbApproach();
            _pullOutWanted = false;
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
            if (_parkingRetryBy <= 0f)
            {
                _failedParkingMan = _parkingRetreat ? Manoeuvre.PullIn : _man;
                _parkingRetryBy = RoadCarSimulation.Now + PullOutPatience;
            }
            _parkReturnApproach = false;
            _parkPlanReady = false;
            _pullOutWanted = false;
            if (KeepsFailedParkingCurve)
            {
                if (_man == Manoeuvre.Reverse)
                {
                    _backLeft = 0f;
                    Speed = 0f;
                    _man = Manoeuvre.None;
                    ClearClaim();
                }
                _parkingRetreat = false;
                Halt(false);
            }
            else
            {
                _hasGoal = false;
                Route = null;
                _turnFirst = false;
                _freeGoal = null;
                if (!Sliding && _man != Manoeuvre.Reverse)
                { _man = Manoeuvre.None; ClearClaim(); }
            }
            ParkingFailed = true;
            if (ParkingReason == "" || ParkingReason == "Approaching parking")
                ParkingReason = "Parking manoeuvre unavailable";
            Why = ParkingReason;
            OnParkingFailed();
            // Do not call OnArrived or mark a traffic lane as parked.
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "parking unavailable", ManFields());
        }

        void BeginPullIn()
        {
            _man = Manoeuvre.PullIn;
            _pullInAsked = RoadCarSimulation.Now;
            // Drive the checked curve, including its straight approach before the start.
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
            _exitAdvance = false;
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

        /// <summary>Hold after backing out of a blocked junction.</summary>
        const float BoxHold = 4f;
        float _boxHoldUntil;

        bool _pullOutWanted, _exitAdvance;
        float _pullOutAsked;
        float _pullInAsked;

        /// <summary>Hold after yielding a stalled lateral move.</summary>
        const float PullOutPatience = 6f;

        /// <summary>Busy-street wait before edging onto the checked fixed-body path.</summary>
        const float PullOutGiveUp = 20f;

        // ------------------------------------------------------------------ the pull-out, ticked

        /// <summary>Called by TickRoad through Decide: the pull-out waits for the lane.</summary>
        void TickPullOut()
        {
            if (!_pullOutWanted) return;
            if (!TryKerbExit(Road, Heading, S, D, out float target, out float len))
            {
                AskRoomToPullOut();
                // A blocked lane need not block the kerb ahead. Drive a checked
                // straight segment, then ask for the exit again from there.
                if (_pullOutWanted && RoadCarSimulation.Now - _pullOutAsked > PullOutPatience &&
                    BandFree(D - HalfWide - SideAir, D + HalfWide + SideAir, 8f, 2f, out _) &&
                    SlidePathClear(D, 8f, 0f))
                {
                    _exitAdvance = true;
                    _pullOutWanted = false;
                    Slide(D, 8f);
                }
                return;
            }
            float lo = target - HalfWide - SideAir, hi = target + HalfWide + SideAir;
            // After the busy-street timeout the driver edges onto the checked
            // fixed-body path; normal following and the collision belt still apply.
            bool impatient = RoadCarSimulation.Now - _pullOutAsked > PullOutGiveUp;
            if (!impatient && (!BandFree(lo, hi, len + 4f, 2f, out _, allowParkedBeyond: true) ||
                !SlidePathClear(target, len, 4f))) return;
            _pullOutWanted = false;
            _manD = target;
            _manPastS = S + Heading * (len + Axle);
            foreach (var other in Road.Occupants)
                if (!ReferenceEquals(other.Who, this) && FixedParkingObstacle(other) &&
                    other.BodyOverlaps(_laneD - HalfWide - SideAir, _laneD + HalfWide + SideAir))
                {
                    float far = Heading > 0 ? other.BodyS1 : other.BodyS0;
                    if ((far - S) * Heading >= 0f && (far - S) * Heading < len + Axle + 8f)
                        _manPastS = Heading > 0 ? Mathf.Max(_manPastS, far + HalfLen * 2f + 3f)
                            : Mathf.Min(_manPastS, far - HalfLen * 2f - 3f);
                }
            Slide(target, len);
            Claim(S - Heading * HalfLen, _manPastS + Heading * HalfLen,
                Mathf.Min(lo, D - HalfWide), Mathf.Max(hi, D + HalfWide));
        }

        /// <summary>Back up within available space before retrying a blocked exit.</summary>
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
