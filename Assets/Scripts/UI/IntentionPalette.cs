using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// The overlay's colour vocabulary, shared by every intention map. Six meanings, and the
    /// point of putting them here is that they mean the SAME thing whoever uses them: amber is
    /// always "heading home", teal is always "manoeuvring in a yard", grey is always "off duty".
    /// A patrol car and a school bus parked in their respective yards read alike because they
    /// ARE alike, and an overlay whose colours had to be learned per subject would be worth
    /// less than no colour at all.
    ///
    /// Lifted out of PoliceIntention, which owned the first four.
    /// </summary>
    public static class IntentionPalette
    {
        /// <summary>Out on the street, working - the default blue.</summary>
        public static readonly Color Working = new Color(0.30f, 0.65f, 1f);

        /// <summary>Heading home - amber, the "about to leave the street" warning tone.</summary>
        public static readonly Color Homeward = new Color(1f, 0.75f, 0.25f);

        /// <summary>Off duty, parked, inside - grey, deliberately unassertive.</summary>
        public static readonly Color Idle = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>A yard manoeuvre, both directions - teal, brief by nature.</summary>
        public static readonly Color Manoeuvre = new Color(0.25f, 0.85f, 0.80f);

        /// <summary>Standing about for something that has not arrived - yellow. The one state
        /// a player is most likely to be looking FOR when they click a child.</summary>
        public static readonly Color Waiting = new Color(0.98f, 0.87f, 0.30f);

        /// <summary>Mid-errand and about to finish: boarding, alighting, unloading - green.</summary>
        public static readonly Color Busy = new Color(0.45f, 0.85f, 0.42f);

        /// <summary>A place rather than an actor. Pale and low-contrast on purpose: a building
        /// marker is permanent and must not compete with the agents moving under it.</summary>
        public static readonly Color Place = new Color(0.80f, 0.80f, 0.86f, 0.80f);
    }
}
