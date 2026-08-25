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

            var stops = new List<float> { x0 };
            stops.AddRange(gates);
            stops.Add(x1);

            var nodes = new RoadNode[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                var c = W(new Vector3(stops[i], 0f, cz));
                var n = new RoadNode { I = -2, J = -i - 1, X = c.x, Z = c.z };
                var box = _inner.ToWorldDir(new Vector3(half, 0f, half));
                n.XMin = n.X - Mathf.Abs(box.x); n.XMax = n.X + Mathf.Abs(box.x);
                n.ZMin = n.Z - Mathf.Abs(box.z); n.ZMax = n.Z + Mathf.Abs(box.z);
                nodes[i] = n;
            }

            // two lanes the length of the street, keeping right: eastbound south of the
            // centre line, westbound north of it
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                float a = stops[i] + half, b = stops[i + 1] - half;
                if (b - a < 5f) continue;
                AddRoad(nodes[i], nodes[i + 1], new Vector3(a, 0f, cz - 2.5f), new Vector3(b, 0f, cz - 2.5f));
                AddRoad(nodes[i + 1], nodes[i], new Vector3(b, 0f, cz + 2.5f), new Vector3(a, 0f, cz + 2.5f));
            }

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

        void AddRoad(RoadNode from, RoadNode to, Vector3 startOwn, Vector3 endOwn)
        {
            var start = W(startOwn);
            var end = W(endOwn);
            var dir = end - start;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            var e = new RoadEdge
            {
                From = from, To = to, Start = start, End = end, Dir = dir / len,
                Length = len, NorthSouth = Mathf.Abs(dir.z) > Mathf.Abs(dir.x), SpeedLimit = 11f,
            };
            from.Outgoing.Add(e);
            to.Incoming.Add(e);
            _roads.Add(e);
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
                    host.Blocked(b, t.name);
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
                n++;
            }
            if (n > 0) Debug.Log($"[Harbor] {n} sheds, works and fences walkers go round");
        }
    }
}
