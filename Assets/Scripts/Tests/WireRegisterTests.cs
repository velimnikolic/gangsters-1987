using System.Collections.Generic;
using LivingCity.UI;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// THE WIRE's register, on the bench.
    ///
    /// <see cref="WireRegister"/> is the whole arithmetic of the tab - what is in scope,
    /// where every band and line stands in the one scroll, what a day comes to - and it
    /// holds no Unity object, so it can be judged without a page. What is proved here is
    /// what the reader sees: the order, the day's own figures, the run dividers, the
    /// register practice of printing an origin only when it changes, and every filter.
    /// </summary>
    public static class WireRegisterTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            NewestFirstWithBothRuns(failures);
            ADayAddsItselfUp(failures);
            OriginPrintsOnlyWhenItChanges(failures);
            EveryNarrowingHolds(failures);
            TheRailKeepsQuietDays(failures);
            WalkingTheDaysStops(failures);
            ArrivalsAboveTheReaderAreCounted(failures);
            ArrivalsOutOfScopeAreNotHeld(failures);

            return failures;
        }

        // The books, as WireBook dresses them: a man's incident carries no clock and a
        // door slip does, and nothing here composes a line - the bodies are given.
        static WireLine Men(int day, string tag, string body, Color ink,
            WireWeight weight = WireWeight.Routine, int heat = 0,
            string where = "DOWNTOWN") =>
            new WireLine("WIRE - " + where, "DAY " + day, body, tag,
                heat > 0 ? "+" + heat + " HEAT" : "", ink, day, default, default, -1,
                WireAction.Record, weight, false, heat);

        static WireLine Door(int day, string clock, string tag, string body, Color ink,
            int money = 0, bool moneyIn = false,
            WireWeight weight = WireWeight.Routine) =>
            new WireLine("WIRE - THE RACKET", "DAY " + day + " · " + clock, body, tag,
                money > 0 ? "$" + money : "", ink, day, default, default, -1,
                WireAction.Record, weight, true, 0, money, moneyIn);

        static WireRegister Staged()
        {
            var lines = new List<WireLine>
            {
                Men(3, "Gunfire", "Vito went to the ground and stayed there.",
                    LedgerStyle.RedPen, WireWeight.Severe, 3),
                Door(3, "16:57", "He pays", "The envelope was waiting.",
                    LedgerStyle.GreenOk, 450, true),
                Door(3, "11:20", "Short", "Half an envelope out of the laundry.",
                    LedgerStyle.PenAmber, 200),
                Men(2, "Watch him", "He has been drinking with men who are not ours.",
                    LedgerStyle.Ballpoint, WireWeight.Notable, 0, "LITTLE HAVANA"),
                Men(2, "Street", "Talk on the beach says they are short of men.",
                    LedgerStyle.TelexPlain, WireWeight.Routine, 0, "LITTLE HAVANA"),
                Door(1, "09:05", "Banked", "The round is in the safe.",
                    LedgerStyle.GreenOk, 1200, true, WireWeight.Notable),
            };
            var register = new WireRegister();
            register.Take(lines);
            register.Build(WireNarrow.Open);
            return register;
        }

        static void NewestFirstWithBothRuns(List<string> failures)
        {
            var register = Staged();
            var items = register.Items;
            if (items.Count == 0 || items[0].Kind != WireItemKind.Day || items[0].Day != 3)
                failures.Add("WIRE: the newest day does not open the register.");

            var runs = 0;
            var dayThreeRuns = 0;
            var lastDay = int.MaxValue;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind == WireItemKind.Day)
                {
                    if (item.Day >= lastDay)
                        failures.Add("WIRE: the day bands are not in newest-first order.");
                    lastDay = item.Day;
                }
                if (item.Kind != WireItemKind.Run)
                    continue;
                runs++;
                if (item.Day == 3)
                    dayThreeRuns++;
            }
            // Day 3 carries both books and is split; days 2 and 1 carry one book each
            // and print no divider at all.
            if (dayThreeRuns != 2 || runs != 2)
                failures.Add("WIRE: run dividers are not printed exactly where a day " +
                    "holds both books.");

            var expected = 3 * WireRegister.DayH + 2 * WireRegister.RunH +
                6 * WireRegister.LineH;
            if (!Mathf.Approximately(register.Height, expected))
                failures.Add("WIRE: the register's height is not its own layout (" +
                    register.Height + " against " + expected + ").");
            if (register.DayAt(0f) != 3 || register.DayAt(register.Height - 1f) != 1)
                failures.Add("WIRE: the reader's day does not follow the scroll.");
        }

        static void ADayAddsItselfUp(List<string> failures)
        {
            var register = Staged();
            WireDay three = null;
            foreach (var day in register.Days)
                if (day.Day == 3)
                    three = day;
            if (three == null)
            {
                failures.Add("WIRE: day three is not on the rail.");
                return;
            }
            if (three.Count != 3 || three.Men != 1 || three.Doors != 2)
                failures.Add("WIRE: a day's two books are miscounted.");
            if (three.Heat != 3)
                failures.Add("WIRE: the day's heat is not the sum of its lines.");
            // The short envelope carries a figure and was never taken: a day that
            // counted it would report money the outfit does not have.
            if (three.Taken != 450)
                failures.Add("WIRE: the day's takings counted money that never came in.");
            if (!three.Counts.Contains("3 ENTRIES") ||
                !three.Counts.Contains("$450 TAKEN AT THE DOORS"))
                failures.Add("WIRE: the day band does not say its own arithmetic.");
            if (three.Hard != 1)
                failures.Add("WIRE: the rail's red share is not the red and blue pens.");
        }

        static void OriginPrintsOnlyWhenItChanges(List<string> failures)
        {
            var register = Staged();
            var printed = new List<string>();
            foreach (var item in register.Items)
                if (item.Kind == WireItemKind.Line)
                    printed.Add(item.Label);
            // Day three's door run carries two slips out of THE RACKET and day two two
            // out of LITTLE HAVANA; in both pairs the second line leaves the column
            // blank. Every other line opens a run or changes the origin and prints it.
            var blanks = 0;
            foreach (var label in printed)
                if (label.Length == 0)
                    blanks++;
            if (blanks != 2)
                failures.Add("WIRE: the origin is repeated where it did not change.");
            if (printed[0] != "DOWNTOWN" || printed[1] != "THE RACKET")
                failures.Add("WIRE: a run divider does not start the origin over.");
            if (printed.Count != 6)
                failures.Add("WIRE: the register lost a line.");
        }

        static void EveryNarrowingHolds(List<string> failures)
        {
            var register = Staged();

            var narrow = WireNarrow.Open;
            narrow.Book = WireScope.OurDoors;
            register.Build(narrow);
            if (register.Count != 3 || register.Men != 0)
                failures.Add("WIRE: OUR DOORS kept a line off the other book.");

            narrow = WireNarrow.Open;
            narrow.Pens = 1 << (int)WirePen.Red;
            register.Build(narrow);
            if (register.Count != 1)
                failures.Add("WIRE: the pen filter does not hold.");

            narrow = WireNarrow.Open;
            narrow.Source = "THE RACKET";
            register.Build(narrow);
            if (register.Count != 3)
                failures.Add("WIRE: the source column does not narrow the archive.");

            narrow = WireNarrow.Open;
            narrow.Query = "envelope";
            register.Build(narrow);
            if (register.Count != 2)
                failures.Add("WIRE: FIND does not read the body of a slip.");

            narrow = WireNarrow.Open;
            narrow.Query = "watch him";
            register.Build(narrow);
            if (register.Count != 1)
                failures.Add("WIRE: FIND does not read a tag.");

            narrow = WireNarrow.Open;
            narrow.DayOnly = 2;
            register.Build(narrow);
            if (register.Count != 2 || register.DaysOnFile != 1)
                failures.Add("WIRE: isolating a day does not isolate it.");
            if (!narrow.Narrowed || WireNarrow.Open.Narrowed)
                failures.Add("WIRE: the sheet cannot tell a narrowed archive from a " +
                    "whole one.");

            register.Build(WireNarrow.Open);
            if (register.Count != 6 || register.Total != 6)
                failures.Add("WIRE: clearing the scope did not give the archive back.");
        }

        static void TheRailKeepsQuietDays(List<string> failures)
        {
            var register = Staged();
            var narrow = WireNarrow.Open;
            narrow.Pens = 1 << (int)WirePen.Red;
            register.Build(narrow);
            if (register.Days.Count != 3)
                failures.Add("WIRE: the rail dropped a day instead of drawing it quiet.");
            var quiet = 0;
            foreach (var day in register.Days)
                if (day.Count == 0)
                    quiet++;
            if (quiet != 2)
                failures.Add("WIRE: a day with nothing in scope is not counted quiet.");
            if (register.TopOf(2) >= 0f)
                failures.Add("WIRE: a day out of scope still claims a place in the scroll.");
        }

        /// <summary>What the held-entries notice counts. A door slip filed under a day
        /// an incident already leads lands BELOW that incident - the books tie on the
        /// day and the tie keeps the incident first - so counting the run in front of
        /// the old head would report nothing while the reader's line moved down.</summary>
        static void ArrivalsAboveTheReaderAreCounted(List<string> failures)
        {
            var before = new List<WireLine>
            {
                Men(3, "Gunfire", "Vito went to the ground.", LedgerStyle.RedPen),
                Door(1, "09:05", "Banked", "The round is in the safe.",
                    LedgerStyle.GreenOk, 1200, true),
            };
            var after = new List<WireLine>
            {
                before[0],
                Door(3, "17:40", "He pays", "The envelope was waiting.",
                    LedgerStyle.GreenOk, 450, true),
                before[1],
            };
            var reader = before[1];
            var moved = WireRegister.FiledAt(after, reader) -
                WireRegister.FiledAt(before, reader);
            if (moved != 1)
                failures.Add("WIRE: a slip filed under a day the other book leads is " +
                    "not counted as arriving above the reader.");
            if (WireRegister.FiledAt(after, Men(9, "Street", "Never filed.",
                    LedgerStyle.TelexPlain)) >= 0)
                failures.Add("WIRE: a slip that is not in the run was found in it.");
        }

        /// <summary>The held notice counts what arrived in the run the reader is
        /// READING. A door slip landing while the register is narrowed to OUR MEN moves
        /// nothing he can see, and a notice offering it would point at a line his own
        /// scope will not print.</summary>
        static void ArrivalsOutOfScopeAreNotHeld(List<string> failures)
        {
            var ours = Men(2, "Watch him", "He drinks with men who are not ours.",
                LedgerStyle.Ballpoint);
            var older = Men(1, "Street", "Talk on the beach.", LedgerStyle.TelexPlain);
            var narrow = WireNarrow.Open;
            narrow.Book = WireScope.OurMen;

            var register = new WireRegister();
            register.Take(new List<WireLine> { ours, older });
            register.Build(narrow);
            var before = register.IndexOf(older);

            register.Take(new List<WireLine>
            {
                ours,
                Door(2, "17:40", "He pays", "The envelope was waiting.",
                    LedgerStyle.GreenOk, 450, true),
                older,
            });
            register.Build(narrow);
            if (register.IndexOf(older) != before)
                failures.Add("WIRE: a slip the scope does not print moved the reader's " +
                    "line, so the held notice would offer him an invisible entry.");

            register.Build(WireNarrow.Open);
            if (register.IndexOf(older) != before + 1)
                failures.Add("WIRE: the same slip is not counted when the scope does " +
                    "print it.");
        }

        static void WalkingTheDaysStops(List<string> failures)
        {
            var register = Staged();
            if (register.Step(3, -1) != 3)
                failures.Add("WIRE: NEWER DAY walked off the head of the archive.");
            if (register.Step(1, 1) != 1)
                failures.Add("WIRE: OLDER DAY walked off the foot of the archive.");
            if (register.Step(3, 1) != 2 || register.Step(2, -1) != 3)
                failures.Add("WIRE: the day keys do not step one day.");

            var index = register.IndexOf(register.Kept[4]);
            if (index != 4 || register.ItemOfLine(4) < 0)
                failures.Add("WIRE: a line cannot be found again by what it says.");
        }
    }
}
