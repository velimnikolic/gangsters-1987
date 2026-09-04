using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>
    /// Review-only presentation for PolicePrecinctDemo.  Number keys expose the authored
    /// floors without duplicating any building geometry; B operates the real parking booms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PolicePrecinctShowcase : MonoBehaviour
    {
        enum ViewMode { Exterior, GroundFloor, HoldingCells, UndergroundGarage }

        [SerializeField] PolicePrecinctVisual precinct;
        [SerializeField] DemoCamera cameraRig;

        ViewMode _mode;
        Quaternion[] _barrierClosed = System.Array.Empty<Quaternion>();
        float _barrier;
        bool _barrierOpen;
        bool _lights = true;
        GUIStyle _titleStyle;
        GUIStyle _bodyStyle;

        public void Configure(PolicePrecinctVisual visual, DemoCamera rig)
        {
            precinct = visual;
            cameraRig = rig;
        }

        void Awake()
        {
            CacheBarrierRotations();
            ApplyMode(ViewMode.Exterior, resetCamera: true);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                    ApplyMode(ViewMode.Exterior, resetCamera: true);
                if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                    ApplyMode(ViewMode.GroundFloor, resetCamera: true);
                if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                    ApplyMode(ViewMode.HoldingCells, resetCamera: true);
                if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
                    ApplyMode(ViewMode.UndergroundGarage, resetCamera: true);
                if (keyboard.bKey.wasPressedThisFrame)
                    _barrierOpen = !_barrierOpen;
                if (keyboard.lKey.wasPressedThisFrame)
                {
                    _lights = !_lights;
                    ApplyLights();
                }
                if (keyboard.rKey.wasPressedThisFrame)
                    FrameMode();
            }

            AnimateBarriers(Time.unscaledDeltaTime);
        }

        void CacheBarrierRotations()
        {
            var arms = precinct != null
                ? precinct.ParkingBarrierArms
                : System.Array.Empty<Transform>();
            _barrierClosed = new Quaternion[arms.Length];
            for (int i = 0; i < arms.Length; i++)
                _barrierClosed[i] = arms[i] != null ? arms[i].localRotation : Quaternion.identity;
        }

        void AnimateBarriers(float dt)
        {
            if (precinct == null) return;
            _barrier = Mathf.MoveTowards(_barrier, _barrierOpen ? 1f : 0f,
                                         Mathf.Max(0f, dt) / 1.4f);
            var arms = precinct.ParkingBarrierArms;
            for (int i = 0; i < arms.Length && i < _barrierClosed.Length; i++)
            {
                var arm = arms[i];
                if (arm == null) continue;
                arm.localRotation = _barrierClosed[i] * Quaternion.AngleAxis(
                    precinct.ParkingBarrierLift * _barrier,
                    precinct.ParkingBarrierLocalAxis);
            }
        }

        void ApplyMode(ViewMode mode, bool resetCamera)
        {
            _mode = mode;
            if (precinct == null) return;

            Set(precinct.SiteAndAccess, mode != ViewMode.UndergroundGarage);
            Set(precinct.UndergroundGarage, mode == ViewMode.Exterior ||
                                                  mode == ViewMode.UndergroundGarage);
            Set(precinct.GroundFloor, mode != ViewMode.UndergroundGarage);
            Set(precinct.UpperFloor, mode == ViewMode.Exterior);
            Set(precinct.ExteriorVehicles, mode != ViewMode.UndergroundGarage);
            ApplyLights();
            if (resetCamera) FrameMode();
        }

        void ApplyLights()
        {
            if (precinct != null && precinct.InteriorLighting != null)
                precinct.InteriorLighting.SetActive(_lights);
        }

        void FrameMode()
        {
            if (cameraRig == null || precinct == null) return;

            Vector3 pivot;
            float distance;
            float yaw;
            float pitch;
            switch (_mode)
            {
                case ViewMode.GroundFloor:
                    pivot = CentreOf(precinct.GroundFloor, new Vector3(-5f, 1f, -8f));
                    distance = 42f;
                    yaw = 202f;
                    pitch = 51f;
                    break;
                case ViewMode.HoldingCells:
                    pivot = precinct.HoldingCells != null
                        ? precinct.HoldingCells.position
                        : CentreOf(precinct.GroundFloor, new Vector3(3f, 1f, -17f));
                    pivot.y = 0.8f;
                    distance = 23f;
                    yaw = 198f;
                    pitch = 55f;
                    break;
                case ViewMode.UndergroundGarage:
                    pivot = CentreOf(precinct.UndergroundGarage, new Vector3(-27f, -2.2f, -20f));
                    distance = 38f;
                    yaw = 196f;
                    pitch = 52f;
                    break;
                default:
                    pivot = new Vector3(-8f, 0.7f, -5f);
                    distance = 70f;
                    yaw = 202f;
                    pitch = 41f;
                    break;
            }

            cameraRig.pivot = pivot;
            cameraRig.distance = distance;
            cameraRig.yaw = yaw;
            cameraRig.ConfigurePitch(pitch, 18f);
            cameraRig.Drop();
        }

        static Vector3 CentreOf(GameObject root, Vector3 fallback)
        {
            if (root == null) return fallback;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            var bounds = new Bounds();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found ? bounds.center : fallback;
        }

        static void Set(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        void OnGUI()
        {
            if (precinct == null) return;
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.35f);
            var box = new Rect(18f * scale, Screen.height - 112f * scale,
                               470f * scale, 92f * scale);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 15f * scale, box.y + 10f * scale,
                               box.width - 30f * scale, 31f * scale),
                      "POLICE PRECINCT  /  " + ModeName(), _titleStyle);
            string barrier = _barrierOpen ? "OPENING / OPEN" : "CLOSING / CLOSED";
            GUI.Label(new Rect(box.x + 15f * scale, box.y + 46f * scale,
                               box.width - 30f * scale, 28f * scale),
                      $"{precinct.AuthoredPropCount} authored props  |  " +
                      $"parking barrier {barrier}  |  lights {(_lights ? "ON" : "OFF")}",
                      _bodyStyle);
        }

        string ModeName() => _mode switch
        {
            ViewMode.GroundFloor => "GROUND FLOOR CUTAWAY",
            ViewMode.HoldingCells => "MINI HOLDING CELLS",
            ViewMode.UndergroundGarage => "UNDERGROUND MOTOR POOL",
            _ => "COMPLETE EXTERIOR",
        };

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.84f, 0.92f, 1f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };
        }
    }
}
