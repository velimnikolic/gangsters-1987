using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

static class TrafficEscape
{
    public static void Run()
    {
        foreach (float dt in new[] { 1f / 30f, .2f, .8f, 1.6f })
            foreach (bool reverseOrder in new[] { false, true }) Pair(dt, reverseOrder);
        foreach (string exclusion in new[] { "parked", "halted", "wrecked", "parking", "pullout", "queue", "third", "walker" }) Excluded(exclusion);
        foreach (string action in new[] { "order", "halt", "despawn", "wreck" }) Cleanup(action);
        foreach (float dt in new[] { 1f / 30f, .2f }) LateThird(dt);
        foreach (float dt in new[] { 1f / 30f, .2f, .8f, 1.6f })
            foreach (bool reverseOrder in new[] { false, true })
                foreach (string action in new[] { "persistent car", "persistent walker", "halt driver",
                    "halt waiting", "order driver", "order waiting" })
                    InterruptedContact(dt, reverseOrder, action);
    }

    static List<RoadCar> Setup()
    {
        TrafficAdmission.Reset(); var f = TrafficAdmission.Junction(1.5f);
        var cars = new List<RoadCar>();
        foreach (int h in new[] { 1, -1 })
        {
            var from = (h > 0 ? f.south : f.north).LaneFor(h, h * 1.5f);
            var to = (h > 0 ? f.north : f.south).LaneFor(h, h * 1.5f);
            var car = new RoadCar { Net = f.net, Profile = DriverProfile.Gangster, HalfWide = 1.6f };
            car.Spawn(from, from.Length);
            car.Route = new Dictionary<RoadEdge, RoadEdge> { [from] = to };
            TrafficAdmission.Call(car, "PlanNext", f.node);
            // Reproduce two late arrivals admitted onto genuinely overlapping
            // envelopes. They start separated, then meet under normal TickStep.
            TrafficAdmission.Call(car, "EnterNode", f.node, 4.5f);
            TrafficAdmission.Call(car, "Place", 0f, float.NaN, float.NaN);
            TrafficAdmission.Set(car, "<Speed>k__BackingField", 0f);
            StreetTraffic.Users.Add(car); cars.Add(car);
        }
        return cars;
    }

    static bool Overlap(RoadCar a, RoadCar b) => RoadSpace.Overlap(a.Position, a.Forward, a.HalfLen, a.HalfWide,
        b.Position, b.Forward, b.HalfLen, b.HalfWide, 0f, out _);

    static void Pair(float dt, bool reverseOrder)
    {
        var cars = Setup(); if (reverseOrder) cars.Reverse();
        bool allowedOverlap = false, illegal = false, jumped = false, fast = false;
        float firstLease = -1f;
        for (int frame = 0; frame < Math.Ceiling(24f / dt); frame++)
        {
            var beforeA = cars[0].Position; var beforeB = cars[1].Position;
            TrafficAdmission.Tick(cars, dt);
            bool active = cars[0].Deadlock.Active || cars[1].Deadlock.Active;
            if (active && firstLease < 0f) firstLease = Time.time;
            if (active) fast |= cars[0].Speed > RoadDeadlock.Pace + .01f || cars[1].Speed > RoadDeadlock.Pace + .01f;
            bool overlapping = Overlap(cars[0], cars[1]);
            allowedOverlap |= overlapping && active;
            illegal |= overlapping && !active;
            jumped |= (cars[0].Position - beforeA).magnitude > 16f * dt + .05f ||
                (cars[1].Position - beforeB).magnitude > 16f * dt + .05f;
            if (Environment.GetEnvironmentVariable("TRACE") == "1" && frame % Math.Max(1, (int)(1f / dt)) == 0)
                Console.WriteLine($"t={Time.time:F1} lease={active} {cars[0].Describe()} v={cars[0].Speed:F2} | {cars[1].Describe()} v={cars[1].Speed:F2}");
        }
        int escapes = cars[0].Deadlock.Escapes + cars[1].Deadlock.Escapes;
        bool clear = cars[0].Via == null && cars[1].Via == null && !Overlap(cars[0], cars[1]);
        TrafficAdmission.Check(escapes == 1 && allowedOverlap && !illegal && !jumped && !fast && clear &&
            firstLease >= RoadDeadlock.Delay && !cars[0].Deadlock.Active && !cars[1].Deadlock.Active &&
            cars[0].TrafficRecoveries + cars[1].TrafficRecoveries == 0,
            $"mutual escape dt={dt:F3} reversed={reverseOrder} count={escapes} at={firstLease:F2} overlap={allowedOverlap} illegal={illegal} fast={fast} clear={clear}");
    }

    static void Excluded(string reason)
    {
        var cars = Setup(); var a = cars[0]; var b = cars[1];
        if (reason == "parked") TrafficAdmission.Set(b, "<Parked>k__BackingField", true);
        if (reason == "halted") b.Halt(false);
        if (reason == "wrecked") b.Wreck();
        if (reason == "parking") { TrafficAdmission.Set(b, "_hasGoal", true); TrafficAdmission.Set(b, "_goalPark", true); }
        if (reason == "pullout") TrafficAdmission.Set(b, "_man", RoadCar.Manoeuvre.PullOut);
        if (reason == "queue") TrafficAdmission.Set(b, "_fwd", a.Forward);
        if (reason == "third")
        {
            var third = new RoadCar { Net = a.Net };
            var road = a.Via.To.Road;
            third.PlaceAt(road.Pose(3f, 1.5f), Vector3.forward); third.Halt(false);
            StreetTraffic.Users.Add(third);
        }
        if (reason == "walker") StreetTraffic.Walkers.Add(new Vector3(1.5f, 0f, 3f));
        // Feed reciprocal blocking evidence to isolate admission exclusions.
        for (int i = 0; i < 240; i++)
        {
            Time.time += 1f / 30f;
            a.Deadlock.BlockedBy(b); b.Deadlock.BlockedBy(a);
            a.Deadlock.Tick(); b.Deadlock.Tick();
        }
        TrafficAdmission.Check(!a.Deadlock.Active && !b.Deadlock.Active, $"escape excludes {reason}");
    }

    static void Cleanup(string action)
    {
        var cars = Setup(); var a = cars[0]; var b = cars[1];
        for (int i = 0; i < 240; i++)
        {
            Time.time += 1f / 30f;
            a.Deadlock.BlockedBy(b); b.Deadlock.BlockedBy(a);
            a.Deadlock.Tick(); b.Deadlock.Tick();
            if (a.Deadlock.Active) break;
        }
        bool armed = a.Deadlock.Active;
        if (action == "order") a.GoTo(a.Via.To.Start + a.Via.To.Dir * 60f, false);
        if (action == "halt") a.Halt(false);
        if (action == "despawn") a.Despawn();
        if (action == "wreck") a.Wreck();
        b.Deadlock.Tick();
        TrafficAdmission.Check(armed && !a.Deadlock.Active && !b.Deadlock.Active, $"escape cleanup on {action}");
    }

    static void LateThird(float dt)
    {
        var cars = Setup(); RoadCar third = null; float inserted = -1f;
        bool touchedThird = false, guarded = false;
        for (int i = 0; i < Math.Ceiling(28f / dt); i++)
        {
            if (third == null && inserted < 0f && cars[0].Deadlock.Active)
            {
                var driver = cars[0].Deadlock.Waiting ? cars[1] : cars[0];
                third = new RoadCar { Net = driver.Net, HalfLen = .5f, HalfWide = .4f };
                third.PlaceAt(driver.Position + driver.Forward * (driver.HalfLen + 1.5f), driver.Forward);
                third.Halt(false); StreetTraffic.Users.Add(third); inserted = Time.time;
            }
            TrafficAdmission.Tick(cars, dt);
            if (third != null)
            {
                foreach (var car in cars) touchedThird |= Overlap(car, third);
                guarded |= Time.time - inserted > 1f && cars[0].Speed < .1f && cars[1].Speed < .1f;
                if (Time.time - inserted > 3f) { third.Vanish(); third = null; }
            }
        }
        TrafficAdmission.Check(inserted > RoadDeadlock.Delay && guarded && !touchedThird &&
            cars[0].Via == null && cars[1].Via == null && !cars[0].Deadlock.Active && !cars[1].Deadlock.Active,
            $"late third vehicle stops active escape dt={dt:F3} guarded={guarded} overlap={touchedThird}");
    }

    static void InterruptedContact(float dt, bool reverseOrder, string action)
    {
        var cars = Setup(); if (reverseOrder) cars.Reverse();
        for (int i = 0; i < Math.Ceiling(15f / dt) &&
            !(cars[0].Deadlock.Active && Overlap(cars[0], cars[1])); i++)
            TrafficAdmission.Tick(cars, dt);
        bool entered = cars[0].Deadlock.Active && Overlap(cars[0], cars[1]);
        var driver = cars[0].Deadlock.Waiting ? cars[1] : cars[0];
        var waiting = driver == cars[0] ? cars[1] : cars[0];
        var commanded = action.EndsWith("driver") ? driver : waiting;
        var start = waiting.Position;
        float interruptedAt = Time.time, separatedAt = -1f;
        RoadCar obstacle = null;
        Vector3 person = default;
        if (action == "persistent car")
        {
            obstacle = new RoadCar { Net = driver.Net, HalfLen = .2f, HalfWide = .2f };
            obstacle.PlaceAt(driver.Position + driver.Forward * (driver.HalfLen + .6f), driver.Forward);
            obstacle.Halt(false); StreetTraffic.Users.Add(obstacle);
        }
        else if (action == "persistent walker")
        {
            person = driver.Position + driver.Forward * (driver.HalfLen + 1.5f);
            StreetTraffic.Walkers.Add(person);
        }
        else if (action.StartsWith("halt")) commanded.Halt(false);
        else commanded.GoTo(commanded.Via.To.Start + commanded.Via.To.Dir * 60f, false);

        bool illegal = false, touchedThird = false, jumped = false, fast = false, lostOrder = false;
        for (int i = 0; i < Math.Ceiling(40f / dt); i++)
        {
            var beforeA = cars[0].Position; var beforeB = cars[1].Position;
            TrafficAdmission.Tick(cars, dt);
            bool active = cars[0].Deadlock.Active || cars[1].Deadlock.Active;
            bool overlap = Overlap(cars[0], cars[1]);
            illegal |= overlap && !active;
            if (!overlap && separatedAt < 0f) separatedAt = Time.time;
            if (active && action.StartsWith("order")) lostOrder |= !commanded.HasGoal;
            foreach (var car in cars)
            {
                if (active) fast |= Math.Abs(car.Speed) > RoadDeadlock.Pace + .01f;
                if (obstacle != null) touchedThird |= Overlap(car, obstacle);
                if (action == "persistent walker") touchedThird |= RoadSpace.Overlap(car.Position, car.Forward,
                    car.HalfLen, car.HalfWide, person, Vector3.forward, .3f, .3f, 0f, out _);
            }
            jumped |= (cars[0].Position - beforeA).magnitude > 16f * dt + .05f ||
                (cars[1].Position - beforeB).magnitude > 16f * dt + .05f;
        }
        bool clear = !Overlap(cars[0], cars[1]) && !cars[0].Deadlock.Active && !cars[1].Deadlock.Active;
        if (!clear)
            foreach (var car in cars)
                Console.WriteLine($"  unfinished contact: {car.Describe()} v={car.Speed:F2} active={car.Deadlock.Active} waiting={car.Deadlock.Waiting} eligible={car.CanEaseTraffic} offset={car.CrossingOffset:F3}");
        bool alternateExit = !action.StartsWith("persistent") || (waiting.Position - start).magnitude > 5f;
        TrafficAdmission.Check(entered && clear && alternateExit && !illegal && !touchedThird && !jumped &&
            !fast && !lostOrder && cars[0].TrafficRecoveries + cars[1].TrafficRecoveries == 0,
            $"interrupted contact {action} dt={dt:F3} reversed={reverseOrder} entered={entered} clear={clear} " +
            $"separatedAfter={separatedAt - interruptedAt:F2} illegal={illegal} third={touchedThird} jumps={jumped} fast={fast} lostOrder={lostOrder}");
        if (obstacle != null) obstacle.Vanish();
    }
}
