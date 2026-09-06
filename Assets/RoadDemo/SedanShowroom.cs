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

        DemoCamera _camera;
        Vector3 _overviewPivot;
        float _overviewDistance, _overviewYaw, _overviewPitch;
        const string Controls = "1-8: inspect car   0: full lineup\n" +
            "WASD/arrows: pan   Q/E or right-drag: orbit   wheel: zoom";

        void Awake()
        {
            _camera = GetComponent<DemoCamera>();
            _overviewPivot = _camera.pivot;
            _overviewDistance = _camera.distance;
            _overviewYaw = _camera.yaw;
            _overviewPitch = _camera.pitch;
            _camera.showHint = true;
            Overview();
        }

        void Overview()
        {
            _camera.Drop();
            _camera.pivot = _overviewPivot;
            _camera.distance = _overviewDistance;
            _camera.yaw = _overviewYaw;
            _camera.pitch = _overviewPitch;
            _camera.hint = "MIAMI 1987 / luxury to everyday\n" + Controls;
        }

        void Focus(int index)
        {
            if (cars == null || index >= cars.Length || !cars[index]) return;
            _camera.Drop();
            _camera.pivot = cars[index].position + Vector3.up * 0.65f;
            _camera.distance = 9.5f;
            _camera.yaw = cars[index].eulerAngles.y + 145f;
            _camera.pitch = 26f;
            string label = labels != null && index < labels.Length
                ? labels[index] : cars[index].name;
            _camera.hint = label + "\n" + Controls;
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.digit0Key.wasPressedThisFrame) Overview();
            if (keys.digit1Key.wasPressedThisFrame) Focus(0);
            if (keys.digit2Key.wasPressedThisFrame) Focus(1);
            if (keys.digit3Key.wasPressedThisFrame) Focus(2);
            if (keys.digit4Key.wasPressedThisFrame) Focus(3);
            if (keys.digit5Key.wasPressedThisFrame) Focus(4);
            if (keys.digit6Key.wasPressedThisFrame) Focus(5);
            if (keys.digit7Key.wasPressedThisFrame) Focus(6);
            if (keys.digit8Key.wasPressedThisFrame) Focus(7);
        }
    }
}
