using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LivingCity.CameraRig
{
    /// <summary>
    /// Play-mode explorer for the building-catalog showroom. Self-installs on the main
    /// camera when BuildingCatalog.unity enters Play - the scene is a build product the
    /// catalog pass regenerates wholesale, so nothing may be hand-added or saved in it
    /// (the same rule the city's runtime layers follow).
    ///
    /// New Input System exclusively - activeInputHandler is "Input System Package (New)"
    /// and a legacy UnityEngine.Input call would throw. Movement lives here; the click
    /// card is BuildingCardPicker, shared with the road demo.
    ///
    /// Controls: WASD pan, Q/E down/up, shift to hurry, scroll zooms along the view ray,
    /// right-drag orbits, left click on a building opens its card, Escape closes it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CatalogExplorer : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "BuildingCatalog")
                return;
            var camera = Camera.main;
            if (!camera)
                return;
            if (!camera.GetComponent<CatalogExplorer>())
                camera.gameObject.AddComponent<CatalogExplorer>();
            if (!camera.GetComponent<BuildingCardPicker>())
                camera.gameObject.AddComponent<BuildingCardPicker>();
        }

        const float PanSpeed = 120f;       // m/s; two catalog cells per second (doubled per the user)
        const float HurryMultiplier = 3f;
        const float ZoomStep = 8f;         // metres per scroll notch, along the view ray
        const float OrbitSpeed = 0.15f;    // degrees per pixel of right-drag

        float yaw;
        float pitch;
        GUIStyle hintStyle;

        void Awake()
        {
            var angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null)
                return;

            var speed = PanSpeed * (kb.leftShiftKey.isPressed ? HurryMultiplier : 1f)
                                 * Time.deltaTime;
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            var move = Vector3.zero;
            if (kb.wKey.isPressed) move += forward;
            if (kb.sKey.isPressed) move -= forward;
            if (kb.dKey.isPressed) move += right;
            if (kb.aKey.isPressed) move -= right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;
            transform.position += move * speed;

            // Scroll dives along the view ray - the natural zoom for a perspective rig.
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                transform.position += transform.forward * Mathf.Sign(scroll) * ZoomStep;

            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * OrbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * OrbitSpeed, 5f, 85f);
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void OnGUI()
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }

            GUI.Label(new Rect(12f, 8f, 940f, 22f),
                "WASD move · Q/E height · shift fast · scroll zoom · right-click + mouse orbit · click a building = card",
                hintStyle);
        }
    }
}
