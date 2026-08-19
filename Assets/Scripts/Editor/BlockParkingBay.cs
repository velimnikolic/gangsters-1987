using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The parking a block grows on the one side of its pad where the buildings left
    /// enough room for a car.
    ///
    /// Its OWN command, not a step of the prop scatter: a yard is either one you want
    /// cars standing in or one you do not, and that is a decision per block rather than
    /// something every dressing pass should hand out. So the scatter lays no parking at
    /// all now, and this pass lays nothing but parking.
    ///
    /// Run it BEFORE the props on a block that wants both. Bays need a particular
    /// rectangle against a particular kerb while a bin is happy in any free cell, so the
    /// parking should choose its ground while the yard is still empty - and the scatter,
    /// which counts every other pass's output as an obstacle, then keeps out of it.
    ///
    /// What it stands up is a whole small car park, not a row of bays: its own slab of
    /// asphalt, painted rows a full car long, a second row served by an aisle where the
    /// yard is deep enough, an entrance drive off the street at one end with its arrow,
    /// sign, machine and bollards, a fence round the boundary with a gap at that drive, a
    /// wheel stop at the head of every bay, and a lamp at each end of the run. The
    /// measured reason for each of those numbers is on the constant that carries it.
    ///
    /// WHOSE cars stand in it is read off the block: a police station parks cruisers, a
    /// warehouse vans, a dealer's forecourt things with spoilers in every bay - see
    /// <see cref="Fleet"/> and <see cref="Tenants"/>. The lot belongs to the building
    /// beside it, and a lot full of the wrong vehicles is the one thing about a car park
    /// nobody has to be told is wrong.
    ///
    /// Everything placed here is a prefab INSTANCE under this pass's own "auto parking"
    /// root, for the two reasons those roots exist: a re-roll wipes it without touching
    /// hand-placed work or the other passes, and BlockLotCapture can name every piece by
    /// asset path when it writes the recipe. Nothing is generated geometry - a mesh built
    /// in the scene would be dropped on capture and lost on the next catalog rebuild. The
    /// paint tile is STRETCHED rather than remodelled for the same reason; a capture
    /// records a prop's scale, so the stretch survives the bake.
    ///
    /// Where the bays go is YardParkingPlan's decision, which is measured, testable and
    /// knows nothing about Unity objects. This file only stands things on that answer.
    /// </summary>
    public static class BlockParkingBay
    {
        /// <summary>The survey's own resolution. A metre is fine enough to find the free
        /// strip along a kerb and coarse enough that the whole pad is a small grid.</summary>
        const float GridCell = 1f;
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProp = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string CityVeh = "Assets/Synty/PolygonCity/Prefabs/Vehicles/";
        const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        const string PalmProp = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string GangVeh = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
        const string CopVeh = "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/";

        /// <summary>The painted bays: a flat 10 x 5 m piece of the PolygonCity road kit
        /// with three bays across it, stretched to YardParkingPlan.RowDepth so a car
        /// stands inside its own paint. See YardParkingPlan.TileFrontage.</summary>
        const string BayTile = CityEnv + "SM_Env_Road_ParkingLines_01";

        /// <summary>The drive: a 5 x 5 m road plate with a lane arrow on it, laid in the
        /// mouth of the entrance. It is asphalt in its own right, so the drive reads as a
        /// drive even before the floor pass beds the lot in.</summary>
        const string DriveArrow = CityEnv + "SM_Env_Road_Arrow_01";

        /// <summary>
        /// The lot's own ground: ONE bare road plate stretched over the whole rectangle,
        /// aprons, aisle and drive included.
        ///
        /// The parking lays this itself rather than leaving it to the floor pass, and the
        /// aisle is why. Between two rows of paint is six metres of nothing, and to the
        /// floor's planner that is yard like any other - it would come back as lawn, or as
        /// paving in a courtyard block, and a car park with a stripe of grass down the
        /// middle of it reads as a bug. A lot is poured in one go, so it is one slab here
        /// too; the floor pass still asphalts the ground around and under it, which is what
        /// makes the edges of this slab disappear.
        /// </summary>
        const string LotSlab = CityEnv + "SM_Env_Road_Bare_01";

        /// <summary>
        /// The boundary, and the post that terminates a run. The city kit's own fence,
        /// which is what the prop scatter already stands along a pad edge, so a fenced lot
        /// and a fenced yard in the same block are the same fence.
        ///
        /// Pairs rather than two lists: a run laid from one kit and terminated with
        /// another's post is worse than no post at all.
        /// </summary>
        static readonly (string segment, string post)[] FencePaths =
        {
            (CityEnv + "SM_Env_Fence_01", CityEnv + "SM_Env_Fence_End_01"),
            (GangBld + "SM_Bld_Fence_01", GangBld + "SM_Bld_Fence_Pole_01"),
        };

        /// <summary>Kerbside parking gets no paint - an American kerb in 1987 does not
        /// have any - so it gets the thing that says the kerb is metered instead.</summary>
        const string Meter = PalmProp + "SM_Prop_Parking_Meter_01";

        /// <summary>The stop a car's front wheels come up against, at the head of a bay.
        /// The long divider first, its end block for a bay too narrow to take one.</summary>
        static readonly string[] StopPaths =
        {
            PalmProp + "SM_Prop_Parking_Divider_01",
            PalmProp + "SM_Prop_Parking_Divider_End_01",
        };

        /// <summary>What stands at the entrance: the sign that says the lot is a lot, the
        /// ticket machine you stop at, and a bollard on each flank of the mouth.</summary>
        const string LotSign = CityProp + "SM_Prop_Sign_Parking_01";
        const string TicketStand = PalmProp + "SM_Prop_Parking_Stand_01";
        const string Bollard = PalmProp + "SM_Prop_Bollard_02";

        /// <summary>A lamp at each end of the run, in the frontage the whole tiles could
        /// not use. One species, so a lot is lit rather than sampled.</summary>
        const string LotLamp = PalmProp + "SM_Prop_Pier_Lamp_01";

        /// <summary>
        /// The ordinary traffic that parks anywhere: civilian cars out of both city packs.
        /// No police car and no ambulance - one of those left on a block that has nothing to
        /// do with either turns it into a station by accident - and no lorry, bus or
        /// limousine, which are filtered out by length rather than by name wherever possible.
        ///
        /// A block whose buildings ask for those vehicles gets them from <see cref="Fleets"/>
        /// instead, on purpose and in quantity.
        /// </summary>
        static readonly string[] CarPaths =
        {
            PalmVeh + "SM_Veh_Sedan_01",
            PalmVeh + "SM_Veh_Sedan_01_Preset_Taxi",
            PalmVeh + "SM_Veh_Suv_01",
            PalmVeh + "SM_Veh_Van_01",
            PalmVeh + "SM_Veh_Buggy_01",
            PalmVeh + "SM_Veh_Supercar_01",
            CityVeh + "SM_Veh_Car_Sedan_01",
            CityVeh + "SM_Veh_Car_Medium_01",
            CityVeh + "SM_Veh_Car_Muscle_01",
            CityVeh + "SM_Veh_Car_Small_01",
            CityVeh + "SM_Veh_Car_Taxi_01",
            CityVeh + "SM_Veh_Car_Van_01",
        };

        /// <summary>
        /// Whose cars these are. A police station's lot is full of cruisers, a warehouse
        /// yard of vans, a dealer's forecourt of things with spoilers - and a lot that
        /// ignores the building it belongs to is the one thing about a car park nobody has
        /// to be told is wrong.
        /// </summary>
        enum Fleet { Civilian, Police, Emergency, Works, Showroom, Nightlife }

        /// <summary>
        /// What the buildings on the pad say the lot is for. Read in ORDER and the first row
        /// that matches wins, which is how the ambiguous names resolve: a CarYard is a
        /// dealer's and not a works yard, and it is listed above the yard words for exactly
        /// that reason.
        ///
        /// One special building carries the whole lot rather than a majority of the block's
        /// area: a police station with apartments either side of it still parks police cars,
        /// because the lot belongs to the station and not to the block.
        ///
        /// Matched against the buildings AND everything inside them, so a station kitbashed
        /// out of the police pack is recognised by its own meshes when it arrives as a bake
        /// rather than as the catalog prefab. That is also why the words are the long ones:
        /// bare "post" would be answered by every SM_Prop_Poster in the yard.
        /// </summary>
        static readonly (Fleet fleet, string[] names)[] Tenants =
        {
            (Fleet.Police, new[] { "policestation", "police" }),
            (Fleet.Emergency, new[] { "hospital", "clinic", "firestation", "ambulance" }),
            (Fleet.Showroom, new[] { "CarYard", "dealer", "showroom" }),
            (Fleet.Works, new[]
            {
                "warehouse", "factory", "workshop", "depot", "industrial", "carwash",
                "wharf", "marina", "storage", "building-post", "postoffice",
            }),
            (Fleet.Nightlife, new[] { "nightclub", "casino", "hotel" }),
        };

        /// <summary>
        /// Each fleet's own vehicles, how much of the lot they take, and how full that lot
        /// stands. Share and fill are the two halves of "more police cars than civilian":
        /// share decides what goes in a bay, fill decides how many bays are used at all -
        /// a station yard is busy, a works yard has half its vans out on jobs, and a
        /// forecourt is nearly full because that is the stock.
        ///
        /// The lists are longer than what survives: everything here is measured against the
        /// bay on load, so the police van and the pickup drop out on length by themselves.
        /// Whatever is left of a fleet is topped up with ordinary traffic - staff cars park
        /// at a station too - and a fleet that loses every vehicle falls back to it whole.
        /// </summary>
        static readonly (Fleet fleet, float share, float fill, string[] paths)[] Fleets =
        {
            // The two marked cars are the force's (VehicleCatalog.PoliceCars) - the
            // other packs' cruisers are deliberately absent, so a station yard is the
            // same fleet the patrols drive. The van and the two bikes are not cars and
            // stay: a yard with nothing but cruisers in it reads as a showroom.
            (Fleet.Police, 0.8f, 0.8f, new[]
            {
                PalmVeh + "SM_Veh_Sedan_01_Preset_Police",
                PalmVeh + "SM_Veh_Pickup_01_Preset_Police",
                CopVeh + "SM_Veh_Van_01",
                CopVeh + "SM_Veh_Motorbike_01",
                CopVeh + "SM_Veh_Motorbike_02",
            }),
            (Fleet.Emergency, 0.35f, 0.7f, new[]
            {
                CityVeh + "SM_Veh_Car_Ambo_01",
                CityVeh + "SM_Veh_Car_Van_01",
                PalmVeh + "SM_Veh_Van_01",
            }),
            (Fleet.Works, 0.7f, 0.5f, new[]
            {
                GangVeh + "SM_Veh_Van_01",
                GangVeh + "SM_Veh_Truck_01",
                GangVeh + "SM_Veh_Forklift_01",
                CityVeh + "SM_Veh_Car_Van_01",
                PalmVeh + "SM_Veh_Van_01",
                PalmVeh + "SM_Veh_Pickup_01_Preset_Construction",
            }),
            (Fleet.Showroom, 0.9f, 0.95f, new[]
            {
                PalmVeh + "SM_Veh_Supercar_01",
                PalmVeh + "SM_Veh_Supercar_02",
                PalmVeh + "SM_Veh_Suv_01",
                PalmVeh + "SM_Veh_Sedan_01",
                CityVeh + "SM_Veh_Car_Muscle_01",
                GangVeh + "SM_Veh_LowCar_01",
                GangVeh + "SM_Veh_LowCar_02",
            }),
            (Fleet.Nightlife, 0.55f, 0.8f, new[]
            {
                PalmVeh + "SM_Veh_Supercar_01",
                PalmVeh + "SM_Veh_Supercar_02",
                PalmVeh + "SM_Veh_Suv_01",
                CityVeh + "SM_Veh_Car_Muscle_01",
                GangVeh + "SM_Veh_LowCar_01",
            }),
        };

        /// <summary>
        /// The longest car a bay may hold. ParkingLayout.StallDepth is what the row band
        /// is sized from (YardParkingPlan.RowDepth), so filtering by the same number is
        /// what guarantees the thing this pass was getting wrong: every car it parks
        /// stands FULLY inside its own paint, with daylight at both ends. The pickup
        /// (6.85) and the limousine (10.3) go out here.
        /// </summary>
        const float MaxCarLength = ParkingLayout.StallDepth;

        /// <summary>How many bays get a car on an ordinary block. A full row reads as a
        /// showroom - which is why the showroom is the one fleet allowed it - and an empty
        /// one reads as a slab of asphalt somebody forgot to finish.</summary>
        const float Fill = 0.6f;

        /// <summary>Ground decals in the demo sit two centimetres up; the lot's asphalt goes
        /// on those, the paint on the asphalt and the cars on the paint, so no wheel is ever
        /// inside a line and no line is ever inside the slab.</summary>
        const float SlabLift = 0.02f;
        const float TileLift = 0.03f;
        const float CarLift = 0.05f;

        /// <summary>Head of the bay to the far end of the car standing in it: every car is
        /// pulled up to its wheel stop, whatever its length, which is why a short car
        /// leaves its daylight at the kerb end and not in the middle of the row.</summary>
        const float StopClear = 0.55f;

        /// <summary>Wheel stop's own setback from the head of the bay.</summary>
        const float StopSetback = 0.3f;

        /// <summary>Nobody parks on the line. A car is nudged this far in each direction
        /// and turned by up to <see cref="YawJitter"/>, which is the difference between a
        /// car park and a car showroom.</summary>
        const float DepthJitter = 0.18f;
        const float SideJitter = 0.12f;
        const float YawJitter = 2.5f;

        /// <summary>Kerb to the near side of a car standing along a kerb.</summary>
        const float KerbSetback = 1.7f;

        /// <summary>How far a meter stands in from the kerb line.</summary>
        const float MeterSetback = 0.8f;

        /// <summary>How far the entrance furniture stands in from the flank of the drive,
        /// and how far up the drive the ticket machine waits.</summary>
        const float DriveFlank = 0.7f;
        const float StandDepth = 2.6f;
        const float BollardDepth = 1.1f;

        /// <summary>Frontage a lamp needs at the end of the run before it gets one.</summary>
        const float LampSlack = 1.2f;

        /// <summary>How far inside the edge of the lot the fence line runs. Enough that a
        /// panel stands on the lot rather than on the pavement outside it, and little enough
        /// that it is still the boundary and not a second row of anything.</summary>
        const float FenceInset = 0.25f;

        /// <summary>A lot smaller than this in either direction is a lay-by with a fence
        /// round it. The bays still go down; the boundary does not.</summary>
        const float MinFencedRun = 8f;

        [MenuItem("Tools/City/Catalog/Fill Block With Parking", priority = 61)]
        public static void Park()
        {
            if (!BlockLotCapture.OpenCatalogScene())
                return;
            if (!BlockPad.TryPick(out var pad))
                return;

            var placed = Park(pad, out var root);
            Selection.activeGameObject = root.gameObject;

            if (placed == 0)
            {
                EditorUtility.DisplayDialog(
                    "No room to park",
                    $"{pad.label} has no strip of yard deep enough for a car - the " +
                    $"bays now want {YardParkingPlan.NoseDepth:F1} m of clear depth so a " +
                    "car stands fully inside its own paint, and a kerbside run wants " +
                    $"{YardParkingPlan.ParallelDepth:F1} m.\n\nMove a building in off one " +
                    "of the pad edges, or run this before the props if the scatter has " +
                    "already filled the yard.", "OK");
                return;
            }

            Debug.Log($"[Parking] {placed} pieces parked on {pad.label} " +
                      $"({pad.width:F0} x {pad.depth:F0} m). They stand under " +
                      $"\"{root.name}\" - delete what you do not want, then run " +
                      "Tools/City/Catalog/Capture Blocks From Lot Pads to save the block.");
        }

        /// <summary>
        /// The pass itself, on a pad already decided: for the menu command above, and for
        /// <see cref="BlockRandomiser"/>, which rolls a whole block and runs every dressing
        /// pass over it in one go. Returns how many pieces were parked; the root is handed
        /// back so the caller can select it, and stands even when nothing was parked.
        /// </summary>
        internal static int Park(BlockPad pad, out Transform root)
        {
            // Clear the old parking BEFORE reading the pad: last run's cars are this
            // run's output, not its input, and a yard surveyed with them still standing
            // in it has no room left to park anything.
            root = pad.ResetAuto(BlockPad.ParkingRoot);
            var content = pad.Contents(withAutoProps: true);

            Random.InitState(System.Environment.TickCount);
            var placed = Build(root, Occupancy(pad, content), pad.MinX, pad.MinZ, GridCell,
                               Stocked(content));

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return placed;
        }

        /// <summary>What the yard is already standing on, one metre cells. Everything on
        /// the pad counts, buildings and dressing alike - the floor does not, and
        /// BlockPad.Contents leaves it out for us.</summary>
        static bool[,] Occupancy(BlockPad pad, List<BlockPad.Item> content)
        {
            var nx = Mathf.Max(1, Mathf.FloorToInt(pad.width / GridCell));
            var nz = Mathf.Max(1, Mathf.FloorToInt(pad.depth / GridCell));
            var blocked = new bool[nx, nz];

            foreach (var item in content)
            {
                var foot = item.Footprint;
                var i0 = Mathf.Clamp(Mathf.FloorToInt((foot.xMin - pad.MinX) / GridCell), 0, nx - 1);
                var i1 = Mathf.Clamp(Mathf.CeilToInt((foot.xMax - pad.MinX) / GridCell), 0, nx - 1);
                var j0 = Mathf.Clamp(Mathf.FloorToInt((foot.yMin - pad.MinZ) / GridCell), 0, nz - 1);
                var j1 = Mathf.Clamp(Mathf.CeilToInt((foot.yMax - pad.MinZ) / GridCell), 0, nz - 1);
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                        blocked[i, j] = true;
            }
            return blocked;
        }

        /// <summary>
        /// Dresses the one strip of yard that can hold cars. Returns how many objects it
        /// stood up; 0 means the yard has no strip deep enough, which is the ordinary case
        /// for a block built out to its own kerb.
        ///
        /// <paramref name="minX"/> and <paramref name="minZ"/> are the grid's own corner,
        /// not the pad's: the pad size is floored into whole cells, and a strip surveyed
        /// on the grid has to be laid on the grid or it walks off the end of what was
        /// surveyed.
        /// </summary>
        static int Build(Transform parent, bool[,] blocked, float minX, float minZ,
                         float cell, Stock stock)
        {
            var plan = YardParkingPlan.Choose(blocked, cell);
            if (plan.Kind == YardParkingPlan.Kind.None)
                return 0;

            var nx = blocked.GetLength(0);
            var nz = blocked.GetLength(1);
            var min = new Vector3(minX, 0f, minZ);
            var max = new Vector3(minX + nx * cell, 0f, minZ + nz * cell);

            YardParkingPlan.Frame(plan.Side, out var along, out var outward);
            var origin = YardParkingPlan.Origin(plan.Side, min, max);
            var inward = -outward;

            var placed = plan.Kind == YardParkingPlan.Kind.Nose
                ? Lot(plan, parent, origin, along, inward, outward, stock)
                : Kerbside(plan, parent, origin, along, inward, outward, stock);

            Debug.Log($"[Parking] the block reads as {stock.Why()}, so the lot is stocked " +
                      $"{stock.Describe()}.");

            if (plan.Kind == YardParkingPlan.Kind.Nose)
                Debug.Log($"[Parking] a {plan.Length:F0} x {plan.Reach:F1} m lot on the " +
                          $"{plan.Side} side: {plan.Rows} row(s) of {plan.Units} tile(s), " +
                          $"{plan.Bays} bays, {plan.Apron:F1} m of apron front and back, " +
                          "asphalt of its own under the lot" +
                          (plan.FrontCanBeFenced
                              ? ", fenced all round"
                              : ", fenced on three sides - a single row is entered over the " +
                                "kerb, so its frontage stays open") +
                          (plan.HasDrive
                              ? $", entrance drive at the {(plan.DriveAtStart ? "low" : "high")} end"
                              : ", no room on the frontage for a drive - the row is nosed " +
                                "into straight off the street") +
                          $" - the strip had {plan.Depth:F1} m of free depth.");
            else
                Debug.Log($"[Parking] {plan.Units} kerb slot(s) on the {plan.Side} side - a " +
                          $"{plan.Length:F0} m frontage with {plan.Depth:F1} m of free " +
                          "depth behind it, too shallow to nose into.");
            return placed;
        }

        // ---------------------------------------------------------------------- lot

        /// <summary>
        /// The car park: one or two painted rows off the street, a car pulled up to the
        /// wheel stop in some of the bays, and the entrance that serves them.
        ///
        /// The tile is stretched to the row band in BOTH directions and measured rather
        /// than assumed for each: its dividers run the full depth of the mesh, so
        /// stretching that depth lengthens the lines and leaves their width alone, and
        /// correcting the frontage to the plan's own ten metres is what keeps a car in the
        /// middle of a painted bay however the pack rounded its mesh.
        /// </summary>
        static int Lot(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                       Vector3 along, Vector3 inward, Vector3 outward, Stock stock)
        {
            var tile = Load(BayTile);
            if (!tile)
                Debug.LogWarning($"[Parking] '{BayTile}' is not in the project - the bays get " +
                                 "their cars but no paint.");

            var tileScale = Vector3.one;
            if (tile)
            {
                var size = PrefabBounds.Get(tile).size;
                tileScale = new Vector3(
                    size.x > 0.01f ? YardParkingPlan.TileFrontage / size.x : 1f,
                    1f,
                    size.z > 0.01f ? YardParkingPlan.RowDepth / size.z : 1f);
            }

            // The tile's ten metres have to lie along the frontage: a quarter turn when the
            // frontage runs down Z, none when it runs down X.
            var tileYaw = Mathf.Abs(along.x) > 0.5f ? 0f : 90f;

            var stop = LoadStop();
            var run = plan.Units * YardParkingPlan.TileFrontage;
            var start = plan.BayStart + (plan.BayLength - run) * 0.5f;

            var placed = Ground(plan, parent, origin, along, inward);

            for (var row = 0; row < plan.Rows; row++)
            {
                var front = plan.RowFront(row);

                // Which end of the band the bays are closed at - the kerb end for the front
                // row of a two-row lot, whose aisle is behind it. Everything else about the
                // row is symmetrical, so this one number turns it round.
                var head = plan.RowHead(row);
                var into = plan.HeadAtKerb(row) ? 1f : -1f;   // head -> open end of the bay

                for (var t = 0; t < plan.Units; t++)
                {
                    var tileStart = start + t * YardParkingPlan.TileFrontage;

                    if (tile)
                    {
                        var centre = origin
                                   + along * (tileStart + YardParkingPlan.TileFrontage * 0.5f)
                                   + inward * (front + YardParkingPlan.RowDepth * 0.5f);
                        Stand(tile, centre, Quaternion.Euler(0f, tileYaw, 0f), TileLift, parent,
                              tileScale);
                        placed++;
                    }

                    for (var b = 0; b < YardParkingPlan.BaysPerTile; b++)
                    {
                        var bay = tileStart + YardParkingPlan.BayWidth * (b + 0.5f);

                        if (stop)
                        {
                            Stand(stop, origin + along * bay
                                      + inward * (head + StopSetback * into),
                                  AcrossBay(stop, along), 0f, parent);
                            placed++;
                        }

                        if (Random.value > stock.fill)
                            continue;

                        var car = stock.Next();
                        if (!car)
                            continue;

                        var length = PrefabBounds.Get(car).size.z;
                        if (length + 0.2f > YardParkingPlan.RowDepth)
                            continue;       // longer than the band; the filter missed it

                        // Pulled up to the stop, or a little short of it - never past it,
                        // which is why the nudge only ever goes back towards the open end
                        // of the bay.
                        var reach = head + (StopClear + length * 0.5f
                                            + Random.Range(0f, DepthJitter)) * into;
                        reach = Mathf.Clamp(reach,
                                            front + length * 0.5f + 0.1f,
                                            front + YardParkingPlan.RowDepth
                                                  - length * 0.5f - 0.1f);

                        // Some cars are nosed in and some backed in, which is what a real
                        // lot looks like from the street - unless somebody is paid to keep
                        // the row straight, which is what a forecourt is.
                        var facing = stock.aligned || Random.value < 0.5f ? outward : inward;
                        var wonk = stock.aligned ? YawJitter * 0.2f : YawJitter;
                        var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg
                                + Random.Range(-wonk, wonk);

                        var centre = origin
                                   + along * (bay + Random.Range(-SideJitter, SideJitter))
                                   + inward * reach;
                        Stand(car, centre, Quaternion.Euler(0f, yaw, 0f), CarLift, parent);
                        placed++;
                    }
                }
            }

            placed += Entrance(plan, parent, origin, along, inward, outward);
            placed += Lamps(plan, parent, origin, along, inward, start, run);
            placed += Fence(plan, parent, origin, along, inward, outward);
            return placed;
        }

        /// <summary>
        /// The slab the whole lot stands on: one plate, stretched, under the paint. See
        /// <see cref="LotSlab"/> for why the parking pours its own ground.
        ///
        /// Laid unturned and scaled in world axes, which the plan's cardinal frame allows -
        /// so the stretch never has to be reasoned about through a rotation.
        /// </summary>
        static int Ground(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                          Vector3 along, Vector3 inward)
        {
            var slab = Load(LotSlab);
            if (!slab)
            {
                Debug.LogWarning($"[Parking] '{LotSlab}' is not in the project - the lot gets " +
                                 "its paint but no asphalt of its own, and the aisle will come " +
                                 "back as whatever the floor pass makes of it.");
                return 0;
            }

            LotRect(plan, origin, along, inward, out var min, out var max);

            var size = PrefabBounds.Get(slab).size;
            if (size.x < 0.01f || size.z < 0.01f)
                return 0;

            var scale = new Vector3((max.x - min.x) / size.x, 1f, (max.y - min.y) / size.z);
            var centre = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
            Stand(slab, centre, Quaternion.identity, SlabLift, parent, scale);
            return 1;
        }

        /// <summary>
        /// The boundary: four runs on the lot's own rectangle, a post at each corner, and a
        /// gap where the drive is.
        ///
        /// The STREET side is only closed on a lot whose aisle serves its rows - see
        /// YardParkingPlan.Plan.HeadAtKerb. A single row is entered over the kerb line, bay
        /// by bay, so fencing that line would wall the cars in; the run is left open there
        /// and the flanks simply terminate on it.
        /// </summary>
        static int Fence(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                         Vector3 along, Vector3 inward, Vector3 outward)
        {
            var (segment, post) = LoadFence();
            if (!segment)
                return 0;

            var reach = plan.Reach;
            if (plan.Length < MinFencedRun || reach < MinFencedRun * 0.5f)
                return 0;

            var lowEnd = plan.Start + FenceInset;
            var highEnd = plan.Start + plan.Length - FenceInset;
            var frontLine = origin + inward * FenceInset;
            var backLine = origin + inward * (reach - FenceInset);

            var placed = 0;

            // The two flanks, running from the kerb to the back of the lot.
            foreach (var (at, faces) in new[] { (lowEnd, -1f), (highEnd, 1f) })
                placed += Run(segment, origin + along * at, inward, along * faces,
                              FenceInset, reach - FenceInset, parent);

            // The back.
            placed += Run(segment, backLine, along, inward, lowEnd, highEnd, parent);

            // And the street side, in one piece or in two round the drive.
            if (plan.FrontCanBeFenced)
            {
                var gapFrom = plan.DriveStart;
                var gapTo = plan.DriveStart + YardParkingPlan.DriveWidth;

                placed += Run(segment, frontLine, along, outward, lowEnd, gapFrom, parent);
                placed += Run(segment, frontLine, along, outward, gapTo, highEnd, parent);

                // A post on each side of the mouth - but not one that would stand a
                // quarter-metre from the corner post, which is where a drive at the very
                // end of the run puts it.
                if (post)
                    foreach (var gate in new[] { gapFrom, gapTo })
                    {
                        if (gate - lowEnd < 0.5f || highEnd - gate < 0.5f)
                            continue;
                        Stand(post, frontLine + along * gate, Quaternion.LookRotation(outward),
                              0f, parent);
                        placed++;
                    }
            }

            if (post)
                foreach (var corner in new[]
                         {
                             frontLine + along * lowEnd, frontLine + along * highEnd,
                             backLine + along * lowEnd, backLine + along * highEnd,
                         })
                {
                    Stand(post, corner, Quaternion.LookRotation(outward), 0f, parent);
                    placed++;
                }

            return placed;
        }

        /// <summary>
        /// One straight length of fence, laid piece to piece between two parameters along a
        /// line. The count is rounded and every piece stretched to the spacing that follows
        /// from it - the same bargain FenceRun strikes at play time, and for the same reason:
        /// a run is never a whole number of panels, and a one-percent stretch beats both a
        /// hole at the end and a doubled post where the last piece overlaps.
        ///
        /// <paramref name="faces"/> is the way the fence looks - out of the lot - and it is
        /// what decides the quarter turn, because a panel's long side and its face are
        /// perpendicular whichever local axis each of them is.
        /// </summary>
        static int Run(GameObject piece, Vector3 line, Vector3 direction, Vector3 faces,
                       float from, float to, Transform parent)
        {
            var span = to - from;
            if (span <= 0.5f)
                return 0;

            var size = PrefabBounds.Get(piece).size;
            var longIsX = size.x >= size.z;
            var length = longIsX ? size.x : size.z;
            if (length < 0.2f)
                return 0;

            var count = Mathf.Max(1, Mathf.RoundToInt(span / length));
            var pitch = span / count;
            var stretch = pitch / length;

            // Whichever axis the panel lies along, ONE of these two rotations puts it on the
            // run: aligning its face with the outward normal does it for a panel built along
            // its local X, aligning its length with the run does it for one built along Z.
            var rotation = Quaternion.LookRotation(longIsX ? faces : direction);
            var scale = longIsX ? new Vector3(stretch, 1f, 1f) : new Vector3(1f, 1f, stretch);

            for (var i = 0; i < count; i++)
                Stand(piece, line + direction * (from + pitch * (i + 0.5f)), rotation, 0f,
                      parent, scale);

            return count;
        }

        /// <summary>The lot's own rectangle in world x/z: the whole run by its whole reach,
        /// aprons, aisle and drive included.</summary>
        static void LotRect(YardParkingPlan.Plan plan, Vector3 origin, Vector3 along,
                         Vector3 inward, out Vector2 min, out Vector2 max)
        {
            var near = origin + along * plan.Start;
            var far = origin + along * (plan.Start + plan.Length) + inward * plan.Reach;

            min = new Vector2(Mathf.Min(near.x, far.x), Mathf.Min(near.z, far.z));
            max = new Vector2(Mathf.Max(near.x, far.x), Mathf.Max(near.z, far.z));
        }

        /// <summary>
        /// The mouth of the lot: the arrow plate laid in the drive, a parking sign facing
        /// the street on its outer flank, the ticket machine a car stops at on the inner
        /// one, and a bollard on each side of the opening.
        ///
        /// All of it inside the drive's own six metres, which the plan kept clear of paint,
        /// so nothing here can stand in a bay.
        /// </summary>
        static int Entrance(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                            Vector3 along, Vector3 inward, Vector3 outward)
        {
            if (!plan.HasDrive)
                return 0;

            var mid = plan.DriveStart + YardParkingPlan.DriveWidth * 0.5f;

            // The flank the street corner is on takes the sign; the one the bays are on
            // takes the machine, which is the side a driver pulls up to.
            var outerSide = plan.DriveAtStart ? -1f : 1f;
            var flank = YardParkingPlan.DriveWidth * 0.5f - DriveFlank;

            var placed = 0;

            var arrow = Load(DriveArrow);
            if (arrow)
            {
                // Measured square, so one figure covers the plate whichever way it is
                // turned: it has to fit between the flanks of the drive AND inside the
                // depth the drive runs to.
                var size = PrefabBounds.Get(arrow).size;
                var plate = Mathf.Max(size.x, size.z);
                if (plate <= YardParkingPlan.DriveWidth && plate + 0.8f <= plan.DriveReach)
                {
                    // Turned to point up the drive. Synty lays its road decals down +Z,
                    // the same facing this file turns every prop by.
                    var centre = origin + along * mid + inward * (plate * 0.5f + 0.4f);
                    Stand(arrow, centre, Quaternion.LookRotation(inward), TileLift, parent);
                    placed++;
                }
            }

            var sign = Load(LotSign);
            if (sign)
            {
                Stand(sign, origin + along * (mid + flank * outerSide) + inward * BollardDepth,
                      Quaternion.LookRotation(outward), 0f, parent);
                placed++;
            }

            var stand = Load(TicketStand);
            if (stand && plan.DriveReach >= StandDepth + 1f)
            {
                // On the flank the bays are on, turned to face back across the lane - a
                // ticket machine faces the driver window, not the row behind it.
                Stand(stand, origin + along * (mid - flank * outerSide) + inward * StandDepth,
                      Quaternion.LookRotation(along * outerSide), 0f, parent);
                placed++;
            }

            var bollard = Load(Bollard);
            if (bollard)
                for (var side = -1; side <= 1; side += 2)
                {
                    var post = mid + (YardParkingPlan.DriveWidth * 0.5f - 0.35f) * side;
                    Stand(bollard, origin + along * post + inward * BollardDepth,
                          Quaternion.LookRotation(outward), 0f, parent);
                    placed++;
                }

            return placed;
        }

        /// <summary>A lamp in whatever frontage the whole tiles could not use, at each end
        /// of the run - the one place in a lot where a post is not in somebody's way.</summary>
        static int Lamps(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                         Vector3 along, Vector3 inward, float start, float run)
        {
            var lamp = Load(LotLamp);
            if (!lamp)
                return 0;

            var low = start - plan.BayStart;
            var high = plan.BayStart + plan.BayLength - (start + run);
            var depth = plan.RowFront(0) + YardParkingPlan.RowDepth * 0.5f;

            var placed = 0;
            if (low >= LampSlack)
            {
                Stand(lamp, origin + along * (plan.BayStart + low * 0.5f) + inward * depth,
                      Quaternion.LookRotation(inward), 0f, parent);
                placed++;
            }
            if (high >= LampSlack)
            {
                Stand(lamp, origin + along * (start + run + high * 0.5f) + inward * depth,
                      Quaternion.LookRotation(inward), 0f, parent);
                placed++;
            }
            return placed;
        }

        // ------------------------------------------------------------------ kerbside

        /// <summary>Cars standing along the kerb where the yard is too shallow to nose into,
        /// all facing the same way as traffic on that side would, with meters between
        /// them.</summary>
        static int Kerbside(YardParkingPlan.Plan plan, Transform parent, Vector3 origin,
                            Vector3 along, Vector3 inward, Vector3 outward, Stock stock)
        {
            if (stock.Empty)
                return 0;

            var meter = Load(Meter);

            // One direction for the whole run - a kerb where every second car faces the
            // other way is a kerb nobody could have parked at.
            var heading = Quaternion.LookRotation(Random.value < 0.5f ? along : -along);

            var run = plan.Units * YardParkingPlan.ParallelPitch;
            var start = plan.Start + (plan.Length - run) * 0.5f;

            var placed = 0;
            for (var s = 0; s < plan.Units; s++)
            {
                var slot = start + YardParkingPlan.ParallelPitch * (s + 0.5f);

                var car = Random.value <= stock.fill ? stock.Next() : null;
                if (car)
                {
                    var width = PrefabBounds.Get(car).size.x;
                    var inset = Mathf.Max(KerbSetback, width * 0.5f + 0.4f);
                    if (inset + width * 0.5f <= plan.Depth)
                    {
                        Stand(car, origin + along * slot + inward * inset, heading, CarLift, parent);
                        placed++;
                    }
                }

                // A meter every other slot, on the boundary between two of them, which is
                // where a kerb actually carries one.
                if (meter && s % 2 == 0)
                {
                    var post = start + YardParkingPlan.ParallelPitch * s;
                    Stand(meter, origin + along * post + inward * MeterSetback,
                          Quaternion.LookRotation(outward), 0f, parent);
                    placed++;
                }
            }
            return placed;
        }

        // -------------------------------------------------------------------- shared

        /// <summary>
        /// Stands a prefab so its FOOTPRINT is centred on <paramref name="centre"/>. The
        /// bay tile's pivot is at a corner of its ten metres, so placing it by transform
        /// position alone would hang half a tile off the run - and the offset has to be
        /// scaled before it is turned, because a stretched tile's pivot moves with the
        /// stretch.
        /// </summary>
        static void Stand(GameObject prefab, Vector3 centre, Quaternion rotation, float lift,
                          Transform parent, Vector3 scale = default)
        {
            if (scale == default)
                scale = Vector3.one;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.localScale = scale;

            var offset = rotation * Vector3.Scale(PrefabBounds.Get(prefab).center, scale);
            instance.transform.SetPositionAndRotation(
                new Vector3(centre.x - offset.x, lift, centre.z - offset.z), rotation);
        }

        /// <summary>The quarter turn that lays a prop's LONGER side across the bay, so a
        /// wheel stop lies along the frontage rather than pointing up the bay.</summary>
        static Quaternion AcrossBay(GameObject prop, Vector3 along)
        {
            var size = PrefabBounds.Get(prop).size;
            var longIsX = size.x >= size.z;
            var frontageIsX = Mathf.Abs(along.x) > 0.5f;
            return Quaternion.Euler(0f, longIsX == frontageIsX ? 0f : 90f, 0f);
        }

        static GameObject Load(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");

        /// <summary>The first wheel stop that fits across a bay. Measured, because the two
        /// dividers the pack ships are a rail and its end block and only one of them is
        /// under three metres.</summary>
        static GameObject LoadStop()
        {
            foreach (var path in StopPaths)
            {
                var prefab = Load(path);
                if (!prefab)
                    continue;
                var size = PrefabBounds.Get(prefab).size;
                if (Mathf.Max(size.x, size.z) <= YardParkingPlan.BayWidth * 0.9f)
                    return prefab;
            }
            return null;
        }

        /// <summary>The first fence pair a pack in the project actually ships. The post is
        /// optional - a run without its corner posts still reads as a fence - so a pair whose
        /// segment loads is taken whatever became of its post.</summary>
        static (GameObject segment, GameObject post) LoadFence()
        {
            foreach (var (segment, post) in FencePaths)
            {
                var panel = Load(segment);
                if (panel)
                    return (panel, Load(post));
            }

            Debug.Log("[Parking] no fence panel any pack ships could be loaded - the lot is " +
                      "left open.");
            return (null, null);
        }

        // --------------------------------------------------------------------- stock

        /// <summary>
        /// What this particular block's lot has standing in it: the tenant's own vehicles,
        /// the ordinary traffic that tops them up, and how much of each there is.
        ///
        /// One object rather than two lists passed about, because the three numbers only
        /// mean anything together: a share with nothing to share out, or a fleet with no
        /// fill, is a lot with either every bay a police car or none of them.
        /// </summary>
        sealed class Stock
        {
            internal Fleet fleet;
            internal readonly List<GameObject> own = new List<GameObject>();
            internal readonly List<GameObject> civil = new List<GameObject>();

            /// <summary>How often a bay takes one of the tenant's own.</summary>
            internal float share;

            /// <summary>How many bays are used at all.</summary>
            internal float fill;

            /// <summary>Whether the cars stand to attention. A forecourt is arranged by
            /// somebody whose job it is - all of them facing the street, straight - and
            /// every other lot is parked in by whoever turned up.</summary>
            internal bool aligned;

            internal bool Empty => own.Count == 0 && civil.Count == 0;

            /// <summary>The next vehicle to stand in a bay, or null when the packs handed
            /// this lot nothing at all.</summary>
            internal GameObject Next()
            {
                var list = own.Count > 0 && (civil.Count == 0 || Random.value < share)
                    ? own
                    : civil;
                return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
            }

            internal string Why() => fleet == Fleet.Civilian
                ? "an ordinary block"
                : $"a {fleet.ToString().ToLowerInvariant()} block";

            internal string Describe() =>
                own.Count == 0
                    ? $"with {civil.Count} kinds of ordinary traffic, {fill:P0} of the bays used"
                    : $"{share:P0} out of {own.Count} of its own vehicles and the rest from " +
                      $"{civil.Count} kinds of ordinary traffic, {fill:P0} of the bays used";
        }

        /// <summary>
        /// Reads the block and loads what belongs in its lot. Everything is measured against
        /// the bay as it loads - see <see cref="MaxCarLength"/> - so a fleet's lorry drops out
        /// here rather than standing half in the aisle, and a fleet left with nothing at all
        /// falls back to ordinary traffic instead of an empty lot.
        /// </summary>
        static Stock Stocked(List<BlockPad.Item> content)
        {
            var stock = new Stock { fleet = FleetOf(content), share = 0f, fill = Fill };
            var missing = new List<string>();

            LoadInto(stock.civil, CarPaths, missing);

            foreach (var (fleet, share, fill, paths) in Fleets)
            {
                if (fleet != stock.fleet)
                    continue;
                LoadInto(stock.own, paths, missing);
                stock.share = share;
                stock.fill = fill;
                stock.aligned = fleet == Fleet.Showroom;
                break;
            }

            if (stock.fleet != Fleet.Civilian && stock.own.Count == 0)
                Debug.Log($"[Parking] the {stock.fleet.ToString().ToLowerInvariant()} fleet has " +
                          "no vehicle short enough for a bay in this project - the lot is " +
                          "stocked with ordinary traffic instead.");

            if (missing.Count > 0)
                Debug.Log("[Parking] vehicles the packs do not ship, skipped: " +
                          string.Join(", ", missing));
            return stock;
        }

        /// <summary>The vehicles of one list a bay can actually hold, measured rather than
        /// assumed - the packs disagree about how long a sedan is by more than a metre.</summary>
        static void LoadInto(List<GameObject> into, string[] paths, List<string> missing)
        {
            foreach (var path in paths)
            {
                var prefab = Load(path);
                if (!prefab)
                {
                    missing.Add(System.IO.Path.GetFileName(path));
                    continue;
                }
                if (PrefabBounds.Get(prefab).size.z <= MaxCarLength)
                    into.Add(prefab);
            }
        }

        /// <summary>
        /// Whose lot this is, from the buildings standing on the pad and the meshes inside
        /// them. See <see cref="Tenants"/> for the order the answer is looked for in and why
        /// one special building beats a block full of ordinary ones.
        /// </summary>
        static Fleet FleetOf(List<BlockPad.Item> content)
        {
            var names = new List<string>();
            foreach (var item in content)
            {
                if (!item.building)
                    continue;
                if (!string.IsNullOrEmpty(item.path))
                    names.Add(System.IO.Path.GetFileNameWithoutExtension(item.path));
                if (item.node)
                    foreach (var node in item.node.GetComponentsInChildren<Transform>())
                        names.Add(node.name);
            }

            foreach (var (fleet, words) in Tenants)
                foreach (var word in words)
                    foreach (var name in names)
                        if (name.Contains(word, System.StringComparison.OrdinalIgnoreCase))
                            return fleet;

            return Fleet.Civilian;
        }
    }
}
