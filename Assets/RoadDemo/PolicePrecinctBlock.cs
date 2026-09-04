using System;
using System.Collections.Generic;
using System.Linq;
using LivingCity.Entities;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The small precinct as an ordinary urban block: a fully enclosed two-storey station,
    /// one surface car park and the same CorePavement contract used by the surrounding city.
    /// There is deliberately no basement, garage mouth or sloped road. The parking cells and
    /// their driveways are cut out by CorePavement itself, so no pavement tile can be laid in
    /// the same cell as parking asphalt.
    /// </summary>
    public static class PolicePrecinctBlock
    {
        public const string ShellPath =
            "Assets/CityKit/PolicePrecinct/building-policestation-compact.prefab";

        const string PatrolSedan =
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Police.prefab";
        const string PatrolPickup =
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01_Preset_Police.prefab";
        const string SurfaceBarrier =
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Barrier_Gate_01.prefab";
        const string Cone =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Props/SM_Prop_Cone_01.prefab";

        public const float BlockFrontage = 45f;
        public const float BlockDepth = 40f;
        public const float PavementWidth = CorePavement.Cell;
        public const float SurfaceY = -0.08f;
        const float ShellX = -5f;
        const int ParkingYaw = 270;

        public static readonly Rect PreviewBounds = Rect.MinMaxRect(-20f, -20f, 25f, 20f);
        public static readonly Rect ParcelBounds = Rect.MinMaxRect(-15f, -15f, 20f, 15f);
        public static readonly Rect BuildingBounds = Rect.MinMaxRect(-12.5f, -10f, 2.5f, 10f);
        public static readonly Rect SurfaceParkingBounds = Rect.MinMaxRect(5f, -10f, 15f, 5f);

        public sealed class Stood
        {
            public GameObject Shell;
            public PolicePrecinctVisual Visual;
            public PoliceStation Station;
            public CorePavement.Plan SurfacePlan;
            public int PavementTiles;
            public int SurfacePatrolCars;
            public int MarkedStalls;
            public int SurfaceBarriers;
            public int SurfaceDriveCells;
            public int ParkingPavementOverlapCells;
            public string PavementReport;

            public override string ToString() =>
                $"compact {BlockFrontage:F0} x {BlockDepth:F0} m ordinary block, " +
                $"fully enclosed two-storey station, {MarkedStalls} surface bay(s), " +
                $"{SurfacePatrolCars} visible cruiser(s), {SurfaceBarriers} surface gate(s), " +
                $"{ParkingPavementOverlapCells} parking/pavement overlap cell(s), mini holding cells";
        }

        public static Stood Compose(
            Transform root, int seed, Func<GameObject, Transform, GameObject> stand)
        {
            var stood = new Stood();
            if (root == null || stand == null) return stood;

            Composer.ForgetMissing();
            Composer.Begin(stand);

            var building = new GameObject("Fully Enclosed Compact Police Station").transform;
            building.SetParent(root, false);
            stood.Shell = Composer.Stand(ShellPath, building, ShellX, 0f, 0f);
            if (stood.Shell == null) return stood;
            stood.Shell.name = "building-policestation-compact";
            SetStatic(stood.Shell);
            stood.Visual = stood.Shell.GetComponent<PolicePrecinctVisual>();

            stood.SurfacePlan = PavementPlan();
            var pavement = new GameObject("One Authoritative Pavement And Surface Car Park")
                .transform;
            pavement.SetParent(root, false);
            stood.PavementTiles = CorePavement.Lay(
                stood.SurfacePlan, stand, pavement, out string report,
                y: SurfaceY, seed: seed * 809 + 113,
                ramps: false, under: true, props: true);
            stood.PavementReport = report;
            stood.SurfaceDriveCells = stood.SurfacePlan.DriveCells;
            stood.ParkingPavementOverlapCells = SurfaceConflicts(stood.SurfacePlan);

            DressSurfaceFleet(stood, root, stood.SurfacePlan.Stalls);
            BuildSurfaceParkingGates(stood, root, stood.SurfacePlan);
            WireStationMarker(stood, root, stood.SurfacePlan.Stalls);

            var layout = root.GetComponent<PolicePrecinctBlockLayout>();
            if (layout == null) layout = root.gameObject.AddComponent<PolicePrecinctBlockLayout>();
            layout.Configure(
                ParcelBounds, BuildingBounds, SurfaceParkingBounds,
                stood.MarkedStalls, stood.SurfaceDriveCells, stood.SurfaceBarriers,
                stood.ParkingPavementOverlapCells, underground: 0);
            return stood;
        }

        public static Stood Compose(Transform root, int seed) =>
            Compose(root, seed, (prefab, parent) =>
                UnityEngine.Object.Instantiate(prefab, parent));

        static CorePavement.Plan PavementPlan()
        {
            var parcel = BoundsOf(ParcelBounds, 0f);
            var building = BoundsOf(BuildingBounds, 0f);
            var parking = BoundsOf(SurfaceParkingBounds, 0f);
            return CorePavement.Around(
                new[] { parcel }, band: 1,
                roofs: new[] { building },
                parks: new[] { parking }, parkYaw: ParkingYaw);
        }

        static int SurfaceConflicts(CorePavement.Plan plan)
        {
            if (plan == null || !plan.Any) return -1;
            int conflicts = 0;
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    bool parkingSurface = plan.Park[i, j] || plan.Drive[i, j] || plan.Ramp[i, j];
                    bool pavementSurface = plan.Ground[i, j] && !plan.Park[i, j] &&
                                           !plan.Drive[i, j] && !plan.Ramp[i, j];
                    if (parkingSurface && pavementSurface) conflicts++;
                }
            return conflicts;
        }

        static void DressSurfaceFleet(
            Stood stood, Transform root, IReadOnlyList<Vector3> stalls)
        {
            stood.MarkedStalls = stalls?.Count ?? 0;
            if (stalls == null || stalls.Count == 0) return;

            var fleet = new GameObject("Surface Response Fleet - One Marked Bay Free").transform;
            fleet.SetParent(root, false);
            for (int i = 0; i < Mathf.Min(3, stalls.Count); i++)
            {
                string path = i == 2 ? PatrolPickup : PatrolSedan;
                string name = i == 2
                    ? "SM_Veh_Pickup_01_Preset_Police - Surface Supervisor"
                    : $"SM_Veh_Sedan_01_Preset_Police - Surface Patrol {i + 1:00}";
                var at = stalls[i];
                var go = Composer.Sit(path, fleet, at.x, at.z, ParkingYaw, 0.02f);
                if (go == null) continue;
                go.name = name;
                SetStatic(go);
                stood.SurfacePatrolCars++;
            }
        }

        static void BuildSurfaceParkingGates(
            Stood stood, Transform root, CorePavement.Plan plan)
        {
            if (plan == null || !plan.Any) return;
            var gates = new GameObject("SURFACE PARKING ENTRY AND EXIT BARRIERS").transform;
            gates.SetParent(root, false);

            var mouths = EastDriveMouths(plan);
            for (int i = 0; i < mouths.Count; i++)
            {
                var at = mouths[i];
                var gate = Composer.Sit(SurfaceBarrier, gates, at.x, at.z, 90f, 0.02f);
                if (gate == null) continue;
                gate.name = i == 0
                    ? "SURFACE PARKING ENTRY BARRIER"
                    : "SURFACE PARKING EXIT BARRIER";
                SetStatic(gate);
                stood.SurfaceBarriers++;

                foreach (float side in new[] { -1.9f, 1.9f })
                {
                    var cone = Composer.Sit(Cone, gates, at.x - 1.1f, at.z + side, 0f, 0.02f);
                    if (cone == null) continue;
                    cone.name = "SM_Prop_Cone_01 - surface parking gate safety";
                    SetStatic(cone);
                }
            }
        }

        static List<Vector3> EastDriveMouths(CorePavement.Plan plan)
        {
            var mouths = new List<Vector3>();
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.Drive[i, j] || !plan.In(i, j) || plan.In(i + 1, j)) continue;
                    mouths.Add(new Vector3(
                        plan.X0 + (i + 0.5f) * CorePavement.Cell,
                        0f,
                        plan.Z0 + (j + 0.5f) * CorePavement.Cell));
                }
            mouths.Sort((a, b) => a.z.CompareTo(b.z));
            return mouths;
        }

        static void WireStationMarker(
            Stood stood, Transform root, IReadOnlyList<Vector3> stalls)
        {
            if (stood.Shell == null) return;
            var station = stood.Shell.GetComponent<PoliceStation>();
            if (station == null) station = stood.Shell.AddComponent<PoliceStation>();

            var locals = new Vector3[stalls?.Count ?? 0];
            for (int i = 0; i < locals.Length; i++)
            {
                Vector3 world = root.TransformPoint(stalls[i]);
                locals[i] = stood.Shell.transform.InverseTransformPoint(world);
            }

            Vector3 doorWorld = stood.Visual != null && stood.Visual.PublicEntrance != null
                ? stood.Visual.PublicEntrance.position
                : root.TransformPoint(new Vector3(ShellX - 1.25f, 0.02f, 10.9f));
            station.SetLayout(
                locals, ParkingYaw,
                stood.Shell.transform.InverseTransformPoint(doorWorld));
            stood.Station = station;
        }

        static Bounds BoundsOf(Rect rect, float y) =>
            new Bounds(
                new Vector3(rect.center.x, y, rect.center.y),
                new Vector3(rect.width, 1f, rect.height));

        static void SetStatic(GameObject go)
        {
            if (go == null) return;
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.isStatic = true;
        }
    }

    /// <summary>
    /// Saved evidence of the block's mutually exclusive surface plan. This keeps the
    /// ResidentialDemo audit meaningful after a domain reload, when Stood no longer exists.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PolicePrecinctBlockLayout : MonoBehaviour
    {
        [SerializeField] Rect parcelBounds;
        [SerializeField] Rect buildingBounds;
        [SerializeField] Rect surfaceParkingBounds;
        [SerializeField] int markedSurfaceStalls;
        [SerializeField] int surfaceDriveCells;
        [SerializeField] int surfaceBarriers;
        [SerializeField] int parkingPavementOverlapCells;
        [SerializeField] int undergroundObjects;

        public Rect ParcelBounds => parcelBounds;
        public Rect BuildingBounds => buildingBounds;
        public Rect SurfaceParkingBounds => surfaceParkingBounds;
        public int MarkedSurfaceStalls => markedSurfaceStalls;
        public int SurfaceDriveCells => surfaceDriveCells;
        public int SurfaceBarriers => surfaceBarriers;
        public int ParkingPavementOverlapCells => parkingPavementOverlapCells;
        public int UndergroundObjects => undergroundObjects;

        public void Configure(
            Rect parcel, Rect building, Rect parking,
            int stalls, int driveCells, int barriers, int overlaps, int underground)
        {
            parcelBounds = parcel;
            buildingBounds = building;
            surfaceParkingBounds = parking;
            markedSurfaceStalls = Mathf.Max(0, stalls);
            surfaceDriveCells = Mathf.Max(0, driveCells);
            surfaceBarriers = Mathf.Max(0, barriers);
            parkingPavementOverlapCells = Mathf.Max(0, overlaps);
            undergroundObjects = Mathf.Max(0, underground);
        }
    }
}
