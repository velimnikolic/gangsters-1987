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

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
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

            var mouse = Mouse.current;
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
            GUI.Label(new Rect(12f, 8f, 640f, 24f),
                "WASD/strelice: pomeranje   Q/E ili desni klik: rotacija   točkić: zum");
        }
    }
}
