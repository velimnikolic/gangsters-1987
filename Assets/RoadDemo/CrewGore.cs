using System.Collections.Generic;
using LivingCity.Ambient;
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
        // The authored texture carries the blood colour and wet highlights. These
        // neutral tints preserve them instead of multiplying everything into flat red.
        static readonly Color Blood = new Color(0.86f, 0.72f, 0.7f, 0.88f);
        static readonly Color Pool = new Color(0.7f, 0.54f, 0.52f, 0.94f);
        static readonly Color StainOnCloth = new Color(0.7f, 0.56f, 0.54f, 0.88f);
        static readonly Color Bloodied = new Color(0.72f, 0.28f, 0.24f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static Material splatMaterial;
        static Transform root;
        static readonly Queue<GameObject> splats = new Queue<GameObject>();
        static readonly Dictionary<PedestrianAgent, MaterialPropertyBlock> blocks = new Dictionary<PedestrianAgent, MaterialPropertyBlock>();

        // ------------------------------------------------------------- the weather
        // NOTHING ON THE PAVEMENT IS FOREVER. Blood and chalk used to be retired only by
        // the caps in here, and a cap is not time: a quiet city kept every pool and every
        // outline for the whole session, so a corner where one man died in the first hour
        // still read as a crime scene days later. Every mark on the ground now holds its
        // colour for a while and then goes out - rain, traffic, and the men with the hose.
        //
        // Counted in GAME hours off the city's own clock, never in real seconds: the
        // scenes run their clocks at anything from 15 to 600 real seconds to the hour, and
        // a fade written in real time would be a week in one scene and a blink in another.
        const float SplatHold = 2f;    // game hours a pool stays as dark as it fell
        const float SplatFade = 6f;    // and the hours it takes to wash out of the stone
        const float ChalkHold = 8f;    // the outline stays while the case is fresh
        const float ChalkFade = 16f;   // and is gone a day after the body went
        const float SweepStep = 0.02f; // game hours between passes over the yard

        struct Mark
        {
            public GameObject Go;
            public MeshRenderer Mr;
            public Color Tint;   // the colour it was laid in; the fade only takes its alpha
            public float Born;   // game hours
            public float Hold, Fade;
            public bool Chalk;   // owns a mesh of its own: destroyed at the end, not pooled
        }

        static readonly List<Mark> marks = new List<Mark>();
        // One block for every mark that is written this frame. A renderer keeps its own
        // copy of whatever is handed to SetPropertyBlock, so the same one serves them all.
        static readonly MaterialPropertyBlock scratch = new MaterialPropertyBlock();
        static float sweepAt;

        // The chalk outlines, capped like the splats. Each is a GameObject with a mesh
        // all its own (never batched), one laid on every death and, before this cap, kept
        // for the rest of the session: a violent night piled thousands of un-batchable
        // draw calls and leaked meshes under the Blood node until the frame rate fell
        // over. The oldest is retired - object AND mesh - once the yard is this full.
        const int MaxChalk = 120;
        static readonly Queue<GameObject> chalks = new Queue<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            splatMaterial = null;
            root = null;
            splats.Clear();
            chalks.Clear();
            marks.Clear();
            sweepAt = 0f;
            blocks.Clear();
            holeMaterial = null;
            holes.Clear();
        }

        // ------------------------------------------------------------------ ground

        /// <summary>A round hit this man: blood on the ground behind him along the
        /// shot's line, and a stain on him.</summary>
        public static void Hit(PedestrianAgent man, Vector3 from, float groundY, bool floor = true)
        {
            if (man == null || man.Tf == null) return;
            var line = man.Tf.position - from;
            line.y = 0f;
            var dir = line.sqrMagnitude > 1e-3f ? line.normalized : -man.Tf.forward;
            var wound = man is CrewWalker crew ? crew.ChestPosition
                : man.Tf.position + Vector3.up * 1.2f;
            bool fatal = man is CrewWalker walker ? walker.Dead
                : man is CivilianAgent civilian && civilian.Dead;
            BloodFx.PlayHit(wound, dir, fatal);
            // exit side: past him, a little to either hand
            var spot = man.Tf.position + dir * Random.Range(0.4f, 1.4f) +
                       Vector3.Cross(Vector3.up, dir) * Random.Range(-0.5f, 0.5f);
            if (floor) Splat(spot, groundY, Random.Range(0.35f, 0.7f), Blood);
            Stain(man, from);
            Colour(man);
        }

        /// <summary>Nonfatal presentation: progressive blood on face and clothing,
        /// independent of the owner's simulation health or business consequences.</summary>
        public static void BeatingHit(PedestrianAgent man, Vector3 from, float groundY, float severity)
        {
            if (man?.Tf == null) return;
            Hit(man, from, groundY);
            Stain(man, from, face: true);
            Stain(man, from, size: 1.6f);
            Colour(man, Mathf.Clamp01(severity));
        }

        /// <summary>He is down: the pool under him, and his colour gone.</summary>
        public static void Death(PedestrianAgent man, float groundY, bool floor = true)
        {
            if (man == null || man.Tf == null) return;
            // no pool on the road for a man who died in the car
            if (floor)
            {
                // he falls forward of where he stood, more or less; the pool goes with him
                var spot = man.Tf.position + man.Tf.forward * Random.Range(0.2f, 0.7f);
                Splat(spot, groundY, Random.Range(1.3f, 1.9f), Pool);
                Splat(spot + man.Tf.right * Random.Range(-0.4f, 0.4f), groundY, Random.Range(0.6f, 1f), Blood);
            }
            Colour(man);
            Forget(man);
        }

        /// <summary>Drop the man's tint block. The renderers keep their own copy of it
        /// once it is set, so nothing is lost - and a dead, chalked or despawned man
        /// otherwise kept an entry in the table for the rest of the session.</summary>
        public static void Forget(PedestrianAgent man)
        {
            if (man != null) blocks.Remove(man);
        }

        static void Splat(Vector3 at, float groundY, float size, Color tint)
        {
            if (!EnsureSplatMaterial()) return;
            EnsureRoot();

            // A quad that has already faded out is standing there switched off: take that
            // one back before making another. The queue is oldest first, so the head is
            // the one most likely to be spent.
            GameObject go = null;
            if (splats.Count > 0)
            {
                var head = splats.Peek();
                if (head == null || !head.activeSelf)
                {
                    go = splats.Dequeue();
                    if (go == null) go = NewSplat();
                }
            }
            if (go == null)
            {
                if (splats.Count >= MaxSplats)
                {
                    go = splats.Dequeue();
                    if (go == null) go = NewSplat();
                }
                else go = NewSplat();
            }
            splats.Enqueue(go);

            go.transform.SetPositionAndRotation(
                new Vector3(at.x, groundY + 0.012f + Random.value * 0.004f, at.z),
                Quaternion.Euler(90f, Random.value * 360f, 0f));
            go.transform.localScale = new Vector3(size * Random.Range(0.8f, 1.2f), size, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            scratch.SetColor(BaseColorId, tint);
            scratch.SetColor(ColorId, tint);
            mr.SetPropertyBlock(scratch);
            go.SetActive(true);
            Track(go, mr, tint, SplatHold, SplatFade, chalk: false);
        }

        static bool EnsureSplatMaterial()
        {
            if (splatMaterial != null) return true;
            var texture = BloodFx.SplatterTexture;
            if (texture == null) return false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Transparent");
            if (shader == null) return false;
            splatMaterial = new Material(shader) { name = "Realistic Blood Decal" };
            splatMaterial.mainTexture = texture;
            if (splatMaterial.HasProperty("_BaseMap"))
                splatMaterial.SetTexture("_BaseMap", texture);
            MakeTransparent(splatMaterial);
            return true;
        }

        /// <summary>The transparent setup a mark on the ground needs, and the reason the
        /// fade works at all: an opaque URP Unlit throws away whatever alpha a property
        /// block writes, so a material set up any other way simply pops out of existence
        /// at the end of its life instead of going.</summary>
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
        }

        // ------------------------------------------------------------- ageing

        /// <summary>The node every mark on the ground hangs off, with the one component
        /// that ages them. A mark is never a MonoBehaviour of its own: a violent night
        /// would put four hundred Updates on the frame.</summary>
        static Transform EnsureRoot()
        {
            if (root == null)
            {
                var go = new GameObject("Blood");
                go.AddComponent<GoreFade>();
                root = go.transform;
            }
            return root;
        }

        /// <summary>Hours the city has run, days folded in so the number only ever climbs.
        /// A scene with no clock (a bare bench) is read at a game hour to the real minute,
        /// so its marks still go.</summary>
        static float GameHours
        {
            get
            {
                var clock = DayClock.Current;
                return clock != null ? clock.Day * 24f + clock.Hour : Time.time / 60f;
            }
        }

        static void Track(GameObject go, MeshRenderer mr, Color tint, float hold, float fade, bool chalk)
        {
            // a quad taken back out of the pool is a new mark: its old life is struck off,
            // or the fade would carry on ageing the fresh splash drawn over it
            for (int i = marks.Count - 1; i >= 0; i--)
                if (marks[i].Go == go) marks.RemoveAt(i);
            marks.Add(new Mark
            {
                Go = go, Mr = mr, Tint = tint, Born = GameHours,
                Hold = hold, Fade = fade, Chalk = chalk,
            });
        }

        /// <summary>Ages every mark on the ground: full colour while it holds, then out
        /// over its fade, then gone - a splat switched off and left in the pool, an
        /// outline destroyed with the mesh it owns.</summary>
        internal static void Age()
        {
            float now = GameHours;
            if (now < sweepAt) return;
            sweepAt = now + SweepStep;
            PruneChalks();
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var mark = marks[i];
                if (mark.Go == null || mark.Mr == null) { marks.RemoveAt(i); continue; }
                float age = now - mark.Born;
                if (age < 0f)
                {
                    // the clock arrived after the mark did - a scene builds its city before
                    // it builds its clock - so take the reading again rather than wash a
                    // fresh pool off the stone at once.
                    mark.Born = now;
                    marks[i] = mark;
                    continue;
                }
                if (age <= mark.Hold) continue;
                float left = 1f - (age - mark.Hold) / mark.Fade;
                if (left <= 0f)
                {
                    if (mark.Chalk)
                    {
                        var mf = mark.Go.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                        Object.Destroy(mark.Go);
                    }
                    else mark.Go.SetActive(false); // back to the pool, not destroyed
                    marks.RemoveAt(i);
                    continue;
                }
                var tint = mark.Tint;
                tint.a *= left;
                scratch.SetColor(BaseColorId, tint);
                scratch.SetColor(ColorId, tint);
                mark.Mr.SetPropertyBlock(scratch);
            }
        }

        /// <summary>Outlines fade in the order they were drawn, so the spent ones are at
        /// the head. Taken off the queue here rather than where they are destroyed: Unity
        /// does not finish a Destroy until the end of the frame, so the reference only
        /// reads null on the next pass.</summary>
        static void PruneChalks()
        {
            while (chalks.Count > 0 && chalks.Peek() == null) chalks.Dequeue();
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
        static void Stain(PedestrianAgent man, Vector3 from, bool face = false, float size = 1f)
        {
            if (!EnsureSplatMaterial()) return;
            var animator = man.Tf.GetComponentInChildren<Animator>();
            var chest = animator ? animator.GetBoneTransform(face ? HumanBodyBones.Head : HumanBodyBones.Chest) ??
                                   animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (chest == null) return;

            var toShooter = from - chest.position;
            toShooter.y = 0f;
            if (toShooter.sqrMagnitude < 1e-3f) toShooter = -man.Tf.forward;
            toShooter.Normalize();
            // out from the spine to the skin, on the shooter's side, a hand's width off centre
            var side = Vector3.Cross(Vector3.up, toShooter);
            var at = chest.position + toShooter * (face ? 0.105f : 0.16f) +
                     side * Random.Range(face ? -0.035f : -0.1f, face ? 0.035f : 0.1f) +
                     Vector3.up * Random.Range(face ? 0.02f : -0.12f, face ? 0.06f : 0.12f);
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
            float s = Random.Range(0.14f, face ? 0.18f : 0.24f) * size;
            go.transform.localScale = new Vector3(s, s * Random.Range(0.8f, 1.3f), 1f);
            go.transform.SetParent(chest, true);
            go.layer = man.Tf.gameObject.layer;
        }

        // The whole man pulled toward blood-red with the damage: a property block on
        // every renderer he owns, the same trick BuildingCardPicker highlights with,
        // never a material instance.
        static void Colour(PedestrianAgent man, float visibleSeverity = -1f)
        {
            float frac = 1f;
            Transform gun = null;
            if (man is CrewWalker cw)
            {
                frac = cw.MaxHealth > 0 ? 1f - Mathf.Clamp01(cw.Health / (float)cw.MaxHealth) : 1f;
                if (cw.Dead) frac = 1f;
                gun = cw.Weapon;
            }
            else if (man is CivilianAgent civ)
                frac = civ.Dead ? 1f : 1f - Mathf.Clamp01(civ.Health / 2f);
            if (visibleSeverity >= 0f) frac = Mathf.Max(frac, Mathf.Clamp01(visibleSeverity));
            var tint = Color.Lerp(Color.white, Bloodied, frac * 0.75f);
            if (!blocks.TryGetValue(man, out var block))
                blocks[man] = block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            foreach (var r in man.Tf.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Stain" || r.name == "Splat") continue;
                // the gun keeps its own colour
                if (gun != null && r.transform.IsChildOf(gun)) continue;
                r.SetPropertyBlock(block);
            }
        }
        // ------------------------------------------------------------------ the chalk

        static Material chalkMaterial;
        static readonly Color ChalkLine = new Color(0.93f, 0.93f, 0.9f, 1f);

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

        // ------------------------------------------------------------------ tin

        static Material holeMaterial;
        static readonly Color HoleInk = new Color(0.06f, 0.06f, 0.07f, 1f);
        const int MaxHoles = 160;
        static readonly Queue<GameObject> holes = new Queue<GameObject>();

        /// <summary>A round that went into a car's tin instead of the man behind it.
        /// <paramref name="outward"/> is the PANEL'S OWN NORMAL - which way the tin it
        /// went through is facing - not the way back to the shooter.
        ///
        /// PARENTED TO THE BODY, unlike everything else in here: blood and chalk belong
        /// to the road and stay where the road is, and a hole belongs to the car and
        /// goes where the car goes. Pooled like the splats so a long fight cannot fill
        /// a scene with them.</summary>
        public static void Hole(Transform body, Vector3 at, Vector3 outward)
        {
            if (body == null) return;
            if (holeMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                holeMaterial = new Material(shader) { name = "Bullet Hole" };
                // A HOLE IS ROUND. The dot the blood is drawn with, inked near-black:
                // an opaque quad at the size a hole has to be to read at the game's own
                // boom is a black SQUARE stuck to a door, which is what the player saw.
                // Same sprite, same transparent setup as Splat - the alpha is what makes
                // it a hole instead of a tile.
                var dot = DemoUi.Dot;
                if (dot != null)
                {
                    holeMaterial.mainTexture = dot.texture;
                    holeMaterial.SetFloat("_Surface", 1f);
                    holeMaterial.SetFloat("_Blend", 0f);
                    holeMaterial.SetFloat("_ZWrite", 0f);
                    holeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    holeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    holeMaterial.SetOverrideTag("RenderType", "Transparent");
                    holeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    holeMaterial.renderQueue = 3000;
                }
                holeMaterial.SetColor(BaseColorId, HoleInk);
                holeMaterial.SetColor(ColorId, HoleInk);
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Hole";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = holeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var n = outward.sqrMagnitude < 1e-6f ? body.right : outward.normalized;
            var tf = quad.transform;
            tf.SetParent(body, worldPositionStays: true);
            // FLUSH TO THE TIN, a finger's width proud of it. Turned to face the SHOOTER
            // it stood at an angle across the panel and read as a card floating beside
            // the car - a hole in a door lies in the plane of the door, whoever fired it.
            tf.position = at + n * 0.02f;
            // A Unity quad's visible face is -forward, NOT +forward: Splat lies flat with
            // Euler(90,..), which points its forward at the ground, and it is the face you
            // see from above. So the quad's forward goes INTO the panel and its face
            // comes out along the normal - the same trick Stain plays with -toShooter.
            tf.rotation = Quaternion.LookRotation(-n, Vector3.up);
            // Sized off the marks the game already reads at, not off a real bullet. The
            // boom stands at 170m through a 45 degree lens: 141m of world over 1080 pixels,
            // 0.13m to the pixel. The 6cm hole this used to draw was half a pixel wide.
            // A stain on a man's shirt is 0.14-0.24 (Stain) and reads; a round dot can
            // sit at the smaller end of that because its corners are not there.
            float size = Random.Range(0.13f, 0.19f);
            tf.localScale = new Vector3(size, size, size);
            quad.layer = body.gameObject.layer;

            holes.Enqueue(quad);
            while (holes.Count > MaxHoles)
            {
                var old = holes.Dequeue();
                if (old != null) Object.Destroy(old);
            }
        }

        /// <summary>Draw the outline where this man lies - the figure's head end toward
        /// his head, its feet toward his feet - and hand back the object, so the body
        /// can go and the chalk stay.</summary>
        public static GameObject Chalk(PedestrianAgent man, float groundY)
        {
            if (man == null || man.Tf == null) return null;
            Forget(man);
            if (chalkMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return null;
                chalkMaterial = new Material(shader) { name = "Chalk" };
                chalkMaterial.SetColor(BaseColorId, ChalkLine);
                chalkMaterial.SetColor(ColorId, ChalkLine);
                MakeTransparent(chalkMaterial);
            }
            EnsureRoot();

            // where he lies: head and hips off the rig if it has them, else his forward
            var animator = man.Tf.GetComponentInChildren<Animator>();
            var head = animator ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var hips = CrewArms.Pelvis(animator);
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
            Track(go, mr, ChalkLine, ChalkHold, ChalkFade, chalk: true);

            // hold the yard to MaxChalk: the oldest outline is taken up, its own mesh
            // freed with it (a bare Destroy of the object would leak the Mesh asset)
            chalks.Enqueue(go);
            if (chalks.Count > MaxChalk)
            {
                var old = chalks.Dequeue();
                if (old != null)
                {
                    var mf = old.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                    Object.Destroy(old);
                }
            }
            return go;
        }
    }

    /// <summary>The one thing on the Blood node: it ages what is drawn on the ground.
    /// Deliberately dumb - the bookkeeping is CrewGore's, because CrewGore is what lays
    /// the marks and what pools them.</summary>
    public sealed class GoreFade : MonoBehaviour
    {
        void Update() => CrewGore.Age();
    }
}
