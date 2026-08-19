using UnityEditor;
using UnityEngine;

namespace HarborDemo.EditorTools
{
    /// <summary>
    /// Bakes the harbor's ships, boxes and industrial sheds for you when you press
    /// Play on the harbor demo, so the quay never comes up empty because a menu
    /// command was not run.
    /// An editor hook because HarborDemoBuilder lives in the player assembly and
    /// cannot call the kit-bash; before Play rather than during it because the bake
    /// writes assets. Only stale or missing stock is baked (HarborShipKitBash.Version),
    /// so after the first time Play is immediate. The first load of a session with no
    /// fleet on disk bakes it too, and writes the kit probe alongside.
    /// </summary>
    [InitializeOnLoad]
    public static class HarborDemoAutoBake
    {
        const string TriedKey = "HarborDemo.AutoBake.Tried";

        static HarborDemoAutoBake()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (!SessionState.GetBool(TriedKey, false) &&
                (!HarborShipKitBash.IsFresh() || !LivingCity.EditorTools.SyntyWarehouseKit.IsFresh()))
            {
                SessionState.SetBool(TriedKey, true);
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        LivingCity.EditorTools.SyntyWarehouseKit.BuildIfStale();
                        HarborShipKitBash.BuildIfStale();
                        HarborKitProbe.Probe();
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
            // the port's own scene, or the city with a port district in it
            if (Object.FindAnyObjectByType<HarborDemoBuilder>() == null &&
                !CityWantsAPort()) return;
            LivingCity.EditorTools.SyntyWarehouseKit.BuildIfStale();
            HarborShipKitBash.BuildIfStale();
        }
    
        /// <summary>Whether the city in this scene has rolled a port: the ships and the
        /// sheds have to be baked before Play there too, not only in the port's own scene.</summary>
        static bool CityWantsAPort()
        {
            var city = Object.FindAnyObjectByType<RoadDemo.RoadDemoBuilder>();
            if (city == null) return false;
            if (city.rollDistricts) return city.harborDistrict;
            if (city.districts == null) return false;
            foreach (var slot in city.districts)
                if (slot != null && slot.kind == RoadDemo.DistrictKind.Harbor) return true;
            return false;
        }
}
}
