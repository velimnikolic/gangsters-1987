using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadDemo;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Industrial blocks, COMPOSED the way the core's sixteen were found.
    ///
    /// The core blocks came out of the Synty city demo by harvest: their artists laid them
    /// and the tray cut them out (<see cref="CoreBlockTray"/>). That pack has no industry
    /// in it - no works, no depot, no stockyard - so those blocks have to be built. All the
    /// care here goes on making a built block indistinguishable from a harvested one once
    /// it is on disk, because both land in the same folder and are read by the same
    /// <see cref="RoadDemo.CoreLayout"/>:
    ///
    ///   - the block carries its OWN kerb, one 5 m tile of it the whole way round, and the
    ///     road outside is somebody else's business. That is the difference between this
    ///     pipeline and the grid city's, where the street lays 6.5 m of pavement itself and
    ///     the block is only the interior.
    ///   - every piece is a prefab INSTANCE. The bake replays each one's overrides onto a
    ///     fresh instance of its source, which is the only way a block comes out of it in
    ///     the colours it was composed in.
    ///   - the floor is calm: one tile to a surface, laid straight, no chequerboard and no
    ///     mixing of packs. Two surfaces at most, and the second only where the yard is
    ///     worked.
    ///   - nothing stands on the kerb ring. It is pavement, and people walk on it.
    ///
    /// The four candidates are the point of the thing. A composed block is a guess, and the
    /// cheapest way to be right is to stand four guesses in a row, look at them, and keep
    /// the ones that read as a place. <see cref="Generate"/> stands them up;
    /// <see cref="BakeChosen"/> files the ones asked for and throws the rest away.
    ///
    /// WHAT IS AND IS NOT HERE (2026-08-26). This file is now a BENCH: the lab scene, the
    /// four candidates, the caption and the bake. The composing itself moved out to
    /// <see cref="RoadDemo.IndustrialBlocks"/> so that the industrial QUARTER
    /// (<see cref="RoadDemo.IndustrialLayout"/>) could stand parcels with the same recipes -
    /// the same move <c>CoreRoads</c> made when it came out of <c>CoreCitySketch</c>. A
    /// recipe fixed for the quarter is therefore fixed here, and a candidate baked off this
    /// bench is made of exactly what the district is made of.
    ///
    /// One thing changed in the move and is worth saying out loud: the service strip no
    /// longer takes a bite out of its own north-east corner. A bite is ground the STREET
    /// takes back as parking, which needs a street to take it - true of a block dropped into
    /// the core, not of a parcel with a neighbour's fence along that corner.
    /// </summary>
    public static class IndustrialBlockForge
    {
        // ---------------------------------------------------------------- the furniture

        /// <summary>The scene root the candidates stand under. <see cref="CoreBlockTray"/>
        /// steps over it when it sweeps, so a candidate can never be swallowed by a tray
        /// rectangle it happens to be standing near.</summary>
        internal const string CandidatesRoot = "INDUSTRIAL CANDIDATES";

        /// <summary>The workbench, and deliberately a thin scene. The harvest scene carries
        /// the whole Synty demo, takes seconds to write, and a bake that saves at the end
        /// of itself runs out of the pipeline's patience there.</summary>
        internal const string LabPath = "Assets/Scenes/IndustrialLab.unity";

        const string CandidatePrefix = "candidate-";
        const string LabelName = "label";

        /// <summary>Walking room between two candidates, so neither reads as part of the
        /// other from above.</summary>
        const float Walk = 30f;

        /// <summary>
        /// What "all" deals: two works, a stockyard and a service strip.
        ///
        /// The depot is a warehouse and there is a stockyard in the four already, so it is
        /// kept for --recipe depot. The quarter's own three - the haulage yard, the tank farm
        /// and the empty plot - are not dealt here either: each is a one-off that only means
        /// anything among other parcels (a filling station on a quarter's corner, a plot
        /// nobody has built on YET), and standing four of them in a row on a bench says
        /// nothing about whether they work.
        /// </summary>
        static readonly IndustrialLayout.Recipe[] Dealt =
        {
            IndustrialLayout.Recipe.Works, IndustrialLayout.Recipe.Plant,
            IndustrialLayout.Recipe.Yard, IndustrialLayout.Recipe.Strip,
        };

        /// <summary>
        /// How big a candidate is dealt: the smallest the recipe can fill, and up to three
        /// cells more each way.
        ///
        /// The forge used to pick from a pair of sizes it kept itself. Now the sizes belong
        /// to <see cref="IndustrialLayout.Smallest"/>, because the quarter deals parcels
        /// against that table and a bench that tried other sizes would be testing something
        /// the district never asks for.
        /// </summary>
        static void Size(IndustrialLayout.Recipe recipe, System.Random rng, out int w, out int d)
        {
            IndustrialLayout.Smallest(recipe, out int cellsW, out int cellsD);
            w = (cellsW + rng.Next(0, 4)) * 5;
            d = (cellsD + rng.Next(0, 3)) * 5;
        }

        static IndustrialLayout.Recipe Choose(string which, int index)
        {
            switch ((which ?? "all").Trim().ToLowerInvariant())
            {
                case "works": return IndustrialLayout.Recipe.Works;
                case "plant": return IndustrialLayout.Recipe.Plant;
                case "depot": return IndustrialLayout.Recipe.Depot;
                case "yard": return IndustrialLayout.Recipe.Yard;
                case "strip": return IndustrialLayout.Recipe.Strip;
                // "stop" still answers, because that is what this was called when it was a
                // truck stop and a note somewhere will still say so
                case "haulage": case "stop": return IndustrialLayout.Recipe.Haulage;
                case "fuel": return IndustrialLayout.Recipe.Fuel;
                case "waste": return IndustrialLayout.Recipe.Waste;
                default: return Dealt[index % Dealt.Length];
            }
        }

        /// <summary>Stands four candidates in a row and says a line about each, which is
        /// what the pipeline command hands back.</summary>
        public static object[] Generate(int seed, string which) =>
            InTheLab(scene => GenerateIn(scene, seed, which));

        static object[] GenerateIn(Scene scene, int seed, string which)
        {
            Wipe(scene);
            IndustrialBlocks.ForgetMissing();

            var root = new GameObject(CandidatesRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            var told = new List<object>();
            float x = 0f;

            for (int k = 0; k < 4; k++)
            {
                var recipe = Choose(which, k);
                var rng = new System.Random(seed * 97 + k * 31 + (int)recipe * 7);

                var candidate = new GameObject($"{CandidatePrefix}{k + 1}");
                candidate.transform.SetParent(root.transform, false);

                // composed at the origin and moved afterwards: every piece is placed by
                // measuring where it lands, and measuring is done in world space.
                //
                // Every side of a bench candidate is its own kerb and its own fence
                // (IndustrialBlocks.Alone) - a block baked for the core stands alone in a
                // row of streets. Only in the QUARTER does a parcel share a fence with the
                // works next door.
                Size(recipe, rng, out int wide, out int deep);
                var block = IndustrialBlocks.Stand(recipe, candidate.transform, wide, deep,
                                                   IndustrialBlocks.Alone(), rng,
                                                   (prefab, parent) =>
                                                       (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                // the same pavement furniture the quarter gives it. Left off, a block baked
                // here would go into the core with no lamp on its kerb while the identical
                // recipe in the district had one - and the two are meant to be the same
                // block (CorePavement bakes the core's own lamps into the core's blocks too)
                block.Streetside(rng);

                int pieces = candidate.transform.childCount;
                candidate.transform.position = new Vector3(x, 0f, 0f);
                Caption(candidate.transform, k + 1, recipe, block, seed);
                x += block.W + Walk;

                told.Add(new
                {
                    index = k + 1,
                    recipe = recipe.ToString().ToLowerInvariant(),
                    seed,
                    width = block.W,
                    depth = block.D,
                    pieces,
                    gaps = block.Gaps(),
                    wallGap = Mathf.Round(block.WallGap * 100f) / 100f,
                    wallInBuilding = block.WallInBuilding(),
                    refused = block.Refused(),
                });
            }

            Undo.RegisterCreatedObjectUndo(root, "Industrial candidates");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;

            // written out at once. A set of candidates that only exists in memory is a set
            // that whoever opens another scene next throws away, and this editor is not
            // always ours alone.
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            if (IndustrialBlocks.Missing.Count > 0)
                Debug.LogWarning("[Industrial] missing from the project, and left out of every " +
                                 "candidate:\n  " + string.Join("\n  ", IndustrialBlocks.Missing));

            return told.ToArray();
        }

        static void Caption(Transform candidate, int index, IndustrialLayout.Recipe recipe,
                            IndustrialBlocks.Block block, int seed)
        {
            BlockLotPads.PadLabel(LabelName,
                                  $"{CandidatePrefix}{index}\n" +
                                  $"{recipe.ToString().ToLowerInvariant()} | " +
                                  $"{block.W} x {block.D} m | seed {seed}",
                                  candidate.position + new Vector3(block.W * 0.5f, 6f, block.D + 4f),
                                  candidate);
            var label = candidate.Find(LabelName);
            if (label) label.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        static GameObject Found(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);

        static void Wipe(Scene scene)
        {
            var root = Found(scene, CandidatesRoot);
            if (root) Object.DestroyImmediate(root);
        }

        // -------------------------------------------------------------------- baking

        sealed class Chosen
        {
            public int Index;
            public string Name, Path, Trouble;
        }

        /// <summary>
        /// Files the candidates asked for and throws the rest away.
        ///
        /// It writes no prefab itself. The pieces are moved onto a tray and
        /// <see cref="CoreBlockTray"/> bakes them, so a composed block goes through exactly
        /// the same door as a harvested one: same pivot rule, same flattening, same replay
        /// of overrides, same <c>BlockLotTag</c>, same folder.
        /// </summary>
        public static object[] BakeChosen(int[] indices, string[] names, bool keepOthers) =>
            InTheLab(scene => BakeIn(scene, indices, names, keepOthers));

        static object[] BakeIn(Scene scene, int[] indices, string[] names, bool keepOthers)
        {
            var root = Found(scene, CandidatesRoot);
            if (root == null)
                return new object[]
                {
                    new { error = "no candidates are standing; generate some first" },
                };

            var chosen = new List<Chosen>();
            var trays = new List<Transform>();

            for (int k = 0; k < indices.Length; k++)
            {
                int index = indices[k];
                var candidate = root.transform.Find($"{CandidatePrefix}{index}");
                if (candidate == null)
                {
                    chosen.Add(new Chosen { Index = index, Trouble = "no such candidate" });
                    continue;
                }

                string name = names != null && k < names.Length && !string.IsNullOrEmpty(names[k])
                    ? names[k].Trim()
                    : NextName(RecipeOf(candidate));
                string path = $"{CoreBlockTray.OutDir}/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    chosen.Add(new Chosen
                    {
                        Index = index,
                        Name = name,
                        Trouble = "a block of that name is already on disk; pass another --names",
                    });
                    continue;
                }

                var box = new Bounds(candidate.position, Vector3.one);
                if (IndustrialBlocks.WorldBox(candidate.gameObject, out var measured)) box = measured;

                var tray = CoreBlockTray.MakeTray(scene, name,
                                                  new Vector3(box.center.x, 0f, box.center.z),
                                                  box.size.x + 2f, box.size.z + 2f);

                var pieces = new List<Transform>();
                foreach (Transform piece in candidate) pieces.Add(piece);
                foreach (var piece in pieces)
                {
                    if (piece.name == LabelName)
                    {
                        Object.DestroyImmediate(piece.gameObject);
                        continue;
                    }
                    piece.SetParent(tray, true);
                }
                Object.DestroyImmediate(candidate.gameObject);

                trays.Add(tray);
                chosen.Add(new Chosen { Index = index, Name = name, Path = path });
            }

            int written = CoreBlockTray.BakeQuietly(scene, out var said);
            Debug.Log($"[Industrial] {written} block prefab(s) written to " +
                      CoreBlockTray.OutDir + ": " + string.Join("; ", said));

            // the trays have done their work and the bake has emptied them; left standing,
            // they would take in whatever is dragged near them next
            foreach (var tray in trays)
                if (tray) Undo.DestroyObjectImmediate(tray.gameObject);

            if (!keepOthers) Wipe(scene);
            if (Found(scene, CoreBlockTray.ReviewRoot)) CoreBlockTray.ShowBaked();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            return chosen.Select(one => (object)new
            {
                index = one.Index,
                name = one.Name,
                path = one.Path,
                wrote = one.Path != null &&
                        AssetDatabase.LoadAssetAtPath<GameObject>(one.Path) != null,
                trouble = one.Trouble,
            }).ToArray();
        }

        /// <summary>What the caption says this candidate was composed from, so that a bake
        /// nobody named is filed as the kind of block it is.</summary>
        static string RecipeOf(Transform candidate)
        {
            var label = candidate.Find(LabelName);
            var text = label ? label.GetComponent<TextMesh>() : null;
            if (text == null) return "block";
            var lines = text.text.Split('\n');
            if (lines.Length < 2) return "block";
            var first = lines[1].Split('|')[0].Trim();
            return string.IsNullOrEmpty(first) ? "block" : first;
        }

        static string NextName(string recipe)
        {
            int highest = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CoreBlockTray.OutDir }))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(guid));
                var match = System.Text.RegularExpressions.Regex.Match(
                    name, $@"^ind-{recipe}-(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                    highest = Mathf.Max(highest, n);
            }
            return $"ind-{recipe}-{highest + 1:00}";
        }

        // --------------------------------------------------------------------- the lab

        /// <summary>
        /// The workbench scene, loaded BESIDE whatever is open rather than instead of it.
        ///
        /// Its own scene, not the harvest's, because the harvest scene carries the whole
        /// Synty demo and takes seconds to write - a bake that saves at the end of itself
        /// runs out of the pipeline's patience there. And loaded additively, because this
        /// editor is not always ours alone: a second session working in another scene, with
        /// changes it has not saved, must not have that scene shut from under it. The lab
        /// is made active only for as long as the work takes, and put back afterwards.
        /// </summary>
        internal static Scene Lab()
        {
            var open = SceneManager.GetActiveScene();
            if (open.path == LabPath) return open;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded.path == LabPath) return loaded;
            }

            if (System.IO.File.Exists(LabPath))
                return EditorSceneManager.OpenScene(LabPath, OpenSceneMode.Additive);

            var made = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var light = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(light, made);
            var sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var trays = new GameObject(CoreBlockTray.TraysRoot);
            SceneManager.MoveGameObjectToScene(trays, made);

            EditorSceneManager.SaveScene(made, LabPath);
            Debug.Log($"[Industrial] {LabPath} made, beside whatever else is open.");
            return made;
        }

        /// <summary>Runs a piece of work with the lab active, and puts the editor back the
        /// way it was found. Everything an editor tool stands up - a temporary instance
        /// taken to measure a prefab, a tray - lands in the ACTIVE scene, so making the lab
        /// active for the duration is what keeps it all out of somebody else's.
        ///
        /// Shared rather than private: the lab is the bake bench, not the industry's own
        /// bench, and <see cref="CoreBuildingBlocks"/> bakes on it for the same reason this
        /// does - a bake that saves at the end of itself cannot run in the harvest scene.</summary>
        internal static T InTheLab<T>(System.Func<Scene, T> work)
        {
            var lab = Lab();
            var was = SceneManager.GetActiveScene();
            bool moved = was != lab && lab.IsValid();
            if (moved) SceneManager.SetActiveScene(lab);
            try { return work(lab); }
            finally { if (moved && was.IsValid()) SceneManager.SetActiveScene(was); }
        }

        [MenuItem("Tools/City/Core/Industrial/Open The Industrial Lab", priority = 60)]
        public static void OpenLab()
        {
            var lab = Lab();
            if (lab.IsValid() && SceneManager.GetActiveScene() != lab)
                SceneManager.SetActiveScene(lab);
        }

        [MenuItem("Tools/City/Core/Industrial/Generate Four Candidates", priority = 61)]
        public static void GenerateMenu()
        {
            var told = Generate(7, "all");
            var root = Found(Lab(), CandidatesRoot);
            var view = SceneView.lastActiveSceneView;
            if (view && root && IndustrialBlocks.WorldBox(root, out var box)) view.Frame(box, false);
            Debug.Log($"[Industrial] {told.Length} candidates standing under \"{CandidatesRoot}\". " +
                      "Keep the ones worth keeping with Bake Candidate N, or with " +
                      "unity command gangsters_industrial --bake 1,2");
        }

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 1", priority = 62)]
        public static void BakeOne() => BakeFromMenu(1);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 2", priority = 63)]
        public static void BakeTwo() => BakeFromMenu(2);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 3", priority = 64)]
        public static void BakeThree() => BakeFromMenu(3);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 4", priority = 65)]
        public static void BakeFour() => BakeFromMenu(4);

        static void BakeFromMenu(int index)
        {
            foreach (var one in BakeChosen(new[] { index }, null, keepOthers: true))
                Debug.Log($"[Industrial] {one}");
        }

        [MenuItem("Tools/City/Core/Industrial/Discard Candidates", priority = 70)]
        public static void DiscardMenu()
        {
            var scene = Lab();
            if (Found(scene, CandidatesRoot) == null)
            {
                Debug.Log("[Industrial] there were no candidates standing.");
                return;
            }
            Wipe(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Industrial] the candidates are gone; nothing was written.");
        }
    }
}
