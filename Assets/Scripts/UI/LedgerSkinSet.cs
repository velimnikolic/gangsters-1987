using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's Modern Menus wardrobe - the Resources bridge between the book and
    /// the Synty INTERFACE Modern Menus pack, in LedgerModelSet's exact mould: the
    /// pack's sprites and TMP fonts are invisible to a running Play session (not in
    /// Resources, not referenced by any scene), so LedgerSkinBootstrap bakes chosen
    /// references into this asset on every editor load, filling only slots it finds
    /// EMPTY - an Inspector override survives re-bakes the way the gun bodies do.
    ///
    /// The pack ships its chrome near-white ON PURPOSE: a Synty screen gets its colour
    /// by tinting these sprites through Image.color, so the tints below are the pack's
    /// own scheme lifted from its screen prefabs (dark navy case, deep blue panel,
    /// powder-blue accent). Sprite PPU is 100 with borders authored for ~500-unit
    /// controls, hence the pixelsPerUnitMultiplier on every dress - without it a
    /// 120px 9-slice border swallows a 120-unit soft-key whole.
    ///
    /// Everything degrades to nothing, UiSkin's rule: no asset yet (fresh checkout,
    /// bootstrap not run) or an empty slot and TryDress* returns false - the almanac
    /// keeps its UiSkin / flat-colour fallbacks and the book still reads.
    /// </summary>
    public sealed class LedgerSkinSet : ScriptableObject
    {
        [Tooltip("Action soft-keys (BUY, PROMOTE, COMMIT...) - the thick 9-slice slab.")]
        public Sprite button;

        [Tooltip("Toolbar segments (the filter bar, CLOSE, sort rows) - a flat chip, " +
                 "deliberately NOT the action slab.")]
        public Sprite toolbarButton;

        [Tooltip("The tab strip's face. Active is said with AccentTint, not a swap.")]
        public Sprite tab;

        [Tooltip("The framed panel behind the detail card, sort menu and orders sheet.")]
        public Sprite panel;

        [Tooltip("The attribute star - the pack's own gold star icon. Full shows it " +
                 "as authored, empty is StarEmptyTint, a half is a Filled overlay.")]
        public Sprite star;

        [Tooltip("The standalone menu scene's full-screen art behind the book.")]
        public Sprite backdrop;

        [Tooltip("Every ledger text that is not a masthead or a tab.")]
        public TMP_FontAsset bodyFont;

        [Tooltip("The masthead and the tab strip.")]
        public TMP_FontAsset headlineFont;

        // -------------------------------------------------------- the pack's scheme

        /// <summary>A button's face - the pack's mid blue, read off its menu items.</summary>
        public static readonly Color FaceTint = new Color(0.10f, 0.35f, 0.58f);

        /// <summary>A toolbar segment's face - two steps darker than the action slab,
        /// so the chrome row reads as furniture and the slabs read as the verbs.</summary>
        public static readonly Color ToolbarTint = new Color(0.055f, 0.19f, 0.33f);

        /// <summary>The active tab / highlight wash - the pack's powder blue.</summary>
        public static readonly Color AccentTint = new Color(0.557f, 0.835f, 0.992f);

        /// <summary>A panel's face - one step of blue above the page behind it.</summary>
        public static readonly Color PanelTint = new Color(0.055f, 0.16f, 0.24f, 0.92f);

        /// <summary>An unearned star slot - the gold icon multiplied down to a ghost,
        /// so the pitch stays readable without competing with the earned stars.</summary>
        public static readonly Color StarEmptyTint = new Color(0.45f, 0.52f, 0.60f, 0.30f);

        // The pack authors its 9-slice borders for roughly 500-unit controls at PPU
        // 100; the ledger's soft-keys are 36 high. The multipliers below shrink the
        // drawn border by the same factor the control shrank, so the bevel stays a
        // bevel instead of becoming the whole button. Button_04 is 128px tall - at
        // 3.5 a 36-unit key shows the face at its native aspect; Button_08's chip is
        // border-60 all round and wants the finer 4.
        const float ButtonPpuMultiplier = 3.5f;
        const float ToolbarPpuMultiplier = 4f;
        const float PanelPpuMultiplier = 4f;

        // ----------------------------------------------------------- Resources plumbing

        static LedgerSkinSet loaded;
        static bool loadTried;

        // Static state outlives Play when domain reload is off - same fix as LedgerModelSet.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            loaded = null;
            loadTried = false;
        }

        public static LedgerSkinSet Instance
        {
            get
            {
                if (loaded || loadTried)
                    return loaded;

                loadTried = true;
                loaded = Resources.Load<LedgerSkinSet>("LedgerSkinSet");
                return loaded;
            }
        }

        /// <summary>True once the baked asset is loadable at all - the almanac's "am I
        /// wearing the pack" question (scanlines off, tab recolours on).</summary>
        public static bool Active => Instance != null;

        /// <summary>The menu scene's art; null keeps the scene on flat Room colour.</summary>
        public static Sprite Backdrop => Instance ? Instance.backdrop : null;

        /// <summary>The pack's gold star; null keeps UiSkin's baked stars.</summary>
        public static Sprite Star => Instance ? Instance.star : null;

        // ------------------------------------------------------------------- dressing

        /// <summary>Puts the pack panel on the Image as sliced chrome, PanelTint unless
        /// told otherwise. False (Image untouched) when the skin or slot is missing.</summary>
        public static bool TryDressPanel(Image image, Color? tint = null)
        {
            var set = Instance;
            if (!set || !set.panel || !image)
                return false;

            image.sprite = set.panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = PanelPpuMultiplier;
            image.color = tint ?? PanelTint;
            return true;
        }

        /// <summary>Themes an ACTION Button with the pack slab. ColorTint multiplies
        /// the face through the CanvasRenderer, so LedgerPalette's multiplier states
        /// carry over unchanged: normal sits dimmed, hover IS the full slab.</summary>
        public static bool TryDressButton(Button button, Image image) =>
            TryDressKey(button, image, Instance ? Instance.button : null,
                FaceTint, ButtonPpuMultiplier);

        /// <summary>A TOOLBAR segment - the flat chip in the darker tint, so the top
        /// bars read as one piece of furniture and never compete with the action
        /// slabs below.</summary>
        public static bool TryDressToolbarButton(Button button, Image image) =>
            TryDressKey(button, image, Instance ? Instance.toolbarButton : null,
                ToolbarTint, ToolbarPpuMultiplier);

        /// <summary>The tab strip's variant - same recipe, its own sprite slot so the
        /// strip can be re-dressed in the Inspector without touching the soft-keys.</summary>
        public static bool TryDressTab(Button button, Image image) =>
            TryDressKey(button, image, Instance ? Instance.tab : null,
                FaceTint, ButtonPpuMultiplier);

        static bool TryDressKey(Button button, Image image, Sprite face, Color tint,
            float ppuMultiplier)
        {
            if (!button || !image || face == null)
                return false;

            image.sprite = face;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = ppuMultiplier;
            image.color = tint;

            button.transition = Selectable.Transition.ColorTint;
            var colours = button.colors;
            colours.normalColor = LedgerPalette.ButtonNormal;
            colours.highlightedColor = LedgerPalette.ButtonHover;
            colours.selectedColor = LedgerPalette.ButtonHover;
            colours.pressedColor = LedgerPalette.ButtonPressed;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            button.colors = colours;
            return true;
        }

        // --------------------------------------------------------------------- fonts

        /// <summary>The pack's body face on this text - a no-op while the skin or the
        /// font is missing, leaving TMP's default (the sprite-less fallback look).</summary>
        public static void ApplyBody(TMP_Text text)
        {
            var set = Instance;
            if (set && set.bodyFont && text)
                text.font = set.bodyFont;
        }

        /// <summary>The display face - the masthead and the tab strip only.</summary>
        public static void ApplyHeadline(TMP_Text text)
        {
            var set = Instance;
            if (set && set.headlineFont && text)
                text.font = set.headlineFont;
        }
    }
}
