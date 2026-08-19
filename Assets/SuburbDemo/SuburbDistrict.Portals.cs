using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // Where the city's street arrives. A suburb is pinned to two or three of the
    // city's road lines; on each of them the junction at the far edge of the map
    // keeps its north arm (OpenGates), which lays the zebra out to the boundary, and
    // here the lane graph is carried the last few metres to the boundary itself,
    // where the host welds it to the city's.
    //
    // Everything in this file runs AFTER MoveIntoPlace: the graph is already in the
    // world, so the portal is built in world coordinates too.
    public partial class SuburbDistrict
    {
        void BuildPortals()
        {
            _portals.Clear();
            if (_links == null) return;   // its own scene: nowhere to go

            foreach (int p in _pinColumns)
            {
                if (p < 0 || p > _nx) continue;
                var node = _nodes[p, _nz];
                if (node?.Road == null)
                {
                    // the junction at the head of a pinned line went missing - a cul-de-sac
                    // took its south arm, or the plan never made it. Without it the suburb
                    // stands with no way in, so say so rather than quietly building an island.
                    Debug.LogError($"[Suburb] no junction at the head of pin column {p}: " +
                                   "this suburb has no street to the city.");
                    continue;
                }

                // the boundary point on this line, in the suburb's own coordinates
                var ownEdge = new Vector3(_vx[p], 0f, MapHeight);
                var worldEdge = W(ownEdge);
                var gate = new RoadNode
                {
                    I = -1, J = -1 - _portals.Count,
                    X = worldEdge.x, Z = worldEdge.z,
                };
                var half = _inner.ToWorldDir(new Vector3(StreetHalf, 0f, StreetHalf));
                gate.XMin = gate.X - Mathf.Abs(half.x); gate.XMax = gate.X + Mathf.Abs(half.x);
                gate.ZMin = gate.Z - Mathf.Abs(half.z); gate.ZMax = gate.Z + Mathf.Abs(half.z);

                // out of the suburb on the left-hand side of the axis as the map is
                // drawn (+2.5 east), back in on the other - right-hand traffic either way
                var face = new Vector3(_vx[p], 0f, _hz[_nz] + StreetHalf);
                AddEdge(node.Road, gate,
                        W(new Vector3(face.x + 2.5f, 0f, face.z)),
                        W(new Vector3(ownEdge.x + 2.5f, 0f, ownEdge.z)), true, false);
                AddEdge(gate, node.Road,
                        W(new Vector3(ownEdge.x - 2.5f, 0f, ownEdge.z)),
                        W(new Vector3(face.x - 2.5f, 0f, face.z)), true, false);

                _portals.Add(new DistrictPortal
                {
                    Local = ToContract(ownEdge),
                    LocalDir = Vector3.forward,
                    Node = gate,
                    WalkLeft = node.Corner[1],    // NW
                    WalkRight = node.Corner[0],   // NE
                    Tag = "suburb gate " + p,
                });
            }
            Debug.Log($"[Suburb] {_portals.Count} streets out to the city");
        }

        /// <summary>Every house, church and shop as ground a walker off the graph may
        /// not stride through.</summary>
        void BlockTheBuildings(IDistrictHost host)
        {
            int n = 0;
            foreach (var root in new[] { _lotRoot, _placeRoot })
            {
                if (root == null) continue;
                foreach (Transform t in root)
                {
                    if (!Pickable(t)) continue;
                    var b = new Bounds();
                    bool started = false;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        if (!started) { b = r.bounds; started = true; }
                        else b.Encapsulate(r.bounds);
                    }
                    if (!started) continue;
                    host.Blocked(b);
                    n++;
                }
            }
            if (n > 0) Debug.Log($"[Suburb] {n} buildings walkers go round");
        }
    }
}
