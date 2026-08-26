using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>One line of a man's police record: when, what for, and how it ended.
    /// Plain strings on purpose - a rap sheet is a TYPED DOCUMENT, and the outcomes a
    /// city writes on one ("dismissed", "18 months, served 11", "no bill") are not a
    /// set an enum can close.</summary>
    public sealed class RapEntry
    {
        /// <summary>As it reads on the sheet - "14 MAR 1984". Priors carry a date from
        /// before the campaign; anything the outfit earns is stamped with the day it
        /// happened, struck through NewsDate so the whole book dates the same way.</summary>
        public string Date = "";

        public string Charge = "";

        /// <summary>How it ended. Printed in red on the file - it is the half of the
        /// line that says whether the man is a liability.</summary>
        public string Outcome = "";
    }

    /// <summary>
    /// A man's record with the city, and where it comes from.
    ///
    /// Every man arrives with a PAST - nobody is on this payroll because his life went
    /// well - so priors are dealt with him from the same rng stream that dealt his name
    /// and his stats, which keeps one seed answering for the whole man. Everything after
    /// that is written as it happens: taken and held, or flagged and hunted.
    ///
    /// Pure and free of UnityEngine like the rest of Personnel, so the headless suite
    /// can deal a sheet and read it without an editor.
    /// </summary>
    public static class RapSheet
    {
        /// <summary>The most priors a man can walk in with. Four is a career; more than
        /// that and he would have been inside for the whole of the eighties.</summary>
        public const int MaxPriors = 4;

        /// <summary>How far back the priors reach. The campaign opens in 1987 and a
        /// prior from before this is ancient history nobody would still type out.</summary>
        public const int EarliestPriorYear = 1979;

        static readonly string[] Charges =
        {
            "Receiving stolen goods", "Assault", "Aggravated assault",
            "Burglary, second degree", "Grand larceny (auto)", "Illegal gaming",
            "Possession of an unlicensed firearm", "Extortion", "Loan sharking",
            "Criminal mischief", "Arson, third degree", "Bookmaking",
            "Hijacking", "Bribery of a public servant", "Racketeering",
        };

        static readonly string[] Outcomes =
        {
            "Dismissed — no witness", "Charges dropped", "Acquitted",
            "No bill", "Pleaded out — probation", "6 months, served 4",
            "18 months, served 11", "2 years, paroled", "Fined $400",
            "Case adjourned in contemplation of dismissal", "Held, released",
        };

        static readonly string[] Months =
        {
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
            "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
        };

        /// <summary>
        /// Deals a man the record he arrives with. Drawn off the SAME stream as the rest
        /// of him - call it once, in order, beside DrawName - so a seed answers for the
        /// whole roster and re-reading the file never re-rolls a man's convictions.
        ///
        /// A third of men come in clean. That is the point of the section: an empty
        /// rap sheet is information, and a page where everybody has four priors tells
        /// the player nothing.
        /// </summary>
        public static void Deal(System.Random rng, Character member)
        {
            member.RapSheet.Clear();

            var count = rng.Next(0, MaxPriors + 2) - 1;
            if (count <= 0)
                return;

            // Oldest first, walking forward through the years, so the sheet reads down
            // the page the way a real one does.
            var year = rng.Next(EarliestPriorYear, 1984);
            for (var i = 0; i < count; i++)
            {
                member.RapSheet.Add(new RapEntry
                {
                    Date = rng.Next(1, 29) + " " + Months[rng.Next(12)] + " " + year,
                    Charge = Charges[rng.Next(Charges.Length)],
                    Outcome = Outcomes[rng.Next(Outcomes.Length)],
                });

                year += rng.Next(0, 3);
                if (year > 1986)
                    year = 1986;
            }
        }

        /// <summary>Writes a line the outfit earned. Newest LAST, like the priors, and
        /// the sheet is trimmed from the front so a long campaign does not turn a man's
        /// file into an archive.</summary>
        public static void Add(Character member, string date, string charge, string outcome)
        {
            if (member == null)
                return;

            member.RapSheet.Add(new RapEntry
            {
                Date = date ?? "",
                Charge = charge ?? "",
                Outcome = outcome ?? "",
            });

            const int kept = 8;
            if (member.RapSheet.Count > kept)
                member.RapSheet.RemoveRange(0, member.RapSheet.Count - kept);
        }
    }
}
