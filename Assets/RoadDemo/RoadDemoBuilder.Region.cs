using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public partial class RoadDemoBuilder
    {
        void BuildCoreRegion()
        {
            _coreRegion = CoreRegion.TryCreate(PrimaryCore, _primaryWorld, BuiltFromSeed, suburbsMax);
            if (_coreRegion == null) return;
            PortIndustryLayout.ReserveGround(_coreRegion.Quarters, _reservations);
            CityEdge harborSide = CityEdge.West;
            AirportDemo.AirportDistrict airport = null;
            var islandBounds = IslandLandform.BoundsFor(_coreRegion.WorldBounds);
            // The works' streets run out onto the port's back street: tell the port where
            // they arrive before it lays that street, so each gets a junction and a gap
            // in the pavement.
            var port = _coreRegion.Quarters.Find(q => q.District is HarborDemo.HarborDistrict)?.District as HarborDemo.HarborDistrict;
            var works = _coreRegion.Quarters.Find(q => q.District is IndustrialDistrict)?.District as IndustrialDistrict;
            if (port != null && works != null)
            {
                var arrivals = new List<float>();
                foreach (float crown in IndustrialDistrict.SeawardStreets(works.Raster, works.SeawardCut))
                    arrivals.Add(port.Frame.ToLocal(works.Frame.ToWorld(new Vector3(works.SeawardCut, 0f, crown))).x);
                port.SetStandaloneBackStreetNorthLinks(arrivals);
            }
            foreach (var q in _coreRegion.Quarters)
            {
                if (q.District is HarborDemo.HarborDistrict harbor) harbor.PlanSeaRoute(islandBounds, _reservations);
                // The port was placed against its PLANNED back street; the sheds it has
                // now measured put the real street a little nearer or farther. Slide the
                // works with it so their blocks keep a metre from the actual pavement.
                if (q.District == works && port != null && port.BackStreetMeasured && Mathf.Abs(port.BackStreetContractZ) < 20f)
                {
                    var frame = works.Frame;
                    frame.origin += port.Frame.ToWorldDir(new Vector3(0f, 0f, port.BackStreetContractZ));
                    works.Frame = frame;
                }
                _built.Add(q.District);
                _districtPlans.Add(new DistrictPlan(q.Slot.name, q.Slot.kind, q.World));
                _landRects.Add(q.World);
                q.District.Reserve(_reservations);
                if (q.District is AirportDemo.AirportDistrict field) airport = field;
                WalkObstacles.City.Add(q.World);
                if (q.Slot.kind == DistrictKind.Harbor) harborSide = q.Slot.edge;
                _districtGroup = new GameObject(q.Slot.name).transform;
                _districtGroup.SetParent(DistrictRoot, false);
                q.District.Build(this);
            }
            _districtGroup = null;
            RegionalRoads.Join(Net, _edges);
            _vehicles.ForEach(vehicle => vehicle.Net = Net);
            _coreRegion.ReconcilePortals();
            var access = new List<RegionalExpresswayPlan.Access>();
            foreach (var c in _coreRegion.Connections)
            {
                if (c.CityNode != null) access.Add(new RegionalExpresswayPlan.Access
                    { Edge = c.Edge, Node = c.CityNode, Face = c.CityFace, Connection = c });
                if (c.Quarter != null && c.Via == null && _coreRegion.TryPortal(c, out var portal))
                    access.Add(new RegionalExpresswayPlan.Access { Edge = c.Edge, Node = portal.Node,
                        Face = c.Quarter.District.Frame.ToWorld(portal.Local), Outlying = true, Connection = c });
            }
            var express = new RegionalExpresswayPlan(_coreRegion.BeltBounds, access, Net, BuiltFromSeed,
                PrimaryCore.Frame.ToWorldRect(PrimaryCore.Layout.Water));
            _landRects.Add(express.Bounds);
            _connectorKit = new StreetKit(((IDistrictHost)this).StaticRoot("Regional access roads")) { LampsOnly = true, Palms = false };
            // Every works street ending at the seaward cut - the artery on the gate line
            // and the four tier streets - runs across the port's pavement into the back
            // street junction the port laid under it: a few metres of bare carriageway,
            // no pavement of its own, because the port has already cleared the corners
            // of that junction for lorries to turn through.
            if (port != null && works != null)
            {
                int joined = 0;
                foreach (var (node, world) in works.SeawardMouths)
                {
                    float contractX = port.Frame.ToLocal(world).x;
                    if (!port.TryLandwardJunction(contractX, out var junction, out var local)) continue;
                    var from = new Vector3(node.X, 0f, node.Z);
                    var to = port.Frame.ToWorld(local);
                    RegionalRoads.Link(Net, node, junction, from, to, 9f);
                    bool ns = Mathf.Abs(to.z - from.z) > Mathf.Abs(to.x - from.x);
                    if (ns) _connectorKit.LayRoadAlongZ(from.x, Mathf.Min(from.z, to.z), Mathf.Max(from.z, to.z));
                    else _connectorKit.LayRoadAlongX(from.z, Mathf.Min(from.x, to.x), Mathf.Max(from.x, to.x));
                    ReserveCorridor(from, to);
                    joined++;
                }
                if (joined != works.SeawardMouths.Count)
                    Debug.LogWarning($"[CoreDemo] {joined}/{works.SeawardMouths.Count} works streets joined the port's back street.");
            }
            foreach (var road in express.Ground) BuildRegionalApproach(road);
            foreach (var node in express.Junctions) { _connectorKit.LayJunction(node.X, node.Z); _connectorKit.LayJunctionCorners(node.X, node.Z); }
            _regionalIsland = new IslandLandform(_primaryWorld, _coreRegion.WorldBounds, BuiltFromSeed,
                _reservations, express, harborSide, airport, PrimaryCore.Frame.ToWorldRect(PrimaryCore.Layout.Water));
            RegionalExpresswayView.Build(express, this, _bare, _regionalIsland.Height);
            Net.Finish();
            IndustrialFreight.Connect(_coreRegion, Net);
            _edges.Clear();
            ((IDistrictHost)this).RegisterRoads(Net.Edges);
            Debug.Log($"[CoreDemo] curved expressway: {express.Ramps.Count} ramps, {access.Count} city/district accesses; seed {BuiltFromSeed}.");
        }

        void BuildRegionalApproach(RegionalExpresswayPlan.GroundRoad road)
        {
            if (road.Path != null)
            {
                RegionalRoadView.Bend(road.Path, this, _bare);
                for (float s = 0; s < road.Path.Length; s += 4f)
                    ReserveCorridor(road.Path.PointAt(s), road.Path.PointAt(Mathf.Min(s + 4f, road.Path.Length)));
                return;
            }
            var from = road.From; var to = road.To;
            var c = road.Access?.Connection;
            var pump = road.Access?.Outlying == true && c.Portal == 0 ? PlanWayside(from, to, c.Quarter.Slot) : null;
            if (pump != null && !_coreRegion.ReserveFuel(pump.Ground)) pump = null;
            bool ns = Mathf.Abs(to.z - from.z) > Mathf.Abs(to.x - from.x);
            float a = ns ? from.z : from.x, b = ns ? to.z : to.x;
            LayConnector(c?.Edge ?? CityEdge.None, ns, ns ? from.x : from.z, Mathf.Min(a, b), Mathf.Max(a, b), pump);
            ReserveCorridor(from, to);
            WalkObstacles.City.Add(Rect.MinMaxRect(Mathf.Min(from.x, to.x) - StreetKit.OuterHalf,
                Mathf.Min(from.z, to.z) - StreetKit.OuterHalf, Mathf.Max(from.x, to.x) + StreetKit.OuterHalf,
                Mathf.Max(from.z, to.z) + StreetKit.OuterHalf));
            StandWayside(pump);
        }
    }
}
