using System;
using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 25's first contract: TWENTY-ONE HOUSES ON ONE SET OF RULES. Every family has
    /// the book the player has - a roster, a safe, an order book, a wage bill - and no
    /// two of them share a man, a crew or a gun.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class UnderworldTests
    {
        /// <summary>The seed the snapshot below was taken on, and the one the whole
        /// suite deals from - the demo's own number.</summary>
        const int Seed = 1987;

        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("TwentyOneHousesAreDealt", TwentyOneHousesAreDealt),
            ("NoTwoHousesShareANumber", NoTwoHousesShareANumber),
            ("EveryFamilyStandsInAShapeTheRulesAllow", EveryFamilyStandsInAShapeTheRulesAllow),
            ("ThePlayersOpeningBooksAreUnchanged", ThePlayersOpeningBooksAreUnchanged),
            ("EachHousePaysItsOwnMenFromItsOwnSafe", EachHousePaysItsOwnMenFromItsOwnSafe),
            ("TheStreetMirrorsEveryBook", TheStreetMirrorsEveryBook),
            ("AnExtinctHouseIsSkipped", AnExtinctHouseIsSkipped),
            ("AHeadlessHouseIsSkipped", AHeadlessHouseIsSkipped),
            ("OneDealPerCity", OneDealPerCity),
            ("ACityDealsOnlyTheHousesItStands", ACityDealsOnlyTheHousesItStands),
            ("TheStreetListsOnlyDealtHouses", TheStreetListsOnlyDealtHouses),
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        // ------------------------------------------------------------------ the deal

        /// <summary>
        /// NO FAMILY RUNS ON PAPER. A city that can stand four families deals four
        /// books and leaves the rest of the catalogue empty - the ids do not move, so
        /// house 1 is the same family in a small city as in a big one, and the slots
        /// above the count answer null rather than dealing a house nobody can walk up to.
        /// </summary>
        static void ACityDealsOnlyTheHousesItStands(List<string> failures)
        {
            var small = Underworld.Deal(Seed, 4);
            if (small.Dealt != 4)
                failures.Add($"Deal: a four-family city dealt {small.Dealt}.");
            if (small.Count != GangCatalog.GangCount)
                failures.Add("Deal: the catalogue shrank with the city; an id moved.");

            for (var gangId = 0; gangId < 4; gangId++)
                if (small.Of(gangId) == null)
                    failures.Add($"Deal: house {gangId} was not dealt in a four-family city.");
            for (var gangId = 4; gangId < GangCatalog.GangCount; gangId++)
                if (small.Of(gangId) != null)
                    failures.Add($"Deal: house {gangId} was dealt into a four-family city.");

            var whole = Underworld.Deal(Seed);
            if (whole.Dealt != GangCatalog.GangCount)
                failures.Add("Deal: the default deal is no longer the whole catalogue.");
            var here = small.Of(1)?.Roster;
            var there = whole.Of(1)?.Roster;
            if (here == null || there == null ||
                here.Members.Count != there.Members.Count)
                failures.Add("Deal: a family changed when the city around it got smaller.");

            // A city below one house is still the player's own, and a city that asks for
            // more than the catalogue holds gets the catalogue.
            if (Underworld.Deal(Seed, 0).Dealt != 1 ||
                Underworld.Deal(Seed, GangCatalog.GangCount + 5).Dealt !=
                    GangCatalog.GangCount)
                failures.Add("Deal: the house count was not held inside the catalogue.");
        }

        /// <summary>
        /// The street registry is the FAMILIES page's source. A four-house city therefore
        /// hands it four entries - ours and three rivals - never the full catalogue with
        /// seventeen empty cards showing zero capos.
        /// </summary>
        static void TheStreetListsOnlyDealtHouses(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed, 4);
            var gangs = GangSeeder.Generate(
                Seed, underworld.Dealt, gang => underworld.Of(gang)?.Roster);

            if (gangs.Length != underworld.Dealt)
                failures.Add($"Registry: a {underworld.Dealt}-house city listed " +
                             $"{gangs.Length} houses.");
            if (gangs.Length - 1 != 3)
                failures.Add($"Ledger: a three-rival city would print {gangs.Length - 1} " +
                             "rival cards.");

            for (var gangId = 1; gangId < gangs.Length; gangId++)
                if (gangs[gangId].Id != gangId || gangs[gangId].Members.Count == 0)
                    failures.Add($"Registry: dealt house {gangId} has no street crew.");
        }

        static void TwentyOneHousesAreDealt(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            if (underworld.Count != GangCatalog.GangCount)
                failures.Add($"Deal: {underworld.Count} houses, not " +
                             $"{GangCatalog.GangCount}.");
            if (underworld.CitySeed != Seed)
                failures.Add("Deal: the underworld forgot the seed it was dealt from.");

            for (var gangId = 0; gangId < GangCatalog.GangCount; gangId++)
            {
                var house = underworld.Of(gangId);
                if (house == null)
                {
                    failures.Add($"Deal: house {gangId} was not dealt.");
                    continue;
                }
                if (house.GangId != gangId || house.Roster == null || house.Runner == null)
                    failures.Add($"Deal: house {gangId} is not a whole house.");
                else if (house.Roster.GangId != gangId)
                    failures.Add($"Deal: house {gangId}'s book says it belongs to " +
                                 $"{house.Roster.GangId}.");
                else if (house.Runner.Accounts.Safe != Accounts.StartingSafe)
                    failures.Add($"Deal: house {gangId} opened on " +
                                 $"{house.Runner.Accounts.Safe}, not the same stake as " +
                                 "everybody else.");
                else if (house.Runner.Accounts.Current == null)
                    failures.Add($"Deal: house {gangId} has no sheet open.");
                else if (house.Extinct)
                    failures.Add($"Deal: house {gangId} was dealt with nobody on it.");
            }

            if (underworld.Player == null || !underworld.Player.IsPlayer)
                failures.Add("Deal: house 0 is not the player's.");
            if (underworld.Of(-1) != null || underworld.Of(GangCatalog.GangCount) != null)
                failures.Add("Deal: a house exists outside the catalog.");
        }

        /// <summary>
        /// The whole of the id rule, measured: characters, crews and equipment are
        /// unique across ALL twenty-one books, by construction. Nothing anywhere has to
        /// decode a family from a number.
        /// </summary>
        static void NoTwoHousesShareANumber(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            var men = new Dictionary<int, int>();
            var crews = new Dictionary<int, int>();
            var stock = new Dictionary<int, int>();

            for (var gangId = 0; gangId < GangCatalog.GangCount; gangId++)
            {
                var roster = underworld.Of(gangId).Roster;
                Claim(failures, men, "character", gangId, roster.Members, m => m.Id);
                Claim(failures, crews, "crew", gangId, roster.Crews, c => c.Id);
                Claim(failures, stock, "item", gangId, roster.Equipment, e => e.Id);
            }
        }

        static void Claim<T>(List<string> failures, Dictionary<int, int> seen, string what,
            int gangId, List<T> items, Func<T, int> idOf)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var id = idOf(items[i]);
                if (seen.TryGetValue(id, out var other))
                    failures.Add($"Ids: {what} {id} is on house {gangId}'s books and on " +
                                 $"house {other}'s.");
                else
                    seen[id] = gangId;
            }
        }

        /// <summary>
        /// Every family stands in a shape the rules allow: a Don, at least one capo,
        /// and every hood in somebody's crew. The player's own house is the stated
        /// exception - a campaign opens on the Don alone, and every name after his is
        /// one the player went out and got.
        /// </summary>
        static void EveryFamilyStandsInAShapeTheRulesAllow(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);

            var player = underworld.Player.Roster;
            if (player.Members.Count != 1 || player.FindBoss() == null)
                failures.Add("Shape: the player's books did not open on the Don alone.");

            for (var gangId = 1; gangId < GangCatalog.GangCount; gangId++)
            {
                var roster = underworld.Of(gangId).Roster;
                var boss = roster.FindBoss();
                if (boss == null || boss.Rank != Rank.Boss)
                    failures.Add($"Shape: house {gangId} has no Don.");
                else if (boss.Surname != GangCatalog.Names[gangId])
                    failures.Add($"Shape: house {gangId}'s Don is a " + boss.Surname + ".");

                var capos = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Rank == Rank.Lieutenant)
                        capos++;
                    else if (member.Rank == Rank.Hood && roster.CrewOf(member.Id) == null)
                        failures.Add($"Shape: house {gangId}'s " + member.FullName +
                                     " stands in no crew.");
                }

                if (capos < 1)
                    failures.Add($"Shape: house {gangId} runs no crew at all.");
                if (boss != null && capos > Command.LieutenantCap(boss))
                    failures.Add($"Shape: house {gangId} holds {capos} capos, past what " +
                                 "its Don can carry.");

                for (var i = 0; i < roster.Crews.Count; i++)
                {
                    var crew = roster.Crews[i];
                    var lieutenant = roster.Find(crew.LieutenantId);
                    if (lieutenant == null)
                        failures.Add($"Shape: house {gangId} has a crew under nobody.");
                    else if (lieutenant.Rank == Rank.Lieutenant &&
                             crew.HoodIds.Count >
                             Command.ManCap(lieutenant, roster.Organization.Limits))
                        failures.Add($"Shape: house {gangId}'s " + lieutenant.FullName +
                                     " holds more men than he can.");
                }
            }
        }

        /// <summary>
        /// THE PLAYER'S CAMPAIGN IS DEALT UNCHANGED. House 0 numbers from zero as it
        /// always has and opens on the same man with the same car - the snapshot was
        /// taken before the houses existed and must still read.
        /// </summary>
        static void ThePlayersOpeningBooksAreUnchanged(List<string> failures)
        {
            var roster = RosterSeeder.Generate(Seed);

            if (roster.GangId != GangCatalog.PlayerGangId)
                failures.Add("Snapshot: the opening books are not house 0's.");
            if (roster.Members.Count != 1 || roster.Crews.Count != 0)
                failures.Add($"Snapshot: {roster.Members.Count} men and " +
                             $"{roster.Crews.Count} crews on day one.");

            var boss = roster.FindBoss();
            if (boss == null || boss.Id != 0 || boss.FullName != GangCatalog.BossName)
                failures.Add("Snapshot: character 0 is not Don Salvatore Ricci.");

            if (roster.Equipment.Count != 1 || roster.Equipment[0].Id != 0 ||
                roster.Equipment[0].DisplayName != "Panel Van")
                failures.Add("Snapshot: the car out back is not the Panel Van, item 0.");

            // The six-man fixture the pure suites measure, by the same numbers.
            var staffed = RosterSeeder.GenerateStaffed(Seed);
            if (staffed.Members.Count != RosterSeeder.FixtureMemberCount ||
                staffed.Crews.Count != 1 || staffed.Crews[0].Id != 0 ||
                staffed.FindBoss().Id != RosterSeeder.FixtureBossCharacterId)
                failures.Add("Snapshot: the staffed fixture was renumbered.");
            if (staffed.Members[0].FullName != "Hank Mazur" ||
                staffed.Crews[0].LieutenantId != 2)
                failures.Add("Snapshot: the staffed fixture was re-dealt.");
        }

        // ----------------------------------------------------------------- the tick

        /// <summary>Every house's wages come out of that house's own safe and nobody
        /// else's - the whole point of a safe per family.</summary>
        static void EachHousePaysItsOwnMenFromItsOwnSafe(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            var before = new int[GangCatalog.GangCount];
            for (var gangId = 0; gangId < GangCatalog.GangCount; gangId++)
                before[gangId] = underworld.Of(gangId).Runner.Accounts.Safe;

            underworld.DayTick();

            for (var gangId = 0; gangId < GangCatalog.GangCount; gangId++)
            {
                var accounts = underworld.Of(gangId).Runner.Accounts;
                if (accounts.Sheets.Count < 2)
                {
                    failures.Add($"Payday: house {gangId} closed no day.");
                    continue;
                }

                var closed = accounts.Sheets[accounts.Sheets.Count - 2];
                var moved = before[gangId] - accounts.Safe;
                if (!closed.Closed)
                    failures.Add($"Payday: house {gangId} left its day open.");
                if (moved != closed.WagesPaid)
                    failures.Add($"Payday: house {gangId}'s safe moved {moved} against " +
                                 $"a wage bill of {closed.WagesPaid} - somebody else's " +
                                 "men were on it.");
                if (gangId > 0 && closed.WagesPaid <= 0)
                    failures.Add($"Payday: house {gangId} has men and paid nobody.");
            }

            // The Don draws nothing, so a house of one man pays nothing - which is the
            // player's opening position and the reason his safe does not move.
            if (underworld.Player.Runner.Accounts.Sheets[0].WagesPaid != 0)
                failures.Add("Payday: the Don drew a wage on day one.");

            // The player is the only house that owes anybody upstairs (D20).
            for (var gangId = 1; gangId < GangCatalog.GangCount; gangId++)
                if (underworld.Of(gangId).Runner.Tribute.Levies.Count != 0)
                    failures.Add($"Tribute: house {gangId} was assessed a levy.");
        }

        /// <summary>An extinct house is skipped by the tick - no wages, no day, no
        /// hours. Its safe is exactly where its last man left it.</summary>
        static void AnExtinctHouseIsSkipped(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            var house = underworld.Of(3);
            for (var i = 0; i < house.Roster.Members.Count; i++)
                RosterOps.Kill(house.Roster, house.Roster.Members[i].Id);

            if (!house.Extinct)
                failures.Add("Extinct: a house with every name struck through is not " +
                             "read as finished.");

            var safe = house.Runner.Accounts.Safe;
            var day = house.Runner.Campaign.Day;
            underworld.DayTick();
            underworld.DayTick();

            if (house.Runner.Accounts.Safe != safe)
                failures.Add("Extinct: a finished house still paid somebody.");
            if (house.Runner.Campaign.Day != day)
                failures.Add("Extinct: a finished house still turned its books.");
            if (underworld.Player.Runner.Campaign.Day == day)
                failures.Add("Extinct: the living houses stopped with the dead one.");
        }

        /// <summary>
        /// A FAMILY WITH NO HEAD IS FINISHED, MEN OR NO MEN (Codex adversarial review,
        /// 2026-09-03). Succession puts the most loyal lieutenant in the dead Don's
        /// chair (RosterOps.StrikeOff); a house with nobody of rank left has nobody to
        /// take it, its own runner latches Fallen and its clock stops - but its men are
        /// still on the books, so it is NOT extinct, and every gate that asked only
        /// about extinction went on letting it think, file orders and work the street
        /// for the rest of the campaign.
        ///
        /// The gate is <see cref="Outfit.House.Finished"/>, and this asserts all four
        /// doors of the underworld are shut against it.
        /// </summary>
        static void AHeadlessHouseIsSkipped(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            var house = underworld.Of(5);

            // A FAMILY DOWN TO ITS DON AND HIS BODYGUARDS. Not culled out of the dealt
            // roster: striking a lieutenant off promotes his most loyal hood into the
            // empty chair, so killing the men of rank one after another simply works
            // through every man in the family and leaves an EXTINCT house - which is
            // the case this contract is not about. The shape is stood up directly.
            var book = Roster.Create(house.GangId);
            var don = new Character
            {
                Id = book.NextCharacterId(), FirstName = "The", Surname = "Don",
                Rank = Rank.Boss, Loyalty = 50,
            };
            book.Members.Add(don);
            book.Organization.BossId = don.Id;
            for (var i = 0; i < 3; i++)
            {
                var guard = new Character
                {
                    Id = book.NextCharacterId(), FirstName = "G", Surname = "uard" + i,
                    Rank = Rank.Hood, Loyalty = 60 + i,
                };
                book.Members.Add(guard);
                book.Organization.BossHoodIds.Add(guard.Id);
            }
            Bodyguards.FallIn(book);
            house.Restock(book);

            if (house.Finished)
                failures.Add("Headless: a family with a living Don reads as finished.");

            RosterOps.Kill(book, book.BossId);

            var standing = 0;
            for (var i = 0; i < book.Members.Count; i++)
                if (!book.Members[i].Gone)
                    standing++;
            if (standing == 0)
                failures.Add("Headless: the fixture killed the whole family - it proves " +
                             "nothing about a house that still has men.");
            if (house.Extinct)
                failures.Add("Headless: a family with men left on its books reads extinct.");
            if (!house.Headless)
                failures.Add("Headless: a family whose Boss is struck off with no " +
                             "successor is not read as headless.");
            if (!house.Finished)
                failures.Add("Headless: a headless family is not finished.");

            // 1. no turn of mind
            var thought = new List<int>();
            underworld.Think(1000.0, 1f, h => thought.Add(h.GangId), maxPerCall: 64);
            if (thought.Contains(house.GangId))
                failures.Add("Headless: a headless family took a turn of mind.");

            // 2. no order into its book
            var before = house.Runner.Book.Jobs.Count;
            var refused = underworld.Issue(new Job
            {
                GangId = house.GangId,
                CrewId = 1,
                Type = OrderType.Guard,
            });
            if (refused.Ok || house.Runner.Book.Jobs.Count != before)
                failures.Add("Headless: a headless family accepted an order.");

            // 3. no midnight
            var safe = house.Runner.Accounts.Safe;
            var day = house.Runner.Campaign.Day;
            underworld.DayTick();
            underworld.DayTick();
            if (house.Runner.Accounts.Safe != safe)
                failures.Add("Headless: a headless family still paid somebody.");
            if (house.Runner.Campaign.Day != day)
                failures.Add("Headless: a headless family still turned its books.");
            if (underworld.Player.Runner.Campaign.Day == day)
                failures.Add("Headless: the living houses stopped with the headless one.");

            // 4. no hours. The runner stamps its house whenever an hour actually
            // moves something (Underworld.AdvanceHours), so the house's own version is
            // the reading: a finished family is never touched again.
            var version = house.Version;
            underworld.AdvanceHours(8f);
            underworld.AdvanceHours(8f);
            if (house.Version != version)
                failures.Add("Headless: time passed for a headless family.");
        }

        // ----------------------------------------------------------------- the street

        /// <summary>
        /// The street's view of a family is that family's own book: every man on it is
        /// a Character with an id, and the flat list still reads "a Lieutenant opens a
        /// crew", which is how the pavement cuts it into knots of men.
        /// </summary>
        static void TheStreetMirrorsEveryBook(List<string> failures)
        {
            var underworld = Underworld.Deal(Seed);
            var gangs = GangSeeder.Generate(
                Seed, underworld.Dealt, gang => underworld.Of(gang)?.Roster);

            if (gangs.Length != GangCatalog.GangCount)
                failures.Add("Mirror: the street does not know twenty-one families.");

            for (var gangId = 0; gangId < gangs.Length; gangId++)
            {
                var gang = gangs[gangId];
                var roster = underworld.Of(gangId).Roster;

                var expected = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Rank != Rank.Boss)
                        expected++;
                if (gang.Members.Count != expected)
                    failures.Add($"Mirror: house {gangId} shows {gang.Members.Count} men " +
                                 $"outside for {expected} on the books.");

                for (var i = 0; i < gang.Members.Count; i++)
                {
                    var street = gang.Members[i];
                    var ledger = roster.Find(street.PersonnelId);
                    if (ledger == null)
                        failures.Add($"Mirror: house {gangId}'s '" + street.FullName +
                                     "' has no Character behind him.");
                    else if (ledger.FullName != street.FullName ||
                             street.Lieutenant != (ledger.Rank == Rank.Lieutenant))
                        failures.Add($"Mirror: house {gangId}'s '" + street.FullName +
                                     "' disagrees with his own file.");
                }

                if (gangId > 0 && (gang.Members.Count == 0 || !gang.Members[0].Lieutenant))
                    failures.Add($"Mirror: house {gangId} does not open on a capo.");
            }
        }

        // ------------------------------------------------------------------ the deal

        /// <summary>One city, one deal. A second call naming the same seed hands back
        /// the same books; a different seed is refused, not obeyed.</summary>
        static void OneDealPerCity(List<string> failures)
        {
            var faults = new List<string>();
            var wasCurrent = Underworld.Current;
            var wasFault = Underworld.Fault;
            Underworld.Fault = faults.Add;
            Underworld.ResetForPlay();
            try
            {
                var first = Underworld.Ensure(Seed);
                if (!ReferenceEquals(Underworld.Ensure(Seed), first))
                    failures.Add("Ensure: the second call dealt a second underworld.");
                if (faults.Count != 0)
                    failures.Add("Ensure: the same seed twice was read as a fault.");

                if (!ReferenceEquals(Underworld.Ensure(Seed + 1), first))
                    failures.Add("Ensure: a different seed re-dealt the city under it.");
                if (faults.Count != 1)
                    failures.Add("Ensure: a second seed passed without a word.");
            }
            finally
            {
                // Put the running Play back exactly as it was - the directors are
                // holding these houses.
                Underworld.Fault = wasFault;
                Underworld.Restore(wasCurrent);
            }
        }
    }
}
