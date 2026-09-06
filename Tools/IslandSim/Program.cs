using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RoadDemo;
using UnityEngine;

class Program
{
    static Rect Box(JsonElement e) => new Rect(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle(),e[3].GetSingle());
    static void Check(bool yes,string message) { if(!yes) throw new Exception(message); }
    static int Main(string[] args)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(args[0]));
        int tested=0;
        foreach(var fixture in document.RootElement.EnumerateArray())
        {
            int seed=fixture.GetProperty("seed").GetInt32();
            var city=Box(fixture.GetProperty("city")); var ring=Box(fixture.GetProperty("ring"));
            var reservations=new DistrictReservations(); reservations.Level(city,RoadDemoBuilder.RoadBed);
            var portArea=new List<CoreRegion.Quarter>();
            var river=Box(fixture.GetProperty("river"));
            river.yMin=city.yMin-1000; river.yMax=city.yMax+1000; reservations.Sea(river,false);
            CityEdge harborSide=CityEdge.West; AirportDemo.AirportDistrict airfield=null;
            foreach(var district in fixture.GetProperty("districts").EnumerateArray())
            {
                var f=district.GetProperty("frame");
                var frame=DistrictFrame.At(f[0].GetSingle(),f[1].GetSingle(),f[2].GetInt32());
                int ds=district.GetProperty("seed").GetInt32();
                var kind=(DistrictKind)district.GetProperty("kind").GetInt32();
                if(kind==DistrictKind.Harbor)
                {
                    // Actual harbor Plan/Reserve: no prefab/Editor calls occur here.
                    var harbor=new HarborDemo.HarborDistrict {berths=district.GetProperty("berths").GetInt32(),Frame=frame};
                    harbor.Plan(new[]{0f,240f},ds);
                    harbor.PlanSeaRoute(IslandLandform.BoundsFor(Box(fixture.GetProperty("region"))),reservations);
                    harbor.Reserve(reservations);
                    portArea.Add(new CoreRegion.Quarter {District=harbor,Slot=new DistrictSlot {
                        kind=kind,edge=(CityEdge)district.GetProperty("edge").GetInt32()}});
                    Check(harbor.LocalBounds==Box(district.GetProperty("bounds")),"region fixture has stale harbor bounds");
                    harborSide=(CityEdge)district.GetProperty("edge").GetInt32();
                }
                else if(kind==DistrictKind.Airport)
                {
                    var airport=new AirportDemo.AirportDistrict {Frame=frame};
                    airfield=airport; airport.Plan(new[]{0f},ds); airport.Reserve(reservations);
                }
                else if(kind==DistrictKind.Pad)
                {
                    var estate=new IndustrialDistrict {compact=true,pocket=true,Frame=frame}; estate.Plan(null,ds);
                    estate.Reserve(reservations);
                    portArea.Add(new CoreRegion.Quarter {District=estate,Slot=new DistrictSlot {
                        kind=kind,edge=(CityEdge)district.GetProperty("edge").GetInt32()}});
                }
                else reservations.Level(frame.ToWorldRect(Box(district.GetProperty("bounds"))),RoadDemoBuilder.RoadBed);
            }
            var net=new LaneNet(); var accesses=new List<RegionalExpresswayPlan.Access>();
            CheckIndustrialGround(portArea,reservations);
            var stubs=new List<Carriageway>();
            var freight=new FreightNetwork(fixture,net);
            foreach(var item in fixture.GetProperty("access").EnumerateArray())
            {
                var edge=(CityEdge)item.GetProperty("edge").GetInt32();
                bool outlying=item.GetProperty("outlying").GetBoolean();
                var p=item.GetProperty("point"); var face=new Vector3(p[0].GetSingle(),0,p[1].GetSingle());
                var actual=outlying?freight.At(face):null;
                if(actual!=null)
                { accesses.Add(new RegionalExpresswayPlan.Access {Edge=edge,Node=actual,Face=face,Outlying=true}); continue; }
                var away=RasterGateways.Outward(edge)*(outlying?1f:-1f);
                var centre=face+away*7.5f;
                var node=net.AddNode(centre.x,centre.z,7.5f,7.5f);
                var tail=centre+away*80f; var end=net.AddNode(tail.x,tail.z,7.5f,7.5f);
                stubs.Add(net.AddRoad(centre+away*7.5f,tail-away*7.5f,7.5f,new[]{2.5f},9,node,end,Math.Abs(away.z)>.5f));
                accesses.Add(new RegionalExpresswayPlan.Access {Edge=edge,Node=node,Face=face,Outlying=outlying});
            }
            var road=new RegionalExpresswayPlan(ring,accesses,net,seed,river);
            foreach(var ground in road.Ground)
            {
                var box=Rect.MinMaxRect(Math.Min(ground.From.x,ground.To.x)-16.5f,Math.Min(ground.From.z,ground.To.z)-16.5f,
                    Math.Max(ground.From.x,ground.To.x)+16.5f,Math.Max(ground.From.z,ground.To.z)+16.5f);
                reservations.Level(box,RoadDemoBuilder.RoadBed);
            }
            var land=new IslandLandform(city,Box(fixture.GetProperty("region")),seed,reservations,road,harborSide,airfield,Box(fixture.GetProperty("river")));
            net.Finish();
            freight.Check(net);
            Regression.Check(road,net,land,seed);
            foreach(float side in new[]{-1f,1f}) for(float d=100;d<=1400;d+=20)
            {
                var p=airfield.Frame.ToWorld(new Vector3(side*(airfield.RunwayHalf+d)-AirportDemo.AirportSpec.ApproachX,
                    0,-AirportDemo.AirportDistrict.BoundaryZ));
                // Existing FlightOps final-approach waypoints, in either wind direction.
                float flight=d<630?Mathf.Lerp(AirportDemo.AirportSpec.PaveY+4,66,(d-70)/560):Mathf.Lerp(66,121,(d-630)/770);
                float ground=land.Height(p.x,p.z), canopy=ground<.3f?0:24;
                Check(ground+canopy<flight-2,$"seed {seed}: scenery obstructs final approach");
            }
            foreach(float side in new[]{-1f,1f})
            {
                float half=airfield.RunwayHalf, pattern=-side*AirportDemo.AirportSpec.PatternWidth;
                var circuit=new[]{
                    (new Vector2(-side*(half+600),pattern),220f),
                    (new Vector2(side*(half+500),pattern),220f),
                    (new Vector2(side*(half+AirportDemo.AirportSpec.FinalLength+150),pattern*.45f),158f),
                    (new Vector2(side*(half+AirportDemo.AirportSpec.FinalLength),0),121f)};
                for(int leg=1;leg<circuit.Length;leg++) for(int sample=0;sample<=20;sample++)
                {
                    float t=sample/20f;
                    var local=Vector2.Lerp(circuit[leg-1].Item1,circuit[leg].Item1,t);
                    var p=airfield.Frame.ToWorld(new Vector3(local.x-AirportDemo.AirportSpec.ApproachX,0,
                        local.y-AirportDemo.AirportDistrict.BoundaryZ));
                    float flight=Mathf.Lerp(circuit[leg-1].Item2,circuit[leg].Item2,t);
                    float ground=land.Height(p.x,p.z), canopy=ground<.3f?0:24;
                    Check(ground+canopy<flight-2,$"seed {seed}: scenery obstructs airport circuit");
                }
            }
            foreach(var target in stubs)
            {
                var routes=net.RouteToward(target.LaneFor(1,2.5f));
                foreach(var source in stubs)
                    if(source!=target && !routes.ContainsKey(source.LaneFor(-1,2.5f)))
                    {
                        Console.WriteLine($"unreachable access {stubs.IndexOf(source)} ({accesses[stubs.IndexOf(source)].Edge}) -> {stubs.IndexOf(target)} ({accesses[stubs.IndexOf(target)].Edge})");
                        foreach(var ramp in road.Ramps)
                        {
                            var lane=net.Roads.Single(r=>r.Path==ramp.Line).LaneFor(1,0);
                            bool enters=ramp.A.Connectors.Any(c=>c.To==lane),leaves=ramp.B.Connectors.Any(c=>c.From==lane);
                            if(!enters||!leaves) Console.WriteLine($"ramp off={ramp.Off} start={ramp.Line.Start.x},{ramp.Line.Start.z} enters={enters} leaves={leaves}");
                        }
                        throw new Exception($"seed {seed}: inaccessible district via expressway");
                    }
            }
            foreach(var ground in road.Ground)
            {
                var line=ground.Line;
                float length=line.Length;
                for(float s=0;s<=length;s+=8f)
                {
                    var at=line.PointAt(s);
                    var right=line.RightAt(s);
                    foreach(float side in new[]{-StreetKit.StreetHalf,0f,StreetKit.StreetHalf})
                    {
                        var p=at+right*side;
                        Check(land.WaterDistance(p.x,p.z)>0,$"seed {seed}: ground road in water at {p.x},{p.z}");
                        Check(land.Height(p.x,p.z)<.2f,$"seed {seed}: terrain covers road at {p.x},{p.z}");
                        Check(land.Height(p.x,p.z)>RoadDemoBuilder.WaterY,$"seed {seed}: ground road has a flooded bed at {p.x},{p.z}");
                    }
                }
            }
            Check(road.Ramps.Count>=12,$"seed {seed}: missing curved interchanges");
            foreach(var deck in road.Decks)
            {
                Check(Vector3.Distance(deck.Line.Start,deck.Line.End)<.001f,"open carriageway loop");
                Check(Vector3.Dot(deck.Line.StartDir,deck.Line.EndDir)>.999999f,"carriageway loop tangent kink");
                Check(deck.Bridges.Count==2,$"seed {seed}: missing river bridge");
                foreach(float at in deck.Bridges)
                    Check(deck.Height(at)-1.65f-RoadDemoBuilder.WaterY>18f,"river bridge obstructs the existing 13.7m mast");
                for(float s=1;s<deck.Line.Length;s+=5)
                    Check(Math.Abs(deck.Height(s+1)-deck.Height(s-1))/2<.075f,$"seed {seed}: mainline grade above 7.5%");
            }
            foreach(var ramp in road.Ramps)
            {
                var node=ramp.Off?ramp.B:ramp.A;
                var direction=ramp.Off?-ramp.Line.EndDir:ramp.Line.StartDir;
                foreach(var floor in road.Ground)
                {
                    if(floor.A!=node && floor.B!=node) continue;
                    var face=(floor.A==node?floor.To-floor.From:floor.From-floor.To).normalized;
                    Check(Vector3.Dot(direction,face)<.95f,"ramp and local collector overlap at terminal");
                }
                float s=ramp.Off?0:ramp.Line.Length;
                ramp.Deck.Line.Project(ramp.Line.PointAt(s),out float station,out _);
                Check(Vector3.Dot(ramp.Line.DirAt(s),ramp.Deck.Line.DirAt(station))>.99999f,"ramp gore tangent kink");
                Check(Math.Abs(ramp.Height(s)-ramp.Deck.Height(station))<.01f,"ramp gore height step");
            }
            foreach(var ramp in road.Ramps)
                for(float s=1;s<ramp.Line.Length;s+=5)
                    Check(Math.Abs(ramp.Height(s+1)-ramp.Height(s-1))/2<.075f,$"seed {seed}: ramp grade above 7.5%");
            foreach(var deck in road.Decks)
                for(float s=24;s<deck.Line.Length;s+=48)
                    if(RegionalExpresswayView.PierFree(road,deck.Line.PointAt(s)))
                    {
                        var at=deck.Line.PointAt(s);
                        foreach(var ground in road.Ground)
                            Check(CurveDistance(at,ground.Line)>=StreetKit.OuterHalf+3f,
                                $"seed {seed}: accepted deck pier intersects floor road");
                    }
            foreach(var ramp in road.Ramps)
                for(float s=25;s<ramp.Line.Length-20;s+=42)
                    if(RegionalExpresswayView.PierFree(road,ramp.Line.PointAt(s),ramp))
                    {
                        var at=ramp.Line.PointAt(s);
                        foreach(var ground in road.Ground)
                            Check(CurveDistance(at,ground.Line)>=StreetKit.OuterHalf+3f,
                                $"seed {seed}: accepted ramp pier intersects floor road");
                    }
            float peak=0;
            for(int z=0;z<=100;z++) for(int x=0;x<=100;x++)
            {
                float wx=Mathf.Lerp(land.Bounds.xMin,land.Bounds.xMax,x/100f),wz=Mathf.Lerp(land.Bounds.yMin,land.Bounds.yMax,z/100f);
                float h=land.Height(wx,wz); Check(float.IsFinite(h),"nonfinite terrain"); peak=Math.Max(peak,h);
                if(x==0||z==0||x==100||z==100) Check(h<RoadDemoBuilder.WaterY,"land reaches mesh edge");
            }
            Check(peak>120 && peak<420,$"seed {seed}: missing/unbounded mountain range, peak {peak}");
            foreach(var water in reservations.Water)
                Check(land.Height(water.center.x,water.center.y)<RoadDemoBuilder.WaterY-6,"shipping basin is not deep water");
            if(tested++==0 && args.Length>1) Export(args[1],land,road,city,reservations);
            Console.WriteLine($"seed {seed}: {road.Ramps.Count} curved ramps; {road.Ground.Count} dry ground roads; all {accesses.Count} accesses route both ways; peak {peak:F0}m");
        }
        Console.WriteLine($"PASSED {tested} island/expressway fixtures using actual runtime assembly, harbor/airport reservations. Suburb/industrial footprints are model contracts; no meshes/Unity/Play verdict.");
        return 0;
    }

    static void CheckIndustrialGround(List<CoreRegion.Quarter> area,DistrictReservations reservations)
    {
        var port=area.Single(q=>q.District is HarborDemo.HarborDistrict);
        var gaps=new List<Vector3>(); var beyond=new List<Vector3>();
        foreach(var q in area.Where(q=>q.District is IndustrialDistrict))
        {
            var bounds=q.World;
            var a=port.District.Frame.ToLocal(new Vector3(bounds.xMin,0,bounds.yMin));
            var b=port.District.Frame.ToLocal(new Vector3(bounds.xMax,0,bounds.yMax));
            float x=(a.x+b.x)*.5f;
            gaps.Add(port.District.Frame.ToWorld(new Vector3(x,0,15f)));
            beyond.Add(port.District.Frame.ToWorld(new Vector3(x,0,Math.Max(a.z,b.z)+45f)));
        }
        Check(gaps.Any(p=>!reservations.InBare(p.x,p.z)),"fixture does not exercise the unreserved port gap");
        var oldBeyond=beyond.Select(p=>reservations.InBare(p.x,p.z)).ToArray();
        PortIndustryLayout.ReserveGround(area,reservations);
        foreach(var point in gaps)
            Check(reservations.InBare(point.x,point.z),"gap between estate and port permits wild flora");
        for(int i=0;i<beyond.Count;i++)
            Check(reservations.InBare(beyond[i].x,beyond[i].z)==oldBeyond[i],"industrial clearing extends beyond a shorter estate");
        var apron=port.District.Frame.ToWorld(new Vector3(port.District.LocalBounds.center.x,0,5f));
        Check(reservations.FlatAt(apron.x,apron.z,20,out var level,out _) && Math.Abs(level-HarborDemo.HarborDistrict.LandY)<.001f,
            "shared industrial ground overrides the harbor apron level");
    }
    static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
    {
        var ab=b-a;
        float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.001f,ab.sqrMagnitude));
        return Vector3.Distance(p,a+ab*t);
    }
    static float CurveDistance(Vector3 p,RoadLine line)
    {
        float nearest=float.MaxValue;
        for(int i=1;i<line.Pts.Length;i++) nearest=Math.Min(nearest,SegmentDistance(p,line.Pts[i-1],line.Pts[i]));
        return nearest;
    }
    static void Export(string path,IslandLandform land,RegionalExpresswayPlan roads,Rect city,DistrictReservations reservations)
    {
        using var writer=new StreamWriter(path);
        writer.WriteLine("x,z,height");
        for(int z=0;z<=180;z++) for(int x=0;x<=220;x++)
        {
            float wx=Mathf.Lerp(land.Bounds.xMin,land.Bounds.xMax,x/220f),wz=Mathf.Lerp(land.Bounds.yMin,land.Bounds.yMax,z/180f);
            writer.WriteLine(FormattableString.Invariant($"{wx},{wz},{land.Height(wx,wz)}"));
        }
        var lines=new List<float[][]>();
        foreach(var deck in roads.Decks) lines.Add(deck.Line.Pts.Select(p=>new[]{p.x,p.z}).ToArray());
        foreach(var ramp in roads.Ramps) lines.Add(ramp.Line.Pts.Select(p=>new[]{p.x,p.z}).ToArray());
        foreach(var road in roads.Ground) lines.Add(road.Line.Pts.Select(p=>new[]{p.x,p.z}).ToArray());
        File.WriteAllText(path+".roads.json",JsonSerializer.Serialize(new {lines,flats=reservations.Flat.Select(p=>new[]{p.area.xMin,p.area.yMin,p.area.width,p.area.height}).ToArray()}));
    }
}
