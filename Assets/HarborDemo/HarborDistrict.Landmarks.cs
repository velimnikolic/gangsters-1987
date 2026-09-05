using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // The port's skyline and its irregular east end. The container terminal remains the
    // long working spine; this file adds a reclaimed bulk pier to that spine and puts the
    // authority building inside the wire. Neither landmark belongs to the industrial
    // parcels beyond the harbor road.
    public partial class HarborDistrict
    {
        public const float BulkTerminalLength = 105f;
        public const float BulkTerminalProjection = 18f;
        public const float BulkSiloShellTop = 70f;
        public const float BulkSiloElevatorTop = 94f;
        public const float BulkSiloFootprintWidth = 58f;
        public const float BulkSiloFootprintDepth = 44f;
        public const float PortHeadquartersMinimumHeight = 30f;

        static Mesh _bulkShellMesh, _bulkSteelMesh;

        Material _bulkShellMaterial, _bulkSteelMaterial;
        Transform _headquarters;
        Rect _headquartersBounds;

        float BulkTerminalEast => QuayHalf + BulkTerminalLength;
        float BulkTerminalSouth => -BulkTerminalProjection;
        float BulkTerminalNorth => Mathf.Max(_fenceZ + 10f, _streetZ - StreetKit.OuterHalf - 2f);

        Rect BulkTerminalApron => Rect.MinMaxRect(
            QuayHalf, BulkTerminalSouth, BulkTerminalEast, BulkTerminalNorth);

        /// <summary>The eastward reclamation is part of the harbor reservation, not a
        /// second industrial district. Kept public as a numerical audit seam.</summary>
        public float PlannedBulkTerminalEast => QuayHalf + BulkTerminalLength;

        /// <summary>Builds the real port authority headquarters in the centre of the shed
        /// line. The caller falls back to its old warehouse when the tower asset is absent.</summary>
        bool TryBuildHeadquarters(float front, ref float backMax,
                                  List<Vector2> taken, List<Vector2> blocked)
        {
            var tower = HarborKit.TryLoad(HarborKit.PortHeadquarters);
            if (tower == null) return false;
            var wing = HarborKit.TryLoad(HarborKit.PortAdministration);

            var towerBounds = HarborKit.PrefabBounds(tower);
            var wingBounds = HarborKit.PrefabBounds(wing);
            float gap = wing != null ? 5f : 0f;
            float totalWidth = towerBounds.size.x + gap + (wing != null ? wingBounds.size.x : 0f);
            float x = -totalWidth * 0.5f;

            _headquarters = new GameObject("Port Authority Headquarters").transform;
            _headquarters.SetParent(_warehouseRoot, false);

            SeatHeadquartersPiece(tower, x, front, "Headquarters Tower");
            x += towerBounds.size.x + gap;
            if (wing != null)
                SeatHeadquartersPiece(wing, x, front + 1.5f, "Administration Wing");

            var bounds = HarborKit.BoundsOf(_headquarters.gameObject);
            if (bounds.size.sqrMagnitude < 1f)
            {
                Destroy(_headquarters.gameObject);
                _headquarters = null;
                return false;
            }

            _headquartersBounds = Rect.MinMaxRect(bounds.min.x, bounds.min.z,
                                                   bounds.max.x, bounds.max.z);
            backMax = Mathf.Max(backMax, bounds.max.z);
            taken.Add(new Vector2(bounds.min.x, bounds.max.x));
            blocked.Add(new Vector2(bounds.min.x - 8f, bounds.max.x + 8f));

            // HarborKit removes the catalogue colliders with the pack behaviours. Restore
            // one honest compound footprint for picking, bullets and the shared cutaway.
            var collider = _headquarters.gameObject.AddComponent<BoxCollider>();
            collider.center = _headquarters.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
            BuildingCutaway.Prepare(_headquarters.gameObject, PortHeadquartersMinimumHeight);
            return true;
        }

        void SeatHeadquartersPiece(GameObject prefab, float minX, float minZ, string name)
        {
            var go = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), _headquarters);
            go.name = name;
            HarborKit.StripBehaviours(go, keepAnimator: false);
            var bounds = HarborKit.BoundsOf(go);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x + minX - bounds.min.x,
                                                TileTop + ShedLift,
                                                p.z + minZ - bounds.min.z);
        }

        void DressHeadquarters()
        {
            if (_headquarters == null) return;

            // A short arrival court separates the authority building from the lorry loop.
            AsphaltStrip(_headquartersBounds.xMin - 2f, _headquartersBounds.xMax + 2f,
                         YardRoadZ1 + 0.5f, _headquartersBounds.yMin - 0.8f, _apronRoot);

            var bounds = HarborKit.BoundsOf(_headquarters.gameObject);
            var aerial = HarborKit.TryLoad(HarborKit.Antenna);
            var flag = HarborKit.TryLoad(HarborKit.Flagpole);
            var sign = HarborKit.TryLoad(HarborKit.CompanySign);
            if (aerial != null)
                HarborKit.Sit(aerial, new Vector3(bounds.center.x - 6f, bounds.max.y,
                                                   bounds.center.z), 0f, WorksRoot, "HQ Aerial");
            if (flag != null)
            {
                var mast = HarborKit.Sit(flag,
                    new Vector3(_headquartersBounds.xMax + 3f, TileTop,
                                _headquartersBounds.yMin - 3f), 90f, WorksRoot, "Authority Flag");
                if (mast != null) mast.transform.localScale *= 2.8f;
            }
            if (sign != null)
                HarborKit.Sit(sign,
                    new Vector3(_headquartersBounds.center.x, TileTop,
                                _headquartersBounds.yMin - 2.2f), 180f, WorksRoot,
                    "Port Authority Sign");
        }

        /// <summary>Lays the reclaimed apron after the ordinary rectangular container
        /// apron. It projects into the water and runs farther inland, giving the port an
        /// L-shaped plan instead of one uniformly narrow strip.</summary>
        void BuildBulkTerminalApron()
        {
            PourTerminalApron("Bulk apron", BulkTerminalApron);
        }

        bool InsideBulkTerminal(float x0, float x1, float z0, float z1)
        {
            var r = BulkTerminalApron;
            return x0 >= r.xMin - 0.01f && x1 <= r.xMax + 0.01f &&
                   z0 >= r.yMin - 0.01f && z1 <= r.yMax + 0.01f;
        }

        /// <summary>The quay wraps around the reclaimed pier: an eighteen-metre return,
        /// a hundred-metre bulk berth and the outer return back to the natural shore.</summary>
        void BuildBulkTerminalQuay()
        {
            if (_quayStraight == null) return;
            var westFront = new Vector3(QuayHalf, 0f, BulkTerminalSouth);
            var westBack = new Vector3(QuayHalf, 0f, 0f);
            var eastFront = new Vector3(BulkTerminalEast, 0f, BulkTerminalSouth);
            var eastBack = new Vector3(BulkTerminalEast, 0f, 0f);

            HarborKit.LayRun(_quayStraight, westBack, westFront, _quayRoot, "Bulk Quay Return",
                i => _quayWorn != null && (i & 1) == 0 ? _quayWorn : _quayStraight);
            HarborKit.LayRun(_quayStraight, westFront, eastFront, _quayRoot, "Bulk Quay",
                i => _quayWorn != null && i % 3 != 1 ? _quayWorn : _quayStraight);
            HarborKit.LayRun(_quayStraight, eastFront, eastBack, _quayRoot, "Bulk Quay Return",
                i => _quayWorn != null && (i & 1) != 0 ? _quayWorn : _quayStraight);

            int slot = 0;
            for (float x = QuayHalf + 4.5f; x < BulkTerminalEast - 4f; x += 9f, slot++)
            {
                var bollard = slot % 2 == 0 || _bollard3 == null ? _bollard1 : _bollard3;
                HarborKit.Prop(bollard,
                    new Vector3(x, BollardY, BulkTerminalSouth + BollardZ),
                    0f, _quayRoot, "Bulk Bollard");
            }
            for (float x = QuayHalf + 13.5f; x < BulkTerminalEast - 4f; x += 27f)
                HarborKit.Prop(_pierLamp,
                    new Vector3(x, TileTop, BulkTerminalSouth + 2.2f),
                    180f, _quayRoot);

            if (_shoreRock != null)
                for (int k = 0; k < 6; k++)
                {
                    var pos = new Vector3(BulkTerminalEast + 1.5f + k * 2.2f,
                        WaterY - 0.6f + k * 0.35f,
                        BulkTerminalSouth - 1.5f - k * 2.2f + HarborKit.Range(_rng, -1f, 1f));
                    var rock = HarborKit.Prop(_shoreRock, pos,
                        HarborKit.Range(_rng, 0f, 360f), _quayRoot, "Rock");
                    rock.transform.localScale = Vector3.one * HarborKit.Range(_rng, 0.9f, 1.6f);
                }
        }

        /// <summary>Continues the lorry loop into the bulk pier and gives it a hammerhead
        /// beside the silo loading face. The container yard's traffic lanes stay intact.</summary>
        void BuildBulkTerminalRoads()
        {
            AsphaltStrip(_gateEastX + 5f, BulkTerminalEast - 8f,
                         YardRoadZ0, YardRoadZ1, _apronRoot);
            AsphaltStrip(BulkTerminalEast - 20f, BulkTerminalEast - 8f,
                         YardRoadZ0, BulkTerminalNorth - 8f, _apronRoot);
            AsphaltStrip(QuayHalf, BulkTerminalEast - 1f,
                         QuayLaneZ - 3f, QuayLaneZ + 3f, _apronRoot);
        }

        void BuildBulkTerminal()
        {
            float x1 = BulkTerminalEast - 22f;
            float x0 = x1 - BulkSiloFootprintWidth;
            float z1 = BulkTerminalNorth - 7f;
            float z0 = z1 - BulkSiloFootprintDepth;
            var area = Rect.MinMaxRect(x0, z0, x1, z1);

            var root = new GameObject("Bulk Silo Terminal").transform;
            root.SetParent(WorksRoot, false);
            root.localPosition = new Vector3(area.center.x, TileTop, area.center.y);
            MeshPart(root, "Twelve giant silo shells", BulkShellMesh(), BulkShellMaterial());
            MeshPart(root, "Elevator and crown galleries", BulkSteelMesh(), BulkSteelMaterial());

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, BulkSiloElevatorTop * 0.5f, 0f);
            collider.size = new Vector3(area.width, BulkSiloElevatorTop, area.height);
            BuildingCutaway.Prepare(root.gameObject);
            _namedWorks.Add((root, "Bulk Silo Terminal"));

            // The gallery is a sibling so its long overhead run does not turn the whole
            // apron into one blocked rectangle when BlockTheYard measures the silos.
            var conveyor = new GameObject("Bulk Conveyor and Ship Loader").transform;
            conveyor.SetParent(WorksRoot, false);
            conveyor.localPosition = root.localPosition;
            float loaderZ = BulkTerminalSouth + 3f - area.center.y;
            var conveyorMesh = ConveyorMesh(loaderZ);
            _terminalMeshes.Add(conveyorMesh);
            MeshPart(conveyor, "Covered conveyor", conveyorMesh, BulkSteelMaterial());

            DressBulkLoadingCourt(area);
        }

        void DressBulkLoadingCourt(Rect silos)
        {
            var truck = HarborKit.TryLoad(HarborKit.TownTruck) ?? HarborKit.TryLoad(HarborKit.Truck);
            var forklift = HarborKit.TryLoad(HarborKit.Forklift);
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var sign = HarborKit.TryLoad(HarborKit.DangerSign);
            float x = BulkTerminalEast - 14f;

            if (truck != null)
                foreach (float z in new[] { YardRoadZ1 + 10f, YardRoadZ1 + 25f })
                {
                    var go = HarborKit.Sit(truck, new Vector3(x, TileTop, z), 0f,
                                            WorksRoot, "Bulk Lorry");
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                }
            if (forklift != null)
            {
                var go = HarborKit.Sit(forklift,
                    new Vector3(silos.xMax + 6f, TileTop, silos.yMin - 5f), 90f,
                    WorksRoot, "Bulk Forklift");
                HarborKit.StripBehaviours(go, keepAnimator: false);
            }
            if (pallet != null)
                for (int k = 0; k < 5; k++)
                    HarborKit.Sit(pallet,
                        new Vector3(silos.xMax + 5f + (k & 1) * 1.6f, TileTop,
                                    silos.yMin + 3f + k * 1.8f), k * 7f,
                        WorksRoot, "Bulk Pallet");
            if (sign != null)
                HarborKit.Sit(sign,
                    new Vector3(QuayHalf + 4f, TileTop, YardRoadZ1 + 2f), 90f,
                    WorksRoot, "Bulk Terminal Sign");
        }

        static MeshRenderer MeshPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            go.isStatic = true;
            return renderer;
        }

        Material BulkShellMaterial()
        {
            if (_bulkShellMaterial == null)
                _bulkShellMaterial = Keep(HarborKit.Flat(
                    "Harbor silo concrete", new Color(0.59f, 0.58f, 0.53f), 0.16f));
            return _bulkShellMaterial;
        }

        Material BulkSteelMaterial()
        {
            if (_bulkSteelMaterial == null)
                _bulkSteelMaterial = Keep(HarborKit.Flat(
                    "Harbor silo steel", new Color(0.29f, 0.32f, 0.31f), 0.28f));
            return _bulkSteelMaterial;
        }

        static Mesh BulkShellMesh()
        {
            if (_bulkShellMesh != null) return _bulkShellMesh;
            var draft = new Draft();
            float[] heights =
            {
                64f, 65f, 63f, 66f,
                65f, 64f, 66f, 63f,
                66f, 64f, 65f, 63f,
            };
            int at = 0;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                {
                    float x = (column - 1.5f) * 12.5f;
                    float z = (row - 1f) * 12.5f;
                    float height = heights[at++];
                    draft.Cylinder(x, z, 0f, height, 5.7f, 20);
                    draft.Cone(x, z, height, BulkSiloShellTop - height, 5.3f, 20);
                }
            _bulkShellMesh = draft.Bake("Harbor giant silo shells");
            return _bulkShellMesh;
        }

        static Mesh BulkSteelMesh()
        {
            if (_bulkSteelMesh != null) return _bulkSteelMesh;
            var draft = new Draft();
            draft.Box(new Vector3(-24f, 42f, 0f), new Vector3(9f, 84f, 11f));
            draft.Box(new Vector3(-24f, 89f, 0f), new Vector3(12f, 10f, 14f));
            foreach (float z in new[] { -12.5f, 0f, 12.5f })
                draft.Box(new Vector3(0f, 71f, z), new Vector3(43f, 2.2f, 3.6f));
            draft.Box(new Vector3(-19f, 73f, 0f), new Vector3(3.6f, 2.2f, 28f));
            draft.Box(new Vector3(-19f, 73f, 0f), new Vector3(10f, 2.2f, 3.6f));

            // Ring beams and roof galleries give the silo group a readable construction scale.
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                {
                    float x = (column - 1.5f) * 12.5f, z = (row - 1f) * 12.5f;
                    for (int ring = 0; ring < 10; ring++)
                        draft.Cylinder(x, z, 5f + ring * 6f, 0.13f, 5.74f, 32);
                    draft.Box(new Vector3(x, 70.6f, z), new Vector3(1.5f, 1.2f, 1.5f));
                }
            foreach (float z in new[] { -12.5f, 0f, 12.5f })
                foreach (float edge in new[] { -1.9f, 1.9f })
                {
                    draft.Box(new Vector3(0f, 73.1f, z + edge), new Vector3(43f, 0.10f, 0.10f));
                    for (float x = -20f; x <= 20f; x += 2.5f)
                        draft.Box(new Vector3(x, 72.5f, z + edge), new Vector3(0.09f, 1.2f, 0.09f));
                }
            for (float y = 4f; y < 84f; y += 6f)
            {
                draft.Box(new Vector3(-24f, y, 0f), new Vector3(9.15f, 0.18f, 11.15f));
                draft.Box(new Vector3(-29.2f, y, 0f), new Vector3(1.4f, 0.2f, 3f));
            }
            for (float y = 0.5f; y < 84f; y += 0.45f)
                draft.Box(new Vector3(-29.4f, y, -1f), new Vector3(0.85f, 0.07f, 0.12f));
            foreach (float x in new[] { -29.86f, -28.94f })
                draft.Box(new Vector3(x, 42f, -1f), new Vector3(0.09f, 84f, 0.12f));

            _bulkSteelMesh = draft.Bake("Harbor silo steelwork");
            return _bulkSteelMesh;
        }

        static Mesh ConveyorMesh(float loaderZ)
        {
            var draft = new Draft();
            const float x = -24f;
            float startZ = -5f;
            float length = Mathf.Abs(startZ - loaderZ);
            float centre = (startZ + loaderZ) * 0.5f;
            draft.Box(new Vector3(x, 31f, centre), new Vector3(3.4f, 2.0f, length));

            int supports = Mathf.Max(3, Mathf.FloorToInt(length / 17f));
            for (int k = 1; k < supports; k++)
            {
                float z = Mathf.Lerp(startZ, loaderZ, k / (float)supports);
                draft.Box(new Vector3(x - 1.4f, 14.5f, z), new Vector3(0.8f, 29f, 0.8f));
                draft.Box(new Vector3(x + 1.4f, 14.5f, z), new Vector3(0.8f, 29f, 0.8f));
                for (float y = 2f; y < 28f; y += 6f)
                    draft.Beam(new Vector3(x - 1.4f, y, z), new Vector3(x + 1.4f, y + 5f, z), 0.22f);

            }

            // Loader tower on the quay edge and a short boom over the ship's hatch.
            foreach (float dx in new[] { -3f, 3f })
                foreach (float dz in new[] { -3f, 3f })
                    draft.Box(new Vector3(x + dx, 29f, loaderZ + dz), new Vector3(0.8f, 58f, 0.8f));
            for (float y = 6f; y < 58f; y += 8f)
            {
                draft.Box(new Vector3(x, y, loaderZ), new Vector3(7f, 0.4f, 7f));
                foreach (float dz in new[] { -3f, 3f })
                    draft.Beam(new Vector3(x - 3f, y, loaderZ + dz), new Vector3(x + 3f, y + 7f, loaderZ + dz), 0.3f);
            }
            draft.Box(new Vector3(x, 57f, loaderZ), new Vector3(8f, 6f, 8f));
            draft.Box(new Vector3(x, 52f, loaderZ - 10f), new Vector3(4f, 4f, 20f));
            draft.Box(new Vector3(x, 34f, loaderZ - 19f), new Vector3(2f, 36f, 2f));
            return draft.Bake("Harbor bulk conveyor and loader");
        }

        sealed class Draft
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<int> _triangles = new List<int>();

            public void Cylinder(float x, float z, float bottom, float height,
                                 float radius, int sides)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a = Mathf.PI * 2f * k / sides;
                    float b = Mathf.PI * 2f * (k + 1) / sides;
                    var one = new Vector3(x + Mathf.Cos(a) * radius, bottom,
                                          z + Mathf.Sin(a) * radius);
                    var two = new Vector3(x + Mathf.Cos(b) * radius, bottom,
                                          z + Mathf.Sin(b) * radius);
                    Quad(one, one + Vector3.up * height,
                         two + Vector3.up * height, two);
                }
            }

            public void Cone(float x, float z, float bottom, float height,
                             float radius, int sides)
            {
                var apex = new Vector3(x, bottom + height, z);
                for (int k = 0; k < sides; k++)
                {
                    float a = Mathf.PI * 2f * k / sides;
                    float b = Mathf.PI * 2f * (k + 1) / sides;
                    Triangle(new Vector3(x + Mathf.Cos(a) * radius, bottom,
                                         z + Mathf.Sin(a) * radius),
                             apex,
                             new Vector3(x + Mathf.Cos(b) * radius, bottom,
                                         z + Mathf.Sin(b) * radius));
                }
            }

            public void Box(Vector3 centre, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                var p000 = centre + new Vector3(-h.x, -h.y, -h.z);
                var p001 = centre + new Vector3(-h.x, -h.y, h.z);
                var p010 = centre + new Vector3(-h.x, h.y, -h.z);
                var p011 = centre + new Vector3(-h.x, h.y, h.z);
                var p100 = centre + new Vector3(h.x, -h.y, -h.z);
                var p101 = centre + new Vector3(h.x, -h.y, h.z);
                var p110 = centre + new Vector3(h.x, h.y, -h.z);
                var p111 = centre + new Vector3(h.x, h.y, h.z);

                Quad(p001, p101, p111, p011);
                Quad(p000, p010, p110, p100);
                Quad(p000, p001, p011, p010);
                Quad(p100, p110, p111, p101);
                Quad(p010, p011, p111, p110);
                Quad(p000, p100, p101, p001);
            }

            public void Beam(Vector3 a, Vector3 b, float width)
            {
                int start = _vertices.Count;
                Box(Vector3.zero, new Vector3(width, Vector3.Distance(a, b), width));
                var rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                for (int i = start; i < _vertices.Count; i++)
                    _vertices[i] = (a + b) * 0.5f + rotation * _vertices[i];
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
                _triangles.Add(first); _triangles.Add(first + 1); _triangles.Add(first + 2);
                _triangles.Add(first); _triangles.Add(first + 2); _triangles.Add(first + 3);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int first = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _triangles.Add(first); _triangles.Add(first + 1); _triangles.Add(first + 2);
            }

            public Mesh Bake(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = _vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(_vertices);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
