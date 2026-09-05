using System;
using RoadDemo;
using UnityEngine;

static class ContactSeparation
{
    sealed class Body : IRoadUser
    {
        public Vector3 Position, Forward;
        public Vector3 RoadPosition => Position;
        public Vector3 RoadForward => Forward;
        public float RoadSpeed => 0f;
        public float HalfLength => 2.3f;
        public float HalfWidth => 1f;
    }

    public static void Run()
    {
        foreach (var angle in new[] { 0f, 72f, 144f, 216f, 288f })
        {
            StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear();
            Time.frameCount++;
            var forward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            var self = new Body { Forward = forward };
            var other = new Body { Forward = forward, Position = forward * 4.62f };
            StreetTraffic.Users.Add(self); StreetTraffic.Users.Add(other);
            var backwards = -forward * .1f;
            var moved = RoadSpace.Advance(self, Vector3.zero, backwards, forward, 2.3f, 1f, out var hit);
            bool separates = hit == null && (moved - backwards).sqrMagnitude < 1e-6f;
            RoadSpace.Advance(self, Vector3.zero, forward * .1f, forward, 2.3f, 1f, out hit);
            bool refusesWorse = hit != null;
            var rear = new Body { Forward = forward, Position = -forward * 4.7f };
            StreetTraffic.Users.Add(rear); Time.frameCount++;
            RoadSpace.Advance(self, Vector3.zero, backwards, forward, 2.3f, 1f, out hit);
            bool protectsRear = hit != null;
            bool passed = separates && refusesWorse && protectsRear;
            Console.WriteLine($"== contact separation angle={angle}: {(passed ? "PASS" : "FAIL")} away={separates} refusesWorse={refusesWorse} protectsRear={protectsRear}");
            if (!passed) Environment.ExitCode = 1;
        }
        StreetTraffic.Users.Clear(); Time.frameCount++;
    }
}
