using System.Collections.Generic;
using UnityEngine;

namespace SuburbDemo
{
    // The suburb's cupboard: every Synty POLYGON Town piece the demo uses, where it
    // lives, what the demo measured about it, and the few helpers every builder file
    // needs - loading through the AssetDatabase (editor only, the other demos' way),
    // measuring a prefab once, dropping a prop, laying a 5 m tile by its cell.
    //
    // The Town kit's ground tiles are 5 x 5 m with the pivot on the +X/+Z corner,
    // covering the square towards local -X/-Z - the same convention as the City kit,
    // so a cell (min corner mx, mz) is filled by the pivots
    //   yaw 0 -> (mx+5, mz+5), 90 -> (mx+5, mz), 180 -> (mx, mz), 270 -> (mx, mz+5).
    // Read off the pack's own Demo scene (3573 instances): a sidewalk straight has its
    // kerb on local +Z; the block-corner piece (Sidewalk_Corner_03) has road on +X and
    // +Z; the dead-end road cap (Road_Corner_End_02) has road on +X and +Z (its round
    // corner at -X/-Z) and its L-shaped 10 x 10 m sidewalk wrap (Sidewalk_Corner_End_02)
    // is set down at the same pivot and yaw; a zebra tile (Road_Crossing_02) runs along
    // local X with the kerb at -Z; a driveway tile runs along Z with the street at +Z;
    // a path tile runs along Z.
    public static class TownKit
    {
        public const float Cell = 5f;

        // ------------------------------------------------------------ folders
        public const string Root = "Assets/Synty/PolygonTown/Prefabs/";
        public const string Bld = Root + "Buildings/";
        public const string Presets = Root + "Buildings/Presets/";
        public const string Env = Root + "Environment/";
        public const string Props = Root + "Props/";
        public const string Veh = Root + "Vehicles/";
        public const string Chr = Root + "Characters/";
        public const string Gen = Root + "Generic/";

        // ------------------------------------------------------------ ground tiles
        public const string Road = Env + "SM_Env_Road_01.prefab";
        public const string Zebra = Env + "SM_Env_Road_Crossing_02.prefab";
        public const string RoadCap = Env + "SM_Env_Road_Corner_End_02.prefab";
        public const string RoadParking = Env + "SM_Env_Road_Parking_01.prefab";
        public const string RoadPatch = Env + "SM_Env_Road_Patch_01.prefab";
        public const string SpeedBump = Env + "SM_Env_Road_SpeedBump_01.prefab";
        public const string Sidewalk = Env + "SM_Env_Sidewalk_Straight_01.prefab";
        public static readonly string[] SidewalkAlts = { Env + "SM_Env_Sidewalk_Straight_02.prefab", Env + "SM_Env_Sidewalk_Straight_03.prefab" };
        public const string SidewalkCorner = Env + "SM_Env_Sidewalk_Corner_03.prefab";      // convex block corner: road on +X and +Z
        public const string SidewalkEndCorner = Env + "SM_Env_Sidewalk_Corner_End_01.prefab"; // pavement turning a grass corner: pavement on +X and +Z
        public const string SidewalkCapWrap = Env + "SM_Env_Sidewalk_Corner_End_02.prefab";   // 10x10 L around a RoadCap, same pivot and yaw
        public const string SidewalkCrossing = Env + "SM_Env_Sidewalk_Crossing_01.prefab";
        public const string SidewalkDriveway = Env + "SM_Env_Sidewalk_Driveway_Wide_01.prefab";
        public const string SidewalkPath = Env + "SM_Env_Sidewalk_Straight_Path_01.prefab";
        public const string Driveway = Env + "SM_Env_Driveway_Wide_01.prefab";
        public const string Path = Env + "SM_Env_Path_01.prefab";
        public const string PathDoor = Env + "SM_Env_Path_Door_01.prefab";
        public const string Grass = Env + "SM_Env_Grass_01.prefab";
        public const string Grass2 = Env + "SM_Env_Grass_02.prefab";
        public const string Concrete = Env + "SM_Env_Concrete_Base_01.prefab";
        public const string Dirt = Env + "SM_Env_DirtPatch_01.prefab";

        // ------------------------------------------------------------ buildings
        public enum Side { PlusX, MinusX, PlusZ, MinusZ }

        public struct House
        {
            public string Path;
            public Side Front;      // the facade with the door, in the prefab's own frame
            public float Width;     // across the facade, metres (from the pack's convex hulls)
            public float Depth;     // facade to back
            public float Height;
            public bool Big;        // wants a 30 m lot
            public bool Narrow;     // fits a 15 m lot
            public bool Shallow;    // fits a 20 m deep lot
        }

        // Fronts read off the pack's Demo scene (nearest driveway / path / kerb to each
        // placed preset) and cross-checked against the Overview scene, where every
        // preset is turned to face the camera; 09 is the one the two readings disagree
        // on. debugFronts on the builder marks every doorstep so a wrong row shows.
        public static readonly House[] Houses =
        {
            new House { Path = Presets + "SM_Bld_House_Preset_01.prefab", Front = Side.PlusX,  Width = 6.5f,  Depth = 9.0f,  Height = 5.6f, Narrow = true, Shallow = true },
            new House { Path = Presets + "SM_Bld_House_Preset_03.prefab", Front = Side.PlusX,  Width = 13.6f, Depth = 13.6f, Height = 6.2f },
            new House { Path = Presets + "SM_Bld_House_Preset_04.prefab", Front = Side.PlusX,  Width = 13.2f, Depth = 13.7f, Height = 6.2f },
            new House { Path = Presets + "SM_Bld_House_Preset_05.prefab", Front = Side.PlusX,  Width = 9.0f,  Depth = 14.1f, Height = 6.2f, Narrow = true },
            new House { Path = Presets + "SM_Bld_House_Preset_06.prefab", Front = Side.PlusX,  Width = 11.1f, Depth = 14.1f, Height = 6.3f },
            new House { Path = Presets + "SM_Bld_House_Preset_07.prefab", Front = Side.MinusZ, Width = 13.2f, Depth = 12.8f, Height = 7.6f },
            new House { Path = Presets + "SM_Bld_House_Preset_08.prefab", Front = Side.PlusX,  Width = 13.5f, Depth = 18.0f, Height = 6.4f },
            new House { Path = Presets + "SM_Bld_House_Preset_09.prefab", Front = Side.MinusX, Width = 8.5f,  Depth = 11.6f, Height = 8.6f, Narrow = true, Shallow = true },
            new House { Path = Presets + "SM_Bld_House_Preset_10.prefab", Front = Side.MinusZ, Width = 13.6f, Depth = 8.8f,  Height = 8.6f, Shallow = true },
            new House { Path = Presets + "SM_Bld_House_Preset_11.prefab", Front = Side.MinusZ, Width = 18.2f, Depth = 13.8f, Height = 9.1f, Big = true },
        };
        public const string Cottage = Presets + "SM_Bld_House_Preset_02.prefab"; // 5.6 x 5.6, a garden studio
        public const string Garage = Presets + "SM_Bld_House_Preset_Garage_01.prefab"; // 6.5 x 9, door on -Z
        public const string Church = Bld + "SM_Bld_Church_01.prefab";               // 10.8 x 14.7 x 16.6, front +Z
        public static readonly string[] Shops = { Bld + "SM_Bld_Shop_01.prefab", Bld + "SM_Bld_Shop_02.prefab", Bld + "SM_Bld_Shop_03.prefab" }; // front +Z
        public const string ShopPad = Bld + "SM_Bld_Shop_Concrete_01.prefab";
        public const string ShopSign = Bld + "SM_Bld_Shop_Sign_01.prefab";
        public const string Shed = Bld + "SM_Bld_GardenShed_01.prefab";

        // The pack's twelve palettes: four wall colours (01 blue-grey, 02 cream, 03 pink,
        // 04 mint) in three finishes each (A siding + brick, B stone + shingles, C stucco
        // + tile roof). Every preset ships on 01_A; a house is given one of these instead.
        public const string Mats = "Assets/Synty/PolygonTown/Materials/Alts/";
        public static readonly string[] Palettes =
        {
            Mats + "PolygonTown_01_A.mat", Mats + "PolygonTown_01_B.mat", Mats + "PolygonTown_01_C.mat",
            Mats + "PolygonTown_02_A.mat", Mats + "PolygonTown_02_B.mat", Mats + "PolygonTown_02_C.mat",
            Mats + "PolygonTown_03_A.mat", Mats + "PolygonTown_03_B.mat", Mats + "PolygonTown_03_C.mat",
            Mats + "PolygonTown_04_A.mat", Mats + "PolygonTown_04_B.mat", Mats + "PolygonTown_04_C.mat",
        };

        // ------------------------------------------------------------ yard kit
        public const string FenceWood = Env + "SM_Env_Fence_Wood_Straight_01.prefab";   // 2.5 m, pivot at one end, runs -X
        public const string FenceWoodPost = Env + "SM_Env_Fence_Wood_Post_01.prefab";
        public const string FenceWhite = Env + "SM_Env_Fence_White_Straight_01.prefab"; // 2.5 m
        public const string FenceWhitePost = Env + "SM_Env_Fence_White_Post_01.prefab";
        public const string FenceWhiteGate = Env + "SM_Env_Fence_White_Gate_01.prefab";
        public static readonly string[] Hedges = { Env + "SM_Env_Hedge_01.prefab", Env + "SM_Env_Hedge_02.prefab", Env + "SM_Env_Hedge_03.prefab" }; // centred, ~6 m along X
        public static readonly string[] Flowers = { Env + "SM_Env_FlowerPatch_01.prefab", Env + "SM_Env_FlowerPatch_02.prefab", Env + "SM_Env_FlowerPatch_03.prefab" };
        public static readonly string[] Bushes = { Env + "SM_Env_Bush_01.prefab", Env + "SM_Env_Bush_02.prefab", Env + "SM_Env_Bush_03.prefab", Env + "SM_Env_Bush_04.prefab" };
        public static readonly string[] GrassTufts = { Env + "SM_Env_Grass_Patch_01.prefab", Env + "SM_Env_Grass_Patch_02.prefab", Env + "SM_Env_Grass_Patch_03.prefab", Env + "SM_Env_Grass_Patch_04.prefab" };
        public static readonly string[] YardTrees = { Env + "SM_Env_Tree_01.prefab", Env + "SM_Env_Tree_02.prefab", Env + "SM_Env_Tree_03.prefab" };
        public static readonly string[] FramedTrees = { Env + "SM_Env_Tree_03_Framed.prefab", Env + "SM_Env_Tree_02_Framed.prefab" }; // street trees in a kerbside planter
        public static readonly string[] TallTrees = { Env + "SM_Env_Tree_Tall_01.prefab", Env + "SM_Env_Tree_Tall_02.prefab", Env + "SM_Env_Tree_Pine_01.prefab", Env + "SM_Env_Tree_Pine_02.prefab", Env + "SM_Env_Tree_Large_01.prefab" };
        public const string TreeBase = Env + "SM_Env_Tree_Base_01.prefab";
        public const string GardenBox = Env + "SM_Env_Gardenbox_Double_01.prefab";
        public static readonly string[] Vegetables = { Env + "SM_Env_Plant_Cabbage_01.prefab", Env + "SM_Env_Plant_Carrot_01.prefab", Env + "SM_Env_Plant_Corn_01.prefab", Env + "SM_Env_Plant_Lettuce_01.prefab", Env + "SM_Env_Plant_Tomato_01.prefab", Env + "SM_Env_Plant_Pumpkin_01.prefab" };

        public const string Streetlamp = Props + "SM_Prop_Streetlamp_01.prefab";
        public const string Streetlamp2 = Props + "SM_Prop_Streetlamp_02.prefab";
        public const string SignPole = Props + "SM_Prop_StreetSign_Pole_01.prefab";
        public static readonly string[] StreetSigns = { Props + "SM_Prop_StreetSign_01.prefab", Props + "SM_Prop_StreetSign_02.prefab", Props + "SM_Prop_StreetSign_03.prefab", Props + "SM_Prop_StreetSign_04.prefab", Props + "SM_Prop_StreetSign_05.prefab", Props + "SM_Prop_StreetSign_06.prefab" };
        public const string StopSign = Props + "SM_Prop_Sign_Stop_01.prefab";
        public const string BusStopSign = Props + "SM_Prop_Sign_BusStop_01.prefab";
        public const string Bench = Props + "SM_Prop_ParkBench_01.prefab";
        public static readonly string[] Bins = { Props + "SM_Prop_RubbishBin_01.prefab", Props + "SM_Prop_RubbishBin_02.prefab" };
        public static readonly string[] TrashBags = { Props + "SM_Prop_TrashBag_01.prefab", Props + "SM_Prop_TrashBag_02.prefab" };
        public const string Manhole = Props + "SM_Prop_Manhole_01.prefab";
        public const string Drain = Props + "SM_Prop_Drain_01.prefab";
        public static readonly string[] LetterBoxes = { Props + "SM_Prop_LetterBox_01.prefab", Props + "SM_Prop_LetterBox_02.prefab", Props + "SM_Prop_LetterBox_03.prefab" };
        public const string ForSale = Props + "SM_Prop_ForSaleSign_01.prefab";
        public const string Hoop = Props + "SM_Prop_Hoop_01.prefab";
        public const string Sprinkler = Props + "SM_Prop_Sprinkler_01.prefab";
        public const string HoseReel = Props + "SM_Prop_HoseReel_01.prefab";
        public const string Barbeque = Props + "SM_Prop_Barbeque_01.prefab";
        public static readonly string[] OutdoorTables = { Props + "SM_Prop_Outdoor_Table_01.prefab", Props + "SM_Prop_Outdoor_Table_02.prefab" };
        public static readonly string[] Chairs = { Props + "SM_Prop_Chair_01.prefab", Props + "SM_Prop_Chair_02.prefab", Props + "SM_Prop_Chair_05.prefab" };
        public static readonly string[] Deckchairs = { Props + "SM_Prop_Deckchair_01.prefab", Props + "SM_Prop_Deckchair_02.prefab", Props + "SM_Prop_Deckchair_03.prefab", Props + "SM_Prop_Deckchair_04.prefab" };
        public const string Doghouse = Props + "SM_Prop_Doghouse_01.prefab";
        public static readonly string[] WashingLines = { Props + "SM_Prop_WashingLine_01.prefab", Props + "SM_Prop_WashingLine_02.prefab", Props + "SM_Prop_WashingLine_03.prefab", Props + "SM_Prop_WashingLine_04.prefab", Props + "SM_Prop_WashingLine_05.prefab" };
        public const string Trampoline = Props + "SM_Prop_Trampoline_01.prefab";
        public const string Sandpit = Props + "SM_Prop_Sandpit_01.prefab";
        public const string PoolInground = Props + "SM_Prop_Pool_Inground_01.prefab";
        public const string PoolAbove = Props + "SM_Prop_Pool_Aboveground_01 (1).prefab";
        public const string PoolChild = Props + "SM_Prop_Pool_Child_01.prefab";
        public const string PoolLadder = Props + "SM_Prop_Pool_Ladder_01.prefab";
        public static readonly string[] PoolFloats = { Props + "SM_Prop_Pool_Float_Ring_01.prefab", Props + "SM_Prop_Pool_Float_Ring_03.prefab", Props + "SM_Prop_Pool_Float_Bed_01.prefab", Props + "SM_Prop_Pool_Float_Noodle_01.prefab" };
        public const string Compost = Props + "SM_Prop_Compost_01.prefab";
        public const string Wheelbarrow = Props + "SM_Prop_Wheelbarrow_01.prefab";
        public const string Swing = Props + "SM_Prop_Playground_Swing_01.prefab";
        public const string Fort = Props + "SM_Prop_Playground_Fort_01.prefab";
        public const string Slide = Props + "SM_Prop_Playground_Slide_01.prefab";
        public const string Roundabout = Props + "SM_Prop_Playground_Roundabout_01.prefab";
        public const string PlayTable = Props + "SM_Prop_Playground_Table_01.prefab";
        public const string Fountain = Props + "SM_Prop_Fountain_01.prefab";
        public const string FountainBase = Props + "SM_Prop_Fountain_Base_01.prefab";
        public const string BikeStand = Props + "SM_Prop_BikeStand_01.prefab";
        public const string Vending = Props + "SM_Prop_VendingMachine_01.prefab";
        public const string Gaspump = Props + "SM_Prop_Gaspump_01.prefab";
        public const string GaspumpBase = Props + "SM_Prop_Gaspump_Base_01.prefab";
        public const string GaspumpCover = Props + "SM_Prop_Gaspump_Cover_01.prefab";
        public static readonly string[] OutdoorLights = { Props + "SM_Prop_Outdoor_Light_01.prefab", Props + "SM_Prop_Outdoor_Light_02.prefab" };
        public const string SatDish = Props + "SM_Prop_SatelliteDish_01.prefab";
        public const string Antenna = Props + "SM_Prop_Antenna_01.prefab";
        public const string SolarPanel = Props + "SM_Prop_SolarPanel_01.prefab";
        public static readonly string[] Boxes = { Props + "SM_Prop_CardboardBox_01.prefab", Props + "SM_Prop_CardboardBox_02.prefab", Props + "SM_Prop_CardboardBox_05.prefab" };
        public const string IceFreezer = Props + "SM_Prop_Icecream_Freezer_01.prefab";
        public const string Propane = Props + "SM_Prop_Propane_01.prefab";

        // ------------------------------------------------------------ life
        public static readonly string[] Cars = { Veh + "SM_Veh_Pickup_01.prefab", Veh + "SM_Veh_Convertable_01.prefab" };
        public static readonly string[] LongVehicles = { Veh + "SM_Veh_SchoolBus_01.prefab", Veh + "SM_Veh_Bus_01.prefab", Veh + "SM_Veh_Truck_01.prefab", Veh + "SM_Veh_Truck_Delivery_01.prefab", Veh + "SM_Veh_Truck_Garbage_01.prefab" };
        public const string SchoolBus = Veh + "SM_Veh_SchoolBus_01.prefab";
        public const string DeliveryTruck = Veh + "SM_Veh_Truck_Delivery_01.prefab";
        public static readonly string[] People =
        {
            Chr + "SM_Chr_Father_01.prefab", Chr + "SM_Chr_Father_02.prefab", Chr + "SM_Chr_Mother_01.prefab", Chr + "SM_Chr_Mother_02.prefab",
            Chr + "SM_Chr_Son_01.prefab", Chr + "SM_Chr_Daughter_01.prefab", Chr + "SM_Chr_SchoolBoy_01.prefab", Chr + "SM_Chr_SchoolGirl_01.prefab",
            Chr + "SM_Chr_ShopKeeper_01.prefab",
        };

        // ------------------------------------------------------------ loading

        static Dictionary<string, string> _byName;

        /// <summary>A Town prefab by its file name, from anywhere under the pack's Prefabs
        /// folder - the clusters lifted off the demo scene name their pieces, not their paths.</summary>
        public static GameObject LoadByName(string name)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, string>();
                foreach (var guid in RoadDemo.DemoAssetLoad.Find("t:Prefab", new[] { Root.TrimEnd('/') }))
                {
                    var path = RoadDemo.DemoAssetLoad.GUIDToAssetPath(guid);
                    var stem = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!_byName.ContainsKey(stem)) _byName[stem] = path;
                }
            }
            if (_byName.TryGetValue(name, out var p)) return RoadDemo.DemoAssetLoad.Load<GameObject>(p);
            Debug.LogWarning("[SuburbDemo] missing prefab (by name) " + name);
            return null;
        }

        public static GameObject Load(string path)
        {
            var go = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
            if (go == null) Debug.LogWarning("[SuburbDemo] missing prefab " + path);
            return go;
        }

        public static GameObject TryLoad(string path)
        {
            return RoadDemo.DemoAssetLoad.Load<GameObject>(path);
        }

        public static List<GameObject> LoadAll(IEnumerable<string> paths)
        {
            var list = new List<GameObject>();
            foreach (var p in paths)
            {
                var go = Load(p);
                if (go != null) list.Add(go);
            }
            return list;
        }

        public static Material LoadMaterial(string path)
        {
            var m = RoadDemo.DemoAssetLoad.Load<Material>(path);
            if (m == null) Debug.LogWarning("[SuburbDemo] missing material " + path);
            return m;
        }

        /// <summary>Swaps every PolygonTown atlas slot on the instance for the given
        /// palette material; glass and the like are left alone.</summary>
        public static void ApplyPalette(GameObject go, Material palette)
        {
            if (go == null || palette == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.StartsWith("PolygonTown_0")) { mats[i] = palette; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        public static Material Flat(string name, Color colour, float smoothness = 0.05f)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        // ------------------------------------------------------------ measuring

        static readonly Dictionary<GameObject, Bounds> BoundsCache = new Dictionary<GameObject, Bounds>();

        /// <summary>Renderer bounds of a prefab in its own frame, measured once off the asset.</summary>
        public static Bounds PrefabBounds(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.zero);
            if (BoundsCache.TryGetValue(prefab, out var cached)) return cached;
            var b = new Bounds();
            bool started = false;
            var toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var local = mesh.bounds;
                var m = toRoot * r.transform.localToWorldMatrix;
                var c = local.center;
                var e = local.extents;
                for (int k = 0; k < 8; k++)
                {
                    var corner = c + new Vector3((k & 1) == 0 ? e.x : -e.x, (k & 2) == 0 ? e.y : -e.y, (k & 4) == 0 ? e.z : -e.z);
                    var p = m.MultiplyPoint3x4(corner);
                    if (!started) { b = new Bounds(p, Vector3.zero); started = true; }
                    else b.Encapsulate(p);
                }
            }
            BoundsCache[prefab] = b;
            return b;
        }

        /// <summary>The XZ footprint (world) a prefab would cover stood at pos with yaw.</summary>
        public static Rect Footprint(GameObject prefab, Vector3 pos, float yaw, float pad = 0f)
        {
            var b = PrefabBounds(prefab);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float xMin = float.MaxValue, xMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                var c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, 0f, (k & 2) == 0 ? b.min.z : b.max.z);
                var w = pos + rot * c;
                xMin = Mathf.Min(xMin, w.x); xMax = Mathf.Max(xMax, w.x);
                zMin = Mathf.Min(zMin, w.z); zMax = Mathf.Max(zMax, w.z);
            }
            return Rect.MinMaxRect(xMin - pad, zMin - pad, xMax + pad, zMax + pad);
        }

        // ------------------------------------------------------------ placing

        /// <summary>The lie of the land under everything placed through here: height and
        /// unit normal at a point of the plan, or null for flat ground. Every y a caller
        /// passes is height above the ground, and is lifted by this.</summary>
        public static System.Func<float, float, float> Ground;
        public static System.Func<float, float, Vector3> GroundNormal;

        public static float GroundAt(float x, float z) => Ground?.Invoke(x, z) ?? 0f;

        public static GameObject Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent, string name = null)
            => Prop(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent, name);

        /// <summary>A prop at a full rotation - the demo's solar panels lie on the roof pitch.
        /// Its y is lifted onto the ground under it, or onto <paramref name="groundY"/> when
        /// the caller knows better (a building's roof gear stands at the building's ground).</summary>
        public static GameObject Prop(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent, string name = null, float? groundY = null)
        {
            if (prefab == null) return null;
            pos.y += groundY ?? GroundAt(pos.x, pos.z);
            var go = Object.Instantiate(prefab, pos, rot, parent);
            go.name = name ?? prefab.name;
            return go;
        }

        /// <summary>A 5 m tile set into the cell with min corner (mx, mz) - the pack's
        /// corner-pivot convention (see the header) - on the ground and tilted to its
        /// slope (about the tile's own centre, so neighbours still meet). A bigger
        /// square piece set with the same corner-pivot (the 10 x 10 cap wrap) gives its size.</summary>
        public static GameObject Tile(GameObject prefab, float mx, float mz, int yaw, Transform parent, float y = 0f, float size = Cell)
        {
            if (prefab == null) return null;
            Vector3 pivot;
            int y4 = ((yaw % 360) + 360) % 360;
            switch (y4)
            {
                case 0: pivot = new Vector3(mx + Cell, y, mz + Cell); break;
                case 90: pivot = new Vector3(mx + Cell, y, mz); break;
                case 180: pivot = new Vector3(mx, y, mz); break;
                default: pivot = new Vector3(mx, y, mz + Cell); break;
            }
            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            if (Ground == null) return Object.Instantiate(prefab, pivot, yawRot, parent);
            // the piece covers local [-size, 0] x [-size, 0]: its centre is half a size in from the pivot
            var half = new Vector3(size * 0.5f, 0f, size * 0.5f);
            var centre = pivot - yawRot * half;
            centre.y += Ground(centre.x, centre.z);
            var n = GroundNormal != null ? GroundNormal(centre.x, centre.z) : Vector3.up;
            var rot = Quaternion.FromToRotation(Vector3.up, n) * yawRot;
            return Object.Instantiate(prefab, centre + rot * half, rot, parent);
        }

        /// <summary>The yaw that turns a tile's kerb (its local +Z) towards the given
        /// world direction: +Z -> 0, +X -> 90, -Z -> 180, -X -> 270.</summary>
        public static int YawFacing(Vector3 dir)
        {
            if (dir.z > 0.5f) return 0;
            if (dir.x > 0.5f) return 90;
            if (dir.z < -0.5f) return 180;
            return 270;
        }

        /// <summary>Local axis of a prefab turned into the world by a yaw.</summary>
        public static Vector3 Turn(Side local, float yaw)
        {
            var v = local switch
            {
                Side.PlusX => Vector3.right,
                Side.MinusX => Vector3.left,
                Side.PlusZ => Vector3.forward,
                _ => Vector3.back,
            };
            return Quaternion.Euler(0f, yaw, 0f) * v;
        }

        /// <summary>The quarter-turn yaw that makes a prefab's local side look along a
        /// world direction.</summary>
        public static int YawToFace(Side localFront, Vector3 worldDir)
        {
            for (int y = 0; y < 360; y += 90)
                if (Vector3.Dot(Turn(localFront, y), worldDir) > 0.9f) return y;
            return 0;
        }

        public static void StripForStatic(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
        }

        public static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }
    }
}
