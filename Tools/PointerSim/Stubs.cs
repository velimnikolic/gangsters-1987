// Scripted device and UI boundaries only; gesture/admission code comes from Assets.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    }
    public static class Mathf { public static float Max(float a, float b) => System.Math.Max(a, b); }
    public static class Time { public static int frameCount; public static float unscaledTime; }
    public static class Screen { public static int height = 1080; }
    public static class Application { public static bool isFocused = true; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
}
namespace UnityEngine.InputSystem
{
    public class Mouse
    {
        public static Mouse current;
        public Button rightButton = new Button();
        public Position position = new Position();
        public Position delta = new Position(), scroll = new Position();
    }
    public class Button { public bool wasPressedThisFrame, wasReleasedThisFrame, isPressed; }
    public class Position
    {
        public UnityEngine.Vector2 Value;
        public UnityEngine.Vector2 ReadValue() => Value;
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem
    {
        public static EventSystem current = new EventSystem();
        public bool Over;
        public bool IsPointerOverGameObject() => Over;
        public static implicit operator bool(EventSystem value) => value != null;
    }
}
namespace LivingCity.UI { public static class ModalGate { public static bool Any; } }
namespace RoadDemo
{
    public static class CityConditionHud { public static bool PointerOverControls; }
    public static class TurfMapHud { public static bool IsOpen; }
    partial class DemoCamera
    {
        public static bool RightDragTaken;
        internal bool SuppressInput, MapOut, PitchLocked;
        internal float yaw, pitch, distance;
        bool BookOpen => LivingCity.UI.ModalGate.Any;
        float DistanceAfterWheel(float scroll) => distance;
        internal void Poll() => LateUpdate();
    }
    public class CrewBar
    {
        public static CrewBar Instance;
        public bool Contains(UnityEngine.Vector2 at) => true;
    }
    // Admission stops at the dispatch boundary. No shop, crew or physical order
    // is fabricated to claim that an end-to-end gameplay scenario ran.
    partial class CrewOverlay
    {
        const float ClickSlackPx = 8f, ClickHold = 0.45f; // HEAD's admission constants
        bool _rightPending;
        UnityEngine.Vector2 _rightDown;
        float _rightDownAt;
        class Canvas { public float scaleFactor = 1f; }
        Canvas _canvas;
        internal bool _ordersOpen, _aiming, Cover;
        internal int Orders, CoverOrders;
        bool BookOpen => LivingCity.UI.ModalGate.Any;
        bool PointerOverUi() => UnityEngine.EventSystems.EventSystem.current.Over || CrewBar.Instance != null;
        void CloseOrders() => _ordersOpen = false;
        bool CoverAimUnder(UnityEngine.Vector2 at, out int aim) { aim = 0; return Cover; }
        class Unit { public bool Wiped; }
        class Crews { public Unit Selected; }
        Unit _aimCrew;
        Crews _crews = new Crews();
        System.Collections.Generic.List<int> _aimPlan = new System.Collections.Generic.List<int>();
        void HideAimGhosts() { }
        void BeginCoverAim(int aim)
        {
            _aiming = DemoCamera.RightDragTaken = true;
            _aimCrew = _crews.Selected = new Unit();
        }
        internal void TickAim() => TickCoverAim(UnityEngine.InputSystem.Mouse.current);
        internal void Poll() { if (UnityEngine.InputSystem.Mouse.current != null) ReadRightClick(UnityEngine.InputSystem.Mouse.current); }
    }
}
