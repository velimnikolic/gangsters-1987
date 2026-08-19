using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The city hosting its quarters. The grid is not the whole town: a port takes one
    // shore, two or three suburbs take others, and each is built by the SAME object
    // that builds it in its own demo scene (IDistrict) - only the host and the frame
    // differ, so what is changed in the demo is what the city gets.
    //
    // What happens here, in order:
    //   PlanDistricts, straight after Respace: roll the slots (or take the ones on the
    //     inspector), work out each district's frame from the road lines it is pinned
    //     to, let it plan its layout, take its ground reservations for the island, and
    //     open the outward arms of the junctions its streets will meet.
    //   BuildDistricts, after the city's own graphs: build each quarter, lay the
    //     connecting street across the wild strip, and weld the two lane and pavement
    //     graphs so a patrol car can drive out to the suburb and a walker can walk in.
    public partial class RoadDemoBuilder : IDistrictHost
    {
        [Header("Districts (the port, the suburbs)")]
        [Tooltip("Same seed, same town: which shore the port takes, how many suburbs " +
                 "there are and where they sit is rolled from this.")]
        public int cityLayoutSeed = 7;
        [Tooltip("Roll the districts from the seed and write them into the list below. " +
                 "Off keeps the list as it stands - a town frozen, or edited by hand.")]
        public bool rollDistricts = true;
        [Tooltip("A port on one shore. Only ever one.")]
        public bool harborDistrict = true;
        [Tooltip("How many suburbs the roll may hang off the grid.")]
        [Range(0, 4)] public int suburbsMin = 2;
        [Range(0, 4)] public int suburbsMax = 3;
        public DistrictSlot[] districts = new DistrictSlot[0];

        [Header("Frame budget")]
        [Tooltip("How far the traffic may be scaled up for a big city (x the inspector's " +
                 "carCount). 4 is the whole town alive; 1.5 is the same town with a third " +
                 "of the cars, and tells you at once whether the frame is the traffic.")]
        [Range(0.5f, 6f)] public float maxCarScale = 4f;
        [Tooltip("The same for the crowd (x pedestrianCount).")]
        [Range(0.5f, 6f)] public float maxCrowdScale = 3f;
        [Tooltip("Scale the traffic and the crowd to how many blocks the city has. " +
                 "Off takes the counts on the inspector as they stand, which is what " +
                 "a scene of one block wants.")]
        public bool scaleLifeToCity = true;
        [Tooltip("Log every few seconds where the frame goes: the traffic, the crowd, the " +
                 "police, the districts. The perf probe only says \"scripts: 140 ms\", which " +
                 "never tells you what to fix.")]
        public bool updateProfile = true;

        readonly List<IDistrict> _built = new List<IDistrict>();
        readonly List<DistrictSlot> _builtSlots = new List<DistrictSlot>();
        readonly List<Transform> _districtLive = new List<Transform>();
        Transform _districtRoot;
        StreetKit _connectorKit;

        /// <summary>Outward arms: a junction on the edge of the grid whose street runs on
        /// out of the city to a district. Keyed (i, j, side) with side 0 N, 1 E, 2 S, 3 W.</summary>
        readonly HashSet<int> _outwardArms = new HashSet<int>();

        static int ArmKey(int i, int j, int side) => ((i * 64 + j) * 4) + side;

        /// <summary>Whether a street runs out of the grid here, to a district.</summary>
        bool OutwardArm(int i, int j, int side) => _outwardArms.Contains(ArmKey(i, j, side));

        Transform DistrictRoot => _districtRoot != null
            ? _districtRoot
            : (_districtRoot = new GameObject("Districts").transform);

        // ------------------------------------------------------------------ plan

        void PlanDistricts()
        {
            if (rollDistricts)
            {
                var grid = new CityLayout.Grid
                {
                    Vx = verticalRoadX,
                    VBoulevard = verticalIsBoulevard,
                    Hz = horizontalRoadZ,
                    HBoulevard = horizontalIsBoulevard,
                    Blocked = LineCarriesSeam,
                };
                var rolled = CityLayout.Roll(grid, cityLayoutSeed, suburbsMin, suburbsMax, harborDistrict);
                districts = rolled.ToArray();
            }
            if (districts == null || districts.Length == 0) return;

            foreach (var slot in districts)
            {
                if (slot == null) continue;
                var district = MakeDistrict(slot);
                if (district == null) continue;

                if (!FrameFor(slot, out var frame, out var links))
                {
                    Debug.LogWarning($"[RoadDemo] district skipped, its lines do not fit the grid: {slot}");
                    continue;
                }
                district.Frame = frame;
                district.Plan(links, slot.seed);

                var world = frame.ToWorldRect(district.LocalBounds);
                _landRects.Add(world);
                district.Reserve(_reservations);

                foreach (int line in slot.pinLines) OpenArm(slot.edge, line);

                _built.Add(district);
                _builtSlots.Add(slot);
                Debug.Log($"[RoadDemo] district planned: {slot} -> {district.Name} at {frame}, " +
                          $"{world.width:F0} x {world.height:F0} m");
            }
        }

        /// <summary>A road line a seam runs out along - the river's mouth - which no
        /// district may be pinned to.</summary>
        bool LineCarriesSeam(bool vertical, int line)
        {
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (s == null) continue;
                // a vertical seam - the park, the wild strip, a river running north to
                // south - lies in the gap between two VERTICAL lines and leaves the grid
                // at both ends; a district pinned to either line would have its approach
                // road down the seam's own edge, and a river's mouth would cut it in two
                if (s.vertical != vertical) continue;
                if (line == s.gap || line == s.gap + 1) return true;
            }
            return false;
        }

        IDistrict MakeDistrict(DistrictSlot slot)
        {
            switch (slot.kind)
            {
                case DistrictKind.Pad: return new PadDistrict();
                case DistrictKind.Suburb: return SuburbDemo.SuburbDistrict.ForCity(slot);
                case DistrictKind.Harbor: return HarborDemo.HarborDistrict.ForCity(slot);
                default: return null;
            }
        }

        // ----------------------------------------------------------- the frame

        /// <summary>Where the district stands and where its streets must meet the city.
        /// A district is laid out with the city at its local +Z and its own body at
        /// local z below zero; the links are the local X the connecting streets land on,
        /// the first of them at zero.</summary>
        bool FrameFor(DistrictSlot slot, out DistrictFrame frame, out float[] links)
        {
            frame = DistrictFrame.Identity;
            links = null;
            if (slot.pinLines == null || slot.pinLines.Length == 0) return false;

            bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
            var axis = vertical ? verticalRoadX : horizontalRoadZ;
            foreach (int line in slot.pinLines)
                if (line < 0 || line >= axis.Length) return false;

            float gx0 = verticalRoadX[0] - VHalf(0) - Sidewalk;
            float gx1 = verticalRoadX[verticalRoadX.Length - 1] + VHalf(verticalRoadX.Length - 1) + Sidewalk;
            float gz0 = horizontalRoadZ[0] - HHalf(0) - Sidewalk;
            float gz1 = horizontalRoadZ[horizontalRoadZ.Length - 1] + HHalf(horizontalRoadZ.Length - 1) + Sidewalk;

            int yaw;
            var pins = new Vector3[slot.pinLines.Length];
            switch (slot.edge)
            {
                case CityEdge.South:
                    yaw = 0;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(axis[slot.pinLines[k]], 0f, gz0 - slot.strip);
                    break;
                case CityEdge.North:
                    yaw = 180;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(axis[slot.pinLines[k]], 0f, gz1 + slot.strip);
                    break;
                case CityEdge.West:
                    yaw = 90;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(gx0 - slot.strip, 0f, axis[slot.pinLines[k]]);
                    break;
                default:
                    yaw = 270;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(gx1 + slot.strip, 0f, axis[slot.pinLines[k]]);
                    break;
            }

            // the district's origin is the westmost (in its own frame) of its pins, so
            // its links run from zero upward whichever shore it is on
            var probe = new DistrictFrame { origin = pins[0], yaw = yaw };
            float min = float.MaxValue;
            var local = new float[pins.Length];
            for (int k = 0; k < pins.Length; k++)
            {
                local[k] = probe.ToLocal(pins[k]).x;
                min = Mathf.Min(min, local[k]);
            }
            frame = new DistrictFrame { origin = probe.ToWorld(new Vector3(min, 0f, 0f)), yaw = yaw };
            links = new float[pins.Length];
            for (int k = 0; k < pins.Length; k++) links[k] = local[k] - min;
            System.Array.Sort(links);
            return true;
        }

        void OpenArm(CityEdge edge, int line)
        {
            int lastV = verticalRoadX.Length - 1, lastH = horizontalRoadZ.Length - 1;
            switch (edge)
            {
                case CityEdge.South: _outwardArms.Add(ArmKey(line, 0, 2)); break;
                case CityEdge.North: _outwardArms.Add(ArmKey(line, lastH, 0)); break;
                case CityEdge.West: _outwardArms.Add(ArmKey(0, line, 3)); break;
                default: _outwardArms.Add(ArmKey(lastV, line, 1)); break;
            }
        }

        // ----------------------------------------------------------------- build

        void BuildDistricts()
        {
            if (_built.Count == 0) return;
            EnsureLife();
            for (int k = 0; k < _built.Count; k++)
            {
                var district = _built[k];
                var slot = _builtSlots[k];
                district.Build(this);
                ConnectDistrict(district, slot);
            }
        }

        /// <summary>The street across the wild strip, and the weld: the district's lane
        /// graph and pavement joined to the city's at the junction its pin line ends on.</summary>
        void ConnectDistrict(IDistrict district, DistrictSlot slot)
        {
            var portals = district.Portals;
            if (portals == null || portals.Count == 0)
            {
                Debug.LogWarning($"[RoadDemo] {district.Name} offered no portal: it stands cut off from the city.");
                return;
            }
            if (_connectorKit == null)
            {
                // its own root, so the connecting streets merge and distance-cull with
                // the rest of the city rather than staying loose renderers
                var root = ((IDistrictHost)this).StaticRoot("District Roads");
                _connectorKit = new StreetKit(root) { Palms = false };
            }

            bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
            var frame = district.Frame;

            for (int p = 0; p < portals.Count; p++)
            {
                var portal = portals[p];
                var world = frame.ToWorld(portal.Local);
                var cityNode = NearestEdgeNode(slot, world, vertical, out var faceWorld);
                if (cityNode == null) continue;

                // the carriageway: along the axis the pin line runs on, from the
                // district's boundary to the junction's face
                if (vertical)
                {
                    float cx = cityNode.X;
                    float z0 = Mathf.Min(world.z, faceWorld.z), z1 = Mathf.Max(world.z, faceWorld.z);
                    if (z1 - z0 > 1f) _connectorKit.LayAlongZ(cx, z0, z1);
                }
                else
                {
                    float cz = cityNode.Z;
                    float x0 = Mathf.Min(world.x, faceWorld.x), x1 = Mathf.Max(world.x, faceWorld.x);
                    if (x1 - x0 > 1f) _connectorKit.LayAlongX(cz, x0, x1);
                }

                ReserveCorridor(faceWorld, world);
                WeldRoads(cityNode, portal, faceWorld, world, frame);
                WeldPavement(cityNode, portal, slot);

                // the first road out to each quarter gets a filling station halfway,
                // now that halfway is a real drive through the wild
                if (p == 0) WaysideStation(faceWorld, world, slot);
            }
        }

        /// <summary>The ground the connecting street stands on: flat at the city's own
        /// level and clear of the wild, from the junction out to the district. Without
        /// this the island's hills would run straight through the road.</summary>
        void ReserveCorridor(Vector3 from, Vector3 to)
        {
            const float Half = StreetKit.OuterHalf + 4f;    // carriageway, pavements and a shoulder
            var rect = Rect.MinMaxRect(Mathf.Min(from.x, to.x) - Half, Mathf.Min(from.z, to.z) - Half,
                                       Mathf.Max(from.x, to.x) + Half, Mathf.Max(from.z, to.z) + Half);
            _reservations.Level(rect, 0f);
            _reservations.NoFlora(rect);
        }

        /// <summary>The junction on the grid's edge this portal belongs to, and the
        /// world point of its outward face.</summary>
        RoadNode NearestEdgeNode(DistrictSlot slot, Vector3 portalWorld, bool vertical, out Vector3 face)
        {
            face = Vector3.zero;
            int lastV = verticalRoadX.Length - 1, lastH = horizontalRoadZ.Length - 1;
            RoadNode best = null;
            float bestD = float.MaxValue;
            foreach (int line in slot.pinLines)
            {
                RoadNode n;
                Vector3 f;
                switch (slot.edge)
                {
                    case CityEdge.South:
                        if (line > lastV) continue;
                        n = _nodes[line, 0]; f = new Vector3(n.X, 0f, n.ZMin); break;
                    case CityEdge.North:
                        if (line > lastV) continue;
                        n = _nodes[line, lastH]; f = new Vector3(n.X, 0f, n.ZMax); break;
                    case CityEdge.West:
                        if (line > lastH) continue;
                        n = _nodes[0, line]; f = new Vector3(n.XMin, 0f, n.Z); break;
                    default:
                        if (line > lastH) continue;
                        n = _nodes[lastV, line]; f = new Vector3(n.XMax, 0f, n.Z); break;
                }
                float d = (new Vector2(f.x, f.z) - new Vector2(portalWorld.x, portalWorld.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; face = f; }
            }
            return best;
        }

        /// <summary>Two lanes between the city's junction and the district's, keeping
        /// right: the outbound lane on the near side, the inbound on the far.</summary>
        void WeldRoads(RoadNode cityNode, DistrictPortal portal, Vector3 faceWorld, Vector3 portalWorld,
                       DistrictFrame frame)
        {
            if (portal.Node == null) return;
            // local frame of the connector: +Z from the district toward the city
            var toCity = frame.ToWorldDir(Vector3.forward);
            var right = frame.ToWorldDir(Vector3.right);
            const float Lane = 2.5f;
            bool ns = Mathf.Abs(toCity.z) > 0.5f;

            // one carriageway, city junction to the district's, a lane each way: the
            // outbound (city -> district, against +Z) keeps right at local -X, the
            // inbound at +X - which is what the network lays for a road from the face
            // to the portal. The two junctions' connectors are laid again over it.
            if (Net == null) return;
            Net.AddRoad(faceWorld, portalWorld, StreetHalf, new[] { Lane }, streetSpeed, cityNode, portal.Node, ns);
            Net.Rebuild(cityNode);
            Net.Rebuild(portal.Node);
            _edges.Clear();
            _edges.AddRange(Net.Edges);
        }

        /// <summary>The pavement across the strip: each of the district's two kerb
        /// corners to the nearer of the city junction's own.</summary>
        void WeldPavement(RoadNode cityNode, DistrictPortal portal, DistrictSlot slot)
        {
            if (_corners == null) return;
            const int NE = 0, NW = 1, SW = 2, SE = 3;
            int a, b;
            switch (slot.edge)
            {
                case CityEdge.South: a = SW; b = SE; break;
                case CityEdge.North: a = NW; b = NE; break;
                case CityEdge.West: a = NW; b = SW; break;
                default: a = NE; b = SE; break;
            }
            var cityA = _corners[cityNode.I, cityNode.J, a];
            var cityB = _corners[cityNode.I, cityNode.J, b];
            LinkWalk(portal.WalkLeft, cityA, cityB);
            LinkWalk(portal.WalkRight, cityA, cityB);
        }

        void LinkWalk(PedNode from, PedNode a, PedNode b)
        {
            if (from == null) return;
            var to = a == null ? b : b == null ? a
                : (from.Pos - a.Pos).sqrMagnitude <= (from.Pos - b.Pos).sqrMagnitude ? a : b;
            if (to == null) return;
            AddPedLink(from, to, false, false, null);
        }

        // ------------------------------------------------------ IDistrictHost

        Transform IDistrictHost.StaticRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(DistrictRoot, false);
            _districtStatic.Add(t);
            return t;
        }

        Transform IDistrictHost.LiveRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(DistrictRoot, false);
            _districtLive.Add(t);
            return t;
        }

        bool IDistrictHost.ProvidesGround => true;

        PedClips _hostClips;
        bool _hostClipsMade;

        PedClips IDistrictHost.Clips
        {
            get
            {
                if (!_hostClipsMade) { _hostClipsMade = true; _hostClips = CrewKit.Clips(); }
                return _hostClips;
            }
        }

        CityLife IDistrictHost.Life
        {
            get { EnsureLife(); return _life; }
        }

        void IDistrictHost.RegisterVehicle(DemoVehicle vehicle)
        {
            if (vehicle == null) return;
            _vehicles.Add(vehicle);
            StreetTraffic.Users.Add(vehicle);
        }

        void IDistrictHost.RegisterCivilian(CivilianAgent civilian)
        {
            if (civilian != null) _pedestrians.Add(civilian);
        }

        void IDistrictHost.RegisterWalker(PedestrianAgent walker)
        {
            if (walker != null) _districtWalkers.Add(walker);
        }

        void IDistrictHost.RegisterSignal(TrafficSignal signal)
        {
            if (signal != null) _signals.Add(signal);
        }

        void IDistrictHost.RegisterRoads(IReadOnlyList<RoadEdge> edges)
        {
            if (edges == null) return;
            for (int i = 0; i < edges.Count; i++) if (edges[i] != null) _edges.Add(edges[i]);
        }

        void IDistrictHost.RegisterPavement(IReadOnlyList<PedLink> links)
        {
            if (links == null) return;
            for (int i = 0; i < links.Count; i++) if (links[i] != null) _pedLinks.Add(links[i]);
        }

        void IDistrictHost.Blocked(Bounds box) => WalkObstacles.Block(box);

        void IDistrictHost.ReportMissing(string what)
            => Debug.LogWarning($"[RoadDemo] a district wanted a prefab it did not get: {what}");

        readonly List<PedestrianAgent> _districtWalkers = new List<PedestrianAgent>();

        void TickDistricts(float dt)
        {
            for (int i = 0; i < _built.Count; i++) _built[i].Tick(dt);
            for (int i = 0; i < _districtWalkers.Count; i++) _districtWalkers[i].Tick(dt);
        }

        void DisposeDistricts()
        {
            for (int i = 0; i < _built.Count; i++) _built[i].Dispose();
        }
    }
}
