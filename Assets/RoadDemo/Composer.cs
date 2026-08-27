using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The hands every composer works with: how a prefab is raised, how big it really is,
    /// how it is set down to cover a rectangle or sit on its own underside, and which
    /// ground has already been spoken for.
    ///
    /// Written once for the park (<see cref="ParkBlocks"/>) and lifted out when the
    /// promenade (<c>QuayBlocks</c>) wanted every one of them: the same bargain
    /// <see cref="IndustrialBlocks"/> struck - one delegate says how a prefab is raised
    /// (the editor wants <c>PrefabUtility.InstantiatePrefab</c> so a bake keeps its links,
    /// the game a plain <c>Instantiate</c>) and nothing here knows which of the two
    /// called it. A composer opens with <see cref="Begin"/>, which takes the delegate and
    /// clears the ground, and works through <c>using static RoadDemo.Composer</c>.
    ///
    /// EVERYTHING IS PLACED BY MEASURING the turned instance rather than by reasoning about
    /// its pivot: pack tiles pivot at a corner, fence panels at one end, props in the
    /// middle, and which corner or which end changes with the turn.
    /// </summary>
    public static class Composer
    {
        public const float Cell = 5f;

        // ------------------------------------------------------------------- the raiser

        static Func<GameObject, Transform, GameObject> _raise;
        static readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();
        static readonly List<string> Absent = new List<string>();

        /// <summary>Prefabs the project has not got, gathered while composing so a caller can
        /// say so once rather than a hundred times.</summary>
        public static IReadOnlyList<string> Missing => Absent;

        public static void ForgetMissing() => Absent.Clear();

        /// <summary>Ground already spoken for, so nothing is set down twice.</summary>
        internal static readonly List<Rect> Taken = new List<Rect>();
        internal static readonly Dictionary<string, int> Refused = new Dictionary<string, int>();

        /// <summary>Opens a composition: takes the raiser and clears the ground.</summary>
        public static void Begin(Func<GameObject, Transform, GameObject> raise)
        {
            _raise = raise;
            Taken.Clear();
            Refused.Clear();
        }

        /// <summary>A prefab's own box, measured once through an INSTANCE and remembered - a
        /// prefab asset reports its renderers in local space, and the root scaling every
        /// Synty pack relies on is only applied once the thing is standing.</summary>
        internal static Bounds Box(string path)
        {
            if (Measured.TryGetValue(path, out var known)) return known;

            var box = new Bounds(Vector3.zero, Vector3.one);
            var asset = DemoAssetLoad.Load<GameObject>(path);
            if (asset == null)
            {
                if (!Absent.Contains(path)) Absent.Add(path);
                return box;                                   // not remembered: an import may fix it
            }

            var go = _raise(asset, null);
            if (go == null) { Measured[path] = box; return box; }
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (WorldBox(go, out var world)) box = world;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            Measured[path] = box;
            return box;
        }

        internal static bool WorldBox(GameObject go, out Bounds box)
        {
            box = default;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            box = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return true;
        }

        internal static int Quarter(float yaw) => ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;

        internal static bool Turned(float yaw) => Quarter(yaw) % 2 == 1;

        internal static Vector2 Foot(string path, float yaw)
        {
            var size = Box(path).size;
            return Turned(yaw) ? new Vector2(size.z, size.x) : new Vector2(size.x, size.z);
        }

        internal static GameObject Raise(string path, Transform parent)
        {
            var asset = DemoAssetLoad.Load<GameObject>(path);
            if (asset == null)
            {
                if (!Absent.Contains(path)) Absent.Add(path);
                return null;
            }
            return _raise(asset, parent);
        }

        /// <summary>
        /// Lays a piece so its footprint covers exactly the rectangle asked for.
        /// </summary>
        internal static GameObject Lay(string path, Transform parent, float minX, float minZ,
                                       float sizeX, float sizeZ, float yaw, float y = 0f)
        {
            var go = Raise(path, parent);
            if (go == null) return null;

            var own = Box(path).size;
            if (own.x > 0.001f && own.z > 0.001f)
            {
                var factor = Turned(yaw)
                    ? new Vector3(sizeZ / own.x, 1f, sizeX / own.z)
                    : new Vector3(sizeX / own.x, 1f, sizeZ / own.z);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, Whole(factor));
            }

            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (WorldBox(go, out var box))
                go.transform.position = new Vector3(minX - box.min.x, y, minZ - box.min.z);
            else
                go.transform.position = new Vector3(minX, y, minZ);
            return go;
        }

        static Vector3 Whole(Vector3 factor) => new Vector3(
            Mathf.Abs(factor.x - 1f) < 0.005f ? 1f : factor.x,
            Mathf.Abs(factor.y - 1f) < 0.005f ? 1f : factor.y,
            Mathf.Abs(factor.z - 1f) < 0.005f ? 1f : factor.z);

        /// <summary>A tile on its cell, at its own size and turned - no stretching, because
        /// every one of these IS a cell.</summary>
        internal static GameObject Tile(string path, Transform parent, int i, int j, float yaw) =>
            Lay(path, parent, i * Cell, j * Cell, Cell, Cell, yaw);

        /// <summary>Sits a prop on its own underside. Synty pivots furniture at its middle as
        /// often as at its feet, so a bench dropped by its pivot is as likely to be buried to
        /// the seat as standing on the grass.</summary>
        internal static GameObject Sit(string path, Transform parent, float x, float z, float yaw,
                                       float y = 0f)
        {
            var go = Raise(path, parent);
            if (go == null) return null;

            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (!WorldBox(go, out var box)) { go.transform.position = new Vector3(x, y, z); return go; }
            go.transform.position = new Vector3(x - box.center.x, y - box.min.y, z - box.center.z);
            return go;
        }

        /// <summary>Stands a piece that carries its own floor - the skatepark bowl, the
        /// toilet block - keeping the level it was baked at.</summary>
        internal static GameObject Stand(string path, Transform parent, float x, float z, float yaw)
        {
            var go = Raise(path, parent);
            if (go == null) return null;

            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (!WorldBox(go, out var box)) { go.transform.position = new Vector3(x, 0f, z); return go; }
            go.transform.position = new Vector3(x - box.center.x, 0f, z - box.center.z);
            return go;
        }

        internal static string Any(string[] of, System.Random rng) => of[rng.Next(of.Length)];

        internal static float Between(System.Random rng, float a, float b) =>
            a + (float)rng.NextDouble() * (b - a);

        internal static bool Chance(System.Random rng, double odds) => rng.NextDouble() < odds;

        // -------------------------------------------------------------------- the ground

        internal static bool Room(Rect want)
        {
            foreach (var taken in Taken) if (taken.Overlaps(want)) return false;
            return true;
        }

        internal static void Claim(Rect what) => Taken.Add(what);

        /// <summary>A prop, if there is room for it - refused rather than crammed in, and the
        /// refusal counted. A place reads as a place because things are set down where they
        /// fit.</summary>
        internal static GameObject Prop(string path, Transform parent, float x, float z, float yaw,
                                        float room = 1f, float y = 0f)
        {
            if (!Book(path, x, z, yaw, room, out var where)) return null;
            var go = Sit(path, parent, x, z, yaw, y);
            if (go != null) Claim(where);
            return go;
        }

        /// <summary>A building stood the way <see cref="Stand"/> stands it - at the level it
        /// was baked at, never lifted to its underside (a diner with a sunken floor stands
        /// on the ground, not 1.5 m in the air) - with its foot claimed like a prop's.</summary>
        internal static GameObject Building(string path, Transform parent, float x, float z, float yaw,
                                            float room = 1f)
        {
            if (!Book(path, x, z, yaw, room, out var where)) return null;
            var go = Stand(path, parent, x, z, yaw);
            if (go != null) Claim(where);
            return go;
        }

        static readonly Dictionary<string, float> Fronts = new Dictionary<string, float>();

        /// <summary>The yaw that turns a baked building's FRONT to +x, measured off its mesh
        /// once (<see cref="FacadeFinder"/>) and remembered - never assumed from the file.
        /// To face the building −z, +x, +z or −x (a block's south, east, north, west), add
        /// 90, 0, 270 or 180 - a yaw is a clockwise turn seen from above.</summary>
        internal static float FrontYaw(string path)
        {
            if (Fronts.TryGetValue(path, out float known)) return known;
            float yaw = 90f;
            var go = Raise(path, null);
            if (go != null)
            {
                try
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var front = FacadeFinder.FrontOf(go, out _);
                    // a storefront's front is where its glass is: the coffee shop's windows
                    // and door are all on one side, and FacadeFinder read its blank flank
                    // as the front (the user, 2026-08-27: "prednja strana kafea mora uvek da
                    // je ka ulici"). Where a mesh carries glass, the glass decides
                    if (GlassSide(go, out var glazed)) front = glazed;
                    yaw = FacadeFinder.YawToPlusZ(front) + 90f;
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Fronts[path] = yaw;
            return yaw;
        }

        /// <summary>The side of the instance that carries most of its glass, when one side
        /// clearly does: the shop window and the door. False when there is no glass or it
        /// is spread round the building.</summary>
        static bool GlassSide(GameObject go, out FacadeFinder.Side side)
        {
            side = FacadeFinder.Side.PlusZ;
            if (!WorldBox(go, out var box)) return false;
            var tally = new int[4];               // PlusZ, PlusX, MinusZ, MinusX
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var drawn = mf.GetComponent<Renderer>();
                if (mesh == null || drawn == null || !mesh.isReadable) continue;
                var mats = drawn.sharedMaterials;
                var verts = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (s >= mats.Length || mats[s] == null ||
                        mats[s].name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (int i in mesh.GetTriangles(s))
                    {
                        var at = mf.transform.TransformPoint(verts[i]) - box.center;
                        if (at.z > 0.3f * box.extents.z) tally[0]++;
                        if (at.x > 0.3f * box.extents.x) tally[1]++;
                        if (at.z < -0.3f * box.extents.z) tally[2]++;
                        if (at.x < -0.3f * box.extents.x) tally[3]++;
                    }
                }
            }
            int best = 0;
            for (int k = 1; k < 4; k++) if (tally[k] > tally[best]) best = k;
            int next = 0;
            for (int k = 0; k < 4; k++) if (k != best && tally[k] > next) next = tally[k];
            if (tally[best] == 0 || tally[best] < next * 2) return false;
            side = (FacadeFinder.Side)best;
            return true;
        }

        /// <summary>Is there room for the piece's foot, with this much air round it? A
        /// refusal is counted against the piece's name.</summary>
        static bool Book(string path, float x, float z, float yaw, float room, out Rect where)
        {
            var foot = Foot(path, yaw);
            where = new Rect(x - foot.x * 0.5f * room, z - foot.y * 0.5f * room,
                             Mathf.Max(0.4f, foot.x * room), Mathf.Max(0.4f, foot.y * room));
            if (Room(where)) return true;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            Refused[name] = Refused.TryGetValue(name, out var seen) ? seen + 1 : 1;
            return false;
        }

        /// <summary>The four things most often refused for want of room, in a line.</summary>
        internal static string Worst()
        {
            if (Refused.Count == 0) return "";
            var worst = Refused.OrderByDescending(one => one.Value)
                               .ThenBy(one => one.Key, StringComparer.Ordinal).Take(4)
                               .Select(one => $"{one.Key} x{one.Value}");
            return string.Join(", ", worst);
        }
    }
}
