using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// ARREST IN PROGRESS. The one line that tells the player a window is open and
    /// roughly how it is going to close - which crew is at gunpoint, and whether the
    /// men look like going quietly or like starting something.
    ///
    /// It exists because the arrest stopped being a prompt (CONF-003 took the Y/N keys
    /// out). With no prompt there is nothing on the screen to say that a lieutenant is
    /// about to be off the street for a sentence, and the player's ONE intervention -
    /// an attack order on the officer - has to be made inside a few seconds he cannot
    /// see. So the banner is the window, drawn while it is open and gone the moment it
    /// closes either way.
    ///
    /// Reads and nothing else: no raycaster, no button, the ledger's own typeface, and
    /// the text is only pushed at the label when it has actually changed.
    /// </summary>
    public sealed class ArrestHud : MonoBehaviour
    {
        const float Width = 620f;
        const float Height = 44f;

        static ArrestHud active;

        /// <summary>Static state outlives Play when domain reload is off, and a banner
        /// left over from the last session would be a destroyed component nobody can
        /// clear. Same reset convention as the clock registry and CrewQuarters.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => active = null;

        RectTransform panel;
        TMP_Text headline;
        TMP_Text leaning;
        string shownHead = "";
        string shownLean = "";
        bool up;

        /// <summary>The scene's banner, built the first time the law needs it. Hung on
        /// the dispatcher's own object so a scene with police has one and a scene
        /// without police carries no HUD it never uses.</summary>
        public static ArrestHud For(GameObject host)
        {
            if (active != null) return active;
            if (host == null || !TMP_Settings.instance) return null;
            active = host.AddComponent<ArrestHud>();
            return active;
        }

        /// <summary>Put it up: whose crew, and which way the men are leaning.</summary>
        public void Show(string crewName, string lean)
        {
            if (headline == null) Build();
            if (headline == null) return;

            var head = "ARREST IN PROGRESS — " + (string.IsNullOrEmpty(crewName) ? "A CREW" : crewName.ToUpperInvariant());
            if (head != shownHead) { shownHead = head; headline.SetText(head); }
            var note = string.IsNullOrEmpty(lean) ? "" : "they look " + lean;
            if (note != shownLean) { shownLean = note; leaning.SetText(note); }
            if (!up) { up = true; panel.gameObject.SetActive(true); }
        }

        /// <summary>The window closed - taken in, or it turned into a fight.</summary>
        public void Clear()
        {
            if (!up || panel == null) return;
            up = false;
            panel.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (active == this) active = null;
        }

        void Build()
        {
            var go = new GameObject("Arrest Banner", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // over the notices, under the clock bar and any card the player can click
            canvas.sortingOrder = 90;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var host = new GameObject("Banner", typeof(RectTransform));
            host.transform.SetParent(go.transform, false);
            panel = (RectTransform)host.transform;
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            // under the top bar and the street's own news line
            panel.anchoredPosition = new Vector2(0f, -150f);
            panel.sizeDelta = new Vector2(Width, Height);

            headline = Line(panel, "Headline", 17f, LedgerStyle.HudCream, 0f);
            leaning = Line(panel, "Leaning", 13f, LedgerStyle.RailGold, -22f);
            panel.gameObject.SetActive(false);
        }

        static TMP_Text Line(RectTransform parent, string name, float size, Color ink, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(Width, 22f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = LedgerStyle.Mono;
            label.fontSize = size;
            label.color = ink;
            label.alignment = TextAlignmentOptions.Top;
            label.raycastTarget = false;
            label.text = "";
            return label;
        }
    }
}
