using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The complete fire-station amenity shared by Core and its review scene. The block is
    /// authored in one local frame: +Z is the street, the engine hall occupies the left side,
    /// the firefighters' quarters the right, and the full foreground is working apron and
    /// staff parking. Compose at the origin, then move/turn the finished root onto its parcel.
    /// </summary>
    public static class FireStationBlock
    {
        public const float BlockFrontage = 50f;
        public const float BlockDepth = 35f;

        public static readonly Rect PreviewBounds = Rect.MinMaxRect(
            -BlockFrontage * 0.5f, -BlockDepth * 0.5f,
             BlockFrontage * 0.5f,  BlockDepth * 0.5f);

        /// <summary>
        /// The half depth the BLOCK is squared up to. Everything here is composed around
        /// the block's own centre, so it is the half measurement that has to land on the
        /// city's five-metre raster, and 17.5 m does not: a parcel whose edge falls at a
        /// cell midpoint cannot be kerbed by CorePavement without leaving a 2.5 m strip
        /// of bare ground inside its own kerb. Half the frontage (25 m) already lands.
        /// <see cref="ComposeBlock"/> pays for the difference with a shallow apron skirt;
        /// the city's own parcel reservation still uses the authored
        /// <see cref="BlockDepth"/>.
        /// </summary>
        public const float ParcelHalfDepth = 20f;

        /// <summary>Deliberately the central generator's pavement width, so this block's
        /// kerb line matches the fuel-station block standing beside it.</summary>
        public const float PavementWidth = CoreBlockMetrics.PavementWidth;

        /// <summary>The station's own ground, squared to the raster.</summary>
        public static readonly Rect ParcelBounds = Rect.MinMaxRect(
            -BlockFrontage * 0.5f, -ParcelHalfDepth,
             BlockFrontage * 0.5f,  ParcelHalfDepth);

        /// <summary>The kerb the appliances cross on their way out, and what the street
        /// beyond the block is measured from.</summary>
        public const float KerbZ = ParcelHalfDepth + PavementWidth;

        /// <summary>The whole block: the parcel and its generated pavement ring.</summary>
        public static readonly Rect BlockBounds = Rect.MinMaxRect(
            ParcelBounds.xMin - PavementWidth, ParcelBounds.yMin - PavementWidth,
            ParcelBounds.xMax + PavementWidth, ParcelBounds.yMax + PavementWidth);

        /// <summary>How much of the frontage the crossover opens, each side of centre.
        /// Both outer corner cells stay kerbed pavement so the block still reads as a
        /// block; everything between them is the working apron that the three engine
        /// runs and the staff bays back out over. A fire station's front IS a crossing.</summary>
        const float ApronMouthHalfX = 20f;

        const float ApronY = -0.025f;

        public const string ShellPath =
            "Assets/CityKit/Buildings/building-firestation.prefab";

        const string RoadTile =
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Road_Bare_01.prefab";
        const string FireTruck =
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Firetruck_01.prefab";
        const string EngineDoor =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/" +
            "SM_Bld_Wall_Metal_Door_Slide_02.prefab";
        const string Sedan =
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Sedan_01.prefab";
        const string MediumCar =
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Medium_01.prefab";
        const string Pickup =
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01.prefab";
        const string Hydrant =
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_Hydrant_01.prefab";
        const string Cone =
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_Cone_01.prefab";

        // A fire station should read as a place that works around the clock, not as a clean
        // architectural model. These are deliberately grouped by use below: apparatus-ready
        // kit beside the doors, a dense service strip, crew-life props, training gear and a
        // proper roofscape. All of it is Synty and all three engine runs stay unobstructed.
        const string GangProps = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        const string PoliceProps = "Assets/Synty/PolygonPoliceStation/Prefabs/Props/";
        const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        const string TownItems = "Assets/Synty/PolygonTown/Prefabs/Items/";

        const string Dumpster = PoliceProps + "SM_Prop_Dumpster_01.prefab";
        const string Pallet = GangProps + "SM_Prop_Pallet_01.prefab";
        const string MetalBarrel = GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        const string PlasticBarrel = GangProps + "SM_Prop_Barrel_Plastic_02.prefab";
        const string Propane = GangProps + "SM_Prop_Propane_Tall_01.prefab";
        const string PipeStack = GangProps + "SM_Prop_PipeStack_01.prefab";
        const string WarehouseRack = GangProps + "SM_Prop_Warehouse_Rack_01.prefab";
        const string PortableLight = GangProps + "SM_Prop_Light_Portable_01.prefab";
        const string PowerBox = GangProps + "SM_Prop_Powerbox_01.prefab";
        const string CardboardStack = GangProps + "SM_Prop_CardboardBox_Stack_01.prefab";
        const string OpenBox = GangProps + "SM_Prop_CardboardBox_Open_03.prefab";

        const string HoseReel = TownProps + "SM_Prop_HoseReel_01.prefab";
        const string Ladder = TownProps + "SM_Prop_Ladder_01.prefab";
        const string ToolCabinet = TownProps + "SM_Prop_ToolCabinet_01.prefab";
        const string ToolBoard = TownProps + "SM_Prop_ToolBoard_01_Combined.prefab";
        const string Workbench = TownProps + "SM_Prop_Workbench_01.prefab";
        const string WorkShelf = TownProps + "SM_Prop_WorkShelf_01_Combined.prefab";
        const string ToolBox = TownItems + "SM_Item_ToolBox_01.prefab";
        const string Wrench = TownItems + "SM_Item_Wrench_01.prefab";

        const string Extinguisher = PoliceProps + "SM_Prop_Fire_Extinguisher_01.prefab";
        const string Bench = PoliceProps + "SM_Prop_Bench_01.prefab";
        const string BikeStand = PoliceProps + "SM_Prop_Bike_Stand_01.prefab";
        const string Bin = PoliceProps + "SM_Prop_Bin_01.prefab";
        const string WaterCooler = PoliceProps + "SM_Prop_WaterCooler_01.prefab";
        const string VendingMachine = PoliceProps + "SM_Prop_Vending_Machine_01.prefab";
        const string TrainingMat = PoliceProps + "SM_Prop_Training_Mat_01.prefab";
        const string TrainingDummy = PoliceProps + "SM_Prop_Training_Dummy_01.prefab";
        const string DumbbellRack = PoliceProps + "SM_Prop_Dumbbell_Rack_01.prefab";
        const string Flag = PoliceProps + "SM_Prop_Flag_Stand_01.prefab";
        const string Streetlight = PoliceProps + "SM_Prop_Streetlight_01.prefab";
        const string DownPipe = PoliceProps + "SM_Prop_DownPipe_01.prefab";

        const string RoofAirconA = PoliceProps + "SM_Prop_Aircon_Roof_01.prefab";
        const string RoofAirconB = PoliceProps + "SM_Prop_Aircon_Roof_02.prefab";
        const string RoofAirconC = PoliceProps + "SM_Prop_Aircon_Roof_03.prefab";
        const string RoofAntennaA = PoliceProps + "SM_Prop_Antenna_01.prefab";
        const string RoofAntennaB = PoliceProps + "SM_Prop_Antenna_02.prefab";
        const string SatelliteDish = TownProps + "SM_Prop_SatelliteDish_01.prefab";

        const string WhitePaint =
            "Assets/CityKit/FireStation/firestation-white.mat";
        const string RedPaint =
            "Assets/CityKit/FireStation/firestation-red.mat";

        const float ShellZ = -7.5f;
        const float PaintY = 0.035f;

        public sealed class Stood
        {
            readonly List<GameObject> _vehicles = new List<GameObject>();
            readonly List<GameObject> _bayDoors = new List<GameObject>();

            public GameObject Shell { get; internal set; }
            public IReadOnlyList<GameObject> Vehicles => _vehicles;
            public IReadOnlyList<GameObject> BayDoors => _bayDoors;
            internal List<GameObject> MutableVehicles => _vehicles;
            internal List<GameObject> MutableBayDoors => _bayDoors;
            public int FireEngines { get; internal set; }
            public int StaffCars { get; internal set; }
            public int DetailProps { get; internal set; }
            public int HangarDoors => _bayDoors.Count;

            /// <summary>Nought unless the station was stood by <see cref="ComposeBlock"/>.</summary>
            public int PavementTiles { get; internal set; }
            public int DriveCells { get; internal set; }
            public string PavementReport { get; internal set; }

            public override string ToString() =>
                $"three-bay engine hall, firefighters' quarters, {FireEngines} fire " +
                $"engine(s), {StaffCars} staff car(s), {HangarDoors} working hangar doors, " +
                $"{DetailProps} operational props, marked front apron" +
                (PavementTiles > 0
                    ? $", {PavementTiles} generated pavement tile(s) and a " +
                      $"{DriveCells}-cell apron crossover"
                    : "");
        }

        /// <summary>Stand the block with the caller's chosen prefab instantiator.</summary>
        public static Stood Compose(
            Transform root, Func<GameObject, Transform, GameObject> stand)
        {
            var stood = new Stood();
            if (root == null || stand == null) return stood;

            Composer.ForgetMissing();
            Composer.Begin(stand);

            var surface = new GameObject("Fire Station Asphalt Apron").transform;
            surface.SetParent(root, false);
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 7; j++)
                {
                    var tile = Composer.Lay(
                        RoadTile, surface,
                        PreviewBounds.xMin + i * Composer.Cell,
                        PreviewBounds.yMin + j * Composer.Cell,
                        Composer.Cell, Composer.Cell, 0f, y: ApronY);
                    SetStatic(tile);
                }

            var buildings = new GameObject("Engine Hall and Firefighters' Quarters").transform;
            buildings.SetParent(root, false);
            stood.Shell = Composer.Stand(ShellPath, buildings, 0f, ShellZ, 0f);
            if (stood.Shell != null)
            {
                stood.Shell.name = "Fire Station - Engine Hall and Crew Quarters";
                SetStatic(stood.Shell);
            }

            // The shell owns the brick jambs and lintels but deliberately leaves the three
            // 6 x 6 m portals open. Each shutter is a separate top-pivoted hierarchy so the
            // Residential runtime can roll it up without deforming or rebaking the building.
            var doors = new GameObject("Working Apparatus Bay Doors").transform;
            doors.SetParent(root, false);
            HangDoor(stood, doors, "Hangar Door 01", -15f, 0.08f);
            HangDoor(stood, doors, "Hangar Door 02", -6f, 0.08f);
            HangDoor(stood, doors, "Hangar Door 03", 3f, 0.08f);

            var vehicles = new GameObject("Parked Fire Station Vehicles").transform;
            vehicles.SetParent(root, false);
            Park(stood, FireTruck, vehicles, "Fire Engine 01", -15f, 7.2f, 0f, engine: true);
            Park(stood, FireTruck, vehicles, "Fire Engine 02", -6f, 7.2f, 0f, engine: true);
            // The third appliance bay is deliberately kept clear: a working station should
            // have somewhere an engine can return to instead of reading as a vehicle showroom.
            Park(stood, Sedan, vehicles, "Firefighters' Car 01", 11.2f, 8.1f, 180f);
            Park(stood, MediumCar, vehicles, "Firefighters' Car 02", 15.2f, 8.1f, 180f);
            Park(stood, Pickup, vehicles, "Firefighters' Pickup", 19.6f, 8.1f, 180f);

            var equipment = new GameObject("Fire Station Yard Equipment").transform;
            equipment.SetParent(root, false);
            Sit(Hydrant, equipment, "Fire Hydrant", 23.1f, 1.2f, 0f);
            Sit(Cone, equipment, "Empty Bay Cone 01", 1.7f, 6.2f, 0f);
            Sit(Cone, equipment, "Empty Bay Cone 02", 4.3f, 6.2f, 0f);
            Sit(Cone, equipment, "Apron Cone 01", -20f, 13.5f, 0f);
            Sit(Cone, equipment, "Apron Cone 02", 7.7f, 13.5f, 0f);

            DressStation(stood, root);
            PaintApron(root);
            return stood;
        }

        public static Stood Compose(Transform root) =>
            Compose(root, (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent));

        /// <summary>
        /// The station as an ordinary city block: the parcel <see cref="Compose"/> stands,
        /// squared to the five-metre raster and wrapped in the same generated CorePavement
        /// ring the rest of the city is kerbed with, with one declared crossover across its
        /// frontage so the appliances are not walled in by their own pavement (the lesson
        /// the police station already taught: "nema ukljucenje jer si je okruzio trotoarom").
        ///
        /// Compose at the origin and translate the finished root: the shared Synty helpers
        /// measure in world space while they stand their children, and CorePavement lays
        /// its raster from the world origin.
        /// </summary>
        public static Stood ComposeBlock(
            Transform root, int seed, Func<GameObject, Transform, GameObject> stand)
        {
            var stood = Compose(root, stand);
            if (root == null || stand == null) return stood;

            Composer.Begin(stand);
            SquareTheApron(root);

            var pavement = new GameObject("Generated City Pavement").transform;
            pavement.SetParent(root, false);
            var plan = PavementPlan(stood);
            stood.PavementTiles = CorePavement.Lay(
                plan, stand, pavement, out string report,
                y: ApronY, seed: seed * 809 + 137,
                ramps: true, under: false, props: true);
            stood.PavementTiles -= ClearTheMouth(pavement);
            stood.PavementReport = report;
            stood.DriveCells = plan.DriveCells;
            return stood;
        }

        public static Stood ComposeBlock(Transform root, int seed) =>
            ComposeBlock(root, seed, (prefab, parent) =>
                UnityEngine.Object.Instantiate(prefab, parent));

        /// <summary>The 2.5 m of apron at the front and the back that carries the authored
        /// 35 m parcel out to <see cref="ParcelBounds"/>. Without it the pavement ring
        /// would be laid against a cell midpoint and the block would show bare ground
        /// inside its own kerb.</summary>
        static void SquareTheApron(Transform root)
        {
            float skirt = ParcelHalfDepth - BlockDepth * 0.5f;
            if (skirt <= 0.001f) return;

            var surface = new GameObject("Fire Station Apron - Block Skirt").transform;
            surface.SetParent(root, false);
            foreach (float minZ in new[] { -ParcelHalfDepth, PreviewBounds.yMax })
                for (int i = 0; i < 10; i++)
                {
                    var tile = Composer.Lay(
                        RoadTile, surface,
                        PreviewBounds.xMin + i * Composer.Cell, minZ,
                        Composer.Cell, skirt, 0f, y: ApronY);
                    SetStatic(tile);
                }
        }

        /// <summary>Anything shorter than this on a pavement is its surface; anything
        /// taller is furniture standing on it.</summary>
        const float TileProud = 0.5f;

        /// <summary>The ground the appliances cross, which nothing may stand on: the
        /// crossover itself, from the apron's edge out to the kerb.</summary>
        static readonly Rect MouthBounds = Rect.MinMaxRect(
            -ApronMouthHalfX, ParcelBounds.yMax,
             ApronMouthHalfX, BlockBounds.yMax);

        /// <summary>
        /// Two things the generator gets right for an ordinary block and wrong for this one.
        ///
        /// It kerbs the inner end of every driveway, because a generic drive runs between two
        /// pavements; this one joins the station's own apron, so that row would stand as a
        /// kerb across all three engine runs. And it furnishes every kerb it lays, so the
        /// lamp on that row ends up planted in the middle of the appliance exit.
        ///
        /// So: the parcel carries its authored asphalt and takes no tile of any kind, and
        /// the crossover carries nothing that stands proud of the ground. A fire station's
        /// way out is kept clear (the user, 2026-09-05: "ulaz na parking se preplice s
        /// trotoarom i ne sme da ima props na ulazu").
        /// </summary>
        static int ClearTheMouth(Transform pavement)
        {
            var remove = new List<GameObject>();
            foreach (Transform child in pavement)
            {
                var renderer = child.GetComponentInChildren<Renderer>();
                if (renderer == null) continue;
                var box = renderer.bounds;
                var footprint = new Rect(box.min.x, box.min.z, box.size.x, box.size.z);

                // By its CENTRE, not its footprint: a band tile laid hard against the
                // parcel touches it on the seam, and a rounding error there would strip
                // the block's own pavement.
                bool onTheApron = ParcelBounds.Contains(
                    new Vector2(box.center.x, box.center.z));
                bool blocksTheMouth = box.size.y > TileProud && MouthBounds.Overlaps(footprint);
                if (onTheApron || blocksTheMouth) remove.Add(child.gameObject);
            }

            for (int i = 0; i < remove.Count; i++)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(remove[i]);
                else UnityEngine.Object.DestroyImmediate(remove[i]);
            }
            return remove.Count;
        }

        static CorePavement.Plan PavementPlan(Stood stood)
        {
            // Measured, never typed: the hall and the crew wing are the one thing no
            // driveway may be routed through, and only the raised shell knows where
            // they actually stand.
            var roofs = new List<Bounds>();
            if (stood.Shell != null) roofs.Add(BoundsOf(stood.Shell));

            var gate = new Bounds(
                new Vector3(0f, 0f, ParcelBounds.yMax - CorePavement.Cell * 0.5f),
                new Vector3(ApronMouthHalfX * 2f, 1f, CorePavement.Cell));

            return CorePavement.Around(
                new[] { Box(ParcelBounds) }, band: CoreBlockMetrics.PavementTiles,
                roofs: roofs, gates: new[] { gate });
        }

        static Bounds Box(Rect rect) =>
            new Bounds(new Vector3(rect.center.x, 0f, rect.center.y),
                       new Vector3(rect.width, 1f, rect.height));

        static void Park(Stood stood, string path, Transform parent, string name,
                         float x, float z, float yaw, bool engine = false)
        {
            var go = Composer.Sit(path, parent, x, z, yaw);
            if (go == null) return;
            go.name = name;
            SetStatic(go);
            stood.MutableVehicles.Add(go);
            if (engine) stood.FireEngines++;
            else stood.StaffCars++;
        }

        static void HangDoor(
            Stood stood, Transform parent, string name, float x, float z)
        {
            var panel = Composer.Sit(EngineDoor, parent, x, z, 0f);
            if (panel == null) return;

            var bounds = BoundsOf(panel);
            var hanger = new GameObject(name);
            hanger.transform.SetParent(parent, false);
            hanger.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                panel.transform.rotation);
            panel.transform.SetParent(hanger.transform, true);
            panel.name = "Roller Shutter Panel";
            SetStatic(hanger);
            stood.MutableBayDoors.Add(hanger);
        }

        static void Sit(string path, Transform parent, string name,
                        float x, float z, float yaw, float y = 0f)
        {
            var go = Composer.Sit(path, parent, x, z, yaw, y);
            if (go == null) return;
            go.name = name;
            SetStatic(go);
        }

        static void DressStation(Stood stood, Transform root)
        {
            var detail = new GameObject("Fire Station Operational Detail").transform;
            detail.SetParent(root, false);

            // Tight against the outside of the hall: this is the dirty working strip. It is
            // intentionally dense but never crosses x = -19.2, leaving the first appliance
            // corridor and every point used by FireStationBlockRuntime untouched.
            var service = Cluster(detail, "Service Yard - Maintenance and Stores");
            Detail(stood, Dumpster, service, "Service Dumpster", -23.0f, -12.3f, 90f);
            Detail(stood, Pallet, service, "Hose Pallet 01", -23.1f, -9.2f, 90f);
            Detail(stood, Pallet, service, "Hose Pallet 02", -22.2f, -8.2f, 0f);
            Detail(stood, PipeStack, service, "Spare Pipe Stack", -22.8f, -6.4f, 90f);
            Detail(stood, WarehouseRack, service, "Rescue Equipment Rack", -22.5f, -3.9f, 90f);
            Detail(stood, WorkShelf, service, "Loaded Work Shelf", -22.5f, -1.4f, 90f);
            Detail(stood, Workbench, service, "Maintenance Workbench", -22.4f, 1.2f, 90f);
            Detail(stood, ToolCabinet, service, "Red Tool Cabinet", -22.5f, 3.7f, 90f);
            Detail(stood, ToolBoard, service, "Mounted Tool Board", -22.5f, 5.5f, 90f);
            Detail(stood, HoseReel, service, "Spare Hose Reel", -22.4f, 7.2f, 90f);
            Detail(stood, Ladder, service, "Extension Ladder", -23.0f, 8.9f, 0f);
            Detail(stood, MetalBarrel, service, "Foam Barrel", -23.3f, 10.5f, 0f);
            Detail(stood, PlasticBarrel, service, "Water Barrel", -21.8f, 10.8f, 12f);
            Detail(stood, Propane, service, "Workshop Gas Bottle", -23.1f, 12.3f, 0f);
            Detail(stood, CardboardStack, service, "Supply Cartons", -21.5f, -11.0f, 8f);
            Detail(stood, OpenBox, service, "Open Parts Box", -21.2f, -8.3f, -12f);
            Detail(stood, PowerBox, service, "External Power Box", -22.6f, -14.6f, 90f);

            // Gear close to the doors, but only in the masonry islands between vehicle runs.
            var ready = Cluster(detail, "Apparatus Ready Line");
            Detail(stood, Extinguisher, ready, "Bay Extinguisher 01", -19.7f, 0.75f, 180f);
            Detail(stood, Extinguisher, ready, "Bay Extinguisher 02", 7.4f, 0.75f, 180f);
            Detail(stood, PortableLight, ready, "Portable Scene Light", -19.8f, 2.5f, 25f);
            Detail(stood, ToolBox, ready, "Ready Tool Box", -19.5f, 4.0f, -8f);
            Detail(stood, Wrench, ready, "Workbench Wrench", -22.0f, 1.2f, 90f, 1.03f);
            Detail(stood, DownPipe, ready, "Hall Downpipe", -20.9f, 0.25f, 180f);
            Detail(stood, DownPipe, ready, "Crew Wing Downpipe", 20.9f, 0.25f, 180f);

            // Firefighters live here. This narrow side court gets the ordinary human clutter
            // that distinguishes the crew wing from another anonymous municipal office.
            var crew = Cluster(detail, "Crew Side Court");
            Detail(stood, Bench, crew, "Crew Bench", 22.7f, -12.1f, 270f);
            Detail(stood, BikeStand, crew, "Firefighter Bike Rack", 22.7f, -9.0f, 90f);
            Detail(stood, Bin, crew, "Crew Recycling Bin", 23.2f, -6.8f, 0f);
            Detail(stood, Bin, crew, "Crew General Bin", 21.9f, -6.8f, 0f);
            Detail(stood, VendingMachine, crew, "Crew Vending Machine", 22.7f, -4.2f, 270f);
            Detail(stood, WaterCooler, crew, "Crew Water Cooler", 22.7f, -2.1f, 270f);
            Detail(stood, Flag, crew, "Fire Department Flag", 7.4f, 2.6f, 0f);
            Detail(stood, Streetlight, crew, "Apron Floodlight Left", -23.6f, 14.4f, 0f);
            Detail(stood, Streetlight, crew, "Apron Floodlight Right", 24.0f, 15.8f, 180f);

            // A compact physical-training corner fills the otherwise dead outer apron edge.
            // Its nearest point remains more than a metre outside the first bay stripe.
            var training = Cluster(detail, "Firefighter Training Corner");
            Detail(stood, TrainingMat, training, "Training Mat", -22.0f, 13.8f, 0f);
            Detail(stood, TrainingDummy, training, "Rescue Training Dummy", -21.7f, 13.8f, 0f);
            Detail(stood, DumbbellRack, training, "Strength Rack", -23.2f, 16.0f, 90f);

            // Broad low roofs look unfinished without mechanical plant. Different silhouettes
            // make the hall and the taller crew wing read as separate working volumes.
            var roof = Cluster(detail, "Roof Plant and Communications");
            Detail(stood, RoofAirconA, roof, "Hall HVAC 01", -15.5f, -10.5f, 0f, 6.12f);
            Detail(stood, RoofAirconB, roof, "Hall HVAC 02", -7.2f, -7.6f, 90f, 6.12f);
            Detail(stood, RoofAirconC, roof, "Hall HVAC 03", 1.5f, -11.1f, 0f, 6.12f);
            Detail(stood, RoofAntennaA, roof, "Hall Radio Antenna", -18.4f, -5.2f, 0f, 6.12f);
            Detail(stood, RoofAirconB, roof, "Crew Wing HVAC", 13.0f, -9.8f, 90f, 9.12f);
            Detail(stood, RoofAntennaB, roof, "Dispatch Antenna", 17.4f, -8.8f, 0f, 9.12f);
            Detail(stood, SatelliteDish, roof, "Dispatch Satellite Dish", 17.8f, -3.6f, 215f, 9.12f);
        }

        static Transform Cluster(Transform parent, string name)
        {
            var cluster = new GameObject(name).transform;
            cluster.SetParent(parent, false);
            return cluster;
        }

        static void Detail(Stood stood, string path, Transform parent, string name,
                           float x, float z, float yaw, float y = 0f)
        {
            var go = Composer.Sit(path, parent, x, z, yaw, y);
            if (go == null) return;
            go.name = name;
            SetStatic(go);
            stood.DetailProps++;
        }

        static void PaintApron(Transform root)
        {
            var paint = new GameObject("Fire Station Parking Markings").transform;
            paint.SetParent(root, false);
            var white = DemoAssetLoad.Load<Material>(WhitePaint) ??
                        ForecourtSet.WhitePaint();
            var red = DemoAssetLoad.Load<Material>(RedPaint) ??
                      ForecourtSet.Flat("Fire Station Red Paint", new Color(0.64f, 0.035f, 0.025f), 0.04f);

            // Three broad engine runs line up with the roller doors. The red threshold is
            // the visual 'keep clear' rule; the empty third run remains unmistakably a bay.
            foreach (float centre in new[] { -15f, -6f, 3f })
            {
                Stripe(paint, new Vector2(centre - 2.7f, 0.8f),
                              new Vector2(centre - 2.7f, 13.2f), 0.12f, white);
                Stripe(paint, new Vector2(centre + 2.7f, 0.8f),
                              new Vector2(centre + 2.7f, 13.2f), 0.12f, white);
                Stripe(paint, new Vector2(centre - 2.7f, 13.2f),
                              new Vector2(centre + 2.7f, 13.2f), 0.12f, white);
                Stripe(paint, new Vector2(centre - 2.7f, 0.85f),
                              new Vector2(centre + 2.7f, 0.85f), 0.22f, red);
            }

            // Four staff bays beside the crew wing: three occupied, one visibly available.
            const float staffMinX = 9.3f;
            const float staffPitch = 3.8f;
            for (int i = 0; i <= 4; i++)
            {
                float x = staffMinX + i * staffPitch;
                Stripe(paint, new Vector2(x, 3.1f), new Vector2(x, 13.3f), 0.1f, white);
            }
            Stripe(paint, new Vector2(staffMinX, 13.3f),
                          new Vector2(staffMinX + 4f * staffPitch, 13.3f), 0.1f, white);

            // One continuous fire lane across the street edge ties trucks and staff parking
            // into a single forecourt instead of four unrelated painted patches.
            Stripe(paint, new Vector2(-21.5f, 15.1f), new Vector2(24f, 15.1f), 0.18f, red);
        }

        static void Stripe(Transform parent, Vector2 a, Vector2 b, float width, Material material)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Paint Stripe";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((a.x + b.x) * 0.5f, PaintY,
                                                (a.y + b.y) * 0.5f);
            go.transform.rotation = Quaternion.Euler(
                0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg, 0f);
            go.transform.localScale = new Vector3(length, 0.025f, width);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }
            go.isStatic = true;
        }

        static void SetStatic(GameObject go)
        {
            if (go == null) return;
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.isStatic = true;
        }

        public static Bounds BoundsOf(GameObject go)
        {
            if (go == null) return default;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
