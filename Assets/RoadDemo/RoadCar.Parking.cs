using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The kerb: finding a free stretch of it to stand in, the swing into
    /// the spot, and the wait for a gap to pull out of it again.</summary>
    public partial class RoadCar
    {
        float _spotFrom;      // road-s where the free kerb stretch the car is parking in begins (travel sense)
        float _spotCheck;
        float _kerbHold;      // seconds stood at the kerb short of the spot
        const float KerbParkReach = 1.2f;

        /// <summary>An extra claim book for a specialised car. The road itself knows
        /// bodies and manoeuvre claims, but not destinations selected by another car
        /// that is still across town.</summary>
        protected virtual bool ParkingSpotAvailable(Vector3 at) => true;

        /// <summary>The parking chooser may move a requested point into another gap as
        /// traffic changes. Specialised cars can keep their destination claim in step.</summary>
        protected virtual void ParkingSpotSelected(Vector3 at) { }

        // The free stretch of kerb nearest the spot the car was sent to, long enough
        // to stand in: the claims in the kerb band (cars parked there, a prop) leave
        // gaps; the nearest gap within reach has the spot moved into it. Nothing near:
        // the car will stop in the lane at the spot (that is as near as it gets).
        //
        // aheadOnly is the re-pick on the approach: a gap BEHIND the car is a turn in
        // the road to reach, and the turn's completion puts the next re-pick behind the
        // car again - the circle a crew car was driven round for ever. On the approach
        // only gaps ahead are considered, and finding none keeps the spot already held
        // rather than degrading the goal.
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
                        float min = a + HalfLen + 0.8f, max = b - HalfLen - 0.8f;
                        // Ordinarily the first try is the old answer: the point in this
                        // gap closest to the requested s. A fleet claim may reject it,
                        // in which case walk out through the SAME free gap instead of
                        // pretending the whole kerb is occupied.
                        for (int step = 0; step <= 15; step++)
                            for (int direction = 0; direction < 2; direction++)
                            {
                                if (step == 0 && direction == 1) continue;
                                float offset = step * 3f * (direction == 0 ? 1f : -1f);
                                float centre = Mathf.Clamp(_goalS + offset, min, max);
                                float dist = Mathf.Abs(centre - _goalS);
                                // 2 m clear ahead: the overshoot that asks for a turn
                                // needs -3, so a re-pick cannot flip the goal behind us.
                                bool behind = aheadOnly && (centre - S) * h < 2f;
                                if (behind || dist >= bestDist) continue;
                                var at = road.Pose(centre, kerb);
                                if (!ParkingSpotAvailable(at)) continue;
                                bestDist = dist;
                                bestS = centre;
                                bestFrom = h > 0 ? a : b;
                            }
                    }
                }
                end = Mathf.Max(end, t.y);
            }
            if (float.IsNaN(bestS) || bestDist > 45f)
            {
                // on the approach the spot already held is the answer when nothing
                // ahead is free: somebody standing in it stops the car behind him and
                // that IS the parking (IsOurParkingSpot), or the 25 s give-up ends it -
                // degrading the goal to the lane here moved it about instead
                if (aheadOnly) return false;
                _goalD = _goalLane != null ? _goalLane.Offset : kerb;  // no kerb to be had: the lane it is
                _spotFrom = float.NaN;
                return false;
            }
            _goalS = bestS;
            _goalD = kerb;
            _spotFrom = bestFrom;
            ParkingSpotSelected(road.Pose(bestS, kerb));
            return true;
        }

        static readonly List<Vector2> _taken = new List<Vector2>();

        // Is the thing ahead stood at the kerb where we mean to pull in ourselves?
        // Then it is not gone round: we stop behind it, and that is where we park.
        bool IsOurParkingSpot(RoadOccupant o)
        {
            if (!_hasGoal || !_goalPark || Road != _goalRoad || Heading != _goalHeading || o == null || !o.Parked) return false;
            float oEnd = (Heading > 0 ? o.S1 : o.S0);
            return (oEnd - _goalS) * Heading > -8f;
        }

        // ------------------------------------------------------------------ parking

        /// <summary>The longest swing into the spot this kerb will take: the comfortable
        /// one, cut so that it STARTS past whatever stands at the kerb before the gap.
        ///
        /// Deliberately NOT cut by the road left to the spot. That was the old shape and
        /// it is what laid the swing from the wrong place: the length was trimmed to fit
        /// between a parked car and the gap, but the swing was still begun the moment the
        /// spot came within a comfortable length - so the car reached the kerb ten metres
        /// early and then drove ALONG it, into the car parked in front of its gap. How far
        /// short of the spot to begin is the trigger's business (TickRoad), and mixing the
        /// two questions is the bug.</summary>
        float PullInSlide()
        {
            float len = PullInLength();
            if (!float.IsNaN(_spotFrom)) len = Mathf.Min(len, (_goalS - _spotFrom) * Heading - HalfLen - 0.3f);
            return Mathf.Max(4f, len);
        }

        void BeginPullIn(float len)
        {
            _man = Manoeuvre.PullIn;
            _pullInAsked = Time.time;
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
                    if (!SlidePathClear(_laneD, len, 4f)) { AskRoomToPullOut(); return; }
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
