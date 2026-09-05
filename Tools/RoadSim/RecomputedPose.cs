using System;
using System.Reflection;
using RoadDemo;
using UnityEngine;
static class RecomputedPose {

 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(RoadCar c,string field,object value)=>typeof(RoadCar).GetField(field,Flags).SetValue(c,value);
 static void Place(RoadCar c)=>typeof(RoadCar).GetMethod("Place",Flags).Invoke(c,new object[]{.033f,c.S,c.D});
 public static void Run(){
 foreach(float angle in new[]{0f,72f,144f,216f,288f}){
  StreetTraffic.Users.Clear();StreetTraffic.Bodies.Clear();StreetTraffic.Walkers.Clear();Time.frameCount++;
  var q=Quaternion.Euler(0,angle,0);Vector3 Rot(float x,float z)=>q*new Vector3(x,0,z);
  var net=new LaneNet();var road=net.AddRoad(Rot(80,-200),Rot(80,200),15,new[]{2.5f},10,null,null,true);net.Finish();
  var car=new RoadCar{Net=net,HalfLen=3.72f,HalfWide=1.28f,Profile=DriverProfile.Police};
  car.Spawn(road.LaneFor(-1,-2.5f),113.4f);Set(car,"<S>k__BackingField",286.6f);Set(car,"<D>k__BackingField",-4.9f);
  var old=Rot(75.5f,86.96f);var forward=Rot(-.54f,-.84f).normalized;
  Set(car,"_pos",old);Set(car,"_fwd",forward);
  var other=new RoadCar{Net=net,HalfLen=3.72f,HalfWide=1.28f};other.Spawn(road.LaneFor(-1,-2.5f),118.48f);
  Set(other,"_pos",Rot(77.5f,81.52f));Set(other,"_fwd",Rot(0,-1));
  StreetTraffic.Users.Add(car);StreetTraffic.Users.Add(other);
  bool before=RoadSpace.Overlap(car.Position,car.Forward,car.HalfLen,car.HalfWide,other.Position,other.Forward,other.HalfLen,other.HalfWide,0,out _);
  for(int i=0;i<10;i++){Time.frameCount++;Place(car);}
  bool overlap=RoadSpace.Overlap(car.Position,car.Forward,car.HalfLen,car.HalfWide,other.Position,other.Forward,other.HalfLen,other.HalfWide,0,out _);
  float drift=(car.Position-old).magnitude, yaw=Vector3.Angle(car.Forward,forward);
  other.Vanish();Time.frameCount++;Place(car);
  bool resumed=Vector3.Angle(car.Forward,Rot(0,-1))<.01f;
  bool ok=!before&&!overlap&&drift<.001f&&yaw<.01f&&resumed;
  Console.WriteLine($"rollback pose {angle}: {(ok?"PASS":"FAIL")} before={before} overlap={overlap} drift={drift:F4} yaw={yaw:F2} resumed={resumed}");
  if(!ok)Environment.ExitCode=1;car.Vanish();
 }
 }
}
