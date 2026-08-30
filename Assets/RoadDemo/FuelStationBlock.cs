using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The useful part of PumpDemo presented as one city block: the complete filling
    /// station parcel, the generated city pavement around it, and two explicit road
    /// connectors. The long PumpDemo road circuit is a bench and is deliberately not
    /// part of this visual block.
    ///
    /// The station itself remains the shared <see cref="FuelStation"/> plan and
    /// <see cref="ForecourtSet"/> composition. <see cref="FuelStationBlockRuntime"/>
    /// supplies a logical circuit only when this block is watched in ResidentialDemo;
    /// in the city the same entry and exit bind to the real lane beside the block.
    ///
    /// Compose at the origin, then translate the returned root. The editor benches use
    /// that convention already: several shared composers place children in world space
    /// before parenting them.
    /// </summary>
    public static class FuelStationBlock
    {
        public const float RoadY = -0.08f;
        public const float ApronY = -0.1f;
        public const float GroundY = -0.13f;

        // The rectangular station lot in the station's own frame. +Z faces the road.
        // Twenty metres each side retains the whole apron, parking, tanker and dressing;
        // the extra five metres outside ApronHalfX are the parcel's planted shoulders.
        public const float ParcelHalfX = 20f;
        public const float ParcelBackZ = -25f;
        public const float ParcelFrontZ = FuelStation.SetBack;

        // This is intentionally the central block generator's current pavement width,
        // not a second fuel-demo width. Its two gate claims cut road tiles all the way
        // through the band at the real PumpDemo mouths.
        public const float PavementWidth = CoreBlockMetrics.PavementWidth;
        // The frontage's innermost row belongs to the pavement plan: sidewalk between
        // the mouths, vehicle surface through them. Stopping the shared apron here keeps
        // two coplanar road tiles from being laid in each opening.
        public const float ForecourtFrontZ = ParcelFrontZ - CorePavement.Cell;
        public const float KerbZ = ParcelFrontZ + PavementWidth;
        public const float CrossZ = (ParcelFrontZ + KerbZ) * 0.5f;

        // The block faces world -Z so it reads like the supplied top-down reference.
        // CityRoadZ is the centre line immediately outside its generated front pavement.
        public const float CityRoadZ = -(KerbZ + StreetKit.StreetHalf);
        public const float KerbRun = 24f;

        // Invisible ResidentialDemo-only LaneNet. None of this is drawn by Compose.
        public const float HarnessHalfRun = 50f;
        public const float HarnessBackRoadZ = CityRoadZ - 60f;

        const string GroundMaterial = "Assets/Materials/Weapons/Demo Ground.mat";
        const string WhiteMaterial = "Assets/Materials/Fuel Paint White.mat";
        const string BlueMaterial = "Assets/CityKit/LotPads/LotPad_3D6B9E.mat";

        /// <summary>The visible block only; the road centre lies just outside yMin.</summary>
        public static readonly Rect PreviewBounds = Rect.MinMaxRect(
            -ParcelHalfX - PavementWidth,
            -ParcelFrontZ - PavementWidth,
            ParcelHalfX + PavementWidth,
            -ParcelBackZ + PavementWidth);

        public sealed class Stood
        {
            public FuelStation Station;
            public int PavementTiles;
            public int ParkedCars;
            public Vector3 Entry;
            public Vector3 Exit;
            public string PavementReport;

            public override string ToString() =>
                $"full PumpDemo parcel, {PavementTiles} generated pavement tile(s), " +
                $"{Station?.Bays?.Length ?? 0} pump bay(s), {ParkedCars} standing vehicle(s), " +
                "entry and exit open to the city road";
        }

        public static Stood Compose(Transform root, int seed)
        {
            var stood = new Stood();
            if (root == null) return stood;

#if UNITY_EDITOR
            var stationRoot = new GameObject("Full PumpDemo Station Parcel").transform;
            stationRoot.SetParent(root, false);
            var rotation = Quaternion.Euler(0f, 180f, 0f);
            stood.Station = FuelStation.Stand(
                stationRoot, Vector3.zero, rotation, ApronY, CrossZ);

            var asphalt = Load<Material>(GroundMaterial);
            ForecourtSet.Slab(stood.Station, stationRoot, "Station Parcel Ground",
                -ParcelHalfX, ParcelBackZ, ParcelHalfX, ParcelFrontZ,
                GroundY - ApronY, asphalt);

            // CorePavement owns the frontage and both crossovers. The apron therefore
            // stops one cell short: the generator lays sidewalk between the mouths and
            // uninterrupted vehicle surface through them, with no tile under a tile.
            ForecourtSet.LayApron(stood.Station, stationRoot, asphalt,
                ForecourtFrontZ, KerbZ, ParcelBackZ, layCrossovers: false);
            ForecourtSet.Paint(stood.Station, stationRoot,
                Load<Material>(WhiteMaterial), Load<Material>(BlueMaterial));

            // Keep PumpDemo's working forecourt dressing, but not its wayside tree line:
            // this is an urban block and the generated pavement supplies its perimeter.
            stood.Station.Dress(stationRoot, new System.Random(seed * 613 + 71),
                plantTreeLine: false);

            var standing = new GameObject("Standing Vehicles").transform;
            standing.SetParent(stationRoot, false);
            var cars = TestBench.WeightedCars(FindVehicle);
            var tanker = FindVehicle("SM_Veh_Truck_Delivery_01") ?? FindVehicle("SM_Veh_Truck_01");
            ForecourtSet.StandTheStill(
                stood.Station, standing, tanker, cars, new System.Random(seed * 613 + 97));
            stood.ParkedCars = standing.GetComponentsInChildren<Renderer>(true).Length > 0
                ? standing.childCount : 0;

            var pavementRoot = new GameObject("Generated City Pavement").transform;
            pavementRoot.SetParent(root, false);
            var plan = PavementPlan(stood.Station);
            stood.PavementTiles = CorePavement.Lay(
                plan, Raise, pavementRoot, out string pavement,
                y: ApronY, seed: seed * 613 + 109, ramps: true, under: false, props: true);
            stood.PavementTiles -= ClearForecourtMouths(pavementRoot, stood.Station);
            stood.PavementReport = pavement;

            var connectors = new GameObject("City Road Connectors").transform;
            connectors.SetParent(root, false);
            stood.Entry = Marker(connectors, "ENTRY -> city road", stood.Station,
                -FuelStation.MouthX);
            stood.Exit = Marker(connectors, "EXIT -> city road", stood.Station,
                FuelStation.MouthX);

            var live = root.GetComponent<FuelStationBlockRuntime>();
            if (live == null) live = root.gameObject.AddComponent<FuelStationBlockRuntime>();
            live.nameSeed = seed;
#else
            Debug.LogError("[FuelBlock] The visual block loads Synty prefabs in the editor.");
#endif
            return stood;
        }

#if UNITY_EDITOR
        static T Load<T>(string path) where T : Object => DemoAssetLoad.Load<T>(path);

        static CorePavement.Plan PavementPlan(FuelStation station)
        {
            var parcel = Box(station, -ParcelHalfX, ParcelBackZ,
                                      ParcelHalfX, ParcelFrontZ);
            var gates = new List<Bounds>(2)
            {
                Box(station,
                    -FuelStation.MouthX - FuelStation.MouthHalf,
                    ParcelFrontZ - CorePavement.Cell,
                    -FuelStation.MouthX + FuelStation.MouthHalf,
                    ParcelFrontZ),
                Box(station,
                    FuelStation.MouthX - FuelStation.MouthHalf,
                    ParcelFrontZ - CorePavement.Cell,
                    FuelStation.MouthX + FuelStation.MouthHalf,
                    ParcelFrontZ),
            };
            var plan = CorePavement.Around(new[] { parcel },
                band: CoreBlockMetrics.PavementTiles, gates: gates);

            // A declared CorePavement gate normally identifies the last owned cell and
            // grows a driveway OUTSIDE it. Here that last row is itself part of the open
            // forecourt mouth (and carries the 5 m arrow plate), so it must be vehicle
            // surface too. Otherwise the generator correctly opens the outer band but
            // puts an inner kerb and pavement furniture straight across each arrow.
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.Gate[i, j] || !plan.Ground[i, j] || plan.Drive[i, j]) continue;
                    plan.Drive[i, j] = true;
                    plan.DriveCells++;
                }
            return plan;
        }

        /// <summary>
        /// CorePavement correctly kerbs the inner end of a generic declared driveway.
        /// A filling-station mouth is different: it joins owned forecourt asphalt, so a
        /// transverse kerb there blocks the lane. Remove that single generated row (and
        /// any furniture it received) after composition; the shared apron immediately
        /// below it remains as the continuous driving surface.
        /// </summary>
        static int ClearForecourtMouths(Transform pavement, FuelStation station)
        {
            if (pavement == null || station == null) return 0;

            var remove = new List<GameObject>();
            var inverse = Quaternion.Inverse(station.Rot);
            float innerZ = ForecourtFrontZ - CorePavement.Cell;

            foreach (Transform child in pavement)
            {
                var renderer = child.GetComponentInChildren<Renderer>();
                if (renderer == null) continue;
                var local = inverse * (renderer.bounds.center - station.Anchor);
                // The four inner-corner cells beside the two mouths sit just outside the
                // mouths' literal bounds: their centres are in the central island and the
                // two outer shoulders. Filtering by distance from MouthX consequently
                // matched none of them and left diagonal pavement wedges cutting across
                // the forecourt behind the island. The whole inner row belongs to the
                // station parcel; the apron already supplies its continuous asphalt.
                bool overForecourt = Mathf.Abs(local.x) <= ParcelHalfX + 0.1f;
                bool inInnerRow = local.z >= innerZ - 0.1f && local.z <= ForecourtFrontZ + 0.1f;
                if (overForecourt && inInnerRow) remove.Add(child.gameObject);
            }

            foreach (var go in remove) Object.DestroyImmediate(go);
            return remove.Count;
        }

        static Bounds Box(FuelStation station, float x0, float z0, float x1, float z1)
        {
            var points = new[]
            {
                station.At(x0, z0), station.At(x1, z0),
                station.At(x0, z1), station.At(x1, z1),
            };
            var box = new Bounds(points[0], new Vector3(0f, 1f, 0f));
            for (int i = 1; i < points.Length; i++) box.Encapsulate(points[i]);
            var size = box.size;
            size.y = 1f;
            box.size = size;
            return box;
        }

        static GameObject Raise(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        static Vector3 Marker(Transform root, string name, FuelStation station, float x)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(root, false);
            marker.SetPositionAndRotation(station.At(x, KerbZ), station.Rot);
            return marker.position;
        }

        static readonly string[] VehicleFolders =
        {
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/",
        };

        static GameObject FindVehicle(string name)
        {
            foreach (var folder in VehicleFolders)
            {
                string path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) return prefab;
            }
            return null;
        }
#endif
    }
}
