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
        // ---------------------------------------------------------------- the plan

        /// <summary>The district roll for a seed, without building anything. This is the
        /// paper plan - the same call RoadDemoBuilder makes at Play - so it answers "what
        /// does seed N give me" in a second instead of a ninety-second run.</summary>
        [CliCommand("gangsters_layout",
                    "Roll the city district layout for a seed and return it, without building or playing. " +
                    "Reads the RoadDemoBuilder in the open scene for the road axes.",
                    MainThreadRequired = true, Tags = new[] { "gangsters" })]
        public static object Layout(
            [CliArg("seed", "City layout seed. Omit to use the one the open scene carries.")] int seed = int.MinValue,
            [CliArg("count", "Roll this many consecutive seeds starting at 'seed' and return a summary of each.")] int count = 1,
            [CliArg("scene", "Scene to open first, e.g. Assets/Scenes/Game.unity. Omit to use the scene already open.")] string scene = "")
        {
            if (!string.IsNullOrEmpty(scene))
            {
                if (EditorApplication.isPlaying)
                    throw new InvalidOperationException("The editor is in play mode; stop it (editor_stop) before opening a scene.");
                var opened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                if (!opened.IsValid()) throw new ArgumentException($"Scene '{scene}' would not open.");
            }

            var city = UnityEngine.Object.FindAnyObjectByType<RoadDemoBuilder>();
            if (city == null)
                throw new InvalidOperationException(
                    "No RoadDemoBuilder in the open scene. Pass --scene Assets/Scenes/Game.unity (every demo scene carries one).");

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
                rollDistricts = city.rollDistricts,
                suburbs = $"{city.suburbsMin}-{city.suburbsMax}",
                rolls = results,
            };
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
            var yard = new GameObject("core (dealing)");
            try
            {
                var blocks = LivingCity.EditorTools.CoreCitySketch.Stand(
                    yard.transform, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                for (int i = 0; i < rolls; i++)
                {
                    int s = seed == CoreLayout.SyntySeed ? (i == 0 ? seed : i) : seed + i;
                    var plan = CoreLayout.Arrange(blocks, s, out var raster);
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
                        spareM2 = raster.SpareArea,
                        size = $"{raster.NX * 5}x{raster.NZ * 5}",
                        rows = plan.Rows.ToArray(),
                        report = raster.Report.Split('\n').Select(line => line.Trim()).ToArray(),
                        map = map ? raster.Map : null,
                    });
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(yard);
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
    }
}
