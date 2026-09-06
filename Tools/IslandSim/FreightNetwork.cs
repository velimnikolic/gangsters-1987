using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RoadDemo;
using UnityEngine;

// Actual estate rasters and public port street joined before building the real expressway.
// The harbour street uses its planned portal level; prefab-dependent warehouse depth is unverified.
sealed class FreightNetwork
{
    readonly List<(Vector3 Face,RoadNode Node)> _portals=new();
    readonly List<(Rect Area,LaneNet Graph)> _estates=new();
    Rect _port;
    public int PortLinks {get;private set;}
    public FreightNetwork(JsonElement fixture,LaneNet net)
    {
        // The works' seaward streets run out onto the port's back street: the zone is
        // planned first so the street can take a junction under each (RoadDemoBuilder.Region).
        var mouths=new List<(RoadNode Node,Vector3 World)>();
        foreach(var district in fixture.GetProperty("districts").EnumerateArray())
        {
            var f=district.GetProperty("frame");
            var frame=DistrictFrame.At(f[0].GetSingle(),f[1].GetSingle(),f[2].GetInt32());
            int seed=district.GetProperty("seed").GetInt32();
            var kind=(DistrictKind)district.GetProperty("kind").GetInt32();
            if(kind==DistrictKind.Pad)
            {
                var estate=new IndustrialDistrict {portZone=true,Frame=frame}; estate.Plan(null,seed);
                var graph=RasterGraph.Build(estate.Raster,frame,9,13,5.4f);
                RegionalRoads.Join(net,graph.Edges);
                _estates.Add((frame.ToWorldRect(estate.LocalBounds),graph));
                foreach(var gate in RasterGateways.Find(estate.Raster))
                    _portals.Add((frame.ToWorld(gate.Face),graph.Nodes[gate.Junction]));
                mouths.AddRange(IndustrialDistrict.FindSeawardMouths(estate.Raster,estate.SeawardCut,graph,frame));
            }
        }
        foreach(var district in fixture.GetProperty("districts").EnumerateArray())
        {
            var f=district.GetProperty("frame");
            var frame=DistrictFrame.At(f[0].GetSingle(),f[1].GetSingle(),f[2].GetInt32());
            int seed=district.GetProperty("seed").GetInt32();
            if((DistrictKind)district.GetProperty("kind").GetInt32()!=DistrictKind.Harbor) continue;
            var harbor=new HarborDemo.HarborDistrict {berths=district.GetProperty("berths").GetInt32(),Frame=frame};
            harbor.Plan(new[]{0f,240f},seed); _port=frame.ToWorldRect(harbor.LocalBounds);
            var stops=new List<float>{-harbor.QuayHalf-10f,0f,240f,harbor.QuayHalf+250f};
            foreach(var mouth in mouths)
            {
                float x=frame.ToLocal(mouth.World).x;
                if(!stops.Exists(s=>Math.Abs(s-x)<.1f)) stops.Add(x);
            }
            stops.Sort();
            var graph=HarborDemo.HarborStreet.Build(frame,stops,-7.5f);
            RegionalRoads.Join(net,graph.Edges);
            _portals.Add((frame.ToWorld(Vector3.zero),graph.Nodes[stops.IndexOf(0f)]));
            _portals.Add((frame.ToWorld(new Vector3(240,0,0)),graph.Nodes[stops.IndexOf(240f)]));
            foreach(var mouth in mouths)
            {
                float x=frame.ToLocal(mouth.World).x;
                int at=stops.FindIndex(s=>Math.Abs(s-x)<.1f);
                if(at<0) throw new Exception("works street has no junction on the port road");
                var from=new Vector3(mouth.Node.X,0,mouth.Node.Z);
                var to=frame.ToWorld(new Vector3(stops[at],0,0));
                RegionalRoads.Link(net,mouth.Node,graph.Nodes[at],from,to,9f);
                PortLinks++;
            }
        }
        if(mouths.Count!=5) throw new Exception($"port zone has {mouths.Count} seaward street mouths, not 5");
        if(PortLinks!=5) throw new Exception($"{PortLinks} works streets joined the port road, not 5");
    }
    public RoadNode At(Vector3 face)=>_portals.Where(p=>(p.Face-face).sqrMagnitude<.1f).Select(p=>p.Node).FirstOrDefault();
    public void Check(LaneNet net)
    {
        var docks=IndustrialFreight.Stops(net,_port);
        if(docks.Count<_estates.Count*2) throw new Exception($"only {docks.Count} port stops for {_estates.Count*2} lorries");
        int index=0;
        foreach(var estate in _estates)
        {
            var stops=IndustrialFreight.Stops(net,estate.Area);
            if(stops.Count==0) throw new Exception("estate has no loading stop");
            for(int i=0;i<2;i++,index++)
            {
                var home=stops[index%stops.Count].Lane; var dock=docks[index].Lane;
                var outward=net.RouteToward(dock); var inward=net.RouteToward(home);
                if(!net.ReachableFrom(home).Contains(dock)) throw new Exception("forward admission misses a routed motorway trip");
                if(!outward.ContainsKey(home)||!inward.ContainsKey(dock)) throw new Exception("estate/port round trip is disconnected");
                if(estate.Graph.Edges.Any(e=>e!=dock&&!outward.ContainsKey(e))) throw new Exception("a spawned estate lorry has no port route");
            }
        }
        Console.WriteLine($"freight graph: {index} distinct port calls, every estate spawn lane and return reachable");
    }
}
