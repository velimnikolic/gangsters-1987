using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>What the marker is drawn as. Both are the same sprite-less Image - a solid
    /// rectangle - and the shape is entirely its rotation, so neither costs any art.</summary>
    public enum OverlayShape
    {
        /// <summary>Something that moves and decides: a car, a walker. The Sims diamond.</summary>
        Diamond,

        /// <summary>A place: a building that reports what is happening inside it. Square-on,
        /// so a glance separates "somebody is doing something" from "here is where".</summary>
        Square,
    }

    /// <summary>
    /// Everything the overlay needs from a thing worth explaining. It is deliberately the whole
    /// contract and nothing more: an anchor to hang the marker on, how it should look, and the
    /// two lines of the popup.
    ///
    /// This exists because the police overlay had reached its limit. It carried one Marker class
    /// with a PolicePatrolAgent field AND a PoliceOfficerAgent field, and a two-way `if
    /// (marker.Car) ... else ...` at four separate sites - colour, picking, popup text, registry
    /// sync. A third subject would have made that a three-way branch in four places; there are
    /// six now.
    ///
    /// The cost model matters and is not symmetric. OverlayColor and OverlayHidden are read for
    /// EVERY subject every frame, so they must be cheap - a switch over a cached state, never a
    /// search. OverlayTitle and OverlayLine are read only for the SELECTED subject and only when
    /// OverlayKey changes, so they may allocate a string and may count things.
    /// </summary>
    public interface IOverlaySubject
    {
        /// <summary>The transform the marker follows. Null once the subject is destroyed, which
        /// is how the HUD notices and drops it.</summary>
        Transform OverlayAnchor { get; }

        /// <summary>Metres above the anchor's origin to float the marker.</summary>
        float OverlayHeight { get; }

        /// <summary>Suppresses the marker without unregistering: a child riding the bus or sat
        /// inside the school is still a live subject, it is just not anywhere to point at.</summary>
        bool OverlayHidden { get; }

        OverlayShape MarkerShape { get; }

        /// <summary>Read every frame for every subject - keep it a switch.</summary>
        Color OverlayColor { get; }

        /// <summary>Read only for the selected subject, only when OverlayKey changes.</summary>
        string OverlayTitle { get; }

        /// <summary>As OverlayTitle. This is the sentence that says what is going on.</summary>
        string OverlayLine { get; }

        /// <summary>
        /// Cheap stand-in for "the popup would say something different now". Any value will do
        /// as long as it changes exactly when the words do - the HUD only ever compares it with
        /// the previous one from the SAME subject, so subjects need not keep out of each other's
        /// number space.
        /// </summary>
        long OverlayKey { get; }
    }

    /// <summary>
    /// How a marker differs from the stock 12px steady diamond. A struct handed over ONCE -
    /// the HUD caches it per marker at build time - so a style must not change over a
    /// subject's lifetime; anything that changes frame to frame (colour, hidden) stays on
    /// the per-frame reads of IOverlaySubject.
    /// </summary>
    public struct MarkerStyle
    {
        /// <summary>Multiplier on the stock marker size. 1 = the HUD's 12px.</summary>
        public float SizeScale;

        /// <summary>Breathe the marker's scale - the "this one is yours" tell.</summary>
        public bool Pulse;

        /// <summary>Seconds per pulse beat.</summary>
        public float PulsePeriod;

        /// <summary>Scale swing of the pulse: 0.2 is plus/minus 20%.</summary>
        public float PulseAmplitude;

        /// <summary>
        /// Clamp to the screen edge instead of culling when the subject is panned out of
        /// view - a marker that must never be lost, whatever the camera is doing.
        /// </summary>
        public bool AlwaysVisible;

        /// <summary>
        /// Draw the marker only while this subject is the selection. For subjects that exist
        /// in the dozens - every business in the city - a permanent square over each roof is
        /// noise, not information. Picking is physics-based and never consults the Image, so
        /// a subject with its marker off is exactly as clickable as one without.
        /// </summary>
        public bool SelectedOnly;

        public static MarkerStyle Default => new MarkerStyle { SizeScale = 1f };
    }

    /// <summary>
    /// Opt-in styling on top of IOverlaySubject. A separate interface rather than a new
    /// member so the six existing implementers stay untouched; the HUD probes with `as` at
    /// marker build time and falls back to MarkerStyle.Default.
    /// </summary>
    public interface IOverlayStyledSubject : IOverlaySubject
    {
        MarkerStyle MarkerStyle { get; }
    }

    /// <summary>
    /// Where a building's marker floats: clear of its own roof. Derived from the renderers at
    /// runtime rather than baked into the marker at generation, which keeps it out of the saved
    /// scene - the bank in an existing city gets its marker without the city being rebuilt, and
    /// a landmark that is ever rescaled cannot end up with a marker inside its own roof.
    ///
    /// Called ONCE per building and cached by the caller: GetComponentsInChildren allocates, and
    /// this runs over every renderer in a landmark.
    /// </summary>
    public static class BuildingMarker
    {
        /// <summary>Metres of daylight between the ridge and the marker.</summary>
        public const float Clearance = 4f;

        public static float HeightFor(Transform building)
        {
            var renderers = building.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return Clearance;

            var top = renderers[0].bounds.max.y;
            for (var i = 1; i < renderers.Length; i++)
                top = Mathf.Max(top, renderers[i].bounds.max.y);

            return top - building.position.y + Clearance;
        }
    }

    /// <summary>
    /// Every subject currently worth a marker. Replaces PolicePatrolAgent.Fleet and
    /// PoliceOfficerAgent.Officers, which existed only to be read by the HUD.
    ///
    /// <see cref="Version"/> is the HUD's whole change detector. The old one compared two list
    /// COUNTS, which cannot see a swap - one subject leaving and another arriving between two
    /// frames reads as no change at all. That was harmless for a police fleet that is spawned
    /// once and never changes again, and is not harmless for bank customers and school parents,
    /// which are destroyed and respawned for the whole session.
    /// </summary>
    public static class OverlayRegistry
    {
        static readonly List<IOverlaySubject> subjects = new List<IOverlaySubject>();

        public static IReadOnlyList<IOverlaySubject> Subjects => subjects;

        /// <summary>Bumped on every add and every remove. Not a count - see the class note.</summary>
        public static int Version { get; private set; }

        public static void Register(IOverlaySubject subject)
        {
            if (subject == null)
                return;

            subjects.Add(subject);
            Version++;
        }

        public static void Unregister(IOverlaySubject subject)
        {
            if (subject != null && subjects.Remove(subject))
                Version++;
        }

        /// <summary>
        /// Static state outlives Play when domain reload is off (Enter Play Mode Options), which
        /// would carry a previous session's destroyed subjects into the next one. The registries
        /// this replaced had the same hole; it is cheaper to close it here once than to remember
        /// the setting.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            subjects.Clear();
            Version = 0;
        }
    }
}
