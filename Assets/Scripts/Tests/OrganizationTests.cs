using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Gangs;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Headless contracts for GAN-55 / ORG-001 through ORG-012 and ORG-014.</summary>
    public static class OrganizationTests
    {
        static readonly TerritoryBlockId BlockA = new TerritoryBlockId("core:test:block:a");
        static readonly TerritoryBlockId BlockB = new TerritoryBlockId("core:test:block:b");

        public static List<string> Run()
        {
            var failures = new List<string>();
            BossIsOneRealStableCharacter(failures);
            RecruitmentPaysThenCreatesOneUnassignedHood(failures);
            HierarchyTransfersIdentityAndHasNoFourHoodLimit(failures);
            CapacityIsCentralHardForMenSoftForBlocks(failures);
            ResponsibilityUsesCanonicalIdsWithoutChangingTerritorySignals(failures);
            QueryProjectsHierarchyAndPhysicalMappings(failures);
            TheBossesOwnDetailIsNotAStaleCommandParent(failures);
            ValidationReportsCorruptionWithoutRepairingIt(failures);
            FilingOfficeAnswersOnlyAfterItsDelay(failures);
            FilingOfficeIsWhereCapacityIsHard(failures);
            OnlyAHoodInACrewCarriesTheBag(failures);
            TheBagIsOneMansAndTheLieutenantHandsIt(failures);
            TheChairPassesToTheMostLoyalLieutenant(failures);
            AHouseWithNoLieutenantHasNobodyToTakeTheChair(failures);
            return failures;
        }

        static Character Stand(Roster roster, Rank rank, int loyalty, string surname)
        {
            var man = new Character
            {
                Id = roster.NextCharacterId(),
                FirstName = "T",
                Surname = surname,
                Rank = rank,
                Loyalty = loyalty,
            };
            roster.Members.Add(man);
            return man;
        }

        /// <summary>
        /// EPIC 25, Q3 - the user's word of 2026-09-03: one of the lieutenants takes the
        /// chair. Nothing used to move <c>Organization.BossId</c> when the man it named
        /// went down, so the books went on naming a corpse and the house latched Fallen
        /// (ours) or read Extinct (a family's) with every other man still standing.
        ///
        /// The rule is the crews' own, one rank up: the MOST LOYAL lieutenant still on
        /// his feet. He leaves his own crew to his own most loyal man, takes the dead
        /// Don's detail, and his bargain is torn up because a new rank is a new bargain.
        /// </summary>
        static void TheChairPassesToTheMostLoyalLieutenant(List<string> failures)
        {
            var roster = Roster.Create(GangCatalog.PlayerGangId);
            var don = Stand(roster, Rank.Boss, 50, "Don");
            roster.Organization.BossId = don.Id;

            var loyal = Stand(roster, Rank.Lieutenant, 90, "Loyal");
            var his = new Crew { Id = roster.NextCrewId(), LieutenantId = loyal.Id };
            roster.Crews.Add(his);
            var second = Stand(roster, Rank.Hood, 30, "Second");
            var favourite = Stand(roster, Rank.Hood, 70, "Favourite");
            his.HoodIds.Add(second.Id);
            his.HoodIds.Add(favourite.Id);
            // A lieutenant the Don trusted less, to prove the pick is the loyalty and
            // not the order of the book.
            var cooler = Stand(roster, Rank.Lieutenant, 40, "Cooler");
            roster.Crews.Add(new Crew { Id = roster.NextCrewId(), LieutenantId = cooler.Id });

            // The Don's own crew IS his detail (Bodyguards), and it has to end up under
            // the man who takes his chair rather than promoting a bodyguard over him.
            var detail = Bodyguards.FormDetail(roster);
            var guard = Stand(roster, Rank.Hood, 95, "Guard");
            detail.HoodIds.Add(guard.Id);

            // He was a lieutenant on a lieutenant's bargain when the shooting started.
            loyal.WageAsked = 220;

            // GROUND ON BOTH SIDES OF THE SUCCESSION. The Don answers for a block
            // himself, and so does the lieutenant who is about to take his chair: one
            // has to follow the man into the chair, the other has to stay with the crew
            // he leaves behind. A row left naming the dead man is a block no crew
            // answers for, and the collector rota skips it in silence.
            if (!RosterOps.AssignBlockResponsibility(roster, BlockA, don.Id, true).Ok)
                failures.Add("CHAIR: the fixture could not give the Don a block.");
            if (!RosterOps.AssignBlockResponsibility(roster, BlockB, loyal.Id, true).Ok)
                failures.Add("CHAIR: the fixture could not give the heir a block.");

            RosterOps.Kill(roster, don.Id);

            if (roster.Organization.BossId != loyal.Id)
                failures.Add("CHAIR: the books still name " + roster.Organization.BossId +
                             " as the Boss after he was killed.");
            if (roster.FindBoss() == null || roster.FindBoss().Status != CharacterStatus.Active)
                failures.Add("CHAIR: the house has no living Boss.");
            if (loyal.Rank != Rank.Boss)
                failures.Add("CHAIR: the heir took the chair without the rank.");
            if (loyal.WageAsked != 0)
                failures.Add("CHAIR: the new Don kept a lieutenant's bargain (" +
                             loyal.WageAsked + ") - a new rank is a new bargain.");
            if (loyal.RankSince != roster.Day)
                failures.Add("CHAIR: the new Don's rank clock was not restarted.");
            if (detail.LieutenantId != loyal.Id)
                failures.Add("CHAIR: the detail did not pass to the man who took the chair.");
            if (his.LieutenantId != favourite.Id || favourite.Rank != Rank.Lieutenant)
                failures.Add("CHAIR: his own crew was not left to his most loyal man.");
            if (guard.Rank != Rank.Hood)
                failures.Add("CHAIR: a bodyguard was promoted over the new Don.");
            // The most loyal LIEUTENANT, not the most loyal man in the book: the guard
            // at 95 is loyaler than the heir at 90 and is not in the running.
            if (roster.Organization.BossId == guard.Id)
                failures.Add("CHAIR: a hood took the chair.");

            // The dead Don's ground answers to the man in his chair; the ground his own
            // old crew held stayed with that crew, under its new lieutenant. Nothing
            // anywhere still names the corpse.
            var paper = roster.Organization.BlockResponsibilities;
            var dons = -1;
            var crews = -1;
            for (var i = 0; i < paper.Count; i++)
            {
                if (paper[i].BlockId == BlockA) dons = paper[i].LeaderId;
                if (paper[i].BlockId == BlockB) crews = paper[i].LeaderId;
                if (paper[i].LeaderId == don.Id)
                    failures.Add("CHAIR: a block still answers to the dead Don.");
            }
            if (dons != loyal.Id)
                failures.Add("CHAIR: the Don's own block did not follow the chair.");
            if (crews != favourite.Id)
                failures.Add("CHAIR: the crew's block did not stay with the crew.");

            // AND THE BOOKS ARE STILL A LEGAL ORGANIZATION. The fallen man keeps his
            // rank on the record - his line is never rewritten - so the one-Boss rule
            // has to count the Bosses the house still HAS, not the ones it ever had.
            var blocks = new HashSet<TerritoryBlockId> { BlockA, BlockB };
            var validation = new List<string>();
            OrganizationValidator.Validate(roster, blocks, null, validation);
            if (validation.Count != 0)
                failures.Add("CHAIR: the books after a succession report " + validation[0]);

            // And it survives being written down and read back: a save taken the day
            // after the Don was shot has to restore the same legal house.
            var restored = Roster.Create(GangCatalog.PlayerGangId);
            RosterSnapshot.Restore(restored, RosterSnapshot.Snapshot(roster));
            if (restored.BossId != loyal.Id)
                failures.Add("CHAIR: the saved campaign forgot who took the chair.");
            validation.Clear();
            OrganizationValidator.Validate(restored, blocks, null, validation);
            if (validation.Count != 0)
                failures.Add("CHAIR: the books after a save/load report " + validation[0]);
        }

        /// <summary>
        /// And the other half: a Don with nobody of rank behind him leaves nobody to
        /// take the chair. What that means for a whole HOUSE - no turn of mind, no
        /// order, no midnight, no hours, even with men still on its books - is
        /// UnderworldTests.AHeadlessHouseIsSkipped; this is the roster half of it.
        /// </summary>
        static void AHouseWithNoLieutenantHasNobodyToTakeTheChair(List<string> failures)
        {
            var roster = Roster.Create(GangCatalog.PlayerGangId);
            var don = Stand(roster, Rank.Boss, 50, "Alone");
            roster.Organization.BossId = don.Id;
            var hood = Stand(roster, Rank.Hood, 99, "Faithful");
            var detail = Bodyguards.FormDetail(roster);
            detail.HoodIds.Add(hood.Id);

            RosterOps.Kill(roster, don.Id);

            var boss = roster.FindBoss();
            if (boss != null && boss.Status == CharacterStatus.Active)
                failures.Add("CHAIR: a house with no lieutenant found a Boss anyway.");
            if (hood.Rank == Rank.Boss)
                failures.Add("CHAIR: a bodyguard was made Don for want of a lieutenant.");
        }

        /// <summary>
        /// GAN-262. One bag to a crew; the boss names any of the lieutenant's men for it
        /// or the lieutenant hands it out himself, as well as his Organization lets him;
        /// the boss's ruling stands until he changes it, and the lieutenant only fills a
        /// gap the boss has not spoken on.
        /// </summary>
        static void TheBagIsOneMansAndTheLieutenantHandsIt(List<string> failures)
        {
            // The four bands, and the clamp that makes a crew of one always yield him.
            if (CollectorChoice.PickRank(8, 4) != 0 || CollectorChoice.PickRank(10, 1) != 0)
                failures.Add("BAG: a four-star organizer did not reach for the best man.");
            if (CollectorChoice.PickRank(6, 4) != 1 || CollectorChoice.PickRank(7, 1) != 0)
                failures.Add("BAG: a three-star organizer did not reach for the second man.");
            if (CollectorChoice.PickRank(4, 4) != 2 || CollectorChoice.PickRank(5, 3) != 1)
                failures.Add("BAG: a two-star organizer did not reach for the middle man.");
            if (CollectorChoice.PickRank(2, 4) != 3 || CollectorChoice.PickRank(3, 1) != 0)
                failures.Add("BAG: a one-star organizer did not reach for the worst man.");
            if (CollectorChoice.PickRank(8, 0) != -1)
                failures.Add("BAG: an empty crew produced a pick.");

            var roster = RosterSeeder.GenerateStaffed(31);
            var crew = roster.Crews[0];
            var lieutenant = roster.Find(crew.LieutenantId);
            // Four men to the crew, whatever the seeder left it holding - the bands
            // below are written against a roll of four.
            var draw = new System.Random(262);
            while (crew.HoodIds.Count < 4)
            {
                var recruit = RosterSeeder.Recruit(roster, draw);
                if (!RosterOps.AssignToCrew(roster, recruit.Id, crew.Id).Ok)
                    break;
            }
            if (crew.HoodIds.Count != 4)
            {
                failures.Add("BAG: the fixture crew holds " + crew.HoodIds.Count +
                             " hoods, not four.");
                return;
            }

            // EVERY MAN LEVEL AT THE FLOOR, and only their greed apart. SetHalfSteps
            // caps at each man's own POTENTIAL, so a fixture that writes a ladder of
            // skills gets whatever his ceilings allow and tests nothing; the floor is
            // the one value it can never refuse. The order is then the greed tiebreak,
            // in the order the men are named here.
            var floor = AttributeScale.MinHalfSteps;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                hood.SetHalfSteps(CharacterAttribute.Streetwise, floor);
                hood.SetHalfSteps(CharacterAttribute.Persuasion, floor);
                hood.SetHalfSteps(CharacterAttribute.Awareness, floor);
                hood.SetPotential(CharacterAttribute.Streetwise, 100);
                hood.SetPotential(CharacterAttribute.Persuasion, 100);
                hood.SetPotential(CharacterAttribute.Awareness, 100);
                hood.Greed = 10 + i * 10;
                hood.Status = CharacterStatus.Active;
                hood.Duty = Duty.None;
            }
            // His own ceiling would otherwise hold the lieutenant at whatever
            // Organization the seeder rolled him, and every band below would read the
            // same man.
            lieutenant.SetPotential(CharacterAttribute.Organization, 100);
            var best = crew.HoodIds[0];
            var second = crew.HoodIds[1];
            var third = crew.HoodIds[2];
            var worst = crew.HoodIds[3];

            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 10);
            if (CollectorChoice.Pick(roster, crew) != best)
                failures.Add("BAG: the organizer did not hand the bag to his best man.");
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 6);
            if (CollectorChoice.Pick(roster, crew) != second)
                failures.Add("BAG: a three-star lieutenant did not hand it to his second man.");
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 4);
            if (CollectorChoice.Pick(roster, crew) != third)
                failures.Add("BAG: a two-star lieutenant did not hand it to a middling man.");
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 2);
            if (CollectorChoice.Pick(roster, crew) != worst)
                failures.Add("BAG: a one-star lieutenant did not hand it to his worst man.");
            lieutenant.SetHalfSteps(CharacterAttribute.Organization, 10);

            // SKILL BEATS GREED: the greediest of the four, raised above the rest,
            // is taken ahead of them - greed only breaks a tie.
            var greedy = roster.Find(worst);
            greedy.SetHalfSteps(CharacterAttribute.Streetwise, 10);
            greedy.SetHalfSteps(CharacterAttribute.Persuasion, 10);
            greedy.SetHalfSteps(CharacterAttribute.Awareness, 10);
            if (CollectorChoice.Fitness(greedy) <= 3 * floor)
                failures.Add("BAG: the fixture could not raise a man above the floor.");
            else if (CollectorChoice.Pick(roster, crew) != worst)
                failures.Add("BAG: greed was read ahead of the trades that carry a bag.");
            greedy.SetHalfSteps(CharacterAttribute.Streetwise, floor);
            greedy.SetHalfSteps(CharacterAttribute.Persuasion, floor);
            greedy.SetHalfSteps(CharacterAttribute.Awareness, floor);

            // One bag to a crew: marking the second man takes it off the first.
            RosterOps.SetDuty(roster, best, Duty.Collector);
            RosterOps.SetDuty(roster, second, Duty.Collector);
            var carried = new List<Character>();
            RosterOps.CollectorsOf(roster, crew.Id, carried);
            if (roster.Find(best).Duty != Duty.None || carried.Count != 1 ||
                RosterOps.CollectorOf(roster, crew.Id) != second)
                failures.Add("BAG: two men of one crew were left holding the bag.");

            // The boss names one of the lieutenant's OWN men, and nobody else's. The
            // stranger is another crew's hood where the fixture has one, else a man
            // out of the pool - either way he is not on this lieutenant's roll.
            var stranger = -1;
            for (var c = 1; c < roster.Crews.Count && stranger < 0; c++)
                if (roster.Crews[c].HoodIds.Count > 0)
                    stranger = roster.Crews[c].HoodIds[0];
            if (stranger < 0)
            {
                var pooled = new List<int>();
                roster.PoolIds(pooled);
                stranger = pooled.Count > 0
                    ? pooled[0]
                    : RosterSeeder.Recruit(roster, draw).Id;
            }
            if (RosterOps.NameCollector(roster, crew.Id, stranger).Ok)
                failures.Add("BAG: another crew's man was named for this crew's bag.");
            if (!RosterOps.NameCollector(roster, crew.Id, third).Ok ||
                RosterOps.CollectorOf(roster, crew.Id) != third || !crew.BagNamedByBoss)
                failures.Add("BAG: the boss's own naming did not take.");

            // A lieutenant with nobody on his feet has nobody to hand it to.
            for (var i = 0; i < crew.HoodIds.Count; i++)
                roster.Find(crew.HoodIds[i]).Status = CharacterStatus.Jailed;
            if (RosterOps.LetLieutenantPick(roster, crew.Id, out _).Ok)
                failures.Add("BAG: a crew with every man inside still produced a bag man.");
            for (var i = 0; i < crew.HoodIds.Count; i++)
                roster.Find(crew.HoodIds[i]).Status = CharacterStatus.Active;

            // The morning pass: only a crew with ground on the paper gets a bag man.
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                roster.Crews[c].BagNamedByBoss = false;
                for (var i = 0; i < roster.Crews[c].HoodIds.Count; i++)
                    roster.Find(roster.Crews[c].HoodIds[i]).Duty = Duty.None;
            }
            roster.Organization.BlockResponsibilities.Add(
                new OrganizationBlockResponsibility(BlockA, crew.LieutenantId));
            var handed = new List<(int crewId, int hoodId)>();
            RosterOps.TendCollectors(roster, handed);
            if (handed.Count != 1 || handed[0].crewId != crew.Id || handed[0].hoodId != best ||
                RosterOps.CollectorOf(roster, crew.Id) != best ||
                (roster.Crews.Count > 1 &&
                 RosterOps.CollectorOf(roster, roster.Crews[1].Id) != -1))
                failures.Add("BAG: the morning pass handed the bag to the wrong crews.");

            // The boss's man keeps it through a sentence; the lieutenant's own pick
            // does not - he hands it on to a man who can walk.
            RosterOps.NameCollector(roster, crew.Id, second);
            roster.Find(second).Status = CharacterStatus.Jailed;
            RosterOps.TendCollectors(roster, handed);
            if (handed.Count != 0 || RosterOps.CollectorOf(roster, crew.Id) != second)
                failures.Add("BAG: the boss's man lost the bag in a cell.");
            crew.BagNamedByBoss = false;
            RosterOps.TendCollectors(roster, handed);
            if (handed.Count != 1 || RosterOps.CollectorOf(roster, crew.Id) != best ||
                roster.Find(second).Duty != Duty.None)
                failures.Add("BAG: the lieutenant left his bag with a man in a cell.");
            roster.Find(second).Status = CharacterStatus.Active;

            // A RULING OUTLIVES A SENTENCE, NOT A MAN. The boss's man in a cell keeps
            // the bag (above); the boss's man who is DEAD does not, or the crew would
            // stand with a standing order naming a corpse and never collect again.
            RosterOps.NameCollector(roster, crew.Id, second);
            roster.Find(second).Status = CharacterStatus.Dead;
            RosterOps.TendCollectors(roster, handed);
            if (handed.Count != 1 || RosterOps.CollectorOf(roster, crew.Id) != best ||
                crew.BagNamedByBoss)
                failures.Add("BAG: a dead man's naming wedged the crew off the bag.");
            roster.Find(second).Status = CharacterStatus.Active;
            roster.Find(second).Duty = Duty.None;

            // And a named man moved to another lieutenant is not this crew's ruling any
            // more: the bag is handed out again here, and over there he is that crew's
            // one bag man, whoever was carrying it for them.
            if (roster.Crews.Count > 1)
            {
                var other = roster.Crews[1];
                var theirs = other.HoodIds.Count > 0 ? other.HoodIds[0] : -1;
                if (theirs >= 0)
                {
                    RosterOps.SetDuty(roster, theirs, Duty.Collector);
                    RosterOps.NameCollector(roster, crew.Id, third);
                    RosterOps.AssignToCrew(roster, third, other.Id);
                    RosterOps.TendCollectors(roster, handed);
                    if (crew.BagNamedByBoss ||
                        RosterOps.CollectorOf(roster, crew.Id) == third)
                        failures.Add("BAG: a man who walked out kept his old crew's bag.");
                    if (RosterOps.CollectorOf(roster, other.Id) != third ||
                        roster.Find(theirs).Duty != Duty.None)
                        failures.Add("BAG: the crew he joined was left with two bag men.");
                }
            }

            // The boss's "nobody" holds until he says LET HIM PICK.
            RosterOps.TakeOffTheBag(roster, best);
            RosterOps.TendCollectors(roster, handed);
            if (handed.Count != 0 || RosterOps.CollectorOf(roster, crew.Id) != -1)
                failures.Add("BAG: the lieutenant overruled the boss's nobody.");
            if (!RosterOps.LetLieutenantPick(roster, crew.Id, out var picked).Ok ||
                picked != best || crew.BagNamedByBoss)
                failures.Add("BAG: LET HIM PICK did not give the job back to the lieutenant.");
        }

        /// <summary>
        /// The bag is a standing job given to a HOOD IN A CREW and to nobody else. A
        /// lieutenant runs the branch, a man in the pool walks nobody's doors, and any
        /// move that changes who a man answers to takes the job off him with it -
        /// otherwise a promoted collector would still be on somebody's round.
        /// </summary>
        static void OnlyAHoodInACrewCarriesTheBag(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(31);
            var crew = roster.Crews[0];
            var hood = crew.HoodIds[0];

            if (!RosterOps.SetDuty(roster, hood, Duty.Collector).Ok)
                failures.Add("DUTY: a hood in a crew was refused the bag.");
            if (roster.Find(hood).Duty != Duty.Collector)
                failures.Add("DUTY: the mark did not stick.");

            if (RosterOps.SetDuty(roster, crew.LieutenantId, Duty.Collector).Ok)
                failures.Add("DUTY: a lieutenant was put on the bag.");

            var pool = new List<int>();
            roster.PoolIds(pool);
            if (pool.Count > 0 && RosterOps.SetDuty(roster, pool[0], Duty.Collector).Ok)
                failures.Add("DUTY: a man in the pool was put on the bag.");

            var carried = new List<Character>();
            RosterOps.CollectorsOf(roster, crew.Id, carried);
            if (carried.Count != 1 || carried[0].Id != hood)
                failures.Add("DUTY: the crew's collectors are " + carried.Count +
                             ", not the one man marked.");

            // A man in a bed is on the books and not on the round.
            roster.Find(hood).Status = CharacterStatus.Hospitalized;
            RosterOps.CollectorsOf(roster, crew.Id, carried);
            if (carried.Count != 0)
                failures.Add("DUTY: a man in a hospital bed was counted onto the round.");
            roster.Find(hood).Status = CharacterStatus.Active;

            // And promotion takes the bag off him: he runs a branch now.
            RosterOps.Promote(roster, hood, out _);
            if (roster.Find(hood).Duty != Duty.None)
                failures.Add("DUTY: a promoted man kept the bag.");

            // Taking a duty off is always allowed, whatever his footing.
            if (!RosterOps.SetDuty(roster, crew.LieutenantId, Duty.None).Ok)
                failures.Add("DUTY: taking a man off the bag was refused.");
        }

        /// <summary>The sheet ASKS and the outfit ANSWERS: nothing the resolver does may
        /// happen at the moment the order is filed, and it must happen exactly once.</summary>
        static void FilingOfficeAnswersOnlyAfterItsDelay(List<string> failures)
        {
            var office = new OutfitFilings { RulingSeconds = 1f };
            var ran = 0;
            var filing = office.File("D1 09:00", "A man put under Artie Byrne.", () =>
            {
                ran++;
                return FilingRuling.Grant("he reports to him from today");
            });

            if (ran != 0 || filing.Status != FilingStatus.Filed || office.AwaitingCount != 1)
                failures.Add("Filings: an order took effect at the moment it was filed.");

            if (office.Tick(0.5f) || ran != 0 || filing.Status != FilingStatus.Filed)
                failures.Add("Filings: the office answered before its delay ran out.");

            if (!office.Tick(0.6f) || ran != 1 ||
                filing.Status != FilingStatus.Granted ||
                filing.Ruling != "he reports to him from today")
                failures.Add("Filings: the ruling did not land exactly once.");

            if (office.Tick(10f) || ran != 1 || office.AwaitingCount != 0)
                failures.Add("Filings: a settled order was ruled on a second time.");

            // Newest first, and both answered in the order they were asked.
            var order = new List<int>();
            var second = new OutfitFilings { RulingSeconds = 0.5f };
            second.File("D1 09:01", "first", () =>
            {
                order.Add(1);
                return FilingRuling.Grant("");
            });
            second.File("D1 09:02", "second", () =>
            {
                order.Add(2);
                return FilingRuling.Refuse("no room");
            });
            second.Tick(1f);
            if (order.Count != 2 || order[0] != 1 || order[1] != 2)
                failures.Add("Filings: orders were not answered in the order they were asked.");
            if (second.All.Count != 2 || second.All[0].Text != "second" ||
                second.All[0].Status != FilingStatus.Refused)
                failures.Add("Filings: the sheet does not read newest order first.");
        }

        /// <summary>The filing office and the roster refuse the ASSIGNMENT that would
        /// create an overage (RANK-001) - but an overage that arises without one (the
        /// config tightening, a succession) is CARRIED, not repaired: nobody is thrown
        /// off a branch by arithmetic.</summary>
        static void FilingOfficeIsWhereCapacityIsHard(List<string> failures)
        {
            var atLimit = new CapacityMeasure(3, 3);
            var room = new CapacityMeasure(2, 3);
            var over = new CapacityMeasure(4, 3);

            if (OutfitFilingRules.AcceptsAnotherMan(atLimit) ||
                OutfitFilingRules.AcceptsAnotherMan(over) ||
                !OutfitFilingRules.AcceptsAnotherMan(room))
                failures.Add("Filings: the office does not refuse the man who would not fit.");
            if (OutfitFilingRules.AcceptsAnotherBlock(atLimit) ||
                !OutfitFilingRules.AcceptsAnotherBlock(room))
                failures.Add("Filings: the office does not refuse the block that would not fit.");

            if (!OutfitFilingRules.ManRefusal("Artie Byrne", atLimit).Contains("3/3") ||
                !OutfitFilingRules.BlockRefusal("Artie Byrne", atLimit).Contains("3/3"))
                failures.Add("Filings: a refusal does not print the figure it refused on.");

            // An overage the ROSTER never assented to - the ceiling dropped under men
            // already standing - is carried and read, and the men stay where they are.
            var roster = RosterSeeder.GenerateStaffed(23);
            var crew = roster.Crews[0];
            var standing = crew.HoodIds.Count;
            RosterOps.ConfigureOrganization(roster, new OrganizationLimits(70, 4, 1, 1));
            var capacity = new OrganizationQuery(roster).CapacityOf(crew.LieutenantId);
            if (standing > 1 && !capacity.Manpower.IsOverCapacity)
                failures.Add("Filings: a config-shrunk overage is not read as one.");
            if (crew.HoodIds.Count != standing)
                failures.Add("Filings: shrinking the ceiling threw men off the branch.");
        }

        /// <summary>A house of one's own: the book, a safe with a sheet open on it,
        /// and nothing else. The same object the player's outfit is.</summary>
        static House HouseOf(Roster roster, int gangId = 0, int safe = -1)
        {
            var runner = new CampaignRunner { Seed = roster.Seed };
            runner.OpenFirstSheet();
            if (safe >= 0)
                runner.Accounts.Safe = safe;
            return new House(gangId, roster, runner);
        }

        static void RecruitmentPaysThenCreatesOneUnassignedHood(List<string> failures)
        {
            if (PersonnelDirector.Instance != null &&
                PersonnelDirector.Instance.HoodRecruitmentCost !=
                EconomyPrices.RecruitSigning)
                failures.Add("Recruitment: the ledger's counter has a price of its own.");
            if (EconomyPrices.RecruitSigning != 500)
                failures.Add("Recruitment: the one signing fee is not $500.");

            var roster = RosterSeeder.GenerateStaffed(1987);
            var house = HouseOf(roster);
            var accounts = house.Runner.Accounts;
            var beforeCount = roster.Members.Count;
            var beforeCrewCount = roster.Crews.Count;
            var beforeSafe = accounts.Safe;

            var result = HouseOps.Recruit(house, out var recruit);
            var query = new OrganizationQuery(roster);
            var hoods = new List<OrganizationPerson>();
            query.CollectHoods(hoods);
            var snapshot = hoods.Find(person => recruit != null && person.Id == recruit.Id);

            if (!result.Ok || recruit == null || roster.Members.Count != beforeCount + 1 ||
                roster.Crews.Count != beforeCrewCount)
                failures.Add("Recruitment: a paid intent did not create exactly one Character.");
            else if (recruit.Rank != Rank.Hood || recruit.Specialty != Specialty.None ||
                     roster.AssignmentOf(recruit.Id).Kind != AssignmentKind.Pool ||
                     !snapshot.IsValid || !snapshot.IsUnassigned)
                failures.Add("Recruitment: the new Character is not an unassigned Hood.");
            else if (!query.TryGetCommandParent(recruit.Id, out var parent) ||
                     parent.Id != roster.BossId)
                failures.Add("Recruitment: the available Hood does not report directly to the Boss.");
            else if (string.IsNullOrEmpty(recruit.FirstName) ||
                     string.IsNullOrEmpty(recruit.Surname) || string.IsNullOrEmpty(recruit.Look))
                failures.Add("Recruitment: identity/name/appearance was not generated by authority.");

            var rolledStats = new HashSet<int>();
            if (recruit != null)
                for (var attribute = 0; attribute < AttributeScale.Count; attribute++)
                {
                    var value = recruit.GetHalfSteps((CharacterAttribute)attribute);
                    rolledStats.Add(value);
                    if (value < AttributeScale.MinHalfSteps ||
                        value > RosterSeeder.RecruitCeilingHalfSteps)
                        failures.Add("Recruitment: a starting stat was outside the recruit roll band.");
                }
            if (recruit != null && rolledStats.Count < 2)
                failures.Add("Recruitment: starting stats were not randomized.");

            if (accounts.Safe != beforeSafe - EconomyPrices.RecruitSigning ||
                accounts.Current == null ||
                accounts.Current.Purchases != EconomyPrices.RecruitSigning)
                failures.Add("Recruitment: the house's own account did not book the fee.");

            var poorRoster = RosterSeeder.GenerateStaffed(1988);
            var poor = HouseOf(poorRoster, gangId: 0,
                safe: EconomyPrices.RecruitSigning - 1);
            var poorCount = poorRoster.Members.Count;
            var poorSafe = poor.Runner.Accounts.Safe;

            var refused = HouseOps.Recruit(poor, out var unpaid);
            if (refused.Ok || unpaid != null || poorRoster.Members.Count != poorCount ||
                poor.Runner.Accounts.Safe != poorSafe)
                failures.Add("Recruitment: insufficient funds still changed money or personnel.");

            // ONE PRICE THROUGH EVERY DOOR. The corner and the counter are the same
            // signature; only the twelve hours and the recruiter's eye differ.
            if (OrderTable.SpecOf(OrderType.Recruit).Cost != EconomyPrices.RecruitSigning)
                failures.Add("Recruitment: the Recruit order and the ledger's door " +
                             "charge different money for one man.");

            // A rival house signs a man through the same call, at the same price.
            var rivalRoster = RosterSeeder.Generate(1987, 7);
            var rival = HouseOf(rivalRoster, gangId: 7);
            var rivalBefore = rival.Runner.Accounts.Safe;
            var rivalCount = rivalRoster.Members.Count;
            if (!HouseOps.Recruit(rival, out var theirs).Ok || theirs == null ||
                rivalRoster.Members.Count != rivalCount + 1 ||
                rival.Runner.Accounts.Safe != rivalBefore - EconomyPrices.RecruitSigning)
                failures.Add("Recruitment: a rival house does not sign a man the way " +
                             "the player's does.");
            else if (rivalRoster.Find(theirs.Id) == null || roster.Find(theirs.Id) != null)
                failures.Add("Recruitment: a rival's new man landed in the wrong book.");
        }

        static void BossIsOneRealStableCharacter(List<string> failures)
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var roster = RosterSeeder.GenerateStaffed(seed);
                var boss = roster.FindBoss();
                var bosses = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Rank == Rank.Boss)
                        bosses++;

                if (bosses != 1 || boss == null || boss.Id != RosterSeeder.FixtureBossCharacterId)
                    failures.Add("Boss: seed " + seed + " did not produce exactly one stable Boss.");
                else if (boss.FullName != GangCatalog.BossName || boss.Look != GangCatalog.BossModel)
                    failures.Add("Boss: canonical Don Salvatore identity/model was not reused.");

                // Adding him consumes no personnel draws and therefore never moves the
                // six pre-existing IDs off 0..5.
                for (var id = 0; id < RosterSeeder.FixtureStaffCount; id++)
                    if (roster.Find(id) == null || roster.Find(id).Rank == Rank.Boss)
                        failures.Add("Boss: existing staff ID " + id + " was displaced.");
            }

            var large = RosterSeeder.GenerateLarge(1987, 60);
            var largeBoss = large.FindBoss();
            if (largeBoss == null || largeBoss.Id != RosterSeeder.FixtureBossCharacterId)
                failures.Add("Boss: scale fixture changed the canonical Boss Character ID.");
            var validation = new List<string>();
            OrganizationValidator.Validate(
                large, new HashSet<TerritoryBlockId>(), null, validation);
            if (validation.Count != 0)
                failures.Add("Boss: scale hierarchy reports " + validation[0]);
        }

        static void HierarchyTransfersIdentityAndHasNoFourHoodLimit(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var query = new OrganizationQuery(roster);
            var crew = roster.Crews[0];
            var lieutenant = roster.Find(crew.LieutenantId);
            var direct = new List<OrganizationPerson>();
            query.CollectDirectSubordinates(roster.BossId, direct);
            var hood = direct.Find(person => person.Rank == Rank.Hood);
            var original = roster.Find(hood.Id);
            original.Look = "stable-look";
            original.ConditionNote = "stable-state";

            var moved = RosterOps.AssignToCrew(roster, hood.Id, crew.Id);
            if (!moved.Ok || roster.Find(hood.Id) != original ||
                original.Look != "stable-look" || original.ConditionNote != "stable-state")
                failures.Add("Hierarchy: transfer cloned or changed the Character record.");
            if (!query.TryGetCommandParent(hood.Id, out var parent) ||
                parent.Id != lieutenant.Id)
                failures.Add("Hierarchy: Lieutenant did not become the one direct parent.");
            if (RosterOps.AssignToCrew(roster, hood.Id, crew.Id).Ok)
                failures.Add("Hierarchy: duplicate parent assignment was accepted.");

            var rng = new System.Random(1987);
            while (crew.HoodIds.Count <= Crew.MaxTacticalHoods + 2)
            {
                var recruit = RosterSeeder.Recruit(roster, rng);
                if (!RosterOps.AssignToCrew(roster, recruit.Id, crew.Id).Ok)
                {
                    failures.Add("Hierarchy: organization still refuses the fifth Hood.");
                    break;
                }
            }
            if (crew.HoodIds.Count <= Crew.MaxTacticalHoods)
                failures.Add("Hierarchy: organizational manpower is still the tactical group.");

            var back = RosterOps.AssignToBoss(roster, hood.Id, roster.BossId);
            if (!back.Ok || !query.TryGetCommandParent(hood.Id, out parent) ||
                parent.Id != roster.BossId || crew.HoodIds.Contains(hood.Id))
                failures.Add("Hierarchy: Boss/Lieutenant transfer left two parents.");
        }

        /// <summary>The numbers live in ONE serialized config; a lieutenant's manpower
        /// cap is HARD at the roster (RANK-001 - he refuses the next man), and block
        /// responsibility stays soft behind its force flag. This test used to assert a
        /// soft manpower overage and looped forever when RANK-001 made the cap hard -
        /// every loop below is guarded on principle.</summary>
        static void CapacityIsCentralHardForMenSoftForBlocks(List<string> failures)
        {
            var defaults = OrganizationLimits.Default;
            if (defaults.BossManpower != 70 || defaults.BossBlocks != 4 ||
                defaults.LieutenantManpower != 50 || defaults.LieutenantBlocks != 3)
                failures.Add("Capacity: canonical defaults moved or became scattered.");

            var roster = RosterSeeder.GenerateStaffed(7);
            RosterOps.ConfigureOrganization(roster, new OrganizationLimits(70, 4, 50, 1));
            var crew = roster.Crews[0];
            var rng = new System.Random(7);
            var query = new OrganizationQuery(roster);

            var manpower = query.CapacityOf(crew.LieutenantId).Manpower;
            if (manpower.Current > manpower.Maximum)
            {
                failures.Add("Capacity: the seed dealt a branch already past its cap.");
                return;
            }

            // Fill the branch to its own derived cap...
            var guard = manpower.Maximum - manpower.Current + 2;
            while (guard-- > 0 &&
                   query.CapacityOf(crew.LieutenantId).Manpower.Current <
                   query.CapacityOf(crew.LieutenantId).Manpower.Maximum)
            {
                var hood = RosterSeeder.Recruit(roster, rng);
                if (!RosterOps.AssignToCrew(roster, hood.Id, crew.Id).Ok)
                {
                    failures.Add("Capacity: the branch refused a man below its cap.");
                    return;
                }
            }

            // ...and the NEXT man is refused at the roster itself (RANK-001).
            var extra = RosterSeeder.Recruit(roster, rng);
            if (RosterOps.AssignToCrew(roster, extra.Id, crew.Id).Ok)
                failures.Add("Capacity: the lieutenant took a man past his manpower cap.");
            var atCap = query.CapacityOf(crew.LieutenantId).Manpower;
            if (atCap.Current != atCap.Maximum || atCap.IsOverCapacity)
                failures.Add("Capacity: manpower did not read full-at-cap after the refusal.");

            // Block responsibility is hard at the cap the same way: ground he cannot
            // carry is ground the outfit does not really hold (RANK-001).
            if (!RosterOps.AssignBlockResponsibility(
                    roster, BlockA, crew.LieutenantId, true).Ok)
                failures.Add("Capacity: a block below the cap was refused.");
            if (RosterOps.AssignBlockResponsibility(
                    roster, BlockB, crew.LieutenantId, true).Ok)
                failures.Add("Capacity: the lieutenant took a block past his cap.");
            var blocks = query.CapacityOf(crew.LieutenantId).Blocks;
            if (blocks.Current != 1 || blocks.Maximum != 1 || blocks.IsOverCapacity)
                failures.Add("Capacity: block current/max is wrong at the cap.");
        }

        static void ResponsibilityUsesCanonicalIdsWithoutChangingTerritorySignals(
            List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(11);
            var lieutenantId = roster.Crews[0].LieutenantId;
            var unknown = RosterOps.AssignBlockResponsibility(
                roster, new TerritoryBlockId("missing"), lieutenantId, false);
            if (unknown.Ok)
                failures.Add("Responsibility: an unknown canonical block was accepted.");

            var definition = new TerritoryBlockDefinition(
                BlockA, 1, new TerritoryNeighborhoodId("test"), "Test", "Block A",
                new TerritoryBounds(0f, 0f, 10f, 10f), "test");
            var territory = new TerritorySimulationState(new[] { definition });
            var territoryVersion = territory.Version;

            if (!RosterOps.AssignBlockResponsibility(roster, BlockA, lieutenantId, true).Ok)
                failures.Add("Responsibility: valid Lieutenant assignment was refused.");
            if (territory.Version != territoryVersion)
                failures.Add("Responsibility: administration mutated territory simulation state.");

            if (!RosterOps.AssignBlockResponsibility(roster, BlockA, roster.BossId, true).Ok)
                failures.Add("Responsibility: reassignment to the Boss was refused.");
            var query = new OrganizationQuery(roster);
            var blocks = new List<OrganizationBlockResponsibility>();
            query.CollectBlockResponsibilities(roster.BossId, blocks);
            if (blocks.Count != 1 || blocks[0].BlockId != BlockA)
                failures.Add("Responsibility: canonical ID/reassignment did not persist.");

            if (!RosterOps.RemoveBlockResponsibility(roster, BlockA, roster.BossId).Ok)
                failures.Add("Responsibility: remove through the authority was refused.");
            query.CollectBlockResponsibilities(roster.BossId, blocks);
            if (blocks.Count != 0)
                failures.Add("Responsibility: removed block still consumes capacity.");
        }

        static void QueryProjectsHierarchyAndPhysicalMappings(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(13);
            var crew = roster.Crews[0];
            var ids = new[] { crew.LieutenantId, crew.HoodIds[0] };
            var mapping = new TacticalPersonnelMapping(crew.Id, crew.LieutenantId, ids);
            ids[0] = -1; // the read model must not retain a caller-owned mutable array
            var source = new FakePhysicalSource(mapping);
            var query = new OrganizationQuery(roster);
            query.BindPhysical(source);

            var lieutenants = new List<OrganizationPerson>();
            query.CollectLieutenants(lieutenants);
            var physical = new List<TacticalPersonnelMapping>();
            query.CollectPhysicalMappings(physical);
            if (lieutenants.Count != 1 || physical.Count != 1 ||
                physical[0].CommandParentId != lieutenants[0].Id ||
                physical[0].PersonnelIds.Count != 2)
                failures.Add("Query: hierarchy/physical read model is incomplete.");

            var validation = new List<string>();
            OrganizationValidator.Validate(
                roster, new HashSet<TerritoryBlockId>(), source, validation);
            if (validation.Count != 0)
                failures.Add("Query: valid physical mapping reports " + validation[0]);
        }

        /// <summary>
        /// THE DON'S OWN DETAIL IS A GROUP LIKE ANY OTHER (RANK-003). The branch check
        /// has always made this exception - the bodyguard crew is the one branch whose
        /// head is not a Lieutenant - but the PHYSICAL projection did not, so the moment
        /// the detail stood on the street the live audit called the whole graph corrupt:
        /// "ORG: tactical group 0 has stale command parent 0", measured on MiniCoreDemo
        /// with Don Salvatore Ricci at the head of his own crew.
        /// </summary>
        static void TheBossesOwnDetailIsNotAStaleCommandParent(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(13);
            var detail = Bodyguards.FormDetail(roster);
            if (detail == null || detail.LieutenantId != roster.BossId)
            {
                failures.Add("Detail: the Boss could not be given his own crew.");
                return;
            }

            var source = new FakePhysicalSource(new TacticalPersonnelMapping(
                detail.Id, detail.LieutenantId, new[] { roster.BossId }));
            var validation = new List<string>();
            OrganizationValidator.Validate(
                roster, new HashSet<TerritoryBlockId>(), source, validation);

            if (Contains(validation, "stale command parent"))
                failures.Add("Detail: the Boss leading his own detail reads as a stale " +
                             "command parent (" + validation[0] + ").");
        }

        static void ValidationReportsCorruptionWithoutRepairingIt(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(17);
            var crew = roster.Crews[0];
            var duplicate = crew.HoodIds[0];
            roster.Organization.BossHoodIds.Add(duplicate);
            roster.Organization.BlockResponsibilities.Add(
                new OrganizationBlockResponsibility(BlockB, duplicate));
            // Deliberately cross the two legal ranks into a Boss <-> Lieutenant loop.
            crew.HoodIds.Add(roster.BossId);
            roster.Organization.BossHoodIds.Add(crew.LieutenantId);

            var beforeBossRows = roster.Organization.BossHoodIds.Count;
            var beforeBlocks = roster.Organization.BlockResponsibilities.Count;
            var stale = new FakePhysicalSource(
                new TacticalPersonnelMapping(99, 999,
                    new[] { 999, 999, 999, 999, 999, 999 }));
            var validation = new List<string>();
            OrganizationValidator.Validate(
                roster, new HashSet<TerritoryBlockId> { BlockA }, stale, validation);

            if (!Contains(validation, "direct command parents") ||
                !Contains(validation, "hierarchy cycle") ||
                !Contains(validation, "unknown block") ||
                !Contains(validation, "stale command parent") ||
                !Contains(validation, "maximum physical projection") ||
                !Contains(validation, "unavailable Character"))
                failures.Add("Validation: corruption did not produce actionable diagnostics.");
            if (roster.Organization.BossHoodIds.Count != beforeBossRows ||
                roster.Organization.BlockResponsibilities.Count != beforeBlocks)
                failures.Add("Validation: diagnostics silently repaired destructive state.");
        }

        static bool Contains(List<string> rows, string fragment)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Contains(fragment))
                    return true;
            return false;
        }

        sealed class FakePhysicalSource : IOrganizationPhysicalSource
        {
            readonly TacticalPersonnelMapping mapping;

            public FakePhysicalSource(TacticalPersonnelMapping mapping) =>
                this.mapping = mapping;

            public void CollectPhysicalMappings(List<TacticalPersonnelMapping> into) =>
                into.Add(mapping);
        }
    }
}
