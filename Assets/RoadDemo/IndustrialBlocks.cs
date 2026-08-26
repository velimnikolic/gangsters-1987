using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What stands on one industrial parcel, composed piece by piece.
    ///
    /// This is <c>IndustrialBlockForge</c>'s composition machinery moved out of the editor,
    /// the same move <see cref="CoreRoads"/> made when it came out of <c>CoreCitySketch</c>:
    /// one delegate says how a prefab is raised (the editor wants
    /// <c>PrefabUtility.InstantiatePrefab</c> so a bake keeps its links; the quarter at Play
    /// wants a plain <c>Instantiate</c>) and nothing else here knows the difference. The
    /// forge is now a lab bench standing over this, and the quarter
    /// (<see cref="IndustrialLayout"/>) stands over the same thing - so a fix to a recipe
    /// shows up in both.
    ///
    /// Two things are new, and both come from a parcel being part of a district rather than
    /// a block on its own:
    ///
    ///   - the SIZE comes in. The forge picked its own; the quarter says "eighty by sixty"
    ///     and the recipe has to fill it. Every recipe therefore places against
    ///     <see cref="Block.In"/>/<see cref="Block.Out"/>/<see cref="Block.Near"/>/
    ///     <see cref="Block.Far"/> and never against a number of its own.
    ///   - a side may be a SHARED FENCE instead of a kerb. Two works next door to each other
    ///     have one fence between them, not two fences and a pavement; and the fence is laid
    ///     by exactly one of the pair, or it is two fences in the same place.
    ///
    /// Everything else is the forge's, and its hard-won rules are kept verbatim: the wall is
    /// a closed ring broken only by the gate, buildings stand BEHIND the wall rather than
    /// being it, the floor is calm, a corner is met rather than crossed, and nothing is ever
    /// scaled below the size it was drawn at.
    /// </summary>
    public static class IndustrialBlocks
    {
        public const float Cell = 5f;

        /// <summary>How far a building stands back from the kerb ring, so the wall passes
        /// in FRONT of it. A works sat hard against the ring becomes the wall itself, and
        /// wherever two of them meet at an angle the perimeter has a hole in it.</summary>
        const float Setback = 1.6f;

        /// <summary>And how far it stands back from a shared fence, which needs less: there
        /// is no pavement on that side, only the fence and the neighbour behind it.</summary>
        const float Party = 0.9f;

        // ------------------------------------------------------------------- the pieces

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string CityBld = "Assets/Synty/PolygonCity/Prefabs/Buildings/";
        const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string GangProps = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        const string GangVeh = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string GenProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        const string GenEnv = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
        const string GenBase = "Assets/Synty/PolygonGeneric/Prefabs/Base/";
        const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        const string KitBld = "Assets/CityKit/Buildings/";
        const string KitShips = "Assets/CityKit/Ships/";

        // the ground
        const string Kerb = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string KerbCorner = CityEnv + "SM_Env_Sidewalk_Corner_01.prefab";
        const string Plate = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Asphalt = CityEnv + "SM_Env_Road_Bare_01.prefab";
        const string PaintedBays = CityEnv + "SM_Env_Road_ParkingLines_01.prefab";
        const string Patch = CityEnv + "SM_Env_Road_Patch_01.prefab";

        // the buildings, every one baked with its front on +Z and its floor on y = 0
        const string Factory = KitBld + "building-factory.prefab";
        const string FactoryOld = KitBld + "building-factory-old.prefab";
        const string FactoryHall = KitBld + "building-factory-hall.prefab";
        const string Workshop = KitBld + "building-workshop.prefab";
        const string ShedLarge = KitBld + "building-warehouse-large.prefab";
        const string ShedSmall = KitBld + "building-warehouse-small.prefab";
        const string DepotGarage = KitBld + "building-depot-garage.prefab";
        const string YardShed = KitBld + "building-yard-shed.prefab";
        const string Diner = KitBld + "building-diner.prefab";

        // the perimeter
        const string BrickPanel = GangBld + "SM_Bld_Fence_Brick_01.prefab";
        const string BrickPillar = GangBld + "SM_Bld_Fence_Brick_Pillar_01.prefab";
        const string FencePanel = GangBld + "SM_Bld_Fence_01.prefab";
        const string FenceCrown = GangBld + "SM_Bld_Fence_Wire_01.prefab";
        const string FenceGate = GangBld + "SM_Bld_Fence_Gate_01.prefab";
        const string FencePole = GangBld + "SM_Bld_Fence_Pole_01.prefab";

        // the works
        const string StackMaterial = "Assets/Materials/IndustrialStack.mat";
        const string WaterTower = CityBld + "SM_Prop_Water_Tower_01.prefab";
        const string Smoke = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_Black_Small_01.prefab";
        const string Steam = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Steam_01.prefab";
        const string PipeRiserTall = CityProps + "SM_Prop_Pipe_Preset_01.prefab";
        const string PipeRiserWide = CityProps + "SM_Prop_Pipe_Preset_02.prefab";

        // the yard
        const string LoadingDock = GangBld + "SM_Bld_LoadingDock_02.prefab";
        const string Pallet = GangProps + "SM_Prop_Pallet_01.prefab";
        const string BarrelMetal = GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        const string BarrelPlastic = GangProps + "SM_Prop_Barrel_Plastic_01.prefab";
        const string WireSpool = GangProps + "SM_Prop_Wirespool_01.prefab";
        const string PipeStack = GangProps + "SM_Prop_PipeStack_01.prefab";
        const string Dumpster = GangProps + "SM_Prop_Dumpster_01.prefab";
        const string YardLamp = GangProps + "SM_Prop_Light_Pole_01.prefab";
        const string DangerSign = GangProps + "SM_Prop_Sign_Danger_01.prefab";
        const string KeepOut = GangProps + "SM_Prop_Sign_KeepOut_01.prefab";
        const string CompanySign = GangProps + "SM_Prop_CompanySign_01.prefab";
        const string Ladder = GangProps + "SM_Prop_Ladder_01.prefab";
        const string Forklift = GangVeh + "SM_Veh_Forklift_01.prefab";
        const string BoxLorry = GangVeh + "SM_Veh_Truck_01.prefab";
        const string Van = GangVeh + "SM_Veh_Van_01.prefab";
        const string TownLorry = "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_01.prefab";
        const string Crate = GenProps + "SM_Gen_Prop_Crate_01.prefab";
        const string BurnBarrel = PalmProps + "SM_Prop_Barrel_Burn_01.prefab";
        const string Cone = PalmProps + "SM_Prop_Cone_01.prefab";
        const string Barrier = CityProps + "SM_Prop_Barrier_01.prefab";
        const string Skip = CityProps + "SM_Prop_Skip_01.prefab";
        const string Billboard = CityProps + "SM_Prop_Billboard_01.prefab";
        const string ForSale = TownProps + "SM_Prop_ForSaleSign_01.prefab";
        const string DeadTree = GenEnv + "SM_Gen_Env_Tree_Dead_01.prefab";
        const string Pillar = GenBase + "SM_Bld_Base_Pillar_01.prefab";
        const string GasPump = TownProps + "SM_Prop_Gaspump_01.prefab";
        const string StreetLamp = PalmProps + "SM_Prop_Street_Lamp_01.prefab";
        const string PowerPole = PalmProps + "SM_Prop_Powerpole_01.prefab";
        const string PowerLine = PalmProps + "SM_Prop_Powerline_01.prefab";
        const string Hydrant = PalmProps + "SM_Prop_Fire_Hydrant_01.prefab";
        const string GasPumpBase = TownProps + "SM_Prop_Gaspump_Base_01.prefab";
        const string HoseReel = TownProps + "SM_Prop_HoseReel_01.prefab";

        static readonly string[] Chemicals =
        {
            GangProps + "SM_Prop_Chemical_01.prefab", GangProps + "SM_Prop_Chemical_02.prefab",
            GangProps + "SM_Prop_Chemical_03.prefab", GangProps + "SM_Prop_Chemical_04.prefab",
        };

        // ------------------------------------------------------------------ the weathering
        //
        // Everything below is FLAT, or near enough, and none of it books ground. It is the
        // layer the pack's own demo compound spends most of its prop budget on and the one a
        // composed block had none of: 24 puddles, twenty-odd clumps of weed through the
        // asphalt, loose stone, drains, and a mottle of differently-worn floor tiles over the
        // one surface the floor pass laid everywhere. A few dozen quads, and they do more for
        // the picture than another lorry would.

        const string GangGen = "Assets/Synty/PolygonGangWarfare/Prefabs/Generic/";

        /// <summary>The gang floor tiles are a 3 m module against this block's 5 m one, which
        /// is exactly why they are laid as PATCHES rather than as cells: at their own size
        /// they break the grid up, and stretched to it they would only re-draw it.</summary>
        static readonly string[] WornAsphalt =
        {
            GangBld + "SM_Bld_Floor_Asphalt_02.prefab",
            GangBld + "SM_Bld_Floor_Asphalt_03.prefab",
        };

        static readonly string[] PaintedFloor =
        {
            GangBld + "SM_Bld_Floor_Lines_02.prefab",
            GangBld + "SM_Bld_Floor_Lines_04.prefab",
        };

        /// <summary>
        /// Weed coming up through the asphalt - NOT the pack's Plant_01/02, which are the
        /// demo's grow-house crop and would put a cannabis nursery in the middle of a foundry.
        /// The crack pieces are drawn long in their own z, so a run beside a wall that goes
        /// east-west is laid at a quarter turn.
        /// </summary>
        static readonly string[] WeedClumps =
        {
            GangProps + "SM_Prop_Grass_Cracks_01.prefab", GangProps + "SM_Prop_Grass_Cracks_02.prefab",
            GangProps + "SM_Prop_Grass_Cracks_03.prefab", GangProps + "SM_Prop_Grass_Cracks_04.prefab",
            GangProps + "SM_Prop_Grass_Cracks_05.prefab",
        };

        static readonly string[] Puddles =
        {
            GangProps + "SM_Prop_Puddle_01.prefab", GangProps + "SM_Prop_Puddle_02.prefab",
            GangProps + "SM_Prop_Puddle_03.prefab", GangProps + "SM_Prop_Puddle_04.prefab",
            GangProps + "SM_Prop_Puddle_05.prefab",
        };

        static readonly string[] Rubble =
        {
            GangGen + "SM_Generic_Small_Rocks_01.prefab",
            GangGen + "SM_Generic_Small_Rocks_02.prefab",
        };

        static readonly string[] Litter = { GangProps + "SM_Prop_Papers_01.prefab" };
        static readonly string[] Drains =
        {
            GangProps + "SM_Prop_Manhole_01.prefab",
            GangBld + "SM_Bld_Floor_Drain_01.prefab",
        };
        const string SpeedBump = GangProps + "SM_Prop_Speed_Bump_01.prefab";
        const string Bollard = GangProps + "SM_Prop_Bollard_01.prefab";

        // ------------------------------------------------------------------ the clutter
        //
        // Not a scatter. Each of these is a small STORY - the bags that go beside the bin, the
        // timber and the gas bottles that go against a wall under a caution board, the boxes
        // left where the lorry was unloaded - and that is most of what makes the pack's demo
        // yard read as somewhere people work rather than as a surface with inventory on it.

        static readonly string[][] BagFamilies =
        {
            new[] { GangProps + "SM_Prop_Bag_Blue_01.prefab",
                    GangProps + "SM_Prop_Bag_Blue_02.prefab",
                    GangProps + "SM_Prop_Bag_Blue_03.prefab" },
            new[] { GangProps + "SM_Prop_Bag_Green_01.prefab",
                    GangProps + "SM_Prop_Bag_Green_02.prefab",
                    GangProps + "SM_Prop_Bag_Green_03.prefab" },
            new[] { GangProps + "SM_Prop_Bag_White_01.prefab",
                    GangProps + "SM_Prop_Bag_White_02.prefab",
                    GangProps + "SM_Prop_Bag_White_03.prefab" },
        };

        const string Woodstack = GangProps + "SM_Prop_Woodstack_01.prefab";
        const string PropaneTall = GangProps + "SM_Prop_Propane_Tall_01.prefab";
        const string PropaneTallB = GangProps + "SM_Prop_Propane_Tall_02.prefab";
        const string GasCan = GangProps + "SM_Prop_GasCan_01.prefab";
        const string PaintCan = GangProps + "SM_Prop_PaintCan_02.prefab";
        const string YardBucket = GangProps + "SM_Prop_Bucket_01.prefab";
        const string BoxStack = GangProps + "SM_Prop_CardboardBox_Stack_01.prefab";
        const string BoxStackB = GangProps + "SM_Prop_CardboardBox_Stack_02.prefab";
        const string WrappedLoad = GangProps + "SM_Prop_Packet_Stack_Large_01.prefab";
        const string ProcessTank = GangProps + "SM_Prop_Lab_Tank_01.prefab";
        const string ProcessTankB = GangProps + "SM_Prop_Lab_Tank_03.prefab";
        const string YardSubstation = GangProps + "SM_Prop_Powerbox_01.prefab";
        const string StorageRack = GangProps + "SM_Prop_Warehouse_Rack_01.prefab";

        /// <summary>The moths round a yard lamp. A particle system with no mesh of its own, so
        /// it costs a renderer and nothing else, and it is the one thing in the pack's demo
        /// that makes a lamp read as switched ON rather than as a pole.</summary>
        const string BugLights = "Assets/Synty/PolygonGangWarfare/Prefabs/FX/FX_BugLights_01.prefab";

        /// <summary>The works' own traffic: a van and two cars for the men who run it, which is
        /// what a painted bay is for and what every bay in the block was missing.</summary>
        static readonly string[] StaffCars =
        {
            GangVeh + "SM_Veh_Van_01.prefab",
            GangVeh + "SM_Veh_LowCar_01.prefab",
            GangVeh + "SM_Veh_LowCar_02.prefab",
        };

        static readonly string[] Containers =
        {
            KitShips + "container-20-red.prefab", KitShips + "container-20-blue.prefab",
            KitShips + "container-20-green.prefab", KitShips + "container-20-rust.prefab",
            KitShips + "container-20-white.prefab",
        };

        /// <summary>What the perimeter is made of.</summary>
        public enum Wall { Brick, Wire, None }

        /// <summary>
        /// What the ground is made of. Two to a block at most, and the second only where the
        /// yard is worked.
        ///
        /// There is no earth among them, and that was tried: the empty plot was floored with
        /// the generic pack's dirt, which turns out to be a ROUND patch rather than a square
        /// tile. One to a cell it laid a field of overlapping brown discs - a polka-dot rug
        /// three hundred feet across, and the loudest floor in the quarter on the one parcel
        /// that is meant to look unused. A disused yard in this city is cracked hardstanding
        /// with weed coming through it, which is what it is now: the same asphalt as every
        /// other yard, and the grass in the cracks does the talking.
        /// </summary>
        public enum Surface { Plate, Asphalt }

        // ------------------------------------------------------------------- the raiser

        /// <summary>
        /// How a prefab is stood up: the editor hands in
        /// <c>PrefabUtility.InstantiatePrefab</c> so a bake keeps its links, the game a
        /// plain <c>Instantiate</c>.
        ///
        /// Set by <see cref="Stand"/> and deliberately NOT cleared afterwards, because a
        /// caller may go on adding to the block it was handed back - the quarter's
        /// <see cref="Block.Streetside"/> is called after Stand returns and raises pieces of
        /// its own. It is a field and not a parameter for the same reason the measurement
        /// cache is static: every method below would otherwise carry it.
        /// </summary>
        static Func<GameObject, Transform, GameObject> _raise;

        static readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();
        static readonly List<string> Absent = new List<string>();

        /// <summary>Prefabs the project has not got, gathered while composing, so a caller
        /// can say so once rather than a hundred times.</summary>
        public static IReadOnlyList<string> Missing => Absent;

        public static void ForgetMissing() => Absent.Clear();

        /// <summary>
        /// A prefab's own box, measured once and remembered.
        ///
        /// Measured through an INSTANCE, never off the asset: a prefab asset's renderers
        /// report bounds in their own local space, and the root scaling every Synty pack
        /// relies on is only applied once the thing is standing in a scene. The answer does
        /// not depend on which raiser stood it, so the cache is shared.
        /// </summary>
        static Bounds Box(string path)
        {
            if (Measured.TryGetValue(path, out var known)) return known;

            var box = new Bounds(Vector3.zero, Vector3.one);
            var asset = DemoAssetLoad.Load<GameObject>(path);
            if (asset == null)
            {
                // NOT remembered. A unit box cached for a prefab the project has not got
                // would outlive the import that adds it, and the piece would stand at the
                // wrong size for the rest of the session
                if (!Absent.Contains(path)) Absent.Add(path);
                return box;
            }

            var go = _raise(asset, null);
            if (go == null) { Measured[path] = box; return box; }
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (WorldBox(go, out var world)) box = world;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            Measured[path] = box;
            return box;
        }

        public static bool WorldBox(GameObject go, out Bounds box)
        {
            box = default;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            box = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return true;
        }

        static int Quarter(float yaw) => ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;

        static bool Turned(float yaw) => Quarter(yaw) % 2 == 1;

        /// <summary>What a piece covers on the ground once it has been turned.</summary>
        static Vector2 Foot(string path, float yaw)
        {
            var size = Box(path).size;
            return Turned(yaw) ? new Vector2(size.z, size.x) : new Vector2(size.x, size.z);
        }

        /// <summary>Where a thing standing in the yard has its feet: the top of the paving
        /// and two centimetres, the same clearance the port gives its sheds so that a floor
        /// and the plate under it do not flicker against each other.</summary>
        static float Deck => Box(Plate).max.y + 0.02f;

        static GameObject Raise(string path, Transform parent)
        {
            var asset = DemoAssetLoad.Load<GameObject>(path);
            if (asset == null)
            {
                if (!Absent.Contains(path)) Absent.Add(path);
                return null;
            }
            return _raise(asset, parent);
        }

        /// <summary>
        /// Lays a piece so its footprint covers exactly the rectangle asked for, stretched
        /// to fit it.
        ///
        /// Where it goes is worked out by MEASURING the turned instance rather than by
        /// reasoning about its pivot. These tiles pivot at a corner, the walls pivot at one
        /// end, and which corner or which end changes with the turn; measuring is the one
        /// answer that is right for all of them and stays right when a pack is replaced.
        /// </summary>
        static GameObject Lay(string path, Transform parent, float minX, float minZ,
                              float sizeX, float sizeZ, float yaw, float y = 0f)
        {
            var go = Raise(path, parent);
            if (go == null) return null;

            var own = Box(path).size;
            if (own.x > 0.001f && own.z > 0.001f)
            {
                var factor = Turned(yaw)
                    ? new Vector3(sizeZ / own.x, 1f, sizeX / own.z)
                    : new Vector3(sizeX / own.x, 1f, sizeZ / own.z);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, Whole(factor));
            }

            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (WorldBox(go, out var box))
                go.transform.position = new Vector3(minX - box.min.x, y, minZ - box.min.z);
            else
                go.transform.position = new Vector3(minX, y, minZ);
            return go;
        }

        /// <summary>A measured stretch, with anything within half a percent of no stretch at
        /// all called no stretch at all - so no prefab comes out carrying a scale of
        /// 0.9999996 for the next reader to rule out.</summary>
        static Vector3 Whole(Vector3 factor) => new Vector3(
            Mathf.Abs(factor.x - 1f) < 0.005f ? 1f : factor.x,
            Mathf.Abs(factor.y - 1f) < 0.005f ? 1f : factor.y,
            Mathf.Abs(factor.z - 1f) < 0.005f ? 1f : factor.z);

        /// <summary>Stands a building on its middle, keeping the floor it was baked with -
        /// which for every one of these kit bakes is y = 0.</summary>
        static GameObject Stand(string path, Transform parent, float cx, float cz, float yaw, float y) =>
            Settle(path, parent, cx, cz, yaw, y, false);

        /// <summary>Sits a prop on its own underside. Synty pivots furniture at its middle
        /// as often as at its feet, so a barrel dropped at the deck height by its pivot is
        /// as likely to be buried to the waist as standing on the ground.</summary>
        static GameObject Sit(string path, Transform parent, float cx, float cz, float yaw, float y) =>
            Settle(path, parent, cx, cz, yaw, y, true);

        static GameObject Settle(string path, Transform parent, float cx, float cz, float yaw,
                                 float y, bool onItsUnderside)
        {
            var go = Raise(path, parent);
            if (go == null) return null;

            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (!WorldBox(go, out var box))
            {
                go.transform.position = new Vector3(cx, y, cz);
                return go;
            }
            go.transform.position = new Vector3(cx - box.center.x,
                                                onItsUnderside ? y - box.min.y : y,
                                                cz - box.center.z);
            return go;
        }

        static int Pick(System.Random rng, params int[] of) => of[rng.Next(of.Length)];

        static bool Chance(System.Random rng, double odds) => rng.NextDouble() < odds;

        static float Between(System.Random rng, float a, float b) =>
            a + (float)rng.NextDouble() * (b - a);

        static string Any(string[] of, System.Random rng) => of[rng.Next(of.Length)];

        /// <summary>The stack's paint: flat brick red, no texture. Among flat-shaded pack
        /// art that is not a stand-in but the right answer - and a round shaft has no UVs
        /// that could find the brick on anybody's atlas anyway.</summary>
        static Material _paint;

        static Material StackPaint()
        {
            if (_paint != null) return _paint;
            _paint = DemoAssetLoad.Load<Material>(StackMaterial);
            if (_paint != null) return _paint;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _paint = new Material(shader) { name = "IndustrialStack" };
            var brick = new Color(0.35f, 0.18f, 0.15f);
            if (_paint.HasProperty("_BaseColor")) _paint.SetColor("_BaseColor", brick);
            if (_paint.HasProperty("_Color")) _paint.SetColor("_Color", brick);
            return _paint;
        }

        // --------------------------------------------------------------------- the block

        /// <summary>
        /// One parcel under composition: which cells it holds, what is on the floor of each,
        /// what stands on them, and which of its four sides is kerb and which a shared fence.
        /// </summary>
        public sealed class Block
        {
            public readonly Transform Root;
            public readonly int W, D, NX, NZ;
            public readonly System.Random Rng;

            /// <summary>South, north, west, east - in the block's OWN frame, which always
            /// faces south. <see cref="IndustrialLayout.Parcel.Local"/> turns the quarter's
            /// compass into this one.</summary>
            readonly IndustrialLayout.Edge[] _sides;

            readonly bool[] _held;
            readonly bool[] _laid;      // something has already floored the cell
            readonly bool[] _drive;     // the pavement gives way to road here: the way in
            readonly bool[] _corridor;  // the drive itself, which nothing else may surface
            readonly Surface[] _floor;
            readonly List<Rect> _taken = new List<Rect>();
            readonly List<Rect> _footprints = new List<Rect>();
            readonly Dictionary<string, int> _refused = new Dictionary<string, int>();

            Vector2 _way;

            /// <summary>What the yard is floored with where nothing else has claimed it.</summary>
            public Surface Ground = Surface.Asphalt;

            /// <summary>Brick unless the block is a yard; none at all for a forecourt, which
            /// is open to the road by definition.</summary>
            public Wall Wall = Wall.Brick;

            // ---- the rectangle a building may stand in, per side

            int Ring(IndustrialLayout.Side side) =>
                _sides[(int)side].Rim == IndustrialLayout.Rim.Kerb ? 1 : 0;

            float Back(IndustrialLayout.Side side) =>
                _sides[(int)side].Rim == IndustrialLayout.Rim.Kerb ? Setback : Party;

            public float In => Ring(IndustrialLayout.Side.West) * Cell + Back(IndustrialLayout.Side.West);
            public float Out => W - Ring(IndustrialLayout.Side.East) * Cell - Back(IndustrialLayout.Side.East);
            public float Near => Ring(IndustrialLayout.Side.South) * Cell + Back(IndustrialLayout.Side.South);
            public float Far => D - Ring(IndustrialLayout.Side.North) * Cell - Back(IndustrialLayout.Side.North);

            /// <summary>The ground inside the perimeter, <paramref name="inset"/> metres in
            /// from the wall line on every side. The wall stands on the kerb ring's inner
            /// edge where there is a kerb and on the boundary itself where the side is a
            /// shared fence, so this is not the same rectangle as In/Near/Out/Far - those
            /// are where a BUILDING may stand, a setback behind the wall.</summary>
            public Rect Yard(float inset) =>
                Rect.MinMaxRect(Ring(IndustrialLayout.Side.West) * Cell + inset,
                                Ring(IndustrialLayout.Side.South) * Cell + inset,
                                W - Ring(IndustrialLayout.Side.East) * Cell - inset,
                                D - Ring(IndustrialLayout.Side.North) * Cell - inset);

            public Block(Transform root, int w, int d, IndustrialLayout.Edge[] sides, System.Random rng)
            {
                Root = root;
                W = w;
                D = d;
                Rng = rng;
                _sides = sides;
                NX = Mathf.Max(3, w / (int)Cell);
                NZ = Mathf.Max(3, d / (int)Cell);
                _held = new bool[NX * NZ];
                _laid = new bool[NX * NZ];
                _drive = new bool[NX * NZ];
                _corridor = new bool[NX * NZ];
                _floor = new Surface[NX * NZ];
                for (int k = 0; k < _held.Length; k++)
                {
                    _held[k] = true;
                    _floor[k] = Surface.Asphalt;
                }
            }

            int At(int i, int j) => j * NX + i;

            bool Held(int i, int j) => i >= 0 && j >= 0 && i < NX && j < NZ && _held[At(i, j)];

            /// <summary>Which side of the block a step off (i, j) leaves by. Only asked of a
            /// step that leaves the block's own rectangle.</summary>
            static IndustrialLayout.Side Leaving(int di, int dj) =>
                di < 0 ? IndustrialLayout.Side.West : di > 0 ? IndustrialLayout.Side.East
                       : dj < 0 ? IndustrialLayout.Side.South : IndustrialLayout.Side.North;

            /// <summary>Does the block's PAVEMENT run through this cell? True on an outer
            /// edge, false where the block simply meets its neighbour: there the fence is
            /// the boundary and the ground up to it is yard.</summary>
            bool Kerbed(int i, int j)
            {
                if (!Held(i, j)) return false;
                foreach (var step in Steps)
                {
                    int ni = i + step.x, nj = j + step.y;
                    if (Held(ni, nj)) continue;
                    // a bite out of the middle of the block is the street's, so its edge is
                    // kerb whatever the sides say
                    if (ni >= 0 && nj >= 0 && ni < NX && nj < NZ) return true;
                    if (_sides[(int)Leaving(step.x, step.y)].Rim == IndustrialLayout.Rim.Kerb) return true;
                }
                return false;
            }

            /// <summary>
            /// Ground a building stands on, whole cells and part cells alike.
            ///
            /// This is the difference between the rule and the bug. <c>_laid</c> is set by
            /// <see cref="Claim"/> only for cells a footprint covers ENTIRELY, because that
            /// is the right test for flooring - a cell lapped halfway still wants its tile.
            /// It is the wrong test for everything else: the drive and the gateway both read
            /// it, so both were free to run road straight through the half of a cell a
            /// building was standing on, and the building came out with its front on
            /// pavement and its flank on tarmac.
            /// </summary>
            bool Apron(int i, int j)
            {
                var cell = new Rect(i * Cell, j * Cell, Cell, Cell);
                foreach (var foot in _footprints)
                    if (foot.Overlaps(cell)) return true;
                return false;
            }

            /// <summary>Is this cell the block's PAVEMENT - a plate, or a kerb tile of the
            /// outer ring? The drive is not: it is road cut through the pavement, and the
            /// pavement it passes wants a kerb against it like any other edge.</summary>
            bool Pave(int i, int j)
            {
                if (!Held(i, j)) return false;
                if (_drive[At(i, j)] || _corridor[At(i, j)]) return false;
                return Kerbed(i, j) || _floor[At(i, j)] == Surface.Plate;
            }

            /// <summary>The block's own working ground: held, and not pavement. What an
            /// inside kerb faces.</summary>
            bool Bare(int i, int j) => Held(i, j) && !Pave(i, j);

            /// <summary>The block's outline, kerb or fence alike.</summary>
            bool Rim(int i, int j)
            {
                if (!Held(i, j)) return false;
                foreach (var step in Steps)
                    if (!Held(i + step.x, j + step.y)) return true;
                return false;
            }

            static readonly Vector2Int[] Steps =
            {
                new Vector2Int(-1, 0), new Vector2Int(1, 0),
                new Vector2Int(0, -1), new Vector2Int(0, 1),
            };

            /// <summary>Takes a bite out of the block. The street will make it a car park;
            /// the block simply stops there and its kerb turns the corner.</summary>
            public void Bite(int i0, int j0, int ni, int nj)
            {
                for (int i = i0; i < i0 + ni; i++)
                    for (int j = j0; j < j0 + nj; j++)
                        if (i >= 0 && j >= 0 && i < NX && j < NZ) _held[At(i, j)] = false;
            }

            // ---- the way in

            /// <summary>
            /// The way in, as a span in metres along the south kerb - and a block is always
            /// composed facing south, so this is always the street it fronts.
            ///
            /// Setting it lays the DRIVE at the same time: road surface from the kerb
            /// straight in until something is standing in the way, and that ground booked so
            /// nothing is set down on it afterwards.
            /// </summary>
            public Vector2 Way
            {
                get => _way;
                set { _way = value; Corridor(); Drive(_way); }
            }

            void Corridor()
            {
                if (_way.y <= _way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, _way.y) - Mathf.Max(a, _way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Rim(i, first)) continue;

                    int last = first;
                    for (int j = first + 1; j < NZ; j++)
                    {
                        // it stops at the far boundary, kerb or fence: a drive that runs
                        // into the neighbour's yard is not a drive. And it stops at a
                        // building's ground, ANY part of it - not just the cells a building
                        // fills whole, which is what let it take half a cell out from under
                        // a shed and leave it standing on two surfaces
                        if (!Held(i, j) || Rim(i, j) || _laid[At(i, j)] || Apron(i, j)) break;
                        _floor[At(i, j)] = Surface.Asphalt;
                        _corridor[At(i, j)] = true;
                        last = j;
                    }
                    if (last > first)
                        _taken.Add(new Rect(a, (first + 1) * Cell, Cell, (last - first) * Cell));
                }
            }

            // ---- the wall's accounting

            float _wallWanted, _wallLaid;

            /// <summary>
            /// What the wall misses, measured against the GROUND it is meant to ring rather
            /// than against what the run-builder happened to ask for. Asking the builder is
            /// no test at all: a side it never worked out is a side it never wanted, and the
            /// hole goes unreported.
            ///
            /// A shared fence the NEIGHBOUR lays is not this block's to miss, so that
            /// stretch is left out of the reckoning on both counts.
            /// </summary>
            public float WallGap
            {
                get
                {
                    float should = 0f;
                    for (int j = 0; j < NZ; j++)
                        for (int i = 0; i < NX; i++)
                        {
                            if (!Held(i, j) || Kerbed(i, j)) continue;
                            foreach (var step in Steps)
                            {
                                int ni = i + step.x, nj = j + step.y;
                                if (Inside(ni, nj)) continue;
                                bool leaves = ni < 0 || nj < 0 || ni >= NX || nj >= NZ;
                                if (leaves)
                                {
                                    // a shared line the neighbour is down to build is not
                                    // this block's to miss
                                    var side = _sides[(int)Leaving(step.x, step.y)];
                                    if (!side.Lays) continue;
                                    if (Wall == Wall.None && side.Rim == IndustrialLayout.Rim.Kerb) continue;
                                }
                                // a forecourt owes no wall along its own pavement either,
                                // and must not be charged for one: the street side is where
                                // it is deliberately open
                                else if (Wall == Wall.None && Kerbed(ni, nj)) continue;
                                should += Cell;
                            }
                        }
                    if (Way.y > Way.x) should -= Way.y - Way.x;
                    return Mathf.Max(0f, Mathf.Max(should, _wallWanted) - _wallLaid);
                }
            }

            bool Inside(int i, int j) => Held(i, j) && !Kerbed(i, j);

            /// <summary>Wall pieces standing inside a building, which is the fault that keeps
            /// coming back and cannot be seen from above: a panel, a pillar or a gate leaf
            /// reaching into a wall reads as the building bursting through the perimeter.
            /// Counted rather than eyeballed, and reported with the block.</summary>
            public int WallInBuilding()
            {
                int through = 0;
                foreach (Transform piece in Root)
                {
                    if (!piece.name.StartsWith("SM_Bld_Fence")) continue;
                    if (!WorldBox(piece.gameObject, out var box)) continue;
                    // the box is measured in WORLD space and the footprints are in the
                    // block's own. Half the parcels in a quarter are turned about, so
                    // subtracting the root's POSITION is not the same as leaving its frame -
                    // and done that way this check quietly passed every parcel that faces
                    // north, which is the half of them nobody would think to look at.
                    var lo = Root.InverseTransformPoint(box.min);
                    var hi = Root.InverseTransformPoint(box.max);
                    var stood = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x) + 0.1f, Mathf.Min(lo.z, hi.z) + 0.1f,
                                                Mathf.Max(lo.x, hi.x) - 0.1f, Mathf.Max(lo.z, hi.z) - 0.1f);
                    foreach (var foot in _footprints)
                        if (foot.Overlaps(stood)) { through++; break; }
                }
                return through;
            }

            // ---- booking ground

            /// <summary>Books ground. Only cells covered WHOLE count as floored - a cell a
            /// building laps half of still wants its tile, or there is a sliver of nothing
            /// showing along the wall.</summary>
            void Claim(Rect metres)
            {
                _taken.Add(metres);
                int i0 = Mathf.CeilToInt(metres.xMin / Cell);
                int i1 = Mathf.FloorToInt(metres.xMax / Cell) - 1;
                int j0 = Mathf.CeilToInt(metres.yMin / Cell);
                int j1 = Mathf.FloorToInt(metres.yMax / Cell) - 1;
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(NX - 1, i1); i++)
                    for (int j = Mathf.Max(0, j0); j <= Mathf.Min(NZ - 1, j1); j++)
                        _laid[At(i, j)] = true;
            }

            /// <summary>Is there room here: inside the block, off the kerb ring, and clear of
            /// everything already standing. A cell against a SHARED fence is ordinary yard
            /// and may be used; a cell of pavement may not.</summary>
            public bool Room(Rect want)
            {
                foreach (var taken in _taken)
                    if (taken.Overlaps(want)) return false;

                return OnYard(want);
            }

            /// <summary>
            /// Ground that belongs to the block and is not the kerb ring - the half of
            /// <see cref="Room"/> that asks about the GROUND rather than about what is already
            /// standing on it.
            ///
            /// Tested at the four corners of a slightly shrunk rectangle, so a piece laid
            /// flush against a cell line is not refused by the last bit of a float.
            /// </summary>
            bool OnYard(Rect want)
            {
                if (want.width <= 0f || want.height <= 0f) return false;

                var inset = new Rect(want.xMin + 0.15f, want.yMin + 0.15f,
                                     Mathf.Max(0.1f, want.width - 0.3f),
                                     Mathf.Max(0.1f, want.height - 0.3f));
                var corners = new[]
                {
                    new Vector2(inset.xMin, inset.yMin), new Vector2(inset.xMax, inset.yMin),
                    new Vector2(inset.xMin, inset.yMax), new Vector2(inset.xMax, inset.yMax),
                };
                foreach (var corner in corners)
                {
                    int i = Mathf.FloorToInt(corner.x / Cell);
                    int j = Mathf.FloorToInt(corner.y / Cell);
                    if (!Held(i, j) || Kerbed(i, j)) return false;
                }
                return true;
            }

            /// <summary>What the block has built on it, so the passes that dress the ground
            /// can keep off the brick and hug the foot of it.</summary>
            public IReadOnlyList<Rect> Built => _footprints;

            // ---- what stands

            /// <summary>A building, seated with the near corner of its footprint where it is
            /// asked for. Returns what it covers - an empty rectangle if the piece is missing
            /// from the project, or if it will not fit where it was asked for.</summary>
            public Rect Put(string path, float minX, float minZ, float yaw)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(minX, minZ, foot.x, foot.y);
                if (!Room(where)) return new Rect();
                var go = IndustrialBlocks.Stand(path, Root, where.center.x, where.center.y, yaw, Deck);
                if (go == null) return new Rect();
                _footprints.Add(where);
                Claim(where);
                return where;
            }

            /// <summary>A prop, if there is room for it. Refused rather than crammed in: a
            /// yard reads as a yard because things are set down where they fit.</summary>
            public GameObject Prop(string path, float x, float z, float yaw, float lift = 0f)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y);
                if (!Room(where))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    _refused[name] = _refused.TryGetValue(name, out var seen) ? seen + 1 : 1;
                    return null;
                }

                var go = Sit(path, Root, x, z, yaw, Deck + lift);
                if (go != null) _taken.Add(where);
                return go;
            }

            /// <summary>
            /// What was asked for and would not fit, worst first.
            ///
            /// Reported with the block because it is the one fault this pipeline could not
            /// see. Every refusal returns null and says nothing, so a recipe whose hand-picked
            /// coordinates went stale - a building grew, a rank got longer - comes out as a
            /// yard that is quietly half empty and a summary that says everything is fine.
            /// </summary>
            public string Refused()
            {
                if (_refused.Count == 0) return "";

                // ties broken by name: a dictionary's own order is not the same twice, and
                // a report that reads differently for the same seed is a report nobody can
                // diff
                var worst = _refused.OrderByDescending(one => one.Value)
                                    .ThenBy(one => one.Key, StringComparer.Ordinal).Take(5)
                                    .Select(one => $"{one.Key} x{one.Value}");
                return string.Join(", ", worst);
            }

            /// <summary>
            /// A flat overlay laid straight on the yard: a worn patch, a puddle, weed through
            /// a crack, litter.
            ///
            /// It books no ground and is refused by nothing standing, which is the whole
            /// point. A puddle under a pallet is still a puddle, and weathering that gave way
            /// wherever the yard is actually USED would be weathering everywhere except where
            /// anyone is looking. The one thing it will not do is lie inside a building or out
            /// on the kerb ring, because there it is not weathering, it is a mistake.
            /// </summary>
            public GameObject Decal(string path, float x, float z, float yaw, float lift = 0f)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y);
                if (!OnYard(where)) return null;

                foreach (var built in _footprints)
                    if (built.Overlaps(where)) return null;

                return Sit(path, Root, x, z, yaw, Deck + lift);
            }

            /// <summary>
            /// A thing set down exactly where it is asked for, booking nothing and refused by
            /// nothing.
            ///
            /// For the few pieces whose position IS the point and where a fit test against
            /// ground already booked could only ever say no: a bollard on the cheek of a gate,
            /// a car in a bay that has just been painted, the second container of a stack.
            /// </summary>
            public GameObject Fix(string path, float x, float z, float yaw, float lift = 0f) =>
                Sit(path, Root, x, z, yaw, Deck + lift);

            /// <summary>A flat thing that lies ON the ground and books nothing: a puddle, a
            /// tuft of weed through a crack. It is a decal, and a yard where nothing may be
            /// set down beside a puddle is a yard with puddles for furniture.</summary>
            public GameObject Mark(string path, float x, float z, float yaw) =>
                Decal(path, x, z, yaw, 0.01f);

            /// <summary>A thing that goes on top of another - the second container of a stack
            /// - which has no ground of its own to book.</summary>
            public GameObject Atop(string path, float x, float z, float yaw, float lift) =>
                Fix(path, x, z, yaw, lift);

            /// <summary>
            /// Painted bays, the pack's own ten metres by five, laid on the grid so they take
            /// the floor of the cells they cover - and, where one is asked for, something
            /// standing in one of the two.
            ///
            /// A yard full of freshly painted empty bays is a car park nobody uses. One car in
            /// two is what the pack's own demo does and it is what a works looks like at any
            /// hour somebody is inside it.
            /// </summary>
            public void Bay(float minX, float minZ, float yaw, string parked = null)
            {
                float sizeX = Turned(yaw) ? Cell : Cell * 2f;
                float sizeZ = Turned(yaw) ? Cell * 2f : Cell;
                var where = new Rect(minX, minZ, sizeX, sizeZ);
                if (!Room(where)) return;
                if (Way.y > Way.x && where.yMin < Cell * 4f &&
                    where.xMax > Way.x - 1f && where.xMin < Way.y + 1f) return;   // not across the gate
                IndustrialBlocks.Lay(PaintedBays, Root, minX, minZ, sizeX, sizeZ, yaw);
                Claim(where);

                if (string.IsNullOrEmpty(parked)) return;

                // The bay pair is two 5 m stalls side by side; the car takes the near one and
                // stands nose-in, which on a bay laid at yaw 0 is along z.
                var stall = Turned(yaw)
                    ? new Vector2(where.center.x, where.yMin + Cell * 0.5f)
                    : new Vector2(where.xMin + Cell * 0.5f, where.center.y);
                Fix(parked, stall.x, stall.y, yaw);
            }

            public void Scatter(string path, int count, Rect area, float spread)
            {
                var foot = Foot(path, 0f);
                for (int stood = 0, guard = 0; stood < count && guard < count * 12; guard++)
                {
                    float x = Half(area.xMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.width));
                    float z = Half(area.yMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.height));
                    float yaw = 90f * Rng.Next(4) + ((float)Rng.NextDouble() * 2f - 1f) * spread;
                    var probe = new Rect(x - foot.x * 0.6f, z - foot.y * 0.6f,
                                         foot.x * 1.2f, foot.y * 1.2f);
                    if (!Room(probe)) continue;
                    if (Prop(path, x, z, yaw) != null) stood++;
                }
            }

            /// <summary>Decals strewn over a rectangle. They book nothing, so this is a
            /// count and not an attempt.</summary>
            public void Strew(string[] paths, int count, Rect area)
            {
                for (int k = 0; k < count; k++)
                {
                    float x = area.xMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.width);
                    float z = area.yMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.height);
                    Mark(paths[Rng.Next(paths.Length)], x, z, 90f * Rng.Next(4));
                }
            }

            internal static float Half(float v) => Mathf.Round(v * 2f) * 0.5f;

            // ---- the works stack

            /// <summary>
            /// A works stack: a tall round shaft that TAPERS, with a crown at the top.
            ///
            /// No pack here ships one, and both ways of faking it out of pack pieces were
            /// worse than this. Stacked villa flues are a metre across with a chimney cap
            /// every two and a half metres - a pile of crates. A square shaft of brick wall
            /// panels is a box, and cannot taper at all without scaling the panels below the
            /// size they were drawn at, which this project does not do.
            /// </summary>
            public void Chimney(float x, float z, float height)
            {
                const float Foot = 2.3f, Head = 1.15f, Drum = 2.2f;
                var ground = new Rect(x - Foot, z - Foot, Foot * 2f, Foot * 2f);
                if (!Room(ground))
                {
                    Debug.LogWarning($"[Industrial] no room for the stack at ({x:F1}, {z:F1}) - " +
                                     "something is standing there, most likely the drive.");
                    return;
                }

                var paint = StackPaint();
                int drums = Mathf.Max(4, Mathf.RoundToInt(height / Drum));
                float course = height / drums;
                for (int k = 0; k < drums; k++)
                {
                    float along = (k + 0.5f) / drums;
                    Barrel(x, z, Deck + k * course, course * 1.02f,
                           Mathf.Lerp(Foot, Head, along), paint, "chimney");
                }
                Barrel(x, z, Deck + height, 0.9f, Head * 1.25f, paint, "chimney crown");

                float mouth = Deck + height + 0.9f;
                var plume = Raise(Smoke, Root);
                if (plume != null)
                {
                    plume.transform.position = new Vector3(x, mouth, z);
                    plume.transform.localScale *= 2.6f;
                }
                Claim(ground);
            }

            /// <summary>One drum of the shaft. Unity's cylinder is two units tall and one
            /// across, so the scale is half the height and twice the radius.</summary>
            void Barrel(float x, float z, float bottom, float height, float radius,
                        Material paint, string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = name;
                go.transform.SetParent(Root, false);
                go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                go.transform.position = new Vector3(x, bottom + height * 0.5f, z);
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer) renderer.sharedMaterial = paint;
                var collider = go.GetComponent<Collider>();
                if (collider) UnityEngine.Object.DestroyImmediate(collider);
            }

            /// <summary>
            /// A storage tank: the pack's own oil drum blown up until it is a tank.
            ///
            /// No pack in this project has a tank or a silo, and the harbour settled this
            /// question first - its tank farm is <c>SM_Prop_Barrel_Metal_01</c> scaled to
            /// four metres and up (HarborKit.TankBody). A drum is the right shape already:
            /// a cylinder with two hoop rims, which is what a bunded tank looks like from
            /// across a fence.
            /// </summary>
            public GameObject Tank(float x, float z, float across, float tall)
            {
                var ground = new Rect(x - across * 0.5f, z - across * 0.5f, across, across);
                if (!Room(ground)) return null;
                var go = Sit(BarrelMetal, Root, x, z, 0f, Deck);
                if (go == null) return null;
                var own = Box(BarrelMetal).size;
                if (own.x < 0.01f || own.y < 0.01f) return go;
                go.transform.localScale = Vector3.Scale(go.transform.localScale,
                    new Vector3(across / own.x, tall / own.y, across / own.z));
                if (WorldBox(go, out var box))
                    go.transform.position += new Vector3(x - box.center.x, Deck - box.min.y, z - box.center.z);
                go.name = "tank";
                Claim(ground);
                return go;
            }

            /// <summary>Steam off a vent, stood on a coordinate rather than seated: a
            /// particle renderer reports whatever bounds it is holding until it plays, so
            /// sitting it on them would carry it anywhere at all.</summary>
            public void Vent(float x, float y, float z, float size)
            {
                var puff = Raise(Steam, Root);
                if (puff == null) return;
                puff.transform.position = new Vector3(x, y, z);
                puff.transform.localScale *= size;
            }

            // ---- the fence

            /// <summary>
            /// The wall round the yard, laid on whichever sides are this block's to lay.
            ///
            /// The crown is not a fence and never was - it is 77 cm of razor wire pivoted
            /// three metres up - so it goes as a run of its own, at whatever height puts its
            /// underside on the panel's top.
            /// </summary>
            public void Fence()
            {
                var panel = Box(Panel);
                float panelY = Deck - panel.min.y;
                float crownY = Wall == Wall.Wire
                    ? panelY + panel.max.y - Box(FenceCrown).min.y
                    : 0f;

                Side(true, false, IndustrialLayout.Side.South, panelY, crownY);
                Side(true, true, IndustrialLayout.Side.North, panelY, crownY);
                Side(false, false, IndustrialLayout.Side.West, panelY, crownY);
                Side(false, true, IndustrialLayout.Side.East, panelY, crownY);
            }

            /// <summary>
            /// What a wall-less block still fences.
            ///
            /// <see cref="Wall.None"/> means open TO THE ROAD, which is what a forecourt is.
            /// It does not mean the neighbour goes without the fence this parcel owes them:
            /// a shared line has exactly one builder, and a truck stop that declined to
            /// build left a full-depth hole down the middle of its island - which
            /// <see cref="WallGap"/> then declined to report. So a wall-less block skips its
            /// kerb sides and wires its shared ones.
            /// </summary>
            Wall Facing(bool kerb) => Wall == Wall.None ? Wall.Wire : Wall;

            string Panel => PanelOf(Wall == Wall.None ? Wall.Wire : Wall);

            static string PanelOf(Wall wall) => wall == Wall.Wire ? FencePanel : BrickPanel;

            static string UprightOf(Wall wall) => wall == Wall.Wire ? FencePole : BrickPillar;

            /// <summary>One side of the yard. <paramref name="alongX"/> is a run east-west,
            /// <paramref name="far"/> picks the north or the east one of the pair. The line
            /// it runs on is read off the mask column by column, so a bitten block fences
            /// round its own bite instead of straight across it.</summary>
            void Side(bool alongX, bool far, IndustrialLayout.Side which, float panelY, float crownY)
            {
                if (!_sides[(int)which].Lays) return;
                bool kerb = _sides[(int)which].Rim == IndustrialLayout.Rim.Kerb;
                if (Wall == Wall.None && kerb) return;    // open to the road, fenced from the neighbour
                var wall = Facing(kerb);

                int outer = alongX ? NX : NZ;
                int inner = alongX ? NZ : NX;
                var wanted = new bool[outer];
                var line = new float[outer];
                var ring = new int[outer];

                for (int a = 0; a < outer; a++)
                    for (int b = 0; b < inner; b++)
                    {
                        int step = far ? inner - 1 - b : b;
                        int i = alongX ? a : step;
                        int j = alongX ? step : a;
                        if (!Held(i, j)) continue;
                        wanted[a] = true;
                        // against a kerb the wall stands just INSIDE the pavement cell;
                        // against a shared fence there is no pavement, and the line is the
                        // boundary itself
                        line[a] = far ? (kerb ? step : step + 1) * Cell
                                      : (kerb ? step + 1 : step) * Cell;
                        ring[a] = step;
                        break;
                    }

                int from = -1;
                for (int a = 0; a <= outer; a++)
                {
                    bool joins = a < outer && wanted[a] &&
                                 (from < 0 || Mathf.Abs(line[a] - line[from]) < 0.01f);
                    if (joins)
                    {
                        if (from < 0) from = a;
                        continue;
                    }
                    if (from >= 0)
                    {
                        WireUp(wall, Corner(from, a - 1, ring, alongX, out bool startsFree, out bool endsFree),
                               line[from], alongX, far, panelY, crownY, startsFree, endsFree);
                        from = -1;
                    }
                    if (a < outer && wanted[a]) from = a;
                }
            }

            /// <summary>
            /// The stretch a run of wall covers, which is not the whole stretch of block it
            /// belongs to.
            ///
            /// A column at the very end of a side is a CORNER cell - the one the wall along
            /// the next side has to turn through - so the run stops a cell short of it and
            /// the two sides MEET there instead of crossing. Without this every block wears
            /// two panels through each other at all four corners.
            ///
            /// Unless that end is a SHARED FENCE, and then it is no corner at all: the wall
            /// carries straight on into the neighbour's, and stopping short of it leaves a
            /// ten metre hole in the middle of an island's frontage.
            /// </summary>
            Vector2 Corner(int first, int last, int[] ring, bool alongX,
                           out bool startsFree, out bool endsFree)
            {
                float from = first * Cell, to = (last + 1) * Cell;
                bool openBefore = alongX ? !Held(first - 1, ring[first])
                                         : !Held(ring[first], first - 1);
                bool openAfter = alongX ? !Held(last + 1, ring[last])
                                        : !Held(ring[last], last + 1);

                var before = alongX ? IndustrialLayout.Side.West : IndustrialLayout.Side.South;
                var after = alongX ? IndustrialLayout.Side.East : IndustrialLayout.Side.North;
                bool partyBefore = _sides[(int)before].Rim == IndustrialLayout.Rim.Party;
                bool partyAfter = _sides[(int)after].Rim == IndustrialLayout.Rim.Party;

                // an end where the block STOPS is a corner the next side turns through, so
                // the run stops a cell short of it; an end where the block carries on at a
                // different depth is the INSIDE corner of a notch, and there the two runs do
                // not meet at all but pass each other five metres apart, so that one is
                // reached out to instead
                from += openBefore ? (partyBefore ? 0f : Cell) : -Cell;
                to += openAfter ? (partyAfter ? 0f : -Cell) : Cell;
                startsFree = openBefore && partyBefore;
                endsFree = openAfter && partyAfter;
                return new Vector2(from, to);
            }

            /// <summary>Wall along one straight stretch, cut only where the way in is: the
            /// wall goes all the way round and the gate is the only thing that breaks it.
            /// Buildings are set back behind it rather than being it.</summary>
            void WireUp(Wall wall, Vector2 span, float line, bool alongX, bool far, float panelY,
                        float crownY, bool startsFree, bool endsFree)
            {
                var free = new List<Vector2> { span };

                // the gate hangs in the ONE run its opening falls in. Judged per SIDE, a
                // bitten block hung a second pair of leaves on every other run of its south
                // side, standing free in no opening at all
                float middle = (Way.x + Way.y) * 0.5f;
                bool wayIn = alongX && !far && Way.y > Way.x &&
                             middle > span.x - 0.01f && middle < span.y + 0.01f;
                if (wayIn) Cut(free, Way);

                foreach (var piece in free) _wallWanted += piece.y - piece.x;

                foreach (var piece in free)
                {
                    if (piece.y - piece.x < 1.2f) continue;
                    _wallLaid += Run(PanelOf(wall), piece, line, alongX, far, panelY);
                    if (wall == Wall.Wire) Run(FenceCrown, piece, line, alongX, far, crownY);
                    // a post where a run ends against nothing, and none where it runs on
                    // into the neighbour's fence: there the neighbour's own end post stands
                    if (!(startsFree && Mathf.Abs(piece.x - span.x) < 0.01f)) Pillar(wall, piece.x, line, alongX, far);
                    if (!(endsFree && Mathf.Abs(piece.y - span.y) < 0.01f)) Pillar(wall, piece.y, line, alongX, far);
                    StreetPlate(piece, line, alongX, far);
                }

                if (!wayIn) return;

                // both leaves stand INSIDE the opening and swung back into the yard, so the
                // way in is open. Thrown the other way each leaf reaches over whatever the
                // gate is cut between - and a gate is usually cut between two buildings.
                var gate = Foot(FenceGate, 90f);
                IndustrialBlocks.Lay(FenceGate, Root, Way.x, line, gate.x, gate.y, 90f, panelY);
                IndustrialBlocks.Lay(FenceGate, Root, Way.y - gate.x, line, gate.x, gate.y, 90f, panelY);
            }

            static void Cut(List<Vector2> spans, Vector2 bite)
            {
                for (int k = spans.Count - 1; k >= 0; k--)
                {
                    var span = spans[k];
                    if (bite.y <= span.x || bite.x >= span.y) continue;
                    spans.RemoveAt(k);
                    if (bite.y < span.y) spans.Insert(k, new Vector2(bite.y, span.y));
                    if (bite.x > span.x) spans.Insert(k, new Vector2(span.x, bite.x));
                }
            }

            /// <summary>Lays a run and returns the metres it covered. The run divides into
            /// whole modules and STRETCHES them; rounded up, a module would come out shorter
            /// than the piece it is - a prefab scaled below the size it was drawn at, which
            /// this project does not do.</summary>
            float Run(string path, Vector2 span, float line, bool alongX, bool far, float y)
            {
                float length = span.y - span.x;
                if (length < 0.5f) return 0f;

                float yaw = alongX ? 0f : 90f;
                var foot = Foot(path, yaw);
                float module = alongX ? foot.x : foot.y;
                float thick = alongX ? foot.y : foot.x;
                if (module < 0.2f) return 0f;

                if (length < module)
                {
                    float over = (module - length) * 0.5f;
                    span = new Vector2(span.x - over, span.y + over);
                    length = module;
                }
                int panels = Mathf.Max(1, Mathf.FloorToInt(length / module + 0.01f));
                float step = length / panels;
                // only what actually STOOD counts. Returned whole, this would report a
                // perimeter with no fence in it as a perimeter with no holes in it, which
                // is precisely the reading WallGap exists to make impossible
                float laid = 0f;
                for (int k = 0; k < panels; k++)
                {
                    float a = span.x + k * step;
                    var piece = alongX
                        ? IndustrialBlocks.Lay(path, Root, a, far ? line - thick : line, step, thick, yaw, y)
                        : IndustrialBlocks.Lay(path, Root, far ? line - thick : line, a, thick, step, yaw, y);
                    if (piece != null) laid += step;
                }
                return laid;
            }

            /// <summary>
            /// A keep-out plate on the STREET face of a run of wall - the one thing somebody
            /// walking past a blank perimeter can actually read off it, and a fitting the
            /// pack's own demo hangs ten of round a single compound.
            ///
            /// The outer face of a wall is at <paramref name="line"/> whichever side of the
            /// block it is on: a run laid near occupies [line, line + thick] and one laid far
            /// occupies [line - thick, line], so both put their street side exactly there. The
            /// only thing that changes between the four sides is which way the plate looks,
            /// which is the yaw - and the three centimetres it stands proud, which is the sign.
            ///
            /// Only where there IS a street. A shared fence has the neighbour's yard on its
            /// other side, and a plate hung there faces nobody but their forklift.
            /// </summary>
            void StreetPlate(Vector2 span, float line, bool alongX, bool far)
            {
                var side = alongX ? (far ? IndustrialLayout.Side.North : IndustrialLayout.Side.South)
                                  : (far ? IndustrialLayout.Side.East : IndustrialLayout.Side.West);
                if (_sides[(int)side].Rim != IndustrialLayout.Rim.Kerb) return;
                if (span.y - span.x < 4f || !Chance(Rng, 0.6)) return;

                var at = Between(Rng, span.x + 1.5f, span.y - 1.5f);
                var off = far ? 0.03f : -0.03f;
                var yaw = alongX ? (far ? 0f : 180f) : (far ? 90f : 270f);

                // Stood rather than sat: this plate pivots at its own middle, and dropping it
                // onto its underside would hang it a hand's breadth higher than asked for.
                IndustrialBlocks.Stand(KeepOut, Root,
                                       alongX ? at : line + off,
                                       alongX ? line + off : at,
                                       yaw, Deck + 1.9f);
            }

            void Pillar(Wall wall, float at, float line, bool alongX, bool far)
            {
                float inward = far ? -0.25f : 0.25f;
                float x = alongX ? at : line + inward;
                float z = alongX ? line + inward : at;
                Sit(UprightOf(wall), Root, x, z, 0f, Deck);
            }

            /// <summary>Takes the way in out of the pavement. Not a dropped kerb: the
            /// pavement STOPS and the road comes through it, the way a yard gate crosses a
            /// footway anywhere.</summary>
            void Drive(Vector2 span)
            {
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, span.y) - Mathf.Max(a, span.x) < Cell * 0.4f) continue;
                    for (int j = 0; j < NZ; j++)
                    {
                        if (!Held(i, j)) continue;
                        if (Kerbed(i, j)) _drive[At(i, j)] = true;
                        break;
                    }
                }
            }

            // ---- the ground

            /// <summary>
            /// What the ground is made of, decided in one place for the whole block.
            ///
            /// A yard is ASPHALT. Pavement is the ground people walk on, and in a works
            /// there are exactly two strips of it: the band inside the wall, and a skirt
            /// round each building. Everything else is where the lorries go.
            /// </summary>
            public void Surfaces()
            {
                for (int k = 0; k < _floor.Length; k++)
                    if (!_corridor[k]) _floor[k] = Ground;

                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || Kerbed(i, j) || _corridor[At(i, j)]) continue;

                        // the walk inside the wall - which, against a shared fence, is the
                        // boundary cell itself, there being no pavement outside it
                        bool walk = Kerbed(i - 1, j) || Kerbed(i + 1, j) ||
                                    Kerbed(i, j - 1) || Kerbed(i, j + 1) || Rim(i, j);

                        // and the skirt a building stands on. A SKIRT, not a five metre
                        // apron: at a cell to the metre, an apron that wide meets the next
                        // building's and the walk inside the wall, and the yard comes out as
                        // islands of asphalt with pavement running between them
                        if (!walk)
                        {
                            var cell = new Rect(i * Cell, j * Cell, Cell, Cell);
                            foreach (var foot in _footprints)
                            {
                                var skirt = new Rect(foot.xMin - 1.2f, foot.yMin - 1.2f,
                                                     foot.width + 2.4f, foot.height + 2.4f);
                                if (!skirt.Overlaps(cell)) continue;
                                walk = true;
                                break;
                            }
                        }

                        if (walk) _floor[At(i, j)] = Surface.Plate;
                    }

                // and then, over the top of all of it: EVERY cell a building touches is
                // pavement. The skirt above already reaches them, but saying it outright is
                // what makes it a rule rather than a consequence - a building stands on one
                // surface, and it is this one.
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                        if (Held(i, j) && !Kerbed(i, j) && Apron(i, j))
                            _floor[At(i, j)] = Surface.Plate;

                Gateway();
            }

            /// <summary>The way in, cut through whatever the walk and the skirts put in
            /// front of it. It runs from the kerb until it reaches ground that is the yard's
            /// own surface anyway, and stops there - a gateway is the few metres between the
            /// street and the yard, and no more.</summary>
            void Gateway()
            {
                if (Way.y <= Way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, Way.y) - Mathf.Max(a, Way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Rim(i, first)) continue;

                    for (int j = first + 1; j < NZ; j++)
                    {
                        if (!Held(i, j) || Rim(i, j)) break;
                        if (_floor[At(i, j)] != Surface.Plate) break;   // the yard: done
                        // a building's ground is NOT the gateway's to take. It used to set
                        // the cell to road and THEN notice the building on it, which is
                        // precisely a building standing half on pavement and half on road
                        if (Apron(i, j)) break;
                        _floor[At(i, j)] = Surface.Asphalt;
                    }
                }
            }

            /// <summary>
            /// The kerb, all the way round whatever shape the mask came out as - and only
            /// where the block's own pavement runs, which is not where a fence is shared.
            ///
            /// Which way a kerb tile faces is not guessed at. The street kit lays a road's
            /// south pavement at yaw 0, which puts the raised stone on the tile's +Z side,
            /// and every other side follows from that; the corner piece carries its stone on
            /// +X and +Z at yaw 0, so a block's north-east corner is 0 and the other three
            /// are quarter turns from it.
            /// </summary>
            public void Kerbs()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Kerbed(i, j)) continue;

                        if (_drive[At(i, j)])
                        {
                            _laid[At(i, j)] =
                                IndustrialBlocks.Lay(Asphalt, Root, i * Cell, j * Cell, Cell, Cell, 0f) != null;
                            continue;
                        }

                        // which way the pavement turns is read off the OUTER sides only: a
                        // block whose east fence is shared has no corner there, and a corner
                        // tile laid at one would point its stone into the neighbour's yard
                        bool west = Open(i, j, -1, 0), east = Open(i, j, 1, 0);
                        bool south = Open(i, j, 0, -1), north = Open(i, j, 0, 1);
                        if (!west && !east && !south && !north) continue;
                        LayKerb(i, j, west, east, south, north);
                    }

                Inside();
            }

            /// <summary>
            /// The kerb round the pavement INSIDE the block, corners and all.
            ///
            /// The yard is tarmac and the ground a building stands on is concrete, and where
            /// the two meet there is a kerb - the same kerb, turning the same corners, as the
            /// one at the street. Without it the apron was a flat plate butted against the
            /// asphalt with nothing but a change of colour between them, which from a car is
            /// no edge at all.
            ///
            /// It goes wherever pavement meets the block's own working ground, and the ONLY
            /// thing that stops it is a building standing on the stone itself.
            ///
            /// The first rule was "not where a building stands", meaning any cell a footprint
            /// touched - and a footprint usually takes a corner of a cell and stops, so the
            /// very cells that ARE the pavement's edge were the ones ruled out, and half the
            /// aprons came out unkerbed. What matters is not whether the building is in the
            /// cell but whether it is on the 90 cm of it the stone occupies, which is what
            /// <see cref="Clear"/> asks.
            ///
            /// Against a shared fence there is no kerb at all: the thing on the far side is
            /// the neighbour, not a road.
            /// </summary>
            void Inside()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || _laid[At(i, j)]) continue;
                        if (_floor[At(i, j)] != Surface.Plate) continue;

                        bool west = Bare(i - 1, j) && Clear(i, j, -1, 0);
                        bool east = Bare(i + 1, j) && Clear(i, j, 1, 0);
                        bool south = Bare(i, j - 1) && Clear(i, j, 0, -1);
                        bool north = Bare(i, j + 1) && Clear(i, j, 0, 1);
                        if (!west && !east && !south && !north) continue;
                        LayKerb(i, j, west, east, south, north);
                    }
            }

            /// <summary>Is the strip of this cell the kerb stone would stand on free of every
            /// building? A kerb under a shed is a kerb nobody can see, and a shed standing on
            /// a step.</summary>
            bool Clear(int i, int j, int di, int dj)
            {
                const float Stone = 0.9f;
                float x = i * Cell, z = j * Cell;
                var strip = di < 0 ? new Rect(x, z, Stone, Cell)
                          : di > 0 ? new Rect(x + Cell - Stone, z, Stone, Cell)
                          : dj < 0 ? new Rect(x, z, Cell, Stone)
                                   : new Rect(x, z + Cell - Stone, Cell, Stone);
                foreach (var foot in _footprints)
                    if (foot.Overlaps(strip)) return false;
                return true;
            }

            /// <summary>
            /// One kerb tile, facing the sides given.
            ///
            /// Which way it faces is not guessed at. The street kit lays a road's south
            /// pavement at yaw 0, which puts the raised stone on the tile's +Z side, and
            /// every other side follows from that; the corner piece carries its stone on +X
            /// and +Z at yaw 0, so a north-east corner is 0 and the other three are quarter
            /// turns from it. The outer ring and the inside edge want the same answer, so
            /// they ask the same question here.
            /// </summary>
            void LayKerb(int i, int j, bool west, bool east, bool south, bool north)
            {
                bool corner = !(west && east) && !(south && north) &&
                              ((east && north) || (east && south) ||
                               (west && south) || (west && north));

                string tile;
                float yaw;
                if (corner)
                {
                    tile = KerbCorner;
                    yaw = east && north ? 0f
                        : east && south ? 90f
                        : west && south ? 180f : 270f;
                }
                else
                {
                    tile = Kerb;
                    yaw = south ? 180f : north ? 0f : west ? 270f : 90f;
                }

                _laid[At(i, j)] =
                    IndustrialBlocks.Lay(tile, Root, i * Cell, j * Cell, Cell, Cell, yaw) != null;
            }

            /// <summary>Does the block's pavement face the street in this direction? Off the
            /// block, and not over a shared fence.</summary>
            bool Open(int i, int j, int di, int dj)
            {
                int ni = i + di, nj = j + dj;
                if (Held(ni, nj)) return false;
                if (ni >= 0 && nj >= 0 && ni < NX && nj < NZ) return true;    // a bite
                return _sides[(int)Leaving(di, dj)].Rim == IndustrialLayout.Rim.Kerb;
            }

            /// <summary>The floor, one tile to a cell and one tile to a surface. Laid last,
            /// so whatever brought ground of its own - the kerb, a painted bay - keeps it
            /// instead of being paved over twice and left to flicker.</summary>
            public void Floor()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || _laid[At(i, j)]) continue;
                        string tile = _floor[At(i, j)] == Surface.Plate ? Plate : Asphalt;
                        // a tile that never stood leaves the cell counted as a hole, which
                        // is what it is: a missing prefab is a block you can see through
                        _laid[At(i, j)] =
                            IndustrialBlocks.Lay(tile, Root, i * Cell, j * Cell, Cell, Cell, 0f) != null;
                    }
            }

            /// <summary>
            /// What stands on the block's OWN pavement, facing the street.
            ///
            /// It belongs to the block and not to the road, which is the core's rule too
            /// (CorePavement bakes a block's lamps into the block). The road reader lays
            /// tarmac and markings and knows nothing about what is beside it; the kerb is
            /// the block's, so the lamp on the kerb is the block's.
            ///
            /// Two things and no more, because this is an industrial estate and not a high
            /// street: a lamp every twenty-five metres on every side that has a street, and
            /// - along the FRONT only - a pole line, which is the one piece of furniture
            /// that says "works" from three hundred metres up. No benches, no newspaper
            /// boxes, no planters, no palms.
            /// </summary>
            public void Streetside(System.Random rng)
            {
                Lampposts(rng);
                Poles();
            }

            /// <summary>A lamp every twenty-five metres, and which cell of the five it
            /// stands in - shared with <see cref="Poles"/>, which must not put a pole in the
            /// same one.</summary>
            const int LampPitch = 5, LampAt = 2;

            void Lampposts(System.Random rng)
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Kerbed(i, j) || _drive[At(i, j)]) continue;
                        // one per side per pitch, counted along the side it faces, and never
                        // on a corner cell - a lamp on a corner is a lamp in the way of both
                        // streets
                        bool west = Open(i, j, -1, 0), east = Open(i, j, 1, 0);
                        bool south = Open(i, j, 0, -1), north = Open(i, j, 0, 1);
                        int faces = (west ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
                        if (faces != 1) continue;

                        int along = south || north ? i : j;
                        if (along % LampPitch != LampAt) continue;

                        // turned to face OUT, so the arm reaches over the carriageway
                        float yaw = south ? 180f : north ? 0f : west ? 270f : 90f;
                        float x = i * Cell + Cell * 0.5f, z = j * Cell + Cell * 0.5f;
                        // and stood a metre in from the kerb stone, where a lamp goes
                        x += south || north ? 0f : (west ? 1.2f : -1.2f);
                        z += west || east ? 0f : (south ? 1.2f : -1.2f);
                        Sit(StreetLamp, Root, x, z, yaw, Deck);
                        // the hydrant steps ALONG the kerb, not always east: offset in x on
                        // a south or north side it walked out into the yard, through the
                        // wall, on every west and east side in the quarter
                        if (!Chance(rng, 0.12)) continue;
                        float hx = x + (south || north ? 2.5f : 0f);
                        float hz = z + (west || east ? 2.5f : 0f);
                        Sit(Hydrant, Root, hx, hz, yaw, Deck);
                    }
            }

            /// <summary>
            /// The pole line down the frontage: a pole every span of cable, and the cable
            /// between them.
            ///
            /// The spacing is MEASURED off the cable rather than chosen, because a cable is
            /// a modelled catenary of one length: poles set further apart leave it hanging
            /// in mid air, and set closer they overlap. If the pack's cable turns out to be
            /// nothing like a span - anything under 10 m or over 40 - the poles go up alone
            /// at forty metres, which is a pole line as far as anyone can see from a car.
            /// </summary>
            void Poles()
            {
                if (_sides[(int)IndustrialLayout.Side.South].Rim != IndustrialLayout.Rim.Kerb) return;

                float span = Foot(PowerLine, 0f).x;
                bool cabled = span > 10f && span < 40f;
                float pitch = cabled ? span : 40f;
                float z = Cell * 0.5f;

                var cable = Box(PowerLine);
                float cableWide = Foot(PowerLine, 0f).y;
                // the cable's UNDERSIDE goes a little under the pole's head. Lay corrects x
                // and z by the measured box but passes y through as the pivot, so the
                // correction has to be made here - as the wall, the crown and the bund all
                // make it, and as this one alone did not
                float head = Deck + Box(PowerPole).size.y - cable.size.y - 0.4f;
                for (float x = pitch * 0.5f; x < W; x += pitch)
                {
                    if (Way.y > Way.x && x > Way.x - 2f && x < Way.y + 2f) continue;
                    int i = Mathf.FloorToInt(x / Cell);
                    if (!Kerbed(i, 0) || _drive[At(i, 0)]) continue;
                    if (i % LampPitch == LampAt) continue;    // a lamp already has this cell
                    Sit(PowerPole, Root, x, z, 0f, Deck);
                    if (!cabled || x + pitch >= W) continue;
                    IndustrialBlocks.Lay(PowerLine, Root, x, z - cableWide * 0.5f, span, cableWide,
                                         0f, head - cable.min.y);
                }
            }

            /// <summary>
            /// Buildings standing on more than one surface - the fault the drive and the
            /// gateway kept making, and the one a screenshot shows plainly the moment
            /// somebody looks for it: a shed with its front on concrete and its flank on
            /// tarmac, because half a cell of its ground was floored as road.
            ///
            /// Counted rather than eyeballed, and nought is the only passing answer.
            /// </summary>
            public int Straddles()
            {
                int split = 0;
                foreach (var foot in _footprints)
                {
                    bool two = false;
                    int i0 = Mathf.FloorToInt(foot.xMin / Cell), i1 = Mathf.CeilToInt(foot.xMax / Cell) - 1;
                    int j0 = Mathf.FloorToInt(foot.yMin / Cell), j1 = Mathf.CeilToInt(foot.yMax / Cell) - 1;
                    for (int i = Mathf.Max(0, i0); i <= Mathf.Min(NX - 1, i1) && !two; i++)
                        for (int j = Mathf.Max(0, j0); j <= Mathf.Min(NZ - 1, j1) && !two; j++)
                        {
                            if (!Held(i, j) || Kerbed(i, j)) continue;   // the ring is its own tile
                            if (_floor[At(i, j)] != Surface.Plate ||
                                _drive[At(i, j)] || _corridor[At(i, j)]) two = true;
                        }
                    if (two) split++;
                }
                return split;
            }

            /// <summary>Cells of the block with nothing on the floor at all, which a block
            /// dropped into the city would be seen straight through.</summary>
            public int Gaps()
            {
                int gaps = 0;
                for (int k = 0; k < _held.Length; k++)
                    if (_held[k] && !_laid[k]) gaps++;
                return gaps;
            }

            /// <summary>Books a rectangle of ground outright, for a recipe that stands
            /// something this class did not place - the pack's own filling station, which
            /// arrives as a cluster and must still keep the yard off its forecourt.</summary>
            public void Book(Rect area) => Claim(area);

            /// <summary>Every building this block put up, for the walkers and the map.</summary>
            public IReadOnlyList<Rect> Footprints => _footprints;
        }

        // ------------------------------------------------------------------- composing

        /// <summary>Every side kerb and every fence this block's own: what the lab bench
        /// composes, and what a parcel alone in the middle of nothing would be.</summary>
        public static IndustrialLayout.Edge[] Alone()
        {
            var sides = new IndustrialLayout.Edge[4];
            for (int k = 0; k < 4; k++)
                sides[k] = new IndustrialLayout.Edge(IndustrialLayout.Rim.Kerb, true);
            return sides;
        }

        /// <summary>
        /// Stands a whole parcel: the recipe's buildings and yard, then the fence, the
        /// surfaces, the kerb and the floor, in that order and no other. The fence has to
        /// know what is standing (so it can leave the gate where the recipe wanted it); the
        /// surfaces have to know where the fence is; the kerb has to be down before the
        /// floor, or the floor pays for the kerb's cells twice.
        /// </summary>
        public static Block Stand(IndustrialLayout.Recipe recipe, Transform root, int w, int d,
                                  IndustrialLayout.Edge[] sides, System.Random rng,
                                  Func<GameObject, Transform, GameObject> raise)
        {
            _raise = raise;
            var block = Compose(recipe, root, w, d, sides, rng);
            block.Fence();
            block.Surfaces();
            block.Kerbs();
            block.Floor();

            // Last, on top of everything, and off the same stream: the ground the block
            // stands on is the last thing decided and the first thing looked at.
            Weather(block, rng);
            return block;
        }

        static Block Compose(IndustrialLayout.Recipe recipe, Transform root, int w, int d,
                             IndustrialLayout.Edge[] sides, System.Random rng)
        {
            var block = new Block(root, w, d, sides, rng);
            switch (recipe)
            {
                case IndustrialLayout.Recipe.Plant: Plant(block, rng); break;
                case IndustrialLayout.Recipe.Depot: Depot(block, rng); break;
                case IndustrialLayout.Recipe.Yard: Stockyard(block, rng); break;
                case IndustrialLayout.Recipe.Strip: Strip(block, rng); break;
                case IndustrialLayout.Recipe.Haulage: Haulage(block, rng); break;
                case IndustrialLayout.Recipe.Fuel: TankFarm(block, rng); break;
                case IndustrialLayout.Recipe.Waste: Wasteland(block, rng); break;
                default: Works(block, rng); break;
            }
            return block;
        }

        // ------------------------------------------------------------------ the recipes

        /// <summary>
        /// The works: brick fronts filling the street with the gate in the gap between them,
        /// a shed across the back, a stack over the yard they share.
        ///
        /// The street side is BUILT, not walled. A works reads as a works because there is
        /// no way to see into it and something tall is smoking over the roofline; a yard
        /// with two sheds in the corner of it reads as a stockyard whatever is in the middle.
        /// </summary>
        static void Works(Block block, System.Random rng)
        {
            var older = block.Put(FactoryOld, block.In, block.Near, 180f);
            float first = older.width > 0f ? older.xMax : block.In;
            float next = Mathf.Min(first + Between(rng, 8f, 10f),
                                   block.Out - Foot(Factory, 180f).x);
            var newer = block.Put(Factory, next, block.Near, 180f);
            block.Way = newer.width > 0f
                ? new Vector2(first + 0.6f, newer.xMin - 0.6f)
                : new Vector2(first + 0.6f, first + 10.6f);

            var shopFoot = Foot(Workshop, 180f);
            bool shopFronts = newer.width > 0f && block.Out - newer.xMax >= shopFoot.x + 2f;
            if (shopFronts) block.Put(Workshop, block.Out - shopFoot.x, block.Near, 180f);

            var hallFoot = Foot(FactoryHall, 180f);
            var hall = block.Put(FactoryHall, block.In + Between(rng, 2f, 5f),
                                 block.Far - hallFoot.y, 180f);
            if (!shopFronts)
                block.Put(Workshop, block.Out - shopFoot.x, block.Far - shopFoot.y, 180f);

            float from = Mathf.Max(Mathf.Max(older.yMax, newer.yMax), block.Near + 2f);
            float to = hall.height > 0f ? hall.yMin : block.Far;

            // the stack goes against the boiler end of the street building rather than free
            // in the middle of the yard, which is where works put them and the only place
            // one does not look planted
            // against the boiler end of the street building - and if that building never
            // stood, against the middle of the frontage instead. An unguarded empty Rect put
            // the stack at (0, 4), inside the kerb ring, where Room refuses it and the works
            // comes out with no chimney at all: the one thing that says works
            block.Chimney(older.width > 0f ? older.center.x : (block.In + block.Out) * 0.5f,
                          older.height > 0f ? older.yMax + 4f : block.Near + 18f,
                          Between(rng, 22f, 27f));
            if (hall.height > 0f)
            {
                block.Prop(PipeRiserTall, hall.xMin + 4f, hall.yMin - 1.2f, 0f);
                if (Chance(rng, 0.6)) block.Prop(PipeRiserWide, hall.xMax - 5f, hall.yMin - 2f, 0f);
                if (hall.width > 8f)
                    Shuffled(block, LoadingDock, hall.center.x,
                             hall.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f,
                             new Vector2(1f, 0f), 9);

                // two of the three small stories hang off the hall: what was unloaded at
                // the dock and not cleared, and the plant that feeds the hall
                Unloaded(block, rng, hall.center.x + 6f, hall.yMin - 3f);
                ProcessPlant(block, rng, hall.center.x - 4f, hall.yMin - 5.5f, 0f);
            }

            var store = block.Put(YardShed, block.In, to - Foot(YardShed, 0f).y - 1f, 0f);
            Ranks(block, BarrelMetal, block.In + 2f, from + 3f, 3, rng.Next(4, 7), 1.15f);
            Ranks(block, Pallet, block.Out - 8f, from + 3f, 2, rng.Next(3, 5), 1.6f);

            var yard = Rect.MinMaxRect(Mathf.Max(store.xMax + 2f, block.In + 2f), from + 2f,
                                       block.Out - 2f, to - 2f);
            block.Prop(WaterTower, block.Out - 4f, to - 5f, 0f);
            block.Scatter(Pallet, rng.Next(2, 5), yard, 6f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), yard, 8f);
            block.Prop(PipeStack, block.Out - 4f, from + 8f, 90f);
            if (Chance(rng, 0.5)) block.Prop(Forklift, yard.xMin + 5f, from + 5f, 90f);
            // the third story, the timber and gas against the far wall, before the yard is
            // filled by the acre: a story that finds no ground is no story at all
            HazardStore(block, rng, block.Out - 2f, Mathf.Lerp(from, to, 0.55f), 0f);
            FillYard(block, yard, rng);
            if (newer.height > 0f)
            {
                block.Prop(Dumpster, newer.center.x, newer.yMax + 1.6f, 0f);
                BinBags(block, rng, newer.center.x + 2f, newer.yMax + 1.4f, rng.Next(3, 6));
            }
            Lamps(block, yard, 2);
            Gatepost(block, rng);
        }

        /// <summary>
        /// The plant: two process sheds across the back with the stack standing in the gap
        /// they leave for it, a works front on the street, and a brick wall between the two
        /// of them carrying the gate. The widest of the recipes, because a plant is mostly
        /// building. What is left over is a working yard, not a car park.
        /// </summary>
        static void Plant(Block block, System.Random rng)
        {
            var works = block.Put(Factory, block.In, block.Near, 180f);
            var shopFoot = Foot(Workshop, 180f);
            var shop = block.Put(Workshop, block.Out - shopFoot.x, block.Near, 180f);
            float mouth = works.width > 0f ? works.xMax + 1f : block.In + 10f;
            block.Way = Gate(block, mouth, mouth + Between(rng, 9f, 11f));

            var hallFoot = Foot(FactoryHall, 180f);
            float gap = Between(rng, 6f, 8f);
            float row = hallFoot.x * 2f + gap;
            float from = block.In + Mathf.Max(0f, (block.Out - block.In - row) * 0.5f);
            var west = block.Put(FactoryHall, from, block.Far - hallFoot.y, 180f);
            var far = block.Put(FactoryHall, from + hallFoot.x + gap, block.Far - hallFoot.y, 180f);

            float yardFrom = Mathf.Max(Mathf.Max(works.yMax, shop.yMax), block.Near + 2f);
            float yardTo = west.height > 0f ? west.yMin : block.Far;

            // the stack stands in the gap between the sheds, which is what the gap is for
            if (west.width > 0f)
                block.Chimney((block.In + west.xMin) * 0.5f, west.yMin + 3f, Between(rng, 26f, 32f));
            else
                block.Chimney(block.In + 4f, block.Far - 6f, Between(rng, 26f, 32f));
            block.Prop(WaterTower, Mathf.Max(block.In + 2f, west.xMin - 3.5f), yardTo - 4f, 0f);
            // A plant is mostly process, so it gets both tank groups - one at each shed's
            // gable - and what came off the last lorry left standing between them.
            if (west.width > 0f)
            {
                block.Prop(PipeRiserTall, west.xMin + 4f, west.yMin - 1.2f, 0f);
                Shuffled(block, LoadingDock, west.center.x,
                         west.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f, new Vector2(1f, 0f), 9);
                ProcessPlant(block, rng, west.xMin + 1.5f, west.yMin - 5f, 0f);
            }
            if (far.width > 0f)
            {
                block.Prop(PipeRiserWide, far.xMax - 5f, far.yMin - 2f, 0f);
                Shuffled(block, LoadingDock, far.center.x,
                         far.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f, new Vector2(1f, 0f), 9);
                ProcessPlant(block, rng, far.xMax - 6f, far.yMin - 5f, 0f);
            }
            if (west.width > 0f && far.width > 0f)
                Unloaded(block, rng, (west.xMax + far.xMin) * 0.5f, yardTo - 4f);
            HazardStore(block, rng, block.In + 2.2f, Mathf.Lerp(yardFrom, yardTo, 0.5f), 0f);

            Ranks(block, BarrelMetal, block.In + 2f, yardFrom + 3f, 3, rng.Next(4, 7), 1.15f);
            Ranks(block, Pallet, block.Out - 9f, yardFrom + 3f, 2, rng.Next(4, 6), 1.6f);

            var yard = Rect.MinMaxRect(block.In + 8f, yardFrom + 2f, block.Out - 11f, yardTo - 2f);
            block.Scatter(BarrelMetal, rng.Next(3, 6), yard, 8f);
            block.Scatter(Pallet, rng.Next(2, 4), yard, 6f);
            block.Scatter(Crate, rng.Next(2, 4), yard, 10f);
            block.Scatter(Any(Chemicals, rng), rng.Next(2, 5), yard, 8f);
            block.Prop(PipeStack, yard.xMin + 3f, yard.center.y, 0f);
            if (Chance(rng, 0.6)) block.Prop(Forklift, yard.center.x, yardFrom + 4f, 90f);
            FillYard(block, yard, rng);
            if (shop.width > 0f)
            {
                block.Prop(Dumpster, shop.xMin - 2f, shop.center.y, 90f);
                BinBags(block, rng, shop.xMin - 2f, shop.center.y + 2f, rng.Next(3, 6));
            }
            Lamps(block, yard, 3);
            Gatepost(block, rng);
        }

        /// <summary>The depot: one big shed across the back with its doors on the yard, and a
        /// forecourt in front of it wide enough to turn a lorry in.</summary>
        static void Depot(Block block, System.Random rng)
        {
            block.Wall = Wall.Wire;

            var foot = Foot(ShedLarge, 180f);
            var shed = block.Put(ShedLarge, Mathf.Round((block.W - foot.x) * 0.5f),
                                 block.Far - foot.y, 180f);
            var hut = block.Put(YardShed, block.Out - Foot(YardShed, 180f).x, block.Near, 180f);
            float middle = shed.width > 0f ? shed.center.x : (block.In + block.Out) * 0.5f;
            block.Way = Gate(block, middle - 6f, middle + 6f);

            // two docks at the doors, six metres either side of the middle: one lorry each,
            // and both clear of the door the shed actually works out of
            if (shed.height > 0f)
            {
                float dock = Foot(LoadingDock, 0f).y;
                block.Prop(LoadingDock, middle - 6f, shed.yMin - dock * 0.5f, 0f);
                block.Prop(LoadingDock, middle + 6f, shed.yMin - dock * 0.5f, 0f);
            }

            for (int k = 0; k < 3; k++)
            {
                float x = Mathf.Round((block.In + 2f + k * 12f) / Cell) * Cell;
                if (hut.width > 0f && x + 10f >= hut.xMin) continue;
                var parked = Chance(rng, 0.55) ? StaffCars[rng.Next(StaffCars.Length)] : null;
                block.Bay(x, Mathf.Round(block.Near / Cell) * Cell + Cell, 0f, parked);
            }

            float apronTo = shed.height > 0f ? shed.yMin - 5f : block.Far - 2f;
            var apron = Rect.MinMaxRect(block.In + 2f, block.Near + 8f, block.Out - 2f, apronTo);
            if (Chance(rng, 0.8)) block.Prop(BoxLorry, apron.xMin + 6f, apron.center.y, 0f);
            if (Chance(rng, 0.6)) block.Prop(BoxLorry, apron.xMax - 6f, apron.center.y, 180f);
            block.Scatter(Pallet, rng.Next(2, 5), apron, 5f);
            if (shed.height > 0f) Unloaded(block, rng, shed.center.x - 10f, shed.yMin - 4f);
            FillYard(block, apron, rng, stacked: true);
            if (hut.width > 0f)
            {
                block.Prop(Dumpster, hut.xMin - 2.5f, block.Near + 2f, 90f);
                BinBags(block, rng, hut.xMin - 2.5f, block.Near + 4f, rng.Next(3, 6));
            }
            block.Prop(Forklift, middle, apronTo - 3f, Between(rng, 60f, 120f));

            // the ground down either side of the shed: stock on one, empties and a spare
            // lorry on the other
            // both flanks are read off the shed, so both need the same guard: with no shed
            // standing, an unguarded xMax of 0 turns the east flank into the whole block and
            // the lorry meant to be down its side ends up in the middle of the yard
            float shedFrom = shed.height > 0f ? shed.yMin : block.Far - 8f;
            float shedWest = shed.width > 0f ? shed.xMin : block.In + 6f;
            float shedEast = shed.width > 0f ? shed.xMax : block.Out - 6f;
            float hutTop = hut.height > 0f ? hut.yMax : block.Near + 8f;
            var alongWest = Rect.MinMaxRect(block.In + 1.5f, shedFrom + 2f,
                                            Mathf.Max(block.In + 3f, shedWest - 1.5f), block.Far - 2f);
            var alongEast = Rect.MinMaxRect(Mathf.Min(block.Out - 3f, shedEast + 1.5f), hutTop + 3f,
                                            block.Out - 1.5f, block.Far - 2f);
            block.Scatter(BarrelMetal, rng.Next(4, 9), alongWest, 8f);
            block.Scatter(Pallet, rng.Next(3, 6), alongWest, 6f);
            block.Prop(BoxLorry, alongWest.center.x, alongWest.yMax - 6f, 0f);
            block.Scatter(Crate, rng.Next(2, 5), alongEast, 10f);
            block.Scatter(WireSpool, rng.Next(1, 3), alongEast, 0f);
            block.Prop(Any(Containers, rng), alongEast.center.x, alongEast.yMin + 4f, 0f);
            block.Prop(StorageRack, alongWest.xMin + 1f, alongWest.center.y, 90f);

            Lamps(block, apron, 3);
            Gatepost(block, rng);
        }

        /// <summary>The stockyard: a row of sheds with their backs to the far street and
        /// blocks of containers standing off in front of them.</summary>
        static void Stockyard(Block block, System.Random rng)
        {
            block.Wall = Wall.Wire;

            var small = Foot(ShedSmall, 180f);
            var garage = Foot(DepotGarage, 180f);
            float gap = Between(rng, 7f, 10f);
            float row = small.x + gap + garage.x;
            float from = block.In + Mathf.Max(0f, (block.Out - block.In - row) * 0.5f);

            var west = block.Put(ShedSmall, from, block.Far - small.y, 180f);
            var far = block.Put(DepotGarage, from + small.x + gap, block.Far - garage.y, 180f);
            float gate = (block.In + block.Out) * 0.5f;
            block.Way = Gate(block, gate - 6f, gate + 6f);

            float lane = Mathf.Min(west.height > 0f ? west.yMin : block.Far,
                                   far.height > 0f ? far.yMin : block.Far);

            // the containers, stacked on the port's odds: mostly two high, a shipper's whole
            // block in one colour now and then, and a gap where one has been taken away
            var can = Box(Containers[0]).size;
            float rank = can.x + 4f;
            int ranks = Mathf.Clamp(Mathf.FloorToInt((lane - 3f - (block.Near + 2f)) / rank), 1, 3);
            for (int r = 0; r < ranks; r++)
            {
                float z = block.Near + 2f + can.x * 0.5f + r * rank;
                bool oneShipper = Chance(rng, 0.35);
                string shipper = Any(Containers, rng);
                int stacks = Mathf.FloorToInt((block.Out - block.In - 2f) / (can.z + 0.4f));
                for (int s = 0; s < stacks; s++)
                {
                    if (Chance(rng, 0.18)) continue;
                    float x = block.In + 1f + s * (can.z + 0.4f) + can.z * 0.5f;
                    // the way in is a way in: no rank closes the lane the gate opens onto
                    if (x + can.z * 0.5f > block.Way.x - 1.5f && x - can.z * 0.5f < block.Way.y + 1.5f) continue;
                    int tall = Chance(rng, 0.2) ? 1 : Chance(rng, 0.75) ? 2 : 3;
                    for (int t = 0; t < tall; t++)
                    {
                        string colour = oneShipper ? shipper : Any(Containers, rng);
                        var stood = t == 0 ? block.Prop(colour, x, z, 90f)
                                           : block.Atop(colour, x, z, 90f, t * can.y);
                        if (stood == null) break;
                    }
                }
            }

            var open = Rect.MinMaxRect(block.In + 2f, lane - 7f, block.Out - 2f, lane - 1f);
            block.Prop(Forklift, open.xMin + 5f, open.center.y, 0f);
            block.Scatter(WireSpool, rng.Next(2, 4), open, 0f);
            block.Scatter(Crate, rng.Next(2, 5), open, 10f);
            block.Scatter(BarrelMetal, rng.Next(3, 7), open, 8f);
            block.Prop(BurnBarrel, block.Out - 6f, block.Near + 2f, 0f);

            float rowWest = west.width > 0f ? west.xMin : block.In + 6f;
            float rowEast = far.width > 0f ? far.xMax : block.Out - 6f;
            var flankWest = Rect.MinMaxRect(block.In + 1.5f, lane + 1f,
                                            Mathf.Max(block.In + 3f, rowWest - 1.5f), block.Far - 2f);
            var flankEast = Rect.MinMaxRect(Mathf.Min(block.Out - 3f, rowEast + 1.5f), lane + 1f,
                                            block.Out - 1.5f, block.Far - 2f);
            block.Scatter(Pallet, rng.Next(2, 5), flankWest, 6f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), flankEast, 8f);
            block.Prop(Dumpster, flankEast.center.x, flankEast.yMax - 2f, 90f);
            block.Prop(BoxLorry, gate, lane - 6f, 0f);
            BinBags(block, rng, flankEast.center.x, flankEast.yMax - 3.5f, rng.Next(3, 6));
            block.Prop(StorageRack, flankWest.xMin + 1f, flankWest.center.y, 90f);
            Unloaded(block, rng, open.center.x, open.center.y);

            Lamps(block, Rect.MinMaxRect(block.In + 2f, block.Near + 2f, block.Out - 2f, block.Far - 2f), 3);
            Gatepost(block, rng);
        }

        /// <summary>The service strip: a workshop and a hall on the street with the way in
        /// between them, and a yard of standing plant behind.</summary>
        static void Strip(Block block, System.Random rng)
        {
            var shop = block.Put(Workshop, block.In, block.Near, 180f);
            var hallFoot = Foot(FactoryHall, 180f);
            var hall = block.Put(FactoryHall, block.Out - hallFoot.x, block.Near, 180f);

            float mouth = shop.width > 0f ? shop.xMax + 0.6f : block.In + 8f;
            float shut = hall.width > 0f ? hall.xMin - 0.6f : block.Out - 8f;
            if (shut - mouth < 7f) shut = mouth + 7f;
            block.Way = Gate(block, mouth, shut);
            float middle = (mouth + shut) * 0.5f;

            float from = Mathf.Max(Mathf.Max(shop.yMax, hall.yMax), block.Near + 2f);

            block.Bay(Mathf.Round(block.In / Cell) * Cell + Cell, block.D - Cell * 2f, 0f,
                      Chance(rng, 0.7) ? StaffCars[rng.Next(StaffCars.Length)] : null);
            block.Bay(Mathf.Round(block.In / Cell) * Cell + Cell, block.D - Cell * 3f, 0f,
                      Chance(rng, 0.5) ? StaffCars[rng.Next(StaffCars.Length)] : null);

            var yard = Rect.MinMaxRect(block.In + 2f, from + 2f, block.Out - 2f, block.Far - 2f);
            if (hall.width > 0f)
            {
                block.Prop(Dumpster, hall.center.x, hall.yMax + 1.6f, 0f);
                BinBags(block, rng, hall.center.x + 2f, hall.yMax + 1.4f, rng.Next(3, 6));
                Unloaded(block, rng, hall.center.x, from + 4f);
            }
            // A service strip is a trade unit, so what it keeps outside is trade stock rather
            // than process: the bottles and the timber against the shop's own flank.
            if (shop.height > 0f) HazardStore(block, rng, block.In + 2.2f, shop.yMax + 4f, 0f);
            block.Prop(CompanySign, middle, from + 3f, 0f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), yard, 8f);
            block.Scatter(Pallet, rng.Next(2, 4), yard, 6f);
            if (Chance(rng, 0.5)) block.Prop(Forklift, yard.xMax - 5f, yard.center.y, 270f);
            FillYard(block, yard, rng);
            Lamps(block, yard, 2);
            Gatepost(block, rng);
        }

        /// <summary>
        /// The haulage yard: the estate's lorries, where they are kept, fuelled and mended.
        ///
        /// It was a TRUCK STOP - the Town pack's whole filling station, canopy and price
        /// board and a convenience shop with a slushie machine in it, standing unfenced on
        /// the corner. Three things were wrong with that and the user named the first: it
        /// looked like a commercial forecourt. It also duplicated the wayside station the
        /// city already stands on the roads between its districts, from the same cluster;
        /// and it was the one unwalled, brightly-coloured parcel in a quarter whose whole
        /// character is walled, grey and low, so it was the first thing the eye went to.
        ///
        /// What replaces it is what an estate actually has. The fuel here is a FLEET
        /// installation, and every difference from a forecourt is deliberate:
        ///
        ///   - a raised island with two pumps on it and nothing over them. No canopy.
        ///   - a bulk tank standing behind the island in its own bund, which is where the
        ///     diesel comes from and is the thing that says "this is not for sale".
        ///   - no shop, no price board, no pole sign.
        ///   - and a wire fence with a gate round the lot of it, like every other yard here.
        ///
        /// Nothing on it is retail, down to the frontage: the canteen that stood there first
        /// was the pack's chrome roadside diner, which broke the same rule the forecourt did.
        /// </summary>
        static void Haulage(Block block, System.Random rng)
        {
            block.Wall = Wall.Wire;

            // the frontage: the fleet garage and the fitter's shop, with the gate between.
            //
            // The canteen was tried here and taken out again. A transport cafe at the gate is
            // a real thing, but the prefab for it is a chrome-and-neon roadside DINER, and
            // stood among brick works it became the loudest thing on the parcel - the same
            // objection that took the forecourt out, one building over. The rule that governs
            // is the one the user gave: nothing here may read as retail.
            // BOTH buildings sit together and the gate takes the far end of the frontage,
            // which is what leaves one yard instead of two.
            //
            // They stood either side of a central gate first, and the drive - which runs from
            // the road to the back fence and books its ground as it goes - cut the yard into
            // a 24 m strip and a 12 m one. A lorry bay is 10 m wide: the wide strip took two
            // columns, the narrow one took a single column, and the whole yard came out with
            // three bays in it. With the drive against one edge the parking is one rectangle
            // and holds four times as many.
            var garage = block.Put(DepotGarage, block.In, block.Near, 180f);
            var shopFoot = Foot(Workshop, 180f);
            float fittersAt = garage.width > 0f ? garage.xMax + 1.5f : block.In;
            var fitters = block.Put(Workshop, Mathf.Min(fittersAt, block.Out - shopFoot.x),
                                    block.Near, 180f);
            float built = Mathf.Max(garage.width > 0f ? garage.xMax : block.In,
                                    fitters.width > 0f ? fitters.xMax : block.In);
            block.Way = Gate(block, built + 1.5f, block.Out);

            float from = Mathf.Max(Mathf.Max(garage.yMax, fitters.yMax), block.Near + 2f) + 2f;

            // the yard: everything inside the drive, behind the buildings, in ONE piece
            float driveAt = block.Way.y > block.Way.x ? block.Way.x - 2f : block.Out - 2f;
            var yard = Rect.MinMaxRect(block.In + 1.5f, from,
                                       Mathf.Max(block.In + 3f, driveAt), block.Far - 1.5f);

            // ---- the fuel island, on the west flank, and the whole point of the recipe
            //
            // Laid by hand rather than through the pack's cluster: the cluster IS the
            // forecourt, canopy and shop and all, and there is no way to ask it for just the
            // pumps. Two bases end to end make one island; the pumps stand back to back on
            // it, which is how a two-hose fleet pump is arranged so a lorry can take either
            // side.
            //
            // The pumps go down with Atop and NOT with Prop, because they stand ON the island
            // and the island has just booked that ground - asked for room, each of them was
            // refused by the very thing it belongs to, and the yard came out with a bare
            // island and no pumps on it.
            if (yard.width > 12f)
            {
                // against the back fence, clear of the parking, which is where a yard puts
                // its pump: a lorry fills up on the way to its bay, not across it
                float islandX = Mathf.Min(yard.xMin + 9f, yard.center.x);
                float islandZ = Mathf.Max(from + 4f, block.Far - 20f);
                var bar = Foot(GasPumpBase, 0f);
                block.Prop(GasPumpBase, islandX - bar.x * 0.5f, islandZ, 0f);
                block.Prop(GasPumpBase, islandX + bar.x * 0.5f, islandZ, 0f);
                float onIsland = Mathf.Max(0f, Box(GasPumpBase).max.y - Deck);
                block.Atop(GasPump, islandX - 1.85f, islandZ, 0f, onIsland);
                block.Atop(GasPump, islandX + 1.85f, islandZ, 180f, onIsland);
                // a bollard at either end, which is what stops a reversing trailer taking the
                // pumps off their island
                block.Prop(Bollard, islandX - bar.x - 1.4f, islandZ, 0f);
                block.Prop(Bollard, islandX + bar.x + 1.4f, islandZ, 0f);
                block.Prop(DangerSign, islandX, islandZ - 3.2f, 0f);

                // the tank the diesel comes out of, bunded, standing behind the island
                float tankZ = Mathf.Min(islandZ + 11f, block.Far - 6f);
                float half = Mathf.Min(6f, yard.width * 0.5f - 1f);
                if (block.Tank(islandX, tankZ, Mathf.Min(4.5f, half * 1.4f), 6f) != null)
                {
                    Bund(block, Rect.MinMaxRect(islandX - half, tankZ - 5f, islandX + half, tankZ + 5f));
                    block.Prop(Ladder, islandX + half - 1f, tankZ, 270f);
                    block.Prop(KeepOut, islandX - half, tankZ - 6.5f, 0f);
                }
            }

            // ---- the lorry park down the east flank, and across the back of the west one
            float lane = Cell * 2f;
            for (float z = Mathf.Round(from / Cell) * Cell; z + lane <= yard.yMax; z += lane)
                for (float x = Mathf.Round(yard.xMin / Cell) * Cell; x + lane <= yard.xMax + 1f; x += lane)
                    block.Bay(x, z, 0f, Chance(rng, 0.5) ? (Chance(rng, 0.6) ? BoxLorry : TownLorry) : null);

            // ---- the wash-down, against the garage where the drain is
            if (garage.width > 0f)
            {
                block.Prop(HoseReel, garage.xMin + 1.5f, garage.yMax + 2f, 0f);
                block.Prop(Bollard, garage.xMin + 4.5f, garage.yMax + 2f, 0f);
            }

            // ---- the office, the fitter's clutter, and whatever ground is left on each side
            var hut = block.Put(YardShed, block.In, block.Far - Foot(YardShed, 0f).y, 0f);
            block.Prop(Forklift, Mathf.Min(yard.xMax - 3f, block.In + 6f), from + 1f, 90f);
            block.Prop(PropaneTall, block.In + 3f, from + 6f, 0f);
            block.Prop(Dumpster, yard.xMax - 2.5f, from + 2f, 90f);

            FillYard(block, yard, rng, stacked: true);
            Lamps(block, Rect.MinMaxRect(block.In + 2f, from, block.Out - 2f, block.Far - 2f), 3);
            Gatepost(block, rng);
        }

        /// <summary>
        /// The tank farm: bunded tanks with the pipework run out to a lorry stand.
        ///
        /// No pack here has a tank, so the harbour's answer stands: a drum blown up until it
        /// is one (<see cref="Block.Tank"/>). The bund round them is the base pillar
        /// stretched into a wall, which is the same trick the port's own bund uses - and
        /// unlike the port's, this one is never scaled shorter than the piece it is made of.
        /// </summary>
        static void TankFarm(Block block, System.Random rng)
        {
            block.Wall = Wall.Wire;

            float middle = (block.In + block.Out) * 0.5f;
            block.Way = Gate(block, middle - 6f, middle + 6f);

            // the compound across the back, and the tanks in it.
            //
            // The COMPOUND is sized to the tanks, not the tanks to the compound. Sized the
            // other way round it took the whole parcel, and a plot 280 m wide - which the
            // deal will hand out - came back as three drums standing a hundred metres apart
            // inside one enormous wall. A tank farm is a tight block of tanks in a bund with
            // yard around it, whatever size the plot is.
            const float Across = 9f, Pitch = 15f;
            float room = block.Out - block.In - 8f;
            int tanks = Mathf.Clamp(Mathf.FloorToInt(room / Pitch), 2, 6);
            float row = tanks * Pitch;
            float bundZ = Mathf.Max(block.Near + 16f, block.Far - (Across + 12f));
            float bundFrom = block.In + 4f;
            var bund = Rect.MinMaxRect(bundFrom, bundZ, bundFrom + row, block.Far - 2f);
            float tall = Across * Between(rng, 0.9f, 1.2f);
            for (int k = 0; k < tanks; k++)
            {
                float x = bund.xMin + Pitch * (k + 0.5f);
                float z = bund.center.y;
                var drum = block.Tank(x, z, Across, tall);
                if (drum == null) continue;
                block.Prop(Ladder, x + Across * 0.5f + 0.6f, z, 270f);
                block.Vent(x, Deck + tall, z, 1.6f);
                // the riser off each tank, run down to the manifold at the front
                block.Prop(PipeStack, x, z - Across * 0.5f - 2.5f, 0f);
            }
            Bund(block, bund);

            // the stand: where the tanker couples up, with the pumps and the notice
            float standZ = block.Near + 8f;
            block.Prop(GasPumpBase, middle + 6f, standZ, 0f);
            block.Prop(GasPump, middle + 6f, standZ, 180f);
            block.Prop(DangerSign, middle - 8f, standZ - 1f, 0f);
            block.Prop(KeepOut, block.In + 3f, block.Near + 3f, 0f);
            if (Chance(rng, 0.8)) block.Prop(BoxLorry, middle + 2f, standZ + 6f, 0f);

            var yard = Rect.MinMaxRect(block.In + 2f, block.Near + 3f, block.Out - 2f, bundZ - 2f);
            block.Scatter(Any(Chemicals, rng), rng.Next(2, 5), yard, 8f);
            block.Prop(PipeRiserWide, block.Out - 5f, bundZ - 4f, 0f);
            FillYard(block, yard, rng);
            // and the ground beside the compound, which the compound does not reach
            var beside = Rect.MinMaxRect(bund.xMax + 3f, bundZ, block.Out - 2f, block.Far - 2f);
            FillYard(block, beside, rng, stacked: true);
            Gatepost(block, rng);
        }

        /// <summary>A wall round the tank compound, made of the base pillar stretched along
        /// each run. Never scaled shorter than the piece is drawn, so the bund stands its own
        /// three metres - which is what a bund round a tank of this size would be.</summary>
        static void Bund(Block block, Rect over)
        {
            var foot = Foot(Pillar, 0f);
            float thick = Mathf.Max(0.3f, Mathf.Min(foot.x, foot.y));
            float y = Deck - Box(Pillar).min.y;
            // all FOUR sides. A bund open at the back is not a bund, and the back of this
            // one faces the street behind the parcel, where it is in plain view
            IndustrialBlocks.Lay(Pillar, block.Root, over.xMin, over.yMin, over.width, thick, 0f, y);
            IndustrialBlocks.Lay(Pillar, block.Root, over.xMin, over.yMax - thick, over.width, thick, 0f, y);
            IndustrialBlocks.Lay(Pillar, block.Root, over.xMin, over.yMin, thick, over.height, 0f, y);
            IndustrialBlocks.Lay(Pillar, block.Root, over.xMax - thick, over.yMin, thick, over.height, 0f, y);
            // and the wall itself is ground: nothing is set down on top of it
            block.Book(new Rect(over.xMin, over.yMin, over.width, thick));
            block.Book(new Rect(over.xMin, over.yMax - thick, over.width, thick));
            block.Book(new Rect(over.xMin, over.yMin, thick, over.height));
            block.Book(new Rect(over.xMax - thick, over.yMin, thick, over.height));
        }

        /// <summary>
        /// The empty plot: a yard nobody has built on, and the one parcel in the quarter that
        /// is meant to look unused.
        ///
        /// It is a RECIPE and not left-over ground. Ground the roads could not use stays bare
        /// and goes in the report for somebody to answer for (Docs/core-district-plan.md);
        /// a plot with a fence round it, a board on the corner and weeds through the hard
        /// standing is a decision, and every industrial estate has one.
        /// </summary>
        static void Wasteland(Block block, System.Random rng)
        {
            block.Wall = Wall.Wire;

            float middle = (block.In + block.Out) * 0.5f;
            block.Way = Gate(block, middle - 5f, middle + 5f);

            var plot = Rect.MinMaxRect(block.In + 2f, block.Near + 3f, block.Out - 2f, block.Far - 2f);

            // what is left of whoever was here last
            block.Prop(Skip, block.In + 5f, block.Far - 6f, Between(rng, 80f, 100f));
            block.Prop(Dumpster, block.Out - 5f, block.Near + 5f, Between(rng, 260f, 280f));
            block.Prop(BurnBarrel, middle + 8f, block.Near + 6f, 0f);
            block.Scatter(DeadTree, rng.Next(2, 5), plot, 30f);
            block.Scatter(Pallet, rng.Next(2, 6), plot, 30f);
            block.Scatter(BarrelPlastic, rng.Next(1, 4), plot, 30f);
            block.Scatter(Crate, rng.Next(1, 3), plot, 30f);

            // the board on the corner, facing the road, and the notice on the gate
            block.Prop(Billboard, block.In + 8f, block.Near + 4f, 0f);
            block.Prop(ForSale, block.Way.y + 3f, block.Near + 2.5f, 0f);
            block.Prop(KeepOut, block.Way.x - 2f, block.Near + 2.5f, 0f);
            // what says disused is the WEAR, not a different floor: weed through the cracks
            // thick enough to read from the road, standing water where the ground has gone,
            // and the odd patch of make-do repair nobody came back to finish
            block.Strew(WeedClumps, rng.Next(30, 46), plot);
            block.Strew(Puddles, rng.Next(4, 9), plot);
            for (int k = 0, tries = 0; k < rng.Next(3, 7) && tries < 30; tries++)
            {
                float x = Block.Half(block.In + (float)rng.NextDouble() * Mathf.Max(0f, block.Out - block.In));
                float z = Block.Half(block.Near + (float)rng.NextDouble() * Mathf.Max(0f, block.Far - block.Near));
                if (block.Mark(Patch, x, z, 90f * rng.Next(4)) != null) k++;
            }
            Lamps(block, plot, 1);
            Gatepost(block, rng);
        }

        // ------------------------------------------------------------------- the dressing

        /// <summary>
        /// A gate opening, kept inside the block it belongs to.
        ///
        /// A recipe works its opening out from whatever it managed to build on the frontage,
        /// and on the narrowest parcel that arithmetic can walk off the end of the block: the
        /// wall run is clipped to the block and the opening is not, so a leaf is hung past
        /// the corner and <see cref="Block.WallGap"/> is credited for metres of wall that
        /// were never wanted.
        /// </summary>
        static Vector2 Gate(Block block, float from, float to)
        {
            float first = Mathf.Max(block.In, from);
            float last = Mathf.Min(block.Out, to);
            if (last - first < 7f) last = Mathf.Min(block.Out, first + 7f);
            return last > first ? new Vector2(first, last) : new Vector2(0f, 0f);
        }

        /// <summary>
        /// What fills the ground a recipe has left over, in proportion to how much of it
        /// there is.
        ///
        /// The recipes were written for a block that picked its own size, so their stock was
        /// a fixed handful: four barrels here, three pallets there. In a quarter the size
        /// comes in, and the same handful on a 105 x 75 m parcel is a works with a car park
        /// where its yard should be - which is what the first drawing of the quarter looked
        /// like from above, and the one thing about it that read as unfinished rather than
        /// as industry.
        ///
        /// So the yard is furnished by the ACRE: a lorry every so many square metres, stock
        /// in ranks along the fence, and stacks in the middle. Everything goes through
        /// <see cref="Block.Prop"/>, which refuses what will not fit - so a small yard is
        /// simply given less, and nothing has to know how big it is.
        /// </summary>
        static void FillYard(Block block, Rect yard, System.Random rng, bool stacked = false)
        {
            if (yard.width < 14f || yard.height < 14f) return;
            float area = yard.width * yard.height;

            // the vehicles that live here, nose to tail down the far side of the yard
            int fleet = Mathf.Clamp(Mathf.RoundToInt(area / 900f), 1, 5);
            for (int k = 0; k < fleet; k++)
            {
                float z = yard.yMin + 5f + k * 9.5f;
                if (z > yard.yMax - 5f) break;
                string body = k % 3 == 0 ? Van : k % 3 == 1 ? BoxLorry : TownLorry;
                block.Prop(body, yard.xMax - 5f, z, 90f + (Chance(rng, 0.5) ? 180f : 0f));
            }

            // stock in ranks along the near fence, as much of it as the ground takes
            int deep = Mathf.Clamp(Mathf.RoundToInt(yard.height / 14f), 1, 4);
            Ranks(block, BarrelMetal, yard.xMin + 1.5f, yard.yMin + 2f, 3, deep + 2, 1.15f);
            Ranks(block, Pallet, yard.xMin + 6.5f, yard.yMin + 2f, 2, deep + 1, 1.6f);

            // and stacks out in the middle, which is what a yard has that a car park has not
            int stacks = Mathf.Clamp(Mathf.RoundToInt(area / 750f), 1, 7);
            var pallet = Box(Pallet).size;
            for (int k = 0; k < stacks; k++)
            {
                float x = Block.Half(yard.xMin + 10f + (float)rng.NextDouble() * Mathf.Max(0f, yard.width - 18f));
                float z = Block.Half(yard.yMin + 3f + (float)rng.NextDouble() * Mathf.Max(0f, yard.height - 6f));
                if (stacked && Chance(rng, 0.45))
                {
                    if (block.Prop(Any(Containers, rng), x, z, 90f * rng.Next(2)) == null) continue;
                    if (Chance(rng, 0.55))
                        block.Atop(Any(Containers, rng), x, z, 90f * rng.Next(2), Box(Containers[0]).size.y);
                    continue;
                }
                int high = rng.Next(2, 5);
                for (int t = 0; t < high; t++)
                {
                    var stood = t == 0 ? block.Prop(Pallet, x, z, 90f * rng.Next(4))
                                       : block.Atop(Pallet, x, z, 90f * rng.Next(4), t * pallet.y);
                    if (stood == null) break;
                }
                if (Chance(rng, 0.5)) block.Prop(Any(Chemicals, rng), x + 2.2f, z, 0f);
            }

            block.Scatter(Crate, Mathf.Clamp(Mathf.RoundToInt(area / 1400f), 1, 5), yard, 10f);
            block.Scatter(WireSpool, Mathf.Clamp(Mathf.RoundToInt(area / 2200f), 0, 3), yard, 0f);
            block.Scatter(BarrelPlastic, Mathf.Clamp(Mathf.RoundToInt(area / 1600f), 1, 5), yard, 8f);
            Lamps(block, yard, Mathf.Clamp(Mathf.RoundToInt(yard.width / 30f), 1, 4));
        }

        /// <summary>Stock set down in ranks: what a yard looks like where somebody is
        /// working, as against <see cref="Block.Scatter"/>, which is what it looks like where
        /// somebody dropped things. Both belong in a yard and neither on its own does.</summary>
        static void Ranks(Block block, string path, float x, float z, int across, int along, float pitch)
        {
            for (int a = 0; a < across; a++)
                for (int b = 0; b < along; b++)
                {
                    if (Chance(block.Rng, 0.12)) continue;   // one gone from the rank
                    block.Prop(path, x + a * pitch, z + b * pitch, 0f);
                }
        }

        /// <summary>
        /// Yard lamps down the working ground, far enough apart that they read as lighting
        /// rather than as a fence of their own.
        ///
        /// Turned to face INTO the yard. The pole reaches its arm out along its own +z and
        /// every one of these stands on the yard's far edge, so at yaw 0 the whole set was
        /// lighting the wall behind it and leaving the ground it is there for in the dark.
        /// </summary>
        static void Lamps(Block block, Rect over, int count)
        {
            if (over.width <= 0f || over.height <= 0f) return;
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count;
                float x = over.xMin + over.width * t, z = over.yMax - 1.2f;

                var pole = Shuffled(block, YardLamp, x, z, 180f, new Vector2(1.5f, 0f), 7);
                if (pole == null) continue;

                // The moths, under the head the arm now carries at z - 0.6.
                block.Fix(BugLights, pole.Value.x, pole.Value.y - 0.55f, 0f, 3.35f);
            }
        }

        /// <summary>
        /// A prop worth moving a pace for: tried where it was asked and then a few steps either
        /// side of it, taking the first ground that is free.
        ///
        /// For the pieces whose PRESENCE is the point and whose exact metre is not - a yard
        /// lamp, a loading dock. They go down late, on ground the loose stock has already had
        /// first refusal of, so the one hand-picked metre they were written for is as often as
        /// not under a lorry by the time they ask for it. A lamp two metres to the left is a
        /// lamp; no lamp at all is a yard gone dark.
        ///
        /// Probed through Room before Prop, the same way Scatter does it, so a spot that was
        /// merely crowded is not counted against the block as something that would not fit.
        ///
        /// Returns the ground it settled on, or null if none of the paces was free. The CENTRE
        /// rather than the instance, because the instance's transform is not where it was asked
        /// to go: Settle offsets the transform by the prefab's own box centre, so a piece whose
        /// mass hangs off its pivot - a lamp, whose arm reaches a half metre out - reports a
        /// position half a metre from the spot the caller picked. Anything measured off that
        /// lands beside the thing it was meant to sit on.
        /// </summary>
        static Vector2? Shuffled(Block block, string path, float x, float z, float yaw,
                                 Vector2 step, int tries)
        {
            var foot = Foot(path, yaw);

            for (var k = 0; k < tries; k++)
            {
                // 0, -1, +1, -2, +2 ... paces out from where it was asked for.
                var pace = (k % 2 == 0 ? k : -(k + 1)) * 0.5f;
                var at = new Vector2(x, z) + step * pace;
                var probe = new Rect(at.x - foot.x * 0.5f, at.y - foot.y * 0.5f, foot.x, foot.y);

                if (!block.Room(probe)) continue;
                if (block.Prop(path, at.x, at.y, yaw) == null) return null;

                return at;
            }

            return null;
        }

        // ------------------------------------------------------------------ the weathering

        /// <summary>
        /// The ground pass, and the layer a composed block was most obviously missing.
        ///
        /// A works yard is not a clean plane with things standing on it. What the pack's own
        /// artists put on theirs - counted off the demo compound - is two dozen puddles,
        /// twenty-odd clumps of weed coming up through the asphalt, loose stone, drains,
        /// litter, and a mottle of differently-worn floor tiles over the one surface the floor
        /// pass laid everywhere. It is the cheapest thing in this file and the one that
        /// changes the picture most, because it is what the camera is pointed at.
        ///
        /// Everything here goes down through <see cref="Block.Decal"/>, so none of it books
        /// ground and none of it can push a barrel out of the way.
        /// </summary>
        static void Weather(Block block, System.Random rng)
        {
            var yard = block.Yard(0.8f);
            if (yard.width < Cell || yard.height < Cell) return;

            // ONE running lift for the whole pass, stepped per piece laid.
            //
            // Not tidiness: these are quads with no thickness of their own, and two of them
            // sharing a plane where they overlap is a z-fight that shimmers as the camera
            // moves. Sprinkling a dozen 3 m patches across a yard makes overlaps the rule
            // rather than the exception, so every one gets a plane to itself - in the order it
            // went down, which is also the order they ought to stack in.
            var lift = 0f;

            // The mottle first, at the bottom: patches of a different wear of the same asphalt,
            // at the gang kit's own 3 m rather than stretched onto this block's 5 m grid -
            // stretched, they would only re-draw the grid they are there to break up.
            lift = Sprinkle(block, rng, WornAsphalt, rng.Next(11, 18), yard, lift);
            lift = Sprinkle(block, rng, Drains, rng.Next(1, 4), yard, lift);
            lift = Sprinkle(block, rng, PaintedFloor, rng.Next(1, 3), yard, lift);

            // Then standing water over it, which is what says outdoors and unswept.
            lift = Sprinkle(block, rng, Puddles, rng.Next(7, 12), yard, lift);
            lift = Sprinkle(block, rng, Litter, rng.Next(1, 4), yard, lift);

            // And last the two that carry a height of their own, so nothing they land on can
            // z-fight them: weed through the cracks, and loose stone.
            Weeds(block, rng);
            Sprinkle(block, rng, Rubble, rng.Next(2, 5), yard, lift);
        }

        /// <summary>Between one flat overlay and the next. Small enough that nothing reads as
        /// floating from the height a city is looked at, large enough that the depth buffer can
        /// still tell two quads apart at the far end of a block.</summary>
        const float PlaneStep = 0.0015f;

        /// <summary>
        /// Lays flat overlays at random over an area, each on a plane of its own.
        ///
        /// <paramref name="lift"/> arrives as the height the first one goes down at and is
        /// returned as the height the NEXT call should start from, so one weathering pass
        /// shares a single ladder and no two quads in it are ever coplanar.
        ///
        /// A refused piece is one that would have been out on the pavement or inside a wall.
        /// The next roll is as good a place as the last, so it is retried rather than mourned -
        /// and it costs no rung, because nothing was laid.
        /// </summary>
        static float Sprinkle(Block block, System.Random rng, string[] of, int count,
                              Rect over, float lift)
        {
            for (int laid = 0, guard = 0; laid < count && guard < count * 6; guard++)
            {
                float x = over.xMin + (float)rng.NextDouble() * over.width;
                float z = over.yMin + (float)rng.NextDouble() * over.height;
                if (block.Decal(of[rng.Next(of.Length)], x, z, 90f * rng.Next(4), lift) == null)
                    continue;

                laid++;
                lift += PlaneStep;
            }

            return lift;
        }

        /// <summary>
        /// Weed at the foot of every wall and every building: the strip nothing drives over
        /// and nobody sweeps, which is the only place it survives in a yard.
        ///
        /// Laid ALONG the line it grows against. The crack pieces are drawn long in their own
        /// z, so a run beside a wall that goes east-west is a quarter turn - and a tuft laid
        /// across a wall instead of along it reads as something dropped there.
        /// </summary>
        static void Weeds(Block block, System.Random rng)
        {
            // The perimeter is half a metre thick and stands on the yard's own line - a cell
            // in from the block edge where there is a kerb, on the boundary where the side is
            // a shared fence - so its inside face is half a metre in and the weed goes just
            // clear of that.
            var foot = block.Yard(0.75f);

            WeedLine(block, rng, foot.xMin, foot.xMax, foot.yMin, alongX: true);
            WeedLine(block, rng, foot.xMin, foot.xMax, foot.yMax, alongX: true);
            WeedLine(block, rng, foot.yMin, foot.yMax, foot.xMin, alongX: false);
            WeedLine(block, rng, foot.yMin, foot.yMax, foot.xMax, alongX: false);

            foreach (var built in block.Built)
            {
                if (built.width <= 0f || built.height <= 0f) continue;

                WeedLine(block, rng, built.xMin, built.xMax, built.yMin - 0.35f, alongX: true);
                WeedLine(block, rng, built.xMin, built.xMax, built.yMax + 0.35f, alongX: true);
                WeedLine(block, rng, built.yMin, built.yMax, built.xMin - 0.35f, alongX: false);
                WeedLine(block, rng, built.yMin, built.yMax, built.xMax + 0.35f, alongX: false);
            }
        }

        /// <summary>One run of weed along a line, at a loose pitch with the gaps rolled, so it
        /// comes out as weed rather than as a hedge.</summary>
        static void WeedLine(Block block, System.Random rng, float from, float to, float at,
                             bool alongX)
        {
            for (var s = from; s < to; s += Between(rng, 1.8f, 5.5f))
            {
                bool skip = Chance(rng, 0.35);
                var path = WeedClumps[rng.Next(WeedClumps.Length)];
                var drift = ((float)rng.NextDouble() * 2f - 1f) * 0.22f;

                if (skip) continue;

                block.Decal(path, alongX ? s : at + drift, alongX ? at + drift : s,
                            alongX ? 90f : 0f);
            }
        }

        // ------------------------------------------------------------------ the clutter

        /// <summary>
        /// Bin bags heaped beside something that collects them - a skip, a back door.
        ///
        /// Rolled from ONE colour family. Five different bags in a pile reads as five bags
        /// somebody arranged; four of one reads as rubbish, which is the point of them.
        /// </summary>
        static void BinBags(Block block, System.Random rng, float x, float z, int count)
        {
            var family = BagFamilies[rng.Next(BagFamilies.Length)];
            for (int laid = 0, guard = 0; laid < count && guard < count * 8; guard++)
            {
                float bx = x + Between(rng, -0.9f, 0.9f);
                float bz = z + Between(rng, -0.7f, 0.7f);
                float yaw = (float)rng.NextDouble() * 360f;
                var bag = family[rng.Next(family.Length)];

                // probed before it is asked for, the way Scatter does: a pile is crowded by
                // design, and every bag that landed on the last one would otherwise go into
                // the refusal report ahead of the one thing that really did not fit
                var foot = Foot(bag, yaw);
                if (!block.Room(new Rect(bx - foot.x * 0.5f, bz - foot.y * 0.5f, foot.x, foot.y))) continue;
                if (block.Prop(bag, bx, bz, yaw) != null) laid++;
            }
        }

        /// <summary>
        /// The dangerous-goods corner: timber and gas bottles against a wall with the board
        /// that says so. What a works keeps where it can be got at and where nobody drives.
        /// The stack is drawn long in its own z, so <paramref name="yaw"/> is which way the
        /// wall it leans on runs.
        /// </summary>
        static void HazardStore(Block block, System.Random rng, float x, float z, float yaw)
        {
            // The stack's long axis at this yaw, and the axis at right angles to it. A proper
            // perpendicular rather than a swap of the two components, which happens to give the
            // same answer for an axis-aligned run and a wrong one for anything else.
            var along = yaw == 0f ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            var across = new Vector2(along.y, -along.x);

            // The timber is the anchor, and it shuffles ALONG the wall it leans on. Everything
            // after it hangs off where it actually ended up rather than off where it was asked
            // for, which is what holds the group together on a busy yard - and if the timber
            // finds nowhere at all, nothing half-lands in the middle of the ground instead.
            var stack = Shuffled(block, Woodstack, x, z, yaw, along, 9);
            if (stack == null) return;

            var foot = stack.Value;
            var bottles = foot + across * 1.5f;
            block.Prop(PropaneTall, bottles.x, bottles.y, 0f);
            if (Chance(rng, 0.7))
            {
                var pair = bottles + along * 0.65f + across * 0.55f;
                block.Prop(PropaneTallB, pair.x, pair.y, 0f);
            }

            // No caution board down here. Sign_Caution_01 is 27 cm tall and Sign_Danger_01 is
            // 14: they are plates for a WALL, and stood on open ground the camera never finds
            // them. The wall plates live on the perimeter, where Block.StreetPlate hangs them.
            var loose = foot + across * 2.6f;
            block.Prop(GasCan, loose.x, loose.y, (float)rng.NextDouble() * 360f);
        }

        /// <summary>What is left standing where a lorry was unloaded and not yet cleared: a
        /// stack or two of boxes, a wrapped pallet load, a bucket somebody put down.</summary>
        static void Unloaded(Block block, System.Random rng, float x, float z)
        {
            // The box stack anchors it and is allowed a pace either way; the rest is measured
            // off where it landed, so the whole pile arrives together or not at all.
            var stack = Shuffled(block, Chance(rng, 0.5) ? BoxStack : BoxStackB,
                                 x, z, 90f * rng.Next(4), new Vector2(1f, 0f), 9);
            if (stack == null) return;

            x = stack.Value.x;
            z = stack.Value.y;

            if (Chance(rng, 0.7))
                block.Prop(WrappedLoad, x + Between(rng, 1.8f, 2.6f), z + Between(rng, -0.8f, 0.8f),
                           90f * rng.Next(4));
            if (Chance(rng, 0.5))
                block.Prop(YardBucket, x - Between(rng, 1.2f, 1.8f), z + Between(rng, -1f, 1f),
                           (float)rng.NextDouble() * 360f);
            if (Chance(rng, 0.4))
                block.Prop(PaintCan, x - Between(rng, 1.4f, 2.2f), z + Between(rng, -1f, 1f),
                           (float)rng.NextDouble() * 360f);
        }

        /// <summary>The plant a works runs on that is not a building: two process tanks and the
        /// substation box that feeds them. Stood as a GROUP rather than sprinkled, because
        /// this is equipment and equipment is installed next to what it serves.</summary>
        static void ProcessPlant(Block block, System.Random rng, float x, float z, float yaw)
        {
            var first = Shuffled(block, ProcessTank, x, z, yaw, new Vector2(1f, 0f), 9);
            if (first == null) return;

            x = first.Value.x;
            z = first.Value.y;

            if (Chance(rng, 0.7)) block.Prop(ProcessTankB, x + 2.2f, z + 0.4f, yaw);
            block.Prop(YardSubstation, x + 4.4f, z, yaw);
        }

        /// <summary>What every gate has around it: a board saying keep out, a cone or two
        /// where the wheels cut the corner, and a block of concrete narrowing the way in so
        /// that a lorry has to slow down for it.</summary>
        static void Gatepost(Block block, System.Random rng)
        {
            if (block.Way.y <= block.Way.x) return;
            float z = block.Near + 2.2f;
            block.Prop(DangerSign, block.Way.x - 1.2f, z, 0f);
            if (Chance(rng, 0.6)) block.Prop(Cone, block.Way.x + 1.2f, z + 1.5f, 0f);
            if (Chance(rng, 0.6)) block.Prop(Cone, block.Way.y - 1.2f, z + 1.5f, 0f);
            if (Chance(rng, 0.5)) block.Prop(Barrier, block.Way.y + 1.4f, z, 0f);
            // and the tyre marks the turning-in leaves, which is the cheapest thing on the
            // block and the one that says lorries come through here
            block.Strew(Puddles, rng.Next(1, 3),
                        Rect.MinMaxRect(block.Way.x, block.Near + 3f, block.Way.y, block.Near + 10f));

            // The threshold itself, which had nothing on it at all. Both of these go down
            // through Fix rather than Prop: the drive was booked as taken the moment the way
            // in was set, so a fit test here could only ever say no - and a gate cheek is
            // exactly where a bollard belongs. The way in is always on the south side and the
            // south side is always the street the block fronts, so the wall line there is one
            // kerb cell in.
            //
            // The step is floored rather than trusted. It is a MEASURED width, and a measured
            // width that ever came back zero - a prefab swapped for one with no renderers, a
            // pack reimported - would leave this loop running until the editor was killed.
            var bump = Mathf.Max(0.5f, Foot(SpeedBump, 0f).x);
            for (var x = block.Way.x + bump * 0.5f; x < block.Way.y; x += bump)
                block.Fix(SpeedBump, x, Cell + 0.9f, 0f);

            block.Fix(Bollard, block.Way.x + 0.5f, Cell + 1.6f, 0f);
            block.Fix(Bollard, block.Way.y - 0.5f, Cell + 1.6f, 0f);
        }

    }
}
