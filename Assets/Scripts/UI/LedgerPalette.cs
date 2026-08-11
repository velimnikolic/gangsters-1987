using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// The personnel ledger's colour vocabulary - IntentionPalette's shape, applied to a
    /// 1980 desktop terminal. The reference is an early PC on the outfit's back-office
    /// desk: a beige plastic case, a near-black tube, and one green phosphor doing all
    /// the talking - bright for data, dim for chrome, inverse video for the selection,
    /// and a second amber "gun" reserved for warnings the way CGA reserved a colour.
    /// Every "material" is still a sprite-less Image: the case is a bevel (four 2px
    /// strips), a button is a translucent phosphor block in a 1px frame, a scanline is
    /// a dark hairline.
    ///
    /// Semantic names, same discipline as IntentionPalette: Phosphor is always data,
    /// PhosphorDim is always chrome, Amber is always "something is wrong", whoever
    /// draws it.
    /// </summary>
    public static class LedgerPalette
    {
        /// <summary>The dark office behind the monitor - and the modal shield.</summary>
        public static readonly Color Room = new Color(0.02f, 0.025f, 0.02f, 0.99f);

        /// <summary>The monitor's beige plastic case.</summary>
        public static readonly Color Case = new Color(0.70f, 0.66f, 0.55f);

        /// <summary>The tube itself - not pure black; a warmed-up CRT never is.</summary>
        public static readonly Color Screen = new Color(0.015f, 0.055f, 0.025f);

        /// <summary>The phosphor at full beam - names, values, anything that IS data.</summary>
        public static readonly Color Phosphor = new Color(0.38f, 1f, 0.50f);

        /// <summary>The phosphor at half beam - rules, tags, frames, chrome.</summary>
        public static readonly Color PhosphorDim = new Color(0.22f, 0.55f, 0.30f);

        /// <summary>Barely-lit phosphor - empty star slots, the loyalty bar's trough.</summary>
        public static readonly Color PhosphorFaint = new Color(0.38f, 1f, 0.50f, 0.14f);

        /// <summary>The ruled line under a record row.</summary>
        public static readonly Color HairLine = new Color(0.38f, 1f, 0.50f, 0.10f);

        /// <summary>The band behind a crew's header row.</summary>
        public static readonly Color BandDim = new Color(0.38f, 1f, 0.50f, 0.16f);

        /// <summary>The detail card's face - one shade of glow above the tube.</summary>
        public static readonly Color CardTint = new Color(0.38f, 1f, 0.50f, 0.05f);

        /// <summary>The warning gun: JAILED, DEAD, WANTED, and every "are you sure".</summary>
        public static readonly Color Amber = new Color(1f, 0.72f, 0.20f);

        /// <summary>The assign-mode wash over a valid drop target.</summary>
        public static readonly Color Target = new Color(0.38f, 1f, 0.50f, 0.22f);

        /// <summary>Present but not actionable - between faint and dim.</summary>
        public static readonly Color Disabled = new Color(0.30f, 0.60f, 0.36f, 0.45f);

        /// <summary>A button's face - a translucent phosphor block inside a 1px frame.</summary>
        public static readonly Color ButtonGlow = new Color(0.38f, 1f, 0.50f, 0.16f);

        /// <summary>The mugshot slot's backing.</summary>
        public static readonly Color PhotoBack = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>The wash behind the card's identity block - the card's own
        /// masthead, a breath of phosphor over the face rather than a black slab.</summary>
        public static readonly Color CardMasthead = new Color(0.38f, 1f, 0.50f, 0.09f);

        /// <summary>The skinned card's face - the pack panel re-cast from its blue
        /// scheme to the tube's green, one lit step above the screen.</summary>
        public static readonly Color CardFace = new Color(0.045f, 0.12f, 0.065f, 0.95f);

        /// <summary>The dark line a CRT draws between raster rows.</summary>
        public static readonly Color ScanLine = new Color(0f, 0f, 0f, 0.28f);

        /// <summary>The lit edge of the raised case bevel (top/left; swapped = sunken).</summary>
        public static readonly Color BevelLight = new Color(1f, 1f, 1f, 0.40f);

        /// <summary>The shaded edge of the raised case bevel (bottom/right).</summary>
        public static readonly Color BevelDark = new Color(0f, 0f, 0f, 0.38f);

        /// <summary>Button tint states - the tint multiplies the face, so normal sits
        /// below 1 and hover IS the full glow.</summary>
        public static readonly Color ButtonNormal = new Color(0.78f, 0.78f, 0.78f);
        public static readonly Color ButtonHover = Color.white;
        public static readonly Color ButtonPressed = new Color(0.5f, 0.5f, 0.5f);

        /// <summary>The multiply that keeps the tube green when UiSkin dresses it - the
        /// sheet's charcoal well times this reads as a warmed-up CRT, not a grey box.</summary>
        public static readonly Color ScreenCast = new Color(0.75f, 1f, 0.82f);
    }
}
