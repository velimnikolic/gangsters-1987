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
        ChecksPublicStreetAtOppositeCorner();
        ChecksAlternativeQuarterPair();
        CheckRetainedServices();
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
            CoreAmenityLayout.PickCourthouse(development,core.Layout.Territory);
            service.Plan(core.Layout,core.Raster,development,seed,repurposed);
            CheckServices(core,service,parking,fuel,development);
            Require(service.FireCount>=2,$"seed {seed}: fewer than two fire stations ({service.FireCount})");

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
            Require(industries.Length==1,"industry is not one zone behind the port");
            var zone=(IndustrialDistrict)industries[0].District;
            Require(zone.Layout.Islands.Count==20,$"port zone has {zone.Layout.Islands.Count} islands, not 20");
            Require(zone.Layout.Parcels.Count==20,$"port zone has {zone.Layout.Parcels.Count} parcels, not 20");
            foreach(var parcel in zone.Layout.Parcels)
            {
                Require(parcel.W>=13&&parcel.W<=14&&parcel.D>=12&&parcel.D<=13,$"port zone parcel {parcel.Name} is {parcel.W}x{parcel.D} cells");
                IndustrialLayout.Least(parcel.Recipe,out int mw,out int md);
                Require(parcel.W>=mw&&parcel.D>=md,$"port zone parcel {parcel.Name} is too small for its {parcel.Recipe}");
            }
            Require(zone.Layout.Parcels.Count(p=>p.Recipe==IndustrialLayout.Recipe.Works||p.Recipe==IndustrialLayout.Recipe.Plant)>=4,"port zone has no works");
            Require(zone.Layout.Parcels.Count(p=>p.Recipe==IndustrialLayout.Recipe.Haulage)==1,"port zone has no haulage yard");
            Require(zone.Raster.Faults==0,$"port zone drawing has {zone.Raster.Faults} raster faults");
            Console.WriteLine($"seed {seed} port zone: {zone.Layout.Name}, {zone.LocalBounds.width:0}x{zone.LocalBounds.height:0} m, "+
                string.Join(" ",zone.Layout.Parcels.GroupBy(p=>p.Recipe).OrderBy(g=>g.Key).Select(g=>$"{g.Key}x{g.Count()}")));
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
            Console.WriteLine($"seed {seed}: raster faults {faults}; {service.FireCount} fire, {service.TotalPoliceCount} total police, {fuel.Count} fuel; {region.Quarters.Count} districts, {region.Connections.Count} links");
        }
        Require(layouts.Count==seeds.Length,"different seeds reused region");
        var fixturePath=Environment.GetEnvironmentVariable("GANGSTERS_REGION_FIXTURES");
        if(!string.IsNullOrEmpty(fixturePath)) System.IO.File.WriteAllText(fixturePath,System.Text.Json.JsonSerializer.Serialize(fixtures));
        var empty=new CoreDistrict {Raster=new CoreRoads.Raster()};
        Require(CoreRegion.TryCreate(empty,new Rect(0,0,100,100),0,2)==null,"missing core gateway did not fall back");
        Console.WriteLine($"PASSED {seeds.Length} seeds: layout replay, variation, footprints, road frontage, spacing and connected port industry. Satellite views are doubles; no Unity/Play verdict.");
        return 0;
    }
    static void CheckServices(CoreDistrict core, CoreServicePlan service,
        List<CoreAmenityLayout.Site> parking, List<CoreAmenityLayout.Site> fuel,
        List<CoreAmenityLayout.Site> development)
    {
        int quarters=core.Layout.Territory.Quarters.Count(q=>q.BlockIds.Count>0);
        int authored=core.Layout.Territory.Blocks.Count(b=>b.SourceName=="police-station-block");
        Require(service.ExistingPoliceCount==authored,"authored station not counted");
        Require(service.TotalPoliceCount==(quarters+1)/2,$"precinct quota: {service.TotalPoliceCount} for {quarters} quarters");
        var residential=core.Layout.Territory.Quarters.Where(q=>q.BlockIds.Count>0 && q.Id!=CoreQuarterId.Downtown);
        Require(service.FireCount==residential.Count(),"fire station missing from a residential quarter");
        foreach(var q in residential)
            Require(service.Sites.Count(s=>!s.Police && s.Quarter==q.Id)==1,"fire stations not distributed by quarter");
        var covered=new HashSet<CoreQuarterId>();
        foreach(var site in service.Sites)
        {
            var b=site.Parcel.Box;
            Require(!development.Any(d=>d.Box.Overlaps(b)),"service still assigned to housing");
            Require(!fuel.Any(f=>f.Box.Overlaps(b)) && !parking.Any(p=>p.Box.Overlaps(b)),"service overlaps amenity");
            Require(!service.Sites.Any(s=>s!=site && s.Source.Overlaps(site.Source)),"services share a source block");
            Require(CoreServicePlan.RoadWidth(core.Raster,b,site.Parcel.Entry)>=3,"service driveway lacks street");
            if(!site.Police) continue;
            Require(site.Serves.Count>=1 && site.Serves.Count<=2 && site.Serves.Contains(site.Quarter),"precinct outside its assigned quarters");
            foreach(var q in site.Serves) Require(covered.Add(q),"quarter assigned to two new precincts");
            var drive=site.Parcel.Entry;
            var door=drive==ParkingEntrySide.East?ParkingEntrySide.North:drive==ParkingEntrySide.North?ParkingEntrySide.West:
                drive==ParkingEntrySide.West?ParkingEntrySide.South:ParkingEntrySide.East;
            Require(CoreServicePlan.RoadWidth(core.Raster,b,door)>=2,"precinct public entrance lacks street");
        }
    }
    static void CheckRetainedServices()
    {
        foreach(int budget in new[]{1,2,3,4,5})
        {
            var core=new CoreDistrict {quarterBudget=budget}; core.Plan(1987);
            var parking=new List<CoreAmenityLayout.Site>(); var fuel=new List<CoreAmenityLayout.Site>();
            var development=new List<CoreAmenityLayout.Site>(); var repurposed=new List<Rect>();
            CoreAmenityLayout.Select(core.Raster,core.Layout.Lots,1987,3,5,parking,fuel,development,core.Layout.Residential.Select(b=>b.Box),repurposed);
            CoreAmenityLayout.PickCourthouse(development,core.Layout.Territory);
            var service=new CoreServicePlan(); service.Plan(core.Layout,core.Raster,development,1987,repurposed);
            CheckServices(core,service,parking,fuel,development);
            string before=string.Join("|",service.Sites.Select(s=>$"{s.Police}:{s.Quarter}:{s.Parcel.Box}:{s.Parcel.Entry}"));
            // Recreate the complete plan so raster mutation cannot hide nondeterminism.
            var replay=new CoreDistrict {quarterBudget=budget}; replay.Plan(1987);
            CoreAmenityLayout.Select(replay.Raster,replay.Layout.Lots,1987,3,5,parking,fuel,development,replay.Layout.Residential.Select(b=>b.Box),repurposed);
            CoreAmenityLayout.PickCourthouse(development,replay.Layout.Territory);
            service.Plan(replay.Layout,replay.Raster,development,1987,repurposed);
            Require(before==string.Join("|",service.Sites.Select(s=>$"{s.Police}:{s.Quarter}:{s.Parcel.Box}:{s.Parcel.Entry}")),"service seed replay changed");
            Console.WriteLine($"retained budget {budget}: {service.TotalPoliceCount}/{service.PoliceTarget} precincts, {service.FireCount} fire stations");
            service.Clear();
            Require(service.TotalPoliceCount==0 && service.PoliceTarget==0 && service.FireCount==0,"service cleanup retained coverage");
        }
    }
    static void ChecksAlternativeQuarterPair()
    {
        var core=new CoreDistrict(); core.Plan(1987);
        var parking=new List<CoreAmenityLayout.Site>(); var fuel=new List<CoreAmenityLayout.Site>();
        var development=new List<CoreAmenityLayout.Site>(); var repurposed=new List<Rect>();
        CoreAmenityLayout.Select(core.Raster,core.Layout.Lots,1987,3,5,parking,fuel,development,core.Layout.Residential.Select(b=>b.Box),repurposed);
        CoreAmenityLayout.PickCourthouse(development,core.Layout.Territory);
        // Both northern quarters retain their territory but all their candidate
        // parcels are unavailable. Each must pair with a southern host instead.
        bool North(CoreQuarterId? q)=>q==CoreQuarterId.NorthLandward || q==CoreQuarterId.NorthRiverside;
        core.Layout.Residential.RemoveAll(b=>North(b.QuarterId));
        development.RemoveAll(s=>North(core.Layout.Territory.QuarterAt(s.Box.center)));
        var service=new CoreServicePlan(); service.Plan(core.Layout,core.Raster,development,1987,repurposed);
        Require(service.TotalPoliceCount==3,"failed first pair dropped the precinct quota");
        foreach(var q in new[]{CoreQuarterId.NorthLandward,CoreQuarterId.NorthRiverside})
            Require(service.Sites.Any(s=>s.Police && s.Serves.Contains(q)),"failed pair stranded a quarter");
    }
    static void ChecksPublicStreetAtOppositeCorner()
    {
        var r=new CoreRoads.Raster {X0=-15,Z0=-15,NX=26,NZ=26,Kinds=new CoreRoads.Kind[26,26]};
        // South driveway is equally good along the whole block; the public door
        // reaches its east street only at the last crop, not the first one.
        for(int i=0;i<26;i++) for(int j=0;j<26;j++)
            r.Kinds[i,j]=i>=23?CoreRoads.Kind.StreetNS:j<3?CoreRoads.Kind.StreetEW:CoreRoads.Kind.Block;
        Require(CoreServicePlan.TryFrontage(r,new Rect(0,0,100,100),ParkingEntrySide.South,40,45,
            out var crop,out _,true),"opposite-corner public street rejected");
        Require(crop.xMax==100,"precinct did not move to its public street");
    }
    static string Signature(CoreRegion region) => string.Join("|",region.Quarters.Select(q=>$"{q.District.Name}:{q.Slot.edge}:{q.Slot.seed}:{q.World}"));
    static float[] Box(Rect r)=>new[]{r.xMin,r.yMin,r.width,r.height};
    static object Fixture(CoreDistrict core,CoreRegion region,int seed)
    {
        var access=new List<object>();
        foreach(var c in region.Connections)
        {
            if(c.CityNode!=null) access.Add(new {edge=(int)c.Edge,outlying=false,point=new[]{c.CityFace.x,c.CityFace.z}});
            if(c.Quarter!=null && c.Via==null && region.TryPortal(c,out var p))
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
                if(other!=c && other.Edge==c.Edge && other.Via!=c.Quarter && c.Via!=other.Quarter) Require(Math.Abs(other.Across-c.Across)>=30f,"overlapping belt junctions");
            if(c.Quarter==null) continue;
            var edge=c.Edge;
            var belt=region.BeltBounds;
            var q=c.Quarter.World;
            if(c.Via!=null)
            {
                // the gate is reached along the works' artery: the line must run through the works
                var works=c.Via.World;
                Require(CoreRegion.Vertical(edge)?c.Across>works.xMin&&c.Across<works.xMax:c.Across>works.yMin&&c.Across<works.yMax,"port gate line misses the works it is reached through");
                continue;
            }
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
            Require(Math.Abs(Math.Min(a.z,b.z)-PortIndustryLayout.Frontage)<.01f,"works do not front the port's back street pavement");
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

            // the artery is a port gate road: the gate lays no approach of its own, the
            // artery's seaward mouth lines up with the gate, and the zone stays inside the
            // port frontage and clear of the other gate road
            var via=region.Connections.Single(c=>c.Via==q);
            Require(via.Quarter==port&&Math.Abs(via.Across-connection.Across)<.01f,"port gate is not on the works' artery line");
            var zone=(IndustrialDistrict)q.District;
            var mouths=IndustrialDistrict.SeawardStreets(zone.Raster,zone.SeawardCut);
            Require(mouths.Count==5,$"port zone has {mouths.Count} streets running out onto the port road, not 5");
            Require(zone.Layout.Roads.MainRoad.y-zone.Layout.Roads.MainRoad.x==15f,"port zone artery is not a 15 m street");
            Require(mouths.Exists(z=>Math.Abs(z-7.5f)<.01f),"port zone artery does not run out onto the port road");
            Require(Math.Abs(q.District.LocalBounds.xMax-zone.SeawardCut)<.01f,"port zone bounds keep the ring past the seaward cut");
            foreach(float crown in mouths)
            {
                var arrival=port.District.Frame.ToLocal(q.District.Frame.ToWorld(new Vector3(zone.SeawardCut,0,crown)));
                Require(arrival.x>port.District.LocalBounds.xMin+20f&&arrival.x<port.District.LocalBounds.xMax-20f,"a works street arrives beyond the port road");
                Require(Math.Abs(arrival.z-PortIndustryLayout.Frontage)<.01f,"a works street does not end at the port pavement");
            }
            Require(Math.Min(a.x,b.x)>=port.District.LocalBounds.xMin-.01f&&Math.Max(a.x,b.x)<=port.District.LocalBounds.xMax+.01f,"works overhang the port frontage");
            foreach(var other in region.Connections)
            {
                if(other==via||other.Edge!=via.Edge||other.Quarter==null||other.Quarter==q) continue;
                float lo=CoreRegion.Vertical(via.Edge)?box.xMin:box.yMin, hi=CoreRegion.Vertical(via.Edge)?box.xMax:box.yMax;
                Require(other.Across<lo-20f||other.Across>hi+20f,$"{other.Quarter.District.Name} gate road runs into the works");
            }
        }
    }
    static float Gap(Rect a,Rect b)
    {
        float x=Math.Max(0,Math.Max(a.xMin-b.xMax,b.xMin-a.xMax));
        float z=Math.Max(0,Math.Max(a.yMin-b.yMax,b.yMin-a.yMax));
        return (float)Math.Sqrt(x*x+z*z);
    }
}
