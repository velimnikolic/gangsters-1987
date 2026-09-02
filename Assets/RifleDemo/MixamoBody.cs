using UnityEngine;

namespace RifleDemo
{
    /// <summary>
    /// The pack's own preview body, and what a Synty "reskin" of it actually means.
    ///
    /// Nothing here ships. It is a bench answer to one question - how does the Mixamo
    /// figure the takes were authored on look next to this city's cast, and can it be
    /// dressed in the city's palette - and the answer is worth having in code because
    /// the naive form of it (hang a Synty material on a foreign mesh) is the one
    /// everybody tries first and it does not work.
    ///
    /// WHY THE PLAIN SWAP FAILS. A Synty character material is not a skin, it is an
    /// ATLAS: a grid of flat colour squares, and every Synty body is unwrapped so that
    /// each of its islands lands inside the square it is meant to be. The Mixamo body is
    /// unwrapped for its OWN painted texture, so laid on the Synty atlas its islands
    /// land wherever they happen to, and the man comes out in confetti.
    ///
    /// WHAT WORKS. The same fact, used the other way round: because the atlas is flat
    /// squares, a mesh whose UVs all sit on ONE point of it renders in one flat colour -
    /// the palette's colour, out of the palette's own material and shader. So the body
    /// is re-unwrapped onto points taken off a real Synty character's mesh, and comes
    /// out in this city's paint. Crude next to a proper re-unwrap, and enough to see
    /// whether the figure belongs on these streets at all.
    /// </summary>
    public static class MixamoBody
    {
        public const string Path = "Assets/Animations/Mixamo/Rifle/Ch15_nonPBR.fbx";

        public enum Skin
        {
            /// <summary>As the pack ships it: its own painted texture.</summary>
            Original,

            /// <summary>The Synty material laid straight on, UVs untouched - the swap
            /// everybody tries first, kept so the confetti can be seen rather than
            /// described.</summary>
            SyntyAtlas,

            /// <summary>Re-unwrapped onto single points of the Synty palette, so the
            /// body renders in the city's own flat colours out of the city's own
            /// material.</summary>
            SyntyFlat
        }

        public static GameObject Source =>
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path);
#else
            null;
#endif

        /// <summary>Dress an instantiated preview body. <paramref name="reference"/> is
        /// any city character - it is where both the material and the palette points
        /// come from, so the bench never hard-codes a Synty path or a colour.</summary>
        public static void Dress(GameObject body, GameObject reference, Skin skin)
        {
            if (body == null || skin == Skin.Original) return;

            var material = MaterialOf(reference);
            if (material == null)
            {
                Debug.LogWarning("[MixamoBody] The city cast brought no material to " +
                                 "reskin with; the body keeps its own.");
                return;
            }

            Vector2 cloth = PaletteAt(reference, 0.45f);
            Vector2 skinTone = PaletteAt(reference, 0.97f);

            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                renderer.sharedMaterials = mats;

                if (skin != Skin.SyntyFlat || renderer.sharedMesh == null) continue;

                // The mesh is CLONED - the imported asset is never written to, the same
                // discipline every other bench here keeps with the packs.
                var mesh = Object.Instantiate(renderer.sharedMesh);
                mesh.name = renderer.sharedMesh.name + " (Synty palette)";
                var uv = new Vector2[mesh.vertexCount];
                for (int i = 0; i < uv.Length; i++) uv[i] = cloth;
                // The head and hands take the skin square, so a face is a face.
                var head = mesh.bounds.max.y - mesh.bounds.size.y * 0.16f;
                var verts = mesh.vertices;
                for (int i = 0; i < uv.Length && i < verts.Length; i++)
                    if (verts[i].y > head) uv[i] = skinTone;
                mesh.uv = uv;
                renderer.sharedMesh = mesh;
            }
        }

        static Material MaterialOf(GameObject reference)
        {
            if (reference == null) return null;
            foreach (var renderer in reference.GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null) return renderer.sharedMaterial;
            return null;
        }

        /// <summary>A point on the palette, taken off a REAL Synty character's mesh -
        /// the vertex at a given height up its own body. Low is trousers and shoes, high
        /// is head and hands, and whatever square those sit in is the square this city
        /// paints them from. No atlas coordinates written down anywhere, so a change of
        /// pack changes the answer by itself.</summary>
        static Vector2 PaletteAt(GameObject reference, float height)
        {
            foreach (var renderer in reference.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.uv == null || mesh.uv.Length == 0) continue;
                var verts = mesh.vertices;
                var uv = mesh.uv;
                float low = mesh.bounds.min.y, span = Mathf.Max(0.001f, mesh.bounds.size.y);
                int best = -1;
                float bestGap = float.MaxValue;
                for (int i = 0; i < verts.Length && i < uv.Length; i++)
                {
                    float t = (verts[i].y - low) / span;
                    float gap = Mathf.Abs(t - height);
                    if (gap < bestGap) { bestGap = gap; best = i; }
                }

                if (best >= 0) return uv[best];
            }

            return new Vector2(0.5f, 0.5f);
        }
    }
}
