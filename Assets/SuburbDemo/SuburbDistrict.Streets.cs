using System.Collections.Generic;
using UnityEngine;

namespace SuburbDemo
{
    // The ground, one Town tile per cell, read off the cell grid: road and zebra,
    // pavement pieces chosen by what stands next to them (a kerb where the road is,
    // a corner where it turns, the dropped kerb where a driveway or path meets it),
    // the dead-end caps with their L wraps, and under the lots whatever surface the
    // lot asked for. Then the street furniture and the leftover lawn.
    public partial class SuburbDistrict
    {
        GameObject _road, _zebra, _roadCap, _roadPatch, _sidewalk, _swCorner, _swEndCorner, _swCapWrap, _swCrossing, _swDriveway, _swPath,
            _driveway, _path, _pathDoor, _grass, _grass2, _concrete, _dirt, _parking;
        List<GameObject> _swAlts;
        GameObject _streetlamp, _streetlamp2, _signPole, _stopSign, _busStopSign, _bench, _manhole, _drain, _speedBump;
        List<GameObject> _bins, _yardTrees, _tallTrees, _bushes, _tufts, _flowers, _streetSigns, _framedTrees;
        GameObject _treeBase;

        void LoadKit()
        {
            _road = TownKit.Load(TownKit.Road);
            _zebra = TownKit.Load(TownKit.Zebra);
            _roadCap = TownKit.Load(TownKit.RoadCap);
            _roadPatch = TownKit.Load(TownKit.RoadPatch);
            _parking = TownKit.Load(TownKit.RoadParking);
            _sidewalk = TownKit.Load(TownKit.Sidewalk);
            _swAlts = TownKit.LoadAll(TownKit.SidewalkAlts);
            _swCorner = TownKit.Load(TownKit.SidewalkCorner);
            _swEndCorner = TownKit.Load(TownKit.SidewalkEndCorner);
            _swCapWrap = TownKit.Load(TownKit.SidewalkCapWrap);
            _swCrossing = TownKit.Load(TownKit.SidewalkCrossing);
            _swDriveway = TownKit.Load(TownKit.SidewalkDriveway);
            _swPath = TownKit.Load(TownKit.SidewalkPath);
            _driveway = TownKit.Load(TownKit.Driveway);
            _path = TownKit.Load(TownKit.Path);
            _pathDoor = TownKit.Load(TownKit.PathDoor);
            _grass = TownKit.Load(TownKit.Grass);
            _grass2 = TownKit.Load(TownKit.Grass2);
            _concrete = TownKit.Load(TownKit.Concrete);
            _dirt = TownKit.Load(TownKit.Dirt);

            _streetlamp = TownKit.Load(TownKit.Streetlamp);
            _streetlamp2 = TownKit.Load(TownKit.Streetlamp2);
            _signPole = TownKit.Load(TownKit.SignPole);
            _streetSigns = TownKit.LoadAll(TownKit.StreetSigns);
            _stopSign = TownKit.Load(TownKit.StopSign);
            _busStopSign = TownKit.Load(TownKit.BusStopSign);
            _bench = TownKit.Load(TownKit.Bench);
            _manhole = TownKit.Load(TownKit.Manhole);
            _drain = TownKit.Load(TownKit.Drain);
            _speedBump = TownKit.Load(TownKit.SpeedBump);
            _bins = TownKit.LoadAll(TownKit.Bins);
            _yardTrees = TownKit.LoadAll(TownKit.YardTrees);
            _tallTrees = TownKit.LoadAll(TownKit.TallTrees);
            _bushes = TownKit.LoadAll(TownKit.Bushes);
            _tufts = TownKit.LoadAll(TownKit.GrassTufts);
            _flowers = TownKit.LoadAll(TownKit.Flowers);
            _treeBase = TownKit.Load(TownKit.TreeBase);
            _framedTrees = TownKit.LoadAll(TownKit.FramedTrees);
            LoadLotKit();
        }

        // ------------------------------------------------------------ tiles

        bool IsRoadish(int cx, int cz) => InGrid(cx, cz) && (_kind[cx, cz] == CellKind.Road || _kind[cx, cz] == CellKind.Zebra || _kind[cx, cz] == CellKind.Cap);

        static readonly (Vector3 dir, int dx, int dz)[] Dirs =
        {
            (Vector3.forward, 0, 1), (Vector3.right, 1, 0), (Vector3.back, 0, -1), (Vector3.left, -1, 0),
        };

        /// <summary>The yaw at which a piece with something on its local +X and +Z sides
        /// has those sides towards world a and b.</summary>
        static int YawForTwoSides(Vector3 a, Vector3 b)
        {
            for (int y = 0; y < 360; y += 90)
            {
                var px = TownKit.Turn(TownKit.Side.PlusX, y);
                var pz = TownKit.Turn(TownKit.Side.PlusZ, y);
                if ((Vector3.Dot(px, a) > 0.9f && Vector3.Dot(pz, b) > 0.9f) || (Vector3.Dot(px, b) > 0.9f && Vector3.Dot(pz, a) > 0.9f)) return y;
            }
            return 0;
        }

        /// <summary>One tile per cell from the plan. <paramref name="hostGround"/> is the
        /// host saying it lays the ground itself - the city's island rings the suburb
        /// and runs under nothing of it - in which case the skirt of lawn and the big
        /// green plane under the world are left off; on its own the suburb still needs
        /// both, or the edge of the grid is the edge of the world.</summary>
        void LayGround(bool hostGround)
        {
            for (int cx = 0; cx < _w; cx++)
                for (int cz = 0; cz < _h; cz++)
                {
                    float mx = cx * Cell, mz = cz * Cell;
                    switch (_kind[cx, cz])
                    {
                        case CellKind.Road:
                            // (SM_Env_Road_Patch_01 is not a road tile - it is an atlas-textured
                            // ground patch the demo drops on lawns; on asphalt it reads as a green square)
                            TownKit.Tile(_road, mx, mz, 0, _streetRoot);
                            break;
                        case CellKind.Zebra:
                            TownKit.Tile(_zebra, mx, mz, _zebraYaw[cx, cz], _streetRoot);
                            break;
                        case CellKind.Cap:
                            LayCap(cx, cz);
                            break;
                        case CellKind.Wrap:
                            break; // under an L wrap laid with its cap
                        case CellKind.Sidewalk:
                            LaySidewalk(cx, cz);
                            break;
                        default:
                            LaySurface(cx, cz);
                            break;
                    }
                }

            if (hostGround) return;

            // a skirt of lawn a cell wide past the outer pavements, and a green ground
            // plane under everything so the edge of the grid is not the edge of the world
            for (int cx = -2; cx <= _w + 1; cx++)
                for (int cz = -2; cz <= _h + 1; cz++)
                    if (cx < 0 || cz < 0 || cx >= _w || cz >= _h)
                        TownKit.Tile(_grass, cx * Cell, cz * Cell, 0, _groundRoot);
            // the plane sits below the pavement tiles' gutter (they dip to -0.23 m at the kerb),
            // or it shows green in every gutter
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "Ground";
            Object.Destroy(plane.GetComponent<Collider>());
            plane.transform.SetParent(_groundRoot, false);
            plane.transform.position = new Vector3(MapWidth * 0.5f, -0.27f, MapHeight * 0.5f);
            plane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = new Vector3(MapWidth + 600f, MapHeight + 600f, 1f);
            var mr = plane.GetComponent<MeshRenderer>();
            mr.sharedMaterial = TownKit.Flat("Suburb Ground", new Color(0.33f, 0.47f, 0.2f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LayCap(int cx, int cz)
        {
            // the cap's round corner is opposite its two road sides
            Vector3 a = Vector3.zero, b = Vector3.zero;
            int n = 0;
            foreach (var (dir, dx, dz) in Dirs)
                if (IsRoadish(cx + dx, cz + dz)) { if (n == 0) a = dir; else b = dir; n++; }
            int yaw = n >= 2 ? YawForTwoSides(a, b) : 0;
            TownKit.Tile(_roadCap, cx * Cell, cz * Cell, yaw, _streetRoot);
            TownKit.Tile(_swCapWrap, cx * Cell, cz * Cell, yaw, _streetRoot);
        }

        void LaySidewalk(int cx, int cz)
        {
            var roads = new List<Vector3>();
            var pavs = new List<Vector3>();
            foreach (var (dir, dx, dz) in Dirs)
            {
                if (IsRoadish(cx + dx, cz + dz)) roads.Add(dir);
                else if (IsPavement(cx + dx, cz + dz)) pavs.Add(dir);
            }
            float mx = cx * Cell, mz = cz * Cell;
            GameObject piece;
            int yaw;
            if (roads.Count == 1 || roads.Count == 3)
            {
                var kerb = roads.Count == 1 ? roads[0] : MiddleOf(roads);
                yaw = TownKit.YawFacing(kerb);
                piece = _swVariant[cx, cz] switch
                {
                    SwVariant.Crossing => Chance(0.1f) ? _swCrossing : _sidewalk,
                    SwVariant.Driveway => _swDriveway,
                    SwVariant.Path => _swPath,
                    _ => Chance(0.12f) && _swAlts.Count > 0 ? Pick(_swAlts) : _sidewalk,
                };
            }
            else if (roads.Count == 2 && Vector3.Dot(roads[0], roads[1]) > -0.5f)
            {
                piece = _swCorner;
                yaw = YawForTwoSides(roads[0], roads[1]);
            }
            else if (roads.Count == 2)
            {
                piece = _sidewalk; // between two roads: a kerb either way, take the first
                yaw = TownKit.YawFacing(roads[0]);
            }
            else if (pavs.Count == 2 && Vector3.Dot(pavs[0], pavs[1]) > -0.5f)
            {
                piece = _swEndCorner;
                yaw = YawForTwoSides(pavs[0], pavs[1]);
            }
            else
            {
                piece = _sidewalk;
                yaw = pavs.Count > 0 ? TownKit.YawFacing(-pavs[0]) : 0;
            }
            TownKit.Tile(piece ?? _sidewalk, mx, mz, yaw, _streetRoot);
        }

        static Vector3 MiddleOf(List<Vector3> dirs)
        {
            // of three roads the middle one is the one not opposite any other
            foreach (var d in dirs)
            {
                bool opposed = false;
                foreach (var o in dirs) if (Vector3.Dot(d, o) < -0.5f) opposed = true;
                if (!opposed) return d;
            }
            return dirs[0];
        }

        void LaySurface(int cx, int cz)
        {
            float mx = cx * Cell, mz = cz * Cell;
            int yaw = _surfYaw[cx, cz];
            switch (_surface[cx, cz])
            {
                case Surface.Grass: TownKit.Tile(_grass, mx, mz, 0, _groundRoot); break;   // laid straight: one calm lawn, no patchwork
                case Surface.Grass2: TownKit.Tile(_grass2 ?? _grass, mx, mz, 0, _groundRoot); break;
                case Surface.Driveway: TownKit.Tile(_driveway, mx, mz, yaw, _lotRoot); break;
                case Surface.Path: TownKit.Tile(_path, mx, mz, yaw, _lotRoot); break;
                case Surface.PathDoor: TownKit.Tile(_pathDoor ?? _path, mx, mz, yaw, _lotRoot); break;
                case Surface.Concrete: TownKit.Tile(_concrete, mx, mz, 0, _lotRoot); break;
                case Surface.Asphalt: TownKit.Tile(_road, mx, mz, 0, _lotRoot); break;
                case Surface.Parking: TownKit.Tile(_road, mx, mz, 0, _lotRoot); break; // the bay markings are laid by the place
                case Surface.Dirt: TownKit.Tile(_dirt ?? _grass, mx, mz, 0, _lotRoot); break;
                case Surface.None: break;
            }
        }

        // ------------------------------------------------------------ furniture

        void BuildStreetFurniture()
        {
            // The Town demo's pavements, measured: a lamp every ~10 m stood at the back of
            // the pavement by the lot line, between the lamps a tree at the kerb - most in
            // a square planter, some a plain tree on a dirt ring - bushes and flower patches
            // along the back edge, a bin by the corners. Both sides of every street.
            foreach (var s in _segments)
            {
                float lo = s.Lo, hi = s.Hi;
                if (s.Stub) { if (s.DeadHi) hi = s.BulbLo; else lo = s.BulbHi; }
                if (hi - lo < 12f) continue;
                var lampPrefab = Chance(0.7f) ? _streetlamp : (_streetlamp2 ?? _streetlamp);
                foreach (float side in new[] { -1f, 1f })
                {
                    float kerb = side * StreetHalf;                 // across of the kerb line
                    float back = side * (StreetHalf + Walk);        // across of the lot line
                    var toRoad = s.Vertical ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);
                    int lampYaw = TownKit.YawFacing(toRoad);
                    float phase = Rnd(0f, 5f);
                    for (float a = lo + 3.5f + phase; a < hi - 3f; a += 10f)
                    {
                        var lampAt = AW(s, a, back - side * 1.0f);
                        if (PavementFree(lampAt) && lampPrefab != null) TownKit.Prop(lampPrefab, lampAt + Vector3.up * WalkY, lampYaw, _streetRoot);
                        float ta = a + 5f;
                        if (ta < hi - 3f && Chance(0.85f * treeDensity))
                        {
                            // the Town pavement tile, kerb to lot line: gutter and kerb 1.1 m,
                            // concrete path 2 m, a grass strip 1.2 m, a concrete edge 0.6 m.
                            // A tree in a planter box stands on the path by the kerb; a plain
                            // tree with its dirt ring, and every bush, in the grass strip
                            bool framed = _framedTrees.Count > 0 && Chance(0.65f);
                            var treeAt = framed ? AW(s, ta, kerb + side * 1.4f) : AW(s, ta, back - side * 1.3f);
                            if (PavementFree(treeAt))
                            {
                                if (framed) TownKit.Prop(Pick(_framedTrees), treeAt + Vector3.up * WalkY, Rnd(4) * 90, _floraRoot);
                                else if (_yardTrees.Count > 0) PlaceTree(Pick(_yardTrees), treeAt + Vector3.up * 0.02f, _floraRoot);
                            }
                        }
                    }
                    // greenery in the pavement's grass strip (never on its concrete edge)
                    for (float a = lo + 2f; a < hi - 2f; a += Rnd(5f, 9f))
                    {
                        if (!Chance(0.55f)) continue;
                        var at = AW(s, a, back - side * 1.3f);
                        if (!PavementFree(at)) continue;
                        float r = Rnd();
                        if (r < 0.4f && _flowers.Count > 0) TownKit.Prop(Pick(_flowers), at, Rnd(360), _floraRoot);
                        else if (r < 0.7f && _bushes.Count > 0) TownKit.Prop(Pick(_bushes), at, Rnd(360), _floraRoot);
                        else if (_tufts.Count > 0) TownKit.Prop(Pick(_tufts), at, Rnd(360), _floraRoot);
                    }
                    // a bin by a corner
                    if (_bins.Count > 0 && Chance(0.35f)) TownKit.Prop(Pick(_bins), AW(s, Chance(0.5f) ? lo + 2.5f : hi - 2.5f, kerb + side * 1.6f) + Vector3.up * WalkY, Rnd(360), _streetRoot);
                }
                if (s.Stub && _speedBump != null)
                {
                    float a = s.DeadHi ? s.Lo + 10f : s.Hi - 10f;
                    foreach (float across in new[] { -2.5f, 2.5f })
                    {
                        var pos = s.Vertical ? new Vector3(s.Axis + across, 0.02f, a) : new Vector3(a, 0.02f, s.Axis + across);
                        TownKit.Prop(_speedBump, pos, s.Vertical ? 0 : 90, _streetRoot);
                    }
                }
            }

            // junctions: a stop sign on the right of every approach, a street-name pole
            // on one corner, a manhole in the middle of some, a drain at a kerb
            foreach (var n in _nodeList)
            {
                float k = StreetHalf + 1.6f;   // a metre and a half in from the kerb corner
                if (n.S) PlaceSign(_stopSign, new Vector3(n.X + k, WalkY, n.Z - k), Vector3.back);
                if (n.N) PlaceSign(_stopSign, new Vector3(n.X - k, WalkY, n.Z + k), Vector3.forward);
                if (n.W) PlaceSign(_stopSign, new Vector3(n.X - k, WalkY, n.Z - k), Vector3.left);
                if (n.E) PlaceSign(_stopSign, new Vector3(n.X + k, WalkY, n.Z + k), Vector3.right);
                if (n.Arms >= 3 && _signPole != null)
                {
                    var p = new Vector3(n.X + k + 1.4f, WalkY, n.Z + k + 1.4f);
                    TownKit.Prop(_signPole, p, Rnd(4) * 90, _streetRoot);
                }
                if (Chance(0.5f) && _manhole != null)
                    TownKit.Prop(_manhole, new Vector3(n.X + Rnd(-2f, 2f), 0.03f, n.Z + Rnd(-2f, 2f)), Rnd(4) * 90, _streetRoot);
                if (Chance(0.4f) && _drain != null)
                    TownKit.Prop(_drain, new Vector3(n.X + StreetHalf - 0.4f, 0.03f, n.Z - StreetHalf - 2.5f), 0, _streetRoot);
            }
        }

        /// <summary>A plain pavement cell - not a driveway cut, not a path, not a zebra's
        /// flank - so nothing is stood in a resident's way in.</summary>
        bool PavementFree(Vector3 at)
        {
            int cx = CellOf(at.x), cz = CellOf(at.z);
            if (!InGrid(cx, cz)) return false;
            if (_kind[cx, cz] != CellKind.Sidewalk) return false;
            return _swVariant[cx, cz] == SwVariant.Plain;
        }

        // A sign stood at the kerb, its face to the traffic coming along `from`
        // (the pack's signs read on their local +Z).
        void PlaceSign(GameObject sign, Vector3 pos, Vector3 trafficFrom)
        {
            if (sign == null) return;
            TownKit.Prop(sign, pos, TownKit.YawFacing(trafficFrom), _streetRoot);
        }

        // ------------------------------------------------------------ leftovers

        // Free cells no lot took: lawn with a tree or a bush here and there - never a
        // bare slab.
        void BuildLeftovers()
        {
            for (int cx = 0; cx < _w; cx++)
                for (int cz = 0; cz < _h; cz++)
                {
                    if (_kind[cx, cz] != CellKind.Free) continue;
                    float r = Rnd() / Mathf.Max(0.2f, treeDensity);
                    var centre = new Vector3((cx + 0.5f) * Cell + Rnd(-1.5f, 1.5f), 0f, (cz + 0.5f) * Cell + Rnd(-1.5f, 1.5f));
                    if (r < 0.42f && _tallTrees.Count > 0) PlaceTree(Pick(_tallTrees), centre, _floraRoot);
                    else if (r < 0.62f && _yardTrees.Count > 0) PlaceTree(Pick(_yardTrees), centre, _floraRoot);
                    else if (r < 0.75f && _bushes.Count > 0) TownKit.Prop(Pick(_bushes), centre, Rnd(360), _floraRoot);
                    else if (r < 0.88f && _tufts.Count > 0) TownKit.Prop(Pick(_tufts), centre, Rnd(360), _floraRoot);
                }

            // a tree line just outside the outer pavements, the way a street at the
            // edge of a suburb looks out on the trees it was cut from - not a backdrop,
            // a verge: one every several metres, none further out than the grass skirt
            if (_tallTrees.Count > 0)
            {
                float step = 9f / Mathf.Max(0.2f, treeDensity);
                for (float x = 2f; x < MapWidth - 2f; x += Rnd(step * 0.7f, step * 1.3f))
                {
                    if (Chance(0.85f)) PlaceTree(Pick(_tallTrees), new Vector3(x, 0f, -Cell * 0.5f + Rnd(-1.5f, 1.5f)), _floraRoot);
                    if (Chance(0.85f)) PlaceTree(Pick(_tallTrees), new Vector3(x + Rnd(-2f, 2f), 0f, MapHeight + Cell * 0.5f + Rnd(-1.5f, 1.5f)), _floraRoot);
                }
                for (float z = 2f; z < MapHeight - 2f; z += Rnd(step * 0.7f, step * 1.3f))
                {
                    if (Chance(0.85f)) PlaceTree(Pick(_tallTrees), new Vector3(-Cell * 0.5f + Rnd(-1.5f, 1.5f), 0f, z), _floraRoot);
                    if (Chance(0.85f)) PlaceTree(Pick(_tallTrees), new Vector3(MapWidth + Cell * 0.5f + Rnd(-1.5f, 1.5f), 0f, z + Rnd(-2f, 2f)), _floraRoot);
                }
            }
        }

        void PlaceTree(GameObject tree, Vector3 at, Transform parent)
        {
            TownKit.Prop(tree, at, Rnd(360), parent);
            if (_treeBase != null && Chance(0.6f)) TownKit.Prop(_treeBase, at + new Vector3(0f, 0.02f, 0f), Rnd(360), parent);
        }
    }
}
