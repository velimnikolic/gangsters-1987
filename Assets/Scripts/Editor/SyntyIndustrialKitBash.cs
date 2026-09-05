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
    ///
    /// v3 adds the two layers a bare wall ring was missing, both counted off the pack's own
    /// demo scene rather than guessed at. That scene stands 2,902 pieces of 355 kinds on one
    /// compound, and what it spends them on is almost never more wall: it is ROOF - 45
    /// extract fans, a jointed venting run, air vents, hoardings - and FITTINGS on the face
    /// of the wall - 27 lights, 20 wall fans, 14 cameras, pipe risers, ladders, company
    /// boards. Those are the two bands a city camera actually sees, and a works that has
    /// neither reads as a shed with windows drawn on it however carefully the walls are laid.
    /// </summary>
    public static partial class SyntyIndustrialKitBash
    {
        const string Bld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string Props = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";

        public const int Version = 7;
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
        const string BrickWindowB = Bld + "SM_Bld_Wall_Brick_Window_02.prefab";
        const string BrickWindowC = Bld + "SM_Bld_Wall_Brick_Window_03.prefab";
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
        const string Fan = Props + "SM_Prop_Warehouse_Fan_02.prefab";
        const string Vent = Props + "SM_Prop_AirVent_02.prefab";

        // The venting run. Straight, and not for want of trying: the pack's two "corner"
        // pieces both turn UP (Corner_01 climbs 3 m, Corner_02 climbs 1) and no piece in the
        // set extends in z at all, so a duct that turns in PLAN cannot be built from this kit.
        const string Duct = Props + "SM_Prop_Warehouse_Venting_01.prefab";
        const string DuctEnd = Props + "SM_Prop_Warehouse_Venting_End_02.prefab";
        const string DuctCollar = Props + "SM_Prop_Warehouse_Venting_Support_01.prefab";

        // Fittings on the face of a wall. Every one of these is authored facing +Z with what
        // it sticks out sticking out along +z, which is what makes WallProp a yaw per side.
        const string WallLight = Props + "SM_Prop_Warehouse_WallLight_01.prefab";
        const string BracketLight = Props + "SM_Prop_Light_Wall_01.prefab";
        const string WallVent = Props + "SM_Prop_AirVent_01.prefab";
        const string WallFan = Props + "SM_Prop_WallFan_01.prefab";
        const string WallCamera = Props + "SM_Prop_Security_Camera_01.prefab";
        const string PowerBox = Props + "SM_Prop_Warehouse_PowerBox_01.prefab";
        const string Firehose = Props + "SM_Prop_Warehouse_Firehose_01.prefab";
        const string CompanySign = Props + "SM_Prop_CompanySign_01.prefab";
        const string Ladder = Props + "SM_Prop_Ladder_03.prefab";
        const string WallLadder = Props + "SM_Prop_Ladder_01.prefab";
        const string Riser = Bld + "SM_Bld_Piping_01.prefab";
        const string Awning = Bld + "SM_Bld_Exterior_Shade_01.prefab";

        // The gantry on the hall's flank
        const string Walkway = Bld + "SM_Bld_Walkway_Double_01.prefab";
        const string Landing = Bld + "SM_Bld_Walkway_Single_01.prefab";
        const string WalkwayRail = Bld + "SM_Bld_Walkway_Rail_01.prefab";
        const string WalkwayLeg = Bld + "SM_Bld_Walkway_Support_01.prefab";

        // The cross-pack borrows: the chimney block, the rooftop water tower and the hoarding.
        const string ChimneyBlock =
            "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/SM_Bld_Villa_Chimney_01.prefab";
        const string WaterTower =
            "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Prop_Water_Tower_01.prefab";
        const string Hoarding =
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_Billboard_01.prefab";
        const float ChimneyCourse = 2.505f; // block height; base sits 0.126 under the pivot

        // ---------------------------------------------------------------- measured offsets
        //
        // Every number below was measured off an instance rather than reasoned about, because
        // these pieces pivot wherever their author found convenient and half of them are not
        // pivoted on the thing they are attached by.

        /// <summary>How far a wall's outer face stands beyond the half-dimension it is laid
        /// on. A gang wall piece is 0.39 thick about its own plane and hangs 0.197 of that
        /// outside, so a fitting screwed to the front of a building goes at halfD + this and
        /// not at halfD, where it would be half swallowed.</summary>
        const float Face = 0.197f;

        /// <summary>Top of a flat roof slab above the storey it caps: Roof_Flat_Open_01 is
        /// 8 cm of deck. Roof furniture stands on THAT, not on the wall head - the old 0.15
        /// left every fan hovering seven centimetres over its own roof.</summary>
        const float RoofDeck = 0.08f;

        /// <summary>Height of the venting duct's axis above what it stands on: the pieces
        /// pivot at the middle of the duct, not at its underside.</summary>
        const float DuctAxis = 0.651f;

        /// <summary>
        /// What the mirrored end cap has to be nudged in z so its duct lines up with the run.
        ///
        /// Every venting piece spans z -0.672..+0.342 about its pivot - the duct is NOT
        /// centred on it - so a piece turned through 180 degrees puts its duct a third of a
        /// metre the other side of the line. A duct that steps sideways at the end of its own
        /// run is the sort of fault nobody sees until it is pointed out, and then cannot stop
        /// seeing.
        /// </summary>
        const float DuctMirror = -0.330f;   // (-0.672) + (+0.342)

        /// <summary>Fan_02 pivots 7.4 cm under its own base, so it is dropped by that much to
        /// stand on the deck rather than float over it.</summary>
        const float FanSink = 0.074f;

        /// <summary>The hoarding's lowest geometry sits 0.717 above its pivot - it is drawn to
        /// hang off a wall, not to stand on a roof - so standing one on a parapet means
        /// dropping the pivot by exactly that.</summary>
        const float HoardingFoot = 0.717f;

        /// <summary>Which wall a fitting hangs on, named from outside looking at the building.
        /// Front is the +Z face every kit bake is authored to front.</summary>
        enum Side { Front, Right, Back, Left }

        [MenuItem("Tools/City/Catalog/Rebuild Synty Industrial Kit (Kit-Bash)", priority = 3)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        public static void BuildIfStale()
        {
            if (IsFresh()) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play before rebuilding the industrial kit.");

            BuildFactory();
            BuildFactoryOld();
            BuildFactoryHall();
            BuildWorkshop();
            BuildProductionHall("building-production-hall", 12, 6, 2);
            BuildProductionHall("building-process-hall", 16, 9, 3);
            BuildProductionHall("building-distribution-hall", 12, 9, 2);

            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        public static bool IsFresh()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            return marker && marker.text.Trim() == Version.ToString() &&
                new[] { "building-factory", "building-factory-old", "building-factory-hall", "building-workshop",
                    "building-production-hall", "building-process-hall", "building-distribution-hall" }
                .All(name => AssetDatabase.LoadAssetAtPath<GameObject>(SyntyKitExtractor.BuildingsDir + "/" + name + ".prefab"));
        }

        /// <summary>
        /// The brick factory: two storeys, personnel door and a metal slide door in the
        /// front (the demo compound mixes metal doors into brick walls the same way),
        /// window rows above, parapet flat roof with fans and vents.
        /// </summary>
        static void BuildFactory()
        {
            const int w = 8, d = 5;
            const float topY = 2 * Course;
            var root = new GameObject("building-factory");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((BrickWall, 1), (Glazing(0), 1), (BrickDoor, 1), (Glazing(1), 1),
                               (MetalSlide, 1), (Glazing(2), 1), (Glazing(3), 1), (BrickWall, 1)),
                    back: Enumerable.Repeat(BrickWall, w).ToArray(),
                    side: new[] { BrickWall, BrickWall, BrickWindow, BrickWall, BrickWall });
                WallRing(root, w, d, floor: 1,
                    front: Row((BrickWall, 1), (Glazing(4), 1), (Glazing(5), 1), (Glazing(6), 1),
                               (Glazing(7), 1), (Glazing(8), 1), (Glazing(9), 1), (BrickWall, 1)),
                    back: new[] { BrickWall, Glazing(2), Glazing(3), Glazing(4),
                                  Glazing(5), Glazing(6), Glazing(7), BrickWall },
                    side: new[] { BrickWall, Glazing(1), Glazing(2), Glazing(3), BrickWall });
                CornerPosts(root, w, d, floors: 2, BrickCorner);
                FlatRoof(root, w, d, topY);

                // --- the roof, which is what the city camera is actually looking at ---------
                var deck = topY + RoofDeck;
                RoofDuct(root, -9f, 7.5f, -5f, deck);
                FanRow(root, -7.5f, 1.5f, -2.5f, deck, 3f);
                Place(root, Vent, -9f, deck, 1f, 0f);
                Place(root, Vent, -5f, deck, 1f, 0f);
                Place(root, WaterTower, 7f, deck, 0f, 0f);

                // The firm's hoarding over the street, which is the one piece of this block a
                // pedestrian reads a WORD off - and 1987 is the right decade for it.
                Place(root, Hoarding, 6f, deck - HoardingFoot, 6f, 0f);

                // --- the front: office door at -4.5, goods door at +1.5 ---------------------
                WallProp(root, Awning, w, d, Side.Front, -4.5f, Course + 0.05f);
                WallProp(root, WallLight, w, d, Side.Front, 1.5f, Course + 0.25f);
                WallProp(root, CompanySign, w, d, Side.Front, -10.5f, 4.6f, standoff: 0.06f);
                WallProp(root, WallCamera, w, d, Side.Front, 10.5f, 5.6f);

                // The back is blind brick on both storeys, so it takes everything that wants a
                // wall rather than a window: the risers, the roof ladder and the vents.
                WallProp(root, Riser, w, d, Side.Back, 9f, 0f, standoff: 0.129f);
                WallProp(root, Riser, w, d, Side.Back, 9f, Course, standoff: 0.129f);
                WallProp(root, Ladder, w, d, Side.Back, -9f, 0f);
                WallProp(root, Ladder, w, d, Side.Back, -9f, Course);
                WallProp(root, WallVent, w, d, Side.Back, -6f, 2f);
                WallProp(root, WallVent, w, d, Side.Back, 6f, 2f);
                WallProp(root, PowerBox, w, d, Side.Left, 0f, 1.2f, standoff: 0.05f);

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
            const float topY = 2 * Course;
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var root = new GameObject("building-factory-old");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((BrickWall, 1), (Glazing(2), 1), (Glazing(3), 1), (BrickDoor, 1),
                               (Glazing(4), 1), (Glazing(5), 1), (BrickWall, 1)),
                    back: Enumerable.Repeat(BrickWall, w).ToArray(),
                    side: new[] { BrickWall, Glazing(0), Glazing(1), BrickWall });
                WallRing(root, w, d, floor: 1,
                    front: Row((BrickWall, 1), (Glazing(6), 1), (Glazing(7), 1), (Glazing(8), 1),
                               (Glazing(9), 1), (Glazing(10), 1), (BrickWall, 1)),
                    back: new[] { BrickWall, BrickWall, Glazing(1), Glazing(2),
                                  Glazing(3), BrickWall, BrickWall },
                    side: new[] { BrickWall, Glazing(4), Glazing(5), BrickWall });
                CornerPosts(root, w, d, floors: 2, BrickCorner);
                FlatRoof(root, w, d, topY);

                // --- the roof: two parallel runs, because the one thing this kit cannot do is
                // turn a duct in plan, and a works roof with a single pipe on it reads as bare
                var deck = topY + RoofDeck;
                RoofDuct(root, -7.5f, 7.5f, -3.5f, deck);
                RoofDuct(root, -4.5f, 4.5f, 3f, deck);
                FanRow(root, -6f, 3f, -1f, deck, 3f);
                Place(root, Vent, -4.5f, deck, 2f, 0f);
                Place(root, Vent, 1.5f, deck, 2f, 0f);

                // --- the front: the door is dead centre on this one ------------------------
                WallProp(root, Awning, w, d, Side.Front, 0f, Course + 0.05f);
                WallProp(root, WallLight, w, d, Side.Front, -6f, Course + 0.25f);
                WallProp(root, WallLight, w, d, Side.Front, 6f, Course + 0.25f);
                WallProp(root, CompanySign, w, d, Side.Front, -9f, 4.6f, standoff: 0.06f);
                WallProp(root, WallCamera, w, d, Side.Front, 9f, 5.6f);

                // The back carries windows across its middle upstairs, so the riser and the
                // ladder take the two blind columns at either end of it.
                WallProp(root, Riser, w, d, Side.Back, -9f, 0f, standoff: 0.129f);
                WallProp(root, Riser, w, d, Side.Back, -9f, Course, standoff: 0.129f);
                WallProp(root, Ladder, w, d, Side.Back, 9f, 0f);
                WallProp(root, Ladder, w, d, Side.Back, 9f, Course);
                WallProp(root, WallVent, w, d, Side.Back, -3f, 2f);
                WallProp(root, WallVent, w, d, Side.Back, 3f, 2f);
                WallProp(root, PowerBox, w, d, Side.Right, -4.5f, 1.2f, standoff: 0.05f);

                // The compound supplies its actual boiler stack. Repeating capped domestic
                // chimney modules here made a second tower of disconnected chimney pots.

                BakeWithSigns(root, ("TRADE OFFICE", new Vector3(9f, 4.5f, d * M / 2f + Face + 0.03f), 2.4f));
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

                // --- the front: two 6 m roller doors with a personnel door between them -----
                //
                // No roof furniture on this one, and that is a measurement rather than an
                // oversight: SM_Bld_Roof_01 rises to 1.88 over a curved rib, so anything stood
                // on it at a single height is either buried in the sheeting or hanging off it.
                WallProp(root, WallLight, w, d, Side.Front, 0f, Course);
                WallProp(root, BracketLight, w, d, Side.Front, -7.9f, 4.5f);
                WallProp(root, BracketLight, w, d, Side.Front, 7.9f, 4.5f);
                WallProp(root, CompanySign, w, d, Side.Front, -9f, 4.5f, standoff: 0.06f);
                WallProp(root, WallCamera, w, d, Side.Front, 10f, 5.6f);
                WallProp(root, WallFan, w, d, Side.Right, -3f, 4.5f);
                WallProp(root, WallFan, w, d, Side.Left, -3f, 4.5f);
                WallProp(root, Firehose, w, d, Side.Back, 7f, 0f);

                Gantry(root, w, d, deckY: Course);

                BakeWithSigns(root, ("METALWORK", new Vector3(9f, 3.4f, d * M / 2f + Face + 0.03f), 2.4f));
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
            const float topY = Course;
            var root = new GameObject("building-workshop");
            try
            {
                WallRing(root, w, d, floor: 0,
                    front: Row((MetalWall, 1), (MetalSlide, 1), (MetalDoor, 1),
                               (MetalWindow, 1), (MetalWall, 1)),
                    back: new[] { MetalWall, MetalWall, MetalWindow, MetalWall, MetalWall },
                    side: new[] { MetalWall, MetalWindow, MetalWindow, MetalWall });
                CornerPosts(root, w, d, floors: 1, MetalCorner);
                FlatRoof(root, w, d, topY);

                var deck = topY + RoofDeck;
                RoofDuct(root, -4.5f, 4.5f, -3f, deck);
                FanRow(root, -4.5f, 1.5f, 0f, deck, 3f);
                Place(root, Vent, 0f, deck, 3f, 0f);

                // --- the front: roller door at -3, personnel door at 0 ---------------------
                WallProp(root, Awning, w, d, Side.Front, 0f, 2.45f);
                WallProp(root, WallLight, w, d, Side.Front, -6f, 2.5f);
                WallProp(root, CompanySign, w, d, Side.Front, 6f, 1.9f, standoff: 0.06f);
                WallProp(root, WallCamera, w, d, Side.Front, 7f, 2.7f);
                WallProp(root, WallVent, w, d, Side.Back, -4.5f, 1.8f);
                WallProp(root, Ladder, w, d, Side.Back, 4.5f, 0f);
                WallProp(root, Firehose, w, d, Side.Right, -4.5f, 0f);
                WallProp(root, PowerBox, w, d, Side.Left, -4.5f, 1.2f, standoff: 0.05f);

                BakeWithSigns(root, ("SERVICE", new Vector3(-6f, 1.55f, d * M / 2f + Face + 0.03f), 2.4f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // BakeGroup recenters the mesh on all geometry, including projecting fittings.
        // Carry facade mounts through that same pivot shift, keeping blank wall positions.
        static void BakeWithSigns(GameObject root, params (string label, Vector3 centre, float width)[] signs)
        {
            var pivot = SyntyKitExtractor.BakeGroup(root, root.name, yaw: 0f);
            if (!pivot.HasValue) return;
            string path = SyntyKitExtractor.BuildingsDir + "/" + root.name + ".prefab";
            var output = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var wall = output.AddComponent<MeshCollider>();
                wall.sharedMesh = output.GetComponent<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();
                foreach (var sign in signs)
                {
                    var centre = sign.centre - pivot.Value;
                    float face = float.NegativeInfinity;
                    // Sample the actual wall ribs. Face also allows for bulky fittings;
                    // using it unchanged would leave the board several cm off the sheet.
                    for (float x = -sign.width * 0.5f; x <= sign.width * 0.5f; x += 0.08f)
                        foreach (float y in new[] { -0.3f, 0f, 0.3f })
                        {
                            var ray = new Ray(centre + new Vector3(x, y, 0.5f), Vector3.back);
                            if (!wall.Raycast(ray, out var hit, 1f))
                                throw new System.InvalidOperationException(root.name + ": unsupported sign " + sign.label);
                            face = Mathf.Max(face, hit.point.z);
                        }
                    centre.z = face + 0.03f;
                    var mount = new GameObject("sign mount " + sign.label).transform;
                    mount.SetParent(output.transform, false);
                    mount.localPosition = centre;
                    mount.localScale = new Vector3(sign.width, 0.7f, 1f);
                }
                Object.DestroyImmediate(wall);
                PrefabUtility.SaveAsPrefabAsset(output, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(output); }
        }

        static (string path, int modules)[] Row(params (string path, int modules)[] row) => row;

        // ------------------------------------------------------------------ the dressing

        /// <summary>
        /// A brick window that is not the same brick window every time.
        ///
        /// The pack draws three of them and the kit was using one, which is what makes a long
        /// elevation read as wallpaper - the pack's own demo spends 11, 10 and 10 on the three,
        /// so they are peers rather than a default and two oddities. Dealt from the column
        /// index and not from a random: a kit bake has to come out identical every run, or the
        /// blocks baked off it stop matching the ones already on disk.
        /// </summary>
        static string Glazing(int column) => (column % 3) switch
        {
            0 => BrickWindow,
            1 => BrickWindowB,
            _ => BrickWindowC,
        };

        /// <summary>
        /// Hangs a fitting on the OUTSIDE of a wall.
        ///
        /// <paramref name="along"/> is the world coordinate along that face - x on the front
        /// and back, z on the flanks - rather than a distance from one end, because every
        /// elevation here is already reasoned about in the same frame the wall rows are laid
        /// in, and a second convention is a second thing to get backwards.
        ///
        /// <paramref name="y"/> is the piece's own pivot height, which for this pack is as
        /// often its middle as its underside; the callers carry the measured number.
        /// <paramref name="standoff"/> is for the few fittings centred on their own depth - a
        /// pipe riser, a sign - which would otherwise sit half inside the brick.
        /// </summary>
        static void WallProp(GameObject root, string path, int w, int d, Side side,
                             float along, float y, float standoff = 0f)
        {
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var front = halfD + Face + standoff;
            var flank = halfW + Face + standoff;

            switch (side)
            {
                case Side.Front: Place(root, path, along, y, front, 0f); break;
                case Side.Right: Place(root, path, flank, y, along, 90f); break;
                case Side.Back: Place(root, path, along, y, -front, 180f); break;
                default: Place(root, path, -flank, y, along, 270f); break;
            }
        }

        /// <summary>
        /// A straight venting run across a flat roof, from <paramref name="x0"/> to
        /// <paramref name="x1"/> at <paramref name="z"/>.
        ///
        /// Straight because straight is all this kit can do - see the note on Duct - and
        /// because a run is what a roof duct mostly is anyway. A segment pivots at its low-x
        /// end and reaches 3 m the other way with an 8 cm flange behind the pivot, so
        /// consecutive segments laid a module apart overlap at the flange exactly as drawn.
        ///
        /// <paramref name="x1"/> is where the last WHOLE segment may end, not where the duct
        /// does: the cap that finishes it reaches 1.73 m further. Every caller here is sized
        /// with that in hand and keeps clear of its own parapet, which sits 0.2 m inside the
        /// roof edge.
        /// </summary>
        static void RoofDuct(GameObject root, float x0, float x1, float z, float deckY)
        {
            if (x1 - x0 < M)
                return;

            var y = deckY + DuctAxis;

            // The low end is the SAME cap turned round, nudged by DuctMirror so its duct stays
            // on the line rather than stepping a third of a metre off it.
            Place(root, DuctEnd, x0, y, z + DuctMirror, 180f);

            var cursor = x0;
            for (var run = 0; cursor + M <= x1 + 0.01f; run++, cursor += M)
            {
                Place(root, Duct, cursor, y, z, 0f);

                // A collar at every other joint. On every joint they read as a concertina;
                // on none of them a 15 m duct has nothing holding it up.
                if (run % 2 == 1)
                    Place(root, DuctCollar, cursor, y, z, 0f);
            }

            Place(root, DuctEnd, cursor, y, z, 0f);
        }

        /// <summary>A rank of extract fans, which is how the pack's own artists cover a roof -
        /// their demo compound stands forty-five of them and no other single prop comes
        /// close.</summary>
        static void FanRow(GameObject root, float x0, float x1, float z, float deckY, float pitch)
        {
            for (var x = x0; x <= x1 + 0.01f; x += pitch)
                Place(root, Fan, x, deckY - FanSink, z, 0f);
        }

        /// <summary>
        /// The maintenance gantry down the hall's right flank: two bays of walkway with a
        /// landing at the near end, a rail along the outer edge, legs to the ground and a
        /// ladder up the wall to the landing.
        ///
        /// It is here rather than in the yard dressing because it belongs to the BUILDING -
        /// baked in, it costs the block nothing and it cannot drift off the wall it is bolted
        /// to. The pieces pivot at a far corner and mirror in x (walkway local x runs -3..0,
        /// local z -1.5..0), so the whole run is laid at yaw 270, where local +x reads out
        /// along world +z and local +z reads out along world +x, away from the wall.
        /// </summary>
        static void Gantry(GameObject root, int w, int d, float deckY)
        {
            float halfW = w * M / 2f, halfD = d * M / 2f;
            var wall = halfW + Face;          // the face the gantry is bolted to
            var edge = wall + 1.5f;           // its outer edge, one walkway deep

            // Two full bays off the far end, then the landing, which leaves the last 1.5 m of
            // flank clear for the ladder to climb.
            Place(root, Walkway, wall, deckY, halfD, 270f);
            Place(root, Walkway, wall, deckY, halfD - M, 270f);
            Place(root, Landing, wall, deckY, halfD - 2f * M, 270f);

            Place(root, WalkwayRail, edge, deckY, halfD, 270f);
            Place(root, WalkwayRail, edge, deckY, halfD - M, 270f);

            // Legs land 0.04 into the underside of the deck, which is what stops a hairline of
            // daylight showing between a leg and the thing it is holding up.
            for (var k = 0; k < 3; k++)
                Place(root, WalkwayLeg, edge - 0.3f, 0f, halfD - 1.5f - k * M, 0f);

            WallProp(root, WallLadder, w, d, Side.Right, halfD - 2.5f * M, 0f);
        }

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
