using System.Collections.Generic;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RoadDemo
{
    // The outfit across the top of the screen, always: one block per lieutenant,
    // in the book's order, the way the old gang games kept the squads in view.
    // A block is the lieutenant LIVE - a small camera rides in front of him and
    // its picture sits in the block, so what he does on the street he does in the
    // block: walks in place, turns, raises the gun, talks, falls - a feed, not a
    // photograph - with the sign of what he is doing in the feed's corner (the
    // same CrewGlyphs sign that rides over him on the map), and beside the feed
    // his name, the gun he carries (a print of the actual piece, out of the same
    // studio as the ledger's armory, with its name), and his hoods in a row of
    // small mugshots (Crew.MaxHoods). No health bars: a wounded man's picture
    // goes amber, a man one hit from the ground goes red, a dead man's goes dark
    // under the skull. An empty chip is a dim plus waiting on a recruit. The
    // selected crew's block wears the gold rim; a click on a block selects that
    // crew, the same as clicking the man. Dressed out of DemoUi like every other
    // screen in the demo.
    //
    // A crew the book has given a car wears its keys at the end of the name line -
    // a little car glyph: click and they walk to the doors and get in, click again
    // on the way and the walk is called off, and once they are aboard the glyph is
    // the arrow back out - click and the car pulls in and lets them down.
    //
    // The feeds are cheap on purpose: a low-res target each, no shadows, no post,
    // and the cameras take turns - one renders per frame - so a bar of four crews
    // costs one small extra render a frame, not four.
    public class CrewBar : MonoBehaviour
    {
        public static CrewBar Instance { get; private set; }

        const int MaxHoods = LivingCity.Personnel.Crew.MaxHoods;
        const float BlockWidth = 180f, BlockHeight = 72f, Gap = 6f;
        const float Pad = 6f;                          // the block's margin, all four sides
        const float FeedSize = BlockHeight - 2f * Pad; // the square the man stands in, whole
        const float ColumnGap = 8f;                    // feed to the right column
        const float RowGap = 3f;                       // between the column's rows
        const float NameHeight = 15f;                  // row one: his name
        const float ArmHeight = 13f;                   // row two: the gun, side-on, and its name
        const float ArmPrintWidth = 32f;
        const float ChipGap = 2f;
        // The right column is one rectangle the height of the feed: the name across
        // its top, the gun under it, the hoods in one row along the bottom, the row
        // ending flush with the feed's bottom and the block's right margin -
        // nothing left ragged.
        const float ColumnX = Pad + FeedSize + ColumnGap;
        const float ColumnWidth = BlockWidth - ColumnX - Pad;
        const float ArmTop = Pad + NameHeight + RowGap;
        const float ChipTop = ArmTop + ArmHeight + RowGap;
        const float ChipHeight = BlockHeight - Pad - ChipTop;
        const float ChipWidth = (ColumnWidth - (MaxHoods - 1) * ChipGap) / MaxHoods;
        const float SignSize = 16f;                    // the activity sign's badge in the feed
        // The wheels: a crew the book has given a car wears a key at the end of its
        // name line - the icon pack's little car, pressed to send them to the doors;
        // once they are in, the key turns into the arrow back out. The line keeps the
        // room for it whether the crew has a car or not, so a name never jumps when
        // the book hands the keys over.
        const float CarKeyWidth = 19f;
        const float CarKeyGap = 3f;
        const float NameWidth = ColumnWidth - CarKeyWidth - CarKeyGap;
        // A bust print (PortraitStudio) is square and puts the head about this far
        // up it; a chip that is not square shows a window of the print, not a
        // squashed print, and the window is centred on the head.
        const float BustHeadLine = 0.70f;
        const int FeedPixels = 128;

        /// <summary>The layer the feed cameras see and nothing else: a lieutenant's
        /// body (and the gun in his hand) is put on it, so his picture is him alone
        /// on a plain ground - no street behind, no other man walking through.
        /// Unnamed in the layer table (29; PortraitStudio holds 31, the strategic map
        /// 30) and rendered by the main camera like any other layer.</summary>
        public const int FeedLayer = 29;
        const int SortingOrder = 22; // over the road demo's top bar (20), under its map (30)

        static readonly Color Wounded = new Color(1f, 0.78f, 0.20f, 0.42f);
        static readonly Color Critical = new Color(1f, 0.25f, 0.20f, 0.5f);
        static readonly Color DeadShade = new Color(0.02f, 0.02f, 0.03f, 0.8f);
        static readonly Color Skull = new Color(1f, 0.36f, 0.30f, 0.95f);
        static readonly Color ChipFace = new Color(0.02f, 0.045f, 0.07f, 1f);
        static readonly Color Refused = new Color(1f, 0.36f, 0.30f, 0.95f);
        static readonly Color FeedBackdrop = new Color(0.10f, 0.13f, 0.17f, 1f);

        sealed class Chip
        {
            public RectTransform Rect;
            public RawImage Portrait;
            public Image Plus, Shade, Skull;
            public CrewWalker Man;
            public float RefusedUntil; // the plus flashes red after a refused recruit
        }

        sealed class Block
        {
            public RectTransform Rect;
            public Image Frame, Rim, Shade, Skull;
            public RawImage Feed;
            public Image SignBack, Sign;   // what he is doing, in the feed's corner
            public Vector2 SignAt;
            public TMP_Text Name;
            public RawImage Arm;           // the gun he carries, and its name
            public TMP_Text ArmLabel;
            public GameObject ArmPrefab;
            public bool ArmKnown;
            public RectTransform CarKey;   // the keys, when the book has given the crew a car
            public Image CarFace, CarArrow;
            public RawImage CarGlyph;      // the icon pack's car, printed
            public bool CarAsked;          // the print is asked for once, not once a frame
            public float CarRefusedUntil;  // the key flashes red on a refused order
            public Chip[] Chips;
            public DemoCrews.Unit Unit;
            public CrewWalker Boss;
            public float Phase;

            // the live picture
            public Camera Cam;
            public RenderTexture Target;
            public float CamYaw = float.NaN;
        }

        DemoCrews _crews;
        Canvas _canvas;
        RectTransform _row;
        Transform _cameraRoot;
        readonly List<Block> _blocks = new List<Block>();
        readonly List<DemoCrews.Unit> _shown = new List<DemoCrews.Unit>();
        float _topInset;
        bool _fonts;
        int _turn;

        public void Init(DemoCrews crews, float topInset)
        {
            Instance = this;
            _crews = crews;
            _topInset = topInset;
            _fonts = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;

            var root = new GameObject("Crew Bar", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _row = DemoUi.NewRect("Row", root.transform);
            _row.anchorMin = _row.anchorMax = new Vector2(0.5f, 1f);
            _row.pivot = new Vector2(0.5f, 1f);
            _row.anchoredPosition = new Vector2(0f, -_topInset);
            _row.sizeDelta = new Vector2(0f, BlockHeight);

            _cameraRoot = new GameObject("Crew Feeds").transform;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var block in _blocks)
                if (block.Target != null) block.Target.Release();
        }

        /// <summary>Is this screen point on the bar? The street pickers stand down for
        /// it - a click on a block is the bar's, not the ground's under it.</summary>
        public bool Contains(Vector2 screen)
        {
            if (_row == null || !_row.gameObject.activeInHierarchy) return false;
            foreach (var block in _blocks)
                if (block.Rect.gameObject.activeSelf &&
                    RectTransformUtility.RectangleContainsScreenPoint(block.Rect, screen))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ chrome

        Block BuildBlock()
        {
            var block = new Block { Phase = Random.value * 6.28f };
            var rect = DemoUi.NewRect("Crew", _row);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(BlockWidth, BlockHeight);
            block.Rect = rect;

            block.Frame = rect.gameObject.AddComponent<Image>();
            block.Frame.raycastTarget = false;
            DemoUi.Dress(block.Frame, DemoUi.Box, 12f, DemoUi.Panel);

            block.Rim = DemoUi.Block(rect, "Rim", DemoUi.Gold);
            DemoUi.Fill(block.Rim.rectTransform, 2f);
            block.Rim.enabled = false;
            var face = DemoUi.Block(rect, "Face", DemoUi.Well);
            DemoUi.Fill(face.rectTransform, 4f);

            // the feed, left, square; its condition tint and skull over it
            var feedRect = DemoUi.NewRect("Feed", rect);
            feedRect.anchorMin = feedRect.anchorMax = new Vector2(0f, 0.5f);
            feedRect.pivot = new Vector2(0f, 0.5f);
            feedRect.anchoredPosition = new Vector2(Pad, 0f);
            feedRect.sizeDelta = new Vector2(FeedSize, FeedSize);
            block.Feed = feedRect.gameObject.AddComponent<RawImage>();
            block.Feed.raycastTarget = false;
            block.Feed.color = Color.white;
            block.Feed.enabled = false;

            block.Shade = DemoUi.Block(feedRect, "Shade", Wounded);
            DemoUi.Fill(block.Shade.rectTransform);
            block.Shade.enabled = false;
            block.Skull = DemoUi.Icon(feedRect, "Skull", DemoUi.IconDeath, 26f, Skull);
            block.Skull.enabled = false;

            // the sign of what he is doing, bottom-left of the feed on a small dark
            // badge so it reads over the picture; nothing while he simply stands
            block.SignBack = DemoUi.Block(feedRect, "Sign", DemoUi.Panel);
            DemoUi.Dress(block.SignBack, DemoUi.Chip, 3f, DemoUi.Panel);
            var badge = block.SignBack.rectTransform;
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0f);
            badge.pivot = new Vector2(0f, 0f);
            badge.anchoredPosition = new Vector2(2f, 2f);
            badge.sizeDelta = new Vector2(SignSize, SignSize);
            block.SignBack.enabled = false;
            block.Sign = DemoUi.Icon(badge, "Glyph", null, SignSize - 5f, Color.clear);
            block.SignAt = block.Sign.rectTransform.anchoredPosition;
            block.Sign.enabled = false;

            // the right column: his name on the top line, the gun he carries under
            // it, his hoods in a row along the bottom
            if (_fonts)
            {
                block.Name = DemoUi.Text(rect, "Name", 11.5f, DemoUi.Ink, TextAlignmentOptions.Left, display: true);
                block.Name.characterSpacing = 1f;
                var nr = block.Name.rectTransform;
                nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                nr.pivot = new Vector2(0f, 1f);
                nr.anchoredPosition = new Vector2(ColumnX, -Pad);
                nr.sizeDelta = new Vector2(NameWidth, NameHeight);
            }

            // the gun: the studio's side-on print of the piece is square with the
            // gun across its middle, so the row shows that middle band, unstretched
            var armRect = DemoUi.NewRect("Arm", rect);
            armRect.anchorMin = armRect.anchorMax = new Vector2(0f, 1f);
            armRect.pivot = new Vector2(0f, 1f);
            armRect.anchoredPosition = new Vector2(ColumnX, -ArmTop);
            armRect.sizeDelta = new Vector2(ArmPrintWidth, ArmHeight);
            block.Arm = armRect.gameObject.AddComponent<RawImage>();
            block.Arm.raycastTarget = false;
            float band = ArmHeight / ArmPrintWidth;
            block.Arm.uvRect = new Rect(0f, (1f - band) * 0.5f, 1f, band);
            block.Arm.enabled = false;

            if (_fonts)
            {
                block.ArmLabel = DemoUi.Text(rect, "Arm Name", 9f, DemoUi.InkDim, TextAlignmentOptions.Left);
                block.ArmLabel.characterSpacing = 0.5f;
                block.ArmLabel.overflowMode = TextOverflowModes.Ellipsis;
                var ar = block.ArmLabel.rectTransform;
                ar.anchorMin = ar.anchorMax = new Vector2(0f, 1f);
                ar.pivot = new Vector2(0f, 1f);
                float labelX = ColumnX + ArmPrintWidth + 4f;
                ar.anchoredPosition = new Vector2(labelX, -ArmTop);
                ar.sizeDelta = new Vector2(BlockWidth - Pad - labelX, ArmHeight);
            }

            block.Chips = new Chip[MaxHoods];
            for (int k = 0; k < MaxHoods; k++)
                block.Chips[k] = BuildChip(rect,
                    new Vector2(ColumnX + k * (ChipWidth + ChipGap), -ChipTop));

            BuildCarKey(block, rect);

            // the camera behind the feed: low-res, no shadows, no post, off until its turn
            block.Target = new RenderTexture(FeedPixels, FeedPixels, 16, RenderTextureFormat.ARGB32)
                { name = "Crew Feed" };
            var camGo = new GameObject("Feed Camera");
            camGo.transform.SetParent(_cameraRoot, false);
            block.Cam = camGo.AddComponent<Camera>();
            block.Cam.targetTexture = block.Target;
            block.Cam.fieldOfView = 30f;
            block.Cam.cullingMask = 1 << FeedLayer;
            block.Cam.nearClipPlane = 0.2f;
            block.Cam.farClipPlane = 60f;
            block.Cam.clearFlags = CameraClearFlags.SolidColor;
            block.Cam.backgroundColor = FeedBackdrop;
            block.Cam.depth = -50f;
            block.Cam.enabled = false;
            var data = block.Cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            block.Feed.texture = block.Target;

            return block;
        }

        // The car key, hard against the right end of the name line: the icon pack's
        // little car while they are on the pavement, the back arrow while they are
        // sat in it - the same arrow the hint over the car in the street shows for
        // GET OUT. A chip under it so it reads as something to press, and the whole
        // key hidden on a crew the book has given no car.
        void BuildCarKey(Block block, RectTransform parent)
        {
            var key = DemoUi.NewRect("Car", parent);
            key.anchorMin = key.anchorMax = new Vector2(0f, 1f);
            key.pivot = new Vector2(0f, 1f);
            key.anchoredPosition = new Vector2(ColumnX + NameWidth + CarKeyGap, -Pad);
            key.sizeDelta = new Vector2(CarKeyWidth, NameHeight);
            block.CarKey = key;

            block.CarFace = key.gameObject.AddComponent<Image>();
            block.CarFace.raycastTarget = false;
            DemoUi.Dress(block.CarFace, DemoUi.Chip, 4f, ChipFace);

            // the print is square with the glyph in the middle of it, so the key shows
            // the band across that middle - the car full width, the air above cropped
            block.CarGlyph = DemoUi.NewRect("Glyph", key).gameObject.AddComponent<RawImage>();
            block.CarGlyph.raycastTarget = false;
            DemoUi.Fill(block.CarGlyph.rectTransform, 1.5f);
            float band = (NameHeight - 3f) / (CarKeyWidth - 3f);
            block.CarGlyph.uvRect = new Rect(0f, (1f - band) * 0.5f, 1f, band);
            block.CarGlyph.enabled = false;

            block.CarArrow = DemoUi.Icon(key, "Out", DemoUi.IconBack, NameHeight - 5f, DemoUi.Gold);
            block.CarArrow.enabled = false;

            key.gameObject.SetActive(false);
        }

        Chip BuildChip(RectTransform parent, Vector2 at)
        {
            var chip = new Chip();
            var rect = DemoUi.NewRect("Hood", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(ChipWidth, ChipHeight);
            chip.Rect = rect;

            var face = rect.gameObject.AddComponent<Image>();
            face.color = ChipFace;
            face.raycastTarget = false;

            chip.Portrait = DemoUi.NewRect("Portrait", rect).gameObject.AddComponent<RawImage>();
            chip.Portrait.raycastTarget = false;
            DemoUi.Fill(chip.Portrait.rectTransform, 1f);
            chip.Portrait.uvRect = BustWindow(ChipWidth - 2f, ChipHeight - 2f);
            chip.Portrait.enabled = false;

            chip.Plus = DemoUi.Icon(rect, "Plus", DemoUi.IconPlus, Mathf.Round(ChipHeight * 0.4f),
                new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.35f));

            chip.Shade = DemoUi.Block(rect, "Shade", Wounded);
            DemoUi.Fill(chip.Shade.rectTransform);
            chip.Shade.enabled = false;
            chip.Skull = DemoUi.Icon(rect, "Skull", DemoUi.IconDeath, Mathf.Round(ChipHeight * 0.55f), Skull);
            chip.Skull.enabled = false;
            return chip;
        }

        /// <summary>The window of a square bust print a chip of this shape shows, in
        /// UV: the print's full width when the chip is wider than tall (a strip
        /// across the head, centred on it, the chest below cropped away), its full
        /// height when taller (a strip down the middle). Never a stretch.</summary>
        static Rect BustWindow(float width, float height)
        {
            if (width >= height)
            {
                float h = height / Mathf.Max(width, 1f);
                float y = Mathf.Min(Mathf.Max(BustHeadLine - h * 0.5f, 0f), 1f - h);
                return new Rect(0f, y, 1f, h);
            }
            float w = width / Mathf.Max(height, 1f);
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            if (_crews == null || _row == null) return;
            bool show = !PersonnelAlmanac.IsOpen;
            if (_row.gameObject.activeSelf != show) _row.gameObject.SetActive(show);

            // the outfit's crews, in book order; blocks pooled and re-bound
            _shown.Clear();
            foreach (var unit in _crews.Units)
                if (unit.Faction == 0) _shown.Add(unit);
            while (_blocks.Count < _shown.Count) _blocks.Add(BuildBlock());
            _row.sizeDelta = new Vector2(_shown.Count * BlockWidth + Mathf.Max(0, _shown.Count - 1) * Gap, BlockHeight);

            var mouse = Mouse.current;
            bool click = show && mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool look = show && mouse != null && mouse.rightButton.wasPressedThisFrame;
            var at = click || look ? mouse.position.ReadValue() : Vector2.zero;

            // the feeds take turns: one camera a frame, round-robin
            _turn = _shown.Count > 0 ? (_turn + 1) % _shown.Count : 0;

            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                bool live = i < _shown.Count;
                if (block.Rect.gameObject.activeSelf != live) block.Rect.gameObject.SetActive(live);
                bool film = live && show && i == _turn;
                if (block.Cam.enabled != film) block.Cam.enabled = film;
                if (!live) continue;
                block.Rect.anchoredPosition = new Vector2(i * (BlockWidth + Gap), 0f);
                var car = _crews.CarOf(_shown[i]);
                Bind(block, _shown[i], car);
                // a right click on a block puts the camera on that crew and leaves it
                // there - it rides him until the player pans the camera off himself
                if (look && RectTransformUtility.RectangleContainsScreenPoint(block.Rect, at))
                    Ride(_shown[i]);
                if (click && RectTransformUtility.RectangleContainsScreenPoint(block.Rect, at))
                {
                    // the car key sends them to the doors, calls the walk off again, and
                    // lets them out once they are in; an empty chip is the recruiting
                    // door - a click on it brings a new man in for that crew; anywhere
                    // else on the block selects the crew
                    bool handled = false;
                    if (car != null && block.CarKey.gameObject.activeSelf &&
                        RectTransformUtility.RectangleContainsScreenPoint(block.CarKey, at))
                    {
                        handled = true;
                        _crews.Select(_shown[i]);
                        if (!OrderCar(_shown[i], car)) block.CarRefusedUntil = Time.unscaledTime + 0.7f;
                    }
                    foreach (var chip in block.Chips)
                    {
                        if (handled) break;
                        if (chip.Man != null || !RectTransformUtility.RectangleContainsScreenPoint(chip.Rect, at)) continue;
                        handled = true;
                        if (!_crews.Recruit(_shown[i])) chip.RefusedUntil = Time.unscaledTime + 0.7f;
                        break;
                    }
                    if (!handled) _crews.Select(_shown[i]);
                }
            }
        }

        /// <summary>What the car key orders for this crew: out of the car when they are
        /// in it, back onto the pavement when they are only walking to it, and into it
        /// otherwise. False - and the key flashes - when the order is refused (nobody
        /// left standing, somebody else's crew already in the seats).</summary>
        bool OrderCar(DemoCrews.Unit unit, CrewCar car)
        {
            if (unit == null || car == null || unit.Wiped) return false;
            // on their way to the doors and not in yet: the same key calls the walk off
            if (unit.Car == null && unit.Boarding == car) return _crews.OrderOut();
            return _crews.OrderCar(car);
        }

        void Bind(Block block, DemoCrews.Unit unit, CrewCar car)
        {
            var boss = unit.Boss;
            if (block.Unit != unit || block.Boss != boss)
            {
                block.Unit = unit;
                block.Boss = boss;
                block.CamYaw = float.NaN;
                block.ArmKnown = false;
                // the feed camera sees its layer only; the man goes on it, gun and all
                if (boss != null && boss.Tf != null) SetLayer(boss.Tf, FeedLayer);
                if (block.Name != null)
                    block.Name.text = FitName(block.Name, boss != null ? boss.DisplayName : unit.Name);
            }

            block.Rim.enabled = _crews.Selected == unit;
            Film(block, boss);
            Condition(boss, block.Shade, block.Skull);
            Signal(block, boss);
            BindArm(block, boss);
            BindCar(block, unit, car);

            for (int k = 0; k < MaxHoods; k++)
                BindChip(block.Chips[k], k < unit.Hoods.Count ? unit.Hoods[k] : null);
        }

        // The sign in the feed's corner: the same glyph, tint and motion the map
        // puts over the man (CrewGlyphs), so bar and street never disagree. Not
        // while he stands (no sign) and not when he is down - the skull over the
        // whole feed says that already.
        static void Signal(Block block, CrewWalker boss)
        {
            var activity = CrewGlyphs.Of(boss);
            var sprite = activity == CrewGlyphs.Activity.Down ? null : CrewGlyphs.SpriteOf(activity);
            bool on = sprite != null;
            if (block.SignBack.enabled != on) block.SignBack.enabled = on;
            if (block.Sign.enabled != on) block.Sign.enabled = on;
            if (!on) return;
            if (block.Sign.sprite != sprite) block.Sign.sprite = sprite;
            CrewGlyphs.Animate(activity, Time.unscaledTime, block.Phase, out var nudge, out float alpha);
            var tint = CrewGlyphs.TintOf(activity);
            tint.a *= alpha;
            block.Sign.color = tint;
            block.Sign.rectTransform.anchoredPosition = block.SignAt + nudge * 0.5f;
        }

        // The gun he carries: a print of the piece itself (PortraitStudio's Item
        // framing, the ledger armory's own picture) and its ledger name; a man with
        // nothing in his hand reads Unarmed, dim. Re-requested only when the piece
        // changes - Arm() swaps prefabs, and a print request per frame is a leak.
        static void BindArm(Block block, CrewWalker boss)
        {
            var prefab = boss != null ? boss.WeaponPrefab : null;
            if (block.ArmKnown && block.ArmPrefab == prefab) return;
            block.ArmKnown = true;
            block.ArmPrefab = prefab;

            block.Arm.enabled = false;
            block.Arm.texture = null;
            if (prefab != null)
                PortraitStudio.Request(prefab, PortraitStudio.Framing.Item, block.Arm);

            if (block.ArmLabel == null) return;
            if (boss == null)
            {
                block.ArmLabel.text = string.Empty;
                return;
            }
            bool armed = prefab != null;
            block.ArmLabel.text = armed ? LedgerText.EquipmentLabel(boss.WeaponKind) : "Unarmed";
            block.ArmLabel.color = armed
                ? DemoUi.InkDim
                : new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.5f);
        }

        // The crew's wheels. The glyph is the icon pack's car printed straight on -
        // one print for the whole bar, whatever body the book gave them - and it turns
        // into the arrow back out the moment they are aboard, so the key always shows
        // what pressing it does rather than what they own. Gold while they are in or
        // on their way, breathing while the walk is on, red for a beat on an order the
        // crew could not take, dim when there is nobody left to give it to.
        static void BindCar(Block block, DemoCrews.Unit unit, CrewCar car)
        {
            bool has = car != null;
            if (block.CarKey.gameObject.activeSelf != has) block.CarKey.gameObject.SetActive(has);
            if (!has) return;

            if (!block.CarAsked)
            {
                block.CarAsked = true;
                PortraitStudio.Request(DemoUi.CarGlyph, PortraitStudio.Framing.Icon, block.CarGlyph);
            }

            bool aboard = unit.Car == car;
            bool boarding = !aboard && unit.Boarding == car;
            bool onTheMove = boarding || (aboard && unit.Leaving);
            bool dead = unit.Wiped;

            // in the car the key is the way out; out of it, the car itself - and the
            // arrow stands in until the print lands
            bool printed = block.CarGlyph.texture != null;
            if (block.CarGlyph.enabled != (printed && !aboard)) block.CarGlyph.enabled = printed && !aboard;
            if (block.CarArrow.enabled != (aboard || !printed)) block.CarArrow.enabled = aboard || !printed;
            var sprite = aboard ? DemoUi.IconBack : DemoUi.IconArrow;
            if (block.CarArrow.sprite != sprite) block.CarArrow.sprite = sprite;

            var tint = Time.unscaledTime < block.CarRefusedUntil ? Refused
                     : dead ? new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.35f)
                     : aboard || boarding ? DemoUi.Gold
                     : DemoUi.Accent;
            if (onTheMove && !dead)
                tint.a *= 0.55f + 0.45f * Mathf.Sin((Time.unscaledTime + block.Phase) * 5f);
            block.CarArrow.color = tint;
            // the printed car is coloured art, so the state rides its tint gently
            block.CarGlyph.color = Time.unscaledTime < block.CarRefusedUntil ? Refused
                                 : dead ? new Color(1f, 1f, 1f, 0.35f)
                                 : Color.white;
            block.CarFace.color = boarding || aboard
                ? new Color(DemoUi.Gold.r * 0.30f, DemoUi.Gold.g * 0.26f, DemoUi.Gold.b * 0.16f, 1f)
                : ChipFace;
        }

        // The camera in front of the man, at chest height, its heading easing after
        // his: when he turns you see him turn, and the picture comes round after -
        // a cameraman keeping up, not a turret bolted to his head. Where he walks the
        // camera walks with him, so in the frame he walks in place.
        void Film(Block block, CrewWalker boss)
        {
            bool on = boss != null && boss.Tf != null;
            if (block.Feed.enabled != on) block.Feed.enabled = on;
            if (!on) return;

            // a gun drawn after the bind lands on the man's layer too
            if (boss.Weapon != null && boss.Weapon.gameObject.layer != FeedLayer) SetLayer(boss.Weapon, FeedLayer);

            var tf = boss.Tf;
            // in the car the feed rides the car instead - the man is out of sight in it
            var car = block.Unit != null ? block.Unit.Car : null;
            if (car != null && car.Tf != null)
            {
                if (car.Tf.gameObject.layer != FeedLayer) SetLayer(car.Tf, FeedLayer);
                float wantCar = car.Tf.eulerAngles.y + 35f;
                if (float.IsNaN(block.CamYaw)) block.CamYaw = wantCar;
                block.CamYaw = Mathf.MoveTowardsAngle(block.CamYaw, wantCar, 110f * Time.unscaledDeltaTime);
                var side = Quaternion.Euler(0f, block.CamYaw, 0f) * Vector3.forward;
                var carEye = car.Position + side * 7.5f + Vector3.up * 2.4f;
                var carLook = car.Position + Vector3.up * 0.7f;
                block.Cam.transform.SetPositionAndRotation(carEye, Quaternion.LookRotation(carLook - carEye, Vector3.up));
                return;
            }

            float want = tf.eulerAngles.y;
            if (float.IsNaN(block.CamYaw)) block.CamYaw = want;
            block.CamYaw = Mathf.MoveTowardsAngle(block.CamYaw, want, 110f * Time.unscaledDeltaTime);
            var ahead = Quaternion.Euler(0f, block.CamYaw, 0f) * Vector3.forward;
            // the whole man in the square: a head-to-toe framing, level with his chest,
            // far enough back for the fall to stay in frame
            var eye = tf.position + ahead * 4.0f + Vector3.up * 1.0f;
            var look = tf.position + Vector3.up * 0.95f;
            block.Cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }

        /// <summary>The man's condition over his picture: amber when he has taken a
        /// hit, red when the next one puts him down, dark and the skull when it has.</summary>
        static void Condition(CrewWalker man, Image shade, Image skull)
        {
            if (man == null)
            {
                shade.enabled = false;
                skull.enabled = false;
                return;
            }
            bool dead = man.Dead;
            bool critical = !dead && man.Health <= 1;
            bool wounded = !dead && !critical && man.Health < man.MaxHealth;
            shade.enabled = dead || critical || wounded;
            if (shade.enabled)
                shade.color = dead ? DeadShade : critical ? Critical : Wounded;
            skull.enabled = dead;
        }

        void BindChip(Chip chip, CrewWalker man)
        {
            if (chip.Man != man)
            {
                chip.Man = man;
                chip.Portrait.enabled = false;
                chip.Portrait.texture = null;
                chip.Plus.enabled = man == null;
                if (man != null && man.SourcePrefab != null)
                    PortraitStudio.Request(man.SourcePrefab, PortraitStudio.Framing.Bust, chip.Portrait);
            }
            Condition(man, chip.Shade, chip.Skull);
            if (man == null)
                chip.Plus.color = Time.unscaledTime < chip.RefusedUntil
                    ? new Color(1f, 0.36f, 0.30f, 0.9f)
                    : new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.35f);
        }

        // The demo camera takes up with the crew and stays with it - the lieutenant,
        // or the car he rides in - down every street it walks, until the player moves
        // the camera himself and takes it back.
        static void Ride(DemoCrews.Unit unit)
        {
            var cam = Object.FindAnyObjectByType<DemoCamera>();
            if (cam == null || unit == null) return;
            cam.Ride(() => Where(unit));
        }

        /// <summary>Where a crew is for the camera: the car while it is riding in one,
        /// else its lieutenant, else the first man of it still on his feet - and null
        /// when there is nobody left, so the camera holds instead of swinging to the
        /// world's origin.</summary>
        static Vector3? Where(DemoCrews.Unit unit)
        {
            if (unit == null) return null;
            if (unit.Car != null && unit.Car.Tf != null) return unit.Car.Position;
            if (unit.Boss != null && unit.Boss.Tf != null) return unit.Boss.Tf.position;
            foreach (var man in unit.All())
                if (man != null && man.Tf != null) return man.Tf.position;
            return null;
        }

        /// <summary>"Bernie Hayes" fits; a name wider than the line, measured in the
        /// label's own face, is trimmed to initial and surname so a block never
        /// wraps or runs under its margin.</summary>
        static string FitName(TMP_Text label, string full)
        {
            if (string.IsNullOrEmpty(full)) return string.Empty;
            if (label.GetPreferredValues(full).x <= NameWidth) return full;
            int cut = full.LastIndexOf(' ');
            return cut > 0 ? full.Substring(0, 1) + ". " + full.Substring(cut + 1) : full;
        }
    }
}
