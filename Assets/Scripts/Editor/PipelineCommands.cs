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
            failures.AddRange(LivingCity.Tests.SkillFoundationTests.Run()
                .Select(failure => "Skill regression: " + failure));
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
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
                    if (line.Contains("TAKE IT"))
                        failures.Add(relative + ":" + (i + 1) + " revives the TAKE IT claim.");
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
                    rivalDemandPresence = racket.Config.RivalDemandPresence,
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
            var runtime = seed < 0
                ? UnityEngine.Object.FindAnyObjectByType<LivingCity.Business.BusinessRuntime>()
                : null;

            LivingCity.Business.BusinessSiteCatalog catalog;
            LivingCity.Business.BusinessDirectory directory;
            LivingCity.Business.BusinessPopulationReport report;
            int citySeed;
            bool live = runtime != null && runtime.Populated;

            if (live)
            {
                catalog = runtime.Catalog;
                directory = runtime.Directory;
                report = runtime.Report;
                citySeed = runtime.CitySeed;
            }
            else
            {
                // No city standing: deal one from the seed. CoreDistrict.Plan is pure data
                // - no prefab is loaded and no GameObject is made - which is exactly why
                // the sweep can be audited from the terminal with the editor idle.
                citySeed = seed < 0 ? 1987 : seed;
                var core = new CoreDistrict();
                core.Plan(null, citySeed);
                core.Frame = DistrictFrame.Identity;

                catalog = new LivingCity.Business.BusinessSiteCatalog();
                catalog.Add(new LivingCity.Business.ResidentialBusinessSites(
                    core.ResidentialBlocks, core.Frame));
                catalog.Add(new LivingCity.Business.StandaloneBusinessSites(core));
                catalog.Add(new LivingCity.Business.CompoundBusinessSites(core, null));
                catalog.Build();

                directory = new LivingCity.Business.BusinessDirectory();
                report = LivingCity.Business.BusinessPopulation.Populate(
                    catalog, citySeed, directory);
            }
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

        // ------------------------------------------------------------ the residential harvest

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
                report.Closed,
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
