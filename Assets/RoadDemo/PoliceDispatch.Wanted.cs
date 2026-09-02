using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A MARKED MAN, AND A MAN RUNNING (GAN-222).
    ///
    /// Two halves of the same sweep, because both are the same question asked of the
    /// same pairs - which of the outfit's men can the law SEE:
    ///
    ///  * CHASE ON SIGHT (FLEE-005). A patrol that lays eyes on a man the city wants
    ///    goes after him. It ends exactly three ways and there is no fourth: he is
    ///    taken (the ordinary collar, and the sentence carries the escape surcharge),
    ///    he is dead (only if he fights - the police still do not shoot first), or he
    ///    got away. Nothing here is a stalemate state.
    ///
    ///  * BROKEN PURSUIT (FLEE-002). Hiding is not a button a running man may press
    ///    with a patrol behind him. He may go inside only after nobody of the law has
    ///    been able to see him for a stretch of seconds; seen going in, the pursuit
    ///    simply follows him to the door and he is taken there. Where he goes is one of
    ///    the outfit's own doors, through the passage that already exists for it
    ///    (CrewQuarters) - no second way of being indoors.
    ///
    /// And the sighting itself is what the wanted clock is made of: a man seen on the
    /// street has not been hidden, whatever he did yesterday, so the sighting resets his
    /// hidden days (WantedLevels.Seen) and going inside starts them.
    /// </summary>
    public sealed partial class PoliceDispatch
    {
        /// <summary>Metres at which a unit of the law can make out who a man is. Short
        /// of the sighting range the swarm uses, because recognising a face off a
        /// wanted sheet is a nearer thing than noticing a fight.</summary>
        const float LawEyes = 55f;

        /// <summary>Seconds out of every policeman's sight before a running crew may go
        /// to ground. Long enough that a man cannot step through a door with a patrol
        /// forty metres behind him, short enough to be a plan rather than a wait.</summary>
        const float PursuitBroken = 12f;

        /// <summary>How often the sweep runs. It is a distance test per crew per unit
        /// and the answer changes on the scale of seconds, so it does not want a frame.
        /// </summary>
        const float WatchEvery = 0.5f;

        /// <summary>Seconds before the law tries the same recognition again. Without it a
        /// collar that fell through - he refused, he walked off, the officer died - was
        /// reopened by the next sweep half a second later, and a wanted man stood in a
        /// loop of officers walking up to him for ever.</summary>
        const float ChaseAgain = 30f;

        /// <summary>Metres a running man will cross to reach the named hideout. Past
        /// this the walk is longer than a broken pursuit is likely to stay broken, so he
        /// takes the nearest door of ours instead - which is what the running man had
        /// before there was a hideout at all, and it has to stay a real fallback.</summary>
        const float HideoutReach = 350f;

        float _watchAt;
        float _chaseAgainAt;
        static readonly List<GangFront> _doors = new List<GangFront>();

        void TickWanted(float dt)
        {
            if (_crews == null || Time.time < _watchAt) return;
            _watchAt = Time.time + WatchEvery;

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            var underworld = LivingCity.Outfit.Underworld.Current;

            foreach (var unit in _crews.Units)
            {
                if (unit == null || unit.Wiped) continue;
                // THE LAW LOOKS AT EVERY FAMILY. A wanted man is a wanted man whichever
                // house he belongs to, and the sweep reads HIS house's book - ours only
                // when he is ours.
                var roster = underworld?.Of(unit.Faction)?.Roster;
                if (roster == null) continue;
                if (unit.Car != null) continue;   // in a car, and not on anybody's pavement
                // and men who are actually INSIDE a building are off the street: nobody
                // on the pavement can see them, and a patrol that "recognised" a man
                // through a wall would make hiding worthless. This is also where the
                // hidden days begin - the moment they are off it, not the moment the run
                // was ordered.
                if (CrewQuarters.Inside(unit))
                {
                    // and it counts HOWEVER he got in. A man the player simply walked
                    // through his own door is as far off the street as one who ran there,
                    // and the hidden days are about being off it - not about the order
                    // that put him there.
                    if (unit.Fleeing) _crews.EndFlight(unit);
                    GoneToGround(roster, unit, today);
                    continue;
                }

                var seenBy = LawWithin(unit.Position, LawEyes);
                bool seen = seenBy != null;
                if (seen)
                {
                    unit.SeenByLawAt = Time.time;
                    // A SIGHTING IS THE WHOLE OF THE WANTED CLOCK'S RESET. Three CLEAR
                    // days is what the design asks for, so a man spotted on day two has
                    // spent none hidden, not two.
                    MarkSeen(roster, unit);
                }

                if (seen && WantedIn(roster, unit) &&
                    _collar == Collar.None && Time.time >= _chaseAgainAt &&
                    StreetAlarm.QuietFor > QuietBefore)
                    ChaseOnSight(unit, seenBy, today);

                if (unit.Fleeing) TickFlight(unit, roster, today, seen);
            }
        }

        /// <summary>
        /// The nearest unit of the law that can actually SEE this spot, or null.
        ///
        /// A man indoors is not looking out of a window: an officer resting behind the
        /// station door and a car docked in its stall are both off the street, and
        /// counting them as eyes meant a crew that went to ground within sight of the
        /// station could never accumulate a single hidden day.
        /// </summary>
        IPoliceUnit LawWithin(Vector3 at, float metres)
        {
            IPoliceUnit best = null;
            float bestD = metres * metres;
            foreach (var u in _units)
            {
                if (u.Tf == null) continue;
                if (u is PoliceFootPatrol foot && foot.State == PoliceFootPatrol.Mode.Inside) continue;
                if (u is PolicePatrolCar car && car.State == PolicePatrolCar.Mode.Resting) continue;
                float d = (u.Position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        static void MarkSeen(Roster roster, DemoCrews.Unit unit)
        {
            if (roster == null) return;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                var member = roster.Find(man.CharacterId);
                if (member != null && member.WantedLevel > 0) WantedLevels.Seen(member);
            }
        }

        static bool WantedIn(Roster roster, DemoCrews.Unit unit)
        {
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                var member = roster.Find(man.CharacterId);
                if (member != null && member.WantedLevel > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// FLEE-005. He was recognised: the officer goes for him, and what happens next
        /// is the collar that already exists - walk up, put the question, and the men
        /// answer it themselves (SurrenderRoll). A wanted man does not get a fresh
        /// warning shout; he gets an officer walking at him.
        /// </summary>
        void ChaseOnSight(DemoCrews.Unit unit, IPoliceUnit law, int today)
        {
            if (law == null || law.Tf == null) return;
            var man = unit.Boss != null && !unit.Boss.Dead
                ? unit.Boss : DemoCrews.NearestOf(unit, law.Tf.position);
            if (man == null || man.Tf == null) return;

            // he is brought to them first; the collar opens when somebody is stood there
            if (law.Available) law.RouteTo(unit.Position, 4f);

            if (law is PoliceFootPatrol foot && foot.Tf != null &&
                (foot.Tf.position - man.Tf.position).sqrMagnitude < 30f * 30f)
            {
                _arrestOfficer = foot;
                _arrestLawman = null;
                _arrestSquad = null;
                _arrestCrew = unit;
                _arrestCollar = man;
                _collar = Collar.WalkingUp;
                _collarAt = Time.time;
                _collarBy = Time.time + CollarPatience;
                _chaseAgainAt = Time.time + ChaseAgain;
                _fightChance = FightOdds(unit);
                // Salted with the DAY and not with the clock: Time.time is whatever the
                // frame rate made of it, and a stream seeded off it would answer
                // differently on two runs of the same seed. A recognition is one thing
                // per crew per day, which is the grain the rest of the sim keeps.
                _willFight = SurrenderRoll.Fights(_fightChance,
                    SurrenderRoll.StreamFor(_crews.CitySeed, CrewKey(unit), -today));
                foot.Challenge(man, _sidearm);
                Banner();
                CrewOverlay.Announce("AN OFFICER HAS RECOGNISED ONE OF OURS",
                    4f, new Color(1f, 0.55f, 0.45f));
            }
        }

        /// <summary>
        /// FLEE-002. The run, and its one ending that is not being caught: nobody of the
        /// law has seen them for long enough, and they go through one of our own doors.
        /// </summary>
        void TickFlight(DemoCrews.Unit unit, Roster roster, int today, bool seen)
        {
            // already walking to a door of ours: let them get there. (Being actually
            // INSIDE is handled a step earlier, where the hidden days begin.)
            if (CrewQuarters.Billeted(unit)) return;
            if (seen) return;
            if (Time.time - unit.SeenByLawAt < PursuitBroken) return;

            var door = OurNearestDoor(unit.Position);
            if (door == null) return;
            if (CrewQuarters.Station(_crews, unit, door.BusinessId) ||
                CrewQuarters.Station(_crews, unit, door.Outside, door.Role))
                CrewOverlay.Announce("THEY HAVE GONE TO GROUND", 4f,
                    new Color(0.95f, 0.9f, 0.6f));
        }

        static void GoneToGround(Roster roster, DemoCrews.Unit unit, int today)
        {
            if (roster == null || today <= 0) return;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                var member = roster.Find(man.CharacterId);
                if (member != null) WantedLevels.WentToGround(member, today);
            }
        }

        /// <summary>
        /// Where a man of ours can get off the street: the nearest door the outfit
        /// holds, the headquarters among them. The epic asks for a bought and designated
        /// HIDEOUT and this is deliberately not that - it is every door we already own,
        /// which is what the city currently has to offer. See the report on GAN-222.
        /// </summary>
        static GangFront OurNearestDoor(Vector3 from)
        {
            _doors.Clear();
            var fronts = GangFront.All;
            for (int i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || front.Boarded) continue;
                if (front.GangId != LivingCity.Gameplay.PlayerCommands.House.Value) continue;
                _doors.Add(front);
            }
            GangFront best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _doors.Count; i++)
            {
                float d = (_doors[i].Outside - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = _doors[i]; }
            }
            return best;
        }
    }
}
