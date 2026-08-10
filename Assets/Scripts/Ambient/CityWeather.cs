using UnityEngine;
using UnityEngine.Rendering;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Ambient
{
    /// <summary>
    /// The weather a city was built under. One roll per city, and then it holds - the sky changes
    /// with the hour, but a foggy city stays a foggy city.
    /// </summary>
    public enum WeatherKind
    {
        /// <summary>
        /// Clear air. Sepia and long shadows at the ends of the day, bright at noon.
        ///
        /// Named for the dusk it was first tuned at, from before the clock existed. The name is
        /// now a half-truth - dusk is an hour, not a weather - and is kept only because renaming
        /// it would orphan the grade asset already sitting in Assets/Settings/Weather.
        /// </summary>
        ClearDusk,

        /// <summary>Flat lead-grey overcast. The colour drained out of the whole city.</summary>
        Overcast,

        /// <summary>Coal haze thick enough to swallow the far end of the grid.</summary>
        Smog,
    }

    /// <summary>
    /// The sky. Two things decide it and they are kept apart on purpose:
    ///
    ///   WEATHER is rolled once from the city seed and never changes - it owns the fog, the
    ///   colour grade and how strong the key light is.
    ///
    ///   TIME comes from CityClock every frame - it owns where the sun is and how far into
    ///   night we are.
    ///
    /// Everything below is the blend of those two. Keeping them separate is what lets three
    /// weathers cover a whole day instead of needing a preset per hour: clear at seven in the
    /// evening is the sepia dusk, the same clear at noon is bright, and smog is smog either way.
    ///
    /// Night is genuinely dark rather than a blue filter, because NightWindows lights the city
    /// from its own windows - see there for why that costs nothing. Without it this would have
    /// needed hundreds of real lights, and there would be no night at all.
    ///
    /// The grades live in three VolumeProfile assets rather than in code, so they can be tuned
    /// in the Inspector without a recompile.
    /// </summary>
    public sealed class CityWeather : MonoBehaviour
    {
        /// <summary>What to do at Start. Anything but Rolled pins the weather for testing.</summary>
        public enum Choice
        {
            Rolled,
            ForceClearDusk,
            ForceOvercast,
            ForceSmog,
        }

        [SerializeField] CityConfig config;

        [Header("Scene wiring")]
        [Tooltip("Drives the sun round the sky. Left empty, the weather holds a fixed midday.")]
        [SerializeField] CityClock clock;

        [Tooltip("The key light. Left empty, the first directional light in the scene is used.")]
        [SerializeField] Light sun;

        [Tooltip("The global post-processing volume. Left empty, the first Volume is used.")]
        [SerializeField] Volume globalVolume;

        [Header("Grades")]
        [SerializeField] VolumeProfile clearDuskProfile;
        [SerializeField] VolumeProfile overcastProfile;
        [SerializeField] VolumeProfile smogProfile;

        [Header("Roll")]
        [SerializeField] Choice choice = Choice.Rolled;

        /// <summary>
        /// How much of the authored look actually lands, 1 being all of it.
        ///
        /// The Look table below is written at full strength and everything is pulled back toward
        /// the neutral baseline by this much on the way out, so the numbers stay readable as what
        /// each weather IS rather than as what is left after a reduction. Turning the whole grade
        /// up or down later is this one value.
        ///
        /// The sun's ANGLE is deliberately outside this. A raking dusk sun is composition, not
        /// intensity - dialling it back toward overhead would cost the long shadows, which are
        /// the best thing the look has, while saving nothing that reads as "too strong".
        /// </summary>
        const float Strength = 0.7f;

        /// <summary>
        /// What the scene looked like before any weather: the key light and ambient the project
        /// was authored with. Strength interpolates out of here, so Strength 0 is the old scene
        /// back, exactly.
        /// </summary>
        const float NeutralIntensity = 2f;
        const float NeutralTemperature = 5000f;
        const float NeutralShadowStrength = 1f;

        static readonly Color NeutralAmbientSky = new(0.212f, 0.227f, 0.259f);
        static readonly Color NeutralAmbientEquator = new(0.114f, 0.125f, 0.133f);
        static readonly Color NeutralAmbientGround = new(0.047f, 0.043f, 0.035f);

        // -- night, lifted from the pack's own "City - Night" demo scene -------------------
        //
        // Not invented. The pack ships a night demo, and its author picked these against these
        // exact models: a purple-blue key at 1.24, a deep blue gradient with a black floor, and
        // blue fog. Guessing a night palette when the artist who made the buildings already
        // published one would be work done twice and worse.
        //
        // What is NOT copied is their fog range - linear, 500m to 1500m - which is sized for a
        // terrain several times this grid. At that range our whole city sits inside the clear
        // zone and the fog may as well be off, so the density carries over as a proportion of
        // the daytime figure instead.

        /// <summary>Moonlight. Their key light, colour and intensity both.</summary>
        static readonly Color MoonColour = new(0.137f, 0.066f, 0.368f);
        const float MoonIntensity = 1.24f;

        /// <summary>Shadows go nearly off at night - moonlight does not throw a hard edge.</summary>
        const float MoonShadowStrength = 0.25f;

        static readonly Color NightAmbientSky = new(0f, 0.079f, 0.236f);
        static readonly Color NightAmbientEquator = new(0f, 0.177f, 0.292f);
        static readonly Color NightAmbientGround = Color.black;

        static readonly Color NightFog = new(0.074f, 0.280f, 0.462f);

        /// <summary>
        /// Night multiplier on the weather's fog. Held at 1 - an earlier 1.6 stacked onto Smog
        /// put the exp² wall at ~200m and swallowed half the grid from the play camera, and the
        /// densities in the Look table are already chosen against the grid, night included.
        /// </summary>
        const float NightFogScale = 1f;

        // -- the day's shape --------------------------------------------------------------

        const float SunriseHour = 6f;
        const float SunsetHour = 20f;

        /// <summary>How long dawn and dusk take. The blend either side of sunrise and sunset.</summary>
        const float TwilightHours = 1.25f;

        /// <summary>Sun height at noon. Below 90 so it always casts a shadow with some length.</summary>
        const float NoonElevation = 62f;

        /// <summary>Compass sweep across the day, east through south to west.</summary>
        const float DawnAzimuth = -75f;
        const float DuskAzimuth = 75f;

        /// <summary>Night lift from the config, defaulting to the pack demo's own level.</summary>
        float NightBrightness => config ? Mathf.Max(0f, config.nightBrightness) : 1f;

        /// <summary>The weather in force. Only meaningful once Apply has run.</summary>
        public WeatherKind Current { get; private set; }

        /// <summary>
        /// True while the strategic map's top-down camera is rendering: a lens 300m up is
        /// deep inside the smog, so the map sets this and the fog stands down. A gate
        /// rather than a save/restore because LateUpdate re-applies the density every
        /// frame - anything saved would be re-clobbered one frame later. Colour, ambient
        /// and the sun are untouched; the map keeps the weather's light, just not its haze.
        /// </summary>
        public static bool FogSuppressed;

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => FogSuppressed = false;

        void Start()
        {
            AdoptMissingWiring();
            Apply();

            Debug.Log($"[Weather] {Current} at {(clock ? clock.Display : "no clock")}, " +
                      $"nightness {Nightness(clock ? clock.Hour : 15f):F2}.", this);
        }

        /// <summary>
        /// Fills in any reference left empty in the Inspector.
        ///
        /// Every one of these fields was added to this component at a different time, and a
        /// component that was already in the scene deserialises the new one as null. The result
        /// is not an error anyone sees - it is a silent fallback to a hard-coded midday, which
        /// looks exactly like the clock being ignored. Finding them here costs one search at load
        /// and removes a whole class of "it works in the editor but not in Play".
        /// </summary>
        void AdoptMissingWiring()
        {
            if (!clock)
                clock = FindAnyObjectByType<CityClock>();

            if (!globalVolume)
                globalVolume = FindAnyObjectByType<Volume>();

            if (!sun)
                sun = FindDirectionalLight();

            if (!config)
            {
                var builder = FindAnyObjectByType<CityBuilder>();
                if (builder)
                    config = builder.Config;
            }
        }

        /// <summary>
        /// Rolls (or reads) the weather and puts it on the scene. Public and side-effect-complete
        /// so the editor preview and the game take exactly the same path - a preview that ran
        /// different code would be worth very little.
        /// </summary>
        public WeatherKind Apply()
        {
            Current = choice switch
            {
                Choice.ForceClearDusk => WeatherKind.ClearDusk,
                Choice.ForceOvercast => WeatherKind.Overcast,
                Choice.ForceSmog => WeatherKind.Smog,
                _ => Roll(),
            };

            Apply(Current);
            return Current;
        }

        /// <summary>
        /// Re-applies every frame once a clock is present, because the sun is now moving. Without
        /// a clock this never runs and the Start-time application stands, which is what keeps the
        /// weather usable on its own.
        /// </summary>
        void LateUpdate()
        {
            if (clock)
                Apply(Current);
        }

        /// <summary>Applies a specific weather, ignoring the roll. Used by the editor preview.</summary>
        public void Apply(WeatherKind kind)
        {
            Current = kind;

            var look = Look.For(kind);

            // No clock means no day: hold the hour the look was designed around rather than
            // defaulting to midnight and leaving someone staring at a black screen wondering
            // what broke.
            var hour = clock ? clock.Hour : 15f;
            var night = Nightness(hour);

            ApplySun(look, hour, night);
            ApplyAtmosphere(look, night);
            ApplyGrade(kind);
        }

        /// <summary>
        /// How much of the night is in force at this hour: 0 in full daylight, 1 after dark, and
        /// a smooth ramp across each twilight.
        ///
        /// Smoothstep rather than a straight line on purpose. A linear dusk reads as a dimmer
        /// switch being turned at a constant rate, which is the one thing sunset does not look
        /// like - it hangs, then goes quickly, then hangs again.
        /// </summary>
        public static float Nightness(float hour)
        {
            if (hour <= SunriseHour - TwilightHours || hour >= SunsetHour + TwilightHours)
                return 1f;

            if (hour >= SunriseHour + TwilightHours && hour <= SunsetHour - TwilightHours)
                return 0f;

            // Inside one of the two twilights. Work out how far through it we are, in the
            // direction that runs from night to day, then flip for dusk.
            if (hour < SunriseHour + TwilightHours)
                return 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(SunriseHour - TwilightHours, SunriseHour + TwilightHours, hour));

            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SunsetHour - TwilightHours, SunsetHour + TwilightHours, hour));
        }

        WeatherKind Roll()
        {
            // No config is not an error worth shouting about - it just means an unseeded scene,
            // and clear weather is the least surprising thing to hand back.
            if (!config)
                return WeatherKind.ClearDusk;

            return (WeatherKind)(Mix(config.seed + SeedOffsets.Weather) % 3);
        }

        /// <summary>
        /// An integer hash, standing in for the <c>new System.Random(seed).Next(3)</c> the rest of
        /// the generation uses.
        ///
        /// Not a stylistic preference. System.Random's FIRST draw is strongly correlated with its
        /// seed, which every other subsystem gets away with because they pull hundreds of numbers
        /// and only the stream as a whole matters. The weather pulls exactly one, and it shows:
        /// straight off the seed, every odd seed from 1 to 15 comes out Overcast, so anyone
        /// walking seeds 1, 3, 5 in the Inspector would conclude the roll was broken. Mixing the
        /// bits first scatters neighbouring seeds across all three.
        /// </summary>
        static int Mix(int value)
        {
            unchecked
            {
                var h = (uint)value;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>
        /// Swings the key light through the day and fades it into moonlight after dark.
        ///
        /// One light does both jobs. A separate moon would have to cross-fade with the sun, and
        /// during the swap both are up at once throwing two sets of shadows in different
        /// directions - which is exactly the moment anyone is looking at the sky.
        ///
        /// The light stops using colour temperature here and works in plain colour, because the
        /// night end of the blend is a purple that no black-body temperature can express. The
        /// weather's temperature still sets the daylight; it is just converted first.
        /// </summary>
        void ApplySun(in Look look, float hour, float night)
        {
            var light = sun ? sun : FindDirectionalLight();
            if (!light)
                return;

            light.transform.rotation = Quaternion.Euler(Elevation(hour), Azimuth(hour), 0f);

            var temperature = Mathf.Lerp(NeutralTemperature, look.SunTemperature, Strength);
            var daylight = Mathf.CorrelatedColorTemperatureToRGB(temperature);

            var dayIntensity = Mathf.Lerp(NeutralIntensity, look.SunIntensity, Strength)
                             * DaylightFalloff(hour);

            light.useColorTemperature = false;
            light.color = Color.Lerp(daylight, MoonColour, night);
            light.intensity = Mathf.Lerp(dayIntensity, MoonIntensity * NightBrightness, night);

            light.shadows = LightShadows.Soft;
            light.shadowStrength = Mathf.Lerp(
                Mathf.Lerp(NeutralShadowStrength, look.ShadowStrength, Strength),
                MoonShadowStrength, night);
        }

        /// <summary>
        /// Height of the key light. A sine over the daylight hours, continued through the night
        /// so the moon crosses the sky too - a fixed moon over a city with a running clock is
        /// the sort of thing nobody notices for a week and then cannot stop noticing.
        /// </summary>
        static float Elevation(float hour)
        {
            var daylight = SunsetHour - SunriseHour;

            if (hour >= SunriseHour && hour <= SunsetHour)
                return Mathf.Sin(Mathf.InverseLerp(SunriseHour, SunsetHour, hour) * Mathf.PI)
                     * NoonElevation;

            // Night, measured from sunset round to sunrise. Lower arc: moonlight rakes.
            var sinceSunset = Mathf.Repeat(hour - SunsetHour, CityClock.HoursPerDay);
            var nightLength = CityClock.HoursPerDay - daylight;

            return Mathf.Sin(sinceSunset / nightLength * Mathf.PI) * NoonElevation * 0.7f;
        }

        static float Azimuth(float hour)
        {
            if (hour >= SunriseHour && hour <= SunsetHour)
                return Mathf.Lerp(DawnAzimuth, DuskAzimuth,
                    Mathf.InverseLerp(SunriseHour, SunsetHour, hour));

            // The moon comes up where the sun went down and vice versa.
            var sinceSunset = Mathf.Repeat(hour - SunsetHour, CityClock.HoursPerDay);
            var nightLength = CityClock.HoursPerDay - (SunsetHour - SunriseHour);

            return Mathf.Lerp(DuskAzimuth, DawnAzimuth + 360f, sinceSunset / nightLength);
        }

        /// <summary>
        /// Daylight is weaker near the horizon. Follows the elevation rather than the clock so it
        /// stays right whatever the sunrise and sunset hours are set to, and floors at 0.35 so a
        /// low sun still reads as a sun rather than going out before the twilight has begun.
        /// </summary>
        static float DaylightFalloff(float hour) =>
            Mathf.Lerp(0.35f, 1f, Mathf.Sin(Mathf.Max(0f, Elevation(hour)) * Mathf.Deg2Rad));

        /// <summary>
        /// Fog, ambient and the horizon behind the city.
        ///
        /// The skybox is cleared on purpose. Unity does not fog the skybox, so leaving the default
        /// blue one up puts a bright clear sky behind a sooty city and undoes the whole effect.
        /// A solid horizon colour matched to the fog makes the far end of the grid dissolve into
        /// nothing instead, which is the look and is also cheaper than drawing a sky.
        ///
        /// Clearing the skybox means skybox-based ambient would go black, so the ambient source
        /// moves to the three-colour gradient, which each weather sets for itself.
        /// </summary>
        void ApplyAtmosphere(in Look look, float night)
        {
            RenderSettings.skybox = null;

            // The night ambient is scaled, not the day's: nightBrightness exists to make an
            // unreadable night readable, and lifting the daylight with it would be a different
            // and unasked-for change.
            var lift = NightBrightness;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(
                Color.Lerp(NeutralAmbientSky, look.AmbientSky, Strength), NightAmbientSky * lift, night);
            RenderSettings.ambientEquatorColor = Color.Lerp(
                Color.Lerp(NeutralAmbientEquator, look.AmbientEquator, Strength), NightAmbientEquator * lift, night);
            RenderSettings.ambientGroundColor = Color.Lerp(
                Color.Lerp(NeutralAmbientGround, look.AmbientGround, Strength), NightAmbientGround, night);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            // Density scales, colour does not - within a weather. Thinning the fog is what "less
            // of it" means; washing its colour out would decouple it from the horizon behind, and
            // the far edge of the grid would stop dissolving and start showing a seam. Night is a
            // different matter: there the colour has to travel too, or a sepia haze hangs over a
            // blue city.
            RenderSettings.fogColor = Color.Lerp(look.FogColor, NightFog, night);
            RenderSettings.fogDensity = FogSuppressed
                ? 0f
                : look.FogDensity * Strength * Mathf.Lerp(1f, NightFogScale, night);

            var camera = Camera.main;
            if (camera)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;

                // The horizon tracks the fog, and at night it goes darker than the fog itself so
                // the sky reads as sky rather than as more fog standing upright.
                camera.backgroundColor = Color.Lerp(look.Horizon, NightFog * 0.45f, night);
            }
        }

        void ApplyGrade(WeatherKind kind)
        {
            var volume = globalVolume ? globalVolume : FindAnyObjectByType<Volume>();
            if (!volume)
                return;

            var profile = kind switch
            {
                WeatherKind.Overcast => overcastProfile,
                WeatherKind.Smog => smogProfile,
                _ => clearDuskProfile,
            };

            // Assigning sharedProfile rather than profile: profile clones the asset per instance,
            // which in the editor quietly leaves an orphaned copy behind every single preview.
            if (profile)
                volume.sharedProfile = profile;
        }

        static Light FindDirectionalLight()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional)
                    return light;

            return null;
        }

        /// <summary>
        /// The three looks, as plain numbers.
        ///
        /// Fog densities are chosen against the size of the grid rather than picked for taste:
        /// exponential-squared fog is total at roughly <c>2 / density</c> metres, so 0.003 puts
        /// the wall at ~670m (past the whole city - a haze) and 0.005 at ~400m (the far edge
        /// fades). Nothing goes denser than that: an earlier 0.009 Smog put the wall at ~220m and
        /// erased half the grid, so Smog now matches Overcast on density and leans on its colour
        /// and grade instead - sooty warm fog against neutral grey.
        /// </summary>
        readonly struct Look
        {
            // No sun angle here any more: the clock owns where the sun is, and a weather that
            // also had an opinion about it would be two authorities on one transform.
            public readonly float SunIntensity, SunTemperature, ShadowStrength;
            public readonly Color FogColor;
            public readonly float FogDensity;
            public readonly Color AmbientSky, AmbientEquator, AmbientGround, Horizon;

            Look(float sunIntensity, float sunTemperature,
                 float shadowStrength, Color fogColor, float fogDensity,
                 Color ambientSky, Color ambientEquator, Color ambientGround, Color horizon)
            {
                SunIntensity = sunIntensity;
                SunTemperature = sunTemperature;
                ShadowStrength = shadowStrength;
                FogColor = fogColor;
                FogDensity = fogDensity;
                AmbientSky = ambientSky;
                AmbientEquator = ambientEquator;
                AmbientGround = ambientGround;
                Horizon = horizon;
            }

            public static Look For(WeatherKind kind) => kind switch
            {
                // Sun almost on the deck, so every building throws a shadow the length of a block.
                // 3100K is well below the 5000K the scene used to sit at, which is where the sepia
                // comes from before a single post-processing effect runs.
                WeatherKind.ClearDusk => new Look(
                    sunIntensity: 2.4f, sunTemperature: 3100f,
                    shadowStrength: 0.85f,
                    fogColor: new Color(0.55f, 0.42f, 0.30f), fogDensity: 0.003f,
                    ambientSky: new Color(0.30f, 0.26f, 0.24f),
                    ambientEquator: new Color(0.22f, 0.19f, 0.17f),
                    ambientGround: new Color(0.10f, 0.08f, 0.06f),
                    horizon: new Color(0.62f, 0.46f, 0.30f)),

                // High and weak, with the shadows most of the way off: under cloud there is no
                // single sun direction left, and hard shadows are the tell that gives away a
                // "cloudy" scene that is really just a dimmed clear one.
                WeatherKind.Overcast => new Look(
                    sunIntensity: 1.15f, sunTemperature: 7200f,
                    shadowStrength: 0.35f,
                    fogColor: new Color(0.52f, 0.53f, 0.55f), fogDensity: 0.005f,
                    ambientSky: new Color(0.42f, 0.44f, 0.47f),
                    ambientEquator: new Color(0.34f, 0.35f, 0.37f),
                    ambientGround: new Color(0.18f, 0.18f, 0.19f),
                    horizon: new Color(0.58f, 0.59f, 0.61f)),

                // Sun low enough to rake through the haze and warm enough to look filtered by it.
                // The dense fog does the rest.
                _ => new Look(
                    sunIntensity: 1.7f, sunTemperature: 4200f,
                    shadowStrength: 0.5f,
                    fogColor: new Color(0.44f, 0.40f, 0.31f), fogDensity: 0.005f,
                    ambientSky: new Color(0.34f, 0.31f, 0.25f),
                    ambientEquator: new Color(0.27f, 0.25f, 0.20f),
                    ambientGround: new Color(0.13f, 0.12f, 0.09f),
                    horizon: new Color(0.50f, 0.45f, 0.34f)),
            };
        }
    }
}
