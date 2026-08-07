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
            var badStation = new List<int>();

            // The two forced-landmark routes are told apart by INDEX now that there are two:
            // the bank rides requiredLandmark, the police station guaranteedLandmark, and a
            // "has any forced landmark" test stopped meaning "has a bank" the day the station
            // joined - it would wave through a city with a station and no bank.
            var bankIndex = -1;
            var stationIndex = -1;
            var stationZone = BlockZone.ResidentialHigh;
            if (prefabs.zonePalettes != null)
                foreach (var palette in prefabs.zonePalettes)
                {
                    if (palette == null)
                        continue;
                    if (palette.requiredLandmark >= 0)
                        bankIndex = palette.requiredLandmark;
                    if (palette.guaranteedLandmark >= 0)
                    {
                        stationIndex = palette.guaranteedLandmark;
                        stationZone = palette.zone;
                    }
                }

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

                if (!ownBlock && !HasForcedLandmark(grid, bankIndex))
                    noBank.Add(seed);

                // The station's guarantee: exactly ONE block of the owning zone marked with
                // its index. Zero means FulfilGuaranteedLandmarks found no host; two means a
                // double-mark bug. (Whether the prefab then FITS the block is a placement
                // question this data-only sweep cannot see - the in-editor pass covers it.)
                if (stationIndex >= 0 && stationIndex != bankIndex)
                {
                    var stationForced = 0;
                    for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                        if (grid.ForcedLandmarkOf(blockId) == stationIndex
                            && grid.ZoneOf(blockId) == stationZone)
                            stationForced++;

                    if (stationForced != 1)
                        badStation.Add(seed);
                }
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

            if (badStation.Count > 0)
                result.Failures.Add(
                    $"{badStation.Count}/{maps} cities do not force exactly one police station " +
                    $"block. First offending seeds: {string.Join(", ", badStation.GetRange(0, System.Math.Min(8, badStation.Count)))}");

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
        /// Whether any block was marked to build THIS landmark index. That mark is how the
        /// bank gets into a street wall, so it stands in for "this city's bank went the other
        /// way" - by index, because the station's guarantee also marks blocks now and an
        /// index-blind test would count a station as a bank. A negative index (no palette
        /// carries the landmark) matches the old any-mark behaviour.
        /// </summary>
        static bool HasForcedLandmark(CityGrid grid, int index)
        {
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var forced = grid.ForcedLandmarkOf(blockId);
                if (index < 0 ? forced >= 0 : forced == index)
                    return true;
            }

            return false;
        }
    }
}
