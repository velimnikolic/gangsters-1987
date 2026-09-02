using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The clock readout across the top of the screen: a day/night disc, the hour as HH:MM,
    /// the calendar as "Week #1 - Monday", and the time controls - pause, slower, faster,
    /// the current speed, and an HQ button that flies the strategic map to the outfit's
    /// front.
    ///
    /// Reads CityClock and nothing else - it owns no notion of time, and deliberately does
    /// not own a notion of "night" either. The disc is tinted by CityWeather.Nightness, the
    /// same static the street lamps and the lit windows ramp on, so the icon cools at
    /// exactly the moment the city does. A second threshold here would drift away from the
    /// lighting the first time either was tuned.
    ///
    /// The bar and labels are baked by Tools/City/Add Clock HUD (see CityHudSetup); the
    /// buttons are NOT - they self-build in Awake, the runtime-self-install rule, so a
    /// scene saved before they existed still grows them at Play. That makes this canvas
    /// the project's third with a GraphicRaycaster (ContextMenuUI and the almanac carry
    /// the others); every non-button graphic on it stays raycastTarget=false, so only the
    /// buttons themselves can eat a click.
    /// </summary>
    public sealed class CityClockHud : MonoBehaviour
    {
        [Tooltip("Left empty, this is found in the scene on Awake.")]
        [SerializeField] Ambient.CityClock clock;

        [SerializeField] TMP_Text timeLabel;
        [SerializeField] TMP_Text dayLabel;

        [Tooltip("The disc left of the time. Tinted between the two colours below by how dark it is.")]
        [SerializeField] Image icon;

        [SerializeField] Color dayTint = new(1f, 0.85f, 0.45f);
        [SerializeField] Color nightTint = new(0.72f, 0.80f, 0.95f);

        /// <summary>Day 0 is a Monday - the ledger's week commits run Monday to Sunday.</summary>
        static readonly string[] DayNames =
        {
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        };

        /// <summary>How far the HQ button zooms the map in - forecourt scale, half the
        /// city still in frame for orientation.</summary>
        const float HqFocusOrtho = 45f;

        // What is currently on screen. Whole game minutes, not the raw hour: the label only shows
        // minutes, so redrawing on every fractional change would be a hundred wasted mesh
        // rebuilds a second for a number that has not moved.
        int shownMinute = -1;
        int shownDay = -1;

        TMP_Text pauseLabel;
        TMP_Text speedLabel;
        int shownSpeedIndex = -1;
        bool shownPaused;
        bool shownControls;

        RectTransform lieutenantRows;
        TMP_Text lieutenantHeader;
        bool lieutenantsOpen;

        const float LieutenantPanelWidth = 220f;

        // The bar used to dodge to the right while the ledger held the left half of
        // the screen. The ledger fills the screen now, so there is nowhere to dodge to
        // and the strip simply stays where the scene put it.

        void Awake()
        {
            // The bar was baked into the scene by Tools/City/Add Clock HUD before the skin
            // existed, and menu steps are never re-run here - so the skin goes on at Play,
            // the same self-wiring rule the rest of the runtime layers follow.
            var bar = transform.Find("Bar");
            if (bar && bar.TryGetComponent(out Image barImage))
                UiSkin.TryDress(barImage, UiSkin.PanelRaised);
            // Same reason the bar is skinned here, and not in the menu that baked it: the
            // readout deserialises with TMP's default face, an Arial clone that belongs to
            // no part of this book. The figures take the fixed-pitch face - a proportional
            // one re-centres the whole strip every time a 1 turns into a 4.
            if (timeLabel && LedgerStyle.Mono)
                timeLabel.font = LedgerStyle.Mono;
            if (dayLabel && LedgerStyle.Condensed)
                dayLabel.font = LedgerStyle.Condensed;

            // Same self-heal as StreetLampLights and NightWindows (StreetLampLights.cs:118).
            // The setup menu wires this, but a component added to a scene by hand deserialises
            // with the field empty and would otherwise sit there showing nothing.
            if (!clock)
                clock = FindAnyObjectByType<Ambient.CityClock>();

            if (clock)
            {
                BuildTimeControls();
                BuildLieutenantPanel();
                return;
            }

            // Once, then stop. A missing clock is a setup mistake, not a per-frame event, and a
            // warning in LateUpdate would bury every other message in the Console.
            Debug.LogWarning("[CityClockHud] No CityClock in the scene - the readout is off. " +
                             "Run Tools/City/Add Clock HUD.", this);
            enabled = false;
        }

        // LateUpdate to match the rest of the ambient stack: CityClock advances in Update, so
        // reading it late means the HUD and the sky are showing the same frame's hour.
        void LateUpdate()
        {
            RefreshControls();

            var hour = clock.Hour;
            var minute = Mathf.FloorToInt(hour * 60f);

            if (minute == shownMinute)
                return;

            shownMinute = minute;

            // TMP's SetText writes the formatted value straight into its own char buffer -
            // "{0:00}" means zero-padded to two digits. Assigning .text with an interpolated
            // string instead would allocate a string every game minute, which at a low
            // realSecondsPerGameHour is every frame.
            if (timeLabel)
                timeLabel.SetText("{0:00}:{1:00}", minute / 60, minute % 60);

            if (icon)
                icon.color = Color.Lerp(dayTint, nightTint, Ambient.CityWeather.Nightness(hour));

            // Day 0 is week 1, Monday - nobody counts weeks from zero. Plain .text is fine
            // here: the sentence changes once per game DAY, not per minute.
            if (dayLabel && clock.Day != shownDay)
            {
                shownDay = clock.Day;
                dayLabel.text = "Week #" + (shownDay / 7 + 1) + " - " + DayNames[shownDay % 7];
            }
        }

        // -------------------------------------------------------------- time controls

        /// <summary>
        /// The buttons, appended to the same centred layout row the disc and labels sit
        /// in - the HorizontalLayoutGroup re-centres the whole strip, so nothing here
        /// needs a position. Pause and speed talk to CityClock; HQ talks to the map.
        /// </summary>
        void BuildTimeControls()
        {
            var content = transform.Find("Bar/Content");
            if (!content)
                return;

            // Buttons need the pointer stack the bar was authored without. The raycaster
            // goes on this canvas; the EventSystem is shared and usually already created
            // by ContextMenuUI - but this HUD must not depend on that layer being alive.
            if (!GetComponent<GraphicRaycaster>())
                gameObject.AddComponent<GraphicRaycaster>();
            if (!EventSystem.current)
            {
                var host = new GameObject("EventSystem");
                host.AddComponent<EventSystem>();
                // Not StandaloneInputModule - the legacy module throws under this
                // project's Input System setting (the ContextMenuUI note).
                host.AddComponent<InputSystemUIInputModule>();
            }

            BuildGap(content, 18f);
            pauseLabel = BuildBarButton(content, "II", 34f, () =>
            {
                clock.Paused = !clock.Paused;
                RefreshControls();
            });
            BuildBarButton(content, "<<", 34f, () =>
            {
                clock.SlowDown();
                RefreshControls();
            });
            speedLabel = BuildBarText(content, 44f);
            BuildBarButton(content, ">>", 34f, () =>
            {
                clock.SpeedUp();
                RefreshControls();
            });

            BuildGap(content, 18f);
            BuildBarButton(content, "HQ", 44f, FocusHeadquarters);

            RefreshControls();
        }

        void RefreshControls()
        {
            if (shownControls && shownPaused == clock.Paused &&
                shownSpeedIndex == clock.SpeedIndex)
                return;

            shownControls = true;
            shownPaused = clock.Paused;
            shownSpeedIndex = clock.SpeedIndex;

            // "II" is the universal pause glyph LiberationSans actually has; the real
            // ⏸/▶ symbols are outside the default TMP atlas and would render as boxes.
            if (pauseLabel)
                pauseLabel.text = clock.Paused ? ">" : "II";
            if (speedLabel)
                speedLabel.text = clock.Paused ? "0x" : clock.SpeedMultiplier + "x";
        }

        /// <summary>
        /// Snap to the outfit's front. Before the gang layer has seated the families
        /// there is no HQ and the click does nothing - a silent button beats a warning
        /// every press.
        /// </summary>
        void FocusHeadquarters()
        {
            var outfit = Gameplay.OutfitDirector.Instance;
            if (outfit && outfit.TryGetHeadquarters(out var hq, out _))
                FocusWorld(hq);
        }

        /// <summary>
        /// Mode-preserving focus - the user's rule: whatever view is up stays up. On the
        /// strategic map the map snaps; in the ordinary city the iso rig cuts there. The
        /// buttons never switch views.
        /// </summary>
        static void FocusWorld(Vector3 world)
        {
            if (StrategicMapHud.IsOpen)
            {
                if (StrategicMapHud.Instance)
                    StrategicMapHud.Instance.FocusOn(world, HqFocusOrtho);
                return;
            }

            var rig = FindAnyObjectByType<CameraRig.IsometricCameraController>();
            if (rig)
                rig.FocusOn(world);
        }

        TMP_Text BuildBarButton(Transform content, string label, float width,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform));
            go.transform.SetParent(content, false);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 26f;

            var face = go.AddComponent<Image>();
            face.sprite = null;
            face.color = new Color(1f, 1f, 1f, 0.14f);
            face.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;

            // The Waste No Space chrome, the project's UI base - same dress-or-degrade
            // seam as the ledger: without the sheet the button stays a flat tinted block.
            if (!UiSkin.TryDressButton(button, face))
            {
                var colours = button.colors;
                colours.normalColor = new Color(1f, 1f, 1f, 0.75f);
                colours.highlightedColor = Color.white;
                colours.pressedColor = new Color(0.65f, 0.65f, 0.65f);
                button.colors = colours;
            }
            button.onClick.AddListener(onClick);

            var text = BuildButtonLabel(go.transform);
            text.text = label;
            return text;
        }

        TMP_Text BuildButtonLabel(Transform parent)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (LedgerStyle.Condensed)
                text.font = LedgerStyle.Condensed;
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.92f, 0.92f);
            text.raycastTarget = false;
            return text;
        }

        /// <summary>The "2x" readout between the arrows - a label, not a button.</summary>
        TMP_Text BuildBarText(Transform content, float width)
        {
            var go = new GameObject("Speed", typeof(RectTransform));
            go.transform.SetParent(content, false);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 26f;

            var text = BuildButtonLabel(go.transform);
            text.color = new Color(0.70f, 0.72f, 0.76f);
            return text;
        }

        /// <summary>Invisible spacer - the layout group's spacing is uniform, and the
        /// control cluster reads better held apart from the calendar.</summary>
        static void BuildGap(Transform content, float width)
        {
            var go = new GameObject("Gap", typeof(RectTransform));
            go.transform.SetParent(content, false);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
        }

        // ---------------------------------------------------------- lieutenant list

        /// <summary>
        /// The lieutenants dropdown, top-left and OUTSIDE the bar: a header button and,
        /// while open, one row per crew lieutenant. A row click snaps the strategic map
        /// to where that lieutenant stands. Rows are rebuilt on every open - promotions
        /// in the almanac change the list, and a rebuild of five buttons is nothing.
        /// </summary>
        void BuildLieutenantPanel()
        {
            var root = new GameObject("Lieutenants", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -56f);
            rect.sizeDelta = new Vector2(LieutenantPanelWidth, 26f);

            var face = root.AddComponent<Image>();
            face.sprite = null;
            face.color = new Color(1f, 1f, 1f, 0.14f);
            face.raycastTarget = true;

            var button = root.AddComponent<Button>();
            button.targetGraphic = face;
            if (!UiSkin.TryDressButton(button, face))
            {
                var colours = button.colors;
                colours.normalColor = new Color(1f, 1f, 1f, 0.75f);
                colours.highlightedColor = Color.white;
                colours.pressedColor = new Color(0.65f, 0.65f, 0.65f);
                button.colors = colours;
            }
            button.onClick.AddListener(ToggleLieutenants);

            lieutenantHeader = BuildButtonLabel(root.transform);
            lieutenantHeader.text = "LIEUTENANTS  v";

            // The rows hang off the header's bottom edge; a vertical layout plus a
            // fitter sizes the strip to however many crews exist.
            var rows = new GameObject("Rows", typeof(RectTransform));
            rows.transform.SetParent(root.transform, false);
            lieutenantRows = (RectTransform)rows.transform;
            lieutenantRows.anchorMin = new Vector2(0f, 0f);
            lieutenantRows.anchorMax = new Vector2(1f, 0f);
            lieutenantRows.pivot = new Vector2(0.5f, 1f);
            lieutenantRows.anchoredPosition = new Vector2(0f, -2f);
            // A fresh RectTransform is 100x100; with stretched X anchors that would
            // read as "parent width PLUS 100". Zero it - the fitter owns the height.
            lieutenantRows.sizeDelta = Vector2.zero;

            var backdrop = rows.AddComponent<Image>();
            backdrop.sprite = null;
            backdrop.color = new Color(0f, 0f, 0f, 0.78f);
            backdrop.raycastTarget = true;
            UiSkin.TryDress(backdrop, UiSkin.PanelDark);

            var layout = rows.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = rows.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            rows.SetActive(false);
        }

        void ToggleLieutenants()
        {
            lieutenantsOpen = !lieutenantsOpen;

            // Active BEFORE the rows build - a TMP label created under an inactive
            // parent never runs OnEnable and crashes on its null material (the
            // runtime-HUD trap).
            lieutenantRows.gameObject.SetActive(lieutenantsOpen);
            if (lieutenantsOpen)
                PopulateLieutenants();
            if (lieutenantHeader)
                lieutenantHeader.text = lieutenantsOpen ? "LIEUTENANTS  ^" : "LIEUTENANTS  v";
        }

        void PopulateLieutenants()
        {
            for (var i = lieutenantRows.childCount - 1; i >= 0; i--)
                Destroy(lieutenantRows.GetChild(i).gameObject);

            var roster = Gameplay.PersonnelDirector.Instance
                ? Gameplay.PersonnelDirector.Instance.Roster
                : null;

            var any = false;
            if (roster != null)
                foreach (var crew in roster.Crews)
                {
                    var lieutenant = roster.Find(crew.LieutenantId);
                    if (lieutenant == null)
                        continue;

                    any = true;
                    var id = lieutenant.Id;
                    BuildBarButton(lieutenantRows, lieutenant.FullName,
                        LieutenantPanelWidth - 8f, () => FocusLieutenant(id));
                }

            if (any)
                return;

            // An empty dropdown reads as broken; say why it is empty instead.
            var note = BuildBarText(lieutenantRows, LieutenantPanelWidth - 8f);
            note.text = "No crews yet";
        }

        /// <summary>
        /// Snap the current view to where this lieutenant's street agent stands. The
        /// player's crew agents mirror the ledger roster by Personnel.Character.Id - the
        /// GangMemberIdentity.PersonnelId binding - so the resolve is an id match, and
        /// a lieutenant with no body on the street (roster grown past the spawned crew)
        /// simply does not move the camera.
        /// </summary>
        void FocusLieutenant(int personnelId)
        {
            foreach (var agent in FindObjectsByType<Entities.GangMemberAgent>(
                         FindObjectsSortMode.None))
            {
                if (agent.GangId != Gangs.GangCatalog.PlayerGangId ||
                    agent.Identity == null ||
                    agent.Identity.PersonnelId != personnelId)
                    continue;

                FocusWorld(agent.transform.position);
                ToggleLieutenants();
                return;
            }
        }
    }
}
