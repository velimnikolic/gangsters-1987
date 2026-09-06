using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEngine;

static class TrafficFlow
{
    public static void Run()
    {
        Distribution();
        Choices();
        foreach (float halfZ in new[] { 5f, 7.5f, 15f }) Corners(halfZ);
        foreach (int heading in new[] { 1, -1 })
            foreach (float dt in new[] { 1f / 30f, .2f })
                foreach (bool followerFirst in new[] { false, true })
                    FollowingTurn(heading, dt, followerFirst);
        foreach (int heading in new[] { 1, -1 }) TurnAdmission(heading);
        foreach (int heading in new[] { 1, -1 })
            foreach (float width in new[] { 5f, 7.5f })
                foreach (float halfLength in new[] { 2.3f, 3.8f }) ParkingTurn(heading, width, halfLength);
        foreach (bool mixed in new[] { false, true }) { BoundaryFlow(mixed); TerminalFlow(mixed); }
    }

    internal static void Body(RoadCar car, int index)
    {
        // Measured large-body envelope already used by the kerb regressions;
        // explicit axle ratios straddle the default sedan's steering threshold.
        if (index % 3 != 0) { car.HalfLen = 3.72353f; car.HalfWide = 1.28412f; }
        car.AxleBack = car.HalfLen * (index % 2 == 0 ? .7f : .6f);
    }

    static void Distribution()
    {
        var net = new LaneNet();
        foreach (float length in new[] { 60f, 120f, 240f, 480f })
            net.AddRoad(new Vector3(net.Roads.Count * 30, 0, 0), new Vector3(net.Roads.Count * 30, 0, length),
                5f, new[] { 2.5f }, 10f, null, null, true);
        net.Finish();
        var auxiliary = new RoadEdge { Length = 500f, Auxiliary = true };
        net.Edges.Add(auxiliary);
        int capacity = net.Edges.Where(e => !e.Auxiliary).Sum(e => (int)((e.Length - 24f) / 18f) + 1);
        foreach (int count in new[] { 0, 3, 25, 1000 })
        {
            var slots = TrafficDistribution.Place(net.Edges, count, 97);
            var again = TrafficDistribution.Place(net.Edges, count, 97);
            bool spacing = true, bounded = true;
            foreach (var lane in net.Edges)
            {
                var progress = slots.Where(s => s.Lane == lane).Select(s => s.Progress).OrderBy(s => s).ToArray();
                for (int i = 1; i < progress.Length; i++) spacing &= progress[i] - progress[i - 1] >= 17.99f;
                foreach (float s in progress) bounded &= s >= 12f && s <= lane.Length - 12f;
            }
            TrafficAdmission.Check(slots.Count == Math.Min(count, capacity) && spacing && bounded &&
                slots.All(s => !s.Lane.Auxiliary) && slots.SequenceEqual(again),
                $"placement count={count} actual={slots.Count} capacity={capacity} spacing={spacing} bounded={bounded} deterministic");
            if (count == 25)
            {
                int shortRoad = slots.Count(s => s.Lane.Road == net.Roads[0]);
                int longRoad = slots.Count(s => s.Lane.Road == net.Roads[3]);
                TrafficAdmission.Check(longRoad > shortRoad * 3 && slots.Any(s => s.Progress > s.Lane.Length * .6f),
                    $"placement follows capacity and fills lane lengths short={shortRoad} long={longRoad}");
            }
        }
        foreach (int h in new[] { 1, -1 })
        {
            var f = Street();
            var lane = f.road.LaneFor(h, h * 2.5f);
            Car(f.net, f.road, h, lane.RoadS(12f), 0f, DriverProfile.Traffic);
            float slot = TrafficDistribution.FreeSlot(lane);
            TrafficAdmission.Check(slot >= 30f, $"service spawn avoids scattered traffic h={h} progress={slot}");
            Car(f.net, f.road, h, lane.RoadS(slot), 0f, DriverProfile.Police);
            TrafficAdmission.Check(TrafficDistribution.FreeSlot(lane) > slot, $"service spawn sees previous patrol h={h}");
        }
    }

    static void Choices()
    {
        var f = Street();
        var a = f.net.AddNode(20, 240, 5, 5);
        var b = f.net.AddNode(40, 240, 5, 5);
        var c = f.net.AddNode(60, 240, 5, 5);
        RoadEdge Exit(RoadNode node, float x)
        {
            var road = f.net.AddRoad(new Vector3(x, 0, 0), new Vector3(x, 0, 240), 5f, new[] { 2.5f }, 10f, null, node, true);
            node.Outgoing.Add(new RoadEdge { Dir = Vector3.forward });
            return road.LaneFor(1, 2.5f);
        }
        var straight = Exit(a, 20); var left = Exit(b, 40); var right = Exit(c, 60);
        var lefts = new List<RoadEdge> { left }; var rights = new List<RoadEdge> { right };
        int rightCount = 0;
        for (int i = 0; i < 10000; i++)
            if (TrafficDistribution.Choose(null, lefts, rights, (i + .5f) / 10000f) == right) rightCount++;
        TrafficAdmission.Check(rightCount > 5300 && rightCount < 5800,
            $"edge junction normalizes missing straight right={rightCount}/10000 (old 8000)");
        for (int i = 0; i < 12; i++) Car(f.net, straight.Road, 1, 15f + i * 18f, 0f, DriverProfile.Traffic);
        int full = 0;
        for (int i = 0; i < 10000; i++)
            if (TrafficDistribution.Choose(straight, lefts, rights, (i + .5f) / 10000f) == straight) full++;
        TrafficAdmission.Check(full < 3000, $"wanderers avoid crowded exit selected={full}/10000");
        right.To = null;
        int terminal = 0;
        for (int i = 0; i < 10000; i++)
            if (TrafficDistribution.Choose(null, lefts, rights, (i + .5f) / 10000f) == right) terminal++;
        TrafficAdmission.Check(terminal < 1200, $"wanderers avoid terminal spur selected={terminal}/10000");
        for (int i = 0; i < 12; i++) Car(f.net, left.Road, 1, 15f + i * 18f, 0f, DriverProfile.Traffic);
        int choseSpur = 0;
        for (int i = 0; i < 1000; i++)
            if (TrafficDistribution.Choose(null, lefts, rights, (i + .5f) / 1000f) == right) choseSpur++;
        TrafficAdmission.Check(choseSpur > 0 && choseSpur < 300,
            $"crowded through exit retains majority over empty terminal spur: spur={choseSpur}/1000");
        TrafficAdmission.Check(TrafficDistribution.Choose(null, new List<RoadEdge>(), rights, .5f) == right,
            "only terminal exit remains usable");
        var seeded = TrafficDistribution.Place(new List<RoadEdge> { left, right }, 6, 93);
        TrafficAdmission.Check(seeded.Count == 6 && seeded.All(s => s.Lane == left), "initial traffic stays off terminal spur when through road exists");
        var spill = TrafficDistribution.Place(new List<RoadEdge> { left, right }, 20, 93);
        TrafficAdmission.Check(spill.Count == 20 && spill.Count(s => s.Lane == left) == 13 && spill.Count(s => s.Lane == right) == 7,
            "placement preserves population by using terminal capacity after through lanes fill");
    }

    static void BoundaryFlow(bool mixed)
    {
        foreach (int seed in new[] { 7, 37 })
        {
            TrafficAdmission.Reset();
            UnityEngine.Random.R = new System.Random(seed);
            var net = Program.Grid(new[] { -200f, -100f, 0f, 100f, 200f },
                new[] { -200f, -100f, 0f, 100f, 200f }, false);
            bool Boundary(RoadEdge e) => Math.Abs((e.Start.x + e.End.x) * .5f) > 190f ||
                Math.Abs((e.Start.z + e.End.z) * .5f) > 190f;
            var edges = net.Edges.Where(Boundary).ToList();
            var cars = new List<RoadCar>();
            foreach (var slot in TrafficDistribution.Place(edges, 32, seed))
            {
                var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
                if (mixed) Body(car, cars.Count);
                car.Spawn(slot.Lane, slot.Progress);
                cars.Add(car); StreetTraffic.Users.Add(car);
            }
            int overlaps = 0, interior = 0, samples = 0;
            for (int i = 0; i < 3600; i++)
            {
                TrafficAdmission.Tick(cars, 1f / 30f);
                if (i >= 1800 && i % 30 == 0)
                { samples += cars.Count; interior += cars.Count(car => car.Lane != null && !Boundary(car.Lane)); }
                for (int a = 0; a < cars.Count; a++)
                    for (int b = a + 1; b < cars.Count; b++)
                    {
                        var x = cars[a]; var y = cars[b];
                        if ((x.Position - y.Position).sqrMagnitude > 100f) continue;
                        if (RoadSpace.Overlap(x.Position, x.Forward, x.HalfLen, x.HalfWide,
                            y.Position, y.Forward, y.HalfLen, y.HalfWide, 0f, out _)) overlaps++;
                    }
            }
            float share = interior / (float)samples;
            TrafficAdmission.Check(cars.Count == 32 && share > .4f && overlaps == 0 && cars.All(car => car.TrafficRecoveries == 0 && !car.Gone),
                $"boundary traffic mixed={mixed} seed={seed}: mean interior={share:P1} (initial 0%) overlaps={overlaps} recoveries={cars.Sum(car => car.TrafficRecoveries)}");
        }
    }

    static void TerminalFlow(bool mixed)
    {
        TrafficAdmission.Reset();
        var trace = Environment.GetEnvironmentVariable("TRAFFIC_FLOW_TRACE");
        if (trace != null) DriveTrace.Open(trace);
        UnityEngine.Random.R = new System.Random(17);
        var net = Program.CrewRing(reach: 60f);
        var cars = new List<RoadCar>();
        foreach (var slot in TrafficDistribution.Place(net.Edges, 24, 91))
        {
            var car = new RoadCar { Net = net, Profile = DriverProfile.Traffic };
            if (mixed) Body(car, cars.Count);
            car.Spawn(slot.Lane, slot.Progress);
            cars.Add(car); StreetTraffic.Users.Add(car);
        }
        int terminal = 0, samples = 0, overlaps = 0;
        for (int frame = 0; frame < 7200; frame++)
        {
            DriveTrace.Now = frame / 30f;
            TrafficAdmission.Tick(cars, 1f / 30f);
            if (frame >= 3600 && frame % 30 == 0)
                foreach (var car in cars)
                {
                    samples++;
                    var road = car.Road ?? car.Via?.To.Road;
                    if (road != null && (road.NodeA.Incoming.Count == 1 || road.NodeB.Incoming.Count == 1)) terminal++;
                }
            for (int a = 0; a < cars.Count; a++)
                for (int b = a + 1; b < cars.Count; b++)
                {
                    var x = cars[a]; var y = cars[b];
                    if ((x.Position - y.Position).sqrMagnitude > 100f) continue;
                    if (RoadSpace.Overlap(x.Position, x.Forward, x.HalfLen, x.HalfWide,
                        y.Position, y.Forward, y.HalfLen, y.HalfWide, 0f, out _)) overlaps++;
                }
        }
        float share = terminal / (float)samples;
        if (trace != null) DriveTrace.Close();
        TrafficAdmission.Check(share > .1f && share < .55f && overlaps == 0 &&
            cars.All(car => car.TrafficRecoveries == 0 && !car.Gone),
            $"terminal-spur flow mixed={mixed}: mean terminal={share:P1} overlaps={overlaps} recoveries={cars.Sum(car => car.TrafficRecoveries)}");
    }

    static void Corners(float halfZ)
    {
        var net = new LaneNet();
        var node = net.AddNode(0f, 0f, 5f, halfZ);
        net.AddRoad(new Vector3(0, 0, -100), new Vector3(0, 0, -halfZ), 5f, new[] { 2.5f }, 10f, null, node, true);
        net.AddRoad(new Vector3(0, 0, halfZ), new Vector3(0, 0, 100), 5f, new[] { 2.5f }, 10f, node, null, true);
        net.AddRoad(new Vector3(-100, 0, 0), new Vector3(-5, 0, 0), 5f, new[] { 2.5f }, 10f, null, node, false);
        net.AddRoad(new Vector3(5, 0, 0), new Vector3(100, 0, 0), 5f, new[] { 2.5f }, 10f, node, null, false);
        net.Finish();
        foreach (var c in node.Connectors)
        {
            if (c.Kind == Turn.Straight || c.UTurn) continue;
            float sign = c.Kind == Turn.Right ? 1f : -1f;
            float overshoot = 0f, reverseYaw = 0f, last = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                c.Pose(c.Length * i / 1000f, out _, out var forward);
                float yaw = Vector3.SignedAngle(c.From.Dir, forward, Vector3.up) * sign;
                overshoot = Math.Max(overshoot, Math.Max(-yaw, yaw - 90f));
                reverseYaw = Math.Max(reverseYaw, last - yaw);
                last = yaw;
            }
            TrafficAdmission.Check(overshoot < .05f && reverseYaw < .05f,
                $"corner halfZ={halfZ} {c.From.Road.Index}->{c.To.Road.Index} {c.Kind}: overshoot={overshoot:F3}deg reverse={reverseYaw:F3}deg");
        }
    }

    static (LaneNet net, Carriageway road) Street(float halfRoad = 7.5f)
    {
        TrafficAdmission.Reset();
        var net = new LaneNet();
        var road = net.AddRoad(Vector3.zero, new Vector3(0, 0, 240), halfRoad, new[] { 2.5f }, 10f, null, null, true);
        net.Finish();
        return (net, road);
    }

    static KerbApproach.ObservedCar Car(LaneNet net, Carriageway road, int heading, float station, float speed,
        DriverProfile profile, float halfLength = 2.3f, float halfWidth = .95f)
    {
        var car = new KerbApproach.ObservedCar { Net = net, Profile = profile, HalfLen = halfLength, HalfWide = halfWidth };
        car.Spawn(road.LaneFor(heading, heading * 2.5f), heading > 0 ? station : road.Length - station);
        TrafficAdmission.Set(car, "<Speed>k__BackingField", speed);
        TrafficAdmission.Call(car, "UpdateOccupant");
        StreetTraffic.Users.Add(car);
        return car;
    }

    static void FollowingTurn(int heading, float dt, bool followerFirst)
    {
        var f = Street();
        var ours = Car(f.net, f.road, heading, 120f, 0f, DriverProfile.Gangster);
        var follower = Car(f.net, f.road, heading, 120f - heading * 18f, 6f, DriverProfile.Traffic);
        ours.Obstacle = follower;
        follower.Obstacle = ours;
        var cars = followerFirst ? new List<RoadCar> { follower, ours } : new List<RoadCar> { ours, follower };
        ours.GoTo(f.road.Pose(120f - heading * 35f, -heading * 2.5f), false);
        float began = -1f, completed = -1f;
        bool overlap = false;
        for (int i = 0; i < Math.Ceiling(12f / dt); i++)
        {
            TrafficAdmission.Tick(cars, dt);
            if (ours.Doing == RoadCar.Manoeuvre.UTurn && began < 0f) began = Time.time;
            if (ours.Heading == -heading && completed < 0f) completed = Time.time;
            overlap |= RoadSpace.Overlap(ours.Position, ours.Forward, ours.HalfLen, ours.HalfWide,
                follower.Position, follower.Forward, follower.HalfLen, follower.HalfWide, 0f, out _);
        }
        TrafficAdmission.Check(began >= 0f && began < 5f && completed >= 0f && completed < 8f && !overlap &&
            !ours.Overlapped && !follower.Overlapped && ours.Discontinuities == 0 && follower.Discontinuities == 0 &&
            ours.TrafficRecoveries == 0 && follower.TrafficRecoveries == 0,
            $"turn with follower h={heading} dt={dt:F3} followerFirst={followerFirst}: begin={began:F2}s complete={completed:F2}s " +
            $"overlap={overlap || ours.Overlapped || follower.Overlapped} jumps={ours.Discontinuities + follower.Discontinuities} refusal={ours.UTurnWhy}");
    }

    static void TurnAdmission(int heading)
    {
        foreach (string kind in new[] { "fast follower", "oncoming", "already passed", "on the arc" })
        {
            var f = Street();
            var ours = Car(f.net, f.road, heading, 120f, 0f, DriverProfile.Gangster);
            int otherHeading = kind == "fast follower" ? heading : -heading;
            float station = kind == "fast follower" ? 120f - heading * 12f :
                kind == "oncoming" ? 120f + heading * 30f :
                kind == "on the arc" ? 120f : 120f - heading * 30f;
            Car(f.net, f.road, otherHeading, station, kind == "on the arc" ? 0f : 12f, DriverProfile.Traffic);
            bool accepted = ours.TryUTurn();
            TrafficAdmission.Check(accepted == (kind == "already passed"),
                $"turn admission h={heading} {kind}: accepted={accepted} refusal={ours.UTurnWhy}");
        }
    }

    static void ParkingTurn(int heading, float halfRoad, float halfLength)
    {
        var f = Street(halfRoad);
        var ours = Car(f.net, f.road, heading, 120f, 0f, DriverProfile.Gangster, halfLength, halfLength > 3f ? 1.28f : .95f);
        var follower = Car(f.net, f.road, heading, 120f - heading * 18f, 6f, DriverProfile.Traffic);
        ours.Obstacle = follower; follower.Obstacle = ours;
        var goal = f.road.Pose(120f - heading * 35f, f.road.KerbD(-heading, ours.HalfWide));
        ours.GoTo(goal, true);
        var cars = new List<RoadCar> { ours, follower };
        float began = -1f;
        for (int i = 0; i < 150 && !ours.Parked; i++)
        {
            TrafficAdmission.Tick(cars, .2f);
            if (ours.Doing == RoadCar.Manoeuvre.UTurn && began < 0f) began = Time.time;
        }
        TrafficAdmission.Check(began >= 0f && began < 5f && ours.Parked && ours.AtGoal &&
            (ours.Position - goal).magnitude < .2f && ours.TrafficRecoveries == 0 &&
            !ours.Overlapped && !follower.Overlapped && ours.Discontinuities + follower.Discontinuities == 0,
            $"parking behind with follower h={heading} halfRoad={halfRoad} halfLength={halfLength}: " +
            $"begin={began:F2}s finish={Time.time:F2}s overlap={ours.Overlapped || follower.Overlapped} jumps={ours.Discontinuities + follower.Discontinuities}");
    }
}
