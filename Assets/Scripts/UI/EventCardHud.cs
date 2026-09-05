using System.Collections.Generic;
using LivingCity.Ambient;
using LivingCity.News;
using LivingCity.Outfit;
using LivingCity.Save;
using RoadDemo;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE PHONE (EPIC 40, STREET-002). A card dealt at midnight is shown at the six
    /// o'clock cut once the paper has closed: it rings, the clock stops, one of our
    /// men says what he heard, and the rows are numbered. 1-3 choose; Esc HOLDS - the
    /// card stays PENDING on the ledger's front page for its three days and nothing is
    /// decided. Every row explains itself: its cost, who goes, the risk in words.
    ///
    /// Every choice leaves through TerritoryRuntime.CarryForPlayer - the same Carry a
    /// mind's answer goes through, the house named on every order it builds - so a card
    /// can never do what a button cannot.
    ///
    /// The face is the "Night Rail" popup of the ledger design system (the user's
    /// handoff of 2026-09-05): a 660-wide card on the rail's dark, the meta bar, the
    /// condensed title, his words in serif, the wire as a telex slip, four readings in
    /// the trough, one light panel row per choice with its keycap, its key and its
    /// consequence, and the keys in words along the foot.
    /// </summary>
    public sealed class EventCardHud : MonoBehaviour
    {
        const int SortingOrder = 129;

        public static EventCardHud Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        /// <summary>Polling input cannot be consumed: Esc stays claimed through the
        /// closing frame so the map, the overlay and the arrest clock do not see it.</summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == lastCloseFrame;

        static int lastCloseFrame = -1;

        GameObject screen;
        CityClock clock;
        bool ownsPause;
        bool wasPaused;
        bool warnedFont;
        int shownDealtDay = -1;
        CardId shownCard;
        EventCard card;
        HoldReason hold;
        string note = "";
        TerritoryRuntime runtime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Instance = null;
            IsOpen = false;
            lastCloseFrame = -1;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        void Update()
        {
            if (IsOpen)
            {
                ReadKeys();
                return;
            }

            // A loaded campaign is not live until its Pending file has been applied.
            if (CampaignSave.Pending != null)
                return;

            var underworld = Underworld.Current;
            var runner = underworld?.Player?.Runner;
            var dayClock = DayClock.Current;
            if (underworld == null || runner == null || dayClock == null || runner.Fallen)
                return;

            var book = runner.Events;
            var pending = book.Pending;
            if (pending == null)
                return;

            // Once per deal. Held with Esc, it waits on STREET TALK's PENDING row.
            if (pending.DealtDay == shownDealtDay && pending.Id == shownCard)
                return;

            // Dealt at midnight, shown at the cut after the paper has closed.
            var day = runner.Campaign.Day;
            if (dayClock.Hour < Edition.PressHour && pending.DealtDay >= day)
                return;
            if (underworld.Press.LastEditionDay < day)
                return;
            if (ModalGate.OtherPaperUp(ModalGate.Paper.Phone) || OutfitEnd.IsUp)
                return;

            Open();
        }

        /// <summary>STREET TALK's PENDING row: the held card, opened again.</summary>
        public void Reopen()
        {
            if (IsOpen)
                return;
            Open();
        }

        void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                Choose(0);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                Choose(1);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                Choose(2);
        }

        void Open()
        {
            runtime = TerritoryRuntime.Instance;
            if (runtime == null || IsOpen)
                return;
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                if (!warnedFont)
                {
                    warnedFont = true;
                    Debug.LogWarning("[Phone] No TMP default font - the card cannot be drawn.", this);
                }
                return;
            }

            card = runtime.PlayerCard(out hold);
            if (card == null)
                return;
            shownDealtDay = card.DealtDay;
            shownCard = card.Id;
            note = "";

            clock = FindAnyObjectByType<CityClock>();
            if (clock)
            {
                wasPaused = clock.Paused;
                ownsPause = true;
                clock.Paused = true;
            }

            EnsureEventSystem();
            Build();
            IsOpen = true;
            DemoAudio.Ui(DemoSounds.Newspaper);
            Debug.Log("[Phone] " + card.SpeakerName + " rings: " + card.Title +
                      (hold != HoldReason.None ? " (held: " + HoldReasons.Line(hold) + ")" : ""));
        }

        void Build()
        {
            if (screen != null)
                Destroy(screen);
            screen = new GameObject("The Phone", typeof(RectTransform));
            screen.transform.SetParent(transform, false);

            var canvas = screen.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // THE HUD'S FRAME, not the book's: the handoff's px are CSS px in a 1280x720
            // frame, so every length copies 1:1 and only the type is converted (a TMP
            // size is the design px over the face's measured optical - see LedgerStyle).
            var scaler = screen.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            screen.AddComponent<GraphicRaycaster>();

            // The desk behind the card. The handoff paints it solid; here the street is
            // what the card is about, so it stays visible under the desk's own dark.
            var shade = NewRect("City held", screen.transform);
            Stretch(shade);
            var shadeImage = Fill(shade, LedgerV2.At(LedgerV2.Rgb2(0x0d0906), 0.82f));
            shadeImage.raycastTarget = true;

            // ---------------------------------------------------------------- measure
            var w = CardWidth - Pad * 2f;
            var titleLines = TitleLines(card.Title);
            var lead = card.Lines.Count > 0 ? card.Lines[0] : "";
            var leadW = Mathf.Min(w, LeadWidth);
            var leadRows = Mathf.Max(1, Mathf.CeilToInt(lead.Length / (leadW / SerifChar)));
            var leadH = leadRows * LeadLine + 4f;
            var telex = TelexBody();
            var telexRows = telex.Length == 0
                ? 0
                : Mathf.Max(1, Mathf.CeilToInt(telex.Length / ((w - 28f) / TypeChar)));
            var telexH = telexRows == 0 ? 0f : TelexHead + telexRows * TelexLine + 10f;
            var holdLine = hold != HoldReason.None
                ? "HELD - " + HoldReasons.Line(hold).ToUpperInvariant() + " - " +
                  HoldReasons.Clears(hold) + "."
                : note;
            var holdH = string.IsNullOrEmpty(holdLine) ? 0f : 36f;
            var rows = Mathf.Min(3, card.Choices.Count);

            var bodyH = BodyTop + titleLines.Length * TitleLine + Gap + leadH +
                        (telexH > 0f ? Gap + telexH : 0f) + Gap + StatsH +
                        (holdH > 0f ? 12f + holdH : 0f) + BodyBottom;
            var cardH = MetaH + bodyH + rows * RowH + FootH;

            // ------------------------------------------------------------------ card
            var sheet = NewRect("Card", screen.transform);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.5f);
            sheet.pivot = new Vector2(0.5f, 0.5f);
            sheet.anchoredPosition = Vector2.zero;
            sheet.sizeDelta = new Vector2(CardWidth, cardH);
            Fill(sheet, LedgerStyle.Rail);
            ShadowUnder(sheet, 14f, new Color(0f, 0f, 0f, 0.38f));
            var group = sheet.gameObject.AddComponent<CanvasGroup>();

            // 1. THE META BAR: what is coming in, and who is on the line.
            var meta = NewRect("Meta", sheet);
            PlaceTopLeft(meta, 0f, 0f, CardWidth, MetaH);
            Rule(meta, 0f, -(MetaH - 1f), CardWidth, LedgerStyle.RailTrough);
            Handset(meta, 20f, -(MetaH * 0.5f));
            var incoming = Line(meta, LedgerStyle.Mono, MonoSize, LedgerStyle.RailLabel, 45f,
                -(MetaH - 18f) * 0.5f, 380f, 18f,
                "INCOMING · DAY " + card.DealtDay + (clock ? " · " + clock.Display : ""));
            incoming.characterSpacing = Tracked;
            var caller = Line(meta, LedgerStyle.Mono, MonoSize, LedgerStyle.RailGold,
                CardWidth - 20f - 300f, -(MetaH - 18f) * 0.5f, 300f, 18f,
                card.SpeakerName.ToUpperInvariant(), TextAlignmentOptions.MidlineRight);
            caller.characterSpacing = Tracked;

            // 2. THE BODY: the title, his words, the wire, the numbers.
            var y = -MetaH - BodyTop;
            for (var i = 0; i < titleLines.Length; i++)
            {
                var title = Line(sheet, LedgerStyle.Condensed, TitleSize, LedgerV2.HeadCream,
                    Pad, y, w, TitleLine + 8f, titleLines[i].ToUpperInvariant());
                title.characterSpacing = 0.5f;
                y -= TitleLine;
            }
            y -= Gap;

            Paragraph(sheet, LedgerStyle.Serif, LeadSize, LedgerStyle.RailValue, Pad, y, leadW,
                leadH, lead, 6f);
            y -= leadH + Gap;

            if (telexH > 0f)
            {
                TelexSlip(sheet, Pad, y, w, telexH, telex);
                y -= telexH + Gap;
            }

            StatStrip(sheet, Pad, y, w);
            y -= StatsH;

            if (holdH > 0f)
            {
                y -= 12f;
                var held = Paragraph(sheet, LedgerStyle.MonoBold, MonoSize, LedgerStyle.RailRed,
                    Pad, y, w, holdH, holdLine, 2f);
                held.characterSpacing = 1f;
                y -= holdH;
            }
            y -= BodyBottom;

            // 3. THE ROWS: one hit target each - the number, the key and the copy.
            for (var i = 0; i < rows; i++)
                ChoiceRow(sheet, y - i * RowH, i);
            y -= rows * RowH;

            // 4. THE FOOT: the keys, in words.
            var foot = NewRect("Foot", sheet);
            PlaceTopLeft(foot, 0f, y, CardWidth, FootH);
            Fill(foot, LedgerV2.Head);
            var hint = Line(foot, LedgerStyle.Mono, LabelSize, LedgerStyle.RailLabel, Pad,
                -(FootH - 17f) * 0.5f, w, 17f,
                (rows > 1 ? "1-" + rows : "1") + " CHOOSE · ESC HOLD THE CARD UNTIL DAY " +
                card.ExpiresDay);
            hint.characterSpacing = Tracked;

            if (entry != null)
                StopCoroutine(entry);
            entry = StartCoroutine(Enter(sheet, group));
        }

        // The handoff's px are the HUD frame's units; a TMP size is px / optical.
        const float CardWidth = 660f;
        const float Pad = 24f;
        const float Gap = 16f;
        const float MetaH = 44f;
        const float BodyTop = 22f;
        const float BodyBottom = 20f;
        const float StatsH = 66f;
        const float RowH = 70f;
        const float FootH = 37f;
        const float LeadWidth = 470f;   // 56ch of 15px serif
        const float LeadLine = 24f;     // 15px x 1.6
        const float TitleLine = 31.5f;  // 30px x 1.05
        const float TelexHead = 26f;
        const float TelexLine = 13.5f;  // Lekton at 13px, 4 of leading
        // measured on the first capture: the advance of one character, in units
        const float SerifChar = 6.3f;   // PT Serif at 15px
        const float TypeChar = 7.5f;    // Lekton at 13px
        const float Tracked = 5f;
        const float MonoSize = 13f / 0.831f;
        const float LabelSize = 12f / 0.831f;
        const float FigureSize = 15f / 0.831f;
        const float TitleSize = 30f / 0.864f;
        const float LeadSize = 15f / 1.017f;
        const float TelexSize = 13f / 1.082f;
        const int PipCount = 6;

        Coroutine entry;

        /// <summary>The title on two hard lines the way the handoff sets it ("A MAN OFF
        /// THE / COUNTY FIELD"): broken at the last space before the middle. A short
        /// title stays on one.</summary>
        static string[] TitleLines(string title)
        {
            title = (title ?? "").Trim();
            if (title.Length <= 16)
                return new[] { title };
            var at = title.LastIndexOf(' ', title.Length / 2);
            if (at < 0)
                at = title.IndexOf(' ');
            if (at < 0)
                return new[] { title };
            return new[] { title.Substring(0, at), title.Substring(at + 1) };
        }

        /// <summary>What came over the wire, in the man's own words: every line of the
        /// card after the lead, the quotes taken off, set in typewriter caps.</summary>
        string TelexBody()
        {
            var body = "";
            for (var i = 1; i < card.Lines.Count; i++)
            {
                var line = card.Lines[i].Trim().Trim('"');
                if (line.Length == 0)
                    continue;
                body += (body.Length > 0 ? " " : "") + line;
            }
            return body.ToUpperInvariant();
        }

        /// <summary>The receiver, drawn: a bar and its two ends, in the rail's gold.</summary>
        static void Handset(Transform parent, float x, float centreY)
        {
            var gold = LedgerStyle.RailGold;
            Block("Handset", parent, x, centreY + 1.5f, 15f, 3f, gold);
            Block("Handset", parent, x, centreY + 5f, 4f, 7f, gold);
            Block("Handset", parent, x + 11f, centreY + 5f, 4f, 7f, gold);
        }

        /// <summary>The telex slip: cream stock, the red rule down its left edge, the
        /// source and the hour in mono caps, the message in typewriter caps.</summary>
        void TelexSlip(Transform parent, float x, float y, float w, float h, string body)
        {
            var rect = NewRect("Telex", parent);
            PlaceTopLeft(rect, x, y, w, h);
            Fill(rect, LedgerStyle.TelexPaper);
            Block("Edge", rect, 0f, 0f, 3f, h, LedgerStyle.TelexDot);

            var source = "WIRE · " + (card.Line == ConnectionLine.Field
                ? "THE COUNTY FIELD"
                : card.Line == ConnectionLine.Port ? "THE PORT" : "THE STREET");
            var head = Line(rect, LedgerStyle.Mono, LabelSize * 0.92f, LedgerStyle.TelexStamp,
                14f, -7f, w - 120f, 16f, source);
            head.characterSpacing = Tracked;
            var time = Line(rect, LedgerStyle.Mono, LabelSize * 0.92f, LedgerStyle.TelexStamp,
                w - 14f - 90f, -7f, 90f, 16f, clock ? clock.Display : "",
                TextAlignmentOptions.MidlineRight);
            time.characterSpacing = Tracked;

            Paragraph(rect, LedgerStyle.Type, TelexSize, LedgerStyle.TelexPlain, 14f,
                -TelexHead, w - 28f, h - TelexHead - 6f, body, 4f);
        }

        /// <summary>Four readings in the trough: the line, the money up front, the risk
        /// and the trust - the last two as counted marks, never a bar.</summary>
        void StatStrip(Transform parent, float x, float y, float w)
        {
            var strip = NewRect("Stats", parent);
            PlaceTopLeft(strip, x, y, w, StatsH);
            Fill(strip, LedgerStyle.RailTrough);

            var underworld = Underworld.Current;
            var ctx = runtime != null ? runtime.PlayerContext() : null;
            var view = runtime != null && underworld?.Player != null
                ? runtime.Peek(underworld.Player)
                : null;
            var paper = ctx?.Connection;
            var line = paper != null && paper.Line != ConnectionLine.None ? paper.Line : card.Line;
            var grade = paper != null ? paper.Grade : SupplierGrade.None;
            var theLine = line == ConnectionLine.None
                ? "NOT YET OURS"
                : Connection.MinLoadFor(line, grade) + " KILOS / " +
                  (line == ConnectionLine.Field ? "FLIGHT" : "BOAT");
            var upFront = card.Choices.Count > 0 && card.Choices[0].Cost > 0
                ? LedgerText.Cash(card.Choices[0].Cost)
                : "NOTHING";
            var attention = view != null ? ConnectionScore.MaxAttention(view) : 0f;
            var risk = Mathf.Clamp(Mathf.CeilToInt(attention / 100f * PipCount), 0, PipCount);
            var trust = paper != null
                ? Mathf.Clamp(Mathf.RoundToInt(paper.Trust / 10f), 0, PipCount)
                : 0;

            var cx = 14f;
            cx = Stat(strip, cx, 150f, "The line", theLine, LedgerStyle.RailValue);
            cx = Stat(strip, cx, 120f, "Up front", upFront, LedgerStyle.RailGold);
            cx = Stat(strip, cx, 100f, "Risk", null, LedgerStyle.RailAmber, risk);
            Stat(strip, cx, 100f, "Trust", null, LedgerStyle.RailGreen, trust);
        }

        static float Stat(Transform strip, float x, float w, string label, string value,
            Color ink, int pips = -1)
        {
            var head = Line(strip, LedgerStyle.Mono, LabelSize, LedgerStyle.RailLabel, x, -12f,
                w, 16f, label.ToUpperInvariant());
            head.characterSpacing = 4f;
            if (value != null)
                Line(strip, LedgerStyle.MonoBold, FigureSize, ink, x, -34f, w, 20f, value);
            else
                LedgerV2.Pips(strip, x, -44f, PipCount, pips, ink, 10f, 10f, 13f,
                    LedgerStyle.Rail);
            return x + w + 26f;
        }

        /// <summary>One choice: the keycap with its number, the key with its verb, the
        /// consequence in one mono line - and the whole row is the button.</summary>
        void ChoiceRow(Transform parent, float y, int index)
        {
            var choice = card.Choices[index];
            var commits = index == 0;
            var row = NewRect("Row " + (index + 1), parent);
            PlaceTopLeft(row, 0f, y, CardWidth, RowH);
            var face = Fill(row, LedgerV2.Panel);
            Rule(row, 0f, 0f, CardWidth, LedgerV2.Hair);
            var open = hold == HoldReason.None;
            if (open)
            {
                RowButton(row, face, () => Choose(index));
                HoverTint.On(row, face, LedgerV2.Panel, LedgerV2.PanelBand);
            }

            // the keycap: the committing verb's sits proud on a hard shadow
            var cap = NewRect("Keycap", row);
            PlaceTopLeft(cap, Pad, -(RowH - 30f) * 0.5f, 30f, 30f);
            if (commits)
            {
                Block("Cap shadow", row, Pad + 2f, -(RowH - 30f) * 0.5f - 2f, 30f, 30f,
                    LedgerV2.Rule);
                cap.SetAsLastSibling();
                Fill(cap, LedgerV2.Head);
            }
            else
                Frame(cap, 1f, LedgerV2.Rule);
            var digit = Line(cap, LedgerStyle.MonoBold, FigureSize,
                commits ? LedgerStyle.RailGold : LedgerV2.Label, 0f, 0f, 30f, 30f,
                (index + 1).ToString(), TextAlignmentOptions.Center);
            digit.raycastTarget = false;

            var key = LedgerV2.Button(row, choice.Label, Pad + 30f + Gap, -(RowH - 42f) * 0.5f,
                150f, 42f, () => Choose(index), commits ? LedgerV2.Key.Dark : LedgerV2.Key.Outline,
                11f);
            LedgerV2.KeyEnabled(key, open, LedgerV2.HeadDim);

            var copyX = Pad + 30f + Gap + 150f + Gap;
            var why = choice.Note ?? "";
            if (!string.IsNullOrEmpty(choice.Risk))
                why += (why.Length > 0 ? " " : "") + choice.Risk;
            var copy = Paragraph(row, LedgerStyle.Mono, MonoSize,
                commits ? LedgerV2.Body : LedgerV2.Muted, copyX, -(RowH - 48f) * 0.5f,
                CardWidth - copyX - Pad, 48f, why, 2f);
            copy.raycastTarget = false;
        }

        /// <summary>The card comes in fast and flat: 120 ms from clear to solid over
        /// eight units of rise. No spring - this UI is hard-edged and instant.</summary>
        System.Collections.IEnumerator Enter(RectTransform sheet, CanvasGroup group)
        {
            var to = sheet.anchoredPosition;
            var from = to + new Vector2(0f, -8f);
            var t = 0f;
            while (t < 0.12f)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / 0.12f);
                group.alpha = k;
                sheet.anchoredPosition = Vector2.Lerp(from, to, k);
                yield return null;
            }
            group.alpha = 1f;
            sheet.anchoredPosition = to;
            entry = null;
        }

        void Choose(int index)
        {
            if (!IsOpen || card == null || index < 0 || index >= card.Choices.Count)
                return;
            if (hold != HoldReason.None)
            {
                note = HoldReasons.Line(hold) + " - " + HoldReasons.Clears(hold) + ".";
                Build();
                return;
            }
            var refusal = runtime != null
                ? runtime.CarryForPlayer(HouseIntent.Choose(card, index, HouseMind.TierCollect,
                    card.SpeakerName + ": " + card.Choices[index].Label))
                : "no street";
            if (!string.IsNullOrEmpty(refusal))
            {
                note = refusal;
                Build();
                return;
            }
            Debug.Log("[Phone] " + card.Id + "/" + card.Choices[index].Label + " - taken.");
            Gameplay.PersonnelDirector.Instance?.Touch();
            Close();
        }

        void Close()
        {
            if (!IsOpen && screen == null)
                return;
            IsOpen = false;
            lastCloseFrame = Time.frameCount;
            if (screen != null)
            {
                Destroy(screen);
                screen = null;
            }
            ReleasePause();
        }

        void ReleasePause()
        {
            if (!ownsPause)
                return;
            if (clock)
                clock.Paused = wasPaused;
            ownsPause = false;
            clock = null;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current)
                return;
            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        void OnDestroy()
        {
            if (Instance != this)
                return;
            ReleasePause();
            IsOpen = false;
            Instance = null;
        }
    }
}
