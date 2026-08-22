using LivingCity.Entities;

namespace LivingCity.Gangs
{
    /// <summary>
    /// A front's two sets of books. Every family operates behind a door that is a real
    /// shop to everybody who walks past it - a cafe, a laundry, a billiard hall - and
    /// something else entirely to the people who work there. This class is BOTH, side by
    /// side, because the whole point of a front is that the two versions of it disagree.
    ///
    /// Plain data. The words are made once, when the family takes the premises, and then
    /// they are the truth about that door for the rest of the campaign.
    /// </summary>
    public sealed class FrontDossier
    {
        // --------------------------------------------------- the face the street sees

        /// <summary>What is painted over the window - "THE BLUE DOOR CAFE".</summary>
        public string Sign = "";

        /// <summary>The trade, as the licence names it: Cafe, Laundry, Billiard Hall.</summary>
        public string Trade = "";

        /// <summary>Whose name is on the licence. Never the capo's - a front the family
        /// signs is not a front.</summary>
        public string Proprietor = "";

        /// <summary>Number and street. Filled in by the city (only the city knows what
        /// its streets are called), so this stays empty in a headless deal.</summary>
        public string Address = "";

        public int Since;
        public string Hours = "";
        public int Staff;

        /// <summary>Weekly takings AS DECLARED - what the trade could plausibly earn.</summary>
        public int Takings;

        public string Licence = "";

        /// <summary>The line an inspector would write, and did.</summary>
        public string Clean = "";

        // ------------------------------------------------- what actually happens there

        /// <summary>The racket run out of the back: numbers, protection, a card room.</summary>
        public string Racket = "";

        /// <summary>How the racket works, in a sentence - the card's note block.</summary>
        public string RacketNote = "";

        /// <summary>The capo whose corner this is - the man standing outside it.</summary>
        public string RunBy = "";

        /// <summary>Weekly, off the books. Several times the takings, which is the
        /// reason a family owns a laundry at all.</summary>
        public int Skim;

        /// <summary>Percent of the skim that goes upstairs to the family.</summary>
        public int Cut;

        /// <summary>Men standing outside it.</summary>
        public int Men;

        /// <summary>How much attention the law is paying this door.</summary>
        public string Heat = "";

        /// <summary>One line of what people say about the place.</summary>
        public string Whisper = "";
    }

    /// <summary>
    /// Deals a front's books. Engine-free like the rest of the Gangs core, off a stream
    /// of its own (the gang's child seed, per the PortDirector idiom in GangSeeder), so
    /// a family's premises can be dealt without disturbing anything else the city rolls.
    ///
    /// Proprietors come out of PedestrianIdentity's shared name tables - the man on the
    /// licence is an ordinary citizen, and he reads like one.
    /// </summary>
    public static class FrontBooks
    {
        /// <summary>Trades a family will put its name behind: small, cash-heavy, and
        /// somewhere a man can sit all day without anybody asking him why.</summary>
        public static readonly string[] Trades =
        {
            "Cafe", "Restaurant", "Grocery", "Bakery", "Laundry", "Barbers",
            "Hardware", "Billiard Hall", "Social Club", "Pawnbrokers",
            "Tailors", "Pizzeria",
        };

        static readonly string[] Stems =
        {
            "Blue Door", "Sorrento", "Ideal", "Eagle", "Regal", "Star", "Roma",
            "Palm", "Century", "Vesuvio", "Nightingale", "Liberty", "Crown",
            "Harbour", "Sunset", "Napoli",
        };

        /// <summary>What is run out of the back, and the line that says how. The two are
        /// separate because they are printed in different places: the trade goes in a
        /// value cell beside its label, the sentence goes in the card's note block. One
        /// string with a dash in it would have to fit both, and fits neither.</summary>
        static readonly (string trade, string how)[] Rackets =
        {
            ("Protection", "Every shop on this street pays on Fridays."),
            ("Numbers bank", "The slips come in at noon and are burned by one."),
            ("Bookmaking", "The wire runs to the back room. It is never quiet in there."),
            ("Loan-sharking", "Six for five, weekly, and nobody writes anything down."),
            ("Fencing", "Lorry loads in, no questions, and out again by morning."),
            ("Bootleg spirits", "The cellar holds more than a shop this size could sell."),
            ("Card room", "Upstairs, nightly. The house takes five percent of every pot."),
            ("Union kickbacks", "The local's books are kept here, and so is the local."),
            ("Chop-shop papers", "Titles are washed in the office at the back."),
            ("Untaxed cigarettes", "Off the docks by the crate, under the flour sacks."),
        };

        static readonly string[] HeatLines =
        {
            "Nobody has been near it.",
            "The precinct captain takes an envelope on Fridays.",
            "A detective has been asking about the back room.",
            "A patrol car parks across the street too often.",
            "A waiter talked to somebody once. He does not work here now.",
            "The licence board has an anonymous letter on file.",
        };

        static readonly string[] Whispers =
        {
            "The coffee is good. Nobody comes for the coffee.",
            "Two tables at the back are never given to anybody.",
            "The till is honest. The safe behind it is not.",
            "The delivery van comes at four in the morning, empty both ways.",
            "The telephone in the office is not on any bill.",
            "Everyone on the street knows. Nobody on the street says.",
        };

        static readonly string[] HoursLines =
        {
            "6am - 11pm, closed Sundays",
            "7am - midnight, seven days",
            "8am - 8pm, half day Wednesday",
            "10am - 2am, members after ten",
            "5am - 6pm, closed Mondays",
        };

        /// <summary>The books for one family's premises. <paramref name="seed"/> is the
        /// gang's own child seed; <paramref name="capo"/> and <paramref name="men"/> are
        /// what is actually standing outside, so the illegit page describes the street
        /// rather than a plan.</summary>
        public static FrontDossier Open(string family, string capo, int men, int seed)
        {
            var rng = new System.Random(seed);
            var trade = Trades[rng.Next(Trades.Length)];
            var takings = 900 + rng.Next(0, 53) * 50;      // $900 - $3,500 a week

            // One door in three carries the family's own name over it. A mob that hides
            // every premises reads as a conspiracy; a mob that signs one in three reads
            // as a family that has been on the street for forty years, which is what it
            // is - and it tells the player whose corner he is standing on before he
            // clicks anything.
            var racket = Rackets[rng.Next(Rackets.Length)];

            // The name on the licence is a CITIZEN's. Drawn from the same shared tables
            // as everybody else in the city, so a proprietor can turn out to be called
            // what the capo standing outside is called - and a front whose licence names
            // the man running the racket is not a front at all. Redrawn until it is
            // somebody else, the way GangSeeder redraws a crewmate's name (twenty tries,
            // then a duplicate beats an unbounded loop).
            var proprietor = "";
            for (var guard = 0; guard < 20; guard++)
            {
                proprietor = PedestrianIdentity.AllMaleNames[
                                 rng.Next(PedestrianIdentity.AllMaleNames.Count)] + " " +
                             PedestrianIdentity.AllSurnames[
                                 rng.Next(PedestrianIdentity.AllSurnames.Count)];
                if (proprietor != capo)
                    break;
            }

            var sign = rng.Next(0, 3) == 0 && !string.IsNullOrEmpty(family)
                ? family + "'s " + trade
                : Stems[rng.Next(Stems.Length)] + " " + trade;

            return new FrontDossier
            {
                Sign = sign.ToUpperInvariant(),
                Trade = trade,
                Proprietor = proprietor,
                Since = 1948 + rng.Next(0, 32),
                Hours = HoursLines[rng.Next(HoursLines.Length)],
                Staff = 2 + rng.Next(0, 7),
                Takings = takings,
                Licence = "City licence " + (1000 + rng.Next(0, 9000)) + "-" +
                          (char)('A' + rng.Next(0, 26)),
                Clean = rng.Next(0, 2) == 0
                    ? "Inspected twice this year. Clean both times."
                    : "Never inspected. The forms are in order.",

                // The whole reason the family holds the lease: the back room earns
                // several times what the counter does.
                Racket = racket.trade,
                RacketNote = racket.how,
                RunBy = string.IsNullOrEmpty(capo) ? "nobody yet" : capo,
                Skim = takings * (3 + rng.Next(0, 7)) / 100 * 100,
                Cut = 40 + rng.Next(0, 7) * 5,
                Men = men,
                Heat = HeatLines[rng.Next(HeatLines.Length)],
                Whisper = Whispers[rng.Next(Whispers.Length)],
            };
        }
    }
}
