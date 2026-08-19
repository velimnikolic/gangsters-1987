using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    // The airport demo's cupboard: where every Synty piece it uses lives, the few
    // helpers every builder file needs - loading through the AssetDatabase (editor
    // only, the road demo's way), measuring a prefab once, dropping a prop, running
    // a wall along an edge, stripping the pack's own scripts off a body the demo
    // drives itself - and the block alphabet the painted markings are drawn with.
    //
    // No Synty pack ships an airport, so the pieces here are borrowed by shape: the
    // gang pack's industrial shell is the hangars, the Generic Base kit and the
    // Plaza's glass are the terminal, the prison's watchtower cab is the control
    // tower, the police pack's fence is the wire. What none of them has - hangars at
    // aircraft scale, the tower, the windsock, the airfield lights, the ground
    // equipment - is baked out of those same pieces by Editor/AirportKitBash into
    // Assets/CityKit/Airport, exactly as the harbour bakes its freighters.
    public static class AirportKit
    {
        // ------------------------------------------------------------ folders
        public const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        public const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        public const string CityBld = "Assets/Synty/PolygonCity/Prefabs/Buildings/";
        public const string CityChars = "Assets/Synty/PolygonCity/Prefabs/Characters/";
        public const string CityVeh = "Assets/Synty/PolygonCity/Prefabs/Vehicles/";
        public const string PalmBld = "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/";
        public const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        public const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        public const string PalmVehAttach = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/Attachments/";
        public const string PalmChars = "Assets/Synty/PolygonPalmCity/Prefabs/Characters/";
        public const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        public const string PalmSigns = "Assets/Synty/PolygonPalmCity/Prefabs/Signs/";
        public const string PalmFx = "Assets/Synty/PolygonPalmCity/Prefabs/FX/";
        public const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        public const string GangProps = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        public const string GangVeh = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
        public const string GangChars = "Assets/Synty/PolygonGangWarfare/Prefabs/Character/";
        public const string GenBase = "Assets/Synty/PolygonGeneric/Prefabs/Base/";
        public const string GenProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        public const string GenEnv = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
        public const string GenChars = "Assets/Synty/PolygonGeneric/Prefabs/Characters/";
        public const string Plaza = "Assets/Synty/PolygonMapsPlaza/Prefabs/";
        public const string Prison = "Assets/Synty/PolygonMapsPrison/Prefabs/Building/";
        public const string PrisonProps = "Assets/Synty/PolygonMapsPrison/Prefabs/Props/";
        public const string PoliceBld = "Assets/Synty/PolygonPoliceStation/Prefabs/Buildings/";
        public const string PoliceProps = "Assets/Synty/PolygonPoliceStation/Prefabs/Props/";
        public const string PoliceVeh = "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/";
        public const string PoliceChars = "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/";
        public const string PoliceSigns = "Assets/Synty/PolygonPoliceStation/Prefabs/Signs/";
        public const string MilWarehouse = "Assets/Synty/PolygonMapsMilitaryWarehouse/Prefabs/";
        public const string NightProps = "Assets/Synty/PolygonNightclubs/Prefabs/Props/";
        public const string TownVeh = "Assets/Synty/PolygonTown/Prefabs/Vehicles/";
        public const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        public const string TownChars = "Assets/Synty/PolygonTown/Prefabs/Characters/";
        public const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        /// <summary>What Editor/AirportKitBash bakes: everything the packs do not have.</summary>
        public const string Kit = "Assets/CityKit/Airport/";

        // ------------------------------------------------------------ materials
        /// <summary>The palm city's tiling concrete and tarmac - what the ramp, the
        /// runway and the roads are laid in, one plane each rather than a grid of slabs
        /// (the harbour's rule: a working surface is not a chessboard).</summary>
        public const string ConcreteMat = "Assets/Synty/PolygonPalmCity/Materials/Buildings/Sidewalk_01.mat";
        public const string AsphaltMat = "Assets/Synty/PolygonPalmCity/Materials/Buildings/Road_Grey_01.mat";
        public const string GrassMat = "Assets/Synty/PolygonPalmCity/Materials/Env/Grass_Triplanar_01.mat";
        public const string GenericConcreteMat = "Assets/Synty/PolygonGeneric/Materials/Generic_Concrete.mat";
        public const string GenericPlasterMat = "Assets/Synty/PolygonGeneric/Materials/Generic_Plaster.mat";
        public const string GenericGlassMat = "Assets/Synty/PolygonGeneric/Materials/Generic_Glass.mat";
        /// <summary>The gang pack's industrial atlas: what the hangars are skinned in,
        /// so a generated roof reads as the same building as the walls under it.</summary>
        public const string MetalMat = "Assets/Synty/PolygonGangWarfare/Materials/Alts/PolygonGangWarfare_01_A.mat";

        // ------------------------------------------------------------ the fleet
        //
        // The aeroplanes - and ONLY the aeroplanes - come from the Simple Airport
        // pack. That pack is drawn in a different style from the Synty POLYGON packs
        // this project is built out of, so its people, cars, ground equipment,
        // buildings and props are never used: a Simple Airport bus parked beside a
        // Synty man reads as a mistake at a glance. Aircraft are the exception
        // because Synty has none worth the name (one floatplane in the whole
        // library), and an aeroplane is always out on the runway or the ramp, away
        // from the crowd and the street furniture, where the difference does not
        // show.
        //
        // Every model here is nose on +Z with its wheels named Wheel01/02 (mains) and
        // Wheel03 (nose), and is scaled at Play to the span AirportSpec gives its
        // class - the pack's import scale is not this project's.
        public const string SimpleVeh = "Assets/SimpleAirport/Prefabs/Vehicles/";

        /// <summary>The trijet that brings the morning flight in - a 727, near enough.</summary>
        public static readonly string[] Jets =
        {
            SimpleVeh + "Jet01.prefab",
            SimpleVeh + "Jet02.prefab",
            SimpleVeh + "Jet03.prefab",
        };
        /// <summary>The twinjet: the same job, one size down.</summary>
        public static readonly string[] SmallJets =
        {
            SimpleVeh + "Jet04.prefab",
            SimpleVeh + "Jet05.prefab",
        };
        /// <summary>The commuter turboprop that works the scheduled runs.</summary>
        public static readonly string[] Commuters =
        {
            SimpleVeh + "Plane_Propellor01.prefab",
            SimpleVeh + "Plane01.prefab",
            SimpleVeh + "Plane02.prefab",
            SimpleVeh + "Plane03.prefab",
        };
        /// <summary>The light singles that are most of what a field like this sees.</summary>
        public static readonly string[] LightPlanes =
        {
            SimpleVeh + "Small_Plane01.prefab",
            SimpleVeh + "Small_Plane02.prefab",
            SimpleVeh + "Small_Plane03.prefab",
            SimpleVeh + "Small_Plane04.prefab",
        };
        public static readonly string[] Helicopters =
        {
            SimpleVeh + "Small_Heli01.prefab",
            SimpleVeh + "Small_Heli02.prefab",
            SimpleVeh + "Small_Heli03.prefab",
        };
        public const string HelipadRaised = PoliceBld + "SM_Bld_HeliPad_01.prefab";
        public const string HelipadFlat = PalmProps + "SM_Prop_Helipad_01.prefab";

        // ------------------------------------------------------------ what the kit bakes
        public const string BoxHangar = Kit + "airport-hangar-box.prefab";
        /// <summary>The same shed with its doors slid aside: one in the row stands open
        /// with an aeroplane inside it.</summary>
        public const string BoxHangarOpen = Kit + "airport-hangar-box-open.prefab";
        public const string MaintHangar = Kit + "airport-hangar-maint.prefab";
        public const string Fbo = Kit + "airport-fbo.prefab";
        public const string Terminal = Kit + "airport-terminal.prefab";
        public const string Tower = Kit + "airport-tower.prefab";
        public const string Arff = Kit + "airport-arff.prefab";
        public const string CargoShed = Kit + "airport-cargo.prefab";
        public const string FuelFarm = Kit + "airport-fuel-farm.prefab";
        public const string GuardBooth = Kit + "airport-guard-booth.prefab";
        public const string Windsock = Kit + "airport-windsock.prefab";
        public const string Papi = Kit + "airport-papi.prefab";
        public const string TaxiSign = Kit + "airport-sign-taxi.prefab";
        public const string HoldSign = Kit + "airport-sign-hold.prefab";
        public const string ApronMast = Kit + "airport-apron-mast.prefab";
        public const string AirStairs = Kit + "airport-airstairs.prefab";
        /// <summary>The tall flight, for an airliner's door two and a half metres up.</summary>
        public const string AirStairsTall = Kit + "airport-airstairs-tall.prefab";
        public const string BaggageCart = Kit + "airport-baggage-cart.prefab";
        public const string FuelBowser = Kit + "airport-fuel-bowser.prefab";
        public const string Chock = Kit + "airport-chock.prefab";
        public static readonly string[] EdgeLights =
        {
            Kit + "airport-light-white.prefab",
            Kit + "airport-light-amber.prefab",
            Kit + "airport-light-green.prefab",
            Kit + "airport-light-red.prefab",
            Kit + "airport-light-blue.prefab",
        };
        public const int LightWhite = 0, LightAmber = 1, LightGreen = 2, LightRed = 3, LightBlue = 4;

        // ------------------------------------------------------------ the shell kit
        // the gang pack's industrial shed, on a 3 m module, 3 m to a course
        public const string MetalWall = GangBld + "SM_Bld_Wall_Metal_01.prefab";
        public const string MetalWallHalf = GangBld + "SM_Bld_Wall_Metal_Half_01.prefab";
        public const string MetalWallWindow = GangBld + "SM_Bld_Wall_Metal_Window_01.prefab";
        public const string MetalCorner = GangBld + "SM_Bld_Wall_Metal_Exterior_Corner_01.prefab";
        /// <summary>The six-metre sliding leaf: the one piece in any pack big enough to
        /// be a hangar door. Three of them side by side make an 18 m opening.</summary>
        public const string MetalDoorBig = GangBld + "SM_Bld_Wall_Metal_Door_Slide_02.prefab";
        public const string MetalDoor = GangBld + "SM_Bld_Wall_Metal_Door_Slide_01.prefab";
        public const string MetalManDoor = GangBld + "SM_Bld_Wall_Metal_Door_01.prefab";
        /// <summary>A roof bay: 3 m of frontage covering a 10 m span.</summary>
        public const string Roof = GangBld + "SM_Bld_Roof_01.prefab";
        public const string RoofEnd = GangBld + "SM_Bld_Roof_End_01.prefab";
        public const string RoofEndAlt = GangBld + "SM_Bld_Roof_End_02.prefab";
        public const string RoofConnector = GangBld + "SM_Bld_Roof_Connector_01.prefab";
        public const string RoofTruss = GangBld + "SM_Bld_Roof_Truss_01.prefab";
        public const string RoofBeam = GangBld + "SM_Bld_Roof_Beam_01.prefab";
        public const string WallBeam = GangBld + "SM_Bld_Wall_Beam_01.prefab";
        public const string LoadingDock = GangBld + "SM_Bld_LoadingDock_02.prefab";
        public const string Walkway = GangBld + "SM_Bld_Walkway_Single_01.prefab";

        // the Generic Base kit, on a 2.5 m module, 3.01 m to a storey
        public const string BaseWall = GenBase + "SM_Bld_Base_Wall_01.prefab";
        public const string BaseWallHalf = GenBase + "SM_Bld_Base_Wall_Half_01.prefab";
        public const string BaseDoor = GenBase + "SM_Bld_Base_Wall_Door_01.prefab";
        public const string BaseDoorDouble = GenBase + "SM_Bld_Base_Wall_Door_Double_01.prefab";
        public const string BaseWindow = GenBase + "SM_Bld_Base_Wall_Window_01.prefab";
        public const string BaseWindowDouble = GenBase + "SM_Bld_Base_Wall_Window_Double_01.prefab";
        public const string BaseFloor = GenBase + "SM_Bld_Base_Floor_01.prefab";
        public const string BaseCeiling = GenBase + "SM_Bld_Base_Ceiling_01.prefab";
        public const string BasePillar = GenBase + "SM_Bld_Base_Pillar_01.prefab";
        public const string BaseRoofTrim = GenBase + "SM_Bld_Base_Roof_Trim_01.prefab";
        public const string BaseStairs = GenBase + "SM_Bld_Base_Stairs_01.prefab";
        public const string GenBuilding = "Assets/Synty/PolygonGeneric/Prefabs/Building/";
        public const string BaseLadder = GenBuilding + "SM_Gen_Bld_Ladder_01.prefab";
        public const string BaseBeam = GenBuilding + "SM_Gen_Bld_Beam_01.prefab";
        public const string PipeStraight = GenBuilding + "SM_Gen_Bld_Pipe_Straight_01.prefab";
        public const string PipeCorner = GenBuilding + "SM_Gen_Bld_Pipe_Corner_01.prefab";

        // the plaza's glass, cut to the same Base module - the terminal's curtain wall
        public const string GlassWall = Plaza + "SM_Bld_Base_Wall_Glass_01.prefab";
        public const string GlassWallHalf = Plaza + "SM_Bld_Base_Wall_Glass_Half_01.prefab";
        public const string GlassDoor = Plaza + "SM_Bld_Base_Wall_Door_Glass_01.prefab";
        public const string PlazaPillar = Plaza + "SM_Bld_Pillar_01.prefab";
        public const string AwningCover = Plaza + "SM_Prop_Awning_Cover_01.prefab";
        public const string AwningPole = Plaza + "SM_Prop_Awning_Pole_01.prefab";
        public const string BusStop = Plaza + "SM_Prop_Bus_Stop_01.prefab";
        public const string PlazaBillboard = Plaza + "SM_Prop_Billboard_01.prefab";

        // the prison's watchtower: the only glazed cab on legs in any pack
        public const string TowerCab = Prison + "SM_Bld_Prison_Tower_Top_01.prefab";
        public const string TowerShaft = Prison + "SM_Bld_Prison_Tower_Middle_01.prefab";
        public const string TowerFoot = Prison + "SM_Bld_Prison_Tower_Bottom_01.prefab";
        public const string CorrugatedRoof = Prison + "SM_Bld_Roof_Corrugated_01.prefab";
        public const string CorrugatedEdge = Prison + "SM_Bld_Roof_Corrugated_Edge_01.prefab";

        // ------------------------------------------------------------ security
        public const string FencePanel = PoliceBld + "SM_Bld_Fence_01.prefab";
        public const string FencePillar = PoliceBld + "SM_Bld_Fence_Pillar_01.prefab";
        public const string FenceGate = PoliceBld + "SM_Bld_Fence_Gate_01.prefab";
        public const string FenceGateAlt = PoliceBld + "SM_Bld_Fence_Gate_02.prefab";
        /// <summary>The razor coil that rides the top of a panel - the only barbed wire
        /// in any pack we own. It wears the Military atlas stand-in, which reads as
        /// grey wire and is right for it.</summary>
        public const string BarbedWire = MilWarehouse + "SM_Prop_Barbed_Wire_01.prefab";
        public const string BoomGate = PalmProps + "SM_Prop_Barrier_Gate_01.prefab";
        public const string Bollard = PalmProps + "SM_Prop_Bollard_01.prefab";
        public const string BollardChain = PalmProps + "SM_Prop_Bollard_Chain_01.prefab";
        public const string Cone = PalmProps + "SM_Prop_Cone_01.prefab";
        public const string ConeAlt = PalmProps + "SM_Prop_Cone_02.prefab";
        public const string Barrier = PalmProps + "SM_Prop_Barrier_01.prefab";
        public const string SecurityCamera = PalmProps + "SM_Prop_Security_Camera_01.prefab";
        public const string BeltBarrier = PoliceProps + "SM_Prop_Belt_Barrier_01.prefab";
        public const string BeltBarrierPole = PoliceProps + "SM_Prop_Belt_Barrier_Pole_01.prefab";
        public const string MetalDetector = PoliceProps + "SM_Prop_Metal_Detector_01.prefab";
        public const string FlagStand = PoliceProps + "SM_Prop_Flag_Stand_01.prefab";
        public const string Antenna = PoliceProps + "SM_Prop_Antenna_01.prefab";
        public const string AntennaAlt = PoliceProps + "SM_Prop_Antenna_02.prefab";
        public const string SatDish = PalmProps + "SM_Prop_Satelite_Dish_02.prefab";
        public const string SatDishSmall = PalmProps + "SM_Prop_Satelite_Dish_01.prefab";
        public const string DangerSign = GangProps + "SM_Prop_Sign_Danger_01.prefab";

        // ------------------------------------------------------------ landside furniture
        public const string StreetLamp = PalmProps + "SM_Prop_Street_Lamp_01.prefab";
        public const string PierLamp = PalmProps + "SM_Prop_Pier_Lamp_01.prefab";
        public const string PayPhone = PalmProps + "SM_Prop_Pay_Phone_01.prefab";
        public const string Atm = PalmProps + "SM_Prop_ATM_01.prefab";
        public const string NewsStand = PalmProps + "SM_Prop_Newspaper_Stand_01.prefab";
        public const string TaxiStand = PalmProps + "SM_Prop_Taxi_Stand_01.prefab";
        public const string TrashBin = PalmProps + "SM_Prop_Trash_Bin_01.prefab";
        public const string Planter = PalmProps + "SM_Prop_Planter_01.prefab";
        public const string PlanterAlt = PalmProps + "SM_Prop_Planter_03.prefab";
        public const string BenchSeat = PalmProps + "SM_Prop_Bench_Seat_01.prefab";
        public const string Seating = PoliceProps + "SM_Prop_Seating_01.prefab";
        public const string Vending = PoliceProps + "SM_Prop_Vending_Machine_01.prefab";
        public const string Counter = PalmProps + "SM_Prop_Counter_01.prefab";
        public const string Reception = PoliceBld + "SM_Bld_Wall_Reception_01.prefab";
        public const string Desk = PoliceProps + "SM_Prop_Desk_01.prefab";
        public const string Clock = PoliceProps + "SM_Prop_Clock_01.prefab";
        public const string Screen = GenProps + "SM_Gen_Prop_Screen_01.prefab";
        public const string Whiteboard = PoliceProps + "SM_Prop_Whiteboard_01.prefab";
        public const string DuffleBag = PoliceProps + "SM_Prop_Duffle_Bag_01.prefab";
        public const string Briefcase = PoliceProps + "SM_Prop_Briefcase_01.prefab";
        public static readonly string[] Luggage =
        {
            PoliceProps + "SM_Prop_Duffle_Bag_01.prefab",
            PoliceProps + "SM_Prop_Briefcase_01.prefab",
            PalmProps + "SM_Prop_Bag_01.prefab",
            PalmProps + "SM_Prop_Bag_02.prefab",
            PalmProps + "SM_Prop_Bag_03.prefab",
        };
        public const string Pallet = GangProps + "SM_Prop_Pallet_01.prefab";
        public static readonly string[] Freight =
        {
            GangProps + "SM_Prop_CardboardBox_Stack_01.prefab",
            GangProps + "SM_Prop_CardboardBox_Stack_02.prefab",
            GenProps + "SM_Gen_Prop_Crate_Preset_01.prefab",
        };
        public const string BarrelMetal = GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        public const string LabTank = GangProps + "SM_Prop_Lab_Tank_03.prefab";
        public const string HoseReel = TownProps + "SM_Prop_HoseReel_01.prefab";
        public const string GasPump = TownProps + "SM_Prop_Gaspump_01.prefab";
        public const string GasPumpBase = TownProps + "SM_Prop_Gaspump_Base_01.prefab";
        public const string Ladder = GangProps + "SM_Prop_Ladder_01.prefab";
        public const string ToolBox = GangProps + "SM_Prop_Box_01.prefab";
        public const string Tree = CityEnv + "SM_Env_Tree_01.prefab";
        public static readonly string[] Trees =
        {
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Tree_03.prefab",
        };
        public static readonly string[] Bushes =
        {
            GenEnv + "SM_Gen_Env_Bush_01.prefab",
            GenEnv + "SM_Gen_Env_Bush_02.prefab",
            GenEnv + "SM_Gen_Env_Bush_03.prefab",
        };

        // ------------------------------------------------------------ signs
        public const string SignPlane = PalmSigns + "SM_Sign_Plane_01.prefab";
        public const string SignFuel = PalmSigns + "SM_Sign_Fuel_01.prefab";
        public const string SignAuthorized = PalmSigns + "SM_Sign_Authorized_Vehicles_01.prefab";
        public const string SignTaxi = PalmSigns + "SM_Sign_Taxi_01.prefab";
        public const string SignParking = PalmSigns + "SM_Sign_Parking_01.prefab";
        public const string SignInfo = PalmSigns + "SM_Sign_Info_01.prefab";
        public const string SignNoEntry = PalmSigns + "SM_Sign_No_Entry_01.prefab";
        public const string SignStop = PalmSigns + "SM_Sign_Stop_01.prefab";
        public const string SignOneWay = PalmSigns + "SM_Sign_One_Way_01.prefab";
        public static readonly string[] SignBlanks =
        {
            PalmSigns + "SM_Sign_Blank_01.prefab",
            PalmSigns + "SM_Sign_Blank_02.prefab",
            PalmSigns + "SM_Sign_Blank_03.prefab",
        };

        // ------------------------------------------------------------ ground vehicles
        public const string Forklift = GangVeh + "SM_Veh_Forklift_01.prefab";
        public const string GangTruck = GangVeh + "SM_Veh_Truck_01.prefab";
        public const string TownTruck = TownVeh + "SM_Veh_Truck_01.prefab";
        public const string TownDelivery = TownVeh + "SM_Veh_Truck_Delivery_01.prefab";
        public const string GolfCart = PalmVeh + "SM_Veh_Golf_Cart_01.prefab";
        public const string Trailer = PalmVehAttach + "SM_Veh_Trailer_01.prefab";
        public const string Pickup = PalmVeh + "SM_Veh_Pickup_01.prefab";
        public const string PickupWorks = PalmVeh + "SM_Veh_Pickup_01_Preset_Construction.prefab";
        public const string PickupLights = PalmVehAttach + "SM_Veh_Pickup_Flood_Lights_01.prefab";
        public const string PickupCones = PalmVehAttach + "SM_Veh_Pickup_Cones_01.prefab";
        public const string FireTruck = TownVeh + "SM_Veh_Firetruck_01.prefab";
        public const string Bus = TownVeh + "SM_Veh_Bus_01.prefab";
        public const string Ambulance = CityVeh + "SM_Veh_Car_Ambo_01.prefab";
        public const string Taxi = CityVeh + "SM_Veh_Car_Taxi_01.prefab";
        public const string PoliceCar = CityVeh + "SM_Veh_Car_Police_01.prefab";
        public const string Limousine = PalmVeh + "SM_Veh_Limousine_01.prefab";
        /// <summary>What parks landside and drives the approach road: 1987 saloons and
        /// vans only - no supercar, no electric anything (the outfit's own rule about
        /// anachronisms, kept here by hand because this demo has no VehicleCatalog).</summary>
        public static readonly string[] Cars =
        {
            CityVeh + "SM_Veh_Car_Sedan_01.prefab",
            CityVeh + "SM_Veh_Car_Medium_01.prefab",
            CityVeh + "SM_Veh_Car_Small_01.prefab",
            CityVeh + "SM_Veh_Car_Muscle_01.prefab",
            CityVeh + "SM_Veh_Car_Van_01.prefab",
            TownVeh + "SM_Veh_Pickup_01.prefab",
            TownVeh + "SM_Veh_Convertable_01.prefab",
            PalmVeh + "SM_Veh_Suv_01.prefab",
            PalmVeh + "SM_Veh_Van_01.prefab",
        };
        public static readonly string[] Lorries = { GangTruck, TownTruck, TownDelivery };

        // ------------------------------------------------------------ people
        /// <summary>The ramp: coveralls and work clothes. None of these is on the
        /// outfit's cast tables - a lineman is a man with a job, and a body the mob may
        /// be dealt has to stay off him.</summary>
        public static readonly string[] RampCrew =
        {
            GenChars + "SM_Gen_Chr_Jumpsuit_Male_01.prefab",
            GenChars + "SM_Gen_Chr_Jumpsuit_Female_01.prefab",
            GenChars + "SM_Gen_Chr_Street_Male_02.prefab",
            GenChars + "SM_Gen_Chr_Street_Male_03.prefab",
        };
        /// <summary>The flight deck: the palm city's sea captain is the only body in
        /// any pack wearing a peaked cap and a uniform shirt.</summary>
        public static readonly string[] Pilots =
        {
            PalmChars + "SM_Chr_SeaCaptain_Male_01.prefab",
            PalmChars + "SM_Chr_SeaCaptain_Female_01.prefab",
        };
        public static readonly string[] Passengers =
        {
            GenChars + "SM_Gen_Chr_Business_Male_01.prefab",
            GenChars + "SM_Gen_Chr_Business_Female_01.prefab",
            GenChars + "SM_Gen_Chr_Street_Male_01.prefab",
            GenChars + "SM_Gen_Chr_Street_Female_01.prefab",
            GenChars + "SM_Gen_Chr_Street_Female_02.prefab",
            CityChars + "Character_BusinessMan_Suit.prefab",
            CityChars + "Character_BusinessMan_Shirt.prefab",
            CityChars + "Character_BusinessWoman.prefab",
            CityChars + "Character_Male_Jacket.prefab",
            CityChars + "Character_Female_Jacket.prefab",
            CityChars + "Character_Female_Coat.prefab",
            PalmChars + "SM_Chr_City_Male_01.prefab",
            PalmChars + "SM_Chr_City_Female_01.prefab",
            PalmChars + "SM_Chr_Rich_Male_01.prefab",
            PalmChars + "SM_Chr_Rich_Female_01.prefab",
            TownChars + "SM_Chr_Father_01.prefab",
            TownChars + "SM_Chr_Mother_01.prefab",
        };
        public static readonly string[] Officers =
        {
            PoliceChars + "SM_Chr_Officer_Male_01.prefab",
            PoliceChars + "SM_Chr_Officer_Female_01.prefab",
            PoliceChars + "SM_Chr_Officer_Male_02.prefab",
        };
        /// <summary>The federal interest in a 1987 county field with a night ramp.</summary>
        public static readonly string[] Agents =
        {
            GangChars + "SM_Chr_DEA_Plainclothes_Male_01.prefab",
            GangChars + "SM_Chr_DEA_Agent_Male_01.prefab",
            PalmChars + "SM_Chr_Detective_Male_01.prefab",
        };
        public const string PalmCharAttach = "Assets/Synty/PolygonPalmCity/Prefabs/Characters/Attachments/";
        public const string HiVis = PalmCharAttach + "SM_Chr_Attach_LifeJacket_01.prefab";

        // ------------------------------------------------------------ FX
        public const string FxPropWash = Fx + "FX_Dust_Blowing_Soft_01.prefab";
        public const string FxTouchdown = Fx + "FX_Dust_Small_01.prefab";
        public const string FxExhaust = Fx + "FX_Trail_Exhaust_01.prefab";
        public const string FxSmoke = Fx + "FX_Smoke_White_Small_01.prefab";
        public const string FxBirds = PalmFx + "FX_Birds_01.prefab";

        // ------------------------------------------------------------ loading

        public static GameObject Load(string path)
        {
#if UNITY_EDITOR
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning("[AirportDemo] missing prefab " + path);
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

        /// <summary>Paint. The markings are not a Synty asset in any pack - no road
        /// decal is drawn at runway scale - so they are flat URP colour on quads, which
        /// is what airfield paint is.</summary>
        public static Material Flat(string name, Color colour, float smoothness = 0.08f)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        /// <summary>A lamp lens: unlit colour that reads at night without a real light
        /// behind it (there are 130 of them round the field).</summary>
        public static Material Lens(string name, Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
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
            // some packs author a piece with its root switched off - an alternate you
            // are meant to enable. Instantiating one gives an invisible object, and it
            // is invisible in a way that measures perfectly (see HasVisibleGeometry).
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        /// <summary>Whether this prefab will actually draw: at least one renderer with
        /// a mesh, ENABLED, on a GameObject that is active. PrefabBounds deliberately
        /// measures inactive geometry too, so a piece can measure 2.5 x 3.0 m and still
        /// put nothing on the screen or into a bake - which is exactly how the terminal
        /// lost its glazed wall.</summary>
        public static bool HasVisibleGeometry(GameObject prefab)
        {
            if (prefab == null) return false;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return true;
            }
            return false;
        }

        /// <summary>A prop set down so its own underside rests on the given point,
        /// whatever its pivot.</summary>
        public static GameObject Sit(GameObject prefab, Vector3 at, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var b = PrefabBounds(prefab);
            return Prop(prefab, new Vector3(at.x, at.y - b.min.y, at.z), yaw, parent, name);
        }

        /// <summary>A prop set down centred on the point, its underside on it - for the
        /// pieces whose pivot is in the middle of the footprint.</summary>
        public static GameObject Centre(GameObject prefab, Vector3 at, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var b = PrefabBounds(prefab);
            var go = Prop(prefab, at, yaw, parent, name);
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var offset = turn * new Vector3(b.center.x, b.min.y, b.center.z);
            go.transform.position = at - offset;
            return go;
        }

        // ------------------------------------------------------------ runs
        //
        // Wall-like pieces run from their pivot along one horizontal axis. Which way
        // is read off the prefab's own bounds rather than assumed: Unity mirrors X on
        // import, so a piece the FBX draws from 0 to +3 arrives running 0 to -3.

        public static Vector3 RunOf(GameObject prefab) => RunOf(prefab, out _);

        /// <summary>The run vector, and whether the piece's pivot sits at its middle
        /// (a door in a frame) rather than at one end.</summary>
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
        /// when <paramref name="fit"/> is set. A centre-pivoted piece is set at the
        /// segment's middle instead.</summary>
        public static GameObject PlaceRun(GameObject prefab, Vector3 from, Vector3 to, Transform parent, bool fit = false, string name = null)
        {
            if (prefab == null) return null;
            var run = RunOf(prefab, out bool centred);
            var want = to - from;
            want.y = 0f;
            if (run.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-6f) return null;
            float yaw = Vector3.SignedAngle(run, want, Vector3.up);
            var at = centred ? (from + to) * 0.5f : from;
            at.y = from.y;
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), parent);
            go.name = name ?? prefab.name;
            if (!go.activeSelf) go.SetActive(true);
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

        /// <summary>The segment A-B filled with modules of the piece, evenly stretched
        /// so the last one lands exactly on B. Returns the number laid.</summary>
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

        /// <summary>The body as geometry only: the packs' vehicles carry colliders and
        /// rigidbodies, the people crowd scripts and controllers. The demo moves all of
        /// them itself.</summary>
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

        /// <summary>Every child whose name contains the token.</summary>
        public static void FindAllDeep(Transform t, string token, List<Transform> into)
        {
            foreach (Transform c in t)
            {
                if (c.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0) into.Add(c);
                FindAllDeep(c, token, into);
            }
        }

        public static float Range(System.Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        public static T Pick<T>(System.Random rng, IList<T> list) => list == null || list.Count == 0 ? default : list[rng.Next(list.Count)];

        // ------------------------------------------------------------ the block alphabet
        //
        // Runway designators, taxiway signs and the fire station's door number are
        // painted from one five-by-seven block face: each glyph is seven rows of five
        // cells, and a run of set cells in a row becomes one quad. Crude next to the
        // FAA's own stencil, but at sixty feet tall and read from the air it is the
        // same thing - a number on the tarmac.
        public static class Glyph
        {
            public const int Cols = 5, Rows = 7;

            static readonly Dictionary<char, string> Faces = new Dictionary<char, string>
            {
                ['0'] = "01110" + "10001" + "10011" + "10101" + "11001" + "10001" + "01110",
                ['1'] = "00100" + "01100" + "00100" + "00100" + "00100" + "00100" + "01110",
                ['2'] = "01110" + "10001" + "00001" + "00010" + "00100" + "01000" + "11111",
                ['3'] = "11111" + "00010" + "00100" + "00010" + "00001" + "10001" + "01110",
                ['4'] = "00010" + "00110" + "01010" + "10010" + "11111" + "00010" + "00010",
                ['5'] = "11111" + "10000" + "11110" + "00001" + "00001" + "10001" + "01110",
                ['6'] = "00110" + "01000" + "10000" + "11110" + "10001" + "10001" + "01110",
                ['7'] = "11111" + "00001" + "00010" + "00100" + "01000" + "01000" + "01000",
                ['8'] = "01110" + "10001" + "10001" + "01110" + "10001" + "10001" + "01110",
                ['9'] = "01110" + "10001" + "10001" + "01111" + "00001" + "00010" + "01100",
                ['A'] = "01110" + "10001" + "10001" + "11111" + "10001" + "10001" + "10001",
                ['B'] = "11110" + "10001" + "10001" + "11110" + "10001" + "10001" + "11110",
                ['L'] = "10000" + "10000" + "10000" + "10000" + "10000" + "10000" + "11111",
                ['R'] = "11110" + "10001" + "10001" + "11110" + "10100" + "10010" + "10001",
                ['-'] = "00000" + "00000" + "00000" + "11111" + "00000" + "00000" + "00000",
                [' '] = "00000" + "00000" + "00000" + "00000" + "00000" + "00000" + "00000",
            };

            public static bool Known(char c) => Faces.ContainsKey(char.ToUpperInvariant(c));

            /// <summary>Whether the cell at (column, row) of this glyph is inked. Row 0
            /// is the top of the figure.</summary>
            public static bool On(char c, int col, int row)
            {
                if (!Faces.TryGetValue(char.ToUpperInvariant(c), out var face)) return false;
                if (col < 0 || col >= Cols || row < 0 || row >= Rows) return false;
                return face[row * Cols + col] == '1';
            }

            /// <summary>The horizontal runs of inked cells in one row, as (from, to)
            /// column pairs - one quad each, so a figure is a handful of rectangles
            /// rather than thirty-five.</summary>
            public static void RowRuns(char c, int row, List<Vector2Int> into)
            {
                into.Clear();
                int start = -1;
                for (int i = 0; i <= Cols; i++)
                {
                    bool on = i < Cols && On(c, i, row);
                    if (on && start < 0) start = i;
                    else if (!on && start >= 0) { into.Add(new Vector2Int(start, i)); start = -1; }
                }
            }
        }
    }
}
