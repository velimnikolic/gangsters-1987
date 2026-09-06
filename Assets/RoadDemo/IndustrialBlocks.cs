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
    public static partial class IndustrialBlocks
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
        const string Smoke = LivingCity.Ambient.FireSmokeFx.Smoke;
        const string Steam = LivingCity.Ambient.FireSmokeFx.Steam;
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

        /// <summary>Employees use the shared passenger fleet; the work van remains.</summary>
        static readonly string[] StaffCars = new List<string>(
            LivingCity.Gameplay.CivilianVehicleCatalog.PassengerPaths)
        {
            GangVeh + "SM_Veh_Van_01.prefab",
        }.ToArray();

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
        public static void ForgetMeasurements() => Measured.Clear();

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
            // Particle bounds may still be at the origin before their first simulation.
            // They describe an effect envelope, never the footprint of its building.
            var renderers = go.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
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
            float radians = yaw * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(radians)), s = Mathf.Abs(Mathf.Sin(radians));
            return new Vector2(size.x * c + size.z * s, size.x * s + size.z * c);
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
            OperationalMarkings(block);
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

        /// <summary>A brick administration building and a two-bay production floor around a working court.</summary>
        static void Works(Block block, System.Random rng)
        {
            FabricationWorks(block, rng);
        }

        /// <summary>An east-side process hall, west-side vessel bank and frontage maintenance shop.</summary>
        static void Plant(Block block, System.Random rng)
        {
            ProcessingWorks(block, rng);
        }

        /// <summary>The depot: one big shed across the back with its doors on the yard, and a
        /// forecourt in front of it wide enough to turn a lorry in.</summary>
        static void Depot(Block block, System.Random rng)
        {
            Distribution(block, rng);
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
            block.ReserveRoute(new Rect(block.In + 1f, lane - 9f,
                                       block.Out - block.In - 2f, 8f));

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
                    int tall = Chance(rng, 0.25) ? 1 : 2;
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

        /// <summary>A row of trade workshops opening onto a shared service court.</summary>
        static void Strip(Block block, System.Random rng)
        {
            TradeCourt(block, rng);
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
            int patches = rng.Next(3, 7);
            for (int k = 0, tries = 0; k < patches && tries < 30; tries++)
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
            lift = Sprinkle(block, rng, WornAsphalt, rng.Next(2, 5), yard, lift);
            lift = Sprinkle(block, rng, Drains, rng.Next(1, 4), yard, lift);
            // Working markings are laid from the circulation plan, never scattered.

            // Then standing water over it, which is what says outdoors and unswept.
            lift = Sprinkle(block, rng, Puddles, rng.Next(2, 5), yard, lift);
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
