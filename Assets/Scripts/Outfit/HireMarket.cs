using System.Collections.Generic;
using LivingCity.Generation;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// One man advertising himself in the morning paper's classified column. He is NOT
    /// on the books: the ad carries the man, and signing him is what puts him there
    /// (PersonnelDirector.HireFromAd). Every figure on it is DERIVED at read - the same
    /// discipline the balance sheet keeps - so an ad can never quote a price the wage
    /// table would not charge.
    /// </summary>
    public sealed class HireAd
    {
        /// <summary>The man himself, dealt with the edition and off the books until
        /// somebody signs him. His id is -1 until then.</summary>
        public Character Man;

        /// <summary>The stat he is selling - his best, and what the ad is headed with.</summary>
        public CharacterAttribute Trade;

        /// <summary>What KIND of man is advertising (GAN-245). None is the ordinary
        /// column - a man offering to run a crew. Anything else is a specialist selling
        /// the one thing he does, and he is signed onto the books without a rank, a
        /// crew or a place in the chain of command.</summary>
        public Specialty Specialty = Specialty.None;

        /// <summary>What a lawyer's ad quotes as his standing in court, 1-5 - the SAME
        /// function the trial reads (Personnel.Lawyer.Skill), so a four-star man in the
        /// paper argues like a four-star man on the day.</summary>
        public int Skill => Specialty == Specialty.Lawyer ? Lawyer.Skill(Man) : 0;

        /// <summary>The box number replies go to. Flavour, but a stable one: it is
        /// dealt with the man, so the same edition prints the same column twice.</summary>
        public string Box = "";

        /// <summary>The neighbourhood he says he worked - the classified's one line of
        /// provenance.</summary>
        public string From = "";

        /// <summary>What he asks a day - what the ad quotes and what he draws off the
        /// safe every midnight once he signs. Read off the wage table, never stored
        /// twice; the books keep days, so the ad is priced in them.</summary>
        public int Daily => Wages.WageFor(Man);

        /// <summary>The signing money, up front, before a single day's wage.</summary>
        public int Down => Wages.SigningFee(Daily);
    }

    /// <summary>
    /// The classified column: the men advertising for a place this morning. A fresh
    /// column every campaign day, dealt from (city seed, day) so one save's paper is
    /// another's - and so re-reading the page never re-rolls the men on it.
    ///
    /// Pure and free of UnityEngine, like the rest of the outfit layer, so the headless
    /// suite can deal a column and price it without an editor. PersonnelDirector owns
    /// the one instance and is the only door that takes a man off it.
    ///
    /// Today the column advertises LIEUTENANTS - men who bring nothing but themselves
    /// and want a crew of their own. Hoods still come off the corner
    /// (PersonnelDirector.Recruit); when the street trades in something else, it
    /// advertises here beside them.
    /// </summary>
    public sealed class HireMarket
    {
        /// <summary>Ads printed per edition. Four fills the page's two-by-two grid; a
        /// column that ran longer would scroll, and a newspaper column does not.</summary>
        public const int AdsPerEdition = 4;

        /// <summary>How often a lawyer advertises (GAN-245). He takes the LAST slot of
        /// that morning's column rather than a fifth box - the page is a two-by-two
        /// grid and a fifth ad would scroll, which a newspaper column does not do.
        ///
        /// A week apart, and never at all while the outfit already has counsel: one
        /// lawyer is the v1 rule, and a paper offering a second one is a paper offering
        /// a button that refuses itself.</summary>
        public const int LawyerAdEveryDays = 7;

        /// <summary>A man who advertises for a crew of his own claims the head for it:
        /// his Awareness and Organization are floored at the house's own promotion
        /// line (RosterOps.LowStatHalfSteps), so the paper never offers the outfit a
        /// lieutenant it would warn against making. Talent is payroll, so the floor is
        /// paid for in the price he asks.</summary>
        public const int HeadFloorHalfSteps = RosterOps.LowStatHalfSteps;

        /// <summary>The band an advertised man rolls in - a cut above the corner boy a
        /// recruiter turns up (RosterSeeder.RecruitCeilingHalfSteps, three stars) and
        /// still short of the founding six's open rolls. That cut is exactly what the
        /// signing money buys: everybody else hired after day one has to be BUILT.</summary>
        public const int AdvertisedCeilingHalfSteps = 8;

        static readonly string[] Neighbourhoods =
        {
            "RIVERSIDE", "EASTSIDE", "LITTLE ITALY", "HARBOR ROW", "OLD TOWN", "BRICKYARD",
        };

        static readonly string[] BoxLetters = { "A", "B", "C", "D", "E", "F", "G", "H" };

        readonly List<HireAd> ads = new List<HireAd>(AdsPerEdition);

        /// <summary>The column as printed, in page order.</summary>
        public IReadOnlyList<HireAd> Ads => ads;

        /// <summary>The day the column was set; -1 before the first edition.</summary>
        public int DealtDay { get; private set; } = -1;

        /// <summary>Moves whenever the column changes - a new edition, or a man signed
        /// off it. The ledger's classified page repaints on this the way every other
        /// page repaints on a director's Version.</summary>
        public int Revision { get; private set; }

        /// <summary>Sets this morning's column if it is not already set. Cheap to call
        /// on every repaint: a day that is already dealt does nothing at all.</summary>
        public void EnsureDealt(Roster roster, int seed, int day)
        {
            // No books, no column: the men are named against the roster so the paper
            // never advertises somebody the outfit already employs, and a null roster
            // is a director that has not seeded yet rather than an empty outfit.
            if (roster == null || day == DealtDay)
                return;

            DealtDay = day;
            ads.Clear();

            // One stream per (seed, day) on the personnel band: retuning the column
            // cannot re-lay the city, and today's men cannot depend on how many jobs
            // the outfit happened to run yesterday.
            var rng = new System.Random(Mix(seed + SeedOffsets.Personnel, day));
            var lawyerSlot = LawyerMorning(roster, day) ? AdsPerEdition - 1 : -1;
            for (var slot = 0; slot < AdsPerEdition; slot++)
                ads.Add(slot == lawyerSlot
                    ? DealLawyer(roster, rng, seed, day, slot)
                    : DealOne(roster, rng, seed, day, slot));

            Revision++;
        }

        /// <summary>Strikes an ad off the column - he signed, or the page is done with
        /// him. False when the ad is not on it, which is how a double click on HIRE is
        /// refused rather than charged twice.</summary>
        public bool Take(HireAd ad)
        {
            if (ad == null || !ads.Remove(ad))
                return false;

            Revision++;
            return true;
        }

        /// <summary>Puts a taken ad back in the column - the signing money was refused
        /// after the ad came off it. Appended rather than slotted back where it was:
        /// the column is four ads in page order, and a man who nearly signed reading
        /// last is a smaller lie than a page that re-orders itself under the reader.</summary>
        public void Restore(HireAd ad)
        {
            if (ad == null || ads.Contains(ad))
                return;

            ads.Add(ad);
            Revision++;
        }

        /// <summary>Whether this morning's column carries the lawyer's ad. Asked of the
        /// BOOKS and not of the courthouse: Lawyer.Counsel answers only for a lawyer who
        /// is standing up, so a retained man who was jailed or put in a hospital bed
        /// printed a second offer of counsel over himself - and nothing refuses the
        /// signing, so the outfit came out of it paying two lawyers for ever while
        /// Counsel quietly used whichever stood first on the roster.</summary>
        static bool LawyerMorning(Roster roster, int day) =>
            day > 0 && day % LawyerAdEveryDays == 0 && Lawyer.OnBooks(roster) == null;

        /// <summary>
        /// THE MAN AT THE COURTHOUSE. Dealt like anybody else and then made what he is:
        /// a specialist, which in this codebase means a man who can never be crewed,
        /// promoted or put on the front (Specialty), with the two stats a lawyer is
        /// floored at the same line a lieutenant's head is - the paper does not print
        /// an offer of counsel who could not get a hearing listed.
        ///
        /// He is priced off the wage table like every other ad: HouseRateAs reads the
        /// specialty before the rank, so his ask is the lawyer's wage plus the market
        /// premium and nothing about a crew enters into it.
        /// </summary>
        HireAd DealLawyer(Roster roster, System.Random rng, int seed, int day, int slot)
        {
            var man = RosterSeeder.Deal(roster, rng, AdvertisedCeilingHalfSteps,
                Potential.Mix(Potential.StreamFor(seed, -2), day * AdsPerEdition + slot));

            FloorHead(man, CharacterAttribute.Awareness);
            FloorHead(man, CharacterAttribute.Organization);

            man.Rank = Rank.Hood;
            man.Specialty = Specialty.Lawyer;
            man.WageAsked = Wages.AskFor(man);

            var box = "BOX " + (11 + rng.Next(80)) + "-" +
                      BoxLetters[rng.Next(BoxLetters.Length)];

            return new HireAd
            {
                Man = man,
                Trade = CharacterAttribute.Awareness,
                Specialty = Specialty.Lawyer,
                From = "THE COURTHOUSE",
                Box = box,
            };
        }

        HireAd DealOne(Roster roster, System.Random rng, int seed, int day, int slot)
        {
            var man = RosterSeeder.Deal(roster, rng, AdvertisedCeilingHalfSteps,
                Potential.Mix(Potential.StreamFor(seed, -1), day * AdsPerEdition + slot));

            // The head a lieutenancy lives on, floored before the price is read off
            // him - his CEILING first, or the floor would be clamped back down by a
            // man who was never going to be much of a head.
            FloorHead(man, CharacterAttribute.Awareness);
            FloorHead(man, CharacterAttribute.Organization);

            // He advertises as what he is: a lieutenant, and priced as one - the house
            // rate for the crew he says he can run, plus the market's premium for a
            // man who walks in ready-made (Wages.AskFor). The ask is stamped on him
            // here and drawn for as long as he holds that rank: a promotion or a
            // demotion tears the bargain up and puts him on the house scale
            // (RosterOps, WAGE-002).
            man.Rank = Rank.Lieutenant;
            man.WageAsked = Wages.AskFor(man);

            var from = Neighbourhoods[rng.Next(Neighbourhoods.Length)];
            var box = "BOX " + (11 + rng.Next(80)) + "-" +
                      BoxLetters[rng.Next(BoxLetters.Length)];

            return new HireAd
            {
                Man = man,
                Trade = BestTrade(man, day + slot),
                From = from,
                Box = box,
            };
        }

        /// <summary>Raises one of the two head stats to the promotion line, ceiling and
        /// all. A man who advertises for a crew claims he can run one; the paper is not
        /// allowed to print an offer the outfit's own promotion rule would warn
        /// against.</summary>
        static void FloorHead(Character man, CharacterAttribute attribute)
        {
            if (man.PotentialValue(attribute) <
                AttributeScale.ValueOf(HeadFloorHalfSteps))
                man.SetPotential(attribute, AttributeScale.ValueOf(HeadFloorHalfSteps));
            if (man.GetHalfSteps(attribute) < HeadFloorHalfSteps)
                man.SetHalfSteps(attribute, HeadFloorHalfSteps);
        }

        /// <summary>The stat the ad is headed with: his best, ties broken by attribute
        /// order rotated with the edition, so four men rolled the same morning do not
        /// all advertise as gun hands.</summary>
        static CharacterAttribute BestTrade(Character man, int salt)
        {
            var start = salt % AttributeScale.Count;
            if (start < 0)
                start += AttributeScale.Count;

            var best = (CharacterAttribute)start;
            var bestValue = man.GetHalfSteps(best);
            for (var step = 1; step < AttributeScale.Count; step++)
            {
                var attribute = (CharacterAttribute)((start + step) % AttributeScale.Count);
                var value = man.GetHalfSteps(attribute);
                if (value > bestValue)
                {
                    best = attribute;
                    bestValue = value;
                }
            }
            return best;
        }

        /// <summary>Avalanches (seed, day) before System.Random sees it - nearby seeds
        /// produce visibly correlated first draws, and consecutive days are as nearby as
        /// two numbers get. Same mix the newspaper's editions and the job rolls use.</summary>
        public static int Mix(int seed, int day)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)day * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (int)h;
            }
        }

        // ------------------------------------------------------------------ the copy

        /// <summary>The word the ad is headed with - what he is selling, in the trade's
        /// own language rather than the ledger's stat name. The man wrote it, not the
        /// book, which is why it lives with the market and not in LedgerText.</summary>
        /// <summary>The word an ad is headed with - a specialist's trade before his
        /// stats, because what he IS is the whole offer.</summary>
        public static string HeadingFor(HireAd ad) =>
            ad == null ? "" :
            ad.Specialty == Specialty.Lawyer ? "COUNSEL" :
            ad.Specialty == Specialty.Accountant ? "BOOKKEEPER" :
            TradeName(ad.Trade);

        /// <summary>The copy under that heading.</summary>
        public static string PitchFor(HireAd ad) =>
            ad == null ? "" :
            ad.Specialty == Specialty.Lawyer
                ? "Attorney at law. Bail applications, arraignments and trial work. " +
                  "I have never yet been told a case was too far gone to look at."
                : ad.Specialty == Specialty.Accountant
                    ? "Books kept, returns filed, questions answered before they are asked."
                    : Pitch(ad.Trade);

        public static string TradeName(CharacterAttribute trade) => trade switch
        {
            CharacterAttribute.Combat => "GUN HAND",
            CharacterAttribute.Awareness => "A THINKING MAN",
            CharacterAttribute.Stealth => "DISCREET PARTY",
            CharacterAttribute.Driving => "WHEEL MAN",
            CharacterAttribute.Streetwise => "BUSINESS HEAD",
            CharacterAttribute.Leadership => "CREW BOSS",
            CharacterAttribute.Organization => "ORGANISER",
            CharacterAttribute.StreetAuthority => "A KNOWN NAME",
            CharacterAttribute.Persuasion => "NEGOTIATOR",
            CharacterAttribute.Intimidation => "COLLECTOR",
            CharacterAttribute.Connections => "WELL CONNECTED",
            _ => "SITUATION WANTED",
        };

        /// <summary>His own copy, in the veiled voice a 1987 paper would actually
        /// print - nothing in a classified says what it means.</summary>
        public static string Pitch(CharacterAttribute trade) => trade switch
        {
            CharacterAttribute.Combat =>
                "Steady hand and quiet about it. Own iron, own car, no questions on " +
                "either side of the arrangement.",
            CharacterAttribute.Awareness =>
                "Educated man seeks serious position. I read what the other fellow " +
                "signed, and I remember it.",
            CharacterAttribute.Stealth =>
                "Discreet party available for delicate errands. In, out, and nobody " +
                "the wiser on either end.",
            CharacterAttribute.Driving =>
                "Wheel man. I know every alley in this city and which of them come " +
                "out the other end.",
            CharacterAttribute.Streetwise =>
                "Books, licences, premises. I have made a losing shop show a profit " +
                "twice, and neither time on paper only.",
            CharacterAttribute.Leadership =>
                "I run men. Nobody late, nobody drunk, nobody missing on a Friday. " +
                "Bring me four and a territory.",
            CharacterAttribute.Organization =>
                "Rotas, stock, keys. Give me the yard and you will stop hearing " +
                "about the yard.",
            CharacterAttribute.StreetAuthority =>
                "My name has been said on these corners for eleven years. Ask after " +
                "it before you write to this box.",
            CharacterAttribute.Persuasion =>
                "Difficult conversations conducted to a conclusion. Both parties " +
                "shake hands and neither one has to be carried.",
            CharacterAttribute.Intimidation =>
                "Persuasive with slow payers. I have rarely had to say a thing twice " +
                "and never had to say it loudly.",
            CharacterAttribute.Connections =>
                "I know a man at the precinct, a man at the courthouse and a man at " +
                "the docks. All three take my calls.",
            _ =>
                "Capable man seeks a place with a serious outfit. Willing, sober, and " +
                "not particular about the hours.",
        };
    }
}
