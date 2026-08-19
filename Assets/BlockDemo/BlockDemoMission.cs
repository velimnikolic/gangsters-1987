using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    /// <summary>
    /// The lab plays the player.
    ///
    /// A run of the quarter with nobody at the mouse: the outfit gets into its car,
    /// is sent at one rival crew after another - which is a drive-by, the guns out
    /// of the windows, the car threading the traffic to get there - and when the
    /// last of them is down it is told to pull in at a kerb and stop. The whole
    /// thing is the clicks a person would make (DemoCrews.Select / OrderCar /
    /// OrderAttack / OrderSelected), nothing the game does not already offer.
    ///
    /// It exists so the driving can be judged by something that FINISHES: the car
    /// must never be stood still for long with somewhere to be, and the run ends
    /// with it parked. Every turn of the mission goes into the trace, and the two
    /// faults it watches for itself - stuck, and never arriving - are the ones the
    /// harness is looking for.
    /// </summary>
    public class BlockDemoMission : MonoBehaviour
    {
        public enum Phase { Waiting, Boarding, Hunting, Storming, Reboarding, Parking, Done, Failed }

        [Tooltip("Sim seconds after the quarter is up before the crew is sent for its car.")]
        public float startAfter = 10f;
        [Tooltip("Seconds allowed for the crew to get into the car before the run gives up on it.")]
        public float boardingPatience = 45f;
        [Tooltip("Seconds allowed on one rival crew before the order is given again.")]
        public float reorderEvery = 12f;
        [Tooltip("Seconds allowed on one rival crew before it is written off as a failure.")]
        public float crewPatience = 120f;
        [Tooltip("Seconds of passes at one crew before the men get out and finish it on " +
                 "foot - what a player does when a drive-by is not doing the job. The car " +
                 "pulls in at the kerb first; it is picked up again afterwards.")]
        public float passesBefore = 45f;
        [Tooltip("Seconds the car may stand still with somewhere to be before it counts as stuck.")]
        public float stuckAfter = 8f;

        public Phase State { get; private set; } = Phase.Waiting;

        DemoCrews _crews;
        DemoCrews.Unit _ours;
        CrewCar _car;
        DemoCrews.Unit _quarry;
        Vector3 _parkAt;
        float _phaseAt, _lastOrder, _stillFor, _nextRow, _saidStuck;
        int _killed, _stuckSpells;
        readonly List<string> _story = new List<string>();

        float Now => Time.timeSinceLevelLoad;
        float InPhase => Now - _phaseAt;

        void Start() => _phaseAt = Now;

        void Update()
        {
            if (State == Phase.Done || State == Phase.Failed) return;
            if (_crews == null)
            {
                _crews = FindAnyObjectByType<DemoCrews>();
                if (_crews == null) return;
            }

            // whatever else is going on: a crew that is dead is not on a job. Out of the
            // car and still alive is NOT the end of one - the driver being shot puts the
            // crew on the pavement, and the fight goes on from there.
            if (_ours != null && _ours.Wiped) { Give($"the outfit was wiped out ({_killed} crews down first)"); return; }

            switch (State)
            {
                case Phase.Waiting: TickWaiting(); break;
                case Phase.Boarding: TickBoarding(); break;
                case Phase.Hunting: TickHunting(); break;
                case Phase.Storming: TickStorming(); break;
                case Phase.Reboarding: TickReboarding(); break;
                case Phase.Parking: TickParking(); break;
            }

            WatchTheCar();
            Row();
        }

        // ------------------------------------------------------------------ the phases

        void TickWaiting()
        {
            if (Now < startAfter) return;

            _ours = null;
            foreach (var unit in _crews.Units)
                if (unit.Faction == 0 && !unit.Wiped) { _ours = unit; break; }
            if (_ours == null) { Give("there is no crew of the outfit in the quarter"); return; }

            _car = _crews.CarOf(_ours);
            if (_car == null)
                foreach (var car in _crews.Cars)
                    if (!car.Civic && car.Occupant == null) { _car = car; break; }
            if (_car == null) _car = StandACar();
            if (_car == null) { Give("the outfit has no car in the quarter"); return; }

            _crews.Select(_ours);
            _crews.BoardCar(_ours, _car);
            Go(Phase.Boarding, $"{_ours.GangName} sent to {_car.DisplayName}, " +
                               $"{Vector3.Distance(_ours.Position, _car.Position):F0} m away");
        }

        /// <summary>The quarter is a lab, not a saved game: the ledger has dealt nobody
        /// a car here, so one is stood at the kerb beside the lieutenant the same way
        /// the ledger's own would be (DemoCrews.AddCar onto a free kerb slot), and the
        /// outfit owns it.</summary>
        CrewCar StandACar()
        {
            var boss = _ours.Boss;
            if (boss == null || boss.Tf == null) return null;
            GameObject prefab = null;
            foreach (var name in LivingCity.Gameplay.VehicleCatalog.GangsterCars)
            {
                prefab = CrewCars.BodyNamed(name);
                if (prefab != null) break;
            }
            if (prefab == null) { Note("no mob car body in any pack"); return null; }

            CrewCars.MeasurePrefab(prefab, out float halfLength, out float halfWidth);
            var net = LaneNet.Active;
            if (!CrewCars.KerbSlotNear(net, boss.Tf.position, halfLength, halfWidth,
                    out var at, out var facing))
            {
                Note("no free kerb near the lieutenant to stand a car on");
                return null;
            }
            var car = _crews.AddCar(prefab, at, facing, -0.06f);
            if (car == null) return null;
            car.DisplayName = prefab.name;
            car.Owner = _ours;
            Note($"stood {car.DisplayName} at the kerb for {_ours.GangName}");
            return car;
        }

        /// <summary>Men of ours still alive in the car.</summary>
        int Aboard()
        {
            int n = 0;
            if (_car != null && _ours != null)
                foreach (var man in _ours.All()) if (!man.Dead && _car.Aboard.Contains(man)) n++;
            return n;
        }

        void TickBoarding()
        {
            int standing = _ours.Standing();
            int aboard = Aboard();

            // EVERY man who is coming, not just enough to drive off with: the guns fire
            // out of the windows they are sat at, so a crew that leaves a man on the
            // kerb leaves one side of the car unarmed - and the passes down that side
            // then go by without a shot, which is exactly what the early runs did.
            // Only when the patience is spent does the run take what it has.
            bool all = aboard >= Mathf.Min(standing, _car.Seats);
            if (all || (aboard > 0 && InPhase > boardingPatience)) { Hunt($"{aboard} of {standing} aboard"); return; }
            if (InPhase > boardingPatience * 1.5f) Give($"only {aboard} of {standing} got in within {boardingPatience * 1.5f:F0}s");
        }

        void TickHunting()
        {
            if (_quarry != null && !_quarry.Wiped)
            {
                if (InPhase > crewPatience)
                {
                    Fault("nokill", $"{_quarry.GangName} still standing after {crewPatience:F0}s");
                    _quarry = null;   // on to the next: the run is not held up by one crew
                    Hunt("given up on that one");
                    return;
                }
                // passes are not doing it: in at the kerb and finish the job on foot.
                // The guns out of the windows only bear for a second or two of each
                // pass, and a crew stood on a pavement shoots back the whole time - so
                // a drive-by that has not broken them in three quarters of a minute
                // is not going to.
                if (Aboard() > 0 && InPhase > passesBefore)
                {
                    _crews.Select(_ours);
                    _orderedOut = false;
                    _crews.OrderSelected(_quarry.Position, out var pullIn);
                    Go(Phase.Storming, $"the passes are not doing it - in at the kerb by " +
                                       $"{_quarry.GangName} ({Vector3.Distance(Car(), pullIn):F0} m)");
                    return;
                }

                // the order given again now and then: a drive-by that has been driven
                // past leaves the car idling otherwise
                if (Now - _lastOrder > reorderEvery)
                {
                    _crews.Select(_ours);
                    _crews.OrderAttack(_quarry);
                    _lastOrder = Now;
                    Note($"at {_quarry.GangName} again ({Vector3.Distance(Car(), _quarry.Position):F0} m" +
                         (Aboard() == 0 ? ", on foot" : "") + ")");
                }
                return;
            }

            if (_quarry != null) { _killed++; Note($"{_quarry.GangName} is down"); _quarry = null; }
            Hunt("next");
        }

        void Hunt(string why)
        {
            if (_ours.Wiped) { Give("the outfit was wiped out"); return; }

            DemoCrews.Unit next = null;
            float best = float.MaxValue;
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                float d = Vector3.SqrMagnitude(unit.Position - Car());
                if (d < best) { best = d; next = unit; }
            }

            if (next == null)
            {
                // nobody left: pull in somewhere and stop - the last thing the run wants
                // to see is a car that can put itself away
                // A crew that has finished a job pulls in where it stands - down this
                // street, at the kerb - not at a spot picked out of the far side of the
                // quarter. (Asked for one of those, the car toured the grid for two
                // minutes and never got closer than forty metres: worth chasing, but it
                // is not what a crew would be told to do.)
                _parkAt = Car() + (_car.Road != null ? _car.RoadForward : Vector3.forward) * 45f;
                var road = _car.Road;
                if (road != null)
                {
                    // well clear of both ends: a car left at the mouth of a street stands
                    // in the lane traffic comes out of the junction into, and the whole
                    // quarter queues behind it
                    float ahead = Mathf.Clamp(_car.S + _car.Heading * 45f, 28f, Mathf.Max(28f, road.Length - 28f));
                    _parkAt = road.Pose(ahead, road.KerbDOnSide(_car.D, _car.HalfWide));
                }
                if (_car != null && Aboard() == 0 && _ours.Standing() > 0)
                {
                    // they finished it on the pavement: back to the car before it is put away
                    _crews.Select(_ours);
                    _crews.BoardCar(_ours, _car);
                    _walkBack = Vector3.Distance(_ours.Position, _car.Position);
                    Go(Phase.Reboarding, $"{_killed} crews down - back to the car, {_walkBack:F0} m off");
                    return;
                }
                _crews.Select(_ours);
                _crews.OrderSelected(_parkAt, out _parkAt);
                Go(Phase.Parking, $"{_killed} crews down - putting the car away " +
                                  $"{Vector3.Distance(Car(), _parkAt):F0} m off");
                return;
            }

            // on the pavement with the next mob three streets away: back in the car
            // first. A crew does not walk across a quarter to a fight it owns a car for.
            if (_car != null && Aboard() == 0 && _ours.Standing() > 0 &&
                Vector3.Distance(_ours.Position, next.Position) > 60f)
            {
                _crews.Select(_ours);
                _crews.BoardCar(_ours, _car);
                _quarry = null;
                _walkBack = Vector3.Distance(_ours.Position, _car.Position);
                Go(Phase.Reboarding, $"{next.GangName} is {Vector3.Distance(_ours.Position, next.Position):F0} m off - " +
                                     $"back to the car, {_walkBack:F0} m off");
                return;
            }

            _quarry = next;
            _crews.Select(_ours);
            if (!_crews.OrderAttack(_quarry)) { Give("the attack order was refused"); return; }
            _lastOrder = Now;
            Go(Phase.Hunting, $"{why}: at {_quarry.GangName}, {Mathf.Sqrt(best):F0} m off");
        }

        /// <summary>In at the kerb beside the mark, everybody out, and at them on foot.
        /// The car is left standing where a car should be left - at the kerb - and is
        /// picked up again when the street is quiet.</summary>
        void TickStorming()
        {
            if (_quarry == null || _quarry.Wiped) { Hunt("they went down on the way in"); return; }
            if (Aboard() == 0)
            {
                _crews.Select(_ours);
                _crews.OrderAttack(_quarry);
                _lastOrder = Now;
                Go(Phase.Hunting, $"out of the car, at {_quarry.GangName} on foot");
                return;
            }
            // near enough, or stopped: out they get. ONCE - "get out" makes the car look
            // for a kerb to pull in at, and asking again every frame moves the kerb it
            // is looking for a car's length further on every frame, so it drives the
            // quarter for ever and nobody ever climbs down.
            float toMark = Vector3.Distance(Car(), _quarry.Position);
            bool close = (_car.Parked || Mathf.Abs(_car.Speed) < 0.2f) && toMark < 45f;
            if (!_orderedOut && (close || InPhase > 60f))
            {
                _orderedOut = true;
                _crews.Select(_ours);
                _crews.OrderOut();
                Note(close ? $"pulled in {toMark:F0} m off {_quarry.GangName} - everybody out"
                           : "it never got there - out where it stands");
                return;
            }
            if (InPhase > 120f) Give("the crew could not be put down beside the mark");
        }

        void TickReboarding()
        {
            // The walk back is as long as the fight took them from the car - a quarter
            // of the city on foot, at a walking pace - so the allowance is the distance,
            // not a flat count of seconds.
            float allowed = boardingPatience + _walkBack / 1.2f;
            if (Aboard() >= Mathf.Min(_ours.Standing(), _car.Seats) || InPhase > allowed)
            {
                if (Aboard() == 0) { Give($"nobody got back into the car in {allowed:F0}s"); return; }
                Hunt("back in the car");
                return;
            }
        }

        float _walkBack;
        bool _orderedOut;

        void TickParking()
        {
            if (_car.Parked && Mathf.Abs(_car.Speed) < 0.05f)
            {
                Go(Phase.Done, $"parked after {Now:F0}s, {_killed} crews down, " +
                               $"{_stuckSpells} spells stuck. {HowItStands()}");
                return;
            }
            if (InPhase > crewPatience)
            {
                Fault("nopark", $"still not parked {InPhase:F0}s after being told to");
                Give("the car would not park");
            }
        }

        /// <summary>How the car ended up standing: at the kerb and square with the
        /// street, or dumped in the middle of the road at an angle. The run is judged
        /// on it - a car that cannot put itself away has not finished the job.</summary>
        string HowItStands()
        {
            if (_car == null) return "no car";
            var road = _car.Road;
            if (road == null) return "off any road";
            float kerb = road.KerbDOnSide(_car.D, _car.HalfWide);
            float offKerb = Mathf.Abs(Mathf.Abs(kerb) - Mathf.Abs(_car.D));
            float askew = Vector3.Angle(_car.RoadForward, road.Axis * _car.Heading);
            return $"{offKerb:F1} m off the kerb, {askew:F0} deg off the street";
        }

        // ------------------------------------------------------------------ the watch

        /// <summary>The one thing this run is really about: a car with somewhere to be
        /// that is not going anywhere.</summary>
        void WatchTheCar()
        {
            if (_car == null || State == Phase.Waiting || State == Phase.Boarding) return;
            bool busy = (State == Phase.Hunting || State == Phase.Parking) && Aboard() > 0;
            // held at a light or behind a queue is not stuck - it is traffic. Stuck is
            // standing still with nothing named as the reason, or with a reason that
            // never clears.
            // Waiting at a light, in a queue, or at the kerb for a gap to pull out into
            // is traffic, not a fault. Stuck is standing still with the way clear.
            bool waiting = _car.InQueue || _car.Why.StartsWith("red") || _car.Why.StartsWith("yellow") ||
                           _car.Doing == RoadCar.Manoeuvre.PullOut || _car.Doing == RoadCar.Manoeuvre.PullIn;
            if (busy && !waiting && Mathf.Abs(_car.Speed) < 0.3f) _stillFor += Time.deltaTime;
            else { _stillFor = 0f; _saidStuck = 0f; }

            if (_stillFor > stuckAfter && _stillFor - _saidStuck > stuckAfter)
            {
                _saidStuck = _stillFor;
                _stuckSpells++;
                Fault("carstuck", $"stood {_stillFor:F0}s in {State}: {_car.Describe()}");
            }
        }

        Vector3 Car() => _car != null ? _car.Position : _ours != null ? _ours.Position : transform.position;

        // ------------------------------------------------------------------ the telling

        void Go(Phase next, string what)
        {
            State = next;
            _phaseAt = Now;
            _stillFor = 0f;
            Note(next + ": " + what);
        }

        void Give(string why)
        {
            State = Phase.Failed;
            Fault("mission", why);
            Note("failed: " + why);
        }

        void Note(string what)
        {
            _story.Add($"{Now:F0}s {what}");
            Debug.Log("[BlockDemo] mission " + what);
            if (DriveTrace.On) DriveTrace.Event("mission", State.ToString(), what);
        }

        void Fault(string kind, string what)
        {
            Debug.LogWarning($"[BlockDemo] mission {kind}: {what}");
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "tag", "mission");
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Str(sb, "state", State.ToString());
            DriveTrace.Num(sb, "v", _car != null ? _car.Speed : 0f);
            DriveTrace.Str(sb, "why", _car != null ? _car.Why : "");
            DriveTrace.Vec(sb, "p", Car());
            DriveTrace.Row("fault", sb.ToString());
        }

        void Row()
        {
            if (!DriveTrace.On || DriveTrace.Now < _nextRow) return;
            _nextRow = DriveTrace.Now + 1f;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "state", State.ToString());
            DriveTrace.Str(sb, "at", _quarry != null ? _quarry.GangName : "");
            DriveTrace.Int(sb, "killed", _killed);
            DriveTrace.Num(sb, "v", _car != null ? _car.Speed : 0f);
            DriveTrace.Num(sb, "still", _stillFor, "F1");
            DriveTrace.Int(sb, "aboard", _car != null ? _car.Aboard.Count : 0);
            DriveTrace.Str(sb, "mode", _car != null ? _car.State.ToString() : "");
            DriveTrace.Str(sb, "why", _car != null ? _car.Why : "");
            DriveTrace.Num(sb, "toGo", _quarry != null ? Vector3.Distance(Car(), _quarry.Position)
                                                       : State == Phase.Parking ? Vector3.Distance(Car(), _parkAt) : 0f);
            DriveTrace.Vec(sb, "p", Car());
            DriveTrace.Row("mission", sb.ToString());
        }

        /// <summary>The run in one paragraph, for the log at the end.</summary>
        public string Story() => string.Join("\n   ", _story);
    }
}
