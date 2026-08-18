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
    // photograph - with his name, the glyph of what he is doing, and his hoods
    // INSIDE the block as a two-by-two of small mugshots (Crew.MaxHoods). No
    // health bars: a wounded man's picture goes amber, a man one hit from the
    // ground goes red, a dead man's goes dark under the skull. An empty chip is a
    // dim plus waiting on a recruit. The selected crew's block wears the gold rim;
    // a click on a block selects that crew, the same as clicking the man.
    // Dressed out of DemoUi like every other screen in the demo.
    //
    // The feeds are cheap on purpose: a low-res target each, no shadows, no post,
    // and the cameras take turns - one renders per frame - so a bar of four crews
    // costs one small extra render a frame, not four.
    public class CrewBar : MonoBehaviour
    {
        public static CrewBar Instance { get; private set; }

        const int MaxHoods = LivingCity.Personnel.Crew.MaxHoods;
        const float BlockWidth = 172f, BlockHeight = 72f, Gap = 6f;
        const float FeedSize = 60f;            // the square the man stands in, whole
        const float ChipWidth = 30f, ChipHeight = 22f, ChipGap = 3f;
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
            public TMP_Text Name;
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
            feedRect.anchoredPosition = new Vector2(6f, 0f);
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

            // the right column: the name on the top line, the men in a two-by-two
            // under it (what he is doing is on the map, over him - not here)
            float column = 6f + FeedSize + 8f;

            if (_fonts)
            {
                block.Name = DemoUi.Text(rect, "Name", 11.5f, DemoUi.Ink, TextAlignmentOptions.TopLeft, display: true);
                block.Name.characterSpacing = 1f;
                var nr = block.Name.rectTransform;
                nr.anchorMin = new Vector2(0f, 1f);
                nr.anchorMax = new Vector2(1f, 1f);
                nr.pivot = new Vector2(0f, 1f);
                nr.anchoredPosition = new Vector2(column, -4f);
                nr.sizeDelta = new Vector2(-(column + 8f), 15f);
            }

            block.Chips = new Chip[MaxHoods];
            for (int k = 0; k < MaxHoods; k++)
            {
                int col = k % 2, row = k / 2;
                block.Chips[k] = BuildChip(rect,
                    new Vector2(column + col * (ChipWidth + ChipGap), -(21f + row * (ChipHeight + ChipGap))));
            }

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
            chip.Portrait.enabled = false;

            chip.Plus = DemoUi.Icon(rect, "Plus", DemoUi.IconPlus, 10f,
                new Color(DemoUi.InkDim.r, DemoUi.InkDim.g, DemoUi.InkDim.b, 0.35f));

            chip.Shade = DemoUi.Block(rect, "Shade", Wounded);
            DemoUi.Fill(chip.Shade.rectTransform);
            chip.Shade.enabled = false;
            chip.Skull = DemoUi.Icon(rect, "Skull", DemoUi.IconDeath, 14f, Skull);
            chip.Skull.enabled = false;
            return chip;
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
                Bind(block, _shown[i]);
                // a right click on a block swings the camera onto that crew
                if (look && RectTransformUtility.RectangleContainsScreenPoint(block.Rect, at))
                    LookAt(_shown[i]);
                if (click && RectTransformUtility.RectangleContainsScreenPoint(block.Rect, at))
                {
                    // an empty chip is the recruiting door: a click on it brings a new man
                    // in for that crew; anywhere else on the block selects the crew
                    bool recruited = false;
                    foreach (var chip in block.Chips)
                    {
                        if (chip.Man != null || !RectTransformUtility.RectangleContainsScreenPoint(chip.Rect, at)) continue;
                        recruited = true;
                        if (!_crews.Recruit(_shown[i])) chip.RefusedUntil = Time.unscaledTime + 0.7f;
                        break;
                    }
                    if (!recruited) _crews.Select(_shown[i]);
                }
            }
        }

        void Bind(Block block, DemoCrews.Unit unit)
        {
            var boss = unit.Boss;
            if (block.Unit != unit || block.Boss != boss)
            {
                block.Unit = unit;
                block.Boss = boss;
                block.CamYaw = float.NaN;
                // the feed camera sees its layer only; the man goes on it, gun and all
                if (boss != null && boss.Tf != null) SetLayer(boss.Tf, FeedLayer);
                if (block.Name != null)
                    block.Name.text = ShortName(boss != null ? boss.DisplayName : unit.Name);
            }

            block.Rim.enabled = _crews.Selected == unit;
            Film(block, boss);
            Condition(boss, block.Shade, block.Skull);

                        for (int k = 0; k < MaxHoods; k++)
                BindChip(block.Chips[k], k < unit.Hoods.Count ? unit.Hoods[k] : null);
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

        /// <summary>"Sal Ricci" fits; a long name is trimmed to initial and surname
        /// so a block never wraps.</summary>
        // The demo camera pans to the crew - its lieutenant, or the car he rides in.
        static void LookAt(DemoCrews.Unit unit)
        {
            var cam = Object.FindAnyObjectByType<DemoCamera>();
            if (cam == null || unit == null) return;
            Vector3 at;
            if (unit.Car != null && unit.Car.Tf != null) at = unit.Car.Position;
            else if (unit.Boss != null && unit.Boss.Tf != null) at = unit.Boss.Tf.position;
            else at = unit.Position;
            cam.pivot = at;
        }

        static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return string.Empty;
            if (full.Length <= 13) return full;
            int cut = full.LastIndexOf(' ');
            return cut > 0 ? full.Substring(0, 1) + ". " + full.Substring(cut + 1) : full;
        }
    }
}
