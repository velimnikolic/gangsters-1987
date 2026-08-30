using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The ground a filling station stands on, and what is painted and parked on it.
    ///
    /// <see cref="FuelStation"/> holds the PLAN - where the bays are, where the mouths
    /// are, what the paint says - and knows nothing about materials, primitives or
    /// prefabs. This is the other half: the asphalt under the plan, the quads that carry
    /// the paint, and the still bodies that stand in the parking row. It is shared for
    /// the same reason the plan is: the pump bench (PumpDemo) and the city's wayside
    /// station want the same forecourt, and a bench whose asphalt was cut differently
    /// from the city's would prove nothing about the city.
    ///
    /// Everything here works in the STATION'S OWN FRAME - x across the frontage, z back
    /// from the road - so a station turned to face any of the four ways lays the same
    /// forecourt. The bench's station happens to be axis-aligned; the city's is turned
    /// by whichever shoulder of whichever connecting road it landed on.
    /// </summary>
    public static class ForecourtSet
    {
        /// <summary>How far the crossovers reach past the carriageway's edge and back
        /// into the apron, so neither seam shows a hairline of ground between the two
        /// surfaces that meet there.</summary>
        const float Overlap = 0.2f;

        // ------------------------------------------------------------- the materials

        public static Material Flat(string name, Color colour, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return null;
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        public static Material Asphalt() => Flat("Forecourt Apron", new Color(0.19f, 0.19f, 0.20f), 0.14f);
        public static Material WhitePaint() => Flat("Forecourt Paint", new Color(0.84f, 0.84f, 0.80f), 0.05f);
        public static Material BluePaint() => Flat("Forecourt Paint Blue", new Color(0.15f, 0.34f, 0.64f), 0.05f);

        // ----------------------------------------------------------------- the ground

        /// <summary>One flat rectangle in the station's own frame, at its ground plus a
        /// lift. A Plane primitive is ten metres square with its face up, so it is
        /// scaled by a tenth of what is wanted and turned with the station.</summary>
        public static GameObject Slab(FuelStation station, Transform under, string name,
                                      float xFrom, float zFrom, float xTo, float zTo,
                                      float lift, Material mat)
        {
            if (station == null || xTo <= xFrom || zTo <= zFrom) return null;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = name;
            floor.transform.SetParent(under, false);
            floor.transform.SetPositionAndRotation(
                station.At((xFrom + xTo) * 0.5f, (zFrom + zTo) * 0.5f) + Vector3.up * lift,
                station.Rot);
            floor.transform.localScale = new Vector3((xTo - xFrom) / 10f, 1f, (zTo - zFrom) / 10f);
            Object.Destroy(floor.GetComponent<Collider>());
            if (mat) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return floor;
        }

        /// <summary>The plain asphalt tile the street itself is made of. A forecourt is
        /// a piece of ROAD - it is the road's own surface carried back off the kerb - so
        /// it is paved out of the same tile rather than out of a flat quad in a colour
        /// picked to look like asphalt. The two never matched: the quad took the light
        /// differently, carried none of the tile's grain, and read as a grey rectangle
        /// somebody had laid the station on.</summary>
        const string RoadTile = "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Road_Bare_01.prefab";

        /// <summary>The lane arrow, off the same kit: a 5 m road plate with the arrow
        /// already on it. The forecourt used to paint its own out of a shaft and two
        /// barbs, which is three quads to say what the kit says in one - and said it
        /// worse, because a hand-built arrowhead is a thing to get wrong and this one
        /// was. Synty lays its road decals down +Z (the city's own lots turn them the
        /// same way, BlockParkingBay).</summary>
        const string ArrowTile = "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Road_Arrow_01.prefab";

        /// <summary>How far proud of the asphalt a road decal stands, and where its
        /// pivot is out of its own centre. Both are the city's own figures.</summary>
        const float TileLift = 0.03f;
        static readonly Vector3 TilePivot = new Vector3(2.5f, 0f, 2.5f);

        /// <summary>The 5 m beat the road kit lays on (StreetKit.Cell).</summary>
        const float Cell = StreetKit.Cell;

        static GameObject _tile, _arrow;
        static bool _tileLooked, _arrowLooked;

        static GameObject Tile()
        {
            if (_tileLooked) return _tile;
            _tileLooked = true;
            _tile = DemoAssetLoad.Load<GameObject>(RoadTile);
            if (_tile == null)
                Debug.LogWarning("[Fuel] " + RoadTile + " is missing - the forecourt falls " +
                                 "back to flat asphalt-coloured ground.");
            return _tile;
        }

        static GameObject ArrowPlate()
        {
            if (_arrowLooked) return _arrow;
            _arrowLooked = true;
            _arrow = DemoAssetLoad.Load<GameObject>(ArrowTile);
            if (_arrow == null)
                Debug.LogWarning("[Fuel] " + ArrowTile + " is missing - the forecourt's " +
                                 "mouths carry no arrows.");
            return _arrow;
        }

        /// <summary>The forecourt's asphalt: the road's own surface from the back of
        /// whatever was stood out to the footway, and a crossover through the footway at
        /// each mouth.
        ///
        /// The BACK edge is measured off what actually got built (FuelStation.BackEdge)
        /// rather than written down. The cluster's own hedges and its tree line stand
        /// seventeen metres behind the canopy, and an apron sized by eye cut the asphalt
        /// straight across them - half a hedge on tarmac, half on grass.
        ///
        /// <paramref name="frontZ"/> is the local z of the pavement's OUTER edge, where
        /// the apron ends, and <paramref name="kerbZ"/> the local z of the carriageway's,
        /// where the crossovers end. Halfway between the two is the line the mouths sit
        /// on, which is what a station is wired with (FuelStation.Stand's crossZ).</summary>
        public static void LayApron(FuelStation station, Transform under, Material fallback,
                                    float frontZ, float kerbZ)
        {
            LayApron(station, under, fallback, frontZ, kerbZ, float.NaN,
                layCrossovers: true);
        }

        /// <summary>Lay the shared apron while guaranteeing an assigned parcel's back edge is
        /// opaque. CoreDemo uses this because its city-wide water plane sits below all land;
        /// PumpDemo and wayside stations keep the measured-cluster overload above.</summary>
        public static void LayApron(FuelStation station, Transform under, Material fallback,
                                    float frontZ, float kerbZ, float assignedBackZ)
        {
            LayApron(station, under, fallback, frontZ, kerbZ, assignedBackZ,
                layCrossovers: true);
        }

        /// <summary>Lay the apron with optional crossover ownership. A generated city
        /// pavement already cuts and surfaces its declared vehicle ways, so an urban fuel
        /// block asks for the forecourt body and arrows but leaves those two cuts to the
        /// pavement generator. PumpDemo and wayside roads retain the default crossovers.</summary>
        public static void LayApron(FuelStation station, Transform under, Material fallback,
                                    float frontZ, float kerbZ, float assignedBackZ,
                                    bool layCrossovers)
        {
            if (station == null) return;
            float measuredBack = station.BackEdge() - 2.5f;
            float back = float.IsNaN(assignedBackZ)
                ? measuredBack
                : Mathf.Min(measuredBack, assignedBackZ);
            Pave(station, under, "Forecourt", -FuelStation.ApronHalfX, back,
                 FuelStation.ApronHalfX, frontZ, fallback);

            if (layCrossovers)
                foreach (float side in new[] { -1f, 1f })
                {
                    float mouth = side * FuelStation.MouthX;
                    Pave(station, under, "Crossover",
                         mouth - FuelStation.MouthHalf, frontZ - Overlap,
                         mouth + FuelStation.MouthHalf, kerbZ + Overlap, fallback);
                }
            LayArrows(station, under);
        }

        /// <summary>Fill an exact road-facing parcel for a compact urban station. Unlike the
        /// full PumpDemo apron this has no separate footway to cross: Core's retained lot
        /// touches the carriageway, so one continuous asphalt rectangle is the honest ground
        /// and also guarantees that the city-wide water plane can never show through.</summary>
        public static void LayParcel(FuelStation station, Transform under, Material fallback,
                                     float halfWidth, float frontZ, float backZ)
        {
            if (station == null || halfWidth <= 0f) return;
            Pave(station, under, "Compact Forecourt", -halfWidth, backZ,
                 halfWidth, frontZ, fallback);
        }

        /// <summary>Compose a compact city parcel as an asphalt court inside one shared
        /// CorePavement cell of footway and kerb. Only the two scaled station mouths cut
        /// through the road-facing band. The old compact composer paved the complete lot
        /// as road, visually merging it with every adjoining carriageway into one enormous
        /// intersection.</summary>
        public static int LayUrbanParcel(FuelStation station, Transform under,
            Material fallback, float halfWidth, float frontZ, float backZ,
            float visualScale, System.Func<GameObject, Transform, GameObject> stand,
            int seed, out string report)
        {
            report = "compact station parcel was not laid";
            if (station == null || under == null || stand == null || halfWidth <= Cell)
                return 0;

            float innerHalf = halfWidth - Cell;
            float innerFront = frontZ - Cell;
            float innerBack = backZ + Cell;
            if (innerHalf <= 0f || innerFront <= innerBack)
            {
                LayParcel(station, under, fallback, halfWidth, frontZ, backZ);
                report = "parcel is too small for its pavement ring; laid plain forecourt";
                return 0;
            }

            // The road kit owns the court itself; CorePavement owns every perimeter tile.
            Pave(station, under, "Urban Forecourt", -innerHalf, innerBack,
                 innerHalf, innerFront, fallback);

            var parcel = Box(station, -halfWidth, backZ, halfWidth, frontZ);
            var plan = CorePavement.Around(new[] { parcel }, band: 0);
            float mouthX = FuelStation.MouthX * Mathf.Max(0.1f, visualScale);
            float mouthHalf = FuelStation.MouthHalf * Mathf.Max(0.1f, visualScale);
            var inverse = Quaternion.Inverse(station.Rot);

            // Around(..., band: 0) gives exactly one owned perimeter cell. Declaring the
            // road-facing mouth cells directly is more exact than asking the generic yard
            // finder to infer an exit outside a plan that intentionally ends at the kerb.
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.Ground[i, j]) continue;
                    var centre = new Vector3(
                        plan.X0 + (i + 0.5f) * CorePavement.Cell,
                        station.GroundY,
                        plan.Z0 + (j + 0.5f) * CorePavement.Cell);
                    var local = inverse * (centre - station.Anchor);
                    bool front = local.z >= frontZ - Cell - 0.01f &&
                                 local.z <= frontZ + 0.01f;
                    bool mouth = Mathf.Abs(Mathf.Abs(local.x) - mouthX) <= mouthHalf;
                    if (!front || !mouth || plan.Drive[i, j]) continue;
                    plan.Drive[i, j] = true;
                    plan.DriveCells++;
                }

            var pavement = new GameObject("Generated Station Pavement").transform;
            pavement.SetParent(under, false);
            return CorePavement.Lay(plan, stand, pavement, out report,
                y: station.GroundY, seed: seed, ramps: false, under: false, props: true);
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

        /// <summary>Two lane arrows, and only two: one in the mouth a car turns in at,
        /// pointing at the pumps, and one in the mouth it leaves by, pointing at the
        /// road. A forecourt this size needs no more than that - the drive between them
        /// is eleven metres of asphalt with a pump island down one side of it, and a
        /// driver who has been told which way in and which way out has been told
        /// everything the ground can tell him.</summary>
        static void LayArrows(FuelStation station, Transform under)
        {
            var arrow = ArrowPlate();
            if (arrow == null) return;
            var root = new GameObject("Arrows").transform;
            root.SetParent(under, false);

            Lay(-FuelStation.MouthX, station.IntoApron);   // in, at the pumps
            Lay(FuelStation.MouthX, station.OutToRoad);    // out, at the road

            void Lay(float x, Vector3 facing)
            {
                var rot = Quaternion.LookRotation(facing, Vector3.up);
                var centre = station.At(x, FuelStation.ArrowZ) + Vector3.up * TileLift;
                var go = Object.Instantiate(arrow, centre + rot * TilePivot, rot, root);
                go.name = arrow.name;
            }
        }

        /// <summary>Cover a rectangle of the station's frame in road tiles, on the road
        /// kit's own 5 m beat with the last row and column stretched to the remainder -
        /// which is exactly what the kit does to close a street (StreetKit.PlaceTile).
        /// The tile's pivot is its +X/+Z corner, so a piece covering [x, x+w] hangs off
        /// the far corner.</summary>
        static void Pave(FuelStation station, Transform under, string name,
                         float xFrom, float zFrom, float xTo, float zTo, Material fallback)
        {
            if (xTo <= xFrom || zTo <= zFrom) return;
            var tile = Tile();
            if (tile == null)
            {
                Slab(station, under, name, xFrom, zFrom, xTo, zTo, 0f, fallback);
                return;
            }

            var root = new GameObject(name).transform;
            root.SetParent(under, false);
            int across = Mathf.Max(1, Mathf.CeilToInt((xTo - xFrom) / Cell));
            int deep = Mathf.Max(1, Mathf.CeilToInt((zTo - zFrom) / Cell));
            for (int i = 0; i < across; i++)
            {
                float x = xFrom + i * Cell;
                float w = Mathf.Min(Cell, xTo - x);
                for (int j = 0; j < deep; j++)
                {
                    float z = zFrom + j * Cell;
                    float d = Mathf.Min(Cell, zTo - z);
                    var go = Object.Instantiate(tile, station.At(x + w, z + d), station.Rot, root);
                    go.name = tile.name;
                    if (Mathf.Abs(w - Cell) > 0.01f || Mathf.Abs(d - Cell) > 0.01f)
                        go.transform.localScale = new Vector3(w / Cell, 1f, d / Cell);
                }
            }
        }

        /// <summary>Where the mouths sit for an apron laid between these two edges.</summary>
        public static float CrossZ(float frontZ, float kerbZ) => (frontZ + kerbZ) * 0.5f;

        // ------------------------------------------------------------------ the paint

        /// <summary>What is painted on the apron. The station says where every line goes
        /// (FuelStation.Marks); this only decides what paint looks like.</summary>
        public static void Paint(FuelStation station, Transform under, Material white, Material blue)
        {
            if (station == null) return;
            var root = new GameObject("Markings").transform;
            root.SetParent(under, false);
            foreach (var mark in station.Marks())
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Paint";
                quad.transform.SetParent(root, false);
                // +90 about X lays a Quad flat FACING UP - Unity's rotations are
                // left-handed, so -90 turns its one face into the ground and the paint
                // is drawn perfectly and seen by nobody. (It reports isVisible true and
                // sits at the right place with the right material, which is what makes
                // it worth a line of comment.)
                quad.transform.SetPositionAndRotation(
                    new Vector3(mark.At.x, station.GroundY + 0.02f, mark.At.z),
                    Quaternion.Euler(90f, mark.Yaw, 0f));
                quad.transform.localScale = new Vector3(mark.Size.x, mark.Size.y, 1f);
                Object.Destroy(quad.GetComponent<Collider>());
                var paint = mark.Kind == FuelStation.Paint.Blue ? blue : white;
                if (paint) quad.GetComponent<MeshRenderer>().sharedMaterial = paint;
            }
        }

        // ------------------------------------------------------------------- the cars

        /// <summary>One body stood still on the ground: painted, stripped of everything
        /// that would tick, and entered among the road's users so the drivers plan round
        /// it like they do every other standing car.</summary>
        public static GameObject Stand(GameObject prefab, Vector3 at, Quaternion facing, Transform root)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, at, facing, root);
            go.name = prefab.name;
            LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Object.Destroy(mb);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            StoodCar.Park(go);
            WalkObstacles.Block(BoxOf(go));
            return go;
        }

        public static Bounds BoxOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var box = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return box;
        }

        /// <summary>The tanker at the back of the forecourt and a car or two in the
        /// parking row, the blue space left clear. Nothing drives them: they are what
        /// says the forecourt is a place with a life of its own rather than two pumps on
        /// a slab, and they are the same two lines in the bench and in the city.</summary>
        public static void StandTheStill(FuelStation station, Transform under, GameObject tanker,
                                         System.Collections.Generic.IList<GameObject> cars,
                                         System.Random rng)
        {
            if (station == null) return;
            if (tanker != null)
                Stand(tanker, station.At(FuelStation.LorryX, FuelStation.LorryZ),
                      Quaternion.LookRotation(station.Way(1f, 0f), Vector3.up), under);

            if (cars == null || cars.Count == 0 || station.Parking == null) return;
            var facing = Quaternion.LookRotation(station.ParkFacing, Vector3.up);
            for (int i = 0; i < station.Parking.Length; i++)
            {
                // the blue bay stays clear, and one other, or the row reads as a car park
                if (i == FuelStation.DisabledSpace || i == 2) continue;
                Stand(cars[rng.Next(cars.Count)], station.Parking[i], facing, under);
            }
        }
    }
}
