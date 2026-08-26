using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The arrest, from the crew's side: hands up, and then taken in.
    ///
    /// The law's half of it - the officer who walks up with his piece out and puts the
    /// question - is PoliceDispatch's. This is only what a crew DOES about it, and it is
    /// deliberately two steps rather than one: a crew that has given up is stood in the
    /// street with its guns away for as long as the officer takes over it, and the men
    /// only go onto the books as held when they are actually led off. A player who is
    /// shot at by somebody else while his men stand there with their hands up has been
    /// wronged by the street, not by a bookkeeping shortcut.
    ///
    /// Where they go: a man of the outfit is JAILED on the roster (RosterOps.Jail), and
    /// the street then re-deals without him - Sync drops anybody who is not Active - so
    /// nothing here has to reach into the scene and delete a body. A rival mob has no
    /// books at all, so its men simply leave with the officer (RemoveUnit).
    /// </summary>
    public partial class DemoCrews
    {
        /// <summary>Days a man is held for. A night in the cells and the two days it
        /// takes a lawyer to be any use - long enough that losing a crew to an arrest
        /// costs the player his week, short enough that it is not a death.</summary>
        public const int HeldDays = 3;

        /// <summary>HANDS UP. The crew stops, puts its guns away and stands where it
        /// stands. Its guns stay away by themselves after this: the concealment rule
        /// asks the man whether he WANTS the piece out (CrewWalker.WantsGunOut) and a
        /// man who has given up never does, whatever is going on round him.
        ///
        /// False when there is nobody left to put their hands up.</summary>
        public bool GiveUp(Unit unit)
        {
            if (unit == null || unit.Wiped || unit.Surrendered) return false;
            unit.Surrendered = true;
            unit.TargetUnit = null;
            unit.OrderedFight = false;
            unit.Searching = false;
            unit.LookUntil = 0f;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                man.Surrendered = true;
                man.Disengage();
                man.Holster();
                man.OrderToPoint(man.Tf.position);
            }
            CrewOverlay.Announce(unit.GangName.ToUpperInvariant() + " GIVE THEMSELVES UP",
                4f, new Color(0.95f, 0.9f, 0.6f));
            return true;
        }

        /// <summary>The arrest fell through - the officer is dead, or gone, or the crew
        /// was never taken. They pick their guns back up and the street resumes.</summary>
        public void LetGo(Unit unit)
        {
            if (unit == null || !unit.Surrendered) return;
            unit.Surrendered = false;
            foreach (var man in unit.All())
                if (man != null && !man.Dead) man.Surrendered = false;
        }

        /// <summary>TAKEN IN. The men are led off the street and onto the books as held,
        /// with the charge on their record - which is the whole point of an arrest being
        /// a thing the ledger knows about rather than a body being deleted.</summary>
        public void TakeIn(Unit unit, string charge = "")
        {
            if (unit == null) return;
            if (unit.Faction != 0)
            {
                // no books behind a mob: its men leave with the officer and that is that
                RemoveUnit(unit);
                return;
            }

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (roster == null) return;   // no books at all: they stand there arrested

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            int backOn = today > 0 ? today + HeldDays : 0;
            string stamp = today > 0 ? "DAY " + today : "";

            int taken = 0;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.CharacterId <= 0) continue;
                if (RosterOps.Jail(roster, man.CharacterId, backOn,
                        "Held at the station", charge, stamp).Ok) taken++;
            }
            if (taken == 0) return;

            // and the street re-deals without them: Sync keeps only Active men, so the
            // bodies go the same way a discharged man's does, through the books
            director.Touch();
            CrewOverlay.Announce(
                taken == 1 ? "ONE MAN TAKEN IN" : taken + " MEN TAKEN IN",
                5f, new Color(0.55f, 0.78f, 1f));
        }
    }
}
