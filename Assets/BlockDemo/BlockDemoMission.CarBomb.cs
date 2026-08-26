using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    /// <summary>
    /// THE CAR BOMB, end to end, with nobody at the mouse.
    ///
    /// One charge, one car, and a car that is NOT the outfit's. A motor of the mark's is
    /// left standing at a kerb; the crew walks up to it, lays a charge under its nose
    /// (DemoCrews.OrderPlantBomb -> PlantedBomb) and walks away out of the blast and out
    /// of the mark's sight; then the mark is sent for his own car - he walks to it, opens
    /// the door, gets in - and the moment the wheels turn the charge springs under him
    /// (PlantedBomb -> Explosion): the car goes to scrap and the man who turned the key
    /// goes with it.
    ///
    /// That is the whole point of a laid charge as against a thrown one, and it is the
    /// half no other run tests: the bomb run (BlockDemoMission.bombRun) lays its charge
    /// under the OUTFIT'S own car and then drives that car off itself, which proves the
    /// trigger and nothing else. Here the charge has to survive being walked away from,
    /// wait through somebody else's walk across the quarter, and then kill the right man
    /// while the crew that laid it is forty metres off and unharmed.
    ///
    /// Judged on five things, each its own fault in the trace, so a soak says WHICH half
    /// broke:
    ///   nobombreach   - the crew could not get near enough to the car to lay it
    ///   nobombclear   - the crew would not walk back out of its own blast
    ///   earlybomb     - the charge went off before anybody had got into the car
    ///   nobombdriver  - the mark never came for his car, or never got into it
    ///   nobombspring  - the car was driven off and the charge never blew
    ///   nobombkill    - it blew, and the man at the wheel walked away from it
    ///   bombonus      - it took men of ours with it
    /// </summary>
    public partial class BlockDemoMission
    {
        [Tooltip("THE CAR BOMB. The crew lays one charge under a car belonging to a rival " +
                 "crew, walks clear of it, and the rival is then sent for his own car: he " +
                 "walks to it, gets in, drives off, and the charge springs under him. What " +
                 "is on trial is the whole of a laid charge - that it survives being left, " +
                 "that somebody else's key sets it off, and that it kills him and not us.")]
        public bool carBombRun;

        [Tooltip("Car bomb: metres the crew walks away from the charge before the mark is " +
                 "sent for his car. Must clear both the blast (6 m) and the range a rival " +
                 "crew opens fire at (24 m), or the run turns into a gunfight.")]
        [Min(10f)] public float carBombClearBy = 45f;

        [Tooltip("Car bomb: seconds any one leg of the run may take before it is a failure " +
                 "- the walk up, the walk clear, the mark's walk to his car, the drive. The " +
                 "longest of them is the mark's, and he walks it: allow a metre and a half " +
                 "a second over the length of the quarter.")]
        [Min(10f)] public float carBombPatience = 90f;

        [Tooltip("Car bomb: seconds to let the rest of the mark's crew climb in after the " +
                 "first man is seated, before the car is driven off. A carload is a better " +
                 "test of the blast than a driver on his own.")]
        [Min(0f)] public float carBombSettle = 8f;

        enum CarBombStep { WalkingUp, Clearing, Calling, Boarding, Driving }

        CarBombStep _cbStep;
        float _cbAt, _cbFirstIn, _cbCallAgain;
        int _cbCalls;
        DemoCrews.Unit _mark;
        Vector3 _cbClearTo;
        float _cbNextTry;
        int _oursAtStart, _cbDriveTries;
        bool _oursLost;
        readonly List<CrewWalker> _riders = new List<CrewWalker>();

        float InStep => Now - _cbAt;

        /// <summary>Metres between the crew and its own charge that count as clear. Thirty
        /// is enough for both things that matter - the blast (Explosion.Radius, 6 m) and
        /// the range a rival crew opens fire at (DemoCrews AlertRange, 24 m) - so a walk
        /// that goes further than that is welcome but not waited for.</summary>
        float SafeGap => Mathf.Clamp(carBombClearBy - 5f, Explosion.Radius + 4f, 30f);

        void CarBombStepTo(CarBombStep next, string what)
        {
            _cbStep = next;
            _cbAt = Now;
            Note("car bomb: " + what);
        }

        // ------------------------------------------------------------------ the setting

        /// <summary>Pick the mark, stand his car where he is not, and send the crew to it.
        /// The car goes on a kerb out beyond the outfit's own muster - away from the mark,
        /// so that his walk to it is a real walk and the crew has somewhere to retire to
        /// that is not through him.</summary>
        void StartCarBomb()
        {
            _mark = NearestRival(_ours.Position);
            if (_mark == null) { Give("there is no rival crew in the quarter to bomb"); return; }
            _quarry = _mark;   // the trace's "at" column follows the mark from here on

            // one charge and a spare, bought onto the ledger and given to the crew's
            // lieutenant the way a player buys them
            StockGrenades(2);

            var away = _ours.Position - _mark.Position;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector3.forward;

            // far enough off the muster that the crew has a walk to make of it, and on the
            // far side of the muster from the mark, so his walk to it is the length of the
            // quarter and does not end among the men who laid the charge
            _bombCar = StandACarFor(_mark, _ours.Position + away * 22f);
            if (_bombCar == null) { Give("no kerb to stand the mark's car on"); return; }

            _oursAtStart = _ours.Standing();
            _cbClearTo = ClearGround(_bombCar.Position, away);
            _cbDriveTries = 0;
            _cbFirstIn = 0f;
            _cbCallAgain = 0f;
            _cbCalls = 0;
            _oursLost = false;
            _riders.Clear();

            _crews.Select(_ours);
            // beside the car, not on top of it: the walk graph will not put a man where a
            // car is standing, and a leg that ends against the body it was aimed at is a
            // leg that reports itself stuck
            if (!_crews.MarchTo(_ours, _bombCar.Position - away * 5f))
            {
                Give("the crew would not take a walk order to the car");
                return;
            }

            State = Phase.Marching;
            _phaseAt = Now;
            CarBombStepTo(CarBombStep.WalkingUp,
                $"{_ours.GangName} sent to {_mark.GangName}'s {_bombCar.DisplayName}, " +
                $"{Vector3.Distance(_ours.Position, _bombCar.Position):F0} m away " +
                $"({Vector3.Distance(_mark.Position, _bombCar.Position):F0} m from its owner)");
        }

        /// <summary>Where the crew retires to once the charge is down: a point one clear
        /// radius off the charge, on the bearing that leaves the widest berth round every
        /// mob in the quarter, clamped inside the fence the scene laid
        /// (WalkObstacles.City - the ground the walk graph is built on).
        ///
        /// It is a SHORT walk on purpose, and that is the whole lesson of the runs before
        /// it. Sending the crew to the far corner of the quarter - the obvious reading of
        /// "walk clear" - marched it the length of the block, past the front of the very
        /// mob whose car it had just mined. They picked the outfit up inside AlertRange
        /// (24 m), dropped the boarding order they had been given, and chased. Ordering
        /// the boarding again every twelve seconds did not help: the trace shows the mark
        /// re-acquiring inside each of those twelve seconds and ending 127 m from his own
        /// car, further off with every order. A crew that walks forty-five metres and
        /// stops is a crew nobody picks up, and the charge is left to do its work.
        ///
        /// (The first attempt was cruder still: a point measured straight off the kerb,
        /// unclamped, which walked the crew off the end of the pavement and stopped it
        /// 26 m from its own charge, wanting 30.)</summary>
        Vector3 ClearGround(Vector3 charge, Vector3 away)
        {
            var flatCharge = Flat(charge);
            var best = flatCharge + away * carBombClearBy;
            float bestScore = float.MinValue;

            const int bearings = 16;
            for (int k = 0; k < bearings; k++)
            {
                var dir = Quaternion.Euler(0f, k * (360f / bearings), 0f) * away;
                var spot = Fenced(flatCharge + dir * carBombClearBy);
                float off = Vector3.Distance(spot, flatCharge);
                if (off < SafeGap) continue;   // the fence pushed it back into the blast

                // the berth this leaves every mob: where they stand when the crew stops,
                // and how near the walk passes them on the way. Both are capped at the
                // range they open fire from - past that it is all the same street.
                float berth = Wide;
                foreach (var unit in _crews.Units)
                {
                    if (unit == null || unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                    berth = Mathf.Min(berth, Vector3.Distance(Flat(unit.Position), spot));
                    berth = Mathf.Min(berth, DistanceToWalk(unit.Position, flatCharge, spot));
                }
                float score = berth * 1000f + off;
                if (score > bestScore) { bestScore = score; best = spot; }
            }
            return best;
        }

        /// <summary>A point pulled inside the quarter's own fence, well short of its hem -
        /// a walk order laid outside it ends where the pavement does, which is not where
        /// it was aimed.</summary>
        static Vector3 Fenced(Vector3 p)
        {
            if (WalkObstacles.City.Count == 0) return p;
            var r = WalkObstacles.City[0];
            const float margin = 12f;
            return new Vector3(
                Mathf.Clamp(p.x, r.xMin + margin, r.xMax - margin), 0f,
                Mathf.Clamp(p.z, r.yMin + margin, r.yMax - margin));
        }

        /// <summary>Metres of daylight to leave between the crew and the mobs: the range a
        /// rival crew opens fire at (DemoCrews AlertRange, 24 m) and a little over, because
        /// a walk is not the straight line it is measured as.</summary>
        const float Wide = 32f;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>How near a walk from <paramref name="from"/> to <paramref name="to"/>
        /// comes to a point, on the flat. The walk itself bends round the block, so this
        /// is the straight line it is judged on - near enough to choose a bearing by.</summary>
        static float DistanceToWalk(Vector3 point, Vector3 from, Vector3 to)
        {
            var line = Flat(to) - Flat(from);
            var toPoint = Flat(point) - Flat(from);
            float len2 = line.sqrMagnitude;
            if (len2 < 1e-4f) return toPoint.magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(toPoint, line) / len2);
            return (toPoint - line * t).magnitude;
        }

        /// <summary>Buy this crew's lieutenant that many grenades on the ledger - the real
        /// path, armory to lieutenant to hand - and cache the count on the unit for a
        /// scene that has no roster behind it at all.</summary>
        void StockGrenades(int need)
        {
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var crew = roster != null ? roster.FindCrew(_ours.CrewId) : null;
            if (crew != null)
            {
                for (int k = 0; k < need; k++)
                {
                    var item = LivingCity.Personnel.RosterOps.AddEquipment(
                        roster, LivingCity.Personnel.EquipmentKind.Grenade, "Grenade", 175);
                    item.OwnerId = crew.LieutenantId;
                    item.HolderId = crew.LieutenantId;
                }
            }
            _ours.Bombs = Mathf.Max(_ours.Bombs, need);   // BindBombs re-derives it where a roster exists
        }

        // ------------------------------------------------------------------ the run

        void TickCarBomb()
        {
            if (_bombCar == null || _bombCar.Tf == null) { Give("the mark's car has gone off the street"); return; }
            if (_ours == null) { Give("the crew that laid it is gone"); return; }
            // A CHARGE DOES NOT NEED THE MEN WHO LAID IT. Once it is down, the outfit
            // being shot to pieces at the other end of the quarter is a thing that
            // happened, not a reason to stop watching the car - and the run says so
            // rather than failing, because what is on trial is the bomb.
            if (_ours.Wiped && !_oursLost)
            {
                _oursLost = true;
                Note("car bomb: the crew that laid it has been wiped out - the charge stands");
            }

            switch (_cbStep)
            {
                case CarBombStep.WalkingUp: TickCarBombWalkUp(); break;
                case CarBombStep.Clearing: TickCarBombClear(); break;
                case CarBombStep.Calling: TickCarBombCall(); break;
                case CarBombStep.Boarding: TickCarBombBoard(); break;
                case CarBombStep.Driving: TickCarBombDrive(); break;
            }
            AimShotCam();
        }

        /// <summary>Up to the car and lay it. The plant is asked for through the same
        /// question the order card asks (CanBombPlant), so the run cannot lay a charge
        /// from anywhere a player could not.</summary>
        void TickCarBombWalkUp()
        {
            if (SprangEarly("while the crew was still walking up to it")) return;

            if (_ours.Wiped) { Give("the crew was wiped out before it could lay the charge"); return; }

            if (!_crews.CanBombPlant(_ours, _bombCar))
            {
                if (InStep > carBombPatience)
                {
                    Fault("nobombreach", $"{OurGap():F0} m off the car after {InStep:F0}s " +
                                         $"and still refused: {_crews.BombRefusal ?? "?"}");
                    Give("the crew never got near enough to lay the charge");
                }
                return;
            }

            _crews.Select(_ours);
            if (!_crews.OrderPlantBomb(_bombCar))
            {
                Give("the plant was refused: " + (_crews.BombRefusal ?? "?"));
                return;
            }

            // and straight back out of it: a man who lays a charge and stands over it is
            // in the blast his own crew set
            if (!_crews.MarchTo(_ours, _cbClearTo))
            {
                Give("the crew would not walk away from the charge it had laid");
                return;
            }
            CarBombStepTo(CarBombStep.Clearing,
                $"charge laid under the {_bombCar.DisplayName} from {OurGap():F0} m; " +
                $"the crew is walking {carBombClearBy:F0} m clear");
        }

        /// <summary>Away from it. Nothing is sprung on the mark until the men who laid it
        /// are out of the blast AND out of the range a rival opens fire at - otherwise
        /// what the run measures is a gunfight.</summary>
        void TickCarBombClear()
        {
            if (SprangEarly("while the crew was walking away from it")) return;

            float gap = OurGap();
            if (gap >= SafeGap)
            {
                CarBombStepTo(CarBombStep.Calling, $"the crew is {gap:F0} m off the car");
                return;
            }
            if (InStep > carBombPatience)
            {
                Fault("nobombclear", $"the crew was still {gap:F0} m from its own charge after " +
                                     $"{InStep:F0}s, wanting {SafeGap:F0} m");
                Give("the crew would not walk clear of the charge it laid");
            }
        }

        /// <summary>And now the man it was laid for is sent for his own car. This is the
        /// order a player never gives - the mark drives himself into it - so the run gives
        /// it on the quarter's behalf, through the same boarding the outfit uses.</summary>
        void TickCarBombCall()
        {
            if (SprangEarly("before the mark was ever sent for it")) return;
            if (_mark.Wiped) { Give("the mark's crew was wiped out before he came for his car"); return; }

            _crews.BoardCar(_mark, _bombCar);
            _cbCallAgain = Now + 12f;
            CarBombStepTo(CarBombStep.Boarding,
                $"{_mark.GangName} sent for his {_bombCar.DisplayName}, " +
                $"{Vector3.Distance(_mark.Position, _bombCar.Position):F0} m away");
        }

        /// <summary>He walks to it and gets in. The charge must sit through all of it -
        /// a charge that goes off at a door being opened is a charge that would have
        /// killed the crew that laid it.</summary>
        void TickCarBombBoard()
        {
            if (SprangEarly("with nobody at the wheel")) return;

            if (_bombCar.Aboard.Count > 0)
            {
                Remember();
                if (_cbFirstIn <= 0f) _cbFirstIn = Now;
                // the rest of his crew are still walking to their doors: a carload is a
                // truer test of the blast than one man at the wheel, so the car is given
                // a moment before it pulls away - but only a moment, and never once
                // everybody who is coming is in
                bool full = _bombCar.Aboard.Count >= _mark.Standing();
                if (!full && Now - _cbFirstIn < carBombSettle) return;

                // the tally our own losses are read against is taken HERE, not at the
                // start: a man of ours shot by some other mob while the mark walked to
                // his car is not the charge taking its own crew
                _oursAtStart = _ours.Standing();
                CarBombStepTo(CarBombStep.Driving,
                    $"{_riders.Count} of {_mark.GangName}'s men are in the car - it is driven off");
                DriveItOff();
                return;
            }
            if (_mark.Wiped) { Give("the mark's crew was wiped out on the way to the car"); return; }

            // A MAN WHO IS SHOT AT STOPS WALKING TO HIS CAR - and never starts again. The
            // mob keeps unit.Boarding pointing at the car (nothing clears it), so the
            // order LOOKS alive; what it loses is the men's feet, which TickCombat
            // overwrites every frame with the fight. Watched from the trace: the mark
            // walked 18 m toward his door, was picked up by the outfit crossing the
            // quarter, and spent the next ninety seconds walking the other way. Ordering
            // the boarding again is what stands the fight down (Board clears TargetUnit),
            // and it is what a car bomb waits on anyway: the man comes back to his own
            // motor sooner or later.
            if ((_mark.Boarding != _bombCar || _mark.TargetUnit != null) && Now >= _cbCallAgain)
            {
                _cbCallAgain = Now + 12f;
                _cbCalls++;
                _crews.BoardCar(_mark, _bombCar);
                Note($"car bomb: {_mark.GangName} had let his car go - sent for it again " +
                     $"({_cbCalls} time{(_cbCalls == 1 ? "" : "s")}, " +
                     $"{Vector3.Distance(_mark.Position, _bombCar.Position):F0} m off it)");
            }

            if (InStep > carBombPatience)
            {
                Fault("nobombdriver", $"nobody of {_mark.GangName} got into the car in {InStep:F0}s " +
                                      $"({Vector3.Distance(_mark.Position, _bombCar.Position):F0} m still to it)");
                Give("the mark never got into his car");
            }
        }

        /// <summary>The wheels turn and it goes. Everything the run exists for happens in
        /// the next second and a half.</summary>
        void TickCarBombDrive()
        {
            Remember();

            if (_bombCar.Wrecked) { JudgeCarBomb(); return; }

            // the order is given again while it is standing: a car that never moves never
            // springs anything, and that is a different failure from one that moves and
            // does not go off
            if (!_bombCar.Moving && Now >= _cbNextTry && _cbDriveTries < 5) DriveItOff();

            if (InStep > carBombPatience)
            {
                Fault("nobombspring", $"the car was driven for {InStep:F0}s and the charge never blew " +
                                      $"(v {_bombCar.RoadSpeed:F1}, {_bombCar.Why})");
                Give("the planted charge never sprang under the man who took the car");
            }
        }

        void DriveItOff()
        {
            _cbDriveTries++;
            _cbNextTry = Now + 8f;
            var nose = _bombCar.Tf != null ? _bombCar.Tf.forward : Vector3.forward;
            if (!_bombCar.GoTo(_bombCar.Position + nose * 60f, park: false))
                _bombCar.GoTo(_bombCar.Position - nose * 60f, park: false);
        }

        // ------------------------------------------------------------------ the verdict

        /// <summary>Who is in the car this instant. Held from frame to frame because the
        /// blast empties it: the men are put out of a wreck the moment it is one, so the
        /// last carload seen is the one to count the dead among.</summary>
        void Remember()
        {
            if (_bombCar.Aboard.Count == 0) return;
            _riders.Clear();
            _riders.AddRange(_bombCar.Aboard);
        }

        /// <summary>A charge that springs before there is a driver in the car has gone off
        /// on the wrong man - very possibly on the crew that laid it. Every step before
        /// the drive asks this.</summary>
        bool SprangEarly(string when)
        {
            if (!_bombCar.Wrecked) return false;
            Fault("earlybomb", "the charge sprang " + when);
            Give("the charge went off " + when);
            return true;
        }

        /// <summary>Metres from the crew's nearest man still standing to the car, or
        /// float.MaxValue with nobody left to measure - which reads as clear, and is.</summary>
        float OurGap()
        {
            var man = DemoCrews.NearestOf(_ours, _bombCar.Position);
            return man != null && man.Tf != null
                ? Vector3.Distance(man.Tf.position, _bombCar.Position)
                : float.MaxValue;
        }

        /// <summary>How far off the crew stood when it went up, for the telling.</summary>
        string GapSaid()
        {
            float gap = OurGap();
            return gap >= float.MaxValue * 0.5f ? "with nobody of the crew left standing"
                                                : $"{gap:F0} m off";
        }

        void JudgeCarBomb()
        {
            int dead = 0;
            for (int i = 0; i < _riders.Count; i++)
                if (_riders[i] != null && _riders[i].Dead) dead++;

            int oursNow = _ours.Standing();
            int lost = _oursLost ? 0 : _oursAtStart - oursNow;

            if (_riders.Count == 0)
            {
                Fault("nobombkill", "the car was blown to scrap with nobody in it");
            }
            else if (dead == 0)
            {
                Fault("nobombkill", $"the car was blown to scrap with {_riders.Count} of " +
                                    $"{_mark.GangName}'s men in it and not one of them was killed");
            }
            if (lost > 0)
                Fault("bombonus", $"the crew's own charge took {lost} of its {_oursAtStart} men, " +
                                  GapSaid());

            Go(Phase.Done, dead > 0 && lost == 0
                ? $"car bomb clean: {_mark.GangName}'s {_bombCar.DisplayName} blown with " +
                  $"{dead} of {_riders.Count} aboard killed, the crew that laid it {GapSaid()} " +
                  (_oursLost ? "(shot to pieces earlier in the run)" : $"and all {oursNow} still up")
                : $"car bomb done (aboard {_riders.Count}, killed {dead}, ours lost {lost})");
        }
    }
}
