using RoadDemo;
using UnityEngine;
namespace LivingCity.Tests
{
    public static partial class MiniCoreRegressionTests
    {
        static void BroadPoliceChallengePoint()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var suspect = fixture.Man();
                var origin = suspect.Tf.position;
                var turn = Quaternion.Euler(0, repetition * 72f, 0);
                var forward = turn * Vector3.forward;
                var side = turn * Vector3.right;
                var lead = fixture.Man(); lead.Tf.position = origin + forward * 4.8f;
                var unit = fixture.Unit(lead); unit.Faction = StreetAlarm.PoliceFaction;
                var beat = new PoliceBeat(crews, unit, 1, null, null, null, Vector2.one, 0f);
                var wanted = origin + forward * 3.2f;
                var plan = new SidewalkPlan();
                plan.Take(new SidewalkPlan.Box { C = new Vector2(wanted.x, wanted.z), H = new Vector2(1.2f, 1.2f),
                    Ax = new Vector2(side.x, side.z), Az = new Vector2(forward.x, forward.z), Solid = true });
                WalkObstacles.RegisterPlan(plan);
                try
                {
                    beat.Challenge(suspect);
                    Require(lead.HasOrder, "blocked challenge point left the patrol idle outside arrest reach");
                    Require(!WalkObstacles.Standing(lead.OrderDestination, WalkRoute.ClearanceRadius) &&
                        (lead.OrderDestination - origin).magnitude < 4.6f,
                        "challenge destination is occupied or outside actual question reach");
                    for (int step = 0; step < 600 && !beat.StoodOver; step++) lead.TickCrew(.033f);
                    Require(beat.StoodOver, "patrol did not physically close the last gap to ask its question");
                }
                finally { WalkObstacles.UnregisterPlan(plan); }
            }
        }


    }
}
