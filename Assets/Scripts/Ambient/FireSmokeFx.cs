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

        public const string ExplosionLarge = FireRoot + "BigExplosion.prefab";
        public const string ExplosionSmall = FireRoot + "SmallExplosion.prefab";
        public const string ExplosionTiny = FireRoot + "TinyExplosion.prefab";
        public const string FlamesLarge = FireRoot + "LargeFlames.prefab";
        public const string FlamesMedium = FireRoot + "MediumFlames.prefab";
        public const string FlamesTiny = FireRoot + "TinyFlames.prefab";
        public const string Smoke = SmokeRoot + "SmokeEffect.prefab";
        public const string Steam = SmokeRoot + "RisingSteam.prefab";

        // Particle start colour multiplies the pack's flipbook and colour-over-life gradient.
        // These are intentionally not opaque: overlapping translucent smoke becomes a flat wall.
        public static readonly Color ExhaustSmoke = new Color(0.72f, 0.74f, 0.76f, 0.34f);
        public static readonly Color EngineSmoke = new Color(0.18f, 0.17f, 0.15f, 0.78f);
        public static readonly Color FireSmoke = new Color(0.24f, 0.22f, 0.19f, 0.72f);
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
    }
}
