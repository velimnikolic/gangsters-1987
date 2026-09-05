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
    /// </summary>
    public sealed class EventCardHud : MonoBehaviour
    {
        const int SortingOrder = 129;
        const float SheetWidth = 980f;
        const float SheetHeight = 620f;
        const float Pad = 34f;

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

            var scaler = screen.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            screen.AddComponent<GraphicRaycaster>();

            var shade = NewRect("City held", screen.transform);
            Stretch(shade);
            var shadeImage = Fill(shade, new Color(0.025f, 0.02f, 0.015f, 0.82f));
            shadeImage.raycastTarget = true;

            var sheet = LedgerV2.Card("Card", screen.transform, 0f, 0f, SheetWidth, SheetHeight);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.5f);
            sheet.pivot = new Vector2(0.5f, 0.5f);
            sheet.anchoredPosition = Vector2.zero;
            sheet.sizeDelta = new Vector2(SheetWidth, SheetHeight);

            var w = SheetWidth - Pad * 2f;
            var y = -Pad;

            // The head: what this is, who is on the line.
            var band = NewRect("Head", sheet);
            PlaceTopLeft(band, 0f, 0f, SheetWidth, 64f);
            Fill(band, LedgerV2.Head);
            var head = Line(band, LedgerStyle.Condensed, 22f, LedgerV2.HeadCream, Pad, -12f,
                w * 0.6f, 36f, "THE PHONE  ·  " + card.Title);
            head.characterSpacing = 4f;
            Line(band, LedgerStyle.Mono, 11.5f, LedgerV2.HeadDim, Pad + w * 0.6f, -22f,
                w * 0.4f, 20f, card.SpeakerName.ToUpperInvariant() + " ON THE LINE",
                TextAlignmentOptions.MidlineRight);
            y = -64f - 22f;

            // His words.
            for (var i = 0; i < card.Lines.Count; i++)
            {
                var line = card.Lines[i];
                var lines = Mathf.Max(1, Mathf.CeilToInt(line.Length / 95f));
                var h = 15.5f * 1.55f * lines + 6f;
                Paragraph(sheet, i == 1 ? LedgerStyle.SerifItalic : LedgerStyle.Serif, 15.5f,
                    LedgerV2.Body, Pad, y, w, h, line, 3f);
                y -= h + 8f;
            }

            Rule(sheet, Pad, y - 4f, w, LedgerV2.Rule);
            y -= 18f;

            // The rows: numbered, each with its cost, who goes, and the risk in words.
            for (var i = 0; i < card.Choices.Count && i < 3; i++)
            {
                var row = card.Choices[i];
                var index = i;
                var key = LedgerV2.Button(sheet, (i + 1) + "  " + row.Label, Pad, y, 300f, 34f,
                    () => Choose(index), LedgerV2.Key.Dark, 12f);
                LedgerV2.KeyEnabled(key, hold == HoldReason.None, LedgerV2.HeadDim);
                var detail = row.Note;
                if (row.Cost > 0)
                    detail = LedgerText.Cash(row.Cost) + "  ·  " + detail;
                if (!string.IsNullOrEmpty(row.Risk))
                    detail += "  ·  " + row.Risk;
                Paragraph(sheet, LedgerStyle.Mono, 11.5f, LedgerV2.Muted, Pad + 316f, y + 2f,
                    w - 316f, 40f, detail, 2f);
                y -= 46f;
            }

            // The hold, in words, and what clears it (the UI rule).
            if (hold != HoldReason.None)
            {
                Line(sheet, LedgerStyle.MonoBold, 12f, LedgerV2.Red, Pad, y, w, 22f,
                    "HELD - " + HoldReasons.Line(hold).ToUpperInvariant() + " - " +
                    HoldReasons.Clears(hold));
                y -= 26f;
            }
            if (!string.IsNullOrEmpty(note))
            {
                Line(sheet, LedgerStyle.MonoBold, 12f, LedgerV2.Red, Pad, y, w, 22f, note);
                y -= 26f;
            }

            var foot = Line(sheet, LedgerStyle.Mono, 11f, LedgerV2.Faint, Pad,
                -(SheetHeight - Pad + 4f), w, 20f,
                "1-3  CHOOSE   ·   ESC  HOLD THE CARD (IT WAITS ON STREET TALK UNTIL DAY " +
                card.ExpiresDay + ")");
            foot.characterSpacing = 2f;
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
