using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;
static class ArcMotion
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(RoadCar c,string n)=>typeof(RoadCar).GetField(n,Flags).GetValue(c);
 static void Set(RoadCar c,string n,object v)=>typeof(RoadCar).GetField(n,Flags).SetValue(c,v);
 static void Reset(){StreetTraffic.Users.Clear();StreetTraffic.Bodies.Clear();StreetTraffic.Walkers.Clear();Time.frameCount++;}
 static RoadCar Make(LaneNet net,Carriageway road,int heading,float s,float d){var c=new RoadCar{Net=net,Profile=DriverProfile.Police,HalfLen=3.72353f,HalfWide=1.28412f};c.Spawn(road.LaneFor(heading,d),heading>0?s:road.Length-s);Set(c,"<D>k__BackingField",d);Set(c,"_pos",road.Pose(s,d));Set(c,"<Speed>k__BackingField",0f);StreetTraffic.Users.Add(c);return c;}
 static LaneNet Net(float angle,out Carriageway road){var q=Quaternion.Euler(0,angle,0);var net=new LaneNet();road=net.AddRoad(q*new Vector3(0,0,0),q*new Vector3(0,0,100),7.5f,new[]{2.5f},10,null,null,true);net.Finish();return net;}
 static void Check(bool ok,string text){Console.WriteLine((ok?"PASS ":"FAIL ")+text);if(!ok)Environment.ExitCode=1;}
 public static void Run(){
  foreach(float angle in new[]{0f,72f,144f,216f,288f}){
   Reset();var net=Net(angle,out var road);var c=Make(net,road,1,14.1321812f,2.5f);var parked=Make(net,road,-1,11.196747f,-6.5959f);Set(parked,"<Parked>k__BackingField",true);
   bool accepted=c.TryUTurn();Check(!accepted,$"arc parked corner {angle}: accepted={accepted} reason={c.UTurnWhy}");c.Vanish();parked.Vanish();
  }
  foreach(float dt in new[]{.033f,.05f,.2f,.4f,.8f}){
   Reset();var net=Net(0,out var road);var c=Make(net,road,1,14.1321812f,2.5f);bool began=c.TryUTurn();var parked=Make(net,road,-1,11.196747f,-6.5959f);Set(parked,"<Parked>k__BackingField",true);float drift=0,yaw=0;bool collided=false;var cars=new List<RoadCar>{c};
   for(int i=0;i<Math.Ceiling(12f/dt);i++){
    Time.time+=dt;Time.frameCount++;RoadCarSimulation.Simulate(cars,dt);
    var a=new object[]{null,null};typeof(RoadCar).GetMethod("Pose",Flags).Invoke(c,a);drift=Math.Max(drift,((Vector3)a[0]-c.Position).magnitude);yaw=Math.Max(yaw,Vector3.Angle((Vector3)a[1],c.Forward));
    collided|=RoadSpace.Overlap(c.Position,c.Forward,c.HalfLen,c.HalfWide,parked.Position,parked.Forward,parked.HalfLen,parked.HalfWide,0,out _);
   }
   Check(began&&drift<.01f&&yaw<.1f&&!collided,$"blocked arc dt={dt}: drift={drift:F3} yaw={yaw:F2} overlap={collided} state={Get(c,"_man")} heading={c.Heading}");
   c.Vanish();parked.Vanish();
  }
  foreach(float dt in new[]{.033f,.05f,.2f,.4f,.8f}){
   Reset();var net=Net(0,out var road);var c=Make(net,road,1,40,2.5f);bool began=c.TryUTurn();float jump=0;var cars=new List<RoadCar>{c};
   for(int i=0;i<Math.Ceiling(15/dt)&&c.Heading==1;i++){var f=c.Forward;Time.time+=dt;Time.frameCount++;RoadCarSimulation.Simulate(cars,dt);jump=Math.Max(jump,Vector3.Angle(f,c.Forward));}
   Check(began&&c.Heading==-1&&jump<Math.Max(3f,dt*130f),$"clear arc dt={dt}: heading={c.Heading} yawStep={jump:F2}");c.Vanish();
  }
 }
}

