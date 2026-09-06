using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEngine;

static class Program
{
    static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    static int Main()
    {
        int[] seeds={1,2,7,31,1987,2026,91237,-42,int.MinValue,int.MaxValue};
        var layouts=new HashSet<string>();
        var fixtures=new List<object>();
        foreach(int seed in seeds)
        {
            var core=new CoreDistrict(); core.Plan(seed);
            int faults=core.Raster.Faults;
            var parking=new List<CoreAmenityLayout.Site>();
            var fuel=core.FuelSites;
            var development=new List<CoreAmenityLayout.Site>();
            var repurposed=new List<Rect>();
            CoreAmenityLayout.Select(core.Raster,core.Layout.Lots,seed,3,5,parking,fuel,development,core.Layout.Residential.Select(b=>b.Box),repurposed);
            var service=new CoreServicePlan();
            service.Plan(core.Layout,core.Raster,development,seed,repurposed);
            Require(service.FireCount>=2,$"seed {seed}: fewer than two fire stations ({service.FireCount})");
            Require(service.PoliceCount>=2,$"seed {seed}: fewer than two precincts ({service.PoliceCount})");
            Require(fuel.Count>=2,$"seed {seed}: fewer than two fuel stations ({fuel.Count})");
            foreach(var site in service.Sites)
            {
                var b=site.Parcel.Box;
                Require(b.xMin>=site.Source.xMin && b.xMax<=site.Source.xMax && b.yMin>=site.Source.yMin && b.yMax<=site.Source.yMax,"service escapes parcel");
                Require(CoreServicePlan.RoadWidth(core.Raster,b,site.Parcel.Entry)>=3,"service driveway lacks street");
                Require(!fuel.Any(f=>f.Box.Overlaps(b)) && !parking.Any(p=>p.Box.Overlaps(b)),"service overlaps amenity");
            }
            for(int i=0;i<fuel.Count;i++) for(int j=i+1;j<fuel.Count;j++)
                Require(Vector2.Distance(fuel[i].Box.center,fuel[j].Box.center)>=400f,"fuel spacing");
            var world=core.Frame.ToWorldRect(core.LocalBounds);
            var region=new CoreRegion(core,world,seed,2);
            fixtures.Add(Fixture(core,region,seed));
            var expanded=new CoreRegion(core,world,seed,4);
            CheckConnections(expanded);
            CheckPortIndustry(expanded);
            fixtures.Add(Fixture(core,expanded,seed));
            var noSuburbs=new CoreRegion(core,world,seed,0);
            CheckPortIndustry(noSuburbs);
            fixtures.Add(Fixture(core,noSuburbs,seed));
            string signature=Signature(region);
            Require(signature==Signature(new CoreRegion(core,world,seed,2)),"seed replay changed region");
            layouts.Add(signature);
            var industries=region.Quarters.Where(q=>q.District is IndustrialDistrict).ToArray();
            Require(industries.Length>=3,"industry was not split into smaller estates");
            Require(industries.Sum(q=>q.World.width*q.World.height)>=region.IndustryAreaTarget,"industry area did not double");
            Require(industries.All(q=>q.World.width*q.World.height<region.IndustryAreaTarget*.5f),"individual estate is not smaller");
            Require(region.Quarters.Count==industries.Length+4,"missing district");
            Require(region.Quarters.Select(q=>q.Slot.name).Distinct().Count()==region.Quarters.Count,"duplicate district names");
            foreach(var q in expanded.Quarters)
                Require(!core.Layout.Territory.Quarters.Any(c=>string.Equals(c.Name,q.Slot.name,StringComparison.OrdinalIgnoreCase)),"suburb reused a Core quarter name");
            Require(region.Connections.Any(c=>c.CityNode!=null),"region has no core entry");
            for(int i=0;i<region.Quarters.Count;i++)
            {
                var q=region.Quarters[i];
                var all=region.WorldBounds;
                Require(all.xMin<=q.World.xMin && all.xMax>=q.World.xMax && all.yMin<=q.World.yMin && all.yMax>=q.World.yMax,"district outside regional map");
                Require(!q.World.Overlaps(world),"district overlaps core");
                for(int j=i+1;j<region.Quarters.Count;j++) Require(!q.World.Overlaps(region.Quarters[j].World),"districts overlap");
                Require(region.Connections.Any(c=>c.Quarter==q),"district disconnected");
            }
            foreach(var c in region.Connections)
            {
                var box=region.BeltBounds;
                float lo=CoreRegion.Vertical(c.Edge)?box.xMin:box.yMin;
                float hi=CoreRegion.Vertical(c.Edge)?box.xMax:box.yMax;
                Require(c.Across>lo && c.Across<hi,"connection outside belt");
            }
            CheckConnections(region);
            CheckPortIndustry(region);
            foreach(var c in region.Connections) Require(region.TryPortal(c,out _),"published portal was not resolved");
            if(seed==1987)
            {
                MissingPortals(region);
                IndustrialDistrict.RejectPlans=100; IndustrialDistrict.PlanCalls=0;
                try
                {
                    var degraded=new CoreRegion(core,world,seed,2);
                    Require(IndustrialDistrict.PlanCalls==24,"failed industry retry loop is not bounded");
                    Require(degraded.Quarters.Any(q=>q.Slot.kind==DistrictKind.Harbor) &&
                        degraded.Quarters.Any(q=>q.Slot.kind==DistrictKind.Airport) &&
                        degraded.Quarters.Count(q=>q.Slot.kind==DistrictKind.Suburb)==2,
                        "bad optional estate discarded harbour, airport or suburbs");
                }
                finally { IndustrialDistrict.RejectPlans=0; }
            }
            Console.WriteLine($"seed {seed}: raster faults {faults}; {service.FireCount} fire, {service.PoliceCount} police, {fuel.Count} fuel; {region.Quarters.Count} districts, {region.Connections.Count} links");
        }
        Require(layouts.Count==seeds.Length,"different seeds reused region");
        var fixturePath=Environment.GetEnvironmentVariable("GANGSTERS_REGION_FIXTURES");
        if(!string.IsNullOrEmpty(fixturePath)) System.IO.File.WriteAllText(fixturePath,System.Text.Json.JsonSerializer.Serialize(fixtures));
        var empty=new CoreDistrict {Raster=new CoreRoads.Raster()};
        Require(CoreRegion.TryCreate(empty,new Rect(0,0,100,100),0,2)==null,"missing core gateway did not fall back");
        Console.WriteLine($"PASSED {seeds.Length} seeds: layout replay, variation, footprints, road frontage, spacing and connected port industry. Satellite views are doubles; no Unity/Play verdict.");
        return 0;
    }
    static string Signature(CoreRegion region) => string.Join("|",region.Quarters.Select(q=>$"{q.District.Name}:{q.Slot.edge}:{q.Slot.seed}:{q.World}"));
    static float[] Box(Rect r)=>new[]{r.xMin,r.yMin,r.width,r.height};
    static object Fixture(CoreDistrict core,CoreRegion region,int seed)
    {
        var access=new List<object>();
        foreach(var c in region.Connections)
        {
            if(c.CityNode!=null) access.Add(new {edge=(int)c.Edge,outlying=false,point=new[]{c.CityFace.x,c.CityFace.z}});
            if(c.Quarter!=null && region.TryPortal(c,out var p))
            {var at=c.Quarter.District.Frame.ToWorld(p.Local);access.Add(new {edge=(int)c.Edge,outlying=true,point=new[]{at.x,at.z}});}
        }
        return new {seed,city=Box(core.Frame.ToWorldRect(core.LocalBounds)),ring=Box(region.BeltBounds),region=Box(region.WorldBounds),
            river=Box(core.Frame.ToWorldRect(core.Layout.Water)),industryGround=Box(region.IndustryGroundBounds),access,
            districts=region.Quarters.Select(q=>new {kind=(int)q.Slot.kind,edge=(int)q.Slot.edge,q.Slot.seed,
                frame=new[]{q.District.Frame.origin.x,q.District.Frame.origin.z,(float)q.District.Frame.yaw},
                bounds=Box(q.District.LocalBounds),berths=q.District is HarborDemo.HarborDistrict h?h.berths:0}).ToArray()};
    }
    static void CheckConnections(CoreRegion region)
    {
        foreach(var c in region.Connections)
        {
            foreach(var other in region.Connections)
                if(other!=c && other.Edge==c.Edge) Require(Math.Abs(other.Across-c.Across)>=30f,"overlapping belt junctions");
            if(c.Quarter==null) continue;
            var edge=c.Edge;
            var belt=region.BeltBounds;
            var q=c.Quarter.World;
            float from=edge==CityEdge.South?belt.yMin-166:edge==CityEdge.North?belt.yMax+166:edge==CityEdge.West?belt.xMin-166:belt.xMax+166;
            float to=edge==CityEdge.South?q.yMax:edge==CityEdge.North?q.yMin:edge==CityEdge.West?q.xMax:q.xMin;
            var corridor=CoreRegion.Vertical(edge)?Rect.MinMaxRect(c.Across-12.5f,Math.Min(from,to),c.Across+12.5f,Math.Max(from,to))
                :Rect.MinMaxRect(Math.Min(from,to),c.Across-12.5f,Math.Max(from,to),c.Across+12.5f);
            foreach(var district in region.Quarters)
                if(district!=c.Quarter) Require(!corridor.Overlaps(district.World),$"{c.Quarter.District.Name} approach crosses {district.District.Name}");
        }
    }
    static void MissingPortals(CoreRegion region)
    {
        var port=region.Quarters.Single(q=>q.Slot.kind==DistrictKind.Harbor);
        var published=((DistrictContract)port.District).Published;
        var second=region.Connections.Single(c=>c.Quarter==port && c.Portal==1);
        published.RemoveAt(0);
        Require(region.TryPortal(second,out _),"portal resolution depends on list index");
        published.Clear();
        region.ReconcilePortals();
        Require(!region.Connections.Any(c=>c.Quarter==port),"missing portals retained a freeway exit");
        Require(region.Connections.Any(c=>c.CityNode!=null),"portal failure removed core gateways");
    }

    static void CheckPortIndustry(CoreRegion region)
    {
        var port=region.Quarters.Single(q=>q.Slot.kind==DistrictKind.Harbor);
        var works=region.Quarters.Where(q=>q.District is IndustrialDistrict).ToArray();
        var joined=new HashSet<CoreRegion.Quarter> {port};
        bool added;
        do
        {
            added=false;
            foreach(var q in works)
            {
                if(joined.Contains(q)) continue;
                if(!joined.Any(other=>Gap(q.World,other.World)<=30.01f)) continue;
                joined.Add(q); added=true;
            }
        } while(added);
        Require(joined.Count==works.Length+1,"industry is not one connected port area");
        foreach(var q in works)
        {
            Require(q.Slot.edge==port.Slot.edge,"estate left the port shore");
            var box=q.World;
            var a=port.District.Frame.ToLocal(new Vector3(box.xMin,0,box.yMin));
            var b=port.District.Frame.ToLocal(new Vector3(box.xMax,0,box.yMax));
            Require(Math.Abs(Math.Min(a.z,b.z)-30f)<.01f,"estate is not beside the port's landward edge");
            var ground=region.IndustryGroundBounds;
            Require(ground.xMin<=box.xMin && ground.xMax>=box.xMax && ground.yMin<=box.yMin && ground.yMax>=box.yMax,
                "shared industrial ground misses an estate");
            var gap=port.District.Frame.ToWorld(new Vector3((a.x+b.x)*.5f,0,15));
            Require(ground.Contains(new Vector2(gap.x,gap.z)),"gap behind port has no shared ground reservation");
            var connection=region.Connections.Single(c=>c.Quarter==q);
            Require(region.TryPortal(connection,out var portal),"estate lost its real raster gateway");
            var facing=q.District.Frame.ToWorldDir(portal.LocalDir);
            Require(Vector3.Dot(facing,RasterGateways.Outward(connection.Edge))<-.99f,"estate gateway faces away from the city");
            var face=q.District.Frame.ToWorld(portal.Local);
            Require(Math.Abs(CoreRegion.Across(connection.Edge,face)-connection.Across)<.01f,"estate approach misses its rotated gateway");
        }
    }
    static float Gap(Rect a,Rect b)
    {
        float x=Math.Max(0,Math.Max(a.xMin-b.xMax,b.xMin-a.xMax));
        float z=Math.Max(0,Math.Max(a.yMin-b.yMax,b.yMin-a.yMax));
        return (float)Math.Sqrt(x*x+z*z);
    }
}
