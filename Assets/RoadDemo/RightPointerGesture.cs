using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>One right-button gesture shared by street orders and camera orbit.
    /// A stationary press is a click regardless of duration; crossing the drag
    /// threshold spends the press even if the pointer returns to its origin.</summary>
    public static class RightPointerGesture
    {
        const float DragSlopPx = 8f;

        static int frame = -1;
        static bool held, dragged, taken, dragTaken, clicked;
        static Vector2 from, at;

        public static bool Clicked { get { Read(); return clicked; } }
        public static bool Dragging { get { Read(); return held && dragged && !dragTaken; } }
        public static Vector2 ClickAt { get { Read(); return at; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            frame = -1;
            held = dragged = taken = dragTaken = clicked = false;
            from = at = default;
        }

        static void Read()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            clicked = false;

            var mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
            {
                held = dragged = taken = dragTaken = false;
                return;
            }

            at = mouse.position.ReadValue();
            bool blocked = LivingCity.UI.ModalGate.Any ||
                (UnityEngine.EventSystems.EventSystem.current &&
                 UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) ||
                (CrewBar.Instance != null && CrewBar.Instance.Contains(at));

            if (mouse.rightButton.wasPressedThisFrame)
            {
                held = true;
                dragged = false;
                taken = blocked;
                dragTaken = blocked;
                from = at;
            }

            // Crossing UI cancels a pending order, but an orbit already started on
            // the street keeps its drag. A modal takes both for the rest of the hold.
            if (held)
            {
                taken |= blocked;
                dragTaken |= LivingCity.UI.ModalGate.Any;
                float slop = DragSlopPx * Mathf.Max(1f, Screen.height / 1080f);
                if ((at - from).sqrMagnitude > slop * slop)
                    dragged = true;
            }

            // Both edges can arrive in one frame. The final level distinguishes
            // press-then-release from release-then-press. In the latter case keep
            // the new hold; coalesce the old release rather than order at a new
            // press position that the polling API cannot identify as its release.
            if (mouse.rightButton.wasReleasedThisFrame && !mouse.rightButton.isPressed)
            {
                clicked = held && !dragged && !taken;
                held = dragged = taken = dragTaken = false;
            }
            else if (!mouse.rightButton.isPressed)
            {
                // A lost release (device/focus change) must not leave an order armed.
                held = dragged = taken = dragTaken = false;
            }
        }
    }
}
