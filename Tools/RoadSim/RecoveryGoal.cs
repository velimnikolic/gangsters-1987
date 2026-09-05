using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class RecoveryGoalChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(RoadCar car, string key, object value) => typeof(RoadCar).GetField(key, Private).SetValue(car,value);
    public static void Run()
    {
        var previous = RoadCar.RecoveryVisibility;
        try {
            foreach (bool hidden in new[] { true, false })
            for (int repetition = 0; repetition < 5; repetition++) {
                var origin = new Vector3(repetition * 500f, 0f, 0f);
                var direction = Quaternion.Euler(0f,repetition * 72f,0f) * Vector3.forward;
                var net = new LaneNet();
                var road = net.AddRoad(origin,origin + direction * 140f,12.5f,new[]{2.5f},10f,null,null,true);
                net.Finish();
                var car = new RoadCar { Net=net,Profile=DriverProfile.Police,HalfLen=3.72f,HalfWide=1.28f };
                car.Spawn(road.LaneFor(1,2.5f),50f);
                Set(car,"<D>k__BackingField",9f);Set(car,"_pos",road.Pose(50f,9f));Set(car,"_fwd",direction);
                StreetTraffic.Users.Add(car);RoadCar.RecoveryVisibility=_=>!hidden;
                var goal = road.Pose(110f,2.5f);car.GoTo(goal,park:false);
                var blockers=new List<RoadCar>();
                for(float station=5;station<=135;station+=5) {
                    var block=new RoadCar{Net=net,HalfLen=2.3f,HalfWide=.95f};
                    block.Spawn(road.LaneFor(1,2.5f),station);Set(block,"<Parked>k__BackingField",true);
                    Set(block,"<Speed>k__BackingField",0f);StreetTraffic.Users.Add(block);blockers.Add(block);
                }
                Time.frameCount++; typeof(RoadSpace).GetMethod("Invalidate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var recovered=(bool)typeof(RoadCar).GetMethod("TryRecoverTraffic",Private).Invoke(car,new object[]{hidden});
                bool reversed=recovered && car.Heading==-1 && car.HasGoal;
                foreach(var block in blockers)block.Vanish();
                for(int frame=0;frame<9000 && car.HasGoal;frame++) {
                    Time.time += 1f/30f;Time.frameCount++;
                    RoadCarSimulation.Simulate(new[]{car},1f/30f);
                }
                bool passed=reversed && car.AtGoal && (car.Position-goal).magnitude<3f && car.Heading==1;
                Console.WriteLine((passed?"PASS ":"FAIL ")+"recovery into opposite lane then physical arrival at original goal; hidden="+hidden+" rotation="+(repetition*72)+" at="+car.Position+" state="+car.Describe());
                if(!passed)Environment.ExitCode=1;
                car.Vanish();
            }
        } finally {RoadCar.RecoveryVisibility=previous;}
    }
}
