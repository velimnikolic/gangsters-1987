using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Police
{
    /// <summary>Where a man the city has taken currently stands.</summary>
    public enum PrisonStage
    {
        /// <summary>In the cells at the precinct, waiting on a judge.</summary>
        Held,

        /// <summary>His day in court has come and there is a transfer to run.</summary>
        ForTransfer,

        /// <summary>On the road, in the back of a car.</summary>
        InTransit,

        /// <summary>Sentenced. The rest is a release date on the books.</summary>
        Sentenced,

        /// <summary>Out of the back of a wrecked transfer and away.</summary>
        Freed,
    }

    /// <summary>One man in the pipe.</summary>
    public sealed class Prisoner
    {
        public int CharacterId;
        public Deed Deed;
        public int TakenOnDay;

        /// <summary>The absolute campaign day the transfer runs and the verdict lands.
        /// A day, never a countdown - a counter drifts over a soak or a save.</summary>
        public int CourtDay;

        public int SentenceDays;
        public int OutOnDay;
        public PrisonStage Stage = PrisonStage.Held;
    }

    /// <summary>
    /// STATION, COURT, PRISON - as paper, plus one drive.
    ///
    /// An arrest used to end the moment the officer led the men away: three days flat on
    /// the books and nothing in between. This is the between. A man is HELD at the
    /// precinct with no release date at all (the day tick will not discharge a man
    /// without one); on his court day the precinct runs a transfer, and when it arrives
    /// the verdict lands - the sentence rolled off the deed and his own record, written
    /// on his rap sheet, and only THEN does he have a day to come back on.
    ///
    /// A transfer that never arrives is the other ending. Wreck the car and the man in
    /// the back walks away: off the books as held, back on his feet, wanted, and with
    /// the escape on his sheet for the next judge to add to.
    ///
    /// No prison interior and no court interior: prison is a ledger state, a release
    /// date and the paper (the epic's own rule). Pure and free of UnityEngine, so the
    /// headless suite can run a man through the whole pipe without a scene.
    /// </summary>
    public sealed class PrisonPipeline
    {
        readonly List<Prisoner> _inside = new List<Prisoner>();
        readonly HashSet<int> _everEscaped = new HashSet<int>();

        /// <summary>The seed the sentence rolls come off. Set once from the roster.</summary>
        public int RosterSeed;

        public IReadOnlyList<Prisoner> Inside => _inside;

        public Prisoner Find(int characterId)
        {
            for (var i = 0; i < _inside.Count; i++)
                if (_inside[i].CharacterId == characterId)
                    return _inside[i];
            return null;
        }

        /// <summary>Has this man been out of custody before? The surcharge reads it, and
        /// so does the next judge.</summary>
        public bool EverEscaped(int characterId) => _everEscaped.Contains(characterId);

        /// <summary>
        /// TAKEN IN. He goes on the books as held with NO release date - a man waiting on
        /// a judge is not serving anything yet, and the day tick only discharges a man
        /// who has a day (RosterOps.Discharge).
        /// </summary>
        public Prisoner Book(Roster roster, int characterId, Deed deed, int today)
        {
            // A roster's first man is id 0 (Roster.NextCharacterId), so the guard is
            // "not a member" and not "not positive": the Don himself would fail the
            // second one.
            if (roster == null || characterId < 0)
                return null;
            if (Find(characterId) != null)
                return null;   // already inside; one arrest per man

            var result = RosterOps.Jail(roster, characterId, 0,
                "Held at the station", Sentencing.ChargeFor(deed), Stamp(today));
            if (!result.Ok)
                return null;

            var prisoner = new Prisoner
            {
                CharacterId = characterId,
                Deed = deed,
                TakenOnDay = today,
                CourtDay = today > 0 ? today + Sentencing.DaysToCourt : 0,
                Stage = PrisonStage.Held,
            };
            _inside.Add(prisoner);
            return prisoner;
        }

        /// <summary>
        /// The day turned. Anybody whose court day has come is put up for transfer, and
        /// the caller decides whether there is a car to run it in - a precinct with no
        /// cars left sends no convoy today, and the man simply waits for tomorrow.
        /// </summary>
        public void DayTick(int today, List<Prisoner> wantTransfer)
        {
            wantTransfer?.Clear();
            for (var i = 0; i < _inside.Count; i++)
            {
                var prisoner = _inside[i];
                if (prisoner.Stage != PrisonStage.Held)
                    continue;
                if (prisoner.CourtDay <= 0 || prisoner.CourtDay > today)
                    continue;
                prisoner.Stage = PrisonStage.ForTransfer;
                wantTransfer?.Add(prisoner);
            }
        }

        /// <summary>The car pulled out with him in it.</summary>
        public void Away(Prisoner prisoner)
        {
            if (prisoner != null && prisoner.Stage == PrisonStage.ForTransfer)
                prisoner.Stage = PrisonStage.InTransit;
        }

        /// <summary>The transfer never got there and nobody is going to chase it back
        /// into the yard: the man is held again and waits for the next court day.</summary>
        public void BackToTheCells(Prisoner prisoner, int today)
        {
            if (prisoner == null) return;
            if (prisoner.Stage != PrisonStage.InTransit &&
                prisoner.Stage != PrisonStage.ForTransfer) return;
            prisoner.Stage = PrisonStage.Held;
            if (today > 0) prisoner.CourtDay = today + 1;
        }

        /// <summary>
        /// THE VERDICT. Rolled at the moment the transfer arrives rather than at the
        /// arrest, because until a judge has seen him nothing is decided - and because
        /// the whole point of an interceptable transfer is that a man freed off the road
        /// was never sentenced at all.
        /// </summary>
        public void Convicted(Roster roster, Prisoner prisoner, int today)
        {
            if (roster == null || prisoner == null || prisoner.Stage == PrisonStage.Sentenced)
                return;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null)
                return;

            var rng = new System.Random(
                Sentencing.StreamFor(RosterSeed, prisoner.CharacterId, today));
            var days = Sentencing.Days(prisoner.Deed, rng, EverEscaped(prisoner.CharacterId));
            prisoner.SentenceDays = days;
            prisoner.OutOnDay = Sentencing.IsLife(days) ? Sentencing.Life : today + days;
            prisoner.Stage = PrisonStage.Sentenced;

            member.Status = CharacterStatus.Jailed;
            member.BackOnDay = prisoner.OutOnDay;
            member.ConditionNote = Sentencing.IsLife(days) ? "Serving life" : "Serving his time";
            RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed),
                Sentencing.Verdict(days, Sentencing.IsLife(days) ? 0 : prisoner.OutOnDay));
        }

        /// <summary>
        /// OUT OF THE BACK OF IT. The escort is dead and the man is on the pavement: off
        /// the books as held, on his feet, wanted, and unarmed - gear reaches a man only
        /// through his lieutenant, and nobody has handed him anything.
        /// </summary>
        public Prisoner Freed(Roster roster, Prisoner prisoner, int today)
        {
            if (roster == null || prisoner == null)
                return null;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null)
                return null;

            _inside.Remove(prisoner);
            prisoner.Stage = PrisonStage.Freed;
            _everEscaped.Add(prisoner.CharacterId);

            member.Status = CharacterStatus.Active;
            member.BackOnDay = 0;
            member.ConditionNote = "";
            // W2: freed off a transfer. A week out of sight clears it, and nothing else
            // does (WantedLevels).
            WantedLevels.Mark(member, WantedLevels.FreedFromTransfer, today);

            // AND HE COMES OUT WITH NOTHING IN HIS HANDS. Gear reaches a man only
            // through his lieutenant (NormalizeArms), and nobody has handed a man in the
            // back of a police car anything: what he was carrying when he was taken goes
            // back to his crew's deck, and he is rearmed the ordinary way or not at all.
            // The item's OWNER is left alone - the gun still belongs to the branch.
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].HolderId == member.Id)
                    roster.Equipment[i].HolderId = RosterEquipment.Unheld;
            RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed),
                Sentencing.EscapeOutcome);
            return prisoner;
        }

        /// <summary>He served it, or somebody let him out: he leaves the pipe. Called
        /// off the roster's own discharge, which is the one place a man stands up.</summary>
        public void Discharged(Roster roster)
        {
            if (roster == null)
                return;
            for (var i = _inside.Count - 1; i >= 0; i--)
            {
                var member = roster.Find(_inside[i].CharacterId);
                if (member == null || member.Status != CharacterStatus.Jailed)
                    _inside.RemoveAt(i);
            }
        }

        static string Stamp(int day) => day > 0 ? "DAY " + day : "";
    }
}
