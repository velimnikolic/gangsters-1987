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
        bool synty = false, map = false, rows = false, tiles = false, stats = false, industrial = false;
        bool park = false, sweep = false, quay = false, residential = false;
        string size = "";
        int deal = -1;
        string file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "blocks.txt");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = int.Parse(args[++i]); break;
                case "--count": count = int.Parse(args[++i]); break;
                case "--synty": synty = true; break;
                case "--industrial": industrial = true; break;
                case "--park": park = true; break;
                case "--residential": residential = true; break;
                case "--quay": quay = true; break;
                case "--size": size = args[++i]; break;
                case "--sweep": sweep = true; break;
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
        if (residential) return Residential(seed, count, size, map);
        if (park) return Parks(seed, count, size, map, sweep);
        if (industrial) return Industry(seed, count, deal, map, rows, stats);

        var blocks = ReadBlocks(file);
        Console.WriteLine($"{blocks.Count} blocks from {Path.GetFullPath(file)}");
        if (quay) return Quays(blocks, seed, count, size, map, sweep);

        // one deal of one seed, drawn out, faults and all
        if (deal >= 0)
        {
            var plan = CoreLayout.Roll(blocks, unchecked(seed * 1000003 + deal * 7919));
            var raster = CoreRoads.Build(CoreLayout.WithGround(blocks, plan), plan);
            Console.WriteLine($"seed {seed} deal {deal + 1}: faults {raster.Faults}, {plan.Parks.Count} park(s), " +
                              $"{plan.Quays.Count} stretch(es) of promenade, {plan.Bridges.Count} bridge(s)");
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
                    // the parks and the river's ground the deal made are blocks too - left
                    // out, they are holes the verdict calls bare, and the tally measures a
                    // city nobody is building
                    var raster = CoreRoads.Build(CoreLayout.WithGround(blocks, plan), plan);
                    deals++;
                    if (raster.Faults == 0) { cleanDeals++; continue; }
                    foreach (var line in raster.Report.Split('\n'))
                    {
                        string key = line.Contains("left bare") ? "left bare" + (line.Contains(" cells") ? " " + line.Trim().Split(' ')[2] + " cells" : "")
                                   : line.Contains("no road along") ? "no road along " + line.Trim().Substring(line.Trim().LastIndexOf("its ") + 4)
                                   : line.Contains("hemmed") ? "stub"
                                   : line.Contains("bridge") ? "bridge broken"
                                   : line.Contains("run together") ? "junctions run together"
                                   : line.Contains("runs out") ? "alley trap"
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
                              $"parking {raster.ParkingArea} spare {raster.SpareArea} water {raster.WaterArea}  " +
                              $"quay {plan.Quays.Count}/{plan.Bridges.Count}  {roads}");
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

    /// <summary>
    /// The industrial quarter, dealt the same way and judged by the same reader. Nothing
    /// is read off disk: its parcels are dealt from the seed, not harvested, so the whole
    /// verdict is in the code.
    /// </summary>
    static int Industry(int seed, int count, int deal, bool map, bool rows, bool stats)
    {
        if (deal >= 0)
        {
            var one = IndustrialLayout.Roll(unchecked(seed * 1000003 + deal * 7919));
            var drawn = CoreRoads.Build(IndustrialLayout.Blocks(one), one.Roads);
            Console.WriteLine($"industrial seed {seed} deal {deal + 1}: faults {drawn.Faults}, " +
                              $"{one.Islands.Count} islands, {one.Parcels.Count} parcels");
            foreach (var row in one.Rows) Console.WriteLine("   " + row);
            foreach (var line in drawn.Report.Split('\n')) Console.WriteLine("   " + line.Trim());
            if (map) Console.WriteLine(drawn.Map);
            return drawn.Faults == 0 ? 0 : 1;
        }
        if (stats)
        {
            var kinds = new Dictionary<string, int>();
            int deals = 0, cleanDeals = 0;
            for (int n = 0; n < count; n++)
                for (int d = 0; d < IndustrialLayout.Deals; d++)
                {
                    var one = IndustrialLayout.Roll(unchecked((seed + n) * 1000003 + d * 7919));
                    var drawn = CoreRoads.Build(IndustrialLayout.Blocks(one), one.Roads);
                    deals++;
                    if (drawn.Faults == 0) { cleanDeals++; continue; }
                    foreach (var line in drawn.Report.Split('\n'))
                    {
                        string key = line.Contains("left bare") ? "left bare"
                                   : line.Contains("no road along") ? "no road along " + line.Trim().Substring(line.Trim().LastIndexOf("its ") + 4)
                                   : line.Contains("hemmed") ? "stub"
                                   : line.Contains("run together") ? "junctions run together"
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
        var needed = new List<int>();
        var cast = new Dictionary<string, int>();
        for (int n = 0; n < count; n++)
        {
            var plan = IndustrialLayout.Arrange(seed + n, out var raster);
            if (raster.Faults == 0) clean++;
            if (plan.Attempt == 0) firstDeal++;
            needed.Add(plan.Attempt + 1);
            worst = Math.Max(worst, raster.Faults);
            foreach (var parcel in plan.Parcels)
            {
                string key = parcel.Recipe.ToString();
                cast[key] = cast.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            string roads = raster.Report.Split('\n').FirstOrDefault(l => l.Contains(" roads:"))?.Trim() ?? "";
            Console.WriteLine($"{plan.Name,-18} deals {plan.Attempt + 1,2}  faults {raster.Faults,2}  " +
                              $"{raster.NX * 5}x{raster.NZ * 5} m  islands {plan.Islands.Count,2} parcels {plan.Parcels.Count,2}  " +
                              $"blocks {raster.BlockArea} road {raster.RoadArea} spare {raster.SpareArea}  {roads}");
            if (rows) foreach (var row in plan.Rows) Console.WriteLine("   " + row);
            if (raster.Faults > 0 || rows)
                foreach (var line in raster.Report.Split('\n'))
                    if (!line.Contains(" roads:")) Console.WriteLine("   " + line.Trim());
            if (map) Console.WriteLine(raster.Map);
        }
        Console.WriteLine($"{count} seeds: {clean} clean, {firstDeal} on the first deal, " +
                          $"deals needed max {needed.Max()} mean {needed.Average():F2}, worst faults {worst}");
        Console.WriteLine("   cast: " + string.Join("  ", cast.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        return clean == count ? 0 : 1;
    }

    /// <summary>
    /// Parks. Nothing is read off disk and no roads are drawn: a park is dealt from its own
    /// SIZE, which is what the quarter hands it, so the whole verdict is arithmetic.
    ///
    /// --sweep is the one that matters. A park generator that works on the sizes it was
    /// written against and falls over on 5 x 30 is a generator that will fall over the first
    /// time a quarter deals it an awkward rectangle, and the belt round the core deals
    /// nothing else.
    /// </summary>
    /// <summary>
    /// The residential block, dealt from seeds and judged with no editor open.
    ///
    /// Thirty seeds of every class is the tally that counts - one seed proves nothing, and a
    /// block that comes out clean once can be a block whose corner unit happened to fit.
    /// </summary>
    static int Residential(int seed, int count, string size, bool map)
    {
        var sizes = new List<(string Name, int W, int D)>();
        if (size.Contains("x"))
        {
            var bits = size.Split('x');
            sizes.Add(("asked", int.Parse(bits[0]), int.Parse(bits[1])));
        }
        else if (size.Length > 0) sizes.Add((size, 0, 0));
        else
        {
            sizes.Add(("corner", 0, 0));
            sizes.Add(("row", 0, 0));
            sizes.Add(("block", 0, 0));
            sizes.Add(("court", 0, 0));
        }

        int tried = 0, clean = 0;
        var faulty = new Dictionary<string, int>();
        var refused = new Dictionary<string, int>();
        var tallies = new List<string>();

        foreach (var want in sizes)
        {
            int good = 0, empties = 0, doors = 0, gaps = 0, cafes = 0, share = 0;
            for (int n = 0; n < count; n++)
            {
                int s = seed + n;
                var dice = new Random(s * 31 + want.Name.Length);
                int w = want.W, d = want.D;
                if (w == 0) Size(want.Name, dice, out w, out d);

                var plan = ResidentialLot.Roll(w, d, s, artery: dice.Next(4));
                tried++;
                if (plan.Clean) { clean++; good++; }
                empties += plan.M.Empty;
                doors += plan.M.Doors;
                gaps += plan.M.Gaps;
                cafes += plan.M.Cafes;
                share = Math.Max(share, plan.M.Share);

                foreach (var fault in plan.Faults)
                {
                    string key = System.Text.RegularExpressions.Regex.Replace(fault, @"[0-9]+", "N");
                    key = key.Substring(0, Math.Min(key.Length, 90));
                    faulty[key] = faulty.TryGetValue(key, out var k) ? k + 1 : 1;
                    if (faulty[key] <= 2) Console.WriteLine($"   {w * 5}x{d * 5} m seed {s}: {fault}");
                }
                foreach (var line in plan.Refused)
                {
                    string key = System.Text.RegularExpressions.Regex.Replace(line, @"[0-9]+", "N");
                    refused[key] = refused.TryGetValue(key, out var r) ? r + 1 : 1;
                }
                if (count <= 3 || map)
                {
                    Console.WriteLine(ResidentialLot.Report(plan));
                    if (map) Console.WriteLine(ResidentialLot.Map(plan));
                }
            }
            tallies.Add($"   {want.Name,-8} {good,3}/{count} clean, " +
                        $"{(double)doors / count:F1} doors, {(double)gaps / count:F1} gaps, " +
                        $"{cafes}/{count} cafes, biggest unit {share}%, " +
                        $"{(double)empties / count:F1} empty cells a block");
        }

        Console.WriteLine($"{tried} block(s), {clean} clean ({100.0 * clean / tried:F0}%)");
        foreach (var line in tallies) Console.WriteLine(line);
        foreach (var pair in refused.OrderByDescending(p => p.Value))
            Console.WriteLine($"   {pair.Value,5}  REFUSED {pair.Key}");
        foreach (var pair in faulty.OrderByDescending(p => p.Value))
            Console.WriteLine($"   {pair.Value,5}  {pair.Key}");
        return clean == tried ? 0 : 1;
    }

    /// <summary>A block of the asked-for class, in cells, pavement ring included.</summary>
    static void Size(string name, Random dice, out int w, out int d)
    {
        switch (name)
        {
            case "corner": w = dice.Next(6, 10); d = dice.Next(5, 9); break;
            case "row": w = dice.Next(4, 7); d = dice.Next(10, 15); break;
            case "court": w = dice.Next(16, 21); d = dice.Next(16, 21); break;
            default: w = dice.Next(10, 16); d = dice.Next(11, 20); break;
        }
        w += 2 * ResidentialLot.Walk;
        d += 2 * ResidentialLot.Walk;
        if (dice.Next(2) == 0) { int t = w; w = d; d = t; }
    }

    static int Parks(int seed, int count, string size, bool map, bool sweep)
    {
        if (sweep)
        {
            int tried = 0, clean = 0, worst = 0;
            var faulty = new Dictionary<string, int>();
            var classes = new Dictionary<ParkWalk.Klass, int>();
            // up to 60 cells - 300 m - because the belt round the core is one unbroken park
            // the whole width of it, and that is bigger than anything the first sweep tried
            for (int w = 5; w <= 60; w += w < 30 ? 1 : 3)
                for (int d = 5; d <= 60; d += d < 30 ? 1 : 3)
                {
                    var plan = ParkWalk.Lay(w, d, ParkWalk.Edge.Alone(), new Random(seed * 7919 + w * 131 + d));
                    string said = ParkWalk.Report(plan, out int faults);
                    tried++;
                    classes[plan.Klass] = classes.TryGetValue(plan.Klass, out var c) ? c + 1 : 1;
                    if (faults == 0) { clean++; continue; }
                    worst = Math.Max(worst, faults);
                    foreach (var line in said.Split('\n'))
                    {
                        if (!line.Contains("WARNING")) continue;
                        string key = line.Substring(line.IndexOf("WARNING") + 9);
                        key = System.Text.RegularExpressions.Regex.Replace(key, @"[0-9]+", "N");
                        faulty[key] = faulty.TryGetValue(key, out var k) ? k + 1 : 1;
                        if (faulty[key] <= 2) Console.WriteLine($"   {w * 5}x{d * 5} m: {key}");
                    }
                }
            Console.WriteLine($"{tried} sizes, {clean} clean ({100.0 * clean / tried:F0}%), worst {worst} faults");
            foreach (var pair in classes.OrderBy(p => p.Key.ToString()))
                Console.WriteLine($"   {pair.Value,5}  {pair.Key}");
            foreach (var pair in faulty.OrderByDescending(p => p.Value))
                Console.WriteLine($"   {pair.Value,5}  {pair.Key}");
            return clean == tried ? 0 : 1;
        }

        int good = 0;
        for (int n = 0; n < count; n++)
        {
            int s = seed + n;
            var dice = new Random(s);
            Measure(size, dice, out int nx, out int nz);
            var plan = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new Random(s));
            string report = ParkWalk.Report(plan, out int faults);
            if (faults == 0) good++;
            Console.WriteLine($"seed {s,-6} {nx * 5}x{nz * 5} m  faults {faults,2}  {report}");
            if (map) Console.WriteLine(plan.Map);
        }
        Console.WriteLine($"{count} parks: {good} clean");
        return good == count ? 0 : 1;
    }

    /// <summary>
    /// The promenade. --sweep lays every depth the core deals against every length up to
    /// the whole height of a core, with the streets arriving at random and every kind of
    /// end, and asks the plan's own verdict of each - the park's lesson, that a generator
    /// is only known to work on the sizes it was tried on. Without --sweep the core is
    /// dealt for the seed and every stretch of its promenade is read off it and laid, the
    /// way the district does it.
    /// </summary>
    static int Quays(List<CoreLayout.Block> blocks, int seed, int count, string size, bool map, bool sweep)
    {
        if (sweep)
        {
            int tried = 0, clean = 0, worst = 0;
            var faulty = new Dictionary<string, int>();
            var cast = new Dictionary<string, int>();
            var ends = new[] { QuayWalk.End.Line, QuayWalk.End.Bridge, QuayWalk.End.Boulevard };
            for (int depth = QuayWalk.DeepMin; depth <= 8; depth++)
                for (int length = 1; length <= 160; length += length < 40 ? 1 : 3)
                    for (int e = 0; e < 4; e++)
                    {
                        var dice = new Random(seed * 7919 + depth * 131 + length * 17 + e);
                        // streets arrive where the rows' blocks put them: 7 to 20 cells apart,
                        // each three wide
                        var mouths = new List<QuayWalk.Mouth>();
                        for (int z = dice.Next(3, 12); z + 3 <= length; z += 3 + dice.Next(7, 21))
                            mouths.Add(new QuayWalk.Mouth(z, z + 3));
                        // the wants are asked only of a strip long enough to hold them at
                        // all (Report counts a want with no room as a fault); whether the
                        // mouths leave a room for them is what the sweep then measures
                        var wants = new QuayWalk.Wants
                        {
                            Fair = e % 2 == 0 && length >= 14, FairAtStart = e < 2,
                            Landing = e != 1 && length >= 8, Diner = e != 2 && length >= 10,
                            Terraces = dice.Next(0, 4),
                        };
                        var plan = QuayWalk.Lay(depth, length, mouths, ends[e % 3], ends[(e + 1) % 3], wants, dice);
                        string said = QuayWalk.Report(plan, out int faults);
                        tried++;
                        foreach (var room in plan.Rooms)
                        {
                            string key = room.Programme.ToString();
                            cast[key] = cast.TryGetValue(key, out var c) ? c + 1 : 1;
                        }
                        if (faults == 0) { clean++; continue; }
                        worst = Math.Max(worst, faults);
                        foreach (var part in said.Split(';'))
                        {
                            if (!part.Contains("WARNING")) continue;
                            string key = part.Substring(part.IndexOf("WARNING") + 9).Trim();
                            key = System.Text.RegularExpressions.Regex.Replace(key, @"[0-9]+", "N");
                            faulty[key] = faulty.TryGetValue(key, out var k) ? k + 1 : 1;
                            if (faulty[key] <= 2) Console.WriteLine($"   {depth * 5}x{length * 5} m ends {e}: {key}");
                        }
                    }
            Console.WriteLine($"{tried} strips, {clean} clean ({100.0 * clean / tried:F0}%), worst {worst} faults");
            Console.WriteLine("   cast: " + string.Join("  ", cast.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
            foreach (var pair in faulty.OrderByDescending(p => p.Value)) Console.WriteLine($"   {pair.Value,5}  {pair.Key}");
            return clean == tried ? 0 : 1;
        }

        int good = 0, all = 0;
        var castAll = new Dictionary<string, int>();
        for (int n = 0; n < count; n++)
        {
            int s = seed + n;
            var plan = CoreLayout.Arrange(blocks, s, out var raster);
            var wants = QuayWalk.Cast(plan);
            int stretches = 0, faultsAll = 0;
            for (int q = 0; q < plan.Quays.Count; q++)
            {
                var box = plan.Quays[q].Box;
                int dice = unchecked(s * 7919 + (int)Math.Round(box.xMin) * 104729 + (int)Math.Round(box.yMin) * 1299709);
                var strip = QuayWalk.ForQuay(plan, plan.Quays[q], wants[q], new Random(dice));
                string said = QuayWalk.Report(strip, out int faults);
                faultsAll += faults;
                stretches++;
                foreach (var room in strip.Rooms)
                {
                    string key = room.Programme.ToString();
                    castAll[key] = castAll.TryGetValue(key, out var c) ? c + 1 : 1;
                }
                if (count == 1 || faults > 0)
                    Console.WriteLine($"   {plan.Quays[q].Name} ({strip.South}..{strip.North}): {said}");
                if (map) Console.WriteLine(strip.Map);
            }
            all++;
            if (faultsAll == 0 && raster.Faults == 0) good++;
            Console.WriteLine($"{plan.Name,-18} core faults {raster.Faults}  promenade {stretches} stretch(es), {faultsAll} fault(s)");
        }
        Console.WriteLine($"{all} seeds: {good} clean");
        Console.WriteLine("   cast: " + string.Join("  ", castAll.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        return good == all ? 0 : 1;
    }

    /// <summary>The size to deal: a class name, an explicit WxD in cells, or anything at
    /// all if nothing was asked for.</summary>
    static void Measure(string size, Random dice, out int nx, out int nz)
    {
        size = (size ?? "").ToLowerInvariant();
        int at = size.IndexOf('x');
        if (at > 0 && int.TryParse(size.Substring(0, at), out nx) &&
            int.TryParse(size.Substring(at + 1), out nz)) return;

        switch (size)
        {
            case "pocket": nx = dice.Next(5, 8); nz = dice.Next(5, 8); return;
            case "square": nx = dice.Next(8, 13); nz = dice.Next(8, 13); return;
            case "park": nx = dice.Next(13, 31); nz = dice.Next(13, 31); return;
            case "strip": nx = dice.Next(20, 61); nz = dice.Next(6, 9); return;
            default: nx = dice.Next(5, 31); nz = dice.Next(5, 31); return;
        }
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
