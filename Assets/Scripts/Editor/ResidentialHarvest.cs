using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The six residential buildings, taken out of the harvest scene as units the block
    /// generator can deal.
    ///
    /// THE NAME IS THE CONTRACT. Every piece of a building carries the building's name -
    /// "residential1" ... "residential6" - put there by hand in
    /// <c>Assets/Scenes/CoreHarvest.unity</c>. A seventh building named "residential7"
    /// tomorrow needs no code: it is swept, measured and baked like the rest. Nothing else
    /// in the scene is touched, so the trays and the review rows carry on as they were.
    ///
    /// Two more families answer to the same contract (2026-08-27, the user's park1, park2
    /// and pizzapub): "parkN" is a PARK - a fenced square of the pack's grass and paths
    /// with its benches and trees, measured off its ground tiles because it has no walls -
    /// and any other name shared by a group of pieces that has a shopfront in it is a
    /// STOREFRONT, kept under the name it was given. A storefront is not dealt as a house:
    /// it stands in a gap in the row, and gets tables in front.
    ///
    /// What comes out is two things, and they are written together so they can never
    /// disagree: a prefab per unit in <see cref="OutDir"/>, and the MEASURED table in
    /// <see cref="TablePath"/> that the recipe deals from - footprint, which sides are
    /// street faces, which cells are sunken, what hangs over the pavement. Nothing in that
    /// table is typed by hand.
    /// </summary>
    /// <remarks>
    /// The module convention this rests on was measured, not assumed (2026-08-26, every
    /// <c>SM_Bld_Apartment_*</c> and <c>SM_Bld_Shop_*</c> prefab stood at the origin and
    /// read with <see cref="RoadDemo.FacadeFinder"/>):
    ///
    /// * a module's pivot is the NE corner of the cell it fills - at yaw 0 it covers
    ///   x -5..0, z -5..0, so its cell centre is the pivot plus (-2.5, -2.5) turned;
    /// * the facade is on +Z, i.e. the pivot side. The only pieces that read otherwise are
    ///   <c>Apartment_Corner_02</c> and the awnings, which are inner corners and covers,
    ///   not fronts - so a face is read off the DOORS, SHOPS and STOOPS on a side, never
    ///   off a single module's own idea of front;
    /// * <c>Apartment_Stairs_01/02</c> reach a whole cell (5 m) past the wall on the facade
    ///   side and drop to -1.5 m: the stoop and the sunken forecourt of a brownstone. That
    ///   cell belongs to the unit - it is why a brownstone's wall stands a cell back from
    ///   the kerb while its railings stand on it.
    /// </remarks>
    public static class ResidentialHarvest
    {
        public const string OutDir = "Assets/Prefabs/Residential";
        public const string TablePath = "Assets/RoadDemo/ResidentialUnits.cs";

        /// <summary>The scenes the harvest reads, swept together. The houses, the parks and
        /// the pizza pubs were laid in the harvest scene; the user laid the amenities - the
        /// basketball court, the skatepark, the car yard, the gym, the two diners and the
        /// three shops - in the Palm City demo (2026-08-28), and both are read at once
        /// because the table is written whole: a bake that saw one scene would drop
        /// everything in the other.</summary>
        public static readonly string[] Sources =
        {
            "Assets/Scenes/CoreHarvest.unity",
            "Assets/Scenes/PalmCityDemo.unity",
        };

        /// <summary>The groups the user asked for by name out of the Palm City demo
        /// (2026-08-28). The demo is full of groups that are not ours - its own fairground
        /// and its beach toilets are named the same way - so what comes out of it is asked
        /// for, never swept up. A tenth amenity tomorrow is one line here.</summary>
        static readonly string[] Amenities =
        {
            "kosarkaskiteren", "skatepark", "caryard", "gym",
            "dinner", "dinner2", "radnja1", "radnja2", "radnja3",
        };

        /// <summary>The city module. Same 5 m everything else in the core is laid on.</summary>
        const float Cell = 5f;

        /// <summary>Below this a cell is a pit and gets no floor slab - the same line
        /// <c>CorePavement.Underground</c> draws, so the pavement and the table agree.</summary>
        const float Sunk = -0.6f;

        /// <summary>How far a piece's yaw may sit from a right angle and still be counted
        /// as square (and squared up in the bake). The demo's own pieces come in at 89.999
        /// and 267.999; anything further out was turned on purpose and is left alone.</summary>
        const float Square = 0.5f;

        static readonly Regex Named = new Regex(@"^residential(\d+)$", RegexOptions.IgnoreCase);
        static readonly Regex ParkNamed = new Regex(@"^park(\d+)$", RegexOptions.IgnoreCase);

        /// <summary>Which contract a named group answers to.</summary>
        enum Family { House, Park, Storefront, Amenity }

        // ------------------------------------------------------------------ what a unit is

        public enum Kind
        {
            /// <summary>One street face: a mid-block building.</summary>
            Row,
            /// <summary>Two faces meeting at a corner.</summary>
            Corner,
            /// <summary>Two opposite faces - a building that goes right through the block
            /// and has no back at all.</summary>
            Through,
            /// <summary>Three or four faces: it stands free.</summary>
            Island,
            /// <summary>A fenced square of grass: no faces, no doors, its own ground.</summary>
            Park,
            /// <summary>A complete outdoor venue: it gets a backing floor and a clear band
            /// from buildings and the block's pavement when the residential recipe stands it.</summary>
            Amenity,
            /// <summary>A shop with living over it, for a gap in the row.</summary>
            Storefront,
        }

        /// <summary>South, east, north, west - the order every per-side array is in.</summary>
        static readonly Vector2Int[] Out =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0),
        };
        static readonly string[] SideName = { "S", "E", "N", "W" };

        public sealed class Unit
        {
            public string Name;              // residential-01
            public string Source;            // residential1, as it is named in the scene
            public int CW, CD;               // the footprint in cells
            public bool[,] Wall;             // cells the building itself stands on
            public bool[,] Yard;             // cells the unit keeps in front: stoop, pit, garden
            public bool[,] Pit;              // cells that drop below Sunk - no floor under these
            public bool[] Face = new bool[4];
            public int[] Doors = new int[4], Shops = new int[4], Stoops = new int[4];
            public float[] Over = new float[4];   // what hangs out over the pavement, metres
            /// <summary>How many cells of this side's outer line the unit stands on.</summary>
            public int[] Front = new int[4];
            public int Trees, Pieces;
            /// <summary>Chairs, tables, umbrellas and benches the unit brings with it. A
            /// diner that arrives with its own terrace is not given a second one.</summary>
            public int Seats;
            /// <summary>Cells its walls stand on, against <see cref="Cells"/>: what tells a
            /// building from a lot.</summary>
            public int Built;
            /// <summary>The pieces themselves, in the scene they were found in - the bake
            /// copies these rather than looking them up a second time.</summary>
            public List<Transform> Parts;
            public float MaxH;
            /// <summary>The lowest the unit's own walls and stoops go: 0 for a house on
            /// the ground, -1.5 m for the brownstone whose whole footprint is sunk.</summary>
            public float Floor;
            public Kind Klass;
            public Vector2 Drift;            // how far off the 5 m raster it stood, and was moved back
            public Vector3 Pivot;            // where its SW corner stood in the scene

            public int Cells
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < CW; i++)
                        for (int j = 0; j < CD; j++)
                            if (Wall[i, j] || Yard[i, j]) n++;
                    return n;
                }
            }
        }

        // ------------------------------------------------------------------ the menu

        [MenuItem("Tools/City/Residential/Bake Named Buildings", priority = 40)]
        public static void BakeMenu()
        {
            int wrote = Bake(out var units, out string report);
            Debug.Log(report);
            EditorUtility.DisplayDialog("Harvest Residential",
                units.Count == 0
                    ? "Nothing in " + string.Join(" or ", Sources) + " is named residential1, " +
                      "residential2, ... (nor park1, ...)\n\n" +
                      "Name every piece of a building after the building and try again."
                    : $"{units.Count} unit(s) measured, {wrote} prefab(s) written to {OutDir},\n" +
                      $"table written to {TablePath}.\n\nThe measurements are in the console.",
                "OK");
        }

        [MenuItem("Tools/City/Residential/Sanitise Baked Colliders", priority = 43)]
        public static void SanitiseBakedCollidersMenu()
        {
            int changed = 0;
            foreach (var unit in RoadDemo.ResidentialUnits.All)
            {
                if (unit == null) continue;
                string path = $"{OutDir}/{unit.Name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    PreparePhysics(root, unit.CW * Cell, unit.CD * Cell,
                                   unit.Floor, unit.MaxH);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ResidentialHarvest] {changed} baked units now use one positive " +
                      "footprint collider; nested Synty colliders are disabled.");
        }

        /// <summary>The same work with no dialog, for a pipeline command - a modal dialog
        /// from a command deadlocks the editor.</summary>
        public static int Bake(out List<Unit> units, out string report)
        {
            var opened = new List<Scene>();
            var scenes = new List<Scene>();
            foreach (string path in Sources)
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened.Add(scene);
                }
                scenes.Add(scene);
            }

            try
            {
                units = Measure(scenes);
                int wrote = 0;
                foreach (var unit in units) if (Write(unit)) wrote++;
                if (units.Count > 0)
                {
                    WriteTable(units);
                    AssetDatabase.Refresh();
                }
                report = Report(units, wrote);
                return wrote;
            }
            finally
            {
                // only what this bake opened is closed again: a scene the user was working
                // in stays open, and nothing it holds is saved from here
                foreach (var scene in opened)
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ------------------------------------------------------------------ the measure

        /// <summary>Every named building in one scene, measured where it stands.</summary>
        public static List<Unit> Measure(Scene scene) => Measure(new[] { scene });

        /// <summary>Every named building in these scenes, measured where it stands. A name
        /// is a name wherever it was typed, so a group split across two scenes would be one
        /// unit - which is why the amenity roster names what comes out of the demo.</summary>
        public static List<Unit> Measure(IList<Scene> scenes)
        {
            // every name that more than one piece carries, and the pieces that carry it -
            // the pack's own names (SM_...) and the tray labels are not a contract
            var named = new Dictionary<string, List<Transform>>();
            foreach (var scene in scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith("SM_") || t.name.Contains(" ")) continue;
                        if (!named.TryGetValue(t.name, out var list)) named[t.name] = list = new List<Transform>();
                        list.Add(t);
                    }
            }

            var houses = new SortedDictionary<int, List<Transform>>();
            var parks = new SortedDictionary<int, List<Transform>>();
            var fronts = new SortedDictionary<string, List<Transform>>(System.StringComparer.Ordinal);
            var amenities = new SortedDictionary<string, List<Transform>>(System.StringComparer.Ordinal);
            foreach (var pair in named)
            {
                var m = Named.Match(pair.Key);
                if (m.Success) { houses[int.Parse(m.Groups[1].Value)] = pair.Value; continue; }
                m = ParkNamed.Match(pair.Key);
                if (m.Success) { parks[int.Parse(m.Groups[1].Value)] = pair.Value; continue; }
                if (pair.Value.Count < 2) continue;
                // an amenity the user asked for by name: a court, a yard or a shop out of
                // the demo, which is neither a house nor a park and carries no shopfront
                // module of the city pack
                if (System.Array.IndexOf(Amenities, pair.Key.ToLowerInvariant()) >= 0)
                {
                    amenities[pair.Key.ToLowerInvariant()] = pair.Value;
                    continue;
                }
                // a storefront: a group with a shopfront module in it. One piece with a
                // fancy name is not a group, and a group with no shop is not a storefront
                if (!pair.Value.Any(t => (Source(t) ?? "").StartsWith("SM_Bld_Shop"))) continue;
                fronts[pair.Key.ToLowerInvariant()] = pair.Value;
            }

            var units = new List<Unit>();
            foreach (var pair in houses)
            {
                var unit = Measure($"residential-{pair.Key:00}", $"residential{pair.Key}", pair.Value, Family.House);
                if (unit != null) units.Add(unit);
            }
            foreach (var pair in parks)
            {
                var unit = Measure($"park-{pair.Key:00}", $"park{pair.Key}", pair.Value, Family.Park);
                if (unit != null) units.Add(unit);
            }
            foreach (var pair in fronts)
            {
                var unit = Measure(pair.Key, pair.Value[0].name, pair.Value, Family.Storefront);
                if (unit != null) units.Add(unit);
            }
            foreach (var pair in amenities)
            {
                var unit = Measure(pair.Key, pair.Value[0].name, pair.Value, Family.Amenity);
                if (unit != null) units.Add(unit);
            }
            return units;
        }

        static Unit Measure(string name, string source, List<Transform> pieces, Family family)
        {
            var read = pieces.Select(Read).ToList();
            // a house is read off its walls and stoops; a park has no walls, and is read
            // off the ground it brings - its grass and its paths. An amenity is read off
            // BOTH: a car yard is a slab of tarmac with a hut on it, and a diner is a
            // building with its deck, and neither is the whole of what stands there
            var shell = family switch
            {
                Family.Park => read.Where(p => p.Kind == PieceKind.Ground).ToList(),
                // a lot is the WHOLE of what the user arranged - its tarmac, its fence, its
                // hut and the tables in front of it: the ground it takes is the ground the
                // block has to keep clear for it
                Family.Amenity => read,
                _ => read.Where(p => p.Kind == PieceKind.Shell || p.Kind == PieceKind.Stoop).ToList(),
            };
            if (shell.Count == 0)
            {
                Debug.LogWarning($"{name}: nothing in it is a " +
                                 (family == Family.Park ? "ground tile" : "building module") + " - not measured");
                return null;
            }

            // where the module grid this building was laid on actually sits. The demo did
            // not lay every building on our raster, so the drift is measured, taken out of
            // both the reading and the bake, and reported rather than hidden.
            //
            // An amenity was never laid on a module grid at all - it is a lot the user
            // arranged out of two-metre panels, ramps and tarmac - so its raster is put
            // where its own south-west corner is, and its footprint reads from there.
            var drift = family == Family.Amenity
                ? Corner(shell)
                : new Vector2(Drift(shell.Select(p => p.Go.position.x)),
                              Drift(shell.Select(p => p.Go.position.z)));

            // Which cells a piece fills is read off the piece's own box, not off its pivot:
            // a shopfront that is two modules wide (Shop_03), a stoop that reaches a whole
            // cell past the wall, a corner roof with an eave - every one of them is a
            // different arithmetic from the pivot, and all of them are the same question
            // asked of the box. A cell is filled when its CENTRE is inside the box, so an
            // eave of 0.8 m or a fire escape of 1.8 m cannot claim the cell next door.
            var wall = new HashSet<Vector2Int>();
            var yard = new HashSet<Vector2Int>();
            var pits = new HashSet<Vector2Int>();
            // cells with a WALL on them, kept apart from the ground the unit brings: it is
            // the share of the two that says whether an amenity is a building or a lot
            var built = new HashSet<Vector2Int>();
            // where a lot's own ground lies. The demo stands on a terrain and its lots sit
            // at whatever height that terrain gave them - one of the shops is two metres
            // under the water line - so the bake brings the lot's ground to zero and every
            // height on it is read from there.
            // (the LOWEST of everything it brought: the pack uses the same floor slab for a
            // shop's floor and for the roof over it, so the ground tiles alone are not the
            // ground - one shop read its roof as its floor and came out 0.9 m tall)
            float floor = family != Family.Amenity ? 0f : read.Min(p => p.Box.min.y);
            if (family == Family.Amenity)
            {
                // A lot's pieces are the pack's own two-metre panels and half-metre trims,
                // and not one of them covers a five-metre cell centre: read that way, the
                // 25 m diner came out as a single cell. So a lot is read by AREA - a cell
                // is the unit's when what it brought covers a good share of it - and the
                // same reading, taken of the walls alone, says how much of the lot is
                // built on.
                var cover = new Dictionary<Vector2Int, float>();
                var walls = new Dictionary<Vector2Int, float>();
                foreach (var p in shell)
                {
                    Spread(cover, p.Box, drift);
                    // What is BUILT on the lot is what it has OVERHEAD: a roof slab, an
                    // upper floor, an awning - anything whose underside clears a man's
                    // head. Walls were the obvious reading and the wrong one: the pack's
                    // panels are a hand thick, so a shop walled on all four sides covered
                    // three square metres of its own floor and read as a car park. A car
                    // yard's tarmac and a skatepark's ramps have nothing over them at all,
                    // and the yard's sales hut has its roof - which is the whole difference.
                    if (p.Box.min.y - floor >= Overhead) Spread(walls, p.Box, drift);
                }
                float least = Share * Cell * Cell;
                foreach (var pair in cover) if (pair.Value >= least) wall.Add(pair.Key);
                foreach (var pair in walls) if (pair.Value >= least) built.Add(pair.Key);
                built.IntersectWith(wall);
            }
            else
            {
                foreach (var p in shell)
                    foreach (var c in Covers(p.Box, drift))
                    {
                        if (p.Kind == PieceKind.Shell || p.Kind == PieceKind.Ground) wall.Add(c); else yard.Add(c);
                        if (p.Kind == PieceKind.Shell) built.Add(c);
                        if (p.Box.min.y < Sunk) pits.Add(c);
                    }
            }
            yard.ExceptWith(wall);
            // A cell the building stands on is never a pit, whatever reaches into it. A
            // stoop's box runs a good way in UNDER the house it serves, and taking that
            // reading at face value put a trench under every wall of both brownstones -
            // the whole of residential5 came out as pit. What has no floor is the FORECOURT
            // the stoop steps down into, and there the reading is true.
            pits.ExceptWith(wall);

            var all = wall.Concat(yard).ToList();
            if (all.Count == 0)
            {
                Debug.LogWarning($"{name}: nothing in it covers a cell of the raster - not measured");
                return null;
            }
            int minI = all.Min(c => c.x), maxI = all.Max(c => c.x);
            int minJ = all.Min(c => c.y), maxJ = all.Max(c => c.y);

            var unit = new Unit
            {
                Name = name,
                Source = source,
                CW = maxI - minI + 1,
                CD = maxJ - minJ + 1,
                Drift = drift,
                Pieces = pieces.Count,
                Parts = pieces,
                Built = built.Count,
                Seats = read.Count(p => p.Seat),
            };
            unit.Wall = new bool[unit.CW, unit.CD];
            unit.Yard = new bool[unit.CW, unit.CD];
            unit.Pit = new bool[unit.CW, unit.CD];
            foreach (var c in wall) unit.Wall[c.x - minI, c.y - minJ] = true;
            Fill(unit.Wall);
            foreach (var c in yard)
                if (!unit.Wall[c.x - minI, c.y - minJ]) unit.Yard[c.x - minI, c.y - minJ] = true;
            foreach (var c in pits) unit.Pit[c.x - minI, c.y - minJ] = true;

            // The faces. A side is a street face because entrances OPEN onto it - doors
            // and shopfronts - and only where they can actually see the street: a door
            // counts for its side when nothing of the building stands between it and that
            // edge of the footprint. Its own forecourt does not block it (a brownstone's
            // door opens over its stoop), but the other wing of an L does.
            //
            // Counting every door that merely pointed a way made all six buildings face
            // all four ways, which is how a courtyard full of doors reads if you do not
            // ask where they look.
            // On a lot it is the ROOFED part that blocks a door, not the lot's own ground:
            // a diner's door opens across its own deck to the street, and reading the deck
            // as a wall left the diner with no frontage at all and nowhere it could stand.
            var blocks = family == Family.Amenity ? built : wall;
            var opens = family == Family.Amenity ? wall : yard;
            foreach (var p in shell)
            {
                int side = Side(p.Yaw);
                if (side < 0 || !(p.Door || p.Shop || p.Stoop)) continue;
                bool open = false;
                // a lot's door is a two-metre panel that covers no cell centre, so which
                // cell it stands on is asked of the ground it OVERLAPS, the same way its
                // footprint was read
                foreach (var c in Standing(p.Box, drift, family))
                    if (Sees(c, Out[side], blocks, opens, minI, maxI, minJ, maxJ)) { open = true; break; }
                if (!open) continue;
                if (p.Door) unit.Doors[side]++;
                if (p.Shop) unit.Shops[side]++;
                if (p.Stoop) unit.Stoops[side]++;
            }

            // Which of those sides the unit is really FOR. An end house with one door round
            // the corner is not a second frontage, and reading it as one turned every
            // building into an island. A side counts when it carries a real share of the
            // entrances: at least two of them, and at least a third of what the busiest
            // side carries. Measured against all six - 3 and 4 against 1 (residential1),
            // 7 and 3 against 1 and 1 (residential3), 6 and 6 against 1 and 1
            // (residential5) - the rule separates every one of them, and the counts are
            // reported either way, so nothing hides behind the rule.
            // how much of each side of the footprint the unit actually stands on
            for (int s = 0; s < 4; s++)
            {
                int n = 0;
                if (s == 0 || s == 2)
                    for (int i = 0; i < unit.CW; i++)
                    {
                        int j = s == 0 ? 0 : unit.CD - 1;
                        if (unit.Wall[i, j] || unit.Yard[i, j]) n++;
                    }
                else
                    for (int j = 0; j < unit.CD; j++)
                    {
                        int i = s == 1 ? unit.CW - 1 : 0;
                        if (unit.Wall[i, j] || unit.Yard[i, j]) n++;
                    }
                unit.Front[s] = n;
            }

            int best = 0;
            for (int s = 0; s < 4; s++) best = Mathf.Max(best, unit.Doors[s] + unit.Shops[s]);
            for (int s = 0; s < 4; s++)
            {
                int n = unit.Doors[s] + unit.Shops[s];
                // a storefront is a shop and a door: one shopfront on a side IS the front,
                // and a corner shop fronts both its streets. An amenity is read the same
                // way and off its doors as well: the demo's shops carry glass walls where
                // the city pack carries shopfronts, and its gym opens through a gate
                unit.Face[s] = family == Family.Storefront ? unit.Shops[s] > 0
                             : family == Family.Amenity ? n > 0
                             : n >= 2 && n * 3 >= best;
            }
            // A building or a lot, measured rather than named: how much of the ground the
            // unit takes has a roof or a wall over it. The demo's car yard is a slab of
            // tarmac with one hut on it and its skatepark is all ramp; its diners and its
            // three shops are built over most of what they stand on. A venue which brings
            // a full terrace is a lot too even when its dining room is roofed: squeezing it
            // into the two-cell storefront strip made both Palm City diners impossible.
            bool aLot = unit.Built * 2 < unit.Cells;
            unit.Klass = family switch
            {
                Family.Park => Kind.Park,
                Family.Storefront => Kind.Storefront,
                Family.Amenity => aLot || unit.Seats >= 6 ? Kind.Amenity : Kind.Storefront,
                _ => Classify(unit.Face),
            };

            // what reaches out past the footprint, per side - the fire escapes and awnings
            // that will hang over the pavement once the unit stands at a kerb
            var origin = new Vector2(minI * Cell + drift.x, minJ * Cell + drift.y);
            float w = unit.CW * Cell, d = unit.CD * Cell;
            foreach (var p in read)
            {
                if (p.Kind == PieceKind.Prop) continue;
                Reach(ref unit.Over[0], origin.y - p.Box.min.z);
                Reach(ref unit.Over[1], p.Box.max.x - (origin.x + w));
                Reach(ref unit.Over[2], p.Box.max.z - (origin.y + d));
                Reach(ref unit.Over[3], origin.x - p.Box.min.x);
            }
            foreach (var p in read)
            {
                if (p.Box.max.y > unit.MaxH) unit.MaxH = p.Box.max.y;
                if (p.Tree) unit.Trees++;
            }
            foreach (var p in shell)
                if (p.Box.min.y < unit.Floor) unit.Floor = p.Box.min.y;

            // A lot is lifted onto the block's floor: it was arranged on the demo's terrain
            // and everything on it is read, baked and reported from its own ground, not
            // from whatever height that terrain happened to be at.
            if (family == Family.Amenity)
            {
                unit.MaxH -= floor;
                unit.Floor = 0f;
            }

            unit.Pivot = new Vector3(origin.x, family == Family.Amenity ? floor : 0f, origin.y);
            return unit;
        }

        /// <summary>Every raster cell whose centre this box stands over, read in the frame
        /// the drift has been taken out of.</summary>
        static IEnumerable<Vector2Int> Covers(Bounds box, Vector2 drift)
        {
            float x0 = box.min.x - drift.x, x1 = box.max.x - drift.x;
            float z0 = box.min.z - drift.y, z1 = box.max.z - drift.y;
            int i0 = Mathf.FloorToInt(x0 / Cell), i1 = Mathf.FloorToInt(x1 / Cell);
            int j0 = Mathf.FloorToInt(z0 / Cell), j1 = Mathf.FloorToInt(z1 / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    float cx = (i + 0.5f) * Cell, cz = (j + 0.5f) * Cell;
                    if (cx > x0 && cx < x1 && cz > z0 && cz < z1)
                        yield return new Vector2Int(i, j);
                }
        }

        static void Reach(ref float most, float much) { if (much > most) most = much; }

        /// <summary>How much of a cell a lot has to cover to claim it. A third: a fence
        /// post's rim of a cell is not a lot, and half a cell of tarmac is.</summary>
        const float Share = 0.35f;

        /// <summary>What a man walks under: a piece whose underside is this far over a
        /// lot's own ground is a roof over it, not a thing standing on it.</summary>
        const float Overhead = 2.2f;

        /// <summary>Where a lot's own raster starts: its south-west corner, so the first
        /// cell begins exactly where its ground does and nothing is lost to a half cell at
        /// either end.</summary>
        static Vector2 Corner(List<Piece> shell)
        {
            float x = shell.Min(p => p.Box.min.x), z = shell.Min(p => p.Box.min.z);
            return new Vector2(Mathf.Repeat(x, Cell), Mathf.Repeat(z, Cell));
        }

        /// <summary>The cells a piece stands on: the ones whose centre is inside its box
        /// for a building module, and every one it lies over at all for a lot's panels.</summary>
        static IEnumerable<Vector2Int> Standing(Bounds box, Vector2 drift, Family family)
        {
            if (family != Family.Amenity) return Covers(box, drift);
            var cover = new Dictionary<Vector2Int, float>();
            Spread(cover, box, drift);
            return cover.Keys;
        }

        /// <summary>Adds this box's floor area to every cell it lies over, in square metres
        /// - the reading a lot's footprint is taken from.</summary>
        static void Spread(Dictionary<Vector2Int, float> onto, Bounds box, Vector2 drift)
        {
            float x0 = box.min.x - drift.x, x1 = box.max.x - drift.x;
            float z0 = box.min.z - drift.y, z1 = box.max.z - drift.y;
            int i0 = Mathf.FloorToInt(x0 / Cell), i1 = Mathf.FloorToInt((x1 - 0.001f) / Cell);
            int j0 = Mathf.FloorToInt(z0 / Cell), j1 = Mathf.FloorToInt((z1 - 0.001f) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    float w = Mathf.Min(x1, (i + 1) * Cell) - Mathf.Max(x0, i * Cell);
                    float d = Mathf.Min(z1, (j + 1) * Cell) - Mathf.Max(z0, j * Cell);
                    if (w <= 0f || d <= 0f) continue;
                    var cell = new Vector2Int(i, j);
                    onto.TryGetValue(cell, out float had);
                    onto[cell] = had + w * d;
                }
        }

        /// <summary>
        /// Can a piece on this cell see the street on this side?
        ///
        /// It can when the way out is the unit's own ground and nothing else: its forecourt,
        /// then the edge of the footprint. Another wing in the way says no, and so does a
        /// gap - a cell inside the footprint that the unit does not stand on is the block's
        /// ground, an alley or a car park, and a shopfront looking into it fronts THAT, not
        /// a street.
        ///
        /// That gap is what made an L read as a building with four frontages: residential3
        /// reported six shops facing north across a north side only two cells wide, all of
        /// them shopfronts on the inner face of its long wing, looking into the notch.
        /// </summary>
        static bool Sees(Vector2Int cell, Vector2Int dir, HashSet<Vector2Int> wall,
                         HashSet<Vector2Int> yard, int minI, int maxI, int minJ, int maxJ)
        {
            var c = cell + dir;
            while (c.x >= minI && c.x <= maxI && c.y >= minJ && c.y <= maxJ)
            {
                if (wall.Contains(c) || !yard.Contains(c)) return false;
                c += dir;
            }
            return true;
        }

        /// <summary>Corner, row, through or island, from which sides open onto a street.</summary>
        static Kind Classify(bool[] face)
        {
            int n = face.Count(f => f);
            if (n >= 3) return Kind.Island;
            if (n <= 1) return Kind.Row;
            int a = System.Array.IndexOf(face, true);
            int b = System.Array.LastIndexOf(face, true);
            return (b - a) == 2 ? Kind.Through : Kind.Corner;
        }

        /// <summary>Everything the outside can reach is outside; the rest of the box is the
        /// building. A shell of wall panels encloses its own floor, and this is what fills
        /// it in - the same reading <c>CoreLayout.Shape</c> takes of a whole block.</summary>
        static void Fill(bool[,] mask)
        {
            int w = mask.GetLength(0), d = mask.GetLength(1);
            var open = new bool[w, d];
            var todo = new Queue<Vector2Int>();
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!mask[i, j] && (i == 0 || j == 0 || i == w - 1 || j == d - 1))
                    {
                        open[i, j] = true;
                        todo.Enqueue(new Vector2Int(i, j));
                    }
            while (todo.Count > 0)
            {
                var c = todo.Dequeue();
                foreach (var n in new[]
                {
                    new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                    new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1),
                })
                {
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= d) continue;
                    if (mask[n.x, n.y] || open[n.x, n.y]) continue;
                    open[n.x, n.y] = true;
                    todo.Enqueue(n);
                }
            }
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!open[i, j]) mask[i, j] = true;
        }

        // ------------------------------------------------------------------ reading a piece

        enum PieceKind
        {
            /// <summary>A module of the building itself - a wall, a corner, a door, a
            /// shopfront, a roof. These are what the footprint is read from.</summary>
            Shell,
            /// <summary>A stoop, its railings, its planters: the unit's own forecourt.
            /// It stands on cells of the block but nothing is built over them.</summary>
            Stoop,
            /// <summary>Hangs off the building: a fire escape, an awning, a roof hatch. It
            /// claims no cell, but it does reach out over the pavement. A park's fence is
            /// read the same way: it stands on the edge of the grass and leans out.</summary>
            Hung,
            /// <summary>Ground a park brings with it: grass and path tiles. These are what
            /// a park's footprint is read from, as a house's is from its walls.</summary>
            Ground,
            /// <summary>Everything else it carries - aircon, planters, signs, trees.</summary>
            Prop,
        }

        struct Piece
        {
            public Transform Go;
            public PieceKind Kind;
            public int Yaw;             // snapped to a right angle
            public Bounds Box;
            public bool Door, Shop, Stoop, Tree, Seat;
        }

        static Piece Read(Transform t)
        {
            var piece = new Piece { Go = t, Kind = PieceKind.Prop, Yaw = Snap(t.eulerAngles.y) };
            var box = Box(t.gameObject);
            piece.Box = box ?? new Bounds(t.position, Vector3.zero);

            string src = Source(t);
            if (src == null) return piece;
            piece.Tree = src.StartsWith("SM_Env_Tree") || src.StartsWith("SM_Env_Palm");
            piece.Seat = src.StartsWith("SM_Prop_Chair") || src.StartsWith("SM_Prop_Table") ||
                         src.StartsWith("SM_Prop_Bench") || src.StartsWith("SM_Prop_Umbrella");
            if (src.StartsWith("SM_Env_Grass")) { piece.Kind = PieceKind.Ground; return piece; }
            if (src.StartsWith("SM_Env_Fence")) { piece.Kind = PieceKind.Hung; return piece; }

            // the ground an amenity brings with it: the demo's basketball court and its
            // wooden ramps, the service road a car yard is surfaced with, and the generic
            // base floor every one of its huts stands on
            if (src.StartsWith("SM_Env_Court") || src.StartsWith("SM_Env_Ramp") ||
                src.StartsWith("SM_Gen_Env_Road") || src.StartsWith("SM_Bld_Base_Floor"))
            {
                piece.Kind = PieceKind.Ground;
                return piece;
            }
            if (!src.StartsWith("SM_Bld_")) return piece;

            // a fence and a railing are not walls: they stand on the edge of the ground and
            // lean out, which is how a park's fence has always been read
            if (src.StartsWith("SM_Bld_Fence") || src.StartsWith("SM_Bld_Wall_Railing") ||
                src.StartsWith("SM_Bld_Deco_Awning"))
            {
                piece.Kind = PieceKind.Hung;
                return piece;
            }

            if (src.Contains("FireEscape") || src.Contains("Roof_Access") || src.Contains("Shop_Cover"))
            {
                piece.Kind = PieceKind.Hung;
                return piece;
            }

            if (src.Contains("_Stairs"))
            {
                piece.Kind = PieceKind.Stoop;
                piece.Stoop = true;
                return piece;
            }

            piece.Kind = PieceKind.Shell;
            piece.Door = src.Contains("_Door");
            // the city pack's shopfront, and the Palm City glass wall that is the same
            // thing under another name - the demo's shops and diners are glazed with
            // SM_Bld_Wall_Window, and a shop with no shopfront reads as a blind wall
            piece.Shop = src.StartsWith("SM_Bld_Shop") || src.StartsWith("SM_Bld_Wall_Window");
            return piece;
        }

        static string Source(Transform t)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            return src ? src.name : null;
        }

        static Bounds? Box(GameObject go)
        {
            Bounds box = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { box = r.bounds; any = true; }
                else box.Encapsulate(r.bounds);
            }
            return any ? box : (Bounds?)null;
        }

        static int Snap(float yaw)
        {
            int q = Mathf.RoundToInt(yaw / 90f) * 90;
            return ((q % 360) + 360) % 360;
        }

        /// <summary>Is this piece square to the grid, or was it turned on purpose?</summary>
        static bool IsSquare(float yaw) => Mathf.Abs(Mathf.DeltaAngle(yaw, Snap(yaw))) <= Square;

        /// <summary>Which way a piece at this yaw faces, as a side index, or -1 if the
        /// yaw is not one of the four.</summary>
        static int Side(int yaw) => yaw switch
        {
            0 => 2, 90 => 1, 180 => 0, 270 => 3, _ => -1,
        };

        /// <summary>
        /// How far this run of module pivots sits from the 5 m raster - the middle reading
        /// rather than the first, so one piece nudged by hand cannot move the building.
        ///
        /// A module's pivot is a corner of its cell, so a building laid on the raster reads
        /// zero here. The demo did not always lay them on it, and a building read half a
        /// cell out is a building whose every wall lands in the wrong cell.
        /// </summary>
        static float Drift(IEnumerable<float> pivots)
        {
            var offs = pivots.Select(v =>
            {
                float o = Mathf.Repeat(v, Cell);
                return o > Cell * 0.5f ? o - Cell : o;      // nearest raster line, either way
            }).OrderBy(v => v).ToList();
            if (offs.Count == 0) return 0f;
            float mid = offs[offs.Count / 2];
            return Mathf.Abs(mid) < 0.01f ? 0f : mid;
        }


        // ------------------------------------------------------------------ the bake

        static bool Write(Unit unit)
        {
            var pieces = unit.Parts;
            if (pieces == null || pieces.Count == 0) return false;

            EnsureFolder(OutDir);
            var go = new GameObject(unit.Name);
            int copied = 0;
            foreach (var t in pieces)
            {
                var p = t.position;
                // x and z are moved onto the raster (the drift comes out here, once, for
                // the whole unit); y is left exactly as the pack gave it, basements and all -
                // except for a lot off the demo's terrain, whose own ground is brought to
                // zero (Pivot.y, nought for everything laid in the harvest scene)
                var stand = new Vector3(p.x - unit.Pivot.x, p.y - unit.Pivot.y, p.z - unit.Pivot.z);
                var copy = CoreBlockTray.Restand(t.gameObject, go.transform, stand, out bool linked);
                if (!linked) copied++;
                if (IsSquare(t.eulerAngles.y))
                    copy.transform.rotation = Quaternion.Euler(0f, Snap(t.eulerAngles.y), 0f);
            }

            var tag = go.AddComponent<LivingCity.Generation.BlockLotTag>();
            tag.lotWidth = unit.CW * Cell;
            tag.lotDepth = unit.CD * Cell;

            // These units are visual recipes. Their nested Synty modules contain many
            // colliders, including mirrored BoxColliders that Unity rejects whenever a
            // pooled instance is reparented. One positive lot box is cheaper, stable under
            // all four unit rotations, and is the footprint BuildingCardPicker expects.
            PreparePhysics(go, unit.CW * Cell, unit.CD * Cell, unit.Floor, unit.MaxH);

            PrefabUtility.SaveAsPrefabAsset(go, $"{OutDir}/{unit.Name}.prefab");
            Object.DestroyImmediate(go);
            if (copied > 0)
                Debug.Log($"{unit.Name}: {copied} piece(s) deep-copied - rebuilt or unlinked");
            return true;
        }

        static void PreparePhysics(GameObject root, float width, float depth,
                                   float floor, float top)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var footprint = root.GetComponent<BoxCollider>();
            if (footprint == null) footprint = root.AddComponent<BoxCollider>();
            float low = Mathf.Min(floor, 0f);
            float high = Mathf.Max(low + 1f, top);
            footprint.center = new Vector3(width * 0.5f, (low + high) * 0.5f, depth * 0.5f);
            footprint.size = new Vector3(Mathf.Max(0.1f, width), high - low,
                                         Mathf.Max(0.1f, depth));
            footprint.isTrigger = false;
            footprint.enabled = true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string soFar = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{soFar}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(soFar, parts[i]);
                soFar = next;
            }
        }

        // ------------------------------------------------------------------ the table

        /// <summary>
        /// The measured table, written as code the recipe and the offline sim both read.
        /// It is generated: every number in it came off the scene a moment ago, and editing
        /// it by hand only means the next harvest disagrees with the buildings.
        /// </summary>
        static void WriteTable(List<Unit> units)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// GENERATED by Tools/City/Residential/Bake Named Buildings.");
            sb.AppendLine("// Every figure here was measured off the buildings named residential1..N, the");
            sb.AppendLine("// parks named park1..N and the named storefronts in Assets/Scenes/CoreHarvest.unity,");
            sb.AppendLine("// and off the amenities the user named in Assets/Scenes/PalmCityDemo.unity.");
            sb.AppendLine("// Do not edit by hand - re-run the harvest.");
            sb.AppendLine("//");
            sb.AppendLine("// A plan row reads west to east; the FIRST row is the NORTH edge, so the table");
            sb.AppendLine("// looks the way the block looks from above with north up.");
            sb.AppendLine("//   '#' the building stands on this cell   'f' its own forecourt: stoop, pit, garden");
            sb.AppendLine("//   ':' forecourt that drops below -0.6 m (no floor slab under it)");
            sb.AppendLine("//   ',' building whose own cell drops below -0.6 m");
            sb.AppendLine("//   '.' nothing - the block's to use");
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine();
            sb.AppendLine("namespace RoadDemo");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>What a residential unit is: its footprint on the 5 m raster, which");
            sb.AppendLine("    /// of its sides open onto a street, and what hangs over the pavement.</summary>");
            sb.AppendLine("    public sealed class ResidentialUnit");
            sb.AppendLine("    {");
            sb.AppendLine("        public string Name;");
            sb.AppendLine("        public int CW, CD;");
            sb.AppendLine("        /// <summary>North row first, west to east. See the key at the top of this file.</summary>");
            sb.AppendLine("        public string[] Plan;");
            sb.AppendLine("        /// <summary>South, east, north, west.</summary>");
            sb.AppendLine("        public bool[] Face;");
            sb.AppendLine("        public int[] Doors, Shops, Stoops;");
            sb.AppendLine("        /// <summary>What reaches out past the footprint on each side, metres.</summary>");
            sb.AppendLine("        public float[] Over;");
            sb.AppendLine("        public int Trees, Pieces;");
            sb.AppendLine("        /// <summary>Chairs, tables, umbrellas and benches it brings with it: a unit");
            sb.AppendLine("        /// that arrives with its own terrace is not given a second one.</summary>");
            sb.AppendLine("        public int Seats;");
            sb.AppendLine("        public float MaxH;");
            sb.AppendLine("        /// <summary>The lowest its walls and stoops go: the level a pit's floor is laid at.</summary>");
            sb.AppendLine("        public float Floor;");
            sb.AppendLine("        public ResidentialKind Kind;");
            sb.AppendLine();
            sb.AppendLine("        public bool Wall(int i, int j) => At(i, j) == '#' || At(i, j) == ',';");
            sb.AppendLine("        public bool Yard(int i, int j) => At(i, j) == 'f' || At(i, j) == ':';");
            sb.AppendLine("        public bool Pit(int i, int j) => At(i, j) == ':' || At(i, j) == ',';");
            sb.AppendLine("        public bool Filled(int i, int j) => At(i, j) != '.';");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>The cell at (i east, j north), reading the plan bottom row first.</summary>");
            sb.AppendLine("        char At(int i, int j) =>");
            sb.AppendLine("            i < 0 || j < 0 || i >= CW || j >= CD ? '.' : Plan[CD - 1 - j][i];");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Row, corner, through and island are houses. A park brings its own grass;");
            sb.AppendLine("    /// an amenity is a complete outdoor lot (court, gym, car yard or diner) which gets a");
            sb.AppendLine("    /// quiet floor and a clear band from the pavement and neighbouring buildings. A");
            sb.AppendLine("    /// storefront is a small shop which stands in a gap in the row.</summary>");
            sb.AppendLine("    public enum ResidentialKind { Row, Corner, Through, Island, Park, Amenity, Storefront }");
            sb.AppendLine();
            sb.AppendLine("    public static class ResidentialUnits");
            sb.AppendLine("    {");
            sb.AppendLine("        public static IEnumerable<ResidentialUnit> Houses =>");
            sb.AppendLine("            All.Where(u => !IsLot(u) && u.Kind != ResidentialKind.Storefront);");
            sb.AppendLine("        public static IEnumerable<ResidentialUnit> Parks => All.Where(IsLot);");
            sb.AppendLine("        public static IEnumerable<ResidentialUnit> Storefronts => All.Where(u => u.Kind == ResidentialKind.Storefront);");
            sb.AppendLine("        public static bool IsLot(ResidentialUnit unit) => unit != null &&");
            sb.AppendLine("            (unit.Kind == ResidentialKind.Park || unit.Kind == ResidentialKind.Amenity);");
            sb.AppendLine();
            sb.AppendLine("        public static readonly ResidentialUnit[] All =");
            sb.AppendLine("        {");
            foreach (var unit in units)
            {
                sb.AppendLine("            new ResidentialUnit");
                sb.AppendLine("            {");
                sb.AppendLine($"                Name = \"{unit.Name}\", CW = {unit.CW}, CD = {unit.CD},");
                sb.AppendLine($"                Kind = ResidentialKind.{unit.Klass},");
                sb.AppendLine($"                MaxH = {unit.MaxH.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}f," +
                              $" Floor = {unit.Floor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}f," +
                              $" Trees = {unit.Trees}, Pieces = {unit.Pieces}, Seats = {unit.Seats},");
                sb.AppendLine("                Plan = new[]");
                sb.AppendLine("                {");
                for (int j = unit.CD - 1; j >= 0; j--)
                {
                    var row = new StringBuilder();
                    for (int i = 0; i < unit.CW; i++) row.Append(Glyph(unit, i, j));
                    sb.AppendLine($"                    \"{row}\",");
                }
                sb.AppendLine("                },");
                sb.AppendLine($"                Face = new[] {{ {string.Join(", ", unit.Face.Select(f => f ? "true" : "false"))} }},");
                sb.AppendLine($"                Doors = new[] {{ {string.Join(", ", unit.Doors)} }},");
                sb.AppendLine($"                Shops = new[] {{ {string.Join(", ", unit.Shops)} }},");
                sb.AppendLine($"                Stoops = new[] {{ {string.Join(", ", unit.Stoops)} }},");
                sb.AppendLine($"                Over = new[] {{ {string.Join(", ", unit.Over.Select(o => Metres(o)))} }},");
                sb.AppendLine("            },");
            }
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            System.IO.File.WriteAllText(TablePath, sb.ToString().Replace("\r\n", "\n"));
        }

        static string Metres(float m) =>
            Mathf.Max(0f, m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "f";

        static char Glyph(Unit unit, int i, int j)
        {
            bool wall = unit.Wall[i, j], yard = unit.Yard[i, j], pit = unit.Pit[i, j];
            if (wall) return pit ? ',' : '#';
            if (yard) return pit ? ':' : 'f';
            return '.';
        }

        // ------------------------------------------------------------------ the report

        static string Report(List<Unit> units, int wrote)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Residential harvest: {units.Count} unit(s), {wrote} prefab(s) written.");
            if (units.Count == 0) return sb.ToString();

            sb.AppendLine();
            sb.AppendLine("unit             cells    m          kind     faces      doors shops stoops trees  h    over (S/E/N/W)   drift");
            foreach (var u in units)
            {
                string faces = string.Join("", Enumerable.Range(0, 4).Select(s => u.Face[s] ? SideName[s] : "-"));
                sb.AppendLine(
                    $"{u.Name,-16} {u.CW,2}x{u.CD,-2}   {u.CW * Cell,3:F0}x{u.CD * Cell,-3:F0}   {u.Klass,-8} {faces,-10} " +
                    $"{u.Doors.Sum(),5} {u.Shops.Sum(),5} {u.Stoops.Sum(),6} {u.Trees,5} {u.MaxH,4:F1}  " +
                    $"{string.Join("/", u.Over.Select(o => o.ToString("F1")))}   " +
                    $"{(u.Drift == Vector2.zero ? "on raster" : $"{u.Drift.x:F2},{u.Drift.y:F2} taken out")}");
            }

            foreach (var u in units)
            {
                sb.AppendLine();
                sb.AppendLine($"{u.Name}  {u.CW * Cell:F0} x {u.CD * Cell:F0} m, {u.Cells} cell(s), {u.Pieces} piece(s), " +
                              $"{u.Built} walled cell(s), {u.Seats} seat(s)");
                for (int s = 0; s < 4; s++)
                    sb.AppendLine($"    {SideName[s]}: {u.Doors[s],2} door(s) {u.Shops[s],2} shop(s) " +
                                  $"{u.Stoops[s],2} stoop(s), frontage {u.Front[s]} cell(s)" +
                                  (u.Face[s] ? "   <- face" : ""));
                for (int j = u.CD - 1; j >= 0; j--)
                {
                    var row = new StringBuilder("    ");
                    for (int i = 0; i < u.CW; i++) row.Append(Glyph(u, i, j));
                    sb.AppendLine(row.ToString());
                }
            }
            return sb.ToString();
        }
    }
}
