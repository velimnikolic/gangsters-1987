using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using LivingCity.Ambient;
using LivingCity.Entities;
using LivingCity.Gameplay;
using LivingCity.Generation;

namespace LivingCity.UI
{
    /// <summary>
    /// The strategic map: press M and the whole city is under you, live. Not a painted
    /// chart - the actual city, rendered by a second top-down orthographic camera while
    /// the isometric rig sits disabled, so every block shows exactly what stands on it
    /// and the traffic keeps moving. People are NOT rendered (their layer is culled);
    /// they appear as dots on a screen-space canvas instead, and only while inside the
    /// player's fog of war (MapVisionRegistry): police blue, civilians white, the player
    /// a gold square. MapAffiliation is the seam a future gang layer colours dots through.
    ///
    /// Clicking a BUILDING selects it - building by building, never a whole block - and
    /// fills the right-rail card: what it is, which family controls the ground it stands
    /// on (the territory ledger, in gang colour), its business or civic role, its block.
    ///
    /// Block identity comes from the ground slab names ("ground_{zone}_{blockId}"), the
    /// one trace of CityGrid that survives into a saved scene - the BlockOverlayHud parse,
    /// here kept with each slab's renderer bounds so blocks are clickable rectangles.
    ///
    /// Modality follows the personnel ledger's convention, not its mechanism: this canvas
    /// has NO GraphicRaycaster (nothing on it is a button), so the pointer half of the
    /// shield is InputBlocked, which InteractionController and CityOverlayHud check the
    /// way they check the almanac. Esc is polled and cannot be consumed, so InputBlocked
    /// stays true through the closing frame - the ClaimsEsc rule - and this reader in
    /// turn yields to PersonnelAlmanac, which sorts above (110 over this 105).
    ///
    /// While open, Camera.main is null on purpose - the map camera is untagged, and every
    /// Camera.main consumer in the project caches its camera or tolerates null. The fog
    /// CityWeather re-applies per frame is gated by CityWeather.FogSuppressed (a camera
    /// 300m up would otherwise be inside the smog); clouds and birds move to a cull-only
    /// layer because they live exactly between this camera and the ground.
    /// </summary>
    public sealed class StrategicMapHud : MonoBehaviour, IMapTargetingSurface
    {
        /// <summary>Above the clock bar's 100, below the personnel ledger's 110 - the
        /// book must stay readable if P is pressed over the map.</summary>
        const int SortingOrder = 105;

        /// <summary>Well above the tallest roof and the 50-100m cloud band's old home;
        /// the far plane still reaches the ground with margin.</summary>
        const float CameraHeight = 300f;

        /// <summary>Closest the map zooms, metres of half-height - street level, where
        /// individual forecourts read. The far limit is whatever fits the whole city.</summary>
        const float MinOrtho = 15f;

        /// <summary>Proportional zoom step per scroll EVENT, sign-only - raw scroll deltas
        /// are wildly platform-dependent (the iso rig documents this), so a fixed fraction
        /// per event behaves the same on a wheel notch and a trackpad flick.</summary>
        const float ZoomStep = 0.06f;

        /// <summary>Reference pan speed at ortho 40, scaled with zoom so the city drifts
        /// past at a consistent visual rate - the iso rig's keyboardPanSpeed rule.</summary>
        const float PanSpeed = 60f;

        const float MoveSmoothing = 12f;
        const float ZoomSmoothing = 10f;

        /// <summary>Runtime-only layer the map camera culls; clouds and birds are moved
        /// here on open so they stop floating between the lens and the city. Unnamed in
        /// the project's layer table - nothing else uses it.</summary>
        const int HiddenAmbientLayer = 30;

        /// <summary>Reference pixels. A person at city scale is under a pixel; the dot is
        /// deliberately a beacon, not a portrait.</summary>
        const float DotSize = 6f;


        /// <summary>The outfit's front reads as a place, not a person - bigger than any
        /// dot, with the family name floating over it.</summary>
        const float HqMarkerSize = 16f;

        /// <summary>Hard ceiling on drawn dots. Every dot is a uGUI Image and moving one
        /// re-batches the canvas - fine in the hundreds, a stutter machine in the
        /// thousands, and the crowd can BE thousands when no vision source restricts the
        /// map. Police are emitted before civilians so the cap never swallows a blue.</summary>
        const int MaxDots = 600;

        const float CardWidth = 360f;
        const float CardHeight = 660f;
        const float CardMargin = 16f;

        static readonly Color PoliceBlue = new Color(0.25f, 0.55f, 1f);
        static readonly Color CivilianWhite = Color.white;
        static readonly Color PlayerGold = new Color(1f, 0.84f, 0.25f);
        static readonly Color MapBackdrop = new Color(0.045f, 0.055f, 0.075f);

        /// <summary>True while the map is open - the keyboard half of the modal shield,
        /// same convention as PersonnelAlmanac.IsOpen.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while open AND on the frame the map closes: polled input cannot
        /// be consumed and Update order is arbitrary, so the click or Esc that closed the
        /// map must not also act in the world - the almanac's ClaimsEsc rule.</summary>
        public static bool InputBlocked => IsOpen || Time.frameCount == lastCloseFrame;

        static int lastCloseFrame = -1;

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            IsOpen = false;
            lastCloseFrame = -1;
            Instance = null;
            // The targeting seam is MapTargeting's now, and it resets itself.
        }

        /// <summary>The scene's map, for the ledger's Orders page - which must open the
        /// map and read its camera without a scene find per frame.</summary>
        public static StrategicMapHud Instance { get; private set; }

        /// <summary>
        /// The order-targeting seam lives in <see cref="MapTargeting"/> now, because
        /// this is no longer the only map that can serve a pick: while a consumer is
        /// set, LMB belongs to it - a drag becomes an area box (world-XZ rect), a click
        /// a point - and the map's own card selection stands down. Clicks over
        /// clickable UI (the ledger's panel has the raycaster) are ignored here.
        /// </summary>
        static IMapTargetingConsumer Targeting => MapTargeting.Consumer;

        public Camera MapCamera => mapCamera;

        void Awake()
        {
            Instance = this;
            // The generated city's map. Where a turf plate exists it outranks this one
            // and the ledger picks blocks there instead.
            MapTargeting.Register(this, MapTargeting.CameraMapRank);
            MapTargeting.Changed += RefreshHint;
        }

        // ------------------------------------------------- IMapTargetingSurface

        bool IMapTargetingSurface.IsShowing => IsOpen;

        /// <summary>This map IS a screen: the ledger opens and closes it.</summary>
        bool IMapTargetingSurface.CanSummon => true;

        bool IMapTargetingSurface.Summon()
        {
            Open();
            return IsOpen;
        }

        void IMapTargetingSurface.Dismiss() => Close();

        string IMapTargetingSurface.SummonHint => "";

        sealed class Block
        {
            public int Id;
            public BlockZone Zone;

            /// <summary>World-XZ union of the block's slabs - the highlight rectangle.</summary>
            public Rect Union;

            /// <summary>One rect per slab. Ordinary blocks have one; the park lays a slab
            /// per cell, and a click anywhere on any of them must land.</summary>
            public readonly List<Rect> Slabs = new List<Rect>();

            public readonly List<string> Landmarks = new List<string>();
        }

        struct Tracked
        {
            public IOverlaySubject Subject;
            public Component Body;
        }

        // ------------------------------------------------------------------ wiring

        Camera isoCamera;
        Behaviour isoController;
        Behaviour occlusionHider;
        Camera mapCamera;

        Canvas canvas;
        GameObject page;

        /// <summary>The screen region the map owns: the whole screen on M, the right
        /// half while the ledger stands open beside it. Every overlay layer lives under
        /// it, and its RectMask2D keeps dots and washes from bleeding onto the book.</summary>
        RectTransform mapArea;
        bool docked;

        Image buildingHighlight;
        RectTransform cardRect;
        TMP_Text cardTitle;
        TMP_Text cardBody;
        TMP_Text hqLabel;
        TMP_Text hintText;
        bool tmpReady;

        readonly List<Image> dotPool = new List<Image>();
        RectTransform dotRoot;
        int dotCursor;

        readonly List<Image> territoryTiles = new List<Image>();
        readonly List<Outfit.Turf.Holding> holdingsScratch = new List<Outfit.Turf.Holding>();
        RectTransform territoryRoot;

        /// <summary>Each held building's footprint, measured once: the wash reprojects
        /// every frame, and a GetComponent per premise per frame was what it cost.
        /// Buildings do not move, so a rect is good until the registry changes.</summary>
        readonly Dictionary<Entities.BusinessMarker, Rect> _footprints =
            new Dictionary<Entities.BusinessMarker, Rect>();
        int _footprintsVersion = -1;

        readonly List<Rect> targetRects = new List<Rect>();
        readonly List<Image> targetTiles = new List<Image>();
        RectTransform targetRoot;
        Color targetColor = Color.white;
        Image dragBox;

        readonly Dictionary<int, Block> blocks = new Dictionary<int, Block>();
        readonly List<(int blockId, Vector3 position)> slabCentres =
            new List<(int, Vector3)>();
        Bounds cityBounds;
        bool built;
        bool warnedEmpty;

        // Map navigation state - the iso rig's target/smoothed pair, without the yaw.
        float fitOrtho;
        float orthoSize;
        float targetOrtho;
        Vector3 focus;
        Vector3 targetFocus;

        int selectedBlockId = -1;
        Transform selectedBuilding;
        Rect selectedBuildingRect;
        Transform buildingsRoot;
        bool cardDirty;
        int paintedPropertyVersion = -1;
        int paintedOutfitVersion = -1;
        bool visionUnrestricted;

        readonly List<Tracked> policeDots = new List<Tracked>();
        readonly List<Tracked> extraDots = new List<Tracked>();
        int overlayVersion = -1;
        DockWorkerAgent[] dockWorkers = System.Array.Empty<DockWorkerAgent>();

        readonly StringBuilder text = new StringBuilder(512);

        // ------------------------------------------------------------------- input

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // The personnel ledger is modal above everything - while it reads, M belongs
            // to nobody, exactly as InteractionController stands down for it. There is
            // no exception any more: the war-room split that kept this map live beside
            // the book is retired, and the file now covers the glass.
            if (PersonnelAlmanac.IsOpen)
                return;

            if (!PersonnelAlmanac.IsOpen && keyboard.mKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }

            if (!IsOpen)
                return;

            // Yield on ClaimsEsc, not IsOpen: the book may have closed THIS frame, and
            // that press already belonged to it.
            if (keyboard.escapeKey.wasPressedThisFrame && !PersonnelAlmanac.ClaimsEsc)
            {
                Close();
                return;
            }

            HandleNavigation(keyboard);

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (Targeting != null)
            {
                HandleTargeting(mouse);
            }
            else if (mouse.leftButton.wasPressedThisFrame)
            {
                // The clock bar now carries real buttons over this map (pause, HQ) - a
                // click the EventSystem already gave to one of them is not a map click.
                var overUi = UnityEngine.EventSystems.EventSystem.current &&
                    UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
                if (!overUi)
                    PickBlock(mouse.position.ReadValue());
            }
        }

        // ------------------------------------------------------- order targeting input

        Vector2 dragStartScreen;
        bool dragging;

        /// <summary>Under this many pixels of travel a press is a click, not a box -
        /// nobody's hand is still enough for zero.</summary>
        const float DragThreshold = 6f;

        void HandleTargeting(Mouse mouse)
        {
            // A click on the ledger's panel belongs to its buttons - that canvas
            // carries a raycaster, so the EventSystem knows.
            var overUi = UnityEngine.EventSystems.EventSystem.current &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            if (mouse.leftButton.wasPressedThisFrame && !overUi)
            {
                dragStartScreen = mouse.position.ReadValue();
                dragging = true;
            }

            if (!dragging)
                return;

            var current = mouse.position.ReadValue();
            var moved = (current - dragStartScreen).sqrMagnitude >
                        DragThreshold * DragThreshold;

            // The box lives while the button is down - blocks light as it swallows
            // them because the consumer repaints highlights from the live preview.
            if (Targeting.WantsArea && dragBox)
            {
                if (moved && mouse.leftButton.isPressed)
                    ProjectRect(dragBox, WorldRectBetween(dragStartScreen, current));
                else if (dragBox.enabled)
                    dragBox.enabled = false;
                if (moved && mouse.leftButton.isPressed)
                    Targeting.OnAreaPreview(WorldRectBetween(dragStartScreen, current));
            }

            if (!mouse.leftButton.wasReleasedThisFrame)
                return;

            dragging = false;
            if (dragBox)
                dragBox.enabled = false;

            if (Targeting.WantsArea && moved)
            {
                Targeting.OnAreaSelected(WorldRectBetween(dragStartScreen, current));
            }
            else if (!moved)
            {
                var world = ScreenToWorldXZ(current);
                var block = Gameplay.CityBlocks.At(world) ??
                            Gameplay.CityBlocks.Nearest(world);
                Targeting.OnPointClicked(world, block?.Id ?? -1);
            }
        }

        Vector2 ScreenToWorldXZ(Vector2 screen)
        {
            var world = mapCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            return new Vector2(world.x, world.z);
        }

        Rect WorldRectBetween(Vector2 screenA, Vector2 screenB)
        {
            var a = ScreenToWorldXZ(screenA);
            var b = ScreenToWorldXZ(screenB);
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>
        /// Scroll zooms toward the cursor, WASD/arrows pan - the same gestures as the iso
        /// rig, which is disabled while the map is up, so the keys are free. Straight-down
        /// camera means no pitch foreshortening: the iso rig's zoom-anchor algebra holds
        /// with a plain world-per-pixel scale, and pan axes are literally +X/+Z.
        /// </summary>
        void HandleNavigation(Keyboard keyboard)
        {
            var maxOrtho = Mathf.Max(fitOrtho, MinOrtho);

            var input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (input != Vector2.zero)
            {
                var speed = PanSpeed * (targetOrtho / 40f) * Time.unscaledDeltaTime;
                targetFocus += new Vector3(input.x * speed, 0f, input.y * speed);
            }

            var pixels = ViewportPixels;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pointer = mouse.position.ReadValue();
                var scroll = mouse.scroll.ReadValue().y;

                // Only while the pointer is over the map's own region - docked beside
                // the book, the wheel over a roster list must scroll the list, not zoom
                // the city. Fullscreen this is the whole screen, exactly the old rule.
                if (Mathf.Abs(scroll) >= 0.01f && pixels.Contains(pointer))
                {
                    var direction = Mathf.Sign(scroll);
                    var previous = targetOrtho;
                    targetOrtho = Mathf.Clamp(
                        targetOrtho * (1f - direction * ZoomStep), MinOrtho, maxOrtho);

                    // Pin the ground point under the pointer while the size changes - the
                    // map grows toward what is being looked at, not the viewport centre.
                    if (!Mathf.Approximately(previous, targetOrtho) && pixels.height > 0f)
                    {
                        var offset = pointer - pixels.center;
                        var worldPerPixel = targetOrtho * 2f / pixels.height;
                        targetFocus += new Vector3(offset.x * worldPerPixel, 0f,
                                                   offset.y * worldPerPixel) *
                                       (previous / targetOrtho - 1f);
                    }
                }
            }

            // The view never leaves the city; zoomed past the city's own proportions the
            // short axis just centres.
            var aspect = pixels.height > 0f ? pixels.width / pixels.height : 1.78f;
            targetFocus = new Vector3(
                ClampAxis(targetFocus.x, cityBounds.min.x, cityBounds.max.x,
                    targetOrtho * aspect),
                0f,
                ClampAxis(targetFocus.z, cityBounds.min.z, cityBounds.max.z, targetOrtho));

            var dt = Time.unscaledDeltaTime;
            focus = Vector3.Lerp(focus, targetFocus, 1f - Mathf.Exp(-MoveSmoothing * dt));
            orthoSize = Mathf.Lerp(orthoSize, targetOrtho, 1f - Mathf.Exp(-ZoomSmoothing * dt));
            mapCamera.transform.position = new Vector3(focus.x, CameraHeight, focus.z);
            mapCamera.orthographicSize = orthoSize;
        }

        static float ClampAxis(float value, float min, float max, float halfView)
        {
            if (max - min <= halfView * 2f)
                return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + halfView, max - halfView);
        }

        public void Open() => OpenAs(beside: false);

        void OpenAs(bool beside)
        {
            if (IsOpen)
            {
                if (docked == beside)
                    return;
                docked = beside;
                ApplyViewport();
                FrameCamera();
                return;
            }

            if (!EnsureBuilt())
                return;

            docked = beside;
            ApplyViewport();

            // Clouds respawn as they drift off the map, so the sweep repeats per open -
            // a handful of objects, not worth tracking spawns for.
            HideAmbientFlyers();
            FrameCamera();

            if (isoController) isoController.enabled = false;
            if (isoCamera) isoCamera.enabled = false;
            if (occlusionHider) occlusionHider.enabled = false;
            mapCamera.enabled = true;
            CityWeather.FogSuppressed = true;

            // Typed caches and the dock roster refresh against the live scene.
            overlayVersion = -1;
            dockWorkers = FindObjectsByType<DockWorkerAgent>(FindObjectsSortMode.None);
            cardDirty = true;

            page.SetActive(true);
            IsOpen = true;
        }

        /// <summary>The camera's viewport and the overlay container, agreeing on the
        /// same normalized region - WorldToScreenPoint and ScreenPointToRay are
        /// viewport-aware, so every projection stays honest in both modes. The hint
        /// drops the close-gesture line while docked: Esc there belongs to the book.</summary>
        void ApplyViewport()
        {
            mapCamera.rect = docked
                ? new Rect(0.5f, 0f, 0.5f, 1f)
                : new Rect(0f, 0f, 1f, 1f);
            mapArea.anchorMin = docked ? new Vector2(0.5f, 0f) : Vector2.zero;
            mapArea.anchorMax = Vector2.one;
            mapArea.offsetMin = mapArea.offsetMax = Vector2.zero;
            RefreshHint();
        }

        void RefreshHint()
        {
            if (!hintText)
                return;
            var action = Targeting == null
                ? "LMB - inspect a building"
                : Targeting.WantsArea
                    ? "LMB drag - select target area"
                    : "LMB - select target block";
            hintText.text = (docked ? "" : "M / Esc - close map    ") +
                action + "    scroll - zoom    WASD - pan";
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            docked = false;
            // Torn down from OnDestroy the page and the camera may already be gone.
            if (page)
                page.SetActive(false);
            if (mapCamera)
            {
                mapCamera.enabled = false;
                mapCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
            if (isoCamera) isoCamera.enabled = true;
            if (isoController) isoController.enabled = true;
            if (occlusionHider) occlusionHider.enabled = true;
            CityWeather.FogSuppressed = false;

            IsOpen = false;
            lastCloseFrame = Time.frameCount;
        }

        void OnDestroy()
        {
            MapTargeting.Changed -= RefreshHint;
            MapTargeting.Unregister(this);

            // Play-stop (or a scene tear-down) mid-map: the world must get its camera,
            // its fog and its occlusion hider back.
            if (IsOpen)
                Close();
        }

        // ------------------------------------------------------------- construction

        bool EnsureBuilt()
        {
            if (built)
                return true;

            if (!CollectBlocks())
                return false;

            // The iso rig by its controller, not by tag - the map camera stays untagged,
            // so Camera.main simply goes null while the map is up rather than flipping.
            var controller = FindAnyObjectByType<CameraRig.IsometricCameraController>();
            isoController = controller;
            isoCamera = controller ? controller.GetComponent<Camera>() : Camera.main;
            occlusionHider = FindAnyObjectByType<Gameplay.PlayerOcclusionHider>();

            BuildCamera();
            BuildCanvas();
            CollectLandmarks();

            built = true;
            return true;
        }

        void BuildCamera()
        {
            var go = new GameObject("Strategic Map Camera");
            go.transform.SetParent(transform, false);

            mapCamera = go.AddComponent<Camera>();
            mapCamera.enabled = false;
            mapCamera.orthographic = true;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = MapBackdrop;
            mapCamera.nearClipPlane = 10f;
            mapCamera.farClipPlane = CameraHeight + 200f;
            mapCamera.cullingMask =
                ~((1 << PedestrianSpawner.PedestrianLayer) | (1 << HiddenAmbientLayer));

            // Straight down with screen-up = world +Z: the map reads north-up no matter
            // where the iso rig's yaw was left.
            mapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Occlusion culling is pure waste from straight above - nothing occludes
            // anything - and top-down the whole city is in frustum, so the queries cost
            // without ever culling.
            mapCamera.useOcclusionCulling = false;

            // URP quality settings are PER CAMERA - a fresh camera gets none of them,
            // which is exactly the "pixelated" complaint: no SMAA, no grade. Inherit the
            // iso camera's look so the map renders the same city the same way.
            var data = mapCamera.GetUniversalAdditionalCameraData();
            if (isoCamera)
            {
                mapCamera.allowHDR = isoCamera.allowHDR;
                var isoData = isoCamera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = isoData.renderPostProcessing;
                data.antialiasing = isoData.antialiasing;
                data.antialiasingQuality = isoData.antialiasingQuality;
            }
            if (data.antialiasing == AntialiasingMode.None)
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // Shadows off per-camera, so the URP asset (which FitShadows owns) is never
            // touched - and a noon-length shadow under every building would only smear
            // the very footprints this view exists to show.
            data.renderShadows = false;
        }

        /// <summary>The camera's own viewport in pixels - the whole screen on M, the
        /// right half docked. All screen-bounds tests and per-pixel math go through
        /// this so both modes share one geometry.</summary>
        Rect ViewportPixels => mapCamera.pixelRect;

        void FrameCamera()
        {
            // Recomputed per open - the window may have been resized. Every open starts
            // back at the whole city; the strategic view IS the overview.
            var pixels = ViewportPixels;
            var aspect = pixels.height > 0f ? pixels.width / pixels.height : 1.78f;
            fitOrtho = Mathf.Max(cityBounds.extents.z, cityBounds.extents.x / aspect) * 1.02f;

            var centre = cityBounds.center;
            focus = targetFocus = new Vector3(centre.x, 0f, centre.z);
            orthoSize = targetOrtho = fitOrtho;
            mapCamera.transform.position = new Vector3(focus.x, CameraHeight, focus.z);
            mapCamera.orthographicSize = orthoSize;
        }

        /// <summary>
        /// Snap the open map to a world point at the given zoom - the clock bar's HQ
        /// button. Smoothed state moves WITH the targets (no glide - the user chose the
        /// cut over the flight), and the camera is placed this same frame so even a
        /// paused game shows the base immediately. HandleNavigation's clamp re-applies
        /// next frame, so this still cannot leave the city.
        /// </summary>
        public void FocusOn(Vector3 world, float ortho)
        {
            if (!IsOpen)
                return;

            focus = targetFocus = new Vector3(world.x, 0f, world.z);
            orthoSize = targetOrtho =
                Mathf.Clamp(ortho, MinOrtho, Mathf.Max(fitOrtho, MinOrtho));
            mapCamera.transform.position = new Vector3(focus.x, CameraHeight, focus.z);
            mapCamera.orthographicSize = orthoSize;
        }

        void BuildCanvas()
        {
            var go = new GameObject("Strategic Map", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Everything toggles through this one child; the canvas itself stays alive.
            page = new GameObject("Page", typeof(RectTransform));
            page.transform.SetParent(go.transform, false);

            // The map's own screen region - fullscreen on M, the right half beside the
            // ledger. Its mask clips every overlay layer to whatever the region is, so
            // a dot or a turf wash can never bleed over the book.
            var area = new GameObject("Map Area", typeof(RectTransform));
            area.transform.SetParent(page.transform, false);
            mapArea = (RectTransform)area.transform;
            mapArea.anchorMin = Vector2.zero;
            mapArea.anchorMax = Vector2.one;
            mapArea.offsetMin = mapArea.offsetMax = Vector2.zero;
            area.AddComponent<RectMask2D>();

            // Territory first of all - the families' turf wash is ground, and every
            // highlight and dot must read over it.
            var turf = new GameObject("Territory", typeof(RectTransform));
            turf.transform.SetParent(mapArea, false);
            territoryRoot = (RectTransform)turf.transform;

            // Hierarchy order is draw order: highlights under the dots, card over both.
            buildingHighlight = BuildRectImage("Building Highlight", mapArea);
            buildingHighlight.color = new Color(1f, 1f, 1f, 0.35f);
            buildingHighlight.enabled = false;

            // Order targets over the highlights, under the dots - a selected block must
            // not hide the men standing on it.
            var targets = new GameObject("Targets", typeof(RectTransform));
            targets.transform.SetParent(mapArea, false);
            targetRoot = (RectTransform)targets.transform;

            var dots = new GameObject("Dots", typeof(RectTransform));
            dots.transform.SetParent(mapArea, false);
            dotRoot = (RectTransform)dots.transform;

            dragBox = BuildRectImage("Drag Box", mapArea);
            dragBox.color = new Color(1f, 1f, 1f, 0.16f);
            dragBox.enabled = false;

            BuildCard();

            // The outfit's name tag, floating over the HQ marker - above the dots in
            // draw order, repositioned per frame with them. Created straight under the
            // still-ACTIVE map area, never via BuildCardText: that helper parents new
            // text under the card, which BuildCard has just closed, and a TMP component
            // added below an inactive parent is the null-material crash that takes every
            // canvas on screen down with it (the runtime-HUD trap).
            if (tmpReady)
            {
                var tag = new GameObject("HQ Label", typeof(RectTransform));
                tag.transform.SetParent(mapArea, false);
                hqLabel = tag.AddComponent<TextMeshProUGUI>();
                if (LedgerStyle.Condensed)
                    hqLabel.font = LedgerStyle.Condensed;
                hqLabel.fontSize = 12f;
                hqLabel.fontStyle = FontStyles.Bold;
                hqLabel.color = PlayerGold;
                hqLabel.alignment = TextAlignmentOptions.Center;
                hqLabel.textWrappingMode = TextWrappingModes.NoWrap;
                hqLabel.raycastTarget = false;
                var rect = hqLabel.rectTransform;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(200f, 16f);
                hqLabel.gameObject.SetActive(false);
            }

            // Built ACTIVE then hidden - the BlockOverlayHud trick: TMP loads its font in
            // OnEnable, which never runs under an inactive parent.
            page.SetActive(false);
        }

        static Image BuildRectImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = null;
            image.raycastTarget = false;
            return image;
        }

        void BuildCard()
        {
            tmpReady = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
            if (!tmpReady)
            {
                // The map itself still works - camera and dots owe TMP nothing.
                Debug.LogWarning("[StrategicMap] No TMP default font - the block card is " +
                                 "disabled until TMP essentials are imported.", this);
                return;
            }

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(mapArea, false);

            cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(1f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(1f, 1f);
            cardRect.anchoredPosition = new Vector2(-CardMargin, -CardMargin);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var background = card.AddComponent<Image>();
            background.sprite = null;
            background.color = new Color(0f, 0f, 0f, 0.78f);
            background.raycastTarget = false;
            UiSkin.TryDress(background, UiSkin.PanelDark);

            cardTitle = BuildCardText("Title", 18f);
            if (LedgerStyle.Condensed)
                cardTitle.font = LedgerStyle.Condensed;
            cardTitle.fontStyle = FontStyles.Bold;
            var titleRect = cardTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(14f, -12f);
            titleRect.sizeDelta = new Vector2(-28f, 26f);

            cardBody = BuildCardText("Body", 13f);
            cardBody.color = new Color(0.88f, 0.88f, 0.88f);
            var bodyRect = cardBody.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.offsetMin = new Vector2(14f, 12f);
            bodyRect.offsetMax = new Vector2(-14f, -46f);
            cardBody.alignment = TextAlignmentOptions.TopLeft;
            cardBody.textWrappingMode = TextWrappingModes.Normal;

            hintText = BuildCardText("Hint", 12f);
            hintText.transform.SetParent(mapArea, false);
            hintText.color = new Color(1f, 1f, 1f, 0.55f);
            var hintRect = hintText.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(0f, 0f);
            hintRect.pivot = new Vector2(0f, 0f);
            hintRect.anchoredPosition = new Vector2(16f, 12f);
            hintRect.sizeDelta = new Vector2(700f, 20f);

            card.SetActive(false);
        }

        TMP_Text BuildCardText(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(cardRect ? cardRect.transform : page.transform, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            // The card reads as a page of the book held over the map, so it is set in the
            // book's faces. The title overrides this to the headline gothic.
            if (LedgerStyle.Mono)
                text.font = LedgerStyle.Mono;
            text.fontSize = size;
            text.color = new Color(0.96f, 0.96f, 0.96f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        // -------------------------------------------------------------- block table

        /// <summary>
        /// The map's block table. A city with a canonical territory plan - every Core
        /// scene, which is the game - is projected straight off the geography, so the
        /// block the player clicks here is the same canonical block the simulation and
        /// the ledger speak of. The older CityBuilder city has no such plan, and keeps
        /// the BlockOverlayHud parse: each slab's renderer bounds becomes a clickable
        /// rect, their union the block's highlight and the whole city's frame.
        /// </summary>
        bool CollectBlocks()
        {
            if (CollectCanonicalBlocks())
                return true;

            var builder = FindAnyObjectByType<CityBuilder>();
            var root = builder ? builder.GeneratedRoot : null;
            var ground = root ? root.Find("Ground") : null;

            if (!ground)
            {
                if (!warnedEmpty)
                {
                    warnedEmpty = true;
                    Debug.LogWarning("[StrategicMap] No generated city and no canonical " +
                                     "territory plan - the strategic map has nothing to show.",
                                     this);
                }
                return false;
            }

            buildingsRoot = root.Find("Buildings");

            blocks.Clear();
            slabCentres.Clear();
            var union = new Bounds();
            var any = false;

            foreach (Transform child in ground)
            {
                // "ground_{zone}_{blockId}" or "ground_{zone}_{blockId}_{x}_{y}" (park).
                // Paint/patch decorations fail the enum parse at parts[1] and drop out.
                var parts = child.name.Split('_');
                if (parts.Length < 3 || parts[0] != "ground")
                    continue;
                if (!System.Enum.TryParse(parts[1], out BlockZone zone))
                    continue;
                if (!int.TryParse(parts[2], out var blockId))
                    continue;

                var renderer = child.GetComponentInChildren<Renderer>();
                if (!renderer)
                    continue;

                var b = renderer.bounds;
                var rect = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);

                if (!blocks.TryGetValue(blockId, out var block))
                {
                    block = new Block { Id = blockId, Zone = zone, Union = rect };
                    blocks.Add(blockId, block);
                }

                block.Slabs.Add(rect);
                block.Union = Rect.MinMaxRect(
                    Mathf.Min(block.Union.xMin, rect.xMin),
                    Mathf.Min(block.Union.yMin, rect.yMin),
                    Mathf.Max(block.Union.xMax, rect.xMax),
                    Mathf.Max(block.Union.yMax, rect.yMax));
                slabCentres.Add((blockId, child.position));

                if (!any)
                {
                    any = true;
                    union = b;
                }
                else
                {
                    union.Encapsulate(b);
                }
            }

            if (!any)
                return false;

            // Slabs stop at the sidewalks; the streets around the outer blocks are half a
            // cell deep each side, so one whole cell of padding shows the city's edge.
            union.Expand(new Vector3(CityGrid.CellSize, 0f, CityGrid.CellSize));
            cityBounds = union;
            return true;
        }

        /// <summary>
        /// The canonical projection: one clickable rectangle per plan block, its own
        /// world bounds. No renderer is measured and no name is parsed - identity comes
        /// from the plan, which is the whole point of routing the map through it.
        /// </summary>
        bool CollectCanonicalBlocks()
        {
            var geography = RoadDemo.TerritoryRuntime.Instance?.Geography;
            if (geography == null || geography.BlockIds.Count == 0)
                return false;

            blocks.Clear();
            slabCentres.Clear();
            buildingsRoot = null;

            var union = new Bounds();
            var any = false;
            var ids = geography.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (!geography.TryGetBlock(ids[i], out var definition) ||
                    definition.LegacyBlockId < 0 || blocks.ContainsKey(definition.LegacyBlockId))
                    continue;

                var bounds = definition.WorldBounds;
                var rect = new Rect(bounds.XMin, bounds.ZMin, bounds.Width, bounds.Depth);
                var block = new Block
                {
                    Id = definition.LegacyBlockId,
                    Zone = CityBlocks.Get(definition.LegacyBlockId)?.Zone ?? BlockZone.ResidentialHigh,
                    Union = rect,
                };
                block.Slabs.Add(rect);
                blocks.Add(block.Id, block);
                slabCentres.Add((block.Id, new Vector3(rect.center.x, 0f, rect.center.y)));

                var worldBounds = new Bounds(
                    new Vector3(rect.center.x, 0f, rect.center.y),
                    new Vector3(rect.width, 1f, rect.height));
                if (!any)
                {
                    any = true;
                    union = worldBounds;
                }
                else
                {
                    union.Encapsulate(worldBounds);
                }
            }

            if (!any)
                return false;

            // A street's width of margin, so the outermost blocks are not flush with the
            // edge of the plate - the same intent as the generated city's cell of padding.
            union.Expand(new Vector3(geography.Settings.StreetWidth, 0f,
                                     geography.Settings.StreetWidth));
            cityBounds = union;
            return true;
        }

        void CollectLandmarks()
        {
            // These carry their block id from generation.
            foreach (var park in FindObjectsByType<ParkGrounds>(FindObjectsSortMode.None))
                AddLandmark(park.BlockId, "Park");
            foreach (var yard in FindObjectsByType<WorksYard>(FindObjectsSortMode.None))
                AddLandmark(yard.BlockId, "Works yard");
            foreach (var port in FindObjectsByType<PortMarker>(FindObjectsSortMode.None))
                AddLandmark(port.BlockId, "Docks");

            // These do not - resolve by nearest slab, the PropertyDirector rule.
            foreach (var school in FindObjectsByType<SchoolMarker>(FindObjectsSortMode.None))
                AddLandmark(NearestBlock(school.transform.position), "School");
            foreach (var station in FindObjectsByType<PoliceStation>(FindObjectsSortMode.None))
                AddLandmark(NearestBlock(station.transform.position), "Police station");
            foreach (var bank in FindObjectsByType<BankForecourt>(FindObjectsSortMode.None))
                AddLandmark(NearestBlock(bank.transform.position), "Bank");
            foreach (var shop in FindObjectsByType<GunShopMarker>(FindObjectsSortMode.None))
                AddLandmark(NearestBlock(shop.transform.position), "Gun shop");
        }

        void AddLandmark(int blockId, string label)
        {
            if (blockId < 0 || !blocks.TryGetValue(blockId, out var block))
                return;
            if (!block.Landmarks.Contains(label))
                block.Landmarks.Add(label);
        }

        int NearestBlock(Vector3 position)
        {
            var best = -1;
            var bestSqr = float.MaxValue;
            foreach (var (blockId, slab) in slabCentres)
            {
                var dx = slab.x - position.x;
                var dz = slab.z - position.z;
                var sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr)
                    continue;

                bestSqr = sqr;
                best = blockId;
            }

            return best;
        }

        // ---------------------------------------------------------------- picking

        void PickBlock(Vector2 screenPosition)
        {
            // A click on the card is reading, not re-selecting the block behind it.
            if (cardRect && cardRect.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(cardRect, screenPosition))
                return;

            // A building first: straight-down ray against the real colliders, triggers
            // ignored (the AI cars tow feeler boxes - the CityOverlayHud trap), the crowd
            // layer ignored (a dot is not a roof). Whatever the nearest hit resolves to
            // under Generated City/Buildings is the building; a hit on ground or props
            // falls through to plain block selection.
            selectedBuilding = null;
            var ray = mapCamera.ScreenPointToRay(screenPosition);
            var hits = Physics.RaycastAll(
                ray, CameraHeight + 100f,
                ~(1 << PedestrianSpawner.PedestrianLayer), QueryTriggerInteraction.Ignore);
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance >= bestDistance || !hit.collider)
                    continue;
                bestDistance = hit.distance;
                selectedBuilding = BuildingRootOf(hit.collider.transform);
            }

            // Selection is building-by-building - a click that lands on ground, street
            // or props selects NOTHING. The turf wash already tells the block story;
            // the card exists to tell the building's.
            if (!selectedBuilding)
            {
                selectedBlockId = -1;
                cardDirty = true;
                return;
            }

            selectedBuildingRect = BuildingFootprint(selectedBuilding);

            // The building's block, for context and control: the slab under the click,
            // or - a building can overhang the sidewalk the slab stops at - the nearest
            // slab to the building, the PropertyDirector rule.
            var world = mapCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, 0f));
            var point = new Vector2(world.x, world.z);

            var hitBlock = -1;
            foreach (var block in blocks.Values)
            {
                if (!block.Union.Contains(point))
                    continue;

                foreach (var slab in block.Slabs)
                {
                    if (!slab.Contains(point))
                        continue;
                    hitBlock = block.Id;
                    break;
                }

                if (hitBlock >= 0)
                    break;
            }

            if (hitBlock < 0)
                hitBlock = NearestBlock(selectedBuilding.position);

            selectedBlockId = hitBlock;
            cardDirty = true;
        }

        /// <summary>The ancestor that is a direct child of Generated City/Buildings -
        /// null for ground, props, yards, anything that is not a building.</summary>
        Transform BuildingRootOf(Transform node)
        {
            if (!buildingsRoot)
                return null;
            while (node && node.parent != buildingsRoot)
                node = node.parent;
            return node;
        }

        static Rect BuildingFootprint(Transform building)
        {
            var renderers = building.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                var p = building.position;
                return new Rect(p.x - 4f, p.z - 4f, 8f, 8f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
        }

        /// <summary>"building-shop-china(Clone)" reads as "Shop china" - the prefab name
        /// IS the building's type, it just needs the plumbing stripped off.</summary>
        static string FriendlyBuildingName(string prefabName)
        {
            var name = prefabName;
            var clone = name.IndexOf('(');
            if (clone >= 0)
                name = name.Substring(0, clone);
            name = name.Trim();
            if (name.StartsWith("building-"))
                name = name.Substring("building-".Length);
            name = name.Replace('-', ' ').Replace('_', ' ');
            if (name.Length > 0)
                name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            return name.Length > 0 ? name : "Building";
        }

        // ------------------------------------------------------------------- paint

        void LateUpdate()
        {
            if (!IsOpen)
                return;

            PaintTerritory();
            PaintTargets();
            SyncTrackedPeople();

            // No eyes anywhere means no fog yet - the map shows everyone rather than
            // no one. Decided once per frame, not per dot.
            visionUnrestricted = !MapVisionRegistry.HasActiveSources;

            dotCursor = 0;

            // The outfit's base, before police - the MaxDots cap must never swallow it.
            PaintHeadquarters();

            // Police before civilians: the MaxDots cap swallows from the tail, and a lost
            // blue dot is information lost - a lost white one is crowd texture.
            EmitTracked(policeDots, PoliceBlue);

            // Civilians: the one list that is exactly "every ordinary pedestrian".
            var agents = PedestrianAgent.Agents;
            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (!agent)
                    continue;

                IOverlaySubject subject = agent;
                if (subject.OverlayHidden)
                    continue;

                var position = agent.transform.position;
                if (!visionUnrestricted && !MapVisionRegistry.IsVisible(position))
                    continue;

                EmitDot(position, MapAffiliation.ColorFor(agent) ?? CivilianWhite, DotSize);
            }

            EmitTracked(extraDots, CivilianWhite);

            // Dock workers register nowhere and never hide - a fixed roster per open.
            for (var i = 0; i < dockWorkers.Length; i++)
            {
                var worker = dockWorkers[i];
                if (!worker)
                    continue;

                var position = worker.transform.position;
                if (!visionUnrestricted && !MapVisionRegistry.IsVisible(position))
                    continue;

                EmitDot(position, MapAffiliation.ColorFor(worker) ?? CivilianWhite, DotSize);
            }

            TrimDots();
            UpdateSelection();
        }

        /// <summary>
        /// The outfit's headquarters: a gold square at its front premise with the family
        /// name over it - "our base", legible at every zoom where a footprint is not.
        /// Absent until GangDirector has seated the families, like the turf wash.
        /// </summary>
        void PaintHeadquarters()
        {
            var outfit = Gameplay.OutfitDirector.Instance;
            if (!outfit || !outfit.TryGetHeadquarters(out var hq, out _))
            {
                if (hqLabel && hqLabel.gameObject.activeSelf)
                    hqLabel.gameObject.SetActive(false);
                return;
            }

            EmitDot(hq, PlayerGold, HqMarkerSize, square: true);

            if (!hqLabel)
                return;

            // Named here, not at build time - the registry is empty until GangDirector
            // installs the families, and a front existing proves that has happened.
            // IsNullOrEmpty, not .Length: a fresh TextMeshProUGUI's text is NULL until
            // the first assignment, and this line runs before any.
            if (string.IsNullOrEmpty(hqLabel.text))
                hqLabel.text = Gangs.GangRegistry.NameOf(Gangs.GangCatalog.PlayerGangId);

            var screen = mapCamera.WorldToScreenPoint(hq);
            var pixels = ViewportPixels;
            var visible = screen.x >= pixels.xMin && screen.x <= pixels.xMax &&
                          screen.y >= pixels.yMin && screen.y <= pixels.yMax;
            if (hqLabel.gameObject.activeSelf != visible)
                hqLabel.gameObject.SetActive(visible);
            if (visible)
                hqLabel.transform.position =
                    new Vector3(screen.x, screen.y + HqMarkerSize * 0.75f, 0f);
        }

        /// <summary>Officers, response officers, school children and forecourt visitors
        /// all keep themselves in OverlayRegistry - filtered into typed lists once per
        /// registry change, the CityOverlayHud versioning convention.</summary>
        void SyncTrackedPeople()
        {
            if (OverlayRegistry.Version == overlayVersion)
                return;

            overlayVersion = OverlayRegistry.Version;
            policeDots.Clear();
            extraDots.Clear();

            foreach (var subject in OverlayRegistry.Subjects)
            {
                switch (subject)
                {
                    case PoliceOfficerAgent officer:
                        policeDots.Add(new Tracked { Subject = subject, Body = officer });
                        break;
                    case SchoolChildAgent child:
                        extraDots.Add(new Tracked { Subject = subject, Body = child });
                        break;
                    case ForecourtVisitorAgent visitor:
                        extraDots.Add(new Tracked { Subject = subject, Body = visitor });
                        break;
                    case GangMemberAgent member:
                        // Colour arrives through MapAffiliation - GangDirector installs
                        // the provider - so the white fallback never actually shows.
                        extraDots.Add(new Tracked { Subject = subject, Body = member });
                        break;
                }
            }
        }

        void EmitTracked(List<Tracked> people, Color fallback)
        {
            for (var i = 0; i < people.Count; i++)
            {
                var tracked = people[i];
                var anchor = tracked.Subject?.OverlayAnchor;
                if (!anchor || tracked.Subject.OverlayHidden)
                    continue;

                var position = anchor.position;
                if (!visionUnrestricted && !MapVisionRegistry.IsVisible(position))
                    continue;

                EmitDot(position, MapAffiliation.ColorFor(tracked.Body) ?? fallback, DotSize);
            }
        }

        void EmitDot(Vector3 world, Color color, float size, bool square = false)
        {
            if (dotCursor >= MaxDots)
                return;

            var screen = mapCamera.WorldToScreenPoint(world);
            var pixels = ViewportPixels;
            if (screen.x < pixels.xMin || screen.x > pixels.xMax ||
                screen.y < pixels.yMin || screen.y > pixels.yMax)
                return;

            Image dot;
            if (dotCursor < dotPool.Count)
            {
                dot = dotPool[dotCursor];
            }
            else
            {
                dot = BuildRectImage("dot", dotRoot);
                dotPool.Add(dot);
            }
            dotCursor++;

            if (!dot.enabled)
                dot.enabled = true;

            screen.z = 0f;
            dot.transform.position = screen;
            if (dot.color != color)
                dot.color = color;

            var rect = dot.rectTransform;
            if (!Mathf.Approximately(rect.sizeDelta.x, size))
                rect.sizeDelta = new Vector2(size, size);
            // The overlay's visual language: agents stand on their corner, places sit flat.
            rect.localRotation = Quaternion.Euler(0f, 0f, square ? 0f : 45f);
        }

        void TrimDots()
        {
            // Same off-screen rule as every HUD here: toggle the Graphic, never SetActive.
            for (var i = dotCursor; i < dotPool.Count; i++)
                if (dotPool[i].enabled)
                    dotPool[i].enabled = false;
        }

        void UpdateSelection()
        {
            // A destroyed building (city regenerated in Play) drops the selection too.
            if (!selectedBuilding || selectedBlockId < 0 ||
                !blocks.TryGetValue(selectedBlockId, out var block))
            {
                if (buildingHighlight.enabled)
                    buildingHighlight.enabled = false;
                if (cardRect && cardRect.gameObject.activeSelf)
                    cardRect.gameObject.SetActive(false);
                return;
            }

            // The camera moves under zoom and pan, so the footprint reprojects every
            // frame - the same rule as the dots. Only the BUILDING is outlined; its
            // block is card context, never a wash of its own (the turf layer owns
            // block-level colour).
            ProjectRect(buildingHighlight, selectedBuildingRect);

            if (!tmpReady)
                return;

            if (!cardRect.gameObject.activeSelf)
                cardRect.gameObject.SetActive(true);

            var outfit = Gameplay.OutfitDirector.Instance;
            var outfitVersion = outfit ? outfit.Version : -1;
            if (!cardDirty && paintedPropertyVersion == PropertyRegistry.Version &&
                paintedOutfitVersion == outfitVersion)
                return;

            cardDirty = false;
            paintedPropertyVersion = PropertyRegistry.Version;
            paintedOutfitVersion = outfitVersion;
            PaintCard(block);
        }

        /// <summary>
        /// The families' holdings, washed over the buildings they hold in their gang
        /// colour. Ground is taken premise by premise, so the wash covers exactly the
        /// premises: a family's front tints its own footprint, never the block around
        /// it. Ownership reads straight off the markers (BusinessMarker.GangId, the
        /// single source) - nothing seeds, so before the gang layer stamps the fronts
        /// there is simply nothing to paint and no cost. Reprojected every frame like
        /// every rect on this canvas (the camera pans and zooms); the tile pool grows
        /// to the holding count and the tail idles disabled.
        /// </summary>
        void PaintTerritory()
        {
            if (_footprintsVersion != PropertyRegistry.Version)
            {
                _footprints.Clear();
                _footprintsVersion = PropertyRegistry.Version;
            }

            var used = 0;
            foreach (var business in Gameplay.PropertyRegistry.Businesses)
            {
                if (!business || business.GangId < 0)
                    continue;

                if (used == territoryTiles.Count)
                    territoryTiles.Add(BuildRectImage("Turf", territoryRoot));
                var tile = territoryTiles[used++];

                var colour = GangPalette.Of(business.GangId);
                // Our own ground reads a shade stronger than the rivals' - the map is
                // the player's, and "mine" should be findable at the full-city zoom.
                colour.a = business.GangId == Gangs.GangCatalog.PlayerGangId ? 0.62f : 0.45f;
                if (tile.color != colour)
                    tile.color = colour;
                if (!_footprints.TryGetValue(business, out var footprint))
                    _footprints[business] = footprint = FootprintOf(business);
                ProjectRect(tile, footprint);
            }

            for (var i = used; i < territoryTiles.Count; i++)
                if (territoryTiles[i].enabled)
                    territoryTiles[i].enabled = false;
        }

        /// <summary>World-XZ footprint of one held building, off its own collider (the
        /// pack prefab keeps its MeshCollider on the marker's transform). A bare
        /// transform degrades to a small square - never to the block around it.</summary>
        static Rect FootprintOf(Entities.BusinessMarker business)
        {
            var collider = business.GetComponent<Collider>();
            if (collider)
            {
                var bounds = collider.bounds;
                return new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
            }

            var position = business.transform.position;
            return new Rect(position.x - 4f, position.z - 4f, 8f, 8f);
        }

        /// <summary>
        /// The Orders page's highlight layer: whatever rects it last handed over, in
        /// its colour, reprojected per frame like everything on this canvas. An empty
        /// hand clears the layer. Deliberately separate from the territory wash and
        /// the selection highlight - three layers, three meanings, no conflicts.
        /// </summary>
        public void SetTargetHighlights(List<Rect> worldRects, Color color)
        {
            targetRects.Clear();
            if (worldRects != null)
                targetRects.AddRange(worldRects);
            targetColor = color;
        }

        void PaintTargets()
        {
            var used = 0;
            for (; used < targetRects.Count; used++)
            {
                if (used == targetTiles.Count)
                    targetTiles.Add(BuildRectImage("Target", targetRoot));
                var tile = targetTiles[used];
                if (tile.color != targetColor)
                    tile.color = targetColor;
                ProjectRect(tile, targetRects[used]);
            }

            for (var i = used; i < targetTiles.Count; i++)
                if (targetTiles[i].enabled)
                    targetTiles[i].enabled = false;
        }

        void ProjectRect(Image image, Rect worldRect)
        {
            var min = mapCamera.WorldToScreenPoint(
                new Vector3(worldRect.xMin, 0f, worldRect.yMin));
            var max = mapCamera.WorldToScreenPoint(
                new Vector3(worldRect.xMax, 0f, worldRect.yMax));

            image.rectTransform.position =
                new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
            image.rectTransform.sizeDelta =
                new Vector2(max.x - min.x, max.y - min.y) / canvas.scaleFactor;
            if (!image.enabled)
                image.enabled = true;
        }

        /// <summary>One building, one card: what it is (title), who controls the ground
        /// it stands on, what goes on inside, and where it stands. Only called with a
        /// building selected - there is no block-only card anymore.</summary>
        void PaintCard(Block block)
        {
            text.Clear();

            cardTitle.text = FriendlyBuildingName(selectedBuilding.name);
            cardTitle.color = BlockOverlayHud.ZoneColour(block.Zone);

            AppendControlLine(block);
            text.Append('\n');

            AppendBuildingLines(selectedBuilding, block);

            text.Append('\n')
                .Append(BlockOverlayHud.DisplayName(block.Zone))
                .Append(" block #").Append(block.Id).Append('\n');

            // .text, not SetText: the sentences themselves change, and only on selection
            // or ownership change - rare, so the allocation is affordable.
            cardBody.text = text.ToString();
        }

        /// <summary>Who holds ground here: ground is taken premise by premise, so the
        /// honest unit is the building - the line names the family holding the most
        /// premises on the block and how many. Nobody holding one, or a shared lead
        /// (contested ground has no controller), reads as open ground.</summary>
        void AppendControlLine(Block block)
        {
            var outfit = Gameplay.OutfitDirector.Instance;
            if (outfit)
                outfit.CollectHoldings(holdingsScratch);
            else
                holdingsScratch.Clear();

            // Who holds this street is the DERIVED reading, not a count of deeds: the men
            // standing on it, what it fears, whose shops pay whom, and whether that house
            // answers for them. The deeds are still printed under it, because a family
            // with premises here is a fact worth knowing - it is simply not the answer.
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            var read = false;
            if (runtime != null && runtime.Control != null &&
                runtime.TryGetBlock(block.Id, out var canonical))
            {
                var state = runtime.Control.StateOf(canonical);
                var leader = runtime.Control.LeaderOf(canonical);
                if (state != Territory.TerritoryControlState.Unknown &&
                    state != Territory.TerritoryControlState.Uncontrolled)
                {
                    read = true;
                    if (leader.IsValid)
                        text.Append("<color=#")
                            .Append(ColorUtility.ToHtmlStringRGB(GangPalette.Of(leader.Value)))
                            .Append('>')
                            .Append(Gangs.GangRegistry.NameOf(leader.Value))
                            .Append("</color> — ");
                    text.Append(LedgerText.ControlWord(state)).Append('\n');
                }
            }

            var gangId = Outfit.Turf.DominantIn(holdingsScratch, block.Id);
            if (gangId >= 0)
            {
                var held = Outfit.Turf.CountIn(holdingsScratch, block.Id, gangId);
                text.Append("<color=#")
                    .Append(ColorUtility.ToHtmlStringRGB(GangPalette.Of(gangId)))
                    .Append('>')
                    .Append(Gangs.GangRegistry.NameOf(gangId))
                    .Append("</color> holds ")
                    .Append(held == 1 ? "a business" : held + " businesses")
                    .Append(" here\n");
            }
            else if (!read)
            {
                text.Append("No family holds ground here\n");
            }
        }

        /// <summary>What THIS building is: its business (name, owner, take), its civic
        /// role, or - for the plain stock - just its type, which the title already
        /// carries. Components sit on the building root; one GetComponent each.</summary>
        void AppendBuildingLines(Transform building, Block block)
        {
            var business = building.GetComponent<Entities.BusinessMarker>();
            if (business)
            {
                text.Append(business.Category).Append(" business\n");
                text.Append(business.BusinessName);
                if (business.Owner != null)
                    text.Append("  —  ").Append(business.Owner.DisplayName);
                text.Append('\n');
                text.Append('$').Append(business.WeeklyIncome).Append("/wk");
                if (business.Protected)
                    text.Append("  ·  protected");
                text.Append('\n');
            }

            if (building.GetComponent<SchoolMarker>())
                text.Append("The city's school\n");
            if (building.GetComponent<PoliceStation>())
                text.Append("The police station\n");
            if (building.GetComponent<BankForecourt>())
                text.Append("The bank\n");
            if (building.GetComponent<GunShopMarker>())
                text.Append("Gun shop - sells under the counter\n");

            if (!business && !building.GetComponent<SchoolMarker>() &&
                !building.GetComponent<PoliceStation>() &&
                !building.GetComponent<BankForecourt>() &&
                !building.GetComponent<GunShopMarker>())
            {
                text.Append(block.Zone == BlockZone.ResidentialHigh
                    ? "Residential building\n"
                    : "No registered business\n");
            }
        }

        // ------------------------------------------------------------------ ambient

        void HideAmbientFlyers()
        {
            foreach (var clouds in FindObjectsByType<CloudSystem>(FindObjectsSortMode.None))
                PedestrianSpawner.SetLayerRecursively(clouds.transform, HiddenAmbientLayer);
            foreach (var birds in FindObjectsByType<BirdFlockSystem>(FindObjectsSortMode.None))
                PedestrianSpawner.SetLayerRecursively(birds.transform, HiddenAmbientLayer);

            // Smoke plumes are transparent overdraw, and top-down the WHOLE city's plumes
            // are in frustum at once - at native Retina resolution that alone stutters.
            // The iso camera renders every layer, so parking them on the cull layer costs
            // the normal view nothing and needs no restore.
            foreach (var stacks in FindObjectsByType<SmokeStackSystem>(FindObjectsSortMode.None))
                PedestrianSpawner.SetLayerRecursively(stacks.transform, HiddenAmbientLayer);
            foreach (var vent in FindObjectsByType<SmokeVent>(FindObjectsSortMode.None))
                PedestrianSpawner.SetLayerRecursively(vent.transform, HiddenAmbientLayer);
        }
    }
}
