using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEngine;

static class Program
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Call(object target, Type owner, string method) =>
        owner.GetMethod(method, Private).Invoke(target, null);
    sealed class ProbeCar : PolicePatrolCar
    {
        public int Discontinuities;
        public string FirstDiscontinuity;
        internal override void TickStep(float dt)
        {
            var before = Position;
            var forward = Forward;
            float speed = Math.Abs(Speed);
            base.TickStep(dt);
            float axle = HalfLen * .6f;
            float travelled = ((Position - Forward * axle) - (before - forward * axle)).magnitude;
            float yaw = Math.Abs(Vector3.SignedAngle(forward, Forward, Vector3.up));
            float budget = Math.Max(speed, Math.Abs(Speed)) * dt * 2f;
            if (travelled > budget + .025f || yaw > budget / 2.2f * Mathf.Rad2Deg + .5f)
            {
                Discontinuities++;
                FirstDiscontinuity ??= $"t={Time.time:F2} axle={travelled:F3}/{budget:F3} yaw={yaw:F2}";
            }
        }
    }

    sealed class Fixture : IDisposable
    {
        public readonly LaneNet Net = new();
        public readonly ProbeCar Car;
        public readonly Carriageway Road;
        public readonly RoadEdge Lane;
        public readonly Vector3 Goal;
        public readonly List<RoadCar> Obstacles = new();
        readonly float dt;
        public Fixture(int heading, float dt, bool junction = false)
        {
            this.dt = dt;
            StreetTraffic.Users.Clear();
            StreetTraffic.Bodies.Clear();
            StreetTraffic.Walkers.Clear();
            CallStatic(typeof(PolicePatrolCar), "ResetLeases");
            Time.time = 0f;
            Time.frameCount++;
            Carriageway destination;
            if (junction)
            {
                var node = Net.AddNode(0f, 0f, 7.5f, 7.5f);
                var south = Net.AddRoad(new Vector3(0f, 0f, -200f), new Vector3(0f, 0f, -7.5f),
                    7.5f, new[] { 2.5f }, 10f, null, node, true);
                var north = Net.AddRoad(new Vector3(0f, 0f, 7.5f), new Vector3(0f, 0f, 200f),
                    7.5f, new[] { 2.5f }, 10f, node, null, true);
                Road = heading > 0 ? south : north;
                destination = heading > 0 ? north : south;
            }
            else destination = Road = Net.AddRoad(Vector3.zero, new Vector3(0f, 0f, 400f),
                7.5f, new[] { 2.5f }, 10f, null, null, true);
            Net.Finish();
            Lane = Road.LaneFor(heading, heading * 2.5f);
            Car = new ProbeCar { Net = Net, Tf = new Transform() };
            Car.InitRolling(Lane, 100f, Lane, 20f, Net.Edges,
                new Dictionary<RoadEdge, RoadEdge>(), new Vector2(90f, 240f), new Vector2Int(4, 6));
            Car.RestsAtKerbs = true;
            StreetTraffic.Users.Add(Car);
            Goal = destination.Pose(junction ? 100f : 200f, destination.KerbD(heading, Car.HalfWide));
        }
        public void Step()
        {
            Time.deltaTime = dt;
            Time.time += dt;
            Time.frameCount++;
            Car.TickPatrol(dt);
        }
        public void Run(float seconds)
        {
            for (int i = 0; i < Math.Ceiling(seconds / dt); i++) Step();
        }
        public void EnterCurve(bool responding)
        {
            if (responding) Car.RouteTo(Goal, 0f);
            else
            {
                Call(Car, typeof(PolicePatrolCar), "BeginParking");
                Require(Car.GoTo(Goal, true), "parking order refused");
            }
            for (int i = 0; i < Math.Ceiling(40f / dt) &&
                (!Car.Sliding || Math.Abs(Car.D - Lane.Offset) < .2f); i++) Step();
            Require(Car.Sliding && Math.Abs(Car.D - Lane.Offset) >= .2f,
                "fixture never entered its parking curve");
        }
        public void ExpireParking() =>
            typeof(PolicePatrolCar).GetField("_parkingBy", Private).SetValue(Car, Time.time - 1f);
        public void Dispose()
        {
            Car.Vanish();
            foreach (var obstacle in Obstacles) obstacle.Vanish();
        }
    }

    static void Main()
    {
        int total = 0, failed = 0;
        foreach (int heading in new[] { 1, -1 })
            foreach (float dt in new[] { 1f / 30f, .2f })
                foreach (string scenario in new[] { "failed parking timeout", "failed response released",
                    "parking timeout mid-entry", "rest completed", "transfer halt retained", "custody return retained",
                    "response released in junction", "parking cancelled awaiting exit",
                    "transfer released while braking", "retired transfer released",
                    "parking timeout during retreat", "response released during retreat",
                    "disabled engine removed", "wreck removed", "halted transfer removed",
                    "healthy rest retained", "failed response retried", "response redirected mid-entry",
                    "response redirected during retreat", "custody return during retreat",
                    "failed rest retried", "failed response waits behind", "failed response retreat interrupted",
                    "failed patrol departure retried", "permanent walker behind", "permanent car behind",
                    "failed parking during pass" })
                {
                    total++;
                    try { Check(scenario, heading, dt); }
                    catch (Exception error)
                    {
                        failed++;
                        Console.WriteLine($"FAIL {scenario} h={heading} dt={dt:F3}: {error.Message}");
                    }
                }
        Console.WriteLine($"Police patrol lifecycle: {total - failed}/{total} passed");
        try { Passengers.Check(); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL passenger lifecycle: " + error.Message); }
        if (failed != 0) Environment.ExitCode = 1;
    }

    static void Check(string scenario, int heading, float dt)
    {
        using var f = new Fixture(heading, dt, scenario == "response released in junction");
        var car = f.Car;
        if (scenario.StartsWith("permanent ") || scenario == "failed parking during pass")
        {
            CheckParkingFallback(f, scenario, heading, dt);
            return;
        }
        if (scenario.StartsWith("failed response") && scenario != "failed response released" ||
            scenario == "failed rest retried" || scenario.StartsWith("response redirected") ||
            scenario == "custody return during retreat")
        {
            CheckParkingOrder(f, scenario, heading, dt);
            return;
        }
        if (scenario.EndsWith("removed") || scenario == "healthy rest retained")
        {
            CheckRemoval(f, scenario, heading);
            return;
        }
        bool resumes = true;
        switch (scenario)
        {
            case "parking timeout during retreat":
            case "response released during retreat":
                f.EnterCurve(scenario.StartsWith("response"));
                Call(car, typeof(RoadCar), "RetreatFromKerb");
                f.Step();
                Require((bool)typeof(RoadCar).GetField("_parkingRetreat", Private).GetValue(car) &&
                    car.Speed < 0f, "fixture did not reverse its actual entry curve");
                if (scenario.StartsWith("response")) car.Release();
                else f.ExpireParking();
                break;
            case "response released in junction":
                car.RouteTo(f.Goal, 0f);
                for (int i = 0; i < Math.Ceiling(30f / dt) && car.Via == null; i++) f.Step();
                Require(car.Via != null, "fixture never entered its junction");
                car.Halt(false);
                Require((bool)typeof(RoadCar).GetField("_haltWhenClear", Private).GetValue(car),
                    "junction stop was not deferred");
                car.Release();
                break;
            case "parking cancelled awaiting exit":
                var kerb = f.Road.Pose(car.S, f.Road.KerbD(heading, car.HalfWide));
                car.InitAtKerb(kerb, Quaternion.LookRotation(f.Lane.Dir), f.Lane, 20f,
                    f.Net.Edges, new Dictionary<RoadEdge, RoadEdge>(),
                    new Vector2(90f, 240f), new Vector2Int(4, 6), 90f);
                var blocker = new RoadCar { Net = f.Net };
                blocker.PlaceAt(f.Road.Pose(car.S, f.Lane.Offset), f.Lane.Dir);
                blocker.Halt(true);
                f.Obstacles.Add(blocker);
                StreetTraffic.Users.Add(blocker);
                car.RouteTo(f.Road.Pose(200f, -f.Road.KerbD(heading, car.HalfWide)), 0f);
                Call(car, typeof(PolicePatrolCar), "BeginParking");
                f.Run(.3f);
                Require((bool)typeof(RoadCar).GetField("_pullOutWanted", Private).GetValue(car) && !car.Sliding,
                    "fixture was not waiting for a gap to exit: " + car.Describe());
                f.ExpireParking();
                f.Step();
                blocker.Vanish();
                break;
            case "failed parking timeout":
            case "failed response released":
                f.EnterCurve(scenario == "failed response released");
                // Fault injection at the production failed-entry boundary. The car
                // must resume even when the owner releases it before its next retry.
                Call(car, typeof(RoadCar), "FailParking");
                Require(car.ParkingFailed && car.Halted && !car.Parked, "failed-entry setup");
                if (scenario == "failed response released") car.Release();
                else f.ExpireParking();
                break;
            case "parking timeout mid-entry":
                f.EnterCurve(false);
                f.ExpireParking();
                break;
            case "failed patrol departure retried":
                car.InitAtKerb(f.Goal, Quaternion.LookRotation(f.Lane.Dir), f.Lane, 20f,
                    f.Net.Edges, new Dictionary<RoadEdge, RoadEdge>(),
                    new Vector2(90f, 240f), new Vector2Int(4, 6), 0f);
                for (int i = 0; i < Math.Ceiling(15f / dt) &&
                    (!car.Sliding || Math.Abs(car.D - f.Road.KerbD(heading, car.HalfWide)) < .2f); i++)
                    f.Step();
                Require(car.State == PolicePatrolCar.Mode.Patrolling && car.Sliding &&
                    Math.Abs(car.D - f.Road.KerbD(heading, car.HalfWide)) >= .2f,
                    "fixture never entered its patrol departure curve");
                Call(car, typeof(RoadCar), "FailParking");
                break;
            case "rest completed":
                car.InitAtKerb(f.Goal, Quaternion.LookRotation(f.Lane.Dir), f.Lane, 20f,
                    f.Net.Edges, new Dictionary<RoadEdge, RoadEdge>(),
                    new Vector2(90f, 240f), new Vector2Int(4, 6), 0f);
                Require(car.ParkedAtKerb, "resting setup");
                break;
            case "transfer halt retained":
            case "transfer released while braking":
            case "retired transfer released":
                car.RouteTo(f.Goal, 0f);
                car.HaltTransfer();
                if (scenario == "retired transfer released")
                {
                    f.Run(2f);
                    Require(!car.Fleetworthy, "fixture did not retire the disabled transfer");
                }
                if (scenario != "transfer halt retained") car.Release();
                resumes = false;
                break;
            case "custody return retained":
                car.RouteTo(f.Goal, 0f);
                car.HoldAtKerb = true;
                car.CustodyReserved = true;
                car.Release();
                resumes = false;
                Require(car.State == PolicePatrolCar.Mode.Returning && car.HasGoal && !car.Available,
                    "custody lost its return route or reservation");
                break;
        }
        var start = car.Position;
        f.Run(resumes ? 10f : 2f);
        Require(car.TrafficRecoveries == 0 && !car.Gone, "relocation or deletion hid the failure");
        Require(car.Discontinuities == 0, "motion discontinuity: " + car.FirstDiscontinuity);
        if (resumes)
        {
            Require(!car.Halted && !car.ParkingFailed && !car.HasGoal && !car.HasRestSpot &&
                car.State == PolicePatrolCar.Mode.Patrolling && car.Available,
                $"stale patrol state: halted={car.Halted}, mode={car.State}, {car.Describe()}");
            Require((car.Position - start).magnitude > 10f && Math.Abs(car.D - f.Lane.Offset) < .3f,
                $"did not rejoin traffic: moved={(car.Position - start).magnitude:F2}, {car.Describe()}");
            Require(!(bool)typeof(PolicePatrolCar).GetField("_hasReservedKerb", Private).GetValue(car),
                "old kerb claim remains reserved");
        }
        else if (scenario.Contains("transfer") && scenario != "custody return retained")
            Require(car.Halted && !car.Fleetworthy && !car.Available && car.Speed < .05f &&
                car.State == PolicePatrolCar.Mode.OnScene && (car.Position - start).magnitude < 10f,
                $"transfer did not brake and remain disabled: v={car.Speed:F2}, halted={car.Halted}, " +
                $"fleetworthy={car.Fleetworthy}, mode={car.State}, moved={(car.Position - start).magnitude:F2}");
        else
            Require(car.HoldAtKerb && car.CustodyReserved && !car.Available &&
                car.State == PolicePatrolCar.Mode.Returning, "custody ownership was released");
        if (scenario.EndsWith("during retreat"))
        {
            Require((bool)typeof(RoadCar).GetMethod("TryReverse", Private).Invoke(car, new object[] { null, 0f }),
                "ordinary reverse setup refused");
            f.Run(12f);
            Require(!car.ParkingFailed && !car.Halted && Math.Abs(car.D - f.Lane.Offset) < .3f &&
                car.Discontinuities == 0, "later reverse reused the abandoned parking curve");
        }
        Console.WriteLine($"PASS {scenario} h={heading} dt={dt:F3}");
    }

    static void CheckRemoval(Fixture f, string scenario, int heading)
    {
        var car = f.Car;
        int cleanupCalls = 0;
        car.BeforeRemoval = body =>
        {
            Require(!body.Gone && body.Tf != null && body.Speed == 0f,
                "passenger cleanup ran after removal or while moving");
            cleanupCalls++;
        };
        if (scenario == "healthy rest retained")
            car.InitAtKerb(f.Goal, Quaternion.LookRotation(f.Lane.Dir), f.Lane, 20f,
                f.Net.Edges, new Dictionary<RoadEdge, RoadEdge>(),
                new Vector2(90f, 240f), new Vector2Int(4, 6), 240f);
        else if (scenario == "wreck removed") car.Wreck();
        else if (scenario == "halted transfer removed")
        {
            car.RouteTo(f.Goal, 0f);
            car.HaltTransfer();
        }
        else
        {
            float chance = RoadCar.EngineChance;
            try
            {
                RoadCar.EngineChance = 1f;
                for (int i = 0; i < RoadCar.EngineHitsToKill; i++)
                    car.TakeRound(car.Tf.position + car.Tf.forward * car.HalfLen, Vector3.zero);
            }
            finally { RoadCar.EngineChance = chance; }
            Require(car.EngineDead, "gunfire never disabled the engine");
        }
        f.Run(PolicePatrolCar.DisabledRemovalSeconds - 1f);
        Require(!car.Gone && cleanupCalls == 0, "body disappeared before the cleanup deadline");
        if (scenario != "healthy rest retained")
        {
            car.Release();
            car.RouteTo(f.Goal, 0f);
        }
        f.Run(2f);
        if (scenario == "healthy rest retained")
            Require(!car.Gone && car.ParkedAtKerb && cleanupCalls == 0, "normal rest was treated as wreckage");
        else
        {
            Require(car.Gone && cleanupCalls == 1 && !car.Fleetworthy && !car.Available &&
                car.Lane == null && car.Road == null && car.Via == null &&
                !StreetTraffic.Users.Contains(car), "disabled body or traffic registration survived");
            foreach (var occupant in f.Road.Occupants)
                Require(occupant.Who != car, "removed car still occupies its lane");
            Require(((IPatrolMarker)car).MarkerTf == null && ((IPoliceUnit)car).Tf == null,
                "removed car still has a map or dispatch body");
            foreach (string list in new[] { "Fleet", "Swinging" })
                Require(!((System.Collections.IList)typeof(PolicePatrolCar).GetField(list,
                    BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Contains(car),
                    "removed car retained its " + list + " lease");
            f.Run(60f);
            Require(cleanupCalls == 1 && car.BeforeRemoval == null,
                "repeated owner ticks reran removal or retained custody callback");
        }
        Console.WriteLine($"PASS {scenario} h={heading}");
    }

    static void CheckParkingOrder(Fixture f, string scenario, int heading, float dt)
    {
        var car = f.Car;
        bool resting = scenario == "failed rest retried";
        f.EnterCurve(!resting);
        if (resting)
        {
            // Keep the rest draw on this street, away from its assigned home lane.
            typeof(PolicePatrolCar).GetField("_allEdges", Private).SetValue(car, new List<RoadEdge> { f.Lane });
            typeof(PolicePatrolCar).GetField("_home", Private).SetValue(car,
                f.Road.LaneFor(-heading, -f.Lane.Offset));
        }
        if (scenario.EndsWith("during retreat"))
        {
            Call(car, typeof(RoadCar), "RetreatFromKerb");
            f.Step();
            Require(car.Speed < 0f, "fixture did not reverse the parking curve");
        }
        var target = f.Goal;
        bool custody = scenario == "custody return during retreat";
        if (resting || scenario.StartsWith("failed response"))
        {
            Call(car, typeof(RoadCar), "FailParking");
            if (scenario == "failed response waits behind" || scenario == "failed response retreat interrupted")
            {
                if (scenario.EndsWith("interrupted"))
                {
                    for (int i = 0; i < Math.Ceiling(3f / dt) && car.Speed >= 0f; i++) f.Step();
                    Require(car.Speed < 0f, "retry never started its retreat");
                }
                else typeof(RoadCar).GetField("<Speed>k__BackingField", Private).SetValue(car, 0f);
                var position = car.Position;
                StreetTraffic.Walkers.Add(position - car.Forward * (car.HalfLen + 1.2f));
                f.Run(2f);
                Require((car.Position - position).magnitude < .05f && !car.ParkedAtKerb,
                    "blocked retreat moved through the person or reported arrival");
                StreetTraffic.Walkers.Clear();
            }
        }
        else if (custody)
        {
            car.HoldAtKerb = car.CustodyReserved = true;
            car.Release();
        }
        else
        {
            target += f.Lane.Dir * 25f;
            car.RouteTo(target, 0f);
        }
        var start = car.Position;
        for (int i = 0; i < Math.Ceiling(30f / dt) &&
            (custody ? (car.Position - start).magnitude < 20f :
                car.State != (resting ? PolicePatrolCar.Mode.Resting : PolicePatrolCar.Mode.OnScene)); i++)
            f.Step();
        Require(car.Discontinuities == 0, "motion discontinuity: " + car.FirstDiscontinuity);
        Require(car.TrafficRecoveries == 0 && !car.Gone, "relocation or deletion hid failed order");
        if (custody)
            Require(car.State == PolicePatrolCar.Mode.Returning && car.HasGoal &&
                (car.Position - start).magnitude >= 20f && !car.Available,
                "custody did not resume its return route: " + car.Describe());
        else
            Require(car.State == (resting ? PolicePatrolCar.Mode.Resting : PolicePatrolCar.Mode.OnScene) &&
                car.AtGoal && car.ParkedAtKerb &&
                (car.Position - (resting ? car.RestSpot : target)).magnitude < 12f,
                $"response never parked: mode={car.State} halted={car.Halted} failed={car.ParkingFailed} " + car.Describe());
        Console.WriteLine($"PASS {scenario} h={heading} dt={dt:F3}");
    }

    static void CheckParkingFallback(Fixture f, string scenario, int heading, float dt)
    {
        var car = f.Car;
        RoadCar obstacle = null;
        bool passing = scenario == "failed parking during pass";
        if (passing)
        {
            obstacle = new RoadCar { Net = f.Net };
            obstacle.PlaceAt(f.Road.Pose(car.S + heading * 25f, f.Lane.Offset), f.Lane.Dir);
            typeof(RoadCar).GetField("<Parked>k__BackingField", Private).SetValue(obstacle, true);
            Call(obstacle, typeof(RoadCar), "UpdateOccupant");
            f.Obstacles.Add(obstacle);
            StreetTraffic.Users.Add(obstacle);
            car.RouteTo(f.Goal, 0f);
            for (int i = 0; i < Math.Ceiling(20f / dt) &&
                !(car.Doing == RoadCar.Manoeuvre.Pass && car.Sliding && Math.Abs(car.D - f.Lane.Offset) > .2f); i++)
                f.Step();
            Require(car.Doing == RoadCar.Manoeuvre.Pass && car.Sliding && car.Speed > 1f,
                "fixture never drove its overtaking curve: " + car.Describe());
        }
        else f.EnterCurve(true);
        Call(car, typeof(RoadCar), "FailParking");
        if (!passing)
        {
            typeof(RoadCar).GetField("<Speed>k__BackingField", Private).SetValue(car, 0f);
            if (scenario == "permanent walker behind")
                StreetTraffic.Walkers.Add(car.Position - car.Forward * (car.HalfLen + 1.2f));
            else
            {
                obstacle = new RoadCar { Net = f.Net };
                obstacle.PlaceAt(f.Road.Pose(car.S - heading * (car.HalfLen + obstacle.HalfLen + .4f), car.D), f.Lane.Dir);
                typeof(RoadCar).GetField("<Parked>k__BackingField", Private).SetValue(obstacle, true);
                Call(obstacle, typeof(RoadCar), "UpdateOccupant");
                f.Obstacles.Add(obstacle);
                StreetTraffic.Users.Add(obstacle);
            }
        }
        var start = car.Position;
        bool escaped = false;
        for (int i = 0; i < Math.Ceiling(25f / dt) && !escaped; i++)
        {
            float speed = car.Speed;
            f.Step();
            Require(car.Speed >= -.01f, "reversed an overtaking curve or into a permanent rear obstacle");
            Require(speed - car.Speed <= car.Profile.HardBrake * dt + .21f,
                "recovery stopped a moving car without braking");
            if (obstacle != null)
                Require(!RoadSpace.Overlap(car.Position, car.Forward, car.HalfLen, car.HalfWide,
                    obstacle.Position, obstacle.Forward, obstacle.HalfLen, obstacle.HalfWide, 0f, out _),
                    "recovery crossed the parked car");
            escaped = !car.Sliding && !car.Halted && !car.ParkingFailed &&
                (car.Position - start).magnitude > 10f;
        }
        Require(escaped && car.State == PolicePatrolCar.Mode.Responding && car.HasGoal,
            "did not clear the failed manoeuvre and retain the response: " + car.Describe());
        Require(car.Discontinuities == 0 && car.TrafficRecoveries == 0 && !car.Gone,
            "jump or relocation hid the failure: " + car.FirstDiscontinuity);
        Console.WriteLine($"PASS {scenario} h={heading} dt={dt:F3}");
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static void CallStatic(Type owner, string method) =>
        owner.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
}
