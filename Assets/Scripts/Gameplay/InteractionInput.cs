using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// What the interaction layer needs to know about the pointer, and nothing about where
    /// it came from. Desktop answers with mouse buttons; the touch implementation (tap /
    /// 0.4s long-press) drops in behind the same five reads without the gameplay code
    /// changing - which is the whole reason this interface exists, because mobile has no
    /// right mouse button to poll.
    /// </summary>
    public interface IInteractionInput
    {
        /// <summary>Select / move. Went down this frame.</summary>
        bool PrimaryPressed { get; }

        /// <summary>Context menu. Went down this frame.</summary>
        bool SecondaryPressed { get; }

        /// <summary>Close whatever is open. Went down this frame.</summary>
        bool CancelPressed { get; }

        Vector2 PointerPosition { get; }

        /// <summary>The camera's drag-pan chord is engaged - the pointer is spoken for.</summary>
        bool PanModifierHeld { get; }

        /// <summary>Hands up - the answer to a police warning window. Went down this frame.</summary>
        bool SurrenderPressed { get; }
    }

    /// <summary>
    /// Mouse and keyboard through the Input System's direct device API - the project's
    /// established input style (no .inputactions asset anywhere). Space is the camera
    /// controller's drag-pan chord, reserved by it on purpose.
    /// </summary>
    public sealed class DesktopInteractionInput : IInteractionInput
    {
        public bool PrimaryPressed =>
            Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        public bool SecondaryPressed =>
            Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        public bool CancelPressed =>
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        public Vector2 PointerPosition =>
            Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        public bool PanModifierHeld =>
            Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

        public bool SurrenderPressed =>
            Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame;
    }
}
