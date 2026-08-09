using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The player's readouts: wanted stars top-right (lit per level, blinking while the
    /// police have eyes on him, faded while the heat drains), a health bar bottom-left, a
    /// surrender hint that appears exactly when surrendering is the live question, the
    /// ARRESTED / WASTED cards, and an F1 debug panel for tuning.
    ///
    /// Built at runtime on its own canvas like every other HUD here (CityOverlayHud's
    /// construction, traps and all: no GraphicRaycaster, active-then-hide for TMP, null-
    /// sprite Images for every shape). Stars are the marker trick - a sprite-less Image
    /// stood on its corner - because the TMP default font has no star glyph to trust.
    /// </summary>
    public sealed class WantedHud : MonoBehaviour
    {
        public static WantedHud Instance { get; private set; }

        const int SortingOrder = 95;
        const float StarSize = 22f;
        const float StarGap = 30f;

        static readonly Color StarLit = new Color(1f, 0.78f, 0.18f);
        static readonly Color StarDim = new Color(1f, 1f, 1f, 0.13f);

        Canvas canvas;
        readonly Image[] stars = new Image[5];
        Image healthBack;
        Image healthFill;
        TMP_Text surrenderHint;
        TMP_Text debugText;
        GameObject debugPanel;
        GameObject endingCard;
        Image endingShade;
        TMP_Text endingLabel;

        PlayerMafioso player;
        PoliceResponseDirector director;
        bool tmpReady;
        float nextDebugAt;
        readonly StringBuilder builder = new StringBuilder(256);

        void Awake()
        {
            if (Instance && Instance != this)
                return;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        void Start()
        {
            player = FindAnyObjectByType<PlayerMafioso>();
            director = FindAnyObjectByType<PoliceResponseDirector>();

            BuildCanvas();
            BuildStars();
            BuildHealthBar();

            tmpReady = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
            if (tmpReady)
            {
                BuildSurrenderHint();
                BuildDebugPanel();
                BuildEndingCard();
            }
        }

        void BuildCanvas()
        {
            var go = new GameObject("Wanted HUD", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
        }

        void BuildStars()
        {
            for (var i = 0; i < stars.Length; i++)
            {
                var go = new GameObject($"Star {i + 1}", typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                // Under the clock bar (its strip is ~34px tall), right-aligned inward.
                rect.anchoredPosition = new Vector2(-16f - (stars.Length - 1 - i) * StarGap, -48f);
                rect.sizeDelta = new Vector2(StarSize, StarSize);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var image = go.AddComponent<Image>();
                image.sprite = null;
                image.color = StarDim;
                image.raycastTarget = false;
                stars[i] = image;
            }
        }

        void BuildHealthBar()
        {
            var back = new GameObject("Health", typeof(RectTransform));
            back.transform.SetParent(canvas.transform, false);

            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
            backRect.pivot = new Vector2(0f, 0f);
            backRect.anchoredPosition = new Vector2(16f, 16f);
            backRect.sizeDelta = new Vector2(220f, 14f);

            healthBack = back.AddComponent<Image>();
            healthBack.sprite = null;
            healthBack.color = new Color(0f, 0f, 0f, 0.6f);
            healthBack.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(back.transform, false);

            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(2f, 0f);
            fillRect.sizeDelta = new Vector2(216f, -4f);

            healthFill = fill.AddComponent<Image>();
            healthFill.sprite = null;
            healthFill.color = new Color(0.75f, 0.15f, 0.12f);
            healthFill.raycastTarget = false;
        }

        void BuildSurrenderHint()
        {
            surrenderHint = BuildText("Surrender Hint", canvas.transform);
            var rect = surrenderHint.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 60f);
            rect.sizeDelta = new Vector2(600f, 30f);

            surrenderHint.fontSize = 20f;
            surrenderHint.alignment = TextAlignmentOptions.Center;
            surrenderHint.color = new Color(1f, 0.85f, 0.4f);
            surrenderHint.text = "\"HANDS UP!\"  -  press H to surrender";
            surrenderHint.gameObject.SetActive(false);
        }

        void BuildDebugPanel()
        {
            debugPanel = new GameObject("Debug", typeof(RectTransform));
            debugPanel.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)debugPanel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -48f);
            rect.sizeDelta = new Vector2(430f, 320f);

            var back = debugPanel.AddComponent<Image>();
            back.sprite = null;
            back.color = new Color(0f, 0f, 0f, 0.66f);
            back.raycastTarget = false;

            debugText = BuildText("Text", debugPanel.transform);
            var textRect = debugText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            debugText.fontSize = 13f;
            debugText.alignment = TextAlignmentOptions.TopLeft;
            debugText.color = new Color(0.9f, 0.95f, 1f);

            debugPanel.SetActive(false);
        }

        void BuildEndingCard()
        {
            endingCard = new GameObject("Ending", typeof(RectTransform));
            endingCard.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)endingCard.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            endingShade = endingCard.AddComponent<Image>();
            endingShade.sprite = null;
            endingShade.color = new Color(0f, 0f, 0f, 0f);
            endingShade.raycastTarget = false;

            endingLabel = BuildText("Label", endingCard.transform);
            var labelRect = endingLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(900f, 140f);

            endingLabel.fontSize = 84f;
            endingLabel.fontStyle = FontStyles.Bold;
            endingLabel.alignment = TextAlignmentOptions.Center;

            endingCard.SetActive(false);
        }

        static TMP_Text BuildText(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------- updates

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && debugPanel)
                debugPanel.SetActive(!debugPanel.activeSelf);

            UpdateStars();
            UpdateHealth();
            UpdateSurrenderHint();
            UpdateDebug();
        }

        void UpdateStars()
        {
            var wanted = WantedSystem.Instance;
            var level = wanted ? wanted.WantedLevel : 0;

            // Blink while they can SEE him; half-lit while the heat is draining.
            var lit = StarLit;
            if (wanted && wanted.HasContact)
                lit.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
            else if (wanted && wanted.IsDecaying)
                lit.a = 0.45f;

            for (var i = 0; i < stars.Length; i++)
            {
                var colour = i < level ? lit : StarDim;
                if (stars[i].color != colour)
                    stars[i].color = colour;
            }
        }

        void UpdateHealth()
        {
            if (!player || !healthFill)
                return;

            var fraction = player.MaxHealth > 0f ? player.Health / player.MaxHealth : 0f;
            var width = 216f * Mathf.Clamp01(fraction);
            var size = healthFill.rectTransform.sizeDelta;
            if (!Mathf.Approximately(size.x, width))
                healthFill.rectTransform.sizeDelta = new Vector2(width, size.y);
        }

        void UpdateSurrenderHint()
        {
            if (!surrenderHint || !director)
                return;

            // Shown exactly while some officer is holding the warning window open.
            var engaged = false;
            var officers = director.ActiveOfficers;
            for (var i = 0; i < officers.Count; i++)
                if (officers[i] &&
                    officers[i].CurrentState == PoliceResponseOfficer.State.Engaging)
                {
                    engaged = true;
                    break;
                }

            if (player && (player.IsSurrendering || player.IsDead))
                engaged = false;

            if (surrenderHint.gameObject.activeSelf != engaged)
                surrenderHint.gameObject.SetActive(engaged);
        }

        void UpdateDebug()
        {
            if (!debugPanel || !debugPanel.activeSelf || !debugText)
                return;
            if (Time.unscaledTime < nextDebugAt)
                return;
            nextDebugAt = Time.unscaledTime + 0.25f;

            var wanted = WantedSystem.Instance;

            builder.Clear();
            builder.AppendLine("WANTED DEBUG (F1)");
            if (wanted)
            {
                builder.Append("heat: ").Append(wanted.Heat.ToString("0.0"))
                       .Append("   level: ").Append(wanted.WantedLevel).AppendLine();
                builder.Append("contact: ").Append(wanted.HasContact)
                       .Append("   decaying: ").Append(wanted.IsDecaying).AppendLine();
                builder.Append("last crime witnesses: ")
                       .Append(wanted.LastCrimeWitnesses).AppendLine();
                builder.Append("last known: ")
                       .Append(wanted.LastKnownPosition.ToString("F1")).AppendLine();
            }
            else
            {
                builder.AppendLine("no WantedSystem");
            }

            if (player)
                builder.Append("player: ").Append(player.CurrentState)
                       .Append("  hp ").Append(player.Health.ToString("0")).AppendLine();

            if (director)
            {
                var officers = director.ActiveOfficers;
                builder.Append("officers: ").Append(officers.Count).AppendLine();
                for (var i = 0; i < officers.Count; i++)
                    if (officers[i])
                        builder.Append("  #").Append(officers[i].UnitNumber)
                               .Append("  ").Append(officers[i].CurrentState).AppendLine();
            }

            debugText.text = builder.ToString();
        }

        // -------------------------------------------------------------------- endings

        public void ShowEnding(bool arrested)
        {
            if (!endingCard)
                return;

            endingCard.SetActive(true);
            endingShade.color = new Color(0f, 0f, 0f, 0.75f);
            endingLabel.text = arrested ? "ARRESTED" : "WASTED";
            endingLabel.color = arrested
                ? new Color(0.85f, 0.9f, 1f)
                : new Color(0.8f, 0.1f, 0.1f);
        }

        public void HideEnding()
        {
            if (endingCard)
                endingCard.SetActive(false);
        }
    }
}
