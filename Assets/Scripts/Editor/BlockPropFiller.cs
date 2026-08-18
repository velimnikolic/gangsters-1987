using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Dresses the FREE SPACE of a block being composed on a lot pad: service clutter in
    /// rows against the buildings, and a perimeter of lamps, trees, seating and hedge
    /// along the pad's own edges.
    ///
    /// Anything with a right way to stand is laid on a LINE. A yard is read as a set of
    /// straight runs - each stretch of building wall the free ground meets, and each
    /// stretch of the pad's own edge - and bins, benches, lamps, trees and hedge stand
    /// against one of them: flush at a fixed setback, turned to face the yard, advanced
    /// along the run by their own measured width. That is the difference between a row of
    /// bins and a heap of them, and it is how those things actually stand in a yard: with
    /// their backs to something, in line with each other.
    ///
    /// Each pad edge gets ONE job - the lamp-and-tree avenue, the street furniture, the
    /// power run, the hedge, the seating, the tree row - so two species never fight over
    /// the same frontage, and the longest edge takes the avenue.
    ///
    /// A wall is not only something to stand a bin against. Half of what a city block
    /// wears hangs ON it, and none of it used to be placed: a hoarding and the ad pasted
    /// over it, a shop's projecting sign, a neon, an aircon unit, a satellite dish, a
    /// drainpipe, a washing line, vines. Those go up on the same wall runs the bins stand
    /// against, each family at its own height, and only where the building behind the run
    /// can carry them - the pass measures how tall whatever stands on the other side of
    /// the face is, and refuses a sign that would finish above its own roof. A hoarding
    /// goes up blank, so it is stood together with one of the pack's ad panels at the very
    /// same transform, which is how the pack's own demo scene assembles them.
    ///
    /// The frontage gets what a street has rather than what a courtyard has: a bus
    /// shelter, a mailbox, a hydrant, a payphone, a news stand, a hoarding - facing OUT,
    /// backs to the block, because the street is the side they are used from.
    ///
    /// And one edge carries the power, which is the most 1987 thing a block can wear:
    /// poles at a road pitch with the cables strung between them, three rows across the
    /// crossarm. A cable is authored as one 7.7 m span running out of its own pivot, so
    /// it is stretched along its run to the gap it has to cross and hung at the crossarm -
    /// which is exactly what the PalmCity demo scene does with the same pieces (poles
    /// 22 m apart, cables scaled 2.6x and sitting at 8.34 on an 8.88 m pole).
    ///
    /// What has no right way to stand is still dropped loose, and deliberately: bags,
    /// spilled cardboard, a pallet, a bush. Nobody lines those up, and a yard where
    /// everything is square reads as a showroom. They go down last, mostly against
    /// whatever is already standing, at whatever angle they land - a handful per yard,
    /// not a carpet.
    ///
    /// This is an authoring aid in the catalog scene, not city generation: the result
    /// stands in the scene, the user throws out what they dislike, and the capture pass
    /// writes the survivors into the block's recipe - so what ships is still a
    /// predefined bake, which is the rule for block interiors. Nothing here runs at
    /// play time and RoadDemoBuilder still dresses no courtyards of its own.
    ///
    /// Run it again for a different arrangement - the previous auto pass is removed
    /// first, and hand-placed props are left alone.
    ///
    /// This pass lays NO parking. Cars in a yard are a decision per block, not something
    /// every dressing run should hand out, so they have a command of their own
    /// (BlockParkingBay). Run the parking first and the rows lay themselves around it: the
    /// whole LOT counts here as an obstacle, its aisle and its entrance drive included,
    /// because empty lanes are what a car park is made of and a bin standing in one is the
    /// same mistake as a bin standing in a bay.
    /// </summary>
    public static class BlockPropFiller
    {
        /// <summary>Everything auto-placed lands under this one child, so a re-roll
        /// cannot touch a prop the user placed or moved by hand.</summary>
        internal const string AutoRoot = BlockPad.PropsRoot;

        /// <summary>
        /// The occupancy grid's resolution. Half a metre, not a metre: a building is
        /// rounded OUT to whole cells, so the wall line a row is laid against sits up to
        /// one cell proud of the real wall - and a row of bins standing a metre off the
        /// building it belongs to is exactly the "why is this floating" look this pass is
        /// here to stop. A pad is still only a few tens of thousands of cells.
        /// </summary>
        const float Cell = 0.5f;

        // Breathing room between two placed props. A row is meant to touch, so it asks
        // for almost none; junk dropped in the open keeps its distance.
        const float RowGap = 0.05f;
        const float DropGap = 0.25f;

        // How much a yard gets. The pads are 70-100 m by 50-95 m, so a block with a
        // courtyard has thousands of square metres of free ground: at the old 45 m2 a
        // prop the CAP was what decided the count on every pad worth dressing, and 60
        // props over half a hectare is a bare lot with a few bins in one corner. The
        // budget is what the rows spend; the facade and the cables have their own.
        const float AreaPerProp = 34f; // one prop per this much free ground
        const float LooseArea = 70f;   // and one dropped piece of junk per this much
        const int MaxProps = 160;

        // How a row sits on its line.
        const float WallSetback = 0.15f;  // bins stand ON the wall, not near it
        const float EdgeSetback = 0.7f;   // the perimeter keeps off the kerb
        const float Snug = 0.12f;         // between two pieces of one cluster
        const float Margin = 0.8f;        // clear of the corner a run starts at

        // A run shorter than this is a nook, not a frontage.
        const float MinWallRun = 2.5f;
        const float MinBorderRun = 8f;
        const int MaxRunPieces = 24;      // one hedge may not eat the whole budget

        // How far apart a repeated row stands. Lamps light a yard rather than line it,
        // so they are the sparse one.
        const float LampPitch = 13f;
        const float TreePitch = 6f;
        const float SeatPitch = 5f;

        // ---- what hangs on the walls
        //
        // A mounted thing is screwed to the wall, and the grid cannot tell it where that
        // is: a building is rounded OUT to whole cells, so the face a row is laid against
        // stands anywhere from nothing to half a metre proud of the brick. A bin hides
        // that and a flat sign cannot - a poster floating half a metre off a wall is the
        // one failure this pass would be judged on. So a mount asks the OCCUPANT for its
        // own edge (see Yard.Wall) and bites three centimetres into it; the fallback is
        // the middle of the rounding error, for the case where no occupant owns the face.
        const float FacadeBite = -0.03f;
        const float FacadeSetback = -0.25f;
        const float FacadeGap = 1.1f;        // along the wall, between two mounted things

        // What a mounted thing may hang past the pad, and the ONE place this toolchain
        // lets anything do that: a block must stay inside its lot because it must not
        // overhang the pavement, and a shop sign screwed to a street facade overhangs the
        // pavement by definition - that is what the bracket is for. A metre buys the
        // pack's deepest hoarding (0.9 m of board and frame) and nothing bigger, and it is
        // spent three metres up where the pavement is empty.
        const float FacadeOverhang = 1f;

        const float FacadeHead = 0.5f;       // a sign finishes this far under the roof
        const float WallPerFacade = 10f;     // one mounted thing per this much wall
        const int MaxFacade = 36;

        // ---- the power run
        //
        // Poles at a road pitch rather than the cable's own 7.7 m: the pack authors one
        // short span and stretches it, and the demo scene stands its poles 22 m apart.
        const float PolePitch = 22f;
        const float MaxStretch = 6f;         // past this the sag reads as a washing line
        const float CableDrop = 0.55f;       // below the pole's head, where the arm is
        const float CableSpread = 0.8f;      // of the crossarm's half width, per cable row
        static readonly float[] CableRows = { -1f, 0f, 1f };

        const string PalmProp = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string CityProp = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        /// <summary>What a prop is FOR, which is what decides the line it stands on and
        /// how the row is spaced - not where it happens to fit. Loose is the exception
        /// that proves it: the things nobody ever put down straight.</summary>
        enum Kind { Service, Seat, Green, Lamp, Edge, Loose, Frontage, Pole }

        /// <summary>The props a block interior is allowed to grow. Anything a pack does
        /// not ship is skipped quietly at load.</summary>
        static readonly (string path, Kind kind)[] Palette =
        {
            // --- back of house, in a row against the wall: the things a yard stands
            //     somewhere on purpose, because they are emptied, opened or plugged in
            (PalmProp + "SM_Prop_Trash_Bin_01", Kind.Service),
            (PalmProp + "SM_Prop_Trash_Bin_02", Kind.Service),
            (PalmProp + "SM_Prop_Trash_Bin_03", Kind.Service),
            (PalmProp + "SM_Prop_Trash_Bin_04", Kind.Service),
            (PalmProp + "SM_Prop_Crate_01", Kind.Service),
            (PalmProp + "SM_Prop_Powerbox_01", Kind.Service),
            (PalmProp + "SM_Prop_PowerBox_02", Kind.Service),
            (PalmProp + "SM_Prop_PowerBox_03", Kind.Service),
            (CityProp + "SM_Prop_TrashCan_01", Kind.Service),
            (CityProp + "SM_Prop_Trashbin_01", Kind.Service),
            (CityProp + "SM_Prop_Trashbin_02", Kind.Service),
            (CityProp + "SM_Prop_Skip_01", Kind.Service),
            (CityProp + "SM_Prop_Skip_02", Kind.Service),
            (CityProp + "SM_Prop_PowerBox_01", Kind.Service),
            (PalmProp + "SM_Prop_PowerBox_04", Kind.Service),
            (PalmProp + "SM_Prop_PowerBoxes_01", Kind.Service),
            (PalmProp + "SM_Prop_PowerBoxes_02", Kind.Service),
            (PalmProp + "SM_Prop_PowerBoxes_03", Kind.Service),
            (PalmProp + "SM_Prop_Barrel_Burn_01", Kind.Service),
            (PalmProp + "SM_Prop_Drinks_Cooler_01", Kind.Service),
            (PalmProp + "SM_Prop_Barrier_01", Kind.Service),
            (PalmProp + "SM_Prop_Barrier_02", Kind.Service),
            (CityProp + "SM_Prop_Barrier_01", Kind.Service),

            // --- dropped, not placed: nobody squares up a bin bag
            (PalmProp + "SM_Prop_Trash_Bag_01", Kind.Loose),
            (PalmProp + "SM_Prop_Trash_Bag_Open_01", Kind.Loose),
            (PalmProp + "SM_Prop_RubbishPile_01", Kind.Loose),
            (PalmProp + "SM_Prop_Junk_Cardboard_01", Kind.Loose),
            (PalmProp + "SM_Prop_Pizza_Box_01", Kind.Loose),
            (PalmProp + "SM_Prop_Package_01", Kind.Loose),
            (PalmProp + "SM_Prop_Package_02", Kind.Loose),
            (PalmProp + "SM_Prop_Bucket_01", Kind.Loose),
            (PalmProp + "SM_Prop_Magazine_01", Kind.Loose),
            (PalmProp + "SM_Prop_Magazine_02", Kind.Loose),
            (PalmProp + "SM_Prop_Drink_Can_Crushed_01", Kind.Loose),
            (PalmProp + "SM_Prop_Spray_Can_01", Kind.Loose),
            (PalmProp + "SM_Prop_Cone_01", Kind.Loose),
            (PalmProp + "SM_Prop_Cone_02", Kind.Loose),
            (CityProp + "SM_Prop_TrashBag_01", Kind.Loose),
            (CityProp + "SM_Prop_TrashBag_02", Kind.Loose),
            (CityProp + "SM_Prop_TrashBag_03", Kind.Loose),
            (CityProp + "SM_Prop_CardboardBox_01", Kind.Loose),
            (CityProp + "SM_Prop_CardboardBox_02", Kind.Loose),
            (CityProp + "SM_Prop_CardboardBox_03", Kind.Loose),
            (CityProp + "SM_Prop_CardboardBox_04", Kind.Loose),
            (CityProp + "SM_Prop_Pallet_01", Kind.Loose),
            (CityProp + "SM_Prop_Newspaper_01", Kind.Loose),
            (CityProp + "SM_Prop_Paper_01", Kind.Loose),
            (CityProp + "SM_Prop_Paper_03", Kind.Loose),
            (CityProp + "SM_Prop_Paper_05", Kind.Loose),
            (CityProp + "SM_Prop_Soda_01", Kind.Loose),
            (CityProp + "SM_Prop_Soda_03", Kind.Loose),
            (CityProp + "SM_Prop_Cone_01", Kind.Loose),
            (CityProp + "SM_Prop_Cone_02", Kind.Loose),
            (PalmEnv + "SM_Env_Bush_01", Kind.Loose),
            (PalmEnv + "SM_Env_Bush_02", Kind.Loose),
            (PalmEnv + "SM_Env_Bush_03", Kind.Loose),
            (PalmEnv + "SM_Env_Leaves_01", Kind.Loose),
            (PalmEnv + "SM_Env_Leaves_02", Kind.Loose),

            // --- the yard's own light. One species per row, so a courtyard is lit by a
            //     set of lamps rather than by a sample of the pack.
            (PalmProp + "SM_Prop_Street_Lamp_08", Kind.Lamp),   // the short park post
            (PalmProp + "SM_Prop_Pier_Lamp_01", Kind.Lamp),
            (PalmProp + "SM_Prop_Pier_Lamp_02", Kind.Lamp),

            // --- seating: benches face the yard, tables stand square to it
            (PalmProp + "SM_Prop_Bench_Seat_01", Kind.Seat),
            (PalmProp + "SM_Prop_Bench_Seat_02", Kind.Seat),
            (PalmProp + "SM_Prop_Planter_Bench_01", Kind.Seat),
            (PalmProp + "SM_Prop_Table_Outdoor_01", Kind.Seat),
            (PalmProp + "SM_Prop_Bike_Stand_02", Kind.Seat),
            (CityProp + "SM_Prop_ParkBench_01", Kind.Seat),
            (CityProp + "SM_Prop_PicnicTable_01", Kind.Seat),

            // --- greenery, planted as a row along a frontage
            (PalmEnv + "SM_Env_Tree_Palm_01", Kind.Green),
            (PalmEnv + "SM_Env_Tree_Palm_03", Kind.Green),
            (PalmEnv + "SM_Env_Tree_Palm_04", Kind.Green),
            (PalmEnv + "SM_Env_Tree_Palm_Small_01", Kind.Green),
            (PalmEnv + "SM_Env_Tree_Palm_Sapling_02", Kind.Green),
            (CityEnv + "SM_Env_Tree_01", Kind.Green),
            (CityEnv + "SM_Env_Tree_02", Kind.Green),
            (CityEnv + "SM_Env_Tree_03", Kind.Green),
            (PalmProp + "SM_Prop_Planter_03", Kind.Green),
            (PalmProp + "SM_Prop_Planter_04", Kind.Green),
            (CityProp + "SM_Prop_PotPlant_01", Kind.Green),
            (CityProp + "SM_Prop_PotPlant_02", Kind.Green),

            // --- what closes a yard off from the street: laid piece to piece
            (PalmEnv + "SM_Env_Hedge_04", Kind.Edge),
            (PalmEnv + "SM_Env_Hedge_05", Kind.Edge),
            (CityEnv + "SM_Env_Fence_01", Kind.Edge),
            (PalmProp + "SM_Prop_Bollard_02", Kind.Edge),

            // --- the frontage: what a STREET puts against a block, standing at the kerb
            //     with its back to the yard. None of it is courtyard furniture and none of
            //     it belongs in a service row - a mailbox is used from the pavement.
            //
            //     No parking meter on this list, deliberately. BlockFloorFiller reads a
            //     Parking_Meter as a piece of the parking pass's own lot and asphalts a
            //     ring around it, so a meter stood here as street furniture would lay a
            //     car park down the frontage of a block that has none.
            (CityProp + "SM_Prop_BusStop_01", Kind.Frontage),
            (CityProp + "SM_Prop_Hydrant_01", Kind.Frontage),
            (CityProp + "SM_Prop_Mailbox_01", Kind.Frontage),
            (CityProp + "SM_Prop_Phones_01", Kind.Frontage),
            (CityProp + "SM_Prop_Newspaper_02", Kind.Frontage),   // the vending box
            (CityProp + "SM_Prop_ATM_01", Kind.Frontage),
            (CityProp + "SM_Prop_HotdogStand_01", Kind.Frontage),
            (CityProp + "SM_Prop_Sign_Bustop_01", Kind.Frontage),
            (CityProp + "SM_Prop_Sign_Street_01", Kind.Frontage),
            (CityProp + "SM_Prop_Sign_Street_02", Kind.Frontage),
            (CityProp + "SM_Prop_SidewalkPoles_01", Kind.Frontage),
            (CityProp + "SM_Prop_Billboard_Roof_01", Kind.Frontage),  // hoarding on legs
            (PalmProp + "SM_Prop_Fire_Hydrant_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Mailbox_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Pay_Phone_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Taxi_Stand_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Newspaper_Stand_01", Kind.Frontage),
            (PalmProp + "SM_Prop_ATM_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Street_Sign_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Street_Sign_02", Kind.Frontage),
            (PalmProp + "SM_Prop_Bike_Stand_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Bollard_01", Kind.Frontage),
            (PalmProp + "SM_Prop_Bollard_03", Kind.Frontage),
            (PalmProp + "SM_Prop_Tube_Man_01", Kind.Frontage),

            // --- what the cables hang off
            (PalmProp + "SM_Prop_Powerpole_01", Kind.Pole),
            (PalmProp + "SM_Prop_Powerpole_02", Kind.Pole),
            (PalmProp + "SM_Prop_Powerpole_03", Kind.Pole),
        };

        /// <summary>
        /// The cable spans, in the order they are tried. Each is authored running out of
        /// its own pivot along local +Z for 7.7 m and hanging below it, so one piece is
        /// one row of wire between two poles - stretched along Z to the gap it has to
        /// cross, which is what the pack's demo scene does with them.
        ///
        /// The Bundle_XX pieces of the same family are NOT here: they measure a metre
        /// across and are the coil of slack that sits ON a pole, not a span. Anything that
        /// does not measure like a span is skipped at load anyway - see PowerRun.
        /// </summary>
        static readonly string[] Cables =
        {
            PalmProp + "SM_Prop_Powerline_03",
            PalmProp + "SM_Prop_Powerline_05",
            PalmProp + "SM_Prop_Powerline_06",
            PalmProp + "SM_Prop_Powerline_04",
            PalmProp + "SM_Prop_Powerline_07",
            PalmProp + "SM_Prop_Powerline_02",
            PalmProp + "SM_Prop_Powerline_01",
            PalmProp + "SM_Prop_Powerline_08",
        };

        /// <summary>
        /// A hoarding is authored blank: the board is one prefab and the ad pasted on it is
        /// another, and the pack's demo scene stands the two at the very same transform
        /// (every Billboard_Sign instance in PolygonCity/Demo sits at a local zero under
        /// its board). So a board placed on its own reads as a grey panel, which is what
        /// makes this list part of placing one rather than a decoration on top.
        /// </summary>
        static readonly string[] AdFaces =
        {
            CityProp + "SM_Prop_Billboard_Sign_01",
            CityProp + "SM_Prop_Billboard_Sign_02",
            CityProp + "SM_Prop_Billboard_Sign_03",
            CityProp + "SM_Prop_Billboard_Sign_04",
            CityProp + "SM_Prop_Billboard_Sign_05",
            CityProp + "SM_Prop_Billboard_Sign_06",
            CityProp + "SM_Prop_Billboard_Sign_07",
        };

        /// <summary>The two boards those faces belong on - one for a wall, one on legs.</summary>
        static readonly string[] Hoardings =
        {
            CityProp + "SM_Prop_Billboard_01",
            CityProp + "SM_Prop_Billboard_Roof_01",
        };

        /// <summary>
        /// What hangs on a WALL, and how high off the ground its underside sits.
        ///
        /// The band is the whole spacing rule for these: nothing is laid out along a
        /// facade, it is put where the thing is used from - a poster at eye level, a shop
        /// sign over the door, an aircon unit above head height, a dish and a hoarding up
        /// where the street can see them. Read off the pack's own geometry: every one of
        /// these is thin in local Z and grows out of a pivot ON that plane, which is the
        /// wall it is meant to be screwed to.
        /// </summary>
        static readonly (string path, float low, float high)[] Facade =
        {
            // the hoarding, and the demo scene's own height for one (5.5 m up a wall)
            (CityProp + "SM_Prop_Billboard_01", 4.5f, 8.5f),

            // the shop's own signs, over its door
            (CityProp + "SM_Prop_Sign_Bar_01", 2.8f, 4.2f),
            (CityProp + "SM_Prop_Sign_Barber_01", 2.6f, 3.8f),
            (CityProp + "SM_Prop_Sign_Cafe_01", 2.8f, 4.2f),
            (CityProp + "SM_Prop_Sign_Pizza_01", 2.8f, 4.4f),
            (CityProp + "SM_Prop_Sign_DeliPizza_01", 2.8f, 4.6f),
            (CityProp + "SM_Prop_Sign_Pub_01", 2.8f, 4.2f),
            (CityProp + "SM_Prop_Sign_Chinese_Noodles_01", 2.8f, 4.2f),
            (CityProp + "SM_Prop_Sign_Hotel_01", 3.5f, 6.5f),
            (CityProp + "SM_Prop_Sign_XXX_01", 2.8f, 4.4f),
            (PalmProp + "SM_Prop_Sign_Above_Board_01", 2.8f, 3.8f),
            (PalmProp + "SM_Prop_Sign_Neon_01", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_02", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_03", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_04", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_05", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_06", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_07", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_08", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_09", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_10", 2.8f, 5.5f),
            (PalmProp + "SM_Prop_Sign_Neon_11", 2.8f, 5.5f),

            // paper, at the height a hand puts it up
            (CityProp + "SM_Prop_Poster_01", 1.3f, 2.4f),
            (CityProp + "SM_Prop_Poster_02", 1.3f, 2.4f),
            (CityProp + "SM_Prop_Poster_03", 1.3f, 2.4f),
            (CityProp + "SM_Prop_Poster_Frame_01", 1.2f, 2.2f),
            (PalmProp + "SM_Prop_Poster_Frame_01", 1.2f, 2.2f),
            (PalmProp + "SM_Prop_Sign_Poster_01", 1.3f, 2.4f),
            (PalmProp + "SM_Prop_Sign_Poster_02", 1.3f, 2.4f),
            (PalmProp + "SM_Prop_Sign_Poster_03", 1.3f, 2.4f),

            // the back of house, which is the half of a facade nobody draws and everybody
            // recognises: plant, ducting, a dish, and the washing that says it is lived in
            (CityProp + "SM_Prop_Aircon_01", 2.2f, 4.5f),
            (CityProp + "SM_Prop_SatDish_01", 4f, 7.5f),
            (PalmProp + "SM_Prop_Satelite_Dish_01", 4f, 7f),
            (CityProp + "SM_Prop_SecurityCamera_01", 3.2f, 4.2f),
            (PalmProp + "SM_Prop_Security_Camera_01", 3.2f, 4.2f),
            (PalmProp + "SM_Prop_Security_Camera_02", 3.2f, 4.2f),
            (CityProp + "SM_Prop_Pipe_Small_01", 0f, 0f),      // drainpipe, off the ground
            (CityProp + "SM_Prop_Pipe_Small_02", 0f, 0f),
            (CityProp + "SM_Prop_Washingline_01", 3f, 6f),
            (CityProp + "SM_Prop_Washingline_02", 3f, 6f),
            (CityProp + "SM_Prop_Washingline_03", 3f, 6f),
            (CityProp + "SM_Prop_Washingline_04", 3f, 6f),

            // the wall's own light, and what grows up it
            (PalmProp + "SM_Prop_Light_Wall_01", 2.8f, 4.2f),
            (PalmProp + "SM_Prop_Light_Wall_02", 2.8f, 4.2f),
            (PalmProp + "SM_Prop_Light_Wall_03", 2.8f, 4.2f),
            (PalmProp + "SM_Prop_Street_Lamp_04", 3f, 4.4f),
            (PalmProp + "SM_Prop_Street_Lamp_05", 3f, 4.4f),
            (PalmProp + "SM_Prop_Street_Lamp_06", 3f, 4.4f),
            (PalmEnv + "SM_Env_Vines_01", 0f, 0.4f),
            (PalmEnv + "SM_Env_Vines_02", 0f, 0.4f),
            (PalmEnv + "SM_Env_Vines_03", 0f, 0.4f),
            (PalmEnv + "SM_Env_Vines_04", 0f, 0.4f),
            (PalmEnv + "SM_Env_Vines_05", 0f, 0.4f),
        };

        [MenuItem("Tools/City/Catalog/Fill Block Free Space With Props", priority = 60)]
        public static void Fill()
        {
            if (!BlockLotCapture.OpenCatalogScene())
                return;

            if (!TryTarget(out var target))
                return;

            var placed = Dress(target, out var story);
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = target.parent.gameObject;

            Debug.Log($"[Props] {placed} props dressed the free space of {target.label} " +
                      $"({target.width:F0} x {target.depth:F0} m). {story}\n" +
                      $"They stand under \"{AutoRoot}\" - delete what you do not want, then run " +
                      "Tools/City/Catalog/Capture Blocks From Lot Pads to save the block.");
            if (placed == 0)
                EditorUtility.DisplayDialog(
                    "Nothing placed",
                    $"{target.label} has no free ground left, or none of the pack props " +
                    "could be loaded. See the Console.", "OK");
        }

        /// <summary>
        /// The pass itself, on a pad already decided: for <see cref="BlockRandomiser"/>,
        /// which rolls a whole block and runs every dressing pass over it in one go, so the
        /// asking half above has nothing left to ask. <see cref="BlockPad.ResetAuto"/> names
        /// and places the root exactly as <see cref="TryTarget"/> does, so a block dressed
        /// either way is the same block. Returns how many props were placed.
        /// </summary>
        internal static int Fill(BlockPad pad, out Transform root)
        {
            root = pad.ResetAuto(AutoRoot);
            var target = new Target
            {
                label = pad.label,
                centre = pad.centre,
                width = pad.width,
                depth = pad.depth,
                parent = root,
                scope = pad.group,
            };

            var placed = Dress(target, out var story);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[Props] {placed} props dressed the free space of {target.label} " +
                      $"({target.width:F0} x {target.depth:F0} m). {story}");
            return placed;
        }

        // ------------------------------------------------------------------- target

        sealed class Target
        {
            public string label;
            public Vector3 centre;
            public float width, depth;
            public Transform parent;    // where the auto props are parented
            public Transform scope;     // the block's own subtree, when it has one
        }

        /// <summary>The pad to dress: the one the selection stands on, or the only one
        /// holding a block.</summary>
        static bool TryTarget(out Target target)
        {
            target = null;
            var scene = SceneManager.GetActiveScene();
            var candidates = new List<Target>();

            var bench = scene.GetRootGameObjects().FirstOrDefault(go => go.name == BlockLotCapture.WorkbenchRoot);
            if (bench)
                foreach (Transform group in bench.transform)
                    if (BlockLotCapture.TryReadWorkbenchName(group.name, out var block, out _,
                                                             out var w, out var d))
                        candidates.Add(new Target
                        {
                            label = $"block {block}",
                            centre = new Vector3(group.position.x, 0f, group.position.z),
                            width = w,
                            depth = d,
                            parent = group,
                            scope = group,
                        });

            var lots = scene.GetRootGameObjects().FirstOrDefault(go => go.name == BlockLotPads.RootName);
            if (lots)
                foreach (Transform pad in lots.transform)
                    if (BlockLotCapture.TryReadPadName(pad.name, out var code, out var w, out var d))
                        candidates.Add(new Target
                        {
                            label = $"lot {code}",
                            centre = new Vector3(pad.position.x, 0f, pad.position.z),
                            width = w,
                            depth = d,
                            parent = null,      // a scene root of its own, made below
                            scope = null,
                        });

            if (candidates.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No pads",
                    "This scene has no lot pads. Run Tools/City/Catalog/Draw Block Lot Pads first.", "OK");
                return false;
            }

            // The selection decides; a selection inside a workbench group belongs to it
            // whatever its bounds say.
            var selected = Selection.activeTransform;
            if (selected)
            {
                for (var node = selected; node; node = node.parent)
                {
                    var owner = candidates.FirstOrDefault(c => c.scope == node);
                    if (owner != null)
                    {
                        target = owner;
                        break;
                    }
                }
                target ??= At(candidates, selected.position);
            }

            if (target == null)
            {
                // No usable selection: dress the only occupied pad, or say what to pick.
                var occupied = candidates.Where(c => Occupants(c).Count > 0).ToList();
                if (occupied.Count == 1)
                    target = occupied[0];
                else
                {
                    EditorUtility.DisplayDialog(
                        "Which block?",
                        occupied.Count == 0
                            ? "No pad holds a building yet. Drag catalog buildings onto a lot " +
                              "pad first - props dress the space AROUND buildings."
                            : "More than one pad holds a block: " +
                              string.Join(", ", occupied.Select(o => o.label)) +
                              ".\n\nSelect a building on the one you mean and run this again.",
                        "OK");
                    return false;
                }
            }

            if (target.parent == null)
            {
                var rootName = $"{target.label} {AutoRoot}";
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == rootName)
                        Object.DestroyImmediate(root);
                var holder = new GameObject(rootName);
                holder.transform.position = target.centre;
                target.parent = holder.transform;
            }
            else
            {
                var existing = target.parent.Find(AutoRoot);
                if (existing)
                    Object.DestroyImmediate(existing.gameObject);
                var holder = new GameObject(AutoRoot);
                holder.transform.SetParent(target.parent, false);
                holder.transform.position = target.centre;
                target.parent = holder.transform;
            }
            return true;
        }

        static Target At(List<Target> candidates, Vector3 point) =>
            candidates.FirstOrDefault(c =>
                Mathf.Abs(point.x - c.centre.x) <= c.width * 0.5f + 4f &&
                Mathf.Abs(point.z - c.centre.z) <= c.depth * 0.5f + 4f);

        /// <summary>Every renderer standing over a pad that is not the pad itself, a
        /// caption, or a prop this pass placed - i.e. what the props have to keep out
        /// of.</summary>
        static List<MeshRenderer> Occupants(Target target)
        {
            var found = new List<MeshRenderer>();
            var minX = target.centre.x - target.width * 0.5f;
            var maxX = target.centre.x + target.width * 0.5f;
            var minZ = target.centre.z - target.depth * 0.5f;
            var maxZ = target.centre.z + target.depth * 0.5f;

            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (renderer.GetComponent<TextMesh>())
                    continue;
                var b = renderer.bounds;
                if (b.max.x < minX || b.min.x > maxX || b.max.z < minZ || b.min.z > maxZ)
                    continue;
                if (IsPadOrAuto(renderer.transform, target))
                    continue;
                found.Add(renderer);
            }
            return found;
        }

        /// <summary>Pads, lot-pad planes and this pass's own output are ground, not
        /// obstacles - and so is a floor BlockFloorFiller already laid, or a block whose
        /// ground was dressed first would come out with nowhere left to stand a bin.
        /// Parking is NOT on the list: another pass's cars and bays are things standing
        /// in the yard, and the rows have to keep out of them.</summary>
        static bool IsPadOrAuto(Transform node, Target target)
        {
            for (var t = node; t; t = t.parent)
            {
                if (t == target.parent)
                    return true;
                if (t.name == "pad" || t.name == BlockLotPads.RootName ||
                    t.name.EndsWith(AutoRoot) || t.name.EndsWith(BlockFloorFiller.AutoRoot) ||
                    t.name.StartsWith("Lot "))
                    return true;
            }
            return false;
        }

        // --------------------------------------------------------------------- lines

        /// <summary>
        /// One straight run of free ground with something solid down one side: a stretch
        /// of building wall, or a stretch of the pad's own edge. Every prop this pass
        /// places stands on one of these, which is what keeps a row a row.
        /// </summary>
        sealed class Line
        {
            /// <summary>On the face itself, at the run's beginning.</summary>
            public Vector3 Start;

            /// <summary>Unit vector along the run, and from the face into the free
            /// ground in front of it.</summary>
            public Vector3 Along, Into;

            public float Length;

            /// <summary>Free ground in front of the face, over the whole run.</summary>
            public float Depth;

            /// <summary>The pad's own edge rather than a building wall.</summary>
            public bool Border;

            /// <summary>A building wall standing ON the pad's edge and looking OUT of it -
            /// the block's street facade. It has no free ground in front of it because the
            /// ground in front of it is the pavement, which is not part of the block; it is
            /// therefore no use to anything that stands on the floor, and it is the single
            /// most important wall the block has to anything that hangs on one.</summary>
            public bool Outward;

            /// <summary>Facing the yard - the way a bench, a bin and a lamp all turn.</summary>
            public float FaceYaw => Mathf.Atan2(Into.x, Into.z) * Mathf.Rad2Deg;

            /// <summary>Facing the street, which is what a hedge presents.</summary>
            public float OutYaw => FaceYaw + 180f;
        }

        // ---------------------------------------------------------------- the yard

        /// <summary>The pad as this pass works it: an occupancy grid, a budget, and the
        /// one primitive that stands a prop against a line.</summary>
        sealed class Yard
        {
            readonly Target _target;
            readonly bool[,] _blocked;

            /// <summary>How tall whatever occupies the cell is. Written per RENDERER
            /// rather than once per prefab: a City_XX cluster is half a dozen buildings in
            /// one instance, and taking the whole instance's box would report a two-storey
            /// wing as being as tall as the tower next to it - and then hang a hoarding
            /// eight metres up a five-metre wall.</summary>
            readonly float[,] _top;

            /// <summary>The same occupants unrounded, which is the only place the REAL
            /// wall line survives - the grid has already lost it to the cell.</summary>
            readonly List<(Rect area, float top)> _solids = new List<(Rect, float)>();

            /// <summary>What this pass has already stood up, to the centimetre - see
            /// Take for why the grid cannot do this job.</summary>
            readonly List<Rect> _taken = new List<Rect>();

            /// <summary>The same for what is mounted ABOVE the ground. Kept apart from
            /// _taken because the two do not compete: a bin may stand under a sign, and a
            /// sign may not hang over another sign.</summary>
            readonly List<Rect> _flown = new List<Rect>();

            readonly float _minX, _minZ;
            readonly int _nx, _nz;

            internal int Budget;
            internal int Facades;
            internal int Placed;

            internal Yard(Target target)
            {
                _target = target;
                _nx = Mathf.Max(1, Mathf.FloorToInt(target.width / Cell));
                _nz = Mathf.Max(1, Mathf.FloorToInt(target.depth / Cell));
                _minX = target.centre.x - target.width * 0.5f;
                _minZ = target.centre.z - target.depth * 0.5f;
                _blocked = new bool[_nx, _nz];
                _top = new float[_nx, _nz];

                var occupants = Occupants(target);
                foreach (var renderer in occupants)
                {
                    var rect = Flat(renderer.bounds);
                    Block(rect, renderer.bounds.max.y);
                    _solids.Add((rect, renderer.bounds.max.y));
                }

                // The parking pass leaves LANES inside its lot - the aisle between two rows
                // and the entrance drive off the street - and to a grid those are free ground
                // like any other, which is how a bin ends up standing in a car park's only
                // way in. So the whole lot is blocked rather than its pieces: a car park is
                // one rectangle, and none of it is yard.
                Rect? lot = null;
                foreach (var renderer in occupants)
                {
                    if (!BlockPad.Under(renderer.transform, BlockPad.ParkingRoot))
                        continue;
                    var rect = Flat(renderer.bounds);
                    lot = lot.HasValue ? Union(lot.Value, rect) : rect;
                }
                if (lot.HasValue)
                    Block(lot.Value, 0f);
            }

            /// <summary>Marks every cell a world rectangle touches as taken, and raises
            /// those cells to the height of what took them.</summary>
            void Block(Rect area, float top)
            {
                var i0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - _minX) / Cell), 0, _nx - 1);
                var i1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - _minX) / Cell), 0, _nx - 1);
                var j0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - _minZ) / Cell), 0, _nz - 1);
                var j1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - _minZ) / Cell), 0, _nz - 1);
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                    {
                        _blocked[i, j] = true;
                        if (top > _top[i, j])
                            _top[i, j] = top;
                    }
            }

            /// <summary>
            /// The wall behind a point on a line: how tall it is, and how far behind the
            /// grid's face it really stands. Sampled half a cell BEHIND the face, so the
            /// answer is the building's own and not the free ground's zero.
            ///
            /// The setback comes back ready to hang a thing off, and it is never positive:
            /// the grid rounded the occupant OUT to whole cells, so the true wall is
            /// somewhere between the face and half a metre behind it, and a mounted piece
            /// bites a little way into it from there. Where no occupant owns the sample -
            /// a cell blocked by the corner of a box that does not otherwise reach it -
            /// the answer is the middle of that error instead.
            /// </summary>
            internal float WallTop(Line line, float along, out float setback)
            {
                var point = line.Start + line.Along * along - line.Into * (Cell * 0.5f);
                var sideways = Mathf.Abs(line.Into.x) > 0.5f;
                var start = sideways ? line.Start.x : line.Start.z;
                var facing = sideways ? line.Into.x : line.Into.z;

                var top = 0f;
                var offset = 0f;
                var found = false;

                // The occupant whose face stands FURTHEST out is the one a sign would
                // touch, and the one that has to carry it.
                foreach (var (area, height) in _solids)
                {
                    if (!area.Contains(new Vector2(point.x, point.z)))
                        continue;
                    var edge = sideways
                        ? (facing > 0f ? area.xMax : area.xMin)
                        : (facing > 0f ? area.yMax : area.yMin);
                    var reach = (edge - start) * facing;
                    if (found && reach <= offset)
                        continue;
                    found = true;
                    offset = reach;
                    top = height;
                }

                if (found)
                {
                    setback = offset + FacadeBite;
                    return top;
                }

                setback = FacadeSetback;
                var i = Mathf.Clamp(Mathf.FloorToInt((point.x - _minX) / Cell), 0, _nx - 1);
                var j = Mathf.Clamp(Mathf.FloorToInt((point.z - _minZ) / Cell), 0, _nz - 1);
                return _top[i, j];
            }

            static Rect Flat(Bounds bounds) =>
                Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);

            static Rect Union(Rect a, Rect b) =>
                Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

            internal float FreeArea
            {
                get
                {
                    var free = 0;
                    for (var i = 0; i < _nx; i++)
                        for (var j = 0; j < _nz; j++)
                            if (!_blocked[i, j])
                                free++;
                    return free * Cell * Cell;
                }
            }

            /// <summary>
            /// Every run the yard offers, walls and pad edges alike. The scan is one and
            /// the same for both: a free cell whose neighbour on one side is solid faces
            /// that way, and OFF THE PAD counts as solid - which is exactly what makes
            /// the pad's own edges come out of the same sweep as the buildings' walls.
            /// </summary>
            internal List<Line> Lines()
            {
                var lines = new List<Line>();
                Scan(lines, Vector3.forward, Vector3.right);   // walls facing north
                Scan(lines, Vector3.back, Vector3.right);      // ... south
                Scan(lines, Vector3.right, Vector3.forward);   // ... east
                Scan(lines, Vector3.left, Vector3.forward);    // ... west
                Rim(lines);
                return lines;
            }

            /// <summary>
            /// The block's own outside: every stretch of the pad boundary a building
            /// actually stands on, looking off the pad.
            ///
            /// The sweep above cannot find these and never could. It reads a wall as free
            /// ground with something solid behind it, and a street facade has NO free
            /// ground in front of it - what is in front of it is the pavement, which is not
            /// on the pad. So the frontage of every block, the one wall a city puts its
            /// hoardings and its shop signs on, was the one wall this pass had no name for.
            /// It is read the other way round instead: a boundary cell that is solid IS a
            /// facade, and the way it looks is off the pad.
            /// </summary>
            void Rim(List<Line> lines)
            {
                Sweep(lines, Vector3.back, Vector3.right, 0, true);         // south face
                Sweep(lines, Vector3.forward, Vector3.right, _nz - 1, true);// north face
                Sweep(lines, Vector3.left, Vector3.forward, 0, false);      // west face
                Sweep(lines, Vector3.right, Vector3.forward, _nx - 1, false);// east face
            }

            void Sweep(List<Line> into, Vector3 face, Vector3 along, int lane, bool horizontal)
            {
                var steps = horizontal ? _nx : _nz;

                // Run() names a line by the FREE cell and puts the face on the boundary it
                // shares with the solid one. Here the free cell is the one that is not
                // there - the pavement, one step off the pad - so that is the lane it is
                // handed, and the plane comes out on the pad's own edge.
                var outside = lane + (face.x + face.z > 0f ? 1 : -1);
                var from = -1;

                for (var s = 0; s <= steps; s++)
                {
                    if (s < steps && _blocked[horizontal ? s : lane, horizontal ? lane : s])
                    {
                        if (from < 0)
                            from = s;
                        continue;
                    }

                    if (from >= 0)
                    {
                        var line = Run(from, s - from, outside, horizontal, face, along, 0, false);
                        line.Outward = true;
                        into.Add(line);
                        from = -1;
                    }
                }
            }

            void Scan(List<Line> into, Vector3 face, Vector3 along)
            {
                var horizontal = Mathf.Abs(along.x) > 0.5f;
                var lanes = horizontal ? _nz : _nx;   // which wall line
                var steps = horizontal ? _nx : _nz;   // along the run
                var di = Mathf.RoundToInt(-face.x);
                var dj = Mathf.RoundToInt(-face.z);

                for (var lane = 0; lane < lanes; lane++)
                {
                    var from = -1;
                    var shallowest = 0;
                    var border = false;

                    for (var s = 0; s <= steps; s++)
                    {
                        // one step past the end closes the last run
                        var depth = 0;
                        var offPad = false;
                        var faces = s < steps &&
                                    Faces(s, lane, horizontal, di, dj, out depth, out offPad);
                        if (faces)
                        {
                            if (from < 0)
                            {
                                from = s;
                                shallowest = depth;
                                border = offPad;
                            }
                            else
                            {
                                shallowest = Mathf.Min(shallowest, depth);
                                // a run half against a wall and half against the pad edge
                                // is neither; the stricter of the two wins
                                border &= offPad;
                            }
                            continue;
                        }

                        if (from >= 0)
                        {
                            into.Add(Run(from, s - from, lane, horizontal, face, along,
                                         shallowest, border));
                            from = -1;
                        }
                    }
                }
            }

            /// <summary>Whether this cell has solid ground behind it and free ground in
            /// front, and how deep that free ground runs.</summary>
            bool Faces(int s, int lane, bool horizontal, int di, int dj,
                       out int depth, out bool offPad)
            {
                depth = 0;
                offPad = false;

                var i = horizontal ? s : lane;
                var j = horizontal ? lane : s;
                if (_blocked[i, j])
                    return false;

                var bi = i + di;
                var bj = j + dj;
                if (bi < 0 || bj < 0 || bi >= _nx || bj >= _nz)
                    offPad = true;                       // the pad's own edge
                else if (!_blocked[bi, bj])
                    return false;                        // open ground, no face here

                // how far the free ground reaches in front of it
                var fi = i;
                var fj = j;
                while (fi >= 0 && fj >= 0 && fi < _nx && fj < _nz && !_blocked[fi, fj])
                {
                    depth++;
                    fi -= di;
                    fj -= dj;
                }
                return true;
            }

            Line Run(int from, int count, int lane, bool horizontal, Vector3 face,
                     Vector3 along, int depth, bool border)
            {
                // The face is the cell boundary the solid ground is on: the lane's own
                // low edge when the free side looks up the axis, its high edge otherwise.
                var lanePos = (lane + (face.x + face.z > 0f ? 0f : 1f)) * Cell;
                var start = from * Cell;

                return new Line
                {
                    Start = horizontal
                        ? new Vector3(_minX + start, 0f, _minZ + lanePos)
                        : new Vector3(_minX + lanePos, 0f, _minZ + start),
                    Along = along,
                    Into = face,
                    Length = count * Cell,
                    Depth = depth * Cell,
                    Border = border,
                };
            }

            // ------------------------------------------------------------ placement

            /// <summary>
            /// Stands one prop against a line: its back on the face at
            /// <paramref name="setback"/>, its near end at <paramref name="anchor"/> along
            /// the run (or centred on it). Returns the length it took along the run, or 0
            /// when it did not fit - in which case nothing is left in the scene.
            ///
            /// Measured after instantiation and after the turn, so what is reserved is the
            /// piece's real footprint at the angle it ended up standing at, not the
            /// prefab's own idea of its size.
            /// </summary>
            internal float Stand(GameObject prefab, Line line, float anchor, bool centred,
                                 float setback, float yaw, GameObject face = null)
            {
                if (Budget <= 0)
                    return 0f;

                var instance = Spawn(prefab, yaw);

                var measured = BlockLotCapture.RendererBounds(instance);
                if (!measured.HasValue)
                {
                    Object.DestroyImmediate(instance);
                    return 0f;
                }

                var b = measured.Value;
                var run = Mathf.Abs(Vector3.Dot(b.size, line.Along));
                var deep = Mathf.Abs(Vector3.Dot(b.size, line.Into));

                var centre = line.Start
                           + line.Along * (centred ? anchor : anchor + run * 0.5f)
                           + line.Into * (setback + deep * 0.5f);

                instance.transform.position = new Vector3(centre.x - b.center.x, 0f,
                                                          centre.z - b.center.z);

                if (!Take(Footprint(centre, b.size), RowGap))
                {
                    Object.DestroyImmediate(instance);
                    return 0f;
                }

                Paste(face, instance.transform);
                Budget--;
                Placed++;
                return run;
            }

            /// <summary>
            /// Hangs one prop on the wall a line runs along: its back ON the wall (see
            /// <see cref="WallTop"/> for how that plane is found), its underside
            /// <paramref name="height"/> off the ground, its near end at
            /// <paramref name="anchor"/> along the run. Returns the length it took along
            /// the wall, or 0 - and 0 is the common answer here, since a facade family
            /// asks for a height the wall in front of it may not have.
            ///
            /// Nothing on the ground is reserved and nothing on the ground refuses it: a
            /// sign four metres up neither stands on a bin nor keeps one away. What it does
            /// have to clear is the roof of the thing it is screwed to, and the other
            /// things already hanging on the same stretch of wall.
            /// </summary>
            internal float Mount(GameObject prefab, Line line, float anchor, float height,
                                 GameObject face = null)
            {
                if (Facades <= 0)
                    return 0f;

                var instance = Spawn(prefab, line.FaceYaw);

                var measured = BlockLotCapture.RendererBounds(instance);
                if (!measured.HasValue)
                {
                    Object.DestroyImmediate(instance);
                    return 0f;
                }

                var b = measured.Value;
                var run = Mathf.Abs(Vector3.Dot(b.size, line.Along));
                var deep = Mathf.Abs(Vector3.Dot(b.size, line.Into));
                var mid = anchor + run * 0.5f;
                var wall = WallTop(line, mid, out var setback);
                var centre = line.Start
                           + line.Along * mid
                           + line.Into * (setback + deep * 0.5f);

                if (height + b.size.y + FacadeHead > wall ||
                    !Fits(Footprint(centre, b.size), _flown, FacadeGap, FacadeOverhang))
                {
                    Object.DestroyImmediate(instance);
                    return 0f;
                }

                instance.transform.position = new Vector3(centre.x - b.center.x,
                                                          height - b.min.y,
                                                          centre.z - b.center.z);
                Paste(face, instance.transform);
                Facades--;
                Placed++;
                return run;
            }

            /// <summary>
            /// Strings one cable span: from <paramref name="from"/> along
            /// <paramref name="along"/>, stretched to <paramref name="span"/> metres out of
            /// the <paramref name="natural"/> it was authored at, hung so its attachment
            /// sits at <paramref name="head"/>.
            ///
            /// It takes no budget and reserves nothing. A cable is not a prop standing in
            /// the yard - it is the thing two poles that are already standing are FOR, and
            /// a span refused for want of budget would leave a gap in a run whose poles
            /// were paid for.
            /// </summary>
            internal void Cable(GameObject prefab, Vector3 from, Vector3 along, float span,
                                float natural, float head)
            {
                var instance = Spawn(prefab, Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);

                var measured = BlockLotCapture.RendererBounds(instance);
                if (!measured.HasValue)
                {
                    Object.DestroyImmediate(instance);
                    return;
                }

                // Only the run is stretched. The sag is in Y and is the cable's own, so a
                // long span hangs the same distance below its poles as a short one - which
                // is what the pack's demo does and what a real span does.
                instance.transform.localScale = new Vector3(1f, 1f, span / natural);
                instance.transform.position = new Vector3(from.x, head - measured.Value.max.y,
                                                          from.z);
                Placed++;
            }

            /// <summary>The prefab's own size, read by standing one up and throwing it
            /// away. There is no other honest way to ask: a pack prefab's bounds are the
            /// composed hierarchy's, not the root mesh's.</summary>
            internal Bounds? Measure(GameObject prefab)
            {
                var probe = Spawn(prefab, 0f);
                var bounds = BlockLotCapture.RendererBounds(probe);
                Object.DestroyImmediate(probe);
                return bounds;
            }

            GameObject Spawn(GameObject prefab, float yaw)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(_target.parent, worldPositionStays: false);
                instance.transform.SetPositionAndRotation(Vector3.zero,
                                                          Quaternion.Euler(0f, yaw, 0f));
                return instance;
            }

            /// <summary>
            /// Stands a second piece on the first one's transform - the ad on a hoarding.
            ///
            /// A SIBLING, not a child, and that is not a detail: the capture pass records
            /// a prefab instance and does not descend into it, so a face parented to its
            /// board would be dressed in the scene and missing from every bake of it.
            /// </summary>
            void Paste(GameObject face, Transform onto)
            {
                if (!face)
                    return;
                var instance = Spawn(face, onto.eulerAngles.y);
                instance.transform.position = onto.position;
                Placed++;
            }

            /// <summary>
            /// Drops one prop on a point at whatever angle it was handed, for the things
            /// no yard ever stood up straight. Same measure-and-reserve as Stand - what
            /// changes is only that nothing decides where its back goes.
            /// </summary>
            internal bool Drop(GameObject prefab, Vector3 point, float yaw)
            {
                if (Budget <= 0)
                    return false;

                var instance = Spawn(prefab, yaw);

                var measured = BlockLotCapture.RendererBounds(instance);
                if (!measured.HasValue)
                {
                    Object.DestroyImmediate(instance);
                    return false;
                }

                var b = measured.Value;
                instance.transform.position = new Vector3(point.x - b.center.x, 0f,
                                                          point.z - b.center.z);

                if (!Take(Footprint(point, b.size), DropGap))
                {
                    Object.DestroyImmediate(instance);
                    return false;
                }

                Budget--;
                Placed++;
                return true;
            }

            /// <summary>The centre of a random cell with nothing on it. Junk gathers where
            /// there is something to gather against, so most of the picks ask for a cell
            /// with a wall, a bin or a car beside it; the rest may land anywhere.</summary>
            internal Vector3? FreeSpot(bool againstSomething)
            {
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    var i = Random.Range(0, _nx);
                    var j = Random.Range(0, _nz);
                    if (_blocked[i, j])
                        continue;
                    if (againstSomething && !Beside(i, j))
                        continue;
                    return new Vector3(_minX + (i + 0.5f) * Cell, 0f, _minZ + (j + 0.5f) * Cell);
                }
                return null;
            }

            bool Beside(int i, int j)
            {
                for (var di = -1; di <= 1; di++)
                    for (var dj = -1; dj <= 1; dj++)
                    {
                        var ni = i + di;
                        var nj = j + dj;
                        if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz)
                            return true;            // the pad's own edge counts
                        if (_blocked[ni, nj])
                            return true;
                    }
                return false;
            }

            static Rect Footprint(Vector3 centre, Vector3 size) =>
                new Rect(centre.x - size.x * 0.5f, centre.z - size.z * 0.5f, size.x, size.z);

            /// <summary>
            /// Reserves the footprint, or answers false having reserved nothing: a prop
            /// that hangs off the pad, stands over a building or overlaps something this
            /// pass already placed is not a prop it may keep.
            ///
            /// The buildings are tested against the cell grid, what this pass placed
            /// against its own exact rectangles. That split is deliberate: a cell is far
            /// too coarse to lay a row of bins on - reserve whole cells and the second bin
            /// of a pair is always refused - while a building only ever needs to be
            /// answered to within half a metre.
            /// </summary>
            bool Take(Rect rect, float gap)
            {
                if (OverBuilding(rect))
                    return false;

                return Fits(rect, _taken, gap);
            }

            /// <summary>The half of <see cref="Take"/> that answers to the pad and to a
            /// list of what is already there, without asking the buildings anything - which
            /// is all a MOUNTED thing has to clear, since the building is what it is
            /// screwed to.</summary>
            bool Fits(Rect rect, List<Rect> against, float gap, float slack = 0.05f)
            {
                // slack: a hedge is allowed to touch the kerb line, and a shop sign is
                // allowed to hang over it - see FacadeOverhang.
                if (rect.xMin < _minX - slack || rect.yMin < _minZ - slack ||
                    rect.xMax > _minX + _nx * Cell + slack ||
                    rect.yMax > _minZ + _nz * Cell + slack)
                    return false;

                var probe = Rect.MinMaxRect(rect.xMin - gap, rect.yMin - gap,
                                            rect.xMax + gap, rect.yMax + gap);
                foreach (var taken in against)
                    if (taken.Overlaps(probe))
                        return false;

                against.Add(rect);
                return true;
            }

            /// <summary>Whether the rectangle covers any occupied cell. Half-open on the
            /// far edge - a piece standing flush against a wall stops AT the cell the wall
            /// is in, and rounding it in would refuse every flush thing on the pad.</summary>
            bool OverBuilding(Rect rect)
            {
                const float eps = 0.05f;
                var i0 = Mathf.Clamp(Mathf.FloorToInt((rect.xMin - _minX + eps) / Cell), 0, _nx - 1);
                var i1 = Mathf.Clamp(Mathf.CeilToInt((rect.xMax - _minX - eps) / Cell) - 1, 0, _nx - 1);
                var j0 = Mathf.Clamp(Mathf.FloorToInt((rect.yMin - _minZ + eps) / Cell), 0, _nz - 1);
                var j1 = Mathf.Clamp(Mathf.CeilToInt((rect.yMax - _minZ - eps) / Cell) - 1, 0, _nz - 1);

                for (var i = i0; i <= Mathf.Max(i0, i1); i++)
                    for (var j = j0; j <= Mathf.Max(j0, j1); j++)
                        if (_blocked[i, j])
                            return true;
                return false;
            }
        }

        // -------------------------------------------------------------------- rows

        /// <summary>
        /// The back-of-house row: short clusters of one species stood shoulder to
        /// shoulder, a stretch of bare wall, then the next cluster. Bins come in twos and
        /// threes in a real yard, and a wall lined end to end with them reads as a set
        /// dressing rather than a place.
        /// </summary>
        static int ServiceRow(Yard yard, Line line, List<GameObject> pool)
        {
            if (pool.Count == 0)
                return 0;

            var placed = 0;
            var cursor = Margin;

            while (cursor < line.Length - Margin && yard.Budget > 0)
            {
                var prefab = pool[Random.Range(0, pool.Count)];
                var cluster = Random.Range(1, 4);

                for (var k = 0; k < cluster; k++)
                {
                    var took = yard.Stand(prefab, line, cursor, false, WallSetback, line.FaceYaw);
                    if (took <= 0f)
                    {
                        cursor += Cell;     // something is in the way - step past it
                        break;
                    }
                    cursor += took + Snug;
                    placed++;
                }

                cursor += Random.Range(1.5f, 4.5f);
            }
            return placed;
        }

        /// <summary>
        /// A repeated row at a fixed pitch, centred on its line so it reads as laid out
        /// rather than started at a corner. ONE species for the whole row: a row of
        /// identical lamps is a lighting scheme, a row of different ones is a catalog
        /// page.
        /// </summary>
        static int PitchedRow(Yard yard, Line line, List<GameObject> pool, float pitch,
                              float setback, float yaw)
        {
            if (pool.Count == 0)
                return 0;

            var usable = line.Length - 2f * Margin;
            if (usable < 0f)
                return 0;

            var slots = Mathf.FloorToInt(usable / pitch) + 1;
            if (slots < 2)
                return 0;

            var span = (slots - 1) * pitch;
            var first = (line.Length - span) * 0.5f;

            // Three goes at the species. A palm's canopy measures four metres across and
            // simply does not stand on a metre and a half of verge - and a row that placed
            // nothing is worth re-rolling as something smaller rather than reporting as an
            // edge that had no room.
            for (var attempt = 0; attempt < 3 && pool.Count > 0; attempt++)
            {
                var prefab = pool[Random.Range(0, pool.Count)];
                var placed = 0;
                for (var s = 0; s < slots; s++)
                    if (yard.Stand(prefab, line, first + s * pitch, true, setback, yaw) > 0f)
                        placed++;
                if (placed > 0)
                    return placed;
            }
            return 0;
        }

        /// <summary>A continuous run of one piece, laid end to end along the whole line -
        /// the hedge or fence that closes a yard off. A piece that cannot stand (a gate,
        /// a bay, a tree already there) is stepped over and the run carries on, so one
        /// obstacle does not end the hedge.</summary>
        static int EdgeRun(Yard yard, Line line, List<GameObject> pool)
        {
            if (pool.Count == 0)
                return 0;

            var prefab = pool[Random.Range(0, pool.Count)];
            var placed = 0;
            var cursor = 0f;

            while (cursor < line.Length && placed < MaxRunPieces && yard.Budget > 0)
            {
                var took = yard.Stand(prefab, line, cursor, false, EdgeSetback, line.OutYaw);
                if (took <= 0f)
                {
                    cursor += Cell;
                    continue;
                }
                cursor += took;
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// The street's own furniture along a pad edge: one piece, a long stretch of
        /// nothing, then a different one. Mixed species rather than clustered, which is the
        /// opposite of the service row and for the same reason - two bins side by side is a
        /// yard, two hydrants side by side is a mistake. All of it turned to face OUT,
        /// backs to the block: a payphone is used from the pavement.
        /// </summary>
        static int FrontageRow(Yard yard, Line line, List<GameObject> pool,
                               List<GameObject> ads)
        {
            if (pool.Count == 0)
                return 0;

            var placed = 0;
            var cursor = Margin;

            while (cursor < line.Length - Margin && yard.Budget > 0)
            {
                var prefab = pool[Random.Range(0, pool.Count)];
                var took = yard.Stand(prefab, line, cursor, false,
                                      EdgeSetback, line.OutYaw, FaceFor(prefab, ads));
                if (took <= 0f)
                {
                    cursor += Cell;
                    continue;
                }
                cursor += took + Random.Range(5f, 13f);
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// What a wall wears. Not a row - the pieces are unrelated to each other and each
        /// one asks for its own height - so the walk along the wall is a cursor with a long
        /// random stride, and a piece that will not go up (a hoarding on a two-storey wall,
        /// a second sign where one already hangs) simply costs its stride.
        /// </summary>
        static int FacadeRow(Yard yard, Line line,
                             List<(GameObject prefab, float low, float high)> pool,
                             List<GameObject> ads)
        {
            if (pool.Count == 0)
                return 0;

            var placed = 0;
            var cursor = Random.Range(0.5f, 3f);

            while (cursor < line.Length - 1f && yard.Facades > 0)
            {
                var pick = pool[Random.Range(0, pool.Count)];
                var took = yard.Mount(pick.prefab, line, cursor,
                                      Random.Range(pick.low, pick.high),
                                      FaceFor(pick.prefab, ads));
                if (took > 0f)
                    placed++;
                cursor += (took > 0f ? took : 0.75f) + Random.Range(2f, 7f);
            }
            return placed;
        }

        /// <summary>
        /// Poles down one edge with the wire strung between them.
        ///
        /// The pitch is the ROAD's, not the cable's: the pack authors one short span and
        /// its own demo scene stretches every one of them, so the poles stand where poles
        /// stand and the wire is scaled to reach. Which cable piece to use is decided by
        /// measuring rather than by name - the family holds spans and coils of slack under
        /// the same prefix, and only a piece that measures like a span can be stretched
        /// into one.
        ///
        /// A span is strung only between two poles that both actually went up. A pole
        /// refused for standing in a doorway or on the parking would otherwise leave a
        /// wire running out of one pole into thin air, which is worse than a gap.
        /// </summary>
        static int PowerRun(Yard yard, Line line, List<GameObject> poles, List<GameObject> cables)
        {
            if (poles.Count == 0)
                return 0;

            var pole = poles[Random.Range(0, poles.Count)];
            var measured = yard.Measure(pole);
            if (!measured.HasValue)
                return 0;

            // The pole is turned so its crossarm crosses the run, which is the way a pole
            // stands: local +Z down the line, local X the arm. So its own X is what reaches
            // into the yard, and its own X is what the cable rows are spread across.
            var poleYaw = Mathf.Atan2(line.Along.x, line.Along.z) * Mathf.Rad2Deg;
            var arm = measured.Value.size.x * 0.5f;
            var head = measured.Value.max.y - CableDrop;
            var reach = EdgeSetback + arm;

            var slots = Mathf.Min(Mathf.FloorToInt((line.Length - 2f * Margin) / PolePitch) + 1,
                                  MaxRunPieces);
            if (slots < 1)
                return 0;

            var first = (line.Length - (slots - 1) * PolePitch) * 0.5f;
            var up = new bool[slots];
            var placed = 0;

            for (var s = 0; s < slots; s++)
                if (yard.Stand(pole, line, first + s * PolePitch, true, EdgeSetback, poleYaw) > 0f)
                {
                    up[s] = true;
                    placed++;
                }

            // One cable piece for the whole run, and one row of it per crossarm position.
            //
            // A candidate has to MEASURE like a span before it can be stretched into one:
            // long, longer that way than any other, and running out of its own pivot
            // rather than centred on it. Everything the placement below assumes is in that
            // test, so a pack that renames its pieces or ships a coil among them costs a
            // run its wires and never a run of wire laid through the middle of a pole.
            var span = 0f;
            GameObject cable = null;
            foreach (var candidate in cables.OrderBy(_ => Random.value))
            {
                var size = yard.Measure(candidate);
                if (!size.HasValue)
                    continue;
                var length = size.Value.size.z;
                if (length < 3f || length < size.Value.size.x ||
                    Mathf.Abs(size.Value.min.z) > 0.5f || PolePitch / length > MaxStretch)
                    continue;
                cable = candidate;
                span = length;
                break;
            }
            if (!cable)
                return placed;

            for (var s = 0; s + 1 < slots; s++)
            {
                if (!up[s] || !up[s + 1])
                    continue;
                foreach (var row in CableRows)
                {
                    var from = line.Start
                             + line.Along * (first + s * PolePitch)
                             + line.Into * (reach + row * arm * CableSpread);
                    yard.Cable(cable, from, line.Along, PolePitch, span, head);
                }
            }
            return placed;
        }

        /// <summary>The ad panel the piece about to be placed needs, which is none unless
        /// it is one of the two blank hoardings. Asked of every placement rather than
        /// wired into one row, so the board on a wall and the board on legs at the kerb are
        /// dressed by the same rule.</summary>
        static GameObject FaceFor(GameObject prefab, List<GameObject> ads)
        {
            if (ads == null || ads.Count == 0 || !prefab)
                return null;
            return Hoardings.Any(h => h.EndsWith("/" + prefab.name, System.StringComparison.Ordinal))
                ? ads[Random.Range(0, ads.Count)]
                : null;
        }

        /// <summary>The mess: a handful of bags, boxes and bushes dropped where they fall,
        /// mostly against something. Runs last, on what the rows left, so the tidy work is
        /// never pushed out of place by it.</summary>
        static int Scatter(Yard yard, List<GameObject> pool, int quota)
        {
            if (pool.Count == 0 || quota <= 0)
                return 0;

            var placed = 0;
            var attempts = 0;
            while (placed < quota && attempts++ < quota * 8 + 20 && yard.Budget > 0)
            {
                var spot = yard.FreeSpot(Random.value < 0.65f);
                if (spot == null)
                    break;
                if (yard.Drop(pool[Random.Range(0, pool.Count)], spot.Value,
                              Random.Range(0f, 360f)))
                    placed++;
            }
            return placed;
        }

        // ------------------------------------------------------------------ dressing

        /// <summary>What a pad edge is FOR. One job per edge, so two species never fight
        /// over the same frontage.</summary>
        enum Job { Avenue, Frontage, Power, Hedge, Seating, Trees }

        static int Dress(Target target, out string story)
        {
            story = "";
            var palette = LoadPalette();
            if (palette.Count == 0)
            {
                Debug.LogWarning("[Props] no palette prop could be loaded - are both Synty " +
                                 "packs still under Assets/Synty?");
                return 0;
            }
            var facade = LoadFacade();
            var ads = LoadList(AdFaces);
            var cables = LoadList(Cables);

            var yard = new Yard(target);
            var free = yard.FreeArea;
            if (free <= 0f)
                return 0;

            Random.InitState(System.Environment.TickCount);
            yard.Budget = Mathf.Clamp(Mathf.RoundToInt(free / AreaPerProp), 0, MaxProps);

            var lines = yard.Lines();
            var told = new List<string>();

            // 1. the walls: service clutter, in rows, on the five longest stretches the
            //    buildings offer. Deep enough to stand a bin in front of and no deeper -
            //    a wall with the whole yard in front of it is a frontage, not a back.
            var walls = lines
                .Where(l => !l.Border && l.Length >= MinWallRun && l.Depth >= 1.2f)
                .OrderByDescending(l => l.Length)
                .Take(5)
                .ToList();

            var service = 0;
            foreach (var wall in walls)
                service += ServiceRow(yard, wall, Pool(palette, Kind.Service));
            if (service > 0)
                told.Add($"{service} against {walls.Count} wall(s)");

            // 2. and what those walls WEAR, which needs no ground at all - so it is asked
            //    of every wall the block has rather than of the few the bins took, and the
            //    street facades come first: the frontage is what a hoarding is for, and a
            //    courtyard gets whatever budget is left after it.
            var faces = lines
                .Where(l => l.Outward || (!l.Border && l.Depth >= 1f))
                .Where(l => l.Length >= MinWallRun)
                .OrderByDescending(l => (l.Outward ? 1000f : 0f) + l.Length)
                .ToList();

            yard.Facades = Mathf.Clamp(
                Mathf.RoundToInt(faces.Sum(l => l.Length) / WallPerFacade), 0, MaxFacade);

            var mounted = 0;
            var dressed = 0;
            var street = 0;
            foreach (var wall in faces)
            {
                var count = FacadeRow(yard, wall, facade, ads);
                if (count == 0)
                    continue;
                mounted += count;
                dressed++;
                if (wall.Outward)
                    street++;
            }
            if (mounted > 0)
                told.Add($"{mounted} hung on {dressed} wall(s), {street} of them street facade");

            // 3. the perimeter: each pad edge gets one job, the longest edge first, so the
            //    frontage everyone looks at is the one that gets the lamps and the trees.
            var borders = lines
                .Where(l => l.Border && l.Length >= MinBorderRun && l.Depth >= 1.5f)
                .OrderByDescending(l => l.Length)
                .ToList();

            foreach (var (border, job) in borders.Zip(Jobs(borders.Count), (b, j) => (b, j)))
            {
                switch (job)
                {
                    case Job.Avenue:
                        // lamps first and trees between them: the lamps are the sparse
                        // row, and whatever the trees cannot fit around them is dropped
                        var lamps = PitchedRow(yard, border, Pool(palette, Kind.Lamp),
                                               LampPitch, EdgeSetback, border.FaceYaw);
                        var trees = PitchedRow(yard, border, Pool(palette, Kind.Green),
                                               TreePitch, EdgeSetback + 0.4f, border.FaceYaw);
                        told.Add($"{lamps} lamps and {trees} trees along {border.Length:F0} m");
                        break;

                    case Job.Frontage:
                        var kerb = FrontageRow(yard, border, Pool(palette, Kind.Frontage), ads);
                        told.Add($"{kerb} pieces of street furniture facing out");
                        break;

                    case Job.Power:
                        var poles = PowerRun(yard, border, Pool(palette, Kind.Pole), cables);
                        told.Add($"{poles} power poles and their spans");
                        break;

                    case Job.Hedge:
                        var hedge = EdgeRun(yard, border, Pool(palette, Kind.Edge));
                        told.Add($"{hedge} hedge pieces closing {border.Length:F0} m");
                        break;

                    case Job.Seating:
                        var seats = PitchedRow(yard, border, Pool(palette, Kind.Seat),
                                               SeatPitch, EdgeSetback + 0.3f, border.FaceYaw);
                        told.Add($"{seats} seats facing the yard");
                        break;

                    default:
                        var row = PitchedRow(yard, border, Pool(palette, Kind.Green),
                                             TreePitch, EdgeSetback + 0.4f, border.FaceYaw);
                        told.Add($"{row} trees along {border.Length:F0} m");
                        break;
                }
            }

            // 4. and the mess on top, on whatever the rows left: a fraction of the yard,
            //    never a carpet of it - the loose stuff is seasoning, not the dish.
            var loose = Scatter(yard, Pool(palette, Kind.Loose),
                                Mathf.Min(yard.Budget, Mathf.RoundToInt(free / LooseArea)));
            if (loose > 0)
                told.Add($"{loose} dropped loose");

            story = told.Count == 0
                ? "No run long enough to lay anything against."
                : string.Join("; ", told) + ".";
            return yard.Placed;
        }

        /// <summary>
        /// The jobs the pad edges take, in the order the edges are handed them.
        ///
        /// Three are fixed and in this order, because they are the three a block is not a
        /// city block without: the avenue on the longest edge, the street furniture on the
        /// next, the power on the one after. A pad has four edges, so the shuffled tail
        /// decides only what the LAST one does - which is enough to keep two blocks side
        /// by side from coming out as the same yard, and not enough to leave a block
        /// without a bus stop because the dice said hedge.
        /// </summary>
        static IEnumerable<Job> Jobs(int count)
        {
            var rest = new List<Job> { Job.Hedge, Job.Seating, Job.Trees, Job.Trees };
            for (var k = rest.Count - 1; k > 0; k--)
            {
                var swap = Random.Range(0, k + 1);
                (rest[k], rest[swap]) = (rest[swap], rest[k]);
            }

            var jobs = new List<Job> { Job.Avenue, Job.Frontage, Job.Power };
            jobs.AddRange(rest);
            return jobs.Take(Mathf.Max(0, count));
        }

        static List<GameObject> Pool(Dictionary<Kind, List<GameObject>> palette, Kind kind) =>
            palette.TryGetValue(kind, out var pool) ? pool : new List<GameObject>();

        static Dictionary<Kind, List<GameObject>> LoadPalette()
        {
            var loaded = new Dictionary<Kind, List<GameObject>>();
            var missing = new List<string>();

            foreach (var (path, kind) in Palette)
            {
                var prefab = Load(path, missing);
                if (!prefab)
                    continue;
                if (!loaded.TryGetValue(kind, out var pool))
                    loaded[kind] = pool = new List<GameObject>();
                pool.Add(prefab);
            }

            Report(missing);
            return loaded;
        }

        static List<(GameObject prefab, float low, float high)> LoadFacade()
        {
            var loaded = new List<(GameObject, float, float)>();
            var missing = new List<string>();

            foreach (var (path, low, high) in Facade)
            {
                var prefab = Load(path, missing);
                if (prefab)
                    loaded.Add((prefab, low, high));
            }

            Report(missing);
            return loaded;
        }

        static List<GameObject> LoadList(string[] paths)
        {
            var loaded = new List<GameObject>();
            var missing = new List<string>();

            foreach (var path in paths)
            {
                var prefab = Load(path, missing);
                if (prefab)
                    loaded.Add(prefab);
            }

            Report(missing);
            return loaded;
        }

        static GameObject Load(string path, List<string> missing)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
            if (!prefab)
                missing.Add(System.IO.Path.GetFileName(path));
            return prefab;
        }

        static void Report(List<string> missing)
        {
            if (missing.Count > 0)
                Debug.Log("[Props] palette entries the packs do not ship, skipped: " +
                          string.Join(", ", missing));
        }
    }
}
