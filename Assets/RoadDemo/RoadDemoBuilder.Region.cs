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
            CityEdge harborSide = CityEdge.West;
            AirportDemo.AirportDistrict airport = null;
            foreach (var q in _coreRegion.Quarters)
            {
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
                if (c.Quarter != null && _coreRegion.TryPortal(c, out var portal))
                    access.Add(new RegionalExpresswayPlan.Access { Edge = c.Edge, Node = portal.Node,
                        Face = c.Quarter.District.Frame.ToWorld(portal.Local), Outlying = true, Connection = c });
            }
            var express = new RegionalExpresswayPlan(_coreRegion.BeltBounds, access, Net, BuiltFromSeed,
                PrimaryCore.Frame.ToWorldRect(PrimaryCore.Layout.Water));
            _landRects.Add(express.Bounds);
            _connectorKit = new StreetKit(((IDistrictHost)this).StaticRoot("Regional access roads")) { LampsOnly = true, Palms = false };
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
