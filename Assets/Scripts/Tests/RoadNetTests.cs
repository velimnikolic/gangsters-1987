using System.Collections.Generic;
using UnityEngine;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>
    /// The block's four streets as a car sees them: a street that runs north-south is
    /// driven by exactly the same rules as one that runs east-west (its own coordinates
    /// - along it, across it - are all the driver ever plans in), the route round the
    /// block is the near way and not the far one, and a corner taken at a junction keeps
    /// the whole car on tarmac.
    ///
    /// That last one is the one worth a test: the swing from one lane into another has
    /// its belly nearest the inside kerb, and a radius chosen a little too generously
    /// puts a wheel on the pavement of the corner - geometry, not driving, so it can be
    /// checked here without a Play mode.
    ///
    /// Same discipline as the rest of this folder: plain statics, no UnityEngine.Object,
    /// nothing logged - load the built Assembly-CSharp.dll into a bare .NET host, call
    /// <see cref="Run"/> by reflection, read the returned list. Empty means passed.
    /// </summary>
    public static class RoadNetTests
    {
        // the crew demo's block: 100 x 70 m, a 6.5 m pavement and 5 m of carriageway
        // out to each centre line, the streets running 35 m past the corners
        const float Walk = 6.5f, Half = 5f;
        const float BlockX = 50f, BlockZ = 35f;
        const float WestX = -(BlockX + Walk + Half), EastX = BlockX + Walk + Half;
        const float SouthZ = -(BlockZ + Walk + Half), NorthZ = BlockZ + Walk + Half;
        const float EndX = EastX + Half + 35f, EndZ = NorthZ + Half + 35f;

        // a car's half width, and the air it wants off a kerb
        const float Body = 0.95f, Air = 0.05f;

        static RoadNet Block(out IRoadModel south, out IRoadModel north,
                             out IRoadModel west, out IRoadModel east)
        {
            var net = new RoadNet();
            south = new StraightStreetModel(SouthZ, -EndX, EndX);
            north = new StraightStreetModel(NorthZ, -EndX, EndX);
            west = StraightStreetModel.AlongZ(WestX, -EndZ, EndZ);
            east = StraightStreetModel.AlongZ(EastX, -EndZ, EndZ);
            net.Add(south);
            net.Add(north);
            net.Add(west);
            net.Add(east);
            return net;
        }

        public static List<string> Run()
        {
            var fails = new List<string>();
            FrameRoundTrips(fails);
            TrafficKeepsRight(fails);
            StreetsCrossWhereTheyShould(fails);
            RouteTakesTheNearWayRound(fails);
            RouteOnTheSameStreetIsNoTurn(fails);
            CornersStayOnTheTarmac(fails);
            return fails;
        }

        // ------------------------------------------------------------------ the frame

        static void FrameRoundTrips(List<string> fails)
        {
            var streets = new IRoadModel[]
            {
                new StraightStreetModel(SouthZ, -EndX, EndX),
                StraightStreetModel.AlongZ(WestX, -EndZ, EndZ),
            };
            foreach (var s in streets)
                for (float along = -60f; along <= 60f; along += 17f)
                    for (float across = -8f; across <= 8f; across += 3f)
                    {
                        var back = s.ToFrame(s.ToWorld(along, across, 0.5f));
                        if (Mathf.Abs(back.x - along) > 1e-3f || Mathf.Abs(back.z - across) > 1e-3f)
                            fails.Add("frame does not round-trip: " + along + "/" + across +
                                      " came back " + back.x + "/" + back.z);
                    }

            // the street that runs along world X keeps the world's own coordinates -
            // which is what makes every plan laid before there were corners still valid
            var xStreet = new StraightStreetModel(SouthZ, -EndX, EndX);
            var world = xStreet.ToWorld(12f, -3f, 0.25f);
            if ((world - new Vector3(12f, 0.25f, -3f)).sqrMagnitude > 1e-6f)
                fails.Add("a street along X no longer plans in world coordinates: " + world);
            if ((xStreet.DirToWorld(Vector3.right) - Vector3.right).sqrMagnitude > 1e-6f)
                fails.Add("'along' on a street laid along X is not +X");

            // and a direction with it: up the street is up the street either way round
            var zStreet = StraightStreetModel.AlongZ(WestX, -EndZ, EndZ);
            var up = zStreet.DirToWorld(Vector3.right);
            if ((up - Vector3.forward).sqrMagnitude > 1e-4f)
                fails.Add("a street laid along Z runs " + up + ", not +Z");
            var frameDir = zStreet.DirToFrame(Vector3.forward);
            if (Mathf.Abs(frameDir.x - 1f) > 1e-3f)
                fails.Add("+Z is not 'along' on a street laid along Z: " + frameDir);
        }

        // Right-hand traffic, on a street running either way: the lane for a car headed
        // "up" the street is on its right, and the kerb it parks at is further right still.
        static void TrafficKeepsRight(List<string> fails)
        {
            var south = new StraightStreetModel(SouthZ, -EndX, EndX);
            var eastbound = south.ToWorld(0f, south.LaneZ(1f), 0f);
            if (eastbound.z >= SouthZ)
                fails.Add("eastbound traffic is not in the south lane: z " + eastbound.z);
            var westbound = south.ToWorld(0f, south.LaneZ(-1f), 0f);
            if (westbound.z <= SouthZ)
                fails.Add("westbound traffic is not in the north lane: z " + westbound.z);

            var west = StraightStreetModel.AlongZ(WestX, -EndZ, EndZ);
            var northbound = west.ToWorld(0f, west.LaneZ(1f), 0f);
            if (northbound.x <= WestX)
                fails.Add("northbound traffic is not in the east lane: x " + northbound.x);
            var southbound = west.ToWorld(0f, west.LaneZ(-1f), 0f);
            if (southbound.x >= WestX)
                fails.Add("southbound traffic is not in the west lane: x " + southbound.x);

            // the kerb is on the same side as the lane, and further out
            foreach (var street in new IRoadModel[] { south, west })
                foreach (float dir in new[] { 1f, -1f })
                {
                    float lane = street.LaneZ(dir), kerb = street.KerbZ(dir, Body);
                    if (Mathf.Sign(kerb - street.CentreZ) != Mathf.Sign(lane - street.CentreZ) ||
                        Mathf.Abs(kerb - street.CentreZ) <= Mathf.Abs(lane - street.CentreZ))
                        fails.Add("the kerb is not outside the lane on its own side: " +
                                  lane + " / " + kerb);
                    if (Mathf.Abs(kerb - street.CentreZ) + Body > street.HalfRoad + 0.5f)
                        fails.Add("a car parked at the kerb hangs over it: " + kerb);
                }
        }

        static void StreetsCrossWhereTheyShould(List<string> fails)
        {
            var net = Block(out var south, out var north, out var west, out _);
            if (!net.Crossing(south, west, out var sw))
                fails.Add("the south street and the west street do not meet");
            else if (Mathf.Abs(sw.x - WestX) > 0.01f || Mathf.Abs(sw.z - SouthZ) > 0.01f)
                fails.Add("the south-west junction is at " + sw + ", not (" + WestX + ", " + SouthZ + ")");

            if (net.Crossing(south, north, out _))
                fails.Add("two streets running the same way were said to cross");

            // one that stops short of the other never meets it
            var stub = new StraightStreetModel(0f, -20f, 20f);
            var far = StraightStreetModel.AlongZ(EastX, -EndZ, EndZ);
            if (net.Crossing(stub, far, out _))
                fails.Add("a street that stops short of another was said to meet it");
        }

        // ------------------------------------------------------------------ the route

        static void RouteTakesTheNearWayRound(List<string> fails)
        {
            var net = Block(out var south, out _, out var west, out var east);

            // a car near the west end of the south street, sent to the north street:
            // round by the WEST street, which is the near way
            var at = new Vector3(-70f, 0f, SouthZ - 2.5f);
            var target = new Vector3(0f, 0f, NorthZ + 2f);
            if (!net.NextTurn(south, at, target, out var next, out var junction, out var waypoint))
                fails.Add("no route from the south street to the north street");
            else
            {
                if (!ReferenceEquals(next, west))
                    fails.Add("the route round the block went the far way about");
                if (Mathf.Abs(junction.x - WestX) > 0.01f || Mathf.Abs(junction.z - SouthZ) > 0.01f)
                    fails.Add("the route turns at " + junction + ", not the south-west junction");
                // and the place to make for once round is the NEXT junction, not the target
                if (Mathf.Abs(waypoint.x - WestX) > 0.01f || Mathf.Abs(waypoint.z - NorthZ) > 0.01f)
                    fails.Add("after the corner the car makes for " + waypoint +
                              ", not the north-west junction");
            }

            // the same car at the east end goes round the east street instead
            at = new Vector3(70f, 0f, SouthZ - 2.5f);
            if (!net.NextTurn(south, at, target, out next, out _, out _))
                fails.Add("no route from the east end of the south street");
            else if (!ReferenceEquals(next, east))
                fails.Add("from the east end the route still went round the west");
        }

        static void RouteOnTheSameStreetIsNoTurn(List<string> fails)
        {
            var net = Block(out var south, out _, out _, out _);
            var at = new Vector3(-70f, 0f, SouthZ - 2.5f);
            if (net.NextTurn(south, at, new Vector3(60f, 0f, SouthZ + 2f), out _, out _, out _))
                fails.Add("a point on the car's own street was said to need a turn");
            // a point on the pavement of that street is still that street's
            if (net.NextTurn(south, at, new Vector3(20f, 0f, SouthZ + Half + 3f), out _, out _, out _))
                fails.Add("a point on the pavement was routed off the street it fronts");
        }

        // ------------------------------------------------------------------ the corner

        // Every corner of the block, taken either way round: the swing from lane to lane
        // must keep the car on the carriageway of the street it is leaving or the one it
        // is taking (their junction box is where the two overlap) - a wheel over the
        // kerb of the corner is the one thing a turn may not do.
        static void CornersStayOnTheTarmac(List<string> fails)
        {
            var net = Block(out var south, out var north, out var west, out var east);
            var pairs = new[]
            {
                (a: south, b: west), (a: south, b: east),
                (a: north, b: west), (a: north, b: east),
            };
            foreach (var pair in pairs)
                foreach (float dirIn in new[] { 1f, -1f })
                    foreach (float dirOut in new[] { 1f, -1f })
                    {
                        if (!net.Crossing(pair.a, pair.b, out var junction)) continue;
                        RoadNet.CornerPoints(pair.a, dirIn, pair.b, dirOut, junction, 0f,
                            6f, 11f, out var mouth, out var exit);

                        // the mouth is behind the junction on the way in, the exit past it
                        // on the way out - a corner is never taken backwards
                        float mIn = pair.a.ToFrame(mouth).x, jIn = pair.a.ToFrame(junction).x;
                        if ((jIn - mIn) * dirIn <= 0f)
                            fails.Add("the turn begins the wrong side of the junction");
                        float eOut = pair.b.ToFrame(exit).x, jOut = pair.b.ToFrame(junction).x;
                        if ((eOut - jOut) * dirOut <= 0f)
                            fails.Add("the turn ends the wrong side of the junction");

                        // ...and every step of the swing is on one carriageway or the other
                        var dir = pair.a.DirToWorld(Vector3.right) * dirIn;
                        var path = new PathBuilder(pair.b, 0f).Corner(mouth, dir, exit)
                                                              .Build(14f, 3f, false);
                        float room = pair.a.HalfRoad - Body - Air;
                        foreach (var sample in path.Samples)
                        {
                            float offA = Mathf.Abs(pair.a.ToFrame(sample.P).z - pair.a.CentreZ);
                            float offB = Mathf.Abs(pair.b.ToFrame(sample.P).z - pair.b.CentreZ);
                            if (offA > room && offB > room)
                                fails.Add("the corner puts the car on the pavement at " +
                                          sample.P + " (" + offA.ToString("F2") + " / " +
                                          offB.ToString("F2") + " m off the two centre lines)");
                        }
                    }
        }
    }
}
