using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using LivingCity.CameraRig;

namespace RoadDemo
{
    /// <summary>
    /// The card that opens over a family's premises: what the shop IS, and what it is
    /// for. Two tabs, because a front is two businesses at one address and the player is
    /// meant to hold both in his head at once - LEGIT is what the licence board would
    /// read, THE BUSINESS is what the family actually runs out of the back.
    ///
    /// A click reaches this the way every other picker in the demo is reached: through
    /// BuildingCardPicker's veto chain. This overlay installs itself LAST (DemoCrews
    /// seats it after the crew overlay), so it is the head of the chain - and it asks
    /// the rest of the chain FIRST anyway, because a gangster standing on the doorstep
    /// is in front of the door and his click is his. Only a click nobody else wanted
    /// gets raycast into the city.
    ///
    /// The pick rule is BuildingCardPicker's own, deliberately: the first collider under
    /// the picker's own pickRoot decides. So whatever building the plain card would have
    /// opened over is the building this asks about - the two can never point at
    /// different premises.
    ///
    /// Built and hit-tested by hand (no Button, no GraphicRaycaster) - CrewOverlay's
    /// rule: a second raycaster in the scene quietly eats the clicks the first one was
    /// answering.
    /// </summary>
    public sealed class FrontOverlay : MonoBehaviour
    {
        const float CardWidth = 372f;
        const float Pad = 12f;
        const float RowHeight = 21f;
        const float TabHeight = 26f;

        /// <summary>Rows the card can print on either tab. LEGIT needs eight.</summary>
        const int MaxRows = 8;

        /// <summary>Metres of daylight between the doorway and the card that reads it.
        /// The card hangs off the DOOR rather than off the click that opened it: placed
        /// once at the pointer it stayed where the pointer was, so panning the city left
        /// the premises behind and the card riding along with the player.</summary>
        const float DoorLift = 3.2f;

        static readonly Color TabOff = new Color(0.10f, 0.35f, 0.58f, 0.35f);
        static readonly Color TabOn = new Color(0.10f, 0.35f, 0.58f, 1f);
        static readonly Color Dirty = new Color(1f, 0.62f, 0.42f);

        Camera _cam;
        Transform _pickRoot;
        bool _pickRootChecked;
        System.Func<Vector2, bool> _previousVeto;

        Canvas _canvas;
        RectTransform _card;
        Image _stripe;
        TMP_Text _sign, _under, _note;
        Image _legitTab, _crookedTab;
        TMP_Text _legitLabel, _crookedLabel;
        readonly List<(RectTransform rect, TMP_Text label, TMP_Text value)> _rows =
            new List<(RectTransform, TMP_Text, TMP_Text)>();

        GangFront _open;
        Vector3 _anchor;   // the doorway the card is reading, in world metres
        bool _crooked;   // which tab is up: false LEGIT, true THE BUSINESS

        public void Init()
        {
            _cam = Camera.main;

            var root = new GameObject("Front Overlay", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // over the crew overlay's 1: the card is a thing being read, and a crew dot
            // must not sit on top of the line the player is reading
            _canvas.sortingOrder = 2;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _previousVeto = BuildingCardPicker.ClickVeto;
            BuildingCardPicker.ClickVeto = ClaimsClick;
        }

        void OnDestroy()
        {
            if (BuildingCardPicker.ClickVeto == (System.Func<Vector2, bool>)ClaimsClick)
                BuildingCardPicker.ClickVeto = _previousVeto;
        }

        void Update()
        {
            if (_open == null) return;

            // Follow the door, not the screen. The card is re-placed every frame, so it
            // sits over its own premises through a pan and a zoom, and stands down
            // entirely once they are out of the view.
            Place();

            // Escape closes it, the way it closes the plain building card. The ledger
            // claims Escape while the book is open, so a card behind the book does not
            // steal the press that is putting the book away.
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.ClaimsEsc)
                Close();
        }

        // ------------------------------------------------------------------ picking

        bool ClaimsClick(Vector2 screen)
        {
            // The card is on top and answers first: a tab takes the click, anything else
            // on the card swallows it (so a near miss does not throw the card away), and
            // a click off the card puts it down and goes on to mean whatever it meant.
            if (_open != null && _card != null && _card.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(_card, screen))
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        _legitTab.rectTransform, screen)) Show(false);
                else if (RectTransformUtility.RectangleContainsScreenPoint(
                        _crookedTab.rectTransform, screen)) Show(true);
                return true;
            }

            // Everybody else's click first - a man on the doorstep stands between the
            // player and the door, and selecting him is what the click was for.
            if (_previousVeto != null && _previousVeto(screen))
            {
                Close();
                return true;
            }

            var front = FrontUnder(screen);
            if (front == null)
            {
                Close();
                return false;   // let the plain building card have it
            }

            // A card that cannot be built (no TMP font) must not eat the click: the
            // plain building card is still better than nothing happening at all.
            return Open(front, screen);
        }

        /// <summary>The front under the pointer, by BuildingCardPicker's own pick rule -
        /// the first collider under its pickRoot, and then whether that building is
        /// somebody's premises. A building in FRONT of the front therefore blocks it,
        /// which is what the eye expects.</summary>
        GangFront FrontUnder(Vector2 screen)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || GangFront.All.Count == 0) return null;

            if (!_pickRootChecked)
            {
                _pickRootChecked = true;
                var picker = FindAnyObjectByType<BuildingCardPicker>();
                if (picker != null) _pickRoot = picker.pickRoot;
            }

            var hits = Physics.RaycastAll(_cam.ScreenPointToRay(screen), 3000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var t = hit.collider.transform;
                if (_pickRoot != null && !t.IsChildOf(_pickRoot)) continue;
                // a facade the cutaway has taken out of the way is not under the
                // pointer as far as the player is concerned: the ray goes on through
                if (StreetCutaway.Invisible(hit.collider)) continue;
                return GangFront.Of(t);   // the first building decides, front or not
            }

            return null;
        }

        // ------------------------------------------------------------------ the card

        bool Open(GangFront front, Vector2 screen)
        {
            if (!Build() || front.Books == null) return false;
            _open = front;
            _crooked = false;

            _stripe.color = LivingCity.UI.GangPalette.Of(front.GangId);
            _sign.text = front.Books != null ? front.Books.Sign : front.GangName;
            _under.text = front.GangId == LivingCity.Gangs.GangCatalog.PlayerGangId
                ? "YOUR OWN HOUSE" + Where(front)
                : front.GangName.ToUpperInvariant() + " FAMILY" + Where(front);

            _anchor = front.Door + Vector3.up * DoorLift;
            Show(false);
            _card.gameObject.SetActive(true);
            Place();
            return true;
        }

        static string Where(GangFront front) =>
            front.Books != null && !string.IsNullOrEmpty(front.Books.Address)
                ? "  ·  " + front.Books.Address
                : "";

        void Close()
        {
            _open = null;
            if (_card != null) _card.gameObject.SetActive(false);
        }

        /// <summary>Fill the card for one of the two tabs and size it to what it printed.
        /// Both faces are one layout on purpose: the same rows in the same places, so the
        /// player reads the difference between the two businesses rather than the
        /// difference between two card designs.</summary>
        void Show(bool crooked)
        {
            if (_open == null || _open.Books == null) return;
            _crooked = crooked;
            var books = _open.Books;

            _legitTab.color = crooked ? TabOff : TabOn;
            _crookedTab.color = crooked ? TabOn : TabOff;
            _legitLabel.color = crooked ? DemoUi.InkDim : DemoUi.Ink;
            _crookedLabel.color = crooked ? Dirty : DemoUi.InkDim;

            var used = 0;
            if (!crooked)
            {
                Row(ref used, "TRADE", books.Trade);
                Row(ref used, "PROPRIETOR", books.Proprietor);
                if (!string.IsNullOrEmpty(books.Address))
                    Row(ref used, "ADDRESS", books.Address);
                Row(ref used, "TRADING SINCE", books.Since.ToString());
                Row(ref used, "HOURS", books.Hours);
                Row(ref used, "STAFF", books.Staff + " on the payroll");
                Row(ref used, "TAKINGS", Money(books.Takings) + " a week");
                Row(ref used, "LICENCE", books.Licence);
                _note.text = books.Clean;
                _note.color = DemoUi.InkDim;
            }
            else
            {
                Row(ref used, "RUN BY", books.RunBy);
                Row(ref used, "RACKET", books.Racket);
                Row(ref used, "SKIM", Money(books.Skim) + " a week");
                Row(ref used, "UPSTAIRS", books.Cut + "% to the family");
                Row(ref used, "ON THE DOOR",
                    books.Men == 0 ? "nobody standing" :
                    books.Men == 1 ? "one man" : books.Men + " men");
                _note.text = books.RacketNote + "\n" + books.Heat + "\n" + books.Whisper;
                _note.color = Dirty;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var live = i < used;
                if (_rows[i].rect.gameObject.activeSelf != live)
                    _rows[i].rect.gameObject.SetActive(live);
            }

            var noteTop = Pad + 30f + 20f + TabHeight + 8f + used * RowHeight + 6f;
            _note.rectTransform.anchoredPosition = new Vector2(Pad, -noteTop);
            _note.rectTransform.sizeDelta = new Vector2(CardWidth - Pad * 2f, crooked ? 62f : 34f);
            _card.sizeDelta = new Vector2(
                CardWidth, noteTop + (crooked ? 62f : 34f) + Pad);
        }

        void Row(ref int used, string label, string value)
        {
            while (_rows.Count <= used) BuildRow();
            var row = _rows[used];
            row.rect.anchoredPosition = new Vector2(
                Pad, -(Pad + 30f + 20f + TabHeight + 8f + used * RowHeight));
            row.label.text = label;
            row.value.text = value;
            used++;
        }

        static string Money(int dollars) => "$" + dollars.ToString("N0");

        /// <summary>Beside the doorway, top-left pivot, so the card reads to the right of
        /// the premises and level with them - and is not drawn at all while the doorway is
        /// off the screen.</summary>
        void Place()
        {
            if (_card == null) return;
            // the same lazy fetch the pick does: Camera.main can be missing at Init
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            var scale = _canvas != null ? _canvas.scaleFactor : 1f;
            var w = _card.sizeDelta.x * scale;
            var h = _card.sizeDelta.y * scale;
            var screen = _cam.WorldToScreenPoint(_anchor);
            if (!LivingCity.UI.OverlayCard.OnScreen(screen, Screen.width, Screen.height))
            {
                if (_card.gameObject.activeSelf) _card.gameObject.SetActive(false);
                return;
            }
            if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);

            // Beside the door, and against a right edge it changes sides rather than
            // being shoved off its own doorway. The pivot is the top-left corner, so the
            // y below puts the middle of the card level with the door.
            var x = screen.x + 14f;
            if (x + w > Screen.width) x = Mathf.Max(0f, screen.x - 14f - w);
            var y = Mathf.Clamp(screen.y + h * 0.5f, h, Screen.height);
            _card.position = new Vector3(x, y, 0f);
        }

        // ------------------------------------------------------------------ chrome

        /// <summary>The card's furniture, made once. False without a TMP font - the same
        /// condition the crew overlay's own card stands down under.</summary>
        bool Build()
        {
            if (_card != null) return true;
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                return false;

            _card = DemoUi.NewRect("Front", _canvas.transform);
            _card.pivot = new Vector2(0f, 1f);
            _card.anchorMin = _card.anchorMax = new Vector2(0f, 0f);
            _card.sizeDelta = new Vector2(CardWidth, 240f);

            var back = _card.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            DemoUi.Dress(back, DemoUi.Box, 15f, DemoUi.Panel);

            // the family's colour down the left edge - the same hue its men wear on the
            // map, so the card is placed before a word of it is read
            _stripe = DemoUi.Block(_card, "Family", DemoUi.Gold);
            _stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
            _stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
            _stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
            _stripe.rectTransform.offsetMin = new Vector2(0f, 6f);
            _stripe.rectTransform.offsetMax = new Vector2(3f, -6f);

            _sign = Label("Sign", 19f, DemoUi.Ink, TextAlignmentOptions.TopLeft);
            _sign.rectTransform.anchoredPosition = new Vector2(Pad, -Pad);
            _sign.rectTransform.sizeDelta = new Vector2(CardWidth - Pad * 2f, 26f);
            _sign.characterSpacing = 2f;

            _under = Label("Family", 12.5f, DemoUi.InkDim, TextAlignmentOptions.TopLeft);
            _under.rectTransform.anchoredPosition = new Vector2(Pad, -(Pad + 28f));
            _under.rectTransform.sizeDelta = new Vector2(CardWidth - Pad * 2f, 18f);
            _under.characterSpacing = 3f;

            var tabTop = Pad + 30f + 20f;
            var tabWidth = (CardWidth - Pad * 2f) * 0.5f - 3f;
            (_legitTab, _legitLabel) = Tab("LEGIT", Pad, tabTop, tabWidth);
            (_crookedTab, _crookedLabel) =
                Tab("THE BUSINESS", Pad + tabWidth + 6f, tabTop, tabWidth);

            _note = Label("Note", 12.5f, DemoUi.InkDim, TextAlignmentOptions.TopLeft);
            _note.rectTransform.sizeDelta = new Vector2(CardWidth - Pad * 2f, 34f);
            _note.textWrappingMode = TextWrappingModes.Normal;
            _note.lineSpacing = 6f;

            _card.gameObject.SetActive(false);
            return true;
        }

        (Image, TMP_Text) Tab(string text, float x, float y, float width)
        {
            var face = DemoUi.Block(_card, text, TabOff);
            face.rectTransform.pivot = new Vector2(0f, 1f);
            face.rectTransform.anchorMin = face.rectTransform.anchorMax = new Vector2(0f, 1f);
            face.rectTransform.anchoredPosition = new Vector2(x, -y);
            face.rectTransform.sizeDelta = new Vector2(width, TabHeight);
            // the pack's own chip, sliced through DemoUi so the 9-slice rim is drawn at
            // the size it was authored for rather than at the sprite's pixel scale
            DemoUi.Dress(face, DemoUi.Chip, 8f, TabOff);

            var label = Label(text + " label", 12.5f, DemoUi.Ink, TextAlignmentOptions.Center);
            label.rectTransform.anchoredPosition = new Vector2(x, -(y + 4f));
            label.rectTransform.sizeDelta = new Vector2(width, 18f);
            label.characterSpacing = 3f;
            return (face, label);
        }

        void BuildRow()
        {
            var rect = DemoUi.NewRect("Row", _card);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(CardWidth - Pad * 2f, RowHeight);

            var label = DemoUi.Text(rect, "Label", 12f, DemoUi.InkDim,
                TextAlignmentOptions.MidlineLeft, display: false);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.42f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.characterSpacing = 2f;

            var value = DemoUi.Text(rect, "Value", 13f, DemoUi.Ink,
                TextAlignmentOptions.MidlineRight, display: false);
            value.rectTransform.anchorMin = new Vector2(0.42f, 0f);
            value.rectTransform.anchorMax = new Vector2(1f, 1f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = Vector2.zero;

            _rows.Add((rect, label, value));
        }

        TMP_Text Label(string name, float size, Color colour, TextAlignmentOptions align)
        {
            var text = DemoUi.Text(_card, name, size, colour, align, display: false);
            var rect = text.rectTransform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            return text;
        }
    }
}
