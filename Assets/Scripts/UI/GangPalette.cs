using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// One colour per gang, everywhere a gang shows: overlay diamonds, the street
    /// overlay's dots over a rival's men, and the M-map (through MapAffiliation and
    /// DemoMap). Gang 0 reuses the map's PlayerGold so "yours" is one colour across
    /// every surface. The rest were picked against the taken palette - police blue,
    /// civilian white, and IntentionPalette's activity teal/green/grey/yellow - and live
    /// in this one file so a retune is a one-line change.
    ///
    /// Twenty rivals is most of a colour wheel, so the walk is deliberate: hue turns
    /// about twenty degrees a family and the VALUE alternates deep/bright, which is what
    /// keeps two neighbouring hues apart on a four-pixel map dot. Two bands are left
    /// out - the gold the player owns, and the blue the law drives in (DemoMap's
    /// PoliceBlue). The first four ids keep the colours they had; an id is a family for
    /// the life of a campaign and its colour must not move under it.
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
            new Color(0.72f, 0.14f, 0.22f), // 5 Corvetti - dried blood
            new Color(0.98f, 0.44f, 0.85f), // 6 Barzini - carnation
            new Color(0.70f, 0.11f, 0.70f), // 7 Moretti - purple
            new Color(0.83f, 0.47f, 0.95f), // 8 Rinaldi - orchid
            new Color(0.12f, 0.57f, 0.80f), // 9 Castellano - petrol (clear of police blue)
            new Color(0.44f, 0.89f, 0.98f), // 10 Vitelli - ice
            new Color(0.14f, 0.68f, 0.66f), // 11 Marchetti - deep teal
            new Color(0.43f, 0.95f, 0.82f), // 12 Greco - aqua
            new Color(0.11f, 0.72f, 0.41f), // 13 Ferraro - bottle green
            new Color(0.47f, 0.95f, 0.62f), // 14 Serrano - mint
            new Color(0.13f, 0.66f, 0.13f), // 15 Delgado - forest
            new Color(0.54f, 0.92f, 0.41f), // 16 O'Shea - shamrock
            new Color(0.42f, 0.70f, 0.11f), // 17 Doyle - olive
            new Color(0.82f, 0.92f, 0.41f), // 18 Kowalski - straw
            new Color(0.62f, 0.62f, 0.09f), // 19 Volkov - brass
            new Color(0.55f, 0.22f, 0.11f), // 20 Petrov - rust
        };

        /// <summary>Grey for -1/unknown rather than throwing - OverlayColor is read
        /// every frame and a bad id must stay a cosmetic bug.</summary>
        public static Color Of(int gangId) =>
            gangId >= 0 && gangId < Colors.Length
                ? Colors[gangId]
                : new Color(0.6f, 0.6f, 0.6f);

        /// <summary>How many families this file can dress. The suite holds it against
        /// GangCatalog.GangCount: a family with no colour of its own would show up grey
        /// on the map beside the law's own grey traffic.</summary>
        public static int Count => Colors.Length;
    }
}
