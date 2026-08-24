using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Gives a civilian car a colour of its own instead of the one the pack shipped it in.
    ///
    /// Every Synty body bakes its colour into UVs: the paint on a car is a flat swatch inside
    /// one palette atlas, and the whole vehicle points at a single material. So there is no
    /// "body colour" to set, and the tint trick the LPEC city used (VehicleTinter, a _BaseColor
    /// that MULTIPLIES the atlas) cannot help here either - the paint swatch is already
    /// saturated (SM_Veh_Sedan_01 is #772C25, the supercars #275383), and a multiply can only
    /// darken what is there.
    ///
    /// The lever the packs do give is the Alts folder: fourteen recolours of the palm city's
    /// atlas and twelve of the city's, all sharing one UV layout. Measured through each car's
    /// own UVs, area-weighted per face, swapping one for another moves the PAINT and nothing
    /// else - the greys come back identical to the byte (#535151 29%, #35383C 16% on the sedan
    /// under every one of the fourteen), because the alts recolour the palette rows and leave
    /// the neutral rows alone. Tyres stay black, chrome stays chrome, and no mask is needed.
    ///
    /// What decides the palettes below is memory, not taste. A palm alt is a 4096px BC7 atlas
    /// and costs 42.7 MB resident; the city's are 1024px at 1.3 MB. So the palm palette is
    /// exactly the set the civilian fleet ALREADY loads - 01_A rides on the pickup and the SUV,
    /// 01_B on the supercars, 02_B on the van, 02_C on the food sedan, 03_A on the sedan,
    /// 03_C on the pickup preset - and colouring cars out of it adds not one byte. Adding a
    /// seventh palm colour costs 42.7 MB and must be argued for. The city pack is cheap enough
    /// to take eleven of its twelve; the twelfth (04_C) is magenta, which is not 1987.
    /// </summary>
    public static class VehiclePaint
    {
        const string PalmAlts = "Assets/Synty/PolygonPalmCity/Materials/Alts/";
        const string CityAlts = "Assets/Synty/PolygonCity/Materials/Alts/";

        /// <summary>The palm city's paint, hex measured at the vehicle swatch (uv 0.132, 0.869).
        /// Every one of these is already resident because some car in the pool wears it - see
        /// the header. Amber, black, white, navy, maroon, olive: a 1987 kerb.</summary>
        static readonly string[] PalmPaints =
        {
            "PolygonPalmCity_01_A",   // #EBA236 amber
            "PolygonPalmCity_01_B",   // #25272A black
            "PolygonPalmCity_02_B",   // #E9EADC white
            "PolygonPalmCity_02_C",   // #33486E navy
            "PolygonPalmCity_03_A",   // #772C25 maroon
            "PolygonPalmCity_03_C",   // #62683D olive
        };

        /// <summary>The city pack's, measured at its own swatch (uv 0.425, 0.090). 1.3 MB each,
        /// so the whole range is affordable - all but the magenta.</summary>
        static readonly string[] CityPaints =
        {
            "PolygonCity_01_A",       // #4566A9 blue
            "PolygonCity_01_B",       // #C05F2E rust
            "PolygonCity_01_C",       // #C5C5C5 silver
            "PolygonCity_02_A",       // #C04531 red
            "PolygonCity_02_B",       // #45A9A2 teal
            "PolygonCity_02_C",       // #40477B indigo
            "PolygonCity_03_A",       // #8D7C69 tan
            "PolygonCity_03_B",       // #C09D31 gold
            "PolygonCity_03_C",       // #69A945 green
            "PolygonCity_04_A",       // #3F3F3F graphite
            "PolygonCity_04_B",       // #588092 steel
        };

        static Material[] _palm;
        static Material[] _city;

        /// <summary>
        /// Paints one freshly spawned car. `prefab` is the body it came from - the instance
        /// cannot answer for itself once its name has been rewritten, and eligibility is a
        /// question about the PREFAB.
        ///
        /// Returns true only when the instance actually changed colour: a liveried body, a
        /// marked one, or a pack this holds no palette for all come back false and untouched.
        /// </summary>
        public static bool Apply(GameObject instance, GameObject prefab)
        {
            if (!instance || !prefab)
                return false;

            // Anybody's marked vehicle keeps its livery, and so does a taxi, a food sedan or a
            // works pickup: the paint swatch IS the livery on those bodies, and a repaint would
            // leave a cab with a roof sign and no yellow. VehicleCatalog holds the one list.
            if (VehicleCatalog.WearsLivery(prefab.name))
                return false;

            // ONE WALK OF THE BODY, and one reading of each renderer's slots. Both are
            // wanted twice - once to find which material the paint is on, once to swap it -
            // and both allocate every time they are asked: GetComponentsInChildren returns a
            // fresh array, and so does the sharedMaterials GETTER, which copies. This runs on
            // every car in the city rather than on a handful, so it is asked once and the
            // answers are held.
            //
            // sharedMaterials, never materials: reading .materials instantiates a private
            // copy per renderer, which leaks and drops the car out of batching.
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            var slots = new Material[renderers.Length][];
            for (var r = 0; r < renderers.Length; r++)
                slots[r] = renderers[r].sharedMaterials;

            var body = BodyMaterial(slots);
            if (!body)
                return false;

            var palette = PaletteFor(body.name);
            if (palette == null || palette.Length == 0)
                return false;

            var paint = palette[Random.Range(0, palette.Length)];
            if (!paint || paint == body)
                return false;   // the factory colour came up; the car keeps it

            var painted = false;

            for (var r = 0; r < renderers.Length; r++)
            {
                var mine = slots[r];
                var changed = false;

                for (var i = 0; i < mine.Length; i++)
                {
                    if (mine[i] != body)
                        continue;   // glass has its own material, and a second tone is an accent
                    mine[i] = paint;
                    changed = true;
                }

                if (!changed)
                    continue;

                renderers[r].sharedMaterials = mine;
                painted = true;
            }

            return painted;
        }

        /// <summary>The tally BodyMaterial counts in, kept between cars rather than allocated
        /// per car: a dictionary a spawn loop throws away is a dictionary the collector has to
        /// come back for, and this is called once per vehicle in the city.</summary>
        static readonly Dictionary<Material, int> Counts = new Dictionary<Material, int>();

        /// <summary>
        /// The material the bodywork is on, by weight of renderers.
        ///
        /// Most bodies wear exactly one atlas material and the question is trivial, but three
        /// in the pool are two-tone - the van is eight panels of 02_B over six of 01_A, the
        /// buggy six of 01_A under one of 01_C - and only the majority material is the paint.
        /// The minority is trim, and repainting it too would flatten the two tones into one.
        /// </summary>
        static Material BodyMaterial(Material[][] slots)
        {
            Counts.Clear();

            foreach (var mine in slots)
                foreach (var material in mine)
                {
                    if (!material || PaletteFor(material.name) == null)
                        continue;
                    Counts.TryGetValue(material, out var seen);
                    Counts[material] = seen + 1;
                }

            Material best = null;
            var most = 0;

            foreach (var pair in Counts)
                if (pair.Value > most) { most = pair.Value; best = pair.Key; }

            return best;
        }

        /// <summary>Which pack's palette a material belongs to, or null for a material this
        /// knows nothing about - glass, a billboard, a boat's water. The palm test comes first
        /// because "PolygonCity" is a prefix of nothing but itself and "PolygonPalmCity" is not
        /// caught by it, but the order is stated rather than relied on.</summary>
        static Material[] PaletteFor(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;

            if (materialName.StartsWith("PolygonPalmCity_"))
                return _palm ??= Load(PalmAlts, PalmPaints);

            if (materialName.StartsWith("PolygonCity_"))
                return _city ??= Load(CityAlts, CityPaints);

            return null;
        }

        /// <summary>
        /// The palette, loaded once and held. An alt that will not load is dropped rather than
        /// left as a null seat: a null in the array would come up as "no repaint" at exactly
        /// its share of the rolls, which reads as a palette that quietly lost a colour.
        /// </summary>
        static Material[] Load(string folder, string[] names)
        {
            var loaded = new List<Material>(names.Length);

            foreach (var name in names)
            {
                var material = RoadDemo.DemoAssetLoad.Load<Material>(folder + name + ".mat");
                if (material) loaded.Add(material);
            }

            if (loaded.Count == 0)
                Debug.LogWarning($"[VehiclePaint] No paint loaded out of {folder}; cars keep the "
                                 + "colour the pack shipped them in.");

            return loaded.ToArray();
        }
    }
}
