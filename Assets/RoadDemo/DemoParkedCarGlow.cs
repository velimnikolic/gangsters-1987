using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Rare parked-car cabin and marker glow after dark. This deliberately creates no
    /// Light components: moving cars and street lamps already own the city's bounded
    /// realtime-light budget, while these tiny emissive surfaces only need to read as a
    /// driver waiting in a bay or somebody who has just parked.
    /// </summary>
    public sealed class DemoParkedCarGlow : MonoBehaviour
    {
        public LivingCity.Ambient.CityClock clock;

        internal const string MarkerRootName = "parked-marker-glow";

        const int Groups = 4;
        const float LitShare = 0.16f;
        const float VigilShare = 0.05f;
        const float FadeHours = 0.45f;
        const float CabinIntensity = 0.32f;
        const float MarkerIntensity = 0.75f;

        // Small-hours values: -2 is 22:00, 0.25 is 00:15. Most parked cars have
        // gone dark by eleven; the last ordinary group fades shortly after midnight.
        static readonly float[] OffHours = { -2f, -1.25f, -0.55f, 0.25f };
        static readonly Color[] CabinTints =
        {
            new Color(1f, 0.78f, 0.46f),
            new Color(1f, 0.86f, 0.62f),
            new Color(0.88f, 0.93f, 1f),
            new Color(1f, 0.70f, 0.38f),
        };
        static readonly Color MarkerTint = new Color(1f, 0.58f, 0.18f);

        static readonly int EnableEmission = Shader.PropertyToID("_Enable_Emission");
        static readonly int SyntyEmissionColor = Shader.PropertyToID("_Emission_Color");
        static readonly int SyntyEmissionMap = Shader.PropertyToID("_Emission_Map");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");

        sealed class Rig
        {
            public Transform Car;
            public DemoVehicle Vehicle;
            public int Group;
            public bool Vigil;
            public GameObject Markers;
        }

        readonly List<Rig> _rigs = new List<Rig>();
        readonly HashSet<Transform> _seen = new HashSet<Transform>();
        readonly HashSet<Transform> _candidates = new HashSet<Transform>();
        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Material> _materials = new List<Material>();
        readonly Dictionary<(Material, int), Material> _glass =
            new Dictionary<(Material, int), Material>();
        readonly Material[] _markers = new Material[Groups];
        readonly float[] _applied = { -1f, -1f, -1f, -1f };

        /// <summary>Every decorative car root found by the shared scene/stream scan,
        /// including the unlit majority. World fog uses the same classification.</summary>
        internal IEnumerable<Transform> VisualCars => _seen;

        void Start() => Register((Transform)null);

        /// <summary>Register a moving/parking vehicle before the scene scan. Its exact
        /// engine state then decides whether accents are allowed.</summary>
        public void Register(DemoVehicle vehicle)
        {
            if (vehicle == null || !vehicle.Tf) return;
            RegisterCar(vehicle.Tf, vehicle);
        }

        /// <summary>Find decorative parked vehicles under a newly visible streamed block,
        /// or across the active scene when root is null. Dynamic cars already explicitly
        /// registered above are skipped rather than mistaken for scenery.</summary>
        public void Register(Transform root)
        {
            _renderers.Clear();
            if (root != null) root.GetComponentsInChildren(true, _renderers);
            else _renderers.AddRange(FindObjectsByType<Renderer>(FindObjectsSortMode.None));

            _candidates.Clear();
            for (int i = 0; i < _renderers.Count; i++)
            {
                var car = VehicleRoot(_renderers[i].transform, root);
                if (car) _candidates.Add(car);
            }
            foreach (var car in _candidates) RegisterCar(car, null);
            _candidates.Clear();
            _renderers.Clear();
        }

        public void Unregister(Transform root)
        {
            if (!root) return;
            // The unlit majority is in _seen too. A pooled vehicle must get a
            // fresh position-based decision when another block borrows it.
            _seen.RemoveWhere(car => !car || car == root || car.IsChildOf(root));
            for (int i = _rigs.Count - 1; i >= 0; i--)
            {
                var car = _rigs[i].Car;
                if (!car || car == root || car.IsChildOf(root))
                {
                    ReleaseMarkers(_rigs[i]);
                    _rigs.RemoveAt(i);
                }
            }
        }

        static void ReleaseMarkers(Rig rig)
        {
            if (!rig.Markers) return;
            rig.Markers.SetActive(false);
            // Destroy is deferred in Play. Detach now, before the same pooled
            // body can be measured or registered again during this frame.
            rig.Markers.transform.SetParent(null, false);
            Retire(rig.Markers);
            rig.Markers = null;
        }

        static void Retire(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        void OnDestroy()
        {
            foreach (var rig in _rigs) ReleaseMarkers(rig);
            foreach (var material in _glass.Values) Retire(material);
            foreach (var material in _markers) Retire(material);
            _rigs.Clear();
            _seen.Clear();
        }

        void RegisterCar(Transform car, DemoVehicle vehicle)
        {
            // Add before the share roll: an unselected dynamic car must still be hidden
            // from the later whole-scene decorative scan.
            if (!car || !_seen.Add(car)) return;

            float roll = Hash01(car.position, 0x51ED270Bu);
            if (roll >= LitShare) return;

            int group = Mathf.Min(Groups - 1,
                Mathf.FloorToInt(Hash01(car.position, 0x9E3779B9u) * Groups));
            bool vigil = Hash01(car.position, 0x85EBCA6Bu) < VigilShare;
            var bounds = LocalBounds(car);

            WireGlass(car, group);
            var rig = new Rig
            {
                Car = car,
                Vehicle = vehicle,
                Group = group,
                Vigil = vigil,
                Markers = MakeMarkers(car, bounds, group),
            };
            rig.Markers.SetActive(false);
            _rigs.Add(rig);
        }

        void WireGlass(Transform car, int group)
        {
            _renderers.Clear();
            car.GetComponentsInChildren(true, _renderers);
            for (int r = 0; r < _renderers.Count; r++)
            {
                var renderer = _renderers[r];
                if (!Mentions(renderer.name, "Glass")) continue;

                _materials.Clear();
                renderer.GetSharedMaterials(_materials);
                bool changed = false;
                for (int i = 0; i < _materials.Count; i++)
                {
                    var original = _materials[i];
                    if (!original || !Mentions(original.name, "Glass")) continue;
                    if (!_glass.TryGetValue((original, group), out var clone))
                    {
                        clone = NightMaterial(original, $"parked cabin {group + 1}");
                        _glass[(original, group)] = clone;
                        // A block can stream in while full night is a flat 1. Without
                        // invalidating the group, its new material would remain at the
                        // black value it was created with until the closing fade begins.
                        _applied[group] = -1f;
                    }
                    _materials[i] = clone;
                    changed = true;
                }
                if (changed) renderer.SetSharedMaterials(_materials);
            }
            _materials.Clear();
            _renderers.Clear();
        }

        GameObject MakeMarkers(Transform car, Bounds bounds, int group)
        {
            var root = new GameObject(MarkerRootName);
            root.transform.SetParent(car, false);
            float x = Mathf.Max(0.36f, bounds.extents.x * 0.58f);
            float y = bounds.min.y + Mathf.Clamp(bounds.size.y * 0.40f, 0.38f, 0.72f);
            float z = bounds.max.z - Mathf.Clamp(bounds.size.z * 0.035f, 0.05f, 0.16f);
            StandMarker(root.transform, new Vector3(-x, y, z), group);
            StandMarker(root.transform, new Vector3(x, y, z), group);
            return root;
        }

        void StandMarker(Transform parent, Vector3 at, int group)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "marker bulb";
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = at;
            marker.transform.localScale = new Vector3(0.15f, 0.10f, 0.07f);
            var collider = marker.GetComponent<Collider>();
            Retire(collider);
            if (_markers[group] == null) _markers[group] = MarkerMaterial(group);
            marker.GetComponent<MeshRenderer>().sharedMaterial = _markers[group];
        }

        void LateUpdate()
        {
            float hour = clock ? clock.Hour : 12f;
            float night = DemoSky.Nightness(hour);
            for (int group = 0; group < Groups; group++)
            {
                float lit = night * GoesOut(hour, OffHours[group]);
                ApplyGroup(group, lit);
            }

            for (int i = _rigs.Count - 1; i >= 0; i--)
            {
                var rig = _rigs[i];
                if (!rig.Car || !rig.Markers) { _rigs.RemoveAt(i); continue; }
                float lit = rig.Vigil ? night * DawnVigil(hour) : night * GoesOut(hour, OffHours[rig.Group]);
                bool parked = rig.Vehicle == null || rig.Vehicle.EngineOff || rig.Vehicle.Parked;
                bool on = parked && lit > 0.002f;
                if (rig.Markers.activeSelf != on) rig.Markers.SetActive(on);
            }
        }

        void ApplyGroup(int group, float lit)
        {
            if (Mathf.Approximately(_applied[group], lit)) return;
            _applied[group] = lit;
            foreach (var pair in _glass)
                if (pair.Key.Item2 == group)
                    SetEmission(pair.Value, CabinTints[group] * (CabinIntensity * lit));
            if (_markers[group])
                SetEmission(_markers[group], MarkerTint * (MarkerIntensity * lit));
        }

        static float GoesOut(float hour, float offHour)
        {
            float small = hour >= 12f ? hour - 24f : hour;
            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(offHour, offHour + FadeHours, small));
        }

        // Roughly one car in a hundred keeps a dim cabin until the small-hours city
        // blackout, then fades too. At five this is the exception, never the street.
        static float DawnVigil(float hour)
        {
            float small = hour >= 12f ? hour - 24f : hour;
            return 0.45f * (1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(4f, 4.5f, small)));
        }

        static Material NightMaterial(Material original, string suffix)
        {
            var clone = new Material(original) { name = original.name + " (" + suffix + ")" };
            if (clone.HasProperty(EnableEmission)) clone.SetFloat(EnableEmission, 1f);
            if (clone.HasProperty(SyntyEmissionMap)) clone.SetTexture(SyntyEmissionMap, Texture2D.whiteTexture);
            if (clone.HasProperty(EmissionMap)) clone.SetTexture(EmissionMap, Texture2D.whiteTexture);
            clone.EnableKeyword("_EMISSION");
            clone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            SetEmission(clone, Color.black);
            return clone;
        }

        static Material MarkerMaterial(int group)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = $"Parked marker {group + 1}" };
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, MarkerTint * 0.28f);
            if (material.HasProperty(LegacyColor)) material.SetColor(LegacyColor, MarkerTint * 0.28f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            SetEmission(material, Color.black);
            return material;
        }

        static void SetEmission(Material material, Color colour)
        {
            if (!material) return;
            if (material.HasProperty(SyntyEmissionColor)) material.SetColor(SyntyEmissionColor, colour);
            if (material.HasProperty(EmissionColor)) material.SetColor(EmissionColor, colour);
        }

        static Transform VehicleRoot(Transform node, Transform boundary)
        {
            Transform found = null;
            for (var at = node; at && at != boundary; at = at.parent)
            {
                if (at.name.StartsWith("SM_Veh_", System.StringComparison.OrdinalIgnoreCase) ||
                    at.name.StartsWith("Parking Car", System.StringComparison.OrdinalIgnoreCase) ||
                    at.name.StartsWith("Patrol Car", System.StringComparison.OrdinalIgnoreCase))
                    found = at;
            }
            return found;
        }

        static Bounds LocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var b = renderers[i].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var world = new Vector3(
                        (corner & 1) == 0 ? b.min.x : b.max.x,
                        (corner & 2) == 0 ? b.min.y : b.max.y,
                        (corner & 4) == 0 ? b.min.z : b.max.z);
                    var local = root.InverseTransformPoint(world);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return any ? bounds : new Bounds(new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.4f, 4.6f));
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

        static bool Mentions(string value, string word) =>
            value.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
