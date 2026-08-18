using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The industrial stock the GangWarfare pack never assembled: a brick factory, a big
    /// metal production hall and a small workshop, kit-bashed from the pack's warehouse
    /// modules and baked through SyntyKitExtractor.BakeGroup like every other kit
    /// building. The pack's one assembled compound (the demo stovariste) is extracted
    /// separately by SyntyKitExtractor.ExtractGangWarehouse; these three are its
    /// catalogue neighbours, built from the same walls so they read as one district.
    ///
    /// Module facts measured offline off the collision meshes' m_LocalAABB (the render
    /// FBX is binary, the convex assets are text): every GangWarfare wall piece is
    /// 3.0 wide x 3.0 high - NOT the PalmCity 2.5 x 3.006 - with a corner pivot whose
    /// local x runs -3..0 (the MIRROR of PalmCity's 0..2.5) and its plane at z~0 facing
    /// +Z. Wall_Metal_Door_Slide_02 is a 6 x 6 piece (two modules, two courses).
    /// Roof_Flat_Open_01 is a 3 x 3 slab covering the -x/-z quadrant of its pivot;
    /// Roof_Flat_Straight_01 is the same slab with a parapet lip on its local +z edge;
    /// Roof_Flat_L_01 adds a second lip on its local -x edge. SM_Bld_Roof_01 is the big
    /// hall roof: one 3 m-wide rib spanning z -9.56..+0.56 (local x -0.08..+3.08, so
    /// pivot at the LOW-x edge, positive run - the one gang piece that is not mirrored),
    /// and SM_Bld_Roof_Truss_01 spans z -9.09..+0.09 under it.
    ///
    /// No Synty pack ships an industrial smokestack, so the old factory's chimney is a
    /// column of PalmCity SM_Bld_Villa_Chimney_01 blocks (1.19 x 1.31 footprint, 2.505
    /// high, centred pivot with the base 0.126 below it) - segmented like a banded brick
    /// stack. The rooftop water tower is PolygonCity's SM_Prop_Water_Tower_01 (2.5 x 2.5,
    /// 5.4 high, centred pivot at its base). Cross-pack borrowing is already the kit's
    /// habit - the fire station takes PalmCity garage doors into PoliceStation walls.
    /// </summary>
    public static class SyntyIndustrialKitBash
    {
        const string Bld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string Props = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";

        public const int Version = 2;
        const string VersionPath = SyntyKitExtractor.BuildingsDir + "/IndustrialKitVersion.txt";

        const float M = 3f;      // the gang module: 3.0 wide, 3.0 per storey
        const float Course = 3f;

        // Walls
        const string MetalWall = Bld + "SM_Bld_Wall_Metal_01.prefab";
        const string MetalWindow = Bld + "SM_Bld_Wall_Metal_Window_01.prefab";
        const string MetalDoor = Bld + "SM_Bld_Wall_Metal_Door_01.prefab";
        const string MetalSlide = Bld + "SM_Bld_Wall_Metal_Door_Slide_01.prefab";
        const string MetalSlideBig = Bld + "SM_Bld_Wall_Metal_Door_Slide_02.prefab";
        const string MetalCorner = Bld + "SM_Bld_Wall_Metal_Exterior_Corner_01.prefab";
        const string BrickWall = Bld + "SM_Bld_Wall_Brick_01.prefab";
        const string BrickWindow = Bld + "SM_Bld_Wall_Brick_Window_01.prefab";
        const string BrickDoor = Bld + "SM_Bld_Wall_Brick_Door_01.prefab";
        const string BrickCorner = Bld + "SM_Bld_Wall_Brick_Exterior_Corner_01.prefab";

        // Flat roof set
        const string RoofOpen = Bld + "SM_Bld_Roof_Flat_Open_01.prefab";
        const string RoofEdge = Bld + "SM_Bld_Roof_Flat_Straight_01.prefab";
        const string RoofL = Bld + "SM_Bld_Roof_Flat_L_01.prefab";

        // Hall roof set
        const string HallRoof = Bld + "SM_Bld_Roof_01.prefab";
        const string HallTruss = Bld + "SM_Bld_Roof_Truss_01.prefab";

        // Roof furniture
        const string Fan = Props + "SM_Prop_Warehouse_Fan_01.prefab";
        const string Vent = Props + "SM_Prop_AirVent_02.prefab";

        // The cross-pack borrows: the chimney block and the rooftop water tower.
        const string ChimneyBlock =
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Villa_Chimney_01.prefab";
        const string WaterTower =
            "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Prop_Water_Tower_01.prefab";
        const float ChimneyCourse = 2.505f; // block height; base sits 0.126 under the pivot

        [MenuItem("Tools/City/Catalog/Rebuild Synty Industrial Kit (Kit-Bash)", priority = 3)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        public static void BuildIfStale()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            if (marker && marker.text.Trim() == Version.ToString())
                return;

            BuildFactory();
            BuildFactoryOld();
            BuildFactoryHall();
            BuildWorkshop();

            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The brick factory: two storeys, personnel door and a metal slide door in the
        /// front (the demo compound mixes metal doors into brick walls the same way),
        /// window rows above, parapet flat roof with fans and vents.
        /// </summary>
        static void BuildFactory()
        {
            const int w = 8, d = 5;
            var root = new GameObject("building-factory");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((BrickWall, 1), (BrickWindow, 1), (BrickDoor, 1), (BrickWindow, 1),
                               (MetalSlide, 1), (BrickWindow, 1), (BrickWindow, 1), (BrickWall, 1)),
                    back: Enumerable.Repeat(BrickWall, w).ToArray(),
                    side: new[] { BrickWall, BrickWall, BrickWindow, BrickWall, BrickWall });
                WallRing(root, w, d, floor: 1,
                    front: Row((BrickWall, 1), (BrickWindow, 1), (BrickWindow, 1), (BrickWindow, 1),
                               (BrickWindow, 1), (BrickWindow, 1), (BrickWindow, 1), (BrickWall, 1)),
                    back: new[] { BrickWall, BrickWindow, BrickWindow, BrickWindow,
                                  BrickWindow, BrickWindow, BrickWindow, BrickWall },
                    side: new[] { BrickWall, BrickWindow, BrickWindow, BrickWindow, BrickWall });
                CornerPosts(root, w, d, floors: 2, BrickCorner);
                FlatRoof(root, w, d, topY: 2 * Course);

                var roofY = 2 * Course + 0.15f;
                Place(root, Fan, -4.5f, roofY, 1.5f, 0f);
                Place(root, Fan, 4.5f, roofY, -1.5f, 90f);
                Place(root, Vent, -1.5f, roofY, -3f, 0f);
                Place(root, WaterTower, 7f, roofY, 0f, 0f);

                SyntyKitExtractor.BakeGroup(root, root.name, yaw: 0f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The old factory: a lower brick works with a freestanding ~12.5 m banded brick
        /// chimney against its back-left flank - the one silhouette the district was
        /// missing. The chimney mouth is what Tools/City/Measure Chimney Vents looks
        /// for, so the smoke system can pick the stack up like the polyperfect works.
        /// </summary>
        static void BuildFactoryOld()
        {
            const int w = 7, d = 4;
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var root = new GameObject("building-factory-old");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((BrickWall, 1), (BrickWindow, 1), (BrickWindow, 1), (BrickDoor, 1),
                               (BrickWindow, 1), (BrickWindow, 1), (BrickWall, 1)),
                    back: Enumerable.Repeat(BrickWall, w).ToArray(),
                    side: new[] { BrickWall, BrickWindow, BrickWindow, BrickWall });
                WallRing(root, w, d, floor: 1,
                    front: Row((BrickWall, 1), (BrickWindow, 1), (BrickWindow, 1), (BrickWindow, 1),
                               (BrickWindow, 1), (BrickWindow, 1), (BrickWall, 1)),
                    back: new[] { BrickWall, BrickWall, BrickWindow, BrickWindow,
                                  BrickWindow, BrickWall, BrickWall },
                    side: new[] { BrickWall, BrickWindow, BrickWindow, BrickWall });
                CornerPosts(root, w, d, floors: 2, BrickCorner);
                FlatRoof(root, w, d, topY: 2 * Course);

                var roofY = 2 * Course + 0.15f;
                Place(root, Fan, -3f, roofY, 1.5f, 0f);
                Place(root, Vent, 3f, roofY, -1.5f, 180f);

                // The chimney: five blocks from the ground up, snug against the back-left
                // corner (block half-width 0.6 m outside the wall plane).
                for (var k = 0; k < 5; k++)
                    Place(root, ChimneyBlock, -halfW - 0.7f, 0.126f + k * ChimneyCourse,
                          -halfD + 1.6f, 0f);

                SyntyKitExtractor.BakeGroup(root, root.name, yaw: 0f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The production hall: 21 x 9 m, walls two courses high, two 6 x 6 slide doors
        /// in the front with a personnel door between them, and the pack's big ribbed
        /// roof spanning the full depth over exposed trusses.
        /// </summary>
        static void BuildFactoryHall()
        {
            const int w = 7, d = 3;
            const float topY = 2 * Course;
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var root = new GameObject("building-factory-hall");
            try
            {
                // Ground course: the big doors take columns 1-2 and 4-5 through BOTH
                // courses, so the upper course only fills the columns they leave
                // (a null row entry is a gap of that many modules).
                WallRing(root, w, d, floor: 0,
                    front: Row((MetalWall, 1), (MetalSlideBig, 2), (MetalDoor, 1),
                               (MetalSlideBig, 2), (MetalWall, 1)),
                    back: new[] { MetalWall, MetalWall, MetalWindow, MetalWall,
                                  MetalWindow, MetalWall, MetalWall },
                    side: new[] { MetalWall, MetalDoor, MetalWall });
                WallRing(root, w, d, floor: 1,
                    front: Row((MetalWall, 1), (null, 2), (MetalWindow, 1),
                               (null, 2), (MetalWall, 1)),
                    back: new[] { MetalWall, MetalWindow, MetalWall, MetalWindow,
                                  MetalWall, MetalWindow, MetalWall },
                    side: new[] { MetalWall, MetalWindow, MetalWall });
                CornerPosts(root, w, d, floors: 2, MetalCorner);

                // The ribbed roof: one 3 m rib per column, pivot at the rib's low-x edge
                // and its +z eave (spans z -9.56..+0.56), so anchoring at z = halfD
                // overhangs each gable end by ~0.5 m. Trusses on the interior module
                // lines carry it visually through the open doors.
                for (var i = 0; i < w; i++)
                    Place(root, HallRoof, -halfW + i * M, topY, halfD, 0f);
                for (var i = 1; i < w; i++)
                    Place(root, HallTruss, -halfW + i * M, topY, halfD, 0f);

                SyntyKitExtractor.BakeGroup(root, root.name, yaw: 0f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The workshop: one storey of metal walls, slide door up front,
        /// parapet flat roof - the small trade unit beside the big two.</summary>
        static void BuildWorkshop()
        {
            const int w = 5, d = 4;
            var root = new GameObject("building-workshop");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((MetalWall, 1), (MetalSlide, 1), (MetalDoor, 1),
                               (MetalWindow, 1), (MetalWall, 1)),
                    back: new[] { MetalWall, MetalWall, MetalWindow, MetalWall, MetalWall },
                    side: new[] { MetalWall, MetalWindow, MetalWindow, MetalWall });
                CornerPosts(root, w, d, floors: 1, MetalCorner);
                FlatRoof(root, w, d, topY: Course);

                var roofY = Course + 0.15f;
                Place(root, Fan, -1.5f, roofY, 1.5f, 0f);
                Place(root, Vent, 3f, roofY, -1.5f, 180f);

                SyntyKitExtractor.BakeGroup(root, root.name, yaw: 0f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static (string path, int modules)[] Row(params (string path, int modules)[] row) => row;

        /// <summary>
        /// One storey of the wall ring. Gang corner-pivot arithmetic (local x -M..0,
        /// facing +Z at identity): a piece at pivot P covers, after yaw,
        ///   yaw 0:   x [P.x-width, P.x]  (front, plane z=P.z)  -> P.x = x0 + width
        ///   yaw 180: x [P.x, P.x+M]      (back)                -> P.x = x0
        ///   yaw 90:  z [P.z, P.z+M]      (right, x=+halfW)     -> P.z = z0
        ///   yaw 270: z [P.z-M, P.z]      (left, x=-halfW)      -> P.z = z0 + M
        /// front[] is a cursor row of (piece, modules) so a multi-module piece (the 6 m
        /// slide door) claims its full span; a null path is a gap that wide (the storey
        /// above a two-course piece). back[] and side[] are plain one-module columns;
        /// side[] runs front to back and mirrors onto both flanks.
        /// </summary>
        static void WallRing(GameObject root, int w, int d, int floor,
                             (string path, int modules)[] front, string[] back, string[] side)
        {
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var y = floor * Course;

            var cursor = -halfW;
            foreach (var (path, modules) in front)
            {
                Place(root, path, cursor + modules * M, y, halfD, 0f);
                cursor += modules * M;
            }
            for (var i = 0; i < w; i++)
                Place(root, back[i], -halfW + i * M, y, -halfD, 180f);
            for (var i = 0; i < d; i++)
            {
                // side[] is authored front to back; the ring runs back to front on the
                // right flank (z0 ascends), so the index flips there.
                var z0 = -halfD + i * M;
                Place(root, side[d - 1 - i], halfW, y, z0, 90f);
                Place(root, side[d - 1 - i], -halfW, y, z0 + M, 270f);
            }
        }

        /// <summary>
        /// The exterior corner posts, one per storey per corner. The post occupies the
        /// -x/-z quadrant of its pivot, so each corner takes the yaw that folds the post
        /// INTO the building: front-right 0, back-right 90, back-left 180, front-left 270.
        /// </summary>
        static void CornerPosts(GameObject root, int w, int d, int floors, string post)
        {
            float halfW = w * M / 2f, halfD = d * M / 2f;
            for (var floor = 0; floor < floors; floor++)
            {
                var y = floor * Course;
                Place(root, post, halfW, y, halfD, 0f);
                Place(root, post, halfW, y, -halfD, 90f);
                Place(root, post, -halfW, y, -halfD, 180f);
                Place(root, post, -halfW, y, halfD, 270f);
            }
        }

        /// <summary>
        /// The parapet flat roof: open 3 x 3 slabs everywhere, the perimeter cells
        /// swapped for parapet-lipped pieces - L in the corners, straight between them.
        /// Same quadrant arithmetic as the walls; the straight piece's lip sits on its
        /// local +z edge, the L's on +z and -x, and the yaws below turn those lips
        /// outward on every side (front 0, right 90, back 180, left 270).
        /// </summary>
        static void FlatRoof(GameObject root, int w, int d, float topY)
        {
            float halfW = w * M / 2f, halfD = d * M / 2f;
            for (var i = 0; i < w; i++)
                for (var j = 0; j < d; j++)
                {
                    float x0 = -halfW + i * M, z0 = -halfD + j * M;
                    bool left = i == 0, right = i == w - 1, backE = j == 0, frontE = j == d - 1;

                    if (frontE && left) Place(root, RoofL, x0 + M, topY, z0 + M, 0f);
                    else if (frontE && right) Place(root, RoofL, x0 + M, topY, z0, 90f);
                    else if (backE && right) Place(root, RoofL, x0, topY, z0, 180f);
                    else if (backE && left) Place(root, RoofL, x0, topY, z0 + M, 270f);
                    else if (frontE) Place(root, RoofEdge, x0 + M, topY, z0 + M, 0f);
                    else if (right) Place(root, RoofEdge, x0 + M, topY, z0, 90f);
                    else if (backE) Place(root, RoofEdge, x0, topY, z0, 180f);
                    else if (left) Place(root, RoofEdge, x0, topY, z0 + M, 270f);
                    else Place(root, RoofOpen, x0 + M, topY, z0 + M, 0f);
                }
        }

        static void Place(GameObject root, string prefabPath, float x, float y, float z, float yaw)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                Debug.LogWarning($"SyntyIndustrialKitBash: missing module {prefabPath}");
                return;
            }

            var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            piece.transform.SetLocalPositionAndRotation(
                new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
