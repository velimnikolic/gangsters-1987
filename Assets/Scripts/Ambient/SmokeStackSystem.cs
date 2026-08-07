using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Smoke from the city's chimneys. Finds every SmokeVent left in the scene by generation and
    /// raises a plume per mouth, from the profile matching the vent's kind.
    ///
    /// Two families, because there are two kinds of chimney and they share no numbers: the works
    /// stacks, which all smoke, and the terrace flues, of which only some do. The thinning is the
    /// point of the house profile rather than a performance measure - the pack reuses one authored
    /// chimney across all eight building-block pieces, so a city where every one of them smokes is
    /// both a smog bank and an obvious tell that the place was generated.
    ///
    /// A vent's kind comes from the spawn path that stamped it: IndustrialDresser marks Works, and
    /// BlockBuilder.Place marks House. That is a property of the spawner rather than of the prefab,
    /// which would misclassify a works stack if one were ever scattered onto an ordinary block. It
    /// cannot happen today - chimney-big and water-tower-medium live only in palette.chimneyProps,
    /// which only IndustrialDresser reads - but if the vent table ever grows a kind column, that is
    /// the reason to move the decision into it.
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

        /// <summary>
        /// Everything that makes one family of chimney smoke the way it does. A works stack and a
        /// terrace flue share every mechanism here and agree on none of the numbers, so the numbers
        /// are grouped and the mechanism is written once.
        /// </summary>
        [System.Serializable]
        struct Profile
        {
            [Tooltip("Puff width in METRES at the mouth and at the end of its life. It grows " +
                     "because smoke does, and because a plume of constant-size puffs reads as a " +
                     "bead string. For reference a city cell is 30 m.")]
            public Vector2 sizeRange;

            [Tooltip("Puffs per second from one chimney. Low on purpose - a chimney smokes, it " +
                     "does not erupt, and each puff is a whole mesh.")]
            [Min(0f)] public float emissionRate;

            [Tooltip("Seconds a puff lives. With the rate this is what sets how many are alive " +
                     "at once: 1.5 x 10 is about fifteen per chimney.")]
            [Min(1f)] public float lifetime;

            [Min(0f)] public float riseSpeed;

            [Tooltip("Peak opacity of one puff. Deliberately far from solid, because the puffs " +
                     "overlap along the rise and anything near opaque stacks into a wall.")]
            [Range(0f, 1f)] public float alpha;

            [Tooltip("Chance one chimney of this kind smokes at all. Below 1 this is what makes " +
                     "a skyline read as a city rather than as a diagram of every chimney in it.")]
            [Range(0f, 1f)] public float chance;

            [Tooltip("Ceiling on live plumes of this kind, whatever the city holds.")]
            [Min(0)] public int maxStacks;
        }

        [Header("Works chimneys")]
        [SerializeField] Profile works = new()
        {
            sizeRange = new Vector2(3f, 8f),
            emissionRate = 1.5f,
            lifetime = 10f,
            riseSpeed = 2f,
            alpha = 0.40f,
            // Every works stack smokes. There are only a handful on a map and a dead factory
            // chimney is the one thing here that would read as broken rather than as quiet.
            chance = 1f,
            maxStacks = 24,
        };

        [Header("House chimneys")]
        [Tooltip("A terrace flue, not a works stack. Smaller, slower, thinner, and only on some " +
                 "of the houses - see the field tooltips.")]
        [SerializeField] Profile house = new()
        {
            // The authored terrace chimney measures 1.20 m in radius, so 1.2 m at the mouth is
            // roughly the flue itself. 3.5 m at the top is a wisp that clears the roof ridge and
            // stops well inside the building's own footprint.
            sizeRange = new Vector2(1.2f, 3.5f),
            // A third of the works rate. A domestic fire is banked, not stoked, and with a
            // lifetime of 8 that is about five puffs in the air at once.
            emissionRate = 0.6f,
            lifetime = 8f,
            // Slower than the works stack: 1.1 x 8 is about 9 m of rise, so the plume stays a
            // roof-height detail instead of climbing into the skyline the factories own.
            riseSpeed = 1.1f,
            // Around half the works opacity. There are far more of these and they sit far lower,
            // so they are what a camera actually looks through.
            alpha = 0.22f,
            // "Po neki dim" - a few chimneys, not every chimney. At 1 every terrace on every
            // block smokes at once, which is both a smog bank and an obvious tell that this was
            // generated; the pack's terrace kit puts the identical chimney on all nine pieces.
            chance = 0.28f,
            // Generous next to the works cap because there are two orders of magnitude more
            // houses. It is a backstop against a huge map, not the thing doing the thinning -
            // chance is. See Raise's caller for why the cap is applied as a probability.
            maxStacks = 45,
        };

        [Header("Weather")]
        [Tooltip("Metres per second of sideways drift. ONE direction for the whole city, drawn " +
                 "from the seed - every chimney leaning the same way is what wind looks like, " +
                 "and per-chimney directions are the clearest possible tell that this was " +
                 "generated.")]
        [SerializeField, Min(0f)] float windSpeed = 1.2f;

        /// <summary>
        /// Metres of authored mesh that one unit of particle size buys, so sizeRange can be read
        /// in metres. A mesh particle SCALES its mesh - size 1 draws the mesh at authored size -
        /// so feeding metres straight to startSize multiplies every value in this inspector by
        /// the mesh's own width. The pack's cloud-fluffy is 6.22 x 4.57 x 4.97 m, which is how
        /// an intended 11 m puff was drawing at 68 m and swallowing the works whole.
        /// Stays 1 for billboards, where particle size already is metres.
        /// </summary>
        float puffMetresPerUnit = 1f;

        void Start()
        {
            if (!config || !prefabs)
            {
                enabled = false;
                return;
            }

            if (!config.chimneySmoke || !prefabs.smokeMaterial)
            {
                enabled = false;
                return;
            }

            // Max of x and z rather than x alone, so swapping the puff for another pack cloud
            // still reads in metres - cloud-long is 12.84 m wide against cloud-fluffy's 6.22.
            // Mesh.bounds is free here, it does not touch the vertex buffer.
            if (prefabs.smokePuffMesh)
            {
                var footprint = prefabs.smokePuffMesh.bounds.size;
                puffMetresPerUnit = Mathf.Max(0.01f, Mathf.Max(footprint.x, footprint.z));
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

            RaiseAll(vents, VentKind.Works, works, wind, rng);
            RaiseAll(vents, VentKind.House, house, wind, rng);
        }

        /// <summary>
        /// Every chimney of one kind, thinned to the profile's chance and cap.
        ///
        /// The cap is folded INTO the probability rather than applied as a running count, and that
        /// is the whole reason this counts the mouths first. The vents are sorted by X, so cutting
        /// off at the cap smokes a western prefix of the map and leaves the east cold - invisible
        /// with a handful of works stacks, glaring with a hundred terraces against a cap of 45.
        /// Expressed as a probability the same ceiling lands evenly across the whole city.
        /// </summary>
        void RaiseAll(
            SmokeVent[] vents, VentKind kind, Profile profile, Vector3 wind, System.Random rng)
        {
            if (profile.chance <= 0f || profile.maxStacks <= 0)
                return;

            var total = 0;
            foreach (var vent in vents)
                if (vent.kind == kind)
                    total += vent.mouths?.Length ?? 0;

            if (total == 0)
                return;

            var mouths = new List<Vector3>();

            var chance = Mathf.Min(profile.chance, profile.maxStacks / (float)total);
            var raised = 0;

            foreach (var vent in vents)
            {
                if (vent.kind != kind)
                    continue;

                vent.MouthsWorld(mouths);

                foreach (var mouth in mouths)
                {
                    // Drawn for every mouth whether or not it smokes, so the sequence does not
                    // depend on how many passed - otherwise one extra chimney anywhere reshuffles
                    // every decision after it and the seed stops meaning anything.
                    if (rng.NextDouble() >= chance)
                        continue;

                    Raise(mouth, wind, profile);
                    raised++;
                }
            }

            Debug.Log($"[SmokeStackSystem] {kind}: {raised} of {total} chimneys smoking " +
                      $"(chance {chance:F2}" +
                      (chance < profile.chance ? $", thinned from {profile.chance:F2} by the " +
                                                 $"cap of {profile.maxStacks}" : string.Empty) +
                      ").");
        }

        /// <summary>One chimney's plume.</summary>
        void Raise(Vector3 mouth, Vector3 wind, Profile profile)
        {
            var host = new GameObject("smoke_stack");
            host.transform.SetParent(transform, false);
            host.transform.position = mouth;

            var system = host.AddComponent<ParticleSystem>();

            // Every module has to be assigned back - ParticleSystem's modules are structs, and
            // mutating the property in place is a no-op that compiles cleanly.
            var main = system.main;
            main.startLifetime = profile.lifetime;
            main.startSpeed = profile.riseSpeed;
            main.startSize = profile.sizeRange.x / puffMetresPerUnit;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.72f, 0.70f, profile.alpha);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(profile.emissionRate * profile.lifetime) + 4;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.rateOverTime = profile.emissionRate;

            // A cone rather than a point, so consecutive puffs do not stack into a column. The
            // radius scales with the puff, or a house flue would spread its 1.2 m puffs over the
            // 0.6 m mouth of a works stack and read as a fire rather than a chimney.
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.6f * profile.sizeRange.x / Mathf.Max(0.01f, works.sizeRange.x);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.Linear(
                    0f, 1f, 1f,
                    profile.sizeRange.y / Mathf.Max(0.01f, profile.sizeRange.x)));

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
