using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The city's face: a marker over everything worth explaining - patrol cars, beat officers,
    /// the school bus, the children waiting for it, the drivers using a forecourt, and the two
    /// buildings that run all of it - coloured by what that thing is doing, plus a
    /// click-to-inspect popup that follows it and words its intention in a sentence.
    ///
    /// It knows about none of them. Every subject implements IOverlaySubject and puts itself in
    /// OverlayRegistry; this class draws whatever is in there. It was PoliceOverlayHud until the
    /// school and the bank needed the same treatment - the old shape carried one field per
    /// subject type and branched two ways at four separate sites, which does not survive a sixth
    /// subject. The file was RENAMED rather than replaced so that its guid, and therefore the
    /// component already baked into grad.unity, survives.
    ///
    /// Left-click is the binding the camera controller reserved on purpose - drag-pan is
    /// Space+LMB or MMB precisely so a bare click stays free for selecting units (its comment
    /// says so).
    ///
    /// Everything is screen-space on a canvas this component builds itself, following
    /// BlockOverlayHud to the letter: ScreenSpaceOverlay means a UI transform.position IS
    /// screen pixels, so WorldToScreenPoint feeds it directly, it stays crisp at every zoom
    /// and ignores camera yaw - the two problems a world-space billboard would have to
    /// solve. Its own canvas rather than the editor-built "HUD" object because these subjects
    /// exist only at runtime and the HUD object is not guaranteed to be in the scene at all.
    ///
    /// The no-GraphicRaycaster rule holds here too: nothing on this canvas is clickable
    /// (every Graphic raycastTarget false, no EventSystem). The popup closes on a click
    /// that selects nothing, or on Escape. Status markers stay cheap sprite-less Images;
    /// human hover and selection use a screen-space bracket projected from the ground
    /// square around the person.
    ///
    /// Picking is the project's only runtime physics query, and the two traps are real:
    /// Queries Hit Triggers is ON project-wide and the AI cars carry a trigger feeler box
    /// 3.5m ahead of the body, so the cast must ignore triggers; and the solid collider is
    /// a child (car body / rig / building mesh), so the subject is found with
    /// GetComponentInParent - which does accept an interface type.
    /// A SphereCastAll rather than a single Raycast because a child's capsule is 0.2m
    /// wide - a hair of slack turns pixel-hunting into clicking - and because the nearest
    /// hit may be a civilian shoulder in a crowd; the nearest SUBJECT is what counts.
    /// </summary>
    public sealed class CityOverlayHud : MonoBehaviour
    {
        /// <summary>Marker edge in reference pixels (1080p design height; the scaler keeps
        /// the on-screen size proportional on other displays).</summary>
        const float MarkerSize = 12f;

        /// <summary>A building marker is permanent and there are only two of them, so it can
        /// afford to be a little bigger without crowding anything.</summary>
        const float PlaceMarkerSize = 14f;

        /// <summary>The selected subject's marker grows this much - the "you are here".</summary>
        const float SelectedScale = 1.4f;

        const float HoverInterval = 0.1f;

        /// <summary>Pick slack, metres. Enough to make a 0.2m child capsule clickable,
        /// small enough not to grab the car in the next lane.</summary>
        const float PickRadius = 0.35f;

        /// <summary>The camera boom is 200m; 600 reaches the far corner at full zoom-out.</summary>
        const float PickDistance = 600f;

        const float PopupWidth = 280f;
        const float PopupHeight = 62f;

        /// <summary>Reference pixels between the subject's screen point and the popup's foot.</summary>
        const float PopupLift = 30f;

        /// <summary>Reference pixels of margin an always-visible marker keeps off the edge.</summary>
        const float EdgeMargin = 18f;

        enum MarkerKind { Status, Hover, SelectionBracket }

        sealed class Marker
        {
            public IOverlaySubject Subject;
            public Transform Target;
            public Graphic Graphic;
            public GroundBracketGraphic Bracket;
            public Color Shown;
            public bool Selected;
            public MarkerKind Kind;
            public readonly Vector3[] WorldCorners = new Vector3[4];
            public readonly Vector2[] LocalCorners = new Vector2[4];

            /// <summary>Cached at build time - styles are fixed for a subject's lifetime.</summary>
            public MarkerStyle Style;
        }

        readonly List<Marker> markers = new List<Marker>();
        int registryVersion = -1;

        Camera cam;
        Canvas canvas;
        RectTransform markerRoot;

        GameObject popup;
        RectTransform popupRect;
        TMP_Text popupTitle;
        TMP_Text popupLine;
        Marker selected;
        Marker selectedBracket;
        Marker hoverBracket;
        long shownPopupKey;
        bool popupDirty;
        float nextHoverAt;

        /// <summary>
        /// The one marker that does not come from the registry: built when the selection is
        /// a subject that never registered. Civilians are the case - at crowd scale a
        /// permanent marker Image each is exactly the cost OverlayRegistry must not carry -
        /// so the marker exists only while one of them is the selection, and dies with it.
        /// </summary>
        Marker ephemeral;

        void Start()
        {
            cam = Camera.main ? Camera.main : FindAnyObjectByType<Camera>();
            if (!cam)
            {
                Debug.LogWarning("[CityOverlayHud] No camera in the scene - overlay off.", this);
                enabled = false;
                return;
            }

            BuildCanvas();
            BuildPopup();
        }

        void BuildCanvas()
        {
            var go = new GameObject("City Overlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvasRect = (RectTransform)go.transform;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the clock bar's 100, so the one permanent HUD element wins the corner.
            canvas.sortingOrder = 90;

            // Same scaler as CityHudSetup, so a marker is the same fraction of the screen
            // height on every display the clock bar is.
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var root = new GameObject("Markers", typeof(RectTransform));
            root.transform.SetParent(go.transform, false);
            markerRoot = (RectTransform)root.transform;
            markerRoot.anchorMin = Vector2.zero;
            markerRoot.anchorMax = Vector2.one;
            markerRoot.offsetMin = Vector2.zero;
            markerRoot.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Built once, active, then hidden - the BlockOverlayHud trick: a TextMeshProUGUI
        /// only loads its font in OnEnable, which never runs under an inactive parent.
        /// </summary>
        void BuildPopup()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                // TMP essentials not imported yet (the clock HUD's two-step). Markers still
                // work - they are plain UI Graphics - the popup just has nothing to write with.
                Debug.LogWarning("[CityOverlayHud] No TMP default font - the overlay popup " +
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
            UiSkin.TryDress(background, UiSkin.PanelDark);

            popupTitle = BuildPopupText("Title", top: true);
            if (LedgerStyle.Condensed)
                popupTitle.font = LedgerStyle.Condensed;
            popupTitle.fontSize = 16f;
            popupTitle.fontStyle = FontStyles.Bold;
            popupTitle.color = new Color(0.96f, 0.96f, 0.96f);

            popupLine = BuildPopupText("Line", top: false);
            if (LedgerStyle.Mono)
                popupLine.font = LedgerStyle.Mono;
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
            // The strategic map is modal: its Esc closes the map, its clicks pick blocks,
            // and neither may leak here - InputBlocked covers the closing frame too.
            if (StrategicMapHud.InputBlocked)
            {
                SetHover(null);
                return;
            }

            if (PersonnelAlmanac.IsOpen)
            {
                SetHover(null);
                return;
            }

            // Polled Esc cannot be consumed, so every reader yields explicitly: while the
            // personnel ledger claims it (open, or closing on this very frame - Update
            // order is arbitrary), the press must not also drop the overlay selection.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame &&
                !PersonnelAlmanac.ClaimsEsc)
                Select(null);

            var mouse = Mouse.current;
            if (mouse == null)
            {
                SetHover(null);
                return;
            }

            var pointer = mouse.position.ReadValue();
            var pointerBlocked = keyboard != null && keyboard.spaceKey.isPressed;

            // Space+LMB is the camera's drag-pan - a pan must not open popups as it starts.
            if (pointerBlocked)
                SetHover(null);

            // A click on the context menu (the one canvas with a GraphicRaycaster) belongs
            // to its button, not to the world behind it. Without this the pick punches
            // straight through the menu. No EventSystem means no clickable UI - carry on.
            if (EventSystem.current && EventSystem.current.IsPointerOverGameObject())
            {
                pointerBlocked = true;
                SetHover(null);
            }

            if (!pointerBlocked && Time.unscaledTime >= nextHoverAt)
            {
                nextHoverAt = Time.unscaledTime + HoverInterval;
                SetHover(Pick(pointer));
            }

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            if (pointerBlocked)
                return;

            Select(Pick(pointer));
        }

        IOverlaySubject Pick(Vector2 screenPosition)
        {
            var ray = cam.ScreenPointToRay(screenPosition);
            var hits = Physics.SphereCastAll(
                ray, PickRadius, PickDistance, ~0, QueryTriggerInteraction.Ignore);

            // The subject itself, not its marker: a subject need not be registered to be
            // clickable. Civilians never register - a marker is built for whichever one is
            // selected - and every registered subject picks exactly as it always did.
            IOverlaySubject best = null;
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance >= bestDistance || !hit.collider)
                    continue;

                // A building the occlusion sweep has dropped to its ground stub is
                // still a solid collider, but only the stub is visible - a click on
                // its invisible upper floors belongs to whoever stands behind them.
                if (PlayerOcclusionHider.InvisibleAt(hit.collider, hit.point))
                    continue;

                var subject = hit.collider.GetComponentInParent<IOverlaySubject>();
                if (subject == null || subject.OverlayHidden || !VisionReveals(subject))
                    continue;

                best = subject;
                bestDistance = hit.distance;
            }

            return best;
        }

        void Select(IOverlaySubject subject)
        {
            if (selected != null ? selected.Subject == subject : subject == null)
                return;

            Marker marker = null;
            var offRegistry = false;
            if (subject != null)
            {
                foreach (var m in markers)
                {
                    if (m.Subject != subject)
                        continue;
                    marker = m;
                    break;
                }

                if (marker == null)
                {
                    var anchor = subject.OverlayAnchor;
                    if (anchor)
                    {
                        marker = BuildMarker(subject, anchor);
                        offRegistry = true;
                    }
                }
            }

            // The outgoing selection may have owned the ephemeral - it goes with it.
            DropEphemeral();
            DropSelectedBracket();
            if (offRegistry)
                ephemeral = marker;

            selected = marker;
            if (marker != null && IsHumanSubject(subject))
                selectedBracket = BuildMarker(subject, marker.Target, MarkerKind.SelectionBracket);
            if (hoverBracket != null && subject == hoverBracket.Subject)
                DropHover();

            popupDirty = true;
            if (popup)
                popup.SetActive(marker != null);
        }

        void DropEphemeral()
        {
            if (ephemeral == null)
                return;

            DestroyMarker(ephemeral);
            if (selected == ephemeral)
                selected = null;
            ephemeral = null;
        }

        void DropSelectedBracket()
        {
            if (selectedBracket == null)
                return;

            DestroyMarker(selectedBracket);
            selectedBracket = null;
        }

        void SetHover(IOverlaySubject subject)
        {
            if (subject != null && (selected != null && subject == selected.Subject ||
                                    !IsHumanSubject(subject)))
                subject = null;

            if (hoverBracket != null && hoverBracket.Subject == subject)
                return;

            DropHover();
            var anchor = subject?.OverlayAnchor;
            if (anchor)
                hoverBracket = BuildMarker(subject, anchor, MarkerKind.Hover);
        }

        void DropHover()
        {
            if (hoverBracket == null)
                return;

            DestroyMarker(hoverBracket);
            hoverBracket = null;
        }

        static void DestroyMarker(Marker marker)
        {
            if (marker == null || !marker.Graphic)
                return;

            Destroy(marker.Graphic.gameObject);
        }

        void LateUpdate()
        {
            // Marker positions come from the iso camera, which the strategic map has
            // disabled - projecting through it would scatter stale markers over the map.
            // Hide whatever is lit and stand down; the normal pass re-enables everything
            // the first frame after the map closes.
            if (StrategicMapHud.IsOpen)
            {
                foreach (var marker in markers)
                    DisableMarker(marker);
                DisableMarker(ephemeral);
                DisableMarker(selectedBracket);
                DisableMarker(hoverBracket);
                if (popup && popup.activeSelf)
                    popup.SetActive(false);
                return;
            }

            SyncMarkers();

            var width = Screen.width;
            var height = Screen.height;

            foreach (var marker in markers)
                UpdateMarker(marker, width, height);

            // The off-registry selection's marker rides the same pass - it is a Marker like
            // any other, just owned by the selection instead of the registry.
            if (ephemeral != null)
                UpdateMarker(ephemeral, width, height);
            if (selectedBracket != null)
                UpdateMarker(selectedBracket, width, height);
            if (hoverBracket != null)
                UpdateMarker(hoverBracket, width, height);

            UpdatePopup(width, height);
        }

        static void DisableMarker(Marker marker)
        {
            if (marker == null || !marker.Graphic)
                return;

            if (marker.Graphic.enabled)
                marker.Graphic.enabled = false;
        }

        void UpdateMarker(Marker marker, float width, float height)
        {
            if (!marker.Target)
                return;

            if (marker.Kind == MarkerKind.Status &&
                ((selectedBracket != null && selectedBracket.Subject == marker.Subject) ||
                 (hoverBracket != null && hoverBracket.Subject == marker.Subject)))
            {
                DisableMarker(marker);
                return;
            }

            // A SelectedOnly marker earns its pixels only as the selection; unselected it
            // pays nothing either - the early-out is before the WorldToScreenPoint and the
            // OverlayColor read, so a hundred quiet businesses cost this loop nothing.
            if (marker.Style.SelectedOnly && marker != selected &&
                marker.Kind == MarkerKind.Status)
            {
                DisableMarker(marker);
                return;
            }

            if (marker.Bracket)
            {
                UpdateGroundBracket(marker, width, height);
                return;
            }

            var hidden = marker.Subject.OverlayHidden || !VisionReveals(marker.Subject);
            var screen = cam.WorldToScreenPoint(
                marker.Target.position + Vector3.up * marker.Subject.OverlayHeight);

            // Same off-screen rule as the block labels: toggle the Graphic, never
            // SetActive - a canvas layout rebuild per edge crossing is the alternative.
            var on = !hidden && screen.z > 0f &&
                     screen.x >= 0f && screen.x <= width &&
                     screen.y >= 0f && screen.y <= height;

            if (marker.Style.AlwaysVisible && !hidden && !on)
            {
                // The rig is orthographic, so a ground-level subject is never behind the
                // camera - off-screen only ever means panned away. This marker's whole
                // job is to never be lost, so it clamps into an edge margin instead of
                // culling; the margin is in reference pixels like every other size here.
                var margin = EdgeMargin * canvas.scaleFactor;
                screen.x = Mathf.Clamp(screen.x, margin, width - margin);
                screen.y = Mathf.Clamp(screen.y, margin, height - margin);
                screen.z = 0f;
                on = true;
            }

            if (marker.Graphic.enabled != on)
                marker.Graphic.enabled = on;

            if (on)
            {
                screen.z = 0f;
                marker.Graphic.transform.position = screen;
            }

            var colour = marker.Subject.OverlayColor;
            if (marker.Shown != colour)
            {
                marker.Shown = colour;
                marker.Graphic.color = colour;
            }

            var wantSelected = marker == selected;
            if (marker.Selected != wantSelected)
            {
                marker.Selected = wantSelected;
                if (!marker.Style.Pulse)
                    marker.Graphic.rectTransform.localScale =
                        Vector3.one * (wantSelected ? SelectedScale : 1f);
            }

            if (marker.Style.Pulse && on)
            {
                // One Sin per pulsing marker per frame - exactly one subject pulses
                // today. SizeScale was baked into sizeDelta at build, so localScale
                // carries only the selection factor and the breath.
                var beat = 1f + marker.Style.PulseAmplitude * Mathf.Sin(
                    Time.time * (2f * Mathf.PI / Mathf.Max(0.2f, marker.Style.PulsePeriod)));
                marker.Graphic.rectTransform.localScale =
                    Vector3.one * (beat * (wantSelected ? SelectedScale : 1f));
            }
        }

        void UpdateGroundBracket(Marker marker, float width, float height)
        {
            if (marker.Subject.OverlayHidden || !VisionReveals(marker.Subject))
            {
                DisableMarker(marker);
                return;
            }

            if (!HumanGroundBracket.TryProject(
                    cam, markerRoot, marker.Target,
                    marker.WorldCorners, marker.LocalCorners,
                    width, height))
            {
                DisableMarker(marker);
                return;
            }

            var selectedHuman = marker.Kind == MarkerKind.SelectionBracket;
            var own = IsOwnHuman(marker.Subject);
            if (!marker.Graphic.enabled)
                marker.Graphic.enabled = true;
            marker.Bracket.Set(
                marker.LocalCorners,
                HumanGroundBracket.ArmLength(selectedHuman, selectedHuman && own, Time.unscaledTime),
                HumanGroundBracket.Thickness,
                HumanGroundBracket.Tint(own));
        }

        static bool VisionReveals(IOverlaySubject subject)
        {
            if (subject == null || subject.MarkerShape == OverlayShape.Square)
                return true;
            var anchor = subject.OverlayAnchor;
            return anchor == null || MapVisionRegistry.IsRevealed(anchor.position);
        }

        static bool IsOwnHuman(IOverlaySubject subject)
        {
            var component = subject as Component;
            if (!component)
                return false;

            var member = component.GetComponentInParent<GangMemberAgent>();
            return member && member.GangId == GangCatalog.PlayerGangId;
        }

        static bool IsHumanSubject(IOverlaySubject subject)
        {
            var component = subject as Component;
            if (!component)
                return false;

            return component.GetComponentInParent<PedestrianAgent>() ||
                   component.GetComponentInParent<SchoolChildAgent>() ||
                   component.GetComponentInParent<GangMemberAgent>() ||
                   component.GetComponentInParent<NpcWitness>() ||
                   component.GetComponentInParent<InteractableNpc>();
        }

        void UpdatePopup(float width, float height)
        {
            if (selected == null || !popup)
                return;

            // The subject was destroyed (a customer drove off the map) - nothing left to
            // describe. A subject going HIDDEN is not that: the popup stays up over the door
            // saying "Inside the station", which is exactly the intention worth reading.
            if (!selected.Target)
            {
                Select(null);
                return;
            }

            RefreshPopupText();

            // Reference-pixel sizes reach the screen multiplied by the scaler's factor.
            // The card hangs off the subject and is not drawn at all once the subject is
            // outside the view: clamped into the viewport it used to sit on the edge and
            // ride along with the camera, over whatever street happened to be there.
            var scale = canvas.scaleFactor;
            if (!OverlayCard.TryPlace(
                    cam, selected.Target.position + Vector3.up * selected.Subject.OverlayHeight,
                    PopupLift * scale, new Vector2(PopupWidth * scale, PopupHeight * scale),
                    width, height, out var position))
            {
                if (popup.activeSelf)
                    popup.SetActive(false);
                return;
            }
            if (!popup.activeSelf)
                popup.SetActive(true);
            popupRect.position = position;
        }

        void RefreshPopupText()
        {
            var key = selected.Subject.OverlayKey;
            if (!popupDirty && key == shownPopupKey)
                return;

            shownPopupKey = key;
            popupDirty = false;

            // .text, not SetText: the sentence itself changes, and SetText formats only
            // numbers. One small allocation per state CHANGE of the selected subject - rare.
            popupTitle.text = selected.Subject.OverlayTitle;
            popupLine.text = selected.Subject.OverlayLine;
        }

        /// <summary>
        /// Rebuilds the marker set when the registry changes. A full teardown is affordable
        /// because it is rare - a handful of times at session start, then once per customer
        /// arriving or leaving - and because getting incremental right is not worth it for a
        /// list this short.
        /// </summary>
        void SyncMarkers()
        {
            if (OverlayRegistry.Version == registryVersion)
                return;

            registryVersion = OverlayRegistry.Version;

            // The selection has to SURVIVE the rebuild. The police-only overlay could drop it -
            // its registries were filled once at session start and never changed again - but the
            // forecourts churn: a bank customer arrives or leaves every half minute, and a popup
            // that shut itself every time some unrelated car reached the map edge would be
            // unusable on exactly the buildings this overlay was added for.
            var wasSelected = selected?.Subject;

            foreach (var marker in markers)
                DestroyMarker(marker);
            markers.Clear();
            selected = null;

            foreach (var subject in OverlayRegistry.Subjects)
            {
                var anchor = subject?.OverlayAnchor;
                if (!anchor)
                    continue;

                var marker = BuildMarker(subject, anchor);
                markers.Add(marker);

                if (subject == wasSelected)
                    selected = marker;
            }

            // The ephemeral is not in the registry, so the rebuild above cannot re-find it -
            // an off-registry selection (a civilian) has to survive registry churn by hand,
            // or every bank customer arriving would close the popup. If its subject somehow
            // joined the registry, the registry marker just won the selection: one marker.
            if (ephemeral != null)
            {
                if (selected == null && wasSelected == ephemeral.Subject)
                    selected = ephemeral;
                else
                    DropEphemeral();
            }

            // Dropped rather than kept: the thing being described has gone.
            if (popup)
                popup.SetActive(selected != null);

            if (selected != null && IsHumanSubject(selected.Subject))
            {
                DropSelectedBracket();
                selectedBracket = BuildMarker(
                    selected.Subject, selected.Target, MarkerKind.SelectionBracket);
            }
            else
            {
                DropSelectedBracket();
            }

            // The graphic is new even when the subject is not, so its colour and scale have to be
            // written again - the next LateUpdate pass does both off Shown/Selected, which
            // BuildMarker leaves cleared.
            popupDirty = true;
        }

        Marker BuildMarker(IOverlaySubject subject, Transform anchor, MarkerKind kind = MarkerKind.Status)
        {
            var go = new GameObject("marker", typeof(RectTransform));
            go.transform.SetParent(markerRoot, false);

            var place = subject.MarkerShape == OverlayShape.Square;
            var groundBracket = kind != MarkerKind.Status && IsHumanSubject(subject);

            // Styles are opt-in: everything that predates them keeps Default.
            var style = subject is IOverlayStyledSubject styled
                ? styled.MarkerStyle
                : MarkerStyle.Default;
            if (style.SizeScale <= 0f)
                style.SizeScale = 1f;

            var size = (place ? PlaceMarkerSize : MarkerSize) * style.SizeScale;

            Graphic graphic;
            GroundBracketGraphic bracket = null;
            if (groundBracket)
            {
                bracket = go.AddComponent<GroundBracketGraphic>();
                graphic = bracket;
                var rect = graphic.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                var image = go.AddComponent<Image>();
                image.sprite = null;
                image.rectTransform.sizeDelta = new Vector2(size, size);
                // The whole Sims trick: a square Image with no sprite, stood on its corner. A
                // building keeps it square-on, which is the only difference between the two shapes.
                image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, place ? 0f : 45f);
                graphic = image;
            }

            graphic.raycastTarget = false;
            graphic.enabled = false;

            return new Marker
            {
                Subject = subject,
                Target = anchor,
                Graphic = graphic,
                Bracket = bracket,
                Shown = Color.clear,
                Kind = kind,
                Style = style,
            };
        }

    }
}
