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
            lgg.lift.value = new Vector4(0.998f, 1f, 0.980f, 0f);
            lgg.gamma.value = new Vector4(1f, 0.925f, 0.927f, 0f);
            lgg.gain.value = new Vector4(0.976f, 0.925f, 1f, 0f);

            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.value = 0.9f;
            _bloom.scatter.value = 0.6f;

            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.value = Color.black;

            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            float night = DemoSky.Nightness(clock ? clock.Hour : 15f);

            _colour.postExposure.value = Mathf.Lerp(DayExposure, NightExposure, night);
            _colour.contrast.value = Mathf.Lerp(DayContrast, NightContrast, night);
            _colour.saturation.value = Mathf.Lerp(DaySaturation, NightSaturation, night);
            _balance.temperature.value = Mathf.Lerp(DayTemperature, NightTemperature, night);
            _bloom.intensity.value = Mathf.Lerp(DayBloom, NightBloom, night);
            _vignette.intensity.value = Mathf.Lerp(DayVignette, NightVignette, night);
        }

        void OnDestroy()
        {
            if (_profile)
                Destroy(_profile);
        }
    }
}
