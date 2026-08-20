using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A motorcycle of the outfit's: a hood at the bars, his mate behind him with the
    /// gun. CrewCar's opposite number, and the reason the whole two-wheeler business is
    /// worth the trouble - a drive-by from a car is a gun out of a window, hemmed in by
    /// the seat it is fired from and blind to its own side of the street, and a
    /// drive-by from a pillion is a man who can turn round and shoot down either
    /// pavement and off the back of the bike as it goes.
    ///
    /// So the rules a car's riders fire under are not this one's. A car's man may only
    /// shoot out of his own window, within sixty degrees of abeam and on his own side
    /// (DemoCrews.TickRiders); a pillion may shoot all the way round except through the
    /// man in front of him. What he cannot do is shoot while the bike is standing on
    /// its stand, and what the RIDER cannot do is shoot at all with both hands on the
    /// bars - he takes a hand off the bar only when the bike has slowed to walking
    /// pace and there is nobody behind him to do it for him.
    /// </summary>
    public sealed class CrewBike : RoadBike
    {
        public enum Mode { Parked, Riding, DriveBy }

        /// <summary>The crew whose lieutenant owns it; the crew on it. The book's
        /// side of a bike is nobody's yet - the ledger sells cars, not motorcycles -
        /// so these are set by whoever puts a crew on one.</summary>
        public DemoCrews.Unit Owner, Occupant;

        /// <summary>The arena, for the one thing a bike cannot do for itself: resolve a
        /// shot. Without it the guns come up and nothing is fired, which is a fair way
        /// for an optional wiring to fail.</summary>
        public DemoCrews Arena;

        public CrewWalker Rider { get; private set; }
        public CrewWalker Pillion { get; private set; }

        /// <summary>The crew being shot up, or null.</summary>
        public DemoCrews.Unit DriveByTarget { get; private set; }

        /// <summary>In a fight on the way somewhere: the rider puts it on and goes round
        /// whatever is in front of him at once. A drive-by is hot on its own.</summary>
        public bool Hot;

        /// <summary>How far past the mark a pass runs before it turns round.</summary>
        public const float PassOvershoot = 44f;

        /// <summary>Walking pace past the mark. A pistol reaches ten metres and a bike
        /// at the hot pace crosses that in half a second, which is a pass with nothing
        /// fired - the same arithmetic that slows the car (CrewCar.LimitTarget).</summary>
        public const float PassSpeed = 8.5f;

        /// <summary>How far round himself a pillion may shoot. Nearly everywhere: what
        /// is barred is the cone through the rider's back.</summary>
        public static float PillionBlindArc = 34f;

        int _passDir = 1;
        BikePose _riderPose, _pillionPose;
        Transform _riderHome, _pillionHome;
        float _riderShot, _pillionShot;

        public CrewBike()
        {
            Profile = DriverProfile.Gangster;
            Tag = "crewbike";
        }

        public Mode State =>
            DriveByTarget != null ? Mode.DriveBy
            : HasGoal || FreeGoal.HasValue || Mathf.Abs(Speed) > 0.05f ? Mode.Riding
            : Mode.Parked;

        public bool Moving => State != Mode.Parked || Mathf.Abs(Speed) > 0.05f;
        public int FreeSeats => (Rider == null ? 1 : 0) + (Pillion == null && Body != null && Body.SeatsTwo ? 1 : 0);

        // ------------------------------------------------------------------ mounting

        /// <summary>Put this man on it - at the bars if it is free, else behind. False
        /// when there is no room, or the body will not take a rider's pose.</summary>
        public bool Mount(CrewWalker man)
        {
            if (man == null || man.Tf == null || man.Dead) return false;
            if (Rider == null) return Mount(man, pillion: false);
            if (Pillion == null && Body != null && Body.SeatsTwo) return Mount(man, pillion: true);
            return false;
        }

        public bool Mount(CrewWalker man, bool pillion)
        {
            if (man == null || man.Tf == null || man.Dead || Body == null || Tilt == null) return false;
            if (pillion ? Pillion != null : Rider != null) return false;

            var pose = man.Tf.GetComponent<BikePose>();
            if (pose == null) pose = man.Tf.gameObject.AddComponent<BikePose>();
            if (!pose.Setup(Body, pillion))
            {
                Object.Destroy(pose);
                return false;
            }
            // astride, not sat in: his legs stay on and BikePose puts them on the pegs
            man.SetRiding(true, astride: true);
            var home = man.Tf.parent;
            man.Tf.SetParent(Tilt, worldPositionStays: false);
            man.Tf.localPosition = pillion ? Body.SaddlePillion : Body.SaddleRider;
            man.Tf.localRotation = Quaternion.identity;

            if (pillion)
            {
                Pillion = man;
                _pillionPose = pose;
                _pillionHome = home;
                pose.Rider = _riderPose;
            }
            else
            {
                Rider = man;
                _riderPose = pose;
                _riderHome = home;
                if (_pillionPose != null) _pillionPose.Rider = pose;
            }
            Take(pose);
            return true;
        }

        /// <summary>Off it, stood on the road beside it on the kerb side.</summary>
        public void Dismount(CrewWalker man)
        {
            if (man == null) return;
            bool pillion = man == Pillion;
            if (!pillion && man != Rider) return;

            var pose = pillion ? _pillionPose : _riderPose;
            Drop(pose);
            if (pose != null) Object.Destroy(pose);
            if (man.Tf != null)
            {
                man.Tf.localScale = Vector3.one;   // BikePose may have taken him down to fit
                man.Tf.SetParent(pillion ? _pillionHome : _riderHome, worldPositionStays: true);
                var side = Tf != null ? Tf.right : Vector3.right;
                var at = Position + side * (HalfWide + 0.8f);
                man.Tf.SetPositionAndRotation(new Vector3(at.x, RoadY, at.z),
                    Quaternion.LookRotation(Tf != null ? Tf.forward : Vector3.forward, Vector3.up));
            }
            man.SetRiding(false);

            if (pillion) { Pillion = null; _pillionPose = null; }
            else
            {
                Rider = null;
                _riderPose = null;
                if (_pillionPose != null) _pillionPose.Rider = null;
                // nobody steering: it is not going anywhere
                Halt(hard: true);
            }
        }

        public void DismountAll()
        {
            Dismount(Pillion);
            Dismount(Rider);
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Ride there and stop at the kerb nearest it.</summary>
        public void RideTo(Vector3 point)
        {
            DriveByTarget = null;
            Profile = Hot ? DriverProfile.Hot : DriverProfile.Gangster;
            if (!OnRoad || Net == null) { GoFree(new Vector3(point.x, RoadY, point.z)); return; }
            if (!GoTo(point, park: true)) GoFree(new Vector3(point.x, RoadY, point.z));
        }

        /// <summary>Shoot the place up: passes along the street past this crew, a turn
        /// at the end of each, until told otherwise or nobody is left standing.</summary>
        public void DriveBy(DemoCrews.Unit target)
        {
            if (target == null || Rider == null) return;
            DriveByTarget = target;
            Profile = DriverProfile.Hot;
            var t = target.Position;
            if (Road != null)
            {
                Road.Project(t, out float ts, out _);
                _passDir = (ts - S) * Heading >= 0f ? Heading : -Heading;
            }
            else _passDir = Vector3.Dot(t - Position, Forward) >= 0f ? 1 : -1;
            PlanPass();
        }

        void PlanPass()
        {
            if (DriveByTarget == null) return;
            var t = DriveByTarget.Position;
            if (Net == null || !OnRoad)
            {
                var f = t - Position; f.y = 0f; f.Normalize();
                GoFree(new Vector3(t.x, RoadY, t.z) + f * PassOvershoot);
                return;
            }
            var road = Net.Locate(t, out float ts, out float td, within: 14f);
            if (road == null) { RideTo(t); return; }
            int dir = Road == road ? _passDir : (td >= 0f ? 1 : -1);
            float endS = Mathf.Clamp(ts + dir * PassOvershoot, 8f, road.Length - 8f);
            var lane = road.LaneFor(dir, td) ?? road.LaneFor(-dir, td);
            if (lane == null) { RideTo(t); return; }
            _passDir = lane.Heading;
            GoTo(road.Pose(endS, lane.Offset), park: false, standOff: 0f, stopAtGoal: false);
        }

        protected override void OnArrived()
        {
            if (DriveByTarget == null) return;
            _passDir = -_passDir;
            PlanPass();
        }

        public void EndDriveBy()
        {
            DriveByTarget = null;
            RideTo(Position + Forward * 30f);
        }

        /// <summary>Both wheels stopped, here, now - the plan torn up.</summary>
        public void HardStop()
        {
            DriveByTarget = null;
            Halt(hard: true);
        }

        // ------------------------------------------------------------------ the frame

        public new void Tick(float dt)
        {
            if (Tf == null) return;
            Profile = DriveByTarget != null || Hot ? DriverProfile.Hot : DriverProfile.Gangster;
            base.Tick(dt);
            TickGuns(dt);
        }

        protected override float LimitTarget(float target)
        {
            if (DriveByTarget == null || Tf == null) return target;
            var to = DriveByTarget.Position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 45f) return target;
            return Mathf.Min(target, Mathf.Lerp(PassSpeed, target, Mathf.InverseLerp(20f, 45f, dist)));
        }

        // Who is firing, at whom, and whether he can see him past the man in front.
        void TickGuns(float dt)
        {
            var target = DriveByTarget;
            if (target == null)
            {
                Aim(_pillionPose, Pillion, null);
                Aim(_riderPose, Rider, null);
                return;
            }
            if (DemoCrews.Finished(target))
            {
                Aim(_pillionPose, Pillion, null);
                Aim(_riderPose, Rider, null);
                EndDriveBy();
                return;
            }

            var mark = DemoCrews.NearestOf(target, Position);
            if (mark == null || mark.Tf == null) { Aim(_pillionPose, Pillion, null); Aim(_riderPose, Rider, null); return; }

            // the pillion: all the way round except through the rider's back
            bool pillionOn = Pillion != null && !Pillion.Dead && Pillion.Armed && Sees(Pillion, mark, blindAhead: true);
            Aim(_pillionPose, Pillion, pillionOn ? mark : null);
            if (pillionOn) Shoot(Pillion, mark, ref _pillionShot, dt);
            else _pillionShot = 0f;

            // the rider: only with nobody behind him to do it, and only at a crawl -
            // a hand off the bar at speed puts the bike in a shop window
            bool riderOn = !pillionOn && Pillion == null && Rider != null && !Rider.Dead && Rider.Armed &&
                           Mathf.Abs(Speed) < PassSpeed * 0.8f && Sees(Rider, mark, blindAhead: false);
            Aim(_riderPose, Rider, riderOn ? mark : null);
            if (riderOn) Shoot(Rider, mark, ref _riderShot, dt);
            else _riderShot = 0f;
        }

        bool Sees(CrewWalker man, CrewWalker mark, bool blindAhead)
        {
            if (man == null || man.Tf == null || mark == null || mark.Tf == null) return false;
            var to = mark.Tf.position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > man.Ballistics.Range * 1.3f) return false;
            if (!blindAhead || dist < 0.1f) return true;
            // straight up the road is where the man in front of him is sitting
            float ahead = Vector3.Angle(Forward, to / dist);
            return ahead > PillionBlindArc;
        }

        void Aim(BikePose pose, CrewWalker man, CrewWalker mark)
        {
            if (pose != null) pose.AimAt = mark != null && mark.Tf != null ? mark.ChestPosition : (Vector3?)null;
            if (man == null) return;
            man.RidingAim = mark != null;
            man.AimAt(mark);
        }

        void Shoot(CrewWalker man, CrewWalker mark, ref float timer, float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = man.Ballistics.Interval;
            if (Arena != null) Arena.FireFrom(man, mark);
            else StreetAlarm.Report(Position, null, 0, man.Ballistics.Loudness);
        }

        public string StatusLine => State switch
        {
            Mode.DriveBy => DriveByTarget != null ? "Drive-by on " + DriveByTarget.GangName : "Drive-by",
            Mode.Riding => Hot ? "On the road, under fire" : "On the road",
            _ => Rider != null ? "Sat on the bike" : "On its stand",
        };
    }
}
