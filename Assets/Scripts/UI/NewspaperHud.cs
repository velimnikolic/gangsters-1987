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
    /// The edition put on the desk at the 06:00 cut. This is deliberately not a page
    /// of the personnel ledger: it owns the screen, stops the city, and can be put
    /// away with X/Esc or continued into THE PAPER with P.
    /// </summary>
    public sealed class NewspaperHud : MonoBehaviour
    {
        const int SortingOrder = 130;
        const float SheetWidth = 1720f;
        const float SheetHeight = 930f;

        public static NewspaperHud Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        /// <summary>Polling input cannot be consumed. Keep Esc claimed through the
        /// closing frame so the map, crew overlay and arrest clock do not see it.</summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == lastCloseFrame;

        /// <summary>The P that continues from the loose sheet into the archive belongs
        /// to this HUD for its entire frame, regardless of component Update order.</summary>
        public static bool ClaimsPaperKey => IsOpen || Time.frameCount == lastPaperFrame;

        static int lastCloseFrame = -1;
        static int lastPaperFrame = -1;

        GameObject screen;
        CityClock clock;
        bool ownsPause;
        bool wasPaused;
        bool warnedFont;
        bool observedClock;
        double lastSerial;
        int dueDay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Instance = null;
            IsOpen = false;
            lastCloseFrame = -1;
            lastPaperFrame = -1;
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
            // In particular, do not flash a day-one sheet built from the fresh scene
            // underneath the save and then restore the real press book over it.
            if (CampaignSave.Pending != null)
                return;

            var underworld = Underworld.Current;
            var runner = underworld?.Player?.Runner;
            var campaign = runner?.Campaign;
            var dayClock = DayClock.Current;
            if (underworld == null || campaign == null || dayClock == null || campaign.Day < 1)
                return;

            ObserveCut(underworld.Press, campaign.Day, dayClock.Hour);
            if (dueDay < 1 || ModalGate.OtherPaperUp(ModalGate.Paper.Newspaper) ||
                OutfitEnd.IsUp || runner.Fallen)
                return;

            Open(underworld, dueDay);
        }

        void ObserveCut(PressBook press, int day, float hour)
        {
            var serial = (day - 1) * 24d + hour;
            if (!observedClock)
            {
                observedClock = true;
                lastSerial = serial;
            }
            else
            {
                var before = System.Math.Floor((lastSerial - Edition.PressHour) / 24d);
                var now = System.Math.Floor((serial - Edition.PressHour) / 24d);
                if (now > before && press.LastEditionDay < day)
                    dueDay = day;
                lastSerial = serial;
            }

            // New campaigns begin exactly at six, and a sheet held behind another
            // modal remains due after the instant itself has passed.
            if (hour >= Edition.PressHour && press.LastEditionDay < day)
                dueDay = day;
        }

        void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.pKey.wasPressedThisFrame)
            {
                lastPaperFrame = Time.frameCount;
                Close();
                var book = PersonnelAlmanac.Instance != null
                    ? PersonnelAlmanac.Instance
                    : FindAnyObjectByType<PersonnelAlmanac>();
                book?.OpenPaper();
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        void Open(Underworld underworld, int editionDay)
        {
            if (IsOpen || underworld == null)
                return;
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                if (!warnedFont)
                {
                    warnedFont = true;
                    Debug.LogWarning("[Press] No TMP default font - the morning edition " +
                                     "cannot be drawn.", this);
                }
                return;
            }

            // A burst still open at the cut belongs to this edition. PressDesk retains
            // the incident number, so later shots become a new continuation record.
            PressDesk.Instance?.FlushOpenIncident();
            underworld.Press.LastEditionDay = editionDay;
            dueDay = 0;

            clock = FindAnyObjectByType<CityClock>();
            if (clock)
            {
                wasPaused = clock.Paused;
                ownsPause = true;
                clock.Paused = true;
            }

            EnsureEventSystem();
            Build(underworld, editionDay);
            IsOpen = true;
            DemoAudio.Ui(DemoSounds.Newspaper);
            Debug.Log("[Press] MORNING EDITION day " + editionDay + " at 06:00");
        }

        void Build(Underworld underworld, int editionDay)
        {
            screen = new GameObject("Morning Edition", typeof(RectTransform));
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

            var sheet = NewRect("Newspaper", screen.transform);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.5f);
            sheet.pivot = new Vector2(0.5f, 0.5f);
            sheet.anchoredPosition = Vector2.zero;
            sheet.sizeDelta = new Vector2(SheetWidth, SheetHeight);

            NewspaperSheet.Paint(sheet, SheetWidth, SheetHeight, underworld.CitySeed,
                editionDay, underworld.Press.Records, NewspaperSheet.CityQuarters(),
                new NewspaperSheet.Controls { Close = Close });
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
