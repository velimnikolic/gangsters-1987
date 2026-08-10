using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The right-click menu: a runtime-built canvas whose buttons come from
    /// ContextActionRegistry at each open, so a new action appears the moment it registers.
    /// Pure view - it never reads input; the InteractionController owns open, close and the
    /// click that falls outside.
    ///
    /// This canvas carries the project's ONLY GraphicRaycaster, and its Start creates the
    /// scene's ONLY EventSystem. That is a deliberate break with the no-clickable-UI rule
    /// the other HUDs follow: a menu is the first UI that must be clicked. The price is
    /// paid where the other HUDs pick - they now guard on IsPointerOverGameObject - and
    /// everything on THIS canvas that is not a button keeps raycastTarget false, so the
    /// menu never swallows clicks beyond its own rows.
    ///
    /// Construction follows CityOverlayHud to the letter: same scaler, panel built ACTIVE
    /// then hidden (a TextMeshProUGUI only loads its font in OnEnable, which never runs
    /// under an inactive parent), TMP_Settings guarded, sizes in reference pixels
    /// converted through canvas.scaleFactor when clamping.
    /// </summary>
    public sealed class ContextMenuUI : MonoBehaviour
    {
        const float RowWidth = 180f;
        const float RowHeight = 30f;
        const float Padding = 4f;

        /// <summary>Above the clock bar's 100 - a menu outranks every readout.</summary>
        const int SortingOrder = 120;

        Canvas canvas;
        RectTransform panel;
        bool tmpReady;

        PlayerMafioso actor;
        IContextTarget target;

        readonly List<IContextAction> available = new List<IContextAction>();

        public bool IsOpen => panel && panel.gameObject.activeSelf;

        void Start()
        {
            EnsureEventSystem();
            BuildCanvas();
            BuildPanel();
        }

        /// <summary>
        /// The scene has never needed an EventSystem before this menu, so the menu brings
        /// its own. InputSystemUIInputModule, not StandaloneInputModule - the legacy module
        /// throws under this project's Input System setting.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current)
                return;

            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        void BuildCanvas()
        {
            var go = new GameObject("Context Menu", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            go.AddComponent<GraphicRaycaster>();
        }

        void BuildPanel()
        {
            tmpReady = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
            if (!tmpReady)
            {
                Debug.LogWarning("[ContextMenu] No TMP default font - the context menu is " +
                                 "disabled until TMP essentials are imported " +
                                 "(Tools/City/Import TMP Essentials).", this);
                return;
            }

            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);

            panel = (RectTransform)go.transform;
            panel.pivot = new Vector2(0f, 1f);
            panel.anchorMin = panel.anchorMax = Vector2.zero;

            var background = go.AddComponent<Image>();
            background.sprite = null;
            background.color = new Color(0.06f, 0.06f, 0.07f, 0.94f);
            background.raycastTarget = false;

            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Builds and shows the menu for this pair at the pointer. Rebuilt per open - a
        /// menu opens a few times a minute, and rebuilding is what makes the registry the
        /// single source of truth.
        /// </summary>
        public void Open(PlayerMafioso menuActor, IContextTarget menuTarget, Vector2 screenPosition)
        {
            if (!tmpReady || !panel)
                return;

            actor = menuActor;
            target = menuTarget;

            // Active BEFORE the buttons are created - see the class comment's TMP trap.
            panel.gameObject.SetActive(true);

            foreach (Transform old in panel)
                Destroy(old.gameObject);

            ContextActionRegistry.GetAvailable(actor, target, available);

            panel.sizeDelta = new Vector2(
                RowWidth + Padding * 2f,
                available.Count * RowHeight + Padding * 2f);

            for (var i = 0; i < available.Count; i++)
                BuildRow(available[i], i);

            Place(screenPosition);
        }

        public void Close()
        {
            if (panel)
                panel.gameObject.SetActive(false);

            actor = null;
            target = null;
        }

        void BuildRow(IContextAction action, int index)
        {
            var go = new GameObject(action.Label, typeof(RectTransform));
            go.transform.SetParent(panel, false);

            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Padding, -Padding - index * RowHeight);
            rect.sizeDelta = new Vector2(RowWidth, RowHeight);

            // The one Graphic in the project with raycastTarget TRUE - it is the button.
            var background = go.AddComponent<Image>();
            background.sprite = null;
            background.color = new Color(0.13f, 0.13f, 0.15f, 0.95f);
            background.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            var colours = button.colors;
            colours.normalColor = new Color(0.85f, 0.85f, 0.85f);
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            button.colors = colours;

            var captured = action;
            // Close BEFORE execute: the action may open UI of its own, and a menu that
            // outlives its own click is how stale-target bugs start.
            button.onClick.AddListener(() =>
            {
                var a = actor;
                var t = target;
                Close();
                captured.Execute(a, t);
            });

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-10f, 0f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = captured.Label;
            label.fontSize = 15f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = new Color(0.94f, 0.94f, 0.94f);
            label.raycastTarget = false;
        }

        /// <summary>
        /// Top-left corner at the pointer, clamped fully on screen. Reference-pixel sizes
        /// reach the screen multiplied by the scaler's factor.
        /// </summary>
        void Place(Vector2 screenPosition)
        {
            var scale = canvas.scaleFactor;
            var width = panel.sizeDelta.x * scale;
            var height = panel.sizeDelta.y * scale;

            panel.position = new Vector3(
                Mathf.Clamp(screenPosition.x, 0f, Screen.width - width),
                Mathf.Clamp(screenPosition.y, height, Screen.height),
                0f);
        }
    }
}
