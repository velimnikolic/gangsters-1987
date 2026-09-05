using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>One deterministic skin tint, shared by street, crew and decorative people.
    /// Uses the packs' skin masks; clothing, eyes and hair keep their authored colours.</summary>
    [DisallowMultipleComponent]
    public sealed class PersonSkinTint : MonoBehaviour
    {
        [SerializeField] int seed;
        [SerializeField] PedestrianPopulationGroup group;
        [SerializeField] bool configured;

        static readonly int SkinColor = Shader.PropertyToID("_Skin_Color");
        static readonly Dictionary<Material, Material> LegacyMaterials = new();
        static Texture2D cityMask, policeMask;

        // Overlapping, natural complexions within each population, rather than one
        // flat colour for every member of a group. These values are sRGB colours.
        static readonly Color32[][] Tones =
        {
            new[] { Rgb(220, 170, 132), Rgb(193, 139, 101), Rgb(165, 111, 77), Rgb(232, 189, 151) },
            new[] { Rgb(243, 207, 180), Rgb(228, 182, 151), Rgb(214, 164, 130), Rgb(238, 195, 170) },
            new[] { Rgb(104, 65, 45), Rgb(128, 82, 56), Rgb(151, 103, 72), Rgb(83, 51, 38) },
            new[] { Rgb(234, 196, 153), Rgb(214, 173, 130), Rgb(197, 149, 107), Rgb(241, 207, 169) },
            new[] { Rgb(227, 184, 146), Rgb(186, 133, 93), Rgb(142, 92, 63), Rgb(111, 73, 51) },
        };

        static Color32 Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

        public static Color Tone(int seed, PedestrianPopulationGroup group)
        {
            uint hash = unchecked((uint)seed * 747796405u + 2891336453u);
            hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u;
            hash = (hash >> 22) ^ hash;
            var palette = Tones[Mathf.Clamp((int)group, 0, Tones.Length - 1)];
            return palette[hash % (uint)palette.Length];
        }

        public static void Apply(GameObject person, int seed, PedestrianPopulationGroup group)
        {
            if (!person) return;
            var tint = person.GetComponent<PersonSkinTint>();
            if (!tint) tint = person.AddComponent<PersonSkinTint>();
            tint.seed = seed;
            tint.group = group;
            tint.configured = true;
            tint.enabled = true;
            // Scene generators also call Apply in edit mode. Save only the appearance
            // data there: transient materials/property blocks must not enter a scene file.
            if (Application.isPlaying) tint.Refresh();
        }

        void OnEnable()
        {
            if (configured && Application.isPlaying) Refresh();
        }

        void Refresh()
        {
            var block = new MaterialPropertyBlock();
            var color = Tone(seed, group);
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (!material) continue;
                    if (!material.HasProperty(SkinColor))
                    {
                        material = WithSkinMask(material);
                        if (!material) continue;
                        materials[slot] = material;
                        changed = true;
                    }
                    // Keep any existing highlight/damage overrides on this slot.
                    renderer.GetPropertyBlock(block, slot);
                    block.SetColor(SkinColor, color);
                    renderer.SetPropertyBlock(block, slot);
                    block.Clear();
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        static Material WithSkinMask(Material original)
        {
            if (!original.HasProperty("_Albedo_Map")) return null;
            var atlas = original.GetTexture("_Albedo_Map");
            if (!atlas) return null;
            bool police = atlas.name.StartsWith("PolygonPoliceStation_01");
            bool city = atlas.name.StartsWith("PolygonCity_01");
            if (!police && !city) return null;
            if (LegacyMaterials.TryGetValue(original, out var cached) && cached) return cached;

            // A Resources reference keeps the existing masked Synty shader in builds.
            var template = Resources.Load<Material>("People/SkinTintTemplate");
            if (!template) return null;
            var material = new Material(template) { name = original.name + " (skin mask)",
                hideFlags = HideFlags.HideAndDontSave };
            material.CopyPropertiesFromMaterial(original);
            material.SetTexture("_Skin_Mask", police
                ? policeMask ? policeMask : policeMask = MakeMask(true)
                : cityMask ? cityMask : cityMask = MakeMask(false));
            material.SetTexture("_Hair_Mask", Texture2D.blackTexture);
            LegacyMaterials[original] = material;
            return material;
        }

        static Texture2D MakeMask(bool police)
        {
            // Authored skin swatches in the two legacy atlases (bottom-left UV origin).
            // The police atlas also contains tattooed skin; its ink is left authored.
            const int size = 512;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    bool skin = u < 0.041f && v > (police ? 0.157f : 0.174f) && v < 0.218f;
                    pixels[y * size + x] = skin ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 255);
                }
            var mask = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            { name = police ? "Police skin swatches" : "City skin swatches",
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave };
            mask.SetPixels32(pixels);
            mask.Apply(false, true);
            return mask;
        }
    }
}
