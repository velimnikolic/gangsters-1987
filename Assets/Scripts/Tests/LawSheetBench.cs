using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Save;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// THE LAW SHEET'S OWN BENCH (GAN-302).
    ///
    /// A staged docket run through <see cref="LawSheet.Collect"/> - the same reader the
    /// ledger's page paints from - printed line by line and judged. What this prints is
    /// what a player sees, which is the whole point of the collector living apart from
    /// the page: a contract here is a contract about the sheet.
    ///
    /// The run also saves and loads between the two readings, because the docket used
    /// not to survive a file at all: a case that came back empty would print a perfect
    /// sheet about nothing.
    ///
    /// Its own oracle. NIGHT-009 is a street trace and this is not coupled to it.
    /// </summary>
    public static class LawSheetBench
    {
        public sealed class Report
        {
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Lines = new List<string>();
        }

        const int Seed = 1987;
        const int Today = 10;

        public static Report Run()
        {
            var report = new Report();

            var roster = new Roster { Seed = Seed };
            var held = Man(roster, "Paulie", "Costa");
            var mate = Man(roster, "Artie", "Levine");
            var bailed = Man(roster, "Wade", "Kelly");
            var counsel = Man(roster, "Ziggy", "Quinn");
            counsel.Specialty = Specialty.Lawyer;

            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };

            // A case being answered: two men in the cells, a shopkeeper, a man on the
            // pavement, and one witness the crew has already got to.
            var file = pipe.OpenCase(Deed.Extortion, 0, Today, Today + 4,
                "shop-4", "THE DELICATESSEN");
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant, Name = "Aldo Bruni", Seed = 7,
                X = 40f, Z = -12f, BusinessId = "shop-4",
            });
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Eyewitness, Name = "Rosa Conti", Seed = 9, X = 12f, Z = 8f,
            });
            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Eyewitness, Name = "Marco Sarto", Seed = 11,
                Standing = WitnessStanding.Withdrawn,
            });
            pipe.Book(roster, held.Id, Deed.Extortion, Today, file);
            pipe.Book(roster, mate.Id, Deed.Extortion, Today, file);

            // A murder heard sooner, with a policeman who saw it.
            var heavy = pipe.OpenCase(Deed.Murder, 0, Today - 1, Today + 2,
                "shop-9", "THE YARD ON DEXTER");
            heavy.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.PoliceSawIt, Name = "Officer Reyes", Seed = 3,
            });
            var out_ = pipe.Book(roster, bailed.Id, Deed.Murder, Today - 1, heavy);

            // And a complaint the crew walked away from.
            pipe.OpenCase(Deed.Extortion, 0, Today, 0, "shop-12", "THE BARBER");

            var skill = Lawyer.Skill(counsel);
            var rows = Read(pipe, roster, Today, skill, report, "BEFORE THE COURT");

            // ---- the docket ----------------------------------------------------
            Want(report, rows.Docket.Count == 3,
                "LAW: three cases of ours are open and " + rows.Docket.Count + " listed.");
            Want(report, rows.Docket.Count == 3 && rows.Docket[0].File == heavy,
                "LAW: the soonest court day is at the top.");
            Want(report, rows.Docket.Count == 3 && rows.Docket[2].NobodyTaken,
                "LAW: the complaint nobody was taken for is last.");

            var docket = rows.Docket.Count > 1 ? rows.Docket[1] : null;
            Want(report, docket != null && docket.Defendants.Count == 2,
                "LAW: both men who answer for the delicatessen are on its card.");
            Want(report, docket != null && docket.Witnesses.Count == 3,
                "LAW: with all three names the prosecution has.");
            var withdrawn = 0;
            for (var i = 0; docket != null && i < docket.Witnesses.Count; i++)
                if (docket.Witnesses[i].Standing == "withdrawn") withdrawn++;
            Want(report, withdrawn == 1,
                "LAW: the one the crew got to reads as withdrawn.");
            Want(report, docket != null && docket.Read != Verdict.NoCounselToAsk,
                "LAW: a lawyer on the books reads the case.");
            Want(report, rows.Counsel.Has && rows.Counsel.Name == counsel.FullName,
                "LAW: and the counsel box names him.");

            // ---- the cells ------------------------------------------------------
            Want(report, rows.Inside.Count == 3,
                "LAW: three men are in the city's hands and " + rows.Inside.Count +
                " are in the cells column.");
            Want(report, rows.Wanted.Count == 0,
                "LAW: nobody is on the run yet.");
            Want(report, rows.Archive.Count == 0,
                "LAW: and nothing has come to court.");

            PrintCarriageStages(report);

            // ---- bail, and the day he does not turn up --------------------------
            var price = PrisonPipeline.BailPrice(out_);
            Want(report, pipe.PostBail(roster, out_, price, Today),
                "LAW: the murder charge is bailable with counsel on the books.");
            pipe.SkipBail(out_);
            pipe.TryOnPaper(roster, heavy.CourtDay);

            // ---- through a file, because the docket used not to survive one ------
            var escapes = new List<int>();
            pipe.CollectEscapes(escapes);
            var written = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(
                new CampaignFile
                {
                    prisoners = PrisonSnapshot.Prisoners(pipe),
                    cases = PrisonSnapshot.Cases(pipe),
                    nextCaseId = pipe.NextCaseId,
                    escaped = escapes.ToArray(),
                    prisonRosterSeed = pipe.RosterSeed,
                }));
            var loaded = new PrisonPipeline();
            PrisonSnapshot.Restore(loaded, written);

            Want(report, loaded.Cases.Count == pipe.Cases.Count,
                "LAW: " + pipe.Cases.Count + " cases were on the docket and " +
                loaded.Cases.Count + " came back off the file.");
            var reopened = loaded.FindCase(file.CaseId);
            Want(report, reopened != null && reopened.Witnesses.Count == 3,
                "LAW: with the witness list the player had already worked on.");

            // ---- the trial itself, on the loaded pipe ---------------------------
            var inCells = loaded.Find(held.Id);
            if (inCells != null)
            {
                loaded.Away(inCells);
                loaded.Tried(roster, inCells, file.CourtDay);
            }
            var second = loaded.Find(mate.Id);
            if (second != null)
            {
                loaded.Away(second);
                // Every witness silenced first: this one is thrown out, not lost.
                for (var i = 0; reopened != null && i < reopened.Witnesses.Count; i++)
                    reopened.Witnesses[i].Standing = WitnessStanding.Withdrawn;
                loaded.Tried(roster, second, file.CourtDay);
            }

            var after = Read(loaded, roster, file.CourtDay, skill, report,
                "AFTER THE COURT");

            Want(report, after.Archive.Count >= 1,
                "LAW: the case that was heard is in the archive.");
            var closed = after.Archive.Count > 0 ? after.Archive[0] : null;
            Want(report, closed != null && closed.Lines.Count >= 1,
                "LAW: with a line per man saying what the court did.");
            Want(report, after.Wanted.Count == 1 &&
                         after.Wanted[0].Name == bailed.FullName,
                "LAW: the man who skipped his bail is on the wanted column.");

            var forfeit = heavy.VerdictFor(bailed.Id);
            Want(report, forfeit != null && forfeit.Outcome == CaseOutcome.BailForfeit,
                "LAW: and his forfeit is on his case's own record.");

            return report;
        }

        /// <summary>Prints every live carriage word through LawSheet.Collect, rather
        /// than contracting the label helper in isolation. This makes the CLI bench
        /// prove the same INSIDE rows the ledger paints for EPIC 35.</summary>
        static void PrintCarriageStages(Report report)
        {
            var roster = new Roster { Seed = Seed + 35 };
            var pipe = new PrisonPipeline { RosterSeed = roster.Seed };
            var stages = new[]
            {
                CarriageStage.Calling,
                CarriageStage.WalkingOut,
                CarriageStage.Boarding,
                CarriageStage.Riding,
                CarriageStage.Halted,
                CarriageStage.WalkingIn,
                CarriageStage.Delivered,
                CarriageStage.Riding,
            };
            var legs = new[]
            {
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Court,
                PrisonLeg.Prison,
            };
            var expected = new[]
            {
                "the car is coming for him",
                "walking to the car",
                "boarding the police car",
                "in the car to the court",
                "the transfer is halted",
                "at the courthouse door",
                "at the courthouse door",
                "in the van out of town",
            };

            for (var i = 0; i < stages.Length; i++)
            {
                var member = Man(roster, "Carriage", (i + 1).ToString());
                var prisoner = pipe.Book(roster, member.Id, Deed.Affray, Today);
                prisoner.Carriage = stages[i];
                prisoner.Leg = legs[i];
            }

            var rows = Read(pipe, roster, Today, 0, report, "CARRIAGE STAGES");
            Want(report, rows.Inside.Count == expected.Length,
                "ROAD-005/LAW: every carriage fixture reaches the INSIDE column.");
            for (var i = 0; i < expected.Length && i < rows.Inside.Count; i++)
                Want(report, rows.Inside[i].Stage == expected[i],
                    "ROAD-005/LAW: " + stages[i] + " reads '" + expected[i] +
                    "', not '" + rows.Inside[i].Stage + "'.");
        }

        static Character Man(Roster roster, string first, string last)
        {
            var man = new Character
            {
                Id = roster.NextCharacterId(), FirstName = first, Surname = last,
            };
            roster.Members.Add(man);
            return man;
        }

        /// <summary>Reads the sheet and PRINTS it - the bench's whole purpose is that a
        /// reader can see the rows the page would paint.</summary>
        static LawSheetRows Read(PrisonPipeline pipe, Roster roster, int today,
            int skill, Report report, string heading)
        {
            var rows = new LawSheetRows();
            LawSheet.Collect(pipe, roster, 0, today, skill, null, rows);

            report.Lines.Add("== " + heading + "  (day " + today + ") ==");
            report.Lines.Add("-- THE DOCKET");
            for (var i = 0; i < rows.Docket.Count; i++)
            {
                var row = rows.Docket[i];
                report.Lines.Add("  " + row.Charge.ToUpperInvariant() +
                    (string.IsNullOrEmpty(row.Where) ? "" : " · " + row.Where) +
                    " · opened day " + row.OpenedDay +
                    (row.NobodyTaken
                        ? " · ON THE DOCKET, nobody taken"
                        : " · court day " + row.CourtDay + " (" + row.DaysToCourt + "d)") +
                    (row.Counts > 0 ? " · +" + row.Counts + " counts" : ""));
                for (var d = 0; d < row.Defendants.Count; d++)
                {
                    var man = row.Defendants[d];
                    report.Lines.Add("      " + man.Name + " · " + man.Stage +
                        (man.Bail > 0 ? " · bail " + man.Bail : " · no bail") +
                        " · keys" +
                        (man.CanPostBail ? " POST" : "") +
                        (man.CanSkipBail ? " SKIP" : "") +
                        (man.CanCutLoose ? " CUT" : ""));
                }
                for (var w = 0; w < row.Witnesses.Count; w++)
                    report.Lines.Add("      " + row.Witnesses[w].Kind + " — " +
                        row.Witnesses[w].Standing +
                        (row.Witnesses[w].CanLeanOn ? "  [LEAN ON HIM]" : ""));
                report.Lines.Add("      counsel says: " + row.Read +
                    " · counsel: " + (row.Counsel.Length > 0 ? row.Counsel : "NONE"));
            }
            if (rows.Docket.Count == 0) report.Lines.Add("  NO CASE AGAINST US");

            report.Lines.Add("-- INSIDE");
            for (var i = 0; i < rows.Inside.Count; i++)
                report.Lines.Add("  " + rows.Inside[i].Name + " · " +
                    rows.Inside[i].Charge + " · " + rows.Inside[i].Stage +
                    (rows.Inside[i].OutOnDay > 0
                        ? " · out day " + rows.Inside[i].OutOnDay
                        : " · court day " + rows.Inside[i].CourtDay));
            if (rows.Inside.Count == 0) report.Lines.Add("  NOBODY INSIDE");

            report.Lines.Add("-- WANTED");
            for (var i = 0; i < rows.Wanted.Count; i++)
                report.Lines.Add("  " + rows.Wanted[i].Name + " · " +
                    rows.Wanted[i].Word + " · " + rows.Wanted[i].When);
            if (rows.Wanted.Count == 0) report.Lines.Add("  NOBODY WANTED");

            report.Lines.Add("-- COUNSEL");
            report.Lines.Add(rows.Counsel.Has
                ? "  " + rows.Counsel.Name + " · " + rows.Counsel.Skill + " of " +
                  Lawyer.MaxSkill + " · " + rows.Counsel.Won + " kept out, " +
                  rows.Counsel.Lost + " went down"
                : "  NO COUNSEL ON THE BOOKS");

            report.Lines.Add("-- VERDICTS");
            for (var i = 0; i < rows.Archive.Count; i++)
            {
                var row = rows.Archive[i];
                report.Lines.Add("  DAY " + row.Day + " · " + row.Charge +
                    (string.IsNullOrEmpty(row.Where) ? "" : " · " + row.Where) +
                    (string.IsNullOrEmpty(row.Note) ? "" : " · " + row.Note));
                for (var v = 0; v < row.Lines.Count; v++)
                    report.Lines.Add("      " + row.Lines[v]);
            }
            if (rows.Archive.Count == 0)
                report.Lines.Add("  NOTHING HAS COME TO COURT");

            return rows;
        }

        static void Want(Report report, bool held, string complaint)
        {
            if (!held) report.Failures.Add(complaint);
        }
    }
}
