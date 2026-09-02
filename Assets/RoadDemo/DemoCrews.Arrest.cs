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
        /// a thing the ledger knows about rather than a body being deleted.
        ///
        /// WHAT THEY GET is no longer three days flat for everything (GAN-219). Handed a
        /// pipeline, the men go in as HELD with no release date at all and wait for a
        /// judge: the sentence is rolled when the transfer reaches court, off the deed
        /// and off the man's own record, and a transfer that never arrives is a man who
        /// was never sentenced. A scene with no pipeline behind it (the crew demo) keeps
        /// the flat hold, so the arrest still means something there.</summary>
        public void TakeIn(Unit unit, Deed deed = Deed.Affray,
            LivingCity.Police.PrisonPipeline pipeline = null,
            LivingCity.Police.CourtCase file = null)
        {
            if (unit == null) return;

            // EVERY house's men go on the books as held, not only ours. A Falcone
            // soldier taken off the street is a man in a cell on the Falcones' own
            // roster - out of their crew, off their street, still drawing their wage -
            // rather than a body quietly deleted.
            var house = HouseOf(unit.Faction);
            var roster = house?.Roster;
            if (roster == null)
            {
                // Nobody's books behind them - a bench scene's mob, the law itself:
                // its men leave with the officer and that is that.
                RemoveUnit(unit);
                return;
            }

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            int today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            int backOn = today > 0 ? today + HeldDays : 0;
            string stamp = today > 0 ? "DAY " + today : "";
            string charge = Sentencing.ChargeFor(deed);

            int taken = 0;
            foreach (var man in unit.All())
            {
                // NEGATIVE is "not on the books", not "not positive". Every walker with
                // no character carries a NEGATIVE id (DemoCrews' anonymous and rival
                // counters both run downwards), and the roster's FIRST man is id 0 - who,
                // in the opening books, is Don Salvatore himself (RosterSeeder). Written
                // as <= 0 this loop quietly made the Don the one man in the city the
                // police could never take, in the one campaign where he is the only man
                // there is.
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                if (pipeline != null)
                {
                    if (pipeline.Book(roster, man.CharacterId, deed, today, file) != null)
                        taken++;
                }
                else if (LivingCity.Outfit.HouseOps.Jail(house, man.CharacterId, backOn,
                        "Held at the station", charge, stamp).Ok) taken++;
            }
            // NOBODY WENT ON THE BOOKS. The men have already put their hands up, and
            // leaving them there would stand a crew in the street with its guns away for
            // the rest of the campaign - the arrest window clears itself without ever
            // telling them the officer has gone. They pick their guns back up.
            if (taken == 0)
            {
                LetGo(unit);
                return;
            }

            // EVERY MAN OF THIS CREW ON ONE CASE (GAN-245), and the arresting officer
            // on the witness list: he found them at the scene, which is the weakest
            // thing on a charge sheet and still the reason there is one. Whatever the
            // crew never answered for lately is attached as extra counts here, because
            // this is the moment the city has them.
            if (pipeline != null && file != null)
            {
                var found = false;
                for (var w = 0; w < file.Witnesses.Count; w++)
                    if (file.Witnesses[w].Kind == LivingCity.Police.WitnessKind.PoliceFoundThem ||
                        file.Witnesses[w].Kind == LivingCity.Police.WitnessKind.PoliceSawIt)
                        found = true;
                if (!found)
                    file.Witnesses.Add(new LivingCity.Police.Witness
                    {
                        Kind = LivingCity.Police.WitnessKind.PoliceFoundThem,
                        Name = "The arresting officer",
                        Seed = StreetAlarm.IncidentNumber,
                        X = unit.Position.x, Y = unit.Position.y, Z = unit.Position.z,
                    });
                pipeline.AttachOpenComplaints(file, today);
            }

            // and the street re-deals without them: Sync keeps only Active men, so the
            // bodies go the same way a discharged man's does, through the books
            house.Touch();
            if (unit.Faction != LivingCity.Gameplay.PlayerCommands.House.Value)
                return;

            var director = PersonnelDirector.Instance;
            if (director != null)
                director.Touch();
            CrewOverlay.Announce(
                taken == 1 ? "ONE MAN TAKEN IN" : taken + " MEN TAKEN IN",
                5f, new Color(0.55f, 0.78f, 1f));
        }
    }
}
