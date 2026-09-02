using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The road demo's wardrobe: one place that owns what every screen in this scene
    /// is made of - the Synty INTERFACE Modern Menus sprites, the book's own condensed
    /// gothic, and the colour names the demo prints in.
    ///
    /// Demo-local ON PURPOSE. RoadDemo does not borrow LivingCity's runtime (it has
    /// its own clock, sky, lamps and headlights), so it dresses itself from the
    /// Modern Menus pack folder here. The ledger the demo installs no longer shares
    /// this scheme - it is a paper book on a desk now (see LedgerStyle) - so the top
    /// bar and the map read as the demo's instruments, the folder as the boss's.
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
        // The Modern Menus scheme the demo's own instruments print in: a deep navy
        // tube, ice-white data, steel chrome, powder-blue highlights and a gold
        // warning gun. (The ledger used to share it; it wears paper now.)

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

        /// <summary>The same navy, meant to be MULTIPLIED with the picture behind it
        /// rather than laid over it: a panel over the street darkens the street to its
        /// own colour instead of hiding it. Only right with <see cref="Multiply"/>.</summary>
        public static readonly Color PanelShade = new Color(0.34f, 0.44f, 0.62f, 1f);

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
        const string Icons = "Assets/Synty/PolygonIcons/Prefabs/";

        /// <summary>A white vertical alpha ramp, clear at the top and solid at the
        /// bottom, uniform across its width - the glow the top strip lifts toward its
        /// accent rule. The pack's own Menu_Bar slab is NOT used for that strip: its
        /// right end is a hard diagonal and its 9-slice keeps 750 of 1024 pixels in
        /// the right border, so a full-width bar puts a sloping cut exactly where the
        /// transport keys sit.</summary>
        public static Sprite Gradient => Slot(ref _gradient,
            General + "SPR_ModernMenus_Menu_Gradient_Vertical_01.png");

        /// <summary>The flat chip every soft-key and readout in the demo wears - the
        /// pack's toolbar species, deliberately not its thick action slab.</summary>
        public static Sprite Chip => Slot(ref _chip, Chrome + "SPR_ModernMenus_Button_08.png");

        /// <summary>The framed box behind a floating popup - the same panel the
        /// ledger's detail card and sort menu wear.</summary>
        public static Sprite Box => Slot(ref _box,
            Chrome + "SPR_ModernMenus_Frame_Box_Medium_01_Background.png");

        /// <summary>Icons: the flat "Clean" cut, authored white so a tint IS the
        /// colour. There is no pause glyph in the pack - the bar draws its own.</summary>
        public static Sprite IconTimer => Slot(ref _iconTimer, Flat + "ICON_ModernMenus_Timer_01_Clean.png");
        public static Sprite IconPlay => Slot(ref _iconPlay, Flat + "ICON_ModernMenus_Play_01_Clean.png");
        public static Sprite IconFaster => Slot(ref _iconFaster, Flat + "ICON_ModernMenus_FastForward_01_Clean.png");

        /// <summary>The crews' activity glyphs - on the move, in a fight, in a word,
        /// down - and the recruit slot's plus. Same flat white cut, tinted in place.</summary>
        public static Sprite IconArrow => Slot(ref _iconArrow, Flat + "ICON_ModernMenus_Arrow_01_Clean.png");
        public static Sprite IconCombat => Slot(ref _iconCombat, Flat + "ICON_ModernMenus_Combat_01_Clean.png");
        public static Sprite IconChat => Slot(ref _iconChat, Flat + "ICON_ModernMenus_Chat_01_Clean.png");
        public static Sprite IconDeath => Slot(ref _iconDeath, Flat + "ICON_ModernMenus_Death_01_Clean.png");
        public static Sprite IconPlus => Slot(ref _iconPlus, Flat + "ICON_ModernMenus_Plus_01_Clean.png");

        /// <summary>The car hint's glyphs: the back arrow for "get out", the plain one for "get in".</summary>
        public static Sprite IconBack => Slot(ref _iconBack, Flat + "ICON_ModernMenus_Arrow_Back_01_Clean.png");

        /// <summary>The shop over the outfit's own door - the crew bar's key to the
        /// front. A storefront and not a house, because that is what a front IS: the
        /// premises the street sees, with the family behind it.</summary>
        public static Sprite IconShop => Slot(ref _iconShop, Flat + "ICON_ModernMenus_Shop_01_Clean.png");

        /// <summary>The soft glow dot the world markers ride on.</summary>
        public static Sprite Dot => Slot(ref _dot, Pack + "Sprites/FX/SPR_ModernMenus_FX_Glow_Dot_01.png");

        /// <summary>The little car the crew bar's key wears. Modern Menus has no
        /// vehicle glyph, so it comes out of Synty's icon pack - a model, not a
        /// sprite, printed dead straight on by PortraitStudio like every other object
        /// the screens show.</summary>
        public static GameObject CarGlyph => ModelSlot(ref _carGlyph, Icons + "SM_Icon_Car_01.prefab");

        /// <summary>The display face: the clock, a popup's title. The type is the one
        /// exception to the demo-local rule below, and for two reasons: the pack's faces
        /// are 2010s screen gothics on a 1987 instrument, and the pack's SDF assets load
        /// through AssetDatabase, so in a player build the screens would print in TMP's
        /// default Arial clone. LedgerStyle builds its faces from Resources at runtime,
        /// which is period-correct and survives a build.</summary>
        public static TMP_FontAsset Headline => LivingCity.UI.LedgerStyle.Condensed;

        /// <summary>Everything else the demo prints - the same gothic, reading weight.</summary>
        public static TMP_FontAsset Body => LivingCity.UI.LedgerStyle.CondensedText;

        static Sprite _gradient, _chip, _box, _iconTimer, _iconPlay, _iconFaster, _dot;
        static Sprite _iconArrow, _iconCombat, _iconChat, _iconDeath, _iconPlus, _iconBack, _iconShop;
        static GameObject _carGlyph;
        static bool _warned;

        // Static state outlives Play when domain reload is off, and a re-import can
        // leave a stale reference behind - the same guard LedgerStyle keeps.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            _gradient = _chip = _box = _iconTimer = _iconPlay = _iconFaster = _dot = null;
            _iconArrow = _iconCombat = _iconChat = _iconDeath = _iconPlus = _iconBack = null;
            _iconShop = null;
            _carGlyph = null;
            _warned = false;
        }

        static Sprite Slot(ref Sprite cached, string path)
        {
            if (cached)
                return cached;
#if UNITY_EDITOR
            cached = RoadDemo.DemoAssetLoad.Load<Sprite>(path);
#endif
            if (!cached)
                Warn();
            return cached;
        }

        static GameObject ModelSlot(ref GameObject cached, string path)
        {
            if (cached)
                return cached;
#if UNITY_EDITOR
            cached = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
#endif
            if (!cached)
                Warn();
            return cached;
        }

        static void Warn()
        {
            if (_warned)
                return;
            _warned = true;
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

        /// <summary>A dropdown in the demo's own paint, built from nothing.
        ///
        /// TMP_Dropdown will not assemble itself: it wants a caption, and a TEMPLATE -
        /// a scroll view whose one item is cloned per option - handed to it before it
        /// is any use, and it wants the template's GameObject switched off so the list
        /// only exists while it is open. All of that is here once so a bench that needs
        /// a list of takes, seeds or bodies does not build the same eight objects again.
        ///
        /// A dropdown is the one thing in these scenes that must be CLICKED, so unlike
        /// every other helper here its parts stay raycast targets and the caller has to
        /// have put an EventSystem up.</summary>
        public static TMP_Dropdown Dropdown(Transform parent, string name, float width,
            float rowHeight = 26f, int visibleRows = 12)
        {
            var root = NewRect(name, parent);
            root.sizeDelta = new Vector2(width, rowHeight + 6f);
            var face = root.gameObject.AddComponent<Image>();
            face.color = Panel;
            var dropdown = root.gameObject.AddComponent<TMP_Dropdown>();

            var caption = Text(root, "Label", rowHeight * 0.62f, Ink, TextAlignmentOptions.Left);
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(1f, 1f);
            captionRect.offsetMin = new Vector2(10f, 0f);
            captionRect.offsetMax = new Vector2(-24f, 0f);

            // ---- the template: scroll view, viewport, content, one item
            var template = NewRect("Template", root);
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, 2f);
            template.sizeDelta = new Vector2(0f, rowHeight * visibleRows);
            var templateFace = template.gameObject.AddComponent<Image>();
            templateFace.color = Well;
            var scroll = template.gameObject.AddComponent<ScrollRect>();

            var viewport = NewRect("Viewport", template);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0f, 1f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportFace = viewport.gameObject.AddComponent<Image>();
            viewportFace.color = new Color(1f, 1f, 1f, 0.004f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, rowHeight);

            var item = NewRect("Item", content);
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(0f, rowHeight);
            var toggle = item.gameObject.AddComponent<Toggle>();

            var itemFace = Block(item, "Item Background", new Color(0f, 0f, 0f, 0f));
            itemFace.raycastTarget = true;
            var itemFaceRect = itemFace.rectTransform;
            itemFaceRect.anchorMin = Vector2.zero;
            itemFaceRect.anchorMax = Vector2.one;
            itemFaceRect.offsetMin = Vector2.zero;
            itemFaceRect.offsetMax = Vector2.zero;

            var tick = Block(item, "Item Checkmark", KeyFace);
            var tickRect = tick.rectTransform;
            tickRect.anchorMin = Vector2.zero;
            tickRect.anchorMax = Vector2.one;
            tickRect.offsetMin = Vector2.zero;
            tickRect.offsetMax = Vector2.zero;

            var itemText = Text(item, "Item Label", rowHeight * 0.60f, Ink,
                TextAlignmentOptions.Left);
            var itemTextRect = itemText.rectTransform;
            itemTextRect.anchorMin = Vector2.zero;
            itemTextRect.anchorMax = Vector2.one;
            itemTextRect.offsetMin = new Vector2(10f, 0f);
            itemTextRect.offsetMax = new Vector2(-6f, 0f);

            toggle.targetGraphic = itemFace;
            toggle.graphic = tick;
            toggle.isOn = true;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = rowHeight;

            dropdown.targetGraphic = face;
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = itemText;
            template.gameObject.SetActive(false);
            return dropdown;
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

        /// <summary>The material that makes an Image multiply with what is behind it
        /// (Photoshop's multiply layer) instead of covering it - one for all of them.
        /// Null when the shader is not in the project, and the caller keeps flat paint.</summary>
        static Material _multiply;
        public static Material Multiply
        {
            get
            {
                if (_multiply != null) return _multiply;
                var shader = Shader.Find("RoadDemo/UI Multiply");
                if (shader == null) return null;
                _multiply = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return _multiply;
            }
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
