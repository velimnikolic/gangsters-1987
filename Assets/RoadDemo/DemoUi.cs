using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The road demo's wardrobe: one place that owns what every screen in this scene
    /// is made of - the Synty INTERFACE Modern Menus sprites, the pack's two type
    /// faces, and the colour names the demo prints in.
    ///
    /// Demo-local ON PURPOSE. RoadDemo does not borrow LivingCity's runtime (it has
    /// its own clock, sky, lamps and headlights), so it does not borrow LedgerSkinSet
    /// either - but it does dress from the SAME pack folder and answers with the same
    /// scheme, so the demo's top bar and the ledger the demo installs read as one
    /// piece of software instead of two.
    ///
    /// Editor-only asset loading, the discipline RoadDemoBuilder and BuildingCardPicker
    /// already keep: this scene is dev tooling that never ships, so AssetDatabase is
    /// fine and Resources stays clean. Every slot degrades to null and every helper
    /// falls back to a flat block, so a missing pack costs colour, never a screen.
    /// </summary>
    public static class DemoUi
    {
        // ------------------------------------------------------------------ colours
        //
        // Every value below is the one the dressed ledger prints in, copied rather
        // than referenced (LedgerPalette is LivingCity's, and this scene keeps its
        // own): Ink is its Phosphor, InkDim its PhosphorDim, Gold its Amber, Panel
        // its CardFace, Accent the pack's AccentTint and KeyFace the pack's FaceTint
        // that its tab strip wears. Move one and move the other.

        /// <summary>Data at full beam - the clock, a unit's name, anything read.</summary>
        public static readonly Color Ink = new Color(0.90f, 0.95f, 1f);

        /// <summary>Chrome at half beam - labels, captions, dividers.</summary>
        public static readonly Color InkDim = new Color(0.52f, 0.66f, 0.80f);

        /// <summary>The pack's powder blue: highlights, rules, the live accent.</summary>
        public static readonly Color Accent = new Color(0.557f, 0.835f, 0.992f);

        /// <summary>"Look here" - a held clock, the building card's own highlight.</summary>
        public static readonly Color Gold = new Color(1f, 0.78f, 0.32f);

        /// <summary>A floating panel's face - one lit step above the world.</summary>
        public static readonly Color Panel = new Color(0.045f, 0.095f, 0.145f, 0.96f);

        /// <summary>The top strip's own floor - a shade lighter than a panel so the
        /// bar reads as furniture bolted to the screen, not a window over it.</summary>
        public static readonly Color BarFace = new Color(0.055f, 0.115f, 0.175f, 0.94f);

        /// <summary>A soft-key's face - the pack's own menu-item blue, the exact
        /// value the ledger's tab strip wears.</summary>
        public static readonly Color KeyFace = new Color(0.10f, 0.35f, 0.58f);

        /// <summary>A readout's well - the ledger's page ground, sunk into the bar as
        /// a key-shaped chip, so a figure you can only read never looks like a key
        /// you can press.</summary>
        public static readonly Color Well = new Color(0.028f, 0.055f, 0.085f, 0.95f);

        /// <summary>Key tint states. These MULTIPLY the face through the
        /// CanvasRenderer, so normal sits dimmed and hover IS the full face.</summary>
        public static readonly Color KeyNormal = new Color(0.80f, 0.80f, 0.80f);
        public static readonly Color KeyHover = Color.white;
        public static readonly Color KeyPressed = new Color(0.55f, 0.55f, 0.55f);

        // ------------------------------------------------------------------ the pack

        const string Pack = "Assets/Synty/InterfaceModernMenus/";
        const string Chrome = Pack + "Sprites/ModernMenus/";
        const string General = Pack + "Sprites/General/";
        const string Flat = Pack + "Sprites/Icons_ModernMenus_Flat/";
        const string Fonts = Pack + "Fonts/";

        /// <summary>A white vertical alpha ramp, clear at the top and solid at the
        /// bottom, uniform across its width - the glow the top strip lifts toward its
        /// accent rule. The pack's own Menu_Bar slab is NOT used for that strip: its
        /// right end is a hard diagonal and its 9-slice keeps 750 of 1024 pixels in
        /// the right border, so a full-width bar puts a sloping cut exactly where the
        /// transport keys sit.</summary>
        public static Sprite Gradient => Slot(ref gradient,
            General + "SPR_ModernMenus_Menu_Gradient_Vertical_01.png");

        /// <summary>The flat chip every soft-key and readout in the demo wears - the
        /// pack's toolbar species, deliberately not its thick action slab.</summary>
        public static Sprite Chip => Slot(ref chip, Chrome + "SPR_ModernMenus_Button_08.png");

        /// <summary>The framed box behind a floating popup - the same panel the
        /// ledger's detail card and sort menu wear.</summary>
        public static Sprite Box => Slot(ref box,
            Chrome + "SPR_ModernMenus_Frame_Box_Medium_01_Background.png");

        /// <summary>Icons: the flat "Clean" cut, authored white so a tint IS the
        /// colour. There is no pause glyph in the pack - the bar draws its own.</summary>
        public static Sprite IconTimer => Slot(ref iconTimer, Flat + "ICON_ModernMenus_Timer_01_Clean.png");
        public static Sprite IconPlay => Slot(ref iconPlay, Flat + "ICON_ModernMenus_Play_01_Clean.png");
        public static Sprite IconFaster => Slot(ref iconFaster, Flat + "ICON_ModernMenus_FastForward_01_Clean.png");

        /// <summary>The soft glow dot the world markers ride on.</summary>
        public static Sprite Dot => Slot(ref dot, Pack + "Sprites/FX/SPR_ModernMenus_FX_Glow_Dot_01.png");

        /// <summary>The display face: the clock, a popup's title.</summary>
        public static TMP_FontAsset Headline => FontSlot(ref headline,
            Fonts + "SairaCondensed/SairaCondensed-ExtraBold SDF.asset");

        /// <summary>Everything else the demo prints.</summary>
        public static TMP_FontAsset Body => FontSlot(ref body,
            Fonts + "BarlowCondensed/BarlowCondensed-Medium SDF.asset");

        static Sprite gradient, chip, box, iconTimer, iconPlay, iconFaster, dot;
        static TMP_FontAsset headline, body;
        static bool warned;

        // Static state outlives Play when domain reload is off, and a re-import can
        // leave a stale reference behind - the same guard LedgerSkinSet keeps.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            gradient = chip = box = iconTimer = iconPlay = iconFaster = dot = null;
            headline = body = null;
            warned = false;
        }

        static Sprite Slot(ref Sprite cached, string path)
        {
            if (cached)
                return cached;
#if UNITY_EDITOR
            cached = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
#endif
            if (!cached)
                Warn();
            return cached;
        }

        static TMP_FontAsset FontSlot(ref TMP_FontAsset cached, string path)
        {
            if (cached)
                return cached;
#if UNITY_EDITOR
            cached = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#endif
            if (!cached)
                Warn();
            return cached;
        }

        static void Warn()
        {
            if (warned)
                return;
            warned = true;
            Debug.LogWarning("[RoadDemo] Interface Modern Menus art is missing - the " +
                             "demo's screens fall back to flat blocks and the default " +
                             "TMP face.");
        }

        // ------------------------------------------------------------------ elements

        /// <summary>A rect centred on its parent with an explicit anchor set - a raw
        /// RectTransform's defaults are easy to misremember, so every rect this
        /// wardrobe hands out starts from one stated position and callers move it.
        /// </summary>
        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>A flat block of colour - the fallback material of every screen
        /// here, and the only way the demo draws a rule or a pause bar.</summary>
        public static Image Block(Transform parent, string name, Color colour)
        {
            var image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Puts a pack sprite on an Image as 9-sliced chrome. The caller says how
        /// thick the drawn rim should LOOK in reference units and the multiplier is
        /// derived from the sprite's own authored border - the pack cuts its borders
        /// for roughly 500-unit controls, and a 60px rim on a 34-unit key would
        /// otherwise swallow the key whole. A sprite-less call leaves the flat tint,
        /// which is exactly the no-pack fallback.
        /// </summary>
        public static Image Dress(Image image, Sprite sprite, float drawnRim, Color tint)
        {
            if (!image)
                return image;

            image.color = tint;
            if (sprite == null)
                return image;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            var edge = sprite.border;
            var thickest = Mathf.Max(Mathf.Max(edge.x, edge.y), Mathf.Max(edge.z, edge.w));
            image.pixelsPerUnitMultiplier =
                thickest > 0f ? Mathf.Max(1f, thickest / Mathf.Max(drawnRim, 1f)) : 1f;
            return image;
        }

        /// <summary>A tinted pack icon at a square size, centred in its parent.</summary>
        public static Image Icon(Transform parent, string name, Sprite sprite, float size,
            Color tint)
        {
            var image = Block(parent, name, tint);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, float size,
            Color colour, TextAlignmentOptions alignment, bool display = false)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            var face = display ? Headline : Body;
            if (face)
                text.font = face;
            return text;
        }

        /// <summary>The demo's one hover/press behaviour, so every key in the scene
        /// answers the pointer the same way.</summary>
        public static void TintStates(Button button, Graphic face)
        {
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = KeyNormal;
            colours.highlightedColor = KeyHover;
            colours.selectedColor = KeyNormal;
            colours.pressedColor = KeyPressed;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colours;
        }

        /// <summary>Stretches a rect over its whole parent, optionally inset.</summary>
        public static void Fill(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
