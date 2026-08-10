using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// The gang layer's model: seeding determinism, crew shapes, the player-roster
    /// mirror, the front-selection geometry, and the popup budgets. GateTests'
    /// discipline: a plain static class, failures returned as data, no UnityEngine -
    /// the whole Gangs core is engine-free on purpose.
    /// </summary>
    public static class GangTests
    {
        const int TitleBudget = 35;
        const int LineBudget = 44;

        public static List<string> Run()
        {
            var failures = new List<string>();

            SameSeedSameGangs(failures);
            DifferentSeedsDiffer(failures);
            CrewShapesHold(failures);
            NoDuplicateNamesWithinGang(failures);
            NamesComeFromTheSharedTables(failures);
            PlayerGangMirrorsRoster(failures);
            EmptyRosterLeavesPlayerGangEmpty(failures);
            FrontsAreDistinct(failures);
            FrontsPreferDistinctBlocks(failures);
            FrontFallbackWhenFewCandidates(failures);
            FrontSelectionIsDeterministic(failures);
            FrontTieBreaksToLowestIndex(failures);
            CatalogTablesAligned(failures);
            IntentionFitsTheBudgets(failures);

            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        static Gang[] Deal(int seed) =>
            GangSeeder.Generate(seed, RosterSeeder.Generate(seed));

        static List<GangFronts.FrontCandidate> Grid(params (int block, float x, float z)[] spots)
        {
            var candidates = new List<GangFronts.FrontCandidate>();
            foreach (var (block, x, z) in spots)
                candidates.Add(new GangFronts.FrontCandidate(block, x, z));
            return candidates;
        }

        // ------------------------------------------------------------------ seeding

        static void SameSeedSameGangs(List<string> failures)
        {
            var a = Deal(1234);
            var b = Deal(1234);

            if (a.Length != b.Length)
            {
                failures.Add("Seeder: same seed, different gang counts.");
                return;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].FrontRoll != b[i].FrontRoll || a[i].MemberSeed != b[i].MemberSeed)
                    failures.Add($"Seeder: same seed, gang {i} rolls differ.");
                if (a[i].Members.Count != b[i].Members.Count)
                {
                    failures.Add($"Seeder: same seed, gang {i} crew sizes differ.");
                    continue;
                }
                for (var m = 0; m < a[i].Members.Count; m++)
                    if (a[i].Members[m].FullName != b[i].Members[m].FullName ||
                        a[i].Members[m].Lieutenant != b[i].Members[m].Lieutenant)
                        failures.Add($"Seeder: same seed, gang {i} member {m} differs.");
            }
        }

        static void DifferentSeedsDiffer(List<string> failures)
        {
            var a = Deal(1);
            var b = Deal(2);

            var same = a[0].FrontRoll == b[0].FrontRoll;
            for (var i = 0; i < a.Length && same; i++)
                same = a[i].MemberSeed == b[i].MemberSeed;

            if (same)
                failures.Add("Seeder: seeds 1 and 2 dealt identical rolls - the seed is " +
                             "not reaching the stream.");
        }

        static void CrewShapesHold(List<string> failures)
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var gangs = Deal(seed);
                if (gangs.Length != GangCatalog.GangCount)
                    failures.Add($"Seeder: seed {seed} dealt {gangs.Length} gangs.");

                foreach (var gang in gangs)
                {
                    if (gang.IsPlayer != (gang.Id == GangCatalog.PlayerGangId))
                        failures.Add($"Seeder: gang {gang.Id} IsPlayer is wrong.");
                    if (gang.Members.Count == 0)
                        continue;

                    var lieutenants = 0;
                    foreach (var member in gang.Members)
                        if (member.Lieutenant)
                            lieutenants++;

                    if (lieutenants != 1)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} has " +
                                     $"{lieutenants} lieutenants.");
                    if (!gang.Members[0].Lieutenant)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} does not lead " +
                                     "with its lieutenant.");

                    if (gang.IsPlayer)
                        continue;

                    var soldiers = gang.Members.Count - 1;
                    if (soldiers < GangSeeder.MinSoldiers || soldiers > GangSeeder.MaxSoldiers)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} has {soldiers} " +
                                     "soldiers, outside 2-3.");
                }
            }
        }

        static void NoDuplicateNamesWithinGang(List<string> failures)
        {
            for (var seed = 0; seed < 40; seed++)
                foreach (var gang in Deal(seed))
                {
                    var seen = new HashSet<string>();
                    foreach (var member in gang.Members)
                        if (!seen.Add(member.FullName))
                            failures.Add($"Seeder: seed {seed} gang {gang.Id} rolled " +
                                         $"'{member.FullName}' twice.");
                }
        }

        static void NamesComeFromTheSharedTables(List<string> failures)
        {
            var firsts = new HashSet<string>(PedestrianIdentity.AllMaleNames);
            var surnames = new HashSet<string>(PedestrianIdentity.AllSurnames);

            foreach (var gang in GangSeeder.Generate(7, null))
                foreach (var member in gang.Members)
                {
                    if (!firsts.Contains(member.FirstName))
                        failures.Add($"Seeder: first name '{member.FirstName}' is not in " +
                                     "the shared table.");
                    if (!surnames.Contains(member.Surname))
                        failures.Add($"Seeder: surname '{member.Surname}' is not in the " +
                                     "shared table.");
                }
        }

        // ------------------------------------------------------------------ the mirror

        static void PlayerGangMirrorsRoster(List<string> failures)
        {
            var roster = RosterSeeder.Generate(99);
            var player = GangSeeder.Generate(99, roster)[GangCatalog.PlayerGangId];

            if (player.Members.Count != roster.Members.Count)
            {
                failures.Add($"Mirror: {player.Members.Count} street members for " +
                             $"{roster.Members.Count} roster members.");
                return;
            }

            foreach (var member in player.Members)
            {
                var ledger = roster.Find(member.PersonnelId);
                if (ledger == null)
                {
                    failures.Add($"Mirror: '{member.FullName}' has no ledger Character.");
                    continue;
                }
                if (ledger.FullName != member.FullName)
                    failures.Add($"Mirror: '{member.FullName}' vs ledger " +
                                 $"'{ledger.FullName}'.");
                if (member.Lieutenant != (ledger.Rank == Rank.Lieutenant))
                    failures.Add($"Mirror: '{member.FullName}' lieutenant flag disagrees " +
                                 "with the ledger rank.");
            }

            if (player.Members.Count > 0 && !player.Members[0].Lieutenant)
                failures.Add("Mirror: the roster's lieutenant is not first in line.");
        }

        static void EmptyRosterLeavesPlayerGangEmpty(List<string> failures)
        {
            var gangs = GangSeeder.Generate(5, null);
            if (gangs[GangCatalog.PlayerGangId].Members.Count != 0)
                failures.Add("Mirror: a null roster still produced player members.");

            // The AI crews must be unaffected by the roster's absence - their draws
            // precede the mirror entirely.
            var with = GangSeeder.Generate(5, RosterSeeder.Generate(5));
            for (var i = 1; i < gangs.Length; i++)
                if (gangs[i].Members.Count != with[i].Members.Count)
                    failures.Add($"Mirror: the roster's presence changed AI gang {i}.");
        }

        // ------------------------------------------------------------------ fronts

        static void FrontsAreDistinct(List<string> failures)
        {
            var candidates = Grid((0, 0, 0), (1, 50, 0), (2, 0, 50), (3, 50, 50), (4, 25, 25),
                                  (5, 80, 10), (6, 10, 80));

            for (var roll = 0; roll < 20; roll++)
            {
                var picks = GangFronts.Select(candidates, roll, GangCatalog.GangCount);
                var seen = new HashSet<int>();
                foreach (var pick in picks)
                {
                    if (pick < 0)
                        failures.Add($"Fronts: roll {roll} left a gang without a front " +
                                     "despite spare candidates.");
                    else if (!seen.Add(pick))
                        failures.Add($"Fronts: roll {roll} seated two gangs on candidate " +
                                     $"{pick}.");
                }
            }
        }

        static void FrontsPreferDistinctBlocks(List<string> failures)
        {
            // Six candidates on three blocks - five gangs CAN cover more than three
            // spots, but the first pass must exhaust distinct blocks before doubling up.
            var candidates = Grid((0, 0, 0), (0, 5, 0), (1, 100, 0), (1, 105, 0),
                                  (2, 0, 100), (2, 5, 100));

            var picks = GangFronts.Select(candidates, 0, 4);
            var blocks = new HashSet<int>();
            for (var i = 0; i < 3; i++)
                blocks.Add(candidates[picks[i]].BlockId);

            if (blocks.Count != 3)
                failures.Add("Fronts: the first three picks did not cover the three " +
                             "distinct blocks.");
        }

        static void FrontFallbackWhenFewCandidates(List<string> failures)
        {
            var picks = GangFronts.Select(
                Grid((0, 0, 0), (1, 10, 0), (2, 20, 0)), 1, GangCatalog.GangCount);

            var seated = 0;
            foreach (var pick in picks)
                if (pick >= 0)
                    seated++;

            if (seated != 3)
                failures.Add($"Fronts: 3 candidates seated {seated} gangs.");
            if (picks[3] != -1 || picks[4] != -1)
                failures.Add("Fronts: the late gangs did not fall back to -1.");

            foreach (var pick in GangFronts.Select(
                         Grid(), 7, GangCatalog.GangCount))
                if (pick != -1)
                    failures.Add("Fronts: an empty city seated a gang.");
        }

        static void FrontSelectionIsDeterministic(List<string> failures)
        {
            var candidates = Grid((0, 0, 0), (1, 30, 10), (2, 60, 60), (3, 5, 70), (4, 90, 20));
            var a = GangFronts.Select(candidates, 12345, GangCatalog.GangCount);
            var b = GangFronts.Select(candidates, 12345, GangCatalog.GangCount);

            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    failures.Add($"Fronts: two identical calls disagreed at gang {i}.");
        }

        static void FrontTieBreaksToLowestIndex(List<string> failures)
        {
            // Two candidates equidistant from the player's seat at index 0: the tie must
            // go to the lower index, every time.
            var candidates = Grid((0, 0, 0), (1, 10, 0), (2, -10, 0));
            var picks = GangFronts.Select(candidates, 0, 2);
            if (picks[1] != 1)
                failures.Add($"Fronts: the tie went to candidate {picks[1]}, not the " +
                             "lowest index.");
        }

        // ------------------------------------------------------------------ words

        static void CatalogTablesAligned(List<string> failures)
        {
            if (GangCatalog.Names.Length != GangCatalog.GangCount ||
                GangCatalog.SoldierModels.Length != GangCatalog.GangCount ||
                GangCatalog.LieutenantModels.Length != GangCatalog.GangCount)
                failures.Add("Catalog: the name/model tables are not all GangCount long.");

            for (var i = 0; i < GangCatalog.GangCount; i++)
            {
                if (string.IsNullOrEmpty(GangCatalog.Names[i]))
                    failures.Add($"Catalog: gang {i} has no name.");
                if (GangCatalog.SoldierModels[i] == GangCatalog.LieutenantModels[i])
                    failures.Add($"Catalog: gang {i}'s lieutenant wears the soldiers' " +
                                 "model - rank would not read.");
            }
        }

        static void IntentionFitsTheBudgets(List<string> failures)
        {
            // Both gender tables, though the roster deals male names today: the mirror
            // reads whatever Personnel writes, and the budget must not depend on that.
            var widestFirst = "";
            foreach (var table in new[]
                     {
                         PedestrianIdentity.AllMaleNames,
                         PedestrianIdentity.AllFemaleNames,
                     })
                foreach (var first in table)
                    if (first.Length > widestFirst.Length)
                        widestFirst = first;

            var widestSurname = "";
            foreach (var surname in PedestrianIdentity.AllSurnames)
                if (surname.Length > widestSurname.Length)
                    widestSurname = surname;

            var title = UI.GangIntention.Title(widestFirst, widestSurname, lieutenant: true);
            if (title.Length > TitleBudget)
                failures.Add($"Intention: widest title is {title.Length} chars " +
                             $"('{title}') over the {TitleBudget} budget.");

            foreach (var gang in GangCatalog.Names)
            {
                var line = UI.GangIntention.Line(gang, isPlayer: false);
                if (line.Length > LineBudget)
                    failures.Add($"Intention: line '{line}' is {line.Length} chars, over " +
                                 $"the {LineBudget} budget.");
            }

            if (UI.GangIntention.Line("ignored", isPlayer: true).Length > LineBudget)
                failures.Add("Intention: the player line is over budget.");
        }
    }
}
