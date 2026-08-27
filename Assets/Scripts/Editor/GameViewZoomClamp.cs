using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Keeps the Game view's zoom at 1x while Play mode runs.
    ///
    /// The Game view Scale slider (trivially bumped by a trackpad pinch) crops the
    /// rendered frame's edges - a top-anchored overlay canvas scrolls off the top while
    /// the world still looks normal, so "the HUD vanished" with nothing actually wrong.
    /// The city has real in-game zoom on both rigs (scroll wheel); the editor-side crop
    /// adds nothing but that trap, so during Play it is simply snapped back.
    ///
    /// Editor-window preference territory again (GameViewResolutionFix documents why
    /// the .dwlt file cannot be edited), so the only route is the live window's
    /// internal API by reflection. Every lookup no-ops with one warning if a future
    /// Unity renames it - the clamp must never become a per-frame exception machine.
    /// </summary>
    [InitializeOnLoad]
    internal static class GameViewZoomClamp
    {
        static readonly Type GameViewType =
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        // Resolved once - the clamp runs every editor tick, so a pinch never gets a
        // visible frame of zoom before the snap, and a per-tick reflection WALK (as
        // opposed to a cached invoke) would be the actual cost worth avoiding.
        static FieldInfo zoomAreaField;
        static PropertyInfo scaleProperty;
        static MethodInfo snapMethod;
        static bool resolved;
        static bool warned;

        /// <summary>A pinch waits this long at most for the snap - a few times a
        /// second, not every editor tick: the check boxes a struct out of reflection
        /// per window per call.</summary>
        const double SnapInterval = 0.25;

        /// <summary>The Game views, found once and kept: FindObjectsOfTypeAll walks
        /// every loaded object, and doing that every tick of Play was the clamp's whole
        /// cost. The list goes stale only when a view opens or closes, so it is taken
        /// again every few seconds.</summary>
        const double ScanInterval = 3.0;

        static readonly List<EditorWindow> _views = new List<EditorWindow>();
        static double _nextSnap;
        static double _nextScan;

        static GameViewZoomClamp()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || GameViewType == null)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < _nextSnap)
                return;
            _nextSnap = now + SnapInterval;

            SnapAll(logCorrections: false, rescan: now >= _nextScan);
        }

        static bool Resolve()
        {
            if (resolved)
                return snapMethod != null;

            resolved = true;
            zoomAreaField = FindMember(GameViewType,
                t => t.GetField("m_ZoomArea", DeclaredInstance));
            var zoomAreaType = zoomAreaField?.FieldType;
            scaleProperty = zoomAreaType == null
                ? null
                : FindMember(zoomAreaType, t => t.GetProperty("scale", DeclaredInstance));
            snapMethod = FindMember(GameViewType, t => t.GetMethod("SnapZoom",
                DeclaredInstance, null, new[] { typeof(float) }, null));

            if (zoomAreaField == null || scaleProperty == null || snapMethod == null)
            {
                snapMethod = null;
                WarnOnce();
            }

            return snapMethod != null;
        }

        [MenuItem("Tools/City/Reset Game View Zoom", priority = 61)]
        static void ResetGameViewZoom()
        {
            if (GameViewType == null)
            {
                Debug.LogError("[GameViewZoomClamp] UnityEditor.GameView type not found; " +
                               "drag the Game view's Scale slider back to 1x by hand.");
                return;
            }

            SnapAll(logCorrections: true, rescan: true);
        }

        static void SnapAll(bool logCorrections, bool rescan)
        {
            if (!Resolve())
                return;

            if (rescan)
                Scan();

            foreach (var window in _views)
            {
                // A view closed since the scan - the next scan drops it.
                if (!window)
                    continue;

                var zoomArea = zoomAreaField.GetValue(window);
                if (zoomArea == null || scaleProperty.GetValue(zoomArea) is not Vector2 scale)
                    continue;

                // y runs negative in the Game view's area; x carries the zoom.
                if (Mathf.Abs(scale.x) <= 1.001f)
                    continue;

                // The window's own 1x snap - it recentres the view too, which a raw
                // scale write would not.
                snapMethod.Invoke(window, new object[] { 1f });
                window.Repaint();
                if (logCorrections)
                    Debug.Log($"[GameViewZoomClamp] {window.titleContent.text}: " +
                              $"zoom {Mathf.Abs(scale.x):F2}x snapped back to 1x.", window);
            }
        }

        // FindObjectsOfTypeAll rather than GetWindow - GetWindow would open a second
        // Game view when none is docked (the GameViewResolutionFix reasoning).
        static void Scan()
        {
            _nextScan = EditorApplication.timeSinceStartup + ScanInterval;
            _views.Clear();
            foreach (var obj in Resources.FindObjectsOfTypeAll(GameViewType))
                if (obj is EditorWindow window)
                    _views.Add(window);
        }

        static void WarnOnce()
        {
            if (warned)
                return;

            warned = true;
            Debug.LogWarning("[GameViewZoomClamp] The Game view zoom API moved in this " +
                             "Unity version - the 1x clamp is off. Keep the Scale " +
                             "slider at 1x by hand or the top HUD crops away.");
        }

        const BindingFlags DeclaredInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        /// <summary>GameViewResolutionFix's base-chain walker: GameView inherits from
        /// PlayModeView, and reflection hides a base type's private members.</summary>
        static T FindMember<T>(Type type, Func<Type, T> lookup) where T : MemberInfo
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var member = lookup(current);
                if (member != null)
                    return member;
            }
            return null;
        }
    }
}
