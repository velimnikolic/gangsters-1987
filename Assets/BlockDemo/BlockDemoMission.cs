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
        public enum Phase { Waiting, Boarding, Marching, Hunting, Storming, Reboarding, Parking, Passing, Done, Failed }

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

        [Tooltip("THE ROADBLOCK. Every few seconds the mob being hunted is marched into " +
                 "the carriageway directly in front of the outfit's car - the scene the " +
                 "run-down exists for, and the one the quarter never produces on its own " +
                 "because rival crews stand at frontages and the car passes them on the " +
                 "road. With this on, the car meets a man in its lane every pass.")]
        public bool roadblock;
        [Tooltip("Roadblock: how far up the road ahead of the car they are put.")]
        public float roadblockAhead = 20f;
        [Tooltip("Roadblock: seconds between orders to stand in the road.")]
        public float roadblockEvery = 9f;

        float _lastBlock = -100f;

        [Tooltip("The whole run on foot: no car at all. The crew is sent at the mob " +
                 "FARTHEST from it - the length of the quarter on foot, over the lots " +
                 "and across the roads - and has it out with them when it gets there.")]
        public bool onFoot;

        [Tooltip("The bomb run: no car mission, no march. The crew is stocked with " +
                 "grenades and made to use both of the things a grenade is for - it " +
                 "throws a few at a rival crew (which must thin it out), then lays a " +
                 "charge under a car and drives the car off (which must tear it to " +
                 "scrap). What is on trial is the bomb layer end to end: the throw " +
                 "kills, and the planted charge springs when the car moves.")]
        public bool bombRun;
        [Tooltip("Bomb run: grenades to throw at the rival before the plant test.")]
        [Min(1)] public int bombThrows = 3;
        [Tooltip("Bomb run: seconds the planted car may take to blow before the run " +
                 "gives up on it.")]
        public float bombPatience = 60f;

        [Tooltip("The run on two wheels: no car, no march. The crew stands where it " +
                 "was dealt and sends two men on the motorcycle the ledger bought it, " +
                 "one pass at a rival, again and again - which is the one order the " +
                 "machine exists for, played over and over so a fault in it cannot hide " +
                 "behind a lucky run.")]
        public bool motoDriveBy;
        [Tooltip("Two wheels: how many passes to ride before the run is done.")]
        [Min(1)] public int passes = 3;
        [Tooltip("Two wheels: seconds one pass may take, door to door - the walk to the " +
                 "machine, the pass, and the ride home - before it is written off. The " +
                 "outer bound of the crews' own three (DemoCrews.Budget measures each " +
                 "leg off its own distance), plus room, so this one only ever fires for " +
                 "something they did not catch.")]
        public float passPatience = 900f;
        [Tooltip("The walkabout: no fight, no car. The crew is sent on foot from corner " +
                 "to corner of the quarter - down the pavements, through the lights, the " +
                 "player's own click (OrderSelected) - a new far corner each time it " +
                 "arrives. What is on trial is the WALK: the pack, the lanes, the lights, " +
                 "the tether; the crew audit's fault rows are the verdict.")]
        public bool walkabout;
        [Tooltip("Walkabout: corners to walk before the run is done. A corner-to-corner " +
                 "leg is six-to-seven sim-minutes at the crew's pace with its lights " +
                 "and its waits, so three of them fill a soak run.")]
        [Min(1)] public int walkLegs = 3;
        [Tooltip("Walkabout: the leg is judged on PROGRESS, not on the clock - a crew " +
                 "that waits its own men over a light is slow and right. This is the " +
                 "seconds the crew may gain NO ground at all before the leg is a fault.")]
        public float legStallAfter = 90f;
        [Tooltip("Walkabout: the hard ceiling on one leg, however it crawls.")]
        public float legPatience = 600f;
        [Tooltip("Walkabout: seconds stood at a corner before the next order.")]
        public float dwell = 6f;

        [Tooltip("The nerve lever: chance a man shot down to his last hit breaks and " +
                 "runs. Below 0 leaves the game's own figure alone (0.4); the brawl " +
                 "soak turns it up so the runners it wants to watch appear every run.")]
        public float panic = -1f;

        [Tooltip("On foot: metres off the mob at which the crew stops marching and opens up.")]
        public float engageWithin = 30f;
        [Tooltip("On foot: seconds the crew may fail to move at all before the march is a failure.")]
        public float marchPatience = 45f;

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
                if (panic >= 0f) _crews.PanicChance = Mathf.Clamp01(panic);
            }

            // whatever else is going on: a crew that is dead is not on a job. Out of the
            // car and still alive is NOT the end of one - the driver being shot puts the
            // crew on the pavement, and the fight goes on from there.
            // (On foot there may be several crews out, and one of them going down is not
            // the outfit going down: TickWar counts the field itself.)
            if (!onFoot && _ours != null && _ours.Wiped)
            {
                // TWO WHEELS: the crew standing at its own kerb being shot to pieces by
                // the mobs it has been riding past is the game being a game, and it ends
                // the run rather than failing it. What is on trial here is the machine's
                // loop, and a run that rode two clean passes before the answer came is a
                // run that proved what it was for.
                if (motoDriveBy && State != Phase.Done && State != Phase.Failed)
                {
                    Note($"the outfit was wiped out after {_passesRidden} pass" +
                         $"{(_passesRidden == 1 ? "" : "es")}");
                    Finish();
                    return;
                }
                Give($"the outfit was wiped out ({_killed} crews down first)");
                return;
            }

            if (bombRun && State != Phase.Waiting) TickBomb();
            else if (motoDriveBy && State != Phase.Waiting) TickMoto();
            else if (walkabout && State != Phase.Waiting) TickWalk();
            else if (onFoot && State != Phase.Waiting) TickWar();
            else
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

            // THE BOMB RUN: no car mission, no march. The crew is handed grenades and
            // made to throw them and lay one.
            if (bombRun) { StartBomb(); return; }

            // TWO WHEELS: nothing is boarded and nowhere is marched to. The crew stands
            // where the deal left it and sends two of its men out on the machine.
            if (motoDriveBy) { StartMoto(); return; }

            // THE WALKABOUT: nobody to fight, nothing to board - just the quarter,
            // corner to corner, judged on how the crew walks it.
            if (walkabout) { StartWalk(); return; }

            // ON FOOT: no car is stood, none is looked for. EVERY crew of the outfit
            // takes the field, each one sent at a mob of its own at the far end of the
            // quarter - three lieutenants at three crews is three walks and three
            // fights, not one crew doing the rounds.
            if (onFoot)
            {
                _squads.Clear();
                foreach (var unit in _crews.Units)
                    if (unit.Faction == 0 && !unit.Wiped)
                        _squads.Add(new Squad { Ours = unit, Mark = unit.Position });
                if (_squads.Count == 0) { Give("there is no crew of the outfit in the quarter"); return; }
                _ours = _squads[0].Ours;

                _mobs = 0;
                foreach (var unit in _crews.Units)
                    if (unit.Faction != 0 && !unit.IsPolice && !unit.Wiped) _mobs++;
                if (_mobs == 0) { Give("there is no rival crew in the quarter"); return; }

                // farthest first, and no two crews sent at the same mob while there
                // are mobs to go round: the walk is the point
                State = Phase.Marching;
                _phaseAt = Now;
                foreach (var squad in _squads) Assign(squad);
                string guns = Guns();
                Note($"Marching: {_squads.Count} crews on foot at {_mobs} mobs" +
                     (guns.Length > 0 ? " - carrying " + guns : ""));
                return;
            }

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

        // ------------------------------------------------------------------ on foot

        /// <summary>One crew of the outfit and the mob it is walking at. The run on
        /// foot is several of these at once - each crew has its own mark, its own way
        /// across the quarter and its own fight at the end of it.</summary>
        sealed class Squad
        {
            public DemoCrews.Unit Ours;
            public DemoCrews.Unit Quarry;
            public Vector3 Mark;      // where its front man was when it last moved
            public float Stall;       // seconds it has not moved at all
            public float LastOrder;
            public bool Engaged;      // told to open up rather than to walk
            public bool Buried;       // it is down, and has been said so once
        }

        readonly List<Squad> _squads = new List<Squad>();
        int _mobs;   // mobs standing when the run began - what _killed is counted off

        /// <summary>The mob this crew walks at: the one FURTHEST from it that no other
        /// crew of ours is already on - the far end of the quarter, which is the point
        /// of a walk. With fewer mobs left than crews they double up on the nearest.</summary>
        void Assign(Squad squad)
        {
            DemoCrews.Unit best = null;
            float far = -1f, near = float.MaxValue;
            DemoCrews.Unit spare = null;
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                float d = Vector3.SqrMagnitude(unit.Position - squad.Ours.Position);
                if (d < near) { near = d; spare = unit; }
                if (Spoken(unit, squad)) continue;
                if (d > far) { far = d; best = unit; }
            }
            squad.Quarry = best ?? spare;
            if (squad.Quarry == null) return;
            squad.Mark = Front(squad);
            squad.Stall = 0f;
            March(squad, true);
        }

        /// <summary>Is another crew of ours, still standing, already on that mob?</summary>
        bool Spoken(DemoCrews.Unit mob, Squad mine)
        {
            foreach (var squad in _squads)
                if (squad != mine && !squad.Ours.Wiped && squad.Quarry == mob) return true;
            return false;
        }

        /// <summary>The man of this crew nearest its mark - the crew's own front.</summary>
        Vector3 Front(Squad squad)
        {
            var best = squad.Ours.Position;
            if (squad.Quarry == null) return best;
            float near = float.MaxValue;
            foreach (var man in squad.Ours.All())
            {
                if (man.Dead || man.Tf == null) continue;
                float d = Vector3.SqrMagnitude(man.Tf.position - squad.Quarry.Position);
                if (d < near) { near = d; best = man.Tf.position; }
            }
            return best;
        }

        string Who(Squad squad) =>
            string.IsNullOrEmpty(squad.Ours.Name) ? squad.Ours.GangName : squad.Ours.Name;

        /// <summary>What the outfit is carrying, as the book dealt it - the line that
        /// says a run of mixed arms really was one.</summary>
        string Guns()
        {
            var said = new List<string>();
            foreach (var squad in _squads)
                foreach (var man in squad.Ours.All())
                    if (!man.Dead) said.Add(man.WeaponKind.ToString());
            return string.Join(", ", said);
        }

        void March(Squad squad, bool first)
        {
            if (squad.Quarry == null) return;
            float gap = Vector3.Distance(squad.Ours.Position, squad.Quarry.Position);
            _crews.Select(squad.Ours);
            if (!_crews.MarchTo(squad.Ours, squad.Quarry.Position))
            {
                // nobody left of that crew to send: it is down, and TickWar buries it on
                // the next pass. Only a crew that HAS men and still cannot be sent is a
                // fault worth failing the run over.
                if (squad.Ours.Standing() == 0) { squad.Quarry = null; return; }
                Give($"{Who(squad)} could not be sent on foot");
                return;
            }
            squad.LastOrder = Now;
            squad.Engaged = false;
            if (first) Note($"{Who(squad)} on foot at {squad.Quarry.GangName}, {gap:F0} m off");
        }

        /// <summary>The whole run on foot, every crew of ours at once: walk, arrive,
        /// open up, and on to whatever is left standing.</summary>
        void TickWar()
        {
            int standing = 0;
            foreach (var squad in _squads) if (!squad.Ours.Wiped) standing++;
            if (standing == 0) { Give($"the outfit was wiped out ({_killed} crews down first)"); return; }

            int mobs = 0;
            foreach (var unit in _crews.Units)
                if (unit.Faction != 0 && !unit.IsPolice && !unit.Wiped) mobs++;
            _killed = Mathf.Max(_killed, _mobs - mobs);
            if (mobs == 0)
            {
                Go(Phase.Done, $"done on foot after {Now:F0}s, {_killed} mobs down, " +
                               $"{standing} of {_squads.Count} crews still standing");
                return;
            }

            bool walking = false;
            foreach (var squad in _squads)
            {
                if (squad.Ours.Wiped)
                {
                    if (!squad.Buried) { squad.Buried = true; Note($"{Who(squad)}'s crew is down"); }
                    continue;
                }
                if (squad.Quarry == null || squad.Quarry.Wiped)
                {
                    if (squad.Quarry != null) Note($"{squad.Quarry.GangName} is down");
                    squad.Quarry = null;
                    Assign(squad);
                    if (squad.Quarry == null) continue;
                }

                float gap = Vector3.Distance(squad.Ours.Position, squad.Quarry.Position);
                if (gap <= engageWithin)
                {
                    if (!squad.Engaged || Now - squad.LastOrder > reorderEvery)
                    {
                        _crews.Select(squad.Ours);
                        if (!_crews.OrderAttack(squad.Quarry)) { squad.Quarry = null; continue; }
                        squad.LastOrder = Now;
                        if (!squad.Engaged)
                            Note($"{Who(squad)} at {squad.Quarry.GangName}, {gap:F0} m off - on foot");
                        squad.Engaged = true;
                    }
                    squad.Stall = 0f;   // a man shooting is not a man stopped
                    continue;
                }

                walking = true;
                squad.Engaged = false;

                // STILL WALKING is the test. How long a walk takes is the city's business -
                // the way round a block, a lot that turns out to be walled - but a crew that
                // has not moved at all has stopped, and that is a fault.
                // ANY of them walking is the crew walking. Judging it by the lieutenant alone
                // failed a crew whose other two were half way across the quarter and closing:
                // one man in an awkward corner is a man to wait for, not a stopped crew.
                // And a crew that has stopped because it is IN A FIGHT - jumped on the way
                // by a mob that was not its own, or scattered by it - has not stopped
                // walking; it is busy. Only the clock of a crew with nothing in its way runs.
                var here = Front(squad);
                bool busy = Fighting(squad);
                if (Vector3.Distance(here, squad.Mark) > 2f || busy)
                { squad.Mark = here; squad.Stall = 0f; }
                else squad.Stall += Time.deltaTime;
                if (squad.Stall > marchPatience)
                {
                    Give($"{Who(squad)} stopped walking {gap:F0} m short of " +
                         $"{squad.Quarry.GangName} ({squad.Stall:F0}s without moving)");
                    return;
                }

                // the way is drawn again now and then: the mob shifts, and so does the
                // street. NOT while the crew is in a fight, though - a march order puts
                // every man's gun down and turns him back to walking, and a crew jumped
                // on the way that is re-ordered every twelve seconds never gets to fire
                // a shot. It finishes what it is in first.
                if (!busy && Now - squad.LastOrder > reorderEvery) March(squad, false);
            }

            var want = walking ? Phase.Marching : Phase.Hunting;
            if (State != want)
                Go(want, walking ? "walking again" : "everybody is in it");
        }

        /// <summary>Is anybody of this crew shooting, being shot at, or running from
        /// it - anything that stops a man walking for a reason of its own?</summary>
        static bool Fighting(Squad squad)
        {
            foreach (var man in squad.Ours.All())
                if (!man.Dead && (man.State == CrewWalker.Mode.Engaging || man.Panicked)) return true;
            return false;
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

                // the mob put where the car is going: in its lane, facing it. What a
                // driver does about that is DemoCrews.RunDown's business.
                if (roadblock && _car != null && _car.Tf != null &&
                    Now - _lastBlock > roadblockEvery)
                {
                    _lastBlock = Now;
                    var inFront = _car.Position + _car.Tf.forward * roadblockAhead;
                    if (_crews.MarchTo(_quarry, inFront))
                        Note($"{_quarry.GangName} put in the road {roadblockAhead:F0} m " +
                             "ahead of the car");
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

            // no car in the run at all (the whole thing on foot) is TickWar's business,
            // not this one's - it never comes through here. A car that has been shot to
            // bits under a crew mid-run still can: there is nothing left to put away.
            if (_car == null)
            {
                if (next == null) { Go(Phase.Done, $"done after {Now:F0}s, {_killed} crews down"); return; }
                _quarry = next;
                _crews.Select(_ours);
                if (!_crews.OrderAttack(_quarry)) { Give("the attack order was refused"); return; }
                _lastOrder = Now;
                Go(Phase.Hunting, $"{why}: at {_quarry.GangName}, {Mathf.Sqrt(best):F0} m off - on foot");
                return;
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
                    _backMark = _ours.Position; _backStall = 0f;
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
                _backMark = _ours.Position; _backStall = 0f;
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
            if (Aboard() >= Mathf.Min(_ours.Standing(), _car.Seats)) { Hunt("back in the car"); return; }

            // STILL WALKING is the test, not a stopwatch and not the crow's line. A walk
            // back across a quarter is pavements, corners and two sets of lights: a
            // straight-line allowance failed crews who were walking perfectly well (55
            // metres of city measured 96 seconds), and closing distance failed them the
            // other way - round the first corner they are FARTHER from the car than when
            // they set off, which is what going round a building is. What was actually
            // broken was men who stopped dead, so that is what is watched: the clock runs
            // only while the crew has not moved at all.
            var here = _ours.Position;
            if (Vector3.Distance(here, _backMark) > 2f) { _backMark = here; _backStall = 0f; }
            else _backStall += Time.deltaTime;
            // and a ceiling all the same: walking in circles for ever is not walking back
            float ceiling = 3f * boardingPatience + _walkBack / 0.8f;
            if (_backStall > boardingPatience)
            {
                Give($"the crew stopped walking back ({Vector3.Distance(here, _car.Position):F0} m " +
                     $"off the car, {_backStall:F0}s without moving)");
                return;
            }
            if (InPhase > ceiling) { Give($"nobody got back into the car in {ceiling:F0}s"); return; }
        }

        Vector3 _backMark;
        float _backStall;

        /// <summary>Seconds a pull-out or a pull-in is allowed to be the reason a car is
        /// standing still before the watch stops taking it for an answer.</summary>
        const float kerbPatience = 25f;
        float _kerbFor;

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
            //
            // A KERB MANOEUVRE IS ONLY TRAFFIC WHILE IT IS GOING SOMEWHERE. Excused
            // outright, it excused the worst fault this lab has had: a car ordered
            // across town that never left its slot at all - "waiting for a gap" with the
            // lane empty, because what was in its way was a body parked in front of it
            // and nothing in the manoeuvre could ever get round one. Sixty-five seconds
            // of live lane went with it, and the run still passed. Past this much of it,
            // the wait IS the fault.
            bool kerb = _car.Doing == RoadCar.Manoeuvre.PullOut || _car.Doing == RoadCar.Manoeuvre.PullIn;
            _kerbFor = kerb ? _kerbFor + Time.deltaTime : 0f;
            bool waiting = _car.InQueue || _car.Why.StartsWith("red") || _car.Why.StartsWith("yellow") ||
                           (kerb && _kerbFor < kerbPatience);
            if (busy && !waiting && Mathf.Abs(_car.Speed) < 0.3f) _stillFor += Time.deltaTime;
            else { _stillFor = 0f; _saidStuck = 0f; }

            if (_stillFor > stuckAfter && _stillFor - _saidStuck > stuckAfter)
            {
                _saidStuck = _stillFor;
                _stuckSpells++;
                Fault("carstuck", $"stood {_stillFor:F0}s in {State}: {_car.Describe()}");
            }
        }

        // ------------------------------------------------------------------ two wheels

        // The drive-by played by the lab, pass after pass, with nobody at the mouse.
        //
        // Deliberately nothing but the order: no car, no march, no storming on foot.
        // What is being watched is the one loop the machine has - two men walk to it,
        // get on, ride one pass at a rival and bring it home - and the only way to see
        // the ways that loop can stop for good is to ride it thirty times over thirty
        // quarters. Every fault it can raise is a thing that never came back rather
        // than a threshold that was loosened, and every one of them is named, because a
        // pass that quietly does not happen looks exactly like a pass that did.

        int _passesRidden, _passesFired;
        float _passAt;
        bool _passWaiting;

        void StartMoto()
        {
            _crews.Select(_ours);
            var bike = _crews.BikeOf(_ours);
            if (bike == null)
            {
                // The book bought one (BlockDemoOutfit) and it never reached the kerb.
                // That is the whole ledger-to-street seam failing quietly, and it is a
                // failure of the run rather than of the driving.
                Give("the outfit has no motorcycle on the street");
                return;
            }
            Go(Phase.Passing, $"{_ours.GangName} on the {bike.DisplayName.ToLowerInvariant()}, " +
                              $"{passes} pass{(passes == 1 ? "" : "es")} to ride");
            Send();
        }

        /// <summary>The next pass: the nearest rival still standing, and the order.</summary>
        void Send()
        {
            if (_passesRidden >= passes) { Finish(); return; }

            DemoCrews.Unit next = null;
            float best = float.MaxValue;
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                float d = Vector3.SqrMagnitude(unit.Position - _ours.Position);
                if (d < best) { best = d; next = unit; }
            }
            if (next == null)
            {
                Note($"no rival left standing after {_passesRidden} pass" +
                     $"{(_passesRidden == 1 ? "" : "es")}");
                Finish();
                return;
            }

            // The book took the machine back. That happens when the crew loses its
            // HEAD - the gear of a crew with no lieutenant reverts to the safe
            // (RosterOps.NormalizeArms) and the street follows the book, so the machine
            // leaves the kerb. It is the ledger working, not a seam coming apart, and it
            // ends the run the way running out of hoods does.
            if (_passesRidden > 0 && _crews.BikeOf(_ours) == null)
            {
                Note($"the book took the machine back after {_passesRidden} pass" +
                     $"{(_passesRidden == 1 ? "" : "es")}");
                Finish();
                return;
            }

            _quarry = next;
            _crews.Select(_ours);
            if (!_crews.OrderDriveBy(next))
            {
                // Out of men to send is the game being a game - the passes cost the crew
                // two hoods and it has none left - and it ends the run rather than
                // failing it. Anything else is the order being impossible with a machine
                // at the kerb and men on their feet, which is a seam that has come apart.
                if (_crews.DriveByShortHanded)
                {
                    Note($"nobody left to send after {_passesRidden} pass" +
                         $"{(_passesRidden == 1 ? "" : "es")}");
                    Finish();
                    return;
                }
                Give("drive-by refused: " + (_crews.DriveByRefusal ?? "no reason given"));
                return;
            }
            _passAt = Now;
            _passWaiting = true;
            Note($"pass {_passesRidden + 1} of {passes} at {next.GangName} " +
                 $"({Vector3.Distance(_ours.Position, next.Position):F0} m)");
        }

        void TickMoto()
        {
            if (State != Phase.Passing) return;
            if (!_passWaiting) { Send(); return; }

            // the raid ends itself, every way it can end (DemoCrews.Finish)
            if (!_crews.RaidActive(_ours))
            {
                _passWaiting = false;
                _passesRidden++;
                if (_crews.LastRaidShots > 0) _passesFired++;
                if (!_crews.LastRaidBothUp)
                    Note($"pass {_passesRidden}: one of the two did not come back");
                Note($"pass {_passesRidden} over - {_crews.LastRaidShots} round" +
                     $"{(_crews.LastRaidShots == 1 ? "" : "s")} fired");
                if (_quarry != null && _quarry.Wiped) { _killed++; Note($"{_quarry.GangName} is down"); }
                Send();
                return;
            }

            if (Now - _passAt > passPatience)
            {
                Fault("raidstall", $"pass {_passesRidden + 1} has been out {passPatience:F0}s");
                Give($"the machine never came back from pass {_passesRidden + 1}");
            }
        }

        void Finish()
        {
            // Every pass ridden and not one round fired off the machine: the men rode
            // past and the guns never bore. Nothing else in the run would have said so.
            // Asked only of runs that rode a pass at all - a run cut short before the
            // first one came home has not failed to fire, it has not fired yet.
            if (_passesRidden > 0 && _passesFired == 0)
                Fault("noshot", $"{_passesRidden} passes ridden and nothing was fired");

            // And the two of them back on their own feet - a man left parented to a
            // motorcycle is a man who has left the game, and he leaves quietly.
            foreach (var man in _ours.All())
                if (!man.Dead && man.Riding)
                {
                    Fault("notback", man.DisplayName + " is still on the machine");
                    break;
                }

            Go(Phase.Done, $"{_passesRidden} pass{(_passesRidden == 1 ? "" : "es")} ridden, " +
                           $"{_passesFired} with shots fired, {_killed} crews down");
        }

        // ------------------------------------------------------------------ the bomb run

        enum BombStep { Throwing, Shop, Planting, Driving, Boarding }
        BombStep _bstep;
        int _bombThrown;
        float _bombNext, _bombStepAt;
        int _rivalMenAtStart;
        CrewCar _bombCar;
        GangFront _bombShop;
        bool _throwThinned, _shopLit;

        /// <summary>Stock the crew with grenades, point it at a rival, and start the
        /// throwing. The lab widens the reach so the throw and the plant fire on the
        /// mechanic rather than on how near the deal happened to drop the crew - what is
        /// on trial is whether a thrown charge kills and a laid one springs, not range.</summary>
        void StartBomb()
        {
            _crews.Select(_ours);
            // buy the grenades the way a player would - onto the ledger, given to this
            // crew's lieutenant - so the run exercises the real path (armory -> crew ->
            // thrown -> struck off). Where a scene has no roster, the unit's own tally is
            // all there is, so stock that instead.
            int need = bombThrows + 2;   // the throws, plus the shop and the plant
            var roster = LivingCity.Gameplay.PersonnelDirector.Instance != null
                ? LivingCity.Gameplay.PersonnelDirector.Instance.Roster : null;
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
            _ours.Bombs = Mathf.Max(_ours.Bombs, need);   // cache; BindBombs re-derives it
            _crews.BombThrowRange = 100000f;
            _crews.BombPlantRange = 100000f;
            _rivalMenAtStart = RivalMenStanding();
            if (_rivalMenAtStart == 0) { Give("there is no rival crew in the quarter to bomb"); return; }

            _quarry = NearestRival(_ours.Position);
            _bstep = BombStep.Throwing;
            _bombThrown = 0;
            _bombNext = Now;
            State = Phase.Hunting;
            _phaseAt = Now;
            Note($"bomb run: {_ours.GangName} with {_ours.Bombs} grenade" +
                 $"{(_ours.Bombs == 1 ? "" : "s")}, {_rivalMenAtStart} rival men standing");
        }

        void TickBomb()
        {
            switch (_bstep)
            {
                case BombStep.Throwing: TickBombThrow(); break;
                case BombStep.Shop: TickBombShop(); break;
                case BombStep.Planting: TickBombPlant(); break;
                case BombStep.Driving: TickBombDrive(); break;
                case BombStep.Boarding: TickBombBoard(); break;
            }
            AimShotCam();
        }

        [Tooltip("Bomb run only: swing the demo camera onto whatever the run is doing " +
                 "(the burning shop, the car being blown) so a headless --shot frames the " +
                 "action instead of the whole quarter. Off leaves the camera alone.")]
        public bool bombShotCam;

        Camera _shotCam;

        /// <summary>Point the camera at what the bomb run is doing this moment - the shop
        /// while it burns and boards, the car while it is driven off and blown, the crew
        /// otherwise - so a screenshot catches it. Test scaffolding, and only when asked.</summary>
        void AimShotCam()
        {
            if (!bombShotCam) return;
            if (_shotCam == null)
            {
                foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (c.targetTexture == null) { _shotCam = c; break; }
                if (_shotCam == null) return;
            }
            Vector3 focus =
                (_bombShop != null && _bombShop.Damaged) ? _bombShop.Door :
                (_bombCar != null && _bombCar.Tf != null) ? _bombCar.Position :
                _ours != null ? _ours.Position : _shotCam.transform.position;
            var eye = focus + new Vector3(10f, 14f, 10f);
            _shotCam.transform.position = eye;
            _shotCam.transform.rotation = Quaternion.LookRotation((focus + Vector3.up * 1.5f) - eye, Vector3.up);
        }

        /// <summary>Throw the grenades, one every couple of seconds, and see the rival
        /// thin out. A quarry wiped before the count is done is replaced; none left is a
        /// fine reason to move on to the plant.</summary>
        void TickBombThrow()
        {
            if (Now < _bombNext) return;

            if (_bombThrown >= bombThrows)
            {
                _throwThinned = RivalMenStanding() < _rivalMenAtStart;
                if (!_throwThinned)
                    Fault("nobombkill", $"{bombThrows} grenades thrown and no rival went down");
                Note($"{bombThrows} grenades thrown - rivals {_rivalMenAtStart} -> {RivalMenStanding()} standing");
                BeginShop();
                return;
            }

            if (_quarry == null || _quarry.Wiped) _quarry = NearestRival(_ours.Position);
            if (_quarry == null)
            {
                _throwThinned = RivalMenStanding() < _rivalMenAtStart;
                Note("no rival left standing - on to the shop");
                BeginShop();
                return;
            }

            if (!_crews.OrderBombThrow(_quarry))
            {
                Give("the throw was refused: " + (_crews.BombRefusal ?? "?"));
                return;
            }
            _bombThrown++;
            _bombNext = Now + 2.5f;   // let the blast land and the count settle
            Note($"grenade {_bombThrown} of {bombThrows} thrown at {_quarry.GangName}");
        }

        /// <summary>Throw a grenade at a rival family's SHOPFRONT - it must catch fire.
        /// The boarding-up is checked later, once the fire has burnt out.</summary>
        void BeginShop()
        {
            // a rival's shop first; failing that (a lab quarter may seat only ours) ANY
            // shop, so the fire-and-boards is exercised either way
            _bombShop = NearestFront(rivalOnly: true) ?? NearestFront(rivalOnly: false);
            if (_bombShop == null) { Note("no shopfront to bomb - on to the plant"); BeginPlant(); return; }

            if (!_crews.OrderBombFront(_bombShop))
            {
                Give("the throw at the shop was refused: " + (_crews.BombRefusal ?? "?"));
                return;
            }
            Note($"grenade thrown at {_bombShop.GangName}'s shopfront");
            _bstep = BombStep.Shop;
            _bombStepAt = Now;
        }

        GangFront NearestFront(bool rivalOnly)
        {
            GangFront best = null;
            float bestD = float.MaxValue;
            var all = GangFront.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i].Damaged) continue;
                if (rivalOnly && all[i].GangName == _ours.GangName) continue;
                float d = (all[i].Door - _ours.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = all[i]; }
            }
            return best;
        }

        void TickBombShop()
        {
            // give the charge its flight, then the shop must be alight
            if (Now - _bombStepAt < 2f) return;
            _shopLit = _bombShop != null && _bombShop.Damaged;
            if (!_shopLit)
                Fault("noshopfire", "the shopfront was thrown at and did not catch fire");
            else
                Note($"{_bombShop.GangName}'s shop is burning");
            BeginPlant();
        }

        void BeginPlant()
        {
            _bombCar = StandACar();
            if (_bombCar == null) { Give("no car to lay a charge under"); return; }
            _bstep = BombStep.Planting;
            _bombStepAt = Now;
        }

        /// <summary>Lay the charge, then send the car off so the charge springs under a
        /// car that is being driven - exactly what it is for.</summary>
        void TickBombPlant()
        {
            if (!_crews.OrderPlantBomb(_bombCar))
            {
                Give("the plant was refused: " + (_crews.BombRefusal ?? "?"));
                return;
            }
            // drive the car off down its own road; the charge is sprung by its wheels turning
            var ahead = _bombCar.Position +
                        (_bombCar.Tf != null ? _bombCar.Tf.forward : Vector3.forward) * 45f;
            if (!_bombCar.GoTo(ahead, park: false))
                _bombCar.GoTo(_ours.Position + Vector3.forward * 45f, park: false);
            _bstep = BombStep.Driving;
            _bombStepAt = Now;
            Note("charge laid; the car is driven off");
        }

        void TickBombDrive()
        {
            if (_bombCar == null || _bombCar.Wrecked)
            {
                Note("the car was blown to scrap");
                // if a shop was set alight, wait for it to burn out and board itself up
                if (_bombShop != null && _bombShop.Damaged)
                {
                    _bstep = BombStep.Boarding;
                    _bombStepAt = Now;
                    return;
                }
                FinishBomb();
                return;
            }
            if (Now - _bombStepAt > bombPatience)
            {
                Fault("nobombcar", $"the car never blew in {bombPatience:F0}s " +
                    $"(v {_bombCar.RoadSpeed:F1}, {_bombCar.Why})");
                Give("the planted charge never sprang");
            }
        }

        /// <summary>Wait for the burning shop to board itself up - the fire out, planks
        /// over the ground-floor windows (ShopDamage). The hard ceiling is the burn time
        /// plus room.</summary>
        void TickBombBoard()
        {
            if (_bombShop != null && _bombShop.Boarded)
            {
                Note($"{_bombShop.GangName}'s shop has boarded up its windows");
                FinishBomb();
                return;
            }
            if (Now - _bombStepAt > ShopDamage.BurnFor + 12f)
            {
                Fault("noboards", "the burnt shop never boarded its windows");
                Give("the shop burned but was never boarded up");
            }
        }

        void FinishBomb()
        {
            bool shopOk = _bombShop == null || (_shopLit && _bombShop.Boarded);
            Go(Phase.Done, _throwThinned && shopOk
                ? "bomb run clean: the throw thinned the rival, the shop burned and boarded, the car was scrapped"
                : $"bomb run done (rival thinned {_throwThinned}, shop lit {_shopLit}, boarded {(_bombShop != null && _bombShop.Boarded)})");
        }

        int RivalMenStanding()
        {
            int n = 0;
            foreach (var unit in _crews.Units)
                if (unit.Faction != 0 && !unit.IsPolice) n += unit.Standing();
            return n;
        }

        DemoCrews.Unit NearestRival(Vector3 from)
        {
            DemoCrews.Unit best = null;
            float bestD = float.MaxValue;
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                float d = (unit.Position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = unit; }
            }
            return best;
        }

        // ------------------------------------------------------------------ the walkabout

        int _legsWalked, _walkRetries;
        Vector3 _walkDest;
        float _dwellUntil, _legBestToGo, _legBestAt;
        bool _walking;

        /// <summary>The far corners of the quarter's own floor (the fence the builder
        /// laid). Each leg goes to the corner FARTHEST from where the crew stands, so
        /// every walk is the length of the quarter and crosses its lights.</summary>
        Vector3 FarCorner(Vector3 from)
        {
            var r = WalkObstacles.City[0];
            const float inset = 15f;
            var best = from;
            float far = -1f;
            for (int k = 0; k < 4; k++)
            {
                var c = new Vector3(
                    k % 2 == 0 ? r.xMin + inset : r.xMax - inset, 0f,
                    k / 2 == 0 ? r.yMin + inset : r.yMax - inset);
                float d = (c - from).sqrMagnitude;
                if (d > far) { far = d; best = c; }
            }
            return best;
        }

        void StartWalk()
        {
            if (WalkObstacles.City.Count == 0) { Give("no city fence laid - nothing to walk"); return; }
            State = Phase.Marching;
            _phaseAt = Now;
            _legsWalked = 0;
            _walkRetries = 0;
            Note($"Walkabout: {_ours.GangName}, {_ours.Standing()} men, {walkLegs} corners");
            OrderLeg();
        }

        void OrderLeg()
        {
            var corner = FarCorner(_ours.Position);
            _crews.Select(_ours);
            // every other leg is the player's double click: the run is the same order
            // at the same corners, and a walkabout that never runs judges half the
            // gaits the town has (the skate and the weave both live in the run)
            if (!_crews.OrderSelected(corner, out _walkDest, run: (_legsWalked & 1) == 1))
            {
                Give("the crew would not take a walk order");
                return;
            }
            _walking = true;
            _phaseAt = Now;
            _legBestToGo = float.MaxValue;
            _legBestAt = Now;
            Note($"leg {_legsWalked + 1}/{walkLegs}: sent " +
                 $"{Vector3.Distance(_ours.Position, _walkDest):F0} m" +
                 ((_legsWalked & 1) == 1 ? " at the run" : ""));
        }

        void TickWalk()
        {
            if (_ours == null || _ours.Wiped) { Give("the crew is gone"); return; }
            var boss = _ours.Boss != null && !_ours.Boss.Dead && _ours.Boss.Tf != null ? _ours.Boss : null;
            if (boss == null) { Give("the lieutenant is gone"); return; }

            if (_walking)
            {
                if (boss.HasOrder)
                {
                    // judged on ground gained: a crew that stands over a light while
                    // its own man crosses is slow and RIGHT - only a crew gaining no
                    // ground at all (or one past any sane ceiling) has stalled
                    float toGo = Vector3.Distance(boss.Tf.position, _walkDest);
                    if (toGo < _legBestToGo - 1f) { _legBestToGo = toGo; _legBestAt = Now; }
                    if (Now - _legBestAt > legStallAfter || InPhase > legPatience)
                    {
                        Fault("mission", $"leg {_legsWalked + 1} stalled - {toGo:F0} m still to go, " +
                                         $"no ground gained for {Now - _legBestAt:F0}s of {InPhase:F0}s");
                        _walking = false;
                        _legsWalked++;
                        _dwellUntil = Now + dwell;
                    }
                    return;
                }
                float off = Vector3.Distance(boss.Tf.position, _walkDest);
                // stopped short (the crowd, a car): sent again at the same corner - a
                // player clicks twice too; only a leg that will not finish is a fault
                if (off > 15f && _walkRetries < 2 && InPhase <= legPatience)
                {
                    _walkRetries++;
                    _crews.Select(_ours);
                    // the retry keeps the leg's gait - a run leg is clicked twice again
                    _crews.OrderSelected(_walkDest, out _walkDest, run: (_legsWalked & 1) == 1);
                    return;
                }
                if (off > 15f)
                    Fault("mission", $"leg {_legsWalked + 1} ended {off:F0} m short of the corner");
                Note($"leg {_legsWalked + 1} done in {InPhase:F0}s, {off:F0} m off the mark");
                _walkRetries = 0;
                _walking = false;
                _legsWalked++;
                _dwellUntil = Now + dwell;
                return;
            }

            if (Now < _dwellUntil) return;
            if (_legsWalked >= walkLegs)
            {
                Go(Phase.Done, $"walkabout done: {walkLegs} corners in {Now:F0}s");
                return;
            }
            OrderLeg();
        }

        Vector3 Car() => _car != null ? _car.Position : _ours != null ? _ours.Position : transform.position;

        // ------------------------------------------------------------------ the telling

        void Go(Phase next, string what)
        {
            State = next;
            _phaseAt = Now;
            _stillFor = 0f;
            Note(next + ": " + what);
            // the last word on how the field stood: no row is written after the run
            // is over, so the one that ends it is written here
            if (next == Phase.Done) LastRow();
        }

        void Give(string why)
        {
            State = Phase.Failed;
            Fault("mission", why);
            Note("failed: " + why);
            LastRow();
        }

        void LastRow()
        {
            _nextRow = float.MinValue;
            Row();
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
            // on foot there are several crews out; the row names the one still walking
            // (the run's own front), and counts the men left on both sides
            var quarry = _quarry;
            var from = Car();
            int mine = 0, theirs = 0;
            bool war = onFoot && _squads.Count > 0;   // before the crews are dealt, nobody is counted
            if (war)
            {
                foreach (var squad in _squads)
                {
                    mine += squad.Ours.Standing();
                    if (quarry == null && !squad.Ours.Wiped && squad.Quarry != null)
                    { quarry = squad.Quarry; from = squad.Ours.Position; }
                }
                foreach (var unit in _crews.Units)
                    if (unit.Faction != 0 && !unit.IsPolice) theirs += unit.Standing();
            }

            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "state", State.ToString());
            DriveTrace.Str(sb, "at", quarry != null ? quarry.GangName : "");
            DriveTrace.Int(sb, "killed", _killed);
            if (war)
            {
                DriveTrace.Int(sb, "ours", mine);
                DriveTrace.Int(sb, "theirs", theirs);
            }
            DriveTrace.Num(sb, "v", _car != null ? _car.Speed : 0f);
            DriveTrace.Num(sb, "still", _stillFor, "F1");
            DriveTrace.Int(sb, "aboard", _car != null ? _car.Aboard.Count : 0);
            DriveTrace.Str(sb, "mode", _car != null ? _car.State.ToString() : "");
            DriveTrace.Str(sb, "why", _car != null ? _car.Why : "");
            DriveTrace.Num(sb, "toGo", quarry != null ? Vector3.Distance(from, quarry.Position)
                                                      : State == Phase.Parking ? Vector3.Distance(Car(), _parkAt) : 0f);
            DriveTrace.Vec(sb, "p", from);
            DriveTrace.Row("mission", sb.ToString());
        }

        /// <summary>The run in one paragraph, for the log at the end.</summary>
        public string Story() => string.Join("\n   ", _story);
    }
}
