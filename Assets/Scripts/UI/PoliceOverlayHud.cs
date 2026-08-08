using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using LivingCity.Entities;

namespace LivingCity.UI
{
    /// <summary>
    /// The police layer's face: a Sims-style diamond over every patrol car and beat officer,
    /// coloured by what the unit is doing (see PoliceIntention), and a click-to-inspect
    /// popup that follows the clicked unit and words its intention. Left-click is the
    /// binding the camera controller reserved on purpose - drag-pan is Space+LMB or MMB
    /// precisely so a bare click stays free for selecting units (its comment says so).
    ///
    /// Everything is screen-space on a canvas this component builds itself, following
    /// BlockOverlayHud to the letter: ScreenSpaceOverlay means a UI transform.position IS
    /// screen pixels, so WorldToScreenPoint feeds it directly, it stays crisp at every zoom
    /// and ignores camera yaw - the two problems a world-space billboard would have to
    /// solve. Its own canvas rather than the editor-built "HUD" object because police units
    /// exist only at runtime and the HUD object is not guaranteed to be in the scene at all.
    ///
    /// The no-GraphicRaycaster rule holds here too: nothing on this canvas is clickable
    /// (every Graphic raycastTarget false, no EventSystem). The popup closes on a click
    /// that selects nothing, or on Escape. The diamond is an Image with NO sprite - a
    /// sprite-less Image draws a solid rectangle - rotated 45 degrees; zero new art.
    ///
    /// Picking is the project's first runtime physics query, and the two traps are real:
    /// Queries Hit Triggers is ON project-wide and the AI cars carry a trigger feeler box
    /// 3.5m ahead of the body, so the cast must ignore triggers; and the solid collider is
    /// a child (car body / officer rig), so the agent is found with GetComponentInParent.
    /// A SphereCastAll rather than a single Raycast because an officer's capsule is 0.2m
    /// wide - a hair of slack turns pixel-hunting into clicking - and because the nearest
    /// hit may be a civilian shoulder in a crowd; the nearest POLICE hit is what counts.
    /// </summary>
    public sealed class PoliceOverlayHud : MonoBehaviour
    {
        /// <summary>Diamond anchor heights, metres above the unit's origin. The car's roof
        /// sits ~1.6m up; the officer's head ~1.9m. Both floats a little clear.</summary>
        const float CarMarkerHeight = 2.6f;
        const float OfficerMarkerHeight = 2.3f;

        /// <summary>Diamond edge in reference pixels (1080p design height; the scaler keeps
        /// the on-screen size proportional on other displays).</summary>
        const float MarkerSize = 12f;

        /// <summary>The selected unit's diamond grows this much - the "you are here".</summary>
        const float SelectedScale = 1.4f;

        /// <summary>Pick slack, metres. Enough to make the 0.2m officer capsule clickable,
        /// small enough not to grab the car in the next lane.</summary>
        const float PickRadius = 0.35f;

        /// <summary>The camera boom is 200m; 600 reaches the far corner at full zoom-out.</summary>
        const float PickDistance = 600f;

        const float PopupWidth = 250f;
        const float PopupHeight = 62f;

        /// <summary>Reference pixels between the unit's screen point and the popup's foot.</summary>
        const float PopupLift = 30f;

        sealed class Marker
        {
            public PolicePatrolAgent Car;
            public PoliceOfficerAgent Officer;
            public Transform Target;
            public Image Image;
            public float Height;
            public Color Shown;
            public bool Selected;
        }

        readonly List<Marker> markers = new List<Marker>();
        int fleetSeen = -1;
        int officersSeen = -1;

        Camera cam;
        Canvas canvas;
        RectTransform markerRoot;

        GameObject popup;
        RectTransform popupRect;
        TMP_Text popupTitle;
        TMP_Text popupLine;
        Marker selected;
        long shownPopupKey = long.MinValue;

        void Start()
        {
            cam = Camera.main ? Camera.main : FindAnyObjectByType<Camera>();
            if (!cam)
            {
                Debug.LogWarning("[PoliceOverlayHud] No camera in the scene - overlay off.", this);
                enabled = false;
                return;
            }

            BuildCanvas();
            BuildPopup();
        }

        void BuildCanvas()
        {
            var go = new GameObject("Police Overlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the clock bar's 100, so the one permanent HUD element wins the corner.
            canvas.sortingOrder = 90;

            // Same scaler as CityHudSetup, so a diamond is the same fraction of the screen
            // height on every display the clock bar is.
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var root = new GameObject("Markers", typeof(RectTransform));
            root.transform.SetParent(go.transform, false);
            markerRoot = (RectTransform)root.transform;
        }

        /// <summary>
        /// Built once, active, then hidden - the BlockOverlayHud trick: a TextMeshProUGUI
        /// only loads its font in OnEnable, which never runs under an inactive parent.
        /// </summary>
        void BuildPopup()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                // TMP essentials not imported yet (the clock HUD's two-step). Diamonds still
                // work - they are plain Images - the popup just has nothing to write with.
                Debug.LogWarning("[PoliceOverlayHud] No TMP default font - the police popup " +
                                 "is disabled until TMP essentials are imported.", this);
                return;
            }

            popup = new GameObject("Popup", typeof(RectTransform));
            popup.transform.SetParent(canvas.transform, false);

            popupRect = (RectTransform)popup.transform;
            popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            popupRect.pivot = new Vector2(0.5f, 0f);

            var background = popup.AddComponent<Image>();
            background.sprite = null;
            background.color = new Color(0f, 0f, 0f, 0.78f);
            background.raycastTarget = false;

            popupTitle = BuildPopupText("Title", top: true);
            popupTitle.fontSize = 16f;
            popupTitle.fontStyle = FontStyles.Bold;
            popupTitle.color = new Color(0.96f, 0.96f, 0.96f);

            popupLine = BuildPopupText("Line", top: false);
            popupLine.fontSize = 13f;
            popupLine.color = new Color(0.85f, 0.85f, 0.85f);

            popup.SetActive(false);
        }

        TMP_Text BuildPopupText(string name, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(popup.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rect.offsetMin = new Vector2(12f, top ? 0f : 7f);
            rect.offsetMax = new Vector2(-12f, top ? -7f : 0f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Select(null);

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            // Space+LMB is the camera's drag-pan - a pan must not open popups as it starts.
            if (keyboard != null && keyboard.spaceKey.isPressed)
                return;

            Select(Pick(mouse.position.ReadValue()));
        }

        Marker Pick(Vector2 screenPosition)
        {
            var ray = cam.ScreenPointToRay(screenPosition);
            var hits = Physics.SphereCastAll(
                ray, PickRadius, PickDistance, ~0, QueryTriggerInteraction.Ignore);

            Marker best = null;
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance >= bestDistance || !hit.collider)
                    continue;

                var car = hit.collider.GetComponentInParent<PolicePatrolAgent>();
                var officer = car ? null : hit.collider.GetComponentInParent<PoliceOfficerAgent>();
                if (!car && !officer)
                    continue;
                if (officer && officer.Hidden)
                    continue;

                foreach (var marker in markers)
                {
                    if ((car && marker.Car == car) || (officer && marker.Officer == officer))
                    {
                        best = marker;
                        bestDistance = hit.distance;
                        break;
                    }
                }
            }

            return best;
        }

        void Select(Marker marker)
        {
            if (selected == marker)
                return;

            selected = marker;
            shownPopupKey = long.MinValue;
            if (popup)
                popup.SetActive(marker != null);
        }

        void LateUpdate()
        {
            SyncMarkers();

            var width = Screen.width;
            var height = Screen.height;

            foreach (var marker in markers)
            {
                if (!marker.Target)
                    continue;

                var hidden = marker.Officer && marker.Officer.Hidden;
                var screen = cam.WorldToScreenPoint(
                    marker.Target.position + Vector3.up * marker.Height);

                // Same off-screen rule as the block labels: toggle the Graphic, never
                // SetActive - a canvas layout rebuild per edge crossing is the alternative.
                var on = !hidden && screen.z > 0f &&
                         screen.x >= 0f && screen.x <= width &&
                         screen.y >= 0f && screen.y <= height;

                if (marker.Image.enabled != on)
                    marker.Image.enabled = on;

                if (on)
                {
                    screen.z = 0f;
                    marker.Image.transform.position = screen;
                }

                var colour = marker.Car
                    ? PoliceIntention.CarColor(marker.Car.CurrentState)
                    : PoliceIntention.OfficerColor(marker.Officer.CurrentState);
                if (marker.Shown != colour)
                {
                    marker.Shown = colour;
                    marker.Image.color = colour;
                }

                var wantSelected = marker == selected;
                if (marker.Selected != wantSelected)
                {
                    marker.Selected = wantSelected;
                    marker.Image.rectTransform.localScale =
                        Vector3.one * (wantSelected ? SelectedScale : 1f);
                }
            }

            UpdatePopup(width, height);
        }

        void UpdatePopup(float width, float height)
        {
            if (selected == null || !popup)
                return;

            // The unit was destroyed (a city rebuilt in Play) - nothing left to describe.
            // An officer going HIDDEN is not that: the popup stays up over the door saying
            // "Inside the station", which is exactly the intention worth reading.
            if (!selected.Target)
            {
                Select(null);
                return;
            }

            RefreshPopupText();

            var screen = cam.WorldToScreenPoint(
                selected.Target.position + Vector3.up * selected.Height);
            if (screen.z <= 0f)
            {
                popup.SetActive(false);
                return;
            }
            if (!popup.activeSelf)
                popup.SetActive(true);

            // Reference-pixel sizes reach the screen multiplied by the scaler's factor.
            var scale = canvas.scaleFactor;
            var halfWidth = PopupWidth * 0.5f * scale;
            var popupHeight = PopupHeight * scale;

            var position = new Vector3(
                Mathf.Clamp(screen.x, halfWidth, width - halfWidth),
                Mathf.Clamp(screen.y + PopupLift * scale, 0f, height - popupHeight),
                0f);
            popupRect.position = position;
        }

        void RefreshPopupText()
        {
            long key;
            if (selected.Car)
                key = ((long)selected.Car.CurrentState << 32) | (uint)selected.Car.RoutesRemaining;
            else
                key = long.MinValue + 1
                    + (((long)selected.Officer.CurrentState << 32) | (uint)selected.Officer.RoutesRemaining);

            if (key == shownPopupKey)
                return;

            shownPopupKey = key;

            // .text, not SetText: the sentence itself changes, and SetText formats only
            // numbers. One small allocation per state CHANGE of the selected unit - rare.
            if (selected.Car)
            {
                popupTitle.text = PoliceIntention.CarTitle(selected.Car.UnitNumber);
                popupLine.text = PoliceIntention.CarIntention(
                    selected.Car.CurrentState, selected.Car.RoutesRemaining);
            }
            else
            {
                popupTitle.text = PoliceIntention.OfficerTitle(selected.Officer.UnitNumber);
                popupLine.text = PoliceIntention.OfficerIntention(
                    selected.Officer.CurrentState, selected.Officer.RoutesRemaining);
            }
        }

        /// <summary>
        /// Rebuilds the marker set when the police population changes - which happens a
        /// handful of times at session start as the director spawns, and then never, so a
        /// count comparison is the whole change detector.
        /// </summary>
        void SyncMarkers()
        {
            if (PolicePatrolAgent.Fleet.Count == fleetSeen
                && PoliceOfficerAgent.Officers.Count == officersSeen)
                return;

            fleetSeen = PolicePatrolAgent.Fleet.Count;
            officersSeen = PoliceOfficerAgent.Officers.Count;

            foreach (var marker in markers)
                if (marker.Image)
                    Destroy(marker.Image.gameObject);
            markers.Clear();
            Select(null);

            foreach (var car in PolicePatrolAgent.Fleet)
                markers.Add(BuildMarker(car.transform, CarMarkerHeight, car, null));
            foreach (var officer in PoliceOfficerAgent.Officers)
                markers.Add(BuildMarker(officer.transform, OfficerMarkerHeight, null, officer));
        }

        Marker BuildMarker(
            Transform target, float markerHeight, PolicePatrolAgent car, PoliceOfficerAgent officer)
        {
            var go = new GameObject("marker", typeof(RectTransform));
            go.transform.SetParent(markerRoot, false);

            var image = go.AddComponent<Image>();
            image.sprite = null;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(MarkerSize, MarkerSize);
            // The whole Sims trick: a square Image with no sprite, stood on its corner.
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            image.enabled = false;

            return new Marker
            {
                Car = car,
                Officer = officer,
                Target = target,
                Image = image,
                Height = markerHeight,
                Shown = Color.clear,
            };
        }
    }
}
