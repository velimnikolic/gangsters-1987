using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Composes a block on the lot pad the user selected, out of WHOLE authored units -
    /// the catalog's assembled street rows (City_01..City_08) and the extracted palm
    /// blocks - by INTERLOCKING them the way the user's own blocks are built.
    ///
    /// That last word is the whole design, and it was read off the blocks the user made
    /// by hand rather than guessed:
    ///
    ///   * residentialblock1 is City_03 with City_05 turned 180 - their bounding boxes
    ///     OVERLAP by 30.7 x 27.0 m. The units are C-shaped, their open sides face each
    ///     other, and what comes out is one block of 26 buildings round a 40 x 21 m court.
    ///   * residentialblock2 is City_01 TWICE, the second one mirrored and turned 90,
    ///     boxes overlapping by 5.9 x 27.2 m.
    ///   * both are 51-57% built. b2block1 is 52%, residentialhighrise 76%.
    ///
    /// So: units are not rectangles to be packed side by side. Their bounding boxes may
    /// overlap as far as they like; what may never overlap is a BUILDING with a building.
    /// This pass therefore carries a mask of each unit's real per-building footprints and
    /// fits masks, not boxes - which is what lets a small unit drop into a big one's
    /// courtyard, exactly as City_02_B sits inside residentialblock2 in b2block1.
    ///
    /// Where a unit goes is decided by CONTACT: every candidate position is scored by how
    /// much of the unit's edge lands against the pad's own edge or against a building
    /// already standing. The highest score wins, so the first unit takes a corner and the
    /// next ones wrap round it. Nothing floats in the middle of a lot.
    ///
    /// Two rules the user stated outright:
    ///   * nothing may hang over the kerb line - a unit fits inside the pad or it is not
    ///     placed at all - but everything is stood hard against the edges;
    ///   * a unit stands at the height its pack authored it at, and what reaches below
    ///     zero stays buried. Every Synty group is assembled against a ground plane at
    ///     zero, so the geometry under it is a foundation skirt, a sunken doorway or a
    ///     bowl: City_04 and City_07 are brownstones whose area doors hang 1.50 m under
    ///     the pavement, the police station carries its garage 3.40 m down. Measuring
    ///     that dip and lifting the unit out of it - which this pass used to do - left
    ///     the buildings floating over the block instead. Where a hole is really wanted
    ///     the floor is what provides it: it reads the cells a bake is sunken in and lays
    ///     no tile over those (see BlockFloorFiller).
    ///
    /// Units stay PACKED - a placed City_03 is one prefab instance, not eleven loose
    /// buildings - which is what the capture pass writes down (one member per unit, as the
    /// user's own recipes already read).
    ///
    /// No BUILDING stands twice on one lot. Every authored unit is a named place rather
    /// than a texture to tile - PalmBlock_07 IS the fairground, PalmBlock_04 the hotel,
    /// City_02 the police station, PalmBlock_05 the car yard - so a lot showing two of them
    /// reads as a mistake however well they are packed. Each unit therefore carries the
    /// list of buildings it is made of (<see cref="Unit.members"/>), and a unit whose
    /// buildings already stand here is out of the draw. That covers the three ways a
    /// double could arise: the same unit rolled twice, two units sharing a building, and a
    /// unit doubling something the user dragged onto the pad by hand. Turning and
    /// mirroring stay, because they are what make one unit look different in two lots -
    /// they are no help at all on the same one.
    ///
    /// A PLACE stands once in the whole city, not once per lot. The fairground, the palm
    /// tower, the hotel, the car yard, the police station: a city has one of each, and two
    /// of them ten streets apart is as wrong as two side by side. So a batch carries one
    /// set of spent landmarks through every lot it rolls (see the cityLandmarks argument
    /// to <see cref="Roll"/>). The anonymous cluster rows are the exception and the reason
    /// the rule can be afforded: City_04 is terrace nobody can tell from City_06, and the
    /// fabric between the landmarks is made of it.
    ///
    /// Its output stands under "auto block" (see <see cref="BlockPad"/>), so a re-roll
    /// destroys only its own work and never a building the user dragged in - those are
    /// obstacles it fits around.
    ///
    /// This is a WHOLE block, not a heap of buildings: having stood the units up it runs
    /// the three dressing passes over them in turn - parking, then props, then floor (see
    /// <see cref="Dress"/> for why that order and no other) - so one command leaves a lot
    /// ready to capture. Each pass still writes its own auto root, so any single layer can
    /// be re-rolled on its own from the menu afterwards, and a re-roll of the whole block
    /// throws all three away: that dressing was planned against buildings which are no
    /// longer there.
    /// </summary>
    public static class BlockRandomiser
    {
        internal const string AutoRoot = BlockPad.BlockRoot;

        /// <summary>The occupancy mask's resolution, and with it how tightly two units may
        /// interlock. A quarter of the kit module.</summary>
        const float Cell = 1.25f;

        /// <summary>How far the placement search steps between candidate positions, in
        /// cells. Every flush-to-an-edge position is tried whatever the step, so this only
        /// coarsens the interior.</summary>
        const int Step = 2;

        /// <summary>How much of the lot ends up built. Read off the user's blocks: 51, 52,
        /// 57 and 76 per cent - a block is half open ground, and packing it fuller is what
        /// makes it read as a warehouse estate.</summary>
        const float MinBuilt = 0.55f;
        const float MaxBuilt = 0.78f;

        /// <summary>How many units one lot may be made of. Generous, because the trimmed rows
        /// that fill a lot out are small: what really ends a block is the built target
        /// above, and this is only here so a pathological lot cannot loop for ever.</summary>
        const int MaxUnits = 12;

        /// <summary>How many storefronts a lot takes AFTER the rows: drawn between one and
        /// this many, stood on the frontage the rows left. Beyond three the block reads as
        /// a strip mall rather than a residential block with a cafe on the corner.</summary>
        const int MaxCommerce = 3;

        /// <summary>How built a lot may get once the storefronts go in. They stand in the
        /// ground the built target left over, so they push past it by design; this is
        /// where that stops, so a lot still keeps a yard for the parking and the props.</summary>
        const float CommerceCeiling = 0.85f;

        [MenuItem("Tools/City/Catalog/Randomise Block On Lot", priority = 63)]
        public static void Roll()
        {
            if (!BlockLotCapture.OpenCatalogScene())
                return;
            if (!BlockPad.TryPick(out var pad, requireContent: false))
                return;

            switch (Roll(pad, out var root, out var story))
            {
                case Result.NoUnits:
                    EditorUtility.DisplayDialog(
                        "Nothing to build with",
                        "Neither the catalog's assembled rows (City_01 and its like) nor the " +
                        "palm block bakes could be found, so there is nothing to build this lot " +
                        "out of.\n\nRun Tools/City/Catalog/Build Synty Building Catalog Scene " +
                        "first.", "OK");
                    return;

                case Result.NoFit:
                    EditorUtility.DisplayDialog(
                        "Nothing fits",
                        $"No authored unit fits inside {pad.label} ({pad.width:F0} x " +
                        $"{pad.depth:F0} m) without hanging over its edge. Clear the pad and " +
                        "run this again.", "OK");
                    return;
            }

            Debug.Log($"[Block] {pad.label} ({pad.width:F0} x {pad.depth:F0} m): {story}\n" +
                      $"The units stand under \"{root.name}\", each one whole - move, turn or " +
                      "delete one, or unpack it to take a single building out.\nRun this again " +
                      "for another block, or any single dressing pass under Tools/City/Catalog " +
                      "to re-roll just that layer. Then Capture Blocks From Lot Pads to save it.");

            Selection.activeGameObject = root.gameObject;
        }

        /// <summary>How a roll ended. The two failures are worth telling apart: one is a
        /// catalog that was never built, the other a lot too small for anything in it.</summary>
        internal enum Result { Ok, NoUnits, NoFit }

        /// <summary>
        /// A whole block on a pad already decided, with no dialog in it: for the menu
        /// command above and for <see cref="BlockLotStock"/>, which rolls one for every lot
        /// in the city and captures each as it goes.
        ///
        /// <paramref name="cityLandmarks"/> is what has already been spent on the rest of
        /// the city, by bare name, and this roll adds its own to it - so a batch that
        /// passes the same set through every lot puts the fairground, the hotel and the
        /// police station in ONE block each and nowhere else. One block on its own from the
        /// menu passes nothing and is bounded only by what stands on its own pad.
        ///
        /// <paramref name="seed"/> is a hand-made block to stand FIRST - a block composed
        /// for a smaller pad, wanted in this bigger one the way the user asked for it: in
        /// a corner, with the rest of the pad randomised round it, never centred with bare
        /// court on every side. The seed goes down whatever the landmark set says (its own
        /// places are what a hand-made block IS), and then claims them like any unit. Null
        /// = an ordinary roll.
        /// </summary>
        internal static Result Roll(BlockPad pad, out Transform root, out string story,
                                    HashSet<string> cityLandmarks = null, GameObject seed = null)
        {
            root = null;
            story = null;
            cityLandmarks ??= new HashSet<string>();

            var units = LoadUnits();
            if (units.Count == 0)
                return Result.NoUnits;

            // The old dressing goes with the buildings it was planned around - and it goes
            // BEFORE the compose rather than being left to each pass's own reset, because
            // the yard is surveyed off what stands on the pad: last roll's cars and bins
            // would otherwise read as obstacles to build around. The pad's own hand-placed
            // content stays and is built around on purpose.
            ClearDressing(pad);
            root = pad.ResetAuto(AutoRoot);

            // A batch rolls a dozen blocks well inside one tick of the clock, and the tick
            // alone would hand two of them the same seed - hence the roll count with it.
            Random.InitState(System.Environment.TickCount + ++_rolls * 7919);
            var placed = Compose(pad, units, LoadCommerce(), root, cityLandmarks, seed, out var built);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            if (placed == 0)
            {
                // Nothing stood up, so nothing owns the root either - an empty "auto block"
                // left in the hierarchy would read as a block that came out invisible.
                Object.DestroyImmediate(root.gameObject);
                root = null;
                return Result.NoFit;
            }

            story = $"{built} Dressed: {Dress(pad)}.";
            return Result.Ok;
        }

        static int _rolls;

        /// <summary>
        /// The three dressing passes over the block just composed, in the one order they
        /// work in - and this order is not a preference, each pass is the next one's input:
        ///
        ///   * the parking first, because it wants a strip of clear yard deep enough for a
        ///     car and the scatter would have spent that yard on bins and benches;
        ///   * then the props, which read the bays and the cars as things standing in the
        ///     yard and lay their rows around them;
        ///   * the floor last, because it beds whatever it finds - grass under a planter,
        ///     asphalt under a parked car - and can only do that once they are standing.
        ///
        /// Every pass writes under its own auto root and destroys only that one, so any of
        /// them can still be re-rolled on its own from the menu afterwards.
        /// </summary>
        static string Dress(BlockPad pad)
        {
            var done = new List<string>();

            var parked = BlockParkingBay.Park(pad, out _);
            done.Add(parked > 0 ? $"{parked} parking pieces"
                                : "no room for parking");

            var props = BlockPropFiller.Fill(pad, out _);
            done.Add(props > 0 ? $"{props} props" : "no room for props");

            var floor = BlockFloorFiller.Lay(pad, out _);
            done.Add(floor < 0 ? "no floor - the packs' ground tiles could not be loaded"
                   : floor > 0 ? $"{floor} ground pieces"
                               : "no floor");

            return string.Join(", ", done);
        }

        // ----------------------------------------------------------------- the pad

        /// <summary>The other passes' output on this pad, destroyed. Each auto root is
        /// found by the very rule that writes it, so a loose "lot A1 auto floor" and a
        /// workbench child "auto floor" both go.</summary>
        static void ClearDressing(BlockPad pad)
        {
            foreach (var rootName in new[] { BlockPad.ParkingRoot, BlockPad.PropsRoot, BlockPad.FloorRoot })
                pad.ClearAuto(rootName);
        }

        // --------------------------------------------------------------- the units

        /// <summary>
        /// One authored thing this pass may stand on a pad: the prefab, the footprints of
        /// the BUILDINGS inside it (never one box round the lot of them - that box is
        /// mostly courtyard, and treating it as solid is what stops two units interlocking).
        /// </summary>
        sealed class Unit
        {
            internal GameObject prefab;
            internal Rect[] parts;      // per-building footprints, unit frame, unturned
            internal string[] partNames; // the child each footprint belongs to, same order
            internal string[] partPaths; // that child's own prefab, or "" when it has none
            internal Rect box;          // their union

            /// <summary>For a TRIMMED row: the children of the cluster prefab that this
            /// unit does without, deleted off the instance once it is stood. Null for a
            /// whole unit. See <see cref="Trims"/>.</summary>
            internal string[] drop;

            internal string Label => drop == null
                ? prefab.name
                : $"{prefab.name} less {drop.Length}";

            /// <summary>Every building this unit puts on the ground, by asset path, and the
            /// unit's own path with them. What the no-doubles rule is checked against: the
            /// members are why two units sharing the hotel cannot both stand here, the
            /// unit's own path is why one unit cannot stand here twice.</summary>
            internal string[] members;

            /// <summary>The members that are PLACES rather than terrace, by bare name. A
            /// city has one fairground, one hotel, one police station; the anonymous cluster
            /// rows are the fabric between them and may stand in any number of blocks. See
            /// <see cref="IsLandmark"/>.</summary>
            internal string[] landmarks;

            internal float Area => box.width * box.height;
        }

        /// <summary>
        /// Whether this building is one of a kind. The rule is
        /// <see cref="LivingCity.Generation.BlockFabric"/>'s,
        /// shared with the road demo so the two never disagree: the anonymous City_XX
        /// terrace and the commercial storefronts are fabric a city is mostly made of;
        /// everything else the packs name is a PLACE - the fairground, the hotel, the
        /// marina, the car yard, the police station - and two of those in one city reads
        /// as a mistake however far apart they stand.
        /// </summary>
        internal static bool IsLandmark(string nameOrPath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(nameOrPath);
            return !LivingCity.Generation.BlockFabric.IsFabric(name);
        }

        /// <summary>The fewest buildings a trimmed row keeps. Below this it is a building
        /// or two standing loose, and the user's word for that was blunt: the cluster
        /// bakes are joined oddly, so a building cut out of one shows a plain party wall
        /// on the side its neighbour used to be, and looks it. Three in a row still reads
        /// as the terrace it came from, with one cut end.</summary>
        const int MinRun = 3;

        /// <summary>How close two buildings of a cluster stand to count as neighbours in
        /// the row - what a trim may cut between.</summary>
        const float Touch = 0.75f;

        /// <summary>
        /// Everything authored that stands as a unit: the catalog's assembled rows, the
        /// same rows TRIMMED (<see cref="Trims"/>), and the palm block bakes.
        ///
        /// A row is told from a single building by structure rather than by name - every
        /// ordinary bake carries its combined mesh on its own root
        /// (SyntyKitExtractor.BakeGroup), while a cluster split into City_XX_A..K is a bare
        /// root holding those as children at the offsets PolygonCity authored.
        ///
        /// Single buildings are NOT candidates here - not the named ones (the diner, the
        /// gym, the mansion: the catalog bakes a demo group at yaw 0, so a single bake
        /// carries no answer to which way it faces, and a lot laid out of them comes out
        /// facing every way at once), and not the cluster slices City_06_J either. Those
        /// were candidates once, to fill a lot out; standing alone they show the party
        /// wall their neighbour covered, and the user said so. What fills a lot out now
        /// is the row itself with its end buildings cut off until it fits - the user's own
        /// description of how a block is made - so every unit is a run of the terrace
        /// still standing as it was authored. The one kind of single building that DOES
        /// stand is the storefront, and it comes from the other kit (<see
        /// cref="LoadCommerce"/>), which knows which way it faces.
        ///
        /// From the block bakes, only the auto-extracted palm candidates
        /// (<see cref="SyntyCityBlocks.PalmBlockPrefix"/>) - reassembled straight off the
        /// PalmCity demo's own streets, so they are authored in the sense that matters. The
        /// rest of that folder is blocks somebody composed FOR a lot, and building one of
        /// those into another lot is going in circles.
        /// </summary>
        static List<Unit> LoadUnits()
        {
            var units = new List<Unit>();
            foreach (var folder in new[] { SyntyBuildingCatalog.CatalogDir, SyntyCityBlocks.BlocksDir })
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!prefab)
                        continue;
                    if (prefab.GetComponent<MeshRenderer>())
                        continue;                   // a single building, not a unit
                    if (prefab.transform.childCount < 2)
                        continue;
                    bool palm = prefab.name.StartsWith(SyntyCityBlocks.PalmBlockPrefix);
                    if (folder == SyntyCityBlocks.BlocksDir && !palm)
                        continue;                   // a block already composed for a lot

                    var unit = Measure(guid, path, prefab);
                    if (unit == null)
                        continue;
                    units.Add(unit);
                    // a palm block is a place, not a terrace - it is never cut
                    if (!palm)
                        units.AddRange(Trims(unit));
                }
            }
            return units;
        }

        /// <summary>
        /// The storefronts (<see cref="LivingCity.Generation.BlockFabric.Commerce"/>) as
        /// units: the cafe, the burger joint, the restaurant, the stores. What the user
        /// asked for in the ground the rows leave over - "we have plenty of residential,
        /// the commercial is missing" - and the one kind of single building that can be
        /// stood alone with a straight face. These are the SyntyKitExtractor bakes, not
        /// the catalog's: the extractor turned each one so its front is on +Z, which is
        /// what lets <see cref="Yard.TryFit"/> stand it with the shopfront on the kerb.
        /// Small - 6 to 24 m across, 7 to 21 deep - so they take the frontage the rows
        /// leave rather than competing with them for the corners.
        /// </summary>
        static List<Unit> LoadCommerce()
        {
            var units = new List<Unit>();
            foreach (var name in LivingCity.Generation.BlockFabric.Commerce)
            {
                var path = $"{SyntyKitExtractor.BuildingsDir}/{name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab)
                    continue;
                var unit = Measure(AssetDatabase.AssetPathToGUID(path), path, prefab);
                if (unit != null)
                    units.Add(unit);
            }
            return units;
        }

        /// <summary>
        /// The row with its end buildings cut off, one more each time, down to
        /// <see cref="MinRun"/>: the units that fill a lot out. An END is a building with
        /// one neighbour in the row - the terrace is a chain, a C-shaped cluster a bent
        /// one, and a cut is only ever made there, so what is left is always one
        /// continuous run of the terrace as authored. Cut from the west end, from the
        /// east end, and from both in turn, so the same row yields three different runs
        /// of every length rather than one.
        ///
        /// A trimmed unit stands the WHOLE cluster prefab and deletes the cut buildings
        /// off the instance (<see cref="Stand"/>), so what stands is still the catalog's
        /// own City_XX_A..K at their authored offsets, and the capture writes them down
        /// one by one.
        /// </summary>
        static IEnumerable<Unit> Trims(Unit whole)
        {
            var n = whole.parts.Length;
            if (n <= MinRun)
                yield break;

            var adjacent = new List<int>[n];
            for (var i = 0; i < n; i++)
            {
                adjacent[i] = new List<int>();
                var grown = Rect.MinMaxRect(whole.parts[i].xMin - Touch, whole.parts[i].yMin - Touch,
                                            whole.parts[i].xMax + Touch, whole.parts[i].yMax + Touch);
                for (var j = 0; j < n; j++)
                    if (i != j && grown.Overlaps(whole.parts[j]))
                        adjacent[i].Add(j);
            }

            var seen = new HashSet<string>();
            foreach (var side in new[] { -1, 1, 0 })
            {
                var kept = new HashSet<int>(Enumerable.Range(0, n));
                var turn = side == 0 ? -1 : side;
                while (kept.Count > MinRun)
                {
                    // the ends of what is left: buildings with one neighbour still kept
                    var ends = kept.Where(i => adjacent[i].Count(j => kept.Contains(j)) == 1).ToList();
                    if (ends.Count == 0)
                        break;                      // a ring, or two rows: nothing to trim
                    // west-most or east-most end, by footprint centre
                    var cut = (turn < 0 ? ends.OrderBy(i => whole.parts[i].center.x)
                                        : ends.OrderByDescending(i => whole.parts[i].center.x))
                              .ThenBy(i => whole.parts[i].center.y).First();
                    kept.Remove(cut);
                    if (side == 0)
                        turn = -turn;

                    // one connected run only: a cut that split the row is not a trim
                    if (!Connected(kept, adjacent))
                        break;

                    var key = string.Join(",", kept.OrderBy(i => i));
                    if (!seen.Add(key))
                        continue;

                    var indices = kept.OrderBy(i => i).ToArray();
                    var parts = indices.Select(i => whole.parts[i]).ToArray();
                    var box = parts[0];
                    foreach (var part in parts)
                        box = Rect.MinMaxRect(Mathf.Min(box.xMin, part.xMin), Mathf.Min(box.yMin, part.yMin),
                                              Mathf.Max(box.xMax, part.xMax), Mathf.Max(box.yMax, part.yMax));
                    var members = indices.Select(i => whole.partPaths[i])
                                         .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                    if (members.Count == 0)
                        members.Add(AssetDatabase.GetAssetPath(whole.prefab) + "#" + key);
                    yield return new Unit
                    {
                        prefab = whole.prefab,
                        parts = parts,
                        partNames = indices.Select(i => whole.partNames[i]).ToArray(),
                        partPaths = indices.Select(i => whole.partPaths[i]).ToArray(),
                        box = box,
                        members = members.ToArray(),
                        landmarks = members.Where(IsLandmark)
                            .Select(System.IO.Path.GetFileNameWithoutExtension).ToArray(),
                        drop = Enumerable.Range(0, n).Where(i => !kept.Contains(i))
                            .Select(i => whole.partNames[i]).ToArray(),
                    };
                }
            }
        }

        static bool Connected(HashSet<int> kept, List<int>[] adjacent)
        {
            if (kept.Count == 0)
                return false;
            var reached = new HashSet<int> { kept.First() };
            var queue = new Queue<int>(reached);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                foreach (var j in adjacent[i])
                    if (kept.Contains(j) && reached.Add(j))
                        queue.Enqueue(j);
            }
            return reached.Count == kept.Count;
        }

        /// <summary>
        /// The unit measured by standing it in the scene - a prefab asset's renderer bounds
        /// are not the footprint the thing ends up with, and every other pass here measures
        /// the same way for the same reason. Each child that renders anything gives one
        /// footprint.
        ///
        /// Kept between rolls (running this again is the whole interaction), keyed by guid
        /// with the prefab file's write time as the guard, so a catalog rebuild measures
        /// afresh instead of answering for geometry that has changed.
        /// </summary>
        static Unit Measure(string guid, string path, GameObject prefab)
        {
            var stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            if (Measured.TryGetValue(guid, out var cached) && cached.stamp == stamp)
                return cached.unit;

            var probe = (GameObject)Object.Instantiate(prefab);
            probe.transform.position = Vector3.zero;
            probe.transform.rotation = Quaternion.identity;

            // Footprints off the probe, names and prefabs off the ASSET, child by child:
            // the probe is a plain clone and has lost every prefab link, while the asset's
            // children are still the nested instances the catalog assembled - which is the
            // whole answer to "which buildings is this made of". A child that resolves to
            // nothing keeps an empty path rather than a guess.
            var parts = new List<Rect>();
            var names = new List<string>();
            var paths = new List<string>();
            // A single baked building (a storefront out of the kit) is one mesh on its
            // own root with nothing under it but decals: the whole thing is one part,
            // and the part IS the prefab.
            var single = probe.GetComponent<MeshRenderer>() != null;
            if (single)
            {
                var whole = BlockLotCapture.RendererBounds(probe);
                if (whole.HasValue)
                {
                    var b = whole.Value;
                    parts.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
                    names.Add(prefab.name);
                    paths.Add(path);
                }
            }
            for (var c = 0; !single && c < probe.transform.childCount && c < prefab.transform.childCount; c++)
            {
                var child = probe.transform.GetChild(c);
                var bounds = BlockLotCapture.RendererBounds(child.gameObject);
                if (!bounds.HasValue)
                    continue;
                var b = bounds.Value;
                parts.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
                names.Add(child.name);
                paths.Add(BlockLotCapture.SourcePathOf(prefab.transform.GetChild(c)) ?? "");
            }
            Object.DestroyImmediate(probe);

            // A composed block (a seed) is measured against ITS PAD, not its renderer
            // box: what shows past the pad is dressing over the kerb - a canopy, a lamp
            // arm, a bay marking - which the sidewalk has room for, and counting it made
            // an 85 x 70 block "not fit" a 100 x 70 pad by a few centimetres of palm.
            var tag = prefab.GetComponent<LivingCity.Generation.BlockLotTag>();
            if (tag != null && tag.lotWidth > 0f && tag.lotDepth > 0f)
            {
                var pad = Rect.MinMaxRect(-tag.lotWidth * 0.5f, -tag.lotDepth * 0.5f,
                                          tag.lotWidth * 0.5f, tag.lotDepth * 0.5f);
                for (var p = parts.Count - 1; p >= 0; p--)
                {
                    var clipped = Rect.MinMaxRect(Mathf.Max(parts[p].xMin, pad.xMin), Mathf.Max(parts[p].yMin, pad.yMin),
                                                  Mathf.Min(parts[p].xMax, pad.xMax), Mathf.Min(parts[p].yMax, pad.yMax));
                    if (clipped.width <= 0f || clipped.height <= 0f)
                    {
                        parts.RemoveAt(p);
                        names.RemoveAt(p);
                        paths.RemoveAt(p);
                    }
                    else
                        parts[p] = clipped;
                }
            }

            // The unit's own path is in the members regardless, so it can never double
            // itself even when nothing under it can be named.
            var members = new List<string> { path };
            foreach (var source in paths)
                if (!string.IsNullOrEmpty(source) && !members.Contains(source))
                    members.Add(source);

            Unit unit = null;
            if (parts.Count > 0)
            {
                var box = parts[0];
                foreach (var part in parts)
                    box = Rect.MinMaxRect(Mathf.Min(box.xMin, part.xMin), Mathf.Min(box.yMin, part.yMin),
                                          Mathf.Max(box.xMax, part.xMax), Mathf.Max(box.yMax, part.yMax));
                if (box.width >= 1f && box.height >= 1f)
                    unit = new Unit
                    {
                        prefab = prefab,
                        parts = parts.ToArray(),
                        partNames = names.ToArray(),
                        partPaths = paths.ToArray(),
                        box = box,
                        members = members.ToArray(),
                        landmarks = members.Where(IsLandmark)
                            .Select(System.IO.Path.GetFileNameWithoutExtension).ToArray(),
                    };
            }

            Measured[guid] = (stamp, unit);
            return unit;
        }

        static readonly Dictionary<string, (long stamp, Unit unit)> Measured = new();

        // --------------------------------------------------------------- the shapes

        /// <summary>
        /// One unit as the search actually uses it: turned, possibly mirrored, and reduced
        /// to the cells its buildings cover plus the ring of cells just outside them. The
        /// covered cells are what may not collide; the ring is what CONTACT is counted
        /// over, and counting it per cell edge makes it a length rather than a tally.
        /// </summary>
        sealed class Shape
        {
            internal Unit unit;
            internal float turn;
            internal bool mirror;
            internal int nx, nz;                  // extent in cells
            internal Vector2Int[] covered;
            internal Vector2Int[] rim;            // one entry per cell edge facing outward
            internal Vector2 min;                 // world offset of cell (0,0) from the unit origin
        }

        /// <summary>Every way a unit may be stood: four quarter turns, each of them plain
        /// and mirrored. Mirroring is the user's own move - residentialblock2 is one cluster
        /// beside its own mirror image.</summary>
        static IEnumerable<Shape> Shapes(Unit unit)
        {
            for (var q = 0; q < 4; q++)
                foreach (var mirror in new[] { false, true })
                    yield return Build(unit, q * 90f, mirror);
        }

        static Shape Build(Unit unit, float turn, bool mirror)
        {
            var parts = unit.parts.Select(p => Transform(p, turn, mirror)).ToArray();
            var box = parts[0];
            foreach (var part in parts)
                box = Rect.MinMaxRect(Mathf.Min(box.xMin, part.xMin), Mathf.Min(box.yMin, part.yMin),
                                      Mathf.Max(box.xMax, part.xMax), Mathf.Max(box.yMax, part.yMax));

            var nx = Mathf.Max(1, Mathf.CeilToInt(box.width / Cell));
            var nz = Mathf.Max(1, Mathf.CeilToInt(box.height / Cell));

            var covered = new HashSet<Vector2Int>();
            foreach (var part in parts)
            {
                var i0 = Mathf.Clamp(Mathf.FloorToInt((part.xMin - box.xMin) / Cell), 0, nx - 1);
                var i1 = Mathf.Clamp(Mathf.CeilToInt((part.xMax - box.xMin) / Cell) - 1, 0, nx - 1);
                var j0 = Mathf.Clamp(Mathf.FloorToInt((part.yMin - box.yMin) / Cell), 0, nz - 1);
                var j1 = Mathf.Clamp(Mathf.CeilToInt((part.yMax - box.yMin) / Cell) - 1, 0, nz - 1);
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                        covered.Add(new Vector2Int(i, j));
            }

            var rim = new List<Vector2Int>();
            foreach (var cell in covered)
                foreach (var step in Neighbours)
                {
                    var side = cell + step;
                    if (!covered.Contains(side))
                        rim.Add(side);
                }

            return new Shape
            {
                unit = unit,
                turn = turn,
                mirror = mirror,
                nx = nx,
                nz = nz,
                covered = covered.ToArray(),
                rim = rim.ToArray(),
                min = new Vector2(box.xMin, box.yMin),
            };
        }

        static readonly Vector2Int[] Neighbours =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        };

        /// <summary>A footprint mirrored about the unit's own X, then turned. The order is
        /// the one Unity applies: the scale is the local flip, the rotation is applied over
        /// it, so a mirrored-and-turned unit lands where the scene puts it.</summary>
        static Rect Transform(Rect rect, float turn, bool mirror)
        {
            var xMin = mirror ? -rect.xMax : rect.xMin;
            var xMax = mirror ? -rect.xMin : rect.xMax;

            return Mathf.RoundToInt(turn) switch
            {
                90 => Rect.MinMaxRect(rect.yMin, -xMax, rect.yMax, -xMin),
                180 => Rect.MinMaxRect(-xMax, -rect.yMax, -xMin, -rect.yMin),
                270 => Rect.MinMaxRect(-rect.yMax, xMin, -rect.yMin, xMax),
                _ => Rect.MinMaxRect(xMin, rect.yMin, xMax, rect.yMax),
            };
        }

        // ------------------------------------------------------------- the compose

        /// <summary>
        /// The block: units drawn one at a time, each stood where it touches the most of
        /// what is already there, until the lot is as built as the user's own blocks are.
        /// Then the storefronts (<paramref name="commerce"/>), one to three of them, on
        /// the street frontage the rows left - each with its front on the kerb.
        /// </summary>
        static int Compose(BlockPad pad, List<Unit> units, List<Unit> commerce, Transform root,
                           HashSet<string> cityLandmarks, GameObject seed, out string story)
        {
            var content = pad.Contents(withAutoProps: true);
            var yard = new Yard(pad, content);
            var target = Random.Range(MinBuilt, MaxBuilt);
            var told = new List<string>();

            // What already stands here, by asset: the buildings the user dragged on by
            // hand. They are obstacles to the packing and named places to the draw, so a
            // hand-placed fairground rules PalmBlock_07 out of the lot rather than being
            // stood beside a second one.
            var standing = new HashSet<string>(
                content.Where(c => c.building && !string.IsNullOrEmpty(c.path)).Select(c => c.path));

            // The seed first, on the empty pad, where the most contact is a corner. It
            // is a whole block already, so the built target is re-aimed at the ground it
            // leaves: the rest is filled to the same share as an ordinary lot would be,
            // instead of the seed alone counting as a lot three-quarters built and the
            // strip beside it staying empty - which is the very thing this is for.
            if (seed != null)
            {
                var path = AssetDatabase.GetAssetPath(seed);
                var unit = Measure(AssetDatabase.AssetPathToGUID(path), path, seed);
                if (unit != null && yard.TryFit(unit, out var shape, out var cell))
                {
                    Stand(unit, shape, cell, yard, root);
                    yard.Mark(shape, cell);
                    standing.UnionWith(unit.members);
                    cityLandmarks.UnionWith(unit.landmarks);
                    target = yard.Built + (1f - yard.Built) * target;
                    told.Add($"{unit.prefab.name} seeded in a corner" +
                             (shape.turn > 0f ? $" turned {shape.turn:F0}" : "") +
                             (shape.mirror ? " mirrored" : ""));
                }
                else
                    Debug.LogWarning($"[Block] {seed.name} does not fit {pad.label} as a seed - " +
                                     "the lot is rolled without it.");
            }

            // Units already found not to fit. A unit that fits nowhere now will fit nowhere
            // later either - the yard only fills up from here - so it leaves the draw
            // instead of spending the ten tries on the one thing too big for the gap left.
            var refused = new HashSet<Unit>();

            for (var n = 0; n < MaxUnits && yard.Built < target; n++)
            {
                var laid = false;
                while (!laid)
                {
                    // Drawn until something fits or there is nothing left to draw - NOT a
                    // fixed number of tries. Every unit that does not fit leaves the pool,
                    // so this ends; and it has to run the pool dry, because the rows are
                    // big and a lot with one corner left is refused by nine of them before
                    // it meets the short run that fills it. Ten tries and out is what left
                    // lots a third built with a car park where the block should be.
                    var pool = units.Where(u => !refused.Contains(u) &&
                                                !u.members.Any(standing.Contains) &&
                                                !u.landmarks.Any(cityLandmarks.Contains)).ToList();
                    if (pool.Count == 0)
                        break;

                    var unit = Draw(pool, Mathf.Max(0f, target - yard.Built) * pad.width * pad.depth);
                    if (!yard.TryFit(unit, out var shape, out var cell))
                    {
                        refused.Add(unit);
                        continue;
                    }

                    Stand(unit, shape, cell, yard, root);
                    yard.Mark(shape, cell);
                    standing.UnionWith(unit.members);
                    // The places it brought are spent for the whole city, not just this
                    // lot: one fairground, one hotel, one police station.
                    cityLandmarks.UnionWith(unit.landmarks);
                    told.Add(unit.Label +
                             (shape.turn > 0f ? $" turned {shape.turn:F0}" : "") +
                             (shape.mirror ? " mirrored" : ""));
                    laid = true;
                }
                if (!laid)
                    break;
            }

            // The commerce, in what the rows left. Drawn evenly rather than by area -
            // the kiosk is as welcome as the restaurant - and stood ONLY with its front
            // flush on a kerb line: a cafe with its back to the street, or facing the
            // yard behind the terrace, is what the user meant by "the prefabs are joined
            // oddly". A storefront that fits nowhere on the frontage is left out; a lot
            // whose rows took every kerb simply has no shops, and the story says so.
            // Once per lot per storefront (the members check), any number per city.
            var rows = told.Count;
            var shops = new List<string>();
            if (rows > 0 && commerce.Count > 0)
            {
                var wanted = Random.Range(1, MaxCommerce + 1);
                var open = commerce.Where(u => !u.members.Any(standing.Contains)).ToList();
                while (shops.Count < wanted && open.Count > 0 && yard.Built < CommerceCeiling)
                {
                    var unit = open[Random.Range(0, open.Count)];
                    open.Remove(unit);
                    if (!yard.TryFit(unit, out var shape, out var cell, street: true))
                        continue;
                    Stand(unit, shape, cell, yard, root);
                    yard.Mark(shape, cell);
                    standing.UnionWith(unit.members);
                    shops.Add(unit.Label +
                              (shape.turn > 0f ? $" facing {Facing(shape.turn)}" : " facing north"));
                }
                told.Add(shops.Count == 0
                    ? "no frontage left for a storefront"
                    : $"{shops.Count} storefront(s): " + string.Join(", ", shops));
            }

            story = rows == 0
                ? "nothing fitted."
                : $"{rows} unit(s) and {shops.Count} storefront(s), " +
                  $"{100f * yard.Built:F0}% built (aimed at {100f * target:F0}%): " +
                  string.Join(", ", told) + ".";
            return rows + shops.Count;
        }

        /// <summary>The compass side a unit turned this far has its front on: the kit
        /// bakes face +Z unturned, and the pad's +Z is the catalog's north.</summary>
        static string Facing(float turn) => Mathf.RoundToInt(turn) switch
        {
            90 => "east",
            180 => "south",
            270 => "west",
            _ => "north",
        };

        /// <summary>
        /// Rolls one unit, by how near its footprint is to the ground still wanted. Weighing
        /// by the SQUARE of the shortfall it covers keeps the big rows in the running while
        /// the lot is empty and hands the last gap to something that fits it, instead of
        /// always drawing the biggest thing in the catalog.
        /// </summary>
        static Unit Draw(List<Unit> pool, float wanted)
        {
            var total = 0.0;
            var weights = new double[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                var over = Mathf.Max(1f, pool[i].Area / Mathf.Max(1f, wanted));
                weights[i] = pool[i].Area / (over * over);
                total += weights[i];
            }

            var roll = Random.value * total;
            for (var i = 0; i < pool.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0.0)
                    return pool[i];
            }
            return pool[pool.Count - 1];
        }

        /// <summary>
        /// Stands the unit at the cell the search chose, measured and aligned afterwards -
        /// what the mask promised is checked against what the prefab really is. Only the
        /// two ground axes are touched: the unit keeps the height its pack authored, so
        /// whatever it carries below zero stays buried (see the rules at the top).
        /// </summary>
        static void Stand(Unit unit, Shape shape, Vector2Int cell, Yard yard, Transform root)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(unit.prefab);
            instance.transform.SetParent(root, worldPositionStays: false);
            instance.transform.SetPositionAndRotation(Vector3.zero,
                                                      Quaternion.Euler(0f, shape.turn, 0f));
            if (shape.mirror)
                instance.transform.localScale = new Vector3(-1f, 1f, 1f);

            // A trimmed row: the cluster stands whole and its cut buildings come off.
            // The outer link has to go first - a prefab instance will not lose a child -
            // and only that one: the buildings stay the catalog's own nested instances,
            // which is what the capture reads them back as. Cut BEFORE measuring, or the
            // row would be placed by the footprint of buildings that are not there.
            if (unit.drop != null && unit.drop.Length > 0)
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot,
                                                   InteractionMode.AutomatedAction);
                var gone = new HashSet<string>(unit.drop);
                for (var c = instance.transform.childCount - 1; c >= 0; c--)
                {
                    var child = instance.transform.GetChild(c);
                    if (gone.Contains(child.name))
                        Object.DestroyImmediate(child.gameObject);
                }
                instance.name = unit.Label;
            }

            var target = yard.World(cell);
            var measured = BlockLotCapture.RendererBounds(instance);
            if (measured.HasValue)
            {
                var b = measured.Value;
                instance.transform.position += new Vector3(target.x - b.min.x, 0f, target.y - b.min.z);
            }
        }

        // ---------------------------------------------------------------- the yard

        /// <summary>
        /// The pad in cells: which ones a building already stands on, and the two questions
        /// the packing asks of it - where does this shape fit, and which of those places
        /// touches the most. Everything already on the pad by hand is marked at the start,
        /// so a lot with a landmark on it gets its units fitted around the landmark.
        /// </summary>
        sealed class Yard
        {
            readonly bool[,] _blocked;
            readonly float _minX, _minZ;
            readonly int _nx, _nz;
            int _taken;

            internal Yard(BlockPad pad, List<BlockPad.Item> content)
            {
                _nx = Mathf.Max(1, Mathf.FloorToInt(pad.width / Cell));
                _nz = Mathf.Max(1, Mathf.FloorToInt(pad.depth / Cell));
                _minX = pad.centre.x - pad.width * 0.5f;
                _minZ = pad.centre.z - pad.depth * 0.5f;
                _blocked = new bool[_nx, _nz];

                foreach (var item in content)
                    Block(item.Footprint);
            }

            /// <summary>How much of the lot is built on, 0..1.</summary>
            internal float Built => (float)_taken / (_nx * _nz);

            /// <summary>The world corner of a cell - where the shape's own (0,0) goes.</summary>
            internal Vector2 World(Vector2Int cell) =>
                new(_minX + cell.x * Cell, _minZ + cell.y * Cell);

            void Block(Rect rect)
            {
                var i0 = Mathf.Clamp(Mathf.FloorToInt((rect.xMin - _minX) / Cell), 0, _nx - 1);
                var i1 = Mathf.Clamp(Mathf.CeilToInt((rect.xMax - _minX) / Cell) - 1, 0, _nx - 1);
                var j0 = Mathf.Clamp(Mathf.FloorToInt((rect.yMin - _minZ) / Cell), 0, _nz - 1);
                var j1 = Mathf.Clamp(Mathf.CeilToInt((rect.yMax - _minZ) / Cell) - 1, 0, _nz - 1);
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                        if (!_blocked[i, j])
                        {
                            _blocked[i, j] = true;
                            _taken++;
                        }
            }

            internal void Mark(Shape shape, Vector2Int at)
            {
                foreach (var cell in shape.covered)
                {
                    var i = at.x + cell.x;
                    var j = at.y + cell.y;
                    if (i < 0 || j < 0 || i >= _nx || j >= _nz || _blocked[i, j])
                        continue;
                    _blocked[i, j] = true;
                    _taken++;
                }
            }

            /// <summary>
            /// Where this unit goes: every quarter turn, mirrored and not, over every
            /// candidate cell, scored by CONTACT - how much of the shape's own edge lands
            /// against the pad's edge or against something already standing. The best score
            /// wins, ties are drawn between, and a shape that collides anywhere or reaches
            /// over the kerb line is simply not a candidate.
            ///
            /// <paramref name="street"/> is for a storefront: a unit whose front is on +Z
            /// unturned, to be stood with that front FLUSH ON A KERB LINE and nowhere else
            /// - the north edge unturned, the east edge turned 90, and so on - and never
            /// mirrored, since a mirrored shopfront reads its own sign backwards. Among
            /// the flush positions contact still decides, so the shop takes the corner or
            /// the gap beside a row end before it takes open frontage.
            /// </summary>
            internal bool TryFit(Unit unit, out Shape shape, out Vector2Int at, bool street = false)
            {
                shape = null;
                at = default;

                var best = -1;
                var seen = 0;
                foreach (var candidate in Shapes(unit))
                {
                    if (candidate.nx > _nx || candidate.nz > _nz)
                        continue;                   // it would hang over the kerb anywhere
                    if (street && candidate.mirror)
                        continue;

                    foreach (var i in Stops(_nx - candidate.nx))
                        foreach (var j in Stops(_nz - candidate.nz))
                        {
                            if (street && !FrontOnKerb(candidate, i, j))
                                continue;
                            if (Hits(candidate, i, j))
                                continue;
                            var score = Contact(candidate, i, j);
                            if (score > best)
                            {
                                best = score;
                                seen = 1;
                                shape = candidate;
                                at = new Vector2Int(i, j);
                            }
                            else if (score == best && Random.Range(0, ++seen) == 0)
                            {
                                shape = candidate;  // reservoir draw between equal places
                                at = new Vector2Int(i, j);
                            }
                        }
                }
                return best >= 0;
            }

            /// <summary>The candidate positions along one axis: the flush ones at both ends
            /// always, and every Step cells between them.</summary>
            static IEnumerable<int> Stops(int span)
            {
                if (span <= 0)
                {
                    yield return 0;
                    yield break;
                }
                for (var v = 0; v < span; v += Step)
                    yield return v;
                yield return span;
            }

            /// <summary>Whether a shape stood at this cell has its front row on the pad's
            /// edge. The front is +Z unturned; <see cref="Transform"/> turns +Z onto +X at
            /// 90, -Z at 180 and -X at 270, exactly as Unity's yaw does to the instance.</summary>
            bool FrontOnKerb(Shape shape, int i0, int j0) => Mathf.RoundToInt(shape.turn) switch
            {
                90 => i0 + shape.nx == _nx,
                180 => j0 == 0,
                270 => i0 == 0,
                _ => j0 + shape.nz == _nz,
            };

            bool Hits(Shape shape, int i0, int j0)
            {
                foreach (var cell in shape.covered)
                    if (_blocked[i0 + cell.x, j0 + cell.y])
                        return true;
                return false;
            }

            int Contact(Shape shape, int i0, int j0)
            {
                var touch = 0;
                foreach (var cell in shape.rim)
                {
                    var i = i0 + cell.x;
                    var j = j0 + cell.y;
                    if (i < 0 || j < 0 || i >= _nx || j >= _nz || _blocked[i, j])
                        touch++;
                }
                return touch;
            }
        }
    }
}
