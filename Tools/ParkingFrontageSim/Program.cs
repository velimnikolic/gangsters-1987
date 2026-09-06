using System;
using RoadDemo;
using UnityEngine;

static class Program
{
    static void Check(bool okay, string message)
    {
        if (!okay) throw new Exception(message);
    }

    static void AccessFixtures()
    {
        var raster = new CoreRoads.Raster { X0 = 0f, Z0 = 0f, NX = 4, NZ = 4,
            Kinds = new CoreRoads.Kind[4, 4] };
        var ground = new ResidentialLot.Use[4, 4];
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
            {
                raster.Kinds[x, z] = CoreRoads.Kind.Block;
                ground[x, z] = ResidentialLot.Use.Walkway;
            }
        var frontage = new CoreParkingFrontage(raster);
        var first = new Vector3(1f, 0f, 1f);
        var last = new Vector3(1f, 0f, 18f);
        Check(!frontage.Allows(first, last), "Authored block without an access plan was accepted.");
        frontage.Add(Rect.MinMaxRect(0, 0, 20, 20), new ResidentialLot.Plan { W = 4, D = 4, Ground = ground });
        Check(frontage.Allows(first, last), "Continuous pavement refused.");
        foreach (var use in new[] { ResidentialLot.Use.Drive, ResidentialLot.Use.Parking, ResidentialLot.Use.Alley })
        {
            ground[0, 2] = use;
            Check(!frontage.Allows(first, last), "Vehicle access was blocked: " + use);
        }
        ground[0, 2] = ResidentialLot.Use.Walkway;
        foreach (var kind in new[] { CoreRoads.Kind.Bare, CoreRoads.Kind.Yard, CoreRoads.Kind.Parking, CoreRoads.Kind.Water })
        {
            raster.Kinds[0, 2] = kind;
            Check(!frontage.Allows(first, last), "Non-kerb cell was accepted: " + kind);
        }
        raster.Kinds[0, 2] = CoreRoads.Kind.Block;
        Check(!frontage.Allows(first, new Vector3(1f, 0f, 25f)), "Out-of-raster frontage accepted.");
    }

    static void Main(string[] args)
    {
        AccessFixtures();
        int count = args.Length > 0 ? int.Parse(args[0]) : 30;
        int min = int.MaxValue, max = 0;
        for (int n = 0; n < count; n++)
        {
            int seed = n == 0 ? 1987 : n * 7919;
            var blocks = CoreBlockCatalog.CreateBlocks();
            var plan = CoreLayout.Arrange(blocks, seed, out var raster);
            var frontage = new CoreParkingFrontage(raster);
            foreach (var block in plan.Residential)
            {
                var box = block.Box;
                int w = Mathf.RoundToInt(box.width / CoreLayout.Cell);
                int d = Mathf.RoundToInt(box.height / CoreLayout.Cell);
                int dice = unchecked(seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 +
                    Mathf.RoundToInt(box.yMin) * 1299709);
                var lot = CoreLayout.IsYard(block) ? ResidentialLot.Yard(w, d, dice, block.Unit)
                    : ResidentialLot.Roll(w, d, dice, Mathf.Max(0, block.Artery));
                frontage.Add(box, lot);
            }
            foreach (var park in plan.Parks) frontage.Add(park.Box);
            int accepted = 0, candidates = 0;
            foreach (var road in raster.Stretches)
            {
                if (road.Width != 3 && road.Width != 7) continue;
                for (float s = road.From + 12f; s < road.To - 10f; s += 14f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float across = road.Crown + side * (road.Width * CoreRoads.Cell * .5f + 1f);
                        var first = road.Vertical ? new Vector3(across, 0f, s - 4.1f)
                            : new Vector3(s - 4.1f, 0f, across);
                        var last = road.Vertical ? new Vector3(across, 0f, s + 4.1f)
                            : new Vector3(s + 4.1f, 0f, across);
                        candidates++;
                        if (frontage.Allows(first, last)) accepted++;
                    }
            }
            Check(accepted >= 60, $"Seed {seed}: only {accepted}/{candidates} usable frontage candidates.");
            min = Math.Min(min, accepted);
            max = Math.Max(max, accepted);
            Console.WriteLine($"seed {seed}: {accepted}/{candidates} frontage candidates accepted");
        }
        Console.WriteLine($"PASS: {count} generated Core layouts, {min}..{max} accepted frontage candidates per layout; access fixtures passed.");
    }
}
