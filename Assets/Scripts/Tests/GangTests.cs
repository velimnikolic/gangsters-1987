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
            EveryFamilyIsARoster(failures);
            FrontsAreDistinct(failures);
            FrontsPreferDistinctBlocks(failures);
            FrontFallbackWhenFewCandidates(failures);
            FrontSelectionIsDeterministic(failures);
            FrontTieBreaksToLowestIndex(failures);
            FrontBooksAreCompleteAndStable(failures);
            CatalogTablesAligned(failures);
            CatalogWearsApprovedBodies(failures);
            NoTwinsInOneCrew(failures);
            RivalCrewsAreNotTwins(failures);
            GangBodiesAreNotCrowdBodies(failures);
            MarkedCarsAreNotCivilianTraffic(failures);
            CostumesAndAnachronismsStayOffTheStreet(failures);
            NoPolicemanKeepsAShop(failures);
            IntentionFitsTheBudgets(failures);

            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>The city's families, mirrored off the books they actually keep.
        /// The player's house opens on the Don alone, so the fixture stands the staffed
        /// six under him - that is what a campaign a few weeks old looks like, and it is
        /// what the mirror contracts below are written against.</summary>
        static Gang[] Deal(int seed) => GangSeeder.Generate(seed, gang =>
            gang == GangCatalog.PlayerGangId
                ? RosterSeeder.GenerateStaffed(seed)
                : Family(seed, gang));

        /// <summary>One rival house's opening book, laid out the way the underworld
        /// lays it out: the canonical limits, the Don's detail standing, arms dealt.
        /// </summary>
        static Roster Family(int seed, int gangId)
        {
            var roster = RosterSeeder.Generate(seed, gangId);
            RosterOps.ConfigureOrganization(roster, OrganizationLimits.Default);
            Bodyguards.FallIn(roster);
            RosterOps.NormalizeArms(roster);
            return roster;
        }

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

                    // A family is one or more CREWS: a Lieutenant entry opens one and
                    // the soldiers behind him are his, until the next opens the next.
                    var lieutenants = 0;
                    foreach (var member in gang.Members)
                        if (member.Lieutenant)
                            lieutenants++;

                    if (lieutenants == 0)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} has no " +
                                     "lieutenant at all.");
                    if (!gang.Members[0].Lieutenant)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} does not lead " +
                                     "with its lieutenant - the door slot at the front is " +
                                     "his.");

                    if (gang.IsPlayer)
                        continue;

                    if (lieutenants < GangSeeder.MinLieutenants ||
                        lieutenants > GangSeeder.MaxLieutenants)
                        failures.Add($"Seeder: seed {seed} gang {gang.Id} runs " +
                                     $"{lieutenants} crews, outside " +
                                     $"{GangSeeder.MinLieutenants}-" +
                                     $"{GangSeeder.MaxLieutenants}.");

                    // every crew of it, counted on its own
                    var soldiers = -1;
                    foreach (var member in gang.Members)
                    {
                        if (member.Lieutenant)
                        {
                            ReportCrew(failures, seed, gang.Id, soldiers);
                            soldiers = 0;
                            continue;
                        }

                        soldiers++;
                    }

                    ReportCrew(failures, seed, gang.Id, soldiers);
                }
            }
        }

        /// <summary>One crew's soldier count against the seeder's own range; -1 is
        /// "no crew open yet" and is not a crew.</summary>
        static void ReportCrew(List<string> failures, int seed, int gangId, int soldiers)
        {
            if (soldiers < 0)
                return;
            if (soldiers < GangSeeder.MinSoldiers || soldiers > GangSeeder.MaxSoldiers)
                failures.Add($"Seeder: seed {seed} gang {gangId} has a crew of " +
                             $"{soldiers} soldiers, outside {GangSeeder.MinSoldiers}-" +
                             $"{GangSeeder.MaxSoldiers}.");
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

            foreach (var gang in Deal(7))
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
            var roster = RosterSeeder.GenerateStaffed(99);
            var player = GangSeeder.Generate(99,
                gang => gang == GangCatalog.PlayerGangId ? roster : null)
                [GangCatalog.PlayerGangId];

            if (player.Members.Count != roster.Members.Count - 1)
            {
                failures.Add($"Mirror: {player.Members.Count} street members for " +
                             $"{roster.Members.Count - 1} non-Boss roster members.");
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

        /// <summary>A house whose books the caller does not hand over stands installed
        /// with nobody outside - the FAMILIES page reads the registry, not the pavement -
        /// and one house's absence never moves another's.</summary>
        static void EmptyRosterLeavesPlayerGangEmpty(List<string> failures)
        {
            var gangs = GangSeeder.Generate(5,
                gang => gang == GangCatalog.PlayerGangId ? null : Family(5, gang));
            if (gangs[GangCatalog.PlayerGangId].Members.Count != 0)
                failures.Add("Mirror: a null roster still produced player members.");

            var with = Deal(5);
            for (var i = 1; i < gangs.Length; i++)
                if (gangs[i].Members.Count != with[i].Members.Count)
                    failures.Add($"Mirror: the player's absence changed AI gang {i}.");

            // And the front picks are the same either way: they are drawn before any
            // book is read.
            if (gangs[GangCatalog.PlayerGangId].FrontRoll !=
                with[GangCatalog.PlayerGangId].FrontRoll)
                failures.Add("Mirror: the front pick moved with the roster.");
        }

        /// <summary>
        /// EVERY house is a house. A rival family's street members are Characters on
        /// that family's own roster - the same books the player keeps - and no two
        /// families share a man.
        /// </summary>
        static void EveryFamilyIsARoster(List<string> failures)
        {
            var seen = new Dictionary<int, int>();
            for (var gangId = 1; gangId < GangCatalog.GangCount; gangId++)
            {
                var roster = Family(1987, gangId);
                var boss = roster.FindBoss();
                if (boss == null || boss.Surname != GangCatalog.Names[gangId])
                    failures.Add($"House {gangId}: the name over the door is not the " +
                                 "Don's own.");
                if (roster.Crews.Count == 0)
                    failures.Add($"House {gangId}: a family with no crew at all.");

                var lieutenants = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Rank == Rank.Lieutenant)
                        lieutenants++;
                    if (seen.TryGetValue(member.Id, out var other))
                        failures.Add($"House {gangId}: character {member.Id} is also on " +
                                     $"house {other}'s books.");
                    else
                        seen[member.Id] = gangId;
                    if (member.Rank == Rank.Hood && roster.CrewOf(member.Id) == null)
                        failures.Add($"House {gangId}: '{member.FullName}' stands in no " +
                                     "crew.");
                }

                if (lieutenants < GangSeeder.MinLieutenants ||
                    lieutenants > GangSeeder.MaxLieutenants)
                    failures.Add($"House {gangId}: {lieutenants} capos, outside " +
                                 $"{GangSeeder.MinLieutenants}-{GangSeeder.MaxLieutenants}.");
            }
        }

        // ------------------------------------------------------------------ fronts

        static void FrontsAreDistinct(List<string> failures)
        {
            // A door to spare for every family and two over - the check is that nobody
            // is left standing and nobody shares, so the fixture has to keep pace with
            // the catalog rather than being a hand-typed handful.
            var spots = new List<(int, float, float)>();
            for (var i = 0; i < GangCatalog.GangCount + 2; i++)
                spots.Add((i, (i % 5) * 50f, (i / 5) * 50f));
            var candidates = Grid(spots.ToArray());

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
            for (var i = 3; i < picks.Length; i++)
                if (picks[i] != -1)
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

        // ------------------------------------------------------------------ the front

        /// <summary>Every family's premises has both sets of books, they say the same
        /// thing every time they are opened, and the back room earns more than the
        /// counter - which is the only reason a mob holds the lease on a laundry.</summary>
        static void FrontBooksAreCompleteAndStable(List<string> failures)
        {
            for (var seed = 0; seed < 20; seed++)
                foreach (var gang in Deal(seed))
                {
                    var capo = gang.Members.Count > 0 ? gang.Members[0].FullName : "";
                    var books = FrontBooks.Open(gang.Name, capo, 4, gang.MemberSeed);
                    var again = FrontBooks.Open(gang.Name, capo, 4, gang.MemberSeed);

                    if (books.Sign != again.Sign || books.Licence != again.Licence ||
                        books.Racket != again.Racket || books.Skim != again.Skim ||
                        books.Proprietor != again.Proprietor)
                        failures.Add($"Front: seed {seed} gang {gang.Id} reads " +
                                     "differently the second time it is opened.");

                    foreach (var (what, line) in new[]
                             {
                                 ("sign", books.Sign), ("trade", books.Trade),
                                 ("proprietor", books.Proprietor), ("hours", books.Hours),
                                 ("licence", books.Licence), ("clean", books.Clean),
                                 ("racket", books.Racket), ("racket note", books.RacketNote),
                                 ("heat", books.Heat), ("whisper", books.Whisper),
                             })
                        if (string.IsNullOrEmpty(line))
                            failures.Add($"Front: seed {seed} gang {gang.Id} has no {what}.");

                    if (books.Skim <= books.Takings)
                        failures.Add($"Front: seed {seed} gang {gang.Id} skims " +
                                     $"{books.Skim} on takings of {books.Takings} - the " +
                                     "back room has to be worth the shop.");
                    if (books.Since >= 1987 || books.Since < 1900)
                        failures.Add($"Front: seed {seed} gang {gang.Id} has been trading " +
                                     $"since {books.Since}.");
                    if (books.Cut < 40 || books.Cut > 70)
                        failures.Add($"Front: seed {seed} gang {gang.Id} sends " +
                                     $"{books.Cut}% upstairs.");
                    if (books.Staff < 2)
                        failures.Add($"Front: seed {seed} gang {gang.Id} has {books.Staff} " +
                                     "on the payroll - a shop with nobody in it is not a front.");
                    if (capo.Length > 0 && books.RunBy != capo)
                        failures.Add($"Front: seed {seed} gang {gang.Id} names " +
                                     $"'{books.RunBy}' where its capo stands.");

                    // the proprietor is a citizen, not the family and not the capo
                    if (books.Proprietor == capo)
                        failures.Add($"Front: seed {seed} gang {gang.Id} put its own capo " +
                                     "on the licence.");
                }
        }

        // ------------------------------------------------------------------ words

        static void CatalogTablesAligned(List<string> failures)
        {
            if (GangCatalog.Names.Length != GangCatalog.GangCount ||
                GangCatalog.SoldierModels.Length != GangCatalog.GangCount ||
                GangCatalog.LieutenantModels.Length != GangCatalog.GangCount)
                failures.Add("Catalog: the name/model tables are not all GangCount long.");

            if (UI.GangPalette.Count < GangCatalog.GangCount)
                failures.Add($"Catalog: {UI.GangPalette.Count} colours for " +
                             $"{GangCatalog.GangCount} families - the last of them would " +
                             "show up on the map in the unknown-gang grey.");

            var seen = new HashSet<string>();
            for (var i = 0; i < GangCatalog.GangCount; i++)
            {
                if (string.IsNullOrEmpty(GangCatalog.Names[i]))
                    failures.Add($"Catalog: gang {i} has no name.");
                else if (!seen.Add(GangCatalog.Names[i]))
                    failures.Add($"Catalog: '{GangCatalog.Names[i]}' names two families - " +
                                 "the popup line and the map legend would read as one.");

                // two families may share a coat; no two may share the PAIR, or the
                // street has no way left to tell them apart but the name over the head
                for (var other = 0; other < i; other++)
                    if (GangCatalog.SoldierModels[i] == GangCatalog.SoldierModels[other] &&
                        GangCatalog.LieutenantModels[i] ==
                            GangCatalog.LieutenantModels[other])
                        failures.Add($"Catalog: gangs {other} and {i} are dealt the same " +
                                     "two bodies.");
                if (GangCatalog.SoldierModels[i] == GangCatalog.LieutenantModels[i])
                    failures.Add($"Catalog: gang {i}'s lieutenant wears the soldiers' " +
                                 "model - rank would not read.");
            }
        }

        // ------------------------------------------------------------------ the cast

        /// <summary>Every model a mob is dealt has to be on the approved stock. The two
        /// tables in GangLooks are the whole decision about who may be a gangster; a
        /// body named anywhere else is one the picking board never passed.</summary>
        static void CatalogWearsApprovedBodies(List<string> failures)
        {
            var hoods = new HashSet<string>(GangLooks.Hoods);
            var capos = new HashSet<string>(GangLooks.Lieutenants);

            for (var i = 0; i < GangCatalog.GangCount; i++)
            {
                var soldier = GangLooks.Bare(GangCatalog.SoldierModels[i]);
                if (!hoods.Contains(soldier))
                    failures.Add($"Cast: gang {i}'s soldier '{soldier}' is not approved muscle.");

                var capo = GangLooks.Bare(GangCatalog.LieutenantModels[i]);
                if (!capos.Contains(capo))
                    failures.Add($"Cast: gang {i}'s lieutenant '{capo}' is not an approved capo.");
            }

            if (GangLooks.Hoods.Length < Personnel.Crew.MaxTacticalHoods + 1)
                failures.Add("Cast: fewer hood bodies than a full crew needs - a crew " +
                             "would have to repeat one.");
        }

        /// <summary>No crew of the outfit is one man standing five times: lieutenant and
        /// hoods all wear different bodies, at every roster size the seeder deals.</summary>
        static void NoTwinsInOneCrew(List<string> failures)
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var roster = RosterSeeder.GenerateStaffed(seed);
                foreach (var crew in roster.Crews)
                {
                    var worn = new HashSet<string>();
                    foreach (var id in Members(roster, crew))
                    {
                        var member = roster.Find(id);
                        if (member == null) continue;

                        var look = GangLooks.LookFor(member, roster);
                        if (look.Length == 0)
                            failures.Add($"Cast: seed {seed} crew {crew.Id} left " +
                                         $"{member.FullName} with no body.");
                        else if (!worn.Add(look))
                            failures.Add($"Cast: seed {seed} crew {crew.Id} has two men " +
                                         $"in '{look}'.");

                        if (PedestrianIdentity.IsFemale(look) != GangLooks.IsFemale(member))
                            failures.Add($"Cast: seed {seed} put {member.FullName} in " +
                                         $"'{look}' - the wrong sex's body.");
                    }
                }
            }
        }

        /// <summary>And no rival mob is either - the hoods it is dealt are all different
        /// and none of them is the body its lieutenant is standing in.</summary>
        static void RivalCrewsAreNotTwins(List<string> failures)
        {
            for (var gang = 0; gang < GangCatalog.GangCount; gang++)
            {
                var capo = GangCatalog.LieutenantModels[gang];
                var looks = GangLooks.HoodsFor(capo, GangCatalog.SoldierModels[gang],
                                               Personnel.Crew.MaxTacticalHoods);

                if (looks.Count != Personnel.Crew.MaxTacticalHoods)
                    failures.Add($"Cast: gang {gang} was dealt {looks.Count} hood bodies " +
                                 $"for {Personnel.Crew.MaxTacticalHoods} men.");

                var worn = new HashSet<string> { GangLooks.Bare(capo) };
                foreach (var look in looks)
                {
                    if (!worn.Add(look))
                        failures.Add($"Cast: gang {gang} has two men in '{look}'.");
                    // rival hoods are dealt male names by both builders
                    if (PedestrianIdentity.IsFemale(look))
                        failures.Add($"Cast: gang {gang}'s male hood was dealt '{look}'.");
                }
            }
        }

        /// <summary>The rule the crowd pools read: every body either table may deal is
        /// spoken for, and nothing else is. RoadDemoBuilder and CrewDemoBuilder scan the
        /// packs wholesale and drop what this answers true for, so a coat that stands on
        /// a corner as one of Falcone's men never also walks past as a nobody - and the
        /// bodies the crowd lives on (the city stock, the surfers, the families) must
        /// still be free, or the pavement empties out with the tables.</summary>
        static void GangBodiesAreNotCrowdBodies(List<string> failures)
        {
            foreach (var table in new[] { GangLooks.Hoods, GangLooks.Lieutenants })
                foreach (var look in table)
                {
                    if (!GangLooks.IsGangBody(look))
                        failures.Add($"Cast: '{look}' is dealt to gangsters but the crowd " +
                                     "pools would still put it on a passer-by.");
                    // GangCatalog still names its men the crowd's old way
                    if (!GangLooks.IsGangBody(look + "_AI"))
                        failures.Add($"Cast: '{look}_AI' slipped the crowd filter - a " +
                                     "suffix must not make one body look like two.");
                }

            // a handful of the bodies the pavement is built out of: none of them may be
            // claimed by the mob, or the crowd loses a face it cannot spare
            string[] civilians =
            {
                "SM_Chr_City_Male_01", "SM_Chr_City_Female_01", "SM_Chr_Rich_Male_01",
                "SM_Chr_Surfer_Male_01", "SM_Gen_Chr_Street_Male_02",
                "SM_Gen_Chr_Jumpsuit_Male_01", "Character_Male_Jacket",
                "SM_Chr_Officer_Male_01",
            };
            foreach (var look in civilians)
                if (GangLooks.IsGangBody(look))
                    failures.Add($"Cast: '{look}' is a civilian body the crowd pools " +
                                 "need, but a table claims it for the mob.");

            if (GangLooks.IsGangBody(null) || GangLooks.IsGangBody(""))
                failures.Add("Cast: the crowd filter claims a body with no name.");

            if (GangLooks.IsGangBody(GangLooks.RetiredKingpin) ||
                GangLooks.IsGangBody(GangLooks.RetiredKingpin + "_AI"))
                failures.Add("Cast: the retired kingpin can still be dealt to a gang.");

            if (!Entities.CrowdLooks.IsBarred(GangLooks.RetiredKingpin))
                failures.Add("Cast: the retired kingpin can return as a passer-by.");
        }

        /// <summary>The other half of the same rule, for cars: what the law drives is
        /// never ordinary traffic. The police pack's names give nothing away - its
        /// cruisers are "SM_Veh_Car_01" and its van "SM_Veh_Van_01" - so the scans ask
        /// with the asset PATH and the folder answers. A caller asking for a marked car
        /// BY NAME still gets it: this is not a bar, it is a filter on the crowd.
        /// </summary>
        static void MarkedCarsAreNotCivilianTraffic(List<string> failures)
        {
            const string fleet = Gameplay.VehicleCatalog.PoliceFleetFolder;
            string[] pack =
            {
                "SM_Veh_Car_01", "SM_Veh_Car_02", "SM_Veh_Van_01", "SM_Veh_Pickup_01",
                "SM_Veh_Motorbike_01", "SM_Veh_Bike_01", "SM_Veh_Helicopter_01",
            };
            foreach (var name in pack)
            {
                if (!Gameplay.VehicleCatalog.IsPoliceVehicle(fleet + name + ".prefab"))
                    failures.Add($"Fleet: '{name}' out of the police pack would drive " +
                                 "as civilian traffic.");
                // a windows scan hands back backslashes
                if (!Gameplay.VehicleCatalog.IsPoliceVehicle(
                        (fleet + name + ".prefab").Replace('/', '\\')))
                    failures.Add($"Fleet: '{name}' slips the filter when the path comes " +
                                 "back with backslashes.");
            }

            // the marked bodies that live outside that pack, by bare name
            foreach (var name in Gameplay.VehicleCatalog.PoliceCars)
                if (!Gameplay.VehicleCatalog.IsPoliceVehicle(name))
                    failures.Add($"Fleet: the patrol car '{name}' is not held off " +
                                 "civilian traffic.");
            if (!Gameplay.VehicleCatalog.IsPoliceVehicle("SM_Veh_Car_Police_01"))
                failures.Add("Fleet: the city pack's cruiser is not held off civilian " +
                             "traffic.");

            // the other services: on a call, so never idling in traffic either. Not the
            // law, so the police half must NOT claim them - the two questions are asked
            // separately and a station forecourt is not an ambulance bay.
            string[] service = { "SM_Veh_Car_Ambo_01", "SM_Veh_Pickup_01_Preset_Coastguard" };
            foreach (var name in service)
            {
                if (!Gameplay.VehicleCatalog.IsMarkedService(name))
                    failures.Add($"Fleet: '{name}' would drive as civilian traffic.");
                if (Gameplay.VehicleCatalog.IsPoliceVehicle(name))
                    failures.Add($"Fleet: '{name}' is not the law's, but the police " +
                                 "filter claims it.");
            }

            // and the cars the city actually runs are untouched. The liveried ones here
            // are the point: a taxi, a caterer and a builder are all doing their job by
            // being in the traffic, which is what tells them apart from a call.
            string[] civilian =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Taxi.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01_Preset_Taxi.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Food.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01_Preset_Construction.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Sedan_01.prefab",
                "SM_Veh_Suv_01", "SM_Veh_Van_01",
            };
            foreach (var path in civilian)
                if (Gameplay.VehicleCatalog.IsMarkedService(path))
                    failures.Add($"Fleet: '{path}' is a civilian car but the filter " +
                                 "holds it off the street.");

            if (Gameplay.VehicleCatalog.IsPoliceVehicle(null) ||
                Gameplay.VehicleCatalog.IsPoliceVehicle("") ||
                Gameplay.VehicleCatalog.IsMarkedService(null) ||
                Gameplay.VehicleCatalog.IsMarkedService(""))
                failures.Add("Fleet: the filter claims a car with no name.");
        }

        /// <summary>What a folder scan drags in behind the people and the cars: a man in
        /// a prison jumpsuit, a technician in a scene suit, a survey car with a laser rig
        /// on the roof. Every one of them is somebody somewhere - so the crowd table is a
        /// filter and the vehicle table a bar, and the difference is whether the body has
        /// a right place in this game at all.</summary>
        static void CostumesAndAnachronismsStayOffTheStreet(List<string> failures)
        {
            foreach (var look in Entities.CrowdLooks.Barred)
            {
                if (!Entities.CrowdLooks.IsBarred(look))
                    failures.Add($"Crowd: '{look}' is on the barred table and still walks.");
                if (!Entities.CrowdLooks.IsBarred(look + "_AI"))
                    failures.Add($"Crowd: '{look}_AI' slips the filter - a suffix must " +
                                 "not make one body look like two.");
                if (!Entities.CrowdLooks.IsBarred(
                        "Assets/Synty/PolygonGeneric/Prefabs/Characters/" + look + ".prefab"))
                    failures.Add($"Crowd: '{look}' slips the filter when asked by path.");
            }

            // the people the pavement is actually made of stay on it
            string[] passersBy =
            {
                "SM_Chr_City_Male_01", "SM_Chr_Rich_Female_01", "SM_Chr_Surfer_Male_01",
                "SM_Gen_Chr_Street_Male_02", "SM_Gen_Chr_Jumpsuit_Male_01",
                "Character_Male_Jacket", "SM_Chr_Detective_Male_01",
            };
            foreach (var look in passersBy)
                if (Entities.CrowdLooks.IsBarred(look))
                    failures.Add($"Crowd: '{look}' is an ordinary passer-by but the " +
                                 "filter keeps it off the street.");

            if (Entities.CrowdLooks.IsBarred(null) || Entities.CrowdLooks.IsBarred(""))
                failures.Add("Crowd: the filter claims a body with no name.");

            // and the cars that were not on a 1987 street reach no scene at all
            string[] anachronisms =
            {
                "SM_Veh_Sedan_01_Preset_Lidar", "SM_Veh_Delivery_Bot_01",
                "SM_Veh_E_Bike_01", "SM_Veh_E_Scooter_01", "SM_Veh_Pickup_01_Preset_Taxi",
            };
            foreach (var name in anachronisms)
            {
                if (!Gameplay.VehicleCatalog.IsBarred(name))
                    failures.Add($"Fleet: '{name}' was not on a street in 1987 and is " +
                                 "not barred.");
                // barred beats every other question - nothing may re-adopt it
                if (!Gameplay.VehicleCatalog.IsBarred(
                        "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/" + name + ".prefab"))
                    failures.Add($"Fleet: '{name}' slips the bar when asked by path.");
            }

            // the sedan the taxi preset is built on is NOT caught with it
            if (Gameplay.VehicleCatalog.IsBarred("SM_Veh_Sedan_01") ||
                Gameplay.VehicleCatalog.IsBarred("SM_Veh_Sedan_01_Preset_Taxi") ||
                Gameplay.VehicleCatalog.IsBarred("SM_Veh_Pickup_01"))
                failures.Add("Fleet: the bar caught a car it was only meant to sit " +
                             "beside - check the preset names.");
        }

        /// <summary>A deed names an ordinary citizen. The face dealt to a gazda comes off
        /// whatever bodies the scene happens to hold (PortraitStudio.CivilianPrefab), and
        /// the ledger's own cast carries the force with it - so the filter, not the
        /// contents of a slot list, is what keeps a policeman from keeping a grocery.</summary>
        static void NoPolicemanKeepsAShop(List<string> failures)
        {
            foreach (var look in Entities.CrowdLooks.Law)
            {
                if (!Entities.CrowdLooks.IsLawBody(look))
                    failures.Add($"Deeds: '{look}' is on the force and the filter does " +
                                 "not know it.");
                if (Entities.CrowdLooks.IsCivilianAdult(look) ||
                    Entities.CrowdLooks.IsCivilianAdult(look + "_AI"))
                    failures.Add($"Deeds: '{look}' can be dealt a shop - a policeman " +
                                 "behind the counter.");
            }

            foreach (var look in Entities.CrowdLooks.Children)
                if (Entities.CrowdLooks.IsCivilianAdult(look) ||
                    Entities.CrowdLooks.IsCivilianAdult(look + "_AI"))
                    failures.Add($"Deeds: '{look}' is a child and can still be dealt a " +
                                 "shop of his own.");

            // whatever the crowd is barred from wearing is no proprietor either
            foreach (var look in Entities.CrowdLooks.Barred)
                if (Entities.CrowdLooks.IsCivilianAdult(look))
                    failures.Add($"Deeds: '{look}' is off the pavement and still holds " +
                                 "a deed.");

            // and the people a shop IS kept by stay eligible
            string[] proprietors =
            {
                "SM_Chr_City_Male_01", "SM_Chr_City_Female_02", "SM_Chr_Rich_Male_01",
                "SM_Gen_Chr_Business_Female_01", "SM_Gen_Chr_Street_Male_02",
                "SM_Gen_Chr_Jumpsuit_Male_01", "Character_Male_Jacket",
                "SM_Chr_Bartender_Male_01", "SM_Chr_ShopKeeper_01",
            };
            foreach (var look in proprietors)
                if (!Entities.CrowdLooks.IsCivilianAdult(look) ||
                    !Entities.CrowdLooks.IsCivilianAdult(look + "_AI") ||
                    !Entities.CrowdLooks.IsCivilianAdult(
                        "Assets/Synty/PolygonCity/Prefabs/Characters/" + look + ".prefab"))
                    failures.Add($"Deeds: '{look}' is an ordinary citizen the filter " +
                                 "will not let hold a deed.");

            if (Entities.CrowdLooks.IsCivilianAdult(null) ||
                Entities.CrowdLooks.IsCivilianAdult(""))
                failures.Add("Deeds: a body with no name can hold a deed.");
        }

        static IEnumerable<int> Members(Personnel.Roster roster, Personnel.Crew crew)
        {
            yield return crew.LieutenantId;
            foreach (var id in crew.HoodIds)
                yield return id;
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
