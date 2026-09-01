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
    /// so after the first time Play is immediate.
    ///
    /// Entering Play is the only thing that bakes here, and deliberately so. This used
    /// to bake on the first domain reload of a session as well, which meant every
    /// script edit in a stale project opened an 8 MB Synty demo scene - a scene that
    /// logs hundreds of missing-prefab errors on the way in - and rewrote four mesh
    /// assets, while the user was waiting on a recompile and had asked for none of it.
    /// Nothing needs the stock before Play: HarborDemoBuilder raises the quay from
    /// Awake, so an idle editor never reads the sheds. What a clone needs at once,
    /// Tools/City/Catalog/Rebuild Synty Warehouse Kit and Tools/City/Probe Harbor Kit
    /// still give on demand.
    /// </summary>
    [InitializeOnLoad]
    public static class HarborDemoAutoBake
    {
        static HarborDemoAutoBake()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
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
