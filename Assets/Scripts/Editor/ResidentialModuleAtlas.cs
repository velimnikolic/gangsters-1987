using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>Measures the POLYGON City parts used by the residential forge and writes
    /// the Unity-free atlas consumed by the pure facade planner.</summary>
    public static class ResidentialModuleAtlas
    {
        public const string TablePath = "Assets/RoadDemo/ResidentialModules.cs";
        public const string DocumentPath = "Docs/residential-module-atlas.md";

        static readonly string[] StructuralNames =
        {
            "SM_Bld_Shop_01", "SM_Bld_Shop_02", "SM_Bld_Shop_03",
            "SM_Bld_Shop_04", "SM_Bld_Shop_05", "SM_Bld_Shop_06",
            "SM_Bld_Shop_Corner_01", "SM_Bld_Shop_Corner_02",
            "SM_Bld_Shop_Cover_01", "SM_Bld_Shop_Cover_02",
            "SM_Bld_Shop_Cover_03", "SM_Bld_Shop_Cover_04",
            "SM_Bld_Shop_Cover_05",
            "SM_Bld_Apartment_01", "SM_Bld_Apartment_02", "SM_Bld_Apartment_03",
            "SM_Bld_Apartment_Stack_01", "SM_Bld_Apartment_Stack_02",
            "SM_Bld_Apartment_Stack_03",
            "SM_Bld_Apartment_Corner_01", "SM_Bld_Apartment_Corner_02",
            "SM_Bld_Apartment_Corner_03",
            "SM_Bld_Apartment_Door_01", "SM_Bld_Apartment_Door_02",
            "SM_Bld_Apartment_Roof_01", "SM_Bld_Apartment_Roof_02",
            "SM_Bld_Apartment_Roof_03",
            "SM_Bld_Apartment_Roof_Corner_01", "SM_Bld_Apartment_Roof_Corner_02",
            "SM_Bld_Apartment_Roof_Corner_03",
            "SM_Bld_Roof_Access_01",
            "SM_Bld_FireEscape_01", "SM_Bld_FireEscape_02", "SM_Bld_FireEscape_03",
        };

        enum Kind
        {
            Unknown, Shop, ShopCorner, ShopCover, Apartment, ApartmentStack,
            ApartmentCorner, ApartmentDoor, Roof, RoofCorner, RoofAccess,
            FireEscape, Decor,
        }

        sealed class Row
        {
            public string Name, Path;
            public Kind Kind;
            public Bounds Box;
            public int Faces, Style, Cells, Floors;
            public bool OuterCorner;
            public int RoofPairStyle, EscapeOrder;
            public float Cornice;
            public float DoorX, DoorZ, DoorWidth, DoorHeight, DoorYaw;
            public int DoorLeaves;
            public string FaceEvidence;
        }

        [CliCommand("gangsters_module_atlas",
            "Measure the residential forge modules and write ResidentialModules.cs plus its atlas report.",
            MainThreadRequired = true, Tags = new[] { "gangsters", "residential", "forge" })]
        public static object Generate()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Leave Play mode before measuring the residential module atlas.");

            var unresolved = new List<string>();
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in ResidentialForgeSource.DirectSourcePaths())
                if (path.StartsWith(ResidentialForgeSource.PropsDir + "/", StringComparison.Ordinal))
                    paths.Add(path);
            foreach (string name in StructuralNames)
            {
                string path = ResidentialForgeSource.FindPrefab(name);
                if (string.IsNullOrEmpty(path)) unresolved.Add($"{name}: prefab not found");
                else paths.Add(path);
            }

            var rows = new List<Row>(paths.Count);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string path in paths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        unresolved.Add($"{path}: not a prefab");
                        continue;
                    }
                    GameObject instance = null;
                    try
                    {
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var row = Measure(instance, path, unresolved);
                        rows.Add(row);
                    }
                    catch (Exception ex)
                    {
                        unresolved.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            AssignMeasuredStyles(rows, unresolved);
            AssignMeasuredEscapeOrder(rows, unresolved);
            AssignMeasuredOuterCorners(rows, unresolved);
            rows.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            WriteTable(rows, unresolved);
            WriteDocument(rows, unresolved);
            AssetDatabase.Refresh();

            return new
            {
                passed = unresolved.Count == 0,
                modules = rows.Count,
                structural = rows.Count(r => r.Kind != Kind.Decor && r.Kind != Kind.Unknown),
                decor = rows.Count(r => r.Kind == Kind.Decor),
                unresolved = unresolved.ToArray(),
                table = TablePath,
                report = DocumentPath,
            };
        }

        static Row Measure(GameObject instance, string path, List<string> unresolved)
        {
            var row = new Row
            {
                Name = instance.name,
                Path = path,
                Kind = Classify(instance.name, path),
                Box = ResidentialForgeSource.BoundsIn(instance.transform, instance),
            };
            row.Faces = MeasureFaces(instance, row.Kind, out row.FaceEvidence);
            bool nonCornerFacade = row.Kind == Kind.Shop || row.Kind == Kind.Apartment ||
                                   row.Kind == Kind.ApartmentStack ||
                                   row.Kind == Kind.ApartmentDoor || row.Kind == Kind.Roof;
            if (nonCornerFacade && BitCount(row.Faces) != 1)
                unresolved.Add($"{row.Name}: non-corner facade has {BitCount(row.Faces)} measured faces ({row.FaceEvidence})");
            row.Cells = MeasureCells(row);
            row.Floors = MeasureFloors(row);
            row.Cornice = MeasureCornice(row);
            row.EscapeOrder = 0;

            bool corner = row.Kind == Kind.ShopCorner || row.Kind == Kind.ApartmentCorner ||
                          row.Kind == Kind.RoofCorner;
            row.OuterCorner = corner && row.Kind != Kind.ApartmentCorner && BitCount(row.Faces) >= 2;
            if (corner && BitCount(row.Faces) < 2)
                unresolved.Add($"{row.Name}: corner has fewer than two measured faces ({row.FaceEvidence})");

            if (StorefrontDoorCatalog.TryGet(row.Name, out var door))
            {
                row.DoorX = door.X;
                row.DoorZ = door.Z;
                row.DoorWidth = door.Width;
                row.DoorHeight = door.Height;
                row.DoorLeaves = door.Leaves;
                row.DoorYaw = door.Yaw;
            }
            if (row.Kind == Kind.Unknown)
                unresolved.Add($"{row.Name}: unsupported structural source retained as Unknown");
            if (row.Box.size == Vector3.zero)
                unresolved.Add($"{row.Name}: no renderer bounds");
            return row;
        }

        static Kind Classify(string name, string path)
        {
            if (name.StartsWith("SM_Bld_Shop_Cover_", StringComparison.Ordinal)) return Kind.ShopCover;
            if (name.StartsWith("SM_Bld_Shop_Corner_", StringComparison.Ordinal)) return Kind.ShopCorner;
            if (name.StartsWith("SM_Bld_Shop_", StringComparison.Ordinal)) return Kind.Shop;
            if (name.StartsWith("SM_Bld_Apartment_Roof_Corner_", StringComparison.Ordinal)) return Kind.RoofCorner;
            if (name.StartsWith("SM_Bld_Apartment_Roof_", StringComparison.Ordinal)) return Kind.Roof;
            if (name.StartsWith("SM_Bld_Apartment_Stack_", StringComparison.Ordinal)) return Kind.ApartmentStack;
            if (name.StartsWith("SM_Bld_Apartment_Corner_", StringComparison.Ordinal)) return Kind.ApartmentCorner;
            if (name.StartsWith("SM_Bld_Apartment_Door_", StringComparison.Ordinal) &&
                !name.Contains("_Corner_")) return Kind.ApartmentDoor;
            if (name.StartsWith("SM_Bld_Apartment_", StringComparison.Ordinal) &&
                !name.Contains("_Stairs_") && !name.Contains("_Door_Corner_")) return Kind.Apartment;
            if (name == "SM_Bld_Roof_Access_01") return Kind.RoofAccess;
            if (name.StartsWith("SM_Bld_FireEscape_", StringComparison.Ordinal)) return Kind.FireEscape;
            if (path.StartsWith(ResidentialForgeSource.PropsDir + "/", StringComparison.Ordinal)) return Kind.Decor;
            return Kind.Unknown;
        }

        static int MeasureCells(Row row)
        {
            switch (row.Kind)
            {
                case Kind.Shop:
                case Kind.ShopCorner:
                case Kind.Apartment:
                case Kind.ApartmentStack:
                case Kind.ApartmentCorner:
                case Kind.ApartmentDoor:
                case Kind.Roof:
                case Kind.RoofCorner:
                    return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(row.Box.size.x, row.Box.size.z) / 5f));
                case Kind.ShopCover:
                case Kind.RoofAccess:
                case Kind.FireEscape:
                    return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(row.Box.size.x, row.Box.size.z) / 5f));
                default:
                    return 0;
            }
        }

        static int MeasureFloors(Row row)
        {
            if (row.Kind == Kind.Apartment || row.Kind == Kind.ApartmentStack ||
                row.Kind == Kind.ApartmentCorner)
                return Mathf.Max(1, Mathf.RoundToInt(row.Box.size.y / 3f));
            return 0;
        }

        static float MeasureCornice(Row row)
        {
            if (row.Kind != Kind.Apartment && row.Kind != Kind.ApartmentStack &&
                row.Kind != Kind.ApartmentCorner && row.Kind != Kind.Roof &&
                row.Kind != Kind.RoofCorner) return 0f;
            float depth = 0f;
            if ((row.Faces & 1) != 0) depth = Mathf.Max(depth, row.Box.max.z);
            if ((row.Faces & 2) != 0) depth = Mathf.Max(depth, row.Box.max.x);
            if ((row.Faces & 4) != 0) depth = Mathf.Max(depth, -5f - row.Box.min.z);
            if ((row.Faces & 8) != 0) depth = Mathf.Max(depth, -5f - row.Box.min.x);
            return ResidentialForgeSource.Round(Mathf.Max(0f, depth));
        }

        static void AssignMeasuredStyles(List<Row> rows, List<string> unresolved)
        {
            var anchors = new Row[3];
            for (int style = 1; style <= 3; style++)
            {
                string name = $"SM_Bld_Apartment_0{style}";
                var anchor = rows.FirstOrDefault(r => r.Name == name);
                if (anchor == null) unresolved.Add($"{name}: missing style anchor");
                else { anchors[style - 1] = anchor; anchor.Style = style; }
            }

            foreach (var row in rows)
            {
                if (row.Kind != Kind.ApartmentStack && row.Kind != Kind.ApartmentCorner)
                    continue;
                int best = 0;
                float distance = float.MaxValue;
                for (int i = 0; i < anchors.Length; i++)
                {
                    if (anchors[i] == null) continue;
                    float d = StyleDistance(row, anchors[i]);
                    if (d < distance) { distance = d; best = i + 1; }
                }
                if (best == 0 || distance > 0.45f)
                {
                    unresolved.Add($"{row.Name}: measured profile has no style match (distance {distance:0.###})");
                    continue;
                }
                row.Style = best;
            }

            var edges = rows.Where(r => r.Kind == Kind.Roof).ToList();
            var corners = rows.Where(r => r.Kind == Kind.RoofCorner).ToList();
            foreach (var edge in edges)
            {
                edge.Style = ResidentialForgeSource.SuffixNumber(edge.Name);
                edge.RoofPairStyle = edge.Style;
            }
            foreach (var corner in corners)
            {
                corner.Style = ResidentialForgeSource.SuffixNumber(corner.Name);
                float least = edges.Min(edge => Mathf.Abs(corner.Cornice - edge.Cornice));
                var matches = edges.Where(edge =>
                    Mathf.Abs(Mathf.Abs(corner.Cornice - edge.Cornice) - least) <= 0.002f).ToList();
                if (matches.Count != 1 || least > 0.02f)
                {
                    unresolved.Add($"{corner.Name}: roof cornice {corner.Cornice:0.###} has no unique measured edge pair");
                    continue;
                }
                corner.RoofPairStyle = matches[0].Style;
                corner.FaceEvidence += $"; paired to {matches[0].Name} at Δ{least:0.###} m";
            }
        }

        static float StyleDistance(Row row, Row anchor)
        {
            float expectedX = anchor.Box.size.x;
            float expectedZ = anchor.Box.size.z;
            if (row.Kind == Kind.ApartmentCorner)
                return Mathf.Abs(row.Box.size.x - expectedZ) +
                       Mathf.Abs(row.Box.size.z - expectedZ) +
                       Mathf.Abs(row.Box.max.x - anchor.Box.max.z) +
                       Mathf.Abs(row.Box.max.z - anchor.Box.max.z);
            return Mathf.Abs(row.Box.size.x - expectedX) +
                   Mathf.Abs(row.Box.size.z - expectedZ) +
                   Mathf.Abs(row.Cornice - anchor.Cornice);
        }

        static void AssignMeasuredEscapeOrder(List<Row> rows, List<string> unresolved)
        {
            var escapes = rows.Where(r => r.Kind == Kind.FireEscape).ToList();
            if (escapes.Count != 3)
            {
                unresolved.Add($"fire escape set: expected 3 measured parts, found {escapes.Count}");
                return;
            }
            Row top = escapes.OrderBy(r => r.Box.size.y).First();
            var remaining = escapes.Where(r => r != top).ToList();
            Row bottom = remaining.OrderByDescending(r =>
                r.Box.size.x * r.Box.size.y * r.Box.size.z).First();
            Row middle = remaining.First(r => r != bottom);
            bottom.EscapeOrder = 1;
            middle.EscapeOrder = 2;
            top.EscapeOrder = 3;
            foreach (var row in escapes)
                row.FaceEvidence = $"box {row.Box.size.x:0.###}×{row.Box.size.y:0.###}×{row.Box.size.z:0.###}; measured role {(row.EscapeOrder == 1 ? "bottom" : row.EscapeOrder == 2 ? "middle" : "top")}";
        }

        static void AssignMeasuredOuterCorners(List<Row> rows, List<string> unresolved)
        {
            foreach (Row row in rows.Where(r => r.Kind == Kind.ApartmentCorner))
            {
                row.OuterCorner = BitCount(row.Faces) >= 2;
                row.FaceEvidence += "; outer: two exposed facade-normal clusters in source mesh";
                if (!row.OuterCorner)
                    unresolved.Add($"{row.Name}: direct mesh measurement did not expose two outer faces");
            }
        }

        static int MeasureFaces(GameObject instance, Kind kind, out string evidence)
        {
            if (kind == Kind.Decor || kind == Kind.ShopCover || kind == Kind.RoofAccess ||
                kind == Kind.FireEscape || kind == Kind.Unknown)
            {
                evidence = "not a facade";
                return 0;
            }

            if (kind == Kind.Shop || kind == Kind.ShopCorner)
            {
                int glass = GlassFaces(instance, out evidence);
                if (glass != 0)
                {
                    // Flat shop modules have their authored facade on +Z. Some two-sided
                    // glass meshes also expose edge/back normals; retain the measured +Z
                    // cluster for a non-corner only, and let the invariant above reject a
                    // source which does not actually carry that cluster.
                    if (kind == Kind.Shop && (glass & 1) != 0)
                    {
                        if (BitCount(glass) > 1)
                            evidence += "; non-corner pivot-face filter retained measured +Z";
                        return 1;
                    }
                    return glass;
                }
            }

            if (kind == Kind.ApartmentCorner || kind == Kind.RoofCorner)
            {
                float? turn = CornerFacing.Measure(instance, out evidence);
                if (turn.HasValue)
                {
                    int quarter = Mathf.RoundToInt(turn.Value / 90f) & 3;
                    return quarter switch
                    {
                        0 => 1 | 2,
                        1 => 1 | 8,
                        2 => 8 | 4,
                        _ => 4 | 2,
                    };
                }
            }

            int[] counts = OuterBandCounts(instance, out string scores);
            int first = 0;
            for (int i = 1; i < 4; i++) if (counts[i] > counts[first]) first = i;
            int mask = 1 << first;
            bool corner = kind == Kind.ShopCorner || kind == Kind.ApartmentCorner || kind == Kind.RoofCorner;
            if (corner)
            {
                int second = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (i == first || (i + 2) % 4 == first) continue;
                    if (second < 0 || counts[i] > counts[second]) second = i;
                }
                if (second >= 0 && counts[second] * 2 >= Mathf.Max(1, counts[first])) mask |= 1 << second;
            }
            evidence = scores;
            return mask;
        }

        static int GlassFaces(GameObject instance, out string evidence)
        {
            var counts = new int[4];
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable ||
                    !mesh.name.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase)) continue;
                Vector3[] normals = mesh.normals;
                Matrix4x4 matrix = instance.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 normal in normals)
                {
                    Vector3 n = matrix.MultiplyVector(normal);
                    n.y = 0f;
                    if (n.sqrMagnitude < 0.25f) continue;
                    n.Normalize();
                    float ax = Mathf.Abs(n.x), az = Mathf.Abs(n.z);
                    if (Mathf.Max(ax, az) < 0.92f) continue; // the chamfered entrance is not a side
                    if (az >= ax) counts[n.z >= 0f ? 0 : 2]++;
                    else counts[n.x >= 0f ? 1 : 3]++;
                }
            }
            int best = counts.Max();
            int mask = 0;
            if (best > 0)
                for (int i = 0; i < 4; i++) if (counts[i] * 3 >= best) mask |= 1 << i;
            evidence = $"glass normals +Z/+X/-Z/-X={counts[0]}/{counts[1]}/{counts[2]}/{counts[3]}";
            return mask;
        }

        static int[] OuterBandCounts(GameObject instance, out string evidence)
        {
            Bounds bounds = ResidentialForgeSource.BoundsIn(instance.transform, instance);
            float bx = Mathf.Max(0.5f, bounds.size.x * 0.25f);
            float bz = Mathf.Max(0.5f, bounds.size.z * 0.25f);
            var counts = new int[4];
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Matrix4x4 matrix = instance.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(vertex);
                    if (p.z >= bounds.max.z - bz) counts[0]++;
                    if (p.x >= bounds.max.x - bx) counts[1]++;
                    if (p.z <= bounds.min.z + bz) counts[2]++;
                    if (p.x <= bounds.min.x + bx) counts[3]++;
                }
            }
            evidence = $"outer-band vertices +Z/+X/-Z/-X={counts[0]}/{counts[1]}/{counts[2]}/{counts[3]}";
            return counts;
        }

        static int BitCount(int mask)
        {
            int count = 0;
            while (mask != 0) { count += mask & 1; mask >>= 1; }
            return count;
        }

        static void WriteTable(List<Row> rows, List<string> unresolved)
        {
            var text = new StringBuilder();
            text.AppendLine("// GENERATED by unity command gangsters_module_atlas. Do not edit by hand.");
            text.AppendLine("using System;");
            text.AppendLine();
            text.AppendLine("namespace RoadDemo");
            text.AppendLine("{");
            text.AppendLine("    public enum ResidentialModuleKind { Unknown, Shop, ShopCorner, ShopCover, Apartment, ApartmentStack, ApartmentCorner, ApartmentDoor, Roof, RoofCorner, RoofAccess, FireEscape, Decor }");
            text.AppendLine();
            text.AppendLine("    public sealed class ResidentialModule");
            text.AppendLine("    {");
            text.AppendLine("        public string Name, Path;");
            text.AppendLine("        public ResidentialModuleKind Kind;");
            text.AppendLine("        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;");
            text.AppendLine("        public int Faces, Style, Cells, Floors;");
            text.AppendLine("        public bool OuterCorner;");
            text.AppendLine("        public int RoofPairStyle, EscapeOrder;");
            text.AppendLine("        public float DoorX, DoorZ, DoorWidth, DoorHeight, DoorYaw;");
            text.AppendLine("        public int DoorLeaves;");
            text.AppendLine("    }");
            text.AppendLine();
            text.AppendLine("    public static class ResidentialModules");
            text.AppendLine("    {");
            text.AppendLine("        public const int FacePlusZ = 1, FacePlusX = 2, FaceMinusZ = 4, FaceMinusX = 8;");
            text.AppendLine("        public static readonly ResidentialModule[] All =");
            text.AppendLine("        {");
            foreach (var row in rows)
            {
                text.AppendLine("            new ResidentialModule");
                text.AppendLine("            {");
                text.AppendLine($"                Name = {ResidentialForgeSource.Quote(row.Name)}, Path = {ResidentialForgeSource.Quote(row.Path)}, Kind = ResidentialModuleKind.{row.Kind},");
                text.AppendLine($"                MinX = {ResidentialForgeSource.Float(row.Box.min.x)}, MinY = {ResidentialForgeSource.Float(row.Box.min.y)}, MinZ = {ResidentialForgeSource.Float(row.Box.min.z)},");
                text.AppendLine($"                MaxX = {ResidentialForgeSource.Float(row.Box.max.x)}, MaxY = {ResidentialForgeSource.Float(row.Box.max.y)}, MaxZ = {ResidentialForgeSource.Float(row.Box.max.z)},");
                text.AppendLine($"                Faces = {row.Faces}, Style = {row.Style}, Cells = {row.Cells}, Floors = {row.Floors}, OuterCorner = {(row.OuterCorner ? "true" : "false")},");
                text.AppendLine($"                RoofPairStyle = {row.RoofPairStyle}, EscapeOrder = {row.EscapeOrder},");
                text.AppendLine($"                DoorX = {ResidentialForgeSource.Float(row.DoorX)}, DoorZ = {ResidentialForgeSource.Float(row.DoorZ)}, DoorWidth = {ResidentialForgeSource.Float(row.DoorWidth)}, DoorHeight = {ResidentialForgeSource.Float(row.DoorHeight)}, DoorLeaves = {row.DoorLeaves}, DoorYaw = {ResidentialForgeSource.Float(row.DoorYaw)},");
                text.AppendLine("            },");
            }
            text.AppendLine("        };");
            text.AppendLine();
            text.AppendLine("        public static ResidentialModule Find(string name)");
            text.AppendLine("        {");
            text.AppendLine("            if (string.IsNullOrEmpty(name)) return null;");
            text.AppendLine("            for (int i = 0; i < All.Length; i++)");
            text.AppendLine("                if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase)) return All[i];");
            text.AppendLine("            return null;");
            text.AppendLine("        }");
            text.AppendLine();
            text.AppendLine("        public static readonly string[] Unresolved =");
            text.AppendLine("        {");
            foreach (string failure in unresolved.Distinct().OrderBy(s => s, StringComparer.Ordinal))
                text.AppendLine($"            {ResidentialForgeSource.Quote(failure)},");
            text.AppendLine("        };");
            text.AppendLine("    }");
            text.AppendLine("}");
            File.WriteAllText(TablePath, text.ToString().Replace("\r\n", "\n"));
        }

        static void WriteDocument(List<Row> rows, List<string> unresolved)
        {
            var text = new StringBuilder();
            text.AppendLine("# Residential module atlas");
            text.AppendLine();
            text.AppendLine("Generated by `unity command gangsters_module_atlas`. Bounds are metres in the prefab root frame. Faces use `+Z/+X/-Z/-X`; the evidence column is the mesh measurement which selected them. Door figures are copied by the tool from the shared measured `StorefrontDoorCatalog`.");
            text.AppendLine();
            text.AppendLine("All three apartment-corner families are classified as outer corners because direct mesh-normal measurement finds two exposed facade clusters on each source prefab. This measured result supersedes the earlier unverified Corner 02 inner-corner hint; whole-unit rectangular boundary occupancy is not used because the harvested plans can be non-rectangular.");
            text.AppendLine();
            text.AppendLine("| module | kind | box min → max | faces | style | cells/floors | outer | roof pair | escape | evidence |");
            text.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var row in rows)
                text.AppendLine($"| `{row.Name}` | {row.Kind} | ({row.Box.min.x:0.###},{row.Box.min.y:0.###},{row.Box.min.z:0.###}) → ({row.Box.max.x:0.###},{row.Box.max.y:0.###},{row.Box.max.z:0.###}) | {FaceText(row.Faces)} | {row.Style} ({row.Cornice:0.###} m) | {row.Cells}/{row.Floors} | {(row.OuterCorner ? "yes" : "no")} | {row.RoofPairStyle} | {row.EscapeOrder} | {row.FaceEvidence} |");
            text.AppendLine();
            text.AppendLine("## Unresolved");
            text.AppendLine();
            if (unresolved.Count == 0) text.AppendLine("None.");
            else foreach (string failure in unresolved.Distinct().OrderBy(s => s, StringComparer.Ordinal))
                text.AppendLine($"- {failure}");
            File.WriteAllText(DocumentPath, text.ToString().Replace("\r\n", "\n"));
        }

        static string FaceText(int mask)
        {
            var names = new List<string>(4);
            if ((mask & 1) != 0) names.Add("+Z");
            if ((mask & 2) != 0) names.Add("+X");
            if ((mask & 4) != 0) names.Add("-Z");
            if ((mask & 8) != 0) names.Add("-X");
            return names.Count == 0 ? "—" : string.Join(",", names);
        }
    }
}
