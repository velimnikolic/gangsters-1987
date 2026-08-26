using System;
using System.Collections.Generic;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>
    /// What stands on a stretch of the promenade, composed piece by piece from the plan
    /// <see cref="QuayWalk"/> drew: the paving and the grass, the quay wall with its railing
    /// and lamps, the cafes, the fountain, the landing with its boats, the one fairground,
    /// and the benches turned to the water.
    ///
    /// EVERY NUMBER BELOW IS MEASURED - off the POLYGON Palm City demo's own waterfront
    /// (Docs/river-plan.md 0.4) and off the pieces themselves. Their pier has its lamps in
    /// pairs every ten to fifteen metres, its cafe puts four tables on ten metres of deck
    /// under 3.25 m umbrellas, its shops stand by the root; their fairground is a 30 x 40 m
    /// yard fenced with barriers, the 31 m wheel in it at full size; their marina floats its
    /// docks 0.45 m above the water. The grid city's river (RoadDemoBuilder.BuildRiver)
    /// lays the same wall at the same height and its lamps at sixteen metres, and the
    /// harbour's water line hangs a life ring at every third lamp and a ladder every sixty
    /// metres - all of it kept, so the water reads the same wherever the city meets it.
    ///
    /// Composed at the ORIGIN, x across the strip from the kerb (0) to the wall
    /// (<c>Depth * Cell</c>), z along it; the host moves the root afterwards. Nothing is
    /// scaled below its authored size: what does not fit is refused and counted.
    /// </summary>
    public static class QuayBlocks
    {
        public const float Cell = QuayWalk.Cell;

        // ------------------------------------------------------------------- the pieces

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmBld = "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        const string GenBld = "Assets/Synty/PolygonGeneric/Prefabs/Building/";
        const string CoffeeProps = "Assets/Synty/PolygonCoffeeShop/Prefabs/";
        const string FX = "Assets/Synty/PolygonPalmCity/Prefabs/FX/";

        // the ground: the city's own paving square and grass, and its bare asphalt for
        // the fairground's yard
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Grass = CityEnv + "SM_Env_Grass_01.prefab";
        const string Asphalt = CityEnv + "SM_Env_Road_Bare_01.prefab";

        // the water line: the grid city's wall, the palm city's railing on its coping
        const string Wall = CityEnv + "SM_Env_WaterEdge_Straight_03.prefab";
        const string WallWorn = CityEnv + "SM_Env_WaterEdge_Straight_02.prefab";
        const string Outfall = CityEnv + "SM_Env_WaterEdge_Pipe_01.prefab";
        const string Railing = PalmBld + "SM_Bld_Wall_Railing_01.prefab";
        const string RailPost = PalmBld + "SM_Bld_Wall_Railing_01_Pillar_02.prefab";
        const string PierLamp = PalmProps + "SM_Prop_Pier_Lamp_01.prefab";
        const string LifeRing = PalmProps + "SM_Prop_Rescue_Buoy_01.prefab";
        const string Ladder = GenBld + "SM_Gen_Bld_Ladder_01.prefab";
        const string Bollard = PalmProps + "SM_Prop_Bollard_02.prefab";
        const string Binoculars = PalmProps + "SM_Prop_Binoculars_01.prefab";
        const string FishingRod = PalmProps + "SM_Prop_Fishing_Rod_01.prefab";
        const string Bucket = PalmProps + "SM_Prop_Bucket_01.prefab";

        // the furniture
        const string PierBench = PalmProps + "SM_Prop_Pier_Bench_02.prefab";
        const string ParkBench = CityProps + "SM_Prop_ParkBench_01.prefab";
        static readonly string[] Bins =
        {
            PalmProps + "SM_Prop_Trash_Bin_04.prefab",
            CityProps + "SM_Prop_Trashbin_01.prefab",
        };
        const string Flower = CityEnv + "SM_Env_Flower_01.prefab";
        static readonly string[] Trees =
        {
            CityEnv + "SM_Env_Tree_01.prefab",
            CityEnv + "SM_Env_Tree_02.prefab",
            CityEnv + "SM_Env_Tree_03.prefab",
        };

        // the cafe: a kiosk by the kerb, tables under umbrellas before the walk
        static readonly string[] Kiosks =
        {
            PalmBld + "SM_Bld_Beach_Shop_01.prefab",
            PalmBld + "SM_Bld_Beach_Shop_02.prefab",
            PalmBld + "SM_Bld_Beach_Shop_03.prefab",
        };
        const string CafeTable = CityProps + "SM_Prop_Table_02.prefab";
        const string CafeChair = CoffeeProps + "SM_Prop_Chair_01.prefab";
        const string Umbrella = PalmProps + "SM_Prop_Umbrella_02.prefab";
        const string MenuStand = PalmProps + "SM_Prop_Menu_Stand_01.prefab";
        const string NewsStand = PalmProps + "SM_Prop_Newspaper_Stand_01.prefab";
        const string BikeStand = PalmProps + "SM_Prop_Bike_Stand_01.prefab";
        /// <summary>The catalog's own coffee shop (5.8 x 7.2 m) stands in for a kiosk one
        /// cafe in four, and the diner (16.3 x 8.8 m) is a programme of its own. Which way
        /// either faces is MEASURED off its mesh (<see cref="FacadeFinder"/>): the coffee
        /// shop was baked with its back to the street once already.</summary>
        const string KitBld = "Assets/CityKit/Buildings/";
        const string CoffeeShop = KitBld + "building-coffeeshop.prefab";
        const string Diner = KitBld + "building-diner.prefab";
        const string HotDogCart = PalmVeh + "SM_Veh_Hot_Dog_Cart_01.prefab";

        // the planting on the paving: palms in their grates (the pavement's own trees in
        // this pack), planters, and the planter with the bench round it
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        static readonly string[] Palms =
        {
            PalmEnv + "SM_Env_Tree_Palm_02.prefab",
            PalmEnv + "SM_Env_Tree_Palm_03.prefab",
            PalmEnv + "SM_Env_Tree_Palm_04.prefab",
        };
        const string Grate = PalmEnv + "SM_Env_Plant_Grate_01.prefab";
        const string Planter = PalmProps + "SM_Prop_Planter_01.prefab";
        const string PlanterBench = PalmProps + "SM_Prop_Planter_Bench_01.prefab";
        const string SignPole = PalmProps + "SM_Prop_Sign_Pole_02.prefab";

        // the plaza
        const string Fountain = PalmProps + "SM_Prop_Fountain_01.prefab";
        const string FountainWater = FX + "FX_Fountain_Water_01.prefab";

        // the landing: a flight of stairs off the wall to a dock a step above the water
        const string DockStairs = PalmBld + "SM_Bld_Dock_Stairs_01.prefab";
        const string DockPlatform = PalmBld + "SM_Bld_Dock_Platform_01.prefab";
        const string DockPole = PalmBld + "SM_Bld_Dock_Pole_01.prefab";
        const string DockRailing = PalmBld + "SM_Bld_Dock_Railing_01.prefab";
        static readonly string[] Boats =
        {
            PalmVeh + "SM_Veh_Party_Boat_01.prefab",
            PalmVeh + "SM_Veh_Power_Boat_01.prefab",
            PalmVeh + "SM_Veh_RIB_Boat_01.prefab",
        };

        // the fairground
        const string Wheel = PalmProps + "SM_Prop_Ferris_Wheel_01.prefab";
        const string JuiceCart = PalmVeh + "SM_Veh_Juice_Cart_01.prefab";
        const string FairTable = PalmProps + "SM_Prop_Table_Outdoor_01.prefab";
        const string FairFence = PalmProps + "SM_Prop_Barrier_Fence_01.prefab";

        // ------------------------------------------------------------------- the levels

        /// <summary>The water, the grid city's own level under its quays.</summary>
        public const float WaterY = -2.65f;
        /// <summary>The wall's coping stands this much proud of the paving, and the railing
        /// stands on the coping.</summary>
        const float Coping = 0.43f;
        /// <summary>The coping's width toward the water: the wall body's reach off the line.</summary>
        const float CopingWide = 1.5f;
        /// <summary>The dock: one flight of the pack's stairs below the walk (its rise), a
        /// metre and more above the water.</summary>
        const float DockY = -1.56f;

        // ------------------------------------------------------------------- the rhythms

        /// <summary>Lamps along the wall, the grid river's own beat.</summary>
        const float LampEvery = 16f;
        /// <summary>A life ring hangs beside every third lamp; a ladder goes down the wall
        /// every sixty metres - the harbour's water line.</summary>
        const int RingEveryLamps = 3;
        const float LadderEvery = 60f;
        /// <summary>Benches turned to the water, along the walk.</summary>
        const float BenchEvery = 9f;
        /// <summary>A fisherman's spot at the railing.</summary>
        const float FishingEvery = 48f;
        /// <summary>The pier's cafe: a table every two and a half metres along, in rows
        /// three metres apart.</summary>
        const float TableAlong = 2.6f, TableRows = 3.2f;
        /// <summary>A cafe's terrace: two rows of tables, nine metres either side of the
        /// kiosk - the pier's own terrace is two rows on ten metres.</summary>
        const int TerraceRows = 2;
        const float TerraceHalf = 9f;

        // ----------------------------------------------------------------------- the walk

        /// <summary>The walk's lanes, across the strip from the wall: a metre and a bit of
        /// railing, lamps and binoculars; five metres of clear way, which is the promenade
        /// and is kept so; and what is left of the two cells for the benches.</summary>
        const float RailLane = 1.2f, ClearWay = 5.2f;

        /// <summary>One stretch, standing - and what the composer could see wrong with its
        /// own work.</summary>
        public sealed class Stood
        {
            public QuayWalk.Plan Plan;
            public Transform Root;
            /// <summary>Cells with nothing on the floor.</summary>
            public int Gaps;
            /// <summary>Metres of railing that should be standing and are not, the landing's
            /// gap discounted.</summary>
            public float RailGap;
            /// <summary>Things standing in the clear way of the walk, counted off what
            /// actually stood.</summary>
            public int OnWalk;
            public int Lamps, Benches, BinCount, TreeCount, PalmCount, Planters, Tables, Kiosks, Carts, Programmes, BoatCount, Rings, Ladders;
            public bool Wheel, DinerStood;
            public string Refused = "";
            /// <summary>What was found in the way, by name - a count alone is a thing to
            /// guess at, and the first drawing had one.</summary>
            public readonly List<string> InTheWay = new List<string>();
        }

        /// <summary>
        /// Stands a whole stretch: the way is booked, then the programmes, the water line,
        /// the furniture, the planting, and the tiles LAST - everything above books the
        /// ground it stands on and the floor fills whatever is left, the park's order.
        /// </summary>
        public static Stood Compose(QuayWalk.Plan plan, Transform root, System.Random rng,
                                    Func<GameObject, Transform, GameObject> raise)
        {
            Begin(raise);
            var stood = new Stood { Plan = plan, Root = root };

            BookTheWay(plan);
            Programmes(plan, root, rng, stood);
            WaterLine(plan, root, rng, stood);
            Furniture(plan, root, rng, stood);
            Planting(plan, root, rng, stood);
            Floor(plan, root, stood);

            stood.OnWalk = OnTheWay(plan, root, stood.InTheWay);
            stood.Refused = Worst();
            return stood;
        }

        /// <summary>The clear way of the walk, end to end, and every lane across the strip:
        /// booked before anything is set down. One rectangle each, so the rule that places
        /// and the rule that judges (<see cref="OnTheWay"/>) read the same ground.</summary>
        static List<Rect> Ways(QuayWalk.Plan plan)
        {
            float wall = plan.Depth * Cell;
            var ways = new List<Rect> { new Rect(wall - RailLane - ClearWay, 0f, ClearWay, plan.Length * Cell) };
            for (int z = 0; z < plan.Length; z++)
            {
                if (plan.At(QuayWalk.Band, z) != QuayWalk.Ground.Lane) continue;
                ways.Add(new Rect(QuayWalk.Band * Cell, z * Cell, (plan.WalkX - QuayWalk.Band) * Cell, Cell));
            }
            return ways;
        }

        static void BookTheWay(QuayWalk.Plan plan)
        {
            foreach (var way in Ways(plan)) Claim(way);
        }

        /// <summary>Things in the clear way, off what stood: everything under the root but the
        /// floor and the pavement, measured by its box against the way.</summary>
        static int OnTheWay(QuayWalk.Plan plan, Transform root, List<string> named)
        {
            var ways = Ways(plan);
            int over = 0;
            foreach (Transform group in root)
            {
                if (group.name == "Ground" || group.name == "Pavement") continue;
                foreach (Transform piece in group.GetComponentsInChildren<Transform>(true))
                {
                    // the pieces themselves, not the pens they are grouped in
                    if (piece == group || piece.childCount > 0 && piece.GetComponent<Renderer>() == null) continue;
                    if (!WorldBox(piece.gameObject, out var box)) continue;
                    // by its foot, a metre square about its middle: a lamp's arm or an
                    // umbrella's canopy reaching over the way is not a thing in the way
                    var foot = new Rect(box.center.x - 0.45f, box.center.z - 0.45f, 0.9f, 0.9f);
                    foreach (var way in ways)
                        if (way.Overlaps(foot))
                        {
                            over++;
                            if (named.Count < 8) named.Add($"{piece.name} at ({box.center.x:F1}, {box.center.z:F1})");
                            break;
                        }
                }
            }
            return over;
        }

        /// <summary>The ground a room offers for what stands on it, in metres: between the
        /// kerb and the walk, half a metre in from every edge.</summary>
        static Rect Fit(QuayWalk.Plan plan, QuayWalk.Room room)
        {
            const float Edge = 0.5f;
            return Rect.MinMaxRect(QuayWalk.Band * Cell + Edge, room.Z0 * Cell + Edge,
                                   plan.WalkX * Cell - Edge, room.Z1 * Cell - Edge);
        }

        // ---------------------------------------------------------------- the programmes

        static void Programmes(QuayWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var under = new GameObject("Programmes").transform;
            under.SetParent(root, false);
            foreach (var room in plan.Rooms)
            {
                var box = Fit(plan, room);
                if (box.width < 1f || box.height < 1f) continue;
                switch (room.Programme)
                {
                    case QuayWalk.Programme.Fountain: Plaza(under, box, rng, stood); break;
                    case QuayWalk.Programme.Terrace: Terrace(plan, under, box, rng, stood); break;
                    case QuayWalk.Programme.Landing: Landing(plan, under, room, rng, stood); break;
                    case QuayWalk.Programme.Fair: Fair(plan, under, box, rng, stood); break;
                    case QuayWalk.Programme.Diner: DinerStand(under, box, rng, stood); break;
                    default: break;       // the lawn and the grove are planted, the paving is left
                }
            }
        }

        static readonly Dictionary<string, float> Fronts = new Dictionary<string, float>();

        /// <summary>The yaw that turns a baked building's FRONT to the water (+x), measured
        /// off its mesh once and remembered - never assumed from the file.</summary>
        static float FrontYaw(string path)
        {
            if (Fronts.TryGetValue(path, out float known)) return known;
            float yaw = 90f;
            var go = Raise(path, null);
            if (go != null)
            {
                try
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var front = FacadeFinder.FrontOf(go, out _);
                    yaw = FacadeFinder.YawToPlusZ(front) + 90f;
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Fronts[path] = yaw;
            return yaw;
        }

        /// <summary>The diner, its back a step off the kerb and its front to the water, with
        /// a few tables under umbrellas before it - a roadside diner turned to the river,
        /// which is what a 1987 waterfront made of the ones it had.</summary>
        static void DinerStand(Transform under, Rect box, System.Random rng, Stood stood)
        {
            var pen = new GameObject("diner").transform;
            pen.SetParent(under, false);
            float yaw = FrontYaw(Diner);
            var foot = Foot(Diner, yaw);
            float x = box.xMin + foot.x * 0.5f + 0.6f, z = box.center.y;
            if (Building(Diner, pen, x, z, yaw, 1.03f) == null) return;
            stood.DinerStood = true;
            stood.Programmes++;
            float chair = Box(CafeChair).size.x * 0.5f + Box(CafeTable).size.x * 0.5f + 0.05f;
            float tx = x + foot.x * 0.5f + 2.4f;
            if (tx > box.xMax - 1f) return;
            for (float tz = z - foot.y * 0.5f + 1.5f; tz < z + foot.y * 0.5f - 1f; tz += TableAlong)
            {
                var table = Prop(CafeTable, pen, tx, tz, 90f * rng.Next(4), 1.15f);
                if (table == null) continue;
                stood.Tables++;
                for (int k = 0; k < 4; k++)
                {
                    var spot = Quaternion.Euler(0f, k * 90f, 0f) * new Vector3(0f, 0f, chair);
                    Sit(CafeChair, pen, tx + spot.x, tz + spot.z, k * 90f + 180f + Between(rng, -15f, 15f));
                }
                if (Chance(rng, 0.5)) Sit(Umbrella, pen, tx, tz, Between(rng, 0f, 360f));
            }
            Prop(SignPole, pen, x + foot.x * 0.5f + 0.8f, z + foot.y * 0.5f + 0.8f, yaw);
        }

        /// <summary>The fountain in the middle of its plaza, the water playing, four benches
        /// round it facing in - the park's own plaza, stood here at the boulevard's mouth.</summary>
        static void Plaza(Transform under, Rect box, System.Random rng, Stood stood)
        {
            var middle = box.center;
            if (Prop(Fountain, under, middle.x, middle.y, 90f * rng.Next(4), 1.15f) == null) return;
            stood.Programmes++;
            var water = Raise(FountainWater, under);
            if (water != null) water.transform.position = new Vector3(middle.x, Box(Fountain).size.y * 0.55f, middle.y);

            float step = Box(Fountain).size.x * 0.5f + 2.2f;
            for (int k = 0; k < 4; k++)
            {
                float yaw = k * 90f;
                var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, step);
                float x = middle.x + spot.x, z = middle.y + spot.z;
                if (!box.Contains(new Vector2(x, z))) continue;
                if (Prop(ParkBench, under, x, z, yaw + 180f) != null) stood.Benches++;
            }
        }

        /// <summary>
        /// The cafe: a kiosk by the kerb with its front to the water, a menu stand at its
        /// corner, and the tables between it and the walk - the pier's own rate, four to
        /// ten metres, in rows, every other one under an umbrella.
        /// </summary>
        static void Terrace(QuayWalk.Plan plan, Transform under, Rect box, System.Random rng, Stood stood)
        {
            var pen = new GameObject("terrace").transform;
            pen.SetParent(under, false);

            // the kiosk, its front to the water (+x) - measured, not assumed - a metre off
            // the kerb in the middle of the room's length; one cafe in four is the
            // catalog's coffee shop instead of a beach kiosk
            string kiosk = Chance(rng, 0.25) ? CoffeeShop : Any(Kiosks, rng);
            float face = FrontYaw(kiosk);
            var foot = Foot(kiosk, face);
            float kx = box.xMin + foot.x * 0.5f + 0.8f, kz = box.center.y;
            var stood0 = kiosk == CoffeeShop ? Building(kiosk, pen, kx, kz, face, 1.05f)
                                             : Prop(kiosk, pen, kx, kz, face, 1.05f);
            if (stood0 == null) return;
            stood.Kiosks++;
            stood.Programmes++;
            Prop(MenuStand, pen, kx + foot.x * 0.5f + 0.6f, kz + foot.y * 0.5f + 0.3f, 90f);
            // the paper stand and the bike stand by its flank
            Prop(NewsStand, pen, kx + Between(rng, -1f, 1f), kz - foot.y * 0.5f - 1.2f, Between(rng, 0f, 360f));
            Prop(BikeStand, pen, kx - foot.x * 0.5f + 0.4f, kz + foot.y * 0.5f + 1.6f, 90f);

            // the tables: two rows before the kiosk, the pier's own terrace and no bigger -
            // laid to fill the room, a 45 m room came out with seventy-five of them, which
            // is a beer garden and not a cafe on a promenade
            float x0 = kx + foot.x * 0.5f + 1.6f, x1 = box.xMax - 0.6f;
            int rows = Mathf.Min(TerraceRows, Mathf.FloorToInt((x1 - x0) / TableRows));
            if (rows <= 0) return;
            float chair = Box(CafeChair).size.x * 0.5f + Box(CafeTable).size.x * 0.5f + 0.05f;
            float zLo = Mathf.Max(box.yMin + 1.6f, kz - TerraceHalf), zHi = Mathf.Min(box.yMax - 1.6f, kz + TerraceHalf);
            for (int r = 0; r < rows; r++)
            {
                float x = x0 + (r + 0.5f) * TableRows;
                for (float z = zLo; z < zHi; z += TableAlong)
                {
                    float tz = z + Between(rng, -0.25f, 0.25f);
                    var table = Prop(CafeTable, pen, x, tz, 90f * rng.Next(4), 1.15f);
                    if (table == null) continue;
                    stood.Tables++;
                    // the chairs on the four sides, turned to the table
                    for (int k = 0; k < 4; k++)
                    {
                        float yaw = k * 90f;
                        var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, chair);
                        Sit(CafeChair, pen, x + spot.x, tz + spot.z, yaw + 180f + Between(rng, -15f, 15f));
                    }
                    if ((r + Mathf.RoundToInt((z - box.yMin) / TableAlong)) % 2 == 0)
                        Sit(Umbrella, pen, x, tz, Between(rng, 0f, 360f));
                }
            }
        }

        /// <summary>
        /// The landing: a flight of stairs off the wall to a dock one rise below the walk,
        /// the dock's own railing on its outer edge, bollards at the gap in the promenade's
        /// railing, and a boat or two alongside - the tour boat, and a power boat.
        /// </summary>
        static void Landing(QuayWalk.Plan plan, Transform under, QuayWalk.Room room, System.Random rng, Stood stood)
        {
            if (room.LandingZ < 0) return;
            var pen = new GameObject("landing").transform;
            pen.SetParent(under, false);

            float wall = plan.Depth * Cell;
            float z0 = room.LandingZ * Cell, z1 = z0 + 2f * Cell;
            float face = wall + CopingWide;                   // the wall's face, where the water begins

            // the dock: two cells long along the wall and one out, of the pack's 2.5 m
            // tiles, on its poles
            float tile = Box(DockPlatform).size.x;
            if (tile < 1f) tile = 2.5f;
            int across = Mathf.Max(1, Mathf.RoundToInt(Cell / tile)), along = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / tile));
            for (int a = 0; a < across; a++)
                for (int b = 0; b < along; b++)
                    Lay(DockPlatform, pen, face + a * tile, z0 + b * tile, tile, tile, 0f, DockY);
            for (int b = 0; b <= along; b += 2)
                foreach (float x in new[] { face + 0.3f, face + across * tile - 0.3f })
                    Sit(DockPole, pen, x, z0 + Mathf.Min(b * tile, z1 - z0 - 0.3f), 0f, WaterY - 1.2f);
            // the railing along the dock's outer edge, not its ends: the boats come alongside
            float rail = Box(DockRailing).size.x;
            if (rail < 1f) rail = 2.5f;
            for (float z = z0; z < z1 - 0.1f; z += rail)
                Lay(DockRailing, pen, face + across * tile - 0.25f, z, 0.21f, rail, 90f, DockY);

            // the stairs down off the wall: the top step at the coping, the flight falling
            // east over the dock. The piece rises toward its own +Z from its pivot, so it is
            // turned +Z west to put the top at the wall
            var stairs = Box(DockStairs).size;
            Lay(DockStairs, pen, face - 0.2f, z0 + (z1 - z0 - stairs.x) * 0.5f, stairs.z, stairs.x, 270f, DockY);
            stood.Programmes++;

            // bollards either side of the gap in the promenade's railing
            foreach (float z in new[] { z0 - 0.5f, z1 + 0.5f })
                Prop(Bollard, pen, wall + 0.5f, z, 0f, 1f, Coping);

            // the boats alongside, bows along the river, at the water: a boat's pivot is
            // its waterline, so the hull goes under by as much as it was modelled to
            float bx = face + across * tile + 2.4f;
            var boat = Boats[0];
            var go = Sit(boat, pen, bx, (z0 + z1) * 0.5f, rng.Next(2) == 0 ? 0f : 180f, WaterY + Box(boat).min.y);
            if (go != null) { stood.BoatCount++; Claim(new Rect(bx - 2f, z0 - 6f, 4f, z1 - z0 + 12f)); }
            if (Chance(rng, 0.6))
            {
                float pz = z1 + 8f;
                string other = Any(Boats, rng);
                var second = Sit(other, pen, bx - 0.4f, pz, Between(rng, -6f, 6f), WaterY + Box(other).min.y);
                if (second != null) stood.BoatCount++;
            }
        }

        /// <summary>
        /// The fairground: the demo's own yard - barriers round it with the gate onto the
        /// walk, the wheel at full size with its plane along the river, the juice cart and
        /// the outdoor tables by the gate.
        /// </summary>
        static void Fair(QuayWalk.Plan plan, Transform under, Rect box, System.Random rng, Stood stood)
        {
            var wheel = Box(Wheel).size;
            // the wheel's plane is its own x; along the river it is turned a quarter
            float across = wheel.z, along = wheel.x;
            if (along + 4f > box.height || across + 3f > box.width)
            {
                Refused["SM_Prop_Ferris_Wheel_01"] = 1;
                return;
            }
            var pen = new GameObject("fairground").transform;
            pen.SetParent(under, false);

            // the wheel toward the kerb end of the yard, the tables and the cart toward
            // the walk; nobody scales a wheel
            float wx = box.xMin + across * 0.5f + 1.2f, wz = box.center.y;
            var go = Stand(Wheel, pen, wx, wz, 90f);
            if (go == null) return;
            Claim(new Rect(wx - across * 0.5f - 0.5f, wz - along * 0.5f - 0.5f, across + 1f, along + 1f));
            stood.Wheel = true;
            stood.Programmes++;

            // the barriers round the yard, the gate in the middle of the walk side
            float panel = Box(FairFence).size.x;
            if (panel < 0.8f) panel = 2f;
            var yard = box;
            for (int k = 0; k < 4; k++)
            {
                bool alongZ = k >= 2;                       // the two long sides run along the river
                float line = k == 0 ? yard.yMin : k == 1 ? yard.yMax : k == 2 ? yard.xMin : yard.xMax;
                float run = alongZ ? yard.height : yard.width;
                // whole panels at their own size, never one scaled down to fit: what is
                // left over is split between the two ends of the side
                int panels = Mathf.Max(1, Mathf.FloorToInt(run / panel + 0.01f));
                float from = (alongZ ? yard.yMin : yard.xMin) + (run - panels * panel) * 0.5f;
                int gate0 = k == 3 ? panels / 2 - 1 : -1;   // two panels out on the walk side
                for (int p = 0; p < panels; p++)
                {
                    if (gate0 >= 0 && (p == gate0 || p == gate0 + 1)) continue;
                    float at = from + p * panel;
                    if (alongZ) Lay(FairFence, pen, line - 0.1f, at, 0.2f, panel, 90f);
                    else Lay(FairFence, pen, at, line - 0.1f, panel, 0.2f, 0f);
                }
            }

            // the cart by the gate, the tables between the wheel and the walk
            float tx0 = wx + across * 0.5f + 1.5f, tx1 = yard.xMax - 1f;
            if (Prop(JuiceCart, pen, tx1 - 2.2f, yard.yMax - 3.5f, 180f, 1.1f) != null) stood.Programmes++;
            int want = 6;
            for (int guard = 0; want > 0 && guard < 40 && tx1 - tx0 > 2f; guard++)
            {
                float x = Between(rng, tx0 + 1f, tx1 - 1f), z = Between(rng, yard.yMin + 1.5f, yard.yMax - 6f);
                if (Prop(FairTable, pen, x, z, Between(rng, 0f, 360f), 1.3f) == null) continue;
                stood.Tables++;
                want--;
            }
            Sit(Any(Bins, rng), pen, tx1 - 0.8f, yard.yMin + 1f, Between(rng, 0f, 360f));
        }

        // ----------------------------------------------------------------- the water line

        /// <summary>
        /// The wall along the water, the railing on its coping, the lamps, the life rings,
        /// the ladders, the fishermen's spots - the grid river's wall and beat, the
        /// harbour's water line, the palm city's railing.
        /// </summary>
        static void WaterLine(QuayWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var line = new GameObject("Waterline").transform;
            line.SetParent(root, false);
            float wall = plan.Depth * Cell, length = plan.Length * Cell;

            // the wall: one piece to a cell, pivot on the line, its body reaching east
            // into the water (RoadDemoBuilder.BuildRiver's own placing, the low bank of a
            // river that runs along z)
            for (int z = 0; z < plan.Length; z++)
            {
                string piece = Chance(rng, 0.18) ? WallWorn : Wall;
                var go = Raise(piece, line);
                if (go == null) continue;
                go.name = "Quay";
                go.transform.SetPositionAndRotation(new Vector3(wall, 0f, z * Cell), Quaternion.Euler(0f, -90f, 0f));
                if (Chance(rng, 0.06))
                {
                    var pipe = Raise(Outfall, line);
                    if (pipe != null)
                    {
                        pipe.name = "Outfall";
                        pipe.transform.SetPositionAndRotation(new Vector3(wall + 0.9f, WaterY + 0.9f, z * Cell + Cell * 0.5f),
                                                              Quaternion.Euler(0f, -90f, 0f));
                    }
                }
            }

            // the railing on the coping, broken only at the landing
            float gap0 = -1f, gap1 = -1f;
            foreach (var room in plan.Rooms)
                if (room.Programme == QuayWalk.Programme.Landing && room.LandingZ >= 0)
                { gap0 = room.LandingZ * Cell; gap1 = gap0 + 2f * Cell; }
            float railLen = Box(Railing).size.x;
            if (railLen < 1f) railLen = 2.5f;
            float thick = Mathf.Max(0.1f, Box(Railing).size.z);
            float wanted = 0f, laid = 0f;
            float rx = wall + 0.5f;
            for (float z = 0f; z < length - 0.1f; z += railLen)
            {
                if (z + railLen * 0.5f > gap0 && z + railLen * 0.5f < gap1) continue;
                wanted += railLen;
                if (Lay(Railing, line, rx - thick * 0.5f, z, thick, railLen, 90f, Coping) != null) laid += railLen;
                Sit(RailPost, line, rx, z, 0f, Coping);
            }
            Sit(RailPost, line, rx, length, 0f, Coping);
            stood.RailGap = Mathf.Max(0f, wanted - laid);

            // lamps by the coping, turned to the water - in the railing's own lane, where
            // their feet keep off the clear way (a metre in, the first drawing had every
            // one of them refused for standing a hand's breadth on it); a life ring beside
            // every third; a ladder down the wall every sixty metres
            int lamp = 0;
            for (float z = LampEvery * 0.5f; z < length - 2f; z += LampEvery, lamp++)
            {
                if (z > gap0 - 1f && z < gap1 + 1f) continue;
                if (Prop(PierLamp, line, wall - 0.7f, z, 90f) != null) stood.Lamps++;
                if (lamp % RingEveryLamps == 1 && Sit(LifeRing, line, wall - 0.6f, z + 1.4f, 90f + Between(rng, -25f, 25f)) != null)
                    stood.Rings++;
            }
            var ladder = Box(Ladder).size;
            for (float z = 22f; z < length - 8f; z += LadderEvery)
            {
                if (z > gap0 - 3f && z < gap1 + 3f) continue;
                var go = Raise(Ladder, line);
                if (go == null) continue;
                go.name = "Ladder";
                // against the wall's face, rungs to the water, stretched to reach the
                // coping from below the water line and never squashed (the harbour's rule)
                go.transform.SetPositionAndRotation(new Vector3(wall + CopingWide + 0.05f, WaterY - 0.4f, z), Quaternion.Euler(0f, 90f, 0f));
                float module = Mathf.Max(0.5f, ladder.y);
                var s = go.transform.localScale;
                s.y *= Mathf.Max(1f, (Coping + 0.2f - (WaterY - 0.4f)) / module);
                go.transform.localScale = s;
                stood.Ladders++;
            }

            // the fishermen: binoculars on the rail and a rod and bucket beside them, at
            // long intervals and never before a cafe
            for (float z = FishingEvery * 0.6f; z < length - 4f; z += FishingEvery)
            {
                if (z > gap0 - 4f && z < gap1 + 4f) continue;
                if (RoomAt(plan, z) is QuayWalk.Room room && room.Programme == QuayWalk.Programme.Terrace) continue;
                // all of it hard against the railing: a rod leans a metre and a half and a
                // bucket a hand's breadth further in stood in the clear way (the first
                // drawing's three things in the way were exactly these)
                if (Prop(Binoculars, line, wall - 0.7f, z, 90f) == null) continue;
                // the rod leans, so it is turned to lean ALONG the wall - whichever quarter
                // gives it the narrower foot across the strip - and laid with its whole
                // reach against the railing
                float lean = Foot(FishingRod, 0f).x <= Foot(FishingRod, 90f).x ? 0f : 90f;
                float reach = Foot(FishingRod, lean).x;
                Sit(FishingRod, line, wall - reach * 0.5f - 0.05f, z + 1.6f, lean + Between(rng, -10f, 10f));
                Sit(Bucket, line, wall - 0.45f, z + 2.3f, Between(rng, 0f, 360f));
            }
        }

        static QuayWalk.Room RoomAt(QuayWalk.Plan plan, float z)
        {
            int cell = Mathf.FloorToInt(z / Cell);
            foreach (var room in plan.Rooms) if (cell >= room.Z0 && cell < room.Z1) return room;
            return null;
        }

        // ------------------------------------------------------------------- the furniture

        /// <summary>
        /// The walk's inner lane: benches turned to the water, a bin beside every second
        /// one, and a palm in its grate between every pair - the pavement's tree in this
        /// pack, planted along the promenade the way the demo's beach front is. Planters
        /// along the walk's edge in the paved rooms, and a hot dog cart where a street's
        /// lane comes out onto the walk, now and then. Not before a cafe or inside the
        /// fair, whose furniture is their own.
        /// </summary>
        static void Furniture(QuayWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var kit = new GameObject("Furniture").transform;
            kit.SetParent(root, false);
            float wall = plan.Depth * Cell;
            float benchX = plan.WalkX * Cell + (wall - RailLane - ClearWay - plan.WalkX * Cell) * 0.5f;
            int since = 0, slot = 0;
            for (float z = BenchEvery * 0.5f; z < plan.Length * Cell - 1.5f; z += BenchEvery, slot++)
            {
                var room = RoomAt(plan, z);
                if (room != null && room.Programme == QuayWalk.Programme.Fair) continue;
                if (plan.At(QuayWalk.Band, Mathf.FloorToInt(z / Cell)) == QuayWalk.Ground.Kerb) continue;
                bool cafe = room != null && (room.Programme == QuayWalk.Programme.Terrace || room.Programme == QuayWalk.Programme.Diner);
                if (slot % 2 == 1)
                {
                    // a palm between the benches, in its grate, a little off the line
                    float px = benchX + Between(rng, -0.4f, 0.4f), pz = z + Between(rng, -0.8f, 0.8f);
                    if (Prop(Any(Palms, rng), kit, px, pz, Between(rng, 0f, 360f), 0.35f) != null)
                    {
                        Sit(Grate, kit, px, pz, 0f, 0.01f);
                        stood.PalmCount++;
                    }
                    continue;
                }
                if (cafe) continue;
                // the bench's back is its +z; turned to look east at the water
                if (Prop(PierBench, kit, benchX, z + Between(rng, -0.6f, 0.6f), 270f) == null) continue;
                stood.Benches++;
                if (++since < 2) continue;
                since = 0;
                if (Prop(Any(Bins, rng), kit, benchX + Between(rng, -0.3f, 0.3f), z + 1.9f, Between(rng, 0f, 360f)) != null) stood.BinCount++;
            }

            // planters along the walk's edge of every paved room, a bench-planter on the
            // plaza's corners
            foreach (var room in plan.Rooms)
            {
                if (room.Programme != QuayWalk.Programme.Fountain && room.Programme != QuayWalk.Programme.Terrace &&
                    room.Programme != QuayWalk.Programme.Landing && room.Programme != QuayWalk.Programme.Diner &&
                    room.Programme != QuayWalk.Programme.Plaza) continue;
                var box = Fit(plan, room);
                for (float z = box.yMin + 2f; z < box.yMax - 2f; z += 7f)
                    if (Prop(Planter, kit, box.xMax - 0.9f, z, 90f, 1.1f) != null) stood.Planters++;
                if (room.Programme == QuayWalk.Programme.Fountain && box.height > 12f)
                    foreach (float z in new[] { box.yMin + 2.2f, box.yMax - 2.2f })
                        if (Prop(PlanterBench, kit, box.xMin + 2.2f, z, 0f, 1.1f) != null) stood.Planters++;
            }

            // a hot dog cart at every third lane's mouth onto the walk, drawn up beside it
            int lane = 0;
            for (int z = 0; z < plan.Length; z++)
            {
                if (plan.At(QuayWalk.Band, z) != QuayWalk.Ground.Lane) continue;
                if (z > 0 && plan.At(QuayWalk.Band, z - 1) == QuayWalk.Ground.Lane) continue;
                if (lane++ % 3 != 1) continue;
                float cz = z * Cell - 2.6f, cx = plan.WalkX * Cell - 2.4f;
                if (Prop(HotDogCart, kit, cx, cz, 0f, 1.05f) != null) stood.Carts++;
            }
        }

        /// <summary>The grass rooms: a grove of city trees and a bench or two on the grass in
        /// a grove, flowers along the walk's edge in a lawn. Palms are the kerb's, and the
        /// pavement plants them (CorePavement).</summary>
        static void Planting(QuayWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var wood = new GameObject("Trees").transform;
            wood.SetParent(root, false);
            foreach (var room in plan.Rooms)
            {
                var box = Fit(plan, room);
                // a line of trees along the walk's edge wherever a room has grass there -
                // the lawns and groves, and the grass either side of a cafe's or a diner's
                // apron: the promenade is walked under trees (the user, 2026-08-26: "dodaj
                // drveca")
                for (float z = box.yMin + 2.5f; z < box.yMax - 2f; z += Between(rng, 6f, 8f))
                {
                    if (plan.At(plan.WalkX - 1, Mathf.FloorToInt(z / Cell)) != QuayWalk.Ground.Grass) continue;
                    if (Prop(Any(Trees, rng), wood, box.xMax - Between(rng, 1.2f, 2f), z, Between(rng, 0f, 360f), 0.75f) != null)
                        stood.TreeCount++;
                }
                if (room.Programme != QuayWalk.Programme.Grove && room.Programme != QuayWalk.Programme.Lawn) continue;
                if (room.Programme == QuayWalk.Programme.Grove)
                {
                    int want = Mathf.Clamp(Mathf.RoundToInt(box.width * box.height / 60f), 3, 9);
                    for (int guard = 0; want > 0 && guard < want * 6; guard++)
                    {
                        float x = Between(rng, box.xMin + 1.5f, box.xMax - 1.5f), z = Between(rng, box.yMin + 1.5f, box.yMax - 1.5f);
                        if (Prop(Any(Trees, rng), wood, x, z, Between(rng, 0f, 360f), 0.75f) == null) continue;
                        stood.TreeCount++;
                        want--;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float z = Between(rng, box.yMin + 2f, box.yMax - 2f);
                        if (Prop(ParkBench, wood, box.xMax - 1.2f, z, 270f) != null) stood.Benches++;
                    }
                    continue;
                }
                // a lawn: flowers in clumps along the walk's edge and a tree or two
                for (float z = box.yMin + 2f; z < box.yMax - 2f; z += Between(rng, 3f, 6f))
                {
                    int many = 3 + rng.Next(4);
                    for (int k = 0; k < many; k++)
                    {
                        float x = box.xMax - Between(rng, 0.6f, 2.2f), fz = z + Between(rng, -1.2f, 1.2f);
                        if (!Room(new Rect(x - 0.2f, fz - 0.2f, 0.4f, 0.4f))) continue;
                        Sit(Flower, wood, x, fz, Between(rng, 0f, 360f));
                    }
                }
                if (box.height > 12f && Chance(rng, 0.7))
                {
                    float x = Between(rng, box.xMin + 1.5f, box.xMax - 3f), z = Between(rng, box.yMin + 2f, box.yMax - 2f);
                    if (Prop(Any(Trees, rng), wood, x, z, Between(rng, 0f, 360f), 0.75f) != null) stood.TreeCount++;
                }
            }
        }

        // ----------------------------------------------------------------------- the floor

        /// <summary>The tiles: paving on the plaza, the walk and the lanes, grass on the
        /// grass, asphalt in the fair's yard. The kerb band is <see cref="Pave"/>'s.</summary>
        static void Floor(QuayWalk.Plan plan, Transform root, Stood stood)
        {
            var ground = new GameObject("Ground").transform;
            ground.SetParent(root, false);
            for (int x = 0; x < plan.Depth; x++)
                for (int z = 0; z < plan.Length; z++)
                {
                    string piece;
                    switch (plan.Cells[x, z])
                    {
                        case QuayWalk.Ground.Kerb: continue;
                        case QuayWalk.Ground.Grass: piece = Grass; break;
                        case QuayWalk.Ground.Yard: piece = Asphalt; break;
                        default: piece = Paving; break;
                    }
                    if (Tile(piece, ground, x, z, 0f) == null) stood.Gaps++;
                }
        }

        // -------------------------------------------------------------------- the pavement

        /// <summary>
        /// The kerb band, laid by <see cref="CorePavement"/> - the same kerb, corners, lamps,
        /// bins and palms as every block across the quay street - by growing the pavement a
        /// band round the strip's inner ground, the way the park does. The band grows on
        /// every side; the tiles it puts beyond the wall and past the line's ends are taken
        /// away again, and the ring's furniture with them.
        /// </summary>
        public static int Pave(QuayWalk.Plan plan, Transform root, out string said,
                               Func<GameObject, Transform, GameObject> stand, int seed)
        {
            float x0 = QuayWalk.Band * Cell, x1 = plan.Depth * Cell;
            float z0 = plan.South == QuayWalk.End.Line ? 0f : QuayWalk.Band * Cell;
            float z1 = plan.Length * Cell - (plan.North == QuayWalk.End.Line ? 0f : QuayWalk.Band * Cell);
            var inner = new Bounds(new Vector3((x0 + x1) * 0.5f, 1f, (z0 + z1) * 0.5f),
                                   new Vector3(x1 - x0 - 0.2f, 2f, z1 - z0 - 0.2f));
            var pavement = CorePavement.Around(new[] { inner }, QuayWalk.Band);
            var under = new GameObject("Pavement").transform;
            under.SetParent(root, false);
            int laid = CorePavement.Lay(pavement, stand, under, out said, 0f, seed, true, false);

            var strip = new Rect(0f, 0f, plan.Depth * Cell, plan.Length * Cell);
            var gone = new List<GameObject>();
            foreach (Transform piece in under)
            {
                if (!WorldBox(piece.gameObject, out var box)) continue;
                if (!strip.Contains(new Vector2(box.center.x, box.center.z))) gone.Add(piece.gameObject);
            }
            foreach (var go in gone) UnityEngine.Object.DestroyImmediate(go);
            return laid - gone.Count;
        }

        /// <summary>What the composer found wrong with its own work, in a line.</summary>
        public static string Report(Stood stood)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"   {stood.Plan.Depth * Cell:F0} x {stood.Plan.Length * Cell:F0} m: {stood.Gaps} cell(s) with no floor, " +
                      $"{stood.RailGap:F1} m of railing missing, {stood.OnWalk} thing(s) in the way; " +
                      $"{stood.Lamps} lamp(s), {stood.Rings} ring(s), {stood.Ladders} ladder(s), {stood.Benches} bench(es), " +
                      $"{stood.BinCount} bin(s), {stood.TreeCount} tree(s), {stood.PalmCount} palm(s), {stood.Planters} planter(s), " +
                      $"{stood.Kiosks} kiosk(s), {stood.Tables} table(s), {stood.Carts} cart(s), " +
                      $"{stood.BoatCount} boat(s), {stood.Programmes} programme(s)" + (stood.Wheel ? ", the wheel" : "") +
                      (stood.DinerStood ? ", the diner" : ""));
            if (stood.InTheWay.Count > 0)
                sb.Append(Environment.NewLine).Append("   WARNING: in the way: ").Append(string.Join(", ", stood.InTheWay));
            if (!string.IsNullOrEmpty(stood.Refused))
                sb.Append(Environment.NewLine).Append("   refused: ").Append(stood.Refused);
            if (Missing.Count > 0)
                sb.Append(Environment.NewLine).Append("   WARNING: missing from the project: ")
                  .Append(string.Join(", ", Missing));
            return sb.ToString();
        }
    }
}
