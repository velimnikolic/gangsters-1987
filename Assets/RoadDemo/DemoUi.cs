using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>Shared city HUD primitives backed by the game's Ledger and Turf art.</summary>
    public static class DemoUi
    {
        // ------------------------------------------------------------------ colours
        //
        // The city HUD palette the demo's own instruments print in: a deep navy
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

        public static Sprite Chip => LivingCity.UI.LedgerStyle.RoundedSmall;
        public static Sprite Box => LivingCity.UI.LedgerStyle.Rounded;
        public static Sprite Dot => LivingCity.UI.LedgerStyle.Disc;
        public static Sprite IconArrow => TurfGlyphs.Arrow;
        public static Sprite IconBack => TurfGlyphs.Back;
        public static Sprite IconCombat => TurfGlyphs.Combat;
        public static Sprite IconChat => TurfGlyphs.Chat;
        public static Sprite IconDeath => TurfGlyphs.Death;
        public static Sprite IconPlus => TurfGlyphs.Plus;
        public static Sprite IconShop => TurfGlyphs.House;
        public static TMP_FontAsset Headline => LivingCity.UI.LedgerStyle.Condensed;
        public static TMP_FontAsset Body => LivingCity.UI.LedgerStyle.CondensedText;

        static GameObject _carGlyph;
        public static GameObject CarGlyph
        {
            get
            {
#if UNITY_EDITOR
                if (!_carGlyph)
                    _carGlyph = DemoAssetLoad.Load<GameObject>(
                        "Assets/Synty/PolygonIcons/Prefabs/SM_Icon_Car_01.prefab");
#endif
                return _carGlyph;
            }
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
