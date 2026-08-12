using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Painted floor pads in the catalog scene, one per lot size the RoadDemo grid
    /// hands out, so a block can be composed against the size it has to fit into
    /// instead of being measured after the fact.
    ///
    /// A pad is exactly the interior of a block - kerb to kerb, sidewalk excluded -
    /// which is the rectangle a block prefab has to stay inside. Drop catalog
    /// buildings on a pad, keep them on it, and the result drops into the demo
    /// without overhanging the pavement.
    ///
    /// They stand west of the NIGHTCLUBS section, off the end of the showroom rows.
    ///
    /// Run from Tools/City/Draw Block Lot Pads, and re-run any time: the pads are
    /// rebuilt under one root, so nothing accumulates. Building the catalog scene
    /// lays them down too.
    /// </summary>
    public static class BlockLotPads
    {
        internal const string RootName = "BLOCK LOTS";
        const string PadDir = "Assets/CityKit/LotPads";

        /// <summary>The lot sizes RoadDemoBuilder's spacing produces - its
        /// blockWidthRange / blockDepthRange spread over 4 columns and 3 rows.
        /// Change them there and change them here.</summary>
        static readonly float[] Widths = { 70f, 85f, 100f, 115f };
        static readonly float[] Depths = { 50f, 70f, 95f };

        /// <summary>The kit's module. Every pad edge lands on it, so a building
        /// aligned to the pad is aligned to the road tiles as well.</summary>
        const float Cell = 5f;

        const float Gap = 25f;         // walking room between pads
        const float Clearance = 60f;   // fallback: clear of the whole showroom
        const float ClubsGap = 100f;   // clear of the NIGHTCLUBS header's own text

        // One colour per width, so a column of pads reads as one lot width at a
        // glance; the depth is the shape itself.
        static readonly Color[] Paints =
        {
            new Color(0.24f, 0.42f, 0.62f),
            new Color(0.22f, 0.50f, 0.42f),
            new Color(0.62f, 0.48f, 0.20f),
            new Color(0.55f, 0.28f, 0.34f),
        };

        [MenuItem("Tools/City/Draw Block Lot Pads", priority = 6)]
        public static void DrawPads()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SyntyBuildingCatalog.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(SyntyBuildingCatalog.ScenePath,
                                                     OpenSceneMode.Single);
            }

            Draw();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[LotPads] {Widths.Length * Depths.Length} lot pads drawn in " +
                      $"{SyntyBuildingCatalog.ScenePath} under \"{RootName}\".");
        }

        /// <summary>Lays the pads out west of the NIGHTCLUBS section, level with its
        /// first row, or north of the whole showroom when that section is missing.
        /// Safe to call twice - an older set is removed first.</summary>
        internal static void Draw()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == RootName)
                    Object.DestroyImmediate(root);

            var lots = new GameObject(RootName);

            float x0, z0;
            var clubs = GameObject.Find($"{SyntyBuildingCatalog.SectionClubs} header");
            if (clubs)
            {
                // the header is anchored lower-RIGHT, so its text runs west from its
                // pivot: leave room for that before the first pad's east edge
                x0 = Snap(clubs.transform.position.x - ClubsGap - Span(Widths));
                z0 = Snap(clubs.transform.position.z);
            }
            else
            {
                x0 = 0f;
                z0 = Snap(NorthEdge() + Clearance);
            }

            Header($"{RootName}  (interior size, kerb to kerb)",
                   new Vector3(x0 - Gap, 0f, z0), lots.transform);

            float z = z0;
            for (int d = 0; d < Depths.Length; d++)
            {
                float x = x0;
                for (int w = 0; w < Widths.Length; w++)
                {
                    Pad(Code(w, d), x, z, Widths[w], Depths[d], Paints[w % Paints.Length],
                        lots.transform);
                    x = Snap(x + Widths[w] + Gap);
                }
                z = Snap(z + Depths[d] + Gap);
            }
        }

        /// <summary>The pad's name: a letter for the width column (A is the narrowest)
        /// and a number for the depth row (1 is the shallowest), so a lot can be named
        /// out loud - "build this one for C2" - instead of quoted in metres.</summary>
        internal static string Code(int width, int depth) =>
            $"{(char)('A' + width)}{depth + 1}";

        /// <summary>How far the whole set reaches along one axis: every size plus the
        /// gaps between them.</summary>
        static float Span(float[] sizes)
        {
            float span = 0f;
            foreach (float s in sizes) span += s + Gap;
            return span - Gap;
        }

        /// <summary>Far edge of what the scene already holds, so the pads never land
        /// on top of the showroom rows.</summary>
        static float NorthEdge()
        {
            float edge = 0f;
            var found = false;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (!found) { edge = r.bounds.max.z; found = true; }
                else edge = Mathf.Max(edge, r.bounds.max.z);
            }
            return found ? edge : 0f;
        }

        static float Snap(float v) => Mathf.Ceil(v / Cell) * Cell;

        // The pad itself: a flat plane on the lot rectangle, sunk two centimetres so
        // a building standing on it still wins the depth test, and unlit so the
        // colour reads the same whatever the scene lighting does.
        //
        // A Plane, not a Quad: the Plane lies in XZ facing up as built, while the
        // Quad stands in XY with its normal on -Z and has to be turned - turn it the
        // wrong way and the pad faces the ground, invisible from every angle anyone
        // looks at the catalog from. Its mesh is 10 x 10 units, hence the /10.
        static void Pad(string code, float xMin, float zMin, float width, float depth,
                        Color paint, Transform parent)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pad.name = $"Lot {code} ({width:F0}x{depth:F0})";
            var collider = pad.GetComponent<Collider>();
            if (collider) Object.DestroyImmediate(collider);

            pad.transform.SetParent(parent, false);
            pad.transform.position =
                new Vector3(xMin + width * 0.5f, -0.02f, zMin + depth * 0.5f);
            pad.transform.localScale = new Vector3(width / 10f, 1f, depth / 10f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = Paint(paint);

            // north edge, outside the pad: a label there survives the pad filling up
            var text = new GameObject($"{pad.name} label");
            text.transform.SetParent(parent, false);
            text.transform.SetPositionAndRotation(
                new Vector3(xMin + width * 0.5f, 5f, zMin + depth + 4f),
                Quaternion.Euler(35f, 0f, 0f));

            var mesh = text.AddComponent<TextMesh>();
            mesh.text = $"{code}\n{width:F0} x {depth:F0} m\n" +
                        $"{width / Cell:F0} x {depth / Cell:F0} cells";
            mesh.fontSize = 48;
            mesh.characterSize = 0.5f;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;
        }

        static void Header(string title, Vector3 position, Transform parent)
        {
            var header = new GameObject($"{title} header");
            header.transform.SetParent(parent, false);
            header.transform.SetPositionAndRotation(position + new Vector3(0f, 18f, 0f),
                                                    Quaternion.Euler(35f, 0f, 0f));

            var text = header.AddComponent<TextMesh>();
            text.text = title;
            text.fontSize = 96;
            text.characterSize = 0.8f;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = new Color(1f, 0.85f, 0.4f);
        }

        // One material asset per paint, kept out of Assets/CityKit/Catalog: the
        // catalog build deletes that folder wholesale on every run.
        static Material Paint(Color colour)
        {
            EnsureFolder();
            string path = $"{PadDir}/LotPad_{ColorUtility.ToHtmlStringRGB(colour)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                             Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    Debug.LogError("[LotPads] no unlit shader found; pads stay unpainted");
                    return null;
                }
                mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit"))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(PadDir))
                AssetDatabase.CreateFolder("Assets/CityKit", "LotPads");
        }
    }
}
