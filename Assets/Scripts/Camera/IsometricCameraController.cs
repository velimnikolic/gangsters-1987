using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using LivingCity.Generation;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace LivingCity.CameraRig
{
    /// <summary>
    /// Orthographic isometric camera.
    ///
    /// Uses the new Input System exclusively. This project has activeInputHandler set to
    /// "Input System Package (New)", so any legacy UnityEngine.Input call throws
    /// InvalidOperationException at runtime - which is also why the package's own
    /// CameraControl.cs / PerspectiveCameraControl.cs cannot be used here.
    ///
    /// The rig is a focus point on the XZ plane plus a yaw; the camera is placed back along
    /// its own forward each frame. Rotating therefore orbits the focus point, and panning is
    /// expressed in the camera's projected axes so WASD still feels correct after a rotation.
    ///
    /// Controls: WASD/arrows = pan, Q/E = rotate 90 degrees, scroll = zoom,
    /// space + left drag (or middle drag) = drag-pan, one finger = pan, two = pinch/twist.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsometricCameraController : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] float pitch = 45f;
        [SerializeField] float yaw = 45f;
        [Tooltip("Distance the camera sits back from the focus point. Irrelevant to " +
                 "orthographic framing - it only needs to clear the near plane and the city.")]
        [SerializeField] float boomLength = 200f;

        [Header("Zoom")]
        [SerializeField] float minOrthoSize = 10f;
        [Tooltip("Grid je 16x7 celija, pa su bounds 480x210 m. Rotirano za yaw 45 ovo je ~488 m " +
                 "po obe planarne ose, a ekranska visina je jos skracena za sin(pitch): ~345 m. " +
                 "Kadar pokriva 2x ovu vrednost, pa cela mapa trazi 173 na 16:9 i 183 na 4:3 - " +
                 "200 pokriva oba sa marginom. Prethodnih 80 je pokazivalo manje od pola grada.")]
        [SerializeField] float maxOrthoSize = 200f;
        [SerializeField] float orthoSize = 40f;
        [Tooltip("Percent change in zoom per scroll step. Na 6% je od default zooma 40 do " +
                 "potpuno odzumirano ~28 klikova, a ceo opseg 10-200 je ~51.")]
        [SerializeField] float scrollZoomSpeed = 6f;
        [SerializeField] float pinchZoomSpeed = 0.05f;

        [Header("Pan")]
        [Tooltip("World units per second at the default zoom. A city cell is 30 units, so this " +
                 "is roughly two blocks per second.")]
        [SerializeField] float keyboardPanSpeed = 60f;
        [Tooltip("Multiplikator za drag pan misem. Na 1 grad prati kursor tacno 1:1; iznad toga " +
                 "jedan povlacaj pokrije vise grada, po cenu da se tlo pod kursorom pomera brze " +
                 "od samog kursora. Touch ostaje 1:1.")]
        [SerializeField] float dragPanSpeed = 3f;
        [Tooltip("Padding beyond the city bounds the focus point may travel.")]
        [SerializeField] float boundsPadding = 30f;

        [Header("Smoothing")]
        [SerializeField] float moveSmoothing = 12f;
        [SerializeField] float rotateSmoothing = 8f;
        [SerializeField] float zoomSmoothing = 10f;

        [Header("Bounds")]
        [Tooltip("Optional. If set, the focus point is clamped to this builder's generated grid.")]
        [SerializeField] CityBuilder cityBuilder;

        Camera cam;
        Vector3 focusPoint;
        Vector3 targetFocus;
        float targetYaw;
        float targetOrthoSize;

        // Two-finger gesture state. A pinch must never be interpreted as a two-finger pan,
        // so panning is restricted to exactly one active touch.
        float lastPinchDistance;
        float lastTwistAngle;
        bool gestureActive;
        bool mouseDragging;

        // Set for any frame the focus was moved by a direct grab-and-drag, which must not be
        // smoothed. Cleared every frame in SmoothToTargets.
        bool focusSnap;

        Bounds cityBounds;
        bool hasBounds;
        bool framed;
        int boundsAttempts;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;

            targetYaw = yaw;
            targetOrthoSize = Mathf.Clamp(orthoSize, minOrthoSize, maxOrthoSize);
            cam.orthographicSize = targetOrthoSize;

            focusPoint = targetFocus = transform.position + transform.forward * boomLength;
            ApplyTransform();
        }

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Start() => ResolveBounds();

        void Update()
        {
            if (!hasBounds)
                ResolveBounds();

            var touchCount = Touch.activeTouches.Count;

            if (touchCount > 0)
                HandleTouch(touchCount);
            else
                gestureActive = false;

            if (touchCount == 0)
            {
                HandleKeyboardPan();
                HandleKeyboardRotate();
                HandleScrollZoom();
                HandleMouseDragPan();
            }

            ClampFocus();
            SmoothToTargets();
            ApplyTransform();
        }

        void HandleKeyboardPan()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            var input = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

            if (input == Vector2.zero) return;

            // Scale with zoom so the city drifts past at a consistent visual rate.
            var speed = keyboardPanSpeed * (targetOrthoSize / 40f) * Time.unscaledDeltaTime;
            targetFocus += PlanarRight() * (input.x * speed) + PlanarForward() * (input.y * speed);
        }

        void HandleKeyboardRotate()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.qKey.wasPressedThisFrame) targetYaw -= 90f;
            if (kb.eKey.wasPressedThisFrame) targetYaw += 90f;
        }

        void HandleScrollZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            // Only the SIGN is used. Raw scroll deltas are wildly platform-dependent - a
            // Windows wheel notch reports about 120 while a trackpad reports single digits -
            // so scaling by the raw value zooms in a huge jump on one machine and almost
            // imperceptibly on another. A proportional step per event behaves the same on both.
            var direction = Mathf.Sign(scroll);
            var previousSize = targetOrthoSize;
            targetOrthoSize = Mathf.Clamp(
                targetOrthoSize * (1f - direction * scrollZoomSpeed * 0.01f),
                minOrthoSize, maxOrthoSize);

            ZoomTowards(mouse.position.ReadValue(), previousSize);
        }

        /// <summary>
        /// Pins the ground point under the pointer while the zoom level changes, so the map
        /// grows toward whatever is being looked at instead of toward the screen centre.
        /// </summary>
        void ZoomTowards(Vector2 screenPosition, float previousSize)
        {
            // Clamped against min/max: the size did not actually change, so nothing to correct.
            if (Mathf.Approximately(previousSize, targetOrthoSize)) return;
            if (Screen.height <= 0) return;

            // Under an orthographic camera the ground offset from the focus point out to any
            // screen position scales exactly with ortho size, so the anchor is algebra rather
            // than a ground-plane raycast. Keeping the point fixed means
            //   focus' = focus + offset(previous) * (1 - new/previous)
            // and since ScreenDeltaToWorld already measures at the NEW size,
            //   offset(previous) = offset(new) * previous/new
            // which collapses the whole correction into the single scale below.
            var screenOffset = screenPosition - new Vector2(Screen.width, Screen.height) * 0.5f;
            targetFocus += ScreenDeltaToWorld(screenOffset) * (previousSize / targetOrthoSize - 1f);
        }

        /// <summary>
        /// Drag-pan. Space is the modifier rather than left-click on its own, which keeps the
        /// left button free for selecting units and buildings later - no click-versus-drag
        /// threshold needed. Middle-drag stays as a shortcut for mice that have the button;
        /// a trackpad does not, which is why it alone was not enough.
        ///
        /// dragPanSpeed deliberately breaks 1:1 cursor tracking: with a mouse the useful limit
        /// is how far the hand can travel in one stroke, not how well the ground sticks to the
        /// pointer, so covering more city per stroke is worth the ground outrunning the cursor.
        /// Touch keeps the exact 1:1 - there the finger IS the contact point and any multiplier
        /// reads as the map slipping.
        /// </summary>
        void HandleMouseDragPan()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                mouseDragging = false;
                return;
            }

            var kb = Keyboard.current;
            var spaceDrag = kb != null && kb.spaceKey.isPressed && mouse.leftButton.isPressed;

            if (!spaceDrag && !mouse.middleButton.isPressed)
            {
                mouseDragging = false;
                return;
            }

            // Swallow the first frame. mouse.delta is accumulated per frame regardless of button
            // state, so grabbing mid-movement would otherwise jerk the city by whatever the
            // pointer happened to travel before the drag was meant to begin. Same reason
            // HandleTouch only records the pinch baseline on its first frame.
            if (!mouseDragging)
            {
                mouseDragging = true;
                return;
            }

            targetFocus -= ScreenDeltaToWorld(mouse.delta.ReadValue()) * dragPanSpeed;
            focusSnap = true;
        }

        void HandleTouch(int touchCount)
        {
            var touches = Touch.activeTouches;

            if (touchCount == 1)
            {
                gestureActive = false;
                targetFocus -= ScreenDeltaToWorld(touches[0].delta);
                focusSnap = true;
                return;
            }

            // Two or more fingers: zoom and twist only. Never pan - otherwise a pinch drifts
            // the city sideways and the gesture reads as broken.
            var a = touches[0].screenPosition;
            var b = touches[1].screenPosition;
            var separation = Vector2.Distance(a, b);
            var angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

            if (!gestureActive)
            {
                gestureActive = true;
                lastPinchDistance = separation;
                lastTwistAngle = angle;
                return;
            }

            var pinchDelta = separation - lastPinchDistance;
            targetOrthoSize = Mathf.Clamp(
                targetOrthoSize - pinchDelta * pinchZoomSpeed * (targetOrthoSize / 40f),
                minOrthoSize, maxOrthoSize);

            targetYaw -= Mathf.DeltaAngle(lastTwistAngle, angle);

            lastPinchDistance = separation;
            lastTwistAngle = angle;
        }

        /// <summary>
        /// Converts a screen-space drag into world movement on the XZ plane. The result is the
        /// exact 1:1 distance; callers scale it if they want the ground to outrun the pointer.
        /// </summary>
        Vector3 ScreenDeltaToWorld(Vector2 screenDelta)
        {
            if (Screen.height <= 0) return Vector3.zero;

            var worldPerPixel = targetOrthoSize * 2f / Screen.height;

            // The ground is viewed at an angle, so it is foreshortened vertically by sin(pitch):
            // moving the ground d units forward only shifts it d * sin(pitch) pixels up the
            // screen. Dividing that back out is what makes a drag track the cursor. Without it
            // sideways drags are exact but up/down drags fall behind by 1/sin(45) = 1.41x, which
            // is felt as "panning is slow" rather than as an axis being wrong.
            var foreshortening = Mathf.Max(Mathf.Sin(pitch * Mathf.Deg2Rad), 0.1f);

            return PlanarRight() * (screenDelta.x * worldPerPixel)
                 + PlanarForward() * (screenDelta.y * worldPerPixel / foreshortening);
        }

        // Camera axes flattened onto the ground plane. Panning along these rather than world
        // axes is what keeps WASD feeling right after the camera has been rotated.
        Vector3 PlanarForward()
        {
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return forward.normalized;
        }

        Vector3 PlanarRight()
        {
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            return right.normalized;
        }

        /// <summary>
        /// Works out what the camera is allowed to look at.
        ///
        /// CityBuilder.Grid only exists when the city was generated in this session. A city
        /// generated in the editor and saved into the scene has no grid at runtime, so the
        /// fallback measures the placed tiles instead - Tile registers every instance in its
        /// OnEnable, so the list is populated before the first Update.
        /// </summary>
        void ResolveBounds()
        {
            if (!cityBuilder)
                cityBuilder = FindFirstObjectByType<CityBuilder>();

            if (cityBuilder != null && cityBuilder.Grid != null)
            {
                AdoptBounds(cityBuilder.Grid.WorldBounds);
                return;
            }

            if (PolyPerfect.City.Tile.Tiles.Count > 0)
            {
                var bounds = new Bounds(PolyPerfect.City.Tile.Tiles[0].transform.position, Vector3.zero);
                foreach (var tile in PolyPerfect.City.Tile.Tiles)
                    if (tile)
                        bounds.Encapsulate(tile.transform.position);

                bounds.Expand(new Vector3(CityGrid.CellSize, 0f, CityGrid.CellSize));
                AdoptBounds(bounds);
                return;
            }

            // Nothing to measure yet. Give up after a while rather than running a scene-wide
            // search every single frame for the lifetime of the game.
            if (++boundsAttempts > 120)
                hasBounds = true;
        }

        void AdoptBounds(Bounds bounds)
        {
            cityBounds = bounds;
            hasBounds = true;

            if (!framed)
            {
                framed = true;
                targetFocus = focusPoint = new Vector3(bounds.center.x, 0f, bounds.center.z);
            }
        }

        void ClampFocus()
        {
            if (!hasBounds) return;

            var min = cityBounds.min;
            var max = cityBounds.max;
            targetFocus.x = Mathf.Clamp(targetFocus.x, min.x - boundsPadding, max.x + boundsPadding);
            targetFocus.z = Mathf.Clamp(targetFocus.z, min.z - boundsPadding, max.z + boundsPadding);
            targetFocus.y = 0f;
        }

        void SmoothToTargets()
        {
            var dt = Time.unscaledDeltaTime;

            // Direct manipulation is never smoothed. Any lerp lets the ground slide out from
            // under the cursor mid-drag and settle back on release, which reads as sluggish
            // even at a high smoothing rate. Keyboard pan, rotate and zoom keep their easing.
            focusPoint = focusSnap
                ? targetFocus
                : Vector3.Lerp(focusPoint, targetFocus, 1f - Mathf.Exp(-moveSmoothing * dt));
            focusSnap = false;

            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-rotateSmoothing * dt));
            orthoSize = Mathf.Lerp(orthoSize, targetOrthoSize, 1f - Mathf.Exp(-zoomSmoothing * dt));
        }

        void ApplyTransform()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focusPoint - rotation * Vector3.forward * boomLength, rotation);
            cam.orthographicSize = orthoSize;
        }

        void OnValidate()
        {
            maxOrthoSize = Mathf.Max(minOrthoSize, maxOrthoSize);
            orthoSize = Mathf.Clamp(orthoSize, minOrthoSize, maxOrthoSize);
            // Zero would kill drag pan outright, which reads as a broken control rather than
            // as a setting turned down.
            dragPanSpeed = Mathf.Max(0.1f, dragPanSpeed);
        }
    }
}
