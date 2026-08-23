using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadDemo
{
    // The demo's colour grade, lifted from the PolygonPalmCity demo scene's own
    // Global Volume (Assets/Synty/PolygonPalmCity/Volumes/Global Volume_Base_01):
    // bloom, vignette, a white balance push, a lift/gamma/gain split and a lift in
    // exposure/contrast/saturation. Without a volume the demo rendered raw URP -
    // flat, grey and unsaturated next to the pack's own screenshots, which is the
    // single biggest reason the two scenes did not look alike.
    //
    // Built in code rather than referencing the pack's asset: the profile is a
    // shared asset, and this grade has to move with the clock (a fixed daytime
    // grade crushes the night, and a night grade washes out noon). Everything is
    // demo-local, like the rest of this folder.
    //
    // One addition on top of the pack's list: Neutral tonemapping. The project's
    // default volume profile leaves tonemapping at None, and the pack's grade
    // pushes exposure +0.4 with bloom over an HDR buffer - without a tonemapper
    // the sky and the lamp glow clip to white.
    [RequireComponent(typeof(Volume))]
    public class DemoGrade : MonoBehaviour
    {
        /// <summary>Whose demo scene this grade is copying. The two Synty packs the
        /// city is built out of ship their own Global Volume and they do NOT agree:
        /// PalmCity pushes exposure and warmth for a coast at noon, PolygonCity sits
        /// darker and flatter with a shadow/midtone split that is most of why its
        /// screenshots read as a working city rather than a model. The city keeps
        /// PalmCity; the block lab asks for PolygonCity, because what it is judging is
        /// whether a PolygonCity interior looks like PolygonCity's own.</summary>
        public enum Look { PalmCity, PolygonCity }
        public Look look = Look.PalmCity;

        public DemoClock clock;

        VolumeProfile _profile;
        ColorAdjustments _colour;
        WhiteBalance _balance;
        Bloom _bloom;
        Vignette _vignette;

        // -- PalmCity's daytime numbers -----------------------------------------
        const float DayExposure = 0.4f;
        const float DayContrast = 14f;
        const float DaySaturation = 16f;
        const float DayTemperature = 16f;
        const float DayBloom = 0.9f;
        const float DayVignette = 0.4f;

        // -- PolygonCity's, read off its own Demo scene's Global Volume Profile
        // (Assets/Synty/PolygonCity/Scenes/Demo/Global Volume Profile.asset):
        // ColorAdjustments postExposure 0.1 / contrast 12 / saturation 15, Bloom
        // threshold 0.9 intensity 1, Vignette 0.4 smoothness 0.2. It sets no white
        // balance at all, so the temperature push goes to zero with it.
        const float CityExposure = 0.1f;
        const float CityContrast = 12f;
        const float CitySaturation = 15f;
        const float CityTemperature = 0f;
        const float CityBloom = 1f;
        const float CityVignette = 0.4f;

        float DayEx => look == Look.PolygonCity ? CityExposure : DayExposure;
        float DayCo => look == Look.PolygonCity ? CityContrast : DayContrast;
        float DaySa => look == Look.PolygonCity ? CitySaturation : DaySaturation;
        float DayTe => look == Look.PolygonCity ? CityTemperature : DayTemperature;
        float DayBl => look == Look.PolygonCity ? CityBloom : DayBloom;
        float DayVi => look == Look.PolygonCity ? CityVignette : DayVignette;

        // -- and what the night wants instead ------------------------------------
        // dimmer and cooler, with the bloom opened up so the street lamps, the
        // headlights and the lit windows carry the frame once the sun is gone
        const float NightExposure = 0.1f;
        const float NightContrast = 8f;
        const float NightSaturation = -4f;
        const float NightTemperature = -10f;
        const float NightBloom = 1.7f;
        const float NightVignette = 0.5f;

        void Start()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "RoadDemo Grade";

            var volume = GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;   // over the project's default profile
            volume.weight = 1f;
            volume.sharedProfile = _profile;

            var tonemap = _profile.Add<Tonemapping>(true);
            tonemap.mode.value = TonemappingMode.Neutral;

            _colour = _profile.Add<ColorAdjustments>(true);
            _balance = _profile.Add<WhiteBalance>(true);
            _balance.tint.overrideState = false;

            // the pack's split: a green-blue lift, warm gamma, cool gain
            var lgg = _profile.Add<LiftGammaGain>(true);
            if (look == Look.PolygonCity)
            {
                lgg.lift.value = new Vector4(0.9589f, 0.9382f, 1f, 0f);
                lgg.gamma.value = new Vector4(1f, 0.9536f, 0.9434f, 0f);
                lgg.gain.value = new Vector4(0.9905f, 0.9760f, 1f, 0f);

                // and the piece PalmCity has no equivalent of: the shadows go green,
                // the midtones go magenta and the shadows come DOWN 0.18. This is the
                // separation between a facade in sun and the same facade in shade that
                // reads as depth in the pack's own screenshots.
                var smh = _profile.Add<ShadowsMidtonesHighlights>(true);
                smh.shadows.value = new Vector4(0.9786f, 1f, 0.9149f, -0.1832f);
                smh.midtones.value = new Vector4(1f, 0.9457f, 0.9864f, 0f);
            }
            else
            {
                lgg.lift.value = new Vector4(0.998f, 1f, 0.980f, 0f);
                lgg.gamma.value = new Vector4(1f, 0.925f, 0.927f, 0f);
                lgg.gain.value = new Vector4(0.976f, 0.925f, 1f, 0f);
            }

            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.value = 0.9f;
            _bloom.scatter.value = look == Look.PolygonCity ? 0.7f : 0.6f;

            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.value = Color.black;
            if (look == Look.PolygonCity) _vignette.smoothness.value = 0.2f;

            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            float night = DemoSky.Nightness(clock ? clock.Hour : 15f);

            // the night numbers are shared: PolygonCity's demo scene is a daylight
            // scene and has none of its own, and a city with a clock in it still has
            // to have a night
            _colour.postExposure.value = Mathf.Lerp(DayEx, NightExposure, night);
            _colour.contrast.value = Mathf.Lerp(DayCo, NightContrast, night);
            _colour.saturation.value = Mathf.Lerp(DaySa, NightSaturation, night);
            _balance.temperature.value = Mathf.Lerp(DayTe, NightTemperature, night);
            _bloom.intensity.value = Mathf.Lerp(DayBl, NightBloom, night);
            _vignette.intensity.value = Mathf.Lerp(DayVi, NightVignette, night);
        }

        void OnDestroy()
        {
            if (_profile)
                Destroy(_profile);
        }
    }
}
