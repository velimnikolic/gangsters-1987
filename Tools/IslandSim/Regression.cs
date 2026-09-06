using System;
using System.Linq;
using RoadDemo;
using UnityEngine;

static class Regression
{
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Check(RegionalExpresswayPlan plan,LaneNet net,IslandLandform land,int seed)
    {
        foreach(var deck in plan.Decks) foreach(float at in deck.Bridges)
        {
            float half=deck.ChannelHalf+95;
            var heading=deck.Line.DirAt(at);
            for(float offset=-half;offset<=half;offset+=4)
                Require(Vector3.Dot(heading,deck.Line.DirAt(Mathf.Repeat(at+offset,deck.Line.Length)))>.9999f,
                    $"seed {seed}: river bridge bends in plan");
            foreach(float side in new[]{-1f,1f})
            {
                float s=Mathf.Repeat(at+half*side,deck.Line.Length); var p=deck.Line.PointAt(s);
                Require(land.Height(p.x,p.z)>RoadDemoBuilder.WaterY,$"seed {seed}: bridge bank support {p.x},{p.z} floor={land.Height(p.x,p.z)} waterDistance={land.WaterDistance(p.x,p.z)} coast={land.Coast(p.x,p.z)}");
                Require(RegionalExpresswayView.PierFree(plan,p),$"seed {seed}: bridge bank support obstructs an access road");
                Require(Math.Abs(deck.Height(s)-deck.Height(at))<.01f,"river bridge deck is not level");
            }
        }
        int rounded=0;
        foreach(var ground in plan.Ground.Where(r=>r.Path!=null))
        {
            rounded++;
            Require(ground.A.Seam && ground.B.Seam,"collector bend introduced a traffic junction");
            foreach(var node in new[]{ground.A,ground.B})
                Require(node.Incoming.All(lane=>node.Connectors.Any(c=>c.From==lane)),"collector bend has a disconnected lane");
            for(float s=2;s<ground.Path.Length-2;s+=2)
                Require(ground.Path.RadiusAt(s)>StreetKit.OuterHalf,"collector pavement folds inside its bend");
        }
        Require(rounded>0,"no rounded collector bends");
        foreach(var node in plan.Junctions)
        {
            var arms=plan.Ground.Where(r=>r.A==node||r.B==node).ToArray();
            if(arms.Length!=2||plan.Ramps.Any(r=>r.A==node||r.B==node)) continue;
            var a=(arms[0].A==node?arms[0].To-arms[0].From:arms[0].From-arms[0].To).normalized;
            var b=(arms[1].A==node?arms[1].To-arms[1].From:arms[1].From-arms[1].To).normalized;
            Require(Vector3.Dot(a,b)<-.999f,$"seed {seed}: unresolved L-shaped collector at {node.X},{node.Z}, arms {arms[0].Line.Length}/{arms[1].Line.Length}, dot={Vector3.Dot(a,b)}");
        }
        foreach(float direction in new[]{-1f,1f}) foreach(float bank in new[]{-1f,1f})
        {
            int samples=0;
            float start=direction<0?land.UrbanRiver.yMin:land.UrbanRiver.yMax;
            float end=direction<0?land.Bounds.yMin:land.Bounds.yMax;
            for(float z=start+direction*10;direction*(z-end)<=0;z+=direction*10)
            {
                Require(land.RiverBanks(z,out var banks),"river quay has no channel");
                float x=(bank<0?banks.x:banks.y)+bank*IslandLandform.QuayWidth*.5f;
                if(land.Coast(x,z)<30) break;
                Require(land.Height(x,z)>RoadDemoBuilder.WaterY,$"seed {seed}: river quay has flooded ground at {x},{z}");
                samples++;
            }
            Require(samples>50,"river quay does not continue to the coast");
        }
        foreach(float aspect in new[]{.5625f,1f,1.6f,1.7777778f,2.4f})
        {
            var view=TurfMapSurvey.FitToPlate(new Rect(land.Bounds.xMin-70,land.Bounds.yMin-70,land.Bounds.width+140,land.Bounds.height+140));
            float boom=TurfRelief.Ceiling(view,1000*aspect,1000,1.25f);
            float height=boom*DemoCamera.BoomToMetres;
            Require(height>=land.Bounds.height && height*aspect>=land.Bounds.width,"regional map zoom cannot show even the full island");
        }
        Require(TurfRelief.DetailRecession(1000,180,8000)>.99f,"street names remain full size at regional zoom");
        Require(TurfRelief.DetailRecession(180,180,8000)==0f,"close street labels lost their size");
        Require(TurfRelief.DetailRecession(260,180,260)==1f,"small-scene lettering zoom changed");
        const System.Reflection.BindingFlags constants=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        float core=(float)typeof(TurfMapHud).GetField("CoreUnits",constants).GetRawConstantValue();
        float pick=(float)typeof(TurfMapHud).GetField("PickRadius",constants).GetRawConstantValue();
        float indicator=(float)typeof(TurfMapHud).GetField("IndicatorScale",constants).GetRawConstantValue();
        Require(core*TurfPlate.S*indicator>=5f && core*indicator>=pick*.95f,
            "crew glyph shrinks below a readable dot or its authored pick tolerance");
        if(seed!=1) return;
        foreach(int columns in new[]{3,4}) foreach(int rows in new[]{3,4})
        {
            var slot=new DistrictSlot {seed=seed,sizeAcross=columns,sizeDeep=rows};
            var small=SuburbDemo.SuburbDistrict.ForCity(slot);
            var large=SuburbDemo.SuburbDistrict.ForCity(slot,2f);
            foreach(var suburb in new[]{small,large})
            {
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(SuburbDemo.SuburbDistrict).GetField("_rng",flags).SetValue(suburb,new System.Random(seed));
                // PlanLines is the actual managed lattice/mask planner; loading prefabs
                // and building streets are deliberately outside this offline check.
                typeof(SuburbDemo.SuburbDistrict).GetMethod("PlanLines",flags).Invoke(suburb,null);
            }
            Require(Math.Abs(large.MapWidth*large.MapHeight/(small.MapWidth*small.MapHeight)-2f)<.001f,
                "actual suburban footprint did not double");
        }
        var field=new TurfHeightField(new Rect(0,0,600,400),(x,z)=>100+x*.2f+z*.1f,10000);
        Require(field.SampleCount<=10000,"terrain survey sample budget exceeded");
        Require(!TurfRelief.At(field,200,200,2,TurfInk.Land).Equals(TurfInk.Land),"elevation does not affect map ink");
        var curve=plan.Decks[0].Line;
        var projection=new TurfProjection(land.Bounds);
        var mask=new byte[TurfPlate.RW*TurfPlate.RH]; var count=new byte[mask.Length]; var major=new byte[mask.Length];
        var geometry=new TurfRoadGeometry(); geometry.Collect(net); geometry.Ink(projection,mask,count,major);
        int hits=0,total=0;
        for(float s=0;s<curve.Length;s+=10)
        {
            var p=projection.ToPlan(curve.PointAt(s))*TurfPlate.S;
            int x=(int)p.x,y=(int)p.y;
            if(x<1||x>=TurfPlate.RW-1||y<1||y>=TurfPlate.RH-1) continue;
            total++;
            if(mask[y*TurfPlate.RW+x]!=0||mask[y*TurfPlate.RW+x+1]!=0||mask[(y+1)*TurfPlate.RW+x]!=0) hits++;
        }
        Require(hits>total*.98f,$"curved motorway missing from turf map ({hits}/{total})");
        foreach(int yaw in new[]{0,90,180,270})
        {
            var street=HarborDemo.HarborStreet.Build(DistrictFrame.At(0,0,yaw),new[]{-300f,0f,240f,480f},-7.5f);
            street.Finish();
            foreach(var road in street.Roads)
                Require(road.Lanes.Count==2 && road.ParkingA && road.ParkingB && road.HalfRoad==StreetKit.StreetHalf,
                    "port public road lacks paired lanes or loading kerbs");
            foreach(var destination in street.Edges)
            {
                var routes=street.RouteToward(destination);
                Require(street.Edges.All(source=>source==destination||routes.ContainsKey(source)),"port truck cannot return from its loading call");
            }
        }
    }
}
