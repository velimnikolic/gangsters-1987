using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LivingCity.CameraRig
{
    /// <summary>
    /// Play-mode explorer for the building-catalog showroom. Self-installs on the main
    /// camera when BuildingCatalog.unity enters Play - the scene is a build product the
    /// catalog pass regenerates wholesale, so nothing may be hand-added or saved in it
    /// (the same rule the city's runtime layers follow).
    ///
    /// New Input System exclusively - activeInputHandler is "Input System Package (New)"
    /// and a legacy UnityEngine.Input call would throw. OnGUI here only DRAWS (a hint
    /// line and the building card); picking is a Physics.Raycast against the footprint
    /// BoxCollider every catalog bake carries, so the IMGUI-input gap under the new
    /// system never matters.
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
            if (camera && !camera.GetComponent<CatalogExplorer>())
                camera.gameObject.AddComponent<CatalogExplorer>();
        }

        const float PanSpeed = 60f;        // m/s; a catalog cell is 60, so one cell per second
        const float HurryMultiplier = 3f;
        const float ZoomStep = 8f;         // metres per scroll notch, along the view ray
        const float OrbitSpeed = 0.15f;    // degrees per pixel of right-drag

        // The card dresses in the Synty InterfaceModernMenus sheet - a dark panel plus a
        // glow triangle flipped into a bubble tail. Editor-only load: the catalog is a
        // dev showroom that never ships, so AssetDatabase is fine and Resources stays clean.
        const string PanelSprite =
            "Assets/Synty/InterfaceModernMenus/Sprites/ModernMenus/SPR_ModernMenus_Box_Medium_04_Dark_Front.png";
        const string TailSprite =
            "Assets/Synty/InterfaceModernMenus/Sprites/General/SPR_ModernMenus_Menu_Triangle_Small_01.png";
        const float PanelSrcBorder = 180f;  // authored 9-slice border (512px texture)
        static readonly Color TailTint = new(0.13f, 0.15f, 0.19f, 0.95f);
        static readonly Color HighlightTint = new(1f, 0.78f, 0.32f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Camera cam;
        float yaw;
        float pitch;
        string cardTitle;
        string cardBody;
        Vector3 cardAnchor;   // world-space top of the picked building; the card tracks it
        Texture2D panelTex, tailTex;
        GUIStyle titleStyle, bodyStyle, hintStyle;
        readonly System.Collections.Generic.List<Renderer> highlighted = new();

        void Awake()
        {
            cam = GetComponent<Camera>();
            var angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x;
#if UNITY_EDITOR
            panelTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PanelSprite);
            tailTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TailSprite);
#endif
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

            if (mouse.leftButton.wasPressedThisFrame)
                Pick(mouse.position.ReadValue());
            if (kb.escapeKey.wasPressedThisFrame)
                CloseCard();
        }

        void CloseCard()
        {
            cardTitle = null;
            foreach (var renderer in highlighted)
                if (renderer)
                    renderer.SetPropertyBlock(null);
            highlighted.Clear();
        }

        /// <summary>
        /// Gold-tints every renderer of the picked building so it reads unambiguously in
        /// a packed row. MaterialPropertyBlock, never renderer.material - the catalog
        /// shares atlas materials and instantiated copies would outlive the selection.
        /// </summary>
        void Highlight(Transform root)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, HighlightTint);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.SetPropertyBlock(block);
                highlighted.Add(renderer);
            }
        }

        void OnDisable() => CloseCard();

        void Pick(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 3000f))
            {
                CloseCard();
                return;
            }

            // Footprint boxes sit exactly on bake roots and nowhere else - for a City
            // block that is the individual sub-building child, not the cluster parent,
            // so the collider's own transform IS the building. No climbing.
            var root = hit.collider.transform;

            var bounds = new Bounds(root.position, Vector3.zero);
            var first = true;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            CloseCard();
            Highlight(root);
            cardTitle = root.name;
            cardBody = new StringBuilder()
                .AppendLine($"osnova  {bounds.size.x:F0} x {bounds.size.z:F0} m")
                .Append($"visina  {bounds.size.y:F0} m")
                .ToString();
            cardAnchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                titleStyle.normal.textColor = Color.white;
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperCenter };
                bodyStyle.normal.textColor = new Color(0.85f, 0.88f, 0.93f);
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }

            GUI.Label(new Rect(12f, 8f, 940f, 22f),
                "WASD pomeranje · Q/E visina · shift brzo · scroll zoom · desni klik + mis rotacija · klik na zgradu = kartica",
                hintStyle);

            if (cardTitle == null)
                return;

            // Anchor the card to the building's roof point, like a real popup. z < 0
            // means the anchor is behind the camera - drawing would mirror the point
            // back onto the screen, so hide instead.
            var sp = cam.WorldToScreenPoint(cardAnchor);
            if (sp.z < 0f)
                return;

            const float width = 250f, height = 100f, tail = 22f, gap = 4f;
            var x = Mathf.Clamp(sp.x - width / 2f, 8f, Screen.width - width - 8f);
            var anchorGuiY = Screen.height - sp.y;
            var y = Mathf.Clamp(anchorGuiY - gap - tail - height, 32f, Screen.height - height - 8f);
            var rect = new Rect(x, y, width, height);

            if (panelTex)
            {
                // Tail first so the panel overlaps its base; V-flipped, the glow
                // triangle points down at the roof. It slides with the anchor even
                // when the panel itself is clamped at a screen edge.
                if (tailTex)
                {
                    var tailX = Mathf.Clamp(sp.x, rect.x + 24f, rect.xMax - 24f);
                    var old = GUI.color;
                    GUI.color = TailTint;
                    GUI.DrawTextureWithTexCoords(
                        new Rect(tailX - tail, rect.yMax - 6f, tail * 2f, tail + 6f),
                        tailTex, new Rect(0f, 1f, 1f, -1f));
                    GUI.color = old;
                }
                DrawSliced(rect, panelTex, PanelSrcBorder, 26f);
            }
            else
            {
                GUI.Box(rect, GUIContent.none);
            }

            GUI.Label(new Rect(rect.x + 18f, rect.y + 14f, rect.width - 36f, 26f), cardTitle, titleStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 42f, rect.width - 36f, 48f), cardBody, bodyStyle);
        }

        /// <summary>
        /// Manual 9-slice: GUIStyle.border slices texture and screen at the SAME pixel
        /// count, which mangles a 512px sprite with a 180px authored border on a
        /// 100px-tall card. Nine DrawTextureWithTexCoords calls keep the two scales free.
        /// </summary>
        static void DrawSliced(Rect dst, Texture2D tex, float srcBorder, float dstBorder)
        {
            var u = srcBorder / tex.width;
            var v = srcBorder / tex.height;
            var xs = new[] { dst.x, dst.x + dstBorder, dst.xMax - dstBorder, dst.xMax };
            var ys = new[] { dst.y, dst.y + dstBorder, dst.yMax - dstBorder, dst.yMax };
            var us = new[] { 0f, u, 1f - u, 1f };
            var vs = new[] { 1f, 1f - v, v, 0f };   // GUI y grows down, UV up
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 3; col++)
                    GUI.DrawTextureWithTexCoords(
                        Rect.MinMaxRect(xs[col], ys[row], xs[col + 1], ys[row + 1]),
                        tex,
                        Rect.MinMaxRect(us[col], vs[row + 1], us[col + 1], vs[row]));
        }
    }
}
