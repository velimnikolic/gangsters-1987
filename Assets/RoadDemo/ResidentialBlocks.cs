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
    ///
    /// The ground is concrete and the trees are palms on the pavement - the user's word on
    /// the first drawing (2026-08-27: "zajebi travu izmedju, ocu samo beton i palme po
    /// trotoaru"). ONE TILE A CELL, always: two coplanar tiles flicker through each other.
    /// </summary>
    public static class ResidentialBlocks
    {
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string Units = "Assets/Prefabs/Residential/";
        const string KitBld = "Assets/CityKit/Buildings/";

        const string Kerb = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string KerbCorner = CityEnv + "SM_Env_Sidewalk_Corner_01.prefab";
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Bare = CityEnv + "SM_Env_Road_Bare_01.prefab";
        const string Bays = CityEnv + "SM_Env_Road_ParkingLines_01.prefab";
        const string Arrow = CityEnv + "SM_Env_Road_Arrow_01.prefab";
        const string Lamp = CityProps + "SM_Prop_LightPole_Base_01.prefab";

        /// <summary>The patio beside the storefront: the pier's own tables and chairs
        /// (QuayBlocks), a bench against the back line.</summary>
        const string CoffeeProps = "Assets/Synty/PolygonCoffeeShop/Prefabs/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string CafeTable = CityProps + "SM_Prop_Table_02.prefab";
        const string CafeChair = CoffeeProps + "SM_Prop_Chair_01.prefab";
        const string Umbrella = PalmProps + "SM_Prop_Umbrella_02.prefab";
        const string ParkBench = CityProps + "SM_Prop_ParkBench_01.prefab";
        const float TableAlong = 2.6f, TableRows = 3.2f;

        /// <summary>The kit storefronts, by the length of the gap they stand in: the coffee
        /// shop (5.8 x 7.2 m) in two or three cells, a diner (16.3 x 8.8 m) in four.</summary>
        const string CoffeeShop = KitBld + "building-coffeeshop.prefab";
        static readonly string[] Diners =
        {
            KitBld + "building-diner.prefab",
            KitBld + "building-burger-joint.prefab",
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
        /// <summary>The height the pack's own paving stands at, so a prop set on a tile
        /// stands on the tile rather than in it.</summary>
        const float Deck = 0.054f;

        /// <summary>Lamps go a cell in from the corner and then every four cells - 20 m -
        /// which is the rhythm measured off the demo (Docs/synty-demo-anatomy.md).</summary>
        const int LampEvery = 4;

        /// <summary>A storefront whose floor goes deeper than this below zero has a sunken
        /// floor of its own (the diner: -1.56 m), and no tile is laid where it stands.</summary>
        const float SunkFloor = -0.5f;

        /// <summary>One painted stall: where its middle is and which way a car noses into
        /// it - away from the aisle.</summary>
        readonly struct Stall
        {
            public readonly Vector3 At;
            public readonly int Into;
            public Stall(Vector3 at, int into) { At = at; Into = into; }
        }

        /// <summary>How many of the stalls have a car in them. One in two is what the pack's
        /// own demo does and what the industrial yards do; a lot of freshly painted empty
        /// bays is a car park nobody uses.</summary>
        const double Parked = 0.5;

        public sealed class Stood
        {
            public int Units, Tiles, Props, Lamps, Palms, Stalls, Cars, Tables, Benches;
            public int Missing;
            public string Cafe = "";
            public string Refused = "";
            public List<string> Absent = new List<string>();

            public override string ToString() =>
                $"{Units} unit(s), {Tiles} tile(s), {Props} prop(s) ({Palms} palm(s), {Lamps} lamp(s), " +
                $"{Tables} table(s), {Benches} bench(es)), {Cars} car(s) in {Stalls} stall(s)" +
                (Cafe.Length > 0 ? $", {Cafe}" : "") +
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

            // the storefront is chosen before the ground, which is laid round its foot
            var cafe = CafeOf(plan, rng, stood);
            var kerbs = new List<CorePavement.Kerbstone>();
            var stalls = new List<Stall>();

            Ground(plan, root, cafe, kerbs, stalls, stood);
            Stand(plan, root, stood);
            if (cafe != null && CafeStand(cafe, root, stood)) Patio(plan, cafe, root, rng, stood);
            Cars(stalls, root, rng, raise, stood);
            Dress(plan, root, rng, stood);

            var standing = new List<Vector3>();
            Lamps(plan, root, standing, stood);
            Palms(kerbs, standing, root, raise, rng.Next(), stood);

            stood.Absent.AddRange(Missing);
            stood.Missing = Missing.Count;
            stood.Refused = Worst();
            return stood;
        }

        // ------------------------------------------------------------------ the ground

        static void Ground(ResidentialLot.Plan plan, Transform root, CafeSpot cafe,
                           List<CorePavement.Kerbstone> kerbs, List<Stall> stalls, Stood stood)
        {
            var laid = new bool[plan.W, plan.D];   // the cells a bay pair has already covered
            float cell = ResidentialLot.Cell;
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (laid[i, j]) continue;
                    string tile;
                    float yaw = 0f;
                    switch (plan.Ground[i, j])
                    {
                        case ResidentialLot.Use.Walkway:
                            Pavement(plan, root, i, j, kerbs, stood);
                            continue;
                        case ResidentialLot.Use.Building:
                        case ResidentialLot.Use.Forecourt:
                            // nothing under a unit or on its own ground: it stands at the
                            // level the pack gave it, and half of these drop below zero -
                            // the brownstone's whole footprint is at -1.5 m, and the garden
                            // inside its L falls into that pit
                            continue;
                        case ResidentialLot.Use.Yard:
                        case ResidentialLot.Use.Court:
                        case ResidentialLot.Use.Paved:
                            // concrete, never grass (the user's call, 2026-08-27)
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Cafe:
                            // the storefront with a sunken floor of its own gets no tile
                            // under its foot; the paved ground round it is paved
                            // any part of the foot in the cell, not just its middle: a 16 m
                            // diner in a 20 m gap runs a metre of sunken wall into the
                            // fourth cell, and a slab laid over that roofs it
                            if (cafe != null && cafe.Sunk &&
                                cafe.Foot.Overlaps(new Rect(i * cell, j * cell, cell, cell))) continue;
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Verge:
                            Verge(plan, root, i, j, stood);
                            continue;
                        case ResidentialLot.Use.Drive:
                            tile = MouthArrow(plan, i, j, out yaw) ? Arrow : Bare;
                            break;
                        case ResidentialLot.Use.Alley:
                            tile = Bare;
                            break;
                        case ResidentialLot.Use.Parking:
                            if (Bay(plan, root, laid, i, j, stalls, stood)) continue;
                            tile = Bare;
                            break;
                        default:
                            continue;                       // Empty: nothing invented here
                    }
                    if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
                }
        }

        /// <summary>
        /// Is this cell the alley's mouth - a ring cell cut for it - and which way does the
        /// alley run? The mouth is ONE tile, the arrow: the arrow tile is a whole road tile
        /// with the arrow painted on, and the bare tile that used to lie under it flickered
        /// through (the user, 2026-08-27: "izbegavaj dupli layering"). The alley runs one
        /// way, west to east or south to north, and the arrow at each mouth points the way
        /// it runs, as the core marks its lanes.
        /// </summary>
        static bool MouthArrow(ResidentialLot.Plan plan, int i, int j, out float yaw)
        {
            yaw = 0f;
            bool ring = i == 0 || j == 0 || i == plan.W - 1 || j == plan.D - 1;
            if (!ring) return false;
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                if (plan.Ground[x, y] != ResidentialLot.Use.Alley) continue;
                // the alley lies north or south of the mouth: it runs north-south, +z
                yaw = side == 0 || side == 2 ? 0f : 90f;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A pair of painted bays, ten metres along the row and five deep, nosed to the
        /// aisle.
        ///
        /// The tile's four lines run across its five-metre depth, so tiles laid ALONG the
        /// aisle make a row of bays, and tiles stacked away from it make stripes the length
        /// of the lot - which is what the first car park was (the user, 2026-08-27: "zna se
        /// kako parking izgleda"). The row runs square to the aisle this cell touches; an odd
        /// cell at the end of a row is bare asphalt, not half a bay.
        /// </summary>
        static bool Bay(ResidentialLot.Plan plan, Transform root, bool[,] laid, int i, int j,
                        List<Stall> stalls, Stood stood)
        {
            bool Is(int x, int y, ResidentialLot.Use use) =>
                x >= 0 && y >= 0 && x < plan.W && y < plan.D && plan.Ground[x, y] == use;
            bool Free(int x, int y) => Is(x, y, ResidentialLot.Use.Parking) && !laid[x, y];
            bool Aisle(int x, int y) => Is(x, y, ResidentialLot.Use.Drive);

            float cell = ResidentialLot.Cell;
            float x0 = i * cell, z0 = j * cell;
            const int perTile = 3;                      // the tile's three stalls, 3.33 m each
            float pitch = cell * 2f / perTile;
            if ((Aisle(i, j - 1) || Aisle(i, j + 1)) && Free(i + 1, j))
            {
                // the aisle north or south: the row runs east-west, the lines north-south,
                // and a car noses away from the aisle
                if (Lay(Bays, root, x0, z0, cell * 2f, cell, 0f) != null) stood.Tiles++;
                laid[i, j] = laid[i + 1, j] = true;
                int into = Aisle(i, j + 1) ? 180 : 0;
                for (int n = 0; n < perTile; n++)
                    stalls.Add(new Stall(new Vector3(x0 + (n + 0.5f) * pitch, 0f, z0 + cell * 0.5f), into));
                return true;
            }
            if ((Aisle(i - 1, j) || Aisle(i + 1, j)) && Free(i, j + 1))
            {
                if (Lay(Bays, root, x0, z0, cell, cell * 2f, 90f) != null) stood.Tiles++;
                laid[i, j] = laid[i, j + 1] = true;
                int into = Aisle(i + 1, j) ? 270 : 90;
                for (int n = 0; n < perTile; n++)
                    stalls.Add(new Stall(new Vector3(x0 + cell * 0.5f, 0f, z0 + (n + 0.5f) * pitch), into));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The cars in the stalls: one stall in two (<see cref="Parked"/>), nosed in, out of
        /// the pool the core's own car parks draw on (<see cref="CoreRoads.PickCar"/> - the
        /// catalogue's road cars, the wrong decade and the marked liveries left out), stood
        /// the way the core stands them (<see cref="CoreRoads.InBay"/>).
        /// </summary>
        static void Cars(List<Stall> stalls, Transform root, System.Random rng,
                         System.Func<GameObject, Transform, GameObject> raise, Stood stood)
        {
            stood.Stalls = stalls.Count;
            if (stalls.Count == 0) return;
            var under = new GameObject("Parked").transform;
            under.SetParent(root, false);
            foreach (var stall in stalls)
            {
                if (!Chance(rng, Parked)) continue;
                var prefab = CoreRoads.PickCar(rng);
                if (prefab == null) return;
                var car = raise(prefab, under);
                if (car == null) continue;
                car.transform.SetPositionAndRotation(stall.At, Quaternion.Euler(0f, stall.Into, 0f));
                CoreRoads.InBay(car, stall.At, stall.Into, ResidentialLot.Cell);
                stood.Cars++;
            }
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
        /// the corners - the rule measured off all sixteen harvested blocks. Every kerb laid
        /// is remembered, because the palms are planted on them afterwards.</summary>
        static void Pavement(ResidentialLot.Plan plan, Transform root, int i, int j,
                             List<CorePavement.Kerbstone> kerbs, Stood stood)
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
            if (Tile(tile, root, i, j, yaw) == null) return;
            stood.Tiles++;
            float cell = ResidentialLot.Cell;
            kerbs.Add(new CorePavement.Kerbstone(new Vector3((i + 0.5f) * cell, 0f, (j + 0.5f) * cell), yaw));
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

        // ------------------------------------------------------------------ the cafe

        sealed class CafeSpot
        {
            public string Path;
            public float X, Z, Yaw;
            public Rect Foot;
            public bool Sunk;
        }

        /// <summary>
        /// The kit storefront in the gap the plan kept for it: the coffee shop in a gap of
        /// two or three cells, a diner in four, turned to face its street with its front a
        /// step in from the pavement line. The front is MEASURED off the mesh
        /// (<see cref="Composer.FrontYaw"/>) - the coffee shop's is +x, the diner's +z, and
        /// a storefront stood by its file would show the street its kitchen.
        /// </summary>
        static CafeSpot CafeOf(ResidentialLot.Plan plan, System.Random rng, Stood stood)
        {
            var gap = plan.Cafe;
            if (gap == null) return null;
            string path = gap.Run >= 4 ? Any(Diners, rng) : CoffeeShop;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);

            float cell = ResidentialLot.Cell;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Cafe) continue;
                    minX = Mathf.Min(minX, i * cell); maxX = Mathf.Max(maxX, (i + 1) * cell);
                    minZ = Mathf.Min(minZ, j * cell); maxZ = Mathf.Max(maxZ, (j + 1) * cell);
                }
            if (minX > maxX) return null;

            // FrontYaw turns the front to +x; the street lies -z, +x, +z, -x of the gap, and a
            // yaw is a CLOCKWISE turn from above (+x goes to -z at 90, -x at 180, +z at 270)
            float yaw = FrontYaw(path) + gap.Side switch { 0 => 90f, 1 => 0f, 2 => 270f, _ => 180f };
            var foot = Foot(path, yaw);
            if (foot.x > maxX - minX + 0.01f || foot.y > maxZ - minZ + 0.01f)
            {
                stood.Cafe = $"no {name}: {foot.x:0.0} x {foot.y:0.0} m does not fit the " +
                             $"{maxX - minX:0} x {maxZ - minZ:0} m gap";
                return null;
            }

            const float step = 0.3f;    // the front a step in from the pavement line
            // at one END of the gap, not its middle: centred, a 5.8 m shop in a 10 m gap
            // leaves 2 m either side and no room for a table on either; at the end it leaves
            // one patio of 4 m, which seats a row. Which end is the seed's
            bool alongX = gap.Side == 0 || gap.Side == 2;
            bool low = rng.Next(2) == 0;
            const float flank = 0.1f;   // a hand off the neighbour's wall: the patio wants every centimetre
            float x = alongX ? (low ? minX + flank + foot.x * 0.5f : maxX - flank - foot.x * 0.5f)
                             : (minX + maxX) * 0.5f;
            float z = alongX ? (minZ + maxZ) * 0.5f
                             : (low ? minZ + flank + foot.y * 0.5f : maxZ - flank - foot.y * 0.5f);
            switch (gap.Side)
            {
                case 0: z = minZ + step + foot.y * 0.5f; break;
                case 2: z = maxZ - step - foot.y * 0.5f; break;
                case 1: x = maxX - step - foot.x * 0.5f; break;
                default: x = minX + step + foot.x * 0.5f; break;
            }
            return new CafeSpot
            {
                Path = path, X = x, Z = z, Yaw = yaw,
                Foot = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y),
                Sunk = Box(path).min.y < SunkFloor,
            };
        }

        static bool CafeStand(CafeSpot cafe, Transform root, Stood stood)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(cafe.Path);
            var go = Building(cafe.Path, root, cafe.X, cafe.Z, cafe.Yaw, 1f);
            if (go == null) { stood.Cafe = $"no {name}: no room"; return false; }
            go.name = $"{name} (cafe)";
            stood.Cafe = name;
            return true;
        }

        /// <summary>
        /// The patio: what the storefront's gap has left beside it, set with the pier's own
        /// tables and chairs (the user, 2026-08-27: "patio pored kafica bi mogao da ima
        /// stolove i klupe"), a bench along its back line facing the street. Everything is
        /// booked, so nothing stands in the storefront's foot or in another table's room;
        /// what does not fit is refused and counted, never crammed.
        /// </summary>
        static void Patio(ResidentialLot.Plan plan, CafeSpot cafe, Transform root, System.Random rng, Stood stood)
        {
            var gap = plan.Cafe;
            if (gap == null) return;
            var pen = new GameObject("patio").transform;
            pen.SetParent(root, false);

            float cell = ResidentialLot.Cell;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Cafe) continue;
                    minX = Mathf.Min(minX, i * cell); maxX = Mathf.Max(maxX, (i + 1) * cell);
                    minZ = Mathf.Min(minZ, j * cell); maxZ = Mathf.Max(maxZ, (j + 1) * cell);
                }
            if (minX > maxX) return;

            // the patio in its own frame: "along" runs with the street, "in" runs away from it
            bool alongX = gap.Side == 0 || gap.Side == 2;
            float along0 = alongX ? minX : minZ, along1 = alongX ? maxX : maxZ;
            float in0, in1, inward;      // in0 at the pavement line, in1 at the back
            switch (gap.Side)
            {
                case 0: in0 = minZ; in1 = maxZ; inward = 1f; break;
                case 2: in0 = maxZ; in1 = minZ; inward = -1f; break;
                case 1: in0 = maxX; in1 = minX; inward = -1f; break;
                default: in0 = minX; in1 = maxX; inward = 1f; break;
            }
            Vector2 At(float along, float @in) => alongX ? new Vector2(along, @in) : new Vector2(@in, along);
            // the bench and the chairs face the street: their front is +z, so the yaw that
            // points +z at the street
            float toStreet = gap.Side switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };

            // the patio is the strip BESIDE the storefront, not the ground behind it: the
            // gap less the shop's own width, on whichever side of it is the wider
            float shop0 = alongX ? cafe.Foot.xMin : cafe.Foot.yMin;
            float shop1 = alongX ? cafe.Foot.xMax : cafe.Foot.yMax;
            const float off = 0.1f;     // off the shop's flank
            float strip0, strip1;
            if (shop0 - along0 >= along1 - shop1) { strip0 = along0; strip1 = shop0 - off; }
            else { strip0 = shop1 + off; strip1 = along1; }
            if (strip1 - strip0 < 2.2f) return;     // not even a bench

            float chair = Box(CafeChair).size.x * 0.5f + Box(CafeTable).size.x * 0.5f + 0.05f;
            float depth = Mathf.Abs(in1 - in0);
            int rows = Mathf.Min(2, Mathf.FloorToInt((depth - 1.2f) / TableRows));
            int n = 0;
            for (int r = 0; r < rows; r++)
            {
                float @in = in0 + inward * (1.6f + r * TableRows);
                // the room is the table AND its chairs (2.4 m), so a table never lands with
                // a chair inside the storefront's wall - and a 10 m gap less the coffee
                // shop's 7.2 m front is 2.5 m, which seats exactly that
                const float room = 2f;
                float half = Box(CafeTable).size.x * room * 0.5f;
                for (float a = strip0 + half; a + half <= strip1 + 0.01f; a += TableAlong)
                {
                    var at = At(a, @in);
                    var table = Prop(CafeTable, pen, at.x, at.y, 90f * rng.Next(4), room);
                    if (table == null) continue;
                    stood.Tables++;
                    stood.Props++;
                    for (int k = 0; k < 4; k++)
                    {
                        float yaw = k * 90f;
                        var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, chair);
                        Sit(CafeChair, pen, at.x + spot.x, at.y + spot.z, yaw + 180f + Between(rng, -15f, 15f));
                    }
                    if (n++ % 2 == 0) Sit(Umbrella, pen, at.x, at.y, Between(rng, 0f, 360f));
                }
            }

            // a bench or two along the strip's back line, looking at the street
            float backIn = in1 - inward * 0.6f;
            float benchHalf = Box(ParkBench).size.x * 0.5f + 0.1f;
            for (float a = strip0 + benchHalf; a + benchHalf <= strip1 + 0.01f; a += 5f)
            {
                var at = At(a, backIn);
                if (Prop(ParkBench, pen, at.x, at.y, toStreet, 1.1f) == null) continue;
                stood.Benches++;
                stood.Props++;
            }
        }

        // ------------------------------------------------------------------ what is on it

        /// <summary>What stands on the ground: the skips on the alley's verges, and nothing
        /// in the yards. The yards had fences, washing lines, bins and boxes; the fences
        /// were a run of railings that finished nowhere and the washing hung on nothing (the
        /// user, 2026-08-27: "makni zidove skroz") - a yard the recipe has nothing for is
        /// left bare, not dressed.</summary>
        static void Dress(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                    if (plan.Ground[i, j] == ResidentialLot.Use.Verge) Bins(plan, root, rng, i, j, stood);
        }

        /// <summary>
        /// The skips: on the verge beside the alley, against the backs of the houses - and
        /// never on the alley itself, which is a road (the user, 2026-08-27: "ako si stavio
        /// put ne mozes na put da stavis kontenjere"). The litter is what spills round them.
        /// </summary>
        static void Bins(ResidentialLot.Plan plan, Transform root, System.Random rng, int i, int j, Stood stood)
        {
            int alley = -1;
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                if (plan.Ground[x, y] == ResidentialLot.Use.Alley) alley = side;
            }
            if (alley < 0 || !Chance(rng, 0.4)) return;

            float cell = ResidentialLot.Cell;
            float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
            // against the far edge of the verge: half the cell, less the skip's own half
            // depth and a hand's breadth off the wall
            const float back = 1.6f;
            float dx = -ResidentialLot.Step[alley, 0] * back, dz = -ResidentialLot.Step[alley, 1] * back;
            bool alongX = alley == 0 || alley == 2;     // the alley runs east-west: so does the skip
            float slide = Between(rng, -1.2f, 1.2f);
            float sx = cx + dx + (alongX ? slide : 0f), sz = cz + dz + (alongX ? 0f : slide);
            // the skip's lid slopes down to its +z side, so that is its front: it is turned
            // to face the alley, back to the wall (the user, 2026-08-27: "kante treba da su
            // okrenute ka ulicici")
            float yaw = alley switch { 2 => 0f, 1 => 90f, 0 => 180f, _ => 270f };
            if (Prop(Any(Skips, rng), root, sx, sz, yaw, 1f, Deck) != null) stood.Props++;

            if (!Chance(rng, 0.5)) return;
            float along = Between(rng, 1.5f, 2.1f) * (Chance(rng, 0.5) ? 1f : -1f);
            float lx = sx + (alongX ? along : Between(rng, -0.4f, 0.4f));
            float lz = sz + (alongX ? Between(rng, -0.4f, 0.4f) : along);
            if (Prop(Any(Litter, rng), root, lx, lz, Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
        }

        /// <summary>Street lamps on the kerb, a cell in from the corner and every 20 m after
        /// - and only on sides that have a street. Where each one stands is remembered, so
        /// no palm is planted through it.</summary>
        static void Lamps(ResidentialLot.Plan plan, Transform root, List<Vector3> standing, Stood stood)
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
                    // never on a mouth: a lamp in the ring cell cut for the car park's way
                    // in stood in the way of the cars (the user, 2026-08-27: "ne mozes na
                    // sred ulaznog puta da stavis lampu")
                    if (plan.Ground[i, j] != ResidentialLot.Use.Walkway) continue;
                    float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
                    // a lamp stands a metre in from the kerb's outer edge, arm over the road
                    x += side == 1 ? -lane : side == 3 ? lane : 0f;
                    z += side == 2 ? -lane : side == 0 ? lane : 0f;
                    float yaw = side switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };
                    if (Prop(Lamp, root, x, z, yaw, 0.4f, Deck) == null) continue;
                    stood.Props++;
                    stood.Lamps++;
                    standing.Add(new Vector3(x, 0f, z));
                }
            }
        }

        /// <summary>The palms on the pavement ring, one to ten kerb tiles in the core's own
        /// lane and rhythm (<see cref="CorePavement.Plant"/>), so a residential block and a
        /// core block are planted alike - the only trees the block gets.</summary>
        static void Palms(List<CorePavement.Kerbstone> kerbs, List<Vector3> standing, Transform root,
                          System.Func<GameObject, Transform, GameObject> raise, int seed, Stood stood)
        {
            var under = new GameObject("Palms").transform;
            under.SetParent(root, false);
            stood.Palms = CorePavement.Plant(kerbs, standing, raise, under, seed);
            stood.Props += stood.Palms;
        }
    }
}
