using System.Collections.Generic;
using System.Text;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// How often each zone actually appears, measured over a few hundred seeds.
    ///
    /// This exists because the city stopped guaranteeing anything. While ZonePlanner's rescue pass
    /// ran on an inferred trigger, the hospital, the school and the church were in every city by
    /// construction and their weights only decided WHERE they went. Now weight decides WHETHER,
    /// and nobody can read a frequency off a weight by eye: four zones carry maxBlockCells 1 and
    /// compete for the map's handful of one-cell blocks, and the shared landmark budget gates two
    /// of them again on top of that. So the weights get tuned against this table rather than
    /// against intuition.
    ///
    /// The bank row is not a tuning input but an assertion. The city is supposed to hold exactly
    /// one bank on every seed, by a route ZonePlanner rolls per city and then fulfils by force,
    /// and this is what checks that the two branches between them actually cover every case.
    ///
    /// Same discipline as ParkPlotTests - no UnityEngine.Object, failures returned as data - so a
    /// bare .NET host can call Run() by reflection with no Editor. The config is MUTATED, one
    /// field, once per iteration: pass a clone, never the project's own asset.
    /// </summary>
    public static class ZoneFrequencySweep
    {
        public sealed class Result
        {
            public string Table;
            public List<string> Failures = new();
        }

        public static Result Run(CityConfig config, PrefabDatabase prefabs, int seeds = 500)
        {
            var result = new Result();

            var zones = (BlockZone[])System.Enum.GetValues(typeof(BlockZone));
            var cities = new int[zones.Length];
            var total = new int[zones.Length];

            var maps = 0;
            var blocks = 0;
            var minBlocks = int.MaxValue;
            var maxBlocks = 0;
            var bothCivic = 0;
            var bankOwnBlock = 0;
            var noBank = new List<int>();

            for (var seed = 0; seed < seeds; seed++)
            {
                config.seed = seed;

                var grid = CityGenerator.Generate(config);

                // CityGenerator already refused this one and said so; counting it would report a
                // generator failure as a zoning frequency.
                if (grid == null || grid.BlockCount <= 1)
                    continue;

                ZonePlanner.Assign(grid, prefabs, config);

                maps++;
                blocks += grid.BlockCount;
                if (grid.BlockCount < minBlocks) minBlocks = grid.BlockCount;
                if (grid.BlockCount > maxBlocks) maxBlocks = grid.BlockCount;

                var seen = new int[zones.Length];
                for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                    seen[(int)grid.ZoneOf(blockId)]++;

                for (var z = 0; z < zones.Length; z++)
                {
                    total[z] += seen[z];
                    if (seen[z] > 0)
                        cities[z]++;
                }

                if (seen[(int)BlockZone.Hospital] > 0 && seen[(int)BlockZone.School] > 0)
                    bothCivic++;

                // The two branches of the bank's route, counted separately: a Bank zone means it
                // took a block, a forced landmark means it went into a street wall. Exactly one
                // must be true, and "neither" is the failure this sweep exists to catch.
                var ownBlock = seen[(int)BlockZone.Bank] > 0;
                if (ownBlock)
                    bankOwnBlock++;

                if (!ownBlock && !HasForcedLandmark(grid))
                    noBank.Add(seed);
            }

            if (maps == 0)
            {
                result.Failures.Add("No usable maps generated - check gridWidth/gridHeight are " +
                                    "actually set; a bare host skips field initialisers.");
                result.Table = "(nothing measured)";
                return result;
            }

            if (noBank.Count > 0)
                result.Failures.Add(
                    $"{noBank.Count}/{maps} cities have NO bank - neither a Bank block nor a " +
                    $"forced landmark. First offending seeds: {string.Join(", ", noBank.GetRange(0, System.Math.Min(8, noBank.Count)))}");

            var text = new StringBuilder();
            text.AppendLine($"[ZoneFrequency] {maps} maps from {seeds} seeds, " +
                            $"{config.gridWidth}x{config.gridHeight}, spacing " +
                            $"{config.minArterialSpacing}-{config.maxArterialSpacing}. " +
                            $"Mean blocks/city {(float)blocks / maps:0.0} (min {minBlocks}, max {maxBlocks}).");
            text.AppendLine($"  Bank present: {100f * (maps - noBank.Count) / maps:0.0}%  " +
                            $"(own block {100f * bankOwnBlock / maps:0.0}%, " +
                            $"street wall {100f * (maps - noBank.Count - bankOwnBlock) / maps:0.0}%)");
            text.AppendLine($"  Both hospital AND school: {100f * bothCivic / maps:0.0}%");
            text.AppendLine("  zone              in >=1 city   mean blocks");

            for (var z = 0; z < zones.Length; z++)
                text.AppendLine($"  {zones[z],-18}{100f * cities[z] / maps,8:0.0}%{(float)total[z] / maps,14:0.00}");

            result.Table = text.ToString().TrimEnd();
            return result;
        }

        /// <summary>
        /// Whether any block was marked to build a required landmark. That mark is how the bank
        /// gets into a street wall, so it stands in for "this city's bank went the other way".
        /// </summary>
        static bool HasForcedLandmark(CityGrid grid)
        {
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                if (grid.ForcedLandmarkOf(blockId) >= 0)
                    return true;

            return false;
        }
    }
}
