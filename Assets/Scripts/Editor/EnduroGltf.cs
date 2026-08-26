using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The smallest glTF 2.0 reader that will open a binary .glb, and it exists because
    /// this project has no glTF importer at all: Unity reads FBX and OBJ out of the box
    /// and nothing else, and the packages that would read a .glb (glTFast, UnityGLTF) are
    /// not in Packages/manifest.json. A machine that arrives as a .glb therefore arrives
    /// as three megabytes Unity will not look at, and the choice is to add a package for
    /// one motorcycle or to read the file.
    ///
    /// This reads the file. It is deliberately a SUBSET and not an importer:
    ///
    ///   what it reads   the JSON chunk, the BIN chunk, the node tree with its matrices,
    ///                   triangle primitives with POSITION / NORMAL / TEXCOORD_0, byte,
    ///                   short and int indices, and the flat colours off each material's
    ///                   pbrMetallicRoughness.
    ///   what it ignores  skins, animations, cameras, images, samplers, sparse accessors,
    ///                   every primitive mode but triangles, and every extension. Nothing
    ///                   here is drawn by a .glb this project bakes; a file that needs any
    ///                   of it should be brought in as an FBX instead.
    ///
    /// Everything comes back in glTF's own frame - right-handed, Y up, metres - because
    /// the conversion to Unity's is the BAKER's business (<see cref="EnduroBikeBaker"/>)
    /// and depends on which way the model was drawn. See the note there.
    /// </summary>
    public static class EnduroGltf
    {
        /// <summary>An opened .glb: the header's JSON as plain dictionaries and lists,
        /// and the binary chunk every accessor points into.</summary>
        public sealed class File
        {
            public Dictionary<string, object> Root;
            public byte[] Bin;

            public List<object> Table(string name) =>
                Root != null && Root.TryGetValue(name, out var value) ? value as List<object> : null;

            public Dictionary<string, object> Row(string table, int index)
            {
                var rows = Table(table);
                return rows != null && index >= 0 && index < rows.Count
                    ? rows[index] as Dictionary<string, object>
                    : null;
            }
        }

        /// <summary>One triangle mesh out of the file, in glTF's frame, with the node
        /// matrix already applied - so a caller never walks the tree twice.</summary>
        public struct Primitive
        {
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Indices;
            public int Material;
        }

        // ------------------------------------------------------------------ the file

        /// <summary>Open a .glb off disk. Null with a logged reason if it is not one -
        /// a .gltf with a sidecar .bin is NOT read, because nothing here ships that way.
        /// </summary>
        public static File Open(string fullPath)
        {
            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(fullPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Enduro] cannot read " + fullPath + ": " + e.Message);
                return null;
            }

            if (bytes.Length < 20 || System.BitConverter.ToUInt32(bytes, 0) != 0x46546C67u)
            {
                Debug.LogError("[Enduro] " + fullPath + " is not a binary glTF (no glTF magic).");
                return null;
            }

            var version = System.BitConverter.ToUInt32(bytes, 4);
            if (version != 2)
            {
                Debug.LogError("[Enduro] " + fullPath + " is glTF " + version + "; only 2 is read.");
                return null;
            }

            var file = new File();
            var at = 12;
            while (at + 8 <= bytes.Length)
            {
                var length = (int)System.BitConverter.ToUInt32(bytes, at);
                var kind = System.BitConverter.ToUInt32(bytes, at + 4);
                at += 8;
                if (length < 0 || at + length > bytes.Length)
                {
                    Debug.LogError("[Enduro] a chunk runs past the end of " + fullPath + ".");
                    return null;
                }

                if (kind == 0x4E4F534Au)         // JSON
                    file.Root = Json.Parse(Encoding.UTF8.GetString(bytes, at, length))
                                as Dictionary<string, object>;
                else if (kind == 0x004E4942u)    // BIN
                {
                    file.Bin = new byte[length];
                    System.Array.Copy(bytes, at, file.Bin, 0, length);
                }

                at += length;
                at += (4 - at % 4) % 4;          // chunks are four-byte aligned
            }

            if (file.Root == null)
            {
                Debug.LogError("[Enduro] no JSON chunk in " + fullPath + ".");
                return null;
            }
            return file;
        }

        // ------------------------------------------------------------------ the tree

        /// <summary>Every drawn primitive in the file's scene, world-space in glTF's own
        /// frame. <paramref name="node"/> is called for each one with the node's name and
        /// the name of the top-level assembly it hangs under (the .glb's own grouping -
        /// "wheel_front", "bodywork"), which is how a baker tells a wheel from a
        /// mudguard without measuring anything.</summary>
        public static void Walk(File file, System.Action<string, string, Primitive> node)
        {
            var scenes = file.Table("scenes");
            var nodes = file.Table("nodes");
            if (scenes == null || nodes == null || scenes.Count == 0) return;

            var scene = scenes[0] as Dictionary<string, object>;
            var roots = scene != null && scene.TryGetValue("nodes", out var list)
                ? list as List<object>
                : null;
            if (roots == null) return;

            foreach (var index in roots)
                Descend(file, Int(index), Matrix4x4.identity, "", 0, node);
        }

        static void Descend(File file, int index, Matrix4x4 parent, string assembly, int depth,
                            System.Action<string, string, Primitive> found)
        {
            var node = file.Row("nodes", index);
            if (node == null) return;

            var world = parent * Local(node);
            var name = node.TryGetValue("name", out var raw) ? raw as string ?? "" : "";

            // The assembly is the name one level UNDER the model's own root, which is how
            // the file is drawn: root "enduro_450" (depth 0), then "wheel_front", "frame",
            // "bodywork" and the rest (depth 1), then the parts themselves. The root's own
            // name is no assembly - it would answer for everything.
            var mine = depth == 1 ? name : assembly;

            if (node.TryGetValue("mesh", out var meshIndex))
                foreach (var primitive in Read(file, Int(meshIndex), world))
                    found(name, mine, primitive);

            if (node.TryGetValue("children", out var kids) && kids is List<object> children)
                foreach (var child in children)
                    Descend(file, Int(child), world, mine, depth + 1, found);
        }

        static Matrix4x4 Local(Dictionary<string, object> node)
        {
            if (node.TryGetValue("matrix", out var raw) && raw is List<object> m && m.Count == 16)
            {
                var matrix = new Matrix4x4();
                for (var c = 0; c < 4; c++)
                    matrix.SetColumn(c, new Vector4(Num(m[c * 4]), Num(m[c * 4 + 1]),
                                                    Num(m[c * 4 + 2]), Num(m[c * 4 + 3])));
                return matrix;
            }

            var t = Vector3.zero;
            var r = Quaternion.identity;
            var s = Vector3.one;
            if (node.TryGetValue("translation", out var tv) && tv is List<object> tl && tl.Count == 3)
                t = new Vector3(Num(tl[0]), Num(tl[1]), Num(tl[2]));
            if (node.TryGetValue("rotation", out var rv) && rv is List<object> rl && rl.Count == 4)
                r = new Quaternion(Num(rl[0]), Num(rl[1]), Num(rl[2]), Num(rl[3]));
            if (node.TryGetValue("scale", out var sv) && sv is List<object> sl && sl.Count == 3)
                s = new Vector3(Num(sl[0]), Num(sl[1]), Num(sl[2]));
            return Matrix4x4.TRS(t, r, s);
        }

        static IEnumerable<Primitive> Read(File file, int meshIndex, Matrix4x4 world)
        {
            var mesh = file.Row("meshes", meshIndex);
            var primitives = mesh != null && mesh.TryGetValue("primitives", out var raw)
                ? raw as List<object>
                : null;
            if (primitives == null) yield break;

            foreach (var entry in primitives)
            {
                if (!(entry is Dictionary<string, object> primitive)) continue;
                if (Field(primitive, "mode", 4) != 4) continue;   // triangles only
                if (!(primitive.TryGetValue("attributes", out var raw2)
                      && raw2 is Dictionary<string, object> attributes)) continue;
                if (!attributes.TryGetValue("POSITION", out var positionIndex)) continue;

                var positions = Vec3(file, Int(positionIndex));
                if (positions == null || positions.Length == 0) continue;

                for (var i = 0; i < positions.Length; i++)
                    positions[i] = world.MultiplyPoint3x4(positions[i]);

                var normals = attributes.TryGetValue("NORMAL", out var normalIndex)
                    ? Vec3(file, Int(normalIndex))
                    : null;
                if (normals != null)
                    for (var i = 0; i < normals.Length; i++)
                        normals[i] = world.MultiplyVector(normals[i]).normalized;

                var uvs = attributes.TryGetValue("TEXCOORD_0", out var uvIndex)
                    ? Vec2(file, Int(uvIndex))
                    : null;

                int[] indices;
                if (primitive.TryGetValue("indices", out var indexIndex))
                    indices = Scalars(file, Int(indexIndex));
                else
                {
                    // A primitive with no index accessor draws its vertices in order -
                    // twenty-seven of the enduro's parts are drawn that way.
                    indices = new int[positions.Length];
                    for (var i = 0; i < indices.Length; i++) indices[i] = i;
                }
                if (indices == null || indices.Length < 3) continue;

                yield return new Primitive
                {
                    Positions = positions,
                    Normals = normals,
                    Uvs = uvs,
                    Indices = indices,
                    Material = Field(primitive, "material", -1),
                };
            }
        }

        // ------------------------------------------------------------- the accessors

        static Vector3[] Vec3(File file, int index)
        {
            if (!Buffer(file, index, out var bytes, out var offset, out var stride, out var count, 12))
                return null;
            var values = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var at = offset + i * stride;
                values[i] = new Vector3(System.BitConverter.ToSingle(bytes, at),
                                        System.BitConverter.ToSingle(bytes, at + 4),
                                        System.BitConverter.ToSingle(bytes, at + 8));
            }
            return values;
        }

        static Vector2[] Vec2(File file, int index)
        {
            if (!Buffer(file, index, out var bytes, out var offset, out var stride, out var count, 8))
                return null;
            var values = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var at = offset + i * stride;
                values[i] = new Vector2(System.BitConverter.ToSingle(bytes, at),
                                        System.BitConverter.ToSingle(bytes, at + 4));
            }
            return values;
        }

        static int[] Scalars(File file, int index)
        {
            var accessor = file.Row("accessors", index);
            if (accessor == null) return null;
            var component = Field(accessor, "componentType", 5123);
            var size = component == 5121 ? 1 : component == 5123 ? 2 : 4;
            if (!Buffer(file, index, out var bytes, out var offset, out var stride, out var count, size))
                return null;

            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                var at = offset + i * stride;
                values[i] = size == 1 ? bytes[at]
                          : size == 2 ? System.BitConverter.ToUInt16(bytes, at)
                          : (int)System.BitConverter.ToUInt32(bytes, at);
            }
            return values;
        }

        static bool Buffer(File file, int index, out byte[] bytes, out int offset,
                           out int stride, out int count, int element)
        {
            bytes = file.Bin;
            offset = 0;
            stride = element;
            count = 0;

            var accessor = file.Row("accessors", index);
            if (accessor == null || bytes == null) return false;
            if (accessor.ContainsKey("sparse")) return false;   // not read: nothing here is sparse
            if (!accessor.TryGetValue("bufferView", out var viewIndex)) return false;

            var view = file.Row("bufferViews", Int(viewIndex));
            if (view == null) return false;

            count = Field(accessor, "count", 0);
            stride = Field(view, "byteStride", 0);
            if (stride <= 0) stride = element;
            offset = Field(view, "byteOffset", 0) + Field(accessor, "byteOffset", 0);
            return count > 0 && offset >= 0 && offset + (count - 1) * stride + element <= bytes.Length;
        }

        // ------------------------------------------------------------- the materials

        /// <summary>A material's flat colour, LINEAR as glTF states it, with its metal
        /// and roughness. Every one of the enduro's thirteen is a plain
        /// pbrMetallicRoughness with no texture at all, which is why the bake can hand
        /// them straight to URP without an atlas.</summary>
        public static void ReadMaterial(File file, int index, out string name, out Color baseColor,
                                        out float metallic, out float roughness)
        {
            name = "material_" + index;
            baseColor = Color.grey;
            metallic = 0f;
            roughness = 0.5f;

            var material = file.Row("materials", index);
            if (material == null) return;
            if (material.TryGetValue("name", out var raw) && raw is string given && given.Length > 0)
                name = given;

            if (!(material.TryGetValue("pbrMetallicRoughness", out var raw2)
                  && raw2 is Dictionary<string, object> pbr)) return;

            if (pbr.TryGetValue("baseColorFactor", out var raw3) && raw3 is List<object> rgba
                && rgba.Count >= 3)
                baseColor = new Color(Num(rgba[0]), Num(rgba[1]), Num(rgba[2]),
                                      rgba.Count > 3 ? Num(rgba[3]) : 1f);

            metallic = Real(pbr, "metallicFactor", 1f);
            roughness = Real(pbr, "roughnessFactor", 1f);
        }

        // ------------------------------------------------------------------ plumbing

        static int Int(object value) => value is double d ? (int)d : -1;
        static float Num(object value) => value is double d ? (float)d : 0f;

        static int Field(Dictionary<string, object> row, string key, int fallback) =>
            row != null && row.TryGetValue(key, out var value) && value is double d ? (int)d : fallback;

        static float Real(Dictionary<string, object> row, string key, float fallback) =>
            row != null && row.TryGetValue(key, out var value) && value is double d ? (float)d : fallback;

        /// <summary>A recursive-descent JSON reader. Unity's own JsonUtility cannot read
        /// glTF - the attributes block is an object whose KEYS are the semantics
        /// (POSITION, NORMAL), and JsonUtility only fills fields it was declared with,
        /// leaving a missing accessor index as 0, which is a real accessor. So the header
        /// is read as plain dictionaries and lists instead, where "absent" is absent.
        /// </summary>
        static class Json
        {
            public static object Parse(string text)
            {
                var at = 0;
                return Value(text, ref at);
            }

            static object Value(string s, ref int at)
            {
                Space(s, ref at);
                if (at >= s.Length) return null;
                switch (s[at])
                {
                    case '{': return Object(s, ref at);
                    case '[': return Array(s, ref at);
                    case '"': return Text(s, ref at);
                    case 't': at += 4; return true;
                    case 'f': at += 5; return false;
                    case 'n': at += 4; return null;
                    default: return Number(s, ref at);
                }
            }

            static Dictionary<string, object> Object(string s, ref int at)
            {
                var map = new Dictionary<string, object>();
                at++;                                   // {
                Space(s, ref at);
                if (at < s.Length && s[at] == '}') { at++; return map; }

                while (at < s.Length)
                {
                    Space(s, ref at);
                    var key = Text(s, ref at);
                    Space(s, ref at);
                    at++;                               // :
                    map[key] = Value(s, ref at);
                    Space(s, ref at);
                    if (at >= s.Length) break;
                    if (s[at] == ',') { at++; continue; }
                    if (s[at] == '}') { at++; break; }
                    break;
                }
                return map;
            }

            static List<object> Array(string s, ref int at)
            {
                var list = new List<object>();
                at++;                                   // [
                Space(s, ref at);
                if (at < s.Length && s[at] == ']') { at++; return list; }

                while (at < s.Length)
                {
                    list.Add(Value(s, ref at));
                    Space(s, ref at);
                    if (at >= s.Length) break;
                    if (s[at] == ',') { at++; continue; }
                    if (s[at] == ']') { at++; break; }
                    break;
                }
                return list;
            }

            static string Text(string s, ref int at)
            {
                var built = new StringBuilder();
                at++;                                   // "
                while (at < s.Length && s[at] != '"')
                {
                    if (s[at] != '\\') { built.Append(s[at++]); continue; }
                    at++;
                    var escape = s[at++];
                    switch (escape)
                    {
                        case 'n': built.Append('\n'); break;
                        case 't': built.Append('\t'); break;
                        case 'r': built.Append('\r'); break;
                        case 'b': built.Append('\b'); break;
                        case 'f': built.Append('\f'); break;
                        case 'u':
                            built.Append((char)int.Parse(s.Substring(at, 4),
                                                         NumberStyles.HexNumber,
                                                         CultureInfo.InvariantCulture));
                            at += 4;
                            break;
                        default: built.Append(escape); break;
                    }
                }
                at++;                                   // "
                return built.ToString();
            }

            static object Number(string s, ref int at)
            {
                var start = at;
                while (at < s.Length && "+-.eE0123456789".IndexOf(s[at]) >= 0) at++;
                return double.Parse(s.Substring(start, at - start), CultureInfo.InvariantCulture);
            }

            static void Space(string s, ref int at)
            {
                while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
            }
        }
    }
}
