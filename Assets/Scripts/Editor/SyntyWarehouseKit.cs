using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The industrial stock the harbour was missing: the four buildings the Maps
    /// Military Warehouse pack assembles by hand in Scenes/Buildings.unity - a
    /// 40 x 32 m warehouse stepping from a nine-metre block down to a skylit hall,
    /// a smaller store with its clerestory windows, a depot garage under a roller
    /// door, and a yard shed - lifted into kit prefabs beside the rest of
    /// Assets/CityKit/Buildings.
    ///
    /// Extracted rather than kit-bashed because here the assembly already exists.
    /// The pack itself ships only pieces (skylights, roller and steel doors,
    /// walkway railings) that lean on the Generic pack's Base kit - 2.5 m wall
    /// modules, 3 m storeys - for walls, floors and roof caps; Buildings.unity is
    /// the one place Synty's own artists put them together. SyntyIndustrialKitBash
    /// builds the GangWarfare halls piece by piece for the opposite reason: that
    /// pack's demo assembles nothing.
    ///
    /// The Building subtree only, as everywhere else in the kit. Dressing is demo
    /// set-decoration and two thirds of it belongs to packs this project does not
    /// own (village walls, pipeline runs, rubble, fluorescent battens), so it would
    /// come through as holes; the shells themselves are all but complete. The demo
    /// forecourt slabs go as well - the harbour lays its own concrete - while road
    /// pieces INSIDE the wall line stay, because that is the warehouse floor.
    /// </summary>
    public static class SyntyWarehouseKit
    {
        const string BuildingsScene =
            "Assets/Synty/PolygonMapsMilitaryWarehouse/Scenes/Buildings.unity";

        /// <summary>Bumped whenever the mapping or the bake below changes; stored beside
        /// the output so Play does not reopen an 8 MB scene every time.</summary>
        public const int Version = 5;   // 2: the Military atlas material exists now (a grey stand-in), so doors, skylights and railings bake in
                                        // 3-5: shot-up wall/floor modules made whole (HealWalls), missing modules filled along the wall lines (FillGaps)
        const string VersionPath = SyntyKitExtractor.BuildingsDir + "/WarehouseKitVersion.txt";

        /// <summary>Demo root -> kit role. Each root holds Building, Dressing and
        /// (for the bigger two) Decals; only Building is taken.</summary>
        static readonly (string root, string role)[] Buildings =
        {
            ("Warehouse Large", "building-warehouse-large"),
            ("Warehouse", "building-warehouse-small"),
            ("Garage_01", "building-depot-garage"),
            ("Shed_01", "building-yard-shed"),
        };

        [MenuItem("Tools/City/Catalog/Rebuild Synty Warehouse Kit (Buildings)", priority = 5)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        // ------------------------------------------------------------ the workshop

        /// <summary>
        /// The hand-finished sheds. Tools/City/Catalog/Warehouse Workshop makes this
        /// scene once - the four Building groups lifted out of the demo, mended as far
        /// as the code can (forecourt stripped, shot-up walls made whole, missing
        /// modules filled), stood in a row, each root named for its kit role - and
        /// opens it; whatever is done to those roots by hand and saved is what the
        /// bake uses from then on, in place of the demo scene. A root missing from the
        /// workshop falls back to the demo. The bake re-runs whenever the scene file is
        /// newer than the last bake (the stamp carries its write time).
        /// </summary>
        public const string WorkshopScene = SyntyKitExtractor.BuildingsDir + "/WarehouseWorkshop.unity";
        const float WorkshopSpacing = 70f;

        static string Stamp()
        {
            long ticks = System.IO.File.Exists(WorkshopScene) ? System.IO.File.GetLastWriteTimeUtc(WorkshopScene).Ticks : 0;
            // the demo scene is edited by hand too (the user mends the shells there): a
            // newer demo re-bakes as well
            long demoTicks = System.IO.File.Exists(BuildingsScene) ? System.IO.File.GetLastWriteTimeUtc(BuildingsScene).Ticks : 0;
            return Version + "|" + ticks + "|" + demoTicks;
        }

        /// <summary>Whether the sheds on disk are this version's, baked from the workshop as it stands.</summary>
        public static bool IsFresh()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            return marker && marker.text.Trim() == Stamp();
        }

        [MenuItem("Tools/City/Catalog/Warehouse Workshop (open or create)", priority = 6)]
        public static void OpenWorkshop()
        {
            if (!System.IO.File.Exists(WorkshopScene))
            {
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(BuildingsScene))
                {
                    Debug.LogWarning("[SyntyWarehouseKit] " + BuildingsScene + " is missing - nothing to put in the workshop.");
                    return;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EnsureFolders();
                var workshop = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var demo = EditorSceneManager.OpenScene(BuildingsScene, OpenSceneMode.Additive);
                try
                {
                    int i = 0;
                    foreach (var (rootName, role) in Buildings)
                    {
                        var copy = LiftFromDemo(demo, rootName, role, mend: true);
                        if (copy == null) continue;
                        copy.transform.position = new Vector3(i * WorkshopSpacing, 0f, 0f) + copy.transform.position - Flat(copy.transform.position);
                        SceneManager.MoveGameObjectToScene(copy, workshop);
                        i++;
                    }
                    var note = new GameObject("README - the four roots are the harbour's sheds: fix them by hand, save, and the bake takes them from here");
                    SceneManager.MoveGameObjectToScene(note, workshop);
                }
                finally
                {
                    EditorSceneManager.CloseScene(demo, removeScene: true);
                }
                EditorSceneManager.SaveScene(workshop, WorkshopScene);
                AssetDatabase.ImportAsset(WorkshopScene);
                Debug.Log("[SyntyWarehouseKit] workshop made at " + WorkshopScene);
            }
            else
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(WorkshopScene, OpenSceneMode.Single);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static void BuildIfStale()
        {
            if (IsFresh()) return;
            bool haveWorkshop = System.IO.File.Exists(WorkshopScene);
            bool haveDemo = AssetDatabase.LoadAssetAtPath<SceneAsset>(BuildingsScene);
            if (!haveWorkshop && !haveDemo)
            {
                Debug.LogWarning("[SyntyWarehouseKit] " + BuildingsScene + " is missing - no industrial sheds baked.");
                return;
            }

            EnsureFolders();
            SyntyBakeUtil.ClearCache();
            var done = new HashSet<string>();
            if (haveWorkshop)
            {
                var workshop = EditorSceneManager.OpenScene(WorkshopScene, OpenSceneMode.Additive);
                try
                {
                    foreach (var (_, role) in Buildings)
                    {
                        var root = workshop.GetRootGameObjects().FirstOrDefault(go => go.name == role);
                        if (!root) continue;
                        var copy = Object.Instantiate(root);
                        copy.name = role;
                        Bake(copy, role, "workshop");
                        done.Add(role);
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(workshop, removeScene: true);
                }
            }
            if (haveDemo && done.Count < Buildings.Length)
            {
                var scene = EditorSceneManager.OpenScene(BuildingsScene, OpenSceneMode.Additive);
                try
                {
                    foreach (var (rootName, role) in Buildings)
                    {
                        if (done.Contains(role)) continue;
                        var copy = LiftFromDemo(scene, rootName, role, mend: true);
                        if (copy != null) Bake(copy, role, "demo");
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
            SyntyBakeUtil.ClearCache();

            System.IO.File.WriteAllText(VersionPath, Stamp());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.BuildingsDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Buildings");
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.BuildingsDir + "/Meshes"))
                AssetDatabase.CreateFolder(SyntyKitExtractor.BuildingsDir, "Meshes");
        }

        /// <summary>The demo's Building group for a root, copied out and - if asked -
        /// mended: forecourt stripped, shot-up walls made whole, missing modules filled.
        /// Null if the demo has no such root.</summary>
        static GameObject LiftFromDemo(Scene scene, string rootName, string role, bool mend)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == rootName);
            if (!root)
            {
                Debug.LogWarning($"[SyntyWarehouseKit] root '{rootName}' not found in {BuildingsScene}");
                return null;
            }
            var shell = root.transform.Find("Building");
            if (!shell)
            {
                Debug.LogWarning($"[SyntyWarehouseKit] '{rootName}' has no Building group");
                return null;
            }
            var copy = Object.Instantiate(shell.gameObject);
            copy.name = role;
            copy.transform.position = shell.position;
            copy.transform.rotation = shell.rotation;
            if (mend)
            {
                var slabs = StripForecourt(copy.transform);
                // (FillGaps is off: the shells are mended by hand in the demo scene now, and
                // a guess at a missing module would undo that)
                var healed = HealWalls(copy.transform);
                Debug.Log($"[SyntyWarehouseKit] {rootName} -> {role}:" +
                          (slabs > 0 ? $" {slabs} forecourt slab(s) dropped," : "") +
                          (healed > 0 ? $" {healed} wall(s) made good," : "") + " lifted");
            }
            return copy;
        }

        /// <summary>The group baked into the kit prefab and destroyed.</summary>
        static void Bake(GameObject copy, string role, string source)
        {
            var yaw = MeasureLoadingFrontYaw(copy, out var report);
            Debug.Log($"[SyntyWarehouseKit] {role} ({source}): loading front {report}");
            SyntyKitExtractor.BakeGroup(copy, role, yaw);
            Object.DestroyImmediate(copy);
        }
        // ------------------------------------------------------------ forecourt

        /// <summary>
        /// Drops the demo's ground slabs from around the building. Synty floors these
        /// halls with the Generic pack's road pieces and then runs the same pieces on
        /// out to make a forecourt and a truck apron; the floor is architecture and
        /// stays, the forecourt is the demo's ground and would fight the harbour's own
        /// concrete for the same plane. The wall line decides which is which.
        /// </summary>
        static int StripForecourt(Transform group)
        {
            var walls = new Bounds();
            var measured = false;
            foreach (var t in group.GetComponentsInChildren<Transform>(true))
            {
                var n = t.gameObject.name;
                if (!n.Contains("_Wall") && !n.Contains("Pillar")) continue;
                if (!TryBounds(t, out var wb)) continue;
                if (measured) walls.Encapsulate(wb);
                else { walls = wb; measured = true; }
            }
            if (!measured) return 0;
            walls.Expand(new Vector3(4f, 4000f, 4f));   // two metres of slack each side

            var dropped = 0;
            foreach (var t in group.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (!t || !t.gameObject.name.StartsWith("SM_Gen_Env_Road")) continue;
                if (!TryBounds(t, out var b)) continue;
                if (walls.Contains(new Vector3(b.center.x, walls.center.y, b.center.z))) continue;
                Object.DestroyImmediate(t.gameObject);
                dropped++;
            }
            return dropped;
        }

        // ------------------------------------------------------------ walls

        const string WholeWall = "Assets/Synty/PolygonGeneric/Prefabs/Base/SM_Bld_Base_Wall_01.prefab";
        const string WholeFloor = "Assets/Synty/PolygonGeneric/Prefabs/Base/SM_Bld_Base_Floor_01.prefab";

        /// <summary>
        /// The demo's sheds are a war map's: a wall module here and there is the pack's
        /// shot-up one, a crater through it, a floor slab a hole. A working port's sheds
        /// are merely old. The pack has no whole version of those pieces of its own -
        /// its walls ARE the Generic base kit's 2.5 m wall module, the destroyed ones a
        /// cut-up copy of it - so every destroyed wall (the pack's, the village kit's,
        /// the base kit's own) becomes the base kit's whole wall on the same transform,
        /// and every destroyed floor the base kit's whole floor; and each takes its
        /// materials off the nearest whole module of its kind in the same building, so
        /// the patch is in the wall's own brick or iron and not the kit's default.
        /// Returns how many were swapped.
        /// </summary>
        static int HealWalls(Transform group)
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(WholeWall);
            var floor = AssetDatabase.LoadAssetAtPath<GameObject>(WholeFloor);
            var all = group.GetComponentsInChildren<Transform>(true).ToArray();
            var healed = 0;
            foreach (var t in all)
            {
                if (!t) continue;
                var n = t.gameObject.name;
                bool isWall = n.Contains("Wall_Destroyed"), isFloor = n.Contains("Floor_Destroyed");
                if (!isWall && !isFloor) continue;
                var whole = isWall ? wall : floor;
                if (!whole) continue;

                var fresh = Object.Instantiate(whole, t.parent);
                fresh.transform.localPosition = t.localPosition;
                fresh.transform.localRotation = t.localRotation;
                fresh.transform.localScale = t.localScale;
                fresh.name = whole.name;

                // the nearest whole module of the same kind lends its materials
                var donor = Nearest(all, t.position, isWall ? "SM_Bld_Base_Wall_01" : "SM_Bld_Base_Floor", t);
                var donorMr = donor ? donor.GetComponentInChildren<MeshRenderer>() : null;
                var freshMr = fresh.GetComponentInChildren<MeshRenderer>();
                if (donorMr && freshMr && donorMr.sharedMaterials.Length == freshMr.sharedMaterials.Length)
                    freshMr.sharedMaterials = donorMr.sharedMaterials;
                else if (donorMr && freshMr && donorMr.sharedMaterials.Length > 0)
                {
                    var mats = freshMr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = donorMr.sharedMaterials[0];
                    freshMr.sharedMaterials = mats;
                }
                Object.DestroyImmediate(t.gameObject);
                healed++;
            }
            return healed;
        }

        const string WholeTrim = "Assets/Synty/PolygonGeneric/Prefabs/Base/SM_Bld_Base_Wall_Trim_01.prefab";

        /// <summary>
        /// The other kind of hole: a module that is simply not there. The demo mixes in
        /// walls from packs this project does not own (the village kit's), and those
        /// instances load as empty "Missing Prefab" dummies - a gap in the wall line with
        /// the roof still on. Along every wall line, storey by storey, a module whose
        /// neighbour has nothing where its next module should stand - while the line goes
        /// on beyond - and an empty dummy standing about there, is a gap, and gets the
        /// base kit's whole wall in the neighbour's orientation and materials. The parapet
        /// trim the same way with the trim module. Runs until nothing is left to fill.
        /// </summary>
        static int FillGaps(Transform group)
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(WholeWall);
            var trim = AssetDatabase.LoadAssetAtPath<GameObject>(WholeTrim);
            var dummies = group.GetComponentsInChildren<Transform>(true)
                .Where(t => t.gameObject.name.Contains("Missing Prefab")).Select(t => t.position).ToList();
            if (dummies.Count == 0) return 0;
            int filled = 0;
            filled += FillFamily(group, wall, dummies, 1.5f,
                n => (n.Contains("_Wall") || n.Contains("_Door") || n.Contains("Window") || n.Contains("Pillar")) && !n.Contains("Trim") && !n.Contains("Destroyed"),
                n => n.StartsWith("SM_Bld_Base_Wall_01"));
            filled += FillFamily(group, trim, dummies, 0.15f,
                n => n.Contains("Wall_Trim"),
                n => n.StartsWith("SM_Bld_Base_Wall_Trim_01"));
            Debug.Log($"[SyntyWarehouseKit] {group.name}: {dummies.Count} missing-prefab dummies, {filled} gap(s) filled");
            return filled;
        }

        /// <summary>One family of wall-line modules (walls, or trims): the gap search and
        /// fill described above. <paramref name="probeY"/> is the height above a module's
        /// foot at which its neighbour is probed (mid-wall, mid-trim).</summary>
        static int FillFamily(Transform group, GameObject whole, List<Vector3> dummies, float probeY,
                              System.Func<string, bool> isMember, System.Func<string, bool> isSeed)
        {
            if (!whole) return 0;
            const float Module = 2.5f;
            int filled = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                var members = group.GetComponentsInChildren<Transform>(true)
                    .Where(t => isMember(t.gameObject.name))
                    .Select(t => (t, ok: TryBounds(t, out var b), b))
                    .Where(m => m.ok).ToList();
                bool Covered(Vector3 p)
                {
                    foreach (var m in members)
                    {
                        var b = m.b; b.Expand(0.25f);
                        if (b.Contains(p)) return true;
                    }
                    return false;
                }
                bool any = false;
                foreach (var m in members.ToArray())
                {
                    if (!isSeed(m.t.gameObject.name)) continue;
                    // which way this module runs off its pivot
                    var right = m.t.right; right.y = 0f;
                    if (right.sqrMagnitude < 0.5f) continue;
                    right.Normalize();
                    float side = Mathf.Sign(Vector3.Dot(m.b.center - m.t.position, right));
                    if (side == 0f) side = 1f;
                    var dir = right * side;
                    foreach (float s in new[] { 1f, -1f })
                    {
                        var d = dir * s;
                        // the slot beyond (or before): its pivot and its middle
                        var foot = m.t.position + d * Module;
                        var mid = foot + dir * (Module * 0.5f) + Vector3.up * probeY;
                        if (Covered(mid)) continue;
                        // the line must go on past the hole: a module or a corner pillar within ten metres beyond
                        bool goesOn = false;
                        for (int k = 1; k <= 8 && !goesOn; k++)
                            goesOn = Covered(mid + d * (Module * 0.5f) * k);
                        if (!goesOn) continue;
                        // and an empty dummy about the hole - the evidence it was a module once
                        bool dummyNear = dummies.Any(p => (p - foot).sqrMagnitude < 2.5f * 2.5f || (p - mid).sqrMagnitude < 2.5f * 2.5f);
                        if (!dummyNear) continue;

                        var fresh = Object.Instantiate(whole, m.t.parent);
                        fresh.transform.position = foot;
                        fresh.transform.rotation = m.t.rotation;
                        fresh.transform.localScale = m.t.localScale;
                        fresh.name = whole.name;
                        var donorMr = m.t.GetComponentInChildren<MeshRenderer>();
                        var freshMr = fresh.GetComponentInChildren<MeshRenderer>();
                        if (donorMr && freshMr && donorMr.sharedMaterials.Length == freshMr.sharedMaterials.Length)
                            freshMr.sharedMaterials = donorMr.sharedMaterials;
                        filled++;
                        any = true;
                        break;      // members have changed: start the pass again
                    }
                    if (any) break;
                }
                if (!any) break;
            }
            return filled;
        }

        static Transform Nearest(Transform[] all, Vector3 at, string namePrefix, Transform not)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var t in all)
            {
                if (!t || t == not || !t.gameObject.name.StartsWith(namePrefix)) continue;
                if (t.gameObject.name.Contains("Destroyed")) continue;
                float d = (t.position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        // ------------------------------------------------------------ front

        /// <summary>
        /// Which way the building is loaded, as the yaw SyntyKitExtractor.BakeGroup
        /// wants (it turns the group by minus this to bring that side onto +Z).
        ///
        /// A shed's front is where the lorries back up - the roller doors and the big
        /// garage panels - not where the doors are most numerous: these are map
        /// interiors, and the personnel doors inside outvote the loading bay several
        /// times over. So the vote is taken over the loading pieces alone, in tiers
        /// (roller and garage panels first, then the double-wide doorways, then plain
        /// large doorways), and each vote is weighted by how near its piece stands to
        /// the footprint edge it votes for: a piece ON the facade counts for several
        /// times a partition door five metres inside.
        /// </summary>
        static float MeasureLoadingFrontYaw(GameObject group, out string report)
        {
            var footprint = new Bounds();
            var measured = false;
            foreach (var r in group.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (measured) footprint.Encapsulate(r.bounds);
                else { footprint = r.bounds; measured = true; }
            }
            if (!measured)
            {
                report = "no geometry, assuming +Z";
                return 0f;
            }

            string[][] tiers =
            {
                new[] { "Door_Roller", "Wall_Garage_Large" },
                new[] { "Wall_Door_Double_Large" },
                new[] { "Wall_Door_Large", "Mil_Door_Large" },
            };
            var sides = new[] { "+Z", "+X", "-Z", "-X" };
            var yaws = new[] { 0f, 90f, 180f, -90f };

            foreach (var tier in tiers)
            {
                var score = new float[4];
                var found = 0;
                foreach (var t in group.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.gameObject.name;
                    if (!tier.Any(key => n.Contains(key))) continue;
                    if (!TryBounds(t, out var b)) continue;

                    var p = b.center;
                    var gap = new[]
                    {
                        footprint.max.z - p.z, footprint.max.x - p.x,
                        p.z - footprint.min.z, p.x - footprint.min.x,
                    };
                    var near = 0;
                    for (var i = 1; i < 4; i++)
                        if (gap[i] < gap[near]) near = i;
                    score[near] += 1f / (0.5f + Mathf.Max(0f, gap[near]));
                    found++;
                }
                if (found == 0) continue;

                var best = 0;
                for (var i = 1; i < 4; i++)
                    if (score[i] > score[best]) best = i;
                report = $"{sides[best]} on {string.Join("/", tier)} " +
                         $"({found} piece(s), scores " +
                         string.Join(" ", sides.Select((s, i) => $"{s}:{score[i]:F1}")) + ")";
                return yaws[best];
            }

            report = "no loading door found, assuming +Z";
            return 0f;
        }

        static bool TryBounds(Transform piece, out Bounds bounds)
        {
            bounds = default;
            var first = true;
            foreach (var r in piece.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return !first;
        }
    }
}
