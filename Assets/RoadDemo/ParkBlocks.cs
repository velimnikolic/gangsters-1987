using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>
    /// What stands on a park, composed piece by piece from the plan <see cref="ParkWalk"/>
    /// drew.
    ///
    /// The same bargain <see cref="IndustrialBlocks"/> struck with the industrial quarter:
    /// one delegate says how a prefab is raised (the editor wants
    /// <c>PrefabUtility.InstantiatePrefab</c> so a bake keeps its links, the game a plain
    /// <c>Instantiate</c>), the size comes in from outside, and nothing here knows which of
    /// the two called it.
    ///
    /// EVERY NUMBER BELOW IS MEASURED, off the two parks the POLYGON City artists drew and
    /// off the pieces themselves (2026-08-26). Block-08 is thirty metres square: a ring of
    /// pavement, sixteen tiles of ground inside it - ten grass, six walk - a fence on every
    /// edge tile broken only where the walk goes out, six trees at half a metre to a metre
    /// OFF the grid in loose pairs beside the walk, two benches within a metre and a half of
    /// the walk's edge, two picnic tables out on the grass, two lamps, two bins and
    /// thirty-eight flowers. That is 2.4 trees per hundred square metres of grass, and it is
    /// the density this aims at.
    ///
    /// What it will not do is invent. There is no lake in this project (a water plane with no
    /// shore is not a lake), no bandstand, no memorial wall - so a park has none of those.
    /// </summary>
    public static class ParkBlocks
    {
        public const float Cell = ParkWalk.Cell;

        /// <summary>Prefabs the project has not got, met while composing - the composer's
        /// shared list (<see cref="Composer.Missing"/>), kept here for the callers that ask
        /// the park.</summary>
        public static IReadOnlyList<string> Missing => Composer.Missing;
        public static void ForgetMissing() => Composer.ForgetMissing();

        // ------------------------------------------------------------------- the pieces

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string TownEnv = "Assets/Synty/PolygonTown/Prefabs/Environment/";
        const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string GenProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        const string KitBld = "Assets/CityKit/Buildings/";
        const string FX = "Assets/Synty/PolygonPalmCity/Prefabs/FX/";

        // the ground - all of it the pack's 5 m module, one tile to a cell
        const string Grass = CityEnv + "SM_Env_Grass_01.prefab";
        const string WalkStraight = CityEnv + "SM_Env_GrassPath_Straight_01.prefab";
        const string WalkCorner = CityEnv + "SM_Env_GrassPath_Corner_01.prefab";
        const string WalkTee = CityEnv + "SM_Env_GrassPath_T_01.prefab";
        const string WalkJunction = CityEnv + "SM_Env_GrassPath_Junction_01.prefab";

        /// <summary>The paved apron a fountain stands on. The pack's own Path pieces are
        /// 2.5 m wide - half a cell - so they are a garden path, not a plaza; the plain
        /// pavement square is the piece that fills a cell, and it is what the artists pave
        /// with everywhere else.</summary>
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";

        // the fence: a 5 m panel on the line, and a post at every join
        const string FencePanel = CityEnv + "SM_Env_Fence_01.prefab";
        const string FencePost = CityEnv + "SM_Env_Fence_End_01.prefab";

        // what stands on the grass
        const string Bench = CityProps + "SM_Prop_ParkBench_01.prefab";
        const string PicnicTable = CityProps + "SM_Prop_PicnicTable_01.prefab";
        const string Flower = CityEnv + "SM_Env_Flower_01.prefab";

        /// <summary>The lamp INSIDE a park is the plain post - the arm-and-box one is a
        /// street lamp and belongs on the kerb, which is where block-08 has its four of
        /// them and its two of these.</summary>
        const string ParkLamp = CityProps + "SM_Prop_LightPole_Base_02.prefab";

        static readonly string[] Bins =
        {
            CityProps + "SM_Prop_Trashbin_01.prefab",
            CityProps + "SM_Prop_Trashbin_02.prefab",
        };

        /// <summary>The city pack's own three, which is what both of the artists' parks are
        /// planted with. Their crowns measure 1.9 to 2.2 m across.</summary>
        static readonly string[] Trees =
        {
            CityEnv + "SM_Env_Tree_01.prefab",
            CityEnv + "SM_Env_Tree_02.prefab",
            CityEnv + "SM_Env_Tree_03.prefab",
        };

        // NO PALMS ARE PLANTED HERE, and it was tried. The core's pavements carry them and a
        // park in this city ought to match its street - but a palm is eight to ten metres
        // across against a city tree's two, so three of them in a thirty metre park read as a
        // park with three trees in it and nothing else: they took half the ground the benches
        // and tables wanted, and two of them hung out over the railings.
        //
        // The artists agree, and it is measurable: block-08 has two palms and BOTH stand on
        // the pavement outside the fence, in their grates, put there by the same pass that
        // plants every other kerb in the core (CorePavement.Plant). Inside the fence it is
        // City Tree_01/02/03 and nothing else.

        // the programmes
        const string Fountain = PalmProps + "SM_Prop_Fountain_01.prefab";
        const string FountainWater = FX + "FX_Fountain_Water_01.prefab";
        const string Pavilion = PalmProps + "SM_Prop_Pavilion_01.prefab";
        const string CourtTennis = PalmEnv + "SM_Env_Court_Tennis_01.prefab";
        const string CourtBasket = PalmEnv + "SM_Env_Court_BasketBall_01.prefab";
        const string TennisNet = PalmProps + "SM_Prop_Tennis_Net_01.prefab";
        const string BasketHoop = PalmProps + "SM_Prop_Basketball_Hoop_01.prefab";
        const string Skatepark = KitBld + "building-skatepark.prefab";
        const string Toilet = KitBld + "building-park-toilet.prefab";

        static readonly string[] Statues =
        {
            GenProps + "SM_Gen_Prop_Statue_01.prefab", GenProps + "SM_Gen_Prop_Statue_02.prefab",
            GenProps + "SM_Gen_Prop_Statue_03.prefab", GenProps + "SM_Gen_Prop_Statue_04.prefab",
        };

        // the playground, and the wooden fence round it
        const string Sandpit = TownProps + "SM_Prop_Sandpit_01.prefab";
        const string PlayFort = TownProps + "SM_Prop_Playground_Fort_01.prefab";
        const string PlaySlide = TownProps + "SM_Prop_Playground_Slide_01.prefab";
        const string PlaySwing = TownProps + "SM_Prop_Playground_Swing_01.prefab";
        const string PlayRoundabout = TownProps + "SM_Prop_Playground_Roundabout_01.prefab";
        const string PlayTable = TownProps + "SM_Prop_Playground_Table_01.prefab";
        const string WoodPanel = TownEnv + "SM_Env_Fence_Wood_Straight_01.prefab";
        const string WoodPost = TownEnv + "SM_Env_Fence_Wood_Post_01.prefab";
        const string WoodGate = TownEnv + "SM_Env_Fence_Wood_Gate_01.prefab";

        // --------------------------------------------------------------------- the park

        /// <summary>One park, standing - and what the composer could see wrong with its own
        /// work.</summary>
        public sealed class Stood
        {
            public ParkWalk.Plan Plan;
            public Transform Root;

            /// <summary>Cells of the park with nothing on the floor at all, which a park
            /// dropped into the city would be seen straight through.</summary>
            public int Gaps;

            /// <summary>Metres of fence that should be standing and are not, the gates
            /// discounted.</summary>
            public float FenceGap;

            /// <summary>Trees standing on the walk, which is the fault nobody notices from
            /// above and everybody notices from a car.</summary>
            public int OnWalk;

            public int TreeCount, Benches, BinCount, Lamps, Tables, Flowers, Programmes;

            /// <summary>Grass, in square metres - what the tree density is reckoned
            /// against.</summary>
            public float Lawn;

            /// <summary>Trees per hundred square metres of grass. The artists' own parks run
            /// at 2.4 and 2.2; under 1.5 a park reads as a field, over 3 as a wood.</summary>
            public float Density => Lawn <= 0f ? 0f : TreeCount * 100f / Lawn;

            public string Refused = "";
        }

        /// <summary>
        /// Stands a whole park: the ground, the fence, the programmes, the planting and the
        /// furniture, in that order and no other.
        ///
        /// The order is the whole of it. The programmes have to be down before the trees, or
        /// a tree stands where the bandstand goes; the trees before the benches, so a bench
        /// is not put under one; the tiles LAST, because everything above books the ground it
        /// stands on and the floor pass fills whatever is left - the same rule the industrial
        /// block follows, arrived at the same way.
        /// </summary>
        public static Stood Compose(ParkWalk.Plan plan, Transform root, System.Random rng,
                                    Func<GameObject, Transform, GameObject> raise)
        {
            Begin(raise);

            var stood = new Stood { Plan = plan, Root = root };

            // the made way is booked BEFORE anything is placed; the aprons only afterwards.
            // Booked together, the fountain and the monument could not stand on the very
            // paving laid for them - both were refused without a word, and a park came out
            // with a bare 15 m square of stone in the middle of it
            BookTheWay(plan, false);
            Programmes(plan, root, rng, stood);
            BookTheWay(plan, true);
            Belt(plan, root, rng, stood);
            Copses(plan, root, rng, stood);
            Furniture(plan, root, rng, stood);
            Fence(plan, root, stood);
            Floor(plan, root, stood);

            foreach (var room in plan.Rooms)
                if (room.Programme == ParkWalk.Programme.Lawn)
                    stood.Lawn += room.Area * Cell * Cell;

            stood.OnWalk = OnTheWalk(plan, root);
            stood.Refused = Worst();
            return stood;
        }

        /// <summary>
        /// Books the made way through every cell of walk, so nothing is set down on the path.
        ///
        /// THE WAY, NOT THE CELL. A cell is five metres and the path through it is about two
        /// and a half - the pack's tile is grass with a made way up the middle - so booking
        /// whole cells would forbid the very places the artists use: block-08 stands a lamp
        /// and a dozen flowers on cells that are walk, out at the edge of the tile where the
        /// grass is. Booked cell by cell instead, the first drawing came out with no lamp at
        /// all, every one of them refused.
        ///
        /// So: a square in the middle of the cell, and an arm of the same width out towards
        /// every side the walk carries on to. That is the shape of the tile, and what is left
        /// over is the grass at its corners.
        /// </summary>
        static void BookTheWay(ParkWalk.Plan plan, bool aprons)
        {
            foreach (var way in Ways(plan, aprons)) Claim(way);
        }

        /// <summary>The made way itself, rectangle by rectangle - what is booked, and what a
        /// tree is measured against afterwards. One reckoning, so the rule that places and the
        /// rule that judges cannot drift apart.</summary>
        static List<Rect> Ways(ParkWalk.Plan plan, bool aprons = true)
        {
            var ways = new List<Rect>();
            float wide = ParkWalk.WalkWide, half = wide * 0.5f, mid = Cell * 0.5f;
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (!plan.Walked(i, j)) continue;
                    float x = i * Cell, z = j * Cell;

                    if (plan.Cells[i, j] == ParkWalk.Ground.Plaza)
                    {
                        // an apron IS paved corner to corner - but it is the fountain's
                        // ground, so it is not booked until the fountain is standing on it
                        if (aprons) ways.Add(new Rect(x, z, Cell, Cell));
                        continue;
                    }

                    ways.Add(new Rect(x + mid - half, z + mid - half, wide, wide));
                    ParkWalk.Arms(plan, new ParkWalk.Spot(i, j), out bool n, out bool s,
                                  out bool e, out bool w);
                    if (n) ways.Add(new Rect(x + mid - half, z + mid, wide, mid));
                    if (s) ways.Add(new Rect(x + mid - half, z, wide, mid));
                    if (e) ways.Add(new Rect(x + mid, z + mid - half, mid, wide));
                    if (w) ways.Add(new Rect(x, z + mid - half, mid, wide));
                }
            return ways;
        }

        /// <summary>
        /// Trees standing over the walk, counted off what ACTUALLY STOOD rather than off what
        /// was asked for.
        ///
        /// The placing test only ever asks about a tree's middle, and a crown is two metres
        /// across: a trunk planted a foot from the edge of the path puts its head over the
        /// path, which from the ground is a tree you walk into. Measured the same way the
        /// industrial block counts fence panels standing inside a building - by looking at
        /// the pieces, because a rule that checks itself always passes.
        /// </summary>
        static int OnTheWalk(ParkWalk.Plan plan, Transform root)
        {
            var wood = root.Find("Trees");
            if (wood == null) return 0;

            var ways = Ways(plan);
            int over = 0;
            foreach (Transform tree in wood)
            {
                if (!WorldBox(tree.gameObject, out var box)) continue;
                // the trunk is what stands in the way; a crown that laps a path is a tree to
                // walk under, which is the point of a tree beside a path. And it is measured
                // against the WAY, not the cell - a tree at the edge of a path tile is
                // standing on grass, which is where the artists put half of theirs
                var stem = new Rect(box.center.x - 0.45f, box.center.z - 0.45f, 0.9f, 0.9f);
                foreach (var way in ways)
                    if (way.Overlaps(stem)) { over++; break; }
            }
            return over;
        }

        // ---------------------------------------------------------------------- the floor

        /// <summary>
        /// The tiles: grass everywhere the plan says grass, the path set on the walk, plain
        /// paving on an apron.
        ///
        /// The pavement ring is NOT laid here - <see cref="CorePavement"/> lays it, the same
        /// kerb the rest of the city stands on, so a park's pavement is the pavement of the
        /// block next door and carries the same lamps.
        /// </summary>
        static void Floor(ParkWalk.Plan plan, Transform root, Stood stood)
        {
            var ground = new GameObject("Ground").transform;
            ground.SetParent(root, false);

            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    GameObject tile;
                    switch (plan.Cells[i, j])
                    {
                        case ParkWalk.Ground.Walk:
                            ParkWalk.Tile(plan, new ParkWalk.Spot(i, j), out var piece, out int yaw);
                            tile = Tile(Path(piece), ground, i, j, yaw);
                            break;
                        case ParkWalk.Ground.Plaza:
                            tile = Tile(Paving, ground, i, j, 0f);
                            break;
                        default:
                            tile = Tile(Grass, ground, i, j, 0f);
                            break;
                    }
                    if (tile == null) stood.Gaps++;
                }
        }

        static string Path(ParkWalk.Piece piece)
        {
            switch (piece)
            {
                case ParkWalk.Piece.Corner: return WalkCorner;
                case ParkWalk.Piece.Tee: return WalkTee;
                case ParkWalk.Piece.Junction: return WalkJunction;
                default: return WalkStraight;
            }
        }

        // ---------------------------------------------------------------------- the fence

        /// <summary>
        /// The railings: a 5 m panel on every edge cell of the ground, and a post at every
        /// join - which is exactly what block-08 has (13 panels, 16 posts on a 6 x 6 block),
        /// and the gaps in it are exactly its three gates.
        ///
        /// The line the fence stands on is the boundary between the grass and the pavement,
        /// not the middle of the edge cell: measured off the artists' own, every panel sits
        /// on the cell line with the posts on the 5 m beat.
        /// </summary>
        static void Fence(ParkWalk.Plan plan, Transform root, Stood stood)
        {
            var rails = new GameObject("Fence").transform;
            rails.SetParent(root, false);

            var gates = new HashSet<ParkWalk.Spot>();
            foreach (var mouth in plan.Mouths) gates.Add(mouth.At);

            float wanted = 0f, laid = 0f;
            for (int k = 0; k < 4; k++)
            {
                var side = (ParkWalk.Side)k;
                // a shared boundary is the neighbour's business unless this park lays it,
                // and a park nobody shares is all its own
                if (plan.Sides[k].Rim == ParkWalk.Rim.Party && !plan.Sides[k].Lays) continue;

                bool along = side == ParkWalk.Side.South || side == ParkWalk.Side.North;
                int from = along ? plan.I0 : plan.J0;
                int to = along ? plan.I1 : plan.J1;
                // the line it stands on, and which way the panel runs
                float line = side == ParkWalk.Side.South ? plan.J0 * Cell
                           : side == ParkWalk.Side.North ? (plan.J1 + 1) * Cell
                           : side == ParkWalk.Side.West ? plan.I0 * Cell
                           : (plan.I1 + 1) * Cell;

                for (int at = from; at <= to; at++)
                {
                    var cell = along ? new ParkWalk.Spot(at, side == ParkWalk.Side.South ? plan.J0 : plan.J1)
                                     : new ParkWalk.Spot(side == ParkWalk.Side.West ? plan.I0 : plan.I1, at);
                    wanted += Cell;
                    if (gates.Contains(cell)) continue;              // the way in

                    // the panel straddles the line rather than standing behind it: measured
                    // off the artists' own, the rail is centred on the boundary between the
                    // grass and the pavement
                    float thick = Mathf.Max(0.1f, Box(FencePanel).size.z);
                    float x = (along ? at * Cell : line) - (along ? 0f : thick * 0.5f);
                    float z = (along ? line : at * Cell) - (along ? thick * 0.5f : 0f);
                    if (Lay(FencePanel, rails, x, z, along ? Cell : thick,
                            along ? thick : Cell, along ? 0f : 90f) != null)
                        laid += Cell;
                }

                // and a post at every join along the run, gates included - the posts are the
                // gateposts there, which is how the artists' own gates read
                for (int at = from; at <= to + 1; at++)
                {
                    float x = along ? at * Cell : line;
                    float z = along ? line : at * Cell;
                    Sit(FencePost, rails, x, z, 0f);
                }
            }
            stood.FenceGap = Mathf.Max(0f, wanted - laid - plan.Mouths.Count * Cell);
        }

        // ------------------------------------------------------------------ the programmes

        /// <summary>What goes in each room, room by room. Everything books the ground it
        /// stands on, so the planting and the furniture that follow keep off it.</summary>
        static void Programmes(ParkWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var under = new GameObject("Programmes").transform;
            under.SetParent(root, false);

            foreach (var room in plan.Rooms)
            {
                var box = Fit(plan, room);
                if (box.width < 1f || box.height < 1f) continue;
                switch (room.Programme)
                {
                    case ParkWalk.Programme.Fountain: Water(plan, under, room, box, rng, stood); break;
                    case ParkWalk.Programme.Statue: Monument(plan, under, room, box, rng, stood); break;
                    case ParkWalk.Programme.Playground: Playground(under, box, rng, stood); break;
                    case ParkWalk.Programme.Courts: Courts(under, box, rng, stood); break;
                    case ParkWalk.Programme.Pavilion: Bandstand(under, box, rng, stood); break;
                    case ParkWalk.Programme.Skatepark: Skate(under, box, rng, stood); break;
                    case ParkWalk.Programme.Toilet: Toilets(plan, under, room, box, rng, stood); break;
                    case ParkWalk.Programme.Grove: break;         // planted with the trees
                    default: break;                               // the lawn stays empty, on purpose
                }
            }
        }

        /// <summary>
        /// The ground a room actually offers, in metres: its biggest whole rectangle, less
        /// half a metre all round so nothing laps onto the walk.
        ///
        /// The BIGGEST WHOLE RECTANGLE and not the bounding box, which is the same
        /// distinction the casting makes (ParkWalk.Room.InnerW) and for the same reason: a
        /// ring of grass round a loop has a bounding box the size of the park and two cells
        /// of ground anywhere in it.
        /// </summary>
        static Rect Fit(ParkWalk.Plan plan, ParkWalk.Room room)
        {
            const float Edge = 0.5f;
            int w = Mathf.Max(1, room.InnerW), d = Mathf.Max(1, room.InnerD);
            return Rect.MinMaxRect(room.InnerI * Cell + Edge, room.InnerJ * Cell + Edge,
                                   (room.InnerI + w) * Cell - Edge, (room.InnerJ + d) * Cell - Edge);
        }

        /// <summary>The fountain on its apron, with the water playing and benches round it
        /// facing in. The apron is already paved by the plan; this only fills it.</summary>
        static void Water(ParkWalk.Plan plan, Transform under, ParkWalk.Room room, Rect box,
                          System.Random rng, Stood stood)
        {
            var middle = Middle(plan, room, ParkWalk.Ground.Plaza, box);
            if (Prop(Fountain, under, middle.x, middle.y, 90f * rng.Next(4), 1.15f) == null) return;
            stood.Programmes++;

            var water = Raise(FountainWater, under);
            if (water != null)
            {
                // the jet sits on the fountain's own rim rather than on the grass
                float lip = Box(Fountain).size.y * 0.55f;
                water.transform.position = new Vector3(middle.x, lip, middle.y);
            }

            // four benches facing the water, on the corners of the apron
            float step = Box(Fountain).size.x * 0.5f + 2.2f;
            for (int k = 0; k < 4; k++)
            {
                float yaw = k * 90f;
                var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, step);
                float x = middle.x + spot.x, z = middle.y + spot.z;
                if (!box.Contains(new Vector2(x, z))) continue;
                // turned to LOOK AT the fountain: the bench's back is its +z
                if (Prop(Bench, under, x, z, yaw + 180f) != null) stood.Benches++;
            }
        }

        /// <summary>A monument on its apron, with flowers round the foot of it.</summary>
        static void Monument(ParkWalk.Plan plan, Transform under, ParkWalk.Room room, Rect box,
                             System.Random rng, Stood stood)
        {
            var middle = Middle(plan, room, ParkWalk.Ground.Plaza, box);
            var statue = Any(Statues, rng);
            if (Prop(statue, under, middle.x, middle.y, 90f * rng.Next(4), 1.6f) == null) return;
            stood.Programmes++;

            for (int k = 0; k < 8; k++)
            {
                float turn = k * 45f + Between(rng, -12f, 12f);
                float out0 = Between(rng, 1.6f, 2.4f);
                var spot = Quaternion.Euler(0f, turn, 0f) * new Vector3(0f, 0f, out0);
                float x = middle.x + spot.x, z = middle.y + spot.z;
                if (!box.Contains(new Vector2(x, z))) continue;
                if (Sit(Flower, under, x, z, Between(rng, 0f, 360f)) != null) stood.Flowers++;
            }
        }

        /// <summary>
        /// The playground: a sandpit with the swing, the fort, the slide and the roundabout
        /// round it, all inside a wooden fence with one gate onto the walk.
        ///
        /// Fenced because a playground in 1987 is fenced - and because the fence is what
        /// makes it read as a playground from above rather than as four toys on a lawn.
        /// </summary>
        static void Playground(Transform under, Rect box, System.Random rng, Stood stood)
        {
            // fifteen metres square is the size the five pieces want; more than that and the
            // fence is round a lot of empty sand
            float side = Mathf.Min(15f, Mathf.Min(box.width, box.height));
            var yard = new Rect(box.center.x - side * 0.5f, box.center.y - side * 0.5f, side, side);
            if (side < 8f || !Room(yard)) return;

            var pen = new GameObject("playground").transform;
            pen.SetParent(under, false);

            float x0 = yard.xMin, z0 = yard.yMin, x1 = yard.xMax, z1 = yard.yMax;
            float panel = Box(WoodPanel).size.x;
            if (panel < 0.5f) panel = 2.5f;
            int gate = rng.Next(4);                       // which side the gate is on

            for (int k = 0; k < 4; k++)
            {
                bool along = k == 0 || k == 1;
                float line = k == 0 ? z0 : k == 1 ? z1 : k == 2 ? x0 : x1;
                float run = along ? yard.width : yard.height;
                int panels = Mathf.Max(1, Mathf.RoundToInt(run / panel));
                float each = run / panels;
                int hole = k == gate ? panels / 2 : -1;
                for (int p = 0; p < panels; p++)
                {
                    float at = (along ? x0 : z0) + p * each;
                    string piece = p == hole ? WoodGate : WoodPanel;
                    if (along) Lay(piece, pen, at, line - 0.07f, each, 0.15f, 0f);
                    else Lay(piece, pen, line - 0.07f, at, 0.15f, each, 90f);
                }
                for (int p = 0; p <= panels; p++)
                {
                    float at = (along ? x0 : z0) + p * each;
                    Sit(WoodPost, pen, along ? at : line, along ? line : at, 0f);
                }
            }
            Claim(yard);
            stood.Programmes++;

            var mid = yard.center;
            Sit(Sandpit, pen, mid.x, mid.y, 90f * rng.Next(4));
            // the four pieces on the corners of the sand, each facing the middle
            var kit = new[] { PlaySwing, PlayFort, PlaySlide, PlayRoundabout };
            for (int k = 0; k < kit.Length; k++)
            {
                float turn = k * 90f + 45f;
                float out0 = side * 0.29f;
                var spot = Quaternion.Euler(0f, turn, 0f) * new Vector3(0f, 0f, out0);
                Sit(kit[k], pen, mid.x + spot.x, mid.y + spot.z, turn + 180f);
            }
            if (side >= 12f) Sit(PlayTable, pen, mid.x, mid.y - side * 0.36f, 0f);
        }

        /// <summary>
        /// The courts: two tennis courts side by side, or one basketball court where there is
        /// only room for that. Ten metres between the tennis courts, which is what the palm
        /// city's own demo lays them at.
        /// </summary>
        static void Courts(Transform under, Rect box, System.Random rng, Stood stood)
        {
            var tennis = Box(CourtTennis).size;
            bool sideways = box.width < box.height;
            float wide = sideways ? box.height : box.width;
            float deep = sideways ? box.width : box.height;
            float yaw = sideways ? 90f : 0f;

            bool pair = wide >= tennis.x * 2f + 2.5f && deep >= tennis.z;
            if (pair)
            {
                float gap = Mathf.Min(10f, (wide - tennis.x * 2f));
                float span = tennis.x * 2f + gap;
                for (int k = 0; k < 2; k++)
                {
                    float at = box.center.x - span * 0.5f + tennis.x * 0.5f + k * (tennis.x + gap);
                    float x = sideways ? box.center.x : at;
                    float z = sideways ? at : box.center.y;
                    if (sideways) { x = box.center.x; z = box.center.y - span * 0.5f + tennis.x * 0.5f + k * (tennis.x + gap); }
                    var court = new Rect(x - tennis.x * 0.5f, z - tennis.z * 0.5f, tennis.x, tennis.z);
                    if (sideways) court = new Rect(x - tennis.z * 0.5f, z - tennis.x * 0.5f, tennis.z, tennis.x);
                    if (!Room(court)) continue;
                    Stand(CourtTennis, under, x, z, yaw);
                    Sit(TennisNet, under, x, z, yaw + 90f);
                    Claim(court);
                    stood.Programmes++;
                }
                return;
            }

            var basket = Box(CourtBasket).size;
            var floor = sideways
                ? new Rect(box.center.x - basket.z * 0.5f, box.center.y - basket.x * 0.5f, basket.z, basket.x)
                : new Rect(box.center.x - basket.x * 0.5f, box.center.y - basket.z * 0.5f, basket.x, basket.z);
            if (!Room(floor)) return;
            Stand(CourtBasket, under, box.center.x, box.center.y, yaw);
            Claim(floor);
            stood.Programmes++;
            // a hoop at each end, facing in down the long axis
            for (int k = 0; k < 2; k++)
            {
                float out0 = basket.z * 0.5f - 0.4f;
                var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, k == 0 ? out0 : -out0);
                Sit(BasketHoop, under, box.center.x + spot.x, box.center.y + spot.z,
                    yaw + (k == 0 ? 180f : 0f));
            }
        }

        /// <summary>The pavilion, with benches round it - the nearest thing this project has
        /// to a bandstand, and near enough.</summary>
        static void Bandstand(Transform under, Rect box, System.Random rng, Stood stood)
        {
            if (Prop(Pavilion, under, box.center.x, box.center.y, 90f * rng.Next(4), 1.1f) == null) return;
            stood.Programmes++;

            float step = Mathf.Max(Box(Pavilion).size.x, Box(Pavilion).size.z) * 0.5f + 2f;
            for (int k = 0; k < 4; k++)
            {
                float yaw = k * 90f + 45f;
                var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, step);
                float x = box.center.x + spot.x, z = box.center.y + spot.z;
                if (!box.Contains(new Vector2(x, z))) continue;
                if (Prop(Bench, under, x, z, yaw + 180f) != null) stood.Benches++;
            }
        }

        /// <summary>The skatepark: one piece, and a big one - forty metres by thirty, which
        /// is a city block of a thing and why it is only ever dealt once in a city.</summary>
        static void Skate(Transform under, Rect box, System.Random rng, Stood stood)
        {
            var size = Box(Skatepark).size;
            bool sideways = box.width < box.height;
            float yaw = sideways ? 90f : 0f;
            var foot = sideways ? new Vector2(size.z, size.x) : new Vector2(size.x, size.z);
            if (foot.x > box.width || foot.y > box.height) return;

            var floor = new Rect(box.center.x - foot.x * 0.5f, box.center.y - foot.y * 0.5f,
                                 foot.x, foot.y);
            if (!Room(floor)) return;
            Stand(Skatepark, under, box.center.x, box.center.y, yaw);
            Claim(floor);
            stood.Programmes++;
        }

        /// <summary>The toilet block, with its back to the fence - which is where a park puts
        /// one, and it is the only programme that wants the edge.</summary>
        static void Toilets(ParkWalk.Plan plan, Transform under, ParkWalk.Room room, Rect box,
                            System.Random rng, Stood stood)
        {
            var size = Box(Toilet).size;
            // the fence it backs onto, if the room touches one
            float x = box.center.x, z = box.center.y, yaw = 0f;
            if (room.J0 <= plan.J0) { z = box.yMin + size.z * 0.5f; yaw = 0f; }
            else if (room.J0 + room.D - 1 >= plan.J1) { z = box.yMax - size.z * 0.5f; yaw = 180f; }
            else if (room.I0 <= plan.I0) { x = box.xMin + size.z * 0.5f; yaw = 90f; }
            else if (room.I0 + room.W - 1 >= plan.I1) { x = box.xMax - size.z * 0.5f; yaw = 270f; }

            if (Prop(Toilet, under, x, z, yaw, 1.1f) != null) stood.Programmes++;
        }

        /// <summary>The middle of a room, preferring the ground the plan already paved for
        /// it - a fountain belongs on its apron and not two metres off it.</summary>
        static Vector2 Middle(ParkWalk.Plan plan, ParkWalk.Room room, ParkWalk.Ground want, Rect box)
        {
            float sx = 0f, sz = 0f;
            int many = 0;
            for (int i = room.I0; i < room.I0 + room.W; i++)
                for (int j = room.J0; j < room.J0 + room.D; j++)
                {
                    if (!plan.In(i, j) || plan.Cells[i, j] != want) continue;
                    sx += i * Cell + Cell * 0.5f;
                    sz += j * Cell + Cell * 0.5f;
                    many++;
                }
            return many == 0 ? box.center : new Vector2(sx / many, sz / many);
        }

        // ------------------------------------------------------------------- the planting

        /// <summary>
        /// The belt of trees against the fence.
        ///
        /// A tree every two or three cells, half a metre to a metre and a half in from the
        /// line and stepped ALONG it as well - because not one of block-08's six trees stands
        /// on the middle of a cell, and a row that did would read as an orchard.
        /// </summary>
        static void Belt(ParkWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var wood = new GameObject("Trees").transform;
            wood.SetParent(root, false);

            int step = 2 + rng.Next(2);
            for (int k = 0; k < 4; k++)
            {
                var side = (ParkWalk.Side)k;
                bool along = side == ParkWalk.Side.South || side == ParkWalk.Side.North;
                int from = along ? plan.I0 : plan.J0;
                int to = along ? plan.I1 : plan.J1;
                int line = side == ParkWalk.Side.South ? plan.J0
                         : side == ParkWalk.Side.North ? plan.J1
                         : side == ParkWalk.Side.West ? plan.I0 : plan.I1;
                int phase = rng.Next(step);

                for (int at = from; at <= to; at++)
                {
                    if ((at + phase) % step != 0) continue;
                    int i = along ? at : line, j = along ? line : at;
                    if (plan.Cells[i, j] != ParkWalk.Ground.Grass) continue;

                    // in from the fence, and off the middle of its cell
                    float inward = Between(rng, 1.2f, 2.6f);
                    float slide = Between(rng, -1.4f, 1.4f);
                    float x = i * Cell + Cell * 0.5f, z = j * Cell + Cell * 0.5f;
                    if (side == ParkWalk.Side.South) { z = j * Cell + inward; x += slide; }
                    else if (side == ParkWalk.Side.North) { z = (j + 1) * Cell - inward; x += slide; }
                    else if (side == ParkWalk.Side.West) { x = i * Cell + inward; z += slide; }
                    else { x = (i + 1) * Cell - inward; z += slide; }

                    Plant(plan, wood, x, z, rng, stood);
                }
            }
        }

        /// <summary>The trees inside: a copse in every room dealt one, and a loose pair or
        /// two beside the walk everywhere else - which is where the artists put theirs.</summary>
        static void Copses(ParkWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var wood = root.Find("Trees") ?? root;
            foreach (var room in plan.Rooms)
            {
                var box = Fit(plan, room);
                if (room.Programme == ParkWalk.Programme.Grove)
                {
                    // a copse: six to ten trees at four to six metres, which is close enough
                    // to read as a wood and open enough to walk through
                    int want = 6 + rng.Next(5);
                    for (int k = 0; k < want * 3 && stood.TreeCount < 400; k++)
                    {
                        float x = Between(rng, box.xMin + 1.5f, box.xMax - 1.5f);
                        float z = Between(rng, box.yMin + 1.5f, box.yMax - 1.5f);
                        if (Plant(plan, wood, x, z, rng, stood)) want--;
                        if (want <= 0) break;
                    }
                    continue;
                }
                if (room.Programme != ParkWalk.Programme.Lawn) continue;

                // beside the walk, in loose pairs - and NEVER in the middle, which is the
                // lawn and stays empty (the one thing you see from above)
                foreach (var cell in room.Cells)
                {
                    if (!Beside(plan, cell)) continue;
                    // a quarter of the cells beside a walk, not half: the belt round the
                    // fence plants its own, and the two together came out at 3.9 trees per
                    // hundred square metres against the artists' 2.2
                    if (!Chance(rng, 0.25)) continue;
                    float x = cell.I * Cell + Between(rng, 1.2f, 3.8f);
                    float z = cell.J * Cell + Between(rng, 1.2f, 3.8f);
                    if (!Plant(plan, wood, x, z, rng, stood)) continue;
                    if (!Chance(rng, 0.35)) continue;
                    Plant(plan, wood, x + Between(rng, -2.6f, 2.6f), z + Between(rng, -2.6f, 2.6f),
                          rng, stood);
                }
            }
        }

        /// <summary>Is this cell of grass next to the walk? A tree goes beside a path, where
        /// it is walked under.</summary>
        static bool Beside(ParkWalk.Plan plan, ParkWalk.Spot cell) =>
            plan.Walked(cell.I + 1, cell.J) || plan.Walked(cell.I - 1, cell.J) ||
            plan.Walked(cell.I, cell.J + 1) || plan.Walked(cell.I, cell.J - 1);

        /// <summary>One tree, if the ground there is grass and nothing else has it. Palms
        /// every so often, because the core's own pavements carry them.</summary>
        static bool Plant(ParkWalk.Plan plan, Transform wood, float x, float z,
                          System.Random rng, Stood stood)
        {
            if (!OnGrass(plan, x, z)) return false;
            // a tree has no facing, so it is turned to any angle at all rather than a quarter
            if (Prop(Any(Trees, rng), wood, x, z, Between(rng, 0f, 360f), 0.75f) == null) return false;
            stood.TreeCount++;
            return true;
        }

        /// <summary>Is this point on grass - not the walk, not an apron, not the pavement?
        /// The whole tree, not just its middle: a trunk on the grass with its crown over the
        /// path is still a tree in the way.</summary>
        static bool OnGrass(ParkWalk.Plan plan, float x, float z)
        {
            int i = Mathf.FloorToInt(x / Cell), j = Mathf.FloorToInt(z / Cell);
            return plan.Inside(i, j) && plan.Cells[i, j] == ParkWalk.Ground.Grass;
        }

        // ------------------------------------------------------------------- the furniture

        /// <summary>
        /// Benches, bins, lamps, tables and flowers.
        ///
        /// The rhythm is the artists': a bench every eight to twelve metres of walk, set less
        /// than a metre and a half off its edge and TURNED TO FACE IT; a bin beside every
        /// second bench; a lamp on the corners of the walk; picnic tables out on the grass,
        /// one to two hundred square metres; and flowers in clumps against the fence, which
        /// is where all thirty-eight of block-08's are.
        /// </summary>
        static void Furniture(ParkWalk.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var kit = new GameObject("Furniture").transform;
            kit.SetParent(root, false);

            // a bench every second cell of walk in a pocket park (block-08 has two on six),
            // and one every fifth in a big one - the same rate over eighty metres of loop
            // gave seventeen benches, which is a bus station
            int benchEvery = plan.Klass == ParkWalk.Klass.Pocket ? 2
                           : plan.Klass == ParkWalk.Klass.Square ? 3 : 5;
            int seen = 0, sinceBin = 0;

            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (plan.Cells[i, j] != ParkWalk.Ground.Walk) continue;
                    seen++;

                    // A LAMP WHERE THE WALK TURNS OR FORKS, and one every four cells of it
                    // besides - block-08 has two on six cells of walk, and lighting only the
                    // junctions left a park with a single lamp in it. It stands at the edge
                    // of the tile, on the grass beside the made way, which is where both of
                    // the artists' inside lamps are
                    ParkWalk.Tile(plan, new ParkWalk.Spot(i, j), out var piece, out int yaw);
                    bool junction = piece != ParkWalk.Piece.Straight;
                    if ((junction || seen % 4 == 1) && Corner(i, j, rng, out var post))
                        if (Prop(ParkLamp, kit, post.x, post.y, Between(rng, 0f, 360f)) != null)
                            stood.Lamps++;

                    if (seen % benchEvery != 0) continue;

                    // beside the walk, on the grass, facing back at it
                    if (!Grassward(plan, i, j, rng, out var spot, out float facing)) continue;
                    if (Prop(Bench, kit, spot.x, spot.y, facing) == null) continue;
                    stood.Benches++;

                    if (++sinceBin < 2) continue;
                    sinceBin = 0;
                    var bin = Any(Bins, rng);
                    float bx = spot.x + Mathf.Cos(facing * Mathf.Deg2Rad) * Between(rng, 1.4f, 2.2f);
                    float bz = spot.y - Mathf.Sin(facing * Mathf.Deg2Rad) * Between(rng, 1.4f, 2.2f);
                    if (Prop(bin, kit, bx, bz, Between(rng, 0f, 360f)) != null) stood.BinCount++;
                }

            Tables(plan, kit, rng, stood);
            Flowers(plan, kit, rng, stood);
        }

        /// <summary>
        /// A corner of a walk tile - the grass left over where the made way does not reach.
        ///
        /// Where a lamp goes, and where both of block-08's inside lamps are. Chosen rather
        /// than rolled for: dropped at a random point in the cell, two lamps in three landed
        /// on the path itself and were refused, and a park came out with one lamp in it.
        /// </summary>
        static bool Corner(int i, int j, System.Random rng, out Vector2 spot)
        {
            spot = default;
            float edge = (Cell - ParkWalk.WalkWide) * 0.5f;      // 1.2 m of grass at each side
            if (edge < 0.6f) return false;

            var corners = new List<Vector2>
            {
                new Vector2(i * Cell + edge * 0.7f, j * Cell + edge * 0.7f),
                new Vector2((i + 1) * Cell - edge * 0.7f, j * Cell + edge * 0.7f),
                new Vector2(i * Cell + edge * 0.7f, (j + 1) * Cell - edge * 0.7f),
                new Vector2((i + 1) * Cell - edge * 0.7f, (j + 1) * Cell - edge * 0.7f),
            };
            Dice.Shuffle(corners, rng);
            spot = corners[0];
            return true;
        }

        /// <summary>A spot on the grass beside this cell of walk, and the way something there
        /// would face to look back at the path.</summary>
        static bool Grassward(ParkWalk.Plan plan, int i, int j, System.Random rng,
                              out Vector2 spot, out float facing)
        {
            spot = default;
            facing = 0f;
            var ways = new List<int> { 0, 1, 2, 3 };
            Dice.Shuffle(ways, rng);
            foreach (int way in ways)
            {
                int di = way == 0 ? 1 : way == 1 ? -1 : 0;
                int dj = way == 2 ? 1 : way == 3 ? -1 : 0;
                int ni = i + di, nj = j + dj;
                if (!plan.Inside(ni, nj) || plan.Cells[ni, nj] != ParkWalk.Ground.Grass) continue;

                // a metre or so onto the grass, measured from the line between the two
                float off = Between(rng, 0.8f, 1.5f);
                spot = new Vector2(ni * Cell + Cell * 0.5f - di * (Cell * 0.5f - off),
                                   nj * Cell + Cell * 0.5f - dj * (Cell * 0.5f - off));
                // the bench's back is its +z, so it faces the walk when turned away from it
                facing = di > 0 ? 90f : di < 0 ? 270f : dj > 0 ? 0f : 180f;
                facing = (facing + 180f) % 360f;
                return true;
            }
            return false;
        }

        /// <summary>Picnic tables, out on the open grass where the trees are not - one to two
        /// hundred square metres and never more than three, which is what the artists' two
        /// parks have between them.</summary>
        static void Tables(ParkWalk.Plan plan, Transform kit, System.Random rng, Stood stood)
        {
            foreach (var room in plan.Rooms)
            {
                if (room.Programme != ParkWalk.Programme.Lawn) continue;
                // one to a hundred and twenty-five square metres, which is what block-08 runs
                // at (two tables on two hundred and fifty), and never more than three
                int want = Mathf.Clamp(room.Area * 25 / 125, 0, 3);
                var box = Fit(plan, room);
                for (int k = 0, guard = 0; k < want && guard < want * 8; guard++)
                {
                    float x = Between(rng, box.xMin + 2f, box.xMax - 2f);
                    float z = Between(rng, box.yMin + 2f, box.yMax - 2f);
                    if (!OnGrass(plan, x, z)) continue;
                    if (Prop(PicnicTable, kit, x, z, 90f * rng.Next(4), 1.3f) == null) continue;
                    stood.Tables++;
                    k++;
                }
            }
        }

        /// <summary>Flowers, in clumps against the fence and round the aprons. Block-08 has
        /// thirty-eight of them on four hundred square metres of grass, so this is not a
        /// scattering - it is the ground cover.</summary>
        static void Flowers(ParkWalk.Plan plan, Transform kit, System.Random rng, Stood stood)
        {
            // fifteen to the hundred square metres of grass is the artists' rate; the cap is
            // there because a big park hits it in the corners alone, and four hundred loose
            // quads on one block is a draw call bill nobody agreed to
            int cap = Mathf.Clamp(plan.W * plan.D * 25 * 15 / 100 / 3, 40, 220);
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (plan.Cells[i, j] != ParkWalk.Ground.Grass) continue;
                    if (stood.Flowers >= cap) return;
                    if (!plan.OnFence(i, j) && !Beside(plan, new ParkWalk.Spot(i, j))) continue;
                    if (!Chance(rng, 0.7)) continue;

                    // block-08 carries thirty-eight of them on two hundred and fifty square
                    // metres of grass - fifteen to the hundred - and the first drawing came
                    // out at eight, which reads as a lawn somebody scattered a few seeds on
                    int many = 4 + rng.Next(6);
                    float cx = i * Cell + Between(rng, 1.2f, 3.8f);
                    float cz = j * Cell + Between(rng, 1.2f, 3.8f);
                    for (int k = 0; k < many; k++)
                    {
                        float x = cx + Between(rng, -1.3f, 1.3f);
                        float z = cz + Between(rng, -1.3f, 1.3f);
                        if (!OnGrass(plan, x, z)) continue;
                        // flowers book no ground of their own - a bench beside a clump of
                        // them is still a bench - but they keep OFF ground something is
                        // standing on, or they come up through the sandpit and inside the
                        // fountain, which is where they went the first time
                        if (!Room(new Rect(x - 0.2f, z - 0.2f, 0.4f, 0.4f))) continue;
                        if (Sit(Flower, kit, x, z, Between(rng, 0f, 360f)) != null) stood.Flowers++;
                    }
                }
        }

        // --------------------------------------------------------------------- the pavement

        /// <summary>
        /// The pavement ring, laid by <see cref="CorePavement"/> - the same kerb, the same
        /// corners, the same lamps and bins as every other block in the core, because a park
        /// is a block and its pavement is the city's.
        ///
        /// The park's own ground is handed in as the thing to grow the pavement round, and
        /// the floor under it is left off: the grass is already down.
        /// </summary>
        public static int Pave(ParkWalk.Plan plan, Transform root, out string said,
                               Func<GameObject, Transform, GameObject> stand, int seed)
        {
            var ground = new Bounds(
                new Vector3((plan.I0 + plan.W * 0.5f) * Cell, 1f, (plan.J0 + plan.D * 0.5f) * Cell),
                new Vector3(plan.W * Cell - 0.2f, 2f, plan.D * Cell - 0.2f));

            var pavement = CorePavement.Around(new[] { ground }, ParkWalk.Band);
            var under = new GameObject("Pavement").transform;
            under.SetParent(root, false);
            return CorePavement.Lay(pavement, stand, under, out said, 0f, seed, true, false);
        }

        /// <summary>What the composer found wrong with its own work, in one line - the
        /// counterpart to the raster's report, which judges the roads, and to
        /// <see cref="ParkWalk.Report"/>, which judges the plan. All three have to be nought
        /// for a park to be finished, and they catch different things.</summary>
        public static string Report(Stood stood)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"   {stood.Plan.Name}: {stood.Gaps} cell(s) with no floor, " +
                      $"{stood.FenceGap:F1} m of fence missing, {stood.OnWalk} tree(s) on the walk; " +
                      $"{stood.TreeCount} tree(s) ({stood.Density:F1} per 100 m2 of grass), " +
                      $"{stood.Benches} bench(es), {stood.BinCount} bin(s), {stood.Lamps} lamp(s), " +
                      $"{stood.Tables} table(s), {stood.Flowers} flower(s), " +
                      $"{stood.Programmes} programme(s)");
            if (stood.Density > 0f && (stood.Density < 1.2f || stood.Density > 3.2f))
                sb.Append(Environment.NewLine)
                  .Append($"   WARNING: {stood.Density:F1} trees per 100 m2 - the artists' own parks " +
                          "run at 2.2 to 2.4");
            if (!string.IsNullOrEmpty(stood.Refused))
                sb.Append(Environment.NewLine).Append("   refused: ").Append(stood.Refused);
            if (Missing.Count > 0)
                sb.Append(Environment.NewLine).Append("   WARNING: missing from the project: ")
                  .Append(string.Join(", ", Missing));
            return sb.ToString();
        }
    }
}
