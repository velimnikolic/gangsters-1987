using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Smoke from the works chimneys. Finds every SmokeVent left in the scene by generation and
    /// raises one plume per mouth.
    ///
    /// The particles are MESHES, not billboards, and that is a deliberate art call rather than a
    /// shortcut. The pack ships no smoke texture at all - the only particle prefabs in the
    /// project are the People pack's birds and confetti - so a soft plume would need a gradient
    /// texture authored from scratch, and the result would be the one blurred thing in a city
    /// built entirely from flat-shaded low poly. The pack's own cloud meshes, tumbling and
    /// fading, sit in the same visual language as everything around them.
    ///
    /// Two URP notes worth keeping: Unity's Default-ParticleSystem material is a Built-in shader
    /// and renders magenta here, so the material is authored by CityAssetBootstrap on
    /// Universal Render Pipeline/Particles/Unlit; and the renderer's shadow casting is off,
    /// because a mesh particle will otherwise throw a hard low-poly shadow across the yard.
    /// </summary>
    public sealed class SmokeStackSystem : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Header("Plume")]
        [Tooltip("Puffs per second from one chimney. Low on purpose - a works chimney smokes, " +
                 "it does not erupt, and each puff is a whole mesh.")]
        [SerializeField, Min(0f)] float emissionRate = 1.5f;

        [Tooltip("Seconds a puff lives. With the rate above this is what sets how many are alive " +
                 "at once: 1.5 x 10 is about fifteen per chimney.")]
        [SerializeField, Min(1f)] float lifetime = 10f;

        [Tooltip("Puff size at the chimney mouth and at the end of its life. It grows because " +
                 "smoke does, and because a plume of constant-size puffs reads as a bead string.")]
        [SerializeField] Vector2 sizeRange = new(3f, 11f);

        [SerializeField, Min(0f)] float riseSpeed = 2f;

        [Tooltip("Metres per second of sideways drift. ONE direction for the whole city, drawn " +
                 "from the seed - every chimney leaning the same way is what wind looks like, " +
                 "and per-chimney directions are the clearest possible tell that this was " +
                 "generated.")]
        [SerializeField, Min(0f)] float windSpeed = 1.2f;

        [Tooltip("Ceiling on live plumes, whatever the city holds. A works block can carry two " +
                 "or three chimneys and a big map several works.")]
        [SerializeField, Min(0)] int maxStacks = 24;

        void Start()
        {
            if (!config || !prefabs)
            {
                enabled = false;
                return;
            }

            if (!config.industrialSmoke || !prefabs.smokeMaterial)
            {
                enabled = false;
                return;
            }

            var vents = FindObjectsByType<SmokeVent>(FindObjectsSortMode.None);
            if (vents.Length == 0)
            {
                enabled = false;
                return;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Ambient);

            var heading = (float)rng.NextDouble() * Mathf.PI * 2f;
            var wind = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * windSpeed;

            // Sorted before the cap bites, so which chimneys smoke does not depend on the order
            // Unity happened to return them in - that order is not stable across loads, and an
            // unstable choice means the same seed smokes from different stacks each run.
            System.Array.Sort(vents, (a, b) =>
            {
                var pa = a.transform.position;
                var pb = b.transform.position;
                var byX = pa.x.CompareTo(pb.x);
                return byX != 0 ? byX : pa.z.CompareTo(pb.z);
            });

            var mouths = new List<Vector3>();
            var raised = 0;

            foreach (var vent in vents)
            {
                vent.MouthsWorld(mouths);

                foreach (var mouth in mouths)
                {
                    if (raised >= maxStacks)
                    {
                        Debug.Log($"[SmokeStackSystem] {maxStacks} plumes raised, capped. " +
                                  "Raise maxStacks if the map has grown.");
                        return;
                    }

                    Raise(mouth, wind);
                    raised++;
                }
            }
        }

        /// <summary>One chimney's plume.</summary>
        void Raise(Vector3 mouth, Vector3 wind)
        {
            var host = new GameObject("smoke_stack");
            host.transform.SetParent(transform, false);
            host.transform.position = mouth;

            var system = host.AddComponent<ParticleSystem>();

            // Every module has to be assigned back - ParticleSystem's modules are structs, and
            // mutating the property in place is a no-op that compiles cleanly.
            var main = system.main;
            main.startLifetime = lifetime;
            main.startSpeed = riseSpeed;
            main.startSize = sizeRange.x;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.72f, 0.70f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(emissionRate * lifetime) + 4;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.rateOverTime = emissionRate;

            // A cone rather than a point, so consecutive puffs do not stack into a column.
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.Linear(0f, 1f, 1f, sizeRange.y / Mathf.Max(0.01f, sizeRange.x)));

            // Fades out rather than vanishing. Without this the plume ends in a hard edge at the
            // lifetime, which is far more obvious than the smoke itself.
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = prefabs.smokeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (prefabs.smokePuffMesh)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = prefabs.smokePuffMesh;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }
    }
}
