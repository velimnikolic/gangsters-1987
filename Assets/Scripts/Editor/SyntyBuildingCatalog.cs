using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The showroom: every assembled building the PalmCity demo scene contains, baked
    /// through the same shell-only pipeline the city kit uses (Building subtree, active
    /// renderers, winding fix) and lined up on a grid in Assets/BuildingCatalog.unity with
    /// a floating label - name and footprint - over each. Built so the user can walk the
    /// line and SAY which buildings become the residential stock; nothing here touches the
    /// kit itself (the bakes land in Assets/CityKit/Catalog, a sibling the bootstrap never
    /// reads).
    ///
    /// Groups are enumerated from the demo scene, not hardcoded - a pack update that adds
    /// a building shows up here for free. Street (the road network) and Beach (dressing
    /// only) are skipped by name; Estate/Mansion rides along at the end.
    /// </summary>
    public static class SyntyBuildingCatalog
    {
        const string PalmDemo = "Assets/Synty/PolygonPalmCity/Scenes/Demo.unity";
        const string CityDemo = "Assets/Synty/PolygonCity/Scenes/Demo.unity";
        const string ScenePath = "Assets/BuildingCatalog.unity";

        // Each pack shows in its own titled block of the grid so the stock never mixes.
        const string SectionPalm = "PALM CITY";
        const string SectionCity = "CITY";
        const string SectionClubs = "NIGHTCLUBS";
        const string SectionPolice = "POLICE STATION";
        const string SectionGang = "GANG WARFARE";
        const string SectionCoffee = "COFFEE SHOP";

        /// <summary>The nightclub pack ships no assembled exteriors - each demo SCENE is
        /// one venue (Scene root + Roof_Layer; Background_Layer is the surrounding street
        /// dressing and stays behind). Per the user's cut of 2026-08-11, only NightClub
        /// made the catalog - DanceClub, DiveBar, RooftopBar and the police-demo street
        /// row are out (re-add a scene line here to bring one back).</summary>
        static readonly (string scene, string name)[] NightclubScenes =
        {
            ("Assets/Synty/PolygonNightclubs/Scenes/Demo_NightClub_01.unity", "NightClub"),
        };
        internal const string CatalogDir = SyntyKitExtractor.KitDir + "/Catalog";
        const string CatalogMeshDir = CatalogDir + "/Meshes";

        /// <summary>
        /// Ground truth for the seam conversation: every piece within 1.5 m of every cut,
        /// with where it ended up, written to Library/CatalogSeamLog.txt on each build.
        /// The offline pivot parse keeps guessing wrong about bounds and roof pieces -
        /// this file is what actually happened.
        /// </summary>
        const string SeamLogPath = "Library/CatalogSeamLog.txt";
        static readonly System.Text.StringBuilder SeamLog = new();

        /// <summary>
        /// Ledger of this build's palm bakes: prefab name, the world pivot BakeGroup
        /// recentred on, and the world bounds of the source pieces. Every catalog bake
        /// runs at yaw 0, so placing the prefab back AT its pivot reproduces the demo
        /// layout exactly - which is what the palm-block extraction does.
        /// </summary>
        internal static readonly List<(string name, Vector3 pivot, Bounds bounds)> PalmLedger = new();

        static readonly string[] SkippedGroups = { "Street", "Beach" };

        /// <summary>
        /// The user's corrections to the automatic split: which segments were really ONE
        /// building all along, by group name and segment letters. "AB" glues segments A
        /// and B back together and the result shows as Group_AB in the catalog, so the
        /// name itself records the decision. Segments not named here keep their letters,
        /// which is what keeps the conversation stable between rebuilds.
        ///
        /// Filled from the user's reading of the catalog scene - e.g. after "spoji
        /// Apartment_03 A i B" this gains ["Apartment_03"] = new[] { "AB" }.
        /// </summary>
        static readonly Dictionary<string, string[]> ManualJoins = new()
        {
            // 2026-08-11, the user's walk of the catalog: all three splits were one
            // building all along, so each group folds back to its bare name.
            ["Fairground"]    = new[] { "AB" },
            ["Skyscraper_02"] = new[] { "AB" },
            ["Skyscraper_03"] = new[] { "ABC" },
            ["Apartment_03"]  = new[] { "ABCD" },
            ["Restaurant_02"] = new[] { "AB" },
            ["CarYard"]       = new[] { "AB" },
            ["Hotel_02"]      = new[] { "AB" },
            ["Mansion"]       = new[] { "ABCDEFG" },
            ["Apartment_01"]  = new[] { "AB" },
            ["Hotel_01"]      = new[] { "BC" },
        };

        /// <summary>
        /// The user's corrections to piece ownership AT a cut. Both buildings' walls stand
        /// in the same party plane, so assignment by bounds centre is a coin toss - at the
        /// ParkingGarage seam the whole plane (segment B's white upper wall AND the brick
        /// lower wall) landed on one side, which read as a stolen wall in the showroom.
        /// Keyed by group name; each rule sends a _Wall piece within 0.8 m of a cut whose
        /// sharedMaterial contains the substring to the lettered segment. Unmatched pieces
        /// keep their geometric assignment.
        ///
        /// ParkingGarage, per the user: B keeps the white wall (Tile_Light storeys, white
        /// plaster railings), A takes the brown brick one.
        ///
        /// Each rule names the CUT it works at (0 = the A/B boundary, in row order), so a
        /// material that reappears at another seam - plaster does, on Hotel_01 - cannot
        /// reach across the row. An optional lateral band (measured on the piece PIVOT
        /// along the seam plane's own axis, demo metres) splits one material between
        /// owners where a plane serves two walls end to end. The letters assume no sliver
        /// got folded (true for every group tabled here).
        /// </summary>
        readonly struct SeamRule
        {
            public readonly int cut;
            public readonly string matContains; // "" matches every wall piece in the plane
            public readonly char segment;
            public readonly float lateralMin, lateralMax;
            public readonly float heightMax; // pivot y; caps a rule to the storeys below
            public readonly string nameContains; // widens the name gate for THIS rule only

            public SeamRule(int cut, string matContains, char segment,
                            float lateralMin = float.NegativeInfinity,
                            float lateralMax = float.PositiveInfinity,
                            float heightMax = float.PositiveInfinity,
                            string nameContains = "")
            {
                this.cut = cut;
                this.matContains = matContains;
                this.segment = segment;
                this.lateralMin = lateralMin;
                this.lateralMax = lateralMax;
                this.heightMax = heightMax;
                this.nameContains = nameContains;
            }
        }

        static readonly Dictionary<string, SeamRule[]> SeamWallOwners = new()
        {
            ["ParkingGarage"] = new[]
            {
                // Glass: the white tower's corner-window pieces stand in the same plane
                // (sharedMaterial Generic_Glass_Opaque) and belong with the white wall.
                // Concrete: the garage annex's terrace-railing trims (Villa_Railing) run
                // ALONG the seam; a stray in B juts out of the tower like a bar.
                new SeamRule(0, "Tile_Light", 'B'),
                new SeamRule(0, "Plaster_White", 'B'),
                new SeamRule(0, "Glass", 'B'),
                // The corner windows' slot 0 is the 01_A atlas, NOT the glass override
                // (the seam log settled this) - the Glass rule never fired for them.
                new SeamRule(0, "PolygonPalmCity_01", 'B'),
                // ALL brick belongs to the garage. A banded experiment gave the northern
                // brick stretch (z>130) to B to close a reported hole - the screenshot
                // showed those panels floating DETACHED in front of the tower: B's body
                // never reaches that far north, the stretch is A's annex wall.
                new SeamRule(0, "Brick", 'A'),
                new SeamRule(0, "Concrete", 'A'),
            },
            ["Hotel_01"] = new[]
            {
                // The settled logic, one seam at a time with the user's screenshots:
                // - A/B seam: the whole plane (blue wall, brick panel, gutter pipes,
                //   the stone garage door) is A's east face - the toss strays floated
                //   detached beside B ("rupa i visak"). Wholesale to A; B closes itself
                //   with a repainted borrow clone (see SeamWallBorrows).
                // - C/D seam: yellow + 04-atlas is D's identity BUT only up to D's
                //   roofline. D's body tops out at y9 (the label's h13 was the wall
                //   itself), so the cap sits at 8.5: storeys y0/3/6 stay D's wall, the
                //   y9 storey is the parapet of C's wooden roof terrace and everything
                //   above it towered over D as "zid desno prevelik".
                new SeamRule(0, "", 'A'),
                // The hut band, FULL height: D's roof-access hut (lat 104-107) closes
                // its west side with the y9 stretch of the shared yellow wall - the
                // capped rule below would send it to C and leave the hut gaping.
                new SeamRule(2, "Plaster_Yellow", 'D', lateralMin: 103f, lateralMax: 108f),
                // D's wall proper: capped at its y9 roofline AND trimmed at the rear -
                // D's back facade sits at lat 103 (the awninged yaw-180 row), so the
                // lat 98-102 stretch of the shared wall is C's, not a 5 m fin behind D.
                new SeamRule(2, "Plaster_Yellow", 'D', lateralMin: 102f, heightMax: 8.5f),
                new SeamRule(2, "PolygonPalmCity_04", 'D', lateralMin: 102f, heightMax: 8.5f),
                // D's roof-access hut STRADDLES the cut (lat 103-108, y~9) - the plain
                // wildcard stole its plaster west wall and left the hut gaping.
                new SeamRule(2, "Generic_Plaster", 'D', lateralMin: 103f, lateralMax: 108f),
                // The terrace-colonnade pillars ("sipkice" over D's roof) carry no _Wall
                // in their names - this rule widens the gate for Hotel_01 only, so the
                // ParkingGarage seam pillars stay untouched.
                new SeamRule(2, "", 'C', nameContains: "_Pillar"),
                new SeamRule(2, "", 'C'),
            },
        };

        /// <summary>
        /// The user's shared-wall corrections. Some seams have only ONE authored wall
        /// serving BOTH buildings (Hotel_01 C/D: D's yellow plane is also C's east
        /// closure - C is a low terrace block whose own east wall was never built).
        /// Moving such a wall just relocates the hole, so instead the neighbour KEEPS the
        /// original and the named segment receives a CLONE repainted into its own
        /// identity (repaint = substring of a material already present in the receiving
        /// segment). Only perpendicular _Wall_ modules clone - trims and railings stay.
        /// </summary>
        static readonly Dictionary<string, (int cut, string matContains, char segment, string repaint)[]> SeamWallBorrows = new()
        {
            ["Hotel_01"] = new[]
            {
                // C/D: D keeps its yellow wall, C's east closure is a plaster clone.
                (2, "Plaster_Yellow", 'C', "Generic_Plaster"),
                // A/B: A keeps its blue wall (the whole plane is A's), B's west closure
                // is a plaster clone of it.
                (0, "Plaster_Blue", 'B', "Generic_Plaster"),
            },
        };

        /// <summary>
        /// The user's "produzi zid do poda" corrections. A party wall the demo only built
        /// on the storeys visible above the neighbour's roof leaves its segment OPEN below
        /// once the row is split - ParkingGarage_B's white wall starts at 12 m because the
        /// brick garage hid everything beneath. For each rule, the matched wall's lowest
        /// authored storey at the seam is cloned downward, storey by storey, to the ground.
        /// </summary>
        static readonly Dictionary<string, (string matContains, char segment)[]> SeamWallFloorFills = new()
        {
            // PolygonPalmCity_01 fills the corner-window column (the strip beside the
            // white wall): the corner pieces' sharedMaterial is the 01_A atlas - the
            // seam log showed the old "Glass" guess matched nothing (glass is a later
            // material slot), which is why the corner column never filled.
            ["ParkingGarage"] = new[] { ("Tile_Light", 'B'), ("PolygonPalmCity_01", 'B') },
        };

        /// <summary>
        /// The user's display renames: demo group name -> catalog name. Only the OUTPUT
        /// (prefab, label) changes - ManualJoins, SeamWallOwners and SeamWallFloorFills
        /// stay keyed by the demo group name, and the kit extractor's role names are a
        /// separate vocabulary this table never touches.
        /// </summary>
        static readonly Dictionary<string, string> ManualNames = new()
        {
            ["Skyscraper_03"] = "Palm Tower",
            // Kit-referenced entries: the prefab keeps its runtime role name, only the
            // showroom label reads human (applied at placement, not at bake).
            ["building-policestation"] = "Police Station",
            ["building-warehouse"] = "Stovariste",
            ["building-coffeeshop"] = "Coffee Shop",
        };

        [MenuItem("Tools/City/Build Synty Building Catalog Scene", priority = 4)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // A fresh showroom every run: a previous, differently-split bake must not leave
            // stale Hotel_01_D/E prefabs beside the new set.
            AssetDatabase.DeleteAsset(CatalogDir);
            EnsureFolders();
            SyntyBakeUtil.ClearCache();
            SeamLog.Clear();
            PalmLedger.Clear();

            // 1. Bake every demo group into Catalog prefabs, one pack at a time.
            var baked = new List<(string section, string name)>();
            var demo = EditorSceneManager.OpenScene(PalmDemo, OpenSceneMode.Additive);
            try
            {
                var city = demo.GetRootGameObjects().FirstOrDefault(go => go.name == "City");
                if (city)
                    foreach (Transform group in city.transform)
                        if (!SkippedGroups.Contains(group.name))
                            BakeCatalogEntry(group, baked, SectionPalm);

                var estate = demo.GetRootGameObjects().FirstOrDefault(go => go.name == "Estate");
                var mansion = estate ? estate.transform.Find("Mansion") : null;
                if (mansion)
                    BakeCatalogEntry(mansion, baked, SectionPalm);

                // While the demo is still open: reassemble its street blocks as candidate
                // block prefabs (buildings from the ledger, props cloned off the scene).
                SyntyCityBlocks.ExtractPalmBlocks(demo);
            }
            finally
            {
                EditorSceneManager.CloseScene(demo, removeScene: true);
                SyntyBakeUtil.ClearCache();
            }

            BakeCityClusters(baked);
            foreach (var (venueScene, venueName) in NightclubScenes)
                BakeVenue(venueScene, venueName, baked);

            // Kit-owned compounds show as REFERENCES to the kit's own bakes (interiors
            // already stripped there) - re-baking them here would put second multi-MB
            // meshes in Catalog/Meshes for buildings the kit already owns.
            foreach (var (section, kitName) in new[]
            {
                (SectionPolice, "building-policestation"),
                (SectionGang, "building-warehouse"),
                (SectionCoffee, "building-coffeeshop"),
            })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{SyntyKitExtractor.BuildingsDir}/{kitName}.prefab"))
                    baked.Add((section, kitName));
                else
                    Debug.LogWarning($"[Catalog] {kitName} kit prefab missing - " +
                                     "run Tools/City/Create or Refresh Config Assets first.");

            if (baked.Count == 0)
            {
                EditorUtility.DisplayDialog("Catalog build failed",
                    "No groups could be baked from any demo scene. See the Console.", "OK");
                return;
            }

            AssetDatabase.SaveAssets();

            // The user's block recipes re-bake NOW, from the fresh Catalog prefabs -
            // this rebuild just regenerated every Catalog guid, so any block composed
            // against the previous bake points at ghosts.
            SyntyCityBlocks.Bake();

            // 2. The showroom scene: a labelled grid, five per row.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            const int columns = 5;
            const float spacing = 60f;
            var index = 0;
            string currentSection = null;
            foreach (var (section, name) in baked)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CatalogDir}/{name}.prefab");
                if (!prefab)
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{SyntyKitExtractor.BuildingsDir}/{name}.prefab");
                if (!prefab)
                    continue;

                if (section != currentSection)
                {
                    // A new pack: finish the row, leave one empty, title the block.
                    if (index % columns != 0)
                        index += columns - index % columns;
                    if (index > 0)
                        index += columns;
                    SectionHeader(section, new Vector3(-spacing * 0.75f, 0f, index / columns * spacing));
                    currentSection = section;
                }

                var row = index / columns;
                var column = index % columns;
                var position = new Vector3(column * spacing, 0f, row * spacing);

                // Yaw 180 so the normalised +Z front looks INTO the camera parked south.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

                Label(instance, ManualNames.TryGetValue(name, out var display) ? display : name,
                      position);
                index++;
            }

            // 3. The user's composed blocks under their own title. A block outgrows the
            //    60 m cell, so the row advances by measured width instead of the grid
            //    step; the pivot (footprint centre) shifts so left edges line up.
            var blockPrefabs = SyntyCityBlocks.LoadBaked();
            if (blockPrefabs.Count > 0)
            {
                if (index % columns != 0)
                    index += columns - index % columns;
                index += columns;
                var blockZ = index / columns * spacing;
                SectionHeader("BLOCKS", new Vector3(-spacing * 0.75f, 0f, blockZ));

                var cursor = 0f;
                foreach (var prefab in blockPrefabs)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetPositionAndRotation(
                        new Vector3(cursor, 0f, blockZ), Quaternion.Euler(0f, 180f, 0f));
                    var bounds = InstanceBounds(instance);
                    instance.transform.position += new Vector3(cursor - bounds.min.x, 0f, 0f);
                    Label(instance, prefab.name, instance.transform.position);
                    cursor += bounds.size.x + 20f;
                }
                index += columns; // the block row counts toward the camera fit below
            }

            var rows = (index + columns - 1) / columns;
            var centre = new Vector3((columns - 1) * spacing / 2f, 0f, (rows - 1) * spacing / 2f);
            var camera = Camera.main;
            if (camera)
            {
                camera.transform.SetPositionAndRotation(
                    centre + new Vector3(0f, 90f, -(rows * spacing / 2f + 90f)),
                    Quaternion.Euler(35f, 0f, 0f));
                camera.farClipPlane = 1200f;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            System.IO.File.WriteAllText(SeamLogPath, SeamLog.ToString());
            Debug.Log($"[Catalog] {index} buildings on show in {ScenePath}: " +
                      string.Join(", ", baked.Select(b => b.name)) +
                      $"\n[Catalog] seam log: {SeamLogPath}");
        }

        static void BakeCatalogEntry(Transform group, List<(string section, string name)> baked,
                                     string section, string nameOverride = null)
        {
            // The Building shell where the group has one, the whole group otherwise -
            // exactly the kit extractor's rule.
            var key = nameOverride ?? group.name;
            var shell = group.Find("Building");
            var source = shell ? shell.gameObject : group.gameObject;

            var copy = Object.Instantiate(source);
            copy.name = key;
            copy.transform.position = shell ? shell.position : group.position;
            copy.transform.rotation = shell ? shell.rotation : group.rotation;
            StripClouds(copy);

            try
            {
                // Demo groups like Hotel_01 are STREET ROWS of several buildings in one
                // flat list (the user spotted three distinct facades under one label), so
                // the row is split into its constituent buildings first and each segment
                // bakes on its own. Single-building groups come back as one segment and
                // bake exactly as before.
                var segments = SplitRow(copy.transform, key);

                // Labels first (A, B, C... in row order), then the user's manual joins glue
                // segments back together - the merged one carries the joined letters.
                var labelled = new List<(string label, List<Transform> pieces)>();
                for (var i = 0; i < segments.Count; i++)
                    labelled.Add((((char)('A' + i)).ToString(), segments[i]));

                if (ManualJoins.TryGetValue(key, out var joins))
                    foreach (var join in joins)
                    {
                        var members = labelled.Where(s => join.Contains(s.label)).ToList();
                        if (members.Count < 2)
                            continue;

                        // Letters run in row order, so a join of non-consecutive letters
                        // (e.g. "AC" while B exists) would bake a building with a hole
                        // where the skipped segment stood.
                        var skipped = labelled.Where(s => !join.Contains(s.label) &&
                                                          s.label.Length == 1 &&
                                                          s.label[0] > join.Min() &&
                                                          s.label[0] < join.Max()).ToList();
                        if (skipped.Count > 0)
                            Debug.LogWarning($"[Catalog] '{key}' join '{join}' skips " +
                                             $"segment(s) {string.Join(", ", skipped.Select(s => s.label))} " +
                                             "between its letters - the merged building will have a gap.");
                        var merged = (label: join, pieces: members.SelectMany(m => m.pieces).ToList());
                        labelled.Insert(labelled.IndexOf(members[0]), merged);
                        foreach (var member in members)
                            labelled.Remove(member);
                    }

                var displayName = ManualNames.TryGetValue(key, out var renamed)
                    ? renamed : key;

                if (labelled.Count <= 1)
                {
                    var bounds = InstanceBounds(copy);
                    var pivot = SyntyKitExtractor.BakeGroup(copy, displayName, yaw: 0f, CatalogDir, CatalogMeshDir);
                    if (section == SectionPalm && pivot.HasValue)
                        PalmLedger.Add((displayName, pivot.Value, bounds));
                    baked.Add((section, displayName));
                    return;
                }

                foreach (var (label, pieces) in labelled)
                {
                    var segmentName = $"{displayName}_{label}";
                    var segmentRoot = new GameObject(segmentName);
                    try
                    {
                        foreach (var piece in pieces)
                            piece.SetParent(segmentRoot.transform, worldPositionStays: true);
                        var bounds = InstanceBounds(segmentRoot);
                        var pivot = SyntyKitExtractor.BakeGroup(segmentRoot, segmentName, yaw: 0f,
                                                                CatalogDir, CatalogMeshDir);
                        if (section == SectionPalm && pivot.HasValue)
                            PalmLedger.Add((segmentName, pivot.Value, bounds));
                        baked.Add((section, segmentName));
                    }
                    finally
                    {
                        Object.DestroyImmediate(segmentRoot);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Catalog] '{key}' failed to bake: {e.Message}");
            }
            finally
            {
                if (copy) Object.DestroyImmediate(copy);
            }
        }

        /// <summary>
        /// The PolygonCity demo is ONE flat list of ~4000 prefab instances - no per-
        /// building groups at all. But its buildings stand apart across streets, so
        /// building pieces (SM_Bld_*) cluster by XZ footprint: expanded rects that touch
        /// are one building. Deterministic names City_01.. in south-west reading order,
        /// so the user's future corrections keep meaning across rebuilds.
        /// </summary>
        static void BakeCityClusters(List<(string section, string name)> baked)
        {
            var demo = EditorSceneManager.OpenScene(CityDemo, OpenSceneMode.Additive);
            try
            {
                var root = demo.GetRootGameObjects().FirstOrDefault(go => go.name == "Demo");
                if (!root)
                {
                    Debug.LogWarning("[Catalog] PolygonCity demo has no 'Demo' root - pack skipped.");
                    return;
                }

                var copy = Object.Instantiate(root);
                try
                {
                    StripClouds(copy);
                    var pieces = new List<Transform>();
                    foreach (Transform child in copy.transform)
                        if (child.name.StartsWith("SM_Bld_"))
                            pieces.Add(child);

                    var rects = new Rect[pieces.Count];
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        var r = pieces[i].GetComponentInChildren<MeshRenderer>(true);
                        var b = r ? r.bounds : new Bounds(pieces[i].position, Vector3.one);
                        rects[i] = Rect.MinMaxRect(b.min.x - 1.5f, b.min.z - 1.5f,
                                                   b.max.x + 1.5f, b.max.z + 1.5f);
                    }

                    var parent = Enumerable.Range(0, pieces.Count).ToArray();
                    int Find(int i)
                    {
                        while (parent[i] != i)
                            i = parent[i] = parent[parent[i]];
                        return i;
                    }
                    for (var i = 0; i < pieces.Count; i++)
                        for (var j = i + 1; j < pieces.Count; j++)
                            if (rects[i].Overlaps(rects[j]))
                                parent[Find(i)] = Find(j);

                    var clusters = new Dictionary<int, List<Transform>>();
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        var id = Find(i);
                        if (!clusters.TryGetValue(id, out var list))
                            clusters[id] = list = new List<Transform>();
                        list.Add(pieces[i]);
                    }

                    var ordered = clusters.Values.Where(c => c.Count >= 6)
                        .OrderBy(c => Mathf.Round(c.Min(t => t.position.z) / 10f))
                        .ThenBy(c => c.Min(t => t.position.x)).ToList();

                    var n = 0;
                    foreach (var cluster in ordered)
                    {
                        var name = $"City_{++n:00}";
                        try
                        {
                            BakeCluster(cluster, name, baked);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"[Catalog] '{name}' failed to bake: {e.Message}");
                        }
                    }
                    Debug.Log($"[Catalog] PolygonCity: {n} building clusters from {pieces.Count} " +
                              $"pieces ({clusters.Values.Count(c => c.Count < 6)} sliver clusters dropped).");
                }
                finally
                {
                    if (copy) Object.DestroyImmediate(copy);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(demo, removeScene: true);
                SyntyBakeUtil.ClearCache();
            }
        }

        /// <summary>
        /// One city block, split into its constituent buildings so the explorer can pick
        /// them "ponaosob". A single-building cluster bakes flat exactly as before; a
        /// multi-building one bakes each building as City_XX_A.. and reassembles them as
        /// children of one City_XX prefab at their authored offsets - each child carries
        /// its own combined mesh and footprint collider, the parent carries none.
        /// </summary>
        static void BakeCluster(List<Transform> cluster, string name,
                                List<(string section, string name)> baked)
        {
            var buildings = SplitClusterByColour(cluster);

            if (buildings.Count <= 1)
            {
                var holder = new GameObject(name);
                try
                {
                    foreach (var piece in cluster)
                        piece.SetParent(holder.transform, worldPositionStays: true);
                    SyntyKitExtractor.BakeGroup(holder, name, yaw: 0f, CatalogDir, CatalogMeshDir);
                    baked.Add((SectionCity, name));
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
                return;
            }

            // Anchor = the whole cluster's footprint centre at ground level, so the
            // assembled parent stands in its showroom cell exactly where the flat bake
            // used to. Measure BEFORE the sub-bakes reparent and consume the pieces.
            var all = ClusterBounds(cluster);
            var anchor = new Vector3(all.center.x, 0f, all.center.z);

            var members = new List<(string sub, Vector3 pivot)>();
            for (var b = 0; b < buildings.Count; b++)
            {
                var subName = $"{name}_{SegmentLabel(b)}";
                var holder = new GameObject(subName);
                try
                {
                    foreach (var piece in buildings[b])
                        piece.SetParent(holder.transform, worldPositionStays: true);
                    var pivot = SyntyKitExtractor.BakeGroup(holder, subName, yaw: 0f,
                                                            CatalogDir, CatalogMeshDir);
                    if (pivot.HasValue)
                        members.Add((subName, pivot.Value));
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Catalog] '{subName}' failed to bake: {e.Message}");
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
            }

            var parentGo = new GameObject(name);
            try
            {
                foreach (var (sub, pivot) in members)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CatalogDir}/{sub}.prefab");
                    if (!prefab)
                        continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetParent(parentGo.transform, worldPositionStays: false);
                    instance.transform.localPosition = pivot - anchor;
                }
                PrefabUtility.SaveAsPrefabAsset(parentGo, $"{CatalogDir}/{name}.prefab");
                baked.Add((SectionCity, name));
                Debug.Log($"[Catalog] '{name}': {members.Count} buildings " +
                          $"({string.Join(", ", members.Select(m => m.sub))}).");
            }
            finally
            {
                Object.DestroyImmediate(parentGo);
            }
        }

        static Bounds ClusterBounds(List<Transform> pieces)
        {
            var bounds = default(Bounds);
            var first = true;
            foreach (var piece in pieces)
                foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            return bounds;
        }

        static string SegmentLabel(int i)
            => i < 26 ? ((char)('A' + i)).ToString()
                      : SegmentLabel(i / 26 - 1) + (char)('A' + i % 26);

        // Dressing families never seed a building of their own - a default-material
        // roofline or shop row would otherwise chain the whole block into one "building".
        // They are dealt to the run whose footprint band they overlap most, so a shop
        // front stays under its own storeys and a roof stays on its own building.
        static readonly string[] DressingTokens =
            { "Roof", "FireEscape", "Cover", "Door", "Stairs", "Shop" };

        /// <summary>
        /// The user's model for the PolygonCity blocks: a building is a CONTIGUOUS SLICE
        /// of the street row - full depth, FULL HEIGHT (ground floor to roof, whatever
        /// each storey's colour), advancing part by part - told apart from its
        /// neighbours BY COLOUR (the PolygonCity_XX_Y atlas variant each wall module
        /// resolves to; no override = the _01_A default, itself a colour).
        ///
        /// Only WALL cores define the slices: the block perimeter is walked as a ring
        /// (S, E, N, W), wall pieces snap to 5m cells on their nearest side, and a
        /// building is a run of adjacent cells whose colour agrees. Everything else -
        /// roofs, shop fronts, escapes, huts - is then dealt to the run whose
        /// interior-extended footprint band it OVERLAPS most. Assigning by overlap
        /// rather than by nearest-side is what keeps a selection one unbroken block:
        /// on an L-corner the set-back roof tiles used to flip to the other street's
        /// run and a wide shop front could leave its own storeys ("selektuje samo
        /// prvi sprat" / roof bleeding over the neighbours - the user's screenshot).
        /// Wall pieces deeper than the ring band are freestanding interior towers and
        /// group separately by colour with strict edge contact (corner-touch must not
        /// chain slices diagonally).
        /// </summary>
        static List<List<Transform>> SplitClusterByColour(List<Transform> pieces)
        {
            const float CellSize = 5f;       // the demo authors on a 5m module grid
            const float RingDepth = 12.5f;   // how deep a street row reaches into the block
            const float RunGap = 8f;         // cells further apart than this never chain

            var all = ClusterBounds(pieces);
            var bounds = new Bounds[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                var renderer = pieces[i].GetComponentInChildren<MeshRenderer>(true);
                bounds[i] = renderer ? renderer.bounds : new Bounds(pieces[i].position, Vector3.one);
            }

            string CoreKey(int i)
            {
                if (DressingTokens.Any(pieces[i].name.Contains))
                    return null;
                var colour = ColourOf(pieces[i]);
                return colour == null ? null : $"{FamilyOf(pieces[i].name)}|{colour}";
            }

            // --- 1. wall cores only: ring cells vs interior; the rest waits for the deal
            var cells = new Dictionary<(int side, int station), List<int>>();
            var interiorCores = new List<int>();
            var loose = new List<int>();
            for (var i = 0; i < pieces.Count; i++)
            {
                if (CoreKey(i) == null)
                {
                    loose.Add(i);
                    continue;
                }
                var c = bounds[i].center;
                var dS = c.z - all.min.z;
                var dE = all.max.x - c.x;
                var dN = all.max.z - c.z;
                var dW = c.x - all.min.x;
                var least = Mathf.Min(dS, dE, dN, dW);
                if (least > RingDepth)
                {
                    interiorCores.Add(i);
                    continue;
                }
                var side = least == dS ? 0 : least == dE ? 1 : least == dN ? 2 : 3;
                var along = side == 0 || side == 2 ? c.x : c.z;
                var key = (side, Mathf.RoundToInt(along / CellSize));
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<int>();
                list.Add(i);
            }

            // --- 2. walk the ring: S west→east, E south→north, N east→west, W north→south
            var ordered = cells
                .OrderBy(kv => kv.Key.side)
                .ThenBy(kv => kv.Key.side is 0 or 1 ? kv.Key.station : -kv.Key.station)
                .Select(kv =>
                {
                    var centre = Vector3.zero;
                    foreach (var i in kv.Value)
                        centre += bounds[i].center;
                    centre /= kv.Value.Count;
                    var colour = kv.Value.Select(CoreKey)
                        .GroupBy(k => k).OrderByDescending(g => g.Count())
                        .Select(g => g.Key).First();
                    return (kv.Key.side, members: kv.Value, centre, colour);
                })
                .ToList();

            var runs = new List<(List<int> members, string colour, Vector3 first, Vector3 last,
                                 Dictionary<int, (float min, float max)> sides)>();
            (float min, float max) CellExtent(int side, List<int> members)
            {
                var min = float.MaxValue;
                var max = float.MinValue;
                foreach (var i in members)
                {
                    min = Mathf.Min(min, side is 0 or 2 ? bounds[i].min.x : bounds[i].min.z);
                    max = Mathf.Max(max, side is 0 or 2 ? bounds[i].max.x : bounds[i].max.z);
                }
                return (min, max);
            }
            void Widen(Dictionary<int, (float min, float max)> sides, int side,
                       (float min, float max) extent)
            {
                sides[side] = sides.TryGetValue(side, out var e)
                    ? (Mathf.Min(e.min, extent.min), Mathf.Max(e.max, extent.max))
                    : extent;
            }
            foreach (var (side, members, centre, colour) in ordered)
            {
                var extend = runs.Count > 0
                    && Vector3.Distance(centre, runs[^1].last) <= RunGap
                    && colour == runs[^1].colour;
                if (extend)
                {
                    var run = runs[^1];
                    run.members.AddRange(members);
                    run.last = centre;
                    Widen(run.sides, side, CellExtent(side, members));
                    runs[^1] = run;
                }
                else
                {
                    var sides = new Dictionary<int, (float min, float max)>();
                    Widen(sides, side, CellExtent(side, members));
                    runs.Add((new List<int>(members), colour, centre, centre, sides));
                }
            }

            // The walk is cyclic: the west side's last cell stands beside the south
            // side's first, so a building wrapping that corner arrives split in two.
            if (runs.Count > 1
                && Vector3.Distance(runs[0].first, runs[^1].last) <= RunGap
                && runs[0].colour == runs[^1].colour)
            {
                var first = runs[0];
                first.members.AddRange(runs[^1].members);
                foreach (var kv in runs[^1].sides)
                    Widen(first.sides, kv.Key, kv.Value);
                runs[0] = first;
                runs.RemoveAt(runs.Count - 1);
            }

            // Each group carries footprint band(s); a ring run's bands reach from its
            // street edge RingDepth into the block, so its set-back roof storeys land
            // on it by overlap instead of drifting to whichever side happens nearer.
            var groups = new List<(List<int> members, List<Rect> bands)>();
            foreach (var run in runs)
            {
                var bands = new List<Rect>();
                foreach (var kv in run.sides)
                {
                    var (min, max) = kv.Value;
                    bands.Add(kv.Key switch
                    {
                        0 => Rect.MinMaxRect(min - 0.5f, all.min.z - 0.5f,
                                             max + 0.5f, all.min.z + RingDepth),
                        1 => Rect.MinMaxRect(all.max.x - RingDepth, min - 0.5f,
                                             all.max.x + 0.5f, max + 0.5f),
                        2 => Rect.MinMaxRect(min - 0.5f, all.max.z - RingDepth,
                                             max + 0.5f, all.max.z + 0.5f),
                        _ => Rect.MinMaxRect(all.min.x - 0.5f, min - 0.5f,
                                             all.min.x + RingDepth, max + 0.5f),
                    });
                }
                groups.Add((run.members, bands));
            }

            // --- 3. interior towers: colour union with strict EDGE contact ------------
            if (interiorCores.Count > 0)
            {
                var parent = interiorCores.ToDictionary(i => i, i => i);
                int Find(int i)
                {
                    while (parent[i] != i)
                        i = parent[i] = parent[parent[i]];
                    return i;
                }
                bool EdgeContact(int a, int b)
                {
                    var x = Mathf.Min(bounds[a].max.x, bounds[b].max.x)
                          - Mathf.Max(bounds[a].min.x, bounds[b].min.x);
                    var z = Mathf.Min(bounds[a].max.z, bounds[b].max.z)
                          - Mathf.Max(bounds[a].min.z, bounds[b].min.z);
                    return (x > -0.6f && z > 1f) || (z > -0.6f && x > 1f);
                }
                foreach (var a in interiorCores)
                    foreach (var b in interiorCores)
                        if (a < b && CoreKey(a) == CoreKey(b) && EdgeContact(a, b))
                            parent[Find(a)] = Find(b);
                foreach (var tower in interiorCores.GroupBy(Find))
                {
                    var members = tower.ToList();
                    var rect = Rect.MinMaxRect(float.MaxValue, float.MaxValue,
                                               float.MinValue, float.MinValue);
                    foreach (var i in members)
                        rect = Rect.MinMaxRect(
                            Mathf.Min(rect.xMin, bounds[i].min.x - 0.5f),
                            Mathf.Min(rect.yMin, bounds[i].min.z - 0.5f),
                            Mathf.Max(rect.xMax, bounds[i].max.x + 0.5f),
                            Mathf.Max(rect.yMax, bounds[i].max.z + 0.5f));
                    groups.Add((members, new List<Rect> { rect }));
                }
            }

            if (groups.Count == 0)
                return new List<List<Transform>> { new(pieces) };

            // --- 4. deal every remaining piece to its best-overlapping building -------
            float Overlap(Rect a, Rect b)
            {
                var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                return w > 0f && h > 0f ? w * h : 0f;
            }
            foreach (var i in loose)
            {
                var rect = Rect.MinMaxRect(bounds[i].min.x, bounds[i].min.z,
                                           bounds[i].max.x, bounds[i].max.z);
                var best = 0;
                var bestScore = float.MinValue;
                for (var g = 0; g < groups.Count; g++)
                {
                    var score = groups[g].bands.Max(band => Overlap(rect, band));
                    if (score <= 0f)
                        // No overlap anywhere: fall back to (negative) distance so the
                        // piece still lands on the closest band rather than group 0.
                        score = -groups[g].bands.Min(band =>
                            (band.center - new Vector2(bounds[i].center.x, bounds[i].center.z))
                            .sqrMagnitude);
                    if (score > bestScore) { bestScore = score; best = g; }
                }
                groups[best].members.Add(i);
            }

            return groups
                .Select(g => g.members.Select(i => pieces[i]).ToList())
                .OrderBy(g => Mathf.Round(g.Min(t => t.position.z) / 10f))
                .ThenBy(g => g.Min(t => t.position.x)).ToList();
        }

        static string ColourOf(Transform piece)
        {
            foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material && material.name.StartsWith("PolygonCity_"))
                        return material.name;
            return null;
        }

        /// <summary>SM_Bld_Apartment_Stack_75 → Apartment; SM_Bld_OfficeOld_Large_Floor_03
        /// → OfficeOld_Large. Type is part of identity: an office tower is not the same
        /// building as the same-coloured apartments beside it.</summary>
        static string FamilyOf(string pieceName)
        {
            var family = pieceName.StartsWith("SM_Bld_") ? pieceName.Substring(7) : pieceName;
            bool stripped;
            do
            {
                stripped = false;
                var cut = family.LastIndexOf('_');
                if (cut < 0)
                    break;
                var tail = family.Substring(cut + 1);
                if (tail.All(char.IsDigit) || tail is "Stack" or "Corner" or "Floor" or "Base")
                {
                    family = family.Substring(0, cut);
                    stripped = true;
                }
            } while (stripped);
            return family;
        }

        /// <summary>One nightclub demo scene = one venue: Scene root + Roof_Layer bake
        /// together; Background_Layer, cameras, light rigs and the sky clouds stay
        /// behind.</summary>
        static void BakeVenue(string scenePath, string name, List<(string section, string name)> baked)
        {
            var demo = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var holder = new GameObject(name);
                try
                {
                    var found = false;
                    foreach (var rootName in new[] { "Scene", "Roof_Layer" })
                    {
                        var root = demo.GetRootGameObjects().FirstOrDefault(go => go.name == rootName);
                        if (!root)
                            continue;
                        found = true;
                        var copy = Object.Instantiate(root);
                        copy.transform.SetParent(holder.transform, worldPositionStays: true);
                    }
                    if (!found)
                    {
                        Debug.LogWarning($"[Catalog] '{scenePath}' has no Scene/Roof_Layer root - skipped.");
                        return;
                    }
                    StripClouds(holder);
                    SyntyKitExtractor.BakeGroup(holder, name, yaw: 0f, CatalogDir, CatalogMeshDir);
                    baked.Add((SectionClubs, name));
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Catalog] '{name}' failed to bake: {e.Message}");
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(demo, removeScene: true);
                SyntyBakeUtil.ClearCache();
            }
        }

        /// <summary>
        /// Sky dressing has no place in a building bake - the nightclub demos hang
        /// SM_Env_Cloud rings over every venue and the city demo scatters its own.
        /// Matched on "_Env_Cloud", NOT "Cloud": SM_Prop_Sign_White_Cloud_01 is a neon
        /// sign and stays.
        /// </summary>
        static void StripClouds(GameObject root)
        {
            var clouds = root.GetComponentsInChildren<Transform>(true)
                             .Where(t => t.name.Contains("_Env_Cloud")).ToList();
            foreach (var cloud in clouds)
                if (cloud)
                    Object.DestroyImmediate(cloud.gameObject);
        }

        /// <summary>The pack title standing left of its block's first row.</summary>
        static void SectionHeader(string title, Vector3 position)
        {
            var header = new GameObject($"{title} header");
            header.transform.position = position + new Vector3(0f, 18f, 0f);
            header.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

            var text = header.AddComponent<TextMesh>();
            text.text = title;
            text.fontSize = 96;
            text.characterSize = 0.8f;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = new Color(1f, 0.85f, 0.4f);
        }

        /// <summary>Two buildings can only meet at a party wall; a real building is at
        /// least this long along the row.</summary>
        const float MinSegmentLength = 7.5f;

        /// <summary>
        /// Splits a flat street-row shell into per-building piece lists, or a single list
        /// when no split is found.
        ///
        /// v1 cut wherever the dominant wall MATERIAL changed and shredded buildings -
        /// storefront ground floors, trim bands and mirrored variants all flip materials
        /// WITHIN one facade. v2 cuts on physics instead: where two buildings in a row
        /// meet there is a PARTY WALL - side-wall pieces standing PERPENDICULAR to the row
        /// axis, stacked one per storey at one coordinate in the row's interior. A cut
        /// needs such a stack (3+ perpendicular wall pieces within 1.25 m) AND a real
        /// change across it (roofline height by more than 1.2 m, or the dominant wall
        /// material) - so a mid-building partition or a decorative return cannot slice
        /// anything. Precision over recall: an occasional merged pair beats confetti.
        /// </summary>
        static List<List<Transform>> SplitRow(Transform shell, string groupName)
        {
            var pieces = new List<Transform>();
            foreach (Transform child in shell)
                pieces.Add(child);
            if (pieces.Count < 40)
                return new List<List<Transform>> { pieces };

            Bounds all = default;
            var first = true;
            foreach (var r in shell.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { all = r.bounds; first = false; }
                else all.Encapsulate(r.bounds);
            }
            var alongX = all.size.x >= all.size.z;
            float Project(Vector3 p) => alongX ? p.x : p.z;
            var min = Project(all.min);
            var length = alongX ? all.size.x : all.size.z;

            // Wall pieces, tagged with projection, height and orientation. A wall's face
            // is its local +Z; it stands perpendicular to the row when that face points
            // ALONG the axis (yaw 90/270 for an x row, 0/180 for a z row).
            var wallProj = new List<(float p, float top, string mat, bool perpendicular)>();
            foreach (var piece in pieces)
            {
                if (!piece.name.Contains("_Wall_"))
                    continue;
                var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                if (!renderer || !renderer.sharedMaterial)
                    continue;

                var yaw = Mathf.RoundToInt(Mathf.Repeat(piece.eulerAngles.y, 360f) / 90f) * 90 % 360;
                var faceAlongAxis = alongX ? (yaw == 90 || yaw == 270) : (yaw == 0 || yaw == 180);
                wallProj.Add((Project(renderer.bounds.center), renderer.bounds.max.y,
                              renderer.sharedMaterial.name, faceAlongAxis));
            }
            if (wallProj.Count == 0)
                return new List<List<Transform>> { pieces };

            // Candidate cuts: interior clusters of perpendicular walls (the party stacks).
            var perpendicular = wallProj.Where(w => w.perpendicular)
                                        .Select(w => w.p)
                                        .Where(p => p > min + MinSegmentLength &&
                                                    p < min + length - MinSegmentLength)
                                        .OrderBy(p => p).ToList();
            var candidates = new List<float>();
            for (var i = 0; i < perpendicular.Count;)
            {
                var start = i;
                while (i < perpendicular.Count && perpendicular[i] - perpendicular[start] <= 1.25f)
                    i++;
                if (i - start >= 3)
                    candidates.Add(perpendicular.Skip(start).Take(i - start).Average());
            }

            // Keep a candidate only if the row genuinely CHANGES across it.
            var cuts = new List<float>();
            foreach (var cut in candidates)
            {
                var left = wallProj.Where(w => !w.perpendicular && w.p < cut && w.p > cut - 6f).ToList();
                var right = wallProj.Where(w => !w.perpendicular && w.p > cut && w.p < cut + 6f).ToList();
                if (left.Count == 0 || right.Count == 0)
                    continue;

                var heightChanged = Mathf.Abs(left.Max(w => w.top) - right.Max(w => w.top)) > 1.2f;
                string DominantMat(List<(float p, float top, string mat, bool perpendicular)> side) =>
                    side.GroupBy(w => w.mat).OrderByDescending(g => g.Count()).First().Key;
                var materialChanged = DominantMat(left) != DominantMat(right);

                if (heightChanged || materialChanged)
                    if (cuts.Count == 0 || cut - cuts[cuts.Count - 1] >= MinSegmentLength)
                        cuts.Add(cut);
            }
            if (cuts.Count == 0)
                return new List<List<Transform>> { pieces };

            var segments = new List<List<Transform>>();
            for (var i = 0; i <= cuts.Count; i++) segments.Add(new List<Transform>());
            foreach (var piece in pieces)
            {
                var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                var p = renderer ? Project(renderer.bounds.center) : Project(piece.position);
                var segment = 0;
                while (segment < cuts.Count && p >= cuts[segment]) segment++;
                segments[segment].Add(piece);
            }

            // A sliver on a boundary is not a building - fold it into its neighbour.
            for (var i = segments.Count - 1; i >= 0; i--)
            {
                if (segments[i].Count >= 20 || segments.Count == 1) continue;
                var neighbour = i > 0 ? i - 1 : 1;
                segments[neighbour].AddRange(segments[i]);
                segments.RemoveAt(i);
            }

            // The user's seam-ownership corrections, applied once the segments (and so the
            // letters) are final. Only pieces standing IN a party plane move; everything
            // else keeps its geometric side.
            if (segments.Count > 1 && SeamWallOwners.TryGetValue(groupName, out var owners))
            {
                var widenedNames = owners.Where(r => r.nameContains.Length > 0)
                                         .Select(r => r.nameContains).ToArray();
                for (var source = 0; source < segments.Count; source++)
                    foreach (var piece in segments[source].ToList())
                    {
                        // Wall furniture in the widest sense: the garage's terrace
                        // railing has no "_Wall" in its name, and a wall that changes
                        // sides must take its gutter pipes, doors and mounted props
                        // along or they float where the wall used to be.
                        // _Roof_Edge: the villa roof's tile-coped rim pieces stand in the
                        // party plane like a wall (Hotel_01's "visak" slab over D) - the
                        // seam log shows none anywhere they should not move.
                        if (!piece.name.Contains("_Wall") && !piece.name.Contains("_Railing") &&
                            !piece.name.Contains("_Pipe") && !piece.name.Contains("_Door") &&
                            !piece.name.Contains("_Prop_") && !piece.name.Contains("_Roof_Edge") &&
                            !widenedNames.Any(n => piece.name.Contains(n)))
                            continue;
                        var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                        if (!renderer || !renderer.sharedMaterial)
                            continue;
                        // Corner pieces WRAP the seam plane, so their bounds centre sits
                        // ~1.2 m off it - judge those by pivot instead.
                        var p = piece.name.Contains("Corner")
                            ? Project(piece.position)
                            : Project(renderer.bounds.center);
                        // The seam plane's own axis - pivot-based, because a piece's
                        // bounds can extend either way from its pivot.
                        var lateral = alongX ? piece.position.z : piece.position.x;
                        foreach (var rule in owners)
                        {
                            if (rule.cut >= cuts.Count ||
                                Mathf.Abs(p - cuts[rule.cut]) > 0.8f ||
                                !renderer.sharedMaterial.name.Contains(rule.matContains) ||
                                lateral < rule.lateralMin || lateral > rule.lateralMax ||
                                piece.position.y > rule.heightMax ||
                                (rule.nameContains.Length > 0 &&
                                 !piece.name.Contains(rule.nameContains)))
                                continue;
                            // A piece may only cross ONE boundary - a party wall always
                            // separates neighbours, so a farther move is a wrong rule.
                            var target = rule.segment - 'A';
                            if (target >= 0 && target < segments.Count &&
                                Mathf.Abs(target - source) == 1)
                            {
                                segments[source].Remove(piece);
                                segments[target].Add(piece);
                            }
                            break;
                        }
                    }
            }

            // "Produzi zid do poda": clone the matched wall's lowest authored storey at
            // the seam down to the ground, one storey pitch at a time. Only the
            // perpendicular plane itself - facade pieces continue down on their own.
            if (segments.Count > 1 && SeamWallFloorFills.TryGetValue(groupName, out var fills))
                foreach (var (matContains, letter) in fills)
                {
                    var target = letter - 'A';
                    if (target < 0 || target >= segments.Count)
                        continue;

                    var matched = new List<Transform>();
                    foreach (var piece in segments[target])
                    {
                        if (!piece.name.Contains("_Wall"))
                            continue;
                        var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                        if (!renderer || !renderer.sharedMaterial ||
                            !renderer.sharedMaterial.name.Contains(matContains))
                            continue;
                        var fillProj = piece.name.Contains("Corner")
                            ? Project(piece.position)
                            : Project(renderer.bounds.center);
                        if (!cuts.Any(cut => Mathf.Abs(fillProj - cut) <= 0.8f))
                            continue;
                        var pieceYaw = Mathf.RoundToInt(Mathf.Repeat(piece.eulerAngles.y, 360f) / 90f) * 90 % 360;
                        var inSeamPlane = alongX ? (pieceYaw == 90 || pieceYaw == 270)
                                                 : (pieceYaw == 0 || pieceYaw == 180);
                        if (inSeamPlane)
                            matched.Add(piece);
                    }
                    if (matched.Count == 0)
                        continue;

                    var storeys = matched.Select(m => Mathf.Round(m.position.y * 100f) / 100f)
                                         .Distinct().OrderBy(y => y).ToList();
                    var pitch = storeys.Count > 1
                        ? storeys.Zip(storeys.Skip(1), (a, b) => b - a).Min()
                        : 3f;
                    if (pitch < 0.5f)
                        pitch = 3f;

                    var floorY = shell.position.y;
                    foreach (var piece in matched.Where(m => Mathf.Abs(m.position.y - storeys[0]) < 0.011f))
                        for (var newY = storeys[0] - pitch; newY > floorY - 0.01f; newY -= pitch)
                        {
                            var clone = Object.Instantiate(piece, shell);
                            clone.name = piece.name;
                            clone.SetPositionAndRotation(
                                new Vector3(piece.position.x, newY, piece.position.z),
                                piece.rotation);
                            segments[target].Add(clone);
                        }
                }

            // Shared-wall borrows: the neighbour keeps its authored wall, this segment
            // gets a clone repainted into its own identity. See SeamWallBorrows.
            if (segments.Count > 1 && SeamWallBorrows.TryGetValue(groupName, out var borrows))
                foreach (var (cutIndex, matContains, letter, repaint) in borrows)
                {
                    var into = letter - 'A';
                    if (cutIndex >= cuts.Count || into >= segments.Count ||
                        (into != cutIndex && into != cutIndex + 1))
                        continue;
                    var source = into == cutIndex ? cutIndex + 1 : cutIndex;
                    if (source >= segments.Count)
                        continue;

                    // The receiving side's own coat of paint.
                    Material coat = null;
                    foreach (var piece in segments[into])
                    {
                        var r = piece.GetComponentInChildren<MeshRenderer>(true);
                        if (!r) continue;
                        foreach (var m in r.sharedMaterials)
                            if (m && m.name.Contains(repaint)) { coat = m; break; }
                        if (coat) break;
                    }

                    foreach (var piece in segments[source].ToList())
                    {
                        if (!piece.name.Contains("_Wall_") || piece.name.Contains("Trim") ||
                            piece.name.Contains("Railing"))
                            continue;
                        var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                        if (!renderer || !renderer.sharedMaterial ||
                            !renderer.sharedMaterial.name.Contains(matContains))
                            continue;
                        var borrowProj = piece.name.Contains("Corner")
                            ? Project(piece.position)
                            : Project(renderer.bounds.center);
                        if (Mathf.Abs(borrowProj - cuts[cutIndex]) > 0.8f)
                            continue;
                        var pieceYaw = Mathf.RoundToInt(Mathf.Repeat(piece.eulerAngles.y, 360f) / 90f) * 90 % 360;
                        var inSeamPlane = alongX ? (pieceYaw == 90 || pieceYaw == 270)
                                                 : (pieceYaw == 0 || pieceYaw == 180);
                        if (!inSeamPlane)
                            continue;

                        var clone = Object.Instantiate(piece, shell);
                        clone.name = piece.name;
                        clone.SetPositionAndRotation(piece.position, piece.rotation);
                        if (coat)
                            foreach (var r in clone.GetComponentsInChildren<MeshRenderer>(true))
                            {
                                var mats = r.sharedMaterials;
                                for (var m = 0; m < mats.Length; m++)
                                    mats[m] = coat;
                                r.sharedMaterials = mats;
                            }
                        segments[into].Add(clone);
                    }
                }

            // The seam census - every piece near every cut, with its final segment.
            if (cuts.Count > 0)
            {
                SeamLog.AppendLine($"=== {groupName}  axis {(alongX ? "x" : "z")}  cuts: " +
                                   string.Join(", ", cuts.Select(c => c.ToString("F2"))));
                for (var s = 0; s < segments.Count; s++)
                    foreach (var piece in segments[s])
                    {
                        var renderer = piece.GetComponentInChildren<MeshRenderer>(true);
                        if (!renderer)
                            continue;
                        var b = Project(renderer.bounds.center);
                        var pv = Project(piece.position);
                        if (!cuts.Any(c => Mathf.Abs(b - c) <= 1.5f || Mathf.Abs(pv - c) <= 1.5f))
                            continue;
                        var lat = alongX ? piece.position.z : piece.position.x;
                        SeamLog.AppendLine(
                            $"{(char)('A' + s)}  {piece.name,-44} y{piece.position.y,6:F2} " +
                            $"lat{lat,7:F2} pv{pv,7:F2} b{b,7:F2} yaw{Mathf.RoundToInt(piece.eulerAngles.y),4} " +
                            $"{(renderer.sharedMaterial ? renderer.sharedMaterial.name : "-")}");
                    }
            }

            return segments;
        }

        /// <summary>Name + measured footprint floating over the roof, readable from the camera.</summary>
        static void Label(GameObject instance, string name, Vector3 position)
        {
            var bounds = new Bounds(position, Vector3.zero);
            var first = true;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }

            var labelObject = new GameObject($"{name} label");
            labelObject.transform.position = new Vector3(
                position.x, bounds.max.y + 6f, position.z);
            labelObject.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

            var text = labelObject.AddComponent<TextMesh>();
            text.text = $"{name}\n{bounds.size.x:F0} x {bounds.size.z:F0} m, h {bounds.size.y:F0}";
            text.fontSize = 48;
            text.characterSize = 0.35f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
        }

        /// <summary>World bounds of a placed instance's renderers; pivot when it has none.</summary>
        static Bounds InstanceBounds(GameObject instance)
        {
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            var first = true;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(CatalogDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Catalog");
            if (!AssetDatabase.IsValidFolder(CatalogMeshDir))
                AssetDatabase.CreateFolder(CatalogDir, "Meshes");
        }
    }
}
