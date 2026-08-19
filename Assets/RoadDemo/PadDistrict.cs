using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The plainest district there is: a paved pad with a road down its middle and a
    /// junction where the city's street arrives. It builds nothing anyone would want
    /// to look at - it exists so the host's half of the contract can be exercised on
    /// its own: the frame, the reservations, the island ringing it, the connecting
    /// street, the weld of the two lane graphs and the two pavements.
    ///
    /// Set a slot's kind to Pad to drop one on any shore.
    /// </summary>
    public sealed class PadDistrict : IDistrict
    {
        /// <summary>Metres of pad either side of the outermost links.</summary>
        public float flank = 40f;
        /// <summary>How deep the pad runs, away from the city.</summary>
        public float depth = 90f;

        DistrictFrame _frame;
        float[] _links = { 0f };
        Rect _bounds;
        StreetKit _kit;
        Transform _root;
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<PedLink> _walks = new List<PedLink>();

        public string Name => "Pad";

        public DistrictFrame Frame { get => _frame; set => _frame = value; }

        public Rect LocalBounds => _bounds;

        public IReadOnlyList<DistrictPortal> Portals => _portals;

        public void Plan(float[] links, int seed)
        {
            _links = links != null && links.Length > 0 ? links : new[] { 0f };
            float lo = _links[0] - flank, hi = _links[_links.Length - 1] + flank;
            // the contract: the city lies at local +Z, the district's body below zero
            _bounds = Rect.MinMaxRect(lo, -depth, hi, 0f);
        }

        public void Reserve(DistrictReservations into)
        {
            var world = _frame.ToWorldRect(_bounds);
            into.Pave(world);
            into.Level(Grow(world, 20f), 0f);
            into.NoFlora(Grow(world, 12f));
        }

        static Rect Grow(Rect r, float by)
            => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        public void Build(IDistrictHost host)
        {
            _root = host.StaticRoot("Pad");
            _kit = new StreetKit(_root) { Palms = false };

            // a street up to the boundary under every link, and a junction at the top
            for (int k = 0; k < _links.Length; k++)
            {
                float x = _links[k];
                LayLocalRoadAlongZ(x, _bounds.yMin + 10f, 0f);
                var node = new RoadNode
                {
                    I = -1, J = -k - 1,
                    X = _frame.ToWorld(new Vector3(x, 0f, -StreetKit.StreetHalf)).x,
                    Z = _frame.ToWorld(new Vector3(x, 0f, -StreetKit.StreetHalf)).z,
                };
                var centre = _frame.ToWorld(new Vector3(x, 0f, -StreetKit.StreetHalf));
                node.X = centre.x; node.Z = centre.z;
                node.XMin = node.X - StreetKit.StreetHalf; node.XMax = node.X + StreetKit.StreetHalf;
                node.ZMin = node.Z - StreetKit.StreetHalf; node.ZMax = node.Z + StreetKit.StreetHalf;

                _portals.Add(new DistrictPortal
                {
                    Local = new Vector3(x, 0f, 0f),
                    LocalDir = Vector3.forward,
                    Node = node,
                    Tag = "pad " + k,
                });
            }
            host.RegisterRoads(_edges);
            host.RegisterPavement(_walks);
        }

        void LayLocalRoadAlongZ(float localX, float z0, float z1)
        {
            if (_kit == null) return;
            var a = _frame.ToWorld(new Vector3(localX, 0f, z0));
            var b = _frame.ToWorld(new Vector3(localX, 0f, z1));
            if (Mathf.Abs(a.x - b.x) < 0.01f) _kit.LayAlongZ(a.x, Mathf.Min(a.z, b.z), Mathf.Max(a.z, b.z));
            else _kit.LayAlongX(a.z, Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x));
        }

        public void Tick(float dt) { }

        public void Dispose() { }
    }
}
