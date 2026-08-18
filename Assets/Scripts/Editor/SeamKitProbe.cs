using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// One-off measuring stick for the river/bridge/park kit: writes every listed
    /// prefab's renderer bounds (relative to its pivot) to Logs/seam-kit-probe.txt
    /// on load and behind Tools/City/Probe Seam Kit, so the seam builder can be
    /// laid out against real numbers rather than guessed ones.
    /// </summary>
    public static class SeamKitProbe
    {
        static readonly string[] Paths =
        {
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Straight_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Straight_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Straight_03.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Corner_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Corner_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Pipe_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_WaterEdge_Rock_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Ocean_Tile_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Bridge_Edge_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Bridge_Pillar_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Bridge_Support_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Bridge_Underside_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Bridge_Wall_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Sidewalk_Straight_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Road_Bare_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_GrassPath_Straight_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_GrassPath_Corner_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_GrassPath_T_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_GrassPath_Junction_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Grass_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_03.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Fence_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_ParkBench_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Road_Highway_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Road_Highway_Corner_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Road_Highway_Pillar_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Road_Highway_Ramp_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Road_Highway_Ramp_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Drawbridge_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Drawbridge_Base_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Fountain_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Pier_Lamp_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Pier_Bench_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Bollard_Chain_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Bollard_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Power_Boat_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sailboat_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Party_Boat_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_RIB_Boat_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Platform_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Pillar_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Railing_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Court_BasketBall_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Court_Tennis_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Path_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Path_Corner_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Pavilion_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Shelter_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Tree_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Tree_02.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Tree_Pine_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Water_Plane_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Props/SM_Prop_Railing_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_03.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Large_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Large_04.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Round_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Simple_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Skinny_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Skinny_05.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Small_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Building_Detail_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Crane_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Environment/SM_Env_Background_Hills_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Skyline_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_MountainRange_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Background_Mountain_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Hill_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Mountain_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Platform_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Platform_End_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Dock_Pole_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Lighthouse_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Wall_Retaining_01.prefab",
            "Assets/CityKit/Catalog/Wharf_A.prefab",
            "Assets/CityKit/Catalog/Wharf_B.prefab",
            "Assets/CityKit/Catalog/Marina.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/SM_Bld_LoadingDock_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/SM_Bld_LoadingDock_02.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Props/SM_Prop_Container_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Props/SM_Prop_Container_02.prefab",
        };

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            // only while the probe file is missing - it is a one-off, not a log spammer
            var file = Path.Combine(Application.dataPath, "..", "Logs", "seam-kit-probe.txt");
            if (File.Exists(file)) return;
            EditorApplication.delayCall += () => Probe(file);
        }

        [MenuItem("Tools/City/Probe Seam Kit")]
        static void ProbeMenu()
        {
            var file = Path.Combine(Application.dataPath, "..", "Logs", "seam-kit-probe.txt");
            Probe(file);
            Debug.Log("[SeamKitProbe] wrote " + file);
        }

        static void Probe(string file)
        {
            var sb = new StringBuilder();
            foreach (var path in Paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine(Path.GetFileNameWithoutExtension(path) + " MISSING"); continue; }
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) { sb.AppendLine(prefab.name + " no renderers"); continue; }
                // bounds in the prefab's own frame: pivot at origin, no rotation
                var b = new Bounds();
                bool started = false;
                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    var local = mesh.bounds;
                    var m = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var c = local.center; var e = local.extents;
                    for (int k = 0; k < 8; k++)
                    {
                        var corner = c + new Vector3((k & 1) == 0 ? e.x : -e.x, (k & 2) == 0 ? e.y : -e.y, (k & 4) == 0 ? e.z : -e.z);
                        var p = m.MultiplyPoint3x4(corner);
                        if (!started) { b = new Bounds(p, Vector3.zero); started = true; } else b.Encapsulate(p);
                    }
                }
                sb.AppendLine($"{prefab.name}  min({b.min.x:F2},{b.min.y:F2},{b.min.z:F2}) max({b.max.x:F2},{b.max.y:F2},{b.max.z:F2}) size({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
        }
    }
}
