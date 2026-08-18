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

        /// <summary>The key line under the top bar. The road demo's by default; a
        /// scene with other clicks to explain writes its own.</summary>
        public string hint =
            "WASD/arrows: move   Q/E or right-click: rotate   wheel: zoom   " +
            "click a building: card   O: lot info   click a lieutenant: select   " +
            "right-click: send his crew   M: mute";

        /// <summary>Pixels the hint sits below the top of the screen (the road demo's
        /// top bar is 42 canvas-px on the 1080 reference height).</summary>
        public float hintTopPx = 42f;

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

            distance = Mathf.Clamp(distance, 18f, 520f);
            pitch = Mathf.Clamp(pitch, 22f, 82f);

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0f, -distance), rot);
        }

        void OnGUI()
        {
            // IMGUI prints over every canvas in the scene, so the hint would land on
            // the open book itself - and none of what it names works there anyway.
            if (BookOpen)
                return;

            // below the top bar, which spans the full width at 42 canvas-px
            // (reference height 1080) - convert to real screen pixels
            float barPx = UnityEngine.Screen.height / 1080f * hintTopPx;
            GUI.Label(new Rect(12f, barPx + 6f, 1400f, 24f), hint);
        }
    }
}
