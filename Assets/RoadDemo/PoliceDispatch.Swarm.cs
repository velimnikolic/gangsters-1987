using System.Collections.Generic;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE SWARM (GAN-220). It is 1987 and every car has a radio. Kill a policeman and
    /// it is not an incident any more - every car the city has answers, and they do not
    /// go home until the men who did it are dead, taken, or gone.
    ///
    /// It is ONE MORE RUNG on the ladder the dispatcher already climbs, not a second
    /// system: the same squads, the same flow (Sent, Deploying, Responding, Warning,
    /// Engaging, Securing, Leaving), the same arrest at the end of it. Three things are
    /// different, and only three:
    ///
    ///  * WHO ANSWERS. Police response is station-LOCAL by a deliberate earlier rule
    ///    (ResponseRange, 150 m) - a car does not cross the city to a fight it could
    ///    never have seen. A dead officer is the one sanctioned exception: every car on
    ///    every roster is on the radio, whatever the distance, up to a cap.
    ///  * WHO THEY GO FOR. Ordinarily a squad picks whoever fired in the last four
    ///    seconds; that loses a crew the moment it stops shooting and runs. The swarm
    ///    remembers: the crews it is hunting stay hunted until one of three things
    ///    happens to each of them.
    ///  * WHEN IT ENDS. Not when the shooting stops - when nobody has SEEN one of them
    ///    for minutes. Then the cars go home to their own stations and the patrol
    ///    rhythm comes back. There is no permanent siege; the permanent thing is the
    ///    wanted level the escapees carry out of it.
    ///
    /// The warning rule still holds for everybody else on the street. It is not
    /// re-given to the hunted: they were warned when the first squad arrived, and a
    /// squad shouting DROP THE GUNS at the man who has just shot a policeman would be
    /// the city being polite about the one thing it is not polite about.
    /// </summary>
    public sealed partial class PoliceDispatch
    {
        /// <summary>The most cars the radio call puts on one scene. A number for
        /// legibility as much as for the frame: past about this many the street is a
        /// car park and the player cannot read what is happening. Never more than the
        /// rosters actually hold either - a wrecked car is not on the radio, and
        /// Nearest only ever returns units that exist.</summary>
        const int SwarmCars = 8;

        /// <summary>Seconds without a sighting of anybody hunted before the force
        /// stands down. Minutes, not hours: a city under permanent lockdown is a city
        /// the player stops playing in.</summary>
        const float SwarmQuiet = 120f;

        /// <summary>Metres at which a police unit counts as having SEEN a hunted man.
        /// This is a sighting test and not a steering one - what the squads actually
        /// chase is their own remembered position of the enemy (DemoCrews' LastSeenPos),
        /// never a live transform.</summary>
        const float SwarmEyes = 70f;

        /// <summary>How often the sighting sweep runs. Every frame would be a distance
        /// test per hunted crew per police unit, for a question that changes on the
        /// scale of seconds.</summary>
        const float SwarmSweep = 0.75f;

        bool _swarm;
        float _swarmSeenAt;
        float _swarmSweepAt;
        Vector3 _swarmScene;

        readonly List<DemoCrews.Unit> _hunted = new List<DemoCrews.Unit>();
        static readonly List<CrewWalker> _swarmShooters = new List<CrewWalker>();

        /// <summary>Whether the whole force is out. Read by the overlay and the map.</summary>
        public bool Swarming => _swarm;

        /// <summary>
        /// SWARM-001: an officer is down. Everything the city has, now.
        /// </summary>
        void RaiseSwarm(Vector3 where)
        {
            _swarmScene = where;
            _swarmSeenAt = Time.time;

            // whoever has been shooting at this incident is who the city is looking for
            _swarmShooters.Clear();
            StreetAlarm.ShootersSince(30f, _swarmShooters);
            foreach (var man in _swarmShooters)
            {
                if (man == null || man.Faction == StreetAlarm.PoliceFaction) continue;
                var unit = _crews != null ? _crews.UnitOf(man) : null;
                if (unit == null || unit.IsPolice || unit.Wiped) continue;
                if (!_hunted.Contains(unit)) _hunted.Add(unit);
            }

            if (_swarm) return;
            _swarm = true;
            CrewOverlay.Announce("OFFICER DOWN — EVERY CAR IN THE CITY IS COMING",
                7f, new Color(1f, 0.45f, 0.4f));
            SendSwarm();
        }

        /// <summary>Cars off every roster, distance no object, up to the cap.</summary>
        void SendSwarm()
        {
            var scene = _swarmScene;
            while (_carsSent < SwarmCars)
            {
                var car = Nearest(scene, carries: true, anyDistance: true);
                if (car == null) break;   // the rosters are empty; nobody else is coming
                car.RouteTo(scene, StandOff);
                _squads.Add(new Squad { Ride = car, Men = MenOf(car), Scene = scene, State = SquadState.Sent });
                if (_lights.TryGetValue(car, out var lights)) lights.Set(true, siren: true);
                _carsSent++;
            }
            _lastSentAt = Time.time;
        }

        void TickSwarm(float dt)
        {
            if (!_swarm) return;

            // SWARM-002: the hunted are hunted until one of three things happens to each.
            for (int i = _hunted.Count - 1; i >= 0; i--)
            {
                var unit = _hunted[i];
                if (unit == null || unit.Wiped) { _hunted.RemoveAt(i); continue; }   // dead
                if (unit.Surrendered) { _hunted.RemoveAt(i); continue; }             // taken
            }

            if (Time.time >= _swarmSweepAt)
            {
                _swarmSweepAt = Time.time + SwarmSweep;
                if (AnyHuntedSeen()) _swarmSeenAt = Time.time;
                // the fight keeps coming to them: a squad standing at a taped-off scene
                // with a hunted crew still on the street goes back after it
                foreach (var squad in _squads)
                {
                    if (squad.Men == null || squad.Men.Wiped) continue;
                    if (squad.State != SquadState.Securing) continue;
                    if (squad.Men.TargetUnit != null) continue;
                    if (PickFight(squad)) continue;
                }
            }

            // SWARM-003: nobody has seen one of them for minutes. Stand down.
            if (Time.time - _swarmSeenAt > SwarmQuiet) StandDown();
        }

        /// <summary>Has any unit of the law laid eyes on anybody it is hunting? A
        /// proximity test, which is what a sighting is; the CHASE itself never reads a
        /// live transform (that rule has four known traps behind it).</summary>
        bool AnyHuntedSeen()
        {
            if (_hunted.Count == 0) return false;
            foreach (var unit in _hunted)
            {
                if (unit == null || unit.Wiped) continue;
                var at = unit.Position;
                foreach (var u in _units)
                {
                    if (u.Tf == null) continue;
                    if ((u.Position - at).sqrMagnitude <= SwarmEyes * SwarmEyes) return true;
                }
                foreach (var squad in _squads)
                {
                    if (squad.Men == null || squad.Men.Wiped) continue;
                    if ((squad.Men.Position - at).sqrMagnitude <= SwarmEyes * SwarmEyes) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// SWARM-003/004. The cars go home to their OWN stations and the patrol rhythm
        /// comes back; everybody still on his feet who was being hunted has ESCAPED, and
        /// escaping this is what a wanted level is for. A cop-killer's grade never cools
        /// - hidden time buys off the other two and buys nothing here.
        /// </summary>
        void StandDown()
        {
            _swarm = false;
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            var underworld = LivingCity.Outfit.Underworld.Current;

            int away = 0;
            foreach (var unit in _hunted)
            {
                if (unit == null || unit.Wiped) continue;
                away++;
                // A cop-killer's grade lands on HIS OWN family's book. It used to land
                // on ours whoever shot the officer, which was the player's men being
                // marked for a Falcone gun.
                var roster = underworld?.Of(unit.Faction)?.Roster;
                if (roster == null) continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.CharacterId < 0) continue;
                    var member = roster.Find(man.CharacterId);
                    if (member == null) continue;
                    WantedLevels.Mark(member, WantedLevels.CopKiller, today);
                }
            }
            _hunted.Clear();

            if (LivingCity.Gameplay.PersonnelDirector.Instance != null && away > 0)
                LivingCity.Gameplay.PersonnelDirector.Instance.Touch();
            CrewOverlay.Announce(away > 0
                    ? "THE SEARCH IS CALLED OFF — THEY ARE WANTED MEN NOW"
                    : "THE SEARCH IS CALLED OFF",
                6f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>A crew the swarm is looking for. Read by PickFight, which otherwise
        /// only knows about men who fired in the last four seconds - and a crew that has
        /// stopped shooting and started running is exactly the crew this is about.</summary>
        bool Hunted(DemoCrews.Unit unit) => _swarm && _hunted.Contains(unit);
    }
}
