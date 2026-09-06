using System;
using UnityEngine;

namespace RoadDemo
{
    public partial class RoadCar
    {
        /// <summary>Resume autonomous driving without changing the body's current curve or pose.</summary>
        protected void ResumeTraffic()
        {
            Stop();
            _halted = _haltWhenClear = _keepGoalWhenHaltClear = _pullOutWanted = false;
            _parkingRetreat = _parkPlanReady = false;
            _backLeft = 0f;
            _freeGoal = null;
            if (Parked) PullOut();
        }

        /// <summary>Optional simulation override for the runtime map visibility service.</summary>
        public static Func<Vector3, bool> RecoveryVisibility;
        const float RecoveryDelay = 45f;
        const float RecoveryRetryDelay = 5f;
        const float HiddenRecoveryReach = 96f;
        const float VisibleRecoveryReach = 32f;
        public int TrafficRecoveries { get; private set; }
        public float LastTrafficRecoveryDistance { get; private set; }
        public bool LastTrafficRecoveryHidden { get; private set; }
        Vector3 _recoveryAnchor;
        float _recoveryStillFor;
        float _recoveryRetryFor;

        static bool RecoveryRevealed(Vector3 position) => RecoveryVisibility?.Invoke(position) ??
#if UNITY_5_3_OR_NEWER
            LivingCity.Gameplay.MapVisionRegistry.IsRevealed(position);
#else
            true;
#endif

        bool RecoveryBodyHidden(Vector3 position, Vector3 forward)
        {
            if (RecoveryRevealed(position)) return false;
            var right = Vector3.Cross(Vector3.up, forward);
            int along = Mathf.CeilToInt((HalfLen + 1f) * 2f);
            int across = Mathf.CeilToInt((HalfWide + 1f) * 2f);
            for (int end = 0; end <= along; end++)
                for (int side = 0; side <= across; side++)
                    if (RecoveryRevealed(position +
                        forward * Mathf.Lerp(-HalfLen - 1f, HalfLen + 1f, (float)end / along) +
                        right * Mathf.Lerp(-HalfWide - 1f, HalfWide + 1f, (float)side / across))) return false;
            return true;
        }

        void WatchTrafficRecovery(float dt)
        {
            if (!OnRoad || Parked || _halted || _haltWhenClear || Wrecked || Gone ||
                (_pos - _recoveryAnchor).sqrMagnitude > 1f)
            {
                _recoveryAnchor = _pos;
                _recoveryStillFor = 0f;
                _recoveryRetryFor = 0f;
                return;
            }
            _recoveryStillFor += dt;
            _recoveryRetryFor = Mathf.Max(0f, _recoveryRetryFor - dt);
            if (_recoveryStillFor < RecoveryDelay || _recoveryRetryFor > 0f) return;
            _recoveryRetryFor = RecoveryRetryDelay;
            bool hidden = RecoveryBodyHidden(_pos, _fwd);
            TryRecoverTraffic(hidden);
        }

        bool TryRecoverTraffic(bool hidden)
        {
            if (!OnRoad || Parked || _halted || _haltWhenClear || Wrecked || Gone) return false;
            float reach = hidden ? HiddenRecoveryReach : VisibleRecoveryReach;
            if (Road != null && (RecoveryOnRoad(Road, Heading, reach, hidden) ||
                RecoveryOnRoad(Road, -Heading, reach, hidden))) return true;
            var connector = Via ?? _via;
            if (connector == null) return false;
            return RecoveryOnRoad(connector.To.Road, connector.To.Heading, reach, hidden) ||
                RecoveryOnRoad(connector.From.Road, connector.From.Heading, reach, hidden);
        }

        bool RecoveryOnRoad(Carriageway road, int heading, float reach, bool hidden)
        {
            if (road == null || road.Elevated || road.Path != null) return false;
            road.Project(_pos, out float centre, out _);
            float margin = HalfLen + Axle + 2f;
            if (road.Length < margin * 2f) return false;
            for (int offset = 0; offset <= 64; offset++)
            {
                float shift = offset == 0 ? 0f : ((offset + 1) / 2) * 3f * (offset % 2 == 1 ? -1f : 1f);
                float station = Mathf.Clamp(centre + heading * shift, margin, road.Length - margin);
                if (_hasGoal && road == _goalRoad && heading == _goalHeading &&
                    (_goalS - S) * heading >= 0f && (_goalS - station) * heading < 2f) continue;
                foreach (var lane in road.Lanes)
                {
                    if (lane.Heading != heading || !road.Drivable(lane.Offset, HalfWide)) continue;
                    float axleStation = station - heading * Axle;
                    var forward = road.DirAt(axleStation) * heading;
                    var position = road.Pose(axleStation, lane.Offset) + forward * Axle;
                    float distance = (position - _pos).magnitude;
                    if (distance > reach || distance < .25f && Vector3.Angle(_fwd, forward) < 5f) continue;
                    // A clear destination can still cut directly into a moving queue.
                    // Admission must leave the follower its stopping distance.
                    var behind = road.Behind(_occ, heading, station - heading * HalfLen,
                        lane.Offset - HalfWide - .25f, lane.Offset + HalfWide + .25f, out float gap);
                    float approach = behind == null ? 0f : Mathf.Max(0f, behind.Vel * heading);
                    float braking = behind?.Car != null ? behind.Car.Brake : DriverProfile.Traffic.Brake;
                    if (approach > .1f && gap < approach * approach / (2f * Mathf.Max(1f, braking)) + approach * .3f + 3f)
                        continue;
                    if (hidden && (!RecoveryBodyHidden(_pos, _fwd) || !RecoveryBodyHidden(position, forward))) continue;
                    if (!hidden)
                    {
                        bool peopleClear = true;
                        float yaw = Vector3.SignedAngle(_fwd, forward, Vector3.up);
                        int steps = Mathf.Max(1, Mathf.Max(Mathf.CeilToInt(distance / .5f), Mathf.CeilToInt(Mathf.Abs(yaw) / 10f)));
                        for (int step = 0; step <= steps && peopleClear; step++)
                        {
                            float fraction = (float)step / steps;
                            var facing = Quaternion.Euler(0f, yaw * fraction, 0f) * _fwd;
                            peopleClear = RecoveryPeopleClear(Vector3.Lerp(_pos, position, fraction), facing);
                        }
                        if (!peopleClear) continue;
                    }
                    if (RoadSpace.Inside(this, position, forward, HalfLen + .75f, HalfWide + .25f, out _) != null ||
                        !RecoveryPeopleClear(position, forward)) continue;
                    var previous = _pos;
                    Spawn(lane, heading > 0 ? station : road.Length - station);
                    Speed = 0f;
                    Derelict = false;
                    _derelictFor = _stoodStillFor = _beltFor = _blockedFor = _jammed = _standoffFor = 0f;
                    _holdFor = _backLeft = _boxStuck = _heldAtLine = _shoved = 0f;
                    _giveUntil = 0f;
                    _backedFor = _jamLeader = null;
                    _backedAt = -999f;
                    _pullOutWanted = false;
                    _arcBacking = false;
                    _recoveryAnchor = _pos;
                    _recoveryStillFor = 0f;
                    _recoveryRetryFor = RecoveryRetryDelay;
                    Replan();
                    UpdateOccupant();
                    RoadSpace.Invalidate();
                    TrafficRecoveries++;
                    LastTrafficRecoveryDistance = (_pos - previous).magnitude;
                    LastTrafficRecoveryHidden = hidden;
                    if (DriveTrace.On) DriveTrace.Event("man", "car " + Id,
                        hidden ? "traffic recovery under fog" : "local traffic recovery", ManFields());
                    return true;
                }
            }
            return false;
        }

        bool RecoveryPeopleClear(Vector3 position, Vector3 forward)
        {
            var right = Vector3.Cross(Vector3.up, forward);
            bool Clear(Vector3 point)
            {
                var delta = point - position;
                return Mathf.Abs(Vector3.Dot(delta, forward)) > HalfLen + 1.2f ||
                    Mathf.Abs(Vector3.Dot(delta, right)) > HalfWide + 1.2f;
            }
            foreach (var point in StreetTraffic.Walkers) if (!Clear(point)) return false;
            foreach (var body in StreetTraffic.Bodies) if (!Clear(body.At)) return false;
            return true;
        }
    }
}
