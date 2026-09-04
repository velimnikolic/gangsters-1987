using System.Collections.Generic;
using System.Text;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// A YARDSTICK THAT CAN FAIL (AI-008, GAN-389). Every house on the paper clock for
    /// a fortnight or a quarter, with no scene at all, and the plan's §1.1 table printed
    /// ONCE A DAY per house: men, crews, doors, blocks, money, the book, the rounds,
    /// the worst door, the grievances, and the intents the gateway actually accepted.
    ///
    /// The first yardstick (RIVAL-011) returned passed on the city that produced every
    /// fault of the 2026-09-04 run, because it could not see any of them: two blocks a
    /// house was the whole world, and it filed no incident, no police, no arrest. This
    /// one runs on four blocks a house with real neighbours and real rates
    /// (<see cref="PaperCity"/>), and it FAILS on the things the run exists to remove -
    /// a house that led no block by day 14, a safe under a week's payroll, a round
    /// older than RoundStallHours, a door demanded more than three times, and a
    /// "stoji": a house that had not one intent accepted in a whole day.
    ///
    /// Its limit is written on its face: THE PAPER CLOCK MEASURES THE BOOKS; THE LIVE
    /// HARNESS MEASURES THE STREET. Nothing here is arrested, shot or stalled on a
    /// pavement, so arrests read zero by construction and the street's own faults
    /// (U2, U5, U7, U9) are the harness's to find, not this file's.
    /// </summary>
    public static class UnderworldSim
    {
        public const float DoorstepMetres = 20f;
        public const float BlockMetres = 200f;
        public const float PresencePerHour = 4f;
        public const float PresenceCap = 60f;

        /// <summary>The day the fortnight's measures are read on.</summary>
        public const int FortnightDay = 14;

        /// <summary>The most any door may be asked before the yardstick fails
        /// (AI-003's measure).</summary>
        public const int MaxDemandsPerDoor = 3;

        public const string Limit =
            "the paper clock measures the books; the live harness measures the street " +
            "(no arrests, no police, no stalled walk can happen here)";

        public sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Failures = new List<string>();
            public int Negatives;
            public int OwnershipRefusals;
            public int Frozen;
            public int ThinkMilliseconds;

            /// <summary>What the fortnight came to, summed over the houses that ran:
            /// the ground held and the doors paying on day 14, which is what the
            /// cadence comparison (A19) is actually about.</summary>
            public int BlocksAtFortnight;
            public int DoorsAtFortnight;
            public int HousesAtFortnight;
            public string Error = "";

            public bool Clean =>
                string.IsNullOrEmpty(Error) && OwnershipRefusals == 0 &&
                Failures.Count == 0;
        }

        /// <summary>What one family has managed, and when; and today's counts.</summary>
        public sealed class Ledger
        {
            public int LostAMan = -1;
            public int Signed = -1;
            public int Deployed = -1;
            public int TookAStreet = -1;
            public int AskedADoor = -1;
            public int BankedABag = -1;
            public int PaidTheMen = -1;
            public int Answered = -1;
            public int Banked;
            public int BankedThisWeek;
            public int AcceptedToday;
            public int ProposedToday;
            public int ThinksToday;
            public int LastTier;
            public int RoundsLostToday;
            public int StoodStill;

            /// <summary>The last thing the gateway said no to today. A house that reads
            /// zero accepted has either proposed nothing - and the tiers are why - or
            /// been refused, and then the refusal is the finding (AI-000's lesson,
            /// applied to the paper clock).</summary>
            public string LastRefusal = "";

            public string Steps() =>
                LostAMan + "/" + Signed + "/" + Deployed + "/" + TookAStreet + "/" +
                AskedADoor + "/" + BankedABag + "/" + PaidTheMen + "/" + Answered;
        }

        public static Report Run(int seed, int days, int houses, float thinkHours = 0f)
        {
            var report = new Report();
            try
            {
                Simulate(seed, days, houses, thinkHours, report);
            }
            catch (System.Exception error)
            {
                report.Error = error.GetType().Name + ": " + error.Message;
            }
            return report;
        }

        static void Simulate(int seed, int days, int houses, float thinkHours,
            Report report)
        {
            if (houses < 1)
                houses = 1;
            if (days < 1)
                days = 1;

            var world = Underworld.Deal(seed);
            if (houses > world.Count)
                houses = world.Count;

            // The cadence under measurement (A19): the model's own unless the caller
            // names one. A fresh config, so a sweep at 4 does not leave 4 behind it.
            var config = new HouseMindConfig();
            if (thinkHours > 0f)
                config.ThinkEveryHours = thinkHours;
            var relations = world.Relations.Config;
            var racket = new TerritoryRacketLedger();
            var dues = new TerritoryDuesLedger();
            var rounds = new TerritoryRoundLedger(racket, dues);
            var clock = new TerritoryPaperClock(rounds);
            var scheduler = new TerritoryRoundScheduler();

            var city = new PaperCity(houses, seed) { Racket = racket };
            var books = new Ledger[houses];
            for (var h = 0; h < houses; h++)
                books[h] = new Ledger();

            // Day one: each family's own street, two doors already paying, and its
            // lieutenant answering for it - the same opening the city gives them.
            for (var h = 0; h < houses; h++)
            {
                var house = world.Of(h);
                var mine = new TerritoryGangId(h);
                var home = city.HomeBlockOf(h);
                house.Front = city.Door(home, 0);
                for (var d = 1; d <= 2 && d < city.DoorsOn(home); d++)
                    racket.Demand(city.Door(home, d), mine, Strong(), 1.0, out _);
                if (house.Roster.Crews.Count > 0)
                    RosterOps.AssignBlockResponsibility(
                        house.Roster, home,
                        house.Roster.Crews[0].Id > 0
                            ? house.Roster.Crews[0].LieutenantId
                            : -1,
                        true);
            }

            // The loss the MVP starts from: one crew of every family is put under
            // strength on the first morning.
            for (var h = 0; h < houses; h++)
                books[h].LostAMan = ShootSomebody(world.Of(h)) ? 0 : -1;

            rounds.Ended = round =>
            {
                var house = world.Of(round.House.Value);
                var book = round.House.Value < books.Length ? books[round.House.Value] : null;
                if (round.Stage == TerritoryRoundStage.Lost)
                {
                    if (book != null)
                        book.RoundsLostToday++;
                    return;
                }
                if (round.Stage != TerritoryRoundStage.Banked || round.Carried <= 0 ||
                    house == null)
                    return;
                house.Runner.BankCollection(round.Carried);
                if (book == null)
                    return;
                book.Banked += round.Carried;
                book.BankedThisWeek += round.Carried;
                if (book.BankedABag < 0)
                    book.BankedABag = city.Day;
            };

            scheduler.Owed = (gang, blockId) => city.Owed(racket, dues, blockId, gang);
            scheduler.StopsOwing = (gang, blockId) => city.Stops(dues, blockId, gang);

            var intents = new List<HouseIntent>();
            var refusals = new List<string>();
            var stopwatch = new System.Diagnostics.Stopwatch();
            var thinkMs = 0.0;

            for (var hour = 0; hour < days * 24; hour++)
            {
                city.Hour += 1.0;

                // Every family holds its own street, and whatever it has walked onto.
                for (var h = 0; h < houses; h++)
                {
                    var mine = new TerritoryGangId(h);
                    city.Stand(city.HomeBlockOf(h), mine, PresencePerHour);
                    city.StandPosted(mine, PresencePerHour);
                }

                world.AdvanceHours(1f);

                world.Think(city.Hour, config.ThinkEveryHours, house =>
                {
                    if (house.GangId >= houses)
                        return;
                    stopwatch.Restart();
                    var view = city.Look(world, racket, dues, house, config, rounds);
                    books[house.GangId].ThinksToday++;
                    books[house.GangId].LastTier =
                        HouseMind.Think(view, config, relations, intents);

                    var done = 0;
                    refusals.Clear();
                    for (var i = 0; i < intents.Count && done < config.MaxIntentsPerThink;
                         i++)
                    {
                        var refusal = city.Carry(
                            world, racket, dues, rounds, clock, house, intents[i],
                            books[house.GangId]);
                        done++;
                        books[house.GangId].ProposedToday++;
                        if (string.IsNullOrEmpty(refusal))
                        {
                            books[house.GangId].AcceptedToday++;
                            continue;
                        }
                        books[house.GangId].LastRefusal = intents[i] + ": " + refusal;
                        refusals.Add(refusal);
                        city.BackoffsOf(house.GangId).Note(
                            intents[i].Key, refusal, city.Hour, config);

                        // THE ONE REFUSAL THAT IS A BUG. A mind proposing something the
                        // gateway forbids for ownership is a mind reaching past its view.
                        if (refusal.Contains("not ours") || refusal.Contains("own outfit"))
                            report.OwnershipRefusals++;
                    }
                    thinkMs += stopwatch.Elapsed.TotalMilliseconds;
                }, houses);

                for (var h = 0; h < houses; h++)
                    scheduler.Tend(
                        world.Of(h), city.Day, city.Day % 7,
                        (int)(city.Hour - city.Day * 24.0), rounds,
                        (house, crew, blockId) =>
                            city.Send(house, crew, blockId, racket, dues, rounds, clock));

                clock.Tick(city.Hour,
                    (round, stop) => city.Stop(racket, dues, round, stop, seed),
                    null);

                // A round the paper clock has left behind (AI-002's measure).
                for (var r = 0; r < rounds.Rounds.Count; r++)
                    if (city.Hour - rounds.Rounds[r].LastMoveAt > config.RoundStallHours + 1.0)
                        Fail(report, "seed " + seed + " day " + city.Day + " house " +
                                     rounds.Rounds[r].House.Value +
                                     ": a round has not moved for " +
                                     (int)(city.Hour - rounds.Rounds[r].LastMoveAt) +
                                     " hours");

                if (city.Day == city.LastDay)
                    continue;
                city.LastDay = city.Day;
                Midnight(world, racket, dues, rounds, city, books, houses, config,
                    seed, report);
            }

            report.ThinkMilliseconds = (int)thinkMs;
        }

        static readonly List<(string key, double until)> heldScratch =
            new List<(string, double)>();

        /// <summary>What this house is not asking for again yet (P4), for a failure
        /// line that has to say why a house stood still.</summary>
        static string Held(PaperCity city, int gangId)
        {
            city.BackoffsOf(gangId).Collect(heldScratch);
            if (heldScratch.Count == 0)
                return "nothing";
            var note = "";
            for (var i = 0; i < heldScratch.Count && i < 4; i++)
                note += heldScratch[i].key + " until " +
                        (double.IsPositiveInfinity(heldScratch[i].until)
                            ? "ever"
                            : ((int)heldScratch[i].until).ToString()) + "; ";
            return note + (heldScratch.Count > 4 ? "(+" + (heldScratch.Count - 4) + ")" : "");
        }

        static void Fail(Report report, string what)
        {
            for (var i = 0; i < report.Failures.Count; i++)
                if (report.Failures[i] == what)
                    return;
            report.Failures.Add(what);
        }

        static readonly List<TerritoryBlockId> ledScratch = new List<TerritoryBlockId>();

        static void Midnight(
            Underworld world, TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryRoundLedger rounds, PaperCity city, Ledger[] books, int houses,
            HouseMindConfig config, int seed, Report report)
        {
            world.DayTick();
            var day = city.Day;

            for (var h = 0; h < houses; h++)
            {
                var house = world.Of(h);
                if (house == null)
                    continue;

                var book = books[h];
                if (book.PaidTheMen < 0 && book.Banked > 0)
                    book.PaidTheMen = day;
                if (book.Deployed < 0 && UpToStrength(house.Roster, config))
                    book.Deployed = day;

                if (house.Runner.Accounts.Safe < 0)
                    report.Negatives++;

                var phase = house.IsPlayer
                    ? HousePhase.War
                    : HouseMind.PhaseOf(
                        city.Look(world, racket, dues, house, config, rounds), config);
                DayLine(world, racket, dues, rounds, city, house, book, config, day,
                    seed, phase, report);

                // The failing conditions, read off the same figures the line prints.
                if (house.IsPlayer || house.Finished)
                    continue;
                var payroll = Wages.DailyPayroll(house.Roster);
                if (day >= 7 && house.Runner.Accounts.Safe < 7 * payroll)
                    Fail(report, "seed " + seed + " day " + day + " house " + h +
                                 ": the safe ($" + house.Runner.Accounts.Safe +
                                 ") is under a week's payroll ($" + 7 * payroll + ")");
                if (day == FortnightDay && city.BlocksLedBy(h) == 0)
                    Fail(report, "seed " + seed + " house " + h +
                                 ": led no block by day " + FortnightDay);
                city.CountDoors(racket, h, out var payingNow, out _, out _, out var worst);
                if (day == FortnightDay)
                {
                    report.HousesAtFortnight++;
                    report.BlocksAtFortnight += city.BlocksLedBy(h);
                    report.DoorsAtFortnight += payingNow;
                }
                if (worst > MaxDemandsPerDoor)
                    Fail(report, "seed " + seed + " day " + day + " house " + h +
                                 ": a door was demanded " + worst + " times");
                // "STOJI": a house with ground still to take that took none all day.
                // A house in MEN or WAR with no money to spend is WAITING, not
                // standing - the paper city gives it eighteen doors and the wage
                // table asks three doors a hood (EPIC 24's question, not this one's).
                if (day >= 2 && book.AcceptedToday == 0 && phase == HousePhase.Land)
                {
                    book.StoodStill++;
                    report.Frozen++;
                    Fail(report, "seed " + seed + " day " + day + " house " + h +
                                 ": STOJI - ground to take and not one intent accepted all day" +
                                 " (thinks " + book.ThinksToday + ", tier " +
                                 book.LastTier + ", proposed " + book.ProposedToday +
                                 (string.IsNullOrEmpty(book.LastRefusal)
                                     ? ""
                                     : ", last refusal " + book.LastRefusal) + ") · " +
                                 HouseMind.PhaseNote(
                                     city.Look(world, racket, dues, house, config, rounds),
                                     config) + " · held " + Held(city, h));
                }
                book.AcceptedToday = 0;
                book.ProposedToday = 0;
                book.ThinksToday = 0;
                book.LastRefusal = "";
                book.RoundsLostToday = 0;
                if (day % 7 == 0)
                    book.BankedThisWeek = 0;
            }

            // A day of the meter for every paying door, at the door's own rate.
            for (var b = 0; b < city.Blocks; b++)
            {
                var blockId = city.BlockAt(b);
                for (var d = 0; d < city.DoorsOn(blockId); d++)
                {
                    var businessId = city.Door(blockId, d);
                    if (racket.TryGetProtector(businessId, out var protector))
                        dues.AccrueDay(businessId, protector, city.RateOf(businessId));
                }
            }
        }

        /// <summary>The plan's §1.1 table, one line per house per day.</summary>
        static void DayLine(Underworld world, TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, TerritoryRoundLedger rounds, PaperCity city,
            House house, Ledger book, HouseMindConfig config, int day, int seed,
            HousePhase phase, Report report)
        {
            var h = house.GangId;
            var roster = house.Roster;
            int active = 0, hurt = 0, jailed = 0, dead = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Status == CharacterStatus.Active)
                    active++;
                else if (man.Status == CharacterStatus.Hospitalized ||
                         man.Status == CharacterStatus.Taken)
                    hurt++;
                else if (man.Status == CharacterStatus.Jailed)
                    jailed++;
                else
                    dead++;
            }

            var crews = 0;
            var full = 0;
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                if (roster.Crews[c].LieutenantId == roster.BossId)
                    continue;
                crews++;
                if (Hoods(roster, roster.Crews[c]) >= config.HoodsPerCrew)
                    full++;
            }

            city.CountDoors(racket, h, out var paying, out var hesitant, out var refused,
                out var worst);

            var jobs = 0;
            var oldestJob = 0;
            for (var i = 0; i < house.Runner.Book.Jobs.Count; i++)
            {
                var job = house.Runner.Book.Jobs[i];
                if (job.Stage == JobStage.Finished)
                    continue;
                jobs++;
                if (day - job.IssuedDay > oldestJob)
                    oldestJob = day - job.IssuedDay;
            }

            var out_ = 0;
            var gap = 0.0;
            for (var r = 0; r < rounds.Rounds.Count; r++)
            {
                if (rounds.Rounds[r].House.Value != h)
                    continue;
                out_++;
                var behind = city.Hour - rounds.Rounds[r].LastMoveAt;
                if (behind > gap)
                    gap = behind;
            }

            var maxGrievance = 0f;
            var against = -1;
            var wars = 0;
            for (var other = 0; other < world.Count; other++)
            {
                if (other == h || world.Of(other) == null)
                    continue;
                if (world.Relations.StanceBetween(h, other) == Stance.War)
                    wars++;
                var owed = world.Relations.Grievance(h, other);
                if (owed > maxGrievance)
                {
                    maxGrievance = owed;
                    against = other;
                }
            }

            var sb = new StringBuilder();
            sb.Append("seed ").Append(seed)
              .Append(" day ").Append(day)
              .Append(" house ").Append(h)
              .Append(" men ").Append(active).Append('/').Append(jailed).Append('/')
              .Append(hurt).Append('/').Append(dead)
              .Append(" crews ").Append(crews).Append('/').Append(full).Append("full")
              .Append(" doors ").Append(paying).Append('/').Append(hesitant).Append('/')
              .Append(refused)
              .Append(" blocks ").Append(city.BlocksLedBy(h))
              .Append(" safe ").Append(house.Runner.Accounts.Safe)
              .Append(" payroll ").Append(Wages.DailyPayroll(roster))
              .Append(" week$ ").Append(book.BankedThisWeek)
              .Append(" jobs ").Append(jobs).Append('/').Append(oldestJob).Append('d')
              .Append(" rounds ").Append(out_).Append('/').Append((int)gap).Append('h')
              .Append(" worstdoor ").Append(worst)
              .Append(" arrests 0")
              .Append(" lost ").Append(book.RoundsLostToday)
              .Append(" grudge ").Append((int)maxGrievance)
              .Append(against >= 0 ? "@" + against : "")
              .Append(" wars ").Append(wars)
              .Append(" accepted ").Append(book.AcceptedToday)
              .Append('/').Append(book.ProposedToday).Append("prop")
              .Append(" phase ").Append(house.IsPlayer ? "-" : phase.ToString())
              .Append(" steps ").Append(book.Steps());
            report.Lines.Add(sb.ToString());
        }

        // ------------------------------------------------------------------- helpers

        static bool ShootSomebody(House house)
        {
            if (house?.Roster == null || house.Roster.Crews.Count == 0)
                return false;
            var crew = house.Roster.Crews[0];
            var shot = false;
            while (Hoods(house.Roster, crew) >= HouseMindConfig.Default.MinHoods)
            {
                var one = false;
                for (var i = 0; i < crew.HoodIds.Count && !one; i++)
                {
                    var man = house.Roster.Find(crew.HoodIds[i]);
                    if (man == null || man.Gone || man.Status != CharacterStatus.Active)
                        continue;
                    HouseOps.Kill(house, man.Id);
                    one = true;
                    shot = true;
                }
                if (!one)
                    break;
            }
            return shot;
        }

        static int Hoods(Roster roster, Crew crew)
        {
            var count = 0;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man != null && !man.Gone && man.Status == CharacterStatus.Active)
                    count++;
            }
            return count;
        }

        static bool UpToStrength(Roster roster, HouseMindConfig config)
        {
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                if (roster.Crews[c].LieutenantId == roster.BossId)
                    continue;
                if (Hoods(roster, roster.Crews[c]) < config.MinHoods)
                    return false;
            }
            return true;
        }

        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);
    }
}
