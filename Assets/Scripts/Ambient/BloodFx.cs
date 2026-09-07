using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Shared bullet-hit blood treatment. One hit is three different physical scales:
    /// a very short impact sheet, ballistic droplets travelling out of the wound, and
    /// a fine mist that expands and disappears. A small ring reuses the particle systems
    /// so automatic fire cannot turn combat into Instantiate/Destroy and GC spikes.
    /// </summary>
    public static class BloodFx
    {
        public const string SplatterPath = "Assets/Vfx/Blood/RealisticBloodSplatter.png";
        public const string MistPath = "Assets/Vfx/Blood/RealisticBloodMist.png";

        const int MaxLiveImpacts = 32;
        static readonly List<Impact> impacts = new List<Impact>(MaxLiveImpacts);
        static Transform root;
        static Material splatterMaterial, mistMaterial, dropletMaterial;
        static Texture2D splatterTexture, mistTexture;
        static int nextImpact;

        public static Texture2D SplatterTexture
        {
            get
            {
                if (splatterTexture == null)
                    splatterTexture = Load<Texture2D>(SplatterPath);
                return splatterTexture;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            root = null;
            splatterMaterial = mistMaterial = dropletMaterial = null;
            splatterTexture = mistTexture = null;
            impacts.Clear();
            nextImpact = 0;
        }

        /// <summary>Play a pooled blood response at the wound. Direction points from
        /// shooter through the victim, so droplets continue along the resolved round.</summary>
        public static void PlayHit(Vector3 at, Vector3 direction, bool fatal = false)
        {
            if (!EnsureMaterials()) return;
            if (direction.sqrMagnitude < 1e-5f) direction = Vector3.forward;
            direction.Normalize();

            if (root == null) root = new GameObject("Blood Impact FX").transform;
            Impact impact;
            if (impacts.Count < MaxLiveImpacts)
            {
                impact = new Impact(root, impacts.Count + 1, splatterMaterial,
                    mistMaterial, dropletMaterial);
                impacts.Add(impact);
            }
            else
            {
                impact = impacts[nextImpact];
                nextImpact = (nextImpact + 1) % MaxLiveImpacts;
            }
            impact.Play(at, direction, fatal ? 1.25f : 1f);
        }

        static bool EnsureMaterials()
        {
            if (splatterMaterial != null && mistMaterial != null && dropletMaterial != null)
                return true;

            var splatter = SplatterTexture;
            if (mistTexture == null) mistTexture = Load<Texture2D>(MistPath);
            if (splatter == null || mistTexture == null) return false;

            splatterMaterial = ParticleMaterial("Blood Splatter", splatter);
            mistMaterial = ParticleMaterial("Blood Mist", mistTexture);
            var dot = RoadDemo.DemoUi.Dot;
            dropletMaterial = ParticleMaterial("Blood Droplets",
                dot != null ? dot.texture : splatter);
            return splatterMaterial != null && mistMaterial != null && dropletMaterial != null;
        }

        static Material ParticleMaterial(string name, Texture texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Particles/Standard Unlit") ??
                         Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = name,
                mainTexture = texture,
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Transparent,
            };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        static T Load<T>(string path) where T : Object
        {
            return RoadDemo.DemoAssetLoad.Load<T>(path);
        }

        sealed class Impact
        {
            readonly ParticleSystem sheet, droplets, mist;

            public Impact(Transform parent, int number, Material splatter,
                Material cloud, Material drops)
            {
                var holder = new GameObject("Blood Hit " + number).transform;
                holder.SetParent(parent, false);
                sheet = MakeSystem(holder, "Impact Splatter", splatter,
                    ParticleSystemRenderMode.Billboard, 2);
                droplets = MakeSystem(holder, "Directional Droplets", drops,
                    ParticleSystemRenderMode.Stretch, 24);
                mist = MakeSystem(holder, "Blood Cloud", cloud,
                    ParticleSystemRenderMode.Billboard, 6);

                var dropMain = droplets.main;
                dropMain.gravityModifier = 1.15f;
                var dropRenderer = droplets.GetComponent<ParticleSystemRenderer>();
                dropRenderer.lengthScale = 2.4f;
                dropRenderer.velocityScale = 0.09f;

                ExpandAndFade(sheet, 0.7f, 1.12f);
                // Mist blooms around the wound. It must not inherit the bullet's
                // ballistic travel or the cloud reads as a red jet/geyser.
                ExpandAndFade(mist, 0.68f, 1.38f);
                Fade(droplets);
            }

            public void Play(Vector3 at, Vector3 direction, float amount)
            {
                sheet.Clear(true);
                droplets.Clear(true);
                mist.Clear(true);

                var side = Vector3.Cross(direction, Vector3.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                side.Normalize();
                var up = Vector3.Cross(side, direction).normalized;

                Emit(sheet, at + direction * 0.04f, direction * 0.45f,
                    Random.Range(0.11f, 0.17f), Random.Range(0.48f, 0.68f) * amount,
                    new Color(0.9f, 0.78f, 0.75f, 0.78f));

                int dropCount = Mathf.RoundToInt(Random.Range(10, 15) * amount);
                for (int i = 0; i < dropCount; i++)
                {
                    var spread = side * Random.Range(-1.1f, 1.1f) +
                                 up * Random.Range(-0.65f, 1.1f);
                    var velocity = direction * Random.Range(2.8f, 6.8f) + spread;
                    Emit(droplets, at, velocity, Random.Range(0.28f, 0.65f),
                        Random.Range(0.025f, 0.075f) * amount,
                        new Color(0.35f, 0.015f, 0.012f, Random.Range(0.72f, 0.96f)));
                }

                int cloudCount = amount > 1f ? 2 : 1;
                for (int i = 0; i < cloudCount; i++)
                {
                    var velocity = direction * Random.Range(0.02f, 0.1f) +
                                   side * Random.Range(-0.1f, 0.1f) +
                                   up * Random.Range(0.03f, 0.14f);
                    Emit(mist, at + direction * 0.035f, velocity,
                        Random.Range(0.22f, 0.34f), Random.Range(0.34f, 0.48f) * amount,
                        new Color(0.62f, 0.43f, 0.42f, Random.Range(0.16f, 0.25f)));
                }

                sheet.Play(true);
                droplets.Play(true);
                mist.Play(true);
            }

            static ParticleSystem MakeSystem(Transform parent, string name,
                Material material, ParticleSystemRenderMode mode, int maxParticles)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var system = go.AddComponent<ParticleSystem>();
                var main = system.main;
                main.loop = false;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = maxParticles;
                main.startSpeed = 0f;
                main.startLifetime = 0.5f;
                main.startSize = 1f;
                var emission = system.emission;
                emission.enabled = false;
                var shape = system.shape;
                shape.enabled = false;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = mode;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return system;
            }

            static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity,
                float lifetime, float size, Color colour)
            {
                var particle = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = velocity,
                    startLifetime = lifetime,
                    startSize = size,
                    startColor = colour,
                    rotation = Random.Range(0f, 360f),
                };
                system.Emit(particle, 1);
            }

            static void ExpandAndFade(ParticleSystem system, float start, float end)
            {
                var size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f,
                    AnimationCurve.EaseInOut(0f, start, 1f, end));
                Fade(system);
            }

            static void Fade(ParticleSystem system)
            {
                var colour = system.colorOverLifetime;
                colour.enabled = true;
                colour.color = new ParticleSystem.MinMaxGradient(new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f),
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.82f, 0.45f),
                        new GradientAlphaKey(0f, 1f),
                    },
                });
            }
        }
    }
}
