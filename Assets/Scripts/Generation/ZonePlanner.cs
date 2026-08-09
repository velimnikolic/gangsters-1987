using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Decides what each block is FOR, before anything is built.
    ///
    /// Runs as its own pass rather than inside BlockBuilder because the ground needs the answer
    /// too - a park is grass and a car park is asphalt - and two placers deriving the zone
    /// independently would eventually disagree and pave a park.
    ///
    /// Three things shape the result, and all three exist because a flat random roll produced a
    /// bad city:
    ///
    /// 1. A radial bias. Rolling uniformly scatters factories through the middle of town and
    ///    leaves the centre no denser than the rim. Chicago put its works out by the rail and
    ///    its density in the Loop, so the masonry fabric rises toward the centre and industry
    ///    rises toward the edge.
    /// 2. Quotas. On a 9x7 map there are only about ten blocks - measured at 9.9 over 500 seeds,
    ///    min 6 and max 12, not the twelve this used to claim. Without a cap an unlucky
    ///    seed gives a city four hospitals and no housing.
    /// 3. A shuffled visit order. Quotas are consumed as blocks are assigned, so walking the
    ///    blocks in id order would hand every scarce zone to the low-numbered corner of the map.
    /// </summary>
    public static class ZonePlanner
    {
        /// <summary>
        /// Zones that are one landmark building and its yard. Capped together as well as
        /// individually - one hospital in a ten-block city is a landmark, two civic blocks
        /// on top of each other is a government district nobody asked for. Police used to be
        /// the third: the station is part of the residential fabric now, see BlockZone.
        /// </summary>
        static readonly BlockZone[] SingleLandmarkZones =
        {
            BlockZone.Hospital,
            BlockZone.School,
        };

        /// <summary>
        /// Blocks per single-landmark zone allowed. A hard ceiling now, and it was not one
        /// before: the rescue pass used to run straight past it and hand each of these zones a
        /// block anyway, so the budget was routinely overshot and never bound. With nothing
        /// guaranteed it binds for real, which makes the arithmetic worth doing against the map
        /// actually shipped rather than the one the old comment assumed. A 9x7 map is about ten
        /// blocks, not twelve; at 6 the budget would be ONE and the hospital and the school would
        /// become mutually exclusive - which is a different rule from "at most one of each" and
        /// not the one intended. At 4 it is two, and a third civic zone still could not join them.
        /// </summary>
        const int BlocksPerLandmarkZone = 4;

        /// <summary>
        /// How often the city's one bank takes a block of its own instead of standing in the
        /// street wall. Both shapes are wanted and only one can happen, so this is a single roll
        /// per city rather than two independent chances that could each miss - see the route
        /// decision in Assign for why the guarantee cannot be left to the two paths separately.
        /// </summary>
        const double BankOwnBlockChance = 0.25;

        public static void Assign(CityGrid grid, PrefabDatabase prefabs, CityConfig config)
        {
            if (grid.BlockCount == 0)
                return;

            // Debugging aid: one palette on every block, so a layout change to it is visible
            // everywhere at once. A warning, not a log - a city generated with this left on
            // looks plausible enough to ship by accident.
            if (config.debugSingleZone)
            {
                for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                    grid.SetZone(blockId, config.debugZone);

                Debug.LogWarning($"[ZonePlanner] Debug Single Zone is ON - every block is " +
                                 $"{config.debugZone}. Toggle it off under Tools/City.");
                return;
            }

            var palettes = new List<PrefabDatabase.ZonePalette>();
            if (prefabs.zonePalettes != null)
                foreach (var palette in prefabs.zonePalettes)
                    if (palette != null && palette.weight > 0f)
                        palettes.Add(palette);

            if (palettes.Count == 0)
            {
                Debug.LogWarning("[ZonePlanner] No zone palettes in the PrefabDatabase - every " +
                                 "block falls back to ResidentialHigh.");
                return;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Zoning);

            // The city has exactly one bank and always has one. WHICH SHAPE it takes is decided
            // here, once, rather than left to the two paths that can place it - because neither
            // path can see the other. Zoning runs to completion before BlockBuilder places a
            // single landmark, so at this point nothing can know whether some residential block
            // will later draw the bank out of its bag; and by the time that is known, zoning is
            // over and the option of giving the bank a block has passed. Two independent chances
            // would therefore sometimes produce two banks (uniqueBuildings catches that) and
            // sometimes none (nothing catches that). One roll up front, fulfilled by force
            // whichever way it falls, is the only version that cannot miss.
            //
            // Rolled from the zoning stream before anything else consumes it, so the route is a
            // pure function of the seed and does not shift when a palette's weight is edited.
            var bankOwnBlock = rng.NextDouble() < BankOwnBlockChance;

            // The port, decided the bank's way: one roll per city, fulfilled by force. A port
            // is not a block, it is a WATERFRONT - when the roll lands, every block touching
            // one map side becomes Port and the sea is laid along the whole of that side. A
            // single port block in the middle of an otherwise dry edge read as a diorama.
            //
            // Both draws are taken unconditionally, so toggling portChance or removing the
            // palette cannot shift what any later roll in this stream produces.
            var portRoll = rng.NextDouble();
            var portSideRoll = rng.Next(4);

            var portBlocks = new HashSet<int>();
            var hasPortPalette = false;
            if (prefabs.zonePalettes != null)
                foreach (var candidate in prefabs.zonePalettes)
                    if (candidate != null && candidate.portYard)
                        hasPortPalette = true;

            if (hasPortPalette && portRoll < config.portChance)
            {
                var side = portSideRoll switch
                {
                    0 => Sides.South,
                    1 => Sides.East,
                    2 => Sides.North,
                    _ => Sides.West,
                };

                ClaimPortSide(grid, side, portBlocks);
            }

            var centroids = Centroids(grid, out var cellCounts);

            // Cached rather than queried per roll: the neighbour probe walks the whole grid, and
            // it is asked for once per block per candidate palette.
            var adjacency = new List<int>[grid.BlockCount];
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                adjacency[blockId] = new List<int>(grid.NeighbourBlocks(blockId));

            // Which blocks front the dual carriageway. Cached for the same reason as adjacency -
            // it is a walk over the block's cells and would otherwise run once per candidate
            // palette per block.
            var onBoulevard = new bool[grid.BlockCount];
            if (grid.HasMainRoad)
                for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                    onBoulevard[blockId] = FacesMainRoad(grid, blockId);

            // Which blocks touch the map boundary - the only place a requiresMapEdge zone (the
            // port) may stand, because its water is laid beyond the outline. Cached for the
            // same reason as the two above.
            var onMapEdge = new bool[grid.BlockCount];
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                onMapEdge[blockId] = TouchesMapEdge(grid, blockId);

            // Two caps, and the difference matters. maxShare scales with the city, which is what
            // parks and works want - a map twice the size should have twice the parkland.
            // maxBlocks does not, which is what a hospital wants: a city has one however big it
            // gets. Expressing the hospital as a share was a real bug - 9% of a twelve-block map
            // rounds to one, and 9% of a forty-block map to three.
            var remaining = new int[palettes.Count];
            for (var i = 0; i < palettes.Count; i++)
            {
                var byShare = palettes[i].maxShare >= 1f
                    ? int.MaxValue
                    : Mathf.Max(1, Mathf.RoundToInt(grid.BlockCount * palettes[i].maxShare));

                remaining[i] = palettes[i].maxBlocks > 0
                    ? Mathf.Min(byShare, palettes[i].maxBlocks)
                    : byShare;
            }

            var landmarkBudget = Mathf.Max(1, grid.BlockCount / BlocksPerLandmarkZone);

            // A zone capped by block size still has to land somewhere: on a map subdivided
            // entirely into big blocks the cap relaxes to the smallest block the map has, so
            // maxBlockCells steers WHERE the zone goes and never whether it exists.
            var smallestBlock = int.MaxValue;
            foreach (var count in cellCounts)
                smallestBlock = Mathf.Min(smallestBlock, count);

            var sizeCap = new int[palettes.Count];
            for (var i = 0; i < palettes.Count; i++)
                sizeCap[i] = palettes[i].maxBlockCells > 0
                    ? Mathf.Max(palettes[i].maxBlockCells, smallestBlock)
                    : 0;

            var order = ShuffledBlockOrder(grid.BlockCount, rng);
            var weights = new float[palettes.Count];

            // Only the rescue pass reads this, and only to answer "did the city end up with one
            // at all". remaining[] cannot answer that: a palette allowed three blocks and given
            // one still has remaining left over, and topping it up would make the pass a filler
            // of quotas rather than a keeper of promises.
            var placed = new int[palettes.Count];

            foreach (var blockId in order)
            {
                // Spoken for by the port route above - the weighted roll never sees it.
                if (portBlocks.Contains(blockId))
                    continue;

                var radial = RadialPosition(centroids[blockId], grid);

                var total = 0f;
                for (var i = 0; i < palettes.Count; i++)
                {
                    var palette = palettes[i];

                    var allowed = remaining[i] > 0
                        && (!IsSingleLandmark(palette.zone) || landmarkBudget > 0)
                        && (sizeCap[i] == 0 || cellCounts[blockId] <= sizeCap[i])
                        && (palette.zone != BlockZone.Bank || bankOwnBlock)

                        // A portYard palette is assigned only by the side route above; a
                        // weight on it must not be able to sprinkle extra port blocks down
                        // the other edges of the map.
                        && !palette.portYard
                        && (!palette.requiresMapEdge || onMapEdge[blockId])
                        && !ClashesWithNeighbour(palette.zone, blockId, adjacency, grid);

                    weights[i] = allowed
                        ? palette.weight
                          * RadialBias(palette.zone, radial)
                          * BoulevardBias(palette.zone, onBoulevard[blockId])
                        : 0f;
                    total += weights[i];
                }

                var chosen = WeightedRoll.Index(weights, total, rng);

                // Every candidate retired. Rather than leave the block at the enum default and
                // pretend, fall back explicitly to the city's connective tissue.
                if (chosen < 0)
                {
                    grid.SetZone(blockId, BlockZone.ResidentialHigh);
                    continue;
                }

                var zone = palettes[chosen].zone;
                grid.SetZone(blockId, zone);

                placed[chosen]++;
                if (remaining[chosen] != int.MaxValue)
                    remaining[chosen]--;
                if (IsSingleLandmark(zone))
                    landmarkBudget--;
            }

            // The city's one promise, and the only pass that can overrule the weighted roll.
            //
            // The trigger used to be INFERRED - maxBlocks == 1 together with a size cap - which
            // read as a rule about size and behaved as a rule about existence, and so quietly
            // promised a block to whatever happened to be shaped like a civic zone. It is stated
            // outright now, one bool per palette, and the database sets it on exactly one:
            // the SCHOOL. The hospital beside it is still a ceiling - at most one - exactly as
            // the post office and the fire station have always been.
            //
            // The school is not the more important building; it is the one with a system hanging
            // off it. Its block carries the city's only SchoolMarker, and SchoolBusDirector has
            // no other way to find a school, so a seed that lost the weighted roll lost the bus,
            // the stops and every schoolchild with it - silently, one warning deep. That is what
            // this pass is for, and it was dormant machinery until the school needed it.
            // (The bank is promised by a different mechanism, below, because what it needs
            // guaranteed is the BUILDING and not this particular shape of block.)
            //
            // Three properties, each deliberate, each with a price:
            //
            // 1. It tests placed[i], not remaining[i]. "Guaranteed" means the city has at least
            //    one, for any maxBlocks - a palette allowed three and given one is satisfied.
            //    Every cap floors at 1, so placed[i] == 0 implies remaining[i] > 0 and the
            //    ceiling cannot be broken by topping up from nothing.
            // 2. sizeCap is IGNORED. By the time this runs the within-cap blocks may all be spent
            //    on parks and car parks, and "the smallest block still going" is the best reading
            //    of a size cap that a finished map can offer. Note what that means for a
            //    guaranteed zone with NO size cap: it still gets handed the map's smallest block.
            //    Right for a one-cell civic landmark, a poor gift to a park.
            // 3. The landmark budget is OVERSHOT, by at most one block per guaranteed palette.
            //    The budget stops a government district forming out of the weighted roll; it was
            //    never a reason for the city to have no hospital at all. With the school
            //    guaranteed that overshoot is paid in the ~60% of cities that would otherwise
            //    have gone without one - one block, knowingly. It is NOT paid by the hospital,
            //    which competes with the school for the same one-cell blocks: the pass runs
            //    after the roll and takes only from ResidentialHigh and Industrial, so 500 seeds
            //    on 16x7 move hospital 45.4% -> 45.4%, park 81.4% -> 81.4%, bank 100% -> 100%,
            //    and school 38.4% -> 100%. The block comes out of the fabric, as designed.
            //
            // The block always comes back from ResidentialHigh or Industrial - the two zones that
            // FILL a map - never from another scarce one. The search runs over `order` rather than
            // over block ids, so ties between equally small blocks fall to whichever the seed's
            // visit order reached first instead of always to the lowest-numbered corner.
            for (var i = 0; i < palettes.Count; i++)
            {
                var palette = palettes[i];
                if (!palette.guaranteed || placed[i] > 0)
                    continue;

                var best = SmallestFabricBlock(grid, palette.zone, order, cellCounts, adjacency);
                if (best < 0)
                    continue;

                grid.SetZone(best, palette.zone);
                placed[i]++;
                remaining[i]--;
                if (IsSingleLandmark(palette.zone))
                    landmarkBudget--;
            }

            FulfilBankRoute(grid, palettes, placed, order, cellCounts, adjacency, bankOwnBlock);
            FulfilGuaranteedLandmarks(grid, palettes, order, cellCounts);
        }

        /// <summary>
        /// Make good on every palette's guaranteedLandmark - today that is one entry, the
        /// police station, which the patrol fleet and the beat officers both treat as home and
        /// which therefore stopped being allowed to lose its 45% roll. Runs AFTER
        /// FulfilBankRoute on purpose: the bank is the older promise and picks its host first,
        /// and a block holds at most one forced landmark, so the station takes the largest
        /// block of the zone that the bank left unclaimed.
        ///
        /// Largest rather than smallest for the same reason as the bank: a landmark with a
        /// forecourt wants frontage, not economy. Preference order is blocks of two cells and
        /// up, then any block of the zone - the station's ~17m face fits even a single-cell
        /// block's street run, so an all-singles map still gets its station rather than none.
        ///
        /// This only marks the block; the build can still come up empty if every street run on
        /// it is shorter than the prefab (PlaceLandmark returns unchanged and the landmark is
        /// dropped), or the 45% roll can deliver the station on an earlier-built block first,
        /// in which case the forced draw is refused by UniqueBuildings and the mark is a no-op.
        /// Either way the city ends with at most one station, and only a pathological seed
        /// with none - PoliceDirector logs and stands down when that happens.
        /// </summary>
        static void FulfilGuaranteedLandmarks(
            CityGrid grid,
            List<PrefabDatabase.ZonePalette> palettes,
            int[] order,
            int[] cellCounts)
        {
            foreach (var palette in palettes)
            {
                if (palette.guaranteedLandmark < 0)
                    continue;

                var host = -1;
                var hostSmall = -1;
                foreach (var blockId in order)
                {
                    if (grid.ZoneOf(blockId) != palette.zone)
                        continue;
                    if (grid.ForcedLandmarkOf(blockId) >= 0)
                        continue;

                    if (cellCounts[blockId] >= 2)
                    {
                        if (host < 0 || cellCounts[blockId] > cellCounts[host])
                            host = blockId;
                    }
                    else if (hostSmall < 0)
                    {
                        hostSmall = blockId;
                    }
                }

                if (host < 0)
                    host = hostSmall;
                if (host >= 0)
                    grid.SetForcedLandmark(host, palette.guaranteedLandmark);
            }
        }

        /// <summary>
        /// Make good on the route rolled at the top of Assign: the city ends this method with
        /// exactly one bank in it, either standing on a block of its own or marked to be built
        /// into a residential street wall.
        ///
        /// Written as one method with two branches rather than as two independent chances,
        /// because that is the whole point - see the roll itself for why neither path can see the
        /// other. The branches are also each other's fallback. A city can genuinely lack anywhere
        /// to put the street-wall version: the bank is 20.53m deep against a terrace kit of
        /// 13-16m, so it wants a block with a courtyard to push back into, and a map subdivided
        /// entirely into single cells has none. When that happens the bank takes a block instead,
        /// which is the one shape that always fits somewhere.
        /// </summary>
        static void FulfilBankRoute(
            CityGrid grid,
            List<PrefabDatabase.ZonePalette> palettes,
            int[] placed,
            int[] order,
            int[] cellCounts,
            List<int>[] adjacency,
            bool ownBlock)
        {
            var bank = palettes.FindIndex(p => p.zone == BlockZone.Bank);
            if (bank < 0)
                return;

            if (!ownBlock)
            {
                // The street wall, which is where the bank usually is. The LARGEST residential
                // block, not the smallest: this is the one placement decision in the file that
                // wants room rather than economy, and a one-cell block would leave the bank's
                // 20.53m of depth eating the entire courtyard behind it.
                for (var i = 0; i < palettes.Count; i++)
                {
                    if (palettes[i].requiredLandmark < 0)
                        continue;

                    var host = -1;
                    foreach (var blockId in order)
                    {
                        if (grid.ZoneOf(blockId) != palettes[i].zone)
                            continue;
                        if (cellCounts[blockId] < 2)
                            continue;
                        if (host < 0 || cellCounts[blockId] > cellCounts[host])
                            host = blockId;
                    }

                    if (host < 0)
                        continue;

                    grid.SetForcedLandmark(host, palettes[i].requiredLandmark);
                    return;
                }
            }

            // Either the roll asked for a block or the street wall had nowhere to put one. If the
            // weighted roll already landed a Bank block this city, the promise is already kept.
            if (placed[bank] > 0)
                return;

            var block = SmallestFabricBlock(grid, BlockZone.Bank, order, cellCounts, adjacency);
            if (block < 0)
                return;

            grid.SetZone(block, BlockZone.Bank);
            placed[bank]++;
        }

        /// <summary>
        /// The smallest block still doing ordinary duty - residential or works - that the given
        /// zone could take over. -1 when the map has none left, which is a real outcome on a map
        /// small enough that every block is already spoken for.
        /// </summary>
        static int SmallestFabricBlock(
            CityGrid grid,
            BlockZone zone,
            int[] order,
            int[] cellCounts,
            List<int>[] adjacency)
        {
            var best = -1;
            foreach (var blockId in order)
            {
                var current = grid.ZoneOf(blockId);
                if (current != BlockZone.ResidentialHigh && current != BlockZone.Industrial)
                    continue;
                if (ClashesWithNeighbour(zone, blockId, adjacency, grid))
                    continue;
                if (best < 0 || cellCounts[blockId] < cellCounts[best])
                    best = blockId;
            }

            return best;
        }

        /// <summary>Zone counts, highest first - the one line that makes a generated layout checkable.</summary>
        public static string Describe(CityGrid grid)
        {
            var counts = new Dictionary<BlockZone, int>();
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var zone = grid.ZoneOf(blockId);
                counts.TryGetValue(zone, out var count);
                counts[zone] = count + 1;
            }

            var ordered = new List<KeyValuePair<BlockZone, int>>(counts);
            ordered.Sort((a, b) => b.Value != a.Value
                ? b.Value.CompareTo(a.Value)
                : a.Key.CompareTo(b.Key));

            var text = new StringBuilder();
            foreach (var entry in ordered)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(entry.Key).Append(' ').Append(entry.Value);
            }
            return text.ToString();
        }

        static bool IsSingleLandmark(BlockZone zone)
        {
            foreach (var candidate in SingleLandmarkZones)
                if (candidate == zone)
                    return true;
            return false;
        }

        /// <summary>
        /// Two parks side by side read as one big park with a road through it, which wastes the
        /// scarcest zone in the city. Nothing else cares who its neighbours are.
        /// </summary>
        static bool ClashesWithNeighbour(
            BlockZone zone, int blockId, List<int>[] adjacency, CityGrid grid)
        {
            if (zone != BlockZone.Park)
                return false;

            foreach (var neighbour in adjacency[blockId])
                if (grid.ZoneOf(neighbour) == BlockZone.Park)
                    return true;

            return false;
        }

        /// <summary>
        /// 0 at the centre of the map, 1 at the corner. Normalised against the half-diagonal so
        /// the value reaches 1 on a rectangular map rather than only on a square one.
        /// </summary>
        static float RadialPosition(Vector2 centroid, CityGrid grid)
        {
            var centre = new Vector2((grid.Width - 1) * 0.5f, (grid.Height - 1) * 0.5f);
            var halfDiagonal = centre.magnitude;
            return halfDiagonal <= 0f ? 0f : Mathf.Clamp01((centroid - centre).magnitude / halfDiagonal);
        }

        /// <summary>
        /// Multiplier on a zone's weight given how far out the block sits. Never returns zero:
        /// a factory near the centre should be unlikely, not impossible, or every seed produces
        /// the same tidy concentric city.
        /// </summary>
        static float RadialBias(BlockZone zone, float radial) => zone switch
        {
            // The two zones that fill the map, pulling against each other. Everything else -
            // the civic landmarks, the park, the car park - stays flat: a hospital is put where
            // there is room for one, not at a radius.
            BlockZone.ResidentialHigh => Mathf.Lerp(1.4f, 0.7f, radial),
            BlockZone.Industrial => Mathf.Lerp(0.2f, 1.8f, radial),
            _ => 1f,
        };

        /// <summary>
        /// Multiplier for a block that fronts the dual carriageway.
        ///
        /// A city's main avenue is lined with its tallest street wall - that is what makes it
        /// read as the main avenue rather than as a wide road that happens to be there. So the
        /// dense fabric is pulled onto it, and the three zones that would break the wall are
        /// pushed off: a works yard, a park's open grass and a car park's tarmac each leave a
        /// gap in the frontage precisely where the city should be at its densest.
        ///
        /// Multiplicative with RadialBias rather than replacing it, so the avenue does not
        /// cancel the radial gradient: a boulevard block out at the rim is still likelier to be
        /// industrial than one in the middle, just less likely than its neighbours off the
        /// avenue. And never zero, for the reason RadialBias gives - a park on the avenue should
        /// be uncommon, not forbidden, or every seed lines the same street the same way.
        /// </summary>
        static float BoulevardBias(BlockZone zone, bool onBoulevard)
        {
            if (!onBoulevard)
                return 1f;

            return zone switch
            {
                BlockZone.ResidentialHigh => 2.5f,
                BlockZone.Industrial => 0.4f,
                BlockZone.Park => 0.4f,
                BlockZone.Parking => 0.4f,

                // A walled compound breaks the avenue's street wall the same way a works does.
                BlockZone.Port => 0.4f,
                _ => 1f,
            };
        }

        /// <summary>
        /// True when any of the block's cells has the dual carriageway across one of its four
        /// sides. Cell-level rather than using the block's bounding box, so a block that only
        /// clips the avenue with one corner cell still counts as fronting it.
        /// </summary>
        static bool FacesMainRoad(CityGrid grid, int blockId)
        {
            foreach (var cell in grid.CellsInBlock(blockId))
                if (grid.IsMainRoad(cell.x, cell.y + 1) || grid.IsMainRoad(cell.x + 1, cell.y)
                    || grid.IsMainRoad(cell.x, cell.y - 1) || grid.IsMainRoad(cell.x - 1, cell.y))
                    return true;

            return false;
        }

        /// <summary>
        /// Turns every block touching one map side into Port. Whole blocks, not just the
        /// touching cells - a block is the atom of zoning - so a deep block on the waterfront
        /// becomes a deep compound, which is what a real port district is.
        /// </summary>
        static void ClaimPortSide(CityGrid grid, Sides side, HashSet<int> portBlocks)
        {
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var touches = false;
                foreach (var cell in grid.CellsInBlock(blockId))
                    if (side switch
                        {
                            Sides.South => cell.y == 0,
                            Sides.North => cell.y == grid.Height - 1,
                            Sides.West => cell.x == 0,
                            _ => cell.x == grid.Width - 1,
                        })
                    {
                        touches = true;
                        break;
                    }

                if (!touches)
                    continue;

                grid.SetZone(blockId, BlockZone.Port);
                portBlocks.Add(blockId);
            }
        }

        /// <summary>
        /// The side of the map the port claimed, or None on a landlocked seed. Derived from the
        /// finished zoning rather than stored, so a saved grid answers it as well as a fresh
        /// one: the port side is the side on which EVERY touching block is Port. Read by the
        /// two build passes to force each block's quay onto the city's one waterfront - a
        /// corner block has two map-edge sides and must not pick its own.
        /// </summary>
        public static Sides PortSideOf(CityGrid grid)
        {
            foreach (var side in new[] { Sides.South, Sides.East, Sides.North, Sides.West })
            {
                var any = false;
                var all = true;

                for (var blockId = 0; blockId < grid.BlockCount && all; blockId++)
                {
                    foreach (var cell in grid.CellsInBlock(blockId))
                        if (side switch
                            {
                                Sides.South => cell.y == 0,
                                Sides.North => cell.y == grid.Height - 1,
                                Sides.West => cell.x == 0,
                                _ => cell.x == grid.Width - 1,
                            })
                        {
                            any = true;
                            if (grid.ZoneOf(blockId) != BlockZone.Port)
                                all = false;
                            break;
                        }
                }

                if (any && all)
                    return side;
            }

            return Sides.None;
        }

        /// <summary>
        /// True when any of the block's cells lies on the outer ring of the grid. Cell-level
        /// like FacesMainRoad, and for the same reason: a block that only clips the boundary
        /// with one corner cell still has water beyond that cell.
        /// </summary>
        static bool TouchesMapEdge(CityGrid grid, int blockId)
        {
            foreach (var cell in grid.CellsInBlock(blockId))
                if (cell.x == 0 || cell.x == grid.Width - 1
                    || cell.y == 0 || cell.y == grid.Height - 1)
                    return true;

            return false;
        }

        /// <summary>
        /// The cell counts fall out of the same walk for free, and maxBlockCells needs them.
        /// </summary>
        static Vector2[] Centroids(CityGrid grid, out int[] cellCounts)
        {
            var sums = new Vector2[grid.BlockCount];
            var counts = cellCounts = new int[grid.BlockCount];

            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Height; z++)
            {
                var blockId = grid.BlockIdAt(x, z);
                if (blockId < 0) continue;

                sums[blockId] += new Vector2(x, z);
                counts[blockId]++;
            }

            for (var i = 0; i < sums.Length; i++)
                if (counts[i] > 0)
                    sums[i] /= counts[i];

            return sums;
        }

        /// <summary>
        /// Fisher-Yates over the block ids. Quotas are spent as blocks are assigned, so without
        /// this the scarce zones would all land in the low-numbered corner the flood fill
        /// happened to start from.
        /// </summary>
        static int[] ShuffledBlockOrder(int blockCount, System.Random rng)
        {
            var order = new int[blockCount];
            for (var i = 0; i < blockCount; i++)
                order[i] = i;

            for (var i = blockCount - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }
    }
}
