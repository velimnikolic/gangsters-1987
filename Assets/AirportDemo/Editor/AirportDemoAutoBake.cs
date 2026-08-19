using UnityEditor;
using UnityEngine;

namespace AirportDemo.EditorTools
{
    /// <summary>
    /// Bakes the airport's hangars, terminal, tower and field furniture for you when
    /// you press Play on the airport demo, so the field never comes up as bare
    /// concrete because a menu command was not run.
    ///
    /// An editor hook because AirportDemoBuilder lives in the player assembly and
    /// cannot call the kit-bash; before Play rather than during it because the bake
    /// writes assets. Only stale or missing stock is baked (AirportKitBash.Version),
    /// so after the first time Play is immediate.
    /// </summary>
    [InitializeOnLoad]
    public static class AirportDemoAutoBake
    {
        const string TriedKey = "AirportDemo.AutoBake.Tried";

        static AirportDemoAutoBake()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (!SessionState.GetBool(TriedKey, false) && (!AirportKitBash.IsFresh() || !SimpleAirportUrp.IsFresh()))
            {
                SessionState.SetBool(TriedKey, true);
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        // the aircraft pack ships on the built-in pipeline and would be
                        // magenta in a URP project until this has run
                        SimpleAirportUrp.ConvertIfStale();
                        AirportKitBash.BuildIfStale();
                        AirportKitProbe.Probe();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                };
            }
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (Object.FindAnyObjectByType<AirportDemoBuilder>() == null) return;
            SimpleAirportUrp.ConvertIfStale();
            AirportKitBash.BuildIfStale();
        }
    }
}
