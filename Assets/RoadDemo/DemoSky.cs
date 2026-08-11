using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    // The demo's sky, self-contained. Unity's procedural skybox (the same sky the
    // PalmCity pack's own demo scene uses) driven by the sun, the PalmCity cloud
    // ring drifting over the grid, a neutral white sun that dips below the horizon
    // at dusk, and a dim blue moon for the ground at night.
    //
    // Deliberately NOT the city's CityWeather: its ClearDusk grade is a period
    // sepia tuned for the main game - on this demo it read as a permanent orange
    // filter - and it clears the skybox to a solid horizon colour, the opposite of
    // the sky the demo wants. The sun stays neutral white at height and only warms
    // as it approaches the horizon, so the sunset lives at sunset.
    public class DemoSky : MonoBehaviour
    {
        public DemoClock clock;
        public Light sun;
        public Transform cloudRing;    // slow drift
        public Renderer cloudRenderer; // night tint

        Light _moon;
        Material _skybox;
        Material _cloudMat;
        Color _cloudTop, _cloudBase, _cloudFresnel;

        // -- the day's shape ---------------------------------------------------
        const float SunriseHour = 6f;
        const float SunsetHour = 20f;
        const float TwilightHours = 1.25f;
        const float NoonElevation = 62f;
        const float DawnAzimuth = -75f;
        const float DuskAzimuth = 75f;

        // -- sun ---------------------------------------------------------------
        static readonly Color SunHigh = new Color(1f, 0.972f, 0.925f);
        static readonly Color SunLow = new Color(1f, 0.72f, 0.45f);
        const float SunIntensity = 1.35f;

        // -- night -------------------------------------------------------------
        static readonly Color MoonColour = new Color(0.35f, 0.42f, 0.72f);
        const float MoonIntensity = 0.45f;
        const float MoonElevationScale = 0.7f;

        static readonly Color DayAmbientSky = new Color(0.55f, 0.62f, 0.72f);
        static readonly Color DayAmbientEquator = new Color(0.42f, 0.44f, 0.47f);
        static readonly Color DayAmbientGround = new Color(0.24f, 0.23f, 0.21f);

        static readonly Color NightAmbientSky = new Color(0f, 0.079f, 0.236f);
        static readonly Color NightAmbientEquator = new Color(0f, 0.177f, 0.292f);
        static readonly Color NightAmbientGround = Color.black;

        // light haze only - the fog must not eat the sky or the cloud ring
        static readonly Color DayFog = new Color(0.74f, 0.80f, 0.88f);
        static readonly Color NightFog = new Color(0.037f, 0.14f, 0.231f);
        const float DayFogDensity = 0.0012f;
        const float NightFogDensity = 0.0016f;

        static readonly Color CloudTopNight = new Color(0.10f, 0.12f, 0.20f);
        static readonly Color CloudBaseNight = new Color(0.05f, 0.07f, 0.12f);
        const float CloudDriftDegPerSec = 0.25f;

        /// <summary>
        /// How much of the night is in force at this hour: 0 in full daylight, 1
        /// after dark, a smoothstep ramp across each twilight. The one curve the
        /// whole demo ramps on - the lamps and the headlights read it too, so the
        /// lights come on at exactly the moment the sky goes.
        /// </summary>
        public static float Nightness(float hour)
        {
            if (hour <= SunriseHour - TwilightHours || hour >= SunsetHour + TwilightHours)
                return 1f;

            if (hour >= SunriseHour + TwilightHours && hour <= SunsetHour - TwilightHours)
                return 0f;

            if (hour < SunriseHour + TwilightHours)
                return 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(SunriseHour - TwilightHours, SunriseHour + TwilightHours, hour));

            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SunsetHour - TwilightHours, SunsetHour + TwilightHours, hour));
        }

        void Start()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader)
            {
                _skybox = new Material(shader);
                _skybox.SetFloat("_SunSize", 0.045f);
                _skybox.SetFloat("_AtmosphereThickness", 1.05f);
                _skybox.SetColor("_GroundColor", new Color(0.47f, 0.45f, 0.40f));
                RenderSettings.skybox = _skybox;
            }

            // the procedural skybox reads the sun's DIRECTION off this - it is what
            // makes dusk arrive in the sky at the same moment it leaves the ground
            RenderSettings.sun = sun;

            var camera = Camera.main;
            if (camera)
                camera.clearFlags = CameraClearFlags.Skybox;

            // one moon, no shadows: URP has one main shadow-casting light and the
            // sun is it; the night look is carried by the street lamps anyway
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(transform, false);
            _moon = moonGo.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.color = MoonColour;
            _moon.intensity = 0f;
            _moon.shadows = LightShadows.None;
            _moon.lightmapBakeType = LightmapBakeType.Realtime;

            if (cloudRenderer)
            {
                // an instance - writing the shared material would edit the asset
                _cloudMat = cloudRenderer.material;
                _cloudTop = _cloudMat.GetColor("_Top_Color");
                _cloudBase = _cloudMat.GetColor("_Base_Color");
                _cloudFresnel = _cloudMat.GetColor("_Fresnel_Color");
            }

            Apply();
        }

        void LateUpdate()
        {
            Apply();

            if (cloudRing)
                cloudRing.Rotate(0f, CloudDriftDegPerSec * Time.deltaTime, 0f);
        }

        void Apply()
        {
            float hour = clock ? clock.Hour : 15f;
            float night = Nightness(hour);

            ApplySun(hour, night);
            ApplyMoon(hour, night);
            ApplyAtmosphere(night);
            ApplyClouds(night);
        }

        // The sun makes the FULL circle: above the horizon by day, below it at
        // night - which is what walks the procedural skybox through sunset glow
        // into genuine darkness with no blending code at all.
        void ApplySun(float hour, float night)
        {
            if (!sun)
                return;

            float elevation, azimuth;
            if (hour >= SunriseHour && hour <= SunsetHour)
            {
                float t = Mathf.InverseLerp(SunriseHour, SunsetHour, hour);
                elevation = Mathf.Sin(t * Mathf.PI) * NoonElevation;
                azimuth = Mathf.Lerp(DawnAzimuth, DuskAzimuth, t);
            }
            else
            {
                float sinceSunset = Mathf.Repeat(hour - SunsetHour, DemoClock.HoursPerDay);
                float nightLength = DemoClock.HoursPerDay - (SunsetHour - SunriseHour);
                float t = sinceSunset / nightLength;
                elevation = -Mathf.Sin(t * Mathf.PI) * NoonElevation * 0.6f;
                azimuth = Mathf.Lerp(DuskAzimuth, DawnAzimuth + 360f, t);
            }

            sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);

            // weaker and warmer near the horizon; gone once night has it
            float height = Mathf.Sin(Mathf.Max(0f, elevation) * Mathf.Deg2Rad);
            sun.color = Color.Lerp(SunLow, SunHigh, height);
            sun.intensity = SunIntensity * Mathf.Lerp(0.35f, 1f, height) * (1f - night);
            sun.shadows = LightShadows.Soft;
            sun.enabled = sun.intensity > 0.01f;
        }

        // The moon comes up where the sun went down, on a lower arc.
        void ApplyMoon(float hour, float night)
        {
            if (!_moon)
                return;

            float sinceSunset = Mathf.Repeat(hour - SunsetHour, DemoClock.HoursPerDay);
            float nightLength = DemoClock.HoursPerDay - (SunsetHour - SunriseHour);
            float t = Mathf.Clamp01(sinceSunset / nightLength);

            _moon.transform.rotation = Quaternion.Euler(
                Mathf.Sin(t * Mathf.PI) * NoonElevation * MoonElevationScale,
                Mathf.Lerp(DuskAzimuth, DawnAzimuth + 360f, t), 0f);
            _moon.intensity = MoonIntensity * night;
            _moon.enabled = night > 0.01f;
        }

        void ApplyAtmosphere(float night)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(DayAmbientSky, NightAmbientSky, night);
            RenderSettings.ambientEquatorColor = Color.Lerp(DayAmbientEquator, NightAmbientEquator, night);
            RenderSettings.ambientGroundColor = Color.Lerp(DayAmbientGround, NightAmbientGround, night);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(DayFog, NightFog, night);
            RenderSettings.fogDensity = Mathf.Lerp(DayFogDensity, NightFogDensity, night);

            if (_skybox)
                _skybox.SetFloat("_Exposure", Mathf.Lerp(1.25f, 0.7f, night));
        }

        void ApplyClouds(float night)
        {
            if (!_cloudMat)
                return;

            _cloudMat.SetColor("_Top_Color", Color.Lerp(_cloudTop, CloudTopNight, night));
            _cloudMat.SetColor("_Base_Color", Color.Lerp(_cloudBase, CloudBaseNight, night));
            _cloudMat.SetColor("_Fresnel_Color", Color.Lerp(_cloudFresnel, CloudBaseNight, night));
        }

        void OnDestroy()
        {
            if (_skybox)
                Destroy(_skybox);
            if (_cloudMat)
                Destroy(_cloudMat);
        }
    }
}
