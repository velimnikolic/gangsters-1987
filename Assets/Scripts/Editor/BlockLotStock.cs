using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Fills the city's block folder with rolled blocks, so the RoadDemo grid is built out
    /// of compositions instead of the same handful of bakes.
    ///
    /// This is the only way the demo CAN use the randomiser. RoadDemoBuilder is a
    /// MonoBehaviour that builds at Play time out of Assembly-CSharp; every pass here lives
    /// in the editor assembly, which that one cannot reference (the dependency runs the
    /// other way). What it can do is what it already does: read
    /// <see cref="SyntyCityBlocks.BlocksDir"/> wholesale, file each bake carrying a
    /// <see cref="LivingCity.Generation.BlockLotTag"/> under that pad's code, and hand the
    /// bakes for a code out in turn - "a city with four B2 interiors shows four different
    /// blocks before it repeats". So the roller writes into that folder, and the demo picks
    /// them up with no edit to it at all.
    ///
    /// One run rolls <see cref="PerLot"/> blocks on EVERY lot pad and captures each: nine
    /// pad sizes times three is twenty-seven blocks, which is more than any one grid has
    /// interiors, so no size runs out of variety. Its own output is replaced rather than
    /// added to - the names are fixed (auto_A1_1 and its like) and last run's recipes go
    /// first - so running it twice leaves a stock of the same size, freshly rolled.
    ///
    /// Some rolls are SEEDED (<see cref="Seeds"/>): a hand-made block that fits inside a
    /// bigger pad is stood in a corner of it first and the rest randomised round it. That
    /// is how a hand-made block reaches a lot it does not fill - as one roll among the
    /// stock, never dropped centred into the lot by the demo, and never forced into a
    /// city whose spacing did not roll its own pad.
    ///
    /// The stock is deliberately kept off the workbench (<see cref="BlockLotCapture"/>):
    /// that bench is for the blocks being edited by hand, and twenty-seven generated ones
    /// standing on it would bury them. Re-roll instead of editing, or capture a keeper
    /// under a name of your own and it becomes an ordinary hand-made block.
    /// </summary>
    public static class BlockLotStock
    {
        /// <summary>What every generated recipe's name starts with: what marks the stock as
        /// this pass's own, to be replaced on the next run and kept off the workbench.
        /// Nothing else may be named this way.</summary>
        internal const string Prefix = "auto_";

        /// <summary>How many blocks are rolled for each lot size.</summary>
        const int PerLot = 3;

        /// <summary>
        /// Which roller made the stock on disk. Bumped whenever the roll's LOOK changes -
        /// the floor's tiles, what counts as a unit, how a seed is stood - so a stock rolled
        /// by an older roller is redone on the next Play (<see cref="RoadDemoAutoStock"/>)
        /// instead of the change waiting for someone to remember the menu command.
        ///   1 - first stock; 2 - one calm tile per surface, trimmed rows in place of
        ///   single slices, seeds measured by their pad; 3 - storefronts on the frontage
        ///   the rows leave.
        /// </summary>
        const int Version = 3;
        static readonly string VersionPath = BlockRecipeStore.Dir + "/stock-version.txt";

        /// <summary>Whether the stock on disk was rolled by an older roller than this one -
        /// or by none at all, when there is a stock but no stamp.</summary>
        internal static bool Outdated()
        {
            if (!System.IO.File.Exists(VersionPath))
                return BlockRecipeStore.LoadAll().Any(r => r.name.StartsWith(Prefix, System.StringComparison.Ordinal));
            return !int.TryParse(System.IO.File.ReadAllText(VersionPath).Trim(), out var v) || v != Version;
        }

        static void Stamp()
        {
            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
        }

        [MenuItem("Tools/City/Catalog/Randomise Blocks For Every Lot", priority = 64)]
        public static void Stock()
        {
            if (!BlockLotCapture.OpenCatalogScene())
                return;

            // The lot pads only. A workbench pad holds a block somebody composed by hand,
            // and rolling over it would destroy exactly the work this pass exists to keep
            // out of the way of.
            var pads = BlockPad.All().Where(p => !p.group && !string.IsNullOrEmpty(p.code)).ToList();
            if (pads.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No pads",
                    "This scene has no lot pads. Run Tools/City/Catalog/Draw Block Lot Pads " +
                    "first.", "OK");
                return;
            }

            var dropped = Clear();
            var made = new List<string>();
            var missed = new List<string>();
            var cancelled = false;

            // One fairground to a city, one hotel, one police station. The set is carried
            // through every roll, so a place spent on one lot is out of the draw for all
            // the rest - and it starts with whatever the hand-made blocks already carry,
            // because those go into the city ahead of any roll (RoadDemoBuilder), and a
            // rolled fairground beside b2block2's own would be exactly the double this is
            // here to stop.
            var landmarks = Spoken();
            var seeds = Seeds(pads);

            try
            {
                var step = 0f;
                var total = pads.Count * PerLot;
                for (var round = 1; round <= PerLot && !cancelled; round++)
                    foreach (var pad in pads)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Randomising blocks for every lot",
                                $"{pad.label} ({pad.width:F0} x {pad.depth:F0} m), block " +
                                $"{round} of {PerLot}", step++ / total))
                        {
                            cancelled = true;
                            break;
                        }

                        var name = $"{Prefix}{pad.code}_{round}";
                        seeds.TryGetValue((pad.code, round), out var seed);
                        switch (BlockRandomiser.Roll(pad, out _, out var story, landmarks, seed))
                        {
                            case BlockRandomiser.Result.NoUnits:
                                EditorUtility.ClearProgressBar();
                                EditorUtility.DisplayDialog(
                                    "Nothing to build with",
                                    "The catalog holds no assembled rows or palm block bakes to " +
                                    "build lots out of.\n\nRun Tools/City/Catalog/Build Synty " +
                                    "Building Catalog Scene first.", "OK");
                                return;

                            case BlockRandomiser.Result.NoFit:
                                missed.Add($"{pad.label} - nothing in the catalog fits it");
                                continue;
                        }

                        var recipe = BlockLotCapture.Capture(pad, name);
                        if (recipe == null)
                        {
                            missed.Add($"{name} - the roll stood nothing that resolved to a building");
                            continue;
                        }
                        made.Add($"{recipe.name} ({recipe.members.Count} buildings, " +
                                 $"{recipe.props.Count} props): {story}");
                    }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // The pads go back to bare ground. Every block rolled here is saved and baked;
            // leaving the last round of them standing would only mean the next capture
            // offering nine blocks nobody asked to save.
            foreach (var pad in pads)
                foreach (var rootName in new[] { BlockPad.BlockRoot, BlockPad.ParkingRoot,
                                                 BlockPad.PropsRoot, BlockPad.FloorRoot })
                    pad.ClearAuto(rootName);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            if (made.Count == 0)
            {
                Debug.LogWarning("[Stock] nothing was rolled" +
                                 (missed.Count > 0 ? ": " + string.Join("; ", missed) : "."));
                return;
            }

            AssetDatabase.SaveAssets();
            SyntyCityBlocks.Bake();
            if (!cancelled)
                Stamp();

            Debug.Log($"[Stock] {made.Count} block(s) rolled and baked to " +
                      $"{SyntyCityBlocks.BlocksDir}" +
                      (dropped > 0 ? $", replacing {dropped} from the last run" : "") +
                      (cancelled ? " (cancelled part way)" : "") + ":\n  " +
                      string.Join("\n  ", made) +
                      (missed.Count > 0 ? "\n[Stock] not rolled: " + string.Join("; ", missed) : "") +
                      "\n[Stock] RoadDemo reads that folder on Play and gives each interior the " +
                      "block composed for its own pad size, taking them in turn - no edit to the " +
                      "demo is needed. Run this again for a different city.");
        }

        /// <summary>
        /// Which rolls start from a hand-made block: for every hand-made block that fits
        /// inside a pad BIGGER than its own, the smallest such pad, first round free. The
        /// roller stands the block in a corner and randomises the rest of the pad round it
        /// - a hand-made block in a lot it does not fill is stood that way, never centred
        /// with bare ground on every side - and the result is stock like any other, so the
        /// demo takes it in turn and no hand-made block is FORCED into a city.
        ///
        /// One roll per hand-made block, and no two seeds sharing a place (b2block2 and
        /// residentialhighrise both carry the parking garage) - the second is left out
        /// with a note, since it would be the same place in two rolls.
        /// </summary>
        static Dictionary<(string code, int round), GameObject> Seeds(List<BlockPad> pads)
        {
            var seeds = new Dictionary<(string code, int round), GameObject>();
            var bakes = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { SyntyCityBlocks.BlocksDir }))
                bakes.Add(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            var seededPlaces = new HashSet<string>();
            var told = new List<string>();

            foreach (var recipe in BlockRecipeStore.LoadAll().OrderBy(r => r.name, System.StringComparer.Ordinal))
            {
                if (recipe.name.StartsWith(Prefix, System.StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(recipe.lot))
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{SyntyCityBlocks.BlocksDir}/{recipe.name}.prefab");
                if (!prefab)
                    continue;

                // The block is its pad: what its renderers show past the pad is dressing
                // over the kerb, and the roller measures a seed by its pad too.
                if (recipe.lotWidth <= 0f || recipe.lotDepth <= 0f)
                    continue;
                var own = recipe.lotWidth * recipe.lotDepth;

                // the smallest pad bigger than the block's own that holds it - the least
                // ground left over to randomise, which reads best as corner-and-rest
                var pad = pads
                    .Where(p => p.code != recipe.lot.Trim().ToUpperInvariant() &&
                                p.width * p.depth > own &&
                                p.width >= recipe.lotWidth && p.depth >= recipe.lotDepth)
                    .OrderBy(p => p.width * p.depth)
                    .FirstOrDefault();
                if (pad == null)
                    continue;

                var places = new HashSet<string>();
                Places(prefab.transform, bakes, places);
                if (places.Overlaps(seededPlaces))
                {
                    told.Add($"{recipe.name} not seeded - shares a place with a seed already chosen");
                    continue;
                }

                var round = 1;
                while (round <= PerLot && seeds.ContainsKey((pad.code, round)))
                    round++;
                if (round > PerLot)
                {
                    told.Add($"{recipe.name} not seeded - every {pad.code} roll already has a seed");
                    continue;
                }

                seeds[(pad.code, round)] = prefab;
                seededPlaces.UnionWith(places);
                told.Add($"{recipe.name} ({recipe.lot}) seeds {Prefix}{pad.code}_{round}");
            }

            if (told.Count > 0)
                Debug.Log("[Stock] seeds: " + string.Join("; ", told));
            return seeds;
        }

        /// <summary>
        /// The places the hand-made blocks already put in the city, by bare name. Read off
        /// the recipes rather than the bakes because a recipe names its members outright,
        /// and read from the hand-made ones only - the rolled stock is about to be thrown
        /// away and rolled again.
        /// </summary>
        static HashSet<string> Spoken()
        {
            var spoken = new HashSet<string>();
            foreach (var recipe in BlockRecipeStore.LoadAll())
            {
                if (recipe.name.StartsWith(Prefix, System.StringComparison.Ordinal))
                    continue;
                foreach (var member in recipe.members)
                    if (!string.IsNullOrEmpty(member.prefab) && BlockRandomiser.IsLandmark(member.prefab))
                        spoken.Add(member.prefab);
            }
            return spoken;
        }

        /// <summary>
        /// The lot codes with no block rolled for them, which is what
        /// <see cref="RoadDemoAutoStock"/> asks before every Play. A recipe alone does not
        /// count: the demo reads the BAKES, so a stock whose prefabs were deleted (or
        /// never baked) is a stock the city cannot use.
        /// </summary>
        internal static List<string> Missing()
        {
            var stocked = new HashSet<string>();
            foreach (var recipe in BlockRecipeStore.LoadAll())
            {
                if (!recipe.name.StartsWith(Prefix, System.StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(recipe.lot))
                    continue;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{SyntyCityBlocks.BlocksDir}/{recipe.name}.prefab"))
                    stocked.Add(recipe.lot.Trim().ToUpperInvariant());
            }
            return BlockLotPads.Codes().Where(c => !stocked.Contains(c)).ToList();
        }

        /// <summary>
        /// The places the rolled stock doubles - a fairground in two rolls, or in a roll
        /// and a hand-made block - as one line each, empty when the stock is sound. What
        /// <see cref="RoadDemoAutoStock"/> asks beside <see cref="Missing"/>: a stock rolled
        /// before the one-place-per-city rule (or with a hand-made block captured since)
        /// still exists, so it is not missing, but the demo would show the same place twice
        /// out of it. Two hand-made blocks sharing a place are the user's own arrangement
        /// and are left alone.
        ///
        /// Read off the BAKES the way the demo reads them (RoadDemoBuilder.LandmarksOf):
        /// a member is a direct child under the prefab's name, a prop a clone named SM_*,
        /// a nested PalmBlock is a place and is walked into for its buildings, and a
        /// nested composed block is fabric and is not - so a rolled block holding
        /// PalmBlock_05 and a hand-made one holding the Diner meet on "Diner", which is
        /// what the roll itself would have refused.
        /// </summary>
        internal static List<string> Doubled()
        {
            var bakes = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { SyntyCityBlocks.BlocksDir }))
                bakes.Add(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));

            var owners = new Dictionary<string, List<string>>();
            foreach (var recipe in BlockRecipeStore.LoadAll())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{SyntyCityBlocks.BlocksDir}/{recipe.name}.prefab");
                if (!prefab)
                    continue;
                var places = new HashSet<string>();
                Places(prefab.transform, bakes, places);
                foreach (var place in places)
                {
                    if (!owners.TryGetValue(place, out var list))
                        owners[place] = list = new List<string>();
                    list.Add(recipe.name);
                }
            }

            var doubled = new List<string>();
            foreach (var pair in owners)
                if (pair.Value.Count > 1 &&
                    pair.Value.Any(n => n.StartsWith(Prefix, System.StringComparison.Ordinal)))
                    doubled.Add($"{pair.Key} in {string.Join(", ", pair.Value.OrderBy(n => n))}");
            doubled.Sort();
            return doubled;
        }

        static void Places(Transform root, HashSet<string> bakes, HashSet<string> into)
        {
            foreach (Transform child in root)
            {
                var name = child.name;
                if (name.StartsWith("SM_", System.StringComparison.Ordinal) ||
                    !BlockRandomiser.IsLandmark(name))
                    continue;
                if (bakes.Contains(name))
                {
                    if (!name.StartsWith(SyntyCityBlocks.PalmBlockPrefix, System.StringComparison.Ordinal))
                        continue;
                    into.Add(name);
                    Places(child, bakes, into);
                    continue;
                }
                into.Add(name);
            }
        }

        /// <summary>Last run's stock, deleted. The recipes go here; their bakes go when
        /// <see cref="SyntyCityBlocks.Bake"/> next runs, which drops every prefab in the
        /// folder that no recipe names any more.</summary>
        static int Clear()
        {
            var gone = 0;
            foreach (var recipe in BlockRecipeStore.LoadAll())
                if (recipe.name.StartsWith(Prefix, System.StringComparison.Ordinal))
                {
                    BlockRecipeStore.Delete(recipe.name);
                    gone++;
                }
            return gone;
        }
    }
}
