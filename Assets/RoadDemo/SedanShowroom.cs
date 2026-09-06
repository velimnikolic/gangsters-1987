using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>Review navigation only; the cars are reusable authored prefabs.</summary>
    [RequireComponent(typeof(DemoCamera))]
    public sealed class SedanShowroom : MonoBehaviour
    {
        public Transform[] cars;
        public string[] labels;
        public LivingCity.Ambient.CityClock clock;
        public DemoHeadlights headlights;
        public Light editorFill;

        DemoCamera _camera;
        Vector3 _overviewPivot;
        float _overviewDistance, _overviewYaw, _overviewPitch;
        int _focused = -1;
        int _comparison = 3;
        const int FirstUtility = 9;
        const string Controls = "F1-F6: new vehicles   U: new row   1-8: sedans   9: Synty\n" +
            "C: compare   0: all vehicles   L: day/night\n" +
            "WASD/arrows: pan   Q/E or right-drag: orbit   wheel: zoom";

        void Awake()
        {
            _camera = GetComponent<DemoCamera>();
            _overviewPivot = _camera.pivot;
            _overviewDistance = _camera.distance;
            _overviewYaw = _camera.yaw;
            _overviewPitch = _camera.pitch;
            _camera.showHint = true;
            NewVehicles();
        }

        void Start()
        {
            // A frozen shared clock avoids borrowing its 1-5 speed shortcuts.
            // DemoSky and DemoHeadlights still consume exactly the game's hour.
            if (clock) { clock.enabled = false; clock.Configure(14f, 600f); clock.Running = false; }
            if (editorFill) editorFill.enabled = false; // DemoSky supplies the Play moon/fill.
            if (headlights && cars != null)
                foreach (var car in cars)
                    if (car && car.TryGetComponent<VehicleLampRig>(out _)) headlights.Register(car, 2.3f);
            RefreshHint();
        }

        void RefreshHint()
        {
            string label = _focused >= 0 && cars != null && _focused < cars.Length && cars[_focused]
                ? (labels != null && _focused < labels.Length ? labels[_focused] : cars[_focused].name)
                : (_focused == -2 ? cars[_comparison].name + " / SYNTY SEDAN"
                    : _focused == -3 ? "NEW UTILITY VEHICLES / awaiting approval" : "MIAMI 1987 / complete collection");
            _camera.hint = label + (clock && clock.Hour > 20f ? " / NIGHT" : " / DAY") + "\n" + Controls;
        }

        void Overview()
        {
            _camera.Drop();
            _camera.pivot = _overviewPivot;
            _camera.distance = _overviewDistance;
            _camera.yaw = _overviewYaw;
            _camera.pitch = _overviewPitch;
            _focused = -1;
            RefreshHint();
        }

        void NewVehicles()
        {
            if (cars == null || cars.Length <= FirstUtility) { Overview(); return; }
            Vector3 centre = Vector3.zero;
            int count = 0;
            for (int i = FirstUtility; i < cars.Length; i++)
                if (cars[i]) { centre += cars[i].position; count++; }
            if (count == 0) { Overview(); return; }
            _camera.Drop();
            _camera.pivot = centre / count + Vector3.up;
            _camera.distance = 32f;
            _camera.yaw = 180f;
            _camera.pitch = 32f;
            _focused = -3;
            RefreshHint();
        }

        void Focus(int index)
        {
            if (cars == null || index < 0 || index >= cars.Length || !cars[index]) return;
            _camera.Drop();
            _camera.pivot = cars[index].position + Vector3.up * (index >= FirstUtility ? 0.9f : 0.65f);
            _camera.distance = index >= FirstUtility ? 11f : 9.5f;
            _camera.yaw = cars[index].eulerAngles.y + 145f;
            _camera.pitch = 26f;
            _focused = index;
            if (index != 8) _comparison = index;
            RefreshHint();
        }

        void Compare()
        {
            if (cars == null || cars.Length < 9 || !cars[_comparison] || !cars[8]) return;
            _camera.Drop();
            _camera.pivot = (cars[_comparison].position + cars[8].position) * 0.5f + Vector3.up * 0.7f;
            _camera.distance = Mathf.Max(14f, Vector3.Distance(cars[_comparison].position, cars[8].position) * 1.25f + 7f);
            _camera.yaw = 160f;
            _camera.pitch = 24f;
            _focused = -2;
            RefreshHint();
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.lKey.wasPressedThisFrame && clock)
            {
                clock.SetHour(clock.Hour > 20f ? 14f : 23f);
                RefreshHint();
            }
            if (keys.digit0Key.wasPressedThisFrame) Overview();
            if (keys.digit1Key.wasPressedThisFrame) Focus(0);
            if (keys.digit2Key.wasPressedThisFrame) Focus(1);
            if (keys.digit3Key.wasPressedThisFrame) Focus(2);
            if (keys.digit4Key.wasPressedThisFrame) Focus(3);
            if (keys.digit5Key.wasPressedThisFrame) Focus(4);
            if (keys.digit6Key.wasPressedThisFrame) Focus(5);
            if (keys.digit7Key.wasPressedThisFrame) Focus(6);
            if (keys.digit8Key.wasPressedThisFrame) Focus(7);
            if (keys.digit9Key.wasPressedThisFrame) Focus(8);
            if (keys.cKey.wasPressedThisFrame) Compare();
            if (keys.uKey.wasPressedThisFrame) NewVehicles();
            if (keys.f1Key.wasPressedThisFrame) Focus(FirstUtility);
            if (keys.f2Key.wasPressedThisFrame) Focus(FirstUtility + 1);
            if (keys.f3Key.wasPressedThisFrame) Focus(FirstUtility + 2);
            if (keys.f4Key.wasPressedThisFrame) Focus(FirstUtility + 3);
            if (keys.f5Key.wasPressedThisFrame) Focus(FirstUtility + 4);
            if (keys.f6Key.wasPressedThisFrame) Focus(FirstUtility + 5);
        }
    }
}
