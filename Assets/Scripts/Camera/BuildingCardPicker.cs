using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LivingCity.CameraRig
{
    /// <summary>
    /// Click-to-inspect for catalog-baked buildings, extracted from CatalogExplorer so
    /// every scene that shows bakes (the showroom, the road demo) shares one picking
    /// implementation. Left click raycasts against the footprint BoxCollider every
    /// catalog bake carries and opens the building card; Escape closes it.
    ///
    /// New Input System exclusively; OnGUI here only DRAWS the card, so the
    /// IMGUI-input gap under the new system never matters.
    ///
    /// When pickRoot is set, only colliders under it answer clicks - demo scenes are
    /// full of Synty street/ground colliders that must stay mute. Left null, the
    /// nearest hit decides, which is the showroom's original behaviour.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BuildingCardPicker : MonoBehaviour
    {
        public Transform pickRoot;

        /// <summary>
        /// A same-click veto: another overlay (the road demo's police popup) registers
        /// itself here, and any click it claims never reaches the building raycast -
        /// a click on a patrol unit must not also open the card of the block behind it.
        /// </summary>
        public static System.Func<Vector2, bool> ClickVeto;

        // The card dresses in the Synty InterfaceModernMenus sheet - a dark panel plus a
        // glow triangle flipped into a bubble tail. Editor-only load: both host scenes
        // are dev tooling that never ships, so AssetDatabase is fine and Resources stays clean.
        const string PanelSprite =
            "Assets/Synty/InterfaceModernMenus/Sprites/ModernMenus/SPR_ModernMenus_Box_Medium_04_Dark_Front.png";
        const string TailSprite =
            "Assets/Synty/InterfaceModernMenus/Sprites/General/SPR_ModernMenus_Menu_Triangle_Small_01.png";
        const float PanelSrcBorder = 180f;  // authored 9-slice border (512px texture)
        static readonly Color TailTint = new(0.13f, 0.15f, 0.19f, 0.95f);
        static readonly Color HighlightTint = new(1f, 0.78f, 0.32f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Camera cam;
        string cardTitle;
        string cardBody;
        Vector3 cardAnchor;   // world-space top of the picked building; the card tracks it
        Texture2D panelTex, tailTex;
        GUIStyle titleStyle, bodyStyle;
        readonly System.Collections.Generic.List<Renderer> highlighted = new();

        void Awake()
        {
            cam = GetComponent<Camera>();
#if UNITY_EDITOR
            panelTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PanelSprite);
            tailTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TailSprite);
#endif
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                Pick(mouse.position.ReadValue());
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
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
        /// a packed row. MaterialPropertyBlock, never renderer.material - the bakes
        /// share atlas materials and instantiated copies would outlive the selection.
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
            if (ClickVeto != null && ClickVeto(screen))
            {
                CloseCard();
                return;
            }

            var ray = cam.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, 3000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Footprint boxes sit exactly on bake roots and nowhere else - for a City
            // block that is the individual sub-building child, not the cluster parent,
            // so the collider's own transform IS the building. No climbing.
            Transform root = null;
            foreach (var hit in hits)
            {
                var t = hit.collider.transform;
                if (pickRoot != null && !t.IsChildOf(pickRoot))
                    continue;
                root = t;
                break;
            }
            if (root == null)
            {
                CloseCard();
                return;
            }

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
                .AppendLine($"footprint  {bounds.size.x:F0} x {bounds.size.z:F0} m")
                .Append($"height  {bounds.size.y:F0} m")
                .ToString();
            cardAnchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        void OnGUI()
        {
            if (cardTitle == null)
                return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                titleStyle.normal.textColor = Color.white;
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperCenter };
                bodyStyle.normal.textColor = new Color(0.85f, 0.88f, 0.93f);
            }

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
