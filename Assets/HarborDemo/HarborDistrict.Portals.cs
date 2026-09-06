using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // Where the city's streets arrive, and the little lane graph that lets them go
    // on: the port's own back street, west end to east end, with a junction at each
    // gate. The lorries still run their own routes along it (they always did), but a
    // patrol car - or anyone else out of the city - can now drive the length of the
    // quay and turn round at the end.
    //
    // Everything here runs AFTER MoveIntoPlace, so it is built in world coordinates.
    public partial class HarborDistrict
    {
        void BuildPortals()
        {
            _portals.Clear();
            _roads.Clear();
            if (_links == null) return;   // its own scene: nowhere to go

            float cz = _streetZ;
            float half = 7.5f;                    // the street kit's own half width
            float x0 = -QuayHalf - 130f, x1 = QuayHalf + 130f;
            var gates = new List<float> { _gateWestX, _gateEastX };
            gates.Sort();

            // the gates, and the landward streets a neighbour runs out onto this road:
            // every one a junction the graph can turn at
            var stops = new List<float> { x0 };
            stops.AddRange(gates);
            foreach (float x in _standaloneBackStreetNorthLinks)
                if (!stops.Exists(present => Mathf.Abs(present - x) < 0.1f) && x > x0 + 20f && x < x1 - 20f) stops.Add(x);
            stops.Add(x1);
            stops.Sort();

            var net = HarborStreet.Build(_inner, stops, cz);
            var nodes = net.Nodes;
            _roads.AddRange(net.Edges);
            _landward.Clear();
            for (int k = 1; k + 1 < stops.Count; k++) _landward.Add((stops[k], nodes[k]));

            // the gates: where the city's streets come down to the quay
            for (int k = 0; k < gates.Count && k < _links.Length; k++)
            {
                int at = stops.IndexOf(gates[k]);
                if (at < 0) continue;
                _portals.Add(new DistrictPortal
                {
                    Local = ToContract(new Vector3(gates[k], 0f, cz + half)),
                    LocalDir = Vector3.forward,
                    Node = nodes[at],
                    Tag = "harbour gate " + k,
                });
            }
            Debug.Log($"[Harbor] {_portals.Count} gates to the city, {_roads.Count} lanes along the back street");
        }

        readonly List<(float OwnX, RoadNode Node)> _landward = new List<(float, RoadNode)>();

        /// <summary>The back street's junction under a landward street at this contract
        /// X - a gate or a link given to <see cref="SetStandaloneBackStreetNorthLinks"/> -
        /// and the landward kerb face of its box in contract coordinates.</summary>
        public bool TryLandwardJunction(float contractX, out RoadNode node, out Vector3 local)
        {
            node = null; local = default;
            float ownX = contractX - GateSpanCentre;
            foreach (var (x, at) in _landward)
            {
                if (Mathf.Abs(x - ownX) > 0.5f) continue;
                node = at;
                local = ToContract(new Vector3(x, 0f, _streetZ + StreetKit.StreetHalf));
                return true;
            }
            return false;
        }

        /// <summary>The sheds, the stacks and the fence as ground a walker off the graph
        /// may not stride through.</summary>
        void BlockTheYard(IDistrictHost host)
        {
            int n = 0;
            foreach (var root in new[] { _warehouseRoot, _fenceRoot })
            {
                if (root == null) continue;
                foreach (Transform t in root)
                {
                    var b = new Bounds();
                    bool started = false;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        if (!started) { b = r.bounds; started = true; }
                        else b.Encapsulate(r.bounds);
                    }
                    if (!started || b.size.y < 1.5f) continue;
                    if (root == _warehouseRoot)
                    {
                        host.Blocked(b, t.name);
                        MapGeometry.AddBuilding(b, t, t.name, TurfType.Warehouse);
                    }
                    else WalkObstacles.Block(b);
                    n++;
                }
            }
            // and the works that have a NAME of their own - the harbourmaster's, the ice
            // store, the bonded shed on offer. They stand under the berth and works
            // roots rather than in the shed line, so they are handed over by hand; a
            // building the map can put a card on is a building the player believes in.
            foreach (var (at, what) in _namedWorks)
            {
                if (at == null) continue;
                var b = new Bounds();
                bool started = false;
                foreach (var r in at.GetComponentsInChildren<Renderer>())
                {
                    if (!started) { b = r.bounds; started = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!started) continue;
                host.Blocked(b, what);
                MapGeometry.AddBuilding(b, at, what, TurfType.Warehouse);
                n++;
            }
            if (n > 0) Debug.Log($"[Harbor] {n} sheds, works and fences walkers go round");
        }
    }
}
