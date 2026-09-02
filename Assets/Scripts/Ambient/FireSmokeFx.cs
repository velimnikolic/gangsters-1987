using UnityEngine;

namespace LivingCity.Ambient
{
    /// <summary>
    /// The one catalogue for visible fire and smoke in the city. Gameplay code decides when
    /// something burns; this class decides which authored Particle Pack effect represents it.
    /// Keeping the paths here prevents cars, premises, industry and district demos from slowly
    /// drifting back to different art packs.
    /// </summary>
    public static class FireSmokeFx
    {
        const string FireRoot =
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/";
        const string SmokeRoot =
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/";
        const string WeaponRoot =
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Weapon Effects/Prefabs/";

        public const string ExplosionLarge = FireRoot + "BigExplosion.prefab";
        public const string ExplosionSmall = FireRoot + "SmallExplosion.prefab";
        public const string ExplosionTiny = FireRoot + "TinyExplosion.prefab";
        public const string FlamesLarge = FireRoot + "LargeFlames.prefab";
        public const string FlamesMedium = FireRoot + "MediumFlames.prefab";
        public const string FlamesTiny = FireRoot + "TinyFlames.prefab";
        public const string Smoke = SmokeRoot + "SmokeEffect.prefab";
        public const string Steam = SmokeRoot + "RisingSteam.prefab";
        public const string MuzzleFlash = WeaponRoot + "MuzzleFlash.prefab";

        // Particle start colour multiplies the pack's flipbook and colour-over-life gradient.
        // These are intentionally not opaque: overlapping translucent smoke becomes a flat wall.
        public static readonly Color ExhaustSmoke = new Color(0.66f, 0.68f, 0.69f, 0.78f);
        public static readonly Color EngineSmoke = new Color(0.18f, 0.17f, 0.15f, 0.78f);
        public static readonly Color FireSmoke = new Color(0.16f, 0.15f, 0.13f, 0.92f);
        public static readonly Color GunSmoke = new Color(0.46f, 0.45f, 0.42f, 0.72f);
        public static readonly Color DieselSmoke = new Color(0.28f, 0.27f, 0.24f, 0.62f);
        public static readonly Color ChimneySmoke = new Color(0.58f, 0.57f, 0.54f, 0.48f);

        /// <summary>Editor demos load art directly from the project, like the rest of the city
        /// builders. Player builds keep the existing null/fallback contract.</summary>
        public static GameObject Load(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        /// <summary>Tint authored smoke without cloning or mutating its shared material.</summary>
        public static void TintSmoke(ParticleSystem system, Color colour)
        {
            if (system == null) return;
            var main = system.main;
            main.startColor = colour;
        }

        /// <summary>Removes the Particle Pack gallery pose and makes one trigger pull one
        /// short flash. The sample root is saved at 1.75 scale and loops for inspection;
        /// neither is appropriate on the end of a handheld barrel.</summary>
        public static float TuneMuzzleFlash(GameObject effect, float metres)
        {
            if (effect == null) return 0f;
            effect.transform.localScale = Vector3.one * Mathf.Max(0.01f, metres);
            float live = 0.2f;
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.loop = false;
                main.prewarm = false;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                live = Mathf.Max(live, main.duration + main.startLifetime.constantMax);
                system.Clear(true);
                system.Play(true);
            }
            return Mathf.Min(live, 1f);
        }

        /// <summary>Turns the continuous gallery smoke into the two-or-three wisps left by
        /// one round. Particles remain in world space, so recoil and a moving car do not drag
        /// the smoke around after it has left the muzzle.</summary>
        public static float TuneGunSmoke(GameObject effect, float calibre, bool rapid,
                                         float amount = 1f, float size = 1f,
                                         float lifetime = 1f)
        {
            if (effect == null) return 0f;
            effect.transform.localScale = Vector3.one;
            var system = effect.GetComponentInChildren<ParticleSystem>(true);
            if (system == null) return 0f;

            calibre = Mathf.Max(0.05f, calibre);
            amount = Mathf.Clamp(amount, 0.1f, 4f);
            size = Mathf.Clamp(size, 0.1f, 4f);
            lifetime = Mathf.Clamp(lifetime, 0.1f, 4f);
            var main = system.main;
            main.loop = false;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.38f * lifetime, (rapid ? 0.7f : 0.9f) * lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                calibre * 0.45f * size, calibre * size);
            main.startColor = GunSmoke;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            int burstMin = Mathf.Max(1, Mathf.RoundToInt((rapid ? 1f : 2f) * amount));
            int burstMax = Mathf.Max(burstMin, Mathf.RoundToInt((rapid ? 2f : 3f) * amount));
            main.maxParticles = burstMax + 1;

            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)burstMin, (short)burstMax)
            });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.012f;
            shape.radiusThickness = 1f;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;

            // All three axes must be written in the same MinMaxCurve mode: Unity
            // rejects a module whose x/y/z disagree, and the pack prefabs arrive with
            // whatever the gallery authored on the axes this code does not care about.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var sizeOverLife = system.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.8f));

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.72f, 0.72f, 0.72f), 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            system.Clear(true);
            system.Play(true);
            return main.startLifetime.constantMax + 0.15f;
        }

        /// <summary>Dense, rising smoke for a burning building. The authored sample is a
        /// thin demonstration column; a frontage gets several of these tuned columns.</summary>
        public static ParticleSystem TuneFireSmoke(GameObject effect, float strength = 1f)
        {
            if (effect == null) return null;
            effect.transform.localScale = Vector3.one;
            var system = effect.GetComponentInChildren<ParticleSystem>(true);
            if (system == null) return null;

            strength = Mathf.Clamp(strength, 0.5f, 2f);
            var main = system.main;
            main.loop = true;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.8f, 6.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.05f, 1.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.85f, 1.45f);
            main.startColor = FireSmoke;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(13f * strength * 6.2f) + 8;

            var emission = system.emission;
            emission.rateOverTime = 13f * strength;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.42f;
            shape.radiusThickness = 1f;
            shape.position = Vector3.zero;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Same rule as above: the drift on x and z is the point here, but y has to
            // be written in the same mode or the whole module is refused.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 2.35f));

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(0.72f, 0.68f, 0.62f), 0f),
                    new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.72f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            system.Clear(true);
            system.Play(true);
            return system;
        }
    }
}
