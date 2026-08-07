using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Lights the windows after dark, and puts them out at dawn.
    ///
    /// The pack did the hard part already. Its atlas material ships with an emission MAP -
    /// "atlas-emission-night-LPEC" - in which the artist painted exactly which pixels of the
    /// texture are lit windows and signage. What it does not ship is any reason for that map to
    /// come and go: `_EmissionColor` sits at a flat 0.154 grey, so the windows glow faintly at
    /// noon and no brighter at midnight. All this does is drive that one colour off the clock.
    ///
    /// The first version of this hunted for a shared glass material, which exists - but only in
    /// the pack's M prefabs, the variant that carries one material per colour. This project
    /// builds from the T variant, where colour is baked into UVs and every building draws from a
    /// single atlas. The glass material was therefore real, shared by 167 prefabs, and used by
    /// nothing in this scene. Hence the search below by emission map rather than by name: it
    /// finds the atlas and every tint variant BuildingTinter has generated from it, and it keeps
    /// working when someone adds another.
    ///
    /// This is not real illumination - a window casts nothing onto the pavement opposite. Street
    /// lamps do that, see StreetLampLights. What this gives is a city that reads as inhabited
    /// after dark instead of as a silhouette.
    /// </summary>
    public sealed class NightWindows : MonoBehaviour
    {
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        [Tooltip("Reads the hour. Left empty, the first clock in the scene is used.")]
        [SerializeField] CityClock clock;

        [Header("Look")]
        [Tooltip("Colour of a lit window. Warm white - the emission map already decides WHICH " +
                 "pixels light up, so this only tints and scales them.")]
        [SerializeField] Color litColour = new(1f, 0.82f, 0.55f);

        [Tooltip("How hard the windows burn at full night. Above 1 pushes them into the bloom.")]
        [SerializeField, Min(0f)] float litIntensity = 2f;

        /// <summary>
        /// Runtime copies of every emissive material in the scene, one per original.
        ///
        /// Never the assets themselves: writing emission onto a shared material edits the file on
        /// disk the moment this runs in the editor, and leaves every window in the project glowing
        /// long after the game stopped.
        /// </summary>
        readonly List<Material> instances = new();

        float applied = -1f;

        void Start()
        {
            if (!clock)
                clock = FindAnyObjectByType<CityClock>();

            AdoptEmissiveMaterials();

            if (instances.Count == 0)
            {
                Debug.LogWarning("[NightWindows] No emissive materials in the scene - windows " +
                                 "will stay as the pack left them. Generate the city first.", this);
                enabled = false;
                return;
            }

            Debug.Log($"[NightWindows] {instances.Count} emissive materials taken over.", this);
        }

        /// <summary>
        /// Finds every material in the scene that carries an emission map, clones each once, and
        /// swaps the clones in wherever the original was used.
        ///
        /// One pass over every renderer, which is not cheap - but it is one pass at load against
        /// a city that does not change shape afterwards. Each original maps to exactly one clone,
        /// so renderers that shared a material still share one and the static batching the
        /// generator set up survives.
        /// </summary>
        void AdoptEmissiveMaterials()
        {
            var clones = new Dictionary<Material, Material>();

            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var materials = renderer.sharedMaterials;
                var touched = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    var original = materials[i];
                    if (!original || !IsEmissive(original))
                        continue;

                    if (!clones.TryGetValue(original, out var clone))
                    {
                        clone = new Material(original) { name = original.name + " (night)" };
                        clone.EnableKeyword("_EMISSION");
                        clone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                        clones[original] = clone;
                        instances.Add(clone);
                    }

                    materials[i] = clone;
                    touched = true;
                }

                if (touched)
                    renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// Identified by carrying an emission map, not by name.
        ///
        /// The atlas and every BuildingTints variant of it point at the same night emission
        /// texture, and there is no name they all share. Testing the map catches the lot, and
        /// catches the next tint someone generates without this needing to be told about it.
        /// </summary>
        static bool IsEmissive(Material material) =>
            material.HasProperty(EmissionMap) && material.GetTexture(EmissionMap);

        void LateUpdate()
        {
            var night = CityWeather.Nightness(clock ? clock.Hour : 12f);

            // A colour write per material per frame is cheap, but it also dirties them and there
            // is no reason to do it while the sun is high and nothing is changing.
            if (Mathf.Approximately(night, applied))
                return;

            applied = night;

            var emission = litColour * litIntensity * night;

            foreach (var material in instances)
                if (material)
                    material.SetColor(EmissionColor, emission);
        }

        void OnDestroy()
        {
            foreach (var material in instances)
                if (material)
                    Destroy(material);

            instances.Clear();
        }
    }
}
