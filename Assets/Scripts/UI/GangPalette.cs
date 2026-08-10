using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// One colour per gang, everywhere a gang shows: overlay diamonds and the M-map's
    /// dots (through MapAffiliation). Gang 0 reuses the map's PlayerGold so "yours" is
    /// one colour across every surface. The rest were picked against the taken palette -
    /// police blue, civilian white, and IntentionPalette's activity teal/green/grey/
    /// yellow - and live in this one file so a retune is a one-line change.
    /// </summary>
    public static class GangPalette
    {
        static readonly Color[] Colors =
        {
            new Color(1f, 0.84f, 0.25f),    // 0 The Outfit - the player's gold
            new Color(0.87f, 0.22f, 0.22f), // 1 Falcone - crimson
            new Color(0.62f, 0.42f, 0.95f), // 2 Santoro - violet
            new Color(0.93f, 0.38f, 0.72f), // 3 Lucchese - magenta
            new Color(0.98f, 0.55f, 0.12f), // 4 DeMarco - burnt orange
        };

        /// <summary>Grey for -1/unknown rather than throwing - OverlayColor is read
        /// every frame and a bad id must stay a cosmetic bug.</summary>
        public static Color Of(int gangId) =>
            gangId >= 0 && gangId < Colors.Length
                ? Colors[gangId]
                : new Color(0.6f, 0.6f, 0.6f);
    }
}
