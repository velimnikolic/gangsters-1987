using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The bomb half of the crews' orders: the grenades a crew carries and the two
    /// things it can do with one - throw it, or lay it.
    ///
    /// A THROW is a lob at a mark the crew can see and reach: a rival, his shopfront,
    /// anywhere the player points inside throwing range. The nearest man of the crew who
    /// is on his feet (not riding, not dead) makes it; the charge arcs over and goes off
    /// where it lands (BombProjectile -> Explosion).
    ///
    /// A PLANT is a charge laid under a car and left armed. The crew has to be at the
    /// car to lay it; once it is down it waits, and springs the moment that car is
    /// driven off (PlantedBomb). What it is for is the car the outfit cannot reach the
    /// man inside of - lay it, walk away, and the next man to turn the key is the one it
    /// takes.
    ///
    /// Every order spends one grenade off the crew's count (Unit.Bombs) and is refused
    /// with a reason when it cannot be given - no grenade, nobody up, out of reach - the
    /// same shape as the drive-by's refusal (DriveByRefusal), so the card can word a row
    /// it cannot offer.
    /// </summary>
    public partial class DemoCrews
    {
        /// <summary>Grenades an outfit crew starts with in a scene that has NO ledger
        /// roster behind it (a bare demo). Where a roster exists - the city, the lab -
        /// the ledger is the truth: BindBombs sets each crew's count to the grenades its
        /// lieutenant has been bought and given (RosterOps), and this is ignored.</summary>
        public int BombsPerCrew = 0;

        /// <summary>The ledger's grenades, counted onto the crews: each outfit crew
        /// carries as many as its lieutenant has been given and not yet thrown. Line for
        /// line the cars' and bikes' deal (BindCars/BindBikes) - the difference is that a
        /// grenade is spent, not kept, so the count only ever falls between buys.</summary>
        void BindBombs(Roster roster)
        {
            if (roster == null) return;
            for (int i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit.Faction != 0) continue;
                var crew = roster.FindCrew(unit.CrewId);
                if (crew == null) continue;
                unit.Bombs = RosterOps.GrenadesOwnedBy(roster, crew.LieutenantId);
            }
        }

        /// <summary>Spend one of the crew's grenades: struck off the ledger the moment it
        /// leaves a man's hand (so BindBombs re-derives the lower count and never puts it
        /// back), and dropped from the unit's own tally at once for the order that just
        /// threw it. In a scene with no roster the tally is all there is.</summary>
        void SpendBomb(Unit unit)
        {
            if (unit == null) return;
            if (unit.Bombs > 0) unit.Bombs--;
            if (unit.Faction != 0) return;
            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (roster == null) return;
            var crew = roster.FindCrew(unit.CrewId);
            if (crew != null) RosterOps.SpendGrenade(roster, crew.LieutenantId);
        }

        /// <summary>How far a man will lob a grenade, in metres. Past this the throw is
        /// refused - the crew has to be walked closer. Pinned to the longest gun's reach
        /// (the rifle's) so a bomb can never be thrown further than a man can shoot -
        /// a rifleman always out-ranges, or at worst matches, a bomber.</summary>
        public float BombThrowRange = CrewArms.LongestReach();

        /// <summary>How near the crew must be to a car to lay a charge under it.</summary>
        public float BombPlantRange = 12f;

        /// <summary>Why the last bomb order was refused, or null. Read by the order card
        /// to word a row it cannot offer.</summary>
        public string BombRefusal { get; private set; }

        /// <summary>Can this crew throw a grenade at that point? (A grenade in hand, a
        /// man on his feet, the mark in range.) The card asks this to light its row, and
        /// the refusal it leaves on BombRefusal is the row's faded note.</summary>
        public bool CanBombThrow(Unit unit, Vector3 targetPos)
        {
            if (CustodyRefuses(unit))
            { BombRefusal = InCustodyRefusal; return false; }
            var man = Thrower(unit, targetPos, out var why);
            BombRefusal = man == null ? why : null;
            return man != null;
        }

        /// <summary>Can this crew lay a charge under that car? (A grenade in hand, a man
        /// up, the crew at the car.)</summary>
        public bool CanBombPlant(Unit unit, RoadCar car)
        {
            if (CustodyRefuses(unit))
            { BombRefusal = InCustodyRefusal; return false; }
            if (car == null || car.Tf == null || car.Wrecked) { BombRefusal = "No car to lay it under"; return false; }
            var man = Planter(unit, car.Position, out var why);
            BombRefusal = man == null ? why : null;
            return man != null;
        }

        // ---------------------------------------------------------------- throw

        /// <summary>Throw a grenade at a rival crew - it lands on the lieutenant, or the
        /// first man of his still standing.</summary>
        public bool OrderBombThrow(Unit target)
        {
            if (target == null || target.Wiped) { BombRefusal = "Nobody to throw at"; return false; }
            var crew = Selected;
            if (!ThrowAt(crew, target.Position, target.GangName)) return false;
            CrewSpeech.Say(crew, LivingCity.Data.VoiceLines.OrdGrenade);
            return true;
        }

        /// <summary>Throw a grenade at a rival family's premises - it lands on the
        /// doorstep.</summary>
        public bool OrderBombFront(GangFront front)
        {
            if (front == null) { BombRefusal = "Nothing to throw at"; return false; }
            var crew = Selected;
            if (!ThrowAt(crew, front.Door, front.GangName)) return false;
            CrewSpeech.Say(crew, LivingCity.Data.VoiceLines.OrdDoorBomb);
            return true;
        }

        /// <summary>Throw a grenade at a point on the ground the player pointed at.</summary>
        public bool OrderBombThrowAt(Vector3 targetPos) => ThrowAt(Selected, targetPos, null);

        bool ThrowAt(Unit unit, Vector3 targetPos, string what)
        {
            BombRefusal = null;
            if (CustodyRefuses(unit))
            { BombRefusal = InCustodyRefusal; return false; }
            var man = Thrower(unit, targetPos, out var why);
            if (man == null) { BombRefusal = why; return false; }

            SpendBomb(unit);
            var from = man.ChestPosition + Vector3.up * 0.15f;
            Face(man, targetPos);
            BombProjectile.Throw(from, targetPos + Vector3.up * 0.2f, this, unit.Faction, GroundY);

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Str(sb, "what", "bomb thrown" + (what != null ? " at " + what : ""));
                DriveTrace.Num(sb, "range", Vector3.Distance(man.Tf.position, targetPos));
                DriveTrace.Int(sb, "left", unit.Bombs);
                DriveTrace.Row("bomb", sb.ToString());
            }
            return true;
        }

        // ---------------------------------------------------------------- plant

        /// <summary>Lay a charge under that car and leave it armed. The crew must be at
        /// the car; the charge springs when the car is next driven off.</summary>
        public bool OrderPlantBomb(RoadCar car)
        {
            BombRefusal = null;
            if (CustodyRefuses(Selected))
            { BombRefusal = InCustodyRefusal; return false; }
            if (car == null || car.Tf == null || car.Wrecked)
            {
                BombRefusal = "No car to lay it under";
                return false;
            }
            var man = Planter(Selected, car.Position, out var why);
            if (man == null) { BombRefusal = why; return false; }

            SpendBomb(Selected);
            // under the nose of the car, where a car pulling out rolls straight over it
            var at = car.Position + (car.Tf != null ? car.Tf.forward : Vector3.forward) * (car.HalfLength + 0.2f);
            at.y = GroundY;
            Face(man, at);
            PlantedBomb.Lay(at, car, this, Selected.Faction, GroundY);

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", Selected.GangName);
                DriveTrace.Str(sb, "what", "bomb planted under a car");
                DriveTrace.Int(sb, "left", Selected.Bombs);
                DriveTrace.Row("bomb", sb.ToString());
            }
            CrewSpeech.Say(Selected, LivingCity.Data.VoiceLines.OrdCarBomb);
            return true;
        }

        // ---------------------------------------------------------------- picking

        /// <summary>The man who makes the throw: the crew's nearest to the mark who is on
        /// his feet, if a grenade is in hand and the mark is in range. Null with the
        /// reason on <paramref name="why"/> - the one function the card's question and the
        /// order's answer both go through, so they cannot disagree.</summary>
        CrewWalker Thrower(Unit unit, Vector3 targetPos, out string why)
        {
            if (!HasGrenade(unit, out why)) return null;

            var best = NearestUp(unit, targetPos, out float range);
            if (best == null) { why = "Nobody up to throw it"; return null; }
            if (range > BombThrowRange) { why = "Too far - move the crew closer"; return null; }
            // a grenade does not pick sides: a mark inside the blast of the man throwing
            // it takes him with it, so a throw that would land on the crew's own feet is
            // refused rather than granted
            if (range < Explosion.Radius + 1.5f) { why = "Too close - you'd catch your own men"; return null; }
            return best;
        }

        /// <summary>The man who LAYS the charge: the crew's nearest to the car who is on
        /// his feet, if a grenade is in hand and he is at the car.
        ///
        /// Deliberately not the thrower's test. A lob has to clear the blast, so a mark
        /// inside <see cref="Explosion.Radius"/> is refused; a charge laid under a car
        /// does not go off in the layer's hands - it waits for whoever turns the key -
        /// so the ONLY distance that matters is the one he still has to walk
        /// (<see cref="BombPlantRange"/>). Running the plant through the throw's test
        /// left a band of 7.5 m to 12 m as the only place a charge could be laid from,
        /// which is to say: a man standing at the car could not lay one under it.</summary>
        CrewWalker Planter(Unit unit, Vector3 at, out string why)
        {
            if (!HasGrenade(unit, out why)) return null;

            var best = NearestUp(unit, at, out float range);
            if (best == null) { why = "Nobody up to lay it"; return null; }
            if (range > BombPlantRange) { why = "Get the crew to the car first"; return null; }
            return best;
        }

        /// <summary>The two things a throw and a plant ask for alike: the crew is ours,
        /// and it has a grenade to spend.</summary>
        static bool HasGrenade(Unit unit, out string why)
        {
            why = null;
            if (unit == null || unit.Faction != 0) { why = "Not your crew"; return false; }
            if (unit.Bombs <= 0) { why = "No grenades - none in the crew's hands"; return false; }
            return true;
        }

        /// <summary>The crew's nearest man to a point who could handle a grenade - alive,
        /// on his own two feet, not riding and not running away - with the metres to it
        /// on <paramref name="range"/>. Null when there is nobody up.</summary>
        static CrewWalker NearestUp(Unit unit, Vector3 pos, out float range)
        {
            CrewWalker best = null;
            float bestD = float.MaxValue;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                if (man.Riding || man.Retreating) continue;
                float d = (man.Tf.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = man; }
            }
            range = best != null ? Mathf.Sqrt(bestD) : float.MaxValue;
            return best;
        }

        /// <summary>Turn a man to face where he is throwing - just his heading, no step;
        /// the walker owns his feet.</summary>
        static void Face(CrewWalker man, Vector3 at)
        {
            if (man == null || man.Tf == null) return;
            var d = at - man.Tf.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-3f) man.Tf.rotation = Quaternion.LookRotation(d.normalized);
        }
    }
}
