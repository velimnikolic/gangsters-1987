using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // Where the city's street arrives, and the little lane graph that lets it go on:
    // the field's own frontage road, end to end, with a junction at the approach, at
    // each of the two service gates and at the two dead ends. The field's own cars
    // run their routes on the kerb loop as they always did; a patrol car - or anyone
    // else out of the city - can now drive in off the belt, along the frontage road
    // past the terminal spur and the gates, and turn round at the end.
    //
    // Everything here runs AFTER MoveIntoPlace, so it is built in world coordinates.
    public partial class AirportDistrict
    {
        void BuildPortals()
        {
            _portals.Clear();
            _roads.Clear();
            if (_links == null) return;   // its own scene: nowhere to go

            float cz = AirportSpec.StreetZ;
            float half = StreetKit.StreetHalf;            // the street kit's own half width
            float x0 = AirportSpec.StreetX0 + half, x1 = AirportSpec.StreetX1 - half;
            var stops = new List<float> { x0, AirportSpec.GaGateX, AirportSpec.ApproachX, AirportSpec.CargoGateX, x1 };
            stops.Sort();

            var nodes = new RoadNode[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                var c = W(new Vector3(stops[i], 0f, cz));
                var n = new RoadNode { I = -4, J = -i - 1, X = c.x, Z = c.z };
                var box = _inner.ToWorldDir(new Vector3(half, 0f, half));
                n.XMin = n.X - Mathf.Abs(box.x); n.XMax = n.X + Mathf.Abs(box.x);
                n.ZMin = n.Z - Mathf.Abs(box.z); n.ZMax = n.Z + Mathf.Abs(box.z);
                nodes[i] = n;
            }

            // two lanes the length of the road, keeping right: eastbound south of the
            // centre line (the field's side), westbound north of it
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                float a = stops[i] + half, b = stops[i + 1] - half;
                if (b - a < 5f) continue;
                AddRoad(nodes[i], nodes[i + 1], new Vector3(a, 0f, cz - 2.5f), new Vector3(b, 0f, cz - 2.5f));
                AddRoad(nodes[i + 1], nodes[i], new Vector3(b, 0f, cz + 2.5f), new Vector3(a, 0f, cz + 2.5f));
            }

            // the approach: where the city's street comes down to the terminal road
            int at = stops.IndexOf(AirportSpec.ApproachX);
            if (at >= 0)
                _portals.Add(new DistrictPortal
                {
                    Local = ToContract(new Vector3(AirportSpec.ApproachX, 0f, cz + half)),
                    LocalDir = Vector3.forward,
                    Node = nodes[at],
                    Tag = "airport approach",
                });
            Debug.Log($"[Airport] {_portals.Count} approach to the city, {_roads.Count} lanes along the frontage road");
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

        /// <summary>The buildings, the wire and the parked cars as ground a walker off
        /// the graph may not stride through - measured in the world, once the field
        /// stands in place.</summary>
        void BlockTheField(IDistrictHost host)
        {
            int n = 0;
            foreach (var root in new[] { _buildingRoot, _fenceRoot })
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
            foreach (var go in _parkedBodies)
            {
                if (go == null) continue;
                host.Blocked(AirportKit.BoundsOf(go));
                n++;
            }
            if (n > 0) Debug.Log($"[Airport] {n} buildings, fences and parked cars walkers go round");
        }
    }
}
