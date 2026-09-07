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
        public LivingCity.Ambient.CityClock clock;

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
            // CorePavement uses PolygonCity's 6.5 m mast rather than PalmCity's
            // shorter post. Its arm points down local +Z, over the carriageway.
            ("SM_Prop_LightPole_Base_01", new Vector3(0f, 6.05f, 2.15f)),
        };

        // Match the cars' period-tungsten beams exactly: the bulb and every nearby
        // headlight now speak one night-light colour instead of amber versus yellow.
        static readonly Color LampColour = DemoHeadlights.BeamColour;
        const float SpotOuterAngle = 152f;
        const float SpotInnerAngle = 86f;
        const float Range = 14f;
        const float Intensity = 8f;

        // Still scarce, but visible across Core: around six dead and six intermittent
        // bulbs at the current ~470-lamp count. Stable by position so a lamp does not
        // become healthy merely because its block was streamed out.
        const float DeadShare = 0.012f;
        const float FlickerShare = 0.012f;
        enum Fault : byte { None, Dead, Flicker }

        const int LitLampBudget = 192;
        const float ResortInterval = 0.4f;

        readonly List<Light> _lamps = new List<Light>();
        readonly Dictionary<Transform, Light> _wired = new Dictionary<Transform, Light>();
        readonly List<Transform> _transformScratch = new List<Transform>();
        // the lamps never move: their positions are read once, and each resort
        // ranks an index table by plain arithmetic - no transform reads, no closure
        Vector3[] _at = System.Array.Empty<Vector3>();
        float[] _key = System.Array.Empty<float>();
        int[] _order = System.Array.Empty<int>();
        bool[] _burning = System.Array.Empty<bool>();
        Fault[] _fault = System.Array.Empty<Fault>();
        float[] _flickerPhase = System.Array.Empty<float>();
        float _nextResort;
        float _lit = -1f;

        void Start()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Register(null);

            Debug.Log($"[RoadDemo] {_lamps.Count} street lamp bulbs wired.", this);
            int dead = 0, flicker = 0;
            for (int i = 0; i < _fault.Length; i++)
                if (_fault[i] == Fault.Dead) dead++;
                else if (_fault[i] == Fault.Flicker) flicker++;
            Debug.Log($"[RoadDemo] Street lamp wear: {dead} dead, {flicker} intermittent.", this);
            clock.Stop();
            // whole-scene sweep at Start: this walks EVERY transform in the city
            // (~376,000 of them) to find lamp roots. Timed because the first frames
            // of Play cost tens of seconds and Start-phase work is invisible to the
            // frame probe - it samples Update, not ScriptRunDelayedStartupFrame.
            Debug.Log($"[DemoStreetLamps] Start took {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>Wire all lamp prefabs under one newly materialised block. Null keeps
        /// the original whole-scene Start pass. Idempotent across either call order.</summary>
        public void Register(Transform root)
        {
            IList<Transform> transforms;
            if (root != null)
            {
                _transformScratch.Clear();
                root.GetComponentsInChildren(true, _transformScratch);
                transforms = _transformScratch;
            }
            else transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            bool added = false;
            for (int t = 0; t < transforms.Count; t++)
            {
                var transform = transforms[t];
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
                if (!match || (_wired.TryGetValue(transform, out var existing) && existing)) continue;

                // A cached block unregisters its bulbs without destroying its hierarchy.
                // Rebinding that same payload must reuse the old bulb, not hang another
                // lamp-light child under the post every time it crosses the cache edge.
                var holder = transform.Find("lamp-light")?.gameObject;
                var light = holder != null ? holder.GetComponent<Light>() : null;
                if (light == null)
                {
                    if (holder == null) holder = new GameObject("lamp-light");
                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = bulb;
                    holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // straight down

                    light = holder.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = SpotOuterAngle;
                    light.innerSpotAngle = SpotInnerAngle;
                    light.color = LampColour;
                    light.range = Range;
                    light.intensity = 0f;
                    light.shadows = LightShadows.None;
                    light.enabled = false;
#if UNITY_EDITOR
                    light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                    light.renderMode = LightRenderMode.ForcePixel;
                    if (!holder.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(out var data))
                        data = holder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                    data.usePipelineSettings = true;
                }

                _wired[transform] = light;
                _lamps.Add(light);
                added = true;
            }
            if (root != null) _transformScratch.Clear();
            if (added) Reindex();
        }

        /// <summary>Drop identities belonging to an evicted ViewHolder before Unity destroys it.</summary>
        public void Unregister(Transform root)
        {
            if (root == null || _wired.Count == 0) return;
            bool removed = false;
            _transformScratch.Clear();
            root.GetComponentsInChildren(true, _transformScratch);
            for (int i = 0; i < _transformScratch.Count; i++)
            {
                var transform = _transformScratch[i];
                if (!_wired.Remove(transform, out var light)) continue;
                // Keep the registered identity even if its component/child was destroyed.
                // A cached hierarchy may be enabled before it is registered again.
                if (light) light.enabled = false;
                _lamps.Remove(light);
                removed = true;
            }
            _transformScratch.Clear();
            if (removed) Reindex();
        }

        void Reindex()
        {
            PruneDestroyedLamps();
            _at = new Vector3[_lamps.Count];
            _key = new float[_lamps.Count];
            _order = new int[_lamps.Count];
            _burning = new bool[_lamps.Count];
            _fault = new Fault[_lamps.Count];
            _flickerPhase = new float[_lamps.Count];
            for (int i = 0; i < _lamps.Count; i++)
            {
                _at[i] = _lamps[i].transform.position;
                _order[i] = i;
                _burning[i] = _lamps[i].enabled;
                float wear = Hash01(_at[i], 0x6D2B79F5u);
                _fault[i] = wear < DeadShare ? Fault.Dead
                    : wear < DeadShare + FlickerShare ? Fault.Flicker
                    : Fault.None;
                _flickerPhase[i] = Hash01(_at[i], 0x9E3779B9u) * 19f;
            }
            _nextResort = 0f;
        }

        bool PruneDestroyedLamps()
        {
            // Streaming and dressing can retire a post before Unregister sees it.
            // Compact before sizing the parallel arrays or reading any Light property.
            bool removed = _lamps.RemoveAll(lamp => !lamp) > 0;
            _transformScratch.Clear();
            foreach (var pair in _wired)
                if (!pair.Key || !pair.Value) _transformScratch.Add(pair.Key);
            for (int i = 0; i < _transformScratch.Count; i++)
                _wired.Remove(_transformScratch[i]);
            _transformScratch.Clear();
            return removed;
        }

        void LateUpdate()
        {
            if (_lamps.Count == 0)
                return;

            float night = DemoSky.Nightness(clock ? clock.Hour : 12f);
            float target = Intensity * night;

            if (Time.unscaledTime >= _nextResort)
            {
                // Dead entries must not consume the nearest-light budget when no new
                // block arrives to trigger registration and a fresh index.
                if (PruneDestroyedLamps()) Reindex();
                _nextResort = Time.unscaledTime + ResortInterval;
                Resort(target);
            }
            else if (!Mathf.Approximately(target, _lit))
            {
                for (int i = 0; i < _lamps.Count; i++)
                    if (_burning[i] && _lamps[i])
                        _lamps[i].intensity = LampIntensity(i, target);
            }
            else
                ApplyFlicker(target);

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

                bool burn = rank < LitLampBudget && intensity > 0.001f &&
                            _fault[i] != Fault.Dead;
                // a light re-registers with the renderer on every enable/disable, so
                // only the ones that actually change state are touched
                if (burn != _burning[i])
                {
                    lamp.enabled = burn;
                    _burning[i] = burn;
                }
                if (burn)
                    lamp.intensity = LampIntensity(i, intensity);
            }
        }

        void ApplyFlicker(float intensity)
        {
            for (int i = 0; i < _lamps.Count; i++)
                if (_burning[i] && _fault[i] == Fault.Flicker && _lamps[i])
                    _lamps[i].intensity = LampIntensity(i, intensity);
        }

        float LampIntensity(int index, float steady)
        {
            if (_fault[index] != Fault.Flicker) return steady;

            // A short, irregular double miss every eight to twelve seconds. It is
            // never a regular sine pulse: a broken starter coughs, then settles.
            float phase = _flickerPhase[index];
            float period = 8.2f + Mathf.Repeat(phase * 0.37f, 3.8f);
            float t = Mathf.Repeat(Time.unscaledTime + phase, period);
            if (t < 0.055f) return steady * 0.06f;
            if (t < 0.115f) return steady;
            if (t < 0.19f) return steady * 0.22f;
            if (t < 0.29f) return steady * 0.78f;
            return steady;
        }

        static float Hash01(Vector3 position, uint salt)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(position.x * 10f) * 73856093
                              ^ Mathf.RoundToInt(position.y * 10f) * 83492791
                              ^ Mathf.RoundToInt(position.z * 10f) * 19349663) ^ salt;
                h ^= h >> 13;
                h *= 2654435761u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
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
