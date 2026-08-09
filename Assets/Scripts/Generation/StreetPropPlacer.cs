using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Dresses the band between the pavement and the block edge.
    ///
    /// Measured tile geometry (tile-road-straight, resolved through the prefab hierarchy), in
    /// the tile's own AUTHORED metres - every figure below is multiplied by CityGrid.TileScale
    /// when it is placed, and so is the tile:
    ///   driving lanes  x = +/-1.5
    ///   sidewalk paths x = +/-4
    ///   tile edge      x = +/-15
    /// So anything from about 5.5 outwards is clear of both the lanes and the walking route.
    ///
    /// That band used to be the road tile's grass verge. It is now surfaced end to end by
    /// GroundPlacer's apron, so these props stand on pavement - which is what a street lamp
    /// wants, and reads as a tree pit for the trees. Beside the park the apron is grass rather
    /// than concrete, so there the same props stand on a lawn that carries on past them into the
    /// park; a lamp on a park verge is right too, and the alternative was a 10m concrete ring
    /// that made the park look set back from its own street.
    /// </summary>
    public static class StreetPropPlacer
    {
        /// <summary>
        /// Distance from the road centreline to the prop line. Sits in the gap between the
        /// pavement (4 authored) and the building line (BlockBuilder.SidewalkClearance, 7), so
        /// lamps and trees stand between the kerb and the walls rather than inside either.
        ///
        /// Carries CityGrid.TileScale because all three of those do: the prop stands on a tile
        /// that was stretched, between a kerb and a wall that both moved out with it. The LAMP is
        /// not scaled - only where it stands is.
        /// </summary>
        const float VergeOffset = 5.5f * CityGrid.TileScale;

        /// <summary>
        /// The same line on the dual carriageway, whose cross-section is wider throughout:
        /// outer lanes at 4.75 and pavements at 7.25, against a street's 1.5 and 4. At 5.5 a
        /// lamp would stand in the avenue's outer LANE. 8.5 keeps the same relation - clear of
        /// the pavement, inside the building line at CityConfig.mainSidewalkWidth (10). All
        /// authored figures, scaled the same way as VergeOffset.
        /// </summary>
        const float MainVergeOffset = 8.5f * CityGrid.TileScale;

        /// <summary>
        /// The old lamp parity, kept ONLY so the prop pass can spend the samples it always spent
        /// - see the stub in Build. It places nothing. Changing it now changes where the trees
        /// stand and not one lamp.
        /// </summary>
        const int LampTileInterval = 2;

        /// <summary>
        /// Distance from a cell centre to the SEAM it shares with the next cell along the same
        /// street - where the lamps stand.
        ///
        /// Lamps used to sit one per cell centre, gated by (x + z) % 2. That parity is a
        /// checkerboard over the whole MAP, while CityGenerator.Subdivide is a BSP that gives
        /// blocks frontages of one to three cells starting at arbitrary coordinates - so the
        /// phase landed differently on every block. A one-cell frontage got a lamp or got none;
        /// a three-cell frontage got two or got one. Same street, same length, different
        /// lighting, which is what read as random.
        ///
        /// Seams have no phase to get wrong. Every cell boundary along a frontage carries a
        /// lamp, so the pitch is one cell everywhere and the run's own length sets nothing.
        /// It also closes the junction gap for free: a junction cell has road on all four sides
        /// and so is never dressed, but the runs either side of it each end ON their boundary
        /// with it, which leaves those two lamps exactly one cell apart - the same pitch as the
        /// rest of the street rather than double it.
        ///
        /// Already WORLD metres, because CityGrid.CellSize carries TileScale. Unlike
        /// VergeOffset and AlongOffsets above, this must NOT be scaled again.
        /// </summary>
        const float SeamOffset = CityGrid.CellSize * 0.5f;

        /// <summary>
        /// Prop slots along the kerb, from the cell centre. World distances along a tile that is
        /// placed at CityGrid.TileScale, so they carry it - otherwise the three slots bunch up in
        /// the middle of a stretched tile and leave its ends bare.
        /// </summary>
        static readonly float[] AlongOffsets =
        {
            -9f * CityGrid.TileScale, 0f, 9f * CityGrid.TileScale,
        };

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null,
            List<Bounds> gateKeepOuts = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;
            var rng = new System.Random(config.seed + SeedOffsets.Props);
            var isCarPark = CarParkTest(grid, prefabs);

            foreach (var cell in grid.RoadCells())
            {
                var centre = grid.CellToWorld(cell);

                foreach (var side in Sides4)
                {
                    if (!Dressed(grid, isCarPark, cell, side)) continue;

                    var outward = side.direction;
                    var along = new Vector3(-outward.z, 0f, outward.x);

                    // Props face back toward the road they serve.
                    var yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);

                    var verge = VergeFor(grid, cell);

                    for (var i = 0; i < AlongOffsets.Length; i++)
                    {
                        var position = centre + outward * verge + along * AlongOffsets[i];
                        var middle = i == 1;

                        // The lamps have moved to PlaceLamps and to their own stream. The DRAW
                        // they used to take here has not moved, and must not: Pick spends a
                        // sample even on the one-element lamp bag, and when this branch hit, the
                        // else-if chain short-circuited past the small-prop roll as well. Delete
                        // it and every middle slot in the city spends a different number of
                        // samples, walking every tree and prop sideways from the first lit cell
                        // on - so the before/after this change is meant to be judged by would
                        // show a relaid city rather than a relit one. The stub spends exactly
                        // what it always spent and stands nothing up. Same trick, same reason, as
                        // RoadNetworkBuilder's crosswalk roll.
                        //
                        // LampTileInterval survives only to shape this; it decides nothing about
                        // where a lamp stands any more.
                        if (middle && (cell.x + cell.y) % LampTileInterval == 0)
                        {
                            Pick(prefabs.streetLamps, rng);
                            continue;
                        }

                        // The middle slot is otherwise free now, but the tree roll is still asked
                        // only of the outer two: opening it to greenery would raise the kerb's
                        // tree count by half, and the job here was the lighting.
                        GameObject prefab = null;

                        if (!middle && rng.NextDouble() < 0.45)
                            prefab = Pick(prefabs.trees, rng);
                        // Up from 0.18 when smallProps was reweighted toward refuse. The bag went
                        // from 30% bins to 50%, so holding the chance would have HALVED the
                        // hydrants and lanterns to pay for the extra rubbish. At 0.24 the
                        // non-refuse rate is where it was (0.18 x 0.70 = 0.126 against
                        // 0.24 x 0.50 = 0.12) and the bins are the only thing that grew.
                        else if (rng.NextDouble() < 0.24)
                            prefab = Pick(prefabs.smallProps, rng);

                        if (!prefab) continue;

                        // A prop stands down for a gate: nothing between the kerb and a
                        // compound entrance, because a tree in front of a lorry gate blocks
                        // the one hole the wall has. Tested AFTER the draws so the rng stream
                        // is untouched - every other prop in the city stays exactly where it
                        // was, which keeps a before/after diff readable. Skipped, not nudged:
                        // a nudge under the 9m slot pitch just leans the tree on a gate pier.
                        if (Blocked(position, gateKeepOuts)) continue;

                        var instance = spawn(prefab, position, rotation, parent);
                        placed.Add(instance);
                    }
                }
            }

            PlaceLamps(grid, prefabs, isCarPark, config, parent, spawn, gateKeepOuts, placed);

            return placed;
        }

        /// <summary>
        /// Stands a lamp on every cell seam along every dressed frontage, once each.
        ///
        /// A seam belongs to two cells, and both reach it - one looking forward along the
        /// street, one looking back - so the pass keys each seam in GRID space and lets the
        /// first claimant win. Grid keys rather than rounded world positions on purpose: the
        /// two claimants compute the same seam from opposite directions, and a float epsilon
        /// is not something to rest "is this the same lamp" on.
        ///
        /// Runs after the prop loop and on its own rng stream (SeedOffsets.Lamps), so how many
        /// lamps the city has cannot move a single tree.
        /// </summary>
        static void PlaceLamps(
            CityGrid grid,
            PrefabDatabase prefabs,
            System.Func<int, int, bool> isCarPark,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> gateKeepOuts,
            List<GameObject> placed)
        {
            var rng = new System.Random(config.seed + SeedOffsets.Lamps);

            foreach (var site in LampSites(grid, isCarPark))
            {
                // Same rule as the props: a lamp stands down for a compound gate.
                if (Blocked(site.Position, gateKeepOuts)) continue;

                var prefab = Pick(prefabs.streetLamps, rng);
                if (!prefab) continue;

                var rotation = Quaternion.Euler(0f, site.Yaw, 0f);
                placed.Add(spawn(prefab, site.Position, rotation, parent));
            }
        }

        /// <summary>Where one lamp stands, and which way it looks.</summary>
        public readonly struct LampSite
        {
            public readonly Vector3 Position;
            public readonly float Yaw;

            public LampSite(Vector3 position, float yaw)
            {
                Position = position;
                Yaw = yaw;
            }
        }

        /// <summary>
        /// Every seam that gets a lamp, as pure geometry: no prefabs, no spawning, no rng.
        ///
        /// Split out from PlaceLamps so LampSpacingTests can assert the spacing invariant over
        /// real generated grids in a bare .NET host, which is the only place the "39m everywhere"
        /// claim can actually be checked. The car park test arrives as a predicate rather than a
        /// PrefabDatabase for the same reason - the database is a ScriptableObject and a test
        /// cannot build one.
        /// </summary>
        public static List<LampSite> LampSites(CityGrid grid, System.Func<int, int, bool> isCarPark)
        {
            var sites = new List<LampSite>();
            var seams = new HashSet<Vector3Int>();

            foreach (var cell in grid.RoadCells())
            {
                var centre = grid.CellToWorld(cell);

                for (var s = 0; s < Sides4.Length; s++)
                {
                    var side = Sides4[s];
                    if (!Dressed(grid, isCarPark, cell, side)) continue;

                    var outward = side.direction;
                    var along = new Vector3(-outward.z, 0f, outward.x);

                    // The grid step that matches `along`. CellToWorld scales both axes by
                    // CellSize, so this cell's +SeamOffset and the next cell's -SeamOffset land
                    // on the very same point - which is what makes the dedupe exact rather than
                    // approximate.
                    var alongStep = new Vector2Int(-side.step.y, side.step.x);

                    // The lamp faces back at the road it lights, like every other prop here.
                    var yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;

                    for (var end = -1; end <= 1; end += 2)
                    {
                        var other = cell + alongStep * end;

                        // Off the map means this seam IS the outline of the world. A road tile
                        // spans exactly +/-half a cell, so the seam is its outer edge - and the
                        // double lamp's two lanterns hang at local +/-3.10 ALONG the street
                        // (StreetLampLights.BulbOffsets), which would leave one of them and its
                        // pool hanging over nothing. Every street that runs to the boundary
                        // would show it. The cost is that the last half cell of an edge street
                        // is unlit, which is where the city stops anyway.
                        if (!grid.InBounds(other.x, other.y)) continue;

                        // Key on the lower cell of the pair so both claimants produce the same
                        // key whichever side they approach from.
                        var low = end < 0 ? other : cell;
                        if (!seams.Add(new Vector3Int(low.x, low.y, s))) continue;

                        // The boulevard's prop line stands further out than a street's, so a
                        // seam shared by two frontage cells has to clear both. Max, and only
                        // over cells that are on this same frontage: a run ends where the road
                        // turns or a crossing street cuts in, and that crossing may well be the
                        // boulevard - taking ITS verge would push a lamp on an ordinary street
                        // past the building line and into the block. Max over the pair is also
                        // symmetric, so both claimants agree without depending on which of them
                        // RoadCells() reaches first.
                        var verge = VergeFor(grid, cell);

                        if (grid.IsRoad(other.x, other.y) && Dressed(grid, isCarPark, other, side))
                            verge = Mathf.Max(verge, VergeFor(grid, other));

                        sites.Add(new LampSite(
                            centre + outward * verge + along * (SeamOffset * end), yaw));
                    }
                }
            }

            return sites;
        }

        static System.Func<int, int, bool> CarParkTest(CityGrid grid, PrefabDatabase prefabs) =>
            (x, z) => BlockBuilder.IsCarParkAt(grid, prefabs, x, z);

        /// <summary>
        /// Whether this edge of this cell is kerb worth dressing: it has to face a block, that
        /// block has to be on the map, and it must not be a car park.
        /// </summary>
        static bool Dressed(
            CityGrid grid, System.Func<int, int, bool> isCarPark, Vector2Int cell, Side side)
        {
            var neighbour = new Vector2Int(cell.x + side.step.x, cell.y + side.step.y);

            // Only dress edges that face a block - not edges facing another road.
            if (grid.IsRoad(neighbour.x, neighbour.y)) return false;
            if (!grid.InBounds(neighbour.x, neighbour.y)) return false;

            // A car park is fenced at the pavement edge (BlockBuilder.ClearanceFor), so the prop
            // line at 5.5 is INSIDE its lot - a lamp here would stand among the parked cars and a
            // tree would grow through the tarmac. Its own scatter dresses the apron instead.
            return !isCarPark(neighbour.x, neighbour.y);
        }

        static float VergeFor(CityGrid grid, Vector2Int cell) =>
            grid.IsMainRoad(cell.x, cell.y) ? MainVergeOffset : VergeOffset;

        static GameObject Pick(GameObject[] options, System.Random rng) =>
            options == null || options.Length == 0 ? null : options[rng.Next(options.Length)];

        static bool Blocked(Vector3 position, List<Bounds> keepOuts)
        {
            if (keepOuts == null)
                return false;

            foreach (var keepOut in keepOuts)
                if (keepOut.Contains(position))
                    return true;

            return false;
        }

        struct Side
        {
            public Vector2Int step;
            public Vector3 direction;
        }

        static readonly Side[] Sides4 =
        {
            new() { step = new Vector2Int(0, 1), direction = Vector3.forward },
            new() { step = new Vector2Int(1, 0), direction = Vector3.right },
            new() { step = new Vector2Int(0, -1), direction = Vector3.back },
            new() { step = new Vector2Int(-1, 0), direction = Vector3.left },
        };
    }
}
