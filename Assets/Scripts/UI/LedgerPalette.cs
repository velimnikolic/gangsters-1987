using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's colour vocabulary - IntentionPalette's shape, in two wardrobes.
    ///
    /// The book has worn Modern Menus chrome ever since LedgerSkinSet landed - blue
    /// pack slabs, blue panels, a blue tab strip - while every WORD printed on it
    /// stayed the old CRT green. Green ink on blue furniture is the hybrid this file
    /// ends: when the pack is dressed, the whole palette resolves to the pack's own
    /// scheme (a deep navy tube, ice-white data, steel chrome, powder-blue highlights
    /// and a gold warning gun read off the building card's own highlight). Take the
    /// pack away - fresh checkout, bootstrap not run, folder gone - and every value
    /// falls back to the 1980 desktop terminal this file was authored as, so the
    /// sprite-less book still reads exactly as it always did.
    ///
    /// Semantic names, same discipline as IntentionPalette: Phosphor is always data,
    /// PhosphorDim is always chrome, Amber is always "something is wrong", whoever
    /// draws it. The names outlive whichever wardrobe answers - a call site never
    /// asks which one it got.
    /// </summary>
    public static class LedgerPalette
    {
        /// <summary>Which wardrobe answers. LedgerSkinSet caches the Resources probe
        /// after the first call, so a per-colour read costs one null check.</summary>
        static bool Packed => LedgerSkinSet.Active;

        /// <summary>The dark office behind the monitor - and the modal shield.</summary>
        public static Color Room => Packed
            ? new Color(0.016f, 0.028f, 0.045f, 0.99f)
            : new Color(0.02f, 0.025f, 0.02f, 0.99f);

        /// <summary>The page ground: the pack's deep navy, or the tube itself - not
        /// pure black, because a warmed-up CRT never is.</summary>
        public static Color Screen => Packed
            ? new Color(0.028f, 0.055f, 0.085f)
            : new Color(0.015f, 0.055f, 0.025f);

        /// <summary>Data at full beam - names, values, anything that IS data.</summary>
        public static Color Phosphor => Packed
            ? new Color(0.90f, 0.95f, 1f)
            : new Color(0.38f, 1f, 0.50f);

        /// <summary>Chrome at half beam - rules, tags, frames, captions.</summary>
        public static Color PhosphorDim => Packed
            ? new Color(0.52f, 0.66f, 0.80f)
            : new Color(0.22f, 0.55f, 0.30f);

        /// <summary>Barely lit - empty star slots, the loyalty bar's trough.</summary>
        public static Color PhosphorFaint => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.14f)
            : new Color(0.38f, 1f, 0.50f, 0.14f);

        /// <summary>The ruled line under a record row.</summary>
        public static Color HairLine => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.10f)
            : new Color(0.38f, 1f, 0.50f, 0.10f);

        /// <summary>The band behind a crew's header row.</summary>
        public static Color BandDim => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.15f)
            : new Color(0.38f, 1f, 0.50f, 0.16f);

        /// <summary>The detail card's face - one shade of glow above the page.</summary>
        public static Color CardTint => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.05f)
            : new Color(0.38f, 1f, 0.50f, 0.05f);

        /// <summary>The warning gun: JAILED, DEAD, WANTED, and every "are you sure".
        /// The dressed book borrows the building card's own gold highlight, so one
        /// colour means "look here" across every screen the project draws.</summary>
        public static Color Amber => Packed
            ? new Color(1f, 0.78f, 0.32f)
            : new Color(1f, 0.72f, 0.20f);

        /// <summary>The assign-mode wash over a valid drop target.</summary>
        public static Color Target => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.22f)
            : new Color(0.38f, 1f, 0.50f, 0.22f);

        /// <summary>Present but not actionable - between faint and dim.</summary>
        public static Color Disabled => Packed
            ? new Color(0.58f, 0.70f, 0.82f, 0.45f)
            : new Color(0.30f, 0.60f, 0.36f, 0.45f);

        /// <summary>A button's face - a translucent block inside a 1px frame.</summary>
        public static Color ButtonGlow => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.14f)
            : new Color(0.38f, 1f, 0.50f, 0.16f);

        /// <summary>The mugshot slot's backing.</summary>
        public static Color PhotoBack => Packed
            ? new Color(0.008f, 0.020f, 0.035f, 0.65f)
            : new Color(0f, 0f, 0f, 0.55f);

        /// <summary>The wash behind the card's identity block - the card's own
        /// masthead, a breath of light over the face rather than a black slab.</summary>
        public static Color CardMasthead => Packed
            ? new Color(0.557f, 0.835f, 0.992f, 0.09f)
            : new Color(0.38f, 1f, 0.50f, 0.09f);

        /// <summary>The skinned card's face - the pack panel one lit step above the
        /// page behind it.</summary>
        public static Color CardFace => Packed
            ? new Color(0.045f, 0.095f, 0.145f, 0.95f)
            : new Color(0.045f, 0.12f, 0.065f, 0.95f);

        /// <summary>The dark line a CRT draws between raster rows. Sprite-less
        /// wardrobe only - a dressed book has the pack's chrome for texture and
        /// draws no raster at all.</summary>
        public static Color ScanLine => new Color(0f, 0f, 0f, 0.28f);

        /// <summary>Button tint states - the tint multiplies the face, so normal sits
        /// below 1 and hover IS the full face. Wardrobe-independent: they are
        /// multipliers, not colours.</summary>
        public static Color ButtonNormal => new Color(0.78f, 0.78f, 0.78f);
        public static Color ButtonHover => Color.white;
        public static Color ButtonPressed => new Color(0.5f, 0.5f, 0.5f);
    }
}
