using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The common, editor-only reading of the fourteen harvested residential prefabs.
    /// Their root children are the authored Synty pieces; no hierarchy is inferred because
    /// the harvest deliberately bakes those pieces as flat nested prefab instances.
    /// </summary>
    internal static class ResidentialForgeSource
    {
        internal const string ResidentialDir = "Assets/Prefabs/Residential";
        internal const string BuildingsDir = "Assets/Synty/PolygonCity/Prefabs/Buildings";
        internal const string PropsDir = "Assets/Synty/PolygonCity/Prefabs/Props";

        internal static readonly string[] UnitNames =
        {
            "residential-01", "residential-02", "residential-03", "residential-04",
            "residential-05", "residential-06", "residential-07", "residential-08",
            "residential-10", "residential-11", "residential-12", "residential-13",
            "residential-15", "residential-16",
        };

        internal sealed class Piece
        {
            internal Transform Transform;
            internal string Name;
            internal string Path;
            internal int ChildIndex;
            internal Bounds Box;
            internal Vector3 Position;
            internal float Yaw;
        }

        internal static string UnitPath(string name) => $"{ResidentialDir}/{name}.prefab";

        internal static GameObject OpenUnit(string name)
        {
            string path = UnitPath(name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException($"Residential forge source is missing: {path}");
            return PrefabUtility.LoadPrefabContents(path);
        }

        internal static void CloseUnit(GameObject root)
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }

        internal static List<Piece> Pieces(GameObject root)
        {
            var pieces = new List<Piece>(root.transform.childCount);
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (string.IsNullOrEmpty(path) || path == AssetDatabase.GetAssetPath(root))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    if (source != null) path = AssetDatabase.GetAssetPath(source);
                }

                string name = string.IsNullOrEmpty(path)
                    ? child.name
                    : Path.GetFileNameWithoutExtension(path);
                pieces.Add(new Piece
                {
                    Transform = child,
                    Name = name,
                    Path = path ?? string.Empty,
                    ChildIndex = i,
                    Box = BoundsIn(root.transform, child.gameObject),
                    Position = root.transform.InverseTransformPoint(child.position),
                    Yaw = NormalYaw(child.eulerAngles.y - root.transform.eulerAngles.y),
                });
            }
            return pieces;
        }

        internal static IEnumerable<string> DirectSourcePaths()
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string unit in UnitNames)
            {
                GameObject root = null;
                try
                {
                    root = OpenUnit(unit);
                    foreach (var piece in Pieces(root))
                        if (!string.IsNullOrEmpty(piece.Path)) paths.Add(piece.Path);
                }
                finally { CloseUnit(root); }
            }
            return paths;
        }

        internal static string FindPrefab(string exactName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{exactName} t:Prefab",
                         new[] { BuildingsDir, PropsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), exactName,
                                  StringComparison.Ordinal)) return path;
            }
            return string.Empty;
        }

        internal static Bounds BoundsIn(Transform frame, GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds box = default;
            bool any = false;
            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer) continue;
                Bounds world = renderer.bounds;
                Vector3 min = world.min, max = world.max;
                for (int mask = 0; mask < 8; mask++)
                {
                    var point = new Vector3(
                        (mask & 1) == 0 ? min.x : max.x,
                        (mask & 2) == 0 ? min.y : max.y,
                        (mask & 4) == 0 ? min.z : max.z);
                    point = frame.InverseTransformPoint(point);
                    if (any) box.Encapsulate(point);
                    else { box = new Bounds(point, Vector3.zero); any = true; }
                }
            }
            return any ? box : new Bounds(frame.InverseTransformPoint(go.transform.position), Vector3.zero);
        }

        internal static float BoundsDistance(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        internal static float NormalYaw(float yaw)
        {
            yaw = Mathf.Repeat(yaw, 360f);
            if (yaw > 359.995f) yaw = 0f;
            return Round(yaw);
        }

        internal static float Round(float value) => Mathf.Round(value * 1000f) * 0.001f;

        internal static int SuffixNumber(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2) return 0;
            int at = name.Length - 2;
            return int.TryParse(name.Substring(at, 2), out int value) ? value : 0;
        }

        internal static bool IsApartmentFacade(string name) =>
            name.StartsWith("SM_Bld_Apartment_", StringComparison.Ordinal) &&
            !name.Contains("_Roof_") && !name.Contains("_Door_") &&
            !name.Contains("_Stairs_");

        internal static bool IsShellAnchor(string name) =>
            IsApartmentFacade(name) ||
            name.StartsWith("SM_Bld_Apartment_Door_", StringComparison.Ordinal) &&
            !name.Contains("_Corner_") ||
            name.StartsWith("SM_Bld_Apartment_Roof_", StringComparison.Ordinal) ||
            name.StartsWith("SM_Bld_Shop_", StringComparison.Ordinal) &&
            !name.StartsWith("SM_Bld_Shop_Cover_", StringComparison.Ordinal);

        internal static int FloorsFromName(string name)
        {
            if (name.Contains("_Stack_")) return 3;
            return IsApartmentFacade(name) ? 1 : 0;
        }

        internal static string Float(float value) =>
            Round(value).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "f";

        internal static string Quote(string value) =>
            "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
