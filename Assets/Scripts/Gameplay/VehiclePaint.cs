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
    ///
    /// THE TOURER IS A THIRD CASE and it had to be built rather than found. It is no pack
    /// body: GangBikeBaker cuts it out of the police pack, whose atlas ships no alts at
    /// all. Its twelve Police_Vehicle_NN textures are 4096px LIVERIES, and they hold one
    /// paint palette between them - sampled at the same swatch all twelve come back
    /// 63,68,72 to the byte - so swapping them would move the chequer and leave the paint,
    /// at 21 MB a piece for the privilege. So the bake gives that machine what the packs
    /// gave the cars: its bodywork on a submesh of its own, still pointing at the atlas's
    /// big flat white field (sRGB 214,211,210), under a material this project owns.
    /// Recolouring is then the same material swap as everywhere else, between materials
    /// that differ only in _BaseColor - which the Synty shader MULTIPLIES into the albedo
    /// (Generic_Basic.shadergraph: Sample Texture 2D -> Multiply -> BaseColor). All of
    /// them share the pack's one texture, so the palette costs nothing.
    /// </summary>
    public static class VehiclePaint
    {
        const string PalmAlts = "Assets/Synty/PolygonPalmCity/Materials/Alts/";
        const string CityAlts = "Assets/Synty/PolygonCity/Materials/Alts/";
        const string TourerPaints = "Assets/Prefabs/Vehicles/Tourer/";

        /// <summary>The prefix every tourer paint material carries, and the whole safety
        /// catch on that machine. These materials are on the BAKED tourer and nowhere
        /// else, so the police pack's own liveried bike cannot be caught by them - which
        /// matters, because it shares the bare name "SM_Veh_Motorbike_01" with Palm
        /// City's and is therefore invisible to <see cref="VehicleCatalog.WearsLivery"/>
        /// when all a caller has is a name. The law keeps its chequer because it carries
        /// no material this answers to, not because a name list remembered it.</summary>
        public const string TourerPaintPrefix = "Tourer_Paint_";

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

        /// <summary>One tourer paint: the material's name and the tint that makes it.</summary>
        public readonly struct TourerPaint
        {
            public readonly string Name;
            public readonly Color Tint;

            public TourerPaint(string name, Color tint) { Name = name; Tint = tint; }
        }

        /// <summary>
        /// The tourer's paints, and the ONE list of them: GangBikeBaker writes the
        /// materials out of this table and this loads them back, so a colour cannot be
        /// added in one place and missed in the other.
        ///
        /// These are tints and not paint. The shader multiplies them into an albedo that
        /// is already sRGB 214 where the bodywork sits, so every one comes out about four
        /// fifths of the value written here - the graphite below lands near sRGB 61,
        /// which is the black the machine has always been, and stays the baked default so
        /// the asset on disk is still the outfit's black tourer.
        ///
        /// Four of them are lifted off the shade a paint chart would give them, and the
        /// reason is the machine's own flanks. Those sit on a fixed charcoal swatch
        /// (GangBikeBaker.Trim, sRGB 37,39,42) so that they read as trim whatever the
        /// bike is painted - which only works while the paint stays clear of them. At
        /// their chart values the maroon came out at a luminance ratio of 1.00 against
        /// the flanks, the navy and the racing green not much better: tonally flat, which
        /// is the exact complaint this palette exists to answer. Lifted, the worst of the
        /// nine separates by 1.42 and every one of them still reads as 1987 paint.
        /// </summary>
        public static readonly TourerPaint[] TourerPalette =
        {
            new TourerPaint("Graphite", Hex(0x4A4E55)),   // the default; reads near-black
            new TourerPaint("Maroon",   Hex(0xA8402F)),
            new TourerPaint("Navy",     Hex(0x35508C)),
            new TourerPaint("Amber",    Hex(0xE0A02C)),
            new TourerPaint("Cream",    Hex(0xE9EADC)),
            new TourerPaint("Olive",    Hex(0x5F6A3A)),
            new TourerPaint("Rust",     Hex(0xC05F2E)),
            new TourerPaint("Racing",   Hex(0x387F44)),
            new TourerPaint("Silver",   Hex(0xB8BCC0)),
        };

        /// <summary>The material name a tourer paint is written under.</summary>
        public static string TourerMaterialName(string paint) => TourerPaintPrefix + paint;

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f,
                                               ((rgb >> 8) & 0xFF) / 255f,
                                               (rgb & 0xFF) / 255f, 1f);

        static Material[] _palm;
        static Material[] _city;
        static Material[] _tourer;

        /// <summary>
        /// Paints one freshly spawned car. `prefab` is the body it came from - the instance
        /// cannot answer for itself once its name has been rewritten, and eligibility is a
        /// question about the PREFAB.
        ///
        /// Returns true only when the instance actually changed colour: a liveried body, a
        /// marked one, or a pack this holds no palette for all come back false and untouched.
        /// </summary>
        public static bool Apply(GameObject instance, GameObject prefab) =>
            Apply(instance, prefab, null);

        /// <summary>
        /// The same, drawing from a stream of the caller's own instead of the shared one.
        ///
        /// THE LEDGER NEEDS THIS AND THE STREET DOES NOT. A body spawned into the city is
        /// painted alongside three hundred other UnityEngine.Random draws the running street
        /// makes - a hood's pace, a bonnet catching fire, which door a man is sent to - and
        /// one more among them changes nothing anybody can name. A page is different: the
        /// player opens the book when he likes, and a photograph developed off that same
        /// stream would shift every roll the street made afterwards, so looking at the book
        /// would change what the street did next. Hand PortraitStudio a stream of its own
        /// and the page costs the street nothing.
        ///
        /// The city's LAYOUT is not the thing at risk here and never was: that is rolled out
        /// of seeded System.Random instances (CityLayout, RoadDemoBuilder's plan, zones and
        /// closes) which this stream cannot reach from either side.
        /// </summary>
        public static bool Apply(GameObject instance, GameObject prefab, System.Random rng)
        {
            if (!instance || !prefab)
                return false;

            // Anybody's marked vehicle keeps its livery, and so does a taxi, a food sedan or a
            // works pickup: the paint swatch IS the livery on those bodies, and a repaint would
            // leave a cab with a roof sign and no yellow. VehicleCatalog holds the one list.
            if (VehicleCatalog.WearsLivery(prefab.name) || CivilianVehicleCatalog.IsAuthored(prefab.name))
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

            var paint = palette[rng != null ? rng.Next(palette.Length) : Random.Range(0, palette.Length)];
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

            if (materialName.StartsWith(TourerPaintPrefix))
                return _tourer ??= LoadTourer();

            return null;
        }

        /// <summary>The tourer's paints off disk, named out of <see cref="TourerPalette"/>.
        /// Empty until GangBikeBaker has been run once - the machine then keeps whichever
        /// colour the prefab was saved with, which is the graphite, so a project that has
        /// not re-baked still gets the black tourer it always had.</summary>
        static Material[] LoadTourer()
        {
            var names = new string[TourerPalette.Length];
            for (var i = 0; i < names.Length; i++)
                names[i] = TourerMaterialName(TourerPalette[i].Name);
            return Load(TourerPaints, names);
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
                Debug.LogWarning($"[VehiclePaint] No paint loaded out of {folder}; bodies that "
                                 + "draw from it keep the colour they were saved with.");

            return loaded.ToArray();
        }
    }
}
