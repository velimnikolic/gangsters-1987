using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The harbor demo's cupboard: where every Synty piece it uses lives, and the few
    // helpers every builder file needs - loading through the AssetDatabase (editor
    // only, the road demo's way), measuring a prefab once, dropping a prop, stripping
    // the pack's own scripts and colliders off a body the demo drives itself.
    public static class HarborKit
    {
        // ------------------------------------------------------------ folders
        public const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        public const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        public const string CityChars = "Assets/Synty/PolygonCity/Prefabs/Characters/";
        public const string PalmBld = "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/";
        public const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        public const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        public const string PalmChars = "Assets/Synty/PolygonPalmCity/Prefabs/Characters/";
        public const string PalmFx = "Assets/Synty/PolygonPalmCity/Prefabs/FX/";
        public const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        public const string GangProps = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        public const string GangVeh = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
        public const string GangChars = "Assets/Synty/PolygonGangWarfare/Prefabs/Character/";
        public const string GenChars = "Assets/Synty/PolygonGeneric/Prefabs/Characters/";
        public const string GenProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        public const string GenEnv = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
        public const string GenBase = "Assets/Synty/PolygonGeneric/Prefabs/Base/";
        public const string MilWarehouse = "Assets/Synty/PolygonMapsMilitaryWarehouse/Prefabs/";
        public const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        public const string KitBuildings = "Assets/CityKit/Buildings/";
        public const string KitTiles = "Assets/CityKit/Tiles/";
        public const string Ships = "Assets/CityKit/Ships/";

        // ------------------------------------------------------------ pieces
        public const string WaterPlane = "Assets/Synty/PNB_Core/Prefabs/SM_Env_Water_Plane_01.prefab";
        public const string OceanTile = CityEnv + "SM_Env_Ocean_Tile_01.prefab";
        public const string PalmGround = "Assets/Synty/PolygonPalmCity/Materials/Env/Grass_Triplanar_01.mat";
        /// <summary>The palm city's tiling concrete and tarmac - what the apron and the
        /// yard roads are laid in, as one plane each rather than a grid of slabs.</summary>
        public const string ConcreteMat = "Assets/Synty/PolygonPalmCity/Materials/Buildings/Sidewalk_01.mat";
        public const string AsphaltMat = "Assets/Synty/PolygonPalmCity/Materials/Buildings/Road_Grey_01.mat";
        public const string QuayStraight = CityEnv + "SM_Env_WaterEdge_Straight_03.prefab";
        public const string QuayWorn = CityEnv + "SM_Env_WaterEdge_Straight_02.prefab";
        public const string QuayPipe = CityEnv + "SM_Env_WaterEdge_Pipe_01.prefab";
        public const string ShoreRock = CityEnv + "SM_Env_WaterEdge_Rock_01.prefab";
        public const string ConcreteTile = KitTiles + "tile-plain_concrete.prefab";
        public const string AsphaltTile = KitTiles + "tile-plain_asphalt.prefab";
        public const string DockPlatform = PalmBld + "SM_Bld_Dock_Platform_01.prefab";
        public const string DockPlatformAlt = PalmBld + "SM_Bld_Dock_Platform_02.prefab";
        public const string DockPillar = PalmBld + "SM_Bld_Dock_Pillar_01.prefab";
        public const string DockRailing = PalmBld + "SM_Bld_Dock_Railing_01.prefab";
        public const string DockPole = PalmBld + "SM_Bld_Dock_Pole_01.prefab";
        public const string PierLamp = PalmProps + "SM_Prop_Pier_Lamp_01.prefab";
        public const string Bollard1 = PalmProps + "SM_Prop_Bollard_01.prefab";
        public const string Bollard3 = PalmProps + "SM_Prop_Bollard_03.prefab";
        public const string Buoy = PalmProps + "SM_Prop_Buoy_01.prefab";
        public const string BuoyBall = PalmProps + "SM_Prop_Buoy_02.prefab";
        public const string RescueBuoy = PalmProps + "SM_Prop_Rescue_Buoy_01.prefab";
        public const string Cone = PalmProps + "SM_Prop_Cone_01.prefab";
        public const string BurnBarrel = PalmProps + "SM_Prop_Barrel_Burn_01.prefab";
        public const string Pallet = GangProps + "SM_Prop_Pallet_01.prefab";
        public const string BarrelMetal = GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        public const string BarrelPlastic = GangProps + "SM_Prop_Barrel_Plastic_01.prefab";
        public const string Forklift = GangVeh + "SM_Veh_Forklift_01.prefab";
        public const string Truck = GangVeh + "SM_Veh_Truck_01.prefab";
        public const string LoadingDock = GangBld + "SM_Bld_LoadingDock_02.prefab";
        public const string FencePanel = GangBld + "SM_Bld_Fence_01.prefab";
        public const string FencePanelAlt = GangBld + "SM_Bld_Fence_02.prefab";
        // NOT a fence: the pack's "wire" piece is the barbed crown alone, 0.77 m of it
        // sitting three metres up, meant to ride on top of a panel (see FenceRun).
        public const string FenceWire = GangBld + "SM_Bld_Fence_Wire_01.prefab";
        public const string FenceGate = GangBld + "SM_Bld_Fence_Gate_01.prefab";
        public const string WalkwaySingle = GangBld + "SM_Bld_Walkway_Single_01.prefab";
        // The sheds: the Military Warehouse pack's own buildings, baked into the kit
        // by SyntyWarehouseKit with their loading fronts turned onto +Z.
        public const string WarehouseLarge = KitBuildings + "building-warehouse-large.prefab";
        public const string WarehouseSmall = KitBuildings + "building-warehouse-small.prefab";
        public const string DepotGarage = KitBuildings + "building-depot-garage.prefab";
        public const string YardShed = KitBuildings + "building-yard-shed.prefab";
        // The yard furniture. The Military Warehouse pack's own props all wear the
        // POLYGON Military atlas, which this project does not own - the material on
        // disk under that GUID is a flat grey stand-in (Materials/PolygonMilitary_01_A)
        // so the pack's pieces render at all. The razor coil reads fine in grey; a
        // sign, a bin and a concrete block do not, so those come from packs we have.
        public const string BarbedWire = MilWarehouse + "SM_Prop_Barbed_Wire_01.prefab";
        public const string BarbedWireLong = MilWarehouse + "SM_Prop_Barbed_Wire_02.prefab";
        public const string DangerSign = GangProps + "SM_Prop_Sign_Danger_01.prefab";
        public const string ConcreteBlock = CityProps + "SM_Prop_Barrier_01.prefab";
        public const string YardBin = PalmProps + "SM_Prop_Trash_Bin_01.prefab";
        public const string Footway = GenEnv + "SM_Gen_Env_Sidewalk_01.prefab";
        public const string FootwayAlt = MilWarehouse + "SM_Env_Sidewalk_Centre_01.prefab";
        // What the back lots between the sheds hold: parked lorries, drum and spool
        // stores, pipe stacks - the working clutter of a port that is not all boxes.
        public const string WireSpool = GangProps + "SM_Prop_Wirespool_01.prefab";
        public const string PipeStack = GangProps + "SM_Prop_PipeStack_01.prefab";
        public const string TownTruck = "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_01.prefab";
        public const string TownDelivery = "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_Delivery_01.prefab";
        /// <summary>The lorries on the road: the one Synty truck body in its three guises -
        /// the gang pack's box lorry, the town's box lorry in its own colours, the town's
        /// flatbed (which carries its pallets in the open).</summary>
        public static readonly string[] Lorries = { Truck, TownDelivery, TownTruck };

        // ------------------------------------------------------------ ground markings
        public const string RoadWarning = GenEnv + "SM_Gen_Env_Road_Warning_01.prefab";
        public const string RoadCrossing = GenEnv + "SM_Gen_Env_Road_Crossing_01.prefab";
        public const string RoadParking = GenEnv + "SM_Gen_Env_Road_Parking_01.prefab";
        public const string RoadArrow = CityEnv + "SM_Env_Road_Arrow_01.prefab";
        /// <summary>The white centre dashes and the solid yellow edge line - what tells
        /// a strip of asphalt from a stain on the concrete.</summary>
        public const string RoadLines = CityEnv + "SM_Env_Road_Lines_01.prefab";
        public const string RoadLinesAlt = CityEnv + "SM_Env_Road_Lines_02.prefab";
        public const string RoadEdgeLine = CityEnv + "SM_Env_Road_YellowLines_01.prefab";
        public static readonly string[] TyreMarks =
        {
            GenEnv + "SM_Gen_Env_Tyremarks_Long_Corner_01.prefab",
            GenEnv + "SM_Gen_Env_Tyremarks_Long_Corner_02.prefab",
            GenEnv + "SM_Gen_Env_Tyremarks_Short_Corner_01.prefab",
            GenEnv + "SM_Gen_Env_Tyremarks_Short_Corner_02.prefab",
        };

        // ------------------------------------------------------------ freight
        /// <summary>What comes off a pallet and stands about a loading bay.</summary>
        public static readonly string[] Freight =
        {
            GangProps + "SM_Prop_CardboardBox_Stack_01.prefab",
            GangProps + "SM_Prop_CardboardBox_Stack_02.prefab",
            GenProps + "SM_Gen_Prop_Crate_Preset_01.prefab",
            GenProps + "SM_Gen_Prop_Sack_Stack_01.prefab",
            GenProps + "SM_Gen_Prop_Sack_Stack_02.prefab",
        };
        public const string Crate1 = GenProps + "SM_Gen_Prop_Crate_01.prefab";
        public const string Crate2 = GenProps + "SM_Gen_Prop_Crate_02.prefab";
        public const string Rope1 = GenProps + "SM_Gen_Prop_Rope_01.prefab";
        public const string Rope2 = GenProps + "SM_Gen_Prop_Rope_02.prefab";
        public const string Powerpole = GangProps + "SM_Prop_Light_Pole_01.prefab";
        public const string Dumpster = GangProps + "SM_Prop_Dumpster_01.prefab";
        public const string FxRipple = Fx + "FX_WaterRipple_01.prefab";
        public const string FxSmoke = Fx + "FX_Smoke_Black_Small_01.prefab";
        public const string FxBirds = PalmFx + "FX_Birds_01.prefab";

        /// <summary>No longer laid anywhere - the finger pier and the pleasure-boat passers
        /// went when the port became a box port only. Kept for whoever wants a marina.</summary>
        public static readonly string[] SmallBoats =
        {
            PalmVeh + "SM_Veh_Power_Boat_01.prefab",
            PalmVeh + "SM_Veh_RIB_Boat_01.prefab",
            PalmVeh + "SM_Veh_Party_Boat_01.prefab",
            PalmVeh + "SM_Veh_Sailboat_01.prefab",
        };

        /// <summary>The bodies that work the quay: dock hands out of four packs and
        /// the palm city's sea captain. Whichever of these load are used.
        ///
        /// None of them is on GangLooks' cast tables - a docker is a man with a job,
        /// and a body the mob may be dealt has to stay off him the same way it stays
        /// off the city crowd. The gang pack's jumpsuits (hazmat, cook) are not on
        /// those tables, so the quay keeps them.</summary>
        public static readonly string[] Workers =
        {
            GenChars + "SM_Gen_Chr_Jumpsuit_Male_01.prefab",
            GenChars + "SM_Gen_Chr_Street_Male_02.prefab",
            GenChars + "SM_Gen_Chr_Street_Male_03.prefab",
            GangChars + "SM_Chr_Hazmat_Suit_Male_01.prefab",
            GangChars + "SM_Chr_Cook_Male_01.prefab",
            PalmChars + "SM_Chr_City_Male_01.prefab",
            PalmChars + "SM_Chr_City_Male_02.prefab",
            CityChars + "Character_Male_Hoodie.prefab",
            CityChars + "Character_Male_Jacket.prefab",
        };
        public const string SeaCaptain = PalmChars + "SM_Chr_SeaCaptain_Male_01.prefab";

        /// <summary>What carries the shed line: buildings big enough to stand on it and
        /// be read as a transit shed from the water. The little yard shed is not one of
        /// them - a six-metre hut set among forty-metre warehouses on the same line is
        /// what makes a row look laid out by a machine.</summary>
        public static readonly string[] LineSheds = { WarehouseSmall, DepotGarage };
        /// <summary>The main building: the demo's big warehouse, stood once, square on the line at
        /// the middle of the port.</summary>
        public const string MainShed = WarehouseLarge;

        /// <summary>What fills the gaps: outbuildings, gatehouses, stores - set off the
        /// line, against a neighbour's flank or beside a gate, never in the row.</summary>
        public static readonly string[] SmallSheds = { YardShed };

        /// <summary>The kit-bashed hulls and boxes (Editor/HarborShipKitBash.cs).</summary>
        public static readonly string[] CargoShips = { Ships + "ship-cargo-A.prefab", Ships + "ship-cargo-B.prefab" };
        public const string Coaster = Ships + "ship-coaster.prefab";
        public static readonly string[] Containers =
        {
            Ships + "container-20-red.prefab", Ships + "container-20-blue.prefab", Ships + "container-20-green.prefab",
            Ships + "container-20-rust.prefab", Ships + "container-20-white.prefab",
        };

        // ------------------------------------------------------------ loading

        public static GameObject Load(string path)
        {
#if UNITY_EDITOR
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning("[HarborDemo] missing prefab " + path);
            return go;
#else
            return null;
#endif
        }

        /// <summary>Loads without complaint - for the optional pieces.</summary>
        public static GameObject TryLoad(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        public static List<GameObject> LoadAll(IEnumerable<string> paths, bool quiet = false)
        {
            var list = new List<GameObject>();
            foreach (var p in paths)
            {
                var go = quiet ? TryLoad(p) : Load(p);
                if (go != null) list.Add(go);
            }
            return list;
        }

        /// <summary>A fresh instance of a project material, so the demo may retune it
        /// without dirtying the shared asset.</summary>
        public static Material LoadMaterial(string path)
        {
#if UNITY_EDITOR
            var src = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            return src != null ? new Material(src) : null;
#else
            return null;
#endif
        }

        public static Material Flat(string name, Color colour, float smoothness = 0.1f)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        // ------------------------------------------------------------ measuring

        static readonly Dictionary<GameObject, Bounds> BoundsCache = new Dictionary<GameObject, Bounds>();

        /// <summary>Renderer bounds of a prefab in its own frame (pivot at the origin,
        /// no rotation), measured once off the asset itself - no instance needed.</summary>
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
                var c = local.center; var e = local.extents;
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

        public static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ placing

        public static GameObject Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            go.name = name ?? prefab.name;
            return go;
        }

        /// <summary>A prop set down so its own underside rests on the given point,
        /// whatever its pivot - the yard furniture is pivoted at its middle as often
        /// as at its feet, and a turn about Y never changes how deep it hangs.</summary>
        public static GameObject Sit(GameObject prefab, Vector3 at, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var b = PrefabBounds(prefab);
            return Prop(prefab, new Vector3(at.x, at.y - b.min.y, at.z), yaw, parent, name);
        }

        // ------------------------------------------------------------ runs
        //
        // Wall-like pieces (walls, railings, fences, quay edges) have their pivot at
        // one end and run from it along one horizontal axis - or, for a 45-degree
        // piece, diagonally. RunOf reads that vector off the prefab's own bounds:
        // the far corner from the pivot along the longest extent. PlaceRun then
        // turns the piece so its run lies from A to B, and LayRun fills a whole
        // segment with equal modules, each stretched a little so the last one lands
        // exactly on B - no gap, no overhang.

        public static Vector3 RunOf(GameObject prefab) => RunOf(prefab, out _);

        /// <summary>The run vector, and whether the piece's pivot sits at its middle
        /// (a door in a frame) rather than at one end - such a piece is placed by its
        /// centre and its run merely gives the length.</summary>
        public static Vector3 RunOf(GameObject prefab, out bool centred)
        {
            var b = PrefabBounds(prefab);
            centred = false;
            if (b.size.x >= b.size.z * 1.5f)
            {
                centred = Mathf.Abs(b.min.x + b.max.x) < 0.2f * b.size.x;
                if (centred) return new Vector3(b.size.x, 0f, 0f);
                return new Vector3(Mathf.Abs(b.min.x) > Mathf.Abs(b.max.x) ? b.min.x : b.max.x, 0f, 0f);
            }
            if (b.size.z >= b.size.x * 1.5f)
            {
                centred = Mathf.Abs(b.min.z + b.max.z) < 0.2f * b.size.z;
                if (centred) return new Vector3(0f, 0f, b.size.z);
                return new Vector3(0f, 0f, Mathf.Abs(b.min.z) > Mathf.Abs(b.max.z) ? b.min.z : b.max.z);
            }
            float fx = Mathf.Abs(b.min.x) > Mathf.Abs(b.max.x) ? b.min.x : b.max.x;
            float fz = Mathf.Abs(b.min.z) > Mathf.Abs(b.max.z) ? b.min.z : b.max.z;
            return new Vector3(fx, 0f, fz);
        }

        /// <summary>One piece with its pivot at <paramref name="from"/> and its run
        /// turned toward <paramref name="to"/>; stretched along the run to reach it
        /// when <paramref name="fit"/> is set and the run is axis-aligned. A piece
        /// with a centred pivot is set at the segment's middle instead.</summary>
        public static GameObject PlaceRun(GameObject prefab, Vector3 from, Vector3 to, Transform parent, bool fit = false, string name = null)
        {
            if (prefab == null) return null;
            var run = RunOf(prefab, out bool centred);
            var want = to - from;
            want.y = 0f;
            if (run.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-6f) return null;
            float yaw = Vector3.SignedAngle(run, want, Vector3.up);
            var at = centred ? (from + to) * 0.5f : from;
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), parent);
            go.name = name ?? prefab.name;
            if (fit)
            {
                float k = want.magnitude / run.magnitude;
                var s = go.transform.localScale;
                if (Mathf.Abs(run.z) < 1e-4f) s.x *= k;
                else if (Mathf.Abs(run.x) < 1e-4f) s.z *= k;
                go.transform.localScale = s;
            }
            return go;
        }

        /// <summary>The segment A-B filled with modules of the piece, evenly stretched.
        /// Returns the number laid.</summary>
        public static int LayRun(GameObject prefab, Vector3 from, Vector3 to, Transform parent, string name = null,
                                 System.Func<int, GameObject> variant = null)
        {
            if (prefab == null) return 0;
            float module = RunOf(prefab).magnitude;
            var d = to - from;
            float len = d.magnitude;
            if (module < 0.01f || len < 0.05f) return 0;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / module));
            if (n * module < len - 0.01f && len / n > module * 1.15f) n++;
            var step = d / n;
            for (int i = 0; i < n; i++)
            {
                var piece = variant != null ? (variant(i) ?? prefab) : prefab;
                PlaceRun(piece, from + step * i, from + step * (i + 1), parent, fit: true, name);
            }
            return n;
        }

        /// <summary>The body as geometry only: the packs' boats carry a bob script, the
        /// vehicles colliders and rigidbodies, the people crowd scripts and controllers.
        /// The demo moves all of them itself.</summary>
        public static void StripBehaviours(GameObject go, bool keepAnimator = true)
        {
            void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Kill(mb);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Kill(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Kill(col);
            foreach (var nav in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Kill(nav);
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
            {
                if (keepAnimator) animator.runtimeAnimatorController = null;
                else Kill(animator);
            }
        }

        public static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }

        /// <summary>The first child (depth-first) whose name contains the token.</summary>
        public static Transform FindDeep(Transform t, string token)
        {
            foreach (Transform c in t)
            {
                if (c.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0) return c;
                var deep = FindDeep(c, token);
                if (deep != null) return deep;
            }
            return null;
        }

        public static float Range(System.Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        public static T Pick<T>(System.Random rng, IList<T> list) => list == null || list.Count == 0 ? default : list[rng.Next(list.Count)];
    }
}
