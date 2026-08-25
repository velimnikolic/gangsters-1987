using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HarborDemo.EditorTools
{
    /// <summary>
    /// The harbor's measuring stick: writes every listed prefab's renderer bounds
    /// (in its own frame) and the run vector HarborKit reads off it to
    /// Logs/harbor-kit-probe.txt, behind Tools/City/Probe Harbor Kit - so the ship
    /// kit-bash and the yard are laid out against real numbers.
    /// </summary>
    public static class HarborKitProbe
    {
        static readonly string[] Paths =
        {
            HarborKit.GenBase + "SM_Bld_Base_Wall_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Wall_Half_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Wall_Window_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Wall_Door_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_45_Wall_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Wall_Round_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Wall_Angle_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Floor_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_45_Floor_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Ceiling_01.prefab",
            HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab",
            HarborKit.GangBld + "SM_Bld_Wall_Metal_01.prefab",
            HarborKit.GangBld + "SM_Bld_Wall_Metal_Door_Slide_01.prefab",
            HarborKit.GangBld + "SM_Bld_Roof_Flat_Open_01.prefab",
            HarborKit.GangBld + "SM_Bld_Fence_Wire_01.prefab",
            HarborKit.GangBld + "SM_Bld_Fence_Gate_01.prefab",
            HarborKit.GangBld + "SM_Bld_Walkway_Single_01.prefab",
            HarborKit.GangBld + "SM_Bld_LoadingDock_02.prefab",
            HarborKit.PalmBld + "SM_Bld_Dock_Platform_01.prefab",
            HarborKit.PalmBld + "SM_Bld_Dock_Pillar_01.prefab",
            HarborKit.PalmBld + "SM_Bld_Dock_Railing_01.prefab",
            HarborKit.PalmBld + "SM_Bld_Dock_Pole_01.prefab",
            HarborKit.PalmBld + "SM_Bld_Railing_01_Straight_01.prefab",
            // the pieces the ships' fittings are scaled off
            HarborKit.GangProps + "SM_Prop_Ladder_01.prefab",
            HarborKit.GangProps + "SM_Prop_Wirespool_01.prefab",
            HarborKit.GangProps + "SM_Prop_AirVent_01.prefab",
            HarborKit.CityProps + "SM_Prop_SatDish_01.prefab",
            HarborKit.PalmVeh + "SM_Veh_RIB_Boat_01.prefab",
            HarborKit.PalmProps + "SM_Prop_Bollard_01.prefab",
            HarborKit.PalmProps + "SM_Prop_Bollard_03.prefab",
            HarborKit.PalmProps + "SM_Prop_Buoy_01.prefab",
            HarborKit.PalmProps + "SM_Prop_Buoy_02.prefab",
            HarborKit.PalmProps + "SM_Prop_Pier_Lamp_01.prefab",
            HarborKit.PalmProps + "SM_Prop_Barrel_Burn_01.prefab",
            HarborKit.PalmProps + "SM_Prop_Cone_01.prefab",
            HarborKit.CityEnv + "SM_Env_WaterEdge_Straight_03.prefab",
            HarborKit.CityEnv + "SM_Env_WaterEdge_Rock_01.prefab",
            HarborKit.GangProps + "SM_Prop_Light_Pole_01.prefab",
            HarborKit.GangProps + "SM_Prop_Dumpster_01.prefab",
            HarborKit.GangProps + "SM_Prop_Pallet_01.prefab",
            HarborKit.GangProps + "SM_Prop_Barrel_Metal_01.prefab",
            HarborKit.GangProps + "SM_Prop_Barrel_Plastic_01.prefab",
            HarborKit.GangVeh + "SM_Veh_Forklift_01.prefab",
            HarborKit.GangVeh + "SM_Veh_Truck_01.prefab",
            HarborKit.GenProps + "SM_Gen_Prop_Chain_Anchor_01.prefab",
            HarborKit.GenProps + "SM_Gen_Prop_Rope_01.prefab",
            HarborKit.GenProps + "SM_Gen_Prop_Crate_01.prefab",
            HarborKit.GenProps + "SM_Gen_Prop_Crate_02.prefab",
            HarborKit.KitTiles + "tile-plain_concrete.prefab",
            HarborKit.KitTiles + "tile-plain_asphalt.prefab",
            HarborKit.WarehouseLarge,
            HarborKit.WarehouseSmall,
            HarborKit.DepotGarage,
            HarborKit.YardShed,
            HarborKit.FencePanel,
            HarborKit.FenceWire,
            HarborKit.BarbedWire,
            HarborKit.DangerSign,
            HarborKit.ConcreteBlock,
            HarborKit.YardBin,
            // the waterline, the works, the berth character - everything the detail
            // pass added, so its placements can be read against real numbers
            HarborKit.Chain,
            HarborKit.ChainAnchor,
            HarborKit.Rope1,
            HarborKit.RopeKnot,
            HarborKit.RescueBuoy,
            HarborKit.Ladder,
            HarborKit.Puddles[0],
            HarborKit.DirtPatches[0],
            HarborKit.GasPump,
            HarborKit.Antenna,
            HarborKit.SatDish,
            HarborKit.Flagpole,
            HarborKit.HireSign,
            HarborKit.SecurityCamera,
            HarborKit.ParkingDivider,
            HarborKit.TackleBox,
            HarborKit.FishingRod,
            HarborKit.Bucket,
            HarborKit.CrabSign,
            HarborKit.DrinksCooler,
            HarborKit.Keg,
            HarborKit.Wheelbarrow,
            HarborKit.Workbench,
            HarborKit.SmallBoats[0],
            HarborKit.WaterPlane,
            HarborKit.Fx + "FX_WaterRipple_01.prefab",
            HarborKit.Fx + "FX_Smoke_Black_Small_01.prefab",
            HarborKit.Ships + "ship-cargo-A.prefab",
            HarborKit.Ships + "ship-cargo-B.prefab",
            HarborKit.Ships + "ship-coaster.prefab",
            HarborKit.Ships + "container-20-red.prefab",
        };

        [MenuItem("Tools/City/Probe Harbor Kit")]
        static void ProbeMenu() => Probe();

        public static void Probe()
        {
            var file = Path.Combine(Application.dataPath, "..", "Logs", "harbor-kit-probe.txt");
            var sb = new StringBuilder();
            foreach (var path in Paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine(Path.GetFileNameWithoutExtension(path) + " MISSING"); continue; }
                var b = HarborKit.PrefabBounds(prefab);
                var run = HarborKit.RunOf(prefab, out bool centred);
                sb.AppendLine($"{prefab.name}  min({b.min.x:F2},{b.min.y:F2},{b.min.z:F2}) max({b.max.x:F2},{b.max.y:F2},{b.max.z:F2}) " +
                              $"size({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})  run({run.x:F2},{run.z:F2}){(centred ? " centred" : "")}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[HarborKitProbe] wrote " + file);
        }
    }
}
