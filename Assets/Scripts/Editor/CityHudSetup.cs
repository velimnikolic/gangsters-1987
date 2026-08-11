using LivingCity.Ambient;
using LivingCity.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Builds the on-screen clock: a canvas holding a bar across the top of the screen with a
    /// day/night disc, the time and the day number, driven by CityClockHud.
    ///
    /// Built from code rather than authored, for the same reason as everything else in the
    /// scene - the setup menu is the description of what the scene is supposed to contain, and
    /// a hand-placed canvas would be the one part of it nobody could rebuild.
    /// </summary>
    public static class CityHudSetup
    {
        const string HudObjectName = "HUD";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string EssentialsPackage = "/Package Resources/TMP Essential Resources.unitypackage";

        /// <summary>The built-in soft circle from Unity's UI skin. Used for the day/night disc.</summary>
        const string KnobSpritePath = "UI/Skin/Knob.psd";

        const float BarHeight = 44f;
        const float DiscSize = 22f;

        /// <summary>
        /// Find-or-add a component, without the `??` trap.
        ///
        /// `GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()` is the idiom used everywhere else in this
        /// folder and it is quietly wrong for BUILT-IN components. A missing Canvas or Light does
        /// not come back as a C# null - it comes back as a wrapper around a null native pointer,
        /// which `??` accepts as a perfectly good object. AddComponent then never runs and the
        /// first property write throws MissingComponentException. It only looks safe because it
        /// is mostly used on MonoBehaviours, where Unity does hand back a real null.
        ///
        /// Unity's overloaded implicit bool is the check that actually reads the native pointer,
        /// so the ternary is the version that works for both.
        /// </summary>
        static T Ensure<T>(GameObject host) where T : Component
        {
            var existing = host.GetComponent<T>();
            return existing ? existing : host.AddComponent<T>();
        }

        static T Ensure<T>(Component host) where T : Component => Ensure<T>(host.gameObject);

        [MenuItem("Tools/City/Add Clock HUD", priority = 3)]
        public static void AddClockHud()
        {
            var hud = EnsureHud();
            if (!hud)
                return;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = hud;

            Debug.Log("[CitySetup] Clock HUD ready. Press Play to see it tick. " +
                      "Save the scene (Cmd+S) to keep it.", hud);
        }

        /// <summary>
        /// Creates or refreshes the HUD canvas and returns its root, or null if TextMeshPro's
        /// resources had to be imported first.
        ///
        /// Idempotent: everything is found by name and reconfigured, so running it twice - or
        /// running it after "Set Up Scene" already did - leaves one HUD, not two.
        /// </summary>
        public static GameObject EnsureHud()
        {
            if (!EnsureTextMeshProResources())
                return null;

            var found = GameObject.Find(HudObjectName);
            var hudObject = found ? found : new GameObject(HudObjectName);

            var canvas = Ensure<Canvas>(hudObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above anything a later HUD element might add without thinking about ordering.
            canvas.sortingOrder = 100;

            // No GraphicRaycaster and no EventSystem, on purpose. Nothing here is clickable, and
            // a raycaster stretched across the top edge of the screen is exactly how a HUD starts
            // quietly eating the camera's drags.
            var scaler = Ensure<CanvasScaler>(hudObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Match height, not width. A top bar should be the same thickness on 16:9 and on an
            // ultrawide; matching width would thin it out as the screen got wider.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var bar = BuildBar(hudObject.transform);
            var content = BuildContent(bar);

            var disc = BuildDisc(content);
            var timeLabel = BuildLabel("Time", content, 26f, new Color(0.96f, 0.96f, 0.96f), "08:00");
            var dayLabel = BuildLabel("Day", content, 16f, new Color(0.70f, 0.72f, 0.76f), "Day 1");

            var hud = Ensure<CityClockHud>(hudObject);

            // The clock lives on Spawners with the rest of the ambient stack. EnsureWeather
            // creates and configures one if the scene has none, so the HUD can be added to a
            // bare scene without a second menu item first.
            var clock = Object.FindAnyObjectByType<CityClock>();
            if (!clock)
            {
                CitySceneSetup.EnsureWeather();
                clock = Object.FindAnyObjectByType<CityClock>();
            }

            CityEditorUtils.SetField(hud, "clock", clock);
            CityEditorUtils.SetField(hud, "timeLabel", timeLabel);
            CityEditorUtils.SetField(hud, "dayLabel", dayLabel);
            CityEditorUtils.SetField(hud, "icon", disc);

            // The O-key block overlay rides on the same canvas. Its labels are built lazily at
            // runtime from the generated city, so there is nothing to author here beyond the
            // component and its camera.
            var overlay = Ensure<BlockOverlayHud>(hudObject);
            var camera = UnityEngine.Camera.main;
            if (!camera)
                camera = Object.FindAnyObjectByType<UnityEngine.Camera>();
            CityEditorUtils.SetField(overlay, "worldCamera", camera);

            return hudObject;
        }

        /// <summary>
        /// The bar itself: a flat dark panel pinned across the top edge, full width at any
        /// resolution. No sprite - an Image without one draws a solid rectangle, which is all
        /// this needs and one fewer asset to depend on.
        /// </summary>
        static RectTransform BuildBar(Transform parent)
        {
            var rect = Child("Bar", parent);

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, BarHeight);
            rect.anchoredPosition = Vector2.zero;

            var image = Ensure<Image>(rect);
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = false;

            return rect;
        }

        /// <summary>
        /// The centred row inside the bar. A layout group plus a size fitter so the disc, the
        /// time and the day arrange themselves - the alternative is hand-tuned widths that go
        /// wrong the moment the day number reaches two digits.
        /// </summary>
        static RectTransform BuildContent(Transform parent)
        {
            var rect = Child("Content", parent);

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            var layout = Ensure<HorizontalLayoutGroup>(rect);

            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 12f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = Ensure<ContentSizeFitter>(rect);

            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            return rect;
        }

        /// <summary>
        /// The day/night disc. CityClockHud tints it; the sprite is Unity's built-in knob, a
        /// soft circle that ships with the editor, so this adds no art to the project.
        /// </summary>
        static Image BuildDisc(Transform parent)
        {
            var rect = Child("Disc", parent);

            var image = Ensure<Image>(rect);
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(KnobSpritePath);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // An Image reports its sprite's native size to a layout group, which for the knob is
            // whatever the editor skin authored it at. Pin it instead.
            var element = Ensure<LayoutElement>(rect);
            element.preferredWidth = DiscSize;
            element.preferredHeight = DiscSize;

            return image;
        }

        static TextMeshProUGUI BuildLabel(string name, Transform parent, float size, Color colour, string placeholder)
        {
            var rect = Child(name, parent);

            var text = Ensure<TextMeshProUGUI>(rect);
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            // CityClockHud only runs in Play. Without this the bar sits empty in the Game view
            // whenever the scene is stopped, which reads as a broken HUD rather than a paused one.
            text.text = placeholder;

            return text;
        }

        /// <summary>Find-or-create a child by name, guaranteeing it carries a RectTransform.</summary>
        static RectTransform Child(string name, Transform parent)
        {
            var existing = parent.Find(name);
            var child = existing ? existing.gameObject : new GameObject(name, typeof(RectTransform));

            // Not Ensure<RectTransform> - Unity refuses to add one beside an existing plain
            // Transform. Fresh children are created carrying one, and anything already under the
            // canvas has had one since Unity parented it there, so the cast is the real path.
            var rect = child.transform as RectTransform;
            if (!rect)
                rect = child.AddComponent<RectTransform>();

            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            return rect;
        }

        [MenuItem("Tools/City/Import TMP Essentials", priority = 61)]
        public static void ImportTextMeshProEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath))
            {
                Debug.Log("[CitySetup] TextMeshPro resources are already imported.");
                return;
            }

            EnsureTextMeshProResources();
        }

        /// <summary>
        /// True when TextMeshPro's runtime resources are present. When they are not, starts the
        /// import and returns false.
        ///
        /// Two steps rather than one because the import triggers a domain reload: a
        /// TextMeshProUGUI created in the same call as the import deserialises with no font
        /// asset and draws nothing, which looks exactly like a broken HUD.
        /// </summary>
        static bool EnsureTextMeshProResources()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath))
                return true;

            var packagePath = TMPro.EditorUtilities.TMP_EditorUtility.packageFullPath + EssentialsPackage;

            Debug.Log($"[CitySetup] Importing TextMeshPro essential resources from {packagePath}.");
            AssetDatabase.ImportPackage(packagePath, false);

            EditorUtility.DisplayDialog(
                "TextMeshPro resources are being imported",
                "The clock HUD needs TextMeshPro's fonts, which this project had never imported.\n\n" +
                "They are being added to Assets/TextMesh Pro now. Once Unity finishes reloading, " +
                "run Tools/City/Add Clock HUD again.",
                "OK");

            return false;
        }
    }
}
