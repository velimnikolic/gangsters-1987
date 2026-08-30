using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The armoured SUV: one copy of Palm City's SM_Veh_Suv_01 turned into the car a
    /// boss rides in - black body, plate over the doors and sills, a bull bar on the
    /// nose, bars across every side window and a plate on the roof.
    ///
    /// Two decisions carry the whole pass, and both are about memory.
    ///
    /// The colour is NOT a Synty Alts swap. Every palm alt is a 4096px atlas that costs
    /// 42.7 MB resident, so a seventh alt for one unique car is the most expensive way
    /// to change one cell of paint. Instead the body meshes are copied and every paint
    /// vertex is moved onto one black cell the SAME atlas already carries: the SUV's
    /// paint sits at u 0.117-0.136, v 0.855-0.858 (#91382F body, #792B25 its shade), and
    /// the atlas black used here sits at (0.65625, 0.90625), #161616. The conversion
    /// armour uses that exact cell too: there is one black, not a lighter body plus
    /// progressively darker plates and bars. Moving the UVs there costs a mesh copy -
    /// kilobytes - and no new texture at all.
    ///
    /// The armour is likewise one procedural mesh on the same atlas material, so the car
    /// stays a two-material vehicle (body atlas + glass) and batches as it did before.
    /// Its boxes are placed off the measured body, not by eye: the shell runs x +-1.12,
    /// y 0.35-2.38, z -2.77..+2.97 with the nose at +Z, the roof surface sits at y 2.20
    /// -2.25 between roof rails at 2.38, the side glass spans y 1.65-2.07 over z
    /// -1.19..-0.24 (rear door) and -0.03..0.99 (front door), and the front bumper's
    /// underside is at y 0.81.
    ///
    /// The glass gets its own darkened material rather than an edit of Glass_01, which
    /// the pack's BUILDINGS also use - tinting that asset would black out half the city.
    ///
    /// Menu: Tools/City/Vehicles/Bake the armoured SUV. It writes
    /// Assets/Prefabs/Vehicles/SM_Veh_Suv_01_Armoured.prefab as a variant of the Synty
    /// original, so a pack update still flows through, plus one .asset holding the
    /// recoloured meshes and one tinted glass material beside it.
    /// </summary>
    public static class ArmouredSuvBuilder
    {
        const string SourcePath = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Suv_01.prefab";
        const string OutFolder = "Assets/Prefabs/Vehicles";
        const string PrefabPath = OutFolder + "/SM_Veh_Suv_01_Armoured.prefab";
        const string MeshPath = OutFolder + "/SM_Veh_Suv_01_Armoured_Meshes.asset";
        const string GlassPath = OutFolder + "/Glass_Armoured_01.mat";

        // The paint cell of the palm atlas, as the SUV's own UVs report it: the body
        // colour spans u 0.128-0.136 and its darker shade u 0.117-0.121, both at v 0.856.
        const float PaintUMin = 0.114f;
        const float PaintUMax = 0.140f;
        const float PaintVMin = 0.850f;
        const float PaintVMax = 0.863f;

        // Centre of an opaque #161616 cell already in the Palm City atlas. Body paint,
        // its old shade, every plate and every bar all land here so the conversion reads
        // as one black vehicle instead of the old four-step gunmetal treatment.
        static readonly Vector2 UvBlack = new Vector2(0.65625f, 0.90625f);

        [MenuItem("Tools/City/Vehicles/Bake the armoured SUV")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null)
            {
                Debug.LogError("Armoured SUV: no source prefab at " + SourcePath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = "SM_Veh_Suv_01_Armoured";

            // One asset holds every mesh this pass authors, so the folder stays readable.
            var meshHolder = new List<Mesh>();

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                if (!renderer.sharedMaterial.name.StartsWith("PolygonPalmCity")) continue;

                var recoloured = Repaint(filter.sharedMesh);
                if (recoloured == null) continue;
                filter.sharedMesh = recoloured;
                meshHolder.Add(recoloured);
            }

            // The atlas material the armour is to wear, taken off whatever renderer in the
            // body actually has it. Asked of the ROOT it worked only because this one
            // prefab happens to carry a renderer there - and the whole point of saving a
            // VARIANT is that a pack update flows through, which is exactly the update
            // that moves a mesh into a child and turns this line into a null reference.
            Material bodyMaterial = null;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.name.StartsWith("PolygonPalmCity"))
                { bodyMaterial = renderer.sharedMaterial; break; }
            if (bodyMaterial == null)
            {
                Debug.LogError("Armoured SUV: no palm atlas material anywhere on " + SourcePath
                               + "; the armour would have nothing to wear.");
                Object.DestroyImmediate(instance);
                return;
            }

            var armour = BuildArmour();
            meshHolder.Add(armour);

            var armourGo = new GameObject("SM_Veh_Suv_01_Armour");
            armourGo.transform.SetParent(instance.transform, false);
            armourGo.AddComponent<MeshFilter>().sharedMesh = armour;
            armourGo.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

            // The glass darkens through a copy: Glass_01 is shared with the pack's buildings.
            // No copy means the pack moved its glass material: leave the windows as they
            // are rather than assigning null over them, which would render them magenta.
            var glass = TintedGlass(instance);
            if (glass != null)
            {
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith("Glass"))
                        renderer.sharedMaterial = glass;
                }
            }
            else Debug.LogWarning("Armoured SUV: no glass material on the pack's SUV - windows left clear.");

            WriteMeshAsset(meshHolder);

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();

            Debug.Log("Armoured SUV: wrote " + PrefabPath + " (" + meshHolder.Count + " meshes, armour "
                      + armour.vertexCount + " verts) variant=" + (prefab != null
                          && PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant));
        }

        /// <summary>A copy of the mesh with every paint-cell vertex moved onto one black cell.</summary>
        static Mesh Repaint(Mesh source)
        {
            if (source == null) return null;
            var uv = source.uv;
            var moved = 0;
            for (int i = 0; i < uv.Length; i++)
            {
                var p = uv[i];
                if (p.x < PaintUMin || p.x > PaintUMax || p.y < PaintVMin || p.y > PaintVMax) continue;
                uv[i] = UvBlack;
                moved++;
            }
            if (moved == 0) return null;

            var copy = Object.Instantiate(source);
            copy.name = source.name + "_Armoured";
            copy.uv = uv;
            copy.UploadMeshData(false);
            return copy;
        }

        /// <summary>Every plate and bar of the conversion, as one mesh in vehicle space.</summary>
        static Mesh BuildArmour()
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // --- nose: bull bar standing clear of the bumper face at z 2.97 -----------
            for (int rail = 0; rail < 3; rail++)
            {
                float y = rail == 0 ? 0.62f : rail == 1 ? 1.02f : 1.42f;
                Box(verts, norms, uvs, tris, new Vector3(-0.98f, y, 2.90f), new Vector3(0.98f, y + 0.14f, 3.06f), UvBlack);
            }
            foreach (float x in new[] { -0.72f, -0.24f, 0.24f, 0.72f })
                Box(verts, norms, uvs, tris, new Vector3(x - 0.06f, 0.58f, 2.91f), new Vector3(x + 0.06f, 1.60f, 3.05f), UvBlack);
            // the arms that tie it back into the chassis
            foreach (float x in new[] { -0.72f, 0.72f })
                Box(verts, norms, uvs, tris, new Vector3(x - 0.07f, 0.86f, 2.62f), new Vector3(x + 0.07f, 1.02f, 2.94f), UvBlack);
            // skid plate below the bumper, whose underside measures y 0.81
            Box(verts, norms, uvs, tris, new Vector3(-0.90f, 0.62f, 2.55f), new Vector3(0.90f, 0.80f, 3.02f), UvBlack);

            // --- tail: bumper plate on a rear face that ends at z -2.77 ---------------
            Box(verts, norms, uvs, tris, new Vector3(-0.98f, 0.62f, -2.90f), new Vector3(0.98f, 1.06f, -2.74f), UvBlack);
            foreach (float x in new[] { -0.70f, 0.70f })
                Box(verts, norms, uvs, tris, new Vector3(x - 0.08f, 0.58f, -2.96f), new Vector3(x + 0.08f, 1.14f, -2.86f), UvBlack);

            // --- flanks: sill plate, door plate, arch flares --------------------------
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = side < 0 ? -1.16f : 1.04f;
                float outer = side < 0 ? -1.04f : 1.16f;

                // sill, kept inside the wheels at z +-1.9
                Box(verts, norms, uvs, tris, new Vector3(Mathf.Min(inner, outer), 0.44f, -1.28f),
                    new Vector3(Mathf.Max(inner, outer), 0.80f, 1.28f), UvBlack);

                // one plate per door, with a gap on the shut line so the doors still read
                Box(verts, norms, uvs, tris, new Vector3(Mathf.Min(inner, outer) + 0.02f, 0.84f, 0.02f),
                    new Vector3(Mathf.Max(inner, outer) - 0.02f, 1.52f, 1.26f), UvBlack);
                Box(verts, norms, uvs, tris, new Vector3(Mathf.Min(inner, outer) + 0.02f, 0.84f, -1.26f),
                    new Vector3(Mathf.Max(inner, outer) - 0.02f, 1.52f, -0.06f), UvBlack);

                // arch flares over wheels at z +1.95 and -1.88
                float archIn = side < 0 ? -1.34f : 1.02f;
                float archOut = side < 0 ? -1.02f : 1.34f;
                foreach (float wheelZ in new[] { 1.95f, -1.88f })
                    Box(verts, norms, uvs, tris, new Vector3(Mathf.Min(archIn, archOut), 1.13f, wheelZ - 0.78f),
                        new Vector3(Mathf.Max(archIn, archOut), 1.29f, wheelZ + 0.78f), UvBlack);

                // --- window bars: side glass runs y 1.65-2.07 ------------------------
                float barIn = side < 0 ? -1.06f : 1.00f;
                float barOut = side < 0 ? -1.00f : 1.06f;
                float x0 = Mathf.Min(barIn, barOut), x1 = Mathf.Max(barIn, barOut);

                foreach (var window in new[] { new Vector2(-0.03f, 0.99f), new Vector2(-1.19f, -0.24f) })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 1.70f + i * 0.15f;
                        Box(verts, norms, uvs, tris, new Vector3(x0, y, window.x), new Vector3(x1, y + 0.06f, window.y), UvBlack);
                    }
                    float mid = (window.x + window.y) * 0.5f;
                    Box(verts, norms, uvs, tris, new Vector3(x0, 1.66f, mid - 0.04f), new Vector3(x1, 2.06f, mid + 0.04f), UvBlack);
                }
            }

            // --- tailgate window: uprights across glass that runs y 1.59-2.03 ---------
            foreach (float x in new[] { -0.62f, -0.31f, 0f, 0.31f, 0.62f })
                Box(verts, norms, uvs, tris, new Vector3(x - 0.04f, 1.56f, -2.74f), new Vector3(x + 0.04f, 2.06f, -2.66f), UvBlack);

            // --- roof: plate bedded between the pack's own roof rails at y 2.38 -------
            Box(verts, norms, uvs, tris, new Vector3(-0.88f, 2.24f, -2.20f), new Vector3(0.88f, 2.32f, 0.92f), UvBlack);
            foreach (float z in new[] { -1.90f, -0.90f, 0.10f, 0.80f })
                Box(verts, norms, uvs, tris, new Vector3(-0.92f, 2.30f, z - 0.05f), new Vector3(0.92f, 2.38f, z + 0.05f), UvBlack);
            // visor over the windscreen, whose glass tops out at y 2.12 by z 0.9
            Box(verts, norms, uvs, tris, new Vector3(-0.90f, 2.20f, 0.90f), new Vector3(0.90f, 2.30f, 1.24f), UvBlack);

            var mesh = new Mesh { name = "SM_Veh_Suv_01_Armour" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        /// <summary>One axis-aligned box, flat-shaded, every vertex on the one atlas cell.</summary>
        static void Box(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                        Vector3 min, Vector3 max, Vector2 uv)
        {
            var faces = new[]
            {
                new[] { new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z) }, // +Z
                new[] { new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z) }, // -Z
                new[] { new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z) }, // +X
                new[] { new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z) }, // -X
                new[] { new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z) }, // +Y
                new[] { new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z) }, // -Y
            };
            var normals = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };

            for (int f = 0; f < faces.Length; f++)
            {
                int b = verts.Count;
                for (int i = 0; i < 4; i++)
                {
                    verts.Add(faces[f][i]);
                    norms.Add(normals[f]);
                    uvs.Add(uv);
                }
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
        }

        static Material TintedGlass(GameObject instance)
        {
            Material source = null;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith("Glass"))
                {
                    source = renderer.sharedMaterial;
                    break;
                }
            }
            if (source == null) return null;

            var tinted = new Material(source) { name = "Glass_Armoured_01" };
            // The source is green architectural glass. Multiplying it preserved that
            // hue and made the black wagon read as another tone. This private copy gets
            // a neutral smoked black directly; its smoothness and reflections stay.
            var smokedBlack = new Color(0.055f, 0.055f, 0.055f, 0.88f);
            foreach (var prop in new[] { "_BaseColor", "_Color" })
            {
                if (!tinted.HasProperty(prop)) continue;
                tinted.SetColor(prop, smokedBlack);
            }

            AssetDatabase.DeleteAsset(GlassPath);
            AssetDatabase.CreateAsset(tinted, GlassPath);
            return AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
        }

        static void WriteMeshAsset(List<Mesh> meshes)
        {
            AssetDatabase.DeleteAsset(MeshPath);
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i == 0) AssetDatabase.CreateAsset(meshes[i], MeshPath);
                else AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
            }
            AssetDatabase.ImportAsset(MeshPath);
        }
    }
}
