using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEngine;

// RoadSim does not otherwise need area selection. This stand-in is Unity's half-open rect contract.
namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x,float y,float width,float height) { this.x=x;this.y=y;this.width=width;this.height=height; }
        public bool Contains(Vector2 p) => p.x>=x && p.x<x+width && p.y>=y && p.y<y+height;
    }
}

class FreightCheck
{
    static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
    class Body : IRoadUser
    {
        public Vector3 Position, Forward=Vector3.right;
        public float Speed;
        public Vector3 RoadPosition=>Position;
        public Vector3 RoadForward=>Forward;
        public float RoadSpeed=>Speed;
        public float HalfLength=>4f;
        public float HalfWidth=>1.3f;
    }
    static LaneNet Street()
    {
        var net=new LaneNet();
        for(int i=0;i<4;i++) net.AddNode(i*400,0,7.5f,7.5f);
        for(int i=0;i<3;i++) net.AddRoad(new Vector3(i*400+7.5f,0,0),new Vector3((i+1)*400-7.5f,0,0),
            7.5f,new[]{2.5f},9f,net.Nodes[i],net.Nodes[i+1],false);
        net.Finish(); return net;
    }
    static void Main()
    {
        var progress=new RoadProgress(90,1);
        progress.Reset(Vector3.zero);
        for(int i=0;i<200;i++) Require(!progress.Stalled(1,Vector3.zero,true),"planned loading wait was treated as a blocked trip");
        for(int i=0;i<90;i++) Require(!progress.Stalled(1,Vector3.zero),"blocked trip expired too soon");
        Require(progress.Stalled(1,Vector3.zero),"blocked trip did not expire");
        progress.Reset(Vector3.zero);
        for(int i=1;i<200;i++) Require(!progress.Stalled(1,new Vector3(i,0,0)),"moving truck was retired as stalled");
        Console.WriteLine("PASS bounded progress watchdog, planned loading and moving-trip exemption");
        var net=Street(); var a=net.Roads[0]; var b=net.Roads[1];
        var lorry=new Body {Position=a.Pose(80,2.5f),Forward=a.Axis,Speed=5};
        var body=new RoadBody(lorry); body.Sync(net,.1f); body.Sync(net,.1f);
        Require(StreetTraffic.Users.Count==1 && a.Occupants.Count==1,"duplicate external registration");
        var occ=a.Occupants.Single(); Require(occ.Vel>4.9f && !occ.Parked,"moving truck looks like a parked prop");
        lorry.Position=b.Pose(50,2.5f); lorry.Speed=2; body.Sync(net,.1f);
        Require(a.Occupants.Count==0 && b.Occupants.Count==1 && occ.Slowing>20,"moving truck leaves a stale road claim");
        var car=new RoadCar {Net=net,HalfLen=3.7f,HalfWide=1.3f,Profile=DriverProfile.Traffic};
        car.PlaceAt(b.Pose(70,2.5f),b.Axis); StreetTraffic.Users.Add(car);
        Require(body.SpeedLimit(3.5f,2.5f,10f)<9,"external driver ignores ordinary traffic");
        car.Slid(b.Pose(120,2.5f)); RoadSpace.Invalidate();
        Require(body.SpeedLimit(3.5f,2.5f,10f)==10f,"clear traffic was incorrectly capped");
        car.Slid(b.Pose(70,2.5f)); RoadSpace.Invalidate();
        Require(RoadSpace.Inside(null,lorry.Position,lorry.Forward,4,1.3f,out _) == lorry,"truck is invisible at admission");
        var reached=RoadSpace.Advance(lorry,lorry.Position,car.Position,lorry.Forward,4,1.3f,out var hit);
        Require(hit==car && !RoadSpace.Overlap(reached,lorry.Forward,4,1.3f,car.Position,car.Forward,car.HalfLen,car.HalfWide,0,out _),"truck swept through road traffic");
        lorry.Position=new Vector3(500,0,100); body.Sync(net,.1f);
        Require(b.Occupants.All(o=>o.Who!=lorry) && StreetTraffic.Users.Contains(lorry),"off-graph yard body disappeared or left a lane claim");
        body.Dispose(); body.Dispose(); car.Despawn(); StreetTraffic.Users.Clear();
        Require(net.Roads.All(r=>r.Occupants.Count==0),"external truck cleanup leaves a ghost");
        Console.WriteLine("PASS external road-body admission, moving claim, shared clearance, off-road visibility and repeated cleanup");

        var home=new Rect(0,-20,390,40); var port=new Rect(805,-20,400,40);
        var stops=IndustrialFreight.Stops(net,port);
        Require(stops.Count>=14 && stops.Select(s=>s.Position).Distinct().Count()==stops.Count,"insufficient distinct port stops for seven estates");
        car=new RoadCar {Net=net,HalfLen=3.7f,HalfWide=1.3f,Profile=DriverProfile.Traffic};
        car.PlaceAt(a.Pose(80,2.5f),a.Axis); StreetTraffic.Users.Add(car);
        var claims=new HashSet<Vector3>();
        var job=IndustrialFreight.TryCreate(car,net,home,port,0,claims);
        Require(job!=null,"reachable freight order rejected");
        job.Tick(3); Require(car.HasGoal,"freight did not start");
        car.Halt(true); job.Tick(1);
        Require(job.CompletedCalls==0,"halt away from port counted as a delivery");
        job.Tick(9); Require(car.HasGoal,"interrupted freight was not retried");
        var originalClaim=claims.Single();
        for(int retry=0;retry<3;retry++) { car.Halt(true); job.Tick(1); job.Tick(9); }
        Require(claims.Count==1 && !claims.Contains(originalClaim),"repeated failed delivery did not select another destination");
        for(int i=0;i<130;i++) job.Tick(1);
        Require(job.CompletedCalls==0,"stalled vehicle counted a call");
        car.StandDerelict(); job.Tick(60); job.Tick(60);
        Require(job.CompletedCalls==0 && !car.HasGoal,"derelict truck was dispatched again");
        car.Despawn(); StreetTraffic.Users.Clear();
        Console.WriteLine("PASS distributed stops, admission, interruption retry, stalled/derelict completion rejection");

        Require(claims.Count==0,"derelict freight retained its destination claim");
        var cars=new List<RoadCar>(); var jobs=new List<IndustrialFreight>();
        for(int i=0;i<4;i++)
        {
            var vehicle=new RoadCar {Net=net,HalfLen=3.7f,HalfWide=1.3f,Profile=DriverProfile.Traffic};
            vehicle.PlaceAt(a.Pose(45+i*50,2.5f),a.Axis); StreetTraffic.Users.Add(vehicle); cars.Add(vehicle);
            var call=IndustrialFreight.TryCreate(vehicle,net,home,port,i,claims); Require(call!=null,"distributed trip rejected"); jobs.Add(call);
        }
        int overlaps=0;
        for(int step=0;step<12000 && jobs.Any(j=>j.CompletedCalls<2);step++)
        {
            const float dt=.1f; Time.time+=dt;Time.frameCount++;
            foreach(var call in jobs) call.Tick(dt);
            RoadCarSimulation.Simulate(cars,dt);
            for(int i=0;i<cars.Count;i++) for(int j=i+1;j<cars.Count;j++)
                if(RoadSpace.Overlap(cars[i].Position,cars[i].Forward,3.7f,1.3f,cars[j].Position,cars[j].Forward,3.7f,1.3f,0,out _)) overlaps++;
        }
        Console.WriteLine("Freight calls: "+string.Join(",",jobs.Select(j=>j.CompletedCalls))+"; overlaps="+overlaps);
        foreach(var vehicle in cars) Console.WriteLine(vehicle.Position+" "+vehicle.DoingLine+" "+vehicle.Why);
        Require(jobs.All(j=>j.CompletedCalls>=2) && overlaps==0,"physical estate-port-return calls did not complete cleanly");
        foreach(var call in jobs) { call.Dispose(); call.Dispose(); }
        Require(claims.Count==0,"freight cleanup leaked destination claims");
        foreach(var vehicle in cars) vehicle.Despawn(); StreetTraffic.Users.Clear();
        Require(net.Roads.All(r=>r.Occupants.Count==0),"freight cleanup leaked road claims");
        Console.WriteLine("PASSED freight model. No scene, prefab, rendering or Unity Play verdict.");
    }
}
