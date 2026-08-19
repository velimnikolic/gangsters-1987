using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    // Free-look demo camera: WASD/arrows pan, Q/E rotate, mouse wheel zoom,
    // right-drag orbit. Uses the new Input System (the project runs InputSystem-only).
    public class DemoCamera : MonoBehaviour
    {
        public Vector3 pivot;
        public float distance = 170f;
        public float yaw = 35f;
        public float pitch = 52f;

        /// <summary>Where the thing the camera is riding is now, or null when there is
        /// nothing left of it to watch. Null itself while the camera is the player's
        /// own - which is all the time until someone calls <see cref="Ride"/>.</summary>
        System.Func<Vector3?> _ride;

        /// <summary>How quickly the pivot closes on what it rides. A man on foot is
        /// held dead centre; a car at speed runs a metre or two ahead of the picture -
        /// the camera eases after it instead of snapping about behind every turn.</summary>
        const float RideEase = 6f;

        /// <summary>The key line under the top bar. The road demo's by default; a
        /// scene with other clicks to explain writes its own.</summary>
        public string hint =
            "WASD/arrows: move   Q/E or right-click: rotate   wheel: zoom   " +
            "click a building: card   O: lot info   click a lieutenant: select   " +
            "right-click: send his crew   M: mute";

        /// <summary>Pixels the hint sits below the top of the screen (the road demo's
        /// top bar is 42 canvas-px on the 1080 reference height).</summary>
        public float hintTopPx = 42f;

        /// <summary>Whether the key line is drawn at all. Off: the road demo wants a
        /// clean picture, and IMGUI over the scene costs a little every frame besides.
        /// A scene that needs its controls explained turns it on.</summary>
        public bool showHint = false;

        /// <summary>Debug readout of the boom in the bottom-left corner: how far back
        /// the camera sits, and at what angles. On while the demos are being tuned -
        /// the numbers are the ones to quote when a zoom level looks wrong.</summary>
        public bool showZoom = true;

        /// <summary>The book owns the screen while it is open - its own keys, its own
        /// wheel (the roster scrolls on it) - and the map takes the half of the world
        /// that is still visible. Nothing the player does over either may also steer
        /// the camera underneath.</summary>
        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = BookOpen ? null : Keyboard.current;
            if (kb != null)
            {
                Vector2 pan = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pan.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pan.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) pan.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) pan.x -= 1f;
                if (pan != Vector2.zero)
                {
                    var flat = Quaternion.Euler(0f, yaw, 0f);
                    pivot += (flat * Vector3.forward * pan.y + flat * Vector3.right * pan.x)
                             * (distance * 0.55f * dt);
                    _ride = null; // the player moved the camera himself: the ride is over
                }
                if (kb.qKey.isPressed) yaw -= 70f * dt;
                if (kb.eKey.isPressed) yaw += 70f * dt;
            }

            var mouse = BookOpen ? null : Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f) distance *= 1f - Mathf.Sign(scroll) * 0.09f;
                if (mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * 0.25f;
                    pitch -= d.y * 0.2f;
                }
            }

            // Riding a crew: the pivot goes where he goes, and keeps going, until the
            // player pans off it. Turning and zooming leave the ride alone - they only
            // look at the same man from somewhere else.
            if (_ride != null)
            {
                var at = _ride();
                if (at.HasValue) pivot = Vector3.Lerp(pivot, at.Value, 1f - Mathf.Exp(-RideEase * dt));
                else _ride = null; // nothing left of him to watch; the camera stays where it stopped
            }

            distance = Mathf.Clamp(distance, 18f, 900f); // far enough back to take in a city of ninety blocks
            pitch = Mathf.Clamp(pitch, 22f, 82f);

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0f, -distance), rot);
        }

        /// <summary>Is the camera riding someone?</summary>
        public bool Riding => _ride != null;

        /// <summary>Ride a man - or the car he is in: the camera goes to him now and
        /// stays with him wherever he walks or drives, until the player pans away. The
        /// call gives his place each frame, and null once there is nothing left of him
        /// to follow.</summary>
        public void Ride(System.Func<Vector3?> where)
        {
            _ride = where;
            if (where == null) return;
            var at = where();
            if (at.HasValue) pivot = at.Value;
        }

        /// <summary>Let go - the camera is the player's again.</summary>
        public void Drop() => _ride = null;

        void OnGUI()
        {
            // IMGUI prints over every canvas in the scene, so this would land on the
            // open book itself - and none of what the hint names works there anyway.
            if (BookOpen)
                return;

            if (showHint)
            {
                // below the top bar, which spans the full width at 42 canvas-px
                // (reference height 1080) - convert to real screen pixels
                float barPx = UnityEngine.Screen.height / 1080f * hintTopPx;
                GUI.Label(new Rect(12f, barPx + 6f, 1400f, 24f), hint);
            }

            if (showZoom)
            {
                // bottom-left, over whatever the scene draws there: the boom in metres
                // is what a "too close / too far" report needs to be reproducible, and
                // the angles go with it because the same distance reads differently
                // from 22 degrees than from 82.
                string line = string.Format(
                    "zoom {0:0.0} m   pitch {1:0}   yaw {2:0}   pivot {3:0}, {4:0}{5}",
                    distance, pitch, Mathf.Repeat(yaw, 360f), pivot.x, pivot.z,
                    _ride != null ? "   [riding]" : string.Empty);
                var at = new Rect(12f, UnityEngine.Screen.height - 26f, 620f, 22f);
                var was = GUI.color;
                GUI.color = Color.black; // cheap drop shadow: the sky is too pale for grey text
                GUI.Label(new Rect(at.x + 1f, at.y + 1f, at.width, at.height), line);
                GUI.color = Color.white;
                GUI.Label(at, line);
                GUI.color = was;
            }
        }
    }
}
