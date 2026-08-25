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

        //  - SM_Prop_Pier_Lamp_01: the harbour's own post, 4.2 m tall with its head
        //    reaching about half a metre to +Z. Only the port plants it, and until it
        //    was named here the whole quay stood dark at night while the street behind
        //    the wire burned - which read as a port that had been abandoned rather than
        //    as one that works two shifts.
        static readonly (string name, Vector3 bulb)[] LampKinds =
        {
            ("SM_Prop_Street_Lamp_01", new Vector3(0f, BulbHeight, 1.3f)),
            ("SM_Prop_Street_Lamp_08", new Vector3(0f, BulbHeight, 0f)),
            ("SM_Prop_Pier_Lamp_01", new Vector3(0f, 3.4f, 0.45f)),
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
        // the lamps never move: their positions are read once, and each resort
        // ranks an index table by plain arithmetic - no transform reads, no closure
        Vector3[] _at;
        float[] _key;
        int[] _order;
        bool[] _burning;
        float _nextResort;
        float _lit = -1f;

        void Start()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
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

            _at = new Vector3[_lamps.Count];
            _key = new float[_lamps.Count];
            _order = new int[_lamps.Count];
            _burning = new bool[_lamps.Count];
            for (int i = 0; i < _lamps.Count; i++)
            {
                _at[i] = _lamps[i].transform.position;
                _order[i] = i;
            }

            Debug.Log($"[RoadDemo] {_lamps.Count} street lamp bulbs wired.", this);
            clock.Stop();
            // whole-scene sweep at Start: this walks EVERY transform in the city
            // (~376,000 of them) to find lamp roots. Timed because the first frames
            // of Play cost tens of seconds and Start-phase work is invisible to the
            // frame probe - it samples Update, not ScriptRunDelayedStartupFrame.
            Debug.Log($"[DemoStreetLamps] Start took {clock.ElapsedMilliseconds} ms");
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
                for (int i = 0; i < _lamps.Count; i++)
                    if (_burning[i] && _lamps[i])
                        _lamps[i].intensity = target;
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

                for (int i = 0; i < _at.Length; i++)
                    _key[i] = (_at[i] - eye).sqrMagnitude;
                // the nearest LitLampBudget to the front; a full sort of the rest is
                // not needed and not done
                Nearest(_key, _order, LitLampBudget);
            }

            for (int rank = 0; rank < _order.Length; rank++)
            {
                int i = _order[rank];
                var lamp = _lamps[i];
                if (!lamp)
                    continue;

                bool burn = rank < LitLampBudget && intensity > 0.001f;
                // a light re-registers with the renderer on every enable/disable, so
                // only the ones that actually change state are touched
                if (burn != _burning[i])
                {
                    lamp.enabled = burn;
                    _burning[i] = burn;
                }
                if (burn)
                    lamp.intensity = intensity;
            }
        }

        /// <summary>Size the pair of arrays a ranking runs on and fill order with
        /// 0..count. BOTH are sized here, together, on purpose: a caller that sizes one
        /// off the other's length lets the two drift apart the moment it takes a branch
        /// that touches only one of them, and the next fill walks off the end of the
        /// short one. Every ranking in the demo goes through this.</summary>
        internal static void Prepare(ref float[] key, ref int[] order, int count)
        {
            if (key.Length != count) key = new float[count];
            if (order.Length != count) order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
        }

        /// <summary>Partial selection: after it, order[0..count) are the count smallest
        /// keys (in no particular order among themselves) and the rest follow. Quickselect,
        /// in place, no allocation.</summary>
        internal static void Nearest(float[] key, int[] order, int count)
        {
            int lo = 0, hi = order.Length - 1;
            if (count >= order.Length) return;
            while (lo < hi)
            {
                float pivot = key[order[(lo + hi) >> 1]];
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (key[order[i]] < pivot) i++;
                    while (key[order[j]] > pivot) j--;
                    if (i <= j) { (order[i], order[j]) = (order[j], order[i]); i++; j--; }
                }
                if (count <= j) hi = j;
                else if (count >= i) lo = i;
                else break;
            }
        }
    }
}
