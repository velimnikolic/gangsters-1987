using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Turns off the Game view's "Low Resolution Aspect Ratios" toggle.
    ///
    /// On a Retina display that toggle makes the Game view render at logical point size and
    /// upscale 2x to the physical panel - a quarter of the pixels it displays. The city reads as
    /// low resolution in Play mode while the Scene view stays sharp, which is what gives the bug
    /// its confusing shape. It hurts most at wide zoom: at orthographicSize 70 one rendered pixel
    /// covers 0.18 m of world, so kerbs and the generated parking-line quads sit near a pixel.
    ///
    /// This is an editor window preference, not a project setting. It lives in
    /// UserSettings/Layouts/*.dwlt, which Unity rewrites from memory on quit - editing that file
    /// does nothing. The only way to change it is through the live window, and the API for it is
    /// internal, hence the reflection below.
    /// </summary>
    internal static class GameViewResolutionFix
    {
        const string LowResProperty = "lowResolutionForAspectRatios";
        const string LowResField = "m_LowResolutionForAspectRatios";

        [MenuItem("Tools/City/Fix Game View Resolution", priority = 60)]
        static void FixGameViewResolution()
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                Debug.LogError("[GameViewResolutionFix] UnityEditor.GameView type not found; " +
                               "uncheck 'Low Resolution Aspect Ratios' in the Game view aspect dropdown by hand.");
                return;
            }

            // FindObjectsOfTypeAll rather than GetWindow: GetWindow would open a second Game view
            // when none is docked, and the new one would carry the same bad default anyway.
            var windows = Resources.FindObjectsOfTypeAll(gameViewType);
            if (windows.Length == 0)
            {
                Debug.LogWarning("[GameViewResolutionFix] No Game view is open. Open one (Window > General > Game) and run this again.");
                return;
            }

            var fixedCount = 0;
            foreach (var obj in windows)
            {
                if (obj is not EditorWindow window) continue;
                if (TurnOffLowResolution(window, gameViewType)) fixedCount++;
                window.Repaint();
                Debug.Log($"[GameViewResolutionFix] {window.titleContent.text}: render target is now {DescribeTargetSize(window, gameViewType)}.", window);
            }

            if (fixedCount == 0)
                Debug.LogWarning("[GameViewResolutionFix] Could not reach the low-resolution flag on any Game view. " +
                                 "Uncheck it manually in the Game view aspect-ratio dropdown.");
        }

        /// <summary>
        /// Clears the flag, preferring the property because its setter also recomputes the render
        /// target size. The serialized fallback exists only because this is an internal API and a
        /// rename in a future Unity would otherwise silently turn this whole tool into a no-op.
        /// </summary>
        static bool TurnOffLowResolution(EditorWindow window, Type gameViewType)
        {
            var property = FindMember(gameViewType, t => t.GetProperty(LowResProperty, DeclaredInstance));
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
            {
                property.SetValue(window, false);
                return true;
            }

            // The backing field is a bool[] indexed by size group, so every entry has to be
            // cleared - the active index is whichever group the window is currently showing.
            var serialized = new SerializedObject(window);
            var array = serialized.FindProperty(LowResField);
            if (array == null || !array.isArray) return false;

            for (var i = 0; i < array.arraySize; i++)
                array.GetArrayElementAtIndex(i).boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The property setter would have done this itself; the serialized path has to ask.
            FindMember(gameViewType, t => t.GetMethod("UpdateZoomAreaAndParent", DeclaredInstance))
                ?.Invoke(window, null);
            return true;
        }

        /// <summary>
        /// Reports the resulting render target so the fix can be confirmed rather than assumed.
        /// Expect it to double, e.g. 1141.5x767 becoming 2283x1534.
        /// </summary>
        static string DescribeTargetSize(EditorWindow window, Type gameViewType)
        {
            var property = FindMember(gameViewType, t => t.GetProperty("targetSize", DeclaredInstance));
            if (property != null && property.PropertyType == typeof(Vector2))
            {
                var size = (Vector2)property.GetValue(window);
                return $"{size.x}x{size.y}";
            }

            var serialized = new SerializedObject(window);
            var field = serialized.FindProperty("m_TargetSize");
            return field != null ? $"{field.vector2Value.x}x{field.vector2Value.y}" : "unknown";
        }

        const BindingFlags DeclaredInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Walks the base chain by hand. GameView inherits most of this from PlayModeView, and
        /// reflection does not surface a base type's private members through the derived type.
        /// </summary>
        static T FindMember<T>(Type type, Func<Type, T> lookup) where T : MemberInfo
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var member = lookup(current);
                if (member != null) return member;
            }
            return null;
        }
    }
}
