using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Something whose surroundings the player can see on the strategic map - the fog-of-war
    /// side of an informant. The map itself shows the whole city (the player lives here; the
    /// streets are not a secret), but a moving PERSON only earns a dot while inside some
    /// source's radius.
    /// </summary>
    public interface IMapVisionSource
    {
        Vector3 VisionCenter { get; }

        /// <summary>Metres, measured flat on XZ - the map is top-down, height is noise.</summary>
        float VisionRadius { get; }

        bool VisionActive { get; }
    }

    /// <summary>
    /// The extension seam for fog of war: today the player is the only registrant, but a crew
    /// member on a job or a business paying protection registers the same way and the map asks
    /// no questions. Deliberately a dumb list - a handful of sources against a few thousand
    /// dots is a multiply per pair, nowhere near needing the pedestrian spatial hash.
    /// </summary>
    public static class MapVisionRegistry
    {
        static readonly List<IMapVisionSource> Registered = new List<IMapVisionSource>();

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
                for (var i = 0; i < Registered.Count; i++)
                    if (Registered[i].VisionActive)
                        return true;
                return false;
            }
        }

        public static bool IsVisible(Vector3 worldPosition)
        {
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

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Registered.Clear();
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
