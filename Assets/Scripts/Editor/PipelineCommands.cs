using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LivingCity.EditorTools;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>
    /// The city, answered from the terminal.
    ///
    /// Unity's Pipeline package holds a small server inside a running editor, and the
    /// `unity` CLI talks to it: `unity command &lt;name&gt; --json`. Its own commands cover the
    /// editor (recompile, console, menu, screenshot, play mode, prefabs, scenes). The ones
    /// here cover this project, and they exist for the questions that used to cost a whole
    /// batch run or a hand-built offline harness:
    ///
    ///   unity command gangsters_layout --seed 12          what quarters seed 12 rolls
    ///   unity command gangsters_measure --name SM_Veh_Car_01   how big that prefab really is
    ///   unity command gangsters_play --scene ... --seconds 60  a harness run in THIS editor
    ///
    /// All three read or drive the open editor, so none of them takes Temp/UnityLockfile and
    /// none of them fights a soak that is already running. See Docs/unity-cli.md.
    /// </summary>
    public static class PipelineCommands
    {
        [CliCommand("gangsters_crew_audit_tests",
                    "Run pure contracts for crew formation, sustained audit grace, and walk-prop classification.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "crew", "tests" })]
        public static object CrewAuditTests()
        {
            var failures = LivingCity.Tests.CrewAuditModelTests.Run();
            // The fighting policy rides along with the formation contracts: both are
            // the crews, and a change to one is routinely a change to the other.
            failures.AddRange(LivingCity.Tests.CloserThreatTests.Run()
                .Select(failure => "Closer threat regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_aim_tests",
                    "Run EPIC 33 contracts for the closer threat: the distance margin, " +
                    "the held dwell, the anti-flicker hysteresis, and the angular miss " +
                    "cone a man's Combat stat opens or tightens.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "crew", "combat", "tests" })]
        public static object AimTests()
        {
            var failures = LivingCity.Tests.CloserThreatTests.Run();
            // The contract list comes back with the verdict on purpose: a stale
            // assembly answers ALL PASS just as cheerfully as a fresh one.
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.CloserThreatTests.ContractNames(),
                table = new
                {
                    oneStar = AimTableRow(2),
                    threeStars = AimTableRow(6),
                    fiveStars = AimTableRow(10),
                },
            };
        }

        /// <summary>One row of the closer-threat table as the build actually reads it -
        /// what the terminal prints beside the verdict, so a tuning change is visible
        /// without opening the file.</summary>
        static object AimTableRow(int halfSteps) => new
        {
            halfSteps,
            marginMetres = RoadDemo.CrewSkill.ThreatMargin(halfSteps),
            dwellSeconds = RoadDemo.CrewSkill.ThreatDwell(halfSteps),
            coneMultiplier = RoadDemo.CrewSkill.MissCone(halfSteps),
            rifleConeDegrees = RoadDemo.CrewSkill.MissConeDegrees(0.88f, halfSteps),
            pistolConeDegrees = RoadDemo.CrewSkill.MissConeDegrees(0.55f, halfSteps),
        };

        [CliCommand("gangsters_core_vacancy_tests",
                    "Run seed-1987 contracts for stand-alone amenity blocks, empty remainders, " +
                    "and residential no-water fallback.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "core", "tests" })]
        public static object CoreVacancyAudit()
        {
            var failures = LivingCity.Tests.CoreVacancyTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                seed = 1987,
            };
        }

        [CliCommand("gangsters_territory_foundation_tests",
                    "Run GAN-46 contracts for stable IDs, commands, projections, events, and fixed game-time scheduling.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "territory", "tests" })]
        public static object TerritoryFoundationAudit()
        {
            var failures = LivingCity.Tests.TerritoryFoundationTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_geography_tests",
                    "Run GAN-68 contracts for the canonical geography: blocks, neighborhoods, " +
                    "the block neighbor graph, road-space resolution and business membership.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "territory", "tests" })]
        public static object GeographyTests()
        {
            var failures = LivingCity.Tests.GeographyTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// The canonical geography of a real city, judged. Like the business audit it can
        /// deal its own quarter from a seed - CoreDistrict.Plan is pure data - so the whole
        /// sweep runs with the editor idle, and it deals the SAME seed twice to prove the
        /// identity and the graph do not move between runs.
        /// </summary>
        [CliCommand("gangsters_geography_audit",
                    "Count and judge one city's canonical blocks, neighborhoods, block " +
                    "adjacency and business membership, and prove they are the same twice.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "territory", "audit" })]
        public static object GeographyAudit(
            [CliArg("seed", "Deal a Core quarter from this seed and audit THAT plan. " +
                            "-1 uses the running city when there is one.")] int seed = -1,
            [CliArg("rows", "List every block with its neighbours and business count.")]
            bool rows = false,
            [CliArg("twice", "Deal the same seed a second time and prove the identity and " +
                             "the graph did not move. Costs a second plan roll, which on a " +
                             "big seed can outrun the CLI's half-minute.")] bool twice = false)
        {
            var live = seed < 0
                ? UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>()
                : null;
            var liveGeography = live != null ? live.Geography : null;

            LivingCity.Territory.ITerritoryGeography geography;
            int citySeed;
            string source;
            var failures = new List<string>();

            if (liveGeography != null && liveGeography.BlockIds.Count > 0)
            {
                geography = liveGeography;
                citySeed = -1;
                source = "live city";
            }
            else
            {
                citySeed = seed < 0 ? 1987 : seed;
                var dealt = DealGeography(citySeed, businesses: true);
                geography = dealt;
                source = "dealt from seed";

                // Same seed, same city: identity, order and adjacency all have to survive
                // a second deal, or every id saved in a campaign is worthless. The second
                // deal is geography only (no businesses), and it is opt-in because one
                // roll of a big seed already takes most of the CLI's half minute.
                if (twice)
                {
                    var again = DealGeography(citySeed, businesses: false);
                    if (dealt.BlockIds.Count != again.BlockIds.Count ||
                        dealt.Report.Edges != again.Report.Edges)
                        failures.Add("GEO: a second deal of seed " + citySeed +
                                     " produced a different city.");
                    else
                        for (var i = 0; i < dealt.BlockIds.Count; i++)
                            if (dealt.BlockIds[i] != again.BlockIds[i])
                            {
                                failures.Add("GEO: block identity moved between two deals " +
                                             "of seed " + citySeed + " at index " + i + ".");
                                break;
                            }
                }
            }

            var report = geography.Report;
            failures.AddRange(report.Faults);

            return new
            {
                passed = failures.Count == 0,
                seed = citySeed,
                source,
                blocks = report.Blocks,
                neighborhoods = report.Neighborhoods,
                edges = report.Edges,
                nested = report.NestedBlocks,
                isolatedBlocks = report.IsolatedBlocks,
                businessSitesPlaced = report.PlacedBusinesses,
                businessSitesUnplaced = report.UnplacedBusinesses,
                offGrid = geography.OffGridAreas
                    .Select(area => new { area.Name, area.Kind, area.Classification }).ToArray(),
                street = new
                {
                    alley = geography.Settings.AlleyWidth,
                    street = geography.Settings.StreetWidth,
                    boulevard = geography.Settings.BoulevardWidth,
                    neighbourGap = geography.Settings.NeighbourGap,
                    roadHysteresis = geography.Settings.RoadHysteresis,
                },
                unplaced = geography.UnplacedBusinesses
                    .Select(business => business.SiteId).ToArray(),
                notes = report.Notes.ToArray(),
                failures = failures.ToArray(),
                rows = rows
                    ? geography.BlockIds.Select(id =>
                    {
                        geography.TryGetBlock(id, out var block);
                        return new
                        {
                            id = id.Value,
                            name = block?.DisplayName,
                            legacy = block?.LegacyBlockId ?? -1,
                            kind = block?.SourceKind,
                            neighborhood = block?.NeighborhoodName,
                            centre = new { x = block?.Center.X ?? 0f, z = block?.Center.Z ?? 0f },
                            neighbours = geography.Neighbours(id).Count,
                            businesses = geography.BusinessesOf(id).Count,
                        };
                    }).ToArray()
                    : null,
            };
        }

        /// <summary>One quarter dealt from a seed, as geography: the same block
        /// definitions TerritoryRuntime builds, with the businesses bound to them.</summary>
        static LivingCity.Territory.TerritoryGeography DealGeography(int seed, bool businesses)
        {
            var core = new RoadDemo.CoreDistrict();
            core.Plan(null, seed);
            core.Frame = RoadDemo.DistrictFrame.Identity;

            var plan = core.Territory;
            var definitions = new List<LivingCity.Territory.TerritoryBlockDefinition>();
            if (plan != null)
                for (var i = 0; i < plan.Blocks.Count; i++)
                {
                    var block = plan.Blocks[i];
                    var quarter = plan.Quarter(block.QuarterId);
                    var bounds = core.Frame.ToWorldRect(block.LocalBounds);
                    definitions.Add(new LivingCity.Territory.TerritoryBlockDefinition(
                        LivingCity.Territory.TerritoryIdentity.ExistingBlock(block.StableId),
                        block.Id,
                        LivingCity.Territory.TerritoryIdentity.CoreNeighborhood(
                            plan.Seed, (int)block.QuarterId),
                        quarter?.Name ?? block.QuarterId.ToString(),
                        block.Name,
                        new LivingCity.Territory.TerritoryBounds(
                            bounds.xMin, bounds.yMin, bounds.width, bounds.height),
                        "CoreTerritoryPlan.StableId",
                        block.Kind));
                }

            var geography = new LivingCity.Territory.TerritoryGeography(
                definitions,
                new LivingCity.Territory.TerritoryGeographySettings(
                    RoadDemo.CoreLayout.AlleyWidth, RoadDemo.CoreLayout.StreetWidth,
                    RoadDemo.CoreLayout.BoulevardWidth));

            if (!businesses)
                return geography;

            var catalog = new LivingCity.Business.BusinessSiteCatalog();
            catalog.Add(new LivingCity.Business.ResidentialBusinessSites(
                core.ResidentialBlocks, core.Frame));
            catalog.Add(new LivingCity.Business.StandaloneBusinessSites(core));
            catalog.Add(new LivingCity.Business.CompoundBusinessSites(core, null));
            catalog.Build();

            // The flats come off the same plan at the same moment they do at Play, so a
            // command that deals a quarter can be asked about them too (EPIC 27).
            LivingCity.Property.ApartmentBuildings.Init(core.ResidentialBlocks, core.Frame);

            var directory = new LivingCity.Business.BusinessDirectory();
            LivingCity.Business.BusinessPopulation.Populate(catalog, seed, directory);
            geography.BindBusinesses(
                new LivingCity.Business.BusinessGeographySites(catalog, directory));
            return geography;
        }

        [CliCommand("gangsters_organization_tests",
                    "Run GAN-55 contracts for Boss identity, hierarchy, soft capacity, " +
                    "responsibility, recruitment, tactical projection, queries and validation.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "organization", "tests" })]
        public static object OrganizationTests()
        {
            var failures = LivingCity.Tests.OrganizationTests.Run();
            failures.AddRange(LivingCity.Tests.PersonnelTests.Run()
                .Select(failure => "Personnel regression: " + failure));
            failures.AddRange(LivingCity.Tests.GangTests.Run()
                .Select(failure => "Gang regression: " + failure));
            // The whole roster track rides along, so a later ticket cannot break an
            // earlier epic quietly. It is affordable: measured end to end outside the
            // editor, all five suites together run in 89 ms.
            failures.AddRange(LivingCity.Tests.SkillFoundationTests.Run()
                .Select(failure => "Skill regression: " + failure));
            failures.AddRange(LivingCity.Tests.LearningTests.Run()
                .Select(failure => "Learning regression: " + failure));
            failures.AddRange(LivingCity.Tests.PersonalityTests.Run()
                .Select(failure => "Personality regression: " + failure));
            failures.AddRange(LivingCity.Tests.CommandTests.Run()
                .Select(failure => "Command regression: " + failure));
            failures.AddRange(LivingCity.Tests.LoyaltyTests.Run()
                .Select(failure => "Loyalty regression: " + failure));
            failures.AddRange(LivingCity.Tests.NotabilityTests.Run()
                .Select(failure => "Notability regression: " + failure));
            // The law & order track rides along with the rest: the roster is what an
            // arrest, a sentence and a wanted level all write into, so a police change
            // that broke the books would otherwise only be found in Play.
            failures.AddRange(LivingCity.Tests.PoliceTests.Run()
                .Select(failure => "Police regression: " + failure));
            // And the crew economy: the wage table is what the roster COSTS, so a
            // change to a man's stats, his rank or his file lands on the payroll, and
            // the yardstick that says one block carries one crew rides here with it.
            failures.AddRange(LivingCity.Tests.WageTests.Run()
                .Select(failure => "Wage regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_hq_tests",
                    "Run GAN-263 contracts for the safe, headquarters report, and armory gate.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "headquarters", "tests" })]
        public static object HeadquartersTests()
        {
            var failures = LivingCity.Tests.HeadquartersTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_police_tests",
                    "Run the law & order contracts (EPICs 17-21): the fight-or-surrender " +
                    "roll, the precinct roster and its watch, the sentence table, the " +
                    "station-court-prison pipe, wanted levels, the deputy, and the exact " +
                    "GAN-315 arrest/escort/shop-entry regressions.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "police", "tests" })]
        public static object PoliceTests()
        {
            var failures = LivingCity.Tests.PoliceTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.PoliceTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_law_sheet",
                    "Print THE LAW sheet's own rows for a staged docket (EPIC 33) and " +
                    "judge them: the docket in order, the witnesses, the counsel's read, " +
                    "the cells, the wanted, and the archive after a trial on paper and a " +
                    "save-and-load. The sheet's oracle - what a contract proves here is " +
                    "what the ledger paints.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "law", "ledger" })]
        public static object LawSheetBench()
        {
            var report = LivingCity.Tests.LawSheetBench.Run();
            return new
            {
                passed = report.Failures.Count == 0,
                failures = report.Failures.ToArray(),
                sheet = report.Lines.ToArray(),
            };
        }

        [CliCommand("gangsters_skill_tests",
                    "Run EPIC 11 contracts for the skill foundation: the eleven general " +
                    "skills, hidden ceilings, the growth curve and aging.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object SkillTests()
        {
            var failures = LivingCity.Tests.SkillFoundationTests.Run();
            // The contracts and the skill names come back with the verdict on purpose:
            // a stale assembly answers ALL PASS just as cheerfully as a fresh one, and
            // these two lists are what tell them apart at a glance.
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.SkillFoundationTests.ContractNames(),
                skills = LivingCity.Tests.SkillFoundationTests.SkillNames(),
            };
        }

        [CliCommand("gangsters_learning_tests",
                    "Run EPIC 12 contracts for learning by doing: the activity table, " +
                    "the passive command drip and the danger ordering.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object LearningTests()
        {
            var failures = LivingCity.Tests.LearningTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.LearningTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_personality_tests",
                    "Run EPIC 13 contracts for personality: the six traits, their roll, " +
                    "their words, and the one door that moves them.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object PersonalityTests()
        {
            var failures = LivingCity.Tests.PersonalityTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.PersonalityTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_command_tests",
                    "Run EPIC 14 contracts for hierarchy and command limits: what a man " +
                    "can hold, what is refused him, what overload looks like, and the " +
                    "ledger's own money contracts.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object CommandTests()
        {
            var failures = LivingCity.Tests.CommandTests.Run();
            // The ledger's suite had no runner of its own: the calendar, the wages, the
            // balance arithmetic and the order book were only ever checked by whoever
            // remembered to call them. They ride here so every suite is reachable from
            // the terminal.
            failures.AddRange(LivingCity.Tests.LedgerTests.Run()
                .Select(failure => "Ledger regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.CommandTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_ledger_tests",
                    "Run the ledger contracts for calendar, income classes, wages, " +
                    "balance arithmetic and the order book.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "ledger", "tests" })]
        public static object LedgerTests()
        {
            var failures = LivingCity.Tests.LedgerTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_underworld_tests",
                    "Run EPIC 25's first contract: twenty-one houses on one set of " +
                    "rules - a roster, a safe and a wage bill apiece, no two of them " +
                    "sharing a man, and the player's own campaign dealt unchanged.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "tests" })]
        public static object UnderworldTests()
        {
            var failures = LivingCity.Tests.UnderworldTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.UnderworldTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_wage_tests",
                    "Run EPIC 24 contracts for the crew economy: the house rate, the " +
                    "life of a bargain, the short envelope, service pay, and the " +
                    "yardstick - one median block carries one crew.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "wages", "tests" })]
        public static object WageTests()
        {
            var failures = LivingCity.Tests.WageTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.WageTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_event_tests",
                    "EPIC 40 STREET-001's contracts for the street event book: the pot " +
                    "is monotone and deterministic, nothing is dealt against a deal gate, " +
                    "a card is dealt against a hold reason and waits, a held card expires " +
                    "on day +3 and cools, one card a day, a card with no speaker is not " +
                    "dealt, Answer records the choice, Esc leaves Pending unchanged, and " +
                    "the day pass rolls every house - the player included.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "events", "tests" })]
        public static object EventTests()
        {
            var failures = LivingCity.Tests.StreetEventTests.Run();
            return new
            {
                passed = failures.Count == 0,
                contracts = LivingCity.Tests.StreetEventTests.ContractNames(),
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_connection_tests",
                    "EPIC 40 CONN-001..005's contracts: two paths open per seed, the " +
                    "trade derived at read, each signal at 0 and 1, the QUIET gate, the " +
                    "man dealt and signed, one Direct man per city, THE CELL on release, " +
                    "the broker and the meeting, the test buy held for a room and the " +
                    "mind leasing one, the sting off the watch, a raid with no case, " +
                    "dirty capped sales, the terms, the paper load, the introducer's " +
                    "fortnight before Supplier only, the round trip, the mind answering " +
                    "before Walk, and Trafficking's mandatory minimum.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "connection", "tests" })]
        public static object ConnectionTests()
        {
            var failures = LivingCity.Tests.ConnectionTests.Run();
            return new
            {
                passed = failures.Count == 0,
                contracts = LivingCity.Tests.ConnectionTests.ContractNames(),
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_connection_probe",
                    "EPIC 40 CONN-005: the paper campaign, one row per house per day - " +
                    "MONEY and NAME with their lines, the QUIET gate, the pots, the stage, " +
                    "the man and his trade, the kilos, the trust, the card on the table " +
                    "with its hold and what clears it, and the last answer. The same words " +
                    "STREET TALK prints. --gang narrows to one house; -1 is every house.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "connection", "audit" })]
        public static object ConnectionProbe(
            int seed = 1987, int days = 30, int houses = 6, int gang = -1)
        {
            var lines = LivingCity.Tests.ConnectionProbe.Run(seed, days, houses, gang);
            var stages = new Dictionary<string, int>();
            var world = LivingCity.Outfit.Underworld.Deal(seed, houses);
            return new
            {
                seed,
                days,
                houses,
                rows = lines.Count,
                lines = lines.ToArray(),
            };
        }

        [CliCommand("gangsters_flat_tests",
                    "Run EPIC 27's contracts for the flats: how a door is named, what a " +
                    "flat reads as with and without a keeper, one man one flat, the card " +
                    "room's bank, the precinct's seal - and, with --seed, how a dealt " +
                    "quarter's buildings come out.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "flats", "tests" })]
        public static object FlatTests(
            [CliArg("seed", "Deal a Core quarter from this seed and count its apartment " +
                            "buildings too. -1 runs the pure contracts only.")] int seed = -1)
        {
            var failures = LivingCity.Tests.FlatTests.Run();
            object dealt = null;

            if (seed >= 0)
            {
                DealOrReadCity(seed);
                var buildings = LivingCity.Property.ApartmentBuildings.All;
                var flats = 0;
                var doorless = 0;
                var storeyless = 0;
                var addresses = new HashSet<string>();
                var duplicates = 0;
                var biggest = 0;
                foreach (var building in buildings)
                {
                    flats += building.Flats;
                    if (building.DoorsPerLanding <= 0)
                        doorless++;
                    if (building.Storeys < 2)
                        storeyless++;
                    if (!addresses.Add(building.Id.Value))
                        duplicates++;
                    if (building.Flats > biggest)
                        biggest = building.Flats;
                }

                if (buildings.Count == 0)
                    failures.Add("FLAT: the dealt quarter has no apartment buildings at all.");
                if (doorless > 0)
                    failures.Add("FLAT: " + doorless + " buildings deal no doors - the " +
                                 "shop-bay count and its harvested-door fallback both " +
                                 "came back empty.");
                if (storeyless > 0)
                    failures.Add("FLAT: " + storeyless + " buildings stand under two " +
                                 "storeys, so they hold a ground floor and nothing else.");
                if (duplicates > 0)
                    failures.Add("FLAT: " + duplicates + " buildings share an id.");

                // The same seed must deal the same building, every load: the deed book is
                // keyed on these ids and a drift moves a bought flat under a live campaign.
                var first = Snapshot();
                DealOrReadCity(seed);
                var second = Snapshot();
                if (first != second)
                    failures.Add("FLAT: the same seed dealt a different set of buildings " +
                                 "the second time.");

                dealt = new
                {
                    seed,
                    buildings = buildings.Count,
                    flats,
                    flatsPerBuilding = buildings.Count == 0 ? 0 : flats /
                        System.Math.Max(1, buildings.Count),
                    biggest,
                };
            }

            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.FlatTests.ContractNames(),
                dealt,
            };
        }

        /// <summary>Every building the plan dealt, as one string - the cheapest thing to
        /// compare two deals of the same seed with.</summary>
        static string Snapshot()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var building in LivingCity.Property.ApartmentBuildings.All)
                sb.Append(building.Id.Value).Append(':').Append(building.Storeys)
                  .Append('x').Append(building.DoorsPerLanding).Append('|');
            return sb.ToString();
        }

        [CliCommand("gangsters_round_tests",
                    "Run RIVAL-004's contracts: a collection round opened, settled, " +
                    "banked and abandoned with no city at all - and the paper clock " +
                    "worth the same money as the walk.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "racket", "tests" })]
        public static object RoundTests()
        {
            var failures = LivingCity.Tests.RoundTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_house_tests",
                    "Run RIVAL-005's contracts: one family running itself on a paper " +
                    "city - it loses a man, signs one, takes the next street, asks a " +
                    "door, collects and pays its men, for every seed 1..30.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "tests" })]
        public static object HouseTests()
        {
            var failures = LivingCity.Tests.HouseMindTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                mvp = LivingCity.Tests.HouseMindTests.Notes.ToArray(),
            };
        }

        [CliCommand("gangsters_diplomacy_tests",
                    "Run EPIC 42's contracts: the proposal book - a house asks another " +
                    "and is answered at the desk the same on two runs, the player's " +
                    "inbox lapses on day three, the same thing is not asked twice, " +
                    "money crosses through one door, a word given keeps a house off a " +
                    "street, and the book survives the file.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "tests" })]
        public static object DiplomacyTests()
        {
            var failures = LivingCity.Tests.DiplomacyTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_diplomacy_probe",
                    "EPIC 42: the table as it stands - per house, what it has asked and " +
                    "been asked and how each was answered, what it keeps off, the " +
                    "lines and pacts it is party to, and the tribute both ways. Reads, " +
                    "never repairs.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "audit" })]
        public static object DiplomacyProbe(
            [CliArg("house", "One gang id to narrow to; -1 for every house.")] int house = -1,
            [CliArg("record", "How many of the last words to print per house.")] int record = 10)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null)
                return new { ok = false, reason = "No underworld is dealt; is a scene playing?" };
            var book = underworld.Diplomacy;
            var houses = new List<object>();
            var offs = new List<(int house, LivingCity.Territory.TerritoryBlockId block, int untilDay)>();
            book.CollectKeepOffs(offs);
            for (var g = 0; g < underworld.Count; g++)
            {
                var one = underworld.Of(g);
                if (one == null || (house >= 0 && g != house))
                    continue;
                var day = one.Runner.Campaign.Day;

                var words = new List<object>();
                var count = 0;
                for (var i = book.All.Count - 1; i >= 0 && count < record; i--)
                {
                    var p = book.All[i];
                    if (p.From != g && p.To != g)
                        continue;
                    count++;
                    words.Add(new
                    {
                        id = p.Id,
                        day = p.Day,
                        from = p.From,
                        to = p.To,
                        kind = p.Kind.ToString(),
                        money = p.Terms.Money,
                        blocks = p.Terms.Blocks.ToArray(),
                        third = p.Terms.Third,
                        status = p.Status.ToString(),
                        answer = p.Answer,
                        envoy = p.Envoy,
                        inTransit = p.InTransit,
                        escrow = p.Escrow,
                    });
                }

                var keptOff = new List<object>();
                for (var i = 0; i < offs.Count; i++)
                    if (offs[i].house == g && book.IsKeptOff(g, offs[i].block, day))
                        keptOff.Add(new { block = offs[i].block.Value, untilDay = offs[i].untilDay });

                var lines = new List<object>();
                for (var i = 0; i < book.Lines.Count; i++)
                    if (book.Lines[i].Names(g))
                        lines.Add(new
                        {
                            with = book.Lines[i].A == g ? book.Lines[i].B : book.Lines[i].A,
                            block = book.Lines[i].Block,
                            untilDay = book.Lines[i].UntilDay,
                        });

                var pacts = new List<object>();
                for (var i = 0; i < book.Pacts.Count; i++)
                    if (book.Pacts[i].Names(g))
                        pacts.Add(new
                        {
                            with = book.Pacts[i].PartnerOf(g),
                            untilDay = book.Pacts[i].UntilDay,
                        });

                var tribute = new List<object>();
                for (var i = 0; i < one.Runner.Tribute.Levies.Count; i++)
                {
                    var levy = one.Runner.Tribute.Levies[i];
                    tribute.Add(new
                    {
                        to = levy.GangId,
                        amount = levy.Amount,
                        dueDay = levy.DueDay,
                        overdue = levy.Overdue,
                        pinned = levy.Pinned(day),
                    });
                }

                houses.Add(new
                {
                    house = g,
                    name = LivingCity.Gangs.GangCatalog.Names[g],
                    day,
                    safe = one.Runner.Accounts.Safe,
                    endurance = LivingCity.Outfit.HouseRelations.Endurance(
                        one.Runner.Accounts.Safe,
                        LivingCity.Outfit.Wages.DailyPayroll(one.Roster)),
                    words = words.ToArray(),
                    keptOff = keptOff.ToArray(),
                    lines = lines.ToArray(),
                    pacts = pacts.ToArray(),
                    tribute = tribute.ToArray(),
                });
            }
            return new { ok = true, proposals = book.All.Count, houses = houses.ToArray() };
        }

        [CliCommand("gangsters_relations_tests",
                    "Run RIVAL-007's contracts: where twenty-one families stand with " +
                    "one another, what each is owed by each, and the one rule the " +
                    "street reads before anybody fires.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "tests" })]
        public static object RelationsTests()
        {
            var failures = LivingCity.Tests.RelationsTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_save_tests",
                    "Run RIVAL-010's contracts: a campaign written down and read back " +
                    "is the same campaign, and goes on being the same as it is played.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "save", "tests" })]
        public static object SaveTests()
        {
            var failures = LivingCity.Tests.SaveTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_news_tests",
                    "Run EPIC 35 contracts for the public-record gate, attribution, " +
                    "06-to-06 editions, newspaper copy, determinism and v2/v3 saves.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "news", "tests" })]
        public static object NewsTests()
        {
            var failures = LivingCity.Tests.NewsTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.NewsTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_press",
                    "Print and proof an EPIC 35 edition staged as quiet, shootout, " +
                    "arrest or arson.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "news", "audit" })]
        public static object PressBench(
            [CliArg("seed", "City seed used by the newspaper compositor.")] int seed = 7,
            [CliArg("stage", "quiet | shootout | arrest | arson")] string stage = "quiet")
        {
            var records = LivingCity.Tests.NewsTests.Stage(stage);
            if (records == null)
                return new
                {
                    passed = false,
                    seed,
                    stage,
                    failures = new[]
                    {
                        "Unknown stage '" + stage +
                        "'. Use quiet, shootout, arrest or arson.",
                    },
                    headlines = Array.Empty<object>(),
                };

            var edition = LivingCity.News.Edition.Compose(seed,
                LivingCity.News.NewsDate.FromClockDay(1), 2, records);
            var failures = LivingCity.Tests.NewsTests.Proof(edition);
            var expected = string.Equals(stage, "quiet",
                StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            var local = edition.Count(row => row.Story != null);
            if (local != expected)
                failures.Add("PRESS BENCH: " + stage + " staged " + expected +
                             " public record(s), but printed " + local + ".");

            return new
            {
                passed = failures.Count == 0,
                seed,
                stage,
                failures = failures.ToArray(),
                headlines = edition.Select(row => new
                {
                    desk = row.Desk.ToString(),
                    headline = row.Text,
                    copy = row.Blurb,
                    source = row.Story != null ? row.Story.Kind.ToString() :
                             row.Historical ? "1987 wire" : "wire",
                }).ToArray(),
            };
        }

        [CliCommand("gangsters_save",
                    "Write the running campaign to a file (default: the autosave).",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "save" })]
        public static object SaveCampaign(string path = null)
        {
            var where = string.IsNullOrEmpty(path)
                ? LivingCity.Save.CampaignSave.AutosavePath
                : path;
            var refusal = LivingCity.Save.CampaignSave.Write(where);
            return new { saved = string.IsNullOrEmpty(refusal), path = where, refusal };
        }

        [CliCommand("gangsters_load",
                    "Read a campaign file and put it over the running city.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "save" })]
        public static object LoadCampaign(string path = null)
        {
            var where = string.IsNullOrEmpty(path)
                ? LivingCity.Save.CampaignSave.AutosavePath
                : path;
            var file = LivingCity.Save.CampaignSave.Read(where, out var refusal);
            if (file == null)
                return new { loaded = false, path = where, refusal };

            LivingCity.Save.CampaignSave.Apply(file);
            return new
            {
                loaded = true,
                path = where,
                day = file.day,
                citySeed = file.citySeed,
                refusal = "",
            };
        }

        [CliCommand("gangsters_underworld_sim",
                    "AI-008's yardstick: every house on the paper clock for D days on " +
                    "four blocks a house, the fortnight table once a day per house, and " +
                    "a verdict that can FAIL - a house leading no block by day 14, a " +
                    "safe under a week's payroll, a stalled round, a door demanded more " +
                    "than three times, a house that stood still a whole day. --think " +
                    "sets the cadence under measurement (A19); --sweep N runs N seeds " +
                    "and reports the distribution. The paper clock measures the books; " +
                    "the live harness measures the street.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "sim" })]
        public static object UnderworldSim(
            int seed = 1987, int days = 14, int houses = 21, int sweep = 0,
            [CliArg("think", "Game hours between one house's thinks; 0 is the model's own.")]
            float think = 0f,
            [CliArg("table", "Print the per-day table lines (they are long).")]
            bool table = false,
            [CliArg("growthDays", "A30: days of payroll the doors must cover before a " +
                                  "house signs a man. -1 leaves the model's own figure, " +
                                  "0 lifts the gate.")]
            int growthDays = -1)
        {
            if (sweep <= 0)
            {
                var one = LivingCity.Tests.UnderworldSim.Run(
                    seed, days, houses, think, growthDays);
                return new
                {
                    passed = one.Clean,
                    seed,
                    days,
                    houses,
                    thinkEveryHours = think > 0f ? think : new LivingCity.Outfit.HouseMindConfig().ThinkEveryHours,
                    limit = LivingCity.Tests.UnderworldSim.Limit,
                    negatives = one.Negatives,
                    frozenHouseDays = one.Frozen,
                    ownershipRefusals = one.OwnershipRefusals,
                    thinkMilliseconds = one.ThinkMilliseconds,
                    suppliersByDay30 = one.HousesAtSupplierByDay30,
                    buyerMoney = one.BuyerMoney,
                    table = new
                    {
                        proposals = one.ProposalsMade,
                        accepted = one.ProposalsAccepted,
                        refused = one.ProposalsRefused,
                        moneyBetweenHouses = one.MoneyBetweenHouses,
                        lines = one.LinesStanding,
                        pacts = one.PactsStanding,
                        kidnaps = one.Kidnaps,
                        ransomsPaid = one.RansomsPaid,
                    },
                    cardsDealtAnsweredExpired = one.CardsDealt + "/" + one.CardsAnswered + "/" +
                                                one.CardsExpired,
                    rivalFlatsRaided = one.RivalFlatsRaided,
                    blocksPerHouseAtDay14 = one.HousesAtFortnight > 0
                        ? System.Math.Round(
                            one.BlocksAtFortnight / (double)one.HousesAtFortnight, 2)
                        : 0.0,
                    doorsPerHouseAtDay14 = one.HousesAtFortnight > 0
                        ? System.Math.Round(
                            one.DoorsAtFortnight / (double)one.HousesAtFortnight, 2)
                        : 0.0,
                    error = one.Error,
                    failures = one.Failures.ToArray(),
                    lines = table ? one.Lines.ToArray() : new string[0],
                };
            }

            // THE SWEEP. Thirty cities, the same fortnight in each - one seed proves
            // nothing (the tally of thirty is the verdict), and the DISTRIBUTION is
            // reported, not the mean: the question is whether ANY city still has a
            // frozen house.
            var rows = new List<string>();
            var failures = new List<string>();
            var errors = new List<string>();
            var negatives = 0;
            var refused = 0;
            var frozenDays = 0;
            var citiesWithAFrozenHouse = 0;
            var citiesFailing = 0;
            var thinkMs = 0;
            var blocks = 0;
            var doors = 0;
            var counted = 0;
            var byKind = new Dictionary<string, int>();
            for (var s = 1; s <= sweep; s++)
            {
                var report = LivingCity.Tests.UnderworldSim.Run(
                    s, days, houses, think, growthDays);
                negatives += report.Negatives;
                refused += report.OwnershipRefusals;
                frozenDays += report.Frozen;
                thinkMs += report.ThinkMilliseconds;
                blocks += report.BlocksAtFortnight;
                doors += report.DoorsAtFortnight;
                counted += report.HousesAtFortnight;
                if (report.Frozen > 0)
                    citiesWithAFrozenHouse++;
                if (!report.Clean)
                    citiesFailing++;
                if (!string.IsNullOrEmpty(report.Error))
                    errors.Add("seed " + s + ": " + report.Error);
                for (var i = 0; i < report.Failures.Count; i++)
                {
                    failures.Add(report.Failures[i]);
                    var kind = report.Failures[i].Substring(
                        report.Failures[i].IndexOf(':') + 2);
                    kind = kind.Split('(')[0]
                        .Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                    byKind.TryGetValue(kind, out var count);
                    byKind[kind] = count + 1;
                }
                if (table)
                    rows.AddRange(report.Lines);
            }

            var kinds = new List<string>();
            foreach (var pair in byKind)
                kinds.Add(pair.Value + " x " + pair.Key);
            kinds.Sort();

            return new
            {
                passed = errors.Count == 0 && refused == 0 && failures.Count == 0,
                seeds = sweep,
                days,
                houses,
                thinkEveryHours = think > 0f ? think : new LivingCity.Outfit.HouseMindConfig().ThinkEveryHours,
                limit = LivingCity.Tests.UnderworldSim.Limit,
                citiesFailing,
                citiesWithAFrozenHouse,
                frozenHouseDays = frozenDays,
                negatives,
                ownershipRefusals = refused,
                thinkMilliseconds = thinkMs,
                blocksPerHouseAtDay14 = counted > 0
                    ? System.Math.Round(blocks / (double)counted, 2) : 0.0,
                doorsPerHouseAtDay14 = counted > 0
                    ? System.Math.Round(doors / (double)counted, 2) : 0.0,
                failuresByKind = kinds.ToArray(),
                errors = errors.ToArray(),
                failures = failures.ToArray(),
                lines = rows.ToArray(),
            };
        }

        /// <summary>
        /// AI-000. THE HOUSE PROBE: a mind trace in an ordinary Play. Per house the
        /// last think with its refusals and the fifty before it, every job on the book
        /// and whether its crew still exists, every round with its stall metres, and
        /// every unit's Surrendered / Billeted state - the per-unit line being the point,
        /// because a partially arrested crew standing frozen at a door and a failed walk
        /// are indistinguishable in a save file and need opposite fixes. Reads and never
        /// repairs.
        /// </summary>
        [CliCommand("gangsters_house_probe",
                    "AI-000: per house, the last thinks with the gateway's refusals, " +
                    "the phase, the book (orphan jobs marked), every round with its " +
                    "stall metres, and every unit's Surrendered/Billeted state. Reads, " +
                    "never repairs.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "audit" })]
        public static object HouseProbe(
            [CliArg("house", "One gang id to narrow to; -1 for every house.")] int house = -1,
            [CliArg("thinks", "How many of the last thinks to print per house.")] int thinks = 5)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            var crews = UnityEngine.Object.FindAnyObjectByType<RoadDemo.DemoCrews>();
            if (underworld == null)
                return new { ok = false, reason = "No underworld is dealt; is a scene playing?" };

            var hour = runtime != null ? runtime.GameHour : 0.0;
            var config = runtime != null ? runtime.MindConfig : LivingCity.Outfit.HouseMindConfig.Default;
            var houses = new List<object>();
            var scores = new List<(LivingCity.Territory.TerritoryBlockId block, int score, bool open)>();
            var holds = new List<(string key, double until)>();
            for (var g = 0; g < underworld.Count; g++)
            {
                var one = underworld.Of(g);
                if (one == null || (house >= 0 && g != house))
                    continue;
                var roster = one.Roster;

                int active = 0, jailed = 0, hurt = 0, dead = 0, wanted = 0;
                for (var i = 0; roster != null && i < roster.Members.Count; i++)
                {
                    var man = roster.Members[i];
                    if (man.Status == LivingCity.Personnel.CharacterStatus.Active) active++;
                    else if (man.Status == LivingCity.Personnel.CharacterStatus.Jailed) jailed++;
                    else if (man.Status == LivingCity.Personnel.CharacterStatus.Hospitalized ||
                             man.Status == LivingCity.Personnel.CharacterStatus.Taken) hurt++;
                    else dead++;
                    if (!man.Gone && man.WantedLevel > 0) wanted++;
                }

                var jobs = new List<object>();
                var book = one.Runner.Book;
                for (var i = 0; i < book.Jobs.Count; i++)
                {
                    var job = book.Jobs[i];
                    jobs.Add(new
                    {
                        id = job.Id,
                        type = job.Type.ToString(),
                        stage = job.Stage.ToString(),
                        crew = job.CrewId,
                        issuedDay = job.IssuedDay,
                        target = string.IsNullOrEmpty(job.TargetBusinessId)
                            ? job.TargetLabel : job.TargetBusinessId,
                        orphan = roster?.FindCrew(job.CrewId) == null,
                    });
                }

                var history = runtime != null ? runtime.ThinkHistory(g) : null;
                var last = new List<object>();
                var acceptedToday = 0;
                var today = (int)(hour / 24.0) + 1;
                for (var i = 0; history != null && i < history.Count; i++)
                {
                    var record = history[i];
                    if (record.Day == today)
                        acceptedToday += record.Accepted;
                    if (i < history.Count - System.Math.Max(0, thinks))
                        continue;
                    var lines = new List<string>();
                    for (var l = 0; l < record.Lines.Count; l++)
                        lines.Add((record.Lines[l].Carried ? "OK  " : "NO  ") +
                                  record.Lines[l].Intent + " · " + record.Lines[l].Reason);
                    last.Add(new
                    {
                        hour = System.Math.Round(record.Hour, 1),
                        day = record.Day,
                        tier = record.Tier,
                        ms = System.Math.Round(record.Milliseconds, 2),
                        lines = lines.ToArray(),
                    });
                }

                var phase = "";
                var neighbours = new List<string>();
                var cells = new List<string>();
                var card = "";
                var connection = "";
                var blocks = new List<string>();
                // The player's house is looked at too (EPIC 40, PRE-002): the same
                // view lines it prints for a rival.
                if (runtime != null)
                {
                    var view = runtime.Peek(one);
                    if (view != null)
                    {
                        for (var i = 0; i < view.Blocks.Count; i++)
                            blocks.Add(view.Blocks[i].Value + " doors " +
                                       view.Businesses(view.Blocks[i]).Count + " attention " +
                                       System.Math.Round(view.PoliceAttention(view.Blocks[i]), 1));
                        if (view.Card != null)
                            card = view.Card.Id + " by " + view.Card.SpeakerName +
                                   (view.CardHold != LivingCity.Outfit.HoldReason.None
                                       ? " HELD: " + LivingCity.Outfit.HoldReasons.Line(view.CardHold)
                                       : "");
                        var paper = view.Connection;
                        if (paper != null)
                            connection = paper.Stage + " line " + paper.Line + " grade " +
                                         paper.Grade + " kilos " + paper.Kilos + " trust " +
                                         paper.Trust + " man " + paper.ManId;
                        phase = LivingCity.Outfit.HouseMind.PhaseOf(view, config).ToString();
                        LivingCity.Outfit.HouseMind.NeighbourScores(view, config, scores);
                        for (var i = 0; i < scores.Count; i++)
                            neighbours.Add(scores[i].block.Value + " " + scores[i].score +
                                           (scores[i].open ? " open" : " held"));
                        for (var i = 0; i < view.Cells.Count; i++)
                            cells.Add(view.Cells[i].Rank + " #" + view.Cells[i].CharacterId +
                                      " since day " + view.Cells[i].HeldSinceDay + " · $" +
                                      view.Cells[i].BailPrice + " · " +
                                      (view.Cells[i].Bailable ? "bailable" : view.Cells[i].Refusal));
                    }
                    runtime.CollectBackoffs(g, holds);
                }
                var heldBack = new List<string>();
                for (var i = 0; i < holds.Count; i++)
                    heldBack.Add(holds[i].key + " until " +
                                 (double.IsPositiveInfinity(holds[i].until)
                                     ? "the case changes"
                                     : System.Math.Round(holds[i].until, 1).ToString()));

                houses.Add(new
                {
                    gang = g,
                    name = LivingCity.Gangs.GangCatalog.Names[g],
                    player = one.IsPlayer,
                    finished = one.Finished,
                    extinct = one.Extinct,
                    headless = one.Headless,
                    safe = one.Runner.Accounts.Safe,
                    payroll = LivingCity.Outfit.Wages.DailyPayroll(roster),
                    endurance = LivingCity.Outfit.HouseRelations.Endurance(
                        one.Runner.Accounts.Safe, LivingCity.Outfit.Wages.DailyPayroll(roster)),
                    men = new { active, jailed, hurt, dead, wanted },
                    crews = roster != null ? roster.Crews.Count : 0,
                    phase,
                    nextThinkHour = System.Math.Round(one.NextThinkHour, 1),
                    thinks = runtime != null ? runtime.ThinksOf(g) : 0,
                    acceptedToday,
                    quietThinks = one.QuietThinks,
                    jobs = jobs.ToArray(),
                    lastThinks = last.ToArray(),
                    neighbours = neighbours.ToArray(),
                    cells = cells.ToArray(),
                    heldBack = heldBack.ToArray(),
                    blocks = blocks.ToArray(),
                    card,
                    connection,
                    wire = one.Runner.Events.Wire.Count > 0
                        ? one.Runner.Events.Wire[one.Runner.Events.Wire.Count - 1].Text : "",
                });
            }

            var rounds = new List<object>();
            if (runtime != null)
            {
                var readings = new List<RoadDemo.TerritoryRuntime.RoundReading>();
                runtime.DescribeRounds(readings);
                var seen = new HashSet<LivingCity.Territory.TerritoryRound>();
                for (var i = 0; i < readings.Count; i++)
                {
                    var r = readings[i];
                    seen.Add(r.Round);
                    if (house >= 0 && r.Round.House.Value != house)
                        continue;
                    rounds.Add(RoundRow(r.Round, hour, "street", r.WalkersStand, r.CarrierWalks,
                        r.Metres, r.Billeted));
                }
                var all = runtime.Rounds != null ? runtime.Rounds.Rounds : null;
                for (var i = 0; all != null && i < all.Count; i++)
                {
                    if (seen.Contains(all[i]) || (house >= 0 && all[i].House.Value != house))
                        continue;
                    rounds.Add(RoundRow(all[i], hour, "paper", false, false, -1f, false));
                }
            }

            var units = new List<object>();
            if (crews != null)
                for (var i = 0; i < crews.Units.Count; i++)
                {
                    var unit = crews.Units[i];
                    if (unit == null || unit.IsPolice || unit.Faction < 0)
                        continue;
                    if (house >= 0 && unit.Faction != house)
                        continue;
                    units.Add(new
                    {
                        faction = unit.Faction,
                        crew = unit.CrewId,
                        detachment = unit.IsDetachment,
                        alive = unit.Standing(),
                        wiped = unit.Wiped,
                        surrendered = unit.Surrendered,
                        inCustody = unit.InCustody,
                        retreated = unit.Retreated,
                        fleeing = unit.Fleeing,
                        billeted = RoadDemo.CrewQuarters.Billeted(unit),
                        inside = RoadDemo.CrewQuarters.Inside(unit),
                        marchOut = RoadDemo.CrewJobs.MarchOutstanding(unit.CrewId),
                        guarding = RoadDemo.CrewJobs.TryGetWatch(unit.CrewId, out var door)
                            ? door.Value : "",
                        at = new
                        {
                            x = System.Math.Round(unit.Position.x, 1),
                            z = System.Math.Round(unit.Position.z, 1),
                        },
                    });
                }

            return new
            {
                ok = true,
                gameHour = System.Math.Round(hour, 2),
                day = (int)(hour / 24.0) + 1,
                thinkEveryHours = config.ThinkEveryHours,
                lastThinkMs = runtime != null ? runtime.ThinkMilliseconds : 0f,
                houses = houses.ToArray(),
                rounds = rounds.ToArray(),
                units = units.ToArray(),
            };
        }

        /// <summary>
        /// AI-008 PART TWO, THE LIVE HALF: the plan's §1.1 table for the running city,
        /// one line per house, on demand - the user plays and asks; nothing here enters
        /// Play. The same columns the paper yardstick prints, read off the real ledgers,
        /// with the two the paper clock cannot have: arrests and rounds lost today.
        /// </summary>
        [CliCommand("gangsters_house_table",
                    "AI-008: the fortnight table for the live city, one line per house " +
                    "- men, crews, doors, blocks, money, the book, the rounds, the worst " +
                    "door, arrests and rounds lost today, the biggest grudge, accepted " +
                    "intents today, the phase. Reads, never repairs.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "underworld", "audit" })]
        public static object HouseTable()
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            if (underworld == null || runtime == null)
                return new { ok = false, reason = "No underworld or territory runtime is running." };

            var hour = runtime.GameHour;
            var day = (int)(hour / 24.0) + 1;
            var config = runtime.MindConfig;
            var racket = runtime.Racket;
            var control = runtime.Control;
            var lines = new List<string>();
            var frozen = new List<string>();
            var rounds = runtime.Rounds != null ? runtime.Rounds.Rounds : null;

            for (var g = 0; g < underworld.Count; g++)
            {
                var house = underworld.Of(g);
                if (house?.Roster == null)
                    continue;
                var roster = house.Roster;
                var mine = new LivingCity.Territory.TerritoryGangId(g);

                int active = 0, jailed = 0, hurt = 0, dead = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var man = roster.Members[i];
                    if (man.Status == LivingCity.Personnel.CharacterStatus.Active) active++;
                    else if (man.Status == LivingCity.Personnel.CharacterStatus.Jailed) jailed++;
                    else if (man.Status == LivingCity.Personnel.CharacterStatus.Hospitalized ||
                             man.Status == LivingCity.Personnel.CharacterStatus.Taken) hurt++;
                    else dead++;
                }

                var crews = 0;
                var full = 0;
                for (var c = 0; c < roster.Crews.Count; c++)
                {
                    var crew = roster.Crews[c];
                    if (crew.LieutenantId == roster.BossId)
                        continue;
                    crews++;
                    var hoods = 0;
                    for (var h = 0; h < crew.HoodIds.Count; h++)
                    {
                        var man = roster.Find(crew.HoodIds[h]);
                        if (man != null && !man.Gone &&
                            man.Status == LivingCity.Personnel.CharacterStatus.Active)
                            hoods++;
                    }
                    if (hoods >= config.HoodsPerCrew)
                        full++;
                }

                int paying = 0, hesitant = 0, refused = 0, worst = 0;
                var protectedBlocks = new HashSet<string>();
                if (racket != null)
                {
                    var ids = racket.Businesses;
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var state = racket.StateOf(ids[i], mine);
                        if (state == LivingCity.Territory.TerritoryProtectionState.Compliant) paying++;
                        else if (state == LivingCity.Territory.TerritoryProtectionState.Hesitant) hesitant++;
                        else if (state == LivingCity.Territory.TerritoryProtectionState.Defiant) refused++;
                        if (racket.TryGetRelationship(ids[i], mine, out var row) &&
                            row.Demands > worst)
                            worst = row.Demands;
                    }
                }

                var led = 0;
                var states = new List<string>();
                if (control != null)
                    for (var b = 0; b < control.Blocks.Count; b++)
                        if (control.LeaderOf(control.Blocks[b]) == mine)
                        {
                            led++;
                            states.Add(control.StateOf(control.Blocks[b]).ToString().Substring(0, 3));
                        }

                var jobs = 0;
                var oldestJob = 0;
                for (var i = 0; i < house.Runner.Book.Jobs.Count; i++)
                {
                    var job = house.Runner.Book.Jobs[i];
                    if (job.Stage == LivingCity.Outfit.JobStage.Finished)
                        continue;
                    jobs++;
                    if (day - job.IssuedDay > oldestJob)
                        oldestJob = day - job.IssuedDay;
                }

                var outRounds = 0;
                var gap = 0.0;
                for (var r = 0; rounds != null && r < rounds.Count; r++)
                {
                    if (rounds[r].House != mine)
                        continue;
                    outRounds++;
                    if (hour - rounds[r].LastMoveAt > gap)
                        gap = hour - rounds[r].LastMoveAt;
                }

                var maxGrievance = 0f;
                var against = -1;
                var wars = 0;
                for (var other = 0; other < underworld.Count; other++)
                {
                    if (other == g || underworld.Of(other) == null)
                        continue;
                    if (underworld.Relations.StanceBetween(g, other) == LivingCity.Outfit.Stance.War)
                        wars++;
                    var owed = underworld.Relations.Grievance(g, other);
                    if (owed > maxGrievance)
                    {
                        maxGrievance = owed;
                        against = other;
                    }
                }

                var accepted = 0;
                var history = runtime.ThinkHistory(g);
                for (var i = 0; i < history.Count; i++)
                    if (history[i].Day == day)
                        accepted += history[i].Accepted;

                var phase = "-";
                if (!house.IsPlayer && !house.Finished)
                {
                    var view = runtime.Peek(house);
                    if (view != null)
                        phase = LivingCity.Outfit.HouseMind.PhaseOf(view, config).ToString();
                }

                var line = "day " + day + " house " + g + " " +
                           LivingCity.Gangs.GangCatalog.Names[g] +
                           " men " + active + "/" + jailed + "/" + hurt + "/" + dead +
                           " crews " + crews + "/" + full + "full" +
                           " doors " + paying + "/" + hesitant + "/" + refused +
                           " blocks " + led + (states.Count > 0 ? "(" + string.Join(",", states) + ")" : "") +
                           " safe " + house.Runner.Accounts.Safe +
                           " payroll " + LivingCity.Outfit.Wages.DailyPayroll(roster) +
                           " power " + runtime.PowerOf(mine) +
                           " jobs " + jobs + "/" + oldestJob + "d" +
                           " rounds " + outRounds + "/" + (int)gap + "h" +
                           " worstdoor " + worst +
                           " arrests " + runtime.CountedToday(g, "arrests") +
                           " lost " + runtime.CountedToday(g, "lost") +
                           " grudge " + (int)maxGrievance + (against >= 0 ? "@" + against : "") +
                           " wars " + wars +
                           " accepted " + accepted +
                           " phase " + phase +
                           (house.Finished ? " FINISHED" : "");
                lines.Add(line);
                if (!house.IsPlayer && !house.Finished && accepted == 0 &&
                    phase == "Land" && hour - (day - 1) * 24.0 >= 12.0)
                    frozen.Add("house " + g + ": ground to take and nothing accepted by " +
                               (int)(hour - (day - 1) * 24.0) + ":00");
            }

            return new
            {
                ok = true,
                gameHour = System.Math.Round(hour, 2),
                day,
                thinkEveryHours = config.ThinkEveryHours,
                stoji = frozen.ToArray(),
                lines = lines.ToArray(),
            };
        }

        static object RoundRow(LivingCity.Territory.TerritoryRound round, double hour,
            string clock, bool walkersStand, bool carrierWalks, float metres, bool billeted) =>
            new
            {
                house = round.House.Value,
                crew = round.CrewId,
                kind = round.Kind.ToString(),
                origin = round.Origin.ToString(),
                clock,
                block = round.BlockId.Value,
                stop = round.StopIndex + "/" + round.Stops.Count,
                stage = round.Stage.ToString(),
                carried = round.Carried,
                missed = round.Missed,
                openedAt = System.Math.Round(round.OpenedAt, 1),
                lastMoveAt = System.Math.Round(round.LastMoveAt, 1),
                stalledHours = System.Math.Round(hour - round.LastMoveAt, 2),
                inTheDoor = round.InTheDoor,
                collector = round.CollectorId,
                carrierWalks,
                walkersStand,
                billeted,
                metresToStop = System.Math.Round(metres, 1),
            };

        [CliCommand("gangsters_loyalty_tests",
                    "Run EPIC 15 contracts for loyalty, promotion and betrayal: who a " +
                    "man answers to, what moves it, who walks and who goes with him.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object LoyaltyTests()
        {
            var failures = LivingCity.Tests.LoyaltyTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.LoyaltyTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_notability_tests",
                    "Run EPIC 16 contracts for the ledger's notability and a man's " +
                    "history with the outfit: the fold, the fade, the sort and the cull.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "skill", "tests" })]
        public static object NotabilityTests()
        {
            var failures = LivingCity.Tests.NotabilityTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                contracts = LivingCity.Tests.NotabilityTests.ContractNames(),
            };
        }

        [CliCommand("gangsters_organization_audit",
                    "Validate the live organization graph without repairing it.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "organization", "audit" })]
        public static object OrganizationAudit()
        {
            var director = UnityEngine.Object.FindAnyObjectByType<
                LivingCity.Gameplay.PersonnelDirector>();
            var failures = new List<string>();
            if (director == null)
                failures.Add("ORG: no PersonnelDirector exists in the loaded scene.");
            else
                director.ValidateOrganization(failures);
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_presence_tests",
                    "Run GAN-79 contracts for Outfit x block Presence: physical bodies, rank and " +
                    "activity weighting, group aggregation, recent memory, decay, rival symmetry " +
                    "and the player's qualitative reading.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "presence", "tests" })]
        public static object PresenceTests()
        {
            var failures = LivingCity.Tests.PresenceTests.Run();
            failures.AddRange(LivingCity.Tests.TerritoryFoundationTests.Run()
                .Select(failure => "Territory regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// What a block's Presence is actually made of, family by family: the men standing
        /// there, what each is worth and why, what the block still remembers, and the sum
        /// those parts make. Report only - it never ticks the simulation to get a number.
        /// </summary>
        [CliCommand("gangsters_presence_audit",
                    "Break down live Presence per family for a block (or the busiest blocks in " +
                    "the city) into contributors, rank and activity weights, memory and total.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "presence", "audit" })]
        public static object PresenceAudit(
            [CliArg("block", "Canonical block id, or the legacy plan index, to break down. " +
                             "Omitted, the blocks carrying the most Presence are reported.")]
            string block = "",
            [CliArg("limit", "How many blocks to report when none is named.")] int limit = 5)
        {
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            var presence = runtime?.Presence;
            if (runtime == null || presence == null)
                return new { ok = false, reason = "No TerritoryRuntime is running in this scene." };

            var wanted = new List<LivingCity.Territory.TerritoryBlockId>();
            if (!string.IsNullOrEmpty(block))
            {
                if (int.TryParse(block, out var legacy) &&
                    runtime.TryGetBlock(legacy, out var byIndex))
                    wanted.Add(byIndex);
                else
                    wanted.Add(new LivingCity.Territory.TerritoryBlockId(block));
            }
            else
            {
                var ranked = new List<LivingCity.Territory.TerritoryBlockId>(presence.Blocks);
                var gangs = new List<LivingCity.Territory.TerritoryGangPresence>();
                ranked.Sort((left, right) => Weight(presence, right, gangs)
                    .CompareTo(Weight(presence, left, gangs)));
                for (var i = 0; i < ranked.Count && i < System.Math.Max(1, limit); i++)
                    wanted.Add(ranked[i]);
            }

            var rows = new List<object>();
            var gangScratch = new List<LivingCity.Territory.TerritoryGangPresence>();
            var contributors = new List<LivingCity.Territory.TerritoryPresenceContributor>();
            for (var i = 0; i < wanted.Count; i++)
            {
                var blockId = wanted[i];
                runtime.DebugTruth.TryGetBlock(blockId, out var truth);
                presence.CollectGangs(blockId, gangScratch);
                var families = new List<object>();
                for (var g = 0; g < gangScratch.Count; g++)
                {
                    var gang = gangScratch[g];
                    presence.CollectContributors(blockId, gang.GangId, contributors);
                    var men = new List<object>();
                    var summed = 0f;
                    for (var c = 0; c < contributors.Count; c++)
                    {
                        var man = contributors[c];
                        summed += man.Contribution;
                        men.Add(new
                        {
                            character = man.CharacterId.Value,
                            name = man.DisplayName,
                            crew = man.GroupId.ToString(),
                            rank = man.Rank.ToString(),
                            activity = man.Activity.ToString(),
                            contribution = man.Contribution,
                        });
                    }

                    families.Add(new
                    {
                        gang = gang.GangId.Value,
                        physical = gang.Physical,
                        residual = gang.Residual,
                        total = gang.Total,
                        contributors = men.ToArray(),
                        // The sum of the parts, printed beside the total so a reader can
                        // see them reconcile rather than take the total on trust.
                        contributorSum = summed,
                        reconciles = System.Math.Abs(
                            summed + gang.Residual - gang.Total) < 0.01f ||
                            gang.Total >= presence.Config.PresenceCap - 0.01f,
                    });
                }

                rows.Add(new
                {
                    block = blockId.Value,
                    name = truth?.Definition.DisplayName ?? "",
                    families = families.ToArray(),
                });
            }

            return new
            {
                ok = true,
                blocksWithPresence = presence.Blocks.Count,
                config = new
                {
                    pointsPerContributor = presence.Config.PointsPerContributor,
                    hood = presence.Config.HoodWeight,
                    lieutenant = presence.Config.LieutenantWeight,
                    boss = presence.Config.BossWeight,
                    transit = presence.Config.TransitWeight,
                    moving = presence.Config.MovingWeight,
                    stationed = presence.Config.StationedWeight,
                    cap = presence.Config.PresenceCap,
                    residualDepositPerHour = presence.Config.ResidualDepositPerHour,
                    residualCap = presence.Config.ResidualCap,
                    residualHalfLifeHours = presence.Config.ResidualHalfLifeHours,
                },
                blocks = rows.ToArray(),
            };
        }

        static float Weight(
            LivingCity.Territory.TerritoryPresenceLedger presence,
            LivingCity.Territory.TerritoryBlockId blockId,
            List<LivingCity.Territory.TerritoryGangPresence> scratch)
        {
            presence.CollectGangs(blockId, scratch);
            var total = 0f;
            for (var i = 0; i < scratch.Count; i++)
                total += scratch[i].Total;
            return total;
        }

        [CliCommand("gangsters_fear_tests",
                    "Run GAN-90 contracts for Gang x Block fear: the act model, severity and " +
                    "visibility weighting, in-block propagation, memory, decay, ignored defiance, " +
                    "the police counterweight and the player's qualitative reading.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "fear", "tests" })]
        public static object FearTests()
        {
            var failures = LivingCity.Tests.FearTests.Run();
            failures.AddRange(LivingCity.Tests.PresenceTests.Run()
                .Select(failure => "Presence regression: " + failure));
            failures.AddRange(LivingCity.Tests.TerritoryFoundationTests.Run()
                .Select(failure => "Territory regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// Why a street is afraid: every act it still remembers, what each was worth when
        /// it happened and what is left of it now, so the current number can be explained
        /// from its history alone. Report only.
        /// </summary>
        [CliCommand("gangsters_fear_audit",
                    "Break down live Fear per family for a block (or the most frightened blocks) " +
                    "into remembered acts, their decay and the police attention on the block.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "fear", "audit" })]
        public static object FearAudit(
            [CliArg("block", "Canonical block id, or the legacy plan index. Omitted, the most " +
                             "frightened blocks are reported.")]
            string block = "",
            [CliArg("limit", "How many blocks to report when none is named.")] int limit = 5)
        {
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            var fear = runtime?.Fear;
            if (runtime == null || fear == null)
                return new { ok = false, reason = "No TerritoryRuntime is running in this scene." };

            var now = runtime.GameHour;
            var wanted = new List<LivingCity.Territory.TerritoryBlockId>();
            if (!string.IsNullOrEmpty(block))
            {
                if (int.TryParse(block, out var legacy) &&
                    runtime.TryGetBlock(legacy, out var byIndex))
                    wanted.Add(byIndex);
                else
                    wanted.Add(new LivingCity.Territory.TerritoryBlockId(block));
            }
            else
            {
                var ranked = new List<LivingCity.Territory.TerritoryBlockId>(fear.Blocks);
                ranked.Sort((left, right) =>
                    fear.BlockFear(right, now).CompareTo(fear.BlockFear(left, now)));
                for (var i = 0; i < ranked.Count && i < System.Math.Max(1, limit); i++)
                    wanted.Add(ranked[i]);
            }

            var rows = new List<object>();
            var gangs = new List<LivingCity.Territory.TerritoryGangValue>();
            var memory = new List<LivingCity.Territory.TerritoryFearMemoryEntry>();
            for (var i = 0; i < wanted.Count; i++)
            {
                var blockId = wanted[i];
                runtime.DebugTruth.TryGetBlock(blockId, out var truth);
                fear.CollectGangs(blockId, now, gangs);
                var families = new List<object>();
                for (var g = 0; g < gangs.Count; g++)
                {
                    var gang = gangs[g];
                    fear.CollectMemory(blockId, gang.GangId, memory);
                    var acts = new List<object>();
                    var summed = 0f;
                    for (var m = 0; m < memory.Count; m++)
                    {
                        var entry = memory[m];
                        var left = entry.At(now);
                        summed += left;
                        acts.Add(new
                        {
                            category = entry.Category.ToString(),
                            visibility = entry.Visibility.ToString(),
                            business = entry.BusinessId.Value,
                            atGameHour = entry.GameHour,
                            worth = entry.Amount,
                            remaining = left,
                            halfLifeHours = entry.HalfLifeHours,
                        });
                    }

                    families.Add(new
                    {
                        gang = gang.GangId.Value,
                        fear = gang.Value,
                        rememberedSum = summed,
                        reconciles = System.Math.Abs(summed - gang.Value) < 0.05f ||
                                     gang.Value >= fear.Config.FearCap - 0.05f,
                        acts = acts.ToArray(),
                    });
                }

                rows.Add(new
                {
                    block = blockId.Value,
                    name = truth?.Definition.DisplayName ?? "",
                    localFear = fear.BlockFear(blockId, now),
                    policeAttention = fear.PoliceAttention(blockId, now),
                    presenceScale = fear.PresenceScale(blockId, now),
                    families = families.ToArray(),
                });
            }

            var open = new List<object>();
            for (var i = 0; i < fear.OpenDefiances.Count; i++)
            {
                var watch = fear.OpenDefiances[i];
                open.Add(new
                {
                    gang = watch.GangId.Value,
                    block = watch.BlockId.Value,
                    business = watch.BusinessId.Value,
                    openedAt = watch.OpenedAt,
                    hoursLeft = fear.Config.DefianceWindowHours - (now - watch.OpenedAt),
                });
            }

            var table = new List<object>();
            foreach (var category in LivingCity.Territory.TerritoryFearConfig.Categories)
            {
                var impact = fear.Config.Of(category);
                table.Add(new
                {
                    category = category.ToString(),
                    impact = impact.Impact,
                    halfLifeHours = impact.MemoryHalfLifeHours,
                    policeWeight = impact.PoliceWeight,
                });
            }

            return new
            {
                ok = true,
                gameHour = now,
                blocksWithFear = fear.Blocks.Count,
                config = new
                {
                    hidden = fear.Config.HiddenWeight,
                    seen = fear.Config.SeenWeight,
                    publicly = fear.Config.PublicWeight,
                    propagationFraction = fear.Config.PropagationFraction,
                    fearCap = fear.Config.FearCap,
                    defianceWindowHours = fear.Config.DefianceWindowHours,
                    policeAttentionCap = fear.Config.PoliceAttentionCap,
                    policeAttentionHalfLifeHours = fear.Config.PoliceAttentionHalfLifeHours,
                    policeEscalation = fear.Config.PoliceEscalation,
                    presenceFloor = fear.Config.PresenceFloor,
                    categories = table.ToArray(),
                },
                openDefiances = open.ToArray(),
                blocks = rows.ToArray(),
            };
        }

        [CliCommand("gangsters_scenario_takeover",
                    "TEST-001: a neutral street taken the only way there is - men, fear, " +
                    "shops that pay - with no capture and no claim anywhere in it.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioTakeover() => Scenario(LivingCity.Tests.ScenarioTests.Takeover());

        [CliCommand("gangsters_scenario_withdrawal",
                    "TEST-002: the men go home; Presence fades without wiping what was earned.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioWithdrawal() =>
            Scenario(LivingCity.Tests.ScenarioTests.Withdrawal());

        [CliCommand("gangsters_scenario_contest",
                    "TEST-003: a rival works the same street by the same rules until it is a fight.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioContest() => Scenario(LivingCity.Tests.ScenarioTests.Contest());

        [CliCommand("gangsters_scenario_loss",
                    "TEST-004: ground goes the way it came - men gone, shops turned, street lost.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioLoss() => Scenario(LivingCity.Tests.ScenarioTests.Loss());

        [CliCommand("gangsters_scenario_responsibility",
                    "TEST-005/006: paperwork is not ground - a block on paper produces no " +
                    "Presence, no fear and no control.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioResponsibility() =>
            Scenario(LivingCity.Tests.ScenarioTests.Responsibility());

        [CliCommand("gangsters_scenario_boss_capacity",
                    "TEST-005: the Boss holds men directly and ground on his paper up to " +
                    "his own ceiling - and the next block is refused, by name.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioBossCapacity() =>
            Scenario(LivingCity.Tests.ScenarioTests.BossCapacity());

        [CliCommand("gangsters_scenario_lieutenant_load",
                    "TEST-006: a branch loaded to the config's fifty men and three blocks - " +
                    "the load READ at the cap, never paid for by a hidden penalty.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioLieutenantLoad() =>
            Scenario(LivingCity.Tests.ScenarioTests.LieutenantLoad());

        [CliCommand("gangsters_scenario_ui_authority",
                    "TEST-007: nothing the player can see is anything the player can write.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioUiAuthority() =>
            Scenario(LivingCity.Tests.ScenarioTests.UiAuthority());

        /// <summary>
        /// TEST-008. Ground is taken by simulation or it is not taken: this reads the tree
        /// and names anything that still writes ownership behind the simulation's back.
        /// An audit that repaired what it found would hide the very fault it is for.
        /// </summary>
        [CliCommand("gangsters_scenario_capture_audit",
                    "TEST-008: name every path that still claims ground directly - a marker " +
                    "write, a revived TAKE IT, or a public owner field on a territory type.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario", "audit" })]
        public static object ScenarioCaptureAudit()
        {
            var failures = new List<string>();
            var root = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets");

            // 1. Nobody writes the deed directly any more. The marker keeps the field for
            //    the legacy front/presentation systems, and the population pass sets it -
            //    a write anywhere in gameplay is the fault this audit is for.
            var allowed = new[]
            {
                "Assets/Scripts/Entities/BusinessMarker.cs",
                "Assets/Scripts/Gameplay/PropertyDirector.cs",
                "Assets/Scripts/Business/",
                // This file: the audit's own patterns are not the thing it hunts.
                "Assets/Scripts/Editor/PipelineCommands.cs",
            };
            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs",
                         System.IO.SearchOption.AllDirectories))
            {
                var relative = file.Substring(
                    System.IO.Directory.GetCurrentDirectory().Length + 1).Replace('\\', '/');
                var skip = false;
                for (var i = 0; i < allowed.Length; i++)
                    skip |= relative.StartsWith(allowed[i], StringComparison.Ordinal);
                if (skip)
                    continue;

                var lines = System.IO.File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // An assignment, not a comparison: "a.GangId == b" is a question and
                    // "a.GangId = b" is a claim, and only the second one is a fault.
                    if (line.Contains(".GangId =") && !line.Contains(".GangId ==") &&
                        line.Contains("Business"))
                        failures.Add(relative + ":" + (i + 1) +
                                     " writes a business deed directly.");
                    // THE ROW ITSELF, not the words. What was taken out was a menu row
                    // reading exactly "TAKE IT" (TurfMapPanel, b80ed6ab) that handed a
                    // building over on a click. Matching the bare phrase matched prose
                    // in a doc comment ("NOBODY TO TAKE IT") and the armory's own "TAKE
                    // IT BACK", which returns a gun to the safe and claims no ground at
                    // all - three false faults that had this audit red at HEAD. The
                    // quoted literal is the claim; the closing quote is what tells it
                    // apart from the button that only shares its first two words.
                    if (line.Contains("\"TAKE IT\""))
                        failures.Add(relative + ":" + (i + 1) + " revives the TAKE IT claim.");

                    // 1b. ONE HOUSE PER ORDER, ONE PLACE THE PLAYER'S NAME GOES ON ONE
                    //     (RIVAL-003). A mind stamps its own house freely; the PLAYER's
                    //     id may only reach a command through PlayerCommands.Stamp.
                    if (line.Contains("House =") && !line.Contains("House ==") &&
                        (line.Contains("PlayerGangId") ||
                         line.Contains("PlayerCommands.House")))
                        failures.Add(relative + ":" + (i + 1) +
                                     " stamps the player's house outside PlayerCommands.");
                }
            }

            // 1c. WHO THE PLAYER IS is a fact about identity, never a rule. The
            //     constant may name house 0 and it may not decide anything, so the
            //     files allowed to mention it at all are listed here by hand and
            //     everything else in the sim is scanned against them.
            //
            //     The street's own HUDS are the player's surfaces and are on the list
            //     for the same reason Assets/Scripts/UI is: a page painted FOR him is
            //     entitled to ask which house is his.
            var namesThePlayer = new[]
            {
                // the catalog that defines him, and the one levy that is his alone (D20)
                "Assets/Scripts/Gangs/GangCatalog.cs",
                "Assets/Scripts/Outfit/CampaignRunner.cs",
                // identity: "which of the twenty-one is house 0"
                "Assets/Scripts/Outfit/House.cs",
                "Assets/Scripts/Outfit/Underworld.cs",
                "Assets/Scripts/Personnel/RosterSeeder.cs",
                "Assets/Scripts/Gangs/GangSeeder.cs",
                "Assets/Scripts/Business/BusinessDeeds.cs",
                // the one place the player's name goes on an order
                "Assets/Scripts/Gameplay/PlayerCommands.cs",
                // the audit's own patterns are not the thing it hunts
                "Assets/Scripts/Editor/PipelineCommands.cs",
            };
            var simFolders = new[]
            {
                "Assets/RoadDemo/", "Assets/Scripts/Outfit/", "Assets/Scripts/Personnel/",
                "Assets/Scripts/Territory/", "Assets/Scripts/Police/",
                "Assets/Scripts/Business/", "Assets/Scripts/Gangs/",
            };
            var playerSurfaces = new[]
            {
                "Assets/RoadDemo/CityAudit.cs", "Assets/RoadDemo/CrewOverlay.cs",
                "Assets/RoadDemo/FrontOverlay.cs", "Assets/RoadDemo/TerritoryNoticeHud.cs",
                "Assets/RoadDemo/TerritoryPlaques.cs", "Assets/RoadDemo/TurfMinimap.cs",
                "Assets/RoadDemo/TurfMapHud.cs", "Assets/RoadDemo/TurfKnowledge.cs",
                "Assets/RoadDemo/StreetHud.cs", "Assets/RoadDemo/DemoClockHud.cs",
                // The incident feed is the player's own paper and is written in the
                // first person ("our men", "our names"): a line about a rival's man
                // would be a lie on his page, so this file has to know whose page it is.
                // It decides nothing on the street - it only chooses what to print.
                "Assets/RoadDemo/LawWire.cs",
            };
            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs",
                         System.IO.SearchOption.AllDirectories))
            {
                var relative = file.Substring(
                    System.IO.Directory.GetCurrentDirectory().Length + 1)
                    .Replace('\\', '/');
                var inSim = false;
                for (var i = 0; i < simFolders.Length; i++)
                    inSim |= relative.StartsWith(simFolders[i], StringComparison.Ordinal);
                if (!inSim)
                    continue;
                var listed = false;
                for (var i = 0; i < namesThePlayer.Length; i++)
                    listed |= relative == namesThePlayer[i];
                for (var i = 0; i < playerSurfaces.Length; i++)
                    listed |= relative == playerSurfaces[i];
                if (listed)
                    continue;

                var lines = System.IO.File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains("PlayerGangId"))
                        failures.Add(relative + ":" + (i + 1) +
                                     " asks the sim which house is the player's.");
            }

            // 1d. MONEY IS COMPUTED IN THE ROUND LEDGER ONLY (RIVAL-004). A round's
            //     Carried is written in TerritoryRounds.cs and nowhere else; a clock
            //     that could add to the bag would be a second economy.
            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs",
                         System.IO.SearchOption.AllDirectories))
            {
                var relative = file.Substring(
                    System.IO.Directory.GetCurrentDirectory().Length + 1)
                    .Replace('\\', '/');
                // The rule guards the RUNNING city against a second economy: a clock, a
                // HUD or a street class that could add to the bag. A contract that
                // stands a round up carrying money to prove the save brings it back is
                // not a clock, and the ledger's own path cannot serve it - Settle rolls
                // for what a door pays and may hand back nothing.
                if (relative.EndsWith("Territory/TerritoryRounds.cs",
                        StringComparison.Ordinal) ||
                    relative.StartsWith("Assets/Scripts/Tests/", StringComparison.Ordinal) ||
                    relative == "Assets/Scripts/Editor/PipelineCommands.cs")
                    continue;

                var lines = System.IO.File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    // The round's own field, reached through the round: ".Carried".
                    // A local named carried or a display field called RoundCarried is
                    // somebody reading it, which is nobody's business but their own.
                    var line = lines[i];
                    if (!line.Contains(".Carried"))
                        continue;
                    if (line.Contains(".Carried +=") || line.Contains(".Carried++") ||
                        (line.Contains(".Carried =") && !line.Contains(".Carried ==")))
                        failures.Add(relative + ":" + (i + 1) +
                                     " writes a round's money; only the ledger may.");
                }
            }

            // 2. No territory type carries a settable owner. The block model is geography
            //    and identity; who holds it is a reading, and a reading has no setter.
            foreach (var type in typeof(LivingCity.Territory.TerritoryBlockDefinition).Assembly
                         .GetTypes())
            {
                if (type.Namespace != "LivingCity.Territory")
                    continue;
                foreach (var property in type.GetProperties())
                {
                    if (!property.CanWrite || !property.GetSetMethod(false)?.IsPublic == true)
                        continue;
                    var name = property.Name;
                    if (name.Contains("Owner") || name.Contains("Controlled") ||
                        name.Contains("Capture"))
                        failures.Add(type.Name + "." + name + " is a settable owner.");
                }
            }

            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// TEST-009. The whole slice in one run, plus the architectural audits. Balance
        /// notes are reported beside the failures, never as failures: a street that takes
        /// ten threats instead of three is a tuning question, not a broken rule.
        /// </summary>
        [CliCommand("gangsters_scenario_phase1",
                    "TEST-009: the full Phase-1 chain - organization, takeover, withdrawal, " +
                    "contest, loss - with the architectural audits, and balance notes kept apart.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "scenario" })]
        public static object ScenarioPhase1()
        {
            var architectural = new List<string>();
            architectural.AddRange(LivingCity.Tests.ScenarioTests.Run());

            var audit = ScenarioCaptureAudit();
            var auditFailures = (string[])audit.GetType()
                .GetProperty("failures").GetValue(audit);
            architectural.AddRange(auditFailures);

            architectural.AddRange(LivingCity.Tests.ControlTests.Run()
                .Select(failure => "Control: " + failure));
            architectural.AddRange(LivingCity.Tests.RackTests.Run()
                .Select(failure => "Racket: " + failure));
            architectural.AddRange(LivingCity.Tests.FearTests.Run()
                .Select(failure => "Fear: " + failure));
            architectural.AddRange(LivingCity.Tests.PresenceTests.Run()
                .Select(failure => "Presence: " + failure));
            architectural.AddRange(LivingCity.Tests.TerritoryFoundationTests.Run()
                .Select(failure => "Territory: " + failure));

            return new
            {
                passed = architectural.Count == 0,
                architectural_failures = architectural.ToArray(),
                balance_observations = LivingCity.Tests.ScenarioTests.BalanceNotes().ToArray(),
            };
        }

        static object Scenario(List<string> failures) => new
        {
            passed = failures.Count == 0,
            failures = failures.ToArray(),
        };

        [CliCommand("gangsters_control_tests",
                    "Run GAN-120 contracts for derived block control: the weighted inputs, the " +
                    "ladder from neutral to held outright, contested detection with hysteresis, " +
                    "organic gain and loss, the Power ledger and the quarter aggregate.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "control", "tests" })]
        public static object ControlTests()
        {
            var failures = LivingCity.Tests.ControlTests.Run();
            failures.AddRange(LivingCity.Tests.RackTests.Run()
                .Select(failure => "Racket regression: " + failure));
            failures.AddRange(LivingCity.Tests.FearTests.Run()
                .Select(failure => "Fear regression: " + failure));
            failures.AddRange(LivingCity.Tests.PresenceTests.Run()
                .Select(failure => "Presence regression: " + failure));
            failures.AddRange(LivingCity.Tests.TerritoryFoundationTests.Run()
                .Select(failure => "Territory regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// Why a street reads the way it does: every family's terms, what the block says
        /// now, and what the quarter it sits in adds up to. Report only.
        /// </summary>
        [CliCommand("gangsters_control_audit",
                    "Break down live block control: per-family presence/fear/compliance/power " +
                    "terms, the derived state and leader, and the neighbourhood aggregate.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "control", "audit" })]
        public static object ControlAudit(
            [CliArg("block", "Canonical block id or legacy plan index. Omitted, the blocks " +
                             "that read as something are reported.")]
            string block = "",
            [CliArg("limit", "How many blocks to report when none is named.")] int limit = 5)
        {
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            var control = runtime?.Control;
            if (runtime == null || control == null)
                return new { ok = false, reason = "No TerritoryRuntime is running in this scene." };

            var wanted = new List<LivingCity.Territory.TerritoryBlockId>();
            if (!string.IsNullOrEmpty(block))
            {
                if (int.TryParse(block, out var legacy) &&
                    runtime.TryGetBlock(legacy, out var byIndex))
                    wanted.Add(byIndex);
                else
                    wanted.Add(new LivingCity.Territory.TerritoryBlockId(block));
            }
            else
            {
                for (var i = 0; i < control.Blocks.Count && wanted.Count < System.Math.Max(1, limit); i++)
                {
                    var candidate = control.Blocks[i];
                    if (control.StateOf(candidate) != LivingCity.Territory.TerritoryControlState.Uncontrolled &&
                        control.StateOf(candidate) != LivingCity.Territory.TerritoryControlState.Unknown)
                        wanted.Add(candidate);
                }
            }

            var rows = new List<object>();
            for (var i = 0; i < wanted.Count; i++)
            {
                var blockId = wanted[i];
                runtime.DebugTruth.TryGetBlock(blockId, out var truth);
                control.Scores(blockId, out var best, out var second);

                var families = new List<object>();
                var seen = new List<int>();
                if (truth != null)
                    for (var g = 0; g < truth.Signals.Gangs.Count; g++)
                        seen.Add(truth.Signals.Gangs[g].GangId.Value);

                for (var g = 0; g < seen.Count; g++)
                {
                    var gangId = new LivingCity.Territory.TerritoryGangId(seen[g]);
                    var inputs = runtime.ControlInputsFor(blockId, gangId);
                    var score = control.Config.Score(inputs);
                    runtime.Power.Collect(blockId, gangId, runtime.GameHour,
                        out var incidents, out var unanswered);
                    families.Add(new
                    {
                        gang = seen[g],
                        presence = inputs.Presence,
                        fear = inputs.Fear,
                        compliance = inputs.Compliance,
                        power = inputs.Power,
                        incidents,
                        unanswered,
                        presenceTerm = score.PresenceTerm,
                        fearTerm = score.FearTerm,
                        complianceTerm = score.ComplianceTerm,
                        total = score.Total,
                    });
                }

                rows.Add(new
                {
                    block = blockId.Value,
                    name = truth?.Definition.DisplayName ?? "",
                    state = control.StateOf(blockId).ToString(),
                    leader = control.LeaderOf(blockId).IsValid
                        ? control.LeaderOf(blockId).Value
                        : -1,
                    bestScore = best,
                    secondScore = second,
                    families = families.ToArray(),
                });
            }

            return new
            {
                ok = true,
                gameHour = runtime.GameHour,
                blocksRead = control.Blocks.Count,
                config = new
                {
                    presenceWeight = control.Config.PresenceWeight,
                    fearWeight = control.Config.FearWeight,
                    complianceWeight = control.Config.ComplianceWeight,
                    influencedAt = control.Config.InfluencedAt,
                    controlledAt = control.Config.ControlledAt,
                    dominatedAt = control.Config.DominatedAt,
                    contestedMargin = control.Config.ContestedMargin,
                    contestedExitMargin = control.Config.ContestedExitMargin,
                    contestedFloor = control.Config.ContestedFloor,
                    holdTicks = control.Config.HoldTicks,
                    powerFloor = control.Config.PowerFloor,
                    powerAnswerWindowHours = control.Config.PowerAnswerWindowHours,
                },
                blocks = rows.ToArray(),
            };
        }

        [CliCommand("gangsters_rack_tests",
                    "Run GAN-103 contracts for the racket: per Business x Gang standing, the " +
                    "owner's evaluation, accept/hesitate/refuse, threats, escalation, " +
                    "protector switching and what the player is allowed to read.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "rack", "tests" })]
        public static object RackTests()
        {
            var failures = LivingCity.Tests.RackTests.Run();
            failures.AddRange(LivingCity.Tests.FearTests.Run()
                .Select(failure => "Fear regression: " + failure));
            failures.AddRange(LivingCity.Tests.PresenceTests.Run()
                .Select(failure => "Presence regression: " + failure));
            failures.AddRange(LivingCity.Tests.TerritoryFoundationTests.Run()
                .Select(failure => "Territory regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// Where a shop stands with every family, and why: the evaluation's own terms, the
        /// fear and presence behind them, and what has passed at that door. Report only.
        /// </summary>
        [CliCommand("gangsters_rack_audit",
                    "Break down the live racket for a business (or the most contested shops): " +
                    "relationships, the compliance terms, and the interaction history.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "rack", "audit" })]
        public static object RackAudit(
            [CliArg("business", "Business id to break down. Omitted, the shops with the most " +
                                "relationships are reported.")]
            string business = "",
            [CliArg("limit", "How many shops to report when none is named.")] int limit = 5)
        {
            var runtime = UnityEngine.Object.FindAnyObjectByType<RoadDemo.TerritoryRuntime>();
            var racket = runtime?.Racket;
            if (runtime == null || racket == null)
                return new { ok = false, reason = "No TerritoryRuntime is running in this scene." };

            var wanted = new List<LivingCity.Territory.TerritoryBusinessId>();
            if (!string.IsNullOrEmpty(business))
                wanted.Add(new LivingCity.Territory.TerritoryBusinessId(business));
            else
                for (var i = 0; i < racket.Businesses.Count && i < System.Math.Max(1, limit); i++)
                    wanted.Add(racket.Businesses[i]);

            var rows = new List<object>();
            var relationships = new List<LivingCity.Territory.TerritoryProtectionRelationship>();
            var history = new List<LivingCity.Territory.TerritoryRacketEntry>();
            for (var i = 0; i < wanted.Count; i++)
            {
                var businessId = wanted[i];
                racket.CollectRelationships(businessId, relationships);
                racket.CollectHistory(businessId, history);

                var houses = new List<object>();
                for (var r = 0; r < relationships.Count; r++)
                {
                    var row = relationships[r];
                    // The terms as they stand RIGHT NOW, so a verdict can be explained
                    // from the same numbers the owner would answer with today.
                    runtime.TryExplainDemand(businessId, row.GangId, out var terms);
                    houses.Add(new
                    {
                        gang = row.GangId.Value,
                        state = row.State.ToString(),
                        stateSince = row.StateSince,
                        lastInteraction = row.LastInteraction,
                        refusedAt = row.RefusedAt,
                        demands = row.Demands,
                        threats = row.Threats,
                        escalations = row.Escalations,
                        terms = new
                        {
                            fear = terms.Fear,
                            presence = terms.Presence,
                            trouble = terms.Trouble,
                            rivalPressure = terms.RivalPressure,
                            score = terms.Score,
                            wouldSay = terms.Verdict.ToString(),
                        },
                    });
                }

                var told = new List<object>();
                for (var h = 0; h < history.Count; h++)
                    told.Add(new
                    {
                        gang = history[h].GangId.Value,
                        what = history[h].What,
                        state = history[h].State.ToString(),
                        atGameHour = history[h].GameHour,
                        score = history[h].Score,
                    });

                racket.TryGetProtector(businessId, out var protector);
                rows.Add(new
                {
                    business = businessId.Value,
                    protector = protector.IsValid ? protector.Value : -1,
                    racketable = runtime.IsRacketable(businessId),
                    houses = houses.ToArray(),
                    history = told.ToArray(),
                });
            }

            return new
            {
                ok = true,
                gameHour = runtime.GameHour,
                shopsWithRelationships = racket.Businesses.Count,
                config = new
                {
                    fearWeight = racket.Config.FearWeight,
                    presenceWeight = racket.Config.PresenceWeight,
                    troubleWeight = racket.Config.TroubleWeight,
                    rivalWeight = racket.Config.RivalWeight,
                    acceptAt = racket.Config.AcceptAt,
                    hesitateAt = racket.Config.HesitateAt,
                    hesitantShare = racket.Config.HesitantComplianceShare,
                    switchMargin = racket.Config.SwitchMargin,
                    switchTicks = racket.Config.SwitchTicks,
                    rivalDemandPresence =
                        LivingCity.Outfit.HouseMindConfig.Default.DemandPresence,
                    approachRadiusMetres = racket.Config.ApproachRadiusMetres,
                },
                businesses = rows.ToArray(),
            };
        }

        [CliCommand("gangsters_business_tests",
                    "Run GAN-154 contracts for the business registry, site providers, archetype " +
                    "catalogue, deterministic owners and city population.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "business", "tests" })]
        public static object BusinessTests()
        {
            var failures = LivingCity.Tests.BusinessFoundationTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>
        /// The EPIC 39 gate: count every usable doorstep, the legal kerb and the cost of
        /// the existing crowd before any of those systems is changed. The implementation
        /// lives beside the command rather than in runtime code because it reads prefab
        /// provenance and runs a disposable preview-scene benchmark.
        /// </summary>
        [CliCommand("gangsters_people_census",
                    "Measure EPIC 39's canonical seed-1987 doors, legal kerb and current crowd " +
                    "tick curve without building or changing the open scene.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "people", "audit" })]
        public static object PeopleCensus(
            [CliArg("seed", "Release-gate seed; only the canonical value 1987 is accepted.")] int seed = 1987,
            [CliArg("rows", "Include every measured door/module row, not only totals and failures.")] bool rows = false)
            => PeopleCensusAudit.Run(seed, rows);

        [CliCommand("gangsters_economy_tests",
                    "Run EPIC 9 contracts: the dues meter, owner profiles, payment rolls, the " +
                    "round planner, policy/archetype tables and the tier guard.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "economy", "tests" })]
        public static object EconomyTests()
        {
            var failures = LivingCity.Tests.EconomyTests.Run();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
            };
        }

        /// <summary>The live city if one is standing and populated, otherwise a quarter
        /// dealt fresh from the seed. CoreDistrict.Plan is pure data - no prefab is loaded
        /// and no GameObject is made - which is exactly why this can run from the terminal
        /// with the editor idle. Shared by every audit that reads a business directory.</summary>
        static (LivingCity.Business.BusinessSiteCatalog catalog,
                LivingCity.Business.BusinessDirectory directory,
                LivingCity.Business.BusinessPopulationReport report,
                int citySeed, bool live) DealOrReadCity(int seed)
        {
            var runtime = seed < 0
                ? UnityEngine.Object.FindAnyObjectByType<LivingCity.Business.BusinessRuntime>()
                : null;
            if (runtime != null && runtime.Populated)
                return (runtime.Catalog, runtime.Directory, runtime.Report, runtime.CitySeed, true);

            var citySeed = seed < 0 ? 1987 : seed;
            var core = new CoreDistrict();
            core.Plan(null, citySeed);
            core.Frame = DistrictFrame.Identity;

            var catalog = new LivingCity.Business.BusinessSiteCatalog();
            catalog.Add(new LivingCity.Business.ResidentialBusinessSites(
                core.ResidentialBlocks, core.Frame));
            catalog.Add(new LivingCity.Business.StandaloneBusinessSites(core));
            catalog.Add(new LivingCity.Business.CompoundBusinessSites(core, null));
            catalog.Build();

            // The flats come off the same plan the shops do, at the same moment, so a
            // command that deals a quarter with the editor idle can be asked about them
            // as well (EPIC 27).
            LivingCity.Property.ApartmentBuildings.Init(core.ResidentialBlocks, core.Frame);

            var directory = new LivingCity.Business.BusinessDirectory();
            var report = LivingCity.Business.BusinessPopulation.Populate(catalog, citySeed, directory);
            return (catalog, directory, report, citySeed, false);
        }

        /// <summary>
        /// What a week of collections would earn (ECON-008). Deals the city's businesses
        /// (or reads the live ones) and prints the protection book by tier - and what of
        /// it a day-one outfit could actually reach, which the tier guard says is tier 1.
        /// </summary>
        [CliCommand("gangsters_economy_audit",
                    "Deal a quarter (or read the live city) and report the weekly protection " +
                    "book by tier, and the slice of it a day-one outfit can reach.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "economy", "audit" })]
        public static object EconomyAudit(
            [CliArg("seed", "Deal from this seed instead of the live scene. -1 uses the " +
                            "running city, falling back to 1987.")] int seed = -1)
        {
            var (_, directory, _, citySeed, live) = DealOrReadCity(seed);

            var counts = new int[5];
            var weekly = new int[5];
            foreach (var id in directory.BusinessIds)
            {
                if (!directory.TryGet(id, out var record) || record == null)
                    continue;
                var price = LivingCity.Outfit.EconomyPrices.Of(record.Archetype);
                var tier = (int)price.Tier;
                counts[tier]++;
                weekly[tier] += price.ProtectionPerWeek;
            }

            return new
            {
                seed = citySeed,
                source = live ? "live city" : "dealt from seed",
                businesses = directory.BusinessIds.Count,
                tiers = new[]
                {
                    new { tier = 1, doors = counts[1], weeklyProtection = weekly[1] },
                    new { tier = 2, doors = counts[2], weeklyProtection = weekly[2] },
                    new { tier = 3, doors = counts[3], weeklyProtection = weekly[3] },
                    new { tier = 4, doors = counts[4], weeklyProtection = weekly[4] },
                },
                dayOneWeeklyTake = weekly[1],
                note = "a day-one outfit reaches tier 1 only - the tier guard prices " +
                       "the rest in standing it does not have yet",
            };
        }

        /// <summary>
        /// Why every business in this city exists, and which source owns it. Report only -
        /// an audit that repaired its data would hide the very faults it is for.
        /// </summary>
        [CliCommand("gangsters_business_audit",
                    "Count the live city's business sites, businesses and owners by provider and " +
                    "archetype, and name every duplicate, unsupported, unpopulated or unbound one.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "business", "audit" })]
        public static object BusinessAudit(
            [CliArg("rows", "List every site, not just the totals and the faults.")] bool rows = false,
            [CliArg("seed", "Deal a Core quarter from this seed and audit THAT plan instead of " +
                            "the live scene. -1 uses the running city.")] int seed = -1)
        {
            var (catalog, directory, report, citySeed, live) = DealOrReadCity(seed);
            var failures = new List<string>(report.Problems);

            var unpopulated = new List<string>();
            var unbound = new List<string>();
            var hintMismatch = new List<string>();
            var bound = 0;

            foreach (var site in catalog.Sites)
            {
                if (!site.Eligible)
                    continue;

                if (!directory.TryGetBySite(site.SiteId, out var record))
                {
                    unpopulated.Add(site.SiteId.Value + " (" + site.ProviderId + ")");
                    continue;
                }

                if (!string.IsNullOrEmpty(site.ArchetypeHint) &&
                    LivingCity.Business.BusinessArchetypes.TryFromSignage(
                        site.ArchetypeHint, out var hinted) &&
                    hinted.Id != record.Archetype)
                    hintMismatch.Add($"{site.SiteId}: signed '{site.ArchetypeHint}' but trades " +
                                     $"as {record.Archetype}.");

                if (live && LivingCity.Business.BusinessViewBindings.TryGet(record.Id, out _))
                    bound++;
                else if (live)
                    unbound.Add(site.SiteId.Value);
            }

            var orphans = new List<string>();
            foreach (var id in directory.BusinessIds)
            {
                directory.TryGet(id, out var record);
                if (record == null)
                    continue;
                if (!catalog.TryGet(record.SiteId, out _))
                    orphans.Add(id.Value + ": no site.");
                else if (!directory.TryGetOwner(record.OwnerId, out _))
                    orphans.Add(id.Value + ": no owner.");
            }

            failures.AddRange(unpopulated.Select(site => "BIZ: eligible site never populated: " + site));
            failures.AddRange(hintMismatch);
            failures.AddRange(orphans.Select(row => "BIZ: orphan " + row));

            return new
            {
                passed = failures.Count == 0,
                seed = citySeed,
                source = live ? "live city" : "dealt from seed",
                sites = catalog.Sites.Count,
                eligible = catalog.EligibleCount,
                businesses = directory.BusinessIds.Count,
                owners = directory.OwnerIds.Count,
                boundViews = bound,
                unboundViews = unbound.Count,
                byProvider = report.ByProvider
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new { provider = pair.Key, count = pair.Value }).ToArray(),
                byArchetype = report.ByArchetype
                    .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                    .Select(pair => new { archetype = pair.Key.ToString(), count = pair.Value })
                    .ToArray(),
                unsupported = report.Unsupported.ToArray(),
                failures = failures.ToArray(),
                rows = rows
                    ? catalog.Sites.Select(site =>
                    {
                        directory.TryGetBySite(site.SiteId, out var record);
                        string owner = null;
                        if (record != null && directory.TryGetOwner(record.OwnerId, out var deed))
                            owner = deed.DisplayName;
                        return new
                        {
                            siteId = site.SiteId.Value,
                            provider = site.ProviderId,
                            plan = site.SourcePlanId,
                            group = site.GroupKey,
                            role = site.Role,
                            hint = site.ArchetypeHint,
                            size = site.Size.ToString(),
                            block = site.BlockHint.Value,
                            legacyBlock = site.LegacyBlockId,
                            approach = new { x = site.Approach.X, z = site.Approach.Z },
                            footprint = new
                            {
                                x = site.Footprint.XMin,
                                z = site.Footprint.ZMin,
                                w = site.Footprint.Width,
                                d = site.Footprint.Depth,
                            },
                            eligible = site.Eligible,
                            reason = site.ExclusionReason,
                            businessId = record?.Id.Value,
                            archetype = record?.Archetype.ToString(),
                            name = record?.DisplayName,
                            ownerId = record?.OwnerId.Value,
                            owner,
                            weekly = record?.EstimatedWeeklyTurnover ?? 0,
                        };
                    }).ToArray()
                    : null,
            };
        }

        // ---------------------------------------------------------------- the plan

        /// <summary>The district roll for a seed, without building anything. This is the
        /// paper plan - the same call RoadDemoBuilder makes at Play - so it answers "what
        /// does seed N give me" in a second instead of a ninety-second run.</summary>
        [CliCommand("gangsters_layout",
                    "Roll the city district layout for a seed and return it, without building or playing. " +
                    "Reads a RoadDemoBuilder in the open scene, or uses its canonical defaults.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Layout(
            [CliArg("seed", "City layout seed. Omit to use the one the open scene carries.")] int seed = int.MinValue,
            [CliArg("count", "Roll this many consecutive seeds starting at 'seed' and return a summary of each.")] int count = 1,
            [CliArg("scene", "Scene to open first, e.g. Assets/Scenes/CoreDemo.unity. Omit to use the scene already open.")] string scene = "")
        {
            if (!string.IsNullOrEmpty(scene))
            {
                if (EditorApplication.isPlaying)
                    throw new InvalidOperationException("The editor is in play mode; stop it (editor_stop) before opening a scene.");
                var opened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                if (!opened.IsValid()) throw new ArgumentException($"Scene '{scene}' would not open.");
            }

            var city = UnityEngine.Object.FindAnyObjectByType<RoadDemoBuilder>();
            GameObject fallback = null;
            if (city == null)
            {
                // CoreDemo creates the shared runtime only when Play starts. The layout
                // command is edit-time and needs no scene objects, so a hidden component
                // provides the same serialized field defaults without keeping a retired
                // full-city harness solely for this tool.
                fallback = new GameObject("RoadDemoBuilder layout defaults")
                    { hideFlags = HideFlags.HideAndDontSave };
                city = fallback.AddComponent<RoadDemoBuilder>();
            }

            try
            {
                int first = seed == int.MinValue ? city.cityLayoutSeed : seed;
                int rolls = Mathf.Clamp(count, 1, 500);
                var grid = city.LayoutGrid();
                var results = new List<object>(rolls);

                for (int i = 0; i < rolls; i++)
                {
                    int s = first + i;
                    var slots = CityLayout.Roll(grid, s, city.suburbsMin, city.suburbsMax,
                                                city.harborDistrict, city.airportDistrict);
                    results.Add(new
                    {
                        seed = s,
                        districts = slots.Count,
                        harbor = slots.Any(d => d != null && d.kind == DistrictKind.Harbor),
                        airport = slots.Any(d => d != null && d.kind == DistrictKind.Airport),
                        suburbs = slots.Count(d => d != null && d.kind == DistrictKind.Suburb),
                        slots = slots.Where(d => d != null).Select(d => new
                        {
                            kind = d.kind.ToString(),
                            name = d.name,
                            edge = d.edge.ToString(),
                            lines = d.pinLines,
                            strip = Mathf.Round(d.strip),
                            seed = d.seed,
                            size = $"{d.sizeAcross}x{d.sizeDeep}",
                        }).ToArray(),
                    });
                }

                return new
                {
                    scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path,
                    source = fallback == null ? "scene" : "RoadDemoBuilder defaults",
                    rollDistricts = city.rollDistricts,
                    suburbs = $"{city.suburbsMin}-{city.suburbsMax}",
                    rolls = results,
                };
            }
            finally
            {
                if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        // ---------------------------------------------------------------- the stock

        /// <summary>What a prefab actually measures, from the imported asset rather than
        /// from the FBX on disk. The pack prefabs carry their own scale and their own pivot,
        /// and reading either out of a binary FBX by hand is a day's work that this answers
        /// in a call.</summary>
        [CliCommand("gangsters_measure",
                    "Measure a prefab: world-space bounding box, size in metres, and where the pivot sits inside it. " +
                    "Give a path or a name.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Measure(
            [CliArg("path", "Asset path, e.g. Assets/Prefabs/Buildings/building-bank.prefab.")] string path = "",
            [CliArg("name", "Prefab name (or part of it) to search for when no path is given.")] string name = "",
            [CliArg("limit", "When searching by name, measure at most this many matches.")] int limit = 5)
        {
            var paths = new List<string>();
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
            else if (!string.IsNullOrEmpty(name))
                paths.AddRange(AssetDatabase.FindAssets($"{name} t:Prefab")
                                            .Select(AssetDatabase.GUIDToAssetPath)
                                            .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                                            .OrderBy(p => p.Length)
                                            .Take(Mathf.Clamp(limit, 1, 50)));
            else throw new ArgumentException("Give either --path or --name.");

            var measured = new List<object>();
            foreach (var p in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab == null) { measured.Add(new { path = p, error = "not a prefab" }); continue; }

                // The asset is measured through an instance: a prefab asset's renderers report
                // bounds in their own local space, and the parent scaling the pack authors rely
                // on is only applied once the thing stands in a scene.
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    go.transform.position = Vector3.zero;
                    go.transform.rotation = Quaternion.identity;
                    var renderers = go.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) { measured.Add(new { path = p, error = "no renderers" }); continue; }

                    var box = renderers[0].bounds;
                    foreach (var r in renderers) box.Encapsulate(r.bounds);

                    measured.Add(new
                    {
                        path = p,
                        name = prefab.name,
                        renderers = renderers.Length,
                        scale = V(go.transform.localScale),
                        size = V(box.size),
                        center = V(box.center),
                        // the pivot is the root at the origin, so the box centre IS the offset
                        pivotFromCentre = V(-box.center),
                        groundOffset = Mathf.Round((box.center.y - box.extents.y) * 1000f) / 1000f,
                    });
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            return new { count = measured.Count, prefabs = measured };
        }

        static object V(Vector3 v) => new
        {
            x = Mathf.Round(v.x * 1000f) / 1000f,
            y = Mathf.Round(v.y * 1000f) / 1000f,
            z = Mathf.Round(v.z * 1000f) / 1000f,
        };

        // ---------------------------------------------------------------- the run

        /// <summary>A harness run in the editor that is already open. Tools/play/run.sh
        /// starts a second Unity in batch mode, which needs Temp/UnityLockfile and so cannot
        /// run while an editor is up; this drives the live one instead. It leaves play mode
        /// when it finishes rather than exiting the editor.</summary>
        [CliCommand("gangsters_play",
                    "Run the play harness inside THIS editor (no batch Unity, no lockfile) and leave the trace behind. " +
                    "Returns immediately; the run is over when summary.json appears in the out folder.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "editor/playmode" })]
        public static object Play(
            [CliArg("scene", "Scene to play, e.g. Assets/Scenes/BlockDemo.unity.")] string scene = "Assets/Scenes/BlockDemo.unity",
            [CliArg("seconds", "Simulated seconds to play.")] float seconds = 90f,
            [CliArg("out", "Folder for trace.jsonl, unity.log and summary.json. Defaults to Temp/play/cli.")] string outDir = "",
            [CliArg("step", "Fixed simulation step. Soak verdicts are only comparable at the same step (0.05).")] float step = 0.05f,
            [CliArg("sample", "Trace sampling period in seconds.")] float sample = 0.1f,
            [CliArg("warm", "Seconds to let the city settle before the trace starts.")] float warm = 3f,
            [CliArg("shot", "Take a screenshot every N seconds. 0 for none.")] float shot = 0f,
            [CliArg("sets", "Field overrides, 'Type.field=value', several joined by ';'.")] string sets = "")
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is already in play mode. Call editor_stop first.");
            if (!File.Exists(scene))
                throw new ArgumentException($"No scene at '{scene}'.");

            var cfg = new PlayHarness.Cfg
            {
                scene = scene,
                outDir = string.IsNullOrEmpty(outDir) ? Path.Combine("Temp", "play", "cli") : outDir,
                seconds = seconds,
                step = step,
                sample = sample,
                warm = warm,
                shot = shot,
                quit = false,   // the editor stays up; this is the whole point of the command
            };
            if (!string.IsNullOrEmpty(sets))
                cfg.sets.AddRange(sets.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)));

            PlayHarness.RunWith(cfg);

            return new
            {
                scene = cfg.scene,
                outDir = cfg.outDir,
                seconds = cfg.seconds,
                step = cfg.step,
                sets = cfg.sets.ToArray(),
                note = "started; poll " + Path.Combine(cfg.outDir, "summary.json") +
                       ", then read it with Tools/play/analyze.py " + cfg.outDir + " --verdict",
            };
        }

        // ---------------------------------------------------------------- the industry

        /// <summary>Industrial blocks for the core, four guesses at a time. Without
        /// <c>--bake</c> it stands them up to be looked at; with it, the ones named are
        /// filed through the block tray's own bake and the rest are thrown away. The
        /// looking is the point, so the two halves are deliberately separate calls.</summary>
        [CliCommand("gangsters_industrial",
                    "Stand four industrial block candidates in the industrial lab scene, or bake the chosen ones " +
                    "into Assets/Prefabs/CoreBlocks. Without --bake it generates; with it, it files.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Industrial(
            [CliArg("seed", "Seed the four candidates are rolled from.")] int seed = 7,
            [CliArg("recipe", "works | depot | yard | strip, or all for one of each.")] string recipe = "all",
            [CliArg("bake", "Candidate numbers to file, e.g. 1,2. The rest are discarded.")] string bake = "",
            [CliArg("names", "Prefab names for --bake, in the same order. Rolled from the recipe when empty.")] string names = "",
            [CliArg("keepOthers", "With --bake, leave the candidates that were not chosen standing.")] bool keepOthers = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            if (string.IsNullOrWhiteSpace(bake))
                return new
                {
                    scene = IndustrialBlockForge.LabPath,
                    seed,
                    recipe,
                    candidates = IndustrialBlockForge.Generate(seed, recipe),
                };

            var chosen = bake.Split(',')
                             .Select(one => one.Trim())
                             .Where(one => one.Length > 0)
                             .Select(one => int.TryParse(one, out var n) ? n : 0)
                             .Where(n => n >= 1 && n <= 4)
                             .ToArray();
            if (chosen.Length == 0)
                throw new ArgumentException("--bake wants candidate numbers between 1 and 4, e.g. 1,2.");

            var called = string.IsNullOrWhiteSpace(names)
                ? new string[0]
                : names.Split(',').Select(one => one.Trim()).ToArray();

            return new
            {
                scene = IndustrialBlockForge.LabPath,
                baked = IndustrialBlockForge.BakeChosen(chosen, called, keepOthers),
            };
        }

        // ---------------------------------------------------------------- the core

        /// <summary>What a seed deals the core into, judged, without a drawing: how many
        /// deals the seed needed before one read clean, and what that one came to. A
        /// tally over thirty seeds is the verdict on the dealer; one seed proves nothing.
        /// With <c>--draw</c> the first seed is also drawn in the open scene.</summary>
        [CliCommand("gangsters_core",
                    "Deal the city core from a seed (or a run of seeds) and report the verdict on each: " +
                    "deals needed, faults, areas, roads. --draw also draws the first one in the open scene.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Core(
            [CliArg("seed", "First seed. -1 is Synty's own arrangement.")] int seed = 1,
            [CliArg("count", "How many consecutive seeds to deal.")] int count = 1,
            [CliArg("draw", "Draw the first seed in the open scene as Tools/City/Core/Sketch The Core City would.")] bool draw = false,
            [CliArg("map", "Include each seed's raster map in the answer.")] bool map = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");
            int rolls = Mathf.Clamp(count, 1, 200);
            var results = new List<object>(rolls);
            int clean = 0, firstDeal = 0;
            // The runtime plans from the baked catalogue too. Keeping this command on
            // that exact data path makes a 30-seed verdict test the implementation, not
            // an editor-only replica that secretly instantiates every prefab first.
            var blocks = CoreBlockCatalog.CreateBlocks();
            for (int i = 0; i < rolls; i++)
            {
                int s = seed == CoreLayout.SyntySeed ? (i == 0 ? seed : i) : seed + i;
                var plan = CoreLayout.Arrange(blocks, s, out var raster);
                // Match CoreDistrict: only ground that belongs to no existing block may
                // become an independent amenity block.
                var amenityCandidates = new List<Rect>(plan.Lots);
                var parking = new List<CoreAmenityLayout.Site>();
                var fuel = new List<CoreAmenityLayout.Site>();
                var development = new List<CoreAmenityLayout.Site>();
                CoreAmenityLayout.Select(raster, amenityCandidates, s, 3, 5,
                    parking, fuel, development);
                // the same pick the city makes, so the verdict MEASURES the courthouse
                // rather than describing an editor-only replica of it (GAN-237)
                var court = CoreAmenityLayout.PickCourthouse(development, plan.Territory);
                if (raster.Faults == 0) clean++;
                if (plan.Attempt == 0) firstDeal++;
                results.Add(new
                {
                    seed = s,
                    plan = plan.Name,
                    deals = plan.Attempt + 1,
                    faults = raster.Faults,
                    blocksM2 = raster.BlockArea,
                    roadM2 = raster.RoadArea,
                    parkingM2 = raster.ParkingArea,
                    fuelStations = fuel.Count,
                    courthouse = court == null ? null : new
                    {
                        x = court.Box.xMin,
                        z = court.Box.yMin,
                        width = court.Box.width,
                        depth = court.Box.height,
                        entry = court.Entry.ToString(),
                    },
                    fuelSites = fuel.Select(site => new
                    {
                        x = site.Box.xMin,
                        z = site.Box.yMin,
                        width = site.Box.width,
                        depth = site.Box.height,
                        entry = site.Entry.ToString(),
                    }).ToArray(),
                    parkingSites = parking.Select(site => new
                    {
                        x = site.Box.xMin,
                        z = site.Box.yMin,
                        width = site.Box.width,
                        depth = site.Box.height,
                        entry = site.Entry.ToString(),
                    }).ToArray(),
                    developmentSites = development.Select(site => new
                    {
                        x = site.Box.xMin,
                        z = site.Box.yMin,
                        width = site.Box.width,
                        depth = site.Box.height,
                        entry = site.Entry.ToString(),
                        programme = "residential-with-pavement",
                    }).ToArray(),
                    spareM2 = raster.SpareArea,
                    size = $"{raster.NX * 5}x{raster.NZ * 5}",
                    rows = plan.Rows.ToArray(),
                    report = raster.Report.Split('\n').Select(line => line.Trim()).ToArray(),
                    map = map ? raster.Map : null,
                });
            }
            if (draw) LivingCity.EditorTools.CoreCitySketch.Draw(seed, quiet: true);
            return new
            {
                dealsPerSeed = CoreLayout.Deals,
                clean,
                firstDeal,
                seeds = results,
            };
        }

        // ------------------------------------------------------------------------ the parks

        /// <summary>
        /// Lays out a park from a seed and a size, and reports the verdict on it.
        ///
        /// Two verdicts again, and both have to be nought: the plan's - is the walk one
        /// piece, does every gate reach it, is any ground stranded more than twenty-five
        /// metres from a path - and, when it is actually stood, the composer's: is every cell
        /// floored, is the fence whole, is anything standing on the walk.
        ///
        /// Without --draw nothing is stood: the plan is pure arithmetic, so a hundred sizes
        /// cost no more than reading them. That is the point of the sweep - a park generator
        /// that works on the sizes it was written against and falls over on 25 x 150 m is one
        /// that will fall over the first time a quarter deals it an awkward rectangle.
        /// </summary>
        [CliCommand("gangsters_park",
                    "Lay out a park from a seed and a size (pocket|square|park|strip|WxD in cells) and " +
                    "report the verdict: ways in, rooms, what they were cast as, faults. --draw also " +
                    "stands the first one in the open scene.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Parks(
            [CliArg("seed", "First seed.")] int seed = 1987,
            [CliArg("count", "How many consecutive seeds to lay out.")] int count = 1,
            [CliArg("size", "pocket, square, park, strip, or WxD in 5 m cells (e.g. 12x9).")] string size = "",
            [CliArg("draw", "Stand the first one in the open scene, as Tools/City/Park/Sketch A Park would.")] bool draw = false,
            [CliArg("map", "Include each park's map in the answer.")] bool map = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            int rolls = Mathf.Clamp(count, 1, 200);
            var results = new List<object>(rolls);
            int clean = 0;
            for (int i = 0; i < rolls; i++)
            {
                int s = seed + i;
                LivingCity.EditorTools.ParkSketch.Measure(size, new System.Random(s), out int nx, out int nz);
                var plan = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new System.Random(s));
                string report = ParkWalk.Report(plan, out int faults);
                if (faults == 0) clean++;
                results.Add(new
                {
                    seed = s,
                    plan = plan.Name,
                    size = $"{plan.Wide:F0}x{plan.Deep:F0}",
                    klass = plan.Klass.ToString(),
                    faults,
                    mouths = plan.Mouths.Count,
                    rooms = plan.Rooms.Count,
                    cast = ParkWalk.Cast(plan),
                    report = report.Split('\n').Select(line => line.Trim()).ToArray(),
                    map = map ? plan.Map : null,
                });
            }

            object drawn = null;
            if (draw)
            {
                var stood = LivingCity.EditorTools.ParkSketch.Draw(seed, size, true);
                drawn = stood == null ? null : new
                {
                    seed,
                    plan = stood.Plan.Name,
                    gaps = stood.Gaps,
                    fenceGap = stood.FenceGap,
                    onWalk = stood.OnWalk,
                    trees = stood.TreeCount,
                    density = stood.Density,
                    benches = stood.Benches,
                    lamps = stood.Lamps,
                    tables = stood.Tables,
                    flowers = stood.Flowers,
                    programmes = stood.Programmes,
                    refused = stood.Refused,
                };
            }
            return new { clean, drawn, parks = results };
        }

        // --------------------------------------------------------------------- the quay

        [CliCommand("gangsters_quay",
                    "Lay out a stretch of the river promenade from a seed, a depth and a length (in " +
                    "5 m cells) and report the verdict: streets arriving, rooms, what they were cast " +
                    "as, faults. --draw also stands the first one in the open scene.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Quays(
            [CliArg("seed", "First seed.")] int seed = 1987,
            [CliArg("count", "How many consecutive seeds to lay out.")] int count = 1,
            [CliArg("depth", "The strip across, in cells (the core deals 12 or 13).")] int depth = 12,
            [CliArg("length", "The strip along the river, in cells.")] int length = 32,
            [CliArg("draw", "Stand the first one in the open scene, as Tools/City/River/Sketch The Quay would.")] bool draw = false,
            [CliArg("map", "Include each stretch's map in the answer.")] bool map = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            int rolls = Mathf.Clamp(count, 1, 200);
            var results = new List<object>(rolls);
            int clean = 0;
            for (int i = 0; i < rolls; i++)
            {
                int s = seed + i;
                var plan = LivingCity.EditorTools.QuaySketch.Plan(s, depth, length);
                string report = QuayWalk.Report(plan, out int faults);
                if (faults == 0) clean++;
                var rooms = new List<string>();
                foreach (var room in plan.Rooms) rooms.Add($"{room.Programme} {room.Length}");
                results.Add(new
                {
                    seed = s,
                    size = $"{plan.Depth * 5}x{plan.Length * 5}",
                    faults,
                    mouths = plan.Mouths.Count,
                    rooms,
                    report,
                    map = map ? plan.Map : null,
                });
            }

            object drawn = null;
            if (draw)
            {
                var stood = LivingCity.EditorTools.QuaySketch.Draw(seed, depth, length, true);
                drawn = stood == null ? null : new
                {
                    seed,
                    gaps = stood.Gaps,
                    railGap = stood.RailGap,
                    onWalk = stood.OnWalk,
                    lamps = stood.Lamps,
                    benches = stood.Benches,
                    tables = stood.Tables,
                    kiosks = stood.Kiosks,
                    venues = stood.VenueNames,
                    gym = stood.GymStood,
                    arches = stood.ArchCount,
                    pavilions = stood.PavilionCount,
                    trees = stood.TreeCount,
                    boats = stood.BoatCount,
                    wheel = stood.Wheel,
                    programmes = stood.Programmes,
                    refused = stood.Refused,
                    missing = string.Join(", ", Composer.Missing),
                };
            }
            return new { clean, drawn, quays = results };
        }

        // ------------------------------------------------------------ the industrial quarter

        /// <summary>
        /// Deals a whole industrial quarter from a seed and reports the verdict on it.
        ///
        /// Two verdicts, and both have to be nought: the raster's, on whether the roads
        /// between the parcels make a place a lorry can drive through, and the composer's,
        /// on whether the parcels themselves came out whole - no cell without a floor, no
        /// hole in a fence, no fence standing inside a building. They catch different
        /// things, which is why both are here.
        ///
        /// Without --draw nothing is stood at all: the deal and its verdict are pure
        /// arithmetic, so a hundred seeds cost no more than reading them. --draw stands the
        /// first one in the open scene, which is the slow part and the point of the thing.
        /// </summary>
        [CliCommand("gangsters_industry",
                    "Deal an industrial quarter from a seed (or a run of seeds) and report the verdict " +
                    "on each: deals needed, faults, parcels and what they were cast as. --draw also " +
                    "draws the first one in the open scene.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Industry(
            [CliArg("seed", "First seed.")] int seed = 1987,
            [CliArg("count", "How many consecutive seeds to deal.")] int count = 1,
            [CliArg("draw", "Draw the first seed in the open scene, as Tools/City/Industrial/Sketch The Industrial Quarter would.")] bool draw = false,
            [CliArg("map", "Include each seed's raster map in the answer.")] bool map = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            int rolls = Mathf.Clamp(count, 1, 200);
            var results = new List<object>(rolls);
            int clean = 0, firstDeal = 0;
            for (int i = 0; i < rolls; i++)
            {
                int s = seed + i;
                var plan = IndustrialLayout.Arrange(s, out var raster);
                if (plan == null || raster == null)
                    throw new InvalidOperationException($"Seed {s} dealt no quarter at all.");
                if (raster.Faults == 0) clean++;
                if (plan.Attempt == 0) firstDeal++;
                results.Add(new
                {
                    seed = s,
                    plan = plan.Name,
                    deals = plan.Attempt + 1,
                    faults = raster.Faults,
                    islands = plan.Islands.Count,
                    parcels = plan.Parcels.Count,
                    cast = IndustrialQuarter.Cast(plan),
                    blocksM2 = raster.BlockArea,
                    roadM2 = raster.RoadArea,
                    spareM2 = raster.SpareArea,
                    size = $"{raster.NX * 5}x{raster.NZ * 5}",
                    rows = plan.Rows.ToArray(),
                    report = raster.Report.Split('\n').Select(line => line.Trim()).ToArray(),
                    map = map ? raster.Map : null,
                });
            }

            object drawn = null;
            if (draw)
            {
                var stoodPlan = LivingCity.EditorTools.IndustrialSketch.Draw(seed, true);
                drawn = stoodPlan == null ? null : new { seed, plan = stoodPlan.Name, parcels = stoodPlan.Parcels.Count };
            }
            return new
            {
                dealsPerSeed = IndustrialLayout.Deals,
                clean,
                firstDeal,
                drawn,
                seeds = results,
            };
        }

        // ------------------------------------------------------- the catalog's buildings

        /// <summary>
        /// The catalog's buildings brought into the core: copied into the kit, baked into
        /// the blocks that are one building each, stood in the stock row.
        ///
        /// It exists because the menu items for the same three jobs all end in a dialog,
        /// which is right for a mouse and disastrous from here - a modal stops the editor's
        /// main thread dead waiting for a hand that is not there, and every call after it
        /// times out. <see cref="CoreBuildingBlocks"/> does the work; this only chooses
        /// which part of it and says what happened.
        /// </summary>
        [CliCommand("gangsters_coreblocks",
                    "Bring the catalog's buildings into the city core: copy them into the kit under " +
                    "the kit's names, bake the ones big enough to be a block on their own, and stand " +
                    "the rest in the stock row beside the trays.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object CoreBuildings(
            [CliArg("what", "copy (into the kit), bake (the blocks), stock (the row), or all.")] string what = "all",
            [CliArg("force", "Bake a block again even when one of that name is already on disk.")] bool force = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            string job = (what ?? "all").Trim().ToLowerInvariant();
            if (job != "copy" && job != "bake" && job != "stock" && job != "all")
                throw new ArgumentException("--what is copy, bake, stock or all.");

            object[] copied = null, baked = null;
            int stood = 0;
            if (job == "copy" || job == "all") copied = CoreBuildingBlocks.CopyBuildings();
            if (job == "bake" || job == "all") baked = CoreBuildingBlocks.BakeBlocks(force);
            if (job == "stock" || job == "all")
                stood = CoreBuildingBlocks.StandStock(EditorSceneManager.GetActiveScene());

            return new
            {
                what = job,
                copied,
                baked,
                stockStanding = stood,
                scene = EditorSceneManager.GetActiveScene().path,
            };
        }

        // ------------------------------------------------------------ the residential forge

        [CliCommand("gangsters_forge",
                    "Roll and report one deterministic residential facade sheet. --rebuild stands that " +
                    "sheet in ForgeDemo; --showroom builds the complete 12-sheet gallery.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "forge" })]
        public static object Forge(
            [CliArg("seed", "Deterministic forge seed.")] int seed = 1987,
            [CliArg("length", "Building length in 5 m cells (3..13).")] int length = 8,
            [CliArg("floors", "Apartment floors above the shops (3..5).")] int floors = 4,
            [CliArg("props", "Optional measured decoration density percent (0..200).")] int propsPercent = ResidentialFacade.DefaultPropsPercent,
            [CliArg("rebuild", "Replace ForgeDemo with this one sheet and save it.")] bool rebuild = false,
            [CliArg("showroom", "Build and save all 12 showroom variants; implies rebuild.")] bool showroom = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");
            if (length < 3 || length > 13)
                throw new ArgumentOutOfRangeException(nameof(length), "--length must be 3..13 cells.");
            if (floors < 3 || floors > 5)
                throw new ArgumentOutOfRangeException(nameof(floors), "--floors must be 3..5.");
            if (propsPercent < ResidentialFacade.MinPropsPercent ||
                propsPercent > ResidentialFacade.MaxPropsPercent)
                throw new ArgumentOutOfRangeException(
                    nameof(propsPercent),
                    $"--props must be {ResidentialFacade.MinPropsPercent}.." +
                    $"{ResidentialFacade.MaxPropsPercent} percent.");

            var sheet = ResidentialFacade.Roll(seed, length, floors, propsPercent);
            ForgeShowroom.Report sceneReport = null;
            if (showroom)
                sceneReport = ForgeShowroom.BuildGallery(seed, propsPercent);
            else if (rebuild)
                sceneReport = ForgeShowroom.BuildSingle(seed, length, floors, propsPercent);

            int bays = sheet.Unit?.ShopBays?.Length ?? 0;
            string[] faults = sheet.Faults == null
                ? Array.Empty<string>()
                : sheet.Faults.Select(fault => Convert.ToString(fault)).ToArray();
            object unit = sheet.Unit == null
                ? null
                : new
                {
                    sheet.Unit.Name,
                    sheet.Unit.CW,
                    sheet.Unit.CD,
                    kind = sheet.Unit.Kind.ToString(),
                    sheet.Unit.MaxH,
                    shopBays = bays,
                    flats = bays * sheet.Floors,
                    doors = sheet.Unit.Doors?.Sum() ?? 0,
                    shops = sheet.Unit.Shops?.Sum() ?? 0,
                    sheet.Unit.Pieces,
                };

            return new
            {
                passed = faults.Length == 0 && (sceneReport == null || sceneReport.Passed),
                sheet.Seed,
                sheet.Length,
                sheet.Floors,
                sheet.PropsPercent,
                sheet.Signature,
                pieces = sheet.Pieces?.Length ?? 0,
                props = sheet.Props?.Length ?? 0,
                faults,
                unit,
                rebuilt = sceneReport != null,
                showroom,
                scene = sceneReport,
            };
        }

        [CliCommand("gangsters_forge_tests",
                    "Run the shared pure GAN-332 residential-facade fault contracts.",
                    MainThreadRequired = false,
                    Tags = new[] { "gangsters", "residential", "forge", "tests" })]
        public static object ForgeTests()
        {
            var result = ResidentialFacadeTests.Run();
            return new
            {
                passed = result.Clean,
                caught = result.Passed,
                total = result.Total,
                faultsPassed = result.FaultsPassed,
                faultsTotal = result.FaultsTotal,
                contractsPassed = result.ContractsPassed,
                contractsTotal = result.ContractsTotal,
                missing = result.Missing.Select(kind => kind.ToString()).ToArray(),
                missingContracts = result.MissingContracts,
                report = result.Report,
            };
        }

        // ------------------------------------------------------------ the residential harvest

        [CliCommand("gangsters_storefront",
                    "Bake, build, demo and audit GAN-294 live residential storefronts. Supports --seed, --unit and --draw.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "residential", "tests" })]
        public static object Storefront(
            [CliArg("what", "all, bake, bench, demo, or audit")] string what = "all",
            [CliArg("seed", "Deterministic storefront dressing seed.")] int seed = 1987,
            [CliArg("unit", "One harvested residential unit, or all when omitted.")] string unit = "",
            [CliArg("draw", "Stand the selected unit in the open scene without saving.")] bool draw = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");
            what = (what ?? "all").Trim().ToLowerInvariant();
            if (what != "all" && what != "bake" && what != "bench" &&
                what != "demo" && what != "audit")
                throw new ArgumentException("--what is all, bake, bench, demo, or audit.");

            StorefrontLeafBaker.BakeReport bake = null;
            StorefrontShowroom.Report bench = null;
            StorefrontTrafficShowroom.Report demo = null;
            if (what == "all" || what == "bake") bake = StorefrontLeafBaker.BakeAll();
            if (what == "all" || what == "bench") bench = StorefrontShowroom.Draw();
            if (what == "all" || what == "demo") demo = StorefrontTrafficShowroom.Draw();

            var meshAudit = StorefrontLeafBaker.Audit();
            var contracts = LivingCity.Tests.StorefrontContractTests.Run();
            var unitAudit = StorefrontUnitAudit(seed, unit, draw);
            object prefabAudit = null;
            if (what == "all" || what == "audit") prefabAudit = StorefrontAudit();
            bool passed = (bake == null || bake.Passed) &&
                          (bench == null || bench.Passed) &&
                          (demo == null || demo.Passed) &&
                          meshAudit.Passed && contracts.Passed && unitAudit.Passed;
            return new
            {
                passed, what, seed, unit = string.IsNullOrWhiteSpace(unit) ? "all" : unit,
                draw, bake, bench, demo, meshAudit, contracts, unitAudit, prefabAudit
            };
        }

        public sealed class StorefrontUnitAuditReport
        {
            public int Seed;
            public bool Drawn;
            public string[] Failures = Array.Empty<string>();
            public StorefrontUnitAuditRow[] Rows = Array.Empty<StorefrontUnitAuditRow>();
            public bool Passed => Failures.Length == 0 && Rows.Length > 0;
        }

        public sealed class StorefrontUnitAuditRow
        {
            public string Unit;
            public int ExpectedBays;
            public int Storefronts;
            public int ExpectedPanes;
            public int Panes;
            public int ExpectedLeaves;
            public int Leaves;
            public bool Deterministic;
            public bool Passed;
        }

        static StorefrontUnitAuditReport StorefrontUnitAudit(
            int seed, string requestedUnit, bool draw)
        {
            var report = new StorefrontUnitAuditReport { Seed = seed, Drawn = draw };
            var failures = new List<string>();
            var rows = new List<StorefrontUnitAuditRow>();
            var eligible = ResidentialUnits.All.Where(candidate =>
                candidate != null && !ResidentialUnits.IsLot(candidate) &&
                candidate.Kind != ResidentialKind.Storefront &&
                candidate.Shops != null && candidate.Shops.Any(count => count > 0) &&
                candidate.ShopBays != null && candidate.ShopBays.Any(bay =>
                    StorefrontDoorCatalog.TryGet(bay.Module, out _))).ToList();
            if (!string.IsNullOrWhiteSpace(requestedUnit))
                eligible = eligible.Where(candidate => string.Equals(
                    candidate.Name, requestedUnit.Trim(),
                    StringComparison.OrdinalIgnoreCase)).ToList();
            else if (draw)
                eligible = eligible.Where(candidate => candidate.Name == "residential-06").ToList();

            if (eligible.Count == 0)
                failures.Add("No eligible residential shop unit matched --unit.");

            for (int i = 0; i < eligible.Count; i++)
            {
                var candidate = eligible[i];
                string path = $"{ResidentialHarvest.OutDir}/{candidate.Name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    failures.Add(candidate.Name + ": source prefab missing");
                    continue;
                }

                GameObject instance = null;
                bool prefabContents = !draw;
                try
                {
                    if (draw)
                    {
                        var scene = EditorSceneManager.GetActiveScene();
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        instance.name = "GAN-294 storefront preview · " + candidate.Name;
                        instance.transform.position = new Vector3(i * 20f, 0f, 0f);
                        Undo.RegisterCreatedObjectUndo(instance, "Draw storefront preview");
                    }
                    else
                        instance = PrefabUtility.LoadPrefabContents(path);

                    int expectedPanes = ResidentialBlocks.AuditStorefrontOpeningCount(
                        instance, candidate, Vector3.zero);
                    ResidentialBlocks.BuildStorefrontPreview(instance, candidate, seed);
                    var storefronts = instance.GetComponentsInChildren<RoadDemo.Storefront>(true);
                    string firstBuild = StorefrontBuildSignature(instance, storefronts);
                    // Exercise the same inactive-child reuse that refreshes and pooled
                    // leases depend on, then compare the real rebuilt hierarchy.
                    foreach (var storefront in storefronts)
                        storefront.gameObject.SetActive(false);
                    ResidentialBlocks.BuildStorefrontPreview(instance, candidate, seed);
                    storefronts = instance.GetComponentsInChildren<RoadDemo.Storefront>(true);
                    string secondBuild = StorefrontBuildSignature(instance, storefronts);
                    int expectedBays = candidate.ShopBays.Count(bay => bay.Door.Leaves > 0);
                    int expectedLeaves = candidate.ShopBays.Sum(bay => bay.Door.Leaves);
                    int panes = storefronts.Sum(front => front.PaneCount);
                    int leaves = storefronts.Sum(front => front.LeafCount);
                    bool deterministic = firstBuild == secondBuild &&
                        storefronts.All(front => front.gameObject.activeSelf);
                    bool countsMatch = storefronts.Length == expectedBays &&
                                       panes == expectedPanes && leaves == expectedLeaves;
                    bool passed = countsMatch && deterministic;
                    if (!countsMatch)
                        failures.Add(candidate.Name + ": expected " + expectedBays + "/" +
                            expectedPanes + "/" + expectedLeaves + " storefronts/panes/leaves, got " +
                            storefronts.Length + "/" + panes + "/" + leaves + ".");
                    if (!deterministic)
                        failures.Add(candidate.Name +
                            ": repeated build drifted or left an inactive storefront bay.");
                    rows.Add(new StorefrontUnitAuditRow
                    {
                        Unit = candidate.Name,
                        ExpectedBays = expectedBays,
                        Storefronts = storefronts.Length,
                        ExpectedPanes = expectedPanes,
                        Panes = panes,
                        ExpectedLeaves = expectedLeaves,
                        Leaves = leaves,
                        Deterministic = deterministic,
                        Passed = passed,
                    });
                    if (draw)
                        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }
                catch (Exception exception)
                {
                    failures.Add(candidate.Name + ": " + exception);
                }
                finally
                {
                    if (prefabContents && instance != null)
                        PrefabUtility.UnloadPrefabContents(instance);
                }
            }

            report.Rows = rows.ToArray();
            report.Failures = failures.ToArray();
            return report;
        }

        static string StorefrontBuildSignature(
            GameObject root, IEnumerable<RoadDemo.Storefront> storefronts)
        {
            if (root == null || storefronts == null) return string.Empty;
            return string.Join(";", storefronts
                .Where(front => front != null)
                .OrderBy(front => front.name, StringComparer.Ordinal)
                .Select(front =>
                {
                    Vector3 door = root.transform.InverseTransformPoint(front.DoorWorld);
                    Vector3 outward = root.transform.InverseTransformDirection(front.OutwardWorld);
                    Bounds binding = front.BindingBounds;
                    Vector3 centre = root.transform.InverseTransformPoint(binding.center);
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}|{1}|{2}|{3:R},{4:R},{5:R}|{6:R},{7:R},{8:R}|" +
                        "{9:R},{10:R},{11:R}|{12:R},{13:R},{14:R}|{15:R}|{16}|{17}",
                        front.name, front.Module, front.gameObject.activeSelf,
                        door.x, door.y, door.z, outward.x, outward.y, outward.z,
                        centre.x, centre.y, centre.z,
                        binding.size.x, binding.size.y, binding.size.z,
                        front.FrontageWidth, front.PaneCount, front.LeafCount);
                }));
        }

        [CliCommand("gangsters_storefront_refresh",
                    "Refresh only generated storefront interiors in the open ResidentialDemo; does not save or rebuild blocks.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "residential" })]
        public static object StorefrontRefresh()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ResidentialSketch.DemoScene)
                throw new InvalidOperationException(
                    "Open ResidentialDemo before refreshing its storefront interiors.");

            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refresh residential storefront interiors");
            foreach (var root in scene.GetRootGameObjects())
            {
                bool carriesStorefront = root.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(component => component != null &&
                                      component.GetType().Name == "ResidentialStorefrontShell");
                if (carriesStorefront)
                    Undo.RegisterFullObjectHierarchyUndo(root,
                        "Refresh residential storefront interiors");
            }

            var report = ResidentialBlocks.RefreshExistingStorefronts(scene);
            if (report.Buildings > 0) EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            return new
            {
                passed = report.Buildings > 0 && report.Failures.Length == 0,
                scene = scene.path,
                saved = false,
                report.Buildings,
                report.Openings,
                report.Displays,
                report.RemovedGeneratedProps,
                report.RemovedLongPieces,
                report.Failures,
            };
        }

        [CliCommand("gangsters_storefront_audit",
                    "Check every harvested residential/shop prefab can receive a shallow interior, without changing it.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "residential", "tests" })]
        public static object StorefrontAudit()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            var rows = new List<object>();
            var failures = new List<string>();
            foreach (var unit in ResidentialUnits.All.Where(unit =>
                         unit != null && !ResidentialUnits.IsLot(unit) &&
                         (unit.Kind == ResidentialKind.Storefront ||
                          (unit.Shops != null && unit.Shops.Any(count => count > 0)))))
            {
                string path = $"{ResidentialHarvest.OutDir}/{unit.Name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    failures.Add($"{unit.Name}: missing {path}");
                    rows.Add(new { unit = unit.Name, prefab = path, openings = 0, passed = false });
                    continue;
                }

                GameObject contents = null;
                int openings = 0;
                try
                {
                    contents = PrefabUtility.LoadPrefabContents(path);
                    openings = ResidentialBlocks.AuditStorefrontOpeningCount(
                        contents, unit, Vector3.zero);
                }
                finally
                {
                    if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                }

                bool passed = openings > 0;
                if (!passed) failures.Add($"{unit.Name}: no measurable storefront opening");
                rows.Add(new
                {
                    unit = unit.Name,
                    kind = unit.Kind.ToString(),
                    declaredShops = unit.Shops?.Sum() ?? 0,
                    openings,
                    passed,
                });
            }

            return new
            {
                passed = failures.Count == 0,
                checkedPrefabs = rows.Count,
                failures = failures.ToArray(),
                rows = rows.ToArray(),
            };
        }

        /// <summary>
        /// The residential harvest, from the terminal: the units the user named in the
        /// harvest scene and in the Palm City demo, measured, baked to prefabs and written
        /// into the table the generator deals from.
        ///
        /// The menu item for the same job ends in a dialog, which stops the editor's main
        /// thread dead when it is called from here. It opens the source scenes itself, so
        /// it does not matter which one is in front.
        /// </summary>
        [CliCommand("gangsters_harvest",
                    "Measure every named residential unit in the harvest scene and the Palm City demo, " +
                    "bake a prefab for each and write ResidentialUnits.cs. Returns the measurements.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Harvest(
            [CliArg("report", "Include the full measured report, plan by plan.")] bool report = false)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");

            int wrote = ResidentialHarvest.Bake(out var units, out string text);
            Debug.Log(text);
            var rows = units.Select(u => new
            {
                name = u.Name,
                kind = u.Klass.ToString(),
                cells = $"{u.CW}x{u.CD}",
                metres = $"{u.CW * 5}x{u.CD * 5}",
                faces = string.Concat(new[] { "S", "E", "N", "W" }.Select((s, i) => u.Face[i] ? s : "-")),
                doors = u.Doors.Sum(),
                shops = u.Shops.Sum(),
                walled = u.Built,
                seats = u.Seats,
                pieces = u.Pieces,
                height = Mathf.Round(u.MaxH * 10f) / 10f,
            }).ToArray();

            return new { units = units.Count, wrote, rows, report = report ? text : null };
        }

        [CliCommand("gangsters_bake_turf_prefabs",
                    "Bake prepared TurfMap proxy data into every existing residential prefab.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object BakeTurfPrefabs()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("The editor is in play mode; leave it first.");
            int changed = ResidentialHarvest.BakeTurfProxyData();
            return new { changed, ready = true };
        }
    }
}
