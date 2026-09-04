using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// A circular source whose surroundings the player can see on a map - the fog-of-war
    /// side of a lone informant. The map itself still shows the whole city; this controls
    /// which moving people and vehicles earn a live mark on top of it.
    /// </summary>
    public interface IMapVisionSource
    {
        Vector3 VisionCenter { get; }

        /// <summary>Metres, measured flat on XZ - the map is top-down, height is noise.</summary>
        float VisionRadius { get; }

        bool VisionActive { get; }
    }

    /// <summary>
    /// A source whose visible ground is not a circle. Crews use this to reveal the exact
    /// city blocks occupied by their living members plus the streets around those blocks.
    /// Keeping the shape behind this interface lets every map ask one question without
    /// learning how a crew, an informant network, or a future lookout defines its sight.
    /// </summary>
    public interface IMapVisionAreaSource
    {
        bool VisionActive { get; }
        bool IsVisible(Vector3 worldPosition);
    }

    /// <summary>
    /// The shared extension seam for map fog of war. Simple informants register circles;
    /// crews register their block-shaped aggregate through IMapVisionAreaSource. Renderers
    /// ask one question and do not fork the intelligence rule between full map and minimap.
    /// </summary>
    public static class MapVisionRegistry
    {
        static readonly List<IMapVisionSource> Registered = new List<IMapVisionSource>();
        static readonly List<IMapVisionAreaSource> RegisteredAreas =
            new List<IMapVisionAreaSource>();

        /// <summary>
        /// The one runtime switch for every fog-of-war consumer. The T debug panel can
        /// turn it off without disabling or unregistering the actual vision sources,
        /// so turning it back on immediately restores the real visibility picture.
        /// </summary>
        public static bool FogOfWarEnabled { get; private set; } = true;

        public static void SetFogOfWarEnabled(bool enabled) =>
            FogOfWarEnabled = enabled;

        public static IReadOnlyList<IMapVisionSource> Sources => Registered;

        public static void Register(IMapVisionSource source)
        {
            if (source != null && !Registered.Contains(source))
                Registered.Add(source);
        }

        public static void Unregister(IMapVisionSource source)
        {
            Registered.Remove(source);
        }

        public static void RegisterArea(IMapVisionAreaSource source)
        {
            if (source != null && !RegisteredAreas.Contains(source))
                RegisteredAreas.Add(source);
        }

        public static void UnregisterArea(IMapVisionAreaSource source)
        {
            RegisteredAreas.Remove(source);
        }

        /// <summary>
        /// False when nobody is providing eyes at all - the playable-mafioso layer is
        /// currently parked, so there may be no player to register. The map treats that
        /// as "no fog yet" and shows everyone: a fog with no possible informants would
        /// just blank the map, which reads as broken rather than as mystery.
        /// </summary>
        public static bool HasActiveSources
        {
            get
            {
                if (!FogOfWarEnabled)
                    return false;

                for (var i = 0; i < RegisteredAreas.Count; i++)
                    if (RegisteredAreas[i].VisionActive)
                        return true;
                for (var i = 0; i < Registered.Count; i++)
                    if (Registered[i].VisionActive)
                        return true;
                return false;
            }
        }

        public static bool IsVisible(Vector3 worldPosition)
        {
            if (!FogOfWarEnabled)
                return true;

            for (var i = 0; i < RegisteredAreas.Count; i++)
            {
                var source = RegisteredAreas[i];
                if (source.VisionActive && source.IsVisible(worldPosition))
                    return true;
            }

            for (var i = 0; i < Registered.Count; i++)
            {
                var source = Registered[i];
                if (!source.VisionActive)
                    continue;

                var centre = source.VisionCenter;
                var dx = centre.x - worldPosition.x;
                var dz = centre.z - worldPosition.z;
                if (dx * dx + dz * dz <= source.VisionRadius * source.VisionRadius)
                    return true;
            }

            return false;
        }

        /// <summary>Presentation helper for layers that also run in scenes with no fog
        /// provider. Such scenes retain their old unrestricted view; once any provider
        /// is active, the shared visibility answer is authoritative.</summary>
        public static bool IsRevealed(Vector3 worldPosition) =>
            !HasActiveSources || IsVisible(worldPosition);

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Registered.Clear();
            RegisteredAreas.Clear();
            FogOfWarEnabled = true;
        }
    }

    /// <summary>
    /// The player's own eyes. GameplayBootstrap adds this to the player the same way it adds
    /// the arsenal; nothing else needs to know the player IS a vision source.
    /// </summary>
    public sealed class PlayerVisionSource : MonoBehaviour, IMapVisionSource
    {
        [Tooltip("How far around the player the strategic map shows people, metres.")]
        [SerializeField] float radius = 60f;

        public Vector3 VisionCenter => transform.position;
        public float VisionRadius => radius;
        public bool VisionActive => isActiveAndEnabled;

        void OnEnable() => MapVisionRegistry.Register(this);
        void OnDisable() => MapVisionRegistry.Unregister(this);
    }
}
