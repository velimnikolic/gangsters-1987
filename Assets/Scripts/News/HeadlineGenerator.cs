using System.Collections.Generic;
using LivingCity.Gangs;

namespace LivingCity.News
{
    /// <summary>
    /// Writes the day's front page: a handful of headlines drawn from per-desk template
    /// pools, with slots filled from the game's own tables (the five families come from
    /// GangCatalog, so the press talks about the same names the street does). When
    /// NewsCalendar pins a real 1987 event to today's date, it leads the page.
    ///
    /// Engine-free and deterministic: the same seed and date always print the same
    /// paper, so a reloaded save reads yesterday's edition, not a reshuffle of it.
    /// Period tone and vocabulary trace back to Docs/1987-period-reference.md.
    /// </summary>
    public static class HeadlineGenerator
    {
        /// <summary>Six stories fills a tabloid front page: the lead plus one from
        /// each remaining desk.</summary>
        public const int FrontPageSize = 6;

        /// <summary>How many stories carry a picture, counted from the lead down. A
        /// real front page runs two or three cuts, and every one of them costs the
        /// studio a staged render - so this is a look decision and a budget both.</summary>
        public const int PhotosPerPage = 2;

        /// <summary>Every headline - filled templates and calendar entries alike -
        /// must fit this. Sized for one line of the newspaper UI's lead font; the
        /// headless suite proofs both pools against it.</summary>
        public const int TextBudget = 56;

        /// <summary>Neighbourhood names the templates drop in. Invented for the city
        /// rather than borrowed from a real one, and written WITHOUT a leading "the"
        /// so they read in both "IN X" and "X WAREHOUSE" positions.</summary>
        static readonly string[] Districts =
        {
            "RIVERSIDE", "EASTSIDE", "LITTLE ITALY", "HARBOR ROW", "OLD TOWN", "BRICKYARD",
        };

        // ------------------------------------------------------------------ pools
        //
        // Slots: {GANG} and {GANG2} draw distinct families from GangCatalog.Names,
        // {DISTRICT} draws from the table above, {COUNT} rolls 3..29, {KILOS} rolls
        // a plausible seizure. All-caps throughout - the paper is a tabloid.

        static readonly string[] CrimeDesk =
        {
            "BODY FOUND BEHIND {DISTRICT} WAREHOUSE",
            "{GANG} SOLDIER GUNNED DOWN IN {DISTRICT}",
            "WITNESS IN {GANG} CASE CHANGES HIS STORY",
            "MASKED CREW EMPTIES {DISTRICT} PAYROLL OFFICE",
            "POLICE SWEEP {DISTRICT}; {COUNT} ARRESTS BY DAWN",
            "GRAND JURY EYES {GANG} FAMILY FINANCES",
            "TURF WAR? {GANG} AND {GANG2} TRADE SHOTS",
            "OFF-DUTY COP WOUNDED IN {DISTRICT} SHOOTOUT",
            "FIREBOMB GUTS SOCIAL CLUB TIED TO {GANG}",
            "MOB LAWYER: MY CLIENT SELLS OLIVE OIL",
        };

        static readonly string[] DrugWarDesk =
        {
            "DEA SEIZES {KILOS} KILOS AT THE WATERFRONT",
            "CRACK CORNER WAR CLAIMS {COUNT} MORE IN {DISTRICT}",
            "FIVE-DOLLAR VIALS, MILLION-DOLLAR MISERY",
            "PAGER AND A PAYPHONE: HOW THE CORNERS WORK",
            "CITY HALL VOWS CRACKDOWN ON CRACK HOUSES",
            "CARTEL PIPELINE RUNS THROUGH OUR DOCKS, FEDS SAY",
            "'JUST SAY NO' BILLBOARD RISES OVER {DISTRICT}",
            "NARCOTICS SQUAD DOUBLES; OVERTIME TRIPLES",
            "SPEEDBOAT CHASE ENDS IN {KILOS}-KILO BUST",
            "SCHOOLYARD DEALERS FACE MANDATORY MINIMUMS",
        };

        static readonly string[] NationDesk =
        {
            "GIULIANI PROMISES MORE RICO INDICTMENTS",
            "FBI: THE FIVE FAMILIES ARE BLEEDING MEN",
            "OMERTA IS DEAD, TURNCOAT TELLS SENATE PANEL",
            "CAPITAL MURDER RATE CLIMBS AS CRACK SPREADS",
            "IRAN-CONTRA QUESTIONS FOLLOW WHITE HOUSE DENIALS",
            "REAGAN APPROVAL DIPS AS HEARINGS DRAG ON",
            "MOB TRIALS CLOG FEDERAL DOCKETS COAST TO COAST",
            "MANDATORY MINIMUMS FILL PRISONS, CRITICS WARN",
        };

        static readonly string[] WorldDesk =
        {
            "GORBACHEV TALKS GLASNOST; OLD GUARD FROWNS",
            "MOSCOW SIGNALS THAW; PENTAGON STAYS WARY",
            "BERLIN WALL STANDS DESPITE THE SPEECHES",
            "PANAMA'S GENERAL DENIES CARTEL TIES",
            "COLOMBIA EXTRADITION FIGHT TURNS BLOODY",
            "MEDELLIN'S REACH FELT FROM MIAMI TO OUR PIERS",
            "SICILY TRIES ITS OWN: MAXI-TRIAL GRINDS ON",
            "AIDS CRISIS DEEPENS; CLINICS PLEAD FOR FUNDS",
        };

        static readonly string[] BusinessDesk =
        {
            "DOW WOBBLES; TRADERS BLAME PROGRAM SELLING",
            "JUNK BOND KING UNDER SEC MICROSCOPE",
            "YUPPIES BID UP WATERFRONT LOFTS IN {DISTRICT}",
            "CAR PHONES: STATUS SYMBOL OR OFFICE ON WHEELS?",
            "LAUNDROMATS, PIZZERIAS AND OTHER CASH MIRACLES",
            "CITY BANKS QUIET ON SUITCASE DEPOSITS",
            "DOCKWORKERS THREATEN STRIKE OVER NIGHT SHIFTS",
            "CASINO PERMIT FIGHT HEATS UP AT CITY HALL",
        };

        static readonly string[] CultureDesk =
        {
            "'MIAMI VICE' RATINGS SLIP BUT THE LOOK REMAINS",
            "HIP-HOP FILLS THE ARMORY; NEIGHBORS SUE",
            "PASTEL SUITS AND SHOULDER PADS SWEEP MENSWEAR",
            "'ROBOCOP' PACKS MIDNIGHT SHOWS DOWNTOWN",
            "WHITNEY HOUSTON ADDS SECOND ARENA NIGHT",
            "VIDEO ARCADES BOOM AS QUARTERS VANISH",
            "BIG HAIR, BIGGER AMPS: METAL RULES THE STRIP",
            "SYNTH-POP AND SAXOPHONES OWN THE AIRWAVES",
        };

        /// <summary>Fixed page order - crime above the fold, culture at the bottom.
        /// The rotation START is rolled per day so pages differ, but the relative
        /// order never surprises the layout.</summary>
        static readonly HeadlineDesk[] DeskOrder =
        {
            HeadlineDesk.Crime, HeadlineDesk.DrugWar, HeadlineDesk.Nation,
            HeadlineDesk.World, HeadlineDesk.Business, HeadlineDesk.Culture,
        };

        /// <summary>
        /// The whole day's paper in one call. Deterministic in (seed, date); count is
        /// clamped to [1, 12] - two passes over six desks is the most the pools can
        /// fill without repeating a template on one page.
        /// </summary>
        public static Headline[] FrontPage(int seed, NewsDate date, int count = FrontPageSize)
        {
            if (count < 1) count = 1;
            if (count > 12) count = 12;

            // One stream per (seed, date): the campaign seed picks the world, the
            // date picks the day's edition, and neither perturbs the other's draws.
            var rng = new System.Random(Mix(seed, date.DayOfYear));
            var page = new List<Headline>(count);

            if (NewsCalendar.TryGet(date, out var pinned))
                page.Add(new Headline { Desk = pinned.Desk, Text = pinned.Text, Historical = true });

            // Walk the desks from a rolled start, one story each, skipping templates
            // already used - so a second lap stays duplicate-free.
            var used = new HashSet<string>();
            var deskCursor = rng.Next(DeskOrder.Length);
            while (page.Count < count)
            {
                var desk = DeskOrder[deskCursor % DeskOrder.Length];
                deskCursor++;

                var pool = PoolFor(desk);
                var pick = rng.Next(pool.Length);
                for (var probe = 0; probe < pool.Length; probe++)
                {
                    var template = pool[(pick + probe) % pool.Length];
                    if (!used.Add(template))
                        continue;
                    var text = Fill(template, rng, out var gangId);
                    page.Add(new Headline { Desk = desk, Text = text, GangId = gangId });
                    break;
                }
            }

            // The picture desk last, so adding or removing a photo can never change
            // which stories ran - the words are set before the camera is loaded.
            var photos = PhotosPerPage < page.Count ? PhotosPerPage : page.Count;
            var printed = new HashSet<string>();
            for (var i = 0; i < photos; i++)
            {
                page[i].Photo = PictureDesk.For(page[i].Desk, page[i].GangId, rng, printed);
                printed.Add(page[i].Photo.ModelName);
            }

            return page.ToArray();
        }

        /// <summary>
        /// Avalanches (seed, day) before it reaches System.Random, whose nearby seeds
        /// produce visibly correlated first draws - without this, consecutive days
        /// keep printing the same culture piece. Fingerprint mix from xxHash.
        /// </summary>
        static int Mix(int seed, int dayOfYear)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)dayOfYear * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (int)h;
            }
        }

        static string[] PoolFor(HeadlineDesk desk)
        {
            switch (desk)
            {
                case HeadlineDesk.Crime: return CrimeDesk;
                case HeadlineDesk.DrugWar: return DrugWarDesk;
                case HeadlineDesk.Nation: return NationDesk;
                case HeadlineDesk.World: return WorldDesk;
                case HeadlineDesk.Business: return BusinessDesk;
                default: return CultureDesk;
            }
        }

        /// <summary>
        /// Slot filling. Draws happen only for slots the template actually contains,
        /// in a fixed order, so adding a template never reshuffles another's fills.
        /// <paramref name="gangId"/> reports the family the story ended up naming, or
        /// -1 - the picture desk prints that family's man.
        /// </summary>
        static string Fill(string template, System.Random rng, out int gangId)
        {
            var text = template;
            gangId = -1;

            if (text.Contains("{GANG}"))
            {
                var gang = rng.Next(GangCatalog.Names.Length);
                gangId = gang;
                text = text.Replace("{GANG}", GangCatalog.Names[gang].ToUpperInvariant());

                if (text.Contains("{GANG2}"))
                {
                    var other = rng.Next(GangCatalog.Names.Length - 1);
                    if (other >= gang) other++;
                    text = text.Replace("{GANG2}", GangCatalog.Names[other].ToUpperInvariant());
                }
            }

            if (text.Contains("{DISTRICT}"))
                text = text.Replace("{DISTRICT}", Districts[rng.Next(Districts.Length)]);

            if (text.Contains("{COUNT}"))
                text = text.Replace("{COUNT}", (3 + rng.Next(27)).ToString());

            if (text.Contains("{KILOS}"))
                text = text.Replace("{KILOS}", (20 + rng.Next(38) * 10).ToString());

            return text;
        }
    }
}
