using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEngine;

// Regression coverage for body (not only heading) overshoot and orders during a crossing.
static class TurnRouting
{
    public static void Run()
    {
        BodyCorners();
        Routes();
        Parking();
    }

    public static void Routes()
    {
        foreach (bool park in new[] { false, true }) CrossingOrder(park);
        LocalOrder();
        CancelledAdmission();
        WaitingTurn();
        LongPreview(149);
        LongPreview(999);
        LongPreview(999, 500f);
        CyclicPreview();
    }

    static void Parking()
    {
        foreach (float length in new[] { 2.3f, 3.8f })
            foreach (float axleRatio in new[] { .6f, .7f })
            foreach (Turn turn in new[] { Turn.Left, Turn.Right })
                foreach (float goal in new[] { 14f, 40f })
                    foreach (float dt in new[] { 1f / 30f, .2f }) CornerParking(length, turn, goal, dt, axleRatio);
        foreach (Turn turn in new[] { Turn.Left, Turn.Right })
            foreach (float before in new[] { 12f, 22f })
                foreach (float dt in new[] { 1f / 30f, .2f }) CornerParking(3.8f, turn, 40f, dt, .7f, before);
    }


    static void BodyCorners()
    {
        foreach (float half in new[] { 5f, 7.5f, 15f })
        {
            var net = Program.Grid(new[] { -100f, 0f, 100f }, new[] { -100f, 0f, 100f }, false, nodeHalf: half);
            var node = net.Nodes.First(n => n.I == 1 && n.J == 1);
            foreach (float length in new[] { 2.3f, 3.8f })
                foreach (float axleRatio in new[] { .5f, .6f, .7f })
                foreach (var c in node.Connectors.Where(c => c.Kind != Turn.Straight && !c.UTurn))
                {
                    float width = length > 3f ? 1.28f : .95f;
                    float intrusion = 0f, pavement = 0f, slip = 0f;
                    Vector3 previousRear = default, previousForward = default;
                    for (float s = -length; s <= c.Length + length * 8f; s += .025f)
                    {
                        JunctionClearance.Pose(c, s, length * axleRatio, out var p, out var f);
                        var right = Vector3.Cross(Vector3.up, f);
                        var rear = p - f * (length * axleRatio);
                        if (s > -length) slip = Math.Max(slip, Math.Abs(Vector3.Dot(rear - previousRear,
                            Vector3.Cross(Vector3.up, (f + previousForward).normalized))));
                        previousRear = rear; previousForward = f;
                        foreach (int front in new[] { -1, 1 })
                            foreach (int side in new[] { -1, 1 })
                            {
                                var corner = p + f * (length * front) + right * (width * side);
                                if (Math.Abs(corner.x) > 5f && Math.Abs(corner.z) > 5f &&
                                    (Math.Abs(corner.x) > half || Math.Abs(corner.z) > half))
                                    pavement = Math.Max(pavement, Math.Min(Math.Abs(corner.x) - 5f, Math.Abs(corner.z) - 5f));
                                c.To.Road.Project(corner, out float station, out float d);
                                // Crossing the opposing stream inside the intersection is
                                // a normal left turn. Crossing its approach past the box is not.
                                if ((station - c.To.S0) * c.To.Heading > 0f)
                                    intrusion = Math.Max(intrusion, -d * c.To.Heading);
                                c.From.Road.Project(corner, out station, out d);
                                if ((station - c.From.RoadS(c.From.Length)) * c.From.Heading < 0f)
                                    intrusion = Math.Max(intrusion, -d * c.From.Heading);
                            }
                    }
                    TrafficAdmission.Check(intrusion < .05f && pavement < .05f && slip < .002f,
                        $"body turn box={half} length={length} axle={axleRatio} {c.From.Road.Index}->{c.To.Road.Index} {c.Kind} opposing={intrusion:F3}m pavement={pavement:F3}m slip={slip:F5}m");
                }
        }
    }

    static void CyclicPreview()
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { -100f, 0f, 100f }, new[] { -100f, 0f, 100f }, false);
        var car = new RoadCar { Net = net, Tf = new Transform() };
        var lane = net.Edges.First();
        car.Spawn(lane, 20f);
        car.GoTo(net.Edges.Last().Start, false);
        var outside = new LaneNet().AddRoad(new Vector3(4000f, 0f, 0f), new Vector3(4100f, 0f, 0f),
            5f, new[] { 2.5f }, 10f, null, null, true);
        TrafficAdmission.Set(car, "_goalRoad", outside);
        TrafficAdmission.Set(car, "_goalLane", outside.Lanes[0]);
        car.Route = new Dictionary<RoadEdge, RoadEdge>();
        foreach (var edge in net.Edges)
            car.Route[edge] = edge.To.Outgoing.First(next => edge.To.ConnectorFor(edge, next) != null);
        var points = new List<Vector3>();
        bool visible = car.CopyPlannedRoute(points);
        var scratch = (List<Vector3>)typeof(RoadCar).GetField("_plannedRouteScratch",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(car);
        TrafficAdmission.Check(!visible && points.Count == 0 && scratch.Capacity <= 4,
            $"cyclic malformed preview stops before building geometry: capacity={scratch.Capacity}");
    }

    static void WaitingTurn()
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { 0f }, new[] { 0f, 240f }, false);
        var lane = net.Edges.First(e => e.Heading == 1);
        var car = new RoadCar { Net = net, Tf = new Transform(), Profile = DriverProfile.Gangster,
            HalfLen = 1.05f, HalfWide = .42f };
        car.Spawn(lane, 30f); StreetTraffic.Users.Add(car);
        var other = new StaticRoadUser { Position = lane.Road.Pose(60f, -2.5f), Forward = -lane.Dir,
            HalfLen = 45f, HalfWide = .95f };
        StreetTraffic.Users.Add(other); net.AddStatic(other);
        car.GoTo(lane.Road.Pose(12f, lane.Offset), false);
        var cars = new List<RoadCar> { car };
        var points = new List<Vector3>();
        int missingConnector = 0, turnFrames = 0;
        var field = typeof(RoadCar).GetField("_next", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var replan = typeof(RoadCar).GetMethod("Replan", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var retryArgs = replan.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { false };
        for (int tick = 0; tick < 120; tick++)
        {
            TrafficAdmission.Tick(cars, 1f / 30f);
            // Exercise the same-intent retry contract independently of the throttle
            // gate; physical brake pulses in the review were not reproduced here.
            if (tick % 15 == 0) replan.Invoke(car, retryArgs);
            if (car.Via == null && field.GetValue(car) == null) missingConnector++;
            if (car.Doing == RoadCar.Manoeuvre.UTurn) turnFrames++;
            car.CopyPlannedRoute(points);
        }
        TrafficAdmission.Check(car.HasGoal && turnFrames == 0 && missingConnector == 0,
            $"same-intent retry while waiting for U-turn gap keeps fallback: missing={missingConnector} turning frames={turnFrames}");
    }

    static void LongPreview(int roads, float metres = 50f)
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { 0f }, Enumerable.Range(0, roads + 1).Select(i => i * metres).ToArray(), false);
        var first = net.Edges.First(e => e.Heading == 1);
        var last = net.Edges.Last(e => e.Heading == 1);
        var car = new RoadCar { Net = net, Tf = new Transform() };
        car.Spawn(first, 10f);
        car.GoTo(last.Road.Pose(20f, last.Offset), park: false);
        var path = new List<Vector3>();
        bool visible = car.CopyPlannedRoute(path);
        TrafficAdmission.Check(visible && path.Count > 1000 && path.Count <= RoutePreviewBudget.MaxPoints &&
            (path.Last() - last.Road.Pose(20f, last.Offset)).magnitude < .5f,
            $"bounded complete {roads}-road preview ({metres}m spacing): visible={visible} samples={path.Count}");
        if (metres > 100f)
        {
            StreetTraffic.Users.Add(car);
            var cars = new List<RoadCar> { car };
            float back = 0f;
            for (int frame = 0; frame < 300; frame++)
            {
                TrafficAdmission.Tick(cars, 1f / 30f);
                visible &= car.CopyPlannedRoute(path);
                if (path.Count > 1) back = Math.Max(back, -Vector3.Dot(path[1] - path[0], car.Forward));
            }
            TrafficAdmission.Check(visible && back < .01f && path.Count <= RoutePreviewBudget.MaxPoints &&
                (path.Last() - last.Road.Pose(20f, last.Offset)).magnitude < .5f,
                $"coarse route follows car along segments, never back to old vertices: backward={back:F2}m travelled={car.S - 10f:F1}m");
        }
        car.Stop();
        TrafficAdmission.Check(!car.CopyPlannedRoute(path) && path.Count == 0, "cancelled order clears movement preview");
    }

    static void CrossingOrder(bool park)
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { -200f, -100f, 0f, 100f, 200f }, new[] { -100f, 0f, 100f }, false);
        var origin = net.Edges.First(e => e.Heading == 1 && Math.Abs(e.Road.A.x + 100f) < 1f && e.Start.z < -50f);
        var car = new KerbApproach.ObservedCar { Net = net, Profile = DriverProfile.Gangster, Tf = new Transform() };
        car.Spawn(origin, origin.Length - 22f);
        StreetTraffic.Users.Add(car);
        var cars = new List<RoadCar> { car };
        var first = origin.To.Outgoing.First(e => Vector3.Dot(e.Dir, origin.Dir) > .9f);
        car.GoTo(first.Road.Pose(first.RoadS(50f), first.Offset), park: false);
        for (int tick = 0; tick < 600 && car.Via == null; tick++) TrafficAdmission.Tick(cars, 1f / 30f);
        if (car.Via == null) { TrafficAdmission.Check(false, "crossing order fixture enters connector"); return; }
        var crossing = car.Via;
        var goal = net.Edges.First(e => e.Heading == 1 && Math.Abs(e.Road.A.z - 100f) < 1f && e.Start.x > 100f);
        var destination = goal.Road.Pose(goal.RoadS(40f), park ? goal.Road.KerbD(goal.Heading, car.HalfWide) : goal.Offset);
        bool accepted = car.GoTo(destination, park);
        var route = new List<Vector3>();
        bool immediate = car.CopyPlannedRoute(route);
        TrafficAdmission.Check(accepted && immediate && car.Route != null && car.Via == crossing,
            $"order during crossing park={park}: accepted={accepted} preview={immediate} route={car.Route?.Count ?? 0} crossing preserved={car.Via == crossing}");
        int missing = 0;
        float distance = 0f;
        for (int tick = 0; tick < 2700 && car.HasGoal; tick++)
        {
            var before = car.Position;
            TrafficAdmission.Tick(cars, 1f / 30f);
            distance += (car.Position - before).magnitude;
            if (car.HasGoal && !car.CopyPlannedRoute(route))
            {
                missing++;
                if (missing <= 3) Console.WriteLine($"MISSING preview {car.DoingLine} road={car.Road?.Index} S={car.S:F2} speed={car.Speed:F2}");
            }
        }
        TrafficAdmission.Check(car.AtGoal && distance < 450f && missing == 0 && car.Discontinuities == 0,
            $"crossing order completion park={park}: arrived={car.AtGoal} driven={distance:F1}m missing preview={missing} jumps={car.Discontinuities} {car.FirstDiscontinuity}");
    }

    static void LocalOrder()
    {
        TrafficAdmission.Reset();
        var net = new LaneNet();
        var a = net.AddNode(0, 0, 5, 5);
        var b = net.AddNode(0, 240, 5, 5);
        var road = net.AddRoad(new Vector3(0, 0, 5), new Vector3(0, 0, 235), 5f, new[] { 2.5f }, 10f, a, b, true);
        net.Finish();
        var car = new RoadCar { Net = net, Profile = DriverProfile.Gangster, Tf = new Transform(), HalfLen = 1.05f, HalfWide = .42f };
        car.Spawn(road.LaneFor(1, 2.5f), 30f);
        StreetTraffic.Users.Add(car);
        car.GoTo(road.Pose(12f, road.KerbD(-1, car.HalfWide)), park: true);
        var cars = new List<RoadCar> { car };
        float travelled = 0f;
        for (int tick = 0; tick < 2700 && car.HasGoal; tick++)
        {
            var before = car.Position;
            TrafficAdmission.Tick(cars, 1f / 30f);
            travelled += (car.Position - before).magnitude;
        }
        TrafficAdmission.Check(car.ParkedAtKerb && travelled < 80f,
            $"fresh order to nearby opposite kerb: parked={car.ParkedAtKerb} travelled={travelled:F1}m");
    }

    static void CancelledAdmission()
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { -100f, 0f, 100f }, new[] { -100f, 0f, 100f }, false);
        var lane = net.Edges.First(e => e.Heading == 1);
        var car = new RoadCar { Net = net, Profile = DriverProfile.Gangster, Tf = new Transform() };
        car.Spawn(lane, 30f);
        StreetTraffic.Users.Add(car);
        TrafficAdmission.Set(car, "_committed", true);
        car.GoTo(lane.Road.Pose(lane.RoadS(12f), lane.Road.KerbD(-lane.Heading, car.HalfWide)), true);
        // Cancellation is also used by reverse-queue yielding, lane changes and
        // backing out of a box. It must consume the deferred replan on the road.
        TrafficAdmission.Set(car, "_committed", false);
        TrafficAdmission.Tick(new List<RoadCar> { car }, 1f / 30f);
        bool deferred = (bool)typeof(RoadCar).GetField("_replanOnExit",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(car);
        TrafficAdmission.Check(!deferred && car.Route == null && car.HasGoal,
            "cancelled admission recalculates local turn intent with the goal intact");
    }

    static void CornerParking(float length, Turn turn, float goal, float dt, float axleRatio, float parkedBefore = 0f)
    {
        TrafficAdmission.Reset();
        var net = Program.Grid(new[] { -100f, 0f, 100f }, new[] { -100f, 0f, 100f }, false, setback: 1.5f);
        var node = net.Nodes.First(n => n.I == 1 && n.J == 1);
        var crossing = node.Connectors.First(c => c.Kind == turn);
        var car = new KerbApproach.ObservedCar { Net = net, Profile = DriverProfile.Gangster, Tf = new Transform(),
            HalfLen = length, HalfWide = length > 3f ? 1.28f : .95f, AxleBack = length * axleRatio };
        if (parkedBefore > 0f)
            car.PlaceAt(crossing.From.Road.Pose(crossing.From.RoadS(crossing.From.Length - parkedBefore),
                crossing.From.Road.KerbD(crossing.From.Heading, car.HalfWide)), crossing.From.Dir);
        else car.Spawn(crossing.From, crossing.From.Length - 22f);
        var opposite = crossing.To.Road.LaneFor(-crossing.To.Heading, -crossing.To.Offset);
        var other = new RoadCar { Net = net };
        other.Spawn(opposite, opposite.Length - 4f);
        other.Halt(true);
        StreetTraffic.Users.Add(car); StreetTraffic.Users.Add(other);
        car.Obstacle = other;
        car.GoTo(crossing.To.Road.Pose(crossing.To.RoadS(goal), crossing.To.Road.KerbD(crossing.To.Heading, car.HalfWide)), park: true);
        var cars = new List<RoadCar> { car };
        for (int tick = 0; tick < 60f / dt && car.HasGoal; tick++) TrafficAdmission.Tick(cars, dt);
        TrafficAdmission.Check(car.ParkedAtKerb && !car.Overlapped && car.Discontinuities == 0 && car.TrafficRecoveries == 0,
            $"turn then park {turn} length={length} axle={axleRatio} from kerb={parkedBefore} goal={goal} dt={dt:F3}: parked={car.ParkedAtKerb} hit={car.Overlapped} jumps={car.Discontinuities} S={car.S:F1} {car.DoingLine} {car.FirstDiscontinuity}");
    }
}
