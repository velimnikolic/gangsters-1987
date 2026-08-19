using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AirportDemo.EditorTools
{
    /// <summary>
    /// The airport's measuring stick: writes every piece the field is laid out
    /// against - its renderer bounds in its own frame and the run vector
    /// AirportKit reads off it - to Logs/airport-kit-probe.txt, behind
    /// Tools/City/Probe Airport Kit.
    ///
    /// It also checks the fleet: that each class of aeroplane is actually in
    /// Assets/SimpleAirport, that its proportions are the shape that class ought to
    /// be (the models are scaled to span at Play, so what matters is length over
    /// span), and that the field's own clearances hold - the hangar opening against
    /// a light wing, the tie-down pitch, the gap between two jets on the stands, the
    /// runway against a trijet's span.
    /// </summary>
    public static class AirportKitProbe
    {
        static readonly string[] Paths =
        {
            AirportKit.Jets[0],
            AirportKit.SmallJets[0],
            AirportKit.Commuters[0],
            AirportKit.LightPlanes[0],
            AirportKit.Helicopters[0],
            AirportKit.HelipadRaised,
            AirportKit.HelipadFlat,
            AirportKit.MetalWall,
            AirportKit.MetalWallHalf,
            AirportKit.MetalDoorBig,
            AirportKit.MetalDoor,
            AirportKit.MetalCorner,
            AirportKit.Roof,
            AirportKit.RoofTruss,
            AirportKit.LoadingDock,
            AirportKit.BaseWall,
            AirportKit.BaseWindow,
            AirportKit.BaseDoor,
            AirportKit.BasePillar,
            AirportKit.BaseFloor,
            AirportKit.GlassWall,
            AirportKit.GlassDoor,
            AirportKit.PlazaPillar,
            AirportKit.AwningCover,
            AirportKit.AwningPole,
            AirportKit.TowerCab,
            AirportKit.TowerShaft,
            AirportKit.FencePanel,
            AirportKit.FencePillar,
            AirportKit.FenceGate,
            AirportKit.BarbedWire,
            AirportKit.BoomGate,
            AirportKit.Bollard,
            AirportKit.Cone,
            AirportKit.MetalDetector,
            AirportKit.Seating,
            AirportKit.BeltBarrier,
            AirportKit.Reception,
            AirportKit.FlagStand,
            AirportKit.Antenna,
            AirportKit.StreetLamp,
            AirportKit.GasPump,
            AirportKit.Forklift,
            AirportKit.GolfCart,
            AirportKit.Trailer,
            AirportKit.TownTruck,
            AirportKit.GangTruck,
            AirportKit.Pickup,
            AirportKit.Bus,
            AirportKit.FireTruck,
            AirportKit.Taxi,
            AirportKit.BusStop,
            AirportKit.TaxiStand,
            // what the kit bakes - missing until the bake has run once
            AirportKit.BoxHangar,
            AirportKit.BoxHangarOpen,
            AirportKit.MaintHangar,
            AirportKit.Fbo,
            AirportKit.Terminal,
            AirportKit.Tower,
            AirportKit.Arff,
            AirportKit.CargoShed,
            AirportKit.FuelFarm,
            AirportKit.GuardBooth,
            AirportKit.Windsock,
            AirportKit.Papi,
            AirportKit.TaxiSign,
            AirportKit.HoldSign,
            AirportKit.ApronMast,
            AirportKit.AirStairs,
            AirportKit.BaggageCart,
            AirportKit.FuelBowser,
            AirportKit.Chock,
        };

        [MenuItem("Tools/City/Probe Airport Kit")]
        static void ProbeMenu() => Probe();

        public static void Probe()
        {
            var file = Path.Combine(Application.dataPath, "..", "Logs", "airport-kit-probe.txt");
            var sb = new StringBuilder();
            sb.AppendLine("# airport kit - bounds in the piece's own frame, metres");
            sb.AppendLine();
            foreach (var path in Paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine(Path.GetFileNameWithoutExtension(path) + "  MISSING"); continue; }
                var b = AirportKit.PrefabBounds(prefab);
                var run = AirportKit.RunOf(prefab, out bool centred);
                sb.AppendLine($"{prefab.name}  min({b.min.x:F2},{b.min.y:F2},{b.min.z:F2}) max({b.max.x:F2},{b.max.y:F2},{b.max.z:F2}) " +
                              $"size({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})  run({run.x:F2},{run.z:F2}){(centred ? " centred" : "")}");
            }

            sb.AppendLine();
            sb.AppendLine("# the checks the field's geometry depends on");
            foreach (var line in Checks()) sb.AppendLine(line);

            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[AirportKitProbe] wrote " + file);
        }

        /// <summary>The handful of assertions worth failing loudly: that the fleet is
        /// there at all, what proportions each class has (the models are scaled to
        /// span at Play, so what matters is that the shape is right), and whether the
        /// field's own clearances hold for the biggest thing that uses them.</summary>
        static List<string> Checks()
        {
            var lines = new List<string>();

            void Fleet(string what, string[] paths, float span, float length)
            {
                var prefab = paths.Length > 0 ? AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]) : null;
                if (prefab == null) { lines.Add($"FAIL  no {what} in Assets/SimpleAirport"); return; }
                var b = AirportKit.PrefabBounds(prefab);
                if (b.size.x < 1e-4f) { lines.Add($"FAIL  {what} has no geometry"); return; }
                // the model is scaled to span at Play, so the check is on its shape:
                // length over span, which is what tells a trijet from a Cessna
                float want = length / span, got = b.size.z / b.size.x;
                lines.Add($"{(Mathf.Abs(got - want) < 0.35f ? "ok  " : "FAIL")}  {what} proportions: length/span {got:F2} " +
                          $"(a real one is {want:F2}) - scaled to {span:F0} m of span at Play");
            }

            Fleet("trijet", AirportKit.Jets, AirportSpec.JetSpan, AirportSpec.JetLength);
            Fleet("twinjet", AirportKit.SmallJets, AirportSpec.JetSpan, AirportSpec.JetLength);
            Fleet("commuter", AirportKit.Commuters, AirportSpec.CommuterSpan, AirportSpec.CommuterLength);
            Fleet("light aeroplane", AirportKit.LightPlanes, AirportSpec.GaSpan, AirportSpec.GaLength);
            Fleet("helicopter", AirportKit.Helicopters, AirportSpec.HeliRotor, AirportSpec.HeliLength);

            float doorClear = AirportSpec.HangarDoorWidth - AirportSpec.GaSpan;
            lines.Add($"{(doorClear > 1f ? "ok  " : "FAIL")}  hangar opening clears a light wing by {doorClear * 0.5f:F2} m each side");
            float hangarDepth = AirportSpec.HangarDepth - AirportSpec.GaLength;
            lines.Add($"{(hangarDepth > 3f ? "ok  " : "FAIL")}  hangar is {hangarDepth:F2} m longer than a light aeroplane");
            float hangarHead = AirportSpec.HangarHeight - AirportSpec.GaHeight;
            lines.Add($"{(hangarHead > 1f ? "ok  " : "FAIL")}  hangar eaves clear the fin by {hangarHead:F2} m");
            float tie = AirportSpec.TieDownPitch - AirportSpec.GaSpan;
            lines.Add($"{(tie > 3f ? "ok  " : "FAIL")}  tie-down pitch leaves {tie:F2} m between wingtips");
            float standGap = Mathf.Abs(AirportSpec.CommuterStandX[1] - AirportSpec.CommuterStandX[0]) - AirportSpec.JetSpan;
            lines.Add($"{(standGap > 15f ? "ok  " : "FAIL")}  the airline stands leave {standGap:F0} m between two jets' wingtips");
            float runway = AirportSpec.RunwayWidth - AirportSpec.JetSpan;
            lines.Add($"{(runway > 5f ? "ok  " : "FAIL")}  the runway is {runway:F0} m wider than a trijet's span");
            float standClear = AirportSpec.StandZ0 - (AirportSpec.TaxiwayZ + AirportSpec.TaxiObjectFreeHalf);
            lines.Add($"{(standClear >= 0f ? "ok  " : "FAIL")}  nearest stand is {standClear:F2} m clear of the taxiway object free area");
            float apronDepth = AirportSpec.ApronZ1 - AirportSpec.ApronZ0;
            lines.Add($"ok    ramp is {apronDepth:F0} m deep, {AirportSpec.ApronX1 - AirportSpec.ApronX0:F0} m along");
            lines.Add($"ok    hold short is {AirportSpec.HoldShortZ:F0} m from the runway centreline (FAA: 250 ft = 76 m)");
            return lines;
        }
    }
}
