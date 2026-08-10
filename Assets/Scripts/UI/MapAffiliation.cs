using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// The strategic map's gang-colour seam. No gangs exist on this branch, so the provider
    /// is null and every dot falls through to the map's defaults (police blue, civilian
    /// white). The sim layer, when it merges, assigns Provider at its own bootstrap and
    /// answers with the person's organisation colour - the map itself never changes.
    /// </summary>
    public static class MapAffiliation
    {
        /// <summary>Given the person's main component, the colour of whoever owns them -
        /// or null for "no affiliation, use the default".</summary>
        public static System.Func<Component, Color?> Provider;

        public static Color? ColorFor(Component person) => Provider?.Invoke(person);

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Provider = null;
    }
}
