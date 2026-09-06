using System;
using System.Collections.Generic;
using LivingCity.Business;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadDemo
{
    /// <summary>Disposable cosmetic projection. Step performs one bounded unit of work;
    /// callers share their frame budget across views. Never composes or edits navigation.</summary>
    internal sealed class ResidentialConditionView : IDisposable
    {
        const int MaxProps = 64;
        static readonly HashSet<Transform> dynamicParts = new HashSet<Transform>();
        public static bool IsDynamic(Transform part) => dynamicParts.Contains(part);
        // The dressing roots alone: pooled litter and props the view stands and takes
        // back. A building's own surfaces used to count as dynamic too, which kept every
        // wall out of the block merge (4,562 of 5,900 renderers in twelve blocks,
        // 2026-09-06); their wear rides on shared materials, and the recycler rebuilds a
        // block whose wear threshold or decoration density moves (the user's rule:
        // blocks are not dynamic, and what repeats is merged).
        static readonly HashSet<Transform> dressingRoots = new HashSet<Transform>();
        public static bool IsDressing(Transform part) => dressingRoots.Contains(part);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { dynamicParts.Clear(); dressingRoots.Clear(); }

        sealed class Surface
        {
            public MeshRenderer renderer;
            public Material[] original, worn;
            public bool decoration, hidden, usingWorn, wilt;
            public Vector3 scale;
            public Quaternion rotation;
            public Transform part;
            public float rank;
        }
        sealed class Slot
        {
            public GameObject prefab, instance;
            public Vector3 position, scale;
            public Quaternion rotation;
            public float threshold, rank;
            public Storefront shop;
            public bool onGround;
        }
        readonly Transform root, dressing;
        readonly ResidentialConditionCatalog catalog;
        readonly ResidentialPrefabPool pool;
        readonly bool preview;
        readonly System.Random random;
        readonly Stack<Transform> scan = new Stack<Transform>();
        readonly List<Surface> surfaces = new List<Surface>();
        readonly List<Slot> slots = new List<Slot>();
        readonly List<Surface> flowers = new List<Surface>();
        readonly List<GameObject> leases = new List<GameObject>();
        readonly Dictionary<Material, Material> weather = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> foliage = new Dictionary<Material, Material>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Color> colours = new List<Color>();
        readonly List<bool> plantMaterials = new List<bool>();
        readonly List<Vector3> bins = new List<Vector3>();
        readonly List<Bounds> paving = new List<Bounds>();
        // Start with flat debris, then accumulate three full-size bags and overflow.
        static readonly int[] LitterKinds = { 1, 4, 2, 0, 0, 0, 3, 0 };
        static readonly float[] LitterAt = { .05f, .10f, .16f, .22f, .34f, .45f, .53f, .66f };
        float appliedNeglect = -1, appliedDensity = -1, nextShopRefresh;
        float passNeglect, passDensity;
        int materialWork, surfaceWork, flowerWork;
        int cursor = -1;
        bool disposed;
        int shutters;
        public bool Prepared => scan.Count == 0;
        /// <summary>Scanned, and the wear and decoration pass applied: what the merge may
        /// fold now is what the block will look like until its condition moves.</summary>
        public bool Settled => scan.Count == 0 && cursor < 0 && judged;
        // the first Step after the scan has either applied a pass or found nothing to
        // apply: before that the applied values are the defaults, not the block's
        bool judged;
        /// <summary>Worn materials are on (neglect above nought).</summary>
        public bool Worn => appliedNeglect > 0;
        public float Density => appliedDensity;

        public ResidentialConditionView(Transform root, int seed, ResidentialPrefabPool pool, bool preview)
        {
            this.root = root; this.pool = pool; this.preview = preview;
            random = new System.Random(seed);
            catalog = ResidentialConditionCatalog.Load();
            dressing = new GameObject("Block condition decoration").transform;
            dressing.SetParent(root, false);
            // The dynamic props live outside the merge and never enter navigation discovery.
            dynamicParts.Add(dressing);
            dressingRoots.Add(dressing);
            if (catalog) scan.Push(root);
        }

        public bool Step(float neglect, float density)
        {
            if (disposed || !catalog) return false;
            neglect = float.IsNaN(neglect) ? 0 : Mathf.Clamp01(neglect);
            density = Mathf.Clamp01(density);
            if (scan.Count > 0)
            {
                var t = scan.Pop();
                if (t == dressing) return true;
                for (int i = t.childCount - 1; i >= 0; i--) scan.Push(t.GetChild(i));
                Inspect(t);
                return true;
            }
            if (cursor < 0)
            {
                if (neglect == appliedNeglect && density == appliedDensity &&
                    (preview || Time.unscaledTime < nextShopRefresh)) { judged = true; return false; }
                passNeglect = neglect; passDensity = density; cursor = 0;
                materialWork = neglect != appliedNeglect ? materials.Count : 0;
                flowerWork = neglect != appliedNeglect ? flowers.Count : 0;
                surfaceWork = (neglect > 0) != (appliedNeglect > 0) || density != appliedDensity ? surfaces.Count : 0;
                nextShopRefresh = Time.unscaledTime + 1;
            }
            int n = cursor++;
            if (n < materialWork)
            {
                var mat = materials[n];
                if (plantMaterials[n])
                {
                    var tint = Color.Lerp(colours[n], new Color(.43f, .32f, .16f, colours[n].a), passNeglect * .8f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                }
                else mat.SetFloat("_NeglectAmount", passNeglect);
                return true;
            }
            n -= materialWork;
            if (n < slots.Count) { Apply(slots[n]); return true; }
            n -= slots.Count;
            if (n < flowerWork)
            {
                var flower = flowers[n];
                if (flower.part)
                {
                    flower.part.localScale = Vector3.Scale(flower.scale, new Vector3(1, Mathf.Lerp(1, .72f, passNeglect), 1));
                    flower.part.localRotation = flower.rotation * Quaternion.Euler(passNeglect * 14, 0, passNeglect * -8);
                }
                return true;
            }
            n -= flowerWork;
            if (n < surfaceWork)
            {
                // Reference/visibility changes are cheap; process a small batch per step.
                // Prefab acquisition above still takes just one slot per step.
                int end = Mathf.Min(n + 16, surfaceWork);
                for (int i = n; i < end; i++)
                {
                    var s = surfaces[i];
                    if (!s.renderer) continue;
                    bool worn = passNeglect > 0;
                    if (worn != s.usingWorn)
                    {
                        s.renderer.sharedMaterials = worn ? s.worn : s.original;
                        s.usingWorn = worn;
                    }
                    s.renderer.forceRenderingOff = s.hidden || (s.decoration && s.rank >= passDensity);
                }
                cursor += end - n - 1;
                return true;
            }
            appliedNeglect = passNeglect; appliedDensity = passDensity; cursor = -1; judged = true;
            return true;
        }

        void Inspect(Transform t)
        {
            var shop = t.GetComponent<Storefront>();
            if (shop && catalog.shutter && shop.ConditionOpenings != null)
            {
                foreach (var opening in shop.ConditionOpenings)
                {
                    if (opening.Entrance || shutters >= 16 || slots.Count >= MaxProps) continue;
                    var outward = t.TransformDirection(opening.Outward).normalized;
                    Vector3 point = t.TransformPoint(opening.Front + Vector3.up * (opening.Height * .5f)) + outward * .025f;
                    shutters++;
                    Add(catalog.shutter, point, Quaternion.LookRotation(outward),
                        new Vector3(Mathf.Max(.1f, opening.Width - .12f), opening.Height - .1f, .045f), .64f + Roll() * .3f, shop);
                }
            }
            var filter = t.GetComponent<MeshFilter>();
            var renderer = t.GetComponent<MeshRenderer>();
            if (!filter || !filter.sharedMesh || !renderer || !renderer.enabled) return;
            string name = filter.sharedMesh.name;
            // Flat slabs supply the actual top surface, not a bin's foot or a raised kerb.
            if (name.Contains("Sidewalk") && renderer.bounds.size.y < .12f)
                paving.Add(renderer.bounds);
            bool flower = name.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0;
            bool small = flower || name.Contains("Trash_Bag") || name.Contains("TrashBag") || name.Contains("Bottle") || name.Contains("Cardboard") || name.Contains("Rubbish");
            bool surface = name.StartsWith("SM_Bld_", StringComparison.Ordinal) || name.Contains("Sidewalk") ||
                name.Contains("Road_Bare") || name.Contains("Fence") || name.Contains("Trash") ||
                name.Contains("Skip_") || name.Contains("Bench") || name.Contains("Awning") || flower;
            if (surface || small)
            {
                var original = renderer.sharedMaterials;
                var worn = (Material[])original.Clone();
                bool changed = false;
                for (int i = 0; i < worn.Length; i++)
                {
                    var source = original[i];
                    if (!source || (!flower && (source.renderQueue >= 2500 ||
                        (!source.HasProperty("_BaseMap") && !source.HasProperty("_Albedo_Map"))))) continue;
                    worn[i] = Convert(source, flower); changed = true;
                }
                if (changed || small)
                {
                    surfaces.Add(new Surface { renderer = renderer, part = t, original = original, worn = worn,
                        decoration = small, rank = Roll(), hidden = renderer.forceRenderingOff,
                        wilt = name.Contains("Env_Flowers"), scale = t.localScale, rotation = t.localRotation });
                    if (surfaces[surfaces.Count - 1].wilt) flowers.Add(surfaces[surfaces.Count - 1]);
                    dynamicParts.Add(t);
                }
            }
            bool bin = name.Contains("Trashbin") || name.Contains("Trash_Bin") || name.Contains("Skip_") || name.Contains("Dumpster");
            if (!bin || slots.Count >= MaxProps || catalog.litter.Length == 0) return;
            Bounds b = renderer.bounds;
            if (b.min.y > root.position.y + .6f) return;
            foreach (var p in bins) if ((p - b.center).sqrMagnitude < 4) return;
            bins.Add(b.center);
            // Stay within the same per-view slot cap. A legible pile has guaranteed
            // bags, cardboard and glass; random single tiny bottles were invisible at
            // the street camera's normal distance.
            for (int i = 0; i < LitterKinds.Length && slots.Count < MaxProps; i++)
            {
                bool fill = i == LitterKinds.Length - 1;
                float across = (i % 3 - 1) * .55f + (Roll() - .5f) * .22f;
                Vector3 point = b.center + new Vector3(fill ? 0 : across, 0,
                    fill ? 0 : b.extents.z + .35f + (i / 3) * .35f);
                point.y = fill ? b.max.y - .08f : b.min.y;
                int kind = Mathf.Min(LitterKinds[i], catalog.litter.Length - 1);
                float size = kind == 0 ? .78f + Roll() * .12f : kind == 1 ? .85f : 1f;
                if (fill) size = Mathf.Min(.65f, b.size.x * .65f);
                Add(catalog.litter[kind], point, Quaternion.Euler(0, Roll() * 360, 0),
                    Vector3.one * size, LitterAt[i], onGround: !fill);
            }
        }

        Material Convert(Material source, bool plant)
        {
            var map = plant ? foliage : weather;
            if (map.TryGetValue(source, out var found)) return found;
            // Cloning a Material Variant retains its parent and forbids shader changes.
            // Start with an independent material, then copy the resolved source values.
            var mat = new Material(plant ? source.shader : catalog.weatherShader)
            {
                name = source.name + " (block condition)",
                hideFlags = HideFlags.DontSave
            };
            Color colour = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            if (plant)
                mat.CopyPropertiesFromMaterial(source); // Same shader and property layout.
            else
            {
                // CopyPropertiesFromMaterial also replaces the saved property sheet.
                // A custom shader such as Pavement Concrete does not supply Lit's blend,
                // workflow or normal-map defaults. Keep the destination defaults, and
                // transfer only properties that the source shader actually exposes.
                bool synty = source.HasProperty("_Albedo_Map");
                string tex = synty ? "_Albedo_Map" : "_BaseMap";
                mat.SetTexture("_BaseMap", source.GetTexture(tex));
                mat.SetTextureScale("_BaseMap", source.GetTextureScale(tex));
                mat.SetTextureOffset("_BaseMap", source.GetTextureOffset(tex));
                mat.SetColor("_BaseColor", colour);
                mat.SetFloat("_Smoothness", ReadFloat(source, "_Smoothness", .18f));
                mat.SetFloat("_Metallic", ReadFloat(source, "_Metallic", 0));
                mat.SetFloat("_WorkflowMode", ReadFloat(source, "_WorkflowMode", 1));
                if (source.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", source.GetColor("_SpecColor"));
                mat.SetFloat("_Surface", 0); mat.SetFloat("_Blend", 0);
                mat.SetFloat("_SrcBlend", 1); mat.SetFloat("_DstBlend", 0);
                mat.SetFloat("_SrcBlendAlpha", 1); mat.SetFloat("_DstBlendAlpha", 0);
                mat.SetFloat("_ZWrite", 1);
                mat.SetFloat("_Cull", ReadFloat(source, "_Cull", 2));
                bool cutout = synty || ReadFloat(source, "_AlphaClip", 0) > .5f;
                mat.SetFloat("_AlphaClip", cutout ? 1 : 0);
                mat.SetFloat("_Cutoff", ReadFloat(source, synty ? "_Alpha_Clip_Threshold" : "_Cutoff", .5f));
                if (cutout) mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = cutout ? 2450 : 2000;
                CopyMap(source, mat, synty ? "_Normal_Map" : "_BumpMap", "_BumpMap", "_NORMALMAP");
                mat.SetFloat("_BumpScale", ReadFloat(source, synty ? "_Normal_Amount" : "_BumpScale", 1));
                if (!synty)
                {
                    CopyMap(source, mat, "_MetallicGlossMap", "_MetallicGlossMap", "_METALLICSPECGLOSSMAP");
                    CopyMap(source, mat, "_OcclusionMap", "_OcclusionMap", "_OCCLUSIONMAP");
                    mat.SetFloat("_OcclusionStrength", ReadFloat(source, "_OcclusionStrength", 1));
                    if (source.IsKeywordEnabled("_SPECULAR_SETUP")) mat.EnableKeyword("_SPECULAR_SETUP");
                    if (source.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A")) mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                }
                string emissionColour = synty ? "_Emission_Color" : "_EmissionColor";
                if (source.HasProperty(emissionColour))
                {
                    Color emission = source.GetColor(emissionColour);
                    mat.SetColor("_EmissionColor", emission);
                    CopyMap(source, mat, synty ? "_Emission_Map" : "_EmissionMap", "_EmissionMap", "_EMISSION");
                    if (emission.maxColorComponent > 0) mat.EnableKeyword("_EMISSION");
                }
                mat.SetFloat("_PavementFinish", source.shader.name == "LivingCity/Pavement Concrete" ? 1 : 0);
                mat.SetFloat("_NeglectAmount", 0);
            }
            map.Add(source, mat); materials.Add(mat); colours.Add(colour); plantMaterials.Add(plant);
            return mat;
        }

        static float ReadFloat(Material source, string property, float fallback) =>
            source.HasProperty(property) ? source.GetFloat(property) : fallback;

        static void CopyMap(Material source, Material target, string from, string to, string keyword)
        {
            if (!source.HasProperty(from) || !source.GetTexture(from)) return;
            target.SetTexture(to, source.GetTexture(from));
            target.EnableKeyword(keyword);
        }

        float Roll() => (float)random.NextDouble();
        void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, float threshold, Storefront shop = null, bool onGround = false)
        {
            if (!prefab) return;
            slots.Add(new Slot { prefab = prefab, position = root.InverseTransformPoint(position),
                rotation = Quaternion.Inverse(root.rotation) * rotation, scale = scale,
                threshold = threshold, rank = Roll(), shop = shop, onGround = onGround });
        }
        void Apply(Slot slot)
        {
            bool visible = passNeglect >= slot.threshold && slot.rank < passDensity;
            if (slot.shop && !preview)
            {
                var runtime = BusinessRuntime.Instance;
                // Closed frontage follows business truth, independently of graphics density.
                visible = runtime && runtime.TryGetBusiness(slot.shop.BusinessId, out var business) &&
                    business.State == BusinessOperationalState.Shut && slot.shop.State == StorefrontState.Intact;
            }
            if (visible && !slot.instance)
            {
                slot.instance = pool.Acquire(slot.prefab, dressing, leases);
                Vector3 position = slot.position;
                if (slot.onGround)
                {
                    var world = root.TransformPoint(position);
                    // Resolve once, after discovery has visited all tiles. Thin glass and
                    // cardboard must sit above the 3 cm slab, not disappear inside it.
                    float top = world.y + .03f;
                    bool supported = false;
                    foreach (var slab in paving)
                    {
                        if (world.x < slab.min.x || world.x > slab.max.x ||
                            world.z < slab.min.z || world.z > slab.max.z) continue;
                        if (!supported || slab.max.y > top) top = slab.max.y;
                        supported = true;
                    }
                    world.y = top + .008f;
                    position = root.InverseTransformPoint(world);
                }
                slot.instance.transform.localPosition = position;
                slot.instance.transform.localRotation = slot.rotation;
                slot.instance.transform.localScale = slot.scale;
            }
            if (slot.instance && slot.instance.activeSelf != visible) slot.instance.SetActive(visible);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var surface in surfaces)
            {
                dynamicParts.Remove(surface.part);
                if (!surface.renderer) continue;
                surface.renderer.sharedMaterials = surface.original;
                surface.renderer.forceRenderingOff = surface.hidden;
                if (surface.wilt && surface.part)
                {
                    surface.part.localScale = surface.scale;
                    surface.part.localRotation = surface.rotation;
                }

            }
            foreach (var material in materials) if (material) Object.Destroy(material);
            pool.ReleaseAll(leases);
            dynamicParts.Remove(dressing);
            dressingRoots.Remove(dressing);
            if (dressing) { dressing.gameObject.SetActive(false); Object.Destroy(dressing.gameObject); }
        }
    }
}
