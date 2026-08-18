using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // What a bullet leaves behind. On the ground: a splash of blood past the man
    // in the direction the round was travelling, and under a man who goes down, a
    // pool that spreads - flat quads on the floor, the pack's soft dot for a shape,
    // kept as a fixed pool of the newest few hundred so a long fight never piles
    // up. On the man: he gets bloodier as he gets hurt - a stain on the torso where
    // each round went in, and his whole colour pulled toward red with the damage,
    // through a property block, so a look at him says how close he is to the ground.
    public static class CrewGore
    {
        const int MaxSplats = 240;
        static readonly Color Blood = new Color(0.42f, 0.03f, 0.03f, 0.9f);
        static readonly Color Pool = new Color(0.36f, 0.02f, 0.02f, 0.95f);
        static readonly Color StainOnCloth = new Color(0.30f, 0.02f, 0.02f, 0.92f);
        static readonly Color Bloodied = new Color(0.72f, 0.28f, 0.24f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static Material splatMaterial;
        static Transform root;
        static readonly Queue<GameObject> splats = new Queue<GameObject>();
        static readonly Dictionary<CrewWalker, MaterialPropertyBlock> blocks = new Dictionary<CrewWalker, MaterialPropertyBlock>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            splatMaterial = null;
            root = null;
            splats.Clear();
            blocks.Clear();
        }

        // ------------------------------------------------------------------ ground

        /// <summary>A round hit this man: blood on the ground behind him along the
        /// shot's line, and a stain on him.</summary>
        public static void Hit(CrewWalker man, Vector3 from, float groundY)
        {
            if (man == null || man.Tf == null) return;
            var line = man.Tf.position - from;
            line.y = 0f;
            var dir = line.sqrMagnitude > 1e-3f ? line.normalized : -man.Tf.forward;
            // exit side: past him, a little to either hand
            var spot = man.Tf.position + dir * Random.Range(0.4f, 1.4f) +
                       Vector3.Cross(Vector3.up, dir) * Random.Range(-0.5f, 0.5f);
            Splat(spot, groundY, Random.Range(0.35f, 0.7f), Blood);
            Stain(man, from);
            Colour(man);
        }

        /// <summary>He is down: the pool under him, and his colour gone.</summary>
        public static void Death(CrewWalker man, float groundY)
        {
            if (man == null || man.Tf == null) return;
            // he falls forward of where he stood, more or less; the pool goes with him
            var spot = man.Tf.position + man.Tf.forward * Random.Range(0.2f, 0.7f);
            Splat(spot, groundY, Random.Range(1.3f, 1.9f), Pool);
            Splat(spot + man.Tf.right * Random.Range(-0.4f, 0.4f), groundY, Random.Range(0.6f, 1f), Blood);
            Colour(man);
        }

        static void Splat(Vector3 at, float groundY, float size, Color tint)
        {
            var sprite = DemoUi.Dot;
            if (sprite == null) return;
            if (splatMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                if (shader == null) return;
                splatMaterial = new Material(shader) { name = "Blood" };
                splatMaterial.mainTexture = sprite.texture;
                // URP Unlit: transparent surface, alpha blend, no depth write, drawn late
                splatMaterial.SetFloat("_Surface", 1f);
                splatMaterial.SetFloat("_Blend", 0f);
                splatMaterial.SetFloat("_ZWrite", 0f);
                splatMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                splatMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                splatMaterial.SetOverrideTag("RenderType", "Transparent");
                splatMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                splatMaterial.renderQueue = 3000;
            }
            if (root == null) root = new GameObject("Blood").transform;

            GameObject go;
            if (splats.Count >= MaxSplats)
            {
                go = splats.Dequeue();
                if (go == null) go = NewSplat();
            }
            else go = NewSplat();
            splats.Enqueue(go);

            go.transform.SetPositionAndRotation(
                new Vector3(at.x, groundY + 0.012f + Random.value * 0.004f, at.z),
                Quaternion.Euler(90f, Random.value * 360f, 0f));
            go.transform.localScale = new Vector3(size * Random.Range(0.8f, 1.2f), size, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            go.SetActive(true);
        }

        static GameObject NewSplat()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Splat";
            var col = go.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = splatMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(root, false);
            return go;
        }

        // ------------------------------------------------------------------ the man

        // A stain where the round went in: a small quad glued to the torso, on the
        // side facing the shooter, so the shirt front or the back reads shot.
        static void Stain(CrewWalker man, Vector3 from)
        {
            if (splatMaterial == null) return;
            var animator = man.Tf.GetComponentInChildren<Animator>();
            var chest = animator ? animator.GetBoneTransform(HumanBodyBones.Chest) ??
                                   animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (chest == null) return;

            var toShooter = from - chest.position;
            toShooter.y = 0f;
            if (toShooter.sqrMagnitude < 1e-3f) toShooter = -man.Tf.forward;
            toShooter.Normalize();
            // out from the spine to the skin, on the shooter's side, a hand's width off centre
            var side = Vector3.Cross(Vector3.up, toShooter);
            var at = chest.position + toShooter * 0.16f + side * Random.Range(-0.1f, 0.1f) +
                     Vector3.up * Random.Range(-0.12f, 0.12f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Stain";
            var col = go.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = splatMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, StainOnCloth);
            block.SetColor(ColorId, StainOnCloth);
            mr.SetPropertyBlock(block);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-toShooter, Vector3.up));
            float s = Random.Range(0.14f, 0.24f);
            go.transform.localScale = new Vector3(s, s * Random.Range(0.8f, 1.3f), 1f);
            go.transform.SetParent(chest, true);
            go.layer = man.Tf.gameObject.layer;
        }

        // The whole man pulled toward blood-red with the damage: a property block on
        // every renderer he owns, the same trick BuildingCardPicker highlights with,
        // never a material instance.
        static void Colour(CrewWalker man)
        {
            float frac = man.MaxHealth > 0 ? 1f - Mathf.Clamp01(man.Health / (float)man.MaxHealth) : 1f;
            if (man.Dead) frac = 1f;
            var tint = Color.Lerp(Color.white, Bloodied, frac * 0.75f);
            if (!blocks.TryGetValue(man, out var block))
                blocks[man] = block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            foreach (var r in man.Tf.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Stain" || r.name == "Splat") continue;
                // the gun keeps its own colour
                if (man.Weapon != null && r.transform.IsChildOf(man.Weapon)) continue;
                r.SetPropertyBlock(block);
            }
        }
        // ------------------------------------------------------------------ the chalk

        static Material chalkMaterial;

        // The outline the police draw round a body: a chalk line on the ground in the
        // shape of a man lying as this one lay - head end where his head was, one arm
        // flung up, the legs apart - drawn as a closed polyline (LineRenderer laid flat),
        // a hand's wobble on every point so no two are the same. It stays after the
        // body is taken away.
        static readonly Vector2[] Figure =
        {
            new Vector2(0.00f, 0.85f), new Vector2(0.06f, 0.85f), new Vector2(0.10f, 0.05f),
            new Vector2(0.28f, 0.02f), new Vector2(0.30f, 0.14f), new Vector2(0.24f, 0.88f),
            new Vector2(0.27f, 1.05f), new Vector2(0.44f, 1.00f), new Vector2(0.52f, 0.60f),
            new Vector2(0.62f, 0.62f), new Vector2(0.56f, 1.06f), new Vector2(0.35f, 1.36f),
            new Vector2(0.20f, 1.46f), new Vector2(0.15f, 1.55f), new Vector2(0.14f, 1.66f),
            new Vector2(0.06f, 1.76f), new Vector2(-0.06f, 1.76f), new Vector2(-0.14f, 1.66f),
            new Vector2(-0.15f, 1.55f), new Vector2(-0.20f, 1.46f), new Vector2(-0.34f, 1.36f),
            new Vector2(-0.56f, 1.52f), new Vector2(-0.62f, 1.86f), new Vector2(-0.50f, 1.92f),
            new Vector2(-0.44f, 1.62f), new Vector2(-0.28f, 1.42f), new Vector2(-0.28f, 1.05f),
            new Vector2(-0.30f, 0.14f), new Vector2(-0.28f, 0.02f), new Vector2(-0.10f, 0.05f),
            new Vector2(-0.06f, 0.85f),
        };

        /// <summary>Draw the outline where this man lies - the figure's head end toward
        /// his head, its feet toward his feet - and hand back the object, so the body
        /// can go and the chalk stay.</summary>
        public static GameObject Chalk(CrewWalker man, float groundY)
        {
            if (man == null || man.Tf == null) return null;
            if (chalkMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return null;
                chalkMaterial = new Material(shader) { name = "Chalk" };
                chalkMaterial.SetColor(BaseColorId, new Color(0.93f, 0.93f, 0.9f, 1f));
                chalkMaterial.SetColor(ColorId, new Color(0.93f, 0.93f, 0.9f, 1f));
            }
            if (root == null) root = new GameObject("Blood").transform;

            // where he lies: head and hips off the rig if it has them, else his forward
            var animator = man.Tf.GetComponentInChildren<Animator>();
            var head = animator ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var hips = animator ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Vector3 centre, up;
            if (head != null && hips != null)
            {
                var h = head.position; h.y = 0f;
                var p = hips.position; p.y = 0f;
                up = h - p;
                up.y = 0f;
                if (up.sqrMagnitude < 0.05f) up = man.Tf.forward;
                up.Normalize();
                centre = p - up * 0.85f; // the figure's origin sits between the feet
            }
            else
            {
                up = man.Tf.forward;
                up.y = 0f;
                up.Normalize();
                centre = man.Tf.position;
            }
            centre.y = groundY + 0.03f;

            var go = new GameObject("Chalk Outline");
            go.transform.SetParent(root, false);
            // local X across the body, local Z along it, local Y up: the strip is built
            // flat in the XZ plane and the object stands on the ground where he lay
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(up, Vector3.up));

            // The outline as a mesh of its own - a closed strip a hand wide, one quad
            // per segment with mitred joins - rather than a LineRenderer: geometry the
            // pipeline draws like any other, from any angle, with no alignment mode to
            // get wrong.
            const float half = 0.045f; // a chalk line, a finger and a half wide
            int n = Figure.Length;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var f = Figure[i];
                pts[i] = new Vector3(f.x + Random.Range(-0.02f, 0.02f), 0f, f.y + Random.Range(-0.02f, 0.02f));
            }
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                var prev = pts[(i - 1 + n) % n];
                var next = pts[(i + 1) % n];
                var dirIn = (pts[i] - prev).normalized;
                var dirOut = (next - pts[i]).normalized;
                var tangent = (dirIn + dirOut).normalized;
                if (tangent.sqrMagnitude < 1e-4f) tangent = dirOut;
                var normal = new Vector3(-tangent.z, 0f, tangent.x); // across the line, on the ground
                verts[i * 2] = pts[i] + normal * half;
                verts[i * 2 + 1] = pts[i] - normal * half;
                uvs[i * 2] = new Vector2(0f, i / (float)n);
                uvs[i * 2 + 1] = new Vector2(1f, i / (float)n);
            }
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = ((i + 1) % n) * 2, d = ((i + 1) % n) * 2 + 1;
                // both windings, so the strip shows from above whichever way it lands
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = "Chalk" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = chalkMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
