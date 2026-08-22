using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE MONKEY: nobody at the mouse, and the whole underworld at each other's throats.
    ///
    /// BlockDemoMission plays a PLAYER - one crew, one job, judged on whether it finishes.
    /// This plays the CITY: every few seconds it picks two mobs and sets them at each
    /// other, sends the outfit out by car, on foot and on the machine, and otherwise
    /// pokes the streets in whatever order its seed says. It is not meant to finish. It
    /// is meant to run a hundred fights past the code and write down everything
    /// impossible that happens on the way.
    ///
    /// What it watches for is deliberately DUMB and physical - a man under the ground, a
    /// man in the air, a body at NaN, a car at ninety metres a second, a fight that has
    /// been walking toward itself for two minutes - because those are the faults nobody
    /// writes a specific test for and they are exactly what a hundred unattended fights
    /// turn up. Every one is written to the trace as a fault row (analyze.py counts
    /// them) and to the log with a [monkey] tag, once per kind per subject, so fifty
    /// runs can be read as a table rather than a wall.
    /// </summary>
    public sealed class MonkeyRunner : MonoBehaviour
    {
        [Tooltip("Sim seconds before the first order - the city has to finish standing " +
                 "up and the crowd has to come out of the doors first.")]
        public float startAfter = 20f;

        [Tooltip("Sim seconds between orders.")]
        public float orderEvery = 5f;

        [Tooltip("Same seed, same run: which mobs are set at which, in what order.")]
        public int seed = 1;

        [Tooltip("Sim seconds between sweeps of the street for the impossible.")]
        public float watchEvery = 0.5f;

        [Tooltip("Metres under the ground a body may be before it is a hole in the floor " +
                 "rather than a kerb. Men stand ON the pavement, which is a little above " +
                 "the road, so the slack is one way only.")]
        public float underGround = 1.5f;

        [Tooltip("Metres over the ground a body may be. A man on a first-floor balcony " +
                 "is not a thing this city has, so anything up there was thrown.")]
        public float overGround = 6f;

        [Tooltip("Seconds two crews may be at war without either closing on the other " +
                 "or anybody going down.")]
        public float warPatience = 120f;

        DemoCrews _crews;
        RoadDemoBuilder _city;
        System.Random _rng;
        float _nextOrder, _nextWatch;
        int _orders, _wars, _driveBys, _motos, _footFights, _marches;
        float _minX, _maxX, _minZ, _maxZ;

        /// <summary>Fault kinds seen, and how many of each - the run's own tally, said
        /// once at the end so a fifty-run soak has one line a run to compare.</summary>
        readonly Dictionary<string, int> _faults = new Dictionary<string, int>();

        /// <summary>Subjects already reported for a kind: a man who has fallen through
        /// the world reports it once, not sixty times a second.</summary>
        readonly HashSet<string> _said = new HashSet<string>();

        /// <summary>Men standing per unit last sweep - the difference is the killing,
        /// and a run of a hundred orders that kills nobody is itself a finding.</summary>
        readonly Dictionary<DemoCrews.Unit, int> _standing =
            new Dictionary<DemoCrews.Unit, int>();

        /// <summary>When a pair went to war, and how far apart they were then.</summary>
        readonly Dictionary<DemoCrews.Unit, (float at, float range, int kills)> _wared =
            new Dictionary<DemoCrews.Unit, (float, float, int)>();

        int _deaths;

        float Now => Time.timeSinceLevelLoad;

        void Start()
        {
            _rng = new System.Random(seed);
            _city = FindAnyObjectByType<RoadDemoBuilder>();
            Bounds();
        }

        /// <summary>The box the city stands in, with room round it for the districts and
        /// the island. Anything outside this was not walking - it was flung.</summary>
        void Bounds()
        {
            _minX = -1200f; _maxX = 2600f; _minZ = -1200f; _maxZ = 1800f;
            if (_city == null || _city.verticalRoadX == null || _city.horizontalRoadZ == null)
                return;
            float lowX = float.MaxValue, highX = float.MinValue;
            foreach (var x in _city.verticalRoadX)
            {
                if (x < lowX) lowX = x;
                if (x > highX) highX = x;
            }
            float lowZ = float.MaxValue, highZ = float.MinValue;
            foreach (var z in _city.horizontalRoadZ)
            {
                if (z < lowZ) lowZ = z;
                if (z > highZ) highZ = z;
            }
            // the quarters (port, suburbs, airport) and the island stand outside the
            // grid, so the box is the grid with a kilometre of room on every side
            _minX = lowX - 1400f; _maxX = highX + 1400f;
            _minZ = lowZ - 1400f; _maxZ = highZ + 1400f;
        }

        void Update()
        {
            if (_crews == null)
            {
                _crews = FindAnyObjectByType<DemoCrews>();
                if (_crews == null) return;
            }

            if (Now >= _nextWatch)
            {
                _nextWatch = Now + Mathf.Max(0.1f, watchEvery);
                Watch();
            }

            if (Now < startAfter || Now < _nextOrder) return;
            _nextOrder = Now + Mathf.Max(0.5f, orderEvery);
            Act();
        }

        // ------------------------------------------------------------------ orders

        void Act()
        {
            var live = Live();
            if (live.Count < 2) { Fault("nobody-on-the-street", "the city", "fewer than two crews are standing"); return; }

            _orders++;
            switch (_rng.Next(0, 10))
            {
                case 0: case 1: case 2: case 3: RivalWar(live); break;
                case 4: case 5: OutfitFoot(live); break;
                case 6: case 7: OutfitCar(live); break;
                case 8: OutfitMoto(live); break;
                default: March(live); break;
            }
        }

        /// <summary>Two mobs, set at each other. Both ways round on purpose: a crew told
        /// to go at somebody who has not been told about it is a shooting, not a fight,
        /// and half the code under test only runs when both sides are coming.</summary>
        void RivalWar(List<DemoCrews.Unit> live)
        {
            var a = Pick(live, u => u.Faction > 0);
            if (a == null) return;
            var b = Nearest(live, a, 400f);
            if (b == null) return;

            _crews.Sic(a, b);
            _crews.Sic(b, a);
            _wars++;
            Remember(a, b);
            Remember(b, a);
            Say("war", $"{a.GangName} at {b.GangName}",
                $"{Vector3.Distance(a.Position, b.Position):F0} m apart");
        }

        void OutfitFoot(List<DemoCrews.Unit> live)
        {
            var ours = Pick(live, u => u.Faction == 0);
            if (ours == null) return;
            var target = Nearest(live, ours, 700f);
            if (target == null) return;

            _crews.Select(ours);
            if (!_crews.OrderAttack(target)) return;
            _footFights++;
            Remember(ours, target);
            Say("attack", $"the outfit at {target.GangName}",
                $"{Vector3.Distance(ours.Position, target.Position):F0} m apart");
        }

        /// <summary>The car: aboard first, and the attack order from inside a car IS the
        /// drive-by (DemoCrews.OrderAttack). So the monkey boards, then orders, exactly
        /// as a player's two clicks would.</summary>
        void OutfitCar(List<DemoCrews.Unit> live)
        {
            // THE CREW WITH THE KEYS, not any crew of ours. The ledger issues a car to
            // one lieutenant, and asking a crew that has none is not a test of the
            // drive-by - it is a test of the refusal, once, which is not what fifty runs
            // are for.
            var ours = Pick(live, u => u.Faction == 0 && _crews.CarOf(u) != null);
            if (ours == null) { Once("no-car", "the outfit", "no crew of ours has a car"); return; }
            var car = _crews.CarOf(ours);
            if (car == null) return;

            _crews.Select(ours);
            if (ours.Car == null) { _crews.OrderCar(car); Say("board", "the outfit", car.DisplayName); return; }

            var target = Nearest(live, ours, 900f);
            if (target == null) return;
            if (!_crews.OrderAttack(target)) return;
            _driveBys++;
            Remember(ours, target);
            Say("drive-by", $"the outfit at {target.GangName}", car.DisplayName);
        }

        /// <summary>The machine. EVERY crew of ours that has one is tried, not the first:
        /// the two men who ride are taken off whatever the crew was doing, so the crew
        /// that owns the machine is very often the crew that has just been sent
        /// somewhere else - and a monkey that gives up on the first "no hood to send"
        /// never rides at all (the first hundred runs of this rode nought passes).</summary>
        void OutfitMoto(List<DemoCrews.Unit> live)
        {
            var mounted = new List<DemoCrews.Unit>();
            foreach (var unit in live)
                if (unit.Faction == 0 && _crews.BikeOf(unit) != null)
                    mounted.Add(unit);

            if (mounted.Count == 0)
            {
                Once("no-machine", "the outfit", "no crew of ours has a machine");
                return;
            }

            var refusal = "";
            foreach (var ours in mounted)
            {
                var target = Nearest(live, ours, 900f);
                if (target == null) continue;
                if (!_crews.CanDriveBy(ours, target))
                {
                    refusal = _crews.DriveByRefusal ?? "no pass to be had";
                    continue;
                }

                _crews.Select(ours);
                if (!_crews.OrderDriveBy(target))
                {
                    refusal = _crews.DriveByRefusal ?? "the order was refused";
                    continue;
                }

                _motos++;
                Remember(ours, target);
                Say("moto", $"the outfit at {target.GangName}", "one pass");
                return;
            }

            if (refusal.Length > 0) Once("no-pass", "the outfit", refusal);
        }

        /// <summary>Somebody sent somewhere for no reason - the walk code, over open
        /// ground, with everything else going on around it.
        ///
        /// Sent to a DOOR (a family's premises) or to another crew's corner, never to a
        /// random point on the map: a spot in the middle of the wild ground is not a
        /// place the walk lattice covers, and marching men at it measures nothing but
        /// the lattice's own edge. Every target here is somewhere men are already
        /// standing, so a march that fails to arrive is a real failure.</summary>
        void March(List<DemoCrews.Unit> live)
        {
            var unit = live[_rng.Next(live.Count)];
            if (unit.IsPolice) return;

            Vector3 to;
            var fronts = GangFront.All;
            if (fronts.Count > 0 && _rng.Next(0, 2) == 0)
            {
                var front = fronts[_rng.Next(fronts.Count)];
                if (front == null) return;
                to = front.Door;
            }
            else
            {
                var other = live[_rng.Next(live.Count)];
                if (other == unit) return;
                to = other.Position;
            }

            if (!_crews.MarchTo(unit, to)) return;
            _marches++;
            Say("march", unit.GangName, $"{Vector3.Distance(unit.Position, to):F0} m");
        }

        List<DemoCrews.Unit> Live()
        {
            var live = new List<DemoCrews.Unit>();
            foreach (var unit in _crews.Units)
                if (unit != null && !unit.Wiped && !unit.IsPolice)
                    live.Add(unit);
            return live;
        }

        DemoCrews.Unit Pick(List<DemoCrews.Unit> live, System.Func<DemoCrews.Unit, bool> want)
        {
            var of = new List<DemoCrews.Unit>();
            foreach (var unit in live)
                if (want(unit)) of.Add(unit);
            return of.Count == 0 ? null : of[_rng.Next(of.Count)];
        }

        /// <summary>The nearest crew of another family within reach - a mob is sent at
        /// somebody it could plausibly walk to, not at the far side of the map.</summary>
        DemoCrews.Unit Nearest(List<DemoCrews.Unit> live, DemoCrews.Unit from, float within)
        {
            DemoCrews.Unit best = null;
            var bestD = within * within;
            foreach (var unit in live)
            {
                if (unit == from || unit.Faction == from.Faction) continue;
                var d = (unit.Position - from.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = unit; }
            }
            return best;
        }

        void Remember(DemoCrews.Unit unit, DemoCrews.Unit target) =>
            _wared[unit] = (Now, Vector3.Distance(unit.Position, target.Position), _deaths);

        // ------------------------------------------------------------------ watching

        void Watch()
        {
            var ground = _crews.GroundY;

            foreach (var unit in _crews.Units)
            {
                if (unit == null) continue;

                foreach (var man in unit.All())
                {
                    if (man == null || man.Tf == null || !man.Tf.gameObject.activeSelf) continue;
                    Body(Who(unit, man), man.Tf.position, ground,
                         aboard: _crews.IsAboard(man) || man.Riding, dead: man.Dead);
                }

                // men standing, and what it cost to lose one
                var standing = unit.Standing();
                if (_standing.TryGetValue(unit, out var was) && standing < was)
                    _deaths += was - standing;
                _standing[unit] = standing;

                War(unit);
            }

            foreach (var car in _crews.Cars)
            {
                if (car == null || car.Tf == null) continue;
                Body("car " + car.DisplayName, car.Position, ground, aboard: false, dead: false);
                if (car.Speed > 45f)
                    Once("car-too-fast", car.DisplayName, $"{car.Speed:F0} m/s");
            }

            foreach (var bike in _crews.Bikes)
            {
                if (bike == null) continue;
                Body("bike " + bike.DisplayName, bike.Position, ground, aboard: false, dead: false);
                if (bike.Speed > 45f)
                    Once("bike-too-fast", bike.DisplayName, $"{bike.Speed:F0} m/s");
            }
        }

        /// <summary>One thing's position, judged against the ground and the map. A body
        /// in a car or on a machine is exempt from the height test - it is riding, and a
        /// car on the freeway really is six metres up.</summary>
        void Body(string who, Vector3 at, float ground, bool aboard, bool dead)
        {
            if (float.IsNaN(at.x) || float.IsNaN(at.y) || float.IsNaN(at.z) ||
                float.IsInfinity(at.x) || float.IsInfinity(at.y) || float.IsInfinity(at.z))
            {
                Once("position-nan", who, at.ToString());
                return;
            }

            if (at.x < _minX || at.x > _maxX || at.z < _minZ || at.z > _maxZ)
            {
                Once("off-the-map", who, $"({at.x:F0}, {at.z:F0})");
                return;
            }

            // a corpse settles into the ground a little (CrewGore), so the dead get slack
            var floor = ground - (dead ? underGround + 1f : underGround);
            if (at.y < floor)
                Once("under-the-ground", who, $"y {at.y:F1} at ({at.x:F0}, {at.z:F0})");
            else if (!aboard && at.y > ground + overGround)
                Once("in-the-air", who, $"y {at.y:F1} at ({at.x:F0}, {at.z:F0})");
        }

        /// <summary>A fight that is going nowhere: still at war after the patience, with
        /// nobody down since it started and the two sides no closer than they were. That
        /// is the shape of every "the crew walked at a wall for two minutes" bug.</summary>
        void War(DemoCrews.Unit unit)
        {
            if (unit.TargetUnit == null) { _wared.Remove(unit); return; }
            if (!_wared.TryGetValue(unit, out var since)) return;
            if (Now - since.at < warPatience) return;

            var range = Vector3.Distance(unit.Position, unit.TargetUnit.Position);
            if (_deaths == since.kills && range > 25f && range > since.range - 10f)
                Once("war-goes-nowhere", Who(unit, null),
                     $"{Now - since.at:F0}s at {unit.TargetUnit.GangName}, " +
                     $"{range:F0} m apart (was {since.range:F0})");

            // said once for this fight either way - a fight already reported is not
            // reported every half second until somebody dies
            _wared.Remove(unit);
        }

        static string Who(DemoCrews.Unit unit, CrewWalker man)
        {
            var gang = unit == null || string.IsNullOrEmpty(unit.GangName) ? "?" : unit.GangName;
            if (man == null) return gang + " crew " + (unit != null ? unit.CrewId : 0);
            return gang + " " + (string.IsNullOrEmpty(man.DisplayName) ? "man" : man.DisplayName);
        }

        // ------------------------------------------------------------------ the book

        /// <summary>An order, into the trace. What the monkey DID, so a fault can be read
        /// against what was going on at the time.</summary>
        void Say(string what, string who, string detail)
        {
            if (DriveTrace.On) DriveTrace.Event("monkey", who, what, Detail(detail));
        }

        static string Detail(string detail)
        {
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "how", detail);
            return sb.ToString();
        }

        /// <summary>Something impossible, once per kind per subject.</summary>
        void Once(string kind, string who, string detail)
        {
            if (!_said.Add(kind + "|" + who)) return;
            Fault(kind, who, detail);
        }

        void Fault(string kind, string who, string detail)
        {
            _faults.TryGetValue(kind, out var n);
            _faults[kind] = n + 1;
            Debug.LogWarning($"[monkey] {kind}: {who} - {detail}");
            if (DriveTrace.On) DriveTrace.Event("fault", who, kind, Detail(detail));
        }

        /// <summary>The run's own line, on the way out - the tally a soak reads.</summary>
        void OnDisable() => Report();

        bool _reported;

        void Report()
        {
            if (_reported) return;
            _reported = true;

            var sb = new StringBuilder();
            sb.Append("[monkey] ").Append(_orders).Append(" orders: ")
              .Append(_wars).Append(" wars, ")
              .Append(_footFights).Append(" attacks, ")
              .Append(_driveBys).Append(" drive-bys, ")
              .Append(_motos).Append(" moto passes, ")
              .Append(_marches).Append(" marches; ")
              .Append(_deaths).Append(" men down");
            Debug.Log(sb.ToString());

            if (_faults.Count == 0)
            {
                Debug.Log("[monkey] no faults");
                return;
            }

            foreach (var kv in _faults)
                Debug.Log($"[monkey] fault {kv.Key} x{kv.Value}");
        }
    }
}
