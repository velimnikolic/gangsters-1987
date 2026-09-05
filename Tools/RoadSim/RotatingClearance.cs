// Replays the exact connector and vehicle poses from the live 140259-bail/1 gridlock.
using System;
using RoadDemo;
using UnityEngine;
static class RotatingClearance
{
    sealed class Body : IRoadUser
    {
        public Vector3 Position, Forward; public float HalfLen, HalfWide;
        public Vector3 RoadPosition => Position; public Vector3 RoadForward => Forward;
        public float RoadSpeed => 0; public float HalfLength => HalfLen; public float HalfWidth => HalfWide;
    }
    public static void Run()
    {
        foreach (float angle in new[] { 0f, 72f, 144f, 216f, 288f })
        {
            StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear(); Time.frameCount++;
            var rotation = Quaternion.Euler(0, angle, 0);
            var origin = new Vector3(394.107483f, 0.0f, 361.378f);
            Vector3 Rot(Vector3 p) => rotation * (p - origin);
            var self = new Body { Position = Vector3.zero, Forward = rotation * new Vector3(-0.297910064f, 0.0f, -0.954593956f), HalfLen = 3.723523f, HalfWide = 1.28414917f };
            var obstacle = new Body { Position = Rot(new Vector3(388.7025f, 0.0f, 356.6378f)), Forward = rotation * new Vector3(1.0f, 0.0f, 0.0f), HalfLen = 3.723584f, HalfWide = 1.28414917f };
            StreetTraffic.Users.Add(self); StreetTraffic.Users.Add(obstacle);
            var via = new Connector { Length = 12.8508263f, Pts = new[] { new Vector3(395.0f, 0.0f, 370.0f), new Vector3(395.0f, 0.0f, 369.5f), new Vector3(395.0f, 0.0f, 369.0f), new Vector3(395.0f, 0.0f, 368.5f), new Vector3(395.0f, 0.0f, 368.0f), new Vector3(395.0f, 0.0f, 367.5f), new Vector3(395.0f, 0.0f, 367.0f), new Vector3(395.0f, 0.0f, 366.5f), new Vector3(395.0f, 0.0f, 366.0f), new Vector3(395.0f, 0.0f, 365.5f), new Vector3(395.0f, 0.0f, 365.0f), new Vector3(394.975922f, 0.0f, 364.509918f), new Vector3(394.903931f, 0.0f, 364.024536f), new Vector3(394.7847f, 0.0f, 363.548584f), new Vector3(394.6194f, 0.0f, 363.086578f), new Vector3(394.4096f, 0.0f, 362.643f), new Vector3(394.157349f, 0.0f, 362.222137f), new Vector3(393.865051f, 0.0f, 361.828033f), new Vector3(393.535522f, 0.0f, 361.464478f), new Vector3(393.171967f, 0.0f, 361.134949f), new Vector3(392.777863f, 0.0f, 360.842651f), new Vector3(392.357f, 0.0f, 360.5904f), new Vector3(391.913422f, 0.0f, 360.3806f), new Vector3(391.451416f, 0.0f, 360.2153f), new Vector3(390.975464f, 0.0f, 360.096069f), new Vector3(390.490082f, 0.0f, 360.024078f), new Vector3(390.0f, 0.0f, 360.0f) }, Tan = new[] { new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(-0.0243123882f, 0.0f, -0.9997044f), new Vector3(-0.09801161f, 0.0f, -0.995185256f), new Vector3(-0.195092171f, 0.0f, -0.980784953f), new Vector3(-0.2903029f, 0.0f, -0.9569348f), new Vector3(-0.3826735f, 0.0f, -0.9238836f), new Vector3(-0.471383125f, 0.0f, -0.881928563f), new Vector3(-0.555576444f, 0.0f, -0.8314654f), new Vector3(-0.63441205f, 0.0f, -0.772995055f), new Vector3(-0.707106769f, 0.0f, -0.707106769f), new Vector3(-0.772995055f, 0.0f, -0.63441205f), new Vector3(-0.8314654f, 0.0f, -0.555576444f), new Vector3(-0.881928563f, 0.0f, -0.471383125f), new Vector3(-0.9238836f, 0.0f, -0.3826735f), new Vector3(-0.9569348f, 0.0f, -0.2903029f), new Vector3(-0.980784953f, 0.0f, -0.195092171f), new Vector3(-0.995185256f, 0.0f, -0.09801161f), new Vector3(-1.0f, 0.0f, 0.0f) }, Cum = new[] { 0.0f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 5.490673f, 5.98136473f, 6.47202444f, 6.96271563f, 7.45339251f, 7.94407f, 8.434738f, 8.925413f, 9.416088f, 9.906756f, 10.3974333f, 10.88811f, 11.3788013f, 11.8694611f, 12.3601532f, 12.8508263f } };
            for (int i = 0; i < via.Pts.Length; i++) { via.Pts[i] = Rot(via.Pts[i]); via.Tan[i] = rotation * via.Tan[i]; }
            float s = 8.745876f; float axle = 2.2341138f; bool first = false; int refused = 0, overlaps = 0;
            for (int i = 0; i < 120 && s > 0; i++)
            {
                float next = Math.Max(0, s - 2.5f / 30f); float sa = next - axle; Vector3 a, f;
                if (sa >= 0) via.Pose(sa, out a, out f); else { f = rotation * Vector3.back; a = via.Pts[0] + f * sa; }
                var to = a + f * axle;
                var moved = RoadSpace.Advance(self, self.Position, to, f, self.HalfLen, self.HalfWide, out var hit);
                if (i == 0) first = hit == null;
                if (hit != null) refused++; else { s = next; self.Position = moved; self.Forward = f; }
                if (RoadSpace.Overlap(self.Position, self.Forward, self.HalfLen, self.HalfWide, obstacle.Position, obstacle.Forward, obstacle.HalfLen, obstacle.HalfWide, 0, out _)) overlaps++;
                Time.frameCount++;
            }
            bool replay = first && s < .01f && overlaps == 0;
            Console.WriteLine($"reverse turn angle={angle}: {(replay ? "PASS" : "FAIL")} first={first} remaining={s:F3} refused={refused} overlaps={overlaps}");
            if (!replay) Environment.ExitCode = 1;
            StreetTraffic.Users.Clear(); Time.frameCount++;
            self = new Body { Position = Vector3.zero, Forward = rotation * Vector3.forward, HalfLen = 3.72f, HalfWide = 1.28f };
            obstacle = new Body { Position = rotation * new Vector3(2.5f, 0, 2.5f), Forward = rotation * Vector3.forward, HalfLen = .2f, HalfWide = .2f };
            StreetTraffic.Users.Add(self); StreetTraffic.Users.Add(obstacle);
            RoadSpace.Advance(self, self.Position, self.Position, rotation * Vector3.right, self.HalfLen, self.HalfWide, out var rotationHit);
            bool guarded = rotationHit != null; Console.WriteLine($"rotation sweep angle={angle}: {(guarded ? "PASS" : "FAIL")}");
            if (!guarded) Environment.ExitCode = 1;
        }
    }
}
