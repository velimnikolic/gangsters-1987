using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // A real spot light in every street lamp the demo places, burning after dark.
    // Self-contained (the demo does not use the city's StreetLampLights): one walk
    // over the scene at Start finds the lamps by prefab name, hangs a down-facing
    // spot under each lantern, and after that only intensity and the lit set move.
    //
    // Only the nearest LitLampBudget bulbs to the camera's ground focus burn - URP
    // Forward+ renders at most 256 additional lights per frame on desktop and the
    // headlights need their share of that too (DemoHeadlights).
    public class DemoStreetLamps : MonoBehaviour
    {
        public DemoClock clock;

        // Bulb points are in the lamp's local space and FIXED, measured off the
        // Synty prefabs, not off the instance's mesh: the lamp geometry is under
        // the static-batched root, and static batching replaces sharedMesh with
        // the combined mesh - measuring an instance at runtime reads half the city.
        //  - SM_Prop_Street_Lamp_01: single arm reaching +Z, head glass around z 1.3
        //  - SM_Prop_Street_Lamp_08: the short symmetric park post, bulb on axis
        // The emit height sits well below the lantern glass so the camera's tilt
        // cannot parallax a pool away from its own post.
        const float BulbHeight = 2.5f;

        static readonly (string name, Vector3 bulb)[] LampKinds =
        {
            ("SM_Prop_Street_Lamp_01", new Vector3(0f, BulbHeight, 1.3f)),
            ("SM_Prop_Street_Lamp_08", new Vector3(0f, BulbHeight, 0f)),
        };

        // the pack's own street-lamp amber, and a cone that paints a pool on the
        // pavement instead of spending itself sideways into the air
        static readonly Color LampColour = new Color(1f, 0.655f, 0.189f);
        const float SpotOuterAngle = 135f;
        const float SpotInnerAngle = 65f;
        const float Range = 10f;
        const float Intensity = 5f;

        const int LitLampBudget = 192;
        const float ResortInterval = 0.4f;

        readonly List<Light> _lamps = new List<Light>();
        float _nextResort;
        float _lit = -1f;

        void Start()
        {
            foreach (var transform in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                Vector3 bulb = default;
                bool match = false;
                foreach (var kind in LampKinds)
                {
                    if (!transform.name.StartsWith(kind.name, System.StringComparison.Ordinal))
                        continue;
                    // children of a lamp are not lamps - no double bulbs
                    if (transform.parent &&
                        transform.parent.name.StartsWith(kind.name, System.StringComparison.Ordinal))
                        break;
                    bulb = kind.bulb;
                    match = true;
                    break;
                }
                if (!match)
                    continue;

                var holder = new GameObject("lamp-light");
                holder.transform.SetParent(transform, false);
                holder.transform.localPosition = bulb;
                holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // straight down

                var light = holder.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = SpotOuterAngle;
                light.innerSpotAngle = SpotInnerAngle;
                light.color = LampColour;
                light.range = Range;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.renderMode = LightRenderMode.ForcePixel;
                holder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>()
                      .usePipelineSettings = true;

                _lamps.Add(light);
            }

            Debug.Log($"[RoadDemo] {_lamps.Count} street lamp bulbs wired.", this);
        }

        void LateUpdate()
        {
            if (_lamps.Count == 0)
                return;

            float night = DemoSky.Nightness(clock ? clock.Hour : 12f);
            float target = Intensity * night;

            if (Time.unscaledTime >= _nextResort)
            {
                _nextResort = Time.unscaledTime + ResortInterval;
                Resort(target);
            }
            else if (!Mathf.Approximately(target, _lit))
            {
                foreach (var lamp in _lamps)
                    if (lamp && lamp.enabled)
                        lamp.intensity = target;
            }

            _lit = target;
        }

        void Resort(float intensity)
        {
            var camera = Camera.main;

            // rank around where the camera LOOKS, not where it stands - the rig
            // parks it a couple hundred metres back along its boom
            if (camera && _lamps.Count > LitLampBudget)
            {
                var eye = camera.transform.position;
                var forward = camera.transform.forward;
                if (forward.y < -0.05f && eye.y > 0f)
                    eye += forward * (eye.y / -forward.y);

                _lamps.Sort((a, b) =>
                {
                    if (!a || !b)
                        return 0;
                    return (a.transform.position - eye).sqrMagnitude
                        .CompareTo((b.transform.position - eye).sqrMagnitude);
                });
            }

            for (int i = 0; i < _lamps.Count; i++)
            {
                var lamp = _lamps[i];
                if (!lamp)
                    continue;

                bool burn = i < LitLampBudget && intensity > 0.001f;
                lamp.enabled = burn;
                if (burn)
                    lamp.intensity = intensity;
            }
        }
    }
}
