using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Lit windows after dark. Self-contained (the demo does not use the city's
    // NightWindows, and its own clock/night curve are the ones that drive this).
    //
    // Two kinds of surface light up, and they are found in two different ways.
    //
    //  - THE PANES. Every building in the demo is a CityKit bash of Synty wall
    //    pieces, and the window openings in those pieces are separate quads with
    //    a glass material on them - Generic_Glass_Opaque, PalmCity Glass_01,
    //    GangWarfare_01_Glass, and so on. A glass material carries no emission
    //    map, so lighting one lights the whole pane at a single flat value: a
    //    window is either on or off, never half of it. Practically every
    //    building in the kit has glass somewhere, so this is what makes the
    //    whole city light rather than a scattering of it.
    //
    //  - THE ATLAS EMISSIVES. Some pack materials carry an emissive atlas but
    //    never switch it on. Those get switched on too - that is where signage,
    //    light fittings and shopfront glow live.
    //
    // What is deliberately NOT done is carving up those atlases. An earlier pass
    // here read them as if the artist had painted each window on the facade
    // texture, and blanked out random 32-texel cells to vary which windows a
    // building lit. They are nothing of the sort: a Synty emissive atlas is a
    // strip of flat colour SWATCHES that emissive faces are UV'd onto (open
    // Emissive_01 and it is black but for a pedestrian signal, a TAXI sign and a
    // palette band at the bottom). A cell grid laid over that cuts straight
    // through a swatch, so every face using it came out lit in patches. The
    // atlases are used whole.
    //
    // The variation is per BUILDING instead, which is the grain the city is built
    // at anyway: a CityKit block is a nest of catalog prefabs - City_03_A,
    // Apartment_02, Palm Tower - one renderer each. Every building is dealt a lot
    // off its position, and a lot decides when in the dusk its windows come on,
    // what colour and how bright they burn, and when it turns in. Which lots a
    // building can draw depends on what it is:
    //
    //  - HOMES (anything inside a residential block, the apartments, the mansion)
    //    include the possibility of not lighting at all. Somebody is asleep,
    //    somebody is out, somebody's flat is empty - so a share of them stay dark
    //    all night, and the rest go to bed at their own hour.
    //  - TRADE - shops, offices, the yards - all light, and shut at closing time,
    //    which runs from ten to half past midnight across the lots. One lot
    //    of them does not close at all: the garage, the lobby, the sign nobody
    //    has the key for.
    //  - LANDMARKS - the Palm Tower - burn from dusk to dawn and are exempt from
    //    everything below.
    //
    // And at four in the morning the city goes out. Every lot but the night-shift
    // one and the landmarks fades away over the following half hour and stays
    // down: by the time the sky itself starts to lift at a quarter to five there
    // is next to nothing burning. The few that are left are drawn deliberately
    // thin, so it is about a twentieth of the windows that were lit at midnight.
    //
    // This is not real illumination - a window casts nothing onto the pavement
    // opposite; DemoStreetLamps does that. What it gives is a city that reads as
    // inhabited after dark.
    public class DemoNightWindows : MonoBehaviour
    {
        public DemoClock clock;

        // The city blocks. Panes are only lit under here, which is what keeps the
        // traffic's windscreens out of it; the atlas emissives are picked up
        // scene-wide, so street furniture and signage still light. Left unset,
        // panes are looked for everywhere and vehicle glass is filtered by name.
        public Transform facadeRoot;

        // Buildings that are never dark. Matched against the renderer's own name
        // and every parent up to facadeRoot, so naming the catalog prefab is
        // enough - the block it sits in does not have to know.
        public string[] landmarks = { "Palm Tower" };

        // What counts as somewhere people live. Same walk up the parents, which is
        // what lets one name on the block - residentialblock1 - speak for the
        // dozen-odd City_03_x / City_05_x buildings baked inside it.
        static readonly string[] HomeNames =
            { "residential", "apartment", "house-block", "mansion", "res-" };

        // Synty's Generic_Basic shader graph exposes emission as a plain Boolean
        // property plus a colour and a map - no keyword, so a SetFloat is all the
        // toggle needs. The Unity-standard names are written too: the pack's
        // materials carry both sets and other kits may only have the standard one.
        static readonly int EnableEmission = Shader.PropertyToID("_Enable_Emission");
        static readonly int SyntyEmissionColor = Shader.PropertyToID("_Emission_Color");
        static readonly int SyntyEmissionMap = Shader.PropertyToID("_Emission_Map");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        // The lots, in one block so the indices below can name their ranges.
        const int HomeLots = 10;     // 0 .. 9    flats and houses
        const int VigilLot = 10;     //           the home lot that is up all night
        const int TradeLot = 11;     // 11 .. 14  shops, offices, yards
        const int TradeLots = 4;
        const int LandmarkLot = 15;
        const int Lots = 16;

        // Share of homes that never light: asleep, out, or empty.
        const float HomeDark = 0.34f;

        // Share of the homes that DO light which draw the night-shift lot. This is
        // what is still burning between four and six, so it is also the answer to
        // "how much of the city is left on" - a twentieth of it.
        const float VigilShare = 0.05f;

        static readonly Color[] HomeTints =
        {
            new Color(1f, 0.86f, 0.60f),    // tungsten
            new Color(1f, 0.80f, 0.52f),    // a warmer, older bulb
            new Color(1f, 0.90f, 0.72f),    // soft white
            new Color(0.98f, 0.84f, 0.66f), // shaded lamp
        };

        static readonly Color[] TradeTints =
        {
            new Color(0.84f, 0.90f, 1f),    // fluorescent tube
            new Color(0.92f, 0.95f, 1f),    // cold office
            new Color(1f, 0.93f, 0.80f),    // shop window
        };

        static readonly Color Tungsten = new Color(1f, 0.86f, 0.60f);

        // A pane is a large flat surface and blooms far more readily than a
        // painted swatch does, so the two are scaled apart.
        // A window is seen through tinted glass, not painted onto the front of it.
        // Keep it below the signage and let the pane's own alpha soften it further.
        const float PaneIntensity = 0.32f;
        const float AtlasIntensity = 0.50f;

        // Four in the morning, and how long the wave takes to pass over the city.
        // Nothing brings the lights back: DemoSky starts lifting the sky at 4:45
        // and by SmallHoursEnd it is light enough that nobody would switch one on.
        const float BlackoutHour = 4f;
        const float BlackoutFade = 0.6f;

        // The hour DemoSky's night curve finally reaches zero - its sunrise plus
        // its twilight. The blackout is lifted here and nowhere earlier, and it
        // has to reach exactly this far: lift it at sunrise itself and the whole
        // city snaps back on for the last of the twilight, because the night it is
        // being scaled by has not run out yet.
        const float SmallHoursEnd = 7.25f;

        // How long a lot takes to go dark once its closing time comes round.
        const float SleepFade = 0.8f;

        // A closing time far enough out that no hour of the night reaches it: the
        // lots that are still burning when the blackout arrives.
        const float Never = 99f;

        // How a lot burns through the night.
        readonly struct Lot
        {
            public readonly float Onset;  // where in the dusk ramp it comes on
            public readonly float Sleep;  // when it turns in, on the small-hours
                                          // clock: 1 is one in the morning, -2 is
                                          // ten at night, Never is not at all
            public readonly Color Tint;   // colour times brightness, at full night
            public readonly bool Vigil;   // exempt from the four o'clock blackout

            public Lot(float onset, float sleep, Color tint, bool vigil)
            {
                Onset = onset;
                Sleep = sleep;
                Tint = tint;
                Vigil = vigil;
            }
        }

        enum Kind { Home, Trade, Landmark }

        [System.Flags]
        enum SurfaceKind { None = 0, Pane = 1, Emissive = 2 }

        // A driven material and the colour it burns at full night.
        readonly struct Lamp
        {
            public readonly Material Material;
            public readonly Color Colour;

            public Lamp(Material material, Color colour)
            {
                Material = material;
                Colour = colour;
            }
        }

        Lot[] _lots;
        readonly List<Lamp>[] _lamps = new List<Lamp>[Lots];
        readonly float[] _applied = new float[Lots];
        readonly Dictionary<(Material, int), Material> _clones =
            new Dictionary<(Material, int), Material>(256);
        readonly Dictionary<Material, SurfaceKind> _surfaceKinds =
            new Dictionary<Material, SurfaceKind>(128);
        readonly HashSet<Renderer> _registered = new HashSet<Renderer>(32768);
        readonly List<Renderer> _rendererScratch = new List<Renderer>();
        readonly List<Material> _materialScratch = new List<Material>();
        int _paneMaterials, _signMaterials, _darkBuildings;

        void Start()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            EnsureReady();
            Register(null);

            if (_paneMaterials + _signMaterials == 0)
            {
                enabled = false;
                Debug.LogWarning("[RoadDemo] Nothing to light after dark - no glass and no unlit " +
                                 "emissive materials found; facades stay as the pack left them.",
                                 this);
                return;
            }

            Debug.Log($"[RoadDemo] Night windows: {_paneMaterials} pane materials and {_signMaterials} emissive " +
                      $"atlases across {Lots} lots, {_darkBuildings} buildings left dark.", this);
            clock.Stop();
            // one pass over EVERY renderer in the city, cloning each emissive
            // material. Timed for the same reason as DemoStreetLamps: the first
            // frames of Play cost tens of seconds and the frame probe cannot see
            // Start-phase work at all.
            Debug.Log($"[DemoNightWindows] Start took {clock.ElapsedMilliseconds} ms");
        }

        void EnsureReady()
        {
            if (_lots != null) return;
            _lots = BuildLots();
            for (int i = 0; i < Lots; i++)
            {
                _lamps[i] = new List<Lamp>();
                _applied[i] = -1f;
            }
        }

        /// <summary>
        /// Wire renderers that arrived after the scene build. A null root performs the old
        /// whole-scene pass; the block recycler passes one newly bound ViewHolder subtree.
        /// Calling twice is harmless, which also removes any Start-order dependency.
        /// </summary>
        public void Register(Transform root)
        {
            EnsureReady();
            IList<Renderer> renderers;
            if (root != null)
            {
                _rendererScratch.Clear();
                root.GetComponentsInChildren(true, _rendererScratch);
                renderers = _rendererScratch;
            }
            else renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            int before = _paneMaterials + _signMaterials;
            for (int r = 0; r < renderers.Count; r++)
            {
                var renderer = renderers[r];
                if (renderer == null || !_registered.Add(renderer)) continue;
                // A streamed root only contains residential-view renderers, so every one
                // is a facade candidate and belongs to the same Home policy its res-
                // ancestor would have selected. Avoid walking/naming that hierarchy again
                // for every pooled bind.
                bool facade = root != null || (facadeRoot
                    ? renderer.transform.IsChildOf(facadeRoot)
                    : IsBuilding(renderer.transform));
                _materialScratch.Clear();
                renderer.GetSharedMaterials(_materialScratch);
                int lot = -2;
                bool touched = false;

                for (int i = 0; i < _materialScratch.Count; i++)
                {
                    var original = _materialScratch[i];
                    if (!original) continue;

                    var surface = SurfaceOf(original);
                    bool pane = facade && (surface & SurfaceKind.Pane) != 0;
                    bool emissive = (surface & SurfaceKind.Emissive) != 0 &&
                                    (!AlreadyEmissive(original) || facade);
                    if (!pane && !emissive) continue;
                    if (lot == -2)
                    {
                        lot = LotOf(renderer.transform.position,
                                    root != null ? Kind.Home : KindOf(renderer.transform));
                        if (lot < 0) _darkBuildings++;
                    }
                    if (lot < 0) break;

                    if (!_clones.TryGetValue((original, lot), out var clone))
                    {
                        clone = MakeNightMaterial(original, pane);
                        _clones[(original, lot)] = clone;
                        float intensity = pane ? PaneIntensity : AtlasIntensity;
                        _lamps[lot].Add(new Lamp(clone, _lots[lot].Tint * intensity));
                        if (pane) _paneMaterials++; else _signMaterials++;
                    }
                    _materialScratch[i] = clone;
                    touched = true;
                }
                if (touched) renderer.SetSharedMaterials(_materialScratch);
            }
            _materialScratch.Clear();
            if (root != null) _rendererScratch.Clear();
            if (_paneMaterials + _signMaterials > before) enabled = true;
        }

        SurfaceKind SurfaceOf(Material material)
        {
            if (_surfaceKinds.TryGetValue(material, out var known)) return known;
            var found = SurfaceKind.None;
            if (IsPane(material)) found |= SurfaceKind.Pane;
            if (EmissionMapOf(material)) found |= SurfaceKind.Emissive;
            _surfaceKinds[material] = found;
            return found;
        }

        /// <summary>Forget destroyed renderer identities while keeping the shared night materials cached.</summary>
        public void Unregister(Transform root)
        {
            if (root == null || _registered.Count == 0) return;
            _rendererScratch.Clear();
            root.GetComponentsInChildren(true, _rendererScratch);
            for (int i = 0; i < _rendererScratch.Count; i++)
                if (!ReferenceEquals(_rendererScratch[i], null)) _registered.Remove(_rendererScratch[i]);
            _rendererScratch.Clear();
        }

        /// <summary>
        /// The lot table. Seeded, so a run looks the same twice and a lot's
        /// behaviour can be recognised across the city.
        /// </summary>
        static Lot[] BuildLots()
        {
            var lots = new Lot[Lots];
            var rng = new System.Random(9187);

            // Homes come on across the whole dusk. Most stay alive through eleven,
            // then go out in a stagger from 22:45 to 01:45 instead of leaving the
            // whole residential city blazing until half three.
            for (int i = 0; i < HomeLots; i++)
                lots[i] = new Lot(
                    0.05f + 0.6f * (i / (float)(HomeLots - 1)),
                    -1.25f + 3f * (float)rng.NextDouble(),
                    Pick(HomeTints, rng) * (0.62f + 0.28f * (float)rng.NextDouble()),
                    false);

            // The night shift: a landing light, a baby, somebody working. Dim, and
            // the only thing besides the landmarks still on at five.
            lots[VigilLot] = new Lot(0.12f, Never, Tungsten * 0.7f, true);

            // Trade lights on early - the sign goes on before the light does - and
            // shuts at closing time, which is spread from ten to half past midnight in the
            // morning across the lots. All but the last of them: something has to
            // still be burning for the four o'clock blackout to take, so the last
            // lot is the all-night end of the trade - the garage, the hotel lobby,
            // the signage nobody has the key for.
            for (int i = 0; i < TradeLots; i++)
            {
                bool allNight = i == TradeLots - 1;
                lots[TradeLot + i] = new Lot(
                    0.3f * (float)rng.NextDouble(),
                    allNight ? Never : -2f + 2.5f * (i / (TradeLots - 2f)),
                    Pick(TradeTints, rng) * (0.72f + 0.25f * (float)rng.NextDouble()),
                    false);
            }

            lots[LandmarkLot] = new Lot(0f, Never, Tungsten * 0.72f, true);
            return lots;
        }

        static Color Pick(Color[] from, System.Random rng) => from[rng.Next(from.Length)];

        /// <summary>
        /// What this renderer belongs to, read off its own name and its parents'.
        /// The nearest name wins, so a landmark parked inside a residential block
        /// is still a landmark.
        /// </summary>
        Kind KindOf(Transform of)
        {
            for (var node = of; node && node != facadeRoot; node = node.parent)
            {
                var name = node.name;

                if (landmarks != null)
                    foreach (var landmark in landmarks)
                        if (!string.IsNullOrEmpty(landmark) && Mentions(name, landmark))
                            return Kind.Landmark;

                foreach (var home in HomeNames)
                    if (Mentions(name, home))
                        return Kind.Home;
            }

            return Kind.Trade;
        }

        static bool Mentions(string name, string word) =>
            name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Which lot a building draws, or -1 for a home that stays dark. Hashed
        /// off its position so it is stable: the same building lights the same way
        /// every run, and nothing has to be stored per renderer.
        /// </summary>
        static int LotOf(Vector3 position, Kind kind)
        {
            if (kind == Kind.Landmark)
                return LandmarkLot;

            float r = Hash01(position);

            if (kind == Kind.Trade)
                return TradeLot + Mathf.Min(TradeLots - 1, (int)(r * TradeLots));

            if (r < HomeDark)
                return -1;

            // renormalised twice so the shares below are shares of the homes that
            // light, not of all of them
            r = (r - HomeDark) / (1f - HomeDark);
            if (r < VigilShare)
                return VigilLot;

            r = (r - VigilShare) / (1f - VigilShare);
            return Mathf.Min(HomeLots - 1, (int)(r * HomeLots));
        }

        static float Hash01(Vector3 position)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(position.x) * 73856093
                              ^ Mathf.RoundToInt(position.y) * 83492791
                              ^ Mathf.RoundToInt(position.z) * 19349663);
                h ^= h >> 13;
                h *= 2654435761u;
                h ^= h >> 16;

                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>
        /// Whether this is a building window pane: a glass material, whether the
        /// pack shipped its emission enabled or not.
        ///
        /// Matched on the name because that is the only thing the kits agree on -
        /// the panes come from five different packs (Generic_Glass_Opaque,
        /// PalmCity Glass_01..03, PolygonGangWarfare_01_Glass, the police
        /// station's Glass_01 and Security_Glass_*) with nothing else in common.
        /// Two families of name have to be kept out: a vehicle's windscreen, and
        /// the "Glasses" a character wears.
        /// </summary>
        static bool IsPane(Material material)
        {
            var name = material.name;
            if (!Mentions(name, "Glass"))
                return false;
            if (Mentions(name, "Vehicle") || Mentions(name, "Glasses"))
                return false;

            return true;
        }

        /// <summary>
        /// The real emission map of a material that carries one, or null.
        ///
        /// Several pack materials have a NORMAL map sitting in the emission slot;
        /// lighting those paints the facade in normal-map lilac, so the texture
        /// has to actually be an emissive one. Already-burning maps are retimed
        /// only when their renderer is architecture; props and vehicles keep the
        /// pack's authored treatment.
        /// </summary>
        static Texture EmissionMapOf(Material material)
        {
            Texture map = null;
            if (material.HasProperty(SyntyEmissionMap))
                map = material.GetTexture(SyntyEmissionMap);
            if (!map && material.HasProperty(EmissionMap))
                map = material.GetTexture(EmissionMap);

            return map && Mentions(map.name, "Emissive") ? map : null;
        }

        /// <summary>Whether a renderer belongs to architecture when Core has no single
        /// facade root. Existing pack emissives are retimed only on buildings: the same
        /// PalmCity material also covers power lines, cars and street props, whose authored
        /// emission must not be mistaken for an occupied window.</summary>
        static bool IsBuilding(Transform transform)
        {
            for (var at = transform; at; at = at.parent)
            {
                string name = at.name;
                if (name.StartsWith("SM_Veh_", System.StringComparison.OrdinalIgnoreCase) ||
                    Mentions(name, "Traffic") || Mentions(name, "Parking Car") ||
                    Mentions(name, "Patrol Car"))
                    return false;
                if (name.StartsWith("SM_Bld_", System.StringComparison.OrdinalIgnoreCase) ||
                    Mentions(name, "building") || Mentions(name, "apartment") ||
                    Mentions(name, "residential") || Mentions(name, "Palm Tower"))
                    return true;
            }
            return false;
        }

        // Emissive by either convention: the Synty toggle, or - for materials that
        // have no toggle - a standard emission colour that is already bright.
        static bool AlreadyEmissive(Material material)
        {
            if (material.HasProperty(EnableEmission))
                return material.GetFloat(EnableEmission) > 0.5f;

            return material.HasProperty(EmissionColor)
                   && material.GetColor(EmissionColor).maxColorComponent > 0.01f;
        }

        static Material MakeNightMaterial(Material original, bool pane)
        {
            // a clone, never the asset: writing emission onto a shared material
            // edits the file on disk the moment this runs in the editor, and leaves
            // every window in the project glowing long after the demo stopped
            var clone = new Material(original) { name = original.name + " (night)" };

            if (clone.HasProperty(EnableEmission))
                clone.SetFloat(EnableEmission, 1f);
            clone.EnableKeyword("_EMISSION");
            clone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // A pane has no emission map and must light flat across the whole
            // quad. Synty's graph defaults an unbound map to white, which would do
            // it, but only that graph promises so - a white texture in the slot
            // gets the same result out of anything.
            if (pane)
            {
                if (clone.HasProperty(SyntyEmissionMap))
                    clone.SetTexture(SyntyEmissionMap, Texture2D.whiteTexture);
                if (clone.HasProperty(EmissionMap))
                    clone.SetTexture(EmissionMap, Texture2D.whiteTexture);
            }

            // dark until the clock says otherwise - Start can run at noon
            if (clone.HasProperty(SyntyEmissionColor))
                clone.SetColor(SyntyEmissionColor, Color.black);
            if (clone.HasProperty(EmissionColor))
                clone.SetColor(EmissionColor, Color.black);

            return clone;
        }

        void LateUpdate()
        {
            float hour = clock ? clock.Hour : 12f;
            float night = DemoSky.Nightness(hour);
            float blackout = Blackout(hour);

            for (int b = 0; b < Lots; b++)
            {
                var lamps = _lamps[b];
                if (lamps.Count == 0)
                    continue;

                float lit = LotNight(_lots[b], hour, night);
                if (!_lots[b].Vigil)
                    lit *= blackout;

                // a colour write per material per frame is cheap, but it also
                // dirties them and there is no reason to do it while the sun is
                // high and nothing is changing
                if (Mathf.Approximately(lit, _applied[b]))
                    continue;

                _applied[b] = lit;

                foreach (var lamp in lamps)
                {
                    if (!lamp.Material)
                        continue;

                    var emission = lamp.Colour * lit;
                    if (lamp.Material.HasProperty(SyntyEmissionColor))
                        lamp.Material.SetColor(SyntyEmissionColor, emission);
                    if (lamp.Material.HasProperty(EmissionColor))
                        lamp.Material.SetColor(EmissionColor, emission);
                }
            }
        }

        /// <summary>
        /// How lit this lot is: the demo's one night curve, entered late by the
        /// lot's own onset, and given up again at its own bedtime.
        /// </summary>
        static float LotNight(Lot lot, float hour, float night)
        {
            float lit = lot.Onset <= 0f
                ? night
                : Mathf.Clamp01((night - lot.Onset) / (1f - lot.Onset));

            // The small-hours clock, on which the evening runs negative into
            // midnight: half nine is -2.5, one in the morning is 1. Closing times
            // are written the same way, so a shop that shuts before midnight and a
            // flat whose light goes off at three are the one piece of arithmetic,
            // and neither of them can be tripped by the daylight hours - those sit
            // far below any lot's closing time, and are zero night anyway.
            float small = hour >= 12f ? hour - 24f : hour;

            return lit * (1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(lot.Sleep, lot.Sleep + SleepFade, small)));
        }

        /// <summary>
        /// The four o'clock blackout: 1 the rest of the night, 0 from half four
        /// until the night curve itself runs out. Every lot but the night shift
        /// and the landmarks is scaled by it.
        /// </summary>
        static float Blackout(float hour)
        {
            if (hour <= BlackoutHour || hour >= SmallHoursEnd)
                return 1f;

            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(BlackoutHour, BlackoutHour + BlackoutFade, hour));
        }

        void OnDestroy()
        {
            foreach (var lamps in _lamps)
            {
                if (lamps == null)
                    continue;
                foreach (var lamp in lamps)
                    if (lamp.Material)
                        Destroy(lamp.Material);
                lamps.Clear();
            }
        }
    }
}
