using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>
    /// Stands a residential block up from the division <see cref="ResidentialLot"/> made:
    /// the units on their cells, the ground under everything that is not a building, and
    /// what belongs on that ground.
    ///
    /// The same bargain as the park and the industrial parcel: one delegate says how a
    /// prefab is raised, so the editor gets linked instances and the game plain ones, and
    /// nothing here knows which called it.
    /// </summary>
    public static class ResidentialBlocks
    {
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string Units = "Assets/Prefabs/Residential/";

        const string Kerb = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string KerbCorner = CityEnv + "SM_Env_Sidewalk_Corner_01.prefab";
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Grass = CityEnv + "SM_Env_Grass_01.prefab";
        const string Bare = CityEnv + "SM_Env_Road_Bare_01.prefab";
        const string Bays = CityEnv + "SM_Env_Road_ParkingLines_01.prefab";
        const string Arrow = CityEnv + "SM_Env_Road_Arrow_01.prefab";
        const string Lamp = CityProps + "SM_Prop_LightPole_Base_01.prefab";
        const string FencePanel = CityEnv + "SM_Env_Fence_01.prefab";
        const string FencePost = CityEnv + "SM_Env_Fence_End_01.prefab";
        const string Bin = CityProps + "SM_Prop_Trashbin_01.prefab";

        static readonly string[] Trees =
        {
            CityEnv + "SM_Env_Tree_01.prefab",
            CityEnv + "SM_Env_Tree_02.prefab",
            CityEnv + "SM_Env_Tree_03.prefab",
        };
        static readonly string[] Skips =
        {
            CityProps + "SM_Prop_Skip_01.prefab",
            CityProps + "SM_Prop_Skip_02.prefab",
        };
        static readonly string[] Litter =
        {
            CityProps + "SM_Prop_TrashBag_01.prefab",
            CityProps + "SM_Prop_TrashBag_03.prefab",
            CityProps + "SM_Prop_CardboardBox_01.prefab",
            CityProps + "SM_Prop_CardboardBox_04.prefab",
        };
        static readonly string[] Washing =
        {
            CityProps + "SM_Prop_Washingline_01.prefab",
            CityProps + "SM_Prop_Washingline_02.prefab",
            CityProps + "SM_Prop_Washingline_03.prefab",
        };

        /// <summary>The height the pack's own paving stands at, so a tile laid on a cell
        /// meets the tiles the blocks were baked with rather than z-fighting them.</summary>
        const float Deck = 0.054f;

        /// <summary>Lamps go a cell in from the corner and then every four cells - 20 m -
        /// which is the rhythm measured off the demo (Docs/synty-demo-anatomy.md).</summary>
        const int LampEvery = 4;

        public sealed class Stood
        {
            public int Units, Tiles, Props, Lamps, Trees, Fence;
            public int Missing;
            public string Refused = "";
            public List<string> Absent = new List<string>();

            public override string ToString() =>
                $"{Units} unit(s), {Tiles} tile(s), {Props} prop(s) ({Trees} tree(s), {Lamps} lamp(s), " +
                $"{Fence} m of fence)" +
                (Absent.Count > 0 ? $", MISSING {string.Join(", ", Absent)}" : "") +
                (Refused.Length > 0 ? $", refused {Refused}" : "");
        }

        /// <summary>
        /// Stands the plan up under <paramref name="root"/>, with the block's south-west
        /// corner at the root's own origin. Composed at the origin and moved afterwards is
        /// the caller's business, as it is for the industrial quarter.
        /// </summary>
        public static Stood Compose(ResidentialLot.Plan plan, Transform root, System.Random rng,
                                    System.Func<GameObject, Transform, GameObject> raise)
        {
            Begin(raise);
            ForgetMissing();
            var stood = new Stood();

            Ground(plan, root, rng, stood);
            Stand(plan, root, stood);
            Dress(plan, root, rng, stood);

            stood.Absent.AddRange(Missing);
            stood.Missing = Missing.Count;
            stood.Refused = Worst();
            return stood;
        }

        // ------------------------------------------------------------------ the ground

        static void Ground(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var laid = new bool[plan.W, plan.D];   // the cells a bay pair has already covered
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (laid[i, j]) continue;
                    string tile = null;
                    float yaw = 0f;
                    switch (plan.Ground[i, j])
                    {
                        case ResidentialLot.Use.Walkway:
                            Pavement(plan, root, i, j, stood);
                            continue;
                        case ResidentialLot.Use.Building:
                            // nothing under a building: the unit stands at the level the pack
                            // gave it, and half of these have a basement below zero
                            continue;
                        case ResidentialLot.Use.Forecourt:
                            // the sunken garden keeps its own floor, cut into the ground -
                            // a slab laid over it would roof the area steps lead down into
                            if (Sunk(plan, i, j)) continue;
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Yard:
                        case ResidentialLot.Use.Court:
                            tile = Grass;
                            break;
                        case ResidentialLot.Use.Paved:
                            // a gap in the row is paved, never grass (the user's call)
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Verge:
                            Verge(plan, root, i, j, stood);
                            continue;
                        case ResidentialLot.Use.Drive:
                        case ResidentialLot.Use.Alley:
                            tile = Bare;
                            break;
                        case ResidentialLot.Use.Parking:
                            // the pack's painted bays are TEN metres by five - the one tile in
                            // the kit that is not a cell - so they are stood on a PAIR of
                            // cells at their own size, as IndustrialBlocks.Bay stands them,
                            // and never squeezed into one. An odd cell at the end of a run
                            // gets bare asphalt, not half a bay.
                            if (Bay(plan, root, laid, i, j, stood)) continue;
                            tile = Bare;
                            break;
                        default:
                            continue;                       // Empty: nothing invented here
                    }
                    if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
                }
        }

        /// <summary>A pair of painted bays over this cell and the next parking cell along -
        /// north of it first, else east of it. False when neither neighbour is parking.</summary>
        static bool Bay(ResidentialLot.Plan plan, Transform root, bool[,] laid, int i, int j, Stood stood)
        {
            bool Free(int x, int y) => x < plan.W && y < plan.D && !laid[x, y] &&
                                       plan.Ground[x, y] == ResidentialLot.Use.Parking;
            float x0 = i * ResidentialLot.Cell, z0 = j * ResidentialLot.Cell;
            if (Free(i, j + 1))
            {
                if (Lay(Bays, root, x0, z0, ResidentialLot.Cell, ResidentialLot.Cell * 2f, 90f) != null) stood.Tiles++;
                laid[i, j] = laid[i, j + 1] = true;
                return true;
            }
            if (Free(i + 1, j))
            {
                if (Lay(Bays, root, x0, z0, ResidentialLot.Cell * 2f, ResidentialLot.Cell, 0f) != null) stood.Tiles++;
                laid[i, j] = laid[i + 1, j] = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The pavement inside the block, where it edges a way cars use: paving, and a kerb
        /// along every side that faces the tarmac.
        ///
        /// This is the user's rule of 2026-08-26 made of tiles - a way put in between the
        /// houses is kerbed on every side, and a car never crosses a pavement to reach it.
        /// </summary>
        static void Verge(ResidentialLot.Plan plan, Transform root, int i, int j, Stood stood)
        {
            // ONE TILE A CELL. The kerb tile is the whole pavement tile with a kerb along one
            // edge (which is why CorePavement lays nothing under it), so a cell that edges
            // tarmac gets the kerb tile and NOT the plain paving as well - the two are
            // coplanar and flicker. Two adjacent sides on tarmac take the corner tile, which
            // wraps one corner of its cell the way the block's own ring turns it; a cell with
            // tarmac on opposite sides has no tile in the kit and keeps the kerb on the first.
            bool[] drives = new bool[4];
            int count = 0;
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                if (!ResidentialLot.Drives(plan.Ground[x, y])) continue;
                drives[side] = true;
                count++;
            }

            string tile = Paving;
            float yaw = 0f;
            if (count == 2 && (drives[0] != drives[2]))
            {
                // sides: 0 south, 1 east, 2 north, 3 west; the corner tile: NE 0, SE 90, SW 180, NW 270
                tile = KerbCorner;
                yaw = drives[2] && drives[1] ? 0f : drives[0] && drives[1] ? 90f : drives[0] ? 180f : 270f;
            }
            else if (count > 0)
            {
                tile = Kerb;
                // the kerb faces the road, the same way the block's own ring faces the street
                int first = drives[0] ? 0 : drives[1] ? 1 : drives[2] ? 2 : 3;
                yaw = first switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };
            }
            if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
        }

        /// <summary>The kerb ring: one tile a cell, turned to face OUT, with corner tiles on
        /// the corners - the rule measured off all sixteen harvested blocks.</summary>
        static void Pavement(ResidentialLot.Plan plan, Transform root, int i, int j, Stood stood)
        {
            bool west = i == 0, east = i == plan.W - 1, south = j == 0, north = j == plan.D - 1;
            string tile;
            float yaw;
            if ((west || east) && (south || north))
            {
                tile = KerbCorner;
                // the corner tile wraps one corner of its cell: NE 0, SE 90, SW 180, NW 270
                yaw = north && east ? 0f : south && east ? 90f : south ? 180f : 270f;
            }
            else
            {
                tile = Kerb;
                yaw = north ? 0f : east ? 90f : south ? 180f : 270f;
            }
            if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
        }

        static bool Sunk(ResidentialLot.Plan plan, int i, int j)
        {
            foreach (var spot in plan.Spots)
            {
                var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                int u = i - spot.I, v = j - spot.J;
                if (u < 0 || v < 0 || u >= turn.CW || v >= turn.CD) continue;
                if (turn.Pit(u, v)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the units

        /// <summary>
        /// A unit on its cells.
        ///
        /// It is placed by ARITHMETIC, not by measuring the instance: a unit's prefab was
        /// baked with its footprint running from its own origin, so where the turned
        /// footprint starts is known exactly - and measuring the instance instead would take
        /// the fire escapes and the eaves into the reckoning and set the building a metre
        /// up the street.
        /// </summary>
        static void Stand(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            foreach (var spot in plan.Spots)
            {
                var asset = DemoAssetLoad.Load<GameObject>($"{Units}{spot.Unit.Name}.prefab");
                if (asset == null)
                {
                    if (!stood.Absent.Contains(spot.Unit.Name)) stood.Absent.Add(spot.Unit.Name);
                    continue;
                }
                var go = Raise($"{Units}{spot.Unit.Name}.prefab", root);
                if (go == null) continue;

                float w = spot.Unit.CW * ResidentialLot.Cell, d = spot.Unit.CD * ResidentialLot.Cell;
                float x = spot.I * ResidentialLot.Cell, z = spot.J * ResidentialLot.Cell;
                var offset = spot.Yaw switch
                {
                    90 => new Vector3(0f, 0f, w),
                    180 => new Vector3(w, 0f, d),
                    270 => new Vector3(d, 0f, 0f),
                    _ => Vector3.zero,
                };
                go.transform.SetPositionAndRotation(new Vector3(x, 0f, z) + offset,
                                                    Quaternion.Euler(0f, spot.Yaw, 0f));
                go.name = $"{spot.Unit.Name} ({spot.I},{spot.J}) {spot.Yaw}";
                stood.Units++;
            }
        }

        // ------------------------------------------------------------------ what is on it

        static void Dress(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            float cell = ResidentialLot.Cell;

            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
                    switch (plan.Ground[i, j])
                    {
                        case ResidentialLot.Use.Court:
                            // a tree or two on the grass, never on a line - the demo sets
                            // them half a metre off the raster in loose pairs
                            if (!Chance(rng, 0.55)) break;
                            if (Prop(Any(Trees, rng), root,
                                     x + Between(rng, -1.2f, 1.2f), z + Between(rng, -1.2f, 1.2f),
                                     rng.Next(4) * 90f, 1.1f, Deck) != null)
                            { stood.Props++; stood.Trees++; }
                            break;

                        case ResidentialLot.Use.Alley:
                            // bins against the backs of the houses, where the alley meets them
                            if (Chance(rng, 0.45) &&
                                Prop(Any(Skips, rng), root, x + Between(rng, -1f, 1f), z + Between(rng, -1f, 1f),
                                     rng.Next(2) * 90f, 1f, Deck) != null) stood.Props++;
                            if (Chance(rng, 0.35) &&
                                Prop(Any(Litter, rng), root, x + Between(rng, -1.8f, 1.8f), z + Between(rng, -1.8f, 1.8f),
                                     Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
                            break;

                        case ResidentialLot.Use.Yard:
                            if (Chance(rng, 0.25) &&
                                Prop(Any(Washing, rng), root, x + Between(rng, -1f, 1f), z + Between(rng, -1f, 1f),
                                     Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
                            break;
                    }
                }

            Lamps(plan, root, stood);
            Fences(plan, root, rng, stood);
        }

        /// <summary>
        /// The back fence: where a yard meets the alley, and where a yard meets a garden the
        /// street can see into.
        ///
        /// Without it the middle of a block reads as one lawn with houses round it. What is
        /// actually back there is a run of fenced yards with an alley between them, and the
        /// fence is the thing that says whose ground is whose - which matters in a game
        /// about who owns what.
        /// </summary>
        static void Fences(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var rails = new GameObject("Fence").transform;
            rails.SetParent(root, false);
            float cell = ResidentialLot.Cell;
            float thick = Mathf.Max(0.1f, Box(FencePanel).size.z);

            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Yard) continue;
                    for (int side = 0; side < 4; side++)
                    {
                        int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        var beyond = plan.Ground[x, y];
                        // the fence is the property line, so it stands between the yard and
                        // the PAVEMENT - the verge along the alley, or the block's own ring.
                        // Against the tarmac itself there is a kerb, and a fence on a kerb is
                        // a fence in the gutter
                        bool wanted = beyond == ResidentialLot.Use.Verge ||
                                      beyond == ResidentialLot.Use.Walkway;
                        if (!wanted) continue;

                        bool along = side == 0 || side == 2;
                        float line = side == 0 ? j * cell : side == 2 ? (j + 1) * cell
                                   : side == 3 ? i * cell : (i + 1) * cell;
                        float px = (along ? i * cell : line) - (along ? 0f : thick * 0.5f);
                        float pz = (along ? line : j * cell) - (along ? thick * 0.5f : 0f);
                        if (Lay(FencePanel, rails, px, pz, along ? cell : thick,
                                along ? thick : cell, along ? 0f : 90f) != null)
                            stood.Fence += (int)cell;
                        Sit(FencePost, rails, along ? i * cell : line, along ? line : j * cell, 0f);
                    }

                    // and what a back yard has in it besides grass
                    float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
                    if (Chance(rng, 0.3) &&
                        Prop(Bin, rails, cx + Between(rng, -1.6f, 1.6f), cz + Between(rng, -1.6f, 1.6f),
                             Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
                    if (Chance(rng, 0.2) &&
                        Prop(Any(Litter, rng), rails, cx + Between(rng, -1.8f, 1.8f), cz + Between(rng, -1.8f, 1.8f),
                             Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
                }
        }

        /// <summary>Street lamps on the kerb, a cell in from the corner and every 20 m after
        /// - and only on sides that have a street.</summary>
        static void Lamps(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            float cell = ResidentialLot.Cell, lane = 1.0f;
            for (int side = 0; side < 4; side++)
            {
                if (!plan.Street[side]) continue;
                bool alongX = side == 0 || side == 2;
                int length = alongX ? plan.W : plan.D;

                for (int at = 1; at < length - 1; at += LampEvery)
                {
                    int i = alongX ? at : side == 1 ? plan.W - 1 : 0;
                    int j = alongX ? (side == 2 ? plan.D - 1 : 0) : at;
                    float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
                    // a lamp stands a metre in from the kerb's outer edge, arm over the road
                    x += side == 1 ? -lane : side == 3 ? lane : 0f;
                    z += side == 2 ? -lane : side == 0 ? lane : 0f;
                    float yaw = side switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };
                    if (Prop(Lamp, root, x, z, yaw, 0.4f, Deck) != null) { stood.Props++; stood.Lamps++; }
                }
            }
        }

        /// <summary>An arrow where the alley meets the street, the way the demo marks its
        /// one-way alleys.</summary>
        public static void Mouths(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Drive) continue;
                    bool nextToAlley = false;
                    for (int s = 0; s < 4; s++)
                    {
                        int x = i + ResidentialLot.Step[s, 0], y = j + ResidentialLot.Step[s, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        if (plan.Ground[x, y] == ResidentialLot.Use.Alley) nextToAlley = true;
                    }
                    if (!nextToAlley) continue;
                    if (Tile(Arrow, root, i, j, 0f) != null) stood.Tiles++;
                }
        }
    }
}
