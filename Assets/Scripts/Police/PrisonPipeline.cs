using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Police
{
    /// <summary>Where a man the city has taken currently stands.</summary>
    public enum PrisonStage
    {
        /// <summary>In the cells at the precinct, waiting on a judge.</summary>
        Held,

        /// <summary>A transfer is due to run today - which one is <see cref="Prisoner.Leg"/>.
        /// </summary>
        ForTransfer,

        /// <summary>On the road, in the back of a car.</summary>
        InTransit,

        /// <summary>Sentenced, and held at the court until the van to the prison runs.
        /// The release date is already on his sheet: the second leg is where he serves
        /// it, not when it starts.</summary>
        Sentenced,

        /// <summary>Delivered. The rest is a release date on the books.</summary>
        Serving,

        /// <summary>Out of the back of a wrecked transfer and away.</summary>
        Freed,

        // Appended (GAN-245) so every serialized stage above keeps its meaning.

        /// <summary>Out on the outfit's money until his court day. A normal man on the
        /// street: he takes orders, he can be arrested again, and on the day itself he
        /// is tried whether he turns up or not.</summary>
        Bailed,

        /// <summary>Tried and acquitted, or the case was thrown out. Off the books of
        /// the city entirely.</summary>
        Cleared,

        /// <summary>He was bailed and never appeared. The money is gone and the city
        /// is looking for him.</summary>
        Skipped,
    }

    /// <summary>Which of the two drives a man is on (GAN-237). Both are real roads and
    /// both can be taken.</summary>
    public enum PrisonLeg
    {
        None,

        /// <summary>The station to the courthouse, on his court day.</summary>
        Court,

        /// <summary>The courthouse out of town, on the day after the verdict.</summary>
        Prison,
    }

    /// <summary>One man in the pipe.</summary>
    public sealed class Prisoner
    {
        public int CharacterId;
        public int GangId = -1;
        public Deed Deed;
        public DoorAnswer Answer;
        public bool Sprung;
        public int TakenOnDay;

        /// <summary>The absolute campaign day the transfer to court runs and the verdict
        /// lands. A day, never a countdown - a counter drifts over a soak or a save.</summary>
        public int CourtDay;

        /// <summary>The absolute day the van to the prison runs. Written by the verdict,
        /// because until a judge has spoken there is nothing to deliver him to.</summary>
        public int PrisonDay;

        /// <summary>Which drive is due, or running. None while he is simply held.</summary>
        public PrisonLeg Leg = PrisonLeg.None;

        public int SentenceDays;
        public int OutOnDay;
        public PrisonStage Stage = PrisonStage.Held;

        /// <summary>The docket number he is on, or -1 for an arrest with no case behind
        /// it (the crew demo, and anything else with no city to try him in).</summary>
        public int CaseId = -1;

        /// <summary>What the outfit put up to get him out; 0 while he is inside.</summary>
        public int BailPaid;

        /// <summary>The boss has said he is not turning up. The money is written off on
        /// his court day rather than the moment the order is given, so a player who
        /// changes his mind before then still has a man to send.</summary>
        public bool SkipOrdered;

        /// <summary>How many days running the transfer for his current leg has failed
        /// to run - no car, the patience gone, the car's body lost (AI-006). Reset by
        /// a leg that starts, and by a verdict.</summary>
        public int TransferFails;

        /// <summary>The live physical journey, when there is one. It is intentionally
        /// absent from PrisonSnapshot: saving a transfer puts the man back at its source
        /// and the scheduler runs it again.</summary>
        public CarriageStage? Carriage;
    }

    /// <summary>
    /// STATION, COURT, PRISON - as paper, plus TWO drives and a trial
    /// (GAN-219, GAN-237, GAN-245).
    ///
    /// An arrest used to end the moment the officer led the men away: three days flat on
    /// the books and nothing in between. This is the between. A man is HELD at the
    /// precinct with no release date at all (the day tick will not discharge a man
    /// without one); on his court day the precinct runs a transfer, and when it arrives
    /// he is TRIED - and being tried is a thing that can be lost by the prosecution
    /// (GAN-245). Only then does he have a day to come back on, or an acquittal.
    ///
    /// He is then held AT THE COURT until the van to the prison runs, a day later. That
    /// second drive is a road of its own and can be taken like the first: the state does
    /// not have him until he is delivered.
    ///
    /// A transfer that never arrives is the other ending, on either leg. Wreck the car
    /// and the man in the back walks away: off the books, back on his feet, wanted, and
    /// with the escape on his sheet for the next judge to add to.
    ///
    /// THE DOCKET (GAN-245) lives here beside the prisoners because it has exactly the
    /// same lifetime: a case is opened by an arrest or by a complaint nobody answered,
    /// it carries the witnesses snapshotted when the incident opened, and it is closed
    /// by a verdict. Prisoner.CaseId points at it.
    ///
    /// No prison interior and no court interior: prison is a ledger state, a release
    /// date and the paper (the epic's own rule; the user's call on GAN-237, 2026-09-02,
    /// is that the state prison stays off this map and the county line is what the
    /// second leg drives to). Pure and free of UnityEngine, so the headless suite can run
    /// a man through the whole pipe without a scene.
    /// </summary>
    public sealed class PrisonPipeline
    {
        readonly List<Prisoner> _inside = new List<Prisoner>();
        readonly HashSet<int> _everEscaped = new HashSet<int>();
        readonly List<CourtCase> _cases = new List<CourtCase>();
        int _nextCaseId = 1;

        /// <summary>Days an unanswered complaint stays on the docket. A crew arrested
        /// inside the fortnight answers for it as an extra count; after that the
        /// precinct has other things to think about.</summary>
        public const int ComplaintMemoryDays = 14;

        /// <summary>
        /// THE COURT THAT NEVER SITS (AI-006, ruling A16: "papirni transfer posle 2
        /// propala dana"). A transfer that failed to run this many days running is
        /// carried on paper - no convoy, the man put in front of the judge, or the van
        /// delivered - so a precinct with every car busy cannot hold a man in the
        /// cells for ever a day at a time. Counted per prisoner per leg, and it applies
        /// to BOTH legs and to the player's own men exactly as to the twenty houses.
        /// </summary>
        public const int TransferFailsBeforePaper = 2;

        /// <summary>The seed the sentence and verdict rolls come off. Set once from the
        /// roster.</summary>
        public int RosterSeed;

        /// <summary>
        /// Whether the shopkeeper is still talking on the morning of the trial - the
        /// Fear gate, which only the street can answer (TerritoryFear). Null means yes,
        /// which is what a headless suite with no city means by it.
        /// </summary>
        public System.Func<CourtCase, bool> ComplainantStillTalks;

        public IReadOnlyList<Prisoner> Inside => _inside;
        public IReadOnlyList<CourtCase> Cases => _cases;

        /// <summary>The docket number the next case will take. The save writes it down
        /// so a case opened after a load cannot collide with one already on the books
        /// (GAN-302).</summary>
        public int NextCaseId => _nextCaseId;

        public Prisoner Find(int characterId)
        {
            for (var i = 0; i < _inside.Count; i++)
                if (_inside[i].CharacterId == characterId)
                    return _inside[i];
            return null;
        }

        public CourtCase FindCase(int caseId)
        {
            for (var i = 0; i < _cases.Count; i++)
                if (_cases[i].CaseId == caseId)
                    return _cases[i];
            return null;
        }

        /// <summary>The case one man is on, or null.</summary>
        public CourtCase CaseOf(int characterId)
        {
            var prisoner = Find(characterId);
            if (prisoner != null && prisoner.CaseId >= 0)
                return FindCase(prisoner.CaseId);
            for (var i = 0; i < _cases.Count; i++)
                if (_cases[i].Status == CaseStatus.Open &&
                    _cases[i].HasDefendant(characterId))
                    return _cases[i];
            return null;
        }

        /// <summary>Has this man been out of custody before? The surcharge reads it, and
        /// so does the next judge.</summary>
        public bool EverEscaped(int characterId) => _everEscaped.Contains(characterId);

        // ------------------------------------------------------------------ the docket

        /// <summary>
        /// A NEW CASE. Opened by an arrest, or by a complaint the crew walked away from
        /// before the officer got there - one with no defendants at all, which is what
        /// makes it an extra count later rather than a charge today.
        /// </summary>
        public CourtCase OpenCase(Deed deed, int gangId, int openedDay, int courtDay,
            string businessId = "", string where = "")
        {
            var file = new CourtCase
            {
                CaseId = _nextCaseId++,
                Deed = deed,
                GangId = gangId,
                BusinessId = businessId ?? "",
                Where = where ?? "",
                OpenedDay = openedDay,
                CourtDay = courtDay,
            };
            _cases.Add(file);
            return file;
        }

        /// <summary>Adds a deed-typed count once. Unlike Counts this is not another case.</summary>
        public static bool AttachCharge(CourtCase file, Deed deed)
        {
            if (file == null || file.ExtraCharges.Contains(deed))
                return false;
            file.ExtraCharges.Add(deed);
            return true;
        }

        /// <summary>
        /// Folds every open complaint against this crew, inside the memory window, into
        /// the case that is actually going to be heard. Each one is worth
        /// a deed-weighted surcharge on a conviction, and each one is
        /// closed by being folded - a count cannot be charged twice.
        /// </summary>
        public int AttachOpenComplaints(CourtCase file, int today)
        {
            if (file == null)
                return 0;
            var attached = 0;
            for (var i = 0; i < _cases.Count; i++)
            {
                var other = _cases[i];
                if (other == file || other.Status != CaseStatus.Open) continue;
                if (other.GangId != file.GangId) continue;
                if (other.Defendants.Count > 0) continue;   // somebody was taken for it
                if (!other.AnyWilling()) continue;          // nobody can put this count up
                if (today > 0 && other.OpenedDay > 0 &&
                    today - other.OpenedDay > ComplaintMemoryDays) continue;

                file.Counts.Add(other.CaseId);
                // FOLDED, NOT TRIED. Nobody stood up for this one: it lives on as a
                // count on the case that is actually going to be heard, and the archive
                // must not print it as a trial that happened (GAN-302).
                other.Status = CaseStatus.Folded;
                attached++;
            }
            return attached;
        }

        /// <summary>Open complaints against a crew, for the paper and the map.</summary>
        public int OpenComplaintsAgainst(int gangId, int today)
        {
            var count = 0;
            for (var i = 0; i < _cases.Count; i++)
            {
                var file = _cases[i];
                if (file.Status != CaseStatus.Open || file.GangId != gangId) continue;
                if (file.Defendants.Count > 0) continue;
                if (today > 0 && file.OpenedDay > 0 &&
                    today - file.OpenedDay > ComplaintMemoryDays) continue;
                count++;
            }
            return count;
        }

        /// <summary>Every case still open with a witness the player could do something
        /// about - what the turf map draws its markers from.</summary>
        public void OpenCases(int gangId, List<CourtCase> into)
        {
            into?.Clear();
            if (into == null) return;
            for (var i = 0; i < _cases.Count; i++)
                if (_cases[i].Status == CaseStatus.Open && _cases[i].GangId == gangId)
                    into.Add(_cases[i]);
        }

        // -------------------------------------------------------------------- the pipe

        /// <summary>
        /// Everybody who has ever come out of the back of a car (RIVAL-010). The set
        /// itself is private because nothing but the pipe may add to it; the campaign
        /// save has to be able to write it down, and a man's second sentence is longer
        /// than his first.
        /// </summary>
        public void CollectEscapes(List<int> into)
        {
            if (into == null)
                return;
            into.Clear();
            foreach (var characterId in _everEscaped)
                into.Add(characterId);
            into.Sort();
        }

        /// <summary>
        /// THE LOAD BOUNDARY (RIVAL-010). Everybody the city was holding, and everybody
        /// it remembered, put back as they were.
        ///
        /// This is not optional plumbing. A held man carries NO release date - the day
        /// tick refuses to discharge a man without one - so the pipe is the only thing
        /// that will ever let him out. A load that rebuilt an empty pipe left him jailed
        /// for the rest of the campaign, drawing his envelope, against his lieutenant's
        /// headcount, and never coming back.
        /// </summary>
        public void RestoreFrom(
            IReadOnlyList<Prisoner> inside, IReadOnlyList<CourtCase> cases,
            IReadOnlyList<int> escaped, int nextCaseId, int rosterSeed)
        {
            _inside.Clear();
            _everEscaped.Clear();
            _cases.Clear();
            RosterSeed = rosterSeed;

            for (var i = 0; inside != null && i < inside.Count; i++)
                if (inside[i] != null)
                    _inside.Add(inside[i]);
            // AND THE DOCKET WITH THEM (GAN-302). A held man restored without his case
            // was tried with nothing behind him: the trial's "no docket, no defence"
            // branch convicts without a roll, so every witness the player had leaned on
            // counted for nothing the moment he loaded.
            for (var i = 0; cases != null && i < cases.Count; i++)
                if (cases[i] != null)
                    _cases.Add(cases[i]);
            for (var i = 0; escaped != null && i < escaped.Count; i++)
                _everEscaped.Add(escaped[i]);

            _nextCaseId = nextCaseId > 0 ? nextCaseId : 1;
            // A file written before the docket was saved carries no number at all; the
            // next case must still not collide with anything that came back.
            for (var i = 0; i < _cases.Count; i++)
                if (_cases[i].CaseId >= _nextCaseId)
                    _nextCaseId = _cases[i].CaseId + 1;
        }

        /// <summary>
        /// TAKEN IN. He goes on the books as held with NO release date - a man waiting on
        /// a judge is not serving anything yet, and the day tick only discharges a man
        /// who has a day (RosterOps.Discharge).
        /// </summary>
        public Prisoner Book(Roster roster, int characterId, Deed deed, int today,
            CourtCase file = null, DoorAnswer answer = DoorAnswer.Quiet, bool sprung = false)
        {
            // A roster's first man is id 0 (Roster.NextCharacterId), so the guard is
            // "not a member" and not "not positive": the Don himself would fail the
            // second one.
            if (roster == null || characterId < 0)
                return null;

            var standing = Find(characterId);
            if (standing != null)
            {
                // TAKEN AGAIN WHILE OUT ON BAIL. A bailed man is an ordinary man on the
                // street (that is the whole point of bail), so he can walk into another
                // arrest - and the pipe used to answer that by booking nothing at all,
                // which left the crew stood in the street with its hands up and the new
                // case with no defendant on it.
                if (standing.Stage == PrisonStage.Bailed)
                    return ReBook(roster, standing, deed, today, file, answer, sprung);
                return null;   // already inside; one arrest per man
            }

            var result = RosterOps.Jail(roster, characterId, 0,
                "Held at the station", Sentencing.ChargeFor(deed), Stamp(today));
            if (!result.Ok)
                return null;

            var courtDay = today > 0 ? today + Sentencing.DaysToCourt : 0;
            if (file != null && file.CourtDay > 0)
                courtDay = file.CourtDay;

            var prisoner = new Prisoner
            {
                CharacterId = characterId,
                GangId = roster.GangId,
                Deed = deed,
                Answer = answer,
                Sprung = sprung || EverEscaped(characterId),
                TakenOnDay = today,
                CourtDay = courtDay,
                Stage = PrisonStage.Held,
                CaseId = file != null ? file.CaseId : -1,
            };
            _inside.Add(prisoner);

            if (file != null)
            {
                if (file.CourtDay <= 0) file.CourtDay = courtDay;
                if (!file.Defendants.Contains(characterId))
                    file.Defendants.Add(characterId);
            }
            return prisoner;
        }

        /// <summary>
        /// BACK INSIDE. His bail bought him the days up to a court date and he has spent
        /// them getting arrested again: he returns to the cells on whichever deed is the
        /// worse of the two, on a fresh court day, and the case he was already answering
        /// for is folded into the new one as an extra count rather than left open for a
        /// trial that will never be listed.
        ///
        /// The bail money is NOT refunded and NOT forfeit: he did not abscond, and the
        /// safe does not get it back either way.
        /// </summary>
        Prisoner ReBook(Roster roster, Prisoner prisoner, Deed deed, int today,
            CourtCase file, DoorAnswer answer, bool sprung)
        {
            var member = roster.Find(prisoner.CharacterId);
            if (member == null || member.Gone)
                return null;

            var worse = Worse(prisoner.Deed, deed);
            var result = RosterOps.Jail(roster, prisoner.CharacterId, 0,
                "Held at the station", Sentencing.ChargeFor(worse), Stamp(today));
            if (!result.Ok)
                return null;

            var old = prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null;

            prisoner.Deed = worse;
            prisoner.GangId = roster.GangId;
            prisoner.Answer = SurrenderRoll.MostSerious(prisoner.Answer, answer);
            prisoner.Sprung = prisoner.Sprung || sprung || EverEscaped(prisoner.CharacterId);
            prisoner.Stage = PrisonStage.Held;
            prisoner.Leg = PrisonLeg.None;
            prisoner.TakenOnDay = today;
            prisoner.SkipOrdered = false;
            member.BailedUntil = 0;

            var courtDay = today > 0 ? today + Sentencing.DaysToCourt : 0;
            if (file != null)
            {
                if (file.CourtDay <= 0) file.CourtDay = courtDay;
                courtDay = file.CourtDay;
                if (!file.Defendants.Contains(prisoner.CharacterId))
                    file.Defendants.Add(prisoner.CharacterId);
                prisoner.CaseId = file.CaseId;

                // What he was already answering for goes on the new sheet as a count.
                if (old != null && old != file && old.Status == CaseStatus.Open)
                {
                    Drop(old, prisoner.CharacterId);
                    if (!file.Counts.Contains(old.CaseId)) file.Counts.Add(old.CaseId);
                    old.Status = CaseStatus.Folded;
                }
            }
            prisoner.CourtDay = courtDay;
            return prisoner;
        }

        /// <summary>The graver of two deeds - what a man taken twice is held on. Read
        /// off the band rather than the enum's order, so appending a deed later cannot
        /// silently re-rank the ones above it.</summary>
        static Deed Worse(Deed first, Deed second) =>
            Sentencing.BandHigh(second) > Sentencing.BandHigh(first) ? second : first;

        /// <summary>
        /// The day turned. Anybody whose day has come is put up for a transfer - the one
        /// to court for a man still held, the one out of town for a man already sentenced
        /// - and the caller decides whether there is a car to run it in. A precinct with
        /// no car free sends no convoy today, and the man simply waits for tomorrow.
        /// </summary>
        public void DayTick(int today, List<Prisoner> wantTransfer,
            List<Prisoner> onPaper = null)
        {
            wantTransfer?.Clear();
            onPaper?.Clear();
            for (var i = 0; i < _inside.Count; i++)
            {
                var prisoner = _inside[i];
                if (prisoner.Stage == PrisonStage.Held)
                {
                    if (prisoner.CourtDay <= 0 || prisoner.CourtDay > today)
                        continue;
                    prisoner.Stage = PrisonStage.ForTransfer;
                    prisoner.Leg = PrisonLeg.Court;
                    Route(prisoner, wantTransfer, onPaper);
                    continue;
                }

                if (prisoner.Stage != PrisonStage.Sentenced)
                    continue;
                if (prisoner.PrisonDay <= 0 || prisoner.PrisonDay > today)
                    continue;
                prisoner.Stage = PrisonStage.ForTransfer;
                prisoner.Leg = PrisonLeg.Prison;
                Route(prisoner, wantTransfer, onPaper);
            }

            LapseAbandonedCases(today);
        }

        /// <summary>A leg that has failed enough days running goes on paper (AI-006);
        /// a caller that hands no paper list gets it as a convoy as before.</summary>
        static void Route(Prisoner prisoner, List<Prisoner> wantTransfer,
            List<Prisoner> onPaper)
        {
            if (onPaper != null && prisoner.TransferFails >= TransferFailsBeforePaper)
                onPaper.Add(prisoner);
            else
                wantTransfer?.Add(prisoner);
        }

        /// <summary>
        /// THE LEG CARRIED ON PAPER (AI-006). No car ran for him twice; the court leg
        /// puts him in front of the judge today, the prison leg delivers him. The same
        /// verdict roll and the same paper as a man off the back of a convoy - only
        /// the road is skipped, and with it the player's chance at the car.
        /// </summary>
        public void OnPaper(Roster roster, Prisoner prisoner, int today)
        {
            if (roster == null || prisoner == null ||
                prisoner.Stage != PrisonStage.ForTransfer)
                return;
            prisoner.TransferFails = 0;
            if (prisoner.Leg == PrisonLeg.Prison)
            {
                prisoner.Stage = PrisonStage.InTransit;
                Delivered(prisoner);
                return;
            }
            prisoner.Stage = PrisonStage.InTransit;
            Tried(roster, prisoner, today);
        }

        /// <summary>
        /// A CASE NOBODY IS LEFT TO TRY LAPSES (GAN-302).
        ///
        /// A man who skips his bail stays a defendant - that is the epic's ruling, and
        /// it is what lets a re-arrest fold the old charge in as a count. But if he is
        /// never taken again, the case sits Open forever: it keeps a card on the docket
        /// for a defendant who is neither held nor bailed nor even wanted any more, and
        /// its witness markers stay on the map for a trial that cannot be listed.
        ///
        /// So an open case whose court day is a memory window behind and whose every
        /// remaining defendant is out of the pipe is FOLDED, with whatever verdicts it
        /// collected already on it. A complaint nobody was ever taken for is untouched:
        /// it has no defendants at all and is exactly what becomes a count later.
        /// </summary>
        void LapseAbandonedCases(int today)
        {
            if (today <= 0) return;
            for (var i = 0; i < _cases.Count; i++)
            {
                var file = _cases[i];
                if (file.Status != CaseStatus.Open) continue;
                if (file.Defendants.Count == 0) continue;
                if (file.CourtDay <= 0 ||
                    today - file.CourtDay <= ComplaintMemoryDays) continue;

                var anybodyToTry = false;
                for (var d = 0; d < file.Defendants.Count && !anybodyToTry; d++)
                    anybodyToTry = Find(file.Defendants[d]) != null;
                if (anybodyToTry) continue;

                file.Status = file.AnyTried ? CaseStatus.Tried : CaseStatus.Folded;
            }
        }

        /// <summary>The car pulled out with him in it.</summary>
        public void Away(Prisoner prisoner)
        {
            if (prisoner != null && prisoner.Stage == PrisonStage.ForTransfer)
                prisoner.Stage = PrisonStage.InTransit;
        }

        /// <summary>The transfer never got there and nobody is going to chase it back
        /// into the yard: the man goes back where he came from and rides again tomorrow -
        /// the cells for the court leg, the court for the prison leg. A sentence already
        /// passed is not passed a second time.</summary>
        public void BackToTheCells(Prisoner prisoner, int today)
        {
            if (prisoner == null) return;
            if (prisoner.Stage != PrisonStage.InTransit &&
                prisoner.Stage != PrisonStage.ForTransfer) return;
            // One more day the road did not run for him (AI-006).
            prisoner.TransferFails++;
            if (prisoner.Leg == PrisonLeg.Prison)
            {
                prisoner.Stage = PrisonStage.Sentenced;
                if (today > 0) prisoner.PrisonDay = today + 1;
            }
            else
            {
                prisoner.Stage = PrisonStage.Held;
                if (today > 0) prisoner.CourtDay = today + 1;
            }
            prisoner.Leg = PrisonLeg.None;
        }

        // -------------------------------------------------------------------- the bail

        /// <summary>Why he cannot be bailed, or null when he can. The lawyer is the
        /// gate: a man with no counsel does not get a remand hearing listed at all, and
        /// there is no bail whatever on a dead policeman.</summary>
        public string BailRefusal(Prisoner prisoner, int lawyerSkill)
        {
            if (prisoner == null)
                return LivingCity.UI.LedgerText.ReasonNoCase;
            if (prisoner.Stage == PrisonStage.Bailed)
                return LivingCity.UI.LedgerText.ReasonAlreadyBailed;
            if (prisoner.Stage != PrisonStage.Held)
                return LivingCity.UI.LedgerText.ReasonNotInside;
            if (Sentencing.Bail(prisoner.Deed) <= 0)
                return LivingCity.UI.LedgerText.ReasonNoBail;
            if (lawyerSkill < Lawyer.BailSkill)
                return LivingCity.UI.LedgerText.ReasonNoCounsel;
            return null;
        }

        /// <summary>What it costs to get this one out.</summary>
        public static int BailPrice(Prisoner prisoner) =>
            prisoner == null ? 0 : Sentencing.Bail(prisoner.Deed);

        /// <summary>
        /// OUT UNTIL HIS DAY. The money has already left the safe (the caller's -
        /// BalanceMath.TryPurchase, so it lands on the day sheet); this is the man.
        /// He walks out Active with a day stamped on him and is an ordinary man on the
        /// street until it comes.
        /// </summary>
        public bool PostBail(Roster roster, Prisoner prisoner, int paid, int today)
        {
            if (roster == null || prisoner == null || prisoner.Stage != PrisonStage.Held)
                return false;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null || member.Gone)
                return false;

            prisoner.Stage = PrisonStage.Bailed;
            prisoner.BailPaid = paid;
            member.Status = CharacterStatus.Active;
            member.BackOnDay = 0;
            member.ConditionNote = "";
            member.BailedUntil = prisoner.CourtDay > 0
                ? prisoner.CourtDay
                : (today > 0 ? today + Sentencing.DaysToCourt : 0);
            member.BailPaid = paid;
            return true;
        }

        /// <summary>The boss says he is not turning up. Nothing happens until the day
        /// itself - see <see cref="TryOnPaper"/>.</summary>
        public bool SkipBail(Prisoner prisoner)
        {
            if (prisoner == null || prisoner.Stage != PrisonStage.Bailed)
                return false;
            prisoner.SkipOrdered = true;
            return true;
        }

        /// <summary>
        /// HIS DAY CAME AND HE IS NOT IN THE DOCK. The money is gone, the case stays
        /// open against him, and the city is looking for him on the same terms as a man
        /// out of the back of a transfer - a week out of sight and nothing else.
        /// </summary>
        public void Forfeit(Roster roster, Prisoner prisoner, int today)
        {
            if (roster == null || prisoner == null) return;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null) return;

            _inside.Remove(prisoner);
            prisoner.Stage = PrisonStage.Skipped;
            member.BailedUntil = 0;
            // The case stays OPEN against him (the GAN-245 ruling), but what he did is
            // on its record from this morning - the archive prints a forfeit whether or
            // not the case is ever heard.
            Note(prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null,
                prisoner.CharacterId, CaseOutcome.BailForfeit, today,
                answer: prisoner.Answer, sprung: prisoner.Sprung);
            WantedLevels.Mark(member, WantedLevels.FreedFromTransfer, today);
            RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed),
                Sentencing.BailForfeitOutcome);
        }

        /// <summary>
        /// The day tick's half of bail: every bailed man whose day has come is tried on
        /// paper with the rest of his case - unless he is hidden, out of town or the
        /// boss ordered him to skip, in which case the money is forfeit instead.
        ///
        /// Returns how many were dealt with, so the caller can decide whether the
        /// ledger needs repainting.
        /// </summary>
        public int TryOnPaper(Roster roster, int today, List<Prisoner> forfeited = null,
            List<Prisoner> tried = null)
        {
            forfeited?.Clear();
            tried?.Clear();
            if (roster == null || today <= 0)
                return 0;

            var done = 0;
            for (var i = _inside.Count - 1; i >= 0; i--)
            {
                var prisoner = _inside[i];
                if (prisoner.GangId >= 0 && prisoner.GangId != roster.GangId)
                    continue;
                if (prisoner.Stage != PrisonStage.Bailed) continue;
                if (prisoner.CourtDay <= 0 || prisoner.CourtDay > today) continue;

                var member = roster.Find(prisoner.CharacterId);
                if (member == null) continue;

                var runs = prisoner.SkipOrdered || member.OutOfTown ||
                           member.Gone || member.WantedLevel > 0;
                if (runs)
                {
                    Forfeit(roster, prisoner, today);
                    forfeited?.Add(prisoner);
                }
                else
                {
                    member.BailedUntil = 0;
                    prisoner.Stage = PrisonStage.InTransit;   // he walked into the court
                    Tried(roster, prisoner, today);
                    // A man tried on paper gets the SAME paper as a man tried off the
                    // back of a convoy: without this he changed from active to serving
                    // a sentence with nothing said about it anywhere.
                    tried?.Add(prisoner);
                }
                done++;
            }
            return done;
        }

        // ------------------------------------------------------------------- the trial

        /// <summary>
        /// THE TRIAL, for one defendant, rolled at the moment the transfer arrives -
        /// because until a judge has seen him nothing is decided, and because the whole
        /// point of an interceptable transfer is that a man freed off the road was never
        /// tried at all.
        ///
        /// Three ways out: the case is thrown out before any roll because nobody is left
        /// to give evidence, he is acquitted, or he is convicted and the sentence table
        /// says for how long. One roll per man per case on one deterministic stream, so
        /// the same city and the same day give the same verdict.
        /// </summary>
        public void Tried(Roster roster, Prisoner prisoner, int today)
        {
            if (roster == null || prisoner == null ||
                prisoner.Stage == PrisonStage.Sentenced ||
                prisoner.Stage == PrisonStage.Cleared)
                return;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null)
                return;

            var file = prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null;
            var counsel = Lawyer.Counsel(roster);
            var lawyerSkill = counsel == null ? 0 : Lawyer.Skill(counsel);
            if (file != null && counsel != null)
                file.LawyerId = counsel.Id;

            // The shopkeeper's nerve is asked ONCE, on the morning of the trial: he has
            // had the night, with the family standing in his doorway, to think it over.
            if (file != null && ComplainantStillTalks != null && !ComplainantStillTalks(file))
                Silence(file, WitnessKind.Complainant);

            // A CASE WITH NOBODY BEHIND IT IS NOT TRIED AT ALL. Every witness withdrawn
            // or dead and no policeman on the list, and the men walk before a single
            // roll - which is what leaning on witnesses is FOR.
            if (file != null && !file.AnyWilling())
            {
                Note(file, prisoner.CharacterId, CaseOutcome.Dismissed, today,
                    answer: prisoner.Answer, sprung: prisoner.Sprung);
                ResolveDefendant(file, prisoner.CharacterId, CaseStatus.Dismissed);
                Walks(roster, member, prisoner, today, Sentencing.DismissedOutcome);
                if (counsel != null) counsel.CasesWon++;
                return;
            }

            var rng = new System.Random(
                Sentencing.StreamFor(RosterSeed, prisoner.CharacterId, today));

            // NO DOCKET, NO DEFENCE. An arrest made in a scene that keeps no cases -
            // the crew demo, a bench, anything without a city behind it - is the old
            // behaviour exactly: caught is convicted, and the only question is how
            // long. The verdict roll is skipped rather than won, so it consumes no
            // draw and the sentence off one stream is the same number it always was.
            if (file != null &&
                !Verdict.Convicts(
                    Verdict.ConvictionChance(file, Priors(member), lawyerSkill), rng))
            {
                Note(file, prisoner.CharacterId, CaseOutcome.Acquitted, today,
                    answer: prisoner.Answer, sprung: prisoner.Sprung);
                ResolveDefendant(file, prisoner.CharacterId, CaseStatus.Tried);
                Walks(roster, member, prisoner, today, Sentencing.AcquittedOutcome);
                if (counsel != null) counsel.CasesWon++;
                return;
            }

            ResolveDefendant(file, prisoner.CharacterId, CaseStatus.Tried);
            if (counsel != null) counsel.CasesLost++;

            var countDays = FoldedCountDays(file);
            var days = Sentencing.Days(prisoner.Deed, rng,
                EverEscaped(prisoner.CharacterId), member.Rank,
                Notability.Marked(member, today), lawyerSkill,
                file != null ? file.Counts.Count + file.ExtraCharges.Count : 0,
                prisoner.Answer, countDays);

            prisoner.SentenceDays = days;
            prisoner.OutOnDay = Sentencing.IsLife(days) ? Sentencing.Life : today + days;
            prisoner.Stage = PrisonStage.Sentenced;
            prisoner.Leg = PrisonLeg.None;
            // A new leg, a fresh count of failed days (AI-006).
            prisoner.TransferFails = 0;
            // AND THE VAN IS BOOKED. He is held at the court overnight and driven out of
            // town in the morning; the road between is the player's last chance at him.
            prisoner.PrisonDay = today > 0 ? today + Sentencing.DaysToPrison : 0;

            member.Status = CharacterStatus.Jailed;
            member.BackOnDay = prisoner.OutOnDay;
            member.BailedUntil = 0;
            member.ConditionNote = Sentencing.IsLife(days) ? "Serving life" : "Serving his time";
            Note(file, prisoner.CharacterId, CaseOutcome.Convicted, today, days,
                Sentencing.IsLife(days) ? 0 : prisoner.OutOnDay,
                prisoner.Answer, prisoner.Sprung);
            RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed),
                Sentencing.Verdict(days, Sentencing.IsLife(days) ? 0 : prisoner.OutOnDay));
        }

        /// <summary>The attached docket keeps the gravity of each deed. A missing
        /// legacy case ID has no deed to read and therefore retains the old flat value.</summary>
        public int FoldedCountDays(CourtCase file)
        {
            if (file == null)
                return 0;
            var days = 0;
            for (var i = 0; i < file.Counts.Count; i++)
            {
                var count = FindCase(file.Counts[i]);
                days += Sentencing.ExtraCountDays(count != null ? (Deed?)count.Deed : null);
            }
            for (var i = 0; i < file.ExtraCharges.Count; i++)
                days += Sentencing.ExtraCountDays(file.ExtraCharges[i]);
            return days;
        }

        /// <summary>The old door, kept for the callers that only ever wanted the
        /// convicted branch of it - and because a scene with no docket behind it still
        /// has to be able to sentence a man.</summary>
        public void Convicted(Roster roster, Prisoner prisoner, int today) =>
            Tried(roster, prisoner, today);

        /// <summary>He walked: acquitted, or the case was thrown out. Off the books of
        /// the city, back on his feet, and NOT wanted - a man the court let go is not a
        /// man on the run.</summary>
        void Walks(Roster roster, Character member, Prisoner prisoner, int today,
            string outcome)
        {
            _inside.Remove(prisoner);
            prisoner.Stage = PrisonStage.Cleared;
            // AND NO VAN IS BOOKED FOR HIM. Only a sentenced man rides the second leg
            // (GAN-237): a man the court let go is off the road as well as off the books.
            prisoner.Leg = PrisonLeg.None;
            prisoner.SentenceDays = 0;
            prisoner.OutOnDay = 0;
            prisoner.PrisonDay = 0;

            member.Status = CharacterStatus.Active;
            member.BackOnDay = 0;
            member.BailedUntil = 0;
            member.ConditionNote = "";
            RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed), outcome);
        }

        /// <summary>How many convictions the city already has on him - what the judge
        /// has in front of him, capped by the arithmetic that reads it.</summary>
        public static int Priors(Character member)
        {
            if (member == null)
                return 0;
            var priors = 0;
            for (var i = 0; i < member.RapSheet.Count; i++)
            {
                var outcome = member.RapSheet[i].Outcome;
                if (!string.IsNullOrEmpty(outcome) && outcome.StartsWith("Convicted"))
                    priors++;
            }
            return priors;
        }

        static void Silence(CourtCase file, WitnessKind kind)
        {
            for (var i = 0; i < file.Witnesses.Count; i++)
                if (file.Witnesses[i].Kind == kind &&
                    file.Witnesses[i].Standing == WitnessStanding.WillTestify)
                    file.Witnesses[i].Standing = WitnessStanding.Withdrawn;
        }

        /// <summary>
        /// DELIVERED. The van reached the county line and the state has him: nothing on
        /// his sheet changes - the sentence and the release date were the verdict's - and
        /// he simply stops being a thing on a road anybody can take.
        /// </summary>
        public void Delivered(Prisoner prisoner)
        {
            if (prisoner == null || prisoner.Stage != PrisonStage.InTransit)
                return;
            prisoner.Stage = PrisonStage.Serving;
            prisoner.Leg = PrisonLeg.None;
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
            // ONLY A MAN WHO WAS IN THE CAR. An escort shot to pieces on its way to
            // COLLECT him kills two policemen and frees nobody - he is still in the cells,
            // or still at the court (GAN-237). The caller checks it too; the pipe refuses
            // it here because this is where the invariant belongs.
            if (prisoner.Stage != PrisonStage.InTransit)
                return null;
            var member = roster.Find(prisoner.CharacterId);
            if (member == null)
                return null;

            _inside.Remove(prisoner);
            prisoner.Stage = PrisonStage.Freed;
            prisoner.Leg = PrisonLeg.None;
            _everEscaped.Add(prisoner.CharacterId);

            // The case stays open, but getting out of a police transfer is itself a
            // count the eventual judge hears. Keep it on the same deed-typed docket
            // path as the arrest-side spring rather than burying it in prose.
            AttachCharge(prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null,
                Deed.Resisting);

            member.Status = CharacterStatus.Active;
            member.BackOnDay = 0;
            member.BailedUntil = 0;
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

        /// <summary>
        /// KILLED IN THE TRANSFER. The physical death reaches the roster through the
        /// shared street death channel; this method closes only the court/pipeline side
        /// of that same death. Keeping those doors separate prevents a blast from
        /// striking the roster twice.
        /// </summary>
        public Prisoner Killed(Roster roster, int characterId, int today)
        {
            var prisoner = Find(characterId);
            if (prisoner == null)
                return null;

            var member = roster?.Find(characterId);
            var file = prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null;
            _inside.Remove(prisoner);
            prisoner.Stage = PrisonStage.Cleared;
            prisoner.Leg = PrisonLeg.None;
            prisoner.Carriage = CarriageStage.Delivered;
            Note(file, characterId, CaseOutcome.Killed, today,
                answer: prisoner.Answer, sprung: prisoner.Sprung);
            ResolveDefendant(file, characterId, CaseStatus.Folded);
            if (member != null)
                RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(prisoner.Deed),
                    Sentencing.KilledInTransferOutcome);
            return prisoner;
        }

        /// <summary>
        /// Freed on the first, station-bound leg. There is deliberately no booking to
        /// undo: the roster remains active, while the escape and wanted truth are still
        /// recorded for the next judge.
        /// </summary>
        public bool Sprung(Roster roster, int characterId, int today)
        {
            if (roster == null) return false;
            var member = roster.Find(characterId);
            if (member == null || member.Gone) return false;
            _everEscaped.Add(characterId);
            WantedLevels.Mark(member, WantedLevels.FreedFromTransfer, today);
            ConfiscateWeapons(roster, characterId);

            // Normally this door is used before booking and there is no pipeline row
            // to remove. A courthouse transfer creates the one deliberate exception:
            // the man has walked out of the cells but has not yet sat in the car. Freed
            // must still refuse him (he never rode), while Sprung releases that exact
            // ForTransfer row and leaves the open case carrying the resisting count.
            var transfer = Find(characterId);
            if (transfer != null && transfer.Stage == PrisonStage.ForTransfer)
            {
                _inside.Remove(transfer);
                transfer.Stage = PrisonStage.Freed;
                transfer.Leg = PrisonLeg.None;
                transfer.Sprung = true;
                transfer.Carriage = null;
                member.Status = CharacterStatus.Active;
                member.BackOnDay = 0;
                member.BailedUntil = 0;
                member.ConditionNote = "";
                AttachCharge(transfer.CaseId >= 0 ? FindCase(transfer.CaseId) : null,
                    Deed.Resisting);
                RapSheet.Add(member, Stamp(today), Sentencing.ChargeFor(transfer.Deed),
                    Sentencing.EscapeOutcome);
            }
            return true;
        }

        /// <summary>The station-bound arrest confiscates what the man carried. Unlike
        /// a transfer wreck, these pieces do not go back into the crew's deck: they
        /// leave the outfit's books at the car door.</summary>
        public static int ConfiscateWeapons(Roster roster, int characterId)
        {
            if (roster == null || characterId < 0) return 0;
            var taken = 0;
            for (var i = roster.Equipment.Count - 1; i >= 0; i--)
            {
                var item = roster.Equipment[i];
                if (item.HolderId != characterId || !RosterOps.IsWeapon(item.Kind))
                    continue;
                roster.Equipment.RemoveAt(i);
                taken++;
            }
            return taken;
        }

        /// <summary>He served it, or somebody let him out: he leaves the pipe. Called
        /// off the roster's own discharge, which is the one place a man stands up.
        /// A man out on bail is NOT swept: he is Active on purpose and still owes the
        /// court a morning.</summary>
        public int Discharged(Roster roster, List<int> released = null)
        {
            if (roster == null)
                return 0;
            var count = 0;
            for (var i = _inside.Count - 1; i >= 0; i--)
            {
                if (_inside[i].GangId >= 0 && _inside[i].GangId != roster.GangId)
                    continue;
                if (_inside[i].Stage == PrisonStage.Bailed)
                    continue;
                var member = roster.Find(_inside[i].CharacterId);
                if (member == null)
                    continue;
                if (member.Status != CharacterStatus.Jailed)
                {
                    released?.Add(_inside[i].CharacterId);
                    _inside.RemoveAt(i);
                    count++;
                }
            }
            return count;
        }

        /// <summary>The boss cut him loose: the outfit's file is closed, and the city
        /// keeps him. He comes off the outfit's side of the pipe and his case goes on
        /// without him.</summary>
        public void CutLoose(int characterId, int today = 0)
        {
            for (var i = _inside.Count - 1; i >= 0; i--)
            {
                if (_inside[i].CharacterId != characterId) continue;
                var prisoner = _inside[i];
                var file = prisoner.CaseId >= 0 ? FindCase(prisoner.CaseId) : null;
                _inside.RemoveAt(i);
                Note(file, characterId, CaseOutcome.CutLoose, today,
                    answer: prisoner.Answer, sprung: prisoner.Sprung);
                DropDefendant(file, characterId);
            }
            // He may be on an open case without being in the pipe at all - bailed and
            // struck off the same morning, say - so the docket is swept as well.
            for (var i = 0; i < _cases.Count; i++)
                if (_cases[i].Status == CaseStatus.Open &&
                    _cases[i].HasDefendant(characterId))
                {
                    var prisoner = Find(characterId);
                    Note(_cases[i], characterId, CaseOutcome.CutLoose, today,
                        answer: prisoner != null ? prisoner.Answer : DoorAnswer.Quiet,
                        sprung: prisoner != null && prisoner.Sprung);
                    DropDefendant(_cases[i], characterId);
                }
        }

        /// <summary>
        /// THIS MAN HAS HAD HIS DAY, and the case is only OVER when every name on it
        /// has had one.
        ///
        /// A case carries the whole crew (the epic's rule: they go in together) and the
        /// verdict is per man - but the transfer need not bring them in one car, and
        /// some of them can be held over to another day. Stamping the shared case on
        /// the FIRST verdict took it straight off OpenCases and dropped its witness
        /// markers while men were still waiting to be tried: the player lost the
        /// counterplay he still had, and an unresolved prosecution stopped showing.
        ///
        /// So each verdict takes only that man off the list, exactly as a man cut loose
        /// is taken off it (DropDefendant), and the LAST one closes the case. A trial
        /// anywhere on it beats a dismissal: a case where one man was heard was not
        /// thrown out, whatever the men after him managed.
        /// </summary>
        static void ResolveDefendant(CourtCase file, int characterId, CaseStatus outcome)
        {
            if (file == null) return;
            if (outcome == CaseStatus.Tried) file.AnyTried = true;
            file.Defendants.Remove(characterId);
            if (file.Defendants.Count > 0) return;
            file.Status = file.AnyTried ? CaseStatus.Tried : outcome;
        }

        /// <summary>
        /// WHAT BECAME OF THIS MAN, written on the case (GAN-302). One line per man per
        /// close, and this is the only door: the rap sheet is his own book and keeps its
        /// prose, while the docket keeps the record the ledger's archive prints.
        ///
        /// A man can only be closed once on one case - a second call for the same name
        /// overwrites nothing and adds nothing, so a re-tried man cannot end up on the
        /// sheet twice.
        /// </summary>
        static void Note(CourtCase file, int characterId, CaseOutcome outcome,
            int today, int days = 0, int outOnDay = 0,
            DoorAnswer answer = DoorAnswer.Quiet, bool sprung = false)
        {
            if (file == null || file.VerdictFor(characterId) != null)
                return;
            file.Verdicts.Add(new CaseVerdict
            {
                CharacterId = characterId,
                Outcome = outcome,
                Answer = answer,
                Sprung = sprung,
                Days = days,
                OutOnDay = outOnDay,
                Day = today,
            });
        }

        /// <summary>
        /// He is off this case. A case that had defendants and has none left is CLOSED:
        /// nothing will ever put it up for transfer again, and an open case with nobody
        /// on it goes on drawing witness markers and taking leans for a trial that
        /// cannot happen.
        ///
        /// A case that never had a defendant is a COMPLAINT nobody was taken for, and
        /// that one is left open on purpose - it is what becomes an extra count the next
        /// time these men are taken.
        /// </summary>
        void DropDefendant(CourtCase file, int characterId)
        {
            if (file == null || !file.Defendants.Remove(characterId))
                return;
            if (file.Defendants.Count == 0 && file.Status == CaseStatus.Open)
                file.Status = file.AnyTried ? CaseStatus.Tried : CaseStatus.Folded;
        }

        /// <summary>Takes a man off a case without judging what is left of it - the
        /// re-book's own move, where the case is being folded rather than emptied.</summary>
        static void Drop(CourtCase file, int characterId) =>
            file?.Defendants.Remove(characterId);

        static string Stamp(int day) => day > 0 ? "DAY " + day : "";
    }
}
