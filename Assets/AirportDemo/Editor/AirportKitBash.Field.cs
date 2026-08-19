using UnityEngine;

namespace AirportDemo.EditorTools
{
    // The field furniture: the things that make a strip of concrete read as an
    // airport rather than a car park, and that no pack we own has in any form - the
    // windsock, the PAPI, the yellow guidance boards, the apron floodlight masts, the
    // airfield lights themselves - plus the ground equipment that gets towed about
    // the ramp. All of it small, all of it built out of boxes and tubes wearing pack
    // materials, and all of it baked so the field lays a hundred and thirty lights
    // without a hundred and thirty instantiations of a live assembly.
    public static partial class AirportKitBash
    {
        /// <summary>The windsock: a six-metre mast, the ring it flies from and the
        /// banded cone. Baked pointing +X - the builder yaws the whole prefab to the
        /// wind, which is right because the mast is round.</summary>
        static void BuildWindsock()
        {
            var root = Scratch("windsock");
            var t = root.transform;
            const float mast = 6f;
            Tube(t, "mast", Vector3.zero, 0.11f, mast, Steel, 10);
            Slab(t, "base", new Vector3(0f, 0.12f, 0f), new Vector3(1.1f, 0.24f, 1.1f), Concrete);
            // the guy struts a frangible mast carries
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Cos(a) * 1.5f, 0.1f, Mathf.Sin(a) * 1.5f);
                var strut = Slab(t, "strut", (foot + new Vector3(0f, mast * 0.55f, 0f)) * 0.5f + new Vector3(0f, 0.3f, 0f),
                                 new Vector3(0.07f, mast * 0.62f, 0.07f), Steel);
                strut.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (new Vector3(0f, mast * 0.55f, 0f) - foot).normalized);
            }
            // the hoop the sock swivels on
            var hoop = Tube(t, "hoop", new Vector3(0f, mast, 0f), 0.55f, 0.09f, Steel, 14, cap: false);
            hoop.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // the sock: five bands, orange and white, tapering away downwind (+X)
            const int bands = 5;
            const float sockLen = 3.6f;
            for (int i = 0; i < bands; i++)
            {
                float t0 = i / (float)bands, t1 = (i + 1) / (float)bands;
                float r0 = Mathf.Lerp(0.52f, 0.24f, t0), r1 = Mathf.Lerp(0.52f, 0.24f, t1);
                float r = (r0 + r1) * 0.5f;
                var band = Tube(t, "sock band", new Vector3(sockLen * t0, mast, 0f), r, sockLen / bands,
                                (i & 1) == 0 ? Orange : White, 10, cap: false);
                band.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            Bake(root, "airport-windsock");
        }

        /// <summary>PAPI: four boxes in a row, each on its frangible legs, the lens
        /// half white and half red - the light that tells a pilot he is high or low.
        /// Baked in a row along Z so the builder lays it parallel to the runway.</summary>
        static void BuildPapi()
        {
            var root = Scratch("papi");
            var t = root.transform;
            for (int i = 0; i < 4; i++)
            {
                float z = (i - 1.5f) * AirportSpec.PapiBoxPitch;
                Slab(t, "box", new Vector3(0f, 0.85f, z), new Vector3(1.15f, 0.6f, 0.75f), Steel);
                Slab(t, "lens white", new Vector3(-0.6f, 0.95f, z), new Vector3(0.08f, 0.3f, 0.55f), White);
                Slab(t, "lens red", new Vector3(-0.6f, 0.7f, z), new Vector3(0.08f, 0.22f, 0.55f), Red);
                for (int k = -1; k <= 1; k += 2)
                    Tube(t, "leg", new Vector3(k * 0.4f, 0f, z), 0.05f, 0.55f, Steel, 6);
                Slab(t, "pad", new Vector3(0f, 0.04f, z), new Vector3(1.4f, 0.08f, 1f), Concrete);
            }
            Bake(root, "airport-papi");
        }

        /// <summary>A taxiway location board: yellow face, black legend, on two legs.
        /// One prefab; the builder paints the letter it needs on with its own quads,
        /// so a single bake serves every board on the field.</summary>
        static void BuildTaxiSign()
        {
            var root = Scratch("sign-taxi");
            var t = root.transform;
            Slab(t, "board", new Vector3(0f, 0.85f, 0f), new Vector3(1.7f, 0.72f, 0.12f), Yellow);
            Slab(t, "border", new Vector3(0f, 0.85f, -0.07f), new Vector3(1.82f, 0.84f, 0.04f), Black);
            for (int k = -1; k <= 1; k += 2)
                Tube(t, "leg", new Vector3(k * 0.6f, 0f, 0f), 0.045f, 0.52f, Steel, 6);
            Bake(root, "airport-sign-taxi");
        }

        /// <summary>The runway holding position board: red face, white legend. The one
        /// sign on an airfield that is not advisory.</summary>
        static void BuildHoldSign()
        {
            var root = Scratch("sign-hold");
            var t = root.transform;
            Slab(t, "board", new Vector3(0f, 0.9f, 0f), new Vector3(2.1f, 0.8f, 0.12f), Red);
            Slab(t, "border", new Vector3(0f, 0.9f, -0.07f), new Vector3(2.22f, 0.92f, 0.04f), White);
            for (int k = -1; k <= 1; k += 2)
                Tube(t, "leg", new Vector3(k * 0.75f, 0f, 0f), 0.045f, 0.55f, Steel, 6);
            Bake(root, "airport-sign-hold");
        }

        /// <summary>An apron floodlight mast: fifteen metres, four heads on a crossbar.
        /// The ramp is lit from masts, not from street lamps.</summary>
        static void BuildApronMast()
        {
            var root = Scratch("apron-mast");
            var t = root.transform;
            const float h = 15f;
            Tube(t, "mast", Vector3.zero, 0.22f, h, Steel, 10);
            Slab(t, "base", new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.6f, 1.4f), Concrete);
            Slab(t, "crossbar", new Vector3(0f, h, 0f), new Vector3(4.4f, 0.16f, 0.16f), Steel);
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 1.25f;
                var head = Slab(t, "head", new Vector3(x, h - 0.42f, 0.25f), new Vector3(0.85f, 0.45f, 0.7f), Steel);
                head.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                var lens = Slab(t, "lens", new Vector3(x, h - 0.62f, 0.48f), new Vector3(0.7f, 0.08f, 0.5f), White);
                lens.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            }
            Bake(root, "airport-apron-mast");
        }

        /// <summary>Boarding stairs: five treads to a 1.2 m sill (the Caravan's cabin
        /// floor, measured off the model's door), handrails, and the little chassis
        /// they are wheeled about the ramp on. 1987 - the passengers walk out and
        /// climb these, there is no airbridge on a field like this.</summary>
        static void BuildAirStairs()
        {
            // a low flight for a light aeroplane and a tall one for an airliner: a
            // passenger walking five steps up to a trijet's door reads wrong from
            // anywhere on the ramp, and the walk up the steps is the whole of boarding
            // at a field with no airbridge
            BuildAirStairs(5, 1.24f, "airport-airstairs");
            BuildAirStairs(10, 2.60f, "airport-airstairs-tall");
        }

        /// <summary>One flight of airstairs: wheels and drawbar at the origin, the
        /// platform out along its own -Z at the height asked for, so parking it that
        /// far outboard of a door puts the platform at the sill.</summary>
        static void BuildAirStairs(int steps, float top, string name)
        {
            var root = Scratch(name);
            var t = root.transform;
            float rise = top / steps, tread = 0.3f, wide = 1.1f, deck = 0.28f;
            float run = steps * tread;
            // the pitch the flight actually sits at, which is what the handrail follows
            float pitch = Mathf.Atan2(top, run) * Mathf.Rad2Deg;
            for (int i = 0; i < steps; i++)
                Slab(t, "tread", new Vector3(0f, deck + rise * (i + 0.5f), -i * tread), new Vector3(wide, rise, tread), Steel);
            Slab(t, "platform", new Vector3(0f, deck + top + 0.05f, -run - 0.4f), new Vector3(wide, 0.1f, 0.9f), Steel);
            for (int k = -1; k <= 1; k += 2)
            {
                var rail = Slab(t, "rail", new Vector3(k * (wide * 0.5f - 0.05f), deck + top * 0.5f + 0.62f, -run * 0.5f),
                                new Vector3(0.06f, 0.06f, Mathf.Sqrt(run * run + top * top) + 0.4f), Steel);
                // the -Z end is the top of the flight, so a positive pitch lifts it
                rail.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                Tube(t, "stanchion", new Vector3(k * (wide * 0.5f - 0.05f), deck + top, -run - 0.3f), 0.035f, 1.05f, Steel, 6);
            }
            Slab(t, "chassis", new Vector3(0f, 0.2f, -run * 0.5f), new Vector3(wide + 0.2f, 0.16f, run + 1.4f), Yellow);
            for (int k = -1; k <= 1; k += 2)
                for (int j = 0; j < 2; j++)
                {
                    var wheel = Tube(t, "wheel", new Vector3(k * (wide * 0.5f + 0.06f), 0.24f, j == 0 ? 0.3f : -run - 0.6f), 0.24f, 0.12f, Black, 10);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            Bake(root, name);
        }

        /// <summary>A baggage dolly: the flat cart with side rails that a tug tows two
        /// or three of, nose to tail, across the ramp.</summary>
        static void BuildBaggageCart()
        {
            var root = Scratch("baggage-cart");
            var t = root.transform;
            Slab(t, "deck", new Vector3(0f, 0.55f, 0f), new Vector3(1.35f, 0.1f, 2.5f), Steel);
            for (int k = -1; k <= 1; k += 2)
                Slab(t, "rail", new Vector3(k * 0.65f, 0.85f, 0f), new Vector3(0.06f, 0.55f, 2.5f), Steel);
            Slab(t, "head rail", new Vector3(0f, 0.85f, -1.22f), new Vector3(1.35f, 0.55f, 0.06f), Steel);
            Slab(t, "chassis", new Vector3(0f, 0.42f, 0f), new Vector3(1.1f, 0.18f, 2.1f), Yellow);
            Slab(t, "drawbar", new Vector3(0f, 0.36f, 1.55f), new Vector3(0.1f, 0.1f, 1.2f), Steel);
            for (int k = -1; k <= 1; k += 2)
                for (int j = -1; j <= 1; j += 2)
                {
                    var wheel = Tube(t, "wheel", new Vector3(k * 0.62f, 0.3f, j * 0.85f), 0.3f, 0.14f, Black, 10);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            Bake(root, "airport-baggage-cart");
        }

        /// <summary>The bowser body: the tank, its cabinet and the hose reel, sized to
        /// drop onto a flatbed lorry's deck. Only the body is baked - the lorry keeps
        /// its own wheels, which the demo spins.</summary>
        static void BuildFuelBowser()
        {
            var root = Scratch("fuel-bowser");
            var t = root.transform;
            var tank = Tube(t, "tank", new Vector3(-2.2f, 0.95f, 0f), 0.85f, 4.4f, White, 14);
            tank.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            Slab(t, "cradle", new Vector3(0f, 0.25f, 0f), new Vector3(4.6f, 0.5f, 1.7f), Steel);
            Slab(t, "cabinet", new Vector3(2.6f, 0.85f, 0f), new Vector3(0.9f, 1.3f, 1.6f), Steel);
            Slab(t, "band", new Vector3(0f, 0.95f, 0f), new Vector3(4.45f, 0.3f, 1.74f), Red);
            var reel = Tube(t, "reel", new Vector3(-2.5f, 1.1f, 0.9f), 0.34f, 0.4f, Black, 10);
            reel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Slab(t, "placard", new Vector3(2.6f, 1.2f, 0.82f), new Vector3(0.7f, 0.45f, 0.05f), White);
            Bake(root, "airport-fuel-bowser");
        }

        /// <summary>A pair of chocks on their rope - what stops a parked aeroplane
        /// rolling, and the one bit of ramp litter that is never litter.</summary>
        static void BuildChock()
        {
            var root = Scratch("chock");
            var t = root.transform;
            for (int k = -1; k <= 1; k += 2)
            {
                var wedge = Slab(t, "chock", new Vector3(k * 0.42f, 0.11f, 0f), new Vector3(0.34f, 0.22f, 0.5f), Yellow);
                wedge.transform.localRotation = Quaternion.Euler(0f, 0f, k * 16f);
            }
            Slab(t, "rope", new Vector3(0f, 0.03f, 0f), new Vector3(0.84f, 0.04f, 0.04f), Rust);
            Bake(root, "airport-chock");
        }

        /// <summary>The airfield lights: an elevated fixture is a squat base and a
        /// coloured lens, and there are a hundred and thirty of them round the field,
        /// so each colour is one baked prefab with an unlit lens that reads after
        /// dark without a real light behind it.</summary>
        static void BuildLights()
        {
            var colours = new (string name, Color colour)[]
            {
                ("white", new Color(0.95f, 0.95f, 0.88f)),
                ("amber", new Color(0.95f, 0.62f, 0.06f)),
                ("green", new Color(0.10f, 0.85f, 0.30f)),
                ("red", new Color(0.90f, 0.12f, 0.10f)),
                ("blue", new Color(0.16f, 0.42f, 0.95f)),
            };
            foreach (var (name, colour) in colours)
            {
                var root = Scratch("light-" + name);
                var t = root.transform;
                var lens = LensMaterial(name, colour);
                Tube(t, "stem", Vector3.zero, 0.06f, 0.26f, Black, 6);
                Slab(t, "base", new Vector3(0f, 0.03f, 0f), new Vector3(0.3f, 0.06f, 0.3f), Black);
                var head = Tube(t, "lens", new Vector3(0f, 0.26f, 0f), 0.11f, 0.16f, lens, 8);
                Slab(t, "cap", new Vector3(0f, 0.45f, 0f), new Vector3(0.24f, 0.05f, 0.24f), Black);
                Bake(root, "airport-light-" + name);
            }
        }

        /// <summary>An unlit lens material, saved beside the kit: a lamp seen from a
        /// hundred metres is a coloured dot, and a hundred and thirty real lights are
        /// not something a forward renderer will thank anybody for.</summary>
        static Material LensMaterial(string name, Color colour)
        {
            var path = $"{MatDir}/airport-lens-{name}.mat";
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "airport-lens-" + name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            UnityEditor.AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
