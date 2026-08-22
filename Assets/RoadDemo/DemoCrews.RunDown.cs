using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a car does to a man who will not get out of its way.
    ///
    /// The scene this exists to end: a rival's man walks off the pavement, stops dead in
    /// front of the outfit's bonnet, and the car - which brakes for anybody in the road,
    /// as a car should - stops four metres short of him. Then the two of them shoot at
    /// each other through the windscreen until one is down. Nothing about that is a
    /// gunfight and nothing about it is what a driver would do.
    ///
    /// Two halves, and they are deliberately apart. The BRAKE is CrewCar.GivesWayTo: a
    /// crew with a fight on does not brake for the men it is fighting (it still brakes
    /// for everybody else, and always for the law). The IMPACT is here: a vehicle of the
    /// outfit's carrying men, moving at a pace that would hurt, with an enemy inside its
    /// own footprint - he goes under it.
    ///
    /// It is a big hit rather than an instant kill so that the same rules cover it as
    /// cover a round: a man may be put down, may be killed outright by a second one, and
    /// the street hears it and reacts (StreetAlarm). And it is bounded - one man per
    /// vehicle per beat - because a car crossing a crowd is a car, not a scythe.
    /// </summary>
    public partial class DemoCrews
    {
        /// <summary>Under this it is a shove, not a knock-down: a car creeping in a
        /// queue does not kill people it touches.</summary>
        public static float RunDownSpeed = 4f;

        /// <summary>What it does to him. A man carries three or four hits (HoodHealth,
        /// BossHealth), so this puts anybody down and kills most - which is what being
        /// hit by a car at twenty kilometres an hour does.</summary>
        public static int RunDownDamage = 3;

        /// <summary>Seconds before the same vehicle can knock anybody else down. Without
        /// it a car driving down a line of men bowls the lot in one frame.</summary>
        public static float RunDownEvery = 0.7f;

        /// <summary>How far past its own nose a vehicle reaches to catch a man - a
        /// little, because the impact should read as the bonnet arriving rather than as
        /// a man dying near a car.</summary>
        public const float RunDownReach = 0.35f;

        readonly System.Collections.Generic.Dictionary<RoadCar, float> _lastRunDown =
            new System.Collections.Generic.Dictionary<RoadCar, float>();

        void TickRunDown()
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                var car = Cars[i];
                if (car == null || car.Civic) continue;
                RunDown(car, car.Occupant ?? car.Owner, car.HalfLength, car.HalfWidth);
            }
            for (int i = 0; i < Bikes.Count; i++)
            {
                var bike = Bikes[i];
                if (bike == null) continue;
                RunDown(bike, bike.Occupant ?? bike.Owner, bike.HalfLen, bike.HalfWide);
            }
        }

        readonly System.Collections.Generic.List<Unit> _quarry =
            new System.Collections.Generic.List<Unit>();

        /// <summary>Every crew this vehicle is at war with - and it must be EVERY one it
        /// declines to brake for, or the two halves disagree and a vehicle drives
        /// straight through a man it refused to stop for without touching him. Which is
        /// exactly what happened: a machine on a pass would not brake for the crew it had
        /// come to shoot (CrewBike.GivesWayTo reads DriveByTarget) while the run-down
        /// only ever looked at TargetUnit, which a drive-by never sets. Sixty runs of the
        /// soak, not one man knocked down.</summary>
        void QuarrelsOf(RoadCar vehicle, Unit unit)
        {
            _quarry.Clear();
            void Add(Unit u)
            {
                if (u == null || u == unit || u.Faction == unit.Faction || u.IsPolice) return;
                if (!_quarry.Contains(u)) _quarry.Add(u);
            }

            Add(unit.TargetUnit);
            if (vehicle is CrewBike bike) Add(bike.DriveByTarget);
            if (vehicle is CrewCar car) Add(car.DriveByTarget);
            // and whoever has picked a fight with US: a standoff is mutual, and the man
            // stood in front of the bonnet is usually the one who came looking
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].TargetUnit == unit) Add(Units[i]);
        }

        void RunDown(RoadCar vehicle, Unit unit, float halfLength, float halfWidth)
        {
            if (vehicle == null || vehicle.Tf == null || unit == null) return;
            if (Mathf.Abs(vehicle.Speed) < RunDownSpeed) return;

            QuarrelsOf(vehicle, unit);
            if (_quarry.Count == 0) return;

            if (_lastRunDown.TryGetValue(vehicle, out var last) &&
                Time.time - last < RunDownEvery)
                return;

            var at = vehicle.Position;
            var fwd = vehicle.Tf.forward;
            var side = Vector3.Cross(Vector3.up, fwd);

            foreach (var quarrel in _quarry)
            foreach (var man in quarrel.All())
            {
                if (man == null || man.Dead || man.Tf == null || IsAboard(man) || man.Riding)
                    continue;

                var d = man.Tf.position - at;
                d.y = 0f;
                // inside the body's own rectangle, nose first
                float along = Vector3.Dot(d, fwd);
                if (along < -halfLength || along > halfLength + RunDownReach) continue;
                if (Mathf.Abs(Vector3.Dot(d, side)) > halfWidth + 0.25f) continue;

                Struck(vehicle, man);
                _lastRunDown[vehicle] = Time.time;
                return;
            }
        }

        /// <summary>He goes under it. The driver is named as the man who did it so the
        /// books, the nerve and the alarm all read it the way they read a round - a crew
        /// that watches a mate go under a bonnet breaks the same way.</summary>
        void Struck(RoadCar vehicle, CrewWalker man)
        {
            var driver = DriverOf(vehicle);
            man.TakeHit(RunDownDamage, driver);
            CrewGore.Hit(man, vehicle.Position, GroundY, floor: true);

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", driver != null ? driver.DisplayName : vehicle.Tag);
                DriveTrace.Str(sb, "what", "ran down " + man.DisplayName);
                DriveTrace.Num(sb, "v", Mathf.Abs(vehicle.Speed));
                DriveTrace.Bool(sb, "dead", man.Dead);
                DriveTrace.Vec(sb, "p", vehicle.Position);
                DriveTrace.Row("rundown", sb.ToString());
            }

            if (!man.Dead)
            {
                man.UnderFire();
                return;
            }

            CrewGore.Death(man, GroundY, floor: true);
            _deaths.Add((man, Time.time + DeathReportDelay));
            StreetAlarm.Death(man.Tf.position, StreetAlarm.DeathOf.Gangster);
            CrewOverlay.Announce(Surname(man.DisplayName).ToUpperInvariant() + " WENT UNDER THE WHEELS",
                3.5f, new Color(1f, 0.55f, 0.45f));
        }

        /// <summary>Whoever is at the wheel or the bars, or null - a machine on its own
        /// still runs a man down, it just has nobody to put it against.</summary>
        CrewWalker DriverOf(RoadCar vehicle)
        {
            if (vehicle is CrewBike bike) return bike.Rider;
            if (vehicle is CrewCar car)
                foreach (var man in car.Aboard)
                    if (car.SeatOf.TryGetValue(man, out int seat) && seat == 0) return man;
            return null;
        }
    }
}
