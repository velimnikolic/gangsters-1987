using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>THE LEFT BUTTON, READ ONCE FOR THE WHOLE FRAME. A press held and moved
    /// is the player taking hold of the ground to shove the picture about; a press
    /// released where it went down is the click that selects a man or opens a card.
    /// The two cannot both be answered on the press, so nothing in the city reads
    /// <c>leftButton.wasPressedThisFrame</c> any more - it asks here, and gets the
    /// click on the RELEASE, only when no drag happened in between.
    ///
    /// The right button has kept exactly this shape for orders since CrewOverlay was
    /// written (press pending, drag turns the aim, release orders); this is that rule
    /// for the left one, in one place, so every scene agrees about what a click is.
    /// </summary>
    public static class PointerGesture
    {
        /// <summary>Pixels of travel, on the 1080-line reference screen, that turn a
        /// press into a drag. Small enough that a deliberate shove takes hold at once,
        /// wide enough that a hand shaking on a click never loses it.</summary>
        public const float DragSlopPx = 5f;

        static int _frame = -1;
        static bool _held, _dragged, _clicked, _taken;
        static Vector2 _from, _at;

        /// <summary>A click was completed this frame: pressed and released without the
        /// pointer wandering, and not spent on a screen.</summary>
        public static bool Clicked { get { Read(); return _clicked; } }

        /// <summary>Where that click landed.</summary>
        public static Vector2 ClickAt { get { Read(); return _at; } }

        /// <summary>The button is down and has moved: the camera has the ground.</summary>
        public static bool Dragging { get { Read(); return _held && _dragged && !_taken; } }

        /// <summary>SOMEBODY ELSE OWNS THIS DRAG. The turf map's marquee and the
        /// strategic map's own hand-drag read the left button themselves; they say so
        /// here and the street camera leaves the button alone for as long as they do.
        /// </summary>
        public static bool DragTaken { get; set; }

        /// <summary>Claim a UI press immediately, even if the EventSystem's hover
        /// cache was read before its raycast this frame. Ownership lasts to release.</summary>
        public static void ClaimPress()
        {
            Read();
            _taken = true;
            _clicked = false;
        }

        static void Read()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _clicked = false;

            var mouse = Mouse.current;
            if (mouse == null)
            {
                _held = _dragged = false;
                return;
            }

            _at = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _held = true;
                _dragged = false;
                _from = _at;
                // A press that went down on a screen belongs to that screen for its
                // whole life: it can neither drag the city nor click it on release.
                _taken = OverUi();
            }

            float slop = DragSlopPx * Mathf.Max(1f, Screen.height / 1080f);
            if (_held && !_dragged && (_at - _from).sqrMagnitude > slop * slop)
                _dragged = true;

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _clicked = _held && !_dragged && !_taken;
                _held = _dragged = _taken = false;
            }
        }

        static bool OverUi() =>
            (UnityEngine.EventSystems.EventSystem.current &&
             UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) ||
            (CrewBar.Instance != null && Mouse.current != null &&
             CrewBar.Instance.Contains(Mouse.current.position.ReadValue()));
    }
}
