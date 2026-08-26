using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The outfit's dirt bike: a 450 enduro, baked out of a .glb into a prefab the rest
    /// of the game already knows how to ride.
    ///
    /// It is the first body in this project that came from outside the Synty packs, so
    /// two things had to be settled that a pack body settles for itself.
    ///
    /// HOW IT GETS IN. Unity reads FBX and OBJ and nothing else, and there is no glTF
    /// package in Packages/manifest.json - a .glb dropped in Assets is three megabytes
    /// the editor will not look at. <see cref="EnduroGltf"/> reads the file instead, and
    /// this bakes what it finds into meshes, materials and a prefab, so the machine is a
    /// re-runnable bake exactly like the black tourer (<see cref="GangBikeBaker"/>) and
    /// not a hand-assembled object nobody can rebuild.
    ///
    /// WHAT IT COMES OUT AS. Everything the street asks of a two-wheeler is asked by
    /// NAME (RoadDemo.BikeBody): WheelSpin takes the parts whose names hold "Wheel" and
    /// reads which is which off a front/rear token, and the bars are the highest part
    /// whose name holds "Handle". The .glb is drawn as 537 separate little parts - every
    /// chain link, every spoke, every tread block - so the bake MERGES it down to the
    /// four the game needs and no more:
    ///
    ///   the root        frame, engine, bodywork, exhaust, forks, rear suspension - one
    ///                   mesh, submeshed by material, and it never moves.
    ///   Wheel_Front_01  the whole front wheel, so it rolls and steers.
    ///   Wheel_Rear_01   the whole rear wheel, so it rolls.
    ///   HandleBars      bar, risers, grips, levers, guards and cables - the part that
    ///                   turns with the steering and the part a rider's fists are put on.
    ///
    /// Four renderers off 537 nodes, and the parts keep the prefab's own origin as their
    /// pivot exactly as the Synty bodies do (WheelSpin measures the hub off the mesh
    /// bounds and spins about that, so a pivot at the origin is what it expects).
    ///
    /// AND HOW BIG. The model is drawn at LIFE SIZE - a 1.48 m wheelbase, which is a real
    /// 450 to the centimetre - and the packs are not. Measured, the two machines this one
    /// has to stand beside are 3.45 m long and 2.04 m tall (Palm City's motorbike, a
    /// chopper, 2.45 m between the axles) and 2.21 by 1.57 (the moped, 1.49 m between the
    /// axles). A life-size enduro parked between them reads as a toy. So the bake scales
    /// it to <see cref="TargetWheelbase"/> - 1.85 m, between the pack's two, which lands
    /// the machine at about 2.9 m long and 1.6 m tall: taller than the moped, well short
    /// of the chopper, which is what a dirt bike is. The scale is applied to the VERTICES,
    /// never to the prefab's transform, so nothing downstream has to know about it.
    /// </summary>
    public static class EnduroBikeBaker
    {
        const string Source = "Assets/Prefabs/Vehicles/Enduro/enduro_450.glb";
        const string Folder = "Assets/Prefabs/Vehicles";
        const string PaintFolder = Folder + "/Enduro";
        const string Name = "SM_Veh_Motorbike_Enduro_450";

        /// <summary>Metres between the axles the machine is scaled to. See the note on
        /// the class: the pack's own two motorcycles measure 2.45 and 1.49, and this sits
        /// between them.</summary>
        const float TargetWheelbase = 1.85f;

        /// <summary>The .glb's own assemblies - its top-level grouping, which is how the
        /// file was drawn and what the bake sorts by.</summary>
        const string FrontWheel = "wheel_front";
        const string RearWheel = "wheel_rear";
        const string FrontEnd = "front_end";

        /// <summary>The parts of the front end that TURN: everything above the top yoke.
        /// The forks, the guards, the brake and the wheel are not among them - a bike's
        /// bars turn against its forks in every pack body too (BikeBody.FindBars takes
        /// the bar part alone), and a rider's fists follow the bars.</summary>
        static readonly HashSet<string> BarParts = new HashSet<string>
        {
            "handlebar", "bar_riser", "bar_clamp_block", "bar_pad",
            "grip", "grip_rib", "control_perch", "lever", "handguard", "control_cable",
        };

        /// <summary>The four objects the machine is merged into, in the order they are
        /// built. <see cref="Piece.Body"/> is the prefab's own root.</summary>
        enum Piece { Body = 0, WheelFront = 1, WheelRear = 2, Bars = 3 }

        static readonly string[] PieceNames =
        {
            Name, "Wheel_Front_01", "Wheel_Rear_01", "HandleBars",
        };

        [MenuItem("Tools/City/Vehicles/Bake the enduro")]
        public static void Bake()
        {
            var file = EnduroGltf.Open(System.IO.Path.GetFullPath(Source));
            if (file == null) return;

            var parts = new Part[PieceNames.Length];
            for (var i = 0; i < parts.Length; i++) parts[i] = new Part();

            // Read the whole file once, sorting every primitive into its piece as it
            // arrives. Positions stay in glTF's frame for now: how far the machine has to
            // be scaled is not known until the two wheels have been measured, and both of
            // them are measured off exactly this pass.
            EnduroGltf.Walk(file, (node, assembly, primitive) =>
                parts[Sort(node, assembly)].Add(primitive));

            if (parts[(int)Piece.WheelFront].Empty || parts[(int)Piece.WheelRear].Empty
                || parts[(int)Piece.Bars].Empty || parts[(int)Piece.Body].Empty)
            {
                Debug.LogError("[Enduro] the .glb did not sort into a body, two wheels and a "
                               + "set of bars - the assembly names in " + Source + " have moved. "
                               + "Nothing written.");
                return;
            }

            // The two measurements everything else follows from, both in the file's own
            // frame, where the machine is drawn along x with its nose at +x.
            var front = parts[(int)Piece.WheelFront].Box;
            var rear = parts[(int)Piece.WheelRear].Box;
            var wheelbase = front.center.x - rear.center.x;
            if (wheelbase <= 0.1f)
            {
                Debug.LogError("[Enduro] the wheels measure " + wheelbase.ToString("0.00")
                               + " m apart - the model is not drawn along x with its nose "
                               + "at +x any more. Nothing written.");
                return;
            }

            var scale = TargetWheelbase / wheelbase;

            var whole = parts[0].Box;
            for (var i = 1; i < parts.Length; i++) whole.Encapsulate(parts[i].Box);

            // Where the prefab's origin lands: on the centre line, on the ground, and at
            // the middle of the machine's length - the Synty vehicles' own convention, and
            // what BikeBody assumes when it leans a bike about its own origin.
            var origin = new Vector3(whole.center.x, whole.min.y, whole.center.z);

            for (var i = 0; i < parts.Length; i++) parts[i].Settle(origin, scale);

            // --------------------------------------------------------------- the assets

            if (!AssetDatabase.IsValidFolder(PaintFolder))
                AssetDatabase.CreateFolder(Folder, "Enduro");

            var materials = Materials(file, parts);
            if (materials == null) return;

            var go = new GameObject(Name);
            var meshes = new List<Mesh>();
            for (var i = 0; i < parts.Length; i++)
            {
                var transform = go.transform;
                if (i != (int)Piece.Body)
                {
                    var child = new GameObject(PieceNames[i]);
                    child.transform.SetParent(go.transform, false);
                    transform = child.transform;
                }

                var mesh = parts[i].Build("Enduro_" + PieceNames[i]);
                meshes.Add(mesh);
                transform.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                transform.gameObject.AddComponent<MeshRenderer>().sharedMaterials =
                    parts[i].Materials(materials);
            }

            var meshPath = Folder + "/" + Name + "_Meshes.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(meshes[0], meshPath);
            for (var i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], meshPath);

            var prefabPath = Folder + "/" + Name + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            // What was measured, printed - the same contract the other benches keep. A
            // machine whose numbers look wrong is a machine to re-scale, and these are
            // the numbers to re-scale it by.
            var size = whole.size * scale;
            var triangles = 0;
            foreach (var part in parts) triangles += part.Triangles;
            Debug.Log("[Enduro] " + prefabPath + " - " + triangles + " triangles in four parts, "
                      + materials.Count + " materials in " + PaintFolder + ". Wheelbase "
                      + (wheelbase * scale).ToString("0.00") + " m (life size "
                      + wheelbase.ToString("0.00") + ", scaled x" + scale.ToString("0.000")
                      + "), wheels " + (front.size.y * scale).ToString("0.00") + " and "
                      + (rear.size.y * scale).ToString("0.00") + " m across, machine "
                      + size.x.ToString("0.00") + " long by " + size.y.ToString("0.00")
                      + " tall by " + size.z.ToString("0.00") + " wide. "
                      + (saved != null ? "Saved." : "SAVE FAILED."));

            if (saved != null)
                Selection.activeObject = saved;
        }

        /// <summary>Which of the four a primitive belongs to, by the .glb's own names.
        /// Everything that is not a wheel or a bar is the machine's body, so a part
        /// nobody has heard of is drawn rather than dropped.</summary>
        static int Sort(string node, string assembly)
        {
            if (assembly == FrontWheel) return (int)Piece.WheelFront;
            if (assembly == RearWheel) return (int)Piece.WheelRear;
            if (assembly == FrontEnd && BarParts.Contains(node)) return (int)Piece.Bars;
            return (int)Piece.Body;
        }

        // ------------------------------------------------------------------ the paints

        /// <summary>One URP material per glTF material, by index. The .glb carries no
        /// textures at all - thirteen flat pbrMetallicRoughness colours, which is exactly
        /// what URP's Lit takes without an atlas - so the whole machine is baked out of
        /// its own numbers and nothing is sampled off a pack.</summary>
        static Dictionary<int, Material> Materials(EnduroGltf.File file, Part[] parts)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Enduro] no URP Lit shader - is the project on URP? Nothing written.");
                return null;
            }

            var wanted = new SortedSet<int>();
            foreach (var part in parts)
                foreach (var index in part.Used)
                    wanted.Add(index);

            // glTF states base colour LINEAR; an ordinary (non-HDR) colour property is
            // gamma-converted by Unity on the way in, so the gamma value is what such a
            // shader must be handed. GangBikeBaker.WantsLinear is the same question asked
            // of the pack's own shader, and for the same reason.
            var linear = WantsLinear(shader, "_BaseColor");

            var made = new Dictionary<int, Material>();
            foreach (var index in wanted)
            {
                EnduroGltf.ReadMaterial(file, index, out var name, out var colour,
                                        out var metallic, out var roughness);

                // A primitive that names no material at all comes through as -1 and gets
                // the plain grey below rather than an empty renderer slot, which draws
                // magenta and reads as a broken shader rather than a missing colour.
                var asset = "Enduro_" + (index < 0 ? "unpainted" : Clean(name));
                var path = PaintFolder + "/" + asset + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                var fresh = material == null;
                if (fresh) material = new Material(shader);
                else material.shader = shader;

                material.name = asset;
                material.SetColor("_BaseColor", linear ? colour : colour.gamma);
                material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
                material.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));

                if (fresh) AssetDatabase.CreateAsset(material, path);
                else EditorUtility.SetDirty(material);
                made[index] = material;
            }
            return made;
        }

        /// <summary>Whether this shader wants a linear value written into that colour -
        /// true for an [HDR] property, false for the ordinary kind Unity converts for us.
        /// The same question GangBikeBaker asks of the pack's shader, asked here because
        /// the answer belongs to the shader and not to the baker.</summary>
        static bool WantsLinear(Shader shader, string property)
        {
            var index = shader.FindPropertyIndex(property);
            if (index < 0) return false;
            return (shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0;
        }

        static string Clean(string name)
        {
            var built = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                built.Append(char.IsLetterOrDigit(c) ? c : '_');
            return built.ToString();
        }

        // ------------------------------------------------------------------ the pieces

        /// <summary>One of the four objects, filling up as the file is walked: its
        /// vertices in the file's own frame until <see cref="Settle"/> puts them in
        /// Unity's, and its triangles kept apart by material so the merged mesh comes out
        /// submeshed rather than repainted.</summary>
        sealed class Part
        {
            readonly List<Vector3> _positions = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly Dictionary<int, List<int>> _triangles = new Dictionary<int, List<int>>();

            Bounds _box;
            bool _any;

            public bool Empty => !_any;
            public Bounds Box => _box;
            public IEnumerable<int> Used => _triangles.Keys;
            public int Triangles
            {
                get
                {
                    var count = 0;
                    foreach (var list in _triangles.Values) count += list.Count / 3;
                    return count;
                }
            }

            public void Add(EnduroGltf.Primitive primitive)
            {
                var first = _positions.Count;
                for (var i = 0; i < primitive.Positions.Length; i++)
                {
                    var p = primitive.Positions[i];
                    _positions.Add(p);
                    _normals.Add(primitive.Normals != null && i < primitive.Normals.Length
                        ? primitive.Normals[i] : Vector3.up);
                    _uvs.Add(primitive.Uvs != null && i < primitive.Uvs.Length
                        ? primitive.Uvs[i] : Vector2.zero);

                    if (_any) _box.Encapsulate(p);
                    else { _box = new Bounds(p, Vector3.zero); _any = true; }
                }

                if (!_triangles.TryGetValue(primitive.Material, out var list))
                    _triangles[primitive.Material] = list = new List<int>();

                // THE WINDING TURNS ROUND HERE. glTF is right-handed and Unity is not, so
                // the conversion in Settle() mirrors the geometry as far as the numbers go;
                // reversing every triangle is what keeps the faces pointing outwards after
                // it. Miss this and the machine renders inside out - a hollow shell whose
                // far side is all you can see.
                for (var i = 0; i + 2 < primitive.Indices.Length; i += 3)
                {
                    list.Add(first + primitive.Indices[i]);
                    list.Add(first + primitive.Indices[i + 2]);
                    list.Add(first + primitive.Indices[i + 1]);
                }
            }

            /// <summary>Out of glTF's frame and into Unity's, scaled and stood on the
            /// ground.
            ///
            /// glTF is right-handed, Y up, and this model is drawn along x with its nose
            /// at +x. Unity is left-handed with a vehicle's nose at +z. The map that takes
            /// one to the other WITHOUT mirroring the machine - a mirrored bike would have
            /// its exhaust, its brake and its kickstand on the wrong side - is
            /// (x, y, z) -> (z, y, x): the nose lands on +z, up stays up, and the model's
            /// own left hand lands on Unity's +x, which is where the left of a body facing
            /// away from you belongs. The triangles are reversed in <see cref="Add"/> to
            /// go with it.</summary>
            public void Settle(Vector3 origin, float scale)
            {
                for (var i = 0; i < _positions.Count; i++)
                {
                    var p = (_positions[i] - origin) * scale;
                    _positions[i] = new Vector3(p.z, p.y, p.x);
                    var n = _normals[i];
                    _normals[i] = new Vector3(n.z, n.y, n.x);
                }

                var box = new Bounds();
                for (var i = 0; i < _positions.Count; i++)
                {
                    if (i == 0) box = new Bounds(_positions[0], Vector3.zero);
                    else box.Encapsulate(_positions[i]);
                }
                _box = box;
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                // Sixty-eight thousand vertices between the four parts, and the front
                // wheel alone is past what a sixteen-bit index buffer can reach.
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);

                var order = new List<int>(_triangles.Keys);
                order.Sort();
                mesh.subMeshCount = order.Count;
                for (var i = 0; i < order.Count; i++)
                    mesh.SetTriangles(_triangles[order[i]], i);

                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>The renderer's slots, in the same order <see cref="Build"/> laid
            /// the submeshes down.</summary>
            public Material[] Materials(Dictionary<int, Material> made)
            {
                var order = new List<int>(_triangles.Keys);
                order.Sort();
                var slots = new Material[order.Count];
                for (var i = 0; i < order.Count; i++)
                    slots[i] = made.TryGetValue(order[i], out var material) ? material : null;
                return slots;
            }
        }
    }
}
