using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Industrial blocks, COMPOSED the way the core's sixteen were found.
    ///
    /// The core blocks came out of the Synty city demo by harvest: their artists laid them
    /// and the tray cut them out (<see cref="CoreBlockTray"/>). That pack has no industry
    /// in it - no works, no depot, no stockyard - so those blocks have to be built. All the
    /// care here goes on making a built block indistinguishable from a harvested one once
    /// it is on disk, because both land in the same folder and are read by the same
    /// <see cref="RoadDemo.CoreLayout"/>:
    ///
    ///   - the block carries its OWN kerb, one 5 m tile of it the whole way round, and the
    ///     road outside is somebody else's business. That is the difference between this
    ///     pipeline and the grid city's, where the street lays 6.5 m of pavement itself and
    ///     the block is only the interior.
    ///   - every piece is a prefab INSTANCE. The bake replays each one's overrides onto a
    ///     fresh instance of its source, which is the only way a block comes out of it in
    ///     the colours it was composed in.
    ///   - the floor is calm: one tile to a surface, laid straight, no chequerboard and no
    ///     mixing of packs. Two surfaces at most, and the second only where the yard is
    ///     worked.
    ///   - nothing stands on the kerb ring. It is pavement, and people walk on it.
    ///
    /// The four candidates are the point of the thing. A composed block is a guess, and the
    /// cheapest way to be right is to stand four guesses in a row, look at them, and keep
    /// the ones that read as a place. <see cref="Generate"/> stands them up;
    /// <see cref="BakeChosen"/> files the ones asked for and throws the rest away.
    /// </summary>
    public static class IndustrialBlockForge
    {
        // ---------------------------------------------------------------- the furniture

        /// <summary>The scene root the candidates stand under. <see cref="CoreBlockTray"/>
        /// steps over it when it sweeps, so a candidate can never be swallowed by a tray
        /// rectangle it happens to be standing near.</summary>
        internal const string CandidatesRoot = "INDUSTRIAL CANDIDATES";

        /// <summary>The workbench, and deliberately a thin scene. The harvest scene carries
        /// the whole Synty demo, takes seconds to write, and a bake that saves at the end
        /// of itself runs out of the pipeline's patience there.</summary>
        internal const string LabPath = "Assets/Scenes/IndustrialLab.unity";

        const string CandidatePrefix = "candidate-";
        const string LabelName = "label";

        /// <summary>The pack's module, and the city's. Every measure here is a multiple of
        /// it, which is what lets a block sit on the same grid the roads are read off.</summary>
        const float Cell = 5f;

        /// <summary>Walking room between two candidates, so neither reads as part of the
        /// other from above.</summary>
        const float Walk = 30f;

        /// <summary>
        /// How far a building stands back from the kerb ring.
        ///
        /// Far enough for the wall to pass in FRONT of it. A works is walled all the way
        /// round and the buildings are behind the wall; sat hard against the ring they
        /// become the wall themselves, and every place two of them meet at an angle the
        /// wall has a hole in it instead of a corner.
        /// </summary>
        const float Setback = 1.6f;

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string GangProps = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        const string GangVeh = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string GenProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        const string KitBld = "Assets/CityKit/Buildings/";
        const string KitShips = "Assets/CityKit/Ships/";

        // the ground: the kerb tile is the block's edge, the plain square is its floor
        const string Kerb = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string KerbCorner = CityEnv + "SM_Env_Sidewalk_Corner_01.prefab";
        const string Plate = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Asphalt = CityEnv + "SM_Env_Road_Bare_01.prefab";
        const string PaintedBays = CityEnv + "SM_Env_Road_ParkingLines_01.prefab";

        // the buildings, every one baked with its front on +Z and its floor on y = 0
        const string Factory = KitBld + "building-factory.prefab";
        const string FactoryOld = KitBld + "building-factory-old.prefab";
        const string FactoryHall = KitBld + "building-factory-hall.prefab";
        const string Workshop = KitBld + "building-workshop.prefab";
        const string ShedLarge = KitBld + "building-warehouse-large.prefab";
        const string ShedSmall = KitBld + "building-warehouse-small.prefab";
        const string DepotGarage = KitBld + "building-depot-garage.prefab";
        const string YardShed = KitBld + "building-yard-shed.prefab";

        // the perimeter. A works has a brick wall on the street like every other
        // building in this city; razor wire is what a stockyard has, and only a stockyard.
        const string BrickPanel = GangBld + "SM_Bld_Fence_Brick_01.prefab";
        const string BrickPillar = GangBld + "SM_Bld_Fence_Brick_Pillar_01.prefab";

        // the plant: what says works rather than warehouse from the height a city is
        // looked at
        /// <summary>The stack's own paint. Flat colour and no texture, which is not a
        /// compromise here: every Synty piece in the block is a flat region of an atlas, so
        /// a flat brick red sits among them exactly right - and a round shaft has no UVs
        /// that could find the brick on that atlas anyway.</summary>
        const string StackMaterial = "Assets/Materials/IndustrialStack.mat";
        const string WaterTower =
            "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Prop_Water_Tower_01.prefab";
        const string Smoke =
            "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_Black_Small_01.prefab";
        const string PipeRiserTall = CityProps + "SM_Prop_Pipe_Preset_01.prefab";
        const string PipeRiserWide = CityProps + "SM_Prop_Pipe_Preset_02.prefab";

        // the yard
        const string FencePanel = GangBld + "SM_Bld_Fence_01.prefab";
        const string FenceCrown = GangBld + "SM_Bld_Fence_Wire_01.prefab";
        const string FenceGate = GangBld + "SM_Bld_Fence_Gate_01.prefab";
        const string FencePole = GangBld + "SM_Bld_Fence_Pole_01.prefab";
        const string LoadingDock = GangBld + "SM_Bld_LoadingDock_02.prefab";
        const string Pallet = GangProps + "SM_Prop_Pallet_01.prefab";
        const string BarrelMetal = GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        const string BarrelPlastic = GangProps + "SM_Prop_Barrel_Plastic_01.prefab";
        const string WireSpool = GangProps + "SM_Prop_Wirespool_01.prefab";
        const string PipeStack = GangProps + "SM_Prop_PipeStack_01.prefab";
        const string Dumpster = GangProps + "SM_Prop_Dumpster_01.prefab";
        const string YardLamp = GangProps + "SM_Prop_Light_Pole_01.prefab";
        const string DangerSign = GangProps + "SM_Prop_Sign_Danger_01.prefab";
        const string Forklift = GangVeh + "SM_Veh_Forklift_01.prefab";
        const string BoxLorry = GangVeh + "SM_Veh_Truck_01.prefab";
        const string Crate = GenProps + "SM_Gen_Prop_Crate_01.prefab";
        const string BurnBarrel = PalmProps + "SM_Prop_Barrel_Burn_01.prefab";
        const string Cone = PalmProps + "SM_Prop_Cone_01.prefab";
        const string Barrier = CityProps + "SM_Prop_Barrier_01.prefab";

        static readonly string[] Containers =
        {
            KitShips + "container-20-red.prefab",
            KitShips + "container-20-blue.prefab",
            KitShips + "container-20-green.prefab",
            KitShips + "container-20-rust.prefab",
            KitShips + "container-20-white.prefab",
        };

        /// <summary>The first four are what "all" deals - two works, one stockyard and one
        /// service strip. The depot is a warehouse and there is a stockyard in the four
        /// already, so it is kept for --recipe depot rather than dealt by default.</summary>
        enum Recipe { Works, Plant, Yard, Strip, Depot }

        /// <summary>What the perimeter is made of.</summary>
        enum Wall { Brick, Wire }

        enum Surface { Plate, Asphalt }

        // ------------------------------------------------------------------- measuring

        static readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();
        static readonly List<string> Absent = new List<string>();

        /// <summary>
        /// A prefab's own box, measured once and remembered.
        ///
        /// Measured through an INSTANCE, never off the asset: a prefab asset's renderers
        /// report bounds in their own local space, and the root scaling every Synty pack
        /// relies on is only applied once the thing is standing in a scene.
        /// </summary>
        static Bounds Box(string path)
        {
            if (Measured.TryGetValue(path, out var known)) return known;

            var box = new Bounds(Vector3.zero, Vector3.one);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                if (!Absent.Contains(path)) Absent.Add(path);
                Measured[path] = box;
                return box;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (WorldBox(go, out var world)) box = world;
            }
            finally { Object.DestroyImmediate(go); }

            Measured[path] = box;
            return box;
        }

        static bool WorldBox(GameObject go, out Bounds box)
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

        // ------------------------------------------------------------------- standing up

        static GameObject Raise(string path, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                if (!Absent.Contains(path)) Absent.Add(path);
                return null;
            }
            return (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
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
                // the local scale is applied before the turn, so a quarter turn swaps which
                // world measure each local axis answers for. It MULTIPLIES what the prefab
                // already carries - several packs scale their own root, and overwriting
                // that would resize the piece rather than stretch it.
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
        /// all called no stretch at all. A tile laid at exactly its own size measures a
        /// hair off it, and a scale of 0.9999996 written into every prefab is noise the
        /// next reader has to rule out.</summary>
        static Vector3 Whole(Vector3 factor) => new Vector3(
            Mathf.Abs(factor.x - 1f) < 0.005f ? 1f : factor.x,
            Mathf.Abs(factor.y - 1f) < 0.005f ? 1f : factor.y,
            Mathf.Abs(factor.z - 1f) < 0.005f ? 1f : factor.z);

        /// <summary>Stands a building on its middle, keeping the floor it was baked with -
        /// which for every one of these kit bakes is y = 0.</summary>
        static GameObject Stand(string path, Transform parent, float cx, float cz, float yaw,
                                float y) =>
            Settle(path, parent, cx, cz, yaw, y, false);

        /// <summary>Sits a prop on its own underside. Synty pivots furniture at its middle
        /// as often as at its feet, so a barrel dropped at the deck height by its pivot is
        /// as likely to be buried to the waist as standing on the ground.</summary>
        static GameObject Sit(string path, Transform parent, float cx, float cz, float yaw,
                              float y) =>
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

        // ------------------------------------------------------------------- the block

        /// <summary>
        /// One block under composition: which cells it holds, what is on the floor of each,
        /// and everything standing on them.
        ///
        /// The block is a MASK of 5 m cells rather than a rectangle, which is what lets a
        /// bite be taken out of one. The demo has no empty ground anywhere, so a corner a
        /// block does not want is a corner the street takes back as parking - and the kerb
        /// has to run round the bite rather than across it.
        /// </summary>
        sealed class Block
        {
            public readonly Transform Root;
            public readonly int W, D, NX, NZ;
            public readonly System.Random Rng;

            /// <summary>The rectangle a building may stand in: inside the kerb ring and
            /// behind the wall.</summary>
            public float In => Cell + Setback;
            public float Out => W - Cell - Setback;
            public float Far => D - Cell - Setback;

            readonly bool[] _held;      // the cell belongs to the block
            readonly bool[] _laid;      // something has already floored the cell
            readonly bool[] _drive;     // the pavement gives way to road here: the way in
            readonly bool[] _corridor;  // the drive itself, which nothing else may surface
            readonly Surface[] _floor;
            readonly List<Rect> _taken = new List<Rect>();   // anything standing, for the props
            readonly List<Rect> _footprints = new List<Rect>();   // buildings, for their apron

            Vector2 _way;

            /// <summary>
            /// The way in, as a span in metres along the south kerb. Empty for a block with
            /// no wall and no gate.
            ///
            /// Setting it lays the DRIVE at the same time: road surface from the kerb
            /// straight in until something is standing in the way, and that ground booked so
            /// nothing is set down on it afterwards. A drive that runs two cells and turns
            /// back into pavement, then into road again where the yard happens to be
            /// surfaced, is not a drive - it is a road, a bit of pavement, and another road.
            /// </summary>
            public Vector2 Way
            {
                get => _way;
                set { _way = value; Corridor(); }
            }

            /// <summary>The columns the gate opens onto, floored as road from the kerb in.
            /// Stops at the first thing standing - a building, a painted bay - and at the
            /// far kerb, which keeps its own tile.</summary>
            void Corridor()
            {
                if (_way.y <= _way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, _way.y) - Mathf.Max(a, _way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Edge(i, first)) continue;

                    int last = first;
                    for (int j = first + 1; j < NZ; j++)
                    {
                        if (!Held(i, j) || Edge(i, j) || _laid[At(i, j)]) break;
                        _floor[At(i, j)] = Surface.Asphalt;
                        _corridor[At(i, j)] = true;
                        last = j;
                    }
                    if (last > first)
                        _taken.Add(new Rect(a, (first + 1) * Cell, Cell, (last - first) * Cell));
                }
            }

            /// <summary>Brick unless the block is a yard.</summary>
            public Wall Wall = Wall.Brick;

            /// <summary>Metres of perimeter the wall was asked for, and metres it actually
            /// covers. The difference is a hole, and a hole in a yard wall is the one fault
            /// nobody can see in a screenshot until they are looking for it.</summary>
            float _wallWanted, _wallLaid;

            /// <summary>
            /// What the wall misses, measured against the ground it is meant to ring rather
            /// than against what the run-builder happened to ask for. Asking the builder is
            /// no test at all: a side it never worked out is a side it never wanted, and the
            /// hole goes unreported - which is exactly how the notch's inside corner hid.
            /// </summary>
            public float WallGap
            {
                get
                {
                    float should = 0f;
                    for (int j = 0; j < NZ; j++)
                        for (int i = 0; i < NX; i++)
                        {
                            if (!Held(i, j) || Edge(i, j)) continue;
                            if (!Inside(i - 1, j)) should += Cell;
                            if (!Inside(i + 1, j)) should += Cell;
                            if (!Inside(i, j - 1)) should += Cell;
                            if (!Inside(i, j + 1)) should += Cell;
                        }
                    if (Way.y > Way.x) should -= Way.y - Way.x;
                    return Mathf.Max(0f, Mathf.Max(should, _wallWanted) - _wallLaid);
                }
            }

            /// <summary>Ground the wall rings: held, and not the kerb.</summary>
            bool Inside(int i, int j) => Held(i, j) && !Edge(i, j);

            /// <summary>
            /// Wall pieces standing inside a building, which is the fault that keeps coming
            /// back and cannot be seen from above: a panel, a pillar or a gate leaf that
            /// reaches into a wall reads as the building bursting through the perimeter.
            /// Counted rather than eyeballed, and reported with the candidate.
            /// </summary>
            public int WallInBuilding()
            {
                int through = 0;
                foreach (UnityEngine.Transform piece in Root)
                {
                    if (!piece.name.StartsWith("SM_Bld_Fence")) continue;
                    if (!WorldBox(piece.gameObject, out var box)) continue;
                    var lo = box.min - Root.position;
                    var hi = box.max - Root.position;
                    var stood = Rect.MinMaxRect(lo.x + 0.1f, lo.z + 0.1f, hi.x - 0.1f, hi.z - 0.1f);
                    foreach (var foot in _footprints)
                        if (foot.Overlaps(stood)) { through++; break; }
                }
                return through;
            }

            public Block(Transform root, int w, int d, System.Random rng)
            {
                Root = root;
                W = w;
                D = d;
                Rng = rng;
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

            bool Held(int i, int j) =>
                i >= 0 && j >= 0 && i < NX && j < NZ && _held[At(i, j)];

            /// <summary>A cell on the block's edge: the kerb runs through it, and nothing
            /// else may.</summary>
            bool Edge(int i, int j) =>
                Held(i, j) && (!Held(i - 1, j) || !Held(i + 1, j) ||
                               !Held(i, j - 1) || !Held(i, j + 1));

            /// <summary>Takes a bite out of the block. The street will make it a car park;
            /// the block simply stops there and its kerb turns the corner.</summary>
            public void Bite(int i0, int j0, int ni, int nj)
            {
                for (int i = i0; i < i0 + ni; i++)
                    for (int j = j0; j < j0 + nj; j++)
                        if (i >= 0 && j >= 0 && i < NX && j < NZ) _held[At(i, j)] = false;
            }

            /// <summary>
            /// What the ground is made of, decided in one place for the whole block.
            ///
            /// A yard is ASPHALT. Pavement is the ground people walk on, and in a works
            /// there are exactly two strips of it: the band inside the wall, and an apron
            /// round each building. Everything else is where the lorries go.
            ///
            /// It used to be the other way about - pavement by default and a rectangle of
            /// asphalt per recipe - which gave every yard a slab of clean plate in the
            /// middle of its working ground and put the recipe in charge of a decision that
            /// is the same for all of them.
            /// </summary>
            public void Surfaces()
            {
                for (int k = 0; k < _floor.Length; k++) _floor[k] = Surface.Asphalt;

                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || Edge(i, j)) continue;

                        // the walk inside the wall
                        bool walk = Edge(i - 1, j) || Edge(i + 1, j) ||
                                    Edge(i, j - 1) || Edge(i, j + 1);

                        // and the skirt a building stands on. A SKIRT, not a five metre
                        // apron: at a cell to the metre, an apron that wide meets the next
                        // building's and the walk inside the wall, and the yard comes out
                        // as islands of asphalt with pavement running between them.
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

                Gateway();
            }

            /// <summary>
            /// The way in, cut through whatever the walk and the skirts put in front of it.
            ///
            /// It runs from the kerb until it reaches ground that is road ANYWAY, and stops
            /// there - the yard beyond is asphalt, so there is nothing left to cut. That is
            /// what keeps it from becoming a lane of road down the side of a building: a
            /// gateway is the few metres between the street and the yard, and no more.
            /// </summary>
            void Gateway()
            {
                if (Way.y <= Way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, Way.y) - Mathf.Max(a, Way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Edge(i, first)) continue;

                    for (int j = first + 1; j < NZ; j++)
                    {
                        if (!Held(i, j) || Edge(i, j)) break;
                        if (_floor[At(i, j)] == Surface.Asphalt) break;   // the yard: done
                        _floor[At(i, j)] = Surface.Asphalt;
                        if (_laid[At(i, j)]) break;                       // a building: done
                    }
                }
            }

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

            /// <summary>Is there room here: inside the block, off the kerb ring, and clear
            /// of everything already standing.</summary>
            public bool Room(Rect want)
            {
                if (want.width <= 0f || want.height <= 0f) return false;
                foreach (var taken in _taken)
                    if (taken.Overlaps(want)) return false;

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
                    if (!Held(i, j) || Edge(i, j)) return false;
                }
                return true;
            }

            // ------------------------------------------------------------ what stands

            /// <summary>A building, seated with the near corner of its footprint where it
            /// is asked for. Returns what it covers - an empty rectangle if the piece is
            /// missing from the project.</summary>
            public Rect Put(string path, float minX, float minZ, float yaw)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(minX, minZ, foot.x, foot.y);
                var go = Stand(path, Root, where.center.x, where.center.y, yaw, Deck);
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
                if (!Room(where)) return null;
                var go = Sit(path, Root, x, z, yaw, Deck + lift);
                if (go != null) _taken.Add(where);
                return go;
            }

            /// <summary>
            /// A works stack: a tall round shaft that TAPERS, with a crown at the top.
            ///
            /// No pack here ships one, and both ways of faking it out of pack pieces were
            /// worse than this. Stacked villa flues are a metre across with a chimney cap
            /// every two and a half metres - a pile of crates. A square shaft of brick wall
            /// panels is a box, and cannot taper at all without scaling the panels below
            /// the size they were drawn at, which this project does not do.
            ///
            /// So the shaft is drums, narrowing as they go up, in a flat brick red. That is
            /// what an industrial chimney is: tall, round and thinner at the top, and it is
            /// the one thing on the block that says works from right across the city.
            /// </summary>
            public void Chimney(float x, float z, float height)
            {
                const float Foot = 2.3f, Head = 1.15f, Drum = 2.2f;
                var ground = new Rect(x - Foot, z - Foot, Foot * 2f, Foot * 2f);
                if (!Room(ground))
                {
                    // a works with no stack is the whole point missed, so this is said out
                    // loud rather than left as a block that quietly came out flat
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
                    Barrel(x, z, Deck + k * course, course * 1.02f,   // a hair of overlap
                           Mathf.Lerp(Foot, Head, along), paint, "chimney");
                }

                // the crown: a course standing a little proud of the shaft, which is what
                // the top of every one of these has
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
            }

            /// <summary>A thing that goes on top of another - the second container of a
            /// stack - which has no ground of its own to book.</summary>
            public GameObject Atop(string path, float x, float z, float yaw, float lift) =>
                Sit(path, Root, x, z, yaw, Deck + lift);

            /// <summary>Painted bays, the pack's own ten metres by five, laid on the grid so
            /// they take the floor of the cells they cover.</summary>
            public void Bay(float minX, float minZ, float yaw)
            {
                float sizeX = Turned(yaw) ? Cell : Cell * 2f;
                float sizeZ = Turned(yaw) ? Cell * 2f : Cell;
                var where = new Rect(minX, minZ, sizeX, sizeZ);
                if (!Room(where)) return;
                if (Way.y > Way.x && where.yMin < Cell * 4f &&
                    where.xMax > Way.x - 1f && where.xMin < Way.y + 1f) return;   // not across the gate
                Lay(PaintedBays, Root, minX, minZ, sizeX, sizeZ, yaw);
                Claim(where);
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

            static float Half(float v) => Mathf.Round(v * 2f) * 0.5f;

            // ------------------------------------------------------------ the fence

            /// <summary>
            /// The wire round the yard: inside the kerb wherever a building is not already
            /// the wall, with the barbed crown sat on the panels and one way in on the
            /// south side.
            ///
            /// The crown is not a fence and never was - it is 77 cm of razor wire pivoted
            /// three metres up - so it goes as a run of its own, at whatever height puts
            /// its underside on the panel's top.
            /// </summary>
            public void Fence()
            {
                var panel = Box(Panel);
                float panelY = Deck - panel.min.y;
                float crownY = Wall == Wall.Wire
                    ? panelY + panel.max.y - Box(FenceCrown).min.y
                    : 0f;

                Side(true, false, panelY, crownY);    // south
                Side(true, true, panelY, crownY);     // north
                Side(false, false, panelY, crownY);   // west
                Side(false, true, panelY, crownY);    // east
            }

            string Panel => Wall == Wall.Wire ? FencePanel : BrickPanel;

            string Upright => Wall == Wall.Wire ? FencePole : BrickPillar;

            /// <summary>One side of the yard. <paramref name="alongX"/> is a run east-west,
            /// <paramref name="far"/> picks the north or the east one of the pair. The line
            /// it runs on is read off the mask column by column, so a bitten block fences
            /// round its own bite instead of straight across it.</summary>
            void Side(bool alongX, bool far, float panelY, float crownY)
            {
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
                        // the first held cell walking in from this side IS the kerb cell,
                        // and the wall stands just inside it
                        wanted[a] = true;
                        line[a] = far ? step * Cell : (step + 1) * Cell;
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
                        WireUp(Corner(from, a - 1, ring, alongX), line[from], alongX, far,
                               panelY, crownY);
                        from = -1;
                    }
                    if (a < outer && wanted[a]) from = a;
                }
                if (from >= 0)
                    WireUp(Corner(from, outer - 1, ring, alongX), line[from], alongX, far,
                           panelY, crownY);
            }

            /// <summary>
            /// The stretch a run of wall covers, which is not the whole stretch of block it
            /// belongs to.
            ///
            /// A column at the very end of a side is a CORNER cell - the one the wall along
            /// the next side has to turn through - so the run stops a cell short of it and
            /// the two sides MEET there instead of crossing. Without this every block wears
            /// two panels through each other at all four corners.
            /// </summary>
            Vector2 Corner(int first, int last, int[] ring, bool alongX)
            {
                float from = first * Cell, to = (last + 1) * Cell;
                bool openBefore = alongX ? !Held(first - 1, ring[first])
                                         : !Held(ring[first], first - 1);
                bool openAfter = alongX ? !Held(last + 1, ring[last])
                                        : !Held(ring[last], last + 1);

                // An end where the block STOPS is a corner the next side turns through, so
                // the run stops a cell short of it. An end where the block carries on at a
                // different depth is the INSIDE corner of a notch, and there the two runs
                // do not meet at all - they pass each other five metres apart. That one has
                // to be reached out to instead, or the wall has a hole at every step.
                from += openBefore ? Cell : -Cell;
                to += openAfter ? -Cell : Cell;
                return new Vector2(from, to);
            }

            /// <summary>Wall along one straight stretch, cut where a building already walls
            /// it and where the way in is.</summary>
            void WireUp(Vector2 span, float line, bool alongX, bool far, float panelY, float crownY)
            {
                // the wall goes all the way round and the ONLY thing that breaks it is the
                // way in. It used to be cut wherever a building stood against it, on the
                // theory that a building is a wall - and every place two of them met at an
                // angle, or one stood a foot short of the line, the perimeter had a hole in
                // it. Buildings are set back behind the wall instead (see Setback).
                var free = new List<Vector2> { span };

                bool wayIn = alongX && !far && Way.y > Way.x;
                if (wayIn) Cut(free, Way);

                foreach (var piece in free) _wallWanted += piece.y - piece.x;

                foreach (var piece in free)
                {
                    if (piece.y - piece.x < 1.2f) continue;
                    _wallLaid += Run(Panel, piece, line, alongX, far, panelY);
                    if (Wall == Wall.Wire) Run(FenceCrown, piece, line, alongX, far, crownY);
                    Pillar(piece.x, line, alongX, far);
                    Pillar(piece.y, line, alongX, far);
                }

                if (!wayIn) return;

                // both leaves swung back into the yard, so the way in is OPEN. A yard whose
                // gate is shut has nothing coming out of it.
                // both leaves stand INSIDE the opening. Swung back the other way they
                // reach over whatever the gate is cut between - and the gate is usually cut
                // between two buildings, so each leaf went a foot into a wall.
                var gate = Foot(FenceGate, 90f);
                Lay(FenceGate, Root, Way.x, line, gate.x, gate.y, 90f, panelY);
                Lay(FenceGate, Root, Way.y - gate.x, line, gate.x, gate.y, 90f, panelY);
                Drive(Way);
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

            /// <summary>Lays a run and returns the metres it covered, which is nothing at
            /// all when the stretch is shorter than one module - the run stretches its
            /// pieces and never squeezes them, so a two metre gap gets no wall.</summary>
            float Run(string path, Vector2 span, float line, bool alongX, bool far, float y)
            {
                float length = span.y - span.x;
                if (length < 0.5f) return 0f;

                float yaw = alongX ? 0f : 90f;
                var foot = Foot(path, yaw);
                float module = alongX ? foot.x : foot.y;
                float thick = alongX ? foot.y : foot.x;
                if (module < 0.2f) return 0f;

                // the run is divided into whole modules and the remainder is taken by
                // stretching each of them a hair. Rounded UP, a module would come out
                // SHORTER than the piece it is - a prefab scaled down to fit, which this
                // project does not do - so a stretch shorter than one module is walled by a
                // single piece STRETCHED to the module and let overhang, which is better
                // than the hole it used to leave.
                if (length < module)
                {
                    float over = (module - length) * 0.5f;
                    span = new Vector2(span.x - over, span.y + over);
                    length = module;
                }
                int panels = Mathf.Max(1, Mathf.FloorToInt(length / module + 0.01f));
                float step = length / panels;
                for (int k = 0; k < panels; k++)
                {
                    float a = span.x + k * step;
                    if (alongX) Lay(path, Root, a, far ? line - thick : line, step, thick, yaw, y);
                    else Lay(path, Root, far ? line - thick : line, a, thick, step, yaw, y);
                }
                return length;
            }

            void Pillar(float at, float line, bool alongX, bool far)
            {
                float inward = far ? -0.25f : 0.25f;
                float x = alongX ? at : line + inward;
                float z = alongX ? line + inward : at;
                Sit(Upright, Root, x, z, 0f, Deck);
            }

            /// <summary>
            /// Takes the way in out of the pavement.
            ///
            /// Not a dropped kerb: the pavement STOPS and the road comes through it, the way
            /// a yard gate crosses a footway anywhere. The road behind it was laid when the
            /// way was set (<see cref="Corridor"/>); this is the kerb cell itself.
            /// </summary>
            void Drive(Vector2 span)
            {
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, span.y) - Mathf.Max(a, span.x) < Cell * 0.4f) continue;
                    for (int j = 0; j < NZ; j++)
                    {
                        if (!Held(i, j)) continue;
                        if (Edge(i, j)) _drive[At(i, j)] = true;
                        break;
                    }
                }
            }

            // ------------------------------------------------------------ the ground

            /// <summary>
            /// The kerb, all the way round whatever shape the mask came out as.
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
                        if (!Held(i, j)) continue;
                        bool west = !Held(i - 1, j), east = !Held(i + 1, j);
                        bool south = !Held(i, j - 1), north = !Held(i, j + 1);
                        if (!west && !east && !south && !north) continue;

                        // the way in first: a drive cell is road whether or not it also
                        // happens to be a corner
                        if (_drive[At(i, j)])
                        {
                            Lay(Asphalt, Root, i * Cell, j * Cell, Cell, Cell, 0f);
                            _laid[At(i, j)] = true;
                            continue;
                        }

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
                            // the way in is not a dropped kerb, it is a break in the
                            // pavement: the block's own road surface run out to the kerb
                            // line, which is how a yard gate reads from a street
                            tile = Kerb;
                            yaw = south ? 180f : north ? 0f : west ? 270f : 90f;
                        }

                        Lay(tile, Root, i * Cell, j * Cell, Cell, Cell, yaw);
                        _laid[At(i, j)] = true;
                    }
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
                        var surface = _floor[At(i, j)];
                        Lay(surface == Surface.Asphalt ? Asphalt : Plate, Root,
                            i * Cell, j * Cell, Cell, Cell, 0f);
                        _laid[At(i, j)] = true;
                    }
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
        }

        // ------------------------------------------------------------------ the recipes

        /// <summary>The stack's material, made once and kept. Same trick the project
        /// already uses for the pieces whose atlas it does not own: a flat colour, which
        /// among flat-shaded pack art is not a stand-in but the right answer.</summary>
        static Material StackPaint()
        {
            var made = AssetDatabase.LoadAssetAtPath<Material>(StackMaterial);
            if (made != null) return made;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            made = new Material(shader) { name = "IndustrialStack" };
            var brick = new Color(0.35f, 0.18f, 0.15f);
            if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", brick);
            if (made.HasProperty("_Color")) made.SetColor("_Color", brick);
            if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", 0.08f);
            AssetDatabase.CreateAsset(made, StackMaterial);
            return made;
        }

        static int Pick(System.Random rng, params int[] of) => of[rng.Next(of.Length)];

        static bool Chance(System.Random rng, double odds) => rng.NextDouble() < odds;

        static float Between(System.Random rng, float a, float b) =>
            a + (float)rng.NextDouble() * (b - a);

        static Block Compose(Recipe recipe, Transform root, System.Random rng)
        {
            switch (recipe)
            {
                case Recipe.Plant: return Plant(root, rng);
                case Recipe.Depot: return Depot(root, rng);
                case Recipe.Yard: return Stockyard(root, rng);
                case Recipe.Strip: return Strip(root, rng);
                default: return Works(root, rng);
            }
        }

        /// <summary>
        /// The works: brick fronts filling the street with the gate in the gap between them,
        /// a shed across the back, a stack over the yard they share.
        ///
        /// The street side is BUILT, not walled. A works reads as a works because there is
        /// no way to see into it and something tall is smoking over the roofline; a yard
        /// with two sheds in the corner of it reads as a stockyard whatever is standing in
        /// the middle.
        /// </summary>
        static Block Works(Transform root, System.Random rng)
        {
            var block = new Block(root, Pick(rng, 75, 90), Pick(rng, 60, 75), rng);
            float east = block.W - Cell;
            float back = block.D - Cell;

            var older = block.Put(FactoryOld, block.In, block.In, 180f);
            float next = Mathf.Min(older.xMax + Between(rng, 8f, 10f),
                                   block.Out - Foot(Factory, 180f).x);
            var newer = block.Put(Factory, next, block.In, 180f);
            block.Way = new Vector2(older.xMax + 0.6f, newer.xMin - 0.6f);

            // whatever street is left over is built on too, so the frontage is a wall of
            // building with one way through it
            var shopFoot = Foot(Workshop, 180f);
            bool shopFronts = block.Out - newer.xMax >= shopFoot.x + 2f;
            if (shopFronts) block.Put(Workshop, block.Out - shopFoot.x, block.In, 180f);

            var hallFoot = Foot(FactoryHall, 180f);
            var hall = block.Put(FactoryHall, block.In + Between(rng, 2f, 5f),
                                 block.Far - hallFoot.y, 180f);
            if (!shopFronts)
                block.Put(Workshop, block.Out - shopFoot.x, block.Far - shopFoot.y, 180f);

            float from = Mathf.Max(older.yMax, newer.yMax);
            float to = hall.height > 0f ? hall.yMin : back;

            // the stack goes against the boiler end of the street building rather than
            // free in the middle of the yard, which is where works put them and the only
            // place one does not look planted
            block.Chimney(older.center.x, older.yMax + 4f, Between(rng, 22f, 27f));
            block.Prop(PipeRiserTall, hall.xMin + 4f, hall.yMin - 1.2f, 0f);
            if (Chance(rng, 0.6)) block.Prop(PipeRiserWide, hall.xMax - 5f, hall.yMin - 2f, 0f);
            if (hall.width > 8f)
                block.Prop(LoadingDock, hall.center.x,
                           hall.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f);

            // a store in the yard and the stock ranked along the wall: a works yard is
            // full of what the works is working on, not a car park with litter on it
            var store = block.Put(YardShed, block.In, to - Foot(YardShed, 0f).y - 1f, 0f);
            Ranks(block, BarrelMetal, Cell + 2f, from + 3f, 3, rng.Next(4, 7), 1.15f);
            Ranks(block, Pallet, east - 8f, from + 3f, 2, rng.Next(3, 5), 1.6f);

            var yard = Rect.MinMaxRect(store.xMax + 2f, from + 2f, east - 2f, to - 2f);
            block.Prop(WaterTower, east - 4f, to - 5f, 0f);
            block.Scatter(Pallet, rng.Next(2, 5), yard, 6f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), yard, 8f);
            block.Prop(PipeStack, east - 4f, from + 8f, 90f);
            block.Prop(WireSpool, east - 4f, from + 14f, 0f);
            if (Chance(rng, 0.7))
                block.Prop(BoxLorry, Between(rng, yard.xMin + 6f, yard.xMax - 6f), to - 7f, 180f);
            if (Chance(rng, 0.5)) block.Prop(Forklift, yard.xMin + 5f, from + 5f, 90f);
            block.Prop(Dumpster, newer.center.x, newer.yMax + 1.6f, 0f);
            Lamps(block, yard, 2);
            Gatepost(block, rng);
            return block;
        }

        /// <summary>
        /// The plant: two process sheds across the back with the stack standing in the gap
        /// they leave for it, a works front on the street, and a brick wall between the two
        /// of them carrying the gate.
        ///
        /// The widest of the recipes, because a plant is mostly building. What is left over
        /// is a working yard, not a car park.
        /// </summary>
        static Block Plant(Transform root, System.Random rng)
        {
            var block = new Block(root, Pick(rng, 90, 105), Pick(rng, 60, 75), rng);
            float east = block.W - Cell;
            float back = block.D - Cell;

            var works = block.Put(Factory, block.In, block.In, 180f);
            var shopFoot = Foot(Workshop, 180f);
            var shop = block.Put(Workshop, block.Out - shopFoot.x, block.In, 180f);
            float mouth = works.xMax + 1f;
            block.Way = new Vector2(mouth, mouth + Between(rng, 9f, 11f));

            var hallFoot = Foot(FactoryHall, 180f);
            float gap = Between(rng, 6f, 8f);
            float row = hallFoot.x * 2f + gap;
            float from = block.In + Mathf.Max(0f, (block.Out - block.In - row) * 0.5f);
            var west = block.Put(FactoryHall, from, block.Far - hallFoot.y, 180f);
            var far = block.Put(FactoryHall, west.xMax + gap, block.Far - hallFoot.y, 180f);

            float yardFrom = Mathf.Max(works.yMax, shop.yMax);
            float yardTo = west.height > 0f ? west.yMin : back;

            // the stack stands in the gap between the sheds, which is what the gap is for
            block.Chimney((block.In + west.xMin) * 0.5f, west.yMin + 3f,
                          Between(rng, 26f, 32f));
            block.Prop(WaterTower, west.xMin - 3.5f, yardTo - 4f, 0f);
            block.Prop(PipeRiserTall, west.xMin + 4f, west.yMin - 1.2f, 0f);
            block.Prop(PipeRiserWide, far.xMax - 5f, far.yMin - 2f, 0f);
            block.Prop(LoadingDock, west.center.x,
                       west.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f);
            block.Prop(LoadingDock, far.center.x,
                       far.yMin - Foot(LoadingDock, 0f).y * 0.5f, 0f);

            Ranks(block, BarrelMetal, Cell + 2f, yardFrom + 3f, 3, rng.Next(4, 7), 1.15f);
            Ranks(block, Pallet, east - 9f, yardFrom + 3f, 2, rng.Next(4, 6), 1.6f);

            var yard = Rect.MinMaxRect(Cell + 8f, yardFrom + 2f, east - 11f, yardTo - 2f);
            block.Scatter(BarrelMetal, rng.Next(3, 6), yard, 8f);
            block.Scatter(Pallet, rng.Next(2, 4), yard, 6f);
            block.Scatter(Crate, rng.Next(2, 4), yard, 10f);
            block.Prop(PipeStack, yard.xMin + 3f, yard.center.y, 0f);
            if (Chance(rng, 0.7)) block.Prop(BoxLorry, block.Way.x + 4f, yardFrom + 7f, 0f);
            if (Chance(rng, 0.6)) block.Prop(Forklift, yard.center.x, yardFrom + 4f, 90f);
            block.Prop(Dumpster, shop.xMin - 2f, shop.center.y, 90f);
            Lamps(block, yard, 3);
            Gatepost(block, rng);
            return block;
        }

        /// <summary>The depot: one big shed across the back with its doors on the yard, and
        /// a forecourt in front of it wide enough to turn a lorry in.</summary>
        static Block Depot(Transform root, System.Random rng)
        {
            var block = new Block(root, Pick(rng, 75, 90), 75, rng) { Wall = Wall.Wire };
            float east = block.W - Cell;

            var foot = Foot(ShedLarge, 180f);
            var shed = block.Put(ShedLarge, Mathf.Round((block.W - foot.x) * 0.5f),
                                 block.Far - foot.y, 180f);
            var hut = block.Put(YardShed, block.Out - Foot(YardShed, 180f).x, block.In, 180f);
            block.Way = new Vector2(shed.center.x - 6f, shed.center.x + 6f);


            // two docks at the doors, six metres either side of the middle: one lorry each,
            // and both clear of the door the shed actually works out of
            float dock = Foot(LoadingDock, 0f).y;
            block.Prop(LoadingDock, shed.center.x - 6f, shed.yMin - dock * 0.5f, 0f);
            block.Prop(LoadingDock, shed.center.x + 6f, shed.yMin - dock * 0.5f, 0f);

            for (int k = 0; k < 3; k++)
            {
                float x = Mathf.Round((Cell + 2f + k * 12f) / Cell) * Cell;
                if (x + 10f < hut.xMin) block.Bay(x, Mathf.Round(block.In / Cell) * Cell + Cell, 0f);
            }

            var apron = Rect.MinMaxRect(Cell + 2f, Cell + 8f, east - 2f, shed.yMin - 5f);
            if (Chance(rng, 0.8)) block.Prop(BoxLorry, apron.xMin + 6f, apron.center.y, 0f);
            if (Chance(rng, 0.6)) block.Prop(BoxLorry, apron.xMax - 6f, apron.center.y, 180f);
            block.Scatter(Pallet, rng.Next(2, 5), apron, 5f);
            block.Prop(Dumpster, hut.xMin - 2.5f, Cell + 2f, 90f);
            block.Prop(Forklift, shed.center.x, shed.yMin - 8f, Between(rng, 60f, 120f));

            // the ground down either side of the shed: stock on one, empties and a spare
            // lorry on the other
            var alongWest = Rect.MinMaxRect(Cell + 1.5f, shed.yMin + 2f,
                                            shed.xMin - 1.5f, block.D - Cell - 2f);
            var alongEast = Rect.MinMaxRect(shed.xMax + 1.5f, hut.yMax + 3f,
                                            east - 1.5f, block.D - Cell - 2f);
            block.Scatter(BarrelMetal, rng.Next(4, 9), alongWest, 8f);
            block.Scatter(Pallet, rng.Next(3, 6), alongWest, 6f);
            block.Prop(BoxLorry, alongWest.center.x, alongWest.yMax - 6f, 0f);
            block.Scatter(Crate, rng.Next(2, 5), alongEast, 10f);
            block.Scatter(WireSpool, rng.Next(1, 3), alongEast, 0f);
            var spare = Containers[rng.Next(Containers.Length)];
            block.Prop(spare, alongEast.center.x, alongEast.yMin + 4f, 0f);

            Lamps(block, apron, 3);
            Gatepost(block, rng);
            return block;
        }

        /// <summary>The stockyard: a row of sheds with their backs to the far street and
        /// blocks of containers standing off in front of them.</summary>
        static Block Stockyard(Transform root, System.Random rng)
        {
            var block = new Block(root, Pick(rng, 90, 105), Pick(rng, 60, 75), rng)
                        { Wall = Wall.Wire };
            float east = block.W - Cell;
            float back = block.D - Cell;

            var small = Foot(ShedSmall, 180f);
            var garage = Foot(DepotGarage, 180f);
            float gap = Between(rng, 7f, 10f);
            float row = small.x + gap + garage.x;
            float from = block.In + Mathf.Max(0f, (block.Out - block.In - row) * 0.5f);

            var west = block.Put(ShedSmall, from, block.Far - small.y, 180f);
            var far = block.Put(DepotGarage, west.xMax + gap, block.Far - garage.y, 180f);
            block.Way = new Vector2(block.W * 0.5f - 6f, block.W * 0.5f + 6f);

            float lane = Mathf.Min(west.yMin, far.yMin);

            // the containers, stacked on the port's odds: mostly two high, a shipper's whole
            // block in one colour now and then, and a gap where one has been taken away
            var can = Box(Containers[0]).size;
            // as many ranks as the ground takes, up to three - one rank against the wire
            // with an empty yard behind it is a photograph of a yard, not a yard
            float rank = can.x + 4f;
            int ranks = Mathf.Clamp(Mathf.FloorToInt((lane - 3f - (block.In + 2f)) / rank), 1, 3);
            for (int r = 0; r < ranks; r++)
            {
                float z = block.In + 2f + can.x * 0.5f + r * rank;
                bool oneShipper = Chance(rng, 0.35);
                string shipper = Containers[rng.Next(Containers.Length)];
                int stacks = Mathf.FloorToInt((block.Out - block.In - 2f) / (can.z + 0.4f));
                for (int s = 0; s < stacks; s++)
                {
                    if (Chance(rng, 0.18)) continue;
                    float x = block.In + 1f + s * (can.z + 0.4f) + can.z * 0.5f;
                    // the way in is a way in: no rank closes the lane the gate opens onto,
                    // or the yard is a yard nothing can be driven into
                    if (x + can.z * 0.5f > block.Way.x - 1.5f &&
                        x - can.z * 0.5f < block.Way.y + 1.5f) continue;
                    int tall = Chance(rng, 0.2) ? 1 : Chance(rng, 0.75) ? 2 : 3;
                    for (int t = 0; t < tall; t++)
                    {
                        string colour = oneShipper ? shipper : Containers[rng.Next(Containers.Length)];
                        var stood = t == 0 ? block.Prop(colour, x, z, 90f)
                                           : block.Atop(colour, x, z, 90f, t * can.y);
                        if (stood == null) break;
                    }
                }
            }

            var open = Rect.MinMaxRect(Cell + 2f, lane - 7f, east - 2f, lane - 1f);
            block.Prop(Forklift, open.xMin + 5f, open.center.y, 0f);
            block.Scatter(WireSpool, rng.Next(2, 4), open, 0f);
            block.Scatter(Crate, rng.Next(2, 5), open, 10f);
            block.Scatter(BarrelMetal, rng.Next(3, 7), open, 8f);
            block.Prop(BurnBarrel, east - 6f, Cell + 2f, 0f);

            // the ground beside the shed row, which the row does not reach
            var flankWest = Rect.MinMaxRect(Cell + 1.5f, lane + 1f, west.xMin - 1.5f, back - 2f);
            var flankEast = Rect.MinMaxRect(far.xMax + 1.5f, lane + 1f, east - 1.5f, back - 2f);
            block.Scatter(Pallet, rng.Next(2, 5), flankWest, 6f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), flankEast, 8f);
            block.Prop(Dumpster, flankEast.center.x, flankEast.yMax - 2f, 90f);
            block.Prop(BoxLorry, (west.xMax + far.xMin) * 0.5f, lane - 6f, 0f);
            Lamps(block, Rect.MinMaxRect(Cell + 2f, Cell + 2f, east - 2f, back - 2f), 3);
            Gatepost(block, rng);
            return block;
        }

        /// <summary>The service strip: a workshop and a hall on the street with the way in
        /// between them, and a bite out of the far corner that the street takes back as a
        /// car park of its own.</summary>
        static Block Strip(Transform root, System.Random rng)
        {
            var block = new Block(root, Pick(rng, 60, 75), Pick(rng, 45, 60), rng);
            block.Bite(block.NX - 4, block.NZ - 2, 4, 2);

            float east = block.W - Cell;
            var shop = block.Put(Workshop, block.In, block.In, 180f);
            var hall = block.Put(FactoryHall, block.Out - Foot(FactoryHall, 180f).x,
                                 block.In, 180f);

            float middle = (shop.xMax + hall.xMin) * 0.5f;
            block.Way = new Vector2(shop.xMax + 0.6f, hall.xMin - 0.6f);

            float from = Mathf.Max(shop.yMax, hall.yMax);

            block.Bay(Cell, block.D - Cell * 2f, 0f);
            block.Bay(Cell, block.D - Cell * 3f, 0f);

            var yard = Rect.MinMaxRect(Cell + 2f, from + 2f, east - 2f, block.D - Cell - 2f);
            block.Prop(Dumpster, hall.center.x, hall.yMax + 1.6f, 0f);
            block.Scatter(BarrelPlastic, rng.Next(2, 5), yard, 8f);
            block.Scatter(Pallet, rng.Next(2, 4), yard, 6f);
            if (Chance(rng, 0.6)) block.Prop(BoxLorry, middle, from + 6f, 0f);
            Lamps(block, yard, 2);
            Gatepost(block, rng);
            return block;
        }

        /// <summary>
        /// Stock set down in ranks: what a yard looks like where somebody is working, as
        /// against <see cref="Block.Scatter"/>, which is what it looks like where somebody
        /// dropped things. Both belong in a yard and neither on its own does.
        /// </summary>
        static void Ranks(Block block, string path, float x, float z, int across, int along,
                          float pitch)
        {
            for (int a = 0; a < across; a++)
                for (int b = 0; b < along; b++)
                {
                    if (Chance(block.Rng, 0.12)) continue;   // one gone from the rank
                    block.Prop(path, x + a * pitch, z + b * pitch, 0f);
                }
        }

        /// <summary>Yard lamps down the working ground, far enough apart that they read as
        /// lighting rather than as a fence of their own.</summary>
        static void Lamps(Block block, Rect over, int count)
        {
            if (over.width <= 0f || over.height <= 0f) return;
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count;
                block.Prop(YardLamp, over.xMin + over.width * t, over.yMax - 1.2f, 0f);
            }
        }

        /// <summary>What every gate has around it: a board saying keep out, a cone or two
        /// where the wheels cut the corner, and a block of concrete narrowing the way in so
        /// that a lorry has to slow down for it.</summary>
        static void Gatepost(Block block, System.Random rng)
        {
            if (block.Way.y <= block.Way.x) return;
            float z = block.In + 2.2f;
            block.Prop(DangerSign, block.Way.x - 1.2f, z, 0f);
            if (Chance(rng, 0.6)) block.Prop(Cone, block.Way.x + 1.2f, z + 1.5f, 0f);
            if (Chance(rng, 0.6)) block.Prop(Cone, block.Way.y - 1.2f, z + 1.5f, 0f);
            if (Chance(rng, 0.5)) block.Prop(Barrier, block.Way.y + 1.4f, z, 0f);
        }

        // ------------------------------------------------------------------ generating

        static Recipe Choose(string which, int index)
        {
            switch ((which ?? "all").Trim().ToLowerInvariant())
            {
                case "works": return Recipe.Works;
                case "depot": return Recipe.Depot;
                case "yard": return Recipe.Yard;
                case "strip": return Recipe.Strip;
                default: return (Recipe)(index % 4);
            }
        }

        /// <summary>Stands four candidates in a row and says a line about each, which is
        /// what the pipeline command hands back.</summary>
        public static object[] Generate(int seed, string which) =>
            InTheLab(scene => GenerateIn(scene, seed, which));

        static object[] GenerateIn(Scene scene, int seed, string which)
        {
            Wipe(scene);
            Absent.Clear();

            var root = new GameObject(CandidatesRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            var told = new List<object>();
            float x = 0f;

            for (int k = 0; k < 4; k++)
            {
                var recipe = Choose(which, k);
                var rng = new System.Random(seed * 97 + k * 31 + (int)recipe * 7);

                var candidate = new GameObject($"{CandidatePrefix}{k + 1}");
                candidate.transform.SetParent(root.transform, false);

                // composed at the origin and moved afterwards: every piece is placed by
                // measuring where it lands, and measuring is done in world space
                var block = Compose(recipe, candidate.transform, rng);
                block.Fence();
                block.Surfaces();
                block.Kerbs();
                block.Floor();

                int pieces = candidate.transform.childCount;
                candidate.transform.position = new Vector3(x, 0f, 0f);
                Caption(candidate.transform, k + 1, recipe, block, seed);
                x += block.W + Walk;

                told.Add(new
                {
                    index = k + 1,
                    recipe = recipe.ToString().ToLowerInvariant(),
                    seed,
                    width = block.W,
                    depth = block.D,
                    pieces,
                    gaps = block.Gaps(),
                    wallGap = Mathf.Round(block.WallGap * 100f) / 100f,
                    wallInBuilding = block.WallInBuilding(),
                });
            }

            Undo.RegisterCreatedObjectUndo(root, "Industrial candidates");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;

            // written out at once. A set of candidates that only exists in memory is a set
            // that whoever opens another scene next throws away, and this editor is not
            // always ours alone.
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            if (Absent.Count > 0)
                Debug.LogWarning("[Industrial] missing from the project, and left out of every " +
                                 "candidate:\n  " + string.Join("\n  ", Absent));

            return told.ToArray();
        }

        static void Caption(Transform candidate, int index, Recipe recipe, Block block, int seed)
        {
            BlockLotPads.PadLabel(LabelName,
                                  $"{CandidatePrefix}{index}\n" +
                                  $"{recipe.ToString().ToLowerInvariant()} | " +
                                  $"{block.W} x {block.D} m | seed {seed}",
                                  candidate.position + new Vector3(block.W * 0.5f, 6f, block.D + 4f),
                                  candidate);
            var label = candidate.Find(LabelName);
            if (label) label.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        static GameObject Found(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);

        static void Wipe(Scene scene)
        {
            var root = Found(scene, CandidatesRoot);
            if (root) Object.DestroyImmediate(root);
        }

        // -------------------------------------------------------------------- baking

        sealed class Chosen
        {
            public int Index;
            public string Name, Path, Trouble;
        }

        /// <summary>
        /// Files the candidates asked for and throws the rest away.
        ///
        /// It writes no prefab itself. The pieces are moved onto a tray and
        /// <see cref="CoreBlockTray"/> bakes them, so a composed block goes through exactly
        /// the same door as a harvested one: same pivot rule, same flattening, same replay
        /// of overrides, same <c>BlockLotTag</c>, same folder.
        /// </summary>
        public static object[] BakeChosen(int[] indices, string[] names, bool keepOthers) =>
            InTheLab(scene => BakeIn(scene, indices, names, keepOthers));

        static object[] BakeIn(Scene scene, int[] indices, string[] names, bool keepOthers)
        {
            var root = Found(scene, CandidatesRoot);
            if (root == null)
                return new object[]
                {
                    new { error = "no candidates are standing; generate some first" },
                };

            var chosen = new List<Chosen>();
            var trays = new List<Transform>();

            for (int k = 0; k < indices.Length; k++)
            {
                int index = indices[k];
                var candidate = root.transform.Find($"{CandidatePrefix}{index}");
                if (candidate == null)
                {
                    chosen.Add(new Chosen { Index = index, Trouble = "no such candidate" });
                    continue;
                }

                string name = names != null && k < names.Length && !string.IsNullOrEmpty(names[k])
                    ? names[k].Trim()
                    : NextName(RecipeOf(candidate));
                string path = $"{CoreBlockTray.OutDir}/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    chosen.Add(new Chosen
                    {
                        Index = index,
                        Name = name,
                        Trouble = "a block of that name is already on disk; pass another --names",
                    });
                    continue;
                }

                var box = new Bounds(candidate.position, Vector3.one);
                if (WorldBox(candidate.gameObject, out var measured)) box = measured;

                var tray = CoreBlockTray.MakeTray(scene, name,
                                                  new Vector3(box.center.x, 0f, box.center.z),
                                                  box.size.x + 2f, box.size.z + 2f);

                var pieces = new List<Transform>();
                foreach (Transform piece in candidate) pieces.Add(piece);
                foreach (var piece in pieces)
                {
                    if (piece.name == LabelName)
                    {
                        Object.DestroyImmediate(piece.gameObject);
                        continue;
                    }
                    piece.SetParent(tray, true);
                }
                Object.DestroyImmediate(candidate.gameObject);

                trays.Add(tray);
                chosen.Add(new Chosen { Index = index, Name = name, Path = path });
            }

            int written = CoreBlockTray.BakeQuietly(scene, out var said);
            Debug.Log($"[Industrial] {written} block prefab(s) written to " +
                      CoreBlockTray.OutDir + ": " + string.Join("; ", said));

            // the trays have done their work and the bake has emptied them; left standing,
            // they would take in whatever is dragged near them next
            foreach (var tray in trays)
                if (tray) Undo.DestroyObjectImmediate(tray.gameObject);

            if (!keepOthers) Wipe(scene);
            if (Found(scene, CoreBlockTray.ReviewRoot)) CoreBlockTray.ShowBaked();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            return chosen.Select(one => (object)new
            {
                index = one.Index,
                name = one.Name,
                path = one.Path,
                wrote = one.Path != null &&
                        AssetDatabase.LoadAssetAtPath<GameObject>(one.Path) != null,
                trouble = one.Trouble,
            }).ToArray();
        }

        /// <summary>What the caption says this candidate was composed from, so that a bake
        /// nobody named is filed as the kind of block it is.</summary>
        static string RecipeOf(Transform candidate)
        {
            var label = candidate.Find(LabelName);
            var text = label ? label.GetComponent<TextMesh>() : null;
            if (text == null) return "block";
            var lines = text.text.Split('\n');
            if (lines.Length < 2) return "block";
            var first = lines[1].Split('|')[0].Trim();
            return string.IsNullOrEmpty(first) ? "block" : first;
        }

        static string NextName(string recipe)
        {
            int highest = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CoreBlockTray.OutDir }))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(guid));
                var match = System.Text.RegularExpressions.Regex.Match(
                    name, $@"^ind-{recipe}-(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                    highest = Mathf.Max(highest, n);
            }
            return $"ind-{recipe}-{highest + 1:00}";
        }

        // --------------------------------------------------------------------- the lab

        /// <summary>
        /// The workbench scene, loaded BESIDE whatever is open rather than instead of it.
        ///
        /// Its own scene, not the harvest's, because the harvest scene carries the whole
        /// Synty demo and takes seconds to write - a bake that saves at the end of itself
        /// runs out of the pipeline's patience there. And loaded additively, because this
        /// editor is not always ours alone: a second session working in another scene, with
        /// changes it has not saved, must not have that scene shut from under it. The lab
        /// is made active only for as long as the work takes, and put back afterwards.
        /// </summary>
        internal static Scene Lab()
        {
            var open = SceneManager.GetActiveScene();
            if (open.path == LabPath) return open;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded.path == LabPath) return loaded;
            }

            if (System.IO.File.Exists(LabPath))
                return EditorSceneManager.OpenScene(LabPath, OpenSceneMode.Additive);

            var made = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var light = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(light, made);
            var sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var trays = new GameObject(CoreBlockTray.TraysRoot);
            SceneManager.MoveGameObjectToScene(trays, made);

            EditorSceneManager.SaveScene(made, LabPath);
            Debug.Log($"[Industrial] {LabPath} made, beside whatever else is open.");
            return made;
        }

        /// <summary>Runs a piece of work with the lab active, and puts the editor back the
        /// way it was found. Everything an editor tool stands up - a temporary instance
        /// taken to measure a prefab, a tray - lands in the ACTIVE scene, so making the lab
        /// active for the duration is what keeps it all out of somebody else's.</summary>
        static T InTheLab<T>(System.Func<Scene, T> work)
        {
            var lab = Lab();
            var was = SceneManager.GetActiveScene();
            bool moved = was != lab && lab.IsValid();
            if (moved) SceneManager.SetActiveScene(lab);
            try { return work(lab); }
            finally { if (moved && was.IsValid()) SceneManager.SetActiveScene(was); }
        }

        [MenuItem("Tools/City/Core/Industrial/Open The Industrial Lab", priority = 60)]
        public static void OpenLab()
        {
            var lab = Lab();
            if (lab.IsValid() && SceneManager.GetActiveScene() != lab)
                SceneManager.SetActiveScene(lab);
        }

        [MenuItem("Tools/City/Core/Industrial/Generate Four Candidates", priority = 61)]
        public static void GenerateMenu()
        {
            var told = Generate(7, "all");
            var root = Found(Lab(), CandidatesRoot);
            var view = SceneView.lastActiveSceneView;
            if (view && root && WorldBox(root, out var box)) view.Frame(box, false);
            Debug.Log($"[Industrial] {told.Length} candidates standing under \"{CandidatesRoot}\". " +
                      "Keep the ones worth keeping with Bake Candidate N, or with " +
                      "unity command gangsters_industrial --bake 1,2");
        }

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 1", priority = 62)]
        public static void BakeOne() => BakeFromMenu(1);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 2", priority = 63)]
        public static void BakeTwo() => BakeFromMenu(2);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 3", priority = 64)]
        public static void BakeThree() => BakeFromMenu(3);

        [MenuItem("Tools/City/Core/Industrial/Bake Candidate 4", priority = 65)]
        public static void BakeFour() => BakeFromMenu(4);

        static void BakeFromMenu(int index)
        {
            foreach (var one in BakeChosen(new[] { index }, null, keepOthers: true))
                Debug.Log($"[Industrial] {one}");
        }

        [MenuItem("Tools/City/Core/Industrial/Discard Candidates", priority = 70)]
        public static void DiscardMenu()
        {
            var scene = Lab();
            if (Found(scene, CandidatesRoot) == null)
            {
                Debug.Log("[Industrial] there were no candidates standing.");
                return;
            }
            Wipe(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Industrial] the candidates are gone; nothing was written.");
        }
    }
}
