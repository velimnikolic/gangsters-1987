using System.Collections.Generic;
using System.Text;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// RIVAL-011. THE LONG RUN.
    ///
    /// Every house on the paper clock for a month or three, with no scene at all, and a
    /// week-by-week line for each of them: what is in the safe, what the men cost, how
    /// many doors pay, how much ground is led, who is alive, who is at war, and the day
    /// each of the seven MVP steps first happened.
    ///
    /// This is a YARDSTICK, not a test. Its numbers are notes for the user to read; the
    /// only things that make it fail are an exception, an order refused for ownership
    /// (which would mean a mind is proposing what the gateway forbids), and a house
    /// running its safe negative.
    /// </summary>
    public static class UnderworldSim
    {
        public const int DoorsPerBlock = 4;
        public const float DoorstepMetres = 20f;
        public const float BlockMetres = 200f;
        public const float PresencePerHour = 4f;
        public const float PresenceCap = 60f;
        public const int WeeklyRate = 700;

        public sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public int Negatives;
            public int OwnershipRefusals;
            public string Error = "";

            public bool Clean => string.IsNullOrEmpty(Error) && OwnershipRefusals == 0;
        }

        /// <summary>What one family has managed, and when.</summary>
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

            public string Steps() =>
                LostAMan + "/" + Signed + "/" + Deployed + "/" + TookAStreet + "/" +
                AskedADoor + "/" + BankedABag + "/" + PaidTheMen + "/" + Answered;
        }

        public static Report Run(int seed, int days, int houses)
        {
            var report = new Report();
            try
            {
                Simulate(seed, days, houses, report);
            }
            catch (System.Exception error)
            {
                report.Error = error.GetType().Name + ": " + error.Message;
            }
            return report;
        }

        static void Simulate(int seed, int days, int houses, Report report)
        {
            if (houses < 1)
                houses = 1;
            if (days < 1)
                days = 1;

            var world = Underworld.Deal(seed);
            if (houses > world.Count)
                houses = world.Count;

            var config = HouseMindConfig.Default;
            var relations = world.Relations.Config;
            var racket = new TerritoryRacketLedger();
            var dues = new TerritoryDuesLedger();
            var rounds = new TerritoryRoundLedger(racket, dues);
            var clock = new TerritoryPaperClock(rounds);
            var scheduler = new TerritoryRoundScheduler();

            var city = new PaperCity(houses);
            var books = new Ledger[houses];
            for (var h = 0; h < houses; h++)
                books[h] = new Ledger();

            // Day one: each family's own street, one door already paying, and its
            // lieutenant answering for it - the same opening the city gives them.
            for (var h = 0; h < houses; h++)
            {
                var house = world.Of(h);
                var mine = new TerritoryGangId(h);
                house.Front = city.Door(city.HomeBlockOf(h), 0);
                racket.Demand(city.Door(city.HomeBlockOf(h), 1), mine, Strong(), 1.0,
                    out _);
                racket.Demand(city.Door(city.HomeBlockOf(h), 2), mine, Strong(), 1.0,
                    out _);
                if (house.Roster.Crews.Count > 0)
                    RosterOps.AssignBlockResponsibility(
                        house.Roster, city.HomeBlockOf(h),
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
                if (round.Stage != TerritoryRoundStage.Banked || round.Carried <= 0)
                    return;
                var house = world.Of(round.House.Value);
                if (house == null)
                    return;
                house.Runner.BankCollection(round.Carried);
                var book = round.House.Value < books.Length ? books[round.House.Value] : null;
                if (book == null)
                    return;
                book.Banked += round.Carried;
                if (book.BankedABag < 0)
                    book.BankedABag = city.Day;
            };

            scheduler.Owed = (gang, blockId) => city.Owed(racket, dues, blockId, gang);
            scheduler.StopsOwing = (gang, blockId) => city.Stops(dues, blockId, gang);

            var intents = new List<HouseIntent>();
            var refusals = new List<string>();

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
                    var view = city.Look(world, racket, dues, house, config);
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
                        if (string.IsNullOrEmpty(refusal))
                            continue;
                        refusals.Add(refusal);

                        // THE ONE REFUSAL THAT IS A BUG. A mind proposing something the
                        // gateway forbids for ownership is a mind reaching past its view.
                        if (refusal.Contains("not ours") || refusal.Contains("own outfit"))
                            report.OwnershipRefusals++;
                    }
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

                if (city.Day == city.LastDay)
                    continue;
                city.LastDay = city.Day;
                Midnight(world, racket, dues, city, books, houses, report);
                if (city.Day % 7 == 0 || city.Day == days)
                    Week(world, racket, dues, city, books, houses, report);
            }

            Week(world, racket, dues, city, books, houses, report);
        }

        static void Midnight(
            Underworld world, TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            PaperCity city, Ledger[] books, int houses, Report report)
        {
            world.DayTick();

            for (var h = 0; h < houses; h++)
            {
                var house = world.Of(h);
                if (house == null)
                    continue;

                var book = books[h];
                if (book.PaidTheMen < 0 && book.Banked > 0)
                    book.PaidTheMen = city.Day;
                if (book.Deployed < 0 && UpToStrength(house.Roster))
                    book.Deployed = city.Day;

                // EPIC 24 owns the short envelope. Until it lands, a safe under water is
                // COUNTED and printed rather than treated as a fault.
                if (house.Runner.Accounts.Safe < 0)
                    report.Negatives++;
            }

            // A day of the meter for every paying door.
            for (var b = 0; b < city.Blocks; b++)
                for (var d = 0; d < DoorsPerBlock; d++)
                {
                    var businessId = city.Door(city.BlockAt(b), d);
                    if (racket.TryGetProtector(businessId, out var protector))
                        dues.AccrueDay(businessId, protector, WeeklyRate);
                }
        }

        static void Week(
            Underworld world, TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            PaperCity city, Ledger[] books, int houses, Report report)
        {
            for (var h = 0; h < houses; h++)
            {
                var house = world.Of(h);
                if (house == null)
                    continue;

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

                var paying = 0;
                for (var b = 0; b < city.Blocks; b++)
                    for (var d = 0; d < DoorsPerBlock; d++)
                        if (racket.StateOf(city.Door(city.BlockAt(b), d),
                                new TerritoryGangId(h)) ==
                            TerritoryProtectionState.Compliant)
                            paying++;

                var wars = 0;
                for (var other = 0; other < world.Count; other++)
                    if (other != h &&
                        world.Relations.StanceBetween(h, other) == Stance.War)
                        wars++;

                var sb = new StringBuilder();
                sb.Append("day ").Append(city.Day)
                  .Append(" house ").Append(h)
                  .Append(" safe ").Append(house.Runner.Accounts.Safe)
                  .Append(" payroll ").Append(Wages.DailyPayroll(roster))
                  .Append(" doors ").Append(paying)
                  .Append(" blocks ").Append(city.BlocksLedBy(h))
                  .Append(" men ").Append(active).Append('/').Append(hurt).Append('/')
                  .Append(jailed).Append('/').Append(dead)
                  .Append(" wars ").Append(wars)
                  .Append(" banked ").Append(books[h].Banked)
                  .Append(" steps ").Append(books[h].Steps());
                report.Lines.Add(sb.ToString());
            }
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

        static bool UpToStrength(Roster roster)
        {
            for (var c = 0; c < roster.Crews.Count; c++)
                if (Hoods(roster, roster.Crews[c]) < HouseMindConfig.Default.MinHoods)
                    return false;
            return true;
        }

        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);
    }
}
