// CoreSim - deals the city core from seeds with no editor, and tallies the verdicts.
//
//     cd Tools/CoreSim
//     dotnet run -c Release -- --seed 1 --count 30       # thirty seeds: deals needed, faults, areas
//     dotnet run -c Release -- --synty --map             # the demo's own arrangement, with its raster
//     dotnet run -c Release -- --seed 7 --map --rows     # one seed drawn out
//
// The blocks come from blocks.txt beside this file: name, demo pivot, ground box, size and
// mask as the editor measured them (Tools/CoreSim/README.md says how to refresh it).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using UnityEngine;

static class Program
{
    static int Main(string[] args)
    {
        int seed = 1, count = 1;
        bool synty = false, map = false, rows = false, tiles = false, stats = false;
        int deal = -1;
        string file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "blocks.txt");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = int.Parse(args[++i]); break;
                case "--count": count = int.Parse(args[++i]); break;
                case "--synty": synty = true; break;
                case "--map": map = true; break;
                case "--rows": rows = true; break;
                case "--tiles": tiles = true; break;
                case "--blocks": file = args[++i]; break;
                case "--deal": deal = int.Parse(args[++i]); break;
                case "--stats": stats = true; break;
                case "--trace": { string want = args[++i]; CoreRoads.Trace = line => { if (line.Contains(want)) Console.WriteLine("      " + line); }; break; }
                default: Console.WriteLine("unknown " + args[i]); return 2;
            }
        }
        var blocks = ReadBlocks(file);
        Console.WriteLine($"{blocks.Count} blocks from {Path.GetFullPath(file)}");

        // one deal of one seed, drawn out, faults and all
        if (deal >= 0)
        {
            var plan = CoreLayout.Roll(blocks, unchecked(seed * 1000003 + deal * 7919));
            var raster = CoreRoads.Build(blocks, plan);
            Console.WriteLine($"seed {seed} deal {deal + 1}: faults {raster.Faults}");
            foreach (var row in plan.Rows) Console.WriteLine("   " + row);
            foreach (var line in raster.Report.Split('\n')) Console.WriteLine("   " + line.Trim());
            Console.WriteLine(raster.Map);
            return raster.Faults == 0 ? 0 : 1;
        }
        // every deal of every seed, and what was wrong with the ones that were
        if (stats)
        {
            var kinds = new Dictionary<string, int>();
            int deals = 0, cleanDeals = 0;
            for (int n = 0; n < count; n++)
                for (int d = 0; d < CoreLayout.Deals; d++)
                {
                    var plan = CoreLayout.Roll(blocks, unchecked((seed + n) * 1000003 + d * 7919));
                    var raster = CoreRoads.Build(blocks, plan);
                    deals++;
                    if (raster.Faults == 0) { cleanDeals++; continue; }
                    foreach (var line in raster.Report.Split('\n'))
                    {
                        string key = line.Contains("left bare") ? "left bare" + (line.Contains(" cells") ? " " + line.Trim().Split(' ')[2] + " cells" : "")
                                   : line.Contains("no road along") ? "no road along " + line.Trim().Substring(line.Trim().LastIndexOf("its ") + 4)
                                   : line.Contains("hemmed") ? "stub"
                                   : line.Contains("claimed") ? "clash" : null;
                        if (key == null) continue;
                        kinds[key] = kinds.TryGetValue(key, out var k) ? k + 1 : 1;
                    }
                }
            Console.WriteLine($"{deals} deals, {cleanDeals} clean ({100.0 * cleanDeals / deals:F0}%)");
            foreach (var pair in kinds.OrderByDescending(p => p.Value)) Console.WriteLine($"   {pair.Value,5}  {pair.Key}");
            return 0;
        }

        int clean = 0, firstDeal = 0, worst = 0;
        var dealsNeeded = new List<int>();
        for (int n = 0; n < count; n++)
        {
            int s = synty ? CoreLayout.SyntySeed : seed + n;
            var plan = CoreLayout.Arrange(blocks, s, out var raster);
            foreach (var block in blocks) CoreLayout.Place(block);
            if (raster.Faults == 0) clean++;
            if (plan.Attempt == 0) firstDeal++;
            dealsNeeded.Add(plan.Attempt + 1);
            worst = Math.Max(worst, raster.Faults);
            string roads = raster.Report.Split('\n').FirstOrDefault(l => l.Contains(" roads:"))?.Trim() ?? "";
            Console.WriteLine($"{plan.Name,-18} deals {plan.Attempt + 1,2}  faults {raster.Faults,2}  " +
                              $"{raster.NX * 5}x{raster.NZ * 5} m  blocks {raster.BlockArea} road {raster.RoadArea} " +
                              $"parking {raster.ParkingArea} spare {raster.SpareArea}  {roads}");
            if (rows) foreach (var row in plan.Rows) Console.WriteLine("   " + row);
            if (raster.Faults > 0 || rows)
                foreach (var line in raster.Report.Split('\n'))
                    if (!line.Contains(" roads:")) Console.WriteLine("   " + line.Trim());
            if (map) Console.WriteLine(raster.Map);
            if (tiles)
            {
                int stood = 0;
                CoreRoads.Lay(raster, (prefab, parent) => { stood++; return new GameObject(prefab.name); }, new GameObject("roads").transform);
                Console.WriteLine($"   {stood} tiles stood");
            }
            if (synty) break;
        }
        if (!synty)
            Console.WriteLine($"{count} seeds: {clean} clean, {firstDeal} on the first deal, " +
                              $"deals needed max {dealsNeeded.Max()} mean {dealsNeeded.Average():F2}, worst faults {worst}");
        return clean == (synty ? 1 : count) ? 0 : 1;
    }

    /// <summary>blocks.txt: a header line "name pivotX pivotZ groundMinX groundMinZ cw cd maxH"
    /// then cd lines of cw characters, north row first, '#' where the block fills the cell.</summary>
    static List<CoreLayout.Block> ReadBlocks(string file)
    {
        var blocks = new List<CoreLayout.Block>();
        var lines = File.ReadAllLines(file);
        for (int at = 0; at < lines.Length;)
        {
            var head = lines[at++].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 8) continue;
            string name = head[0];
            float gx = float.Parse(head[3], System.Globalization.CultureInfo.InvariantCulture);
            float gz = float.Parse(head[4], System.Globalization.CultureInfo.InvariantCulture);
            int cw = int.Parse(head[5]), cd = int.Parse(head[6]);
            float maxH = float.Parse(head[7], System.Globalization.CultureInfo.InvariantCulture);
            var mask = new bool[cw, cd];
            for (int j = cd - 1; j >= 0; j--)
            {
                string row = lines[at++];
                for (int i = 0; i < cw; i++) mask[i, j] = row[i] == '#';
            }
            // the measure rounds the box to the 5 m beat; so does this
            gx = (float)Math.Round(gx / 5f) * 5f;
            gz = (float)Math.Round(gz / 5f) * 5f;
            blocks.Add(CoreLayout.Describe(name, new Vector2(gx, gz), cw, cd, mask, maxH));
        }
        return blocks;
    }
}
