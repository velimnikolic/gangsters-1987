using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.UI;

namespace LivingCity.Police
{
    /// <summary>One name on a docket card and what can be done about him.</summary>
    public sealed class DefendantRow
    {
        public int CharacterId;
        public string Name = "";

        /// <summary>Where in the pipe he stands, in the book's one vocabulary
        /// (<see cref="LedgerText.StageLabel"/>), or "hiding" for a man the city is
        /// looking for.</summary>
        public string Stage = "";

        /// <summary>What it costs to get him out; 0 when there is no bail on the
        /// charge.</summary>
        public int Bail;

        /// <summary>Why bail cannot be asked for, or null.</summary>
        public string BailRefusal;

        public bool CanPostBail;
        public bool CanSkipBail;
        public bool CanCutLoose;
    }

    /// <summary>One name on the prosecution's list, as the sheet reads it.</summary>
    public sealed class WitnessRow
    {
        public Witness Witness;
        public string Kind = "";
        public string Standing = "";

        /// <summary>Whether the crew has anything to say to him. A policeman is not
        /// leaned on, and neither is a man who has already withdrawn or died.</summary>
        public bool CanLeanOn;

        /// <summary>Whether the court will actually hear him on the morning - the
        /// complainant's nerve is asked by the pipeline's own gate, not by a number this
        /// layer guesses at.</summary>
        public bool WillBeHeard;
    }

    /// <summary>One case still being answered.</summary>
    public sealed class DocketRow
    {
        public CourtCase File;
        public string Charge = "";
        public string Where = "";
        public int OpenedDay;
        public int CourtDay;

        /// <summary>Days until it is heard; 0 or less when the day has come or there is
        /// no day yet.</summary>
        public int DaysToCourt;

        public int Counts;

        /// <summary>A complaint the crew walked away from: on the docket, nobody taken,
        /// and worth an extra count the next time these men are.</summary>
        public bool NobodyTaken;

        public readonly List<DefendantRow> Defendants = new List<DefendantRow>();
        public readonly List<WitnessRow> Witnesses = new List<WitnessRow>();

        /// <summary>Counsel's read of what the state has, in words and never a
        /// number.</summary>
        public string Read = "";

        /// <summary>The lawyer of record, or empty.</summary>
        public string Counsel = "";
    }

    /// <summary>One man the city is holding.</summary>
    public sealed class InsideRow
    {
        public int CharacterId;
        public string Name = "";
        public string Charge = "";
        public string Stage = "";
        public int CourtDay;
        public int OutOnDay;
        public bool Life;
    }

    /// <summary>One man the city is looking for.</summary>
    public sealed class WantedRow
    {
        public int CharacterId;
        public string Name = "";
        public string Word = "";

        /// <summary>How long he has been out of sight, or when he is back in town.</summary>
        public string When = "";
    }

    /// <summary>The lawyer on the books, or the want of one.</summary>
    public sealed class CounselRow
    {
        public bool Has;
        public int CharacterId = -1;
        public string Name = "";
        public int Skill;
        public int Won;
        public int Lost;
        public int Wage;
        public bool CanAskBail;
    }

    /// <summary>One case that is over.</summary>
    public sealed class ArchiveRow
    {
        public CourtCase File;
        public string Charge = "";
        public string Where = "";

        /// <summary>The day it closed - the last verdict on it, or the day it was
        /// listed for.</summary>
        public int Day;

        /// <summary>One line per man: his name and what the court did to him.</summary>
        public readonly List<string> Lines = new List<string>();

        /// <summary>What a case with no verdicts at all was: folded into a later one, or
        /// lapsed with nobody left to try.</summary>
        public string Note = "";
    }

    /// <summary>Everything the law sheet paints, in the order it paints it.</summary>
    public sealed class LawSheetRows
    {
        public readonly List<DocketRow> Docket = new List<DocketRow>();
        public readonly List<InsideRow> Inside = new List<InsideRow>();
        public readonly List<WantedRow> Wanted = new List<WantedRow>();
        public readonly List<ArchiveRow> Archive = new List<ArchiveRow>();
        public CounselRow Counsel = new CounselRow();

        public void Clear()
        {
            Docket.Clear();
            Inside.Clear();
            Wanted.Clear();
            Archive.Clear();
            Counsel = new CounselRow();
        }
    }

    /// <summary>
    /// WHAT THE LAW HAS AGAINST US, GATHERED (GAN-302).
    ///
    /// The ledger's law sheet scatters over four regions what the model already holds in
    /// two lists, and this is the one reader between them: pure, UnityEngine-free, and
    /// contracted headlessly. The page paints these rows and the bench prints them, so
    /// what a contract proves is what the player is shown - the WireBook.Collect shape.
    ///
    /// TWO RULES WORTH THE COMMENT:
    ///
    /// The complainant's nerve is asked through the PIPELINE'S OWN GATE
    /// (<see cref="PrisonPipeline.ComplainantStillTalks"/>, and behind it
    /// PoliceForce.StillTalks), never a fear number this layer compares for itself. A
    /// Connected owner testifies whatever the street has done to him, and a sheet that
    /// read fear alone would tell the player his shopkeeper was frightened off while the
    /// man turned up and put his crew away.
    ///
    /// And the counsel's read is taken on the witnesses THE COURT WILL HEAR - the
    /// silenced complainant already removed - because a read taken on the raw list is a
    /// read of a trial that is not going to happen.
    /// </summary>
    public static class LawSheet
    {
        /// <summary>What a defendant who is not in the pipe at all reads as: the city is
        /// looking for him.</summary>
        public const string Hiding = "hiding";

        public static void Collect(
            PrisonPipeline pipeline, Roster roster, int gangId, int today,
            int lawyerSkill, Func<CourtCase, bool> complainantStillTalks,
            LawSheetRows into)
        {
            if (into == null)
                return;
            into.Clear();
            if (pipeline == null || roster == null)
                return;

            into.Counsel = Counsel(roster);
            Docket(pipeline, roster, gangId, today, lawyerSkill, complainantStillTalks, into);
            Inside(pipeline, roster, into);
            Wanted(roster, today, into);
            Archive(pipeline, roster, gangId, into);
        }

        // ------------------------------------------------------------------ the docket

        static void Docket(
            PrisonPipeline pipeline, Roster roster, int gangId, int today,
            int lawyerSkill, Func<CourtCase, bool> complainantStillTalks,
            LawSheetRows into)
        {
            for (var i = 0; i < pipeline.Cases.Count; i++)
            {
                var file = pipeline.Cases[i];
                if (file == null || file.Status != CaseStatus.Open) continue;
                if (file.GangId != gangId) continue;

                var talks = complainantStillTalks == null || complainantStillTalks(file);
                var row = new DocketRow
                {
                    File = file,
                    Charge = Sentencing.ChargeFor(file.Deed),
                    Where = file.Where,
                    OpenedDay = file.OpenedDay,
                    CourtDay = file.CourtDay,
                    DaysToCourt = file.CourtDay > 0 && today > 0 ? file.CourtDay - today : 0,
                    Counts = file.Counts.Count,
                    NobodyTaken = file.Defendants.Count == 0,
                    Counsel = into.Counsel.Has
                        ? into.Counsel.Name + "  ·  " + into.Counsel.Skill + " of " +
                          Lawyer.MaxSkill
                        : "",
                };

                for (var d = 0; d < file.Defendants.Count; d++)
                    row.Defendants.Add(Defendant(
                        pipeline, roster, file.Defendants[d], lawyerSkill));

                for (var w = 0; w < file.Witnesses.Count; w++)
                    row.Witnesses.Add(Witness(file.Witnesses[w], talks));

                row.Read = Read(file, roster, lawyerSkill, talks, into.Counsel.Has);
                into.Docket.Add(row);
            }

            // SOONEST FIRST, AND THE UNANSWERED COMPLAINTS LAST. A card with a day on it
            // is a thing the player has until; a complaint nobody was taken for has no
            // day at all and is only a count waiting to happen.
            into.Docket.Sort(ByCourtDay);
        }

        static int ByCourtDay(DocketRow a, DocketRow b)
        {
            if (a.NobodyTaken != b.NobodyTaken)
                return a.NobodyTaken ? 1 : -1;
            var mine = a.CourtDay > 0 ? a.CourtDay : int.MaxValue;
            var theirs = b.CourtDay > 0 ? b.CourtDay : int.MaxValue;
            if (mine != theirs)
                return mine.CompareTo(theirs);
            return a.File.CaseId.CompareTo(b.File.CaseId);
        }

        static DefendantRow Defendant(
            PrisonPipeline pipeline, Roster roster, int characterId, int lawyerSkill)
        {
            var member = roster.Find(characterId);
            var prisoner = pipeline.Find(characterId);
            var row = new DefendantRow
            {
                CharacterId = characterId,
                Name = member != null ? member.FullName : "",
                Stage = prisoner != null
                    ? LedgerText.StageLabel(prisoner.Stage)
                    : Hiding,
                Bail = PrisonPipeline.BailPrice(prisoner),
                BailRefusal = pipeline.BailRefusal(prisoner, lawyerSkill),
                CanSkipBail = prisoner != null &&
                              prisoner.Stage == PrisonStage.Bailed && !prisoner.SkipOrdered,
                CanCutLoose = RosterOps.CanCutLoose(member),
            };
            row.CanPostBail = row.BailRefusal == null;
            return row;
        }

        static WitnessRow Witness(Witness witness, bool complainantTalks)
        {
            var heard = witness.Willing &&
                        (witness.Kind != WitnessKind.Complainant || complainantTalks);
            return new WitnessRow
            {
                Witness = witness,
                Kind = KindOf(witness),
                Standing = StandingOf(witness, complainantTalks),
                // A POLICEMAN IS NOT LEANED ON. The case knows which of its names the
                // crew can reach (Witness.CanBePressured); a man who has already
                // withdrawn or died is nothing to reach for either.
                CanLeanOn = witness.CanBePressured && witness.Willing,
                WillBeHeard = heard,
            };
        }

        static string KindOf(Witness witness) => witness.Kind switch
        {
            WitnessKind.Complainant => string.IsNullOrEmpty(witness.Name)
                ? "the shopkeeper"
                : "the shopkeeper, " + witness.Name,
            WitnessKind.Eyewitness => string.IsNullOrEmpty(witness.Name)
                ? "a man on the pavement"
                : "a man on the pavement, " + witness.Name,
            WitnessKind.PoliceSawIt => "the officer who saw it",
            _ => "the officer who found them",
        };

        static string StandingOf(Witness witness, bool complainantTalks)
        {
            if (witness.Standing == WitnessStanding.Dead) return "dead";
            if (witness.Standing == WitnessStanding.Withdrawn) return "withdrawn";
            // FRIGHTENED, and only on the gate's own word. A Connected owner has a
            // cousin at the precinct and turns up whatever the street has done to him;
            // reading fear alone here would print a lie on the one line the player
            // decides his week by.
            if (witness.Kind == WitnessKind.Complainant && !complainantTalks)
                return "may not testify — frightened";
            return "will testify";
        }

        /// <summary>
        /// COUNSEL'S READ, on the witnesses the court will actually hear. The silenced
        /// complainant is taken out before the chance is worked out, which is the whole
        /// difference between the sheet agreeing with the courtroom and second-guessing
        /// it.
        /// </summary>
        static string Read(
            CourtCase file, Roster roster, int lawyerSkill, bool complainantTalks,
            bool hasCounsel)
        {
            var eyewitnesses = file.WillingEyewitnesses();
            var sawIt = file.Has(WitnessKind.PoliceSawIt);
            var foundThem = file.Has(WitnessKind.PoliceFoundThem);
            var complainant = file.Has(WitnessKind.Complainant) && complainantTalks;

            // AN EMPTY LIST NEEDS NO LAWYER TO READ. A case with nobody willing to give
            // evidence is thrown out before any roll (PrisonPipeline.Tried), so this one
            // is a fact rather than an opinion and stands whether or not the outfit has
            // counsel on the books.
            if (eyewitnesses == 0 && !sawIt && !foundThem && !complainant)
                return Verdict.NoWitnessesLeft;

            if (!hasCounsel)
                return Verdict.NoCounselToAsk;

            // The judge reads the worst sheet in the dock: a case is as bad as it is for
            // the man the court has the most on.
            var priors = 0;
            for (var i = 0; i < file.Defendants.Count; i++)
            {
                var record = PrisonPipeline.Priors(roster.Find(file.Defendants[i]));
                if (record > priors) priors = record;
            }

            return Verdict.Leaning(Verdict.ConvictionChance(
                file.Deed, eyewitnesses, sawIt, foundThem, complainant, priors,
                lawyerSkill));
        }

        // ------------------------------------------------------------------ the cells

        static void Inside(PrisonPipeline pipeline, Roster roster, LawSheetRows into)
        {
            for (var i = 0; i < pipeline.Inside.Count; i++)
            {
                var prisoner = pipeline.Inside[i];
                var member = roster.Find(prisoner.CharacterId);
                if (member == null) continue;
                into.Inside.Add(new InsideRow
                {
                    CharacterId = prisoner.CharacterId,
                    Name = member.FullName,
                    Charge = Sentencing.ChargeFor(prisoner.Deed),
                    Stage = LedgerText.StageLabel(prisoner.Stage),
                    CourtDay = prisoner.CourtDay,
                    OutOnDay = prisoner.OutOnDay,
                    Life = Sentencing.IsLife(prisoner.SentenceDays),
                });
            }
        }

        // ----------------------------------------------------------------- the wanted

        static void Wanted(Roster roster, int today, LawSheetRows into)
        {
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member == null || member.WantedLevel <= 0) continue;

                var needed = WantedLevels.DaysToCool(member.WantedLevel);
                var hidden = WantedLevels.HiddenDays(member, today);
                var when = member.OutOfTown
                    ? "out of town until day " + member.BackOnDay
                    : needed == WantedLevels.Never
                        ? "nothing cools this one"
                        : hidden <= 0
                            ? "seen on the street — nothing is cooling"
                            : "hidden " + hidden + " of " + needed +
                              (needed == 1 ? " day" : " days");

                into.Wanted.Add(new WantedRow
                {
                    CharacterId = member.Id,
                    Name = member.FullName,
                    Word = WantedLevels.Word(member.WantedLevel),
                    When = when,
                });
            }
        }

        // ---------------------------------------------------------------- the counsel

        static CounselRow Counsel(Roster roster)
        {
            var lawyer = Lawyer.Counsel(roster);
            if (lawyer == null)
                return new CounselRow();
            return new CounselRow
            {
                Has = true,
                CharacterId = lawyer.Id,
                Name = lawyer.FullName,
                Skill = Lawyer.Skill(lawyer),
                Won = lawyer.CasesWon,
                Lost = lawyer.CasesLost,
                Wage = Outfit.Wages.WageFor(lawyer),
                CanAskBail = Lawyer.Skill(lawyer) >= Lawyer.BailSkill,
            };
        }

        // ---------------------------------------------------------------- the archive

        static void Archive(
            PrisonPipeline pipeline, Roster roster, int gangId, LawSheetRows into)
        {
            for (var i = 0; i < pipeline.Cases.Count; i++)
            {
                var file = pipeline.Cases[i];
                if (file == null || file.Status == CaseStatus.Open) continue;
                if (file.GangId != gangId) continue;

                var row = new ArchiveRow
                {
                    File = file,
                    Charge = Sentencing.ChargeFor(file.Deed),
                    Where = file.Where,
                    Day = file.CourtDay > 0 ? file.CourtDay : file.OpenedDay,
                };

                for (var v = 0; v < file.Verdicts.Count; v++)
                {
                    var verdict = file.Verdicts[v];
                    var member = roster.Find(verdict.CharacterId);
                    row.Lines.Add((member != null ? member.FullName : "A man of ours") +
                                  " — " + LedgerText.CaseOutcomeLine(verdict));
                    if (verdict.Day > row.Day) row.Day = verdict.Day;
                }

                if (row.Lines.Count == 0)
                    row.Note = file.Status == CaseStatus.Folded
                        ? "folded into a later case"
                        : "case dismissed — no witnesses";
                else if (file.Status == CaseStatus.Folded && file.Defendants.Count > 0)
                    row.Note = "lapsed — nobody left to try";

                into.Archive.Add(row);
            }

            // NEWEST FIRST: the last thing the court did is the thing the boss is
            // reading the sheet about.
            into.Archive.Sort((a, b) =>
                a.Day != b.Day
                    ? b.Day.CompareTo(a.Day)
                    : b.File.CaseId.CompareTo(a.File.CaseId));
        }
    }
}
