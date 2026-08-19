using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SuburbDemo.EditorTools
{
    /// <summary>
    /// The suburb's measuring stick: writes every Town piece the demo uses with its
    /// renderer bounds in its own frame to Logs/town-kit-probe.txt, behind
    /// Tools/City/Probe Town Kit - and for the preset houses the front the table in
    /// TownKit claims next to the front FacadeFinder measures off the mesh (the busy
    /// side), so a wrong row in the table shows up as a disagreement.
    /// </summary>
    public static class TownKitProbe
    {
        static readonly string[] Paths =
        {
            TownKit.Road, TownKit.Zebra, TownKit.RoadCap, TownKit.RoadParking, TownKit.RoadPatch, TownKit.SpeedBump,
            TownKit.Sidewalk, TownKit.SidewalkCorner, TownKit.SidewalkEndCorner, TownKit.SidewalkCapWrap, TownKit.SidewalkCrossing,
            TownKit.SidewalkDriveway, TownKit.SidewalkPath, TownKit.Driveway, TownKit.Path, TownKit.PathDoor, TownKit.Grass, TownKit.Concrete,
            TownKit.Garage, TownKit.Cottage, TownKit.Church, TownKit.ShopPad, TownKit.ShopSign, TownKit.Shed,
            TownKit.FenceWood, TownKit.FenceWoodPost, TownKit.FenceWhite, TownKit.FenceWhitePost, TownKit.FenceWhiteGate,
            TownKit.Streetlamp, TownKit.Streetlamp2, TownKit.SignPole, TownKit.StopSign, TownKit.BusStopSign, TownKit.Bench,
            TownKit.Manhole, TownKit.Drain, TownKit.GardenBox, TownKit.Barbeque, TownKit.Doghouse, TownKit.Trampoline, TownKit.Sandpit,
            TownKit.PoolInground, TownKit.PoolAbove, TownKit.PoolChild, TownKit.Swing, TownKit.Fort, TownKit.Slide, TownKit.Roundabout,
            TownKit.Fountain, TownKit.FountainBase, TownKit.Gaspump, TownKit.GaspumpBase, TownKit.GaspumpCover, TownKit.Vending,
            TownKit.SchoolBus, TownKit.DeliveryTruck, TownKit.TreeBase,
        };

        [MenuItem("Tools/City/Probe Town Kit")]
        static void Probe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("POLYGON Town kit probe - renderer bounds in the prefab's own frame (pivot at the origin)");
            sb.AppendLine("Ground tiles: 5 x 5 m, pivot on the +X/+Z corner, covering towards -X/-Z (City convention).");
            sb.AppendLine();

            sb.AppendLine("== houses: the table's front vs FacadeFinder's busy side");
            foreach (var h in TownKit.Houses) ProbeHouse(sb, h.Path, h.Front.ToString(), h.Width, h.Depth);
            ProbeHouse(sb, TownKit.Garage, "MinusZ (door)", 6.5f, 9f);
            ProbeHouse(sb, TownKit.Church, "PlusZ", 10.8f, 14.7f);
            foreach (var s in TownKit.Shops) ProbeHouse(sb, s, "PlusZ", 0f, 0f);
            sb.AppendLine();

            sb.AppendLine("== pieces");
            foreach (var path in Paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"MISSING  {path}"); continue; }
                var b = TownKit.PrefabBounds(prefab);
                sb.AppendLine($"{prefab.name,-40} min ({b.min.x,6:F2} {b.min.y,6:F2} {b.min.z,6:F2})  max ({b.max.x,6:F2} {b.max.y,6:F2} {b.max.z,6:F2})  size ({b.size.x,5:F2} {b.size.y,5:F2} {b.size.z,5:F2})");
            }
            foreach (var arr in new[] { TownKit.Hedges, TownKit.YardTrees, TownKit.TallTrees, TownKit.LetterBoxes, TownKit.Bins, TownKit.Cars, TownKit.LongVehicles })
                foreach (var path in arr)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { sb.AppendLine($"MISSING  {path}"); continue; }
                    var b = TownKit.PrefabBounds(prefab);
                    sb.AppendLine($"{prefab.name,-40} min ({b.min.x,6:F2} {b.min.y,6:F2} {b.min.z,6:F2})  max ({b.max.x,6:F2} {b.max.y,6:F2} {b.max.z,6:F2})  size ({b.size.x,5:F2} {b.size.y,5:F2} {b.size.z,5:F2})");
                }

            var dir = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "town-kit-probe.txt");
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[SuburbDemo] Town kit probe written to " + file);
        }

        static void ProbeHouse(StringBuilder sb, string path, string tableFront, float w, float d)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"MISSING  {path}"); return; }
            var b = TownKit.PrefabBounds(prefab);
            var instance = Object.Instantiate(prefab);
            string measured;
            try
            {
                var side = RoadDemo.FacadeFinder.FrontOf(instance, out var report);
                measured = side.ToString() + "  (" + report.Replace('\n', ' ') + ")";
            }
            catch (System.Exception e) { measured = "n/a (" + e.Message + ")"; }
            finally { Object.DestroyImmediate(instance); }
            sb.AppendLine($"{prefab.name,-32} table {tableFront,-14} measured {measured}");
            sb.AppendLine($"{"",-32} bounds size ({b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2}), table says {w:F1} x {d:F1}, min y {b.min.y:F2}");
        }
    }
}
