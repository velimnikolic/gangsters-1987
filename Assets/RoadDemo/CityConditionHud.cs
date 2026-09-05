using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>Shared graphics settings; the showroom also exposes the simulation input.</summary>
    public sealed class CityConditionHud : MonoBehaviour
    {
        ResidentialConditionDemo demo;
        RectTransform panel;
        GameObject settings;
        TMP_Text conditionLabel, densityLabel;
        Slider densitySlider, conditionSlider;
        int shownNeglect = -1, shownDensity = -1;
        public static bool PointerOverControls
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null) return false;
                foreach (var hud in instances)
                    if (hud && hud.panel && hud.panel.gameObject.activeInHierarchy &&
                        (RectTransformUtility.RectangleContainsScreenPoint(hud.panel, mouse.position.ReadValue()) ||
                         (mouse.leftButton.isPressed && EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                          EventSystem.current.currentSelectedGameObject.transform.IsChildOf(hud.panel)))) return true;
                return false;
            }
        }
        static readonly System.Collections.Generic.List<CityConditionHud> instances = new System.Collections.Generic.List<CityConditionHud>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => instances.Clear();
        public static void Ensure(DemoCamera rig, ResidentialConditionDemo demo = null)
        {
            if (!rig) return;
            var hud = rig.GetComponent<CityConditionHud>();
            if (!hud) hud = rig.gameObject.AddComponent<CityConditionHud>();
            if (demo) hud.demo = demo;
        }
        void Start()
        {
            if (!demo) demo = GetComponent<ResidentialConditionDemo>();
            instances.Add(this);
            if (!EventSystem.current)
            {
                var events = new GameObject("City settings input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            var canvas = new GameObject("City graphics settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var c = canvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 80;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            panel = Rect("Controls", canvas.transform, new Vector2(390, demo ? 178 : 54));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1); panel.anchoredPosition = new Vector2(-16, -16);
            panel.gameObject.AddComponent<Image>().color = new Color(.055f, .065f, .075f, .96f);
            var button = Rect("Settings", panel, new Vector2(150, 34), new Vector2(226, -12));
            button.gameObject.AddComponent<Image>().color = new Color(.22f, .25f, .27f);
            var toggle = button.gameObject.AddComponent<Button>();
            Label(button, "SETTINGS", 18, Vector2.zero, new Vector2(145, 32));
            if (demo)
            {
                conditionLabel = Label(panel, "", 20, new Vector2(14, -55), new Vector2(362, 28));
                conditionSlider = MakeSlider(panel, new Vector2(18, -102)); conditionSlider.SetValueWithoutNotify(demo.neglect);
                conditionSlider.onValueChanged.AddListener(value => demo.neglect = value);
                Label(panel, "NORMALAN                         ZAPUSTEN", 14, new Vector2(18, -125), new Vector2(354, 24));
                Label(panel, "HOME: svi blokovi  |  WASD, Q/E, RMB, tocak", 13, new Vector2(14, -151), new Vector2(366, 22));
            }
            settings = Rect("Decoration settings", panel, new Vector2(390, 108), new Vector2(0, demo ? -178 : -54)).gameObject;
            settings.AddComponent<Image>().color = new Color(.055f, .065f, .075f, .96f);
            densityLabel = Label(settings.transform, "", 18, new Vector2(14, -8), new Vector2(362, 28));
            densitySlider = MakeSlider(settings.transform, new Vector2(18, -55));
            densitySlider.SetValueWithoutNotify(CityDecorationSettings.Density);
            densitySlider.onValueChanged.AddListener(value => CityDecorationSettings.Density = value);
            Label(settings.transform, "Kolicina sitne dekoracije", 14, new Vector2(18, -77), new Vector2(354, 24));
            settings.SetActive(false);
            toggle.onClick.AddListener(() =>
            {
                settings.SetActive(!settings.activeSelf);
                panel.sizeDelta = new Vector2(390, (demo ? 178 : 54) + (settings.activeSelf ? 108 : 0));
                if (!settings.activeSelf) PlayerPrefs.Save();
            });
        }
        void Update()
        {
            if (!panel) return;
            panel.gameObject.SetActive(!LivingCity.UI.ModalGate.ScreenTaken && !TurfMapHud.IsOpen);
            int neglectPercent = demo ? Mathf.RoundToInt(demo.neglect * 100) : 0;
            if (demo && shownNeglect != neglectPercent)
            {
                shownNeglect = neglectPercent;
                conditionLabel.text = $"ZAPUSTENOST BLOKOVA: {shownNeglect}%";
                conditionSlider.SetValueWithoutNotify(demo.neglect);
            }
            int densityPercent = Mathf.RoundToInt(CityDecorationSettings.Density * 100);
            if (densityLabel && shownDensity != densityPercent)
            {
                shownDensity = densityPercent;
                densityLabel.text = $"KOLICINA PROPSA: {shownDensity}%";
            }
            if (densitySlider && !Mathf.Approximately(densitySlider.value, CityDecorationSettings.Density))
                densitySlider.SetValueWithoutNotify(CityDecorationSettings.Density);
        }
        void OnDestroy() { instances.Remove(this); }
        void OnApplicationQuit() => PlayerPrefs.Save();
        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position = default)
        {
            var t = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            t.SetParent(parent, false); t.anchorMin = t.anchorMax = t.pivot = new Vector2(0, 1);
            t.sizeDelta = size; t.anchoredPosition = position; return t;
        }
        static TMP_Text Label(Transform parent, string text, float font, Vector2 position, Vector2 size)
        {
            var label = Rect("Label", parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = font; label.color = new Color(.92f, .93f, .88f);
            label.raycastTarget = false; return label;
        }
        static Slider MakeSlider(Transform parent, Vector2 position)
        {
            var rect = Rect("Slider", parent, new Vector2(354, 24), position);
            rect.gameObject.AddComponent<Image>().color = new Color(.14f, .17f, .18f);
            var slider = rect.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            var fill = Rect("Fill", rect, Vector2.zero); fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = new Vector2(0, 7); fill.offsetMax = new Vector2(0, -7);
            fill.gameObject.AddComponent<Image>().color = new Color(.62f, .67f, .39f); slider.fillRect = fill;
            var handle = Rect("Handle", rect, new Vector2(16, 30));
            handle.anchorMin = handle.anchorMax = handle.pivot = new Vector2(.5f, .5f);
            var target = handle.gameObject.AddComponent<Image>(); target.color = new Color(.9f, .86f, .67f);
            slider.handleRect = handle; slider.targetGraphic = target; return slider;
        }
    }
}
